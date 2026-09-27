using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboSquadApp.Data;

namespace TurboSquadApp.Events;

public sealed record EventVersionSummary(int Version, DateTimeOffset PublishedAt);
public sealed record EventSummary(
    string Id, string Title, string Topic, IReadOnlyList<string> ServiceClasses,
    int LatestVersion, DateTimeOffset PublishedAt, IReadOnlyList<EventVersionSummary> Versions);
public sealed record EventVersionView(string Id, int Version, DateTimeOffset PublishedAt, string Document);
public sealed record EventEditorRequest(string Document);
public sealed record PublishEventRequest(int ExpectedVersion, string Document);

/// <summary>Опубликованные версии Событий и проверка JSON редактора Методиста.</summary>
public sealed class EventCmsService(AppDbContext db, TimeProvider clock)
{
    // Одновременные публикации в одном процессе сериализуются; ключ (id, version) защищает и между процессами.
    private static readonly SemaphoreSlim PublishGate = new(1, 1);

    public async Task<IResult> ListAsync(CancellationToken cancellationToken)
    {
        var records = await db.EventDocuments.AsNoTracking()
            .OrderBy(row => row.EventId).ThenBy(row => row.Version).ToListAsync(cancellationToken);
        var events = records.GroupBy(row => row.EventId).Select(group =>
        {
            var latest = group.Last();
            var document = JsonSerializer.Deserialize<EventDocument>(latest.Document, EventJson.Options)!;
            var classes = (document.Conditions ?? [])
                .Where(condition => condition.Type == "class")
                .SelectMany(condition => condition.Classes ?? [])
                .Distinct().ToList();
            return new EventSummary(group.Key, document.Title, document.Topic, classes,
                latest.Version, latest.PublishedAt,
                group.Select(row => new EventVersionSummary(row.Version, row.PublishedAt)).ToList());
        }).ToList();
        return Results.Ok(events);
    }

    public async Task<IResult> GetVersionAsync(string id, int version, CancellationToken cancellationToken)
    {
        var record = await db.EventDocuments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EventId == id && row.Version == version, cancellationToken);
        return record is null ? Results.NotFound() : Results.Ok(View(record));
    }

    public async Task<IResult> ValidateAsync(string id, EventEditorRequest request, CancellationToken cancellationToken) =>
        Results.Ok(await ReportAsync(id, request.Document, cancellationToken));

    public async Task<IResult> PublishAsync(string id, PublishEventRequest request, CancellationToken cancellationToken)
    {
        await PublishGate.WaitAsync(cancellationToken);
        try
        {
            var current = await db.EventDocuments.AsNoTracking()
                .Where(row => row.EventId == id)
                .OrderByDescending(row => row.Version)
                .FirstOrDefaultAsync(cancellationToken);
            if (current is null) return Results.NotFound();
            if (current.Version != request.ExpectedVersion) return VersionConflict(current.Version);

            var report = await ReportAsync(id, request.Document, cancellationToken);
            if (!report.IsValid) return Results.BadRequest(report);

            var document = JsonSerializer.Deserialize<EventDocument>(request.Document, EventJson.Options)!;
            var record = new EventDocumentRecord
            {
                EventId = id, Version = document.Version, Document = request.Document,
                PublishedAt = clock.GetUtcNow(),
            };
            db.EventDocuments.Add(record);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                       { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                var latest = await db.EventDocuments.AsNoTracking()
                    .Where(row => row.EventId == id).MaxAsync(row => row.Version, cancellationToken);
                return VersionConflict(latest);
            }

            return Results.Created($"/api/cms/events/{id}/versions/{record.Version}", View(record));
        }
        finally
        {
            PublishGate.Release();
        }
    }

    private async Task<ValidationReport> ReportAsync(string id, string document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document))
            return new ValidationReport(
                [new ValidationIssue(Severity.Error, IssueLocation.Event, "Документ пуст", "ADR-0001")], []);
        var scales = await db.Scales.AsNoTracking().Select(scale => new ScaleDefinition(
            scale.Code, scale.Name, scale.Min, scale.Max, scale.Start, scale.FailureThreshold,
            scale.Mandatory, scale.FailureReason)).ToListAsync(cancellationToken);
        var classes = await db.ServiceClasses.AsNoTracking().Select(serviceClass => new ServiceClass(
            serviceClass.Code, serviceClass.Name, serviceClass.Description)).ToListAsync(cancellationToken);
        var rows = await db.EventDocuments.AsNoTracking().Where(row => row.EventId != id)
            .ToListAsync(cancellationToken);
        var flags = rows.GroupBy(row => row.EventId)
            .Select(group => JsonSerializer.Deserialize<EventDocument>(group.MaxBy(row => row.Version)!.Document, EventJson.Options)!)
            .SelectMany(ev => ev.FlagsSet).ToHashSet();
        var report = EventValidator.ValidateJson(document, new ContentDirectory(scales, classes), flags);
        if (!report.IsValid) return report;

        var parsed = JsonSerializer.Deserialize<EventDocument>(document, EventJson.Options)!;
        var issues = new List<ValidationIssue>();
        if (parsed.Id != id)
            issues.Add(new ValidationIssue(Severity.Error, IssueLocation.Event,
                $"ID документа «{parsed.Id}» не совпадает с ID События «{id}»", "ADR-0001"));
        var latestVersion = await db.EventDocuments.AsNoTracking()
            .Where(row => row.EventId == id).Select(row => (int?)row.Version)
            .MaxAsync(cancellationToken);
        if (latestVersion is null)
            issues.Add(new ValidationIssue(Severity.Error, IssueLocation.Event,
                $"Событие «{id}» не найдено", "ADR-0001"));
        else if (parsed.Version != latestVersion + 1)
            issues.Add(new ValidationIssue(Severity.Error, IssueLocation.Event,
                $"Новая версия должна быть {latestVersion + 1}", "ADR-0001"));
        return issues.Count == 0 ? report : report with { Errors = [.. report.Errors, .. issues] };
    }

    private static EventVersionView View(EventDocumentRecord record) =>
        new(record.EventId, record.Version, record.PublishedAt, record.Document);

    private static IResult VersionConflict(int latestVersion) => Results.Conflict(new
    {
        reason = "VersionConflict", latestVersion,
        message = $"Событие уже обновлено до версии {latestVersion}. Откройте актуальную версию перед публикацией.",
    });
}
