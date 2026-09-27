using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Questions;

/// <summary>Вопрос в форме Методиста; JSON вариантов остаётся типизированным по Type.</summary>
public sealed class QuestionEditorInput
{
    public string Type { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
    public JsonElement Options { get; set; }
    public string ExplanationText { get; set; } = string.Empty;
    public string ExplanationKeyFact { get; set; } = string.Empty;
    public string? Quote { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public IReadOnlyList<string> Categories { get; set; } = [];
    public IReadOnlyList<string> ServiceClasses { get; set; } = [];
    public double BaseFrequency { get; set; } = 1;
    public int? TimeLimitSec { get; set; }
}

public sealed record QuestionView(
    string Id, string Type, string Status, string Statement, JsonElement Options,
    string ExplanationText, string ExplanationKeyFact, string? Quote, string Source, string Topic,
    IReadOnlyList<string> Categories, IReadOnlyList<string> ServiceClasses,
    double BaseFrequency, int? TimeLimitSec)
{
    public static QuestionView Of(QuestionRecord record) => new(
        record.Id, record.Type, record.Status, record.Statement, JsonSerializer.Deserialize<JsonElement>(record.Options),
        record.ExplanationText, record.ExplanationKeyFact, record.Quote, record.Source, record.Topic,
        JsonSerializer.Deserialize<List<string>>(record.Categories) ?? [],
        JsonSerializer.Deserialize<List<string>>(record.ServiceClasses) ?? [],
        record.BaseFrequency, record.TimeLimitSec);
}

public sealed record QuestionCatalog(
    IReadOnlyList<string> Topics, IReadOnlyList<string> Categories,
    IReadOnlyList<ServiceClass> ServiceClasses, IReadOnlyList<ScaleDefinition> Scales);

/// <summary>Операции банка Вопросов; публикация применяет правку только после успешной проверки.</summary>
public sealed class QuestionBankService(AppDbContext db)
{
    public async Task<IResult> CatalogAsync(CancellationToken cancellationToken)
    {
        var questions = await db.Questions.AsNoTracking()
            .Select(question => new { question.Topic, question.Categories })
            .ToListAsync(cancellationToken);
        var topics = questions.Select(question => question.Topic).Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct().OrderBy(topic => topic).ToList();
        var categories = questions.SelectMany(question => JsonSerializer.Deserialize<List<string>>(question.Categories) ?? [])
            .Distinct().OrderBy(category => category).ToList();
        var classes = await db.ServiceClasses.OrderBy(serviceClass => serviceClass.SortOrder)
            .Select(serviceClass => new ServiceClass(serviceClass.Code, serviceClass.Name, serviceClass.Description))
            .ToListAsync(cancellationToken);
        var scales = await db.Scales.OrderBy(scale => scale.Code)
            .Select(scale => new ScaleDefinition(scale.Code, scale.Name, scale.Min, scale.Max, scale.Start,
                scale.FailureThreshold, scale.Mandatory, scale.FailureReason))
            .ToListAsync(cancellationToken);
        return Results.Ok(new QuestionCatalog(topics, categories, classes, scales));
    }

    public async Task<IResult> ListAsync(
        string? topic, string? category, string? serviceClass, string? type, string? status,
        CancellationToken cancellationToken)
    {
        var query = db.Questions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(topic)) query = query.Where(q => q.Topic == topic);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(q => q.Type == type);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(q => q.Status == status);
        var questions = await query.OrderBy(q => q.Id).ToListAsync(cancellationToken);
        var filtered = questions.Select(QuestionView.Of)
            .Where(q => string.IsNullOrWhiteSpace(category) || q.Categories.Contains(category))
            .Where(q => string.IsNullOrWhiteSpace(serviceClass) ||
                q.ServiceClasses.Count == 0 || q.ServiceClasses.Contains(serviceClass))
            .ToList();
        return Results.Ok(filtered);
    }

    public async Task<IResult> GetAsync(string id, CancellationToken cancellationToken)
    {
        var record = await db.Questions.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        return record is null ? Results.NotFound() : Results.Ok(QuestionView.Of(record));
    }

    public async Task<IResult> CreateAsync(QuestionEditorInput input, CancellationToken cancellationToken)
    {
        if (!KnownType(input.Type)) return InvalidType();
        var record = new QuestionRecord { Id = $"q-{Guid.NewGuid():N}", Status = QuestionStatuses.Draft };
        Apply(record, input);
        db.Questions.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/cms/questions/{record.Id}", QuestionView.Of(record));
    }

    public async Task<IResult> UpdateAsync(string id, QuestionEditorInput input, CancellationToken cancellationToken)
    {
        var record = await db.Questions.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (record is null) return Results.NotFound();
        if (!KnownType(input.Type)) return InvalidType();

        var candidate = new QuestionRecord { Id = id, Status = record.Status };
        Apply(candidate, input);
        if (record.Status == QuestionStatuses.Published &&
            await ValidateAsync(candidate, cancellationToken) is { IsValid: false } invalid)
            return Results.BadRequest(invalid);

        Apply(record, input);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(QuestionView.Of(record));
    }

    public async Task<IResult> PublishAsync(string id, CancellationToken cancellationToken)
    {
        var record = await db.Questions.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (record is null) return Results.NotFound();
        var report = await ValidateAsync(record, cancellationToken);
        if (!report.IsValid) return Results.BadRequest(report);
        record.Status = QuestionStatuses.Published;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(QuestionView.Of(record));
    }

    public async Task<IResult> UnpublishAsync(string id, CancellationToken cancellationToken)
    {
        var record = await db.Questions.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (record is null) return Results.NotFound();
        record.Status = QuestionStatuses.Draft;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(QuestionView.Of(record));
    }

    public async Task<IResult> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var record = await db.Questions.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (record is null) return Results.NotFound();
        if (record.Status != QuestionStatuses.Draft)
            return Results.Conflict(new { message = "Сначала снимите Вопрос с публикации" });

        var used = await db.SwipeAnswers.AnyAsync(answer => answer.QuestionId == id, cancellationToken);
        if (!used)
        {
            var decks = await db.SwipeShifts.Select(shift => shift.Deck).ToListAsync(cancellationToken);
            used = decks.Any(deck => (JsonSerializer.Deserialize<List<string>>(deck) ?? []).Contains(id));
        }
        if (used) return Results.Conflict(new { message = "Вопрос есть в прошлой Смене и не может быть удалён" });

        db.Questions.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private async Task<QuestionValidationReport> ValidateAsync(QuestionRecord record, CancellationToken cancellationToken)
    {
        var scales = await db.Scales.Select(scale => new ScaleDefinition(
            scale.Code, scale.Name, scale.Min, scale.Max, scale.Start, scale.FailureThreshold,
            scale.Mandatory, scale.FailureReason)).ToListAsync(cancellationToken);
        var classes = await db.ServiceClasses.Select(serviceClass => new ServiceClass(
            serviceClass.Code, serviceClass.Name, serviceClass.Description)).ToListAsync(cancellationToken);
        return QuestionValidator.ValidateForPublication(record, new ContentDirectory(scales, classes));
    }

    private static void Apply(QuestionRecord record, QuestionEditorInput input)
    {
        record.Type = input.Type;
        record.Statement = input.Statement ?? string.Empty;
        record.Options = input.Options.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "{}" : input.Options.GetRawText();
        record.ExplanationText = input.ExplanationText ?? string.Empty;
        record.ExplanationKeyFact = input.ExplanationKeyFact ?? string.Empty;
        record.Quote = input.Quote;
        record.Source = input.Source ?? string.Empty;
        record.Topic = input.Topic ?? string.Empty;
        record.Categories = JsonSerializer.Serialize(input.Categories ?? []);
        record.ServiceClasses = JsonSerializer.Serialize(input.ServiceClasses ?? []);
        record.BaseFrequency = input.BaseFrequency;
        record.TimeLimitSec = input.TimeLimitSec;
    }

    private static bool KnownType(string type) => type is
        QuestionTypes.Swipe or QuestionTypes.Single or QuestionTypes.Multiple or QuestionTypes.Sequence;

    private static IResult InvalidType() => Results.BadRequest(new QuestionValidationReport(
        [new("type", "Неизвестный тип Вопроса")]));
}
