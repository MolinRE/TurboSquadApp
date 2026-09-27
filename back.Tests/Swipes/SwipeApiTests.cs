using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Swipes;

// Критерии тикета #27: Смена на свайпах проходится через API, правила считает сервер. Вопросы — сиды, база — в памяти.
public class SwipeApiTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Calm_shift_of_10_cards_is_played_through_api_to_result()
    {
        var shift = await ShiftClient.Start(factory, "calm");
        Assert.Equal(("calm", "running"), (shift.Mode, shift.Status));
        Assert.Null(shift.Json["cycle"]);
        Assert.Equal((0, 10, 0), (shift.Done, shift.Total, shift.Verdicts.Count));
        Assert.Equal(
            [("loyalty", 70, 0, 100, 70, 0), ("safety", 70, 0, 100, 70, 0)],
            shift.Json["scales"]!.AsArray().Select(s => (
                (string)s!["code"]!, (int)s["value"]!, (int)s["min"]!, (int)s["max"]!, (int)s["start"]!, (int)s["failureThreshold"]!)));

        var card = shift.Card!;
        var question = Seed(shift.CardId);
        Assert.Equal(
            ["questionId", "statement", "rightLabel", "leftLabel", "topic", "serviceClasses", "isRepeat", "readingMs", "timeLimitMs"],
            card.AsObject().Select(p => p.Key));
        Assert.Equal((question.Statement, question.Topic, false), ((string)card["statement"]!, (string)card["topic"]!, (bool)card["isRepeat"]!));
        Assert.Equal(question.Statement.Length * 35, (int)card["readingMs"]!);
        Assert.Null(card["timeLimitMs"]);

        var outcomes = await shift.Play((c, _) => Correct(c));

        Assert.Equal(10, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal("correct", (string)o["verdict"]!));
        var first = outcomes[0];
        Assert.Equal(Options(question).Correct.ToString().ToLowerInvariant(), (string)first["correctSide"]!);
        Assert.Equal(
            (question.Explanation.Text, question.Explanation.KeyFact, question.Explanation.Source),
            ((string)first["explanation"]!["text"]!, (string)first["explanation"]!["keyFact"]!, (string)first["explanation"]!["source"]!));
        Assert.Null(first["shift"]!["card"]);               // следующую карточку экран просит сам

        Assert.Equal(("passed", 10, 10), (shift.Status, shift.Done, shift.Total));
        Assert.Equal(Enumerable.Repeat("correct", 10), shift.Verdicts);
        var result = shift.Json["result"]!;
        Assert.Null(result["failedScale"]);
        Assert.Equal((10, 10, 0), ((int)result["firstTryCorrect"]!, (int)result["total"]!, (int)result["averageAnswerMs"]!));
        Assert.Empty(result["mistakes"]!.AsArray());
        Assert.Equal(10, shift.Shown.Distinct().Count());

        var answers = await factory.Database(db => db.SwipeAnswers.Where(a => a.ShiftId == shift.Id).OrderBy(a => a.Seq).ToListAsync());
        Assert.Equal(shift.Shown, answers.Select(a => a.QuestionId));
        Assert.All(answers, a => Assert.Equal(("correct", false), (a.Verdict, a.IsRepeat)));
        var record = await factory.Database(db => db.SwipeShifts.SingleAsync(s => s.Id == shift.Id));
        Assert.Equal(("calm", "passed"), (record.Mode, record.Status));
        Assert.Null(record.FailureScale);
        Assert.NotNull(record.FinishedAt);
    }

    [Fact]
    public async Task Wrong_answer_and_unknown_return_card_after_3_cards_at_most_twice()
    {
        var shift = await ShiftClient.Start(factory, "calm");
        string? unknown = null;
        var outcomes = await shift.Play((card, i) =>
        {
            if (i == 0) return Wrong(card);                              // вернётся одним Повтором и будет отвечен верно
            if (i == 1) unknown = (string)card["questionId"]!;
            return (string)card["questionId"]! == unknown ? "unknown" : Correct(card);   // «Не знаю» каждый раз
        });

        var shown = shift.Shown;
        Assert.Equal(13, shown.Count);                                   // 10 Вопросов + 1 Повтор + 2 Повтора
        Assert.Equal(shown[0], shown[4]);
        Assert.Equal([1, 5, 9], shown.Select((id, i) => (id, i)).Where(x => x.id == unknown).Select(x => x.i));
        Assert.Equal(
            [false, false, false, false, true, true, false, false, false, true, false, false, false],
            shift.ShownCards.Select(card => (bool)card["isRepeat"]!));

        // Прогресс считает Вопросы: Вопрос с ошибкой не закончен, пока у него есть Повторы; Повтор не добавляет точку.
        Assert.Equal([0, 0, 1, 2, 3, 3, 4, 5, 6, 7, 8, 9, 10], outcomes.Select(o => (int)o["shift"]!["progress"]!["done"]!));
        Assert.Equal(
            [1, 2, 3, 4, 4, 4, 5, 6, 7, 7, 8, 9, 10],
            outcomes.Select(o => o["shift"]!["progress"]!["verdicts"]!.AsArray().Count));
        Assert.Equal(["wrong", "unknown", .. Enumerable.Repeat("correct", 8)], shift.Verdicts);
        Assert.Equal("passed", shift.Status);
        var result = shift.Json["result"]!;
        Assert.Equal(8, (int)result["firstTryCorrect"]!);
        Assert.Equal([shown[0], unknown], result["mistakes"]!.AsArray().Select(m => (string)m!["questionId"]!));
        Assert.Equal(Seed(shown[0]).Explanation.Text, (string)result["mistakes"]![0]!["explanation"]!["text"]!);

        var answers = await factory.Database(db => db.SwipeAnswers.Where(a => a.ShiftId == shift.Id).OrderBy(a => a.Seq).ToListAsync());
        Assert.Equal(shift.ShownCards.Select(card => (bool)card["isRepeat"]!), answers.Select(a => a.IsRepeat));
        Assert.Equal(["wrong", "unknown", "correct", "correct", "correct", "unknown"], answers.Take(6).Select(a => a.Verdict));
    }

    [Fact]
    public async Task Scale_failure_ends_shift_at_once_and_work_on_mistakes_starts_after_it()
    {
        var shift = await ShiftClient.Start(factory, "calm");
        await shift.Play((card, _) => Wrong(card));

        Assert.Equal("failed", shift.Status);
        var failedScale = (string)shift.Json["result"]!["failedScale"]!;
        Assert.Equal(0, shift.Scale(failedScale));
        Assert.Null(shift.Card);
        Assert.True(shift.Done < shift.Total, "Срыв при незаконченных Вопросах");
        Assert.Equal("ShiftNotRunning", await shift.Rejected("answer", new { questionId = shift.Shown[^1], answer = "left" }));
        var record = await factory.Database(db => db.SwipeShifts.SingleAsync(s => s.Id == shift.Id));
        Assert.Equal(("failed", failedScale), (record.Status, record.FailureScale));

        var mistakes = shift.Json["result"]!["mistakes"]!.AsArray().Select(m => (string)m!["questionId"]!).ToList();
        Assert.Equal(shift.Shown.Distinct(), mistakes);                 // по разу на Вопрос, по порядку первых ответов

        var work = new ShiftClient(shift.Http, await shift.Post($"/api/swipe-shifts/{shift.Id}/work-on-mistakes", new { }));
        Assert.NotEqual(shift.Id, work.Id);
        Assert.Equal(("calm", "running", 0, mistakes.Count), (work.Mode, work.Status, work.Done, work.Total));
        Assert.Equal((70, 70), (work.Scale("loyalty"), work.Scale("safety")));
        await work.Play((card, _) => Correct(card));
        Assert.Equal(mistakes.Order(), work.Shown.Order());
        Assert.Equal("passed", work.Status);
        Assert.Equal("NoMistakes", await work.Rejected("work-on-mistakes", new { }));
    }

    [Fact]
    public async Task Work_on_mistakes_is_rejected_while_shift_runs()
    {
        var shift = await ShiftClient.Start(factory, "calm");
        await shift.Answer(Wrong(shift.Card!));

        Assert.Equal("ShiftNotFinished", await shift.Rejected("work-on-mistakes", new { }));
    }

    [Fact]
    public async Task Answer_time_runs_from_end_of_typing_by_server_clock_and_stale_answer_is_rejected()
    {
        var shift = await ShiftClient.Start(factory, "calm");
        var first = shift.Card!;
        factory.Clock.Advance(TimeSpan.FromMilliseconds((int)first["readingMs"]! + 3400));
        var outcome = await shift.Answer(Correct(first));
        Assert.Equal((3400, false), ((int)outcome["elapsedMs"]!, (bool)outcome["timedOut"]!));

        // Повторный ответ на ту же карточку — устаревший, как повторный клик в Рейсе.
        Assert.Equal("StaleCard", await shift.Rejected("answer", new { questionId = (string)first["questionId"]!, answer = Correct(first) }));

        // После перезагрузки: состояние без карточки, пока экран её не попросит.
        var state = await shift.Get();
        Assert.Null(state["card"]);
        Assert.Equal(1, shift.Done);

        await shift.NextCard();
        var second = shift.Card!;
        factory.Clock.Advance(TimeSpan.FromMilliseconds((int)second["readingMs"]! + 1000));
        await shift.NextCard();                                            // повторная просьба не меняет ни карточку, ни время
        Assert.Equal((string)second["questionId"]!, shift.CardId);
        Assert.Equal((string)second["questionId"]!, (string)(await shift.Get())["card"]!["questionId"]!);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.Equal(2000, (int)(await shift.Answer(Correct(second)))["elapsedMs"]!);

        await shift.Play((card, _) => Correct(card));                      // остальные — сразу после печати
        Assert.Equal((3400 + 2000) / 10, (int)shift.Json["result"]!["averageAnswerMs"]!);
    }

    [Fact]
    public async Task Speed_mode_runs_three_cycles_with_shorter_limits_and_times_out_by_server_clock()
    {
        var cycle1 = await ShiftClient.Start(factory, "woodpecker");
        Assert.Equal((1, 0), cycle1.CycleInfo);
        Assert.Equal([10_000, 7_000, 5_000], cycle1.Json["cycle"]!["timeLimitsMs"]!.AsArray().Select(l => (int)l!));
        var first = cycle1.Card!;
        Assert.Equal((10_000, Seed(cycle1.CardId).Statement.Length * 35), ((int)first["timeLimitMs"]!, (int)first["readingMs"]!));

        // «Время вышло» принимается, только когда лимит истёк по часам сервера, с допуском в секунду.
        Assert.Equal("TimeNotExpired", await cycle1.Rejected("timeout", new { questionId = cycle1.CardId }));
        factory.Clock.Advance(TimeSpan.FromMilliseconds((int)first["readingMs"]! + 9_001));
        var timedOut = await cycle1.TimeOut();
        Assert.Equal(("unknown", true, 10_000), ((string)timedOut["verdict"]!, (bool)timedOut["timedOut"]!, (int)timedOut["elapsedMs"]!));

        // Ответ позже лимита больше чем на секунду сервер сам превращает во «Время вышло»…
        await cycle1.NextCard();
        factory.Clock.Advance(TimeSpan.FromMilliseconds((int)cycle1.Card!["readingMs"]! + 11_001));
        var late = await cycle1.Answer(Correct(cycle1.Card!));
        Assert.Equal(("unknown", true, 10_000), ((string)late["verdict"]!, (bool)late["timedOut"]!, (int)late["elapsedMs"]!));

        // …а в пределах допуска принимает.
        await cycle1.NextCard();
        factory.Clock.Advance(TimeSpan.FromMilliseconds((int)cycle1.Card!["readingMs"]! + 10_900));
        var inTime = await cycle1.Answer(Correct(cycle1.Card!));
        Assert.Equal(("correct", false, 10_900), ((string)inTime["verdict"]!, (bool)inTime["timedOut"]!, (int)inTime["elapsedMs"]!));

        // Ошибка в Цикле Повтором не возвращается (open-questions §4).
        await cycle1.Play((card, i) => i == 0 ? Wrong(card) : Correct(card));
        Assert.Equal(10, cycle1.Shown.Count);
        Assert.All(cycle1.ShownCards, card => Assert.False((bool)card["isRepeat"]!));
        Assert.Equal(("passed", 10), (cycle1.Status, cycle1.Done));
        Assert.Equal(["unknown", "unknown", "correct", "wrong", .. Enumerable.Repeat("correct", 6)], cycle1.Verdicts);
        var rows = await factory.Database(db => db.SwipeAnswers.Where(a => a.ShiftId == cycle1.Id).OrderBy(a => a.Seq).ToListAsync());
        Assert.Equal(["timeout", "timeout"], rows.Take(2).Select(a => a.Answer));

        // Цикл 2: та же колода в новом порядке, лимит 7 с, печать быстрее, Шкалы с начала, итог Цикла 1.
        var cycle2 = new ShiftClient(cycle1.Http, await cycle1.Post($"/api/swipe-shifts/{cycle1.Id}/next-cycle", new { }));
        Assert.Equal((2, 1), cycle2.CycleInfo);
        var previous = cycle2.Json["cycle"]!["previous"]![0]!;
        Assert.Equal((1, 10_000, 7, 10, (10_000 + 10_000 + 10_900) / 10),
            ((int)previous["number"]!, (int)previous["timeLimitMs"]!, (int)previous["firstTryCorrect"]!, (int)previous["total"]!, (int)previous["averageAnswerMs"]!));
        Assert.Null(previous["failedScale"]);
        Assert.Equal((7_000, Seed(cycle2.CardId).Statement.Length * 21), ((int)cycle2.Card!["timeLimitMs"]!, (int)cycle2.Card!["readingMs"]!));
        Assert.Equal((70, 70), (cycle2.Scale("loyalty"), cycle2.Scale("safety")));
        await cycle2.Play((card, _) => Correct(card));
        Assert.Equal(cycle1.Shown.Order(), cycle2.Shown.Order());
        Assert.NotEqual(cycle1.Shown, cycle2.Shown);

        var cycle3 = new ShiftClient(cycle1.Http, await cycle2.Post($"/api/swipe-shifts/{cycle2.Id}/next-cycle", new { }));
        Assert.Equal((3, 2), cycle3.CycleInfo);
        Assert.Equal((5_000, Seed(cycle3.CardId).Statement.Length * 12), ((int)cycle3.Card!["timeLimitMs"]!, (int)cycle3.Card!["readingMs"]!));
        await cycle3.Play((card, _) => Correct(card));
        Assert.Equal("NoNextCycle", await cycle3.Rejected("next-cycle", new { }));
    }

    [Fact]
    public async Task Next_cycle_is_rejected_in_calm_mode_and_while_cycle_runs()
    {
        var calm = await ShiftClient.Start(factory, "calm");
        await calm.Play((card, _) => Correct(card));
        Assert.Equal("NoNextCycle", await calm.Rejected("next-cycle", new { }));
        Assert.Equal("NoTimeLimit", await (await ShiftClient.Start(factory, "calm")).RejectedTimeOut());

        var speed = await ShiftClient.Start(factory, "woodpecker");
        Assert.Equal("NoNextCycle", await speed.Rejected("next-cycle", new { }));
    }

    [Fact]
    public async Task Shift_needs_conductor_role_and_is_visible_only_to_its_conductor()
    {
        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var manager = await factory.CreateUserClient(UserRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" })).StatusCode);

        var shift = await ShiftClient.Start(factory, "calm");
        var stranger = await factory.CreateConductorClient();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/swipe-shifts/{shift.Id}")).StatusCode);
        var answer = await stranger.PostAsJsonAsync($"/api/swipe-shifts/{shift.Id}/answer", new { questionId = shift.CardId, answer = "left" });
        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal(0, await factory.Database(db => db.SwipeAnswers.CountAsync(a => a.ShiftId == shift.Id)));
    }

    internal static SeedQuestion Seed(string questionId) => SeedContent.Questions.Single(q => q.Id == questionId);

    internal static SwipeOptions Options(SeedQuestion question) => question.Options.Deserialize<SwipeOptions>(EventJson.Options)!;

    internal static string Correct(JsonNode card) => Options(Seed((string)card["questionId"]!)).Correct.ToString().ToLowerInvariant();

    internal static string Wrong(JsonNode card) => Correct(card) == "right" ? "left" : "right";

    /// <summary>Смена от имени нового Проводника: каждое действие — запрос к API, Json — состояние Смены.</summary>
    internal sealed class ShiftClient(HttpClient http, JsonNode json)
    {
        public JsonNode Json { get; private set; } = json;

        /// <summary>Показанные карточки по порядку показа.</summary>
        public List<JsonNode> ShownCards { get; } = [];

        /// <summary>Вопросы показанных карточек по порядку показа.</summary>
        public List<string> Shown => ShownCards.Select(card => (string)card["questionId"]!).ToList();

        public HttpClient Http => http;
        public Guid Id => Guid.Parse((string)Json["shiftId"]!);
        public string Mode => (string)Json["mode"]!;
        public string Status => (string)Json["status"]!;
        public JsonNode? Card => Json["card"];
        public string CardId => (string)Card!["questionId"]!;
        public int Done => (int)Json["progress"]!["done"]!;
        public int Total => (int)Json["progress"]!["total"]!;
        public List<string> Verdicts => Json["progress"]!["verdicts"]!.AsArray().Select(v => (string)v!).ToList();

        public int Scale(string code) =>
            (int)Json["scales"]!.AsArray().Single(s => (string)s!["code"]! == code)!["value"]!;

        public static async Task<ShiftClient> Start(TripApiFactory factory, string mode)
        {
            var http = await factory.CreateConductorClient();
            var response = await http.PostAsJsonAsync("/api/swipe-shifts", new { mode });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return new ShiftClient(http, (await response.Content.ReadFromJsonAsync<JsonNode>())!);
        }

        /// <summary>Отвечает на текущую карточку; Json — Смена из ответа, без следующей карточки.</summary>
        public async Task<JsonNode> Answer(string answer)
        {
            ShownCards.Add(Card!.DeepClone());
            var outcome = await Post($"/api/swipe-shifts/{Id}/answer", new { questionId = CardId, answer });
            Json = outcome["shift"]!;
            return outcome;
        }

        public async Task NextCard() => Json = await Post($"/api/swipe-shifts/{Id}/next-card", new { });

        /// <summary>«Время вышло» на текущей карточке.</summary>
        public async Task<JsonNode> TimeOut()
        {
            ShownCards.Add(Card!.DeepClone());
            var outcome = await Post($"/api/swipe-shifts/{Id}/timeout", new { questionId = CardId });
            Json = outcome["shift"]!;
            return outcome;
        }

        public Task<string> RejectedTimeOut() => Rejected("timeout", new { questionId = CardId });

        /// <summary>Номер Цикла и сколько у него итогов прошлых Циклов.</summary>
        public (int Number, int Previous) CycleInfo =>
            ((int)Json["cycle"]!["number"]!, Json["cycle"]!["previous"]!.AsArray().Count);

        /// <summary>Проходит Смену до итога: policy(карточка, номер показа) → ответ.</summary>
        public async Task<List<JsonNode>> Play(Func<JsonNode, int, string> policy)
        {
            var outcomes = new List<JsonNode>();
            while (Status == "running")
            {
                Assert.True(outcomes.Count <= 60, "Смена не кончается");
                if (Card is null) await NextCard();
                outcomes.Add(await Answer(policy(Card!, outcomes.Count)));
            }
            return outcomes;
        }

        public async Task<JsonNode> Get() =>
            Json = (await (await http.GetAsync($"/api/swipe-shifts/{Id}")).Content.ReadFromJsonAsync<JsonNode>())!;

        /// <summary>Действие отклонено: 409 с кодом причины.</summary>
        public async Task<string> Rejected(string action, object body)
        {
            var response = await http.PostAsJsonAsync($"/api/swipe-shifts/{Id}/{action}", body);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            return (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!;
        }

        public async Task<JsonNode> Post(string url, object body)
        {
            var response = await http.PostAsJsonAsync(url, body);
            Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        }
    }
}

// Своя база: тесты снимают Вопросы с публикации, Сменам класса SwipeApiTests это бы помешало.
public class SwipeDeckTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Draft_never_gets_into_deck_and_fewer_than_10_published_are_all_taken()
    {
        string[] published = ["sw-pet-carrier", "sw-wait-first", "sw-vape"];
        await PublishOnly(published);

        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");
        Assert.Equal(3, shift.Total);
        await shift.Play((card, _) => SwipeApiTests.Correct(card));
        Assert.Equal(published.Order(), shift.Shown.Order());
    }

    [Fact]
    public async Task Unknown_costs_half_of_wrong_side_penalties_without_its_bonus()
    {
        await PublishOnly("sw-pet-carrier");                 // вправо «Разрешить»: безопасность −15, лояльность +5; верно влево: +5
        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");

        var unknown = await shift.Answer("unknown");
        Assert.Equal("unknown", (string)unknown["verdict"]!);
        Assert.Equal(["safety:-7"], ScaleChanges(unknown));  // −15 / 2 с округлением к нулю, без +5 лояльности
        Assert.Equal((70, 63), (shift.Scale("loyalty"), shift.Scale("safety")));

        // Впереди карточек нет: Повтор — сразу следующей, и её сначала нужно показать.
        Assert.Equal("CardNotShown", await shift.Rejected("answer", new { questionId = "sw-pet-carrier", answer = "left" }));
        await shift.NextCard();
        Assert.True((bool)shift.Card!["isRepeat"]!);
        var wrong = await shift.Answer("right");
        Assert.Equal(["loyalty:5", "safety:-15"], ScaleChanges(wrong));
        await shift.NextCard();
        var correct = await shift.Answer("left");
        Assert.Equal(["safety:5"], ScaleChanges(correct));

        Assert.Equal(("passed", 1, 75, 53), (shift.Status, shift.Done, shift.Scale("loyalty"), shift.Scale("safety")));
        Assert.Equal(["unknown"], shift.Verdicts);
    }

    private static List<string> ScaleChanges(JsonNode outcome) =>
        outcome["scaleChanges"]!.AsObject().Select(change => $"{change.Key}:{(int)change.Value!}").Order().ToList();

    private Task PublishOnly(params string[] questionIds) => factory.Database(async db =>
    {
        foreach (var question in await db.Questions.ToListAsync())
            question.Status = questionIds.Contains(question.Id) ? QuestionStatuses.Published : QuestionStatuses.Draft;
        return await db.SaveChangesAsync();
    });
}
