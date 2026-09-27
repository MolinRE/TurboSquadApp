using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Voice;

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

        var decisions = await factory.Database(db => db.TripJournal.Where(r => r.TripId == trip.Id && r.Kind == "decision").ToListAsync());
        Assert.Equal(8, decisions.Count);
        Assert.All(decisions, d => Assert.Equal((d.EventId == "zastup" ? 1 : 4, false), (d.EventVersion, d.ElapsedMs is null)));
        var voiceAttempts = await factory.Database(db => db.TripJournal.Where(r => r.TripId == trip.Id && r.Kind == "voiceAttempt").ToListAsync());
        Assert.Equal(5, voiceAttempts.Count);
        Assert.All(voiceAttempts, attempt => Assert.True(attempt.VoiceApplied));
        Assert.Contains(decisions, decision => decision.EventId == "sit-06" && decision.StepId == "s2" && decision.VariantId == "a");
        var debriefVoices = debrief["voiceAttempts"]!.AsArray();
        Assert.Equal(5, debriefVoices.Count);
        Assert.Equal(
            [("sit-06", 4, "s1"), ("sit-33", 4, "s1"), ("sit-33", 4, "s2"), ("sit-33", 4, "s3"), ("sit-33", 4, "s4")],
            debriefVoices.Select(attempt => ((string)attempt!["eventId"]!, (int)attempt["eventVersion"]!, (string)attempt["stepId"]!)));
        Assert.All(debriefVoices, voiceAttempt =>
        {
            Assert.Equal(0.8, voiceAttempt!["score"]!.GetValue<double>(), 3);
            Assert.Equal(4, voiceAttempt["roleStages"]!.AsObject().Count);
            Assert.Equal(0.05, voiceAttempt["safetyViolation"]!.GetValue<double>(), 3);
            Assert.Equal(5, voiceAttempt["sttLatencyMs"]!.GetValue<int>());
            Assert.Equal(7, voiceAttempt["layaLatencyMs"]!.GetValue<int>());
            Assert.NotNull(voiceAttempt["llmLatencyMs"]);
        });

        using var manager = await factory.CreateManagerClient();
        var analytics = (await manager.GetFromJsonAsync<JsonNode>("/api/analytics/voice"))!;
        Assert.True((int)analytics["attempts"]! >= 1);
        Assert.Equal(0, (int)analytics["fallbackAttempts"]!);
        Assert.Contains(analytics["steps"]!.AsArray(), step =>
            (string)step!["eventId"]! == "sit-06" && (string)step["stepId"]! == "s1");
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
        Assert.Equal("TripNotRunning", await trip.Rejected($"/api/trips/{trip.Id}/variant", new { eventId = "sit-33", stepId = "s2", variantId = "a" }));

        var debrief = await trip.Debrief();
        Assert.Equal(
            [("zastup", "success"), ("sit-06", "failure"), ("sit-33", "interrupted")],
            DebriefEvents(debrief).Select(e => ((string)e["eventId"]!, (string)e["result"]!)));

        var record = await factory.Database(db => db.Trips.SingleAsync(t => t.Id == trip.Id));
        Assert.Equal(("failed", "scale", "loyalty"), (record.Status, record.FailureCause, record.FailureScale));
        Assert.NotNull(record.FinishedAt);
        var interrupted = await factory.Database(db => db.TripJournal.SingleAsync(r => r.TripId == trip.Id && r.Result == "interrupted"));
        Assert.Equal(("sit-33", 4), (interrupted.EventId, interrupted.EventVersion));
    }

    [Fact]
    public async Task Hidden_variant_is_not_offered_and_is_rejected()
    {
        var trip = await TripClient.Start(factory, "first");
        await trip.Choose("a", "a");
        await trip.Proactive("tech");
        await trip.Choose("a", "a");                  // №33 → s3, где «место классом выше» скрыто в Первом классе

        Assert.Equal(("sit-33", "s3", "voice"), (trip.EventId, trip.StepId, trip.AnswerType));
        var rejected = await trip.PostVoice("b");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("HiddenVariant", (string)(await rejected.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!);
    }

    [Fact]
    public async Task Voice_step_asks_laya_only_for_role_stages_of_its_variants()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        await trip.Choose("b", "a");                  // №6: s1 → s3 → №33, у Вариантов №6 нет этапов Ролевой модели
        await trip.Choose("a", "a");                  // №33: s1 → s2, у Варианта s2/a этапы «правило» и «решение»

        var requests = factory.Services.GetRequiredService<FakeVoicePipeline>().Requests;
        Assert.Empty(requests.Last(r => (r.EventId, r.StepId) == ("sit-06", "s1")).RoleStages);
        Assert.Equal(["acknowledge"], requests.Last(r => (r.EventId, r.StepId) == ("sit-33", "s1")).RoleStages);
        Assert.Equal(["rule", "solution"], requests.Last(r => (r.EventId, r.StepId) == ("sit-33", "s2")).RoleStages);
        Assert.Equal("Проводник сообщает, что оба билета на одно место, и зовёт начальника поезда",
            requests.Last(r => (r.EventId, r.StepId) == ("sit-33", "s2")).Questions.Single(q => q.Id == "a").Text);
    }

    [Fact]
    public async Task Escalation_conversation_uses_voice_for_second_spoken_step()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        await trip.Choose("b");

        Assert.Equal(("sit-06", "s3", "voice"), (trip.EventId, trip.StepId, trip.AnswerType));
        Assert.Equal("VoiceStepRequiresVoice", await trip.Rejected($"/api/trips/{trip.Id}/variant",
            new { eventId = trip.EventId, stepId = trip.StepId, variantId = "a" }));

        await trip.Choose("a");
        Assert.Equal("sit-33", trip.EventId);
        var voiceSteps = await factory.Database(db => db.TripJournal
            .Where(row => row.TripId == trip.Id && row.Kind == "voiceAttempt")
            .Select(row => row.StepId)
            .ToListAsync());
        Assert.Equal(["s1", "s3"], voiceSteps);
    }

    [Fact]
    public async Task Repeated_answer_to_previous_step_is_rejected()
    {
        var trip = await TripClient.Start(factory, "business");
        var med = new { eventId = "zastup", stepId = "med", variantId = "a" };
        await trip.Choose("a");                       // медкомиссия пройдена, теперь приёмка поезда

        Assert.Equal("StaleStep", await trip.Rejected($"/api/trips/{trip.Id}/variant", med));
        Assert.Equal("StaleStep", await trip.Rejected($"/api/trips/{trip.Id}/timeout", new { eventId = "zastup", stepId = "med" }));
        Assert.Equal("priemka", trip.StepId);
    }

    [Fact]
    public async Task Voice_step_rejects_button_input_and_keeps_step()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        Assert.Equal(("sit-06", "s1", "voice"), (trip.EventId, trip.StepId, trip.AnswerType));
        Assert.Empty(trip.Json["step"]!["variants"]!.AsArray());

        Assert.Equal("VoiceStepRequiresVoice", await trip.Rejected(
            $"/api/trips/{trip.Id}/variant",
            new { eventId = trip.EventId, stepId = trip.StepId, variantId = "a" }));
        Assert.Equal(("sit-06", "s1"), (trip.EventId, trip.StepId));
    }

    [Fact]
    public async Task Low_confidence_voice_attempt_asks_passenger_to_clarify_without_progress()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");

        var attemptId = Guid.NewGuid().ToString("N");
        var response = await trip.PostVoice("x", attemptId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("LowConfidence", (string)error["reason"]!);
        Assert.True((bool)error["trip"]!["voiceAttempt"]!["pending"]!);
        Assert.Equal(attemptId, (string)error["trip"]!["voiceAttempt"]!["attemptId"]!);

        var llm = factory.Services.GetRequiredService<FakeLlmClient>();
        var stream = await (await trip.StreamReply(attemptId)).Content.ReadAsStringAsync();
        Assert.Contains("event: done", stream);
        var prompt = llm.Requests.Last();
        Assert.Contains("правильно ли ты понял, что проводник вежливо просит пассажира вести себя тише", prompt.SystemPrompt);
        Assert.Contains("Невнятный ответ", prompt.UserPrompt);

        var after = await trip.Get();
        Assert.Equal(("sit-06", "s1"), ((string)after["step"]!["eventId"]!, (string)after["step"]!["stepId"]!));
        var attempt = await factory.Database(db => db.TripJournal
            .SingleAsync(row => row.TripId == trip.Id && row.Kind == "voiceAttempt"));
        Assert.Equal(("Невнятный ответ", false, "LowConfidence", "Пассажир отвечает"),
            (attempt.VoiceTranscript, attempt.VoiceApplied, attempt.VoiceError, attempt.VoicePassengerReply));

        var repeated = await (await trip.StreamReply(attemptId)).Content.ReadAsStringAsync();
        Assert.Contains("event: done", repeated);
        await trip.Choose("a");                       // после уточнения проводник отвечает снова на том же Шаге
        Assert.Equal(("sit-06", "s2"), (trip.EventId, trip.StepId));
    }

    [Fact]
    public async Task Passenger_reply_prompt_sets_tone_by_loyalty()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        await trip.Choose("a");

        var prompt = factory.Services.GetRequiredService<FakeLlmClient>().Requests.Last();
        Assert.Contains(PassengerTone.For(trip.Scales.Loyalty), prompt.SystemPrompt);
    }

    [Fact]
    public async Task Provider_error_returns_explicit_attempt_error_without_progress()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");

        var response = await trip.PostVoice("y");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("SttUnavailable", (string)error["reason"]!);
        Assert.Equal(("sit-06", "s1"), (trip.EventId, trip.StepId));
        var attempt = await factory.Database(db => db.TripJournal.SingleAsync(row => row.TripId == trip.Id && row.Kind == "voiceAttempt"));
        Assert.Equal(("SttUnavailable", false), (attempt.VoiceError, attempt.VoiceApplied));
    }

    [Fact]
    public async Task Passenger_reply_is_streamed_and_duplicate_attempt_is_idempotent()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        var attemptId = Guid.NewGuid().ToString("N");

        var first = await trip.PostVoice("a", attemptId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstJson = (await first.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(attemptId, (string)firstJson["voiceAttempt"]!["attemptId"]!);
        Assert.True((bool)firstJson["voiceAttempt"]!["pending"]!);
        Assert.Equal("s2", (string)firstJson["step"]!["stepId"]!);
        Assert.Empty(await factory.Database(db => db.TripJournal
            .Where(row => row.TripId == trip.Id && row.Kind == "decision" && row.EventId == "sit-06")
            .ToListAsync()));

        var callsBefore = factory.Services.GetRequiredService<FakeLlmClient>().Calls;
        var stream = await trip.StreamReply(attemptId);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        var sse = await stream.Content.ReadAsStringAsync();
        Assert.Contains("event: token", sse);
        Assert.Contains("event: done", sse);

        var duplicate = await trip.PostVoice("a", attemptId);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var rows = await factory.Database(db => db.TripJournal
            .Where(row => row.TripId == trip.Id && row.Kind == "voiceAttempt")
            .ToListAsync());
        Assert.Single(rows);
        Assert.Equal("Пассажир отвечает", rows[0].VoicePassengerReply);
        Assert.Equal(callsBefore + 1, factory.Services.GetRequiredService<FakeLlmClient>().Calls);
    }

    [Fact]
    public async Task Passenger_reply_error_is_streamed_and_persisted_without_alternative_transition()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        var attemptId = Guid.NewGuid().ToString("N");

        var first = await trip.PostVoice("b", attemptId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var fakeLlm = factory.Services.GetRequiredService<FakeLlmClient>();
        fakeLlm.Fail = true;
        try
        {
            var streamed = await trip.StreamReply(attemptId);
            var sse = await streamed.Content.ReadAsStringAsync();
            Assert.Contains("event: error", sse);
            Assert.Contains("LlmProviderError", sse);
        }
        finally
        {
            fakeLlm.Fail = false;
        }

        var row = await factory.Database(db => db.TripJournal.SingleAsync(item => item.TripId == trip.Id && item.Kind == "voiceAttempt"));
        Assert.Equal("LlmProviderError", row.VoiceReplyError);
        Assert.Equal("s3", (string)(await first.Content.ReadFromJsonAsync<JsonNode>())!["step"]!["stepId"]!);
        Assert.Empty(await factory.Database(db => db.TripJournal
            .Where(item => item.TripId == trip.Id && item.Kind == "decision" && item.EventId == "sit-06")
            .ToListAsync()));
        var current = await trip.Get();
        Assert.Equal("s1", (string)current["step"]!["stepId"]!);
    }

    [Fact]
    public async Task Client_cancellation_is_recorded_as_explicit_reply_error()
    {
        var trip = await TripClient.Start(factory, "business");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");
        var attemptId = Guid.NewGuid().ToString("N");
        var first = await trip.PostVoice("c", attemptId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var fakeLlm = factory.Services.GetRequiredService<FakeLlmClient>();
        fakeLlm.Block = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trip.StreamReply(attemptId, cancellation.Token));
        }
        finally
        {
            fakeLlm.Block = false;
        }
        await Task.Delay(100);

        var row = await factory.Database(db => db.TripJournal.SingleAsync(item => item.TripId == trip.Id && item.Kind == "voiceAttempt"));
        Assert.Equal("LlmClientDisconnected", row.VoiceReplyError);
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

        Assert.Equal("TimerNotExpired", await trip.Rejected($"/api/trips/{trip.Id}/timeout", new { eventId = trip.EventId, stepId = trip.StepId }));
        factory.Clock.Advance(TimeSpan.FromSeconds(22.5));
        await trip.Choose("a");                       // позже таймера больше чем на допуск загрузки голоса (2 с): ветка таймаута

        Assert.Equal(("s2", 65, 85), (trip.StepId, trip.Scales.Loyalty, trip.Scales.Safety));
        var timedOut = await factory.Database(db =>
            db.TripJournal.SingleAsync(r => r.TripId == trip.Id && r.Kind == "decision" && r.EventId == "sit-33" && r.StepId == "s1"));
        Assert.Equal((true, null, 22500), (timedOut.TimedOut, timedOut.VariantId, timedOut.ElapsedMs));
    }

    [Fact]
    public async Task Voice_answer_started_before_deadline_counts_despite_recording_and_processing_time()
    {
        var trip = await TripClient.Start(factory, "standard");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");                // №6 s1, таймер 20 с

        factory.Clock.Advance(TimeSpan.FromSeconds(23));
        var response = await trip.PostVoice("s", recordingMs: 5_000);   // начал на 18-й секунде, обработка ещё 5 с

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("s2", (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["step"]!["stepId"]!);
    }

    [Fact]
    public async Task Voice_answer_started_after_deadline_and_upload_tolerance_follows_timeout_branch()
    {
        var trip = await TripClient.Start(factory, "standard");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");

        factory.Clock.Advance(TimeSpan.FromSeconds(25));
        var response = await trip.PostVoice("a", recordingMs: 2_000);    // начал на 23-й секунде: позже 20 + 2 с

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("VoiceDeadlineExceeded", (string)body["voiceAttempt"]!["errorCode"]!);
        Assert.Equal("s3", (string)body["step"]!["stepId"]!);   // ветка таймаута №6 s1
        Assert.True(await factory.Database(db => db.TripJournal.AnyAsync(
            r => r.TripId == trip.Id && r.Kind == "decision" && r.StepId == "s1" && r.TimedOut)));
    }

    [Fact]
    public async Task Passenger_reply_finishing_after_deadline_still_applies_accepted_answer()
    {
        var trip = await TripClient.Start(factory, "standard");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");

        factory.Clock.Advance(TimeSpan.FromSeconds(19));
        var attemptId = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await trip.PostVoice("a", attemptId, recordingMs: 2_000)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(10));
        var stream = await (await trip.StreamReply(attemptId)).Content.ReadAsStringAsync();

        Assert.Contains("event: done", stream);
        Assert.Equal("s2", (string)(await trip.Get())["step"]!["stepId"]!);
    }

    [Fact]
    public async Task Passenger_clarification_restarts_step_timer()
    {
        var trip = await TripClient.Start(factory, "standard");
        await trip.Choose("a", "a");
        await trip.Proactive("obhod");                // №6 s1, таймер 20 с

        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        var attemptId = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await trip.PostVoice("x", attemptId, recordingMs: 3_000)).StatusCode);
        Assert.Contains("event: done", await (await trip.StreamReply(attemptId)).Content.ReadAsStringAsync());

        var after = await trip.Get();
        Assert.Equal(factory.Clock.Now.AddSeconds(20), (DateTimeOffset)after["step"]!["expiresAt"]!);
    }

    internal static IEnumerable<JsonNode> DebriefEvents(JsonNode debrief) =>
        debrief["items"]!.AsArray().Where(i => (string)i!["kind"]! == "event").Select(i => i!);

    /// <summary>Рейс от имени нового Проводника: каждый ход — запрос к API, ответ — состояние Рейса.</summary>
    internal sealed class TripClient(HttpClient http, JsonNode json)
    {
        public JsonNode Json { get; private set; } = json;
        public HttpClient Http => http;

        public Guid Id => Guid.Parse((string)Json["id"]!);
        public string Status => (string)Json["status"]!;
        public string? EventId => (string?)Json["step"]?["eventId"];
        public string? StepId => (string?)Json["step"]?["stepId"];
        public string? AnswerType => (string?)Json["step"]?["answerType"];
        public (int Loyalty, int Safety) Scales => (Scale("loyalty"), Scale("safety"));

        private int Scale(string code) =>
            (int)Json["scales"]!.AsArray().Single(s => (string)s!["code"]! == code)!["value"]!;

        public static async Task<TripClient> Start(TripApiFactory factory, string serviceClass, HttpClient? client = null)
        {
            var http = client ?? await factory.CreateConductorClient();
            var response = await http.PostAsJsonAsync("/api/trips", new { serviceClass });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return new TripClient(http, (await response.Content.ReadFromJsonAsync<JsonNode>())!);
        }

        public async Task Choose(params string[] variantIds)
        {
            foreach (var variantId in variantIds)
                Json = AnswerType == "voice"
                    ? await Voice(variantId)
                    : await Post($"/api/trips/{Id}/variant", new { eventId = EventId, stepId = StepId, variantId });
        }

        public async Task<JsonNode> Voice(string marker)
        {
            var response = await PostVoice(marker);
            Assert.True(response.IsSuccessStatusCode,
                $"/voice: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            var pending = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            if ((bool?)pending["voiceAttempt"]?["pending"] != true)
                return await Get();
            var attemptId = (string)pending["voiceAttempt"]!["attemptId"]!;
            var stream = await StreamReply(attemptId);
            Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
            Assert.Contains("event: done", await stream.Content.ReadAsStringAsync());
            return await Get();
        }

        public async Task<HttpResponseMessage> PostVoice(string marker, string? attemptId = null, int? recordingMs = null)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(EventId!), "eventId");
            form.Add(new StringContent(StepId!), "stepId");
            form.Add(new StringContent(attemptId ?? Guid.NewGuid().ToString("N")), "attemptId");
            var audio = new ByteArrayContent([ (byte)marker[0] ]);
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(audio, "audio", "answer.wav");
            if (recordingMs is not null) form.Add(new StringContent(recordingMs.Value.ToString()), "recordingMs");

            return await http.PostAsync($"/api/trips/{Id}/voice", form);
        }

        public Task<HttpResponseMessage> StreamReply(string attemptId, CancellationToken cancellationToken = default) =>
            http.GetAsync($"/api/trips/{Id}/voice/{attemptId}/reply", cancellationToken);

        public async Task<JsonNode> Get() =>
            (await (await http.GetAsync($"/api/trips/{Id}")).Content.ReadFromJsonAsync<JsonNode>())!;

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

// Своя база: тест меняет опубликованный контент, другим Рейсам класса TripApiTests это бы помешало.
public class TripContentPinningTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Trip_keeps_content_it_started_with_when_content_changes_mid_trip()
    {
        var trip = await TripApiTests.TripClient.Start(factory, "business");
        await trip.Choose("a", "a");

        // Методист публикует №6 v5 и добавляет в пул Событие, которого в этом Рейсе нет.
        var v5 = JsonNode.Parse(SeedContent.Events.Single(e => e.Document.Id == "sit-06").Json)!;
        v5["version"] = 5;
        v5["title"] = "Пассажир навеселе (v5)";
        var settings = JsonNode.Parse(await factory.Database(db => db.TripSettings.Select(s => s.Document).SingleAsync()))!;
        settings["proactiveChoice"]!["options"]![0]!["pool"]!.AsArray().Add("sit-99");
        await factory.Database(db =>
        {
            db.EventDocuments.Add(new EventDocumentRecord { EventId = "sit-06", Version = 5, Document = v5.ToJsonString() });
            db.TripSettings.Single().Document = settings.ToJsonString();
            return db.SaveChangesAsync();
        });

        await trip.Proactive("obhod");
        await trip.Choose("a", "a");                  // №6 той версии, с которой Рейс начат
        await trip.Choose("a", "a", "b", "a");        // №33

        Assert.Equal("arrived", trip.Status);
        var sit06 = TripApiTests.DebriefEvents(await trip.Debrief()).Single(e => (string)e["eventId"]! == "sit-06");
        Assert.Equal(("Пассажир с признаками алкогольного опьянения", 4), ((string)sit06["title"]!, (int)sit06["version"]!));
    }
}
