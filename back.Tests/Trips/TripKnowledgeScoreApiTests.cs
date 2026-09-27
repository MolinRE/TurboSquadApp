using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TurboSquadApp.Tests.Trips;

public class TripKnowledgeScoreApiTests
{
    [Fact]
    public async Task Voice_error_does_not_score_and_timeout_removes_previous_mastery()
    {
        using var factory = new TripApiFactory();
        using var http = await factory.CreateConductorClient();

        var first = await TripApiTests.TripClient.Start(factory, "business", http);
        await first.Choose("a", "a");
        await first.Proactive("obhod");
        var failedVoice = await first.PostVoice("y");
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, failedVoice.StatusCode);
        Assert.Equal(0, await Points());
        await first.Choose("a");
        Assert.Equal(10, await Points());

        var second = await TripApiTests.TripClient.Start(factory, "business", http);
        await second.Choose("a", "a");
        await second.Proactive("obhod");
        factory.Clock.Advance(TimeSpan.FromSeconds(21));
        var timeout = await http.PostAsJsonAsync($"/api/trips/{second.Id}/timeout",
            new { eventId = "sit-06", stepId = "s1" });
        Assert.Equal(System.Net.HttpStatusCode.OK, timeout.StatusCode);
        Assert.Equal(0, await Points());

        var third = await TripApiTests.TripClient.Start(factory, "business", http);
        await third.Choose("a", "a");
        await third.Proactive("obhod");
        await third.Choose("a");
        Assert.Equal(10, await Points());

        async Task<int> Points() => (int)(await http.GetFromJsonAsync<JsonNode>("/api/profile"))!["knowledgePoints"]!;
    }

    [Fact]
    public async Task Accepted_trip_decisions_award_knowledge_once()
    {
        using var factory = new TripApiFactory();
        var trip = await TripApiTests.TripClient.Start(factory, "business");

        Assert.Equal(0, await Points());
        await trip.Choose("a"); // медкомиссия: заглушка без Знания
        Assert.Equal(0, await Points());
        Assert.Equal("StaleStep", await trip.Rejected($"/api/trips/{trip.Id}/variant",
            new { eventId = "zastup", stepId = "med", variantId = "a" }));
        Assert.Equal(0, await Points());
        await trip.Choose("a"); // приёмка: заглушка без Знания
        await trip.Proactive("obhod");
        await trip.Choose("a"); // №6, первый голосовой Шаг
        Assert.Equal(10, await Points());
        await trip.Choose("a"); // №6, кнопочный Шаг
        Assert.Equal(20, await Points());

        async Task<int> Points() => (int)(await trip.Http.GetFromJsonAsync<JsonNode>("/api/profile"))!["knowledgePoints"]!;
    }
}
