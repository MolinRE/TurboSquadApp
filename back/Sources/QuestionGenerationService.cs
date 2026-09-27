using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;

namespace TurboSquadApp.Sources;

public sealed record GenerationResult(
    int Created, int Duplicates, IReadOnlyList<string> Errors, IReadOnlyList<QuestionView> Drafts);

public sealed class QuestionGenerationService(AppDbContext db, IQuestionGenerationClient generator)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IResult> DraftsAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        if (!await db.Sources.AnyAsync(source => source.Id == sourceId, cancellationToken))
            return Results.NotFound();
        var drafts = await db.Questions.AsNoTracking()
            .Where(question => question.SourceId == sourceId && question.Status == QuestionStatuses.Draft)
            .OrderBy(question => question.Id).ToListAsync(cancellationToken);
        return Results.Ok(drafts.Select(QuestionView.Of).ToList());
    }

    public async Task<IResult> GenerateAsync(Guid sourceId, string? model, CancellationToken cancellationToken)
    {
        if (model is not null && !GenerationModels.Allowed.Contains(model))
            return Results.BadRequest(new { message = "Эта модель не доступна для сравнения" });
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(item => item.Id == sourceId, cancellationToken);
        if (source is null) return Results.NotFound();
        var sections = SourceText.Sections(source.Text);
        if (sections.Count == 0) return Results.BadRequest(new { message = "Текст Источника пуст" });

        var scales = await db.Scales.Select(scale => new ScaleDefinition(
            scale.Code, scale.Name, scale.Min, scale.Max, scale.Start, scale.FailureThreshold,
            scale.Mandatory, scale.FailureReason)).ToListAsync(cancellationToken);
        var classes = await db.ServiceClasses.Select(item => new ServiceClass(
            item.Code, item.Name, item.Description)).ToListAsync(cancellationToken);
        var directory = new ContentDirectory(scales, classes);
        var existing = await db.Questions.AsNoTracking().Select(question => new { question.Statement, question.Topic })
            .ToListAsync(cancellationToken);
        var topics = existing.Select(question => question.Topic).Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct().OrderBy(topic => topic).ToList();
        var seen = existing.Select(question => Key(question.Statement)).ToHashSet();
        var drafts = new List<QuestionView>();
        var errors = new List<string>();
        var duplicates = 0;

        // Только очищенный текст уходит модели. Одновременно запускаем не более трёх пунктов.
        using var slots = new SemaphoreSlim(3);
        var firstResponses = await Task.WhenAll(sections.Select(async section =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                var context = section.Context is null ? string.Empty :
                    $"Контекст Источника: {SourceText.RemovePersonalData(section.Context)}\n";
                var prompt = new QuestionGenerationPrompt(SourceText.RemovePersonalData(source.Title), section.Reference,
                    context + SourceText.RemovePersonalData(section.Text), topics, null, Model: model);
                return (Section: section, Prompt: prompt, Response: await CallAsync(prompt, cancellationToken));
            }
            finally { slots.Release(); }
        }));

        foreach (var (section, initialPrompt, initialResponse) in firstResponses)
        {
            var response = initialResponse;
            for (var attempt = 0; attempt <= 2; attempt++)
            {
                var feedback = new List<string>();
                if (response is null)
                    feedback.Add("Модель недоступна или вернула ошибку");
                else if (response.FinishReason == "length")
                    feedback.Add("Модель исчерпала лимит ответа");
                else if (string.IsNullOrWhiteSpace(response.Content))
                    feedback.Add("Модель вернула пустой ответ");
                else
                {
                    foreach (var candidate in ReadCandidates(response.Content, feedback))
                    {
                        if (candidate is null)
                        {
                            feedback.Add("Пустой Вопрос в ответе модели");
                            continue;
                        }
                        var record = Record(candidate, sourceId, source.Title, section.Reference);
                        var report = QuestionValidator.ValidateForPublication(record, directory);
                        if (string.IsNullOrWhiteSpace(record.Quote) ||
                            !SourceText.RemovePersonalData(section.Text).Contains(record.Quote, StringComparison.Ordinal))
                            feedback.Add("Цитата должна дословно присутствовать в очищенном пункте Источника");
                        else if (!topics.Contains(record.Topic))
                            feedback.Add("Тема должна быть из справочника");
                        else if (!report.IsValid)
                            feedback.AddRange(report.Errors.Select(error => $"{error.Path}: {error.Message}"));
                        else if (!seen.Add(Key(record.Statement)))
                            duplicates++;
                        else
                        {
                            db.Questions.Add(record);
                            drafts.Add(QuestionView.Of(record));
                        }
                    }
                    if (drafts.Count > 0) await db.SaveChangesAsync(cancellationToken);
                }
                if (feedback.Count == 0) break;
                if (attempt == 2)
                {
                    errors.Add($"Пункт {section.Reference}: {string.Join("; ", feedback.Distinct())}");
                    break;
                }
                var cleanedResponse = response is null ? string.Empty : SourceText.RemovePersonalData(response.Content);
                var needsMoreRoom = response is not null &&
                    (response.FinishReason == "length" || string.IsNullOrWhiteSpace(response.Content));
                response = await CallAsync(initialPrompt with
                {
                    Feedback = string.Join("; ", feedback.Distinct()),
                    PreviousOutput = cleanedResponse[..Math.Min(cleanedResponse.Length, 20_000)],
                    MaxTokens = needsMoreRoom ? initialPrompt.MaxTokens + 4_000 * (attempt + 1) : initialPrompt.MaxTokens,
                }, cancellationToken);
            }
        }
        return Results.Ok(new GenerationResult(drafts.Count, duplicates, errors, drafts));
    }

    private async Task<QuestionGenerationResponse?> CallAsync(QuestionGenerationPrompt prompt, CancellationToken cancellationToken)
    {
        try { return await generator.GenerateAsync(prompt, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    private static IReadOnlyList<QuestionEditorInput?> ReadCandidates(string response, List<string> feedback)
    {
        var candidates = new List<QuestionEditorInput?>();
        JsonDocument document;
        try { document = JsonDocument.Parse(response); }
        catch (JsonException) { feedback.Add("Некорректный JSON"); return candidates; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("questions", out var questions) ||
                questions.ValueKind != JsonValueKind.Array)
            {
                feedback.Add("Ожидается объект с массивом questions");
                return candidates;
            }
            foreach (var item in questions.EnumerateArray())
            {
                if (!MatchesSchema(item))
                {
                    feedback.Add("Структура Вопроса не соответствует JSON-схеме");
                    continue;
                }
                try { candidates.Add(item.Deserialize<QuestionEditorInput>(JsonOptions)); }
                catch (JsonException) { feedback.Add("Структура Вопроса не соответствует JSON-схеме"); }
            }
        }
        return candidates;
    }

    private static bool MatchesSchema(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;
        return Has("type", JsonValueKind.String) && Has("statement", JsonValueKind.String) &&
            Has("options", JsonValueKind.Object) && Has("explanationText", JsonValueKind.String) &&
            Has("explanationKeyFact", JsonValueKind.String) && Has("quote", JsonValueKind.String) &&
            Has("topic", JsonValueKind.String) && Has("categories", JsonValueKind.Array) &&
            Has("serviceClasses", JsonValueKind.Array) && Has("baseFrequency", JsonValueKind.Number) &&
            Has("timeLimitSec", JsonValueKind.Number);

        bool Has(string name, JsonValueKind kind) =>
            item.TryGetProperty(name, out var property) && property.ValueKind == kind;
    }

    private static QuestionRecord Record(QuestionEditorInput input, Guid sourceId, string title, string section)
    {
        var point = $"{title}, п. {section}";
        var options = input.Options.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}" : input.Options.GetRawText();
        options = QuestionOptionsCodec.WithSource(input.Type, options, input.Quote, point);
        return new QuestionRecord
        {
            Id = $"q-{Guid.NewGuid():N}", Status = QuestionStatuses.Draft, SourceId = sourceId,
            Type = input.Type, Statement = input.Statement ?? string.Empty, Options = options,
            ExplanationText = input.ExplanationText ?? string.Empty,
            ExplanationKeyFact = input.ExplanationKeyFact ?? string.Empty,
            Quote = input.Quote, Source = point, Topic = input.Topic ?? string.Empty,
            Categories = JsonSerializer.Serialize(input.Categories ?? []),
            ServiceClasses = JsonSerializer.Serialize(input.ServiceClasses ?? []),
            BaseFrequency = input.BaseFrequency, TimeLimitSec = input.TimeLimitSec,
        };
    }

    private static string Key(string statement) =>
        Regex.Replace(statement.ToLowerInvariant().Replace('ё', 'е'), @"[^\p{L}\p{N}]+", " ").Trim();
}
