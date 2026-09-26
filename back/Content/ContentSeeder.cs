using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Content;

/// <summary>
/// Засев стартового контента при запуске: добавляет только то, чего в базе ещё нет,
/// поэтому повторный запуск дублей не создаёт, а правки Методиста не перезаписывает.
/// </summary>
public sealed class ContentSeeder(AppDbContext db)
{
    public const string DefaultTripSettingsId = "default";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var directory = SeedContent.Directory;

        var existingScales = await db.Scales.Select(s => s.Code).ToListAsync(cancellationToken);
        db.Scales.AddRange(directory.Scales
            .Where(s => !existingScales.Contains(s.Code))
            .Select(s => new ScaleRecord
            {
                Code = s.Code, Name = s.Name, Min = s.Min, Max = s.Max, Start = s.Start,
                FailureThreshold = s.FailureThreshold, Mandatory = s.Mandatory, FailureReason = s.FailureReason,
            }));

        var existingClasses = await db.ServiceClasses.Select(c => c.Code).ToListAsync(cancellationToken);
        db.ServiceClasses.AddRange(directory.Classes
            .Select((c, index) => new ServiceClassRecord { Code = c.Code, Name = c.Name, Description = c.Description, SortOrder = index })
            .Where(c => !existingClasses.Contains(c.Code)));

        var existingEvents = await db.EventDocuments
            .Select(e => new { e.EventId, e.Version })
            .ToListAsync(cancellationToken);
        db.EventDocuments.AddRange(SeedContent.Events
            .Where(seed => !existingEvents.Any(e => e.EventId == seed.Document.Id && e.Version == seed.Document.Version))
            .Select(seed => new EventDocumentRecord
            {
                EventId = seed.Document.Id, Version = seed.Document.Version, Document = seed.Json, PublishedAt = DateTimeOffset.UtcNow,
            }));

        if (!await db.TripSettings.AnyAsync(t => t.Id == DefaultTripSettingsId, cancellationToken))
            db.TripSettings.Add(new TripSettingsRecord
            {
                Id = DefaultTripSettingsId,
                Document = JsonSerializer.Serialize(SeedContent.Trip, EventJson.Options),
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
