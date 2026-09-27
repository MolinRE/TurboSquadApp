using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Questions;
using TurboSquadApp.Tests.Swipes;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Blitz;

// Критерии тикета #41: сессия Блица с одним ответом проходится через API, вердикт и время считает сервер.
// Вопросы — сиды #40, база — в памяти; в колоде только два single, multiple проверяет BlitzMultipleTests.
public class BlitzApiTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    /// <summary>Верные варианты сидов «Ситуаций на борту» (№21, №32) — независимо от кода сервера.</summary>
    internal static readonly Dictionary<string, string> CorrectOption = new()
    {
        ["bz-alcohol-bistro"] = "b",
        ["bz-dead-phone-ticket"] = "b",
    };

    internal static string Wrong(string questionId) => CorrectOption[questionId] == "a" ? "b" : "a";

    private async Task<BlitzClient> Start()
    {
        await SwipeDeckTests.PublishOnly(factory, [.. CorrectOption.Keys]);
        return await BlitzClient.Start(factory);
    }

    [Fact]
    public async Task Single_session_is_played_through_api_to_result_without_revealing_correct_option_before_answer()
    {
        var session = await Start();
        Assert.Equal(("running", 0, 2), (session.Status, session.Done, session.Total));

        var question = session.Question!;
        Assert.Equal(["questionId", "type", "statement", "topic", "options", "timeLimitMs"], question.AsObject().Select(p => p.Key));
        Assert.Equal(("single", 15_000), ((string)question["type"]!, (int)question["timeLimitMs"]!));
        Assert.All(question["options"]!.AsArray(), option => Assert.Equal(["id", "text"], option!.AsObject().Select(p => p.Key)));
        Assert.Equal(4, question["options"]!.AsArray().Count);

        var firstId = session.QuestionId;
        var wrong = await session.Answer(Wrong(firstId));
        Assert.Equal(("wrong", false), ((string)wrong["verdict"]!, (bool)wrong["timedOut"]!));
        Assert.Equal([CorrectOption[firstId]], wrong["correctOptionIds"]!.AsArray().Select(id => (string)id!));
        Assert.False(string.IsNullOrEmpty((string)wrong["explanation"]!["keyFact"]!));
        Assert.StartsWith("Ситуации на борту", (string)wrong["explanation"]!["source"]!);
        Assert.Null(session.Question);                                  // следующий Вопрос экран просит сам, после Пояснения

        await session.NextQuestion();
        var secondId = session.QuestionId;
        Assert.NotEqual(firstId, secondId);
        var correct = await session.Answer(CorrectOption[secondId]);
        Assert.Equal("correct", (string)correct["verdict"]!);

        Assert.Equal(("finished", 2, 2), (session.Status, session.Done, session.Total));
        Assert.Equal(["wrong", "correct"], session.Verdicts);
        var result = session.Json["result"]!;
        Assert.Equal((1, 2), ((int)result["correct"]!, (int)result["total"]!));
        Assert.Equal([firstId], result["mistakes"]!.AsArray().Select(m => (string)m!["questionId"]!));

        var answers = await factory.Database(db => db.BlitzAnswers.Where(a => a.SessionId == session.Id).OrderBy(a => a.Seq).ToListAsync());
        Assert.Equal([(firstId, "wrong"), (secondId, "correct")], answers.Select(a => (a.QuestionId, a.Verdict)));
    }

    [Fact]
    public async Task Server_clock_turns_late_answer_into_timeout_and_repeated_answer_gives_no_second_result()
    {
        var session = await Start();
        var firstId = session.QuestionId;
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = new[] { "a", "b" } }));
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = new[] { "z" } }));

        // Ответ позже лимита 15 с больше чем на секунду сервер сам засчитывает как «Время вышло».
        factory.Clock.Advance(TimeSpan.FromMilliseconds(16_001));
        var late = await session.Answer(CorrectOption[firstId]);
        Assert.Equal(("unknown", true, 15_000), ((string)late["verdict"]!, (bool)late["timedOut"]!, (int)late["elapsedMs"]!));
        Assert.Equal([CorrectOption[firstId]], late["correctOptionIds"]!.AsArray().Select(id => (string)id!));

        // Повторный ответ на тот же Вопрос отклоняется и второго результата не создаёт.
        Assert.Equal("StaleQuestion", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = new[] { "b" } }));
        Assert.Equal("StaleQuestion", await session.Rejected("timeout", new { questionId = firstId }));
        var answers = await factory.Database(db => db.BlitzAnswers.Where(a => a.SessionId == session.Id).ToListAsync());
        Assert.Equal([(firstId, true, "unknown")], answers.Select(a => (a.QuestionId, a.TimedOut, a.Verdict)));

        // Следующий Вопрос: время идёт с показа, повторный показ его не сбрасывает.
        Assert.Null(session.Question);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));                  // панель с Пояснением время не тратит
        await session.NextQuestion();
        var secondId = session.QuestionId;
        Assert.Equal("TimeNotExpired", await session.Rejected("timeout", new { questionId = secondId }));
        factory.Clock.Advance(TimeSpan.FromMilliseconds(10_000));
        await session.NextQuestion();
        Assert.Equal(secondId, session.QuestionId);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(4_100));          // 14,1 с: в пределах допуска
        var timedOut = await session.TimeOut();
        Assert.Equal(("unknown", true, 15_000), ((string)timedOut["verdict"]!, (bool)timedOut["timedOut"]!, (int)timedOut["elapsedMs"]!));
        Assert.Equal("finished", session.Status);
        Assert.Equal(2, session.Json["result"]!["mistakes"]!.AsArray().Count);
        Assert.Equal("SessionNotRunning", await session.Rejected("answer", new { questionId = secondId, selectedOptionIds = new[] { "b" } }));
    }

    [Fact]
    public async Task Answer_in_time_is_timed_from_showing_by_server_clock()
    {
        var session = await Start();
        factory.Clock.Advance(TimeSpan.FromMilliseconds(15_900));         // позже лимита, но в пределах допуска
        var outcome = await session.Answer(CorrectOption[session.QuestionId]);
        Assert.Equal(("correct", false, 15_900), ((string)outcome["verdict"]!, (bool)outcome["timedOut"]!, (int)outcome["elapsedMs"]!));

        Assert.Equal("QuestionNotShown", await session.Rejected("answer", new { questionId = NextId(session), selectedOptionIds = new[] { "b" } }));
        await session.NextQuestion();
        factory.Clock.Advance(TimeSpan.FromMilliseconds(2_300));
        Assert.Equal(2_300, (int)(await session.Answer(CorrectOption[session.QuestionId]))["elapsedMs"]!);
        Assert.Equal((15_900 + 2_300) / 2, (int)session.Json["result"]!["averageAnswerMs"]!);
    }

    [Fact]
    public async Task Session_needs_conductor_role_is_hidden_from_other_conductors_and_restores_after_reload()
    {
        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/blitz-sessions", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var manager = await factory.CreateUserClient(UserRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/blitz-sessions", new { })).StatusCode);

        var session = await Start();
        var stranger = await factory.CreateConductorClient();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/blitz-sessions/{session.Id}")).StatusCode);
        var body = new { questionId = session.QuestionId, selectedOptionIds = new[] { "b" } };
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/blitz-sessions/{session.Id}/answer", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/blitz-sessions/{session.Id}/timeout", new { body.questionId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/blitz-sessions/{session.Id}/next-question", new { })).StatusCode);
        Assert.Equal(0, await factory.Database(db => db.BlitzAnswers.CountAsync(a => a.SessionId == session.Id)));

        // Перезагрузка: своя сессия приходит с тем же Вопросом, а после ответа — без Вопроса, пока его не попросят.
        var shown = session.Question!.ToJsonString();
        Assert.Equal(shown, (await session.Get())["question"]!.ToJsonString());
        await session.Answer(CorrectOption[session.QuestionId]);
        var reloaded = await session.Get();
        Assert.Null(reloaded["question"]);
        Assert.Equal(1, session.Done);
        Assert.Equal(["correct"], session.Verdicts);
    }

    /// <summary>Второй Вопрос колоды из двух сидов single — тот, на который ещё не отвечали.</summary>
    private static string NextId(BlitzClient session) => CorrectOption.Keys.Single(id => id != session.Answered[0]);

    /// <summary>Сессия от имени нового Проводника: каждое действие — запрос к API, Json — состояние сессии.</summary>
    internal sealed class BlitzClient(HttpClient http, JsonNode json)
    {
        public JsonNode Json { get; private set; } = json;

        /// <summary>Вопросы, на которые ответили или у которых вышло время, по порядку.</summary>
        public List<string> Answered { get; } = [];

        public HttpClient Http => http;
        public Guid Id => Guid.Parse((string)Json["sessionId"]!);
        public string Status => (string)Json["status"]!;
        public JsonNode? Question => Json["question"];
        public string QuestionId => (string)Question!["questionId"]!;
        public int Done => (int)Json["progress"]!["done"]!;
        public int Total => (int)Json["progress"]!["total"]!;
        public List<string> Verdicts => Json["progress"]!["verdicts"]!.AsArray().Select(v => (string)v!).ToList();

        public static async Task<BlitzClient> Start(TripApiFactory factory) => await Start(await factory.CreateConductorClient());

        public static async Task<BlitzClient> Start(HttpClient http)
        {
            var response = await http.PostAsJsonAsync("/api/blitz-sessions", new { });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return new BlitzClient(http, (await response.Content.ReadFromJsonAsync<JsonNode>())!);
        }

        /// <summary>Отвечает на текущий Вопрос; Json — сессия из ответа, без следующего Вопроса.</summary>
        public async Task<JsonNode> Answer(params string[] selectedOptionIds)
        {
            Answered.Add(QuestionId);
            var outcome = await Post("answer", new { questionId = QuestionId, selectedOptionIds });
            Json = outcome["session"]!;
            return outcome;
        }

        /// <summary>Отвечает на текущий Вопрос sequence порядком шагов.</summary>
        public async Task<JsonNode> AnswerOrder(params string[] orderedStepIds)
        {
            Answered.Add(QuestionId);
            var outcome = await Post("answer", new { questionId = QuestionId, orderedStepIds });
            Json = outcome["session"]!;
            return outcome;
        }

        public async Task<JsonNode> TimeOut()
        {
            Answered.Add(QuestionId);
            var outcome = await Post("timeout", new { questionId = QuestionId });
            Json = outcome["session"]!;
            return outcome;
        }

        public async Task NextQuestion() => Json = await Post("next-question", new { });

        public async Task<JsonNode> Get() =>
            Json = (await (await http.GetAsync($"/api/blitz-sessions/{Id}")).Content.ReadFromJsonAsync<JsonNode>())!;

        /// <summary>Действие отклонено: 409 с кодом причины.</summary>
        public async Task<string> Rejected(string action, object body)
        {
            var response = await http.PostAsJsonAsync($"/api/blitz-sessions/{Id}/{action}", body);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            return (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!;
        }

        private async Task<JsonNode> Post(string action, object body)
        {
            var response = await http.PostAsJsonAsync($"/api/blitz-sessions/{Id}/{action}", body);
            Assert.True(response.IsSuccessStatusCode, $"{action}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        }
    }
}

// Своя база: тесты снимают Вопросы с публикации и правят их, другим тестам это бы помешало.
public class BlitzDeckTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Drafts_never_get_into_session()
    {
        await SwipeDeckTests.PublishOnly(factory, "bz-dead-phone-ticket", "bz-unattended-item", "bz-role-model", "sw-pet-carrier");

        // Свайпы в Блиц не идут.
        var session = await BlitzApiTests.BlitzClient.Start(factory);
        Assert.Equal(3, session.Total);
        await AnswerByType(session);
        await session.NextQuestion();
        await AnswerByType(session);
        await session.NextQuestion();
        await AnswerByType(session);
        Assert.Equal(["bz-dead-phone-ticket", "bz-role-model", "bz-unattended-item"], session.Answered.Order());

        await SwipeDeckTests.PublishOnly(factory, "sw-pet-carrier");
        var http = await factory.CreateConductorClient();
        var response = await http.PostAsJsonAsync("/api/blitz-sessions", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NoPublishedQuestions", (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!);
    }

    private static Task<JsonNode> AnswerByType(BlitzApiTests.BlitzClient session) => (string)session.Question!["type"]! switch
    {
        "multiple" => session.Answer("a", "b", "c"),
        "sequence" => session.AnswerOrder("a", "b", "c", "d"),
        _ => session.Answer("b"),
    };

    [Fact]
    public async Task Edit_of_source_question_after_start_does_not_change_started_session()
    {
        await SwipeDeckTests.PublishOnly(factory, "bz-alcohol-bistro");
        var session = await BlitzApiTests.BlitzClient.Start(factory);
        var shown = session.Question!.ToJsonString();

        // Методист меняет формулировку, верный вариант, Пояснение и лимит уже после старта.
        await factory.Database(async db =>
        {
            var question = await db.Questions.SingleAsync(q => q.Id == "bz-alcohol-bistro");
            question.Statement = "Правка Методиста";
            question.SetOptions(new ChoiceOptions([new ChoiceOption("a", "Везде", true), new ChoiceOption("b", "Нигде", false)]));
            question.ExplanationText = "Новое Пояснение";
            question.TimeLimitSec = 5;
            return await db.SaveChangesAsync();
        });

        Assert.Equal(shown, (await session.Get())["question"]!.ToJsonString());
        factory.Clock.Advance(TimeSpan.FromSeconds(10));                  // по новому лимиту 5 с было бы «Время вышло»
        var outcome = await session.Answer("b");
        Assert.Equal(("correct", false), ((string)outcome["verdict"]!, (bool)outcome["timedOut"]!));
        Assert.StartsWith("Алкоголь допускается только в вагоне-бистро", (string)outcome["explanation"]!["text"]!);
    }
}

// Критерии тикета #42: Вопрос multiple засчитывается только за точный набор верных вариантов.
// Своя база: в колоде только два multiple из сидов #40.
public class BlitzMultipleTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    /// <summary>Верные варианты сидов «Ситуаций на борту» (№41, №19 и №28) — независимо от кода сервера; у обоих «d» неверный.</summary>
    private static readonly Dictionary<string, string[]> CorrectOptions = new()
    {
        ["bz-unattended-item"] = ["a", "b", "c"],
        ["bz-passenger-unwell"] = ["a", "b", "c"],
    };

    private async Task<BlitzApiTests.BlitzClient> Start()
    {
        await SwipeDeckTests.PublishOnly(factory, [.. CorrectOptions.Keys]);
        return await BlitzApiTests.BlitzClient.Start(factory);
    }

    [Fact]
    public async Task Exact_set_in_any_order_is_correct_and_correct_options_come_only_with_answer()
    {
        var session = await Start();
        Assert.Equal(("running", 0, 2), (session.Status, session.Done, session.Total));
        var question = session.Question!;
        Assert.Equal("multiple", (string)question["type"]!);
        Assert.All(question["options"]!.AsArray(), option => Assert.Equal(["id", "text"], option!.AsObject().Select(p => p.Key)));

        var firstId = session.QuestionId;
        var first = await session.Answer("c", "a", "b");
        Assert.Equal(("correct", false), ((string)first["verdict"]!, (bool)first["timedOut"]!));
        Assert.Equal(CorrectOptions[firstId], first["correctOptionIds"]!.AsArray().Select(id => (string)id!).Order());
        Assert.False(string.IsNullOrEmpty((string)first["explanation"]!["keyFact"]!));

        await session.NextQuestion();
        Assert.Equal("correct", (string)(await session.Answer("b", "c", "a"))["verdict"]!);
        Assert.Equal(("finished", 2, 2), (session.Status, session.Done, session.Total));
        Assert.Equal(2, (int)session.Json["result"]!["correct"]!);
    }

    [Fact]
    public async Task Missing_or_extra_option_is_wrong_without_partial_credit()
    {
        var session = await Start();
        var firstId = session.QuestionId;
        var missing = await session.Answer("a", "b");
        Assert.Equal("wrong", (string)missing["verdict"]!);
        Assert.Equal(CorrectOptions[firstId], missing["correctOptionIds"]!.AsArray().Select(id => (string)id!).Order());

        await session.NextQuestion();
        var secondId = session.QuestionId;
        Assert.Equal("wrong", (string)(await session.Answer("a", "b", "c", "d"))["verdict"]!);

        var result = session.Json["result"]!;
        Assert.Equal((0, 2), ((int)result["correct"]!, (int)result["total"]!));
        Assert.Equal([firstId, secondId], result["mistakes"]!.AsArray().Select(m => (string)m!["questionId"]!));
        var answers = await factory.Database(db => db.BlitzAnswers.Where(a => a.SessionId == session.Id).OrderBy(a => a.Seq).ToListAsync());
        Assert.Equal(["[\"a\",\"b\"]", "[\"a\",\"b\",\"c\",\"d\"]"], answers.Select(a => a.SelectedOptionIds));
    }

    [Fact]
    public async Task Unverifiable_selection_is_rejected_and_timeout_counts_as_mistake_by_rules_of_single()
    {
        var session = await Start();
        var firstId = session.QuestionId;
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = Array.Empty<string>() }));
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = new[] { "a", "a", "b", "c" } }));
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = new[] { "a", "z" } }));

        // Лимит сидов multiple — 20 с; позже больше чем на секунду даже точный набор — «Время вышло».
        factory.Clock.Advance(TimeSpan.FromMilliseconds(21_001));
        var late = await session.Answer("a", "b", "c");
        Assert.Equal(("unknown", true, 20_000), ((string)late["verdict"]!, (bool)late["timedOut"]!, (int)late["elapsedMs"]!));
        Assert.Equal(CorrectOptions[firstId], late["correctOptionIds"]!.AsArray().Select(id => (string)id!).Order());
        Assert.Equal("StaleQuestion", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = new[] { "a", "b", "c" } }));

        await session.NextQuestion();
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        var timedOut = await session.TimeOut();
        Assert.Equal(("unknown", true), ((string)timedOut["verdict"]!, (bool)timedOut["timedOut"]!));
        Assert.Equal(["unknown", "unknown"], session.Verdicts);
        Assert.Equal(2, session.Json["result"]!["mistakes"]!.AsArray().Count);
    }
}

// Критерии тикета #43: Вопрос sequence засчитывается только за полный верный порядок шагов.
// Своя база: в колоде только два sequence из сидов #40.
public class BlitzSequenceTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    /// <summary>Верный порядок шагов по тексту — из «Ситуаций на борту», независимо от кода сервера.</summary>
    private static readonly Dictionary<string, string[]> CorrectOrder = new()
    {
        ["bz-role-model"] = ["Признать ситуацию", "Обозначить правило", "Предложить решение", "Заверить"],
        ["bz-pet-carrier-steps"] =
        [
            "Вежливо напомнить правила провоза питомцев",
            "Предложить разместить животное в переноске, при продаже на борту — приобрести её",
            "При отказе вызвать начальника поезда",
        ],
    };

    private async Task<BlitzApiTests.BlitzClient> Start()
    {
        await SwipeDeckTests.PublishOnly(factory, [.. CorrectOrder.Keys]);
        return await BlitzApiTests.BlitzClient.Start(factory);
    }

    private static List<(string Id, string Text)> Shown(BlitzApiTests.BlitzClient session) =>
        session.Question!["options"]!.AsArray().Select(step => ((string)step!["id"]!, (string)step["text"]!)).ToList();

    /// <summary>id показанных шагов в верном порядке.</summary>
    private static string[] RightOrder(BlitzApiTests.BlitzClient session)
    {
        var shown = Shown(session);
        return CorrectOrder[session.QuestionId].Select(text => shown.Single(step => step.Text == text).Id).ToArray();
    }

    [Fact]
    public async Task Steps_come_shuffled_without_order_hint_and_full_right_order_is_correct()
    {
        var session = await Start();
        Assert.Equal(("running", 0, 2), (session.Status, session.Done, session.Total));
        var question = session.Question!;
        Assert.Equal("sequence", (string)question["type"]!);
        Assert.All(question["options"]!.AsArray(), step => Assert.Equal(["id", "text"], step!.AsObject().Select(p => p.Key)));

        // Шаги перемешаны, а id — по месту показа: ни порядок показа, ни id не выдают верный порядок.
        var shown = Shown(session);
        Assert.NotEqual(CorrectOrder[session.QuestionId], shown.Select(step => step.Text));
        Assert.Equal(shown.Select(step => step.Id).Order(), shown.Select(step => step.Id));
        var right = RightOrder(session);
        Assert.NotEqual(right.Order(), right);

        var outcome = await session.AnswerOrder(right);
        Assert.Equal(("correct", false), ((string)outcome["verdict"]!, (bool)outcome["timedOut"]!));
        Assert.Equal(right, outcome["correctOptionIds"]!.AsArray().Select(id => (string)id!));
        Assert.StartsWith("Ситуации на борту", (string)outcome["explanation"]!["source"]!);
    }

    [Fact]
    public async Task Swap_of_any_two_steps_is_wrong_without_partial_credit()
    {
        // Шесть сессий: в каждой оба Вопроса с k-й перестановкой пары — все пары у 4 шагов и у 3 шагов.
        for (var k = 0; k < 6; k++)
        {
            var session = await Start();
            for (var answered = 0; answered < 2; answered++)
            {
                if (answered > 0) await session.NextQuestion();
                var order = RightOrder(session);
                var pairs = (from i in Enumerable.Range(0, order.Length) from j in Enumerable.Range(i + 1, order.Length - i - 1) select (i, j)).ToList();
                var (a, b) = pairs[k % pairs.Count];
                (order[a], order[b]) = (order[b], order[a]);
                Assert.Equal("wrong", (string)(await session.AnswerOrder(order))["verdict"]!);
            }
            Assert.Equal(0, (int)session.Json["result"]!["correct"]!);
        }
    }

    [Fact]
    public async Task Order_and_time_are_stored_incomplete_order_is_rejected_and_timeout_counts_as_mistake()
    {
        var session = await Start();
        var firstId = session.QuestionId;
        var right = RightOrder(session);
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, orderedStepIds = right[..^1] }));
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, orderedStepIds = right.Append(right[0]).ToArray() }));
        Assert.Equal("InvalidSelection", await session.Rejected("answer", new { questionId = firstId, selectedOptionIds = right }));

        factory.Clock.Advance(TimeSpan.FromMilliseconds(3_400));
        Assert.Equal((3_400, "correct"), ((int)(await session.AnswerOrder(right))["elapsedMs"]!, session.Verdicts[0]));
        Assert.Equal("StaleQuestion", await session.Rejected("answer", new { questionId = firstId, orderedStepIds = right }));

        // Лимит сидов sequence — 25 с.
        await session.NextQuestion();
        factory.Clock.Advance(TimeSpan.FromSeconds(25));
        var timedOut = await session.TimeOut();
        Assert.Equal(("unknown", true, 25_000), ((string)timedOut["verdict"]!, (bool)timedOut["timedOut"]!, (int)timedOut["elapsedMs"]!));
        Assert.Equal(CorrectOrder[session.Answered[1]].Length, timedOut["correctOptionIds"]!.AsArray().Count);
        Assert.Equal(("finished", 1), (session.Status, (int)session.Json["result"]!["correct"]!));

        var answers = await factory.Database(db => db.BlitzAnswers.Where(a => a.SessionId == session.Id).OrderBy(a => a.Seq).ToListAsync());
        Assert.Equal(
            [(firstId, "[" + string.Join(",", right.Select(id => $"\"{id}\"")) + "]", "correct", 3_400), (session.Answered[1], "[]", "unknown", 25_000)],
            answers.Select(a => (a.QuestionId, a.SelectedOptionIds, a.Verdict, a.ElapsedMs)));
    }
}
