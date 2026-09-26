using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Tests.Trips;

// Критерии тикета #7: Рейс проходится через API, журнал решений лежит в базе. Контент — сиды, база — в памяти.
public class TripApiTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Successful_trip_is_played_through_api_to_arrival_and_debrief()
    {
        var trip = await TripClient.Start(factory, "business");
        Assert.Equal(("running", "zastup", "med"), (trip.Status, trip.EventId, trip.StepId));

        await trip.Choose("a", "a");
        Assert.Equal(["obhod", "tech"], trip.Json["proactiveChoice"]!["options"]!.AsArray().Select(o => (string)o!["id"]!));
        await trip.Proactive("obhod");
        await trip.Choose("a", "a");                  // №6
        await trip.Choose("a", "a", "b", "a");        // №33

        Assert.Equal("arrived", trip.Status);
        Assert.Equal((100, 95), trip.Scales);
        Assert.StartsWith("Прибытие", (string)trip.Json["result"]!["summary"]!);

        var debrief = await trip.Debrief();
        Assert.Equal("arrived", (string)debrief["result"]!);
        Assert.Equal(
            [("zastup", "success"), ("sit-06", "success"), ("sit-33", "success")],
            DebriefEvents(debrief).Select(e => ((string)e["eventId"]!, (string)e["result"]!)));

        await using var db = factory.OpenDatabase();
        var decisions = await db.TripJournal.Where(r => r.TripId == trip.Id && r.Kind == "decision").ToListAsync();
        Assert.Equal(8, decisions.Count);
        Assert.All(decisions, d => Assert.Equal((1, false), (d.EventVersion, d.ElapsedMs is null)));
    }

    [Fact]
    public async Task Failed_trip_marks_event_interrupted_in_journal_and_debrief()
    {
        var trip = await TripClient.Start(factory, "standard");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        await trip.Choose("c", "a");                  // №6: неудачный Исход
        await trip.Choose("b", "b");                  // №33: лояльность 0

        Assert.Equal("failed", trip.Status);
        Assert.Equal((0, 70), trip.Scales);
        Assert.Contains("Лояльность пассажира", (string)trip.Json["result"]!["summary"]!);
        Assert.Equal("TripNotRunning", await trip.Rejected($"/api/trips/{trip.Id}/variant", new { variantId = "a" }));

        var debrief = await trip.Debrief();
        Assert.Equal(
            [("zastup", "success"), ("sit-06", "failure"), ("sit-33", "interrupted")],
            DebriefEvents(debrief).Select(e => ((string)e["eventId"]!, (string)e["result"]!)));

        await using var db = factory.OpenDatabase();
        var record = await db.Trips.SingleAsync(t => t.Id == trip.Id);
        Assert.Equal(("failed", "scale", "loyalty"), (record.Status, record.FailureCause, record.FailureScale));
        Assert.NotNull(record.FinishedAt);
        var interrupted = await db.TripJournal.SingleAsync(r => r.TripId == trip.Id && r.Result == "interrupted");
        Assert.Equal(("sit-33", 1), (interrupted.EventId, interrupted.EventVersion));
    }

    [Fact]
    public async Task Hidden_variant_is_not_offered_and_is_rejected()
    {
        var trip = await TripClient.Start(factory, "first");
        await trip.Choose("a", "a");
        await trip.Proactive("tech");
        await trip.Choose("a", "a");                  // №33 → s3, где «место классом выше» скрыто в Первом классе

        Assert.Equal(("sit-33", "s3"), (trip.EventId, trip.StepId));
        Assert.Equal(["a"], trip.Json["step"]!["variants"]!.AsArray().Select(v => (string)v!["id"]!));
        Assert.Equal("HiddenVariant", await trip.Rejected($"/api/trips/{trip.Id}/variant", new { variantId = "b" }));
    }

    [Fact]
    public async Task Answer_later_than_timer_and_tolerance_follows_timeout_branch()
    {
        var trip = await TripClient.Start(factory, "standard");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");                // №6 s1, таймер 20 с
        Assert.Equal(factory.Clock.Now.AddSeconds(20), (DateTimeOffset)trip.Json["step"]!["expiresAt"]!);

        factory.Clock.Advance(TimeSpan.FromSeconds(20.5));
        await trip.Choose("a");                       // в пределах допуска: Вариант принят
        Assert.Equal(("s2", 75, 75), (trip.StepId, trip.Scales.Loyalty, trip.Scales.Safety));
        await trip.Choose("a");                       // №6 → удачный Исход, №33 s1, таймер 20 с

        Assert.Equal("TimerNotExpired", await trip.Rejected($"/api/trips/{trip.Id}/timeout", new { }));
        factory.Clock.Advance(TimeSpan.FromSeconds(21.5));
        await trip.Choose("a");                       // позже таймера больше чем на допуск: ветка таймаута

        Assert.Equal(("s2", 65, 85), (trip.StepId, trip.Scales.Loyalty, trip.Scales.Safety));
        await using var db = factory.OpenDatabase();
        var timedOut = await db.TripJournal.SingleAsync(r => r.TripId == trip.Id && r.EventId == "sit-33" && r.StepId == "s1");
        Assert.Equal((true, null, 21500), (timedOut.TimedOut, timedOut.VariantId, timedOut.ElapsedMs));
    }

    private static IEnumerable<JsonNode> DebriefEvents(JsonNode debrief) =>
        debrief["items"]!.AsArray().Where(i => (string)i!["kind"]! == "event").Select(i => i!);

    /// <summary>Рейс от имени нового Проводника: каждый ход — запрос к API, ответ — состояние Рейса.</summary>
    private sealed class TripClient(HttpClient http, JsonNode json)
    {
        public JsonNode Json { get; private set; } = json;

        public Guid Id => Guid.Parse((string)Json["id"]!);
        public string Status => (string)Json["status"]!;
        public string? EventId => (string?)Json["step"]?["eventId"];
        public string? StepId => (string?)Json["step"]?["stepId"];
        public (int Loyalty, int Safety) Scales => (Scale("loyalty"), Scale("safety"));

        private int Scale(string code) =>
            (int)Json["scales"]!.AsArray().Single(s => (string)s!["code"]! == code)!["value"]!;

        public static async Task<TripClient> Start(TripApiFactory factory, string serviceClass)
        {
            var http = await factory.CreateConductorClient();
            var response = await http.PostAsJsonAsync("/api/trips", new { serviceClass });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return new TripClient(http, (await response.Content.ReadFromJsonAsync<JsonNode>())!);
        }

        public async Task Choose(params string[] variantIds)
        {
            foreach (var variantId in variantIds)
                Json = await Post($"/api/trips/{Id}/variant", new { variantId });
        }

        public async Task Proactive(string optionId) => Json = await Post($"/api/trips/{Id}/proactive", new { optionId });

        public async Task<JsonNode> Debrief()
        {
            var response = await http.GetAsync($"/api/trips/{Id}/debrief");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        }

        /// <summary>Ход отклонён: 409 с кодом причины.</summary>
        public async Task<string> Rejected(string url, object body)
        {
            var response = await http.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            return (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!;
        }

        private async Task<JsonNode> Post(string url, object body)
        {
            var response = await http.PostAsJsonAsync(url, body);
            Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        }
    }
}
