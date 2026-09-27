using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TurboSquadApp.Tests.Trips;

public class TripReviewApiTests
{
    [Fact]
    public async Task Finished_trip_is_listed_with_the_same_decisions_after_reopening()
    {
        using var factory = new TripApiFactory();
        var trip = await TripApiTests.TripClient.Start(factory, "business");
        Assert.Empty((await trip.Http.GetFromJsonAsync<JsonArray>("/api/trips/debriefs"))!);

        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        factory.Clock.Advance(TimeSpan.FromSeconds(2));
        await trip.Choose("a", "a");
        await trip.Choose("a", "a", "b", "a");
        Assert.Equal("arrived", trip.Status);

        var history = (await trip.Http.GetFromJsonAsync<JsonArray>("/api/trips/debriefs"))!;
        var entry = Assert.Single(history);
        Assert.Equal(trip.Id.ToString(), (string)entry!["id"]!);
        Assert.Equal("arrived", (string)entry["result"]!);

        var reopened = (await trip.Http.GetFromJsonAsync<JsonNode>($"/api/trips/{trip.Id}/debrief"))!;
        var sit06 = TripApiTests.DebriefEvents(reopened).Single(e => (string)e["eventId"]! == "sit-06");
        var first = sit06["decisions"]!.AsArray()[0]!;
        Assert.Equal("a", (string)first["variantId"]!);
        Assert.Equal(2000, (int)first["elapsedMs"]!);
        Assert.Equal(10, (int)first["knowledgeDelta"]!);
        Assert.NotEmpty((string)first["comment"]!);
        Assert.NotEmpty(first["changes"]!.AsArray());

        using var anotherConductor = await factory.CreateConductorClient();
        Assert.Empty((await anotherConductor.GetFromJsonAsync<JsonArray>("/api/trips/debriefs"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anotherConductor.GetAsync($"/api/trips/{trip.Id}/debrief")).StatusCode);
    }
}
