using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Events;

public class EventCmsApiTests
{
    [Fact]
    public async Task Methodologist_can_list_versions_and_validate_a_broken_editor_document()
    {
        await using var factory = new TripApiFactory();
        using var methodologist = await factory.CreateUserClient(UserRoles.Methodologist);

        var events = (await methodologist.GetFromJsonAsync<JsonNode>("/api/cms/events"))!.AsArray();
        var sit33 = events.Single(item => (string)item!["id"]! == "sit-33")!;
        Assert.Equal(3, (int)sit33["latestVersion"]!);
        Assert.Contains(sit33["versions"]!.AsArray(), version => (int)version!["version"]! == 3);

        var version3 = (await methodologist.GetFromJsonAsync<JsonNode>("/api/cms/events/sit-33/versions/3"))!;
        Assert.Equal("sit-33", (string)JsonNode.Parse((string)version3["document"]!)!["id"]!);

        var response = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/validate", new { document = TestData.BrokenDraftJson() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.False((bool)report["isValid"]!);
        Assert.Contains(report["errors"]!.AsArray(), issue =>
            (string?)issue!["location"]?["stepId"] is not null && (string?)issue["rule"] is not null);
        Assert.NotEmpty(report["warnings"]!.AsArray());
        var wrongVersion = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/validate", new
        {
            document = EditedEvent("sit-33", 5),
        });
        Assert.Equal(HttpStatusCode.OK, wrongVersion.StatusCode);
        Assert.False((bool)(await wrongVersion.Content.ReadFromJsonAsync<JsonNode>())!["isValid"]!);
        var malformed = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/validate", new { document = "{" });
        Assert.Equal(HttpStatusCode.OK, malformed.StatusCode);
        Assert.False((bool)(await malformed.Content.ReadFromJsonAsync<JsonNode>())!["isValid"]!);

        using var conductor = await factory.CreateConductorClient();
        using var manager = await factory.CreateManagerClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/cms/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.GetAsync("/api/cms/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/cms/events/sit-33/validate", new { document = "{}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.PostAsJsonAsync("/api/cms/events/sit-33/publish", new { expectedVersion = 3, document = "{}" })).StatusCode);
    }

    [Fact]
    public async Task Invalid_document_does_not_publish_and_stale_version_returns_conflict()
    {
        await using var factory = new TripApiFactory();
        using var methodologist = await factory.CreateUserClient(UserRoles.Methodologist);
        var broken = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/publish", new
        {
            expectedVersion = 3, document = TestData.BrokenDraftJson(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.False((bool)(await broken.Content.ReadFromJsonAsync<JsonNode>())!["isValid"]!);
        Assert.Equal(3, await LatestVersion(factory, "sit-33"));

        var wrongId = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/publish", new
        {
            expectedVersion = 3, document = EditedEvent("sit-06", 4),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongId.StatusCode);
        Assert.Contains((await wrongId.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray(),
            issue => (string)issue!["where"]! == "Событие");

        var wrongVersion = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/publish", new
        {
            expectedVersion = 3, document = EditedEvent("sit-33", 5),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongVersion.StatusCode);

        var edited = EditedEvent("sit-33", 4);
        var published = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/publish", new { expectedVersion = 3, document = edited });
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal(4, (int)(await published.Content.ReadFromJsonAsync<JsonNode>())!["version"]!);
        var stale = await methodologist.PostAsJsonAsync("/api/cms/events/sit-33/publish", new { expectedVersion = 3, document = edited });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("VersionConflict", (string)(await stale.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!);
        Assert.Equal(4, await LatestVersion(factory, "sit-33"));
        Assert.Equal(2, await factory.Database(db => db.EventDocuments.CountAsync(row => row.EventId == "sit-33")));
        var previous = (await methodologist.GetFromJsonAsync<JsonNode>("/api/cms/events/sit-33/versions/3"))!;
        Assert.Equal(3, (int)JsonNode.Parse((string)previous["document"]!)!["version"]!);
    }

    [Fact]
    public async Task Concurrent_publishers_get_one_new_version_and_one_conflict()
    {
        await using var factory = new TripApiFactory();
        using var first = await factory.CreateUserClient(UserRoles.Methodologist);
        using var second = await factory.CreateUserClient(UserRoles.Methodologist);
        var edited = EditedEvent("sit-33", 4);

        var results = await Task.WhenAll(
            first.PostAsJsonAsync("/api/cms/events/sit-33/publish", new { expectedVersion = 3, document = edited }),
            second.PostAsJsonAsync("/api/cms/events/sit-33/publish", new { expectedVersion = 3, document = edited }));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(result => result.StatusCode).Order());
        Assert.Equal(4, await LatestVersion(factory, "sit-33"));
    }

    [Fact]
    public async Task New_trip_uses_published_version_while_started_trip_and_debrief_keep_old_version()
    {
        await using var factory = new TripApiFactory();
        using var methodologist = await factory.CreateUserClient(UserRoles.Methodologist);
        var oldTrip = await TripApiTests.TripClient.Start(factory, "business");
        await oldTrip.Choose("a", "a");
        await oldTrip.Proactive("obhod");

        var edited = EditedEvent("sit-06", 4);
        var published = await methodologist.PostAsJsonAsync("/api/cms/events/sit-06/publish", new { expectedVersion = 3, document = edited });
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        var newTrip = await TripApiTests.TripClient.Start(factory, "business");
        await newTrip.Choose("a", "a");
        await newTrip.Proactive("obhod");
        await oldTrip.Choose("a");
        await newTrip.Choose("a");
        Assert.Equal(7, newTrip.Scales.Loyalty - oldTrip.Scales.Loyalty);
        Assert.Equal(3, (int)JsonNode.Parse(await factory.Database(db => db.Trips.Where(row => row.Id == oldTrip.Id).Select(row => row.EventVersions).SingleAsync()))!["sit-06"]!);
        Assert.Equal(4, (int)JsonNode.Parse(await factory.Database(db => db.Trips.Where(row => row.Id == newTrip.Id).Select(row => row.EventVersions).SingleAsync()))!["sit-06"]!);

        await oldTrip.Choose("a");
        await oldTrip.Choose("a", "a", "b", "a");
        Assert.Equal("arrived", oldTrip.Status);
        var debrief = await oldTrip.Debrief();
        var oldEvent = TripApiTests.DebriefEvents(debrief).Single(item => (string)item["eventId"]! == "sit-06");
        Assert.Equal(3, (int)oldEvent["version"]!);
    }

    private static Task<int> LatestVersion(TripApiFactory factory, string eventId) =>
        factory.Database(db => db.EventDocuments.Where(row => row.EventId == eventId).MaxAsync(row => row.Version));

    private static string EditedEvent(string eventId, int version)
    {
        var document = JsonNode.Parse(SeedContent.Events.Single(seed => seed.Document.Id == eventId).Json)!;
        document["version"] = version;
        document["title"] = "Исправленное Событие";
        document["steps"]![0]!["variants"]![0]!["scaleDeltas"]!["loyalty"] = 12;
        return document.ToJsonString();
    }
}
