using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Tests.Content;

public class ContentSeederTests
{
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private async Task Seed()
    {
        await using var db = new AppDbContext(_options);
        await new ContentSeeder(db).SeedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Empty_database_gets_directories_events_version_1_and_trip_settings()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        Assert.Equal(["loyalty", "safety"], await db.Scales.OrderBy(s => s.Code).Select(s => s.Code).ToListAsync());
        Assert.Equal(
            ["standard", "comfort", "business", "first"],
            await db.ServiceClasses.OrderBy(c => c.SortOrder).Select(c => c.Code).ToListAsync());
        Assert.Equal(
            [("sit-06", 1), ("sit-33", 1), ("zastup", 1)],
            (await db.EventDocuments.ToListAsync()).Select(e => (e.EventId, e.Version)).Order());
        var trip = Assert.Single(await db.TripSettings.ToListAsync());
        Assert.Equal("zastup", JsonSerializer.Deserialize<TripSettings>(trip.Document, EventJson.Options)!.ShiftStartEvent);
    }

    [Fact]
    public async Task Stored_event_document_is_readable_by_validator()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        var stored = await db.EventDocuments.SingleAsync(e => e.EventId == "sit-33");
        var report = EventValidator.ValidateJson(stored.Document, SeedContent.Directory, SeedContent.FlagsSetElsewhere("sit-33"));
        Assert.True(report.IsValid);
    }

    [Fact]
    public async Task Repeated_start_creates_no_duplicates()
    {
        await Seed();
        await Seed();

        await using var db = new AppDbContext(_options);
        Assert.Equal(
            (2, 4, 3, 1),
            (await db.Scales.CountAsync(), await db.ServiceClasses.CountAsync(),
             await db.EventDocuments.CountAsync(), await db.TripSettings.CountAsync()));
    }
}
