using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Sources;

public sealed record EventGenerationResult(
    int Created, int Duplicates, IReadOnlyList<string> Errors, IReadOnlyList<EventDraftView> Drafts);

public sealed class EventGenerationService(AppDbContext db, IEventGenerationClient generator, TimeProvider clock)
{
    public async Task<IResult> GenerateAsync(Guid sourceId, string? model, CancellationToken cancellationToken)
    {
        model ??= GenerationModels.Default;
        if (!GenerationModels.Allowed.Contains(model))
            return Results.BadRequest(new { message = "Эта модель не доступна для сравнения" });
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(item => item.Id == sourceId, cancellationToken);
        if (source is null) return Results.NotFound();
        var sections = SourceText.Sections(source.Text);
        if (sections.Count == 0) return Results.BadRequest(new { message = "Текст Источника пуст" });

        var scales = await db.Scales.AsNoTracking().Select(scale => new ScaleDefinition(
            scale.Code, scale.Name, scale.Min, scale.Max, scale.Start, scale.FailureThreshold,
            scale.Mandatory, scale.FailureReason)).ToListAsync(cancellationToken);
        var classes = await db.ServiceClasses.AsNoTracking().Select(item => new ServiceClass(
            item.Code, item.Name, item.Description)).ToListAsync(cancellationToken);
        var directory = new ContentDirectory(scales, classes);
        var published = await db.EventDocuments.AsNoTracking().ToListAsync(cancellationToken);
        var current = published.GroupBy(row => row.EventId)
            .Select(group => JsonSerializer.Deserialize<EventDocument>(group.MaxBy(row => row.Version)!.Document, EventJson.Options)!)
            .ToList();
        var knownFlags = current.SelectMany(item => item.FlagsSet).ToHashSet();
        var titles = current.Select(item => item.Title.Trim().ToLowerInvariant()).ToHashSet();
        var existingDrafts = await db.EventDrafts.AsNoTracking().Select(item => item.Document).ToListAsync(cancellationToken);
        foreach (var json in existingDrafts)
        {
            try { titles.Add(JsonSerializer.Deserialize<EventDocument>(json, EventJson.Options)!.Title.Trim().ToLowerInvariant()); }
            catch (JsonException) { /* Ручной Черновик может быть неполным. */ }
        }
        var topics = await db.Questions.AsNoTracking().Select(item => item.Topic).Distinct().ToListAsync(cancellationToken);
        topics = topics.Concat(current.Select(item => item.Topic)).Distinct().Order().ToList();
        var drafts = new List<EventDraftView>();
        var errors = new List<string>();
        var duplicates = 0;

        // Только очищенный текст уходит модели; одновременно не более трёх пунктов.
        using var slots = new SemaphoreSlim(3);
        var firstResponses = await Task.WhenAll(sections.Select(async section =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                var context = section.Context is null ? string.Empty :
                    $"Контекст Источника: {SourceText.RemovePersonalData(section.Context)}\n";
                var prompt = new EventGenerationPrompt(SourceText.RemovePersonalData(source.Title), section.Reference,
                    context + SourceText.RemovePersonalData(section.Text), topics, model);
                return (Section: section, Prompt: prompt, Response: await CallAsync(prompt, cancellationToken));
            }
            finally { slots.Release(); }
        }));

        foreach (var (section, prompt, firstResponse) in firstResponses)
        {
            var response = firstResponse;
            for (var attempt = 0; attempt <= 2; attempt++)
            {
                var feedback = new List<string>();
                var candidate = ReadCandidate(response, feedback);
                if (candidate is not null)
                {
                    var quotes = candidate.Steps.SelectMany(step => step.Variants ?? [])
                        .Select(variant => variant.Source).ToList();
                    var sectionText = SourceText.RemovePersonalData(section.Text);
                    if (quotes.Count == 0 || quotes.Any(quote => string.IsNullOrWhiteSpace(quote) ||
                        !sectionText.Contains(quote, StringComparison.Ordinal)))
                        feedback.Add("У каждого Варианта нужна дословная цитата из очищенного пункта Источника");
                    if (!topics.Contains(candidate.Topic))
                        feedback.Add("Тема должна быть из справочника");
                    if (feedback.Count == 0)
                    {
                        var id = Guid.NewGuid();
                        var sourcePoint = $"{source.Title}, п. {section.Reference}";
                        var document = candidate with
                        {
                            Id = $"event-{id:N}", Version = 1, Source = sourcePoint,
                            Steps = candidate.Steps.Select(step => step with
                            {
                                Variants = step.Variants?.Select(variant => variant with
                                {
                                    Source = $"{sourcePoint}: «{variant.Source}»",
                                }).ToList(),
                            }).ToList(),
                        };
                        try
                        {
                            var report = EventValidator.Validate(document, directory, knownFlags);
                            feedback.AddRange(report.Errors.Select(issue => $"{issue.Where}: {issue.Message}"));
                        }
                        catch (ArgumentException) { feedback.Add("Граф События не прошёл проверку"); }
                        if (feedback.Count == 0)
                        {
                            if (!titles.Add(document.Title.Trim().ToLowerInvariant())) duplicates++;
                            else
                            {
                                var draft = new EventDraftRecord
                                {
                                    Id = id, SourceId = sourceId, CreatedAt = clock.GetUtcNow(),
                                    Document = JsonSerializer.Serialize(document, EventJson.Options),
                                };
                                db.EventDrafts.Add(draft);
                                await db.SaveChangesAsync(cancellationToken);
                                drafts.Add(new EventDraftView(draft.Id, draft.SourceId, draft.Document, draft.CreatedAt));
                            }
                            break;
                        }
                    }
                }
                else if (feedback.Count == 0) break; // Модель не нашла сценарий.
                if (attempt == 2)
                {
                    errors.Add($"Пункт {section.Reference}: {SourceText.RemovePersonalData(string.Join("; ", feedback.Distinct()))}");
                    break;
                }
                var previous = response is null ? string.Empty : SourceText.RemovePersonalData(response);
                response = await CallAsync(prompt with
                {
                    Feedback = SourceText.RemovePersonalData(string.Join("; ", feedback.Distinct())),
                    PreviousOutput = previous[..Math.Min(previous.Length, 20_000)],
                }, cancellationToken);
            }
        }
        return Results.Ok(new EventGenerationResult(drafts.Count, duplicates, errors, drafts));
    }

    private async Task<string?> CallAsync(EventGenerationPrompt prompt, CancellationToken cancellationToken)
    {
        try { return await generator.GenerateAsync(prompt, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    private static EventDocument? ReadCandidate(string? response, List<string> feedback)
    {
        if (response is null) { feedback.Add("Модель недоступна или вернула ошибку"); return null; }
        try
        {
            using var parsed = JsonDocument.Parse(response);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("event", out var item))
            {
                feedback.Add("Ожидается объект с полем event");
                return null;
            }
            if (item.ValueKind == JsonValueKind.Null) return null;
            return item.Deserialize<EventDocument>(EventJson.Options);
        }
        catch (JsonException) { feedback.Add("Некорректная структура JSON События"); return null; }
    }
}
