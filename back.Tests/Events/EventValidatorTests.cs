using System.Text.Json;
using System.Text.Json.Nodes;
using TurboSquadApp.Events;

namespace TurboSquadApp.Tests.Events;

public class EventValidatorTests
{
    // Минимальное корректное Событие: Шаг с таймером и веткой таймаута, два Исхода.
    internal const string ValidEventJson = """
        {
          "id": "sit-06",
          "version": 1,
          "title": "Пассажир с признаками алкогольного опьянения",
          "topic": "Конфликты",
          "conditions": [],
          "start": "s1",
          "steps": [
            {
              "id": "s1",
              "answerType": "buttons",
              "timerSec": 20,
              "situation": "Пассажир громко разговаривает, соседи оглядываются.",
              "variants": [
                {
                  "id": "a",
                  "text": "«Прошу Вас соблюдать спокойствие»",
                  "scaleDeltas": { "loyalty": 5, "safety": 5 },
                  "roleStages": ["acknowledge"],
                  "comment": "Не вступать в спор.",
                  "transitions": [{ "to": "ok" }]
                },
                {
                  "id": "b",
                  "text": "Не подходить",
                  "scaleDeltas": { "loyalty": 5, "safety": -15 },
                  "transitions": [{ "to": "fail" }]
                }
              ],
              "timeout": {
                "text": "Замер: пассажир начинает спорить с соседом",
                "scaleDeltas": { "loyalty": -10, "safety": -10 },
                "transitions": [{ "to": "fail" }]
              }
            },
            { "id": "ok", "outcome": "success", "situation": "Пассажир спокоен." },
            { "id": "fail", "outcome": "failure", "situation": "Конфликт в вагоне." }
          ]
        }
        """;

    private static JsonNode ValidEvent() => JsonNode.Parse(ValidEventJson)!;

    private static ValidationReport Validate(JsonNode ev, params string[] flagsSetElsewhere) =>
        EventValidator.ValidateJson(ev.ToJsonString(), ContentDirectory.Default, flagsSetElsewhere);

    private static void AssertSingleError(ValidationReport report, string where, string rule)
    {
        var error = Assert.Single(report.Errors);
        Assert.Equal(where, error.Where);
        Assert.Equal(rule, error.Rule);
        Assert.False(report.IsValid);
    }

    private static void AssertSingleWarning(ValidationReport report, string where)
    {
        Assert.Empty(report.Errors);
        var warning = Assert.Single(report.Warnings);
        Assert.Equal(where, warning.Where);
        Assert.True(report.IsValid);
    }

    private static List<(string Where, string Rule)> SortedOrdinal(IEnumerable<(string Where, string Rule)> issues) =>
        issues.OrderBy(i => i.Where, StringComparer.Ordinal).ThenBy(i => i.Rule, StringComparer.Ordinal).ToList();

    private static JsonNode Step(JsonNode ev, string id) =>
        ev["steps"]!.AsArray().First(s => (string)s!["id"]! == id)!;

    private static JsonNode Variant(JsonNode ev, string stepId, string variantId) =>
        Step(ev, stepId)["variants"]!.AsArray().First(v => (string)v!["id"]! == variantId)!;

    [Fact]
    public void Correct_event_passes_without_errors_and_warnings()
    {
        var report = Validate(ValidEvent());

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void Transition_to_missing_step_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""[{ "to": "s2_" }]""");

        AssertSingleError(Validate(ev), "Шаг s1 → Вариант b", "ADR-0001");
    }

    [Fact]
    public void Step_with_timer_without_timeout_branch_is_an_error()
    {
        var ev = ValidEvent();
        Step(ev, "s1").AsObject().Remove("timeout");

        AssertSingleError(Validate(ev), "Шаг s1", "ADR-0001");
    }

    [Fact]
    public void Step_unreachable_from_start_is_an_error()
    {
        var ev = ValidEvent();
        ev["steps"]!.AsArray().Add(JsonNode.Parse("""
            {
              "id": "s5", "answerType": "buttons", "situation": "Шаг, на который не ведёт ни один переход.",
              "variants": [{ "id": "a", "text": "Переспросить", "transitions": [{ "to": "ok" }] }]
            }
            """));

        AssertSingleError(Validate(ev), "Шаг s5", "ADR-0001");
    }

    [Fact]
    public void Step_that_cannot_reach_any_outcome_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""[{ "to": "loop" }]""");
        ev["steps"]!.AsArray().Add(JsonNode.Parse("""
            {
              "id": "loop", "answerType": "buttons", "situation": "Пассажир повторяет вопрос.",
              "variants": [{ "id": "a", "text": "Переспросить", "transitions": [{ "to": "loop" }] }]
            }
            """));

        AssertSingleError(Validate(ev), "Шаг loop", "ADR-0001");
    }

    [Fact]
    public void Variant_without_transition_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b").AsObject().Remove("transitions");

        AssertSingleError(Validate(ev), "Шаг s1 → Вариант b", "ADR-0001");
    }

    [Fact]
    public void Last_transition_with_conditions_is_an_error_because_of_possible_dead_end()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""
            [{ "to": "fail", "conditions": [{ "type": "scale", "scale": "loyalty", "op": ">=", "value": 40 }] }]
            """);

        AssertSingleError(Validate(ev), "Шаг s1 → Вариант b", "ADR-0001");
    }

    [Theory]
    [InlineData("""{ "type": "scale", "scale": "politeness", "op": "<", "value": 40 }""")]
    [InlineData("""{ "type": "scale", "scale": "loyalty", "op": "меньше", "value": 40 }""")]
    [InlineData("""{ "type": "scale", "scale": "loyalty", "op": "<" }""")]
    [InlineData("""{ "type": "flag", "flag": "did_not_report_drunk" }""")]
    [InlineData("""{ "type": "class", "classes": ["business", "premium"] }""")]
    [InlineData("""{ "type": "class", "classes": [] }""")]
    [InlineData("""{ "type": "random", "chance": 0.5 }""")]
    public void Malformed_condition_on_transition_is_an_error(string condition)
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse($$"""
            [{ "to": "ok", "conditions": [{{condition}}] }, { "to": "fail" }]
            """);

        AssertSingleError(Validate(ev, "did_not_report_drunk"), "Шаг s1 → Вариант b, переход 1", "ADR-0002");
    }

    [Fact]
    public void Class_condition_on_variant_uses_class_codes_from_directory()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["conditions"] = JsonNode.Parse("""[{ "type": "class", "classes": ["Бизнес"] }]""");

        AssertSingleError(Validate(ev), "Шаг s1 → Вариант b", "ADR-0002");
    }

    [Fact]
    public void Change_of_unknown_scale_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["scaleDeltas"] = JsonNode.Parse("""{ "loyalty": -25, "politeness": -10 }""");

        AssertSingleError(Validate(ev), "Шаг s1 → Вариант b", "ADR-0001");
    }

    [Fact]
    public void Outcome_does_not_change_scales()
    {
        var ev = ValidEvent();
        Step(ev, "fail")["scaleDeltas"] = JsonNode.Parse("""{ "loyalty": -10 }""");

        AssertSingleError(Validate(ev), "Шаг fail", "ADR-0001");
    }

    [Fact]
    public void Outcome_has_no_variants()
    {
        var ev = ValidEvent();
        Step(ev, "ok")["variants"] = JsonNode.Parse("""[{ "id": "a", "text": "Ещё", "transitions": [{ "to": "fail" }] }]""");

        AssertSingleError(Validate(ev), "Шаг ok", "ADR-0001");
    }

    [Fact]
    public void Outcome_is_success_or_failure()
    {
        var ev = ValidEvent();
        Step(ev, "ok")["outcome"] = "удачный";

        AssertSingleError(Validate(ev), "Шаг ok", "PRD §5.2");
    }

    [Fact]
    public void Step_without_variants_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""[{ "to": "wait" }]""");
        ev["steps"]!.AsArray().Add(JsonNode.Parse("""
            {
              "id": "wait", "answerType": "buttons", "timerSec": 10, "situation": "Пассажир ждёт ответа.",
              "timeout": { "text": "Замер", "transitions": [{ "to": "fail" }] }
            }
            """));

        AssertSingleError(Validate(ev), "Шаг wait", "ADR-0001");
    }

    [Fact]
    public void Duplicate_step_id_is_an_error()
    {
        var ev = ValidEvent();
        ev["steps"]!.AsArray().Add(JsonNode.Parse("""{ "id": "ok", "outcome": "success", "situation": "Дубль." }"""));

        AssertSingleError(Validate(ev), "Шаг ok", "ADR-0001");
    }

    [Fact]
    public void Duplicate_variant_id_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["id"] = "a";

        AssertSingleError(Validate(ev), "Шаг s1", "ADR-0001");
    }

    [Fact]
    public void Missing_start_step_is_an_error()
    {
        var ev = ValidEvent();
        ev["start"] = "s0";

        var report = Validate(ev);

        Assert.Contains(report.Errors, e => e.Where == "Событие" && e.Rule == "ADR-0001");
    }

    [Fact]
    public void Answer_type_is_buttons_or_voice()
    {
        var ev = ValidEvent();
        Step(ev, "s1")["answerType"] = "кнопки";

        AssertSingleError(Validate(ev), "Шаг s1", "PRD §5.2");
    }

    [Fact]
    public void Unknown_role_model_stage_is_an_error()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "a")["roleStages"] = JsonNode.Parse("""["признание"]""");

        AssertSingleError(Validate(ev), "Шаг s1 → Вариант a", "PRD §7.2");
    }

    private static JsonNode WithFlagCheckOnVariantB(JsonNode ev, string flag)
    {
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse($$"""
            [{ "to": "ok", "conditions": [{ "type": "flag", "flag": "{{flag}}", "present": true }] }, { "to": "fail" }]
            """);
        return ev;
    }

    [Fact]
    public void Flag_that_nobody_sets_is_a_warning()
    {
        var ev = WithFlagCheckOnVariantB(ValidEvent(), "did_not_report_drunkk");

        AssertSingleWarning(Validate(ev, "did_not_report_drunk"), "Шаг s1 → Вариант b, переход 1");
    }

    [Fact]
    public void Flag_set_by_another_event_is_fine()
    {
        var ev = WithFlagCheckOnVariantB(ValidEvent(), "did_not_report_drunk");

        var report = Validate(ev, "did_not_report_drunk");

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Flag_set_in_the_same_event_is_fine()
    {
        var ev = WithFlagCheckOnVariantB(ValidEvent(), "was_calm");
        Step(ev, "s1")["timeout"]!["setsFlags"] = JsonNode.Parse("""["was_calm"]""");

        var report = Validate(ev);

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void All_variants_with_conditions_is_a_warning()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "a")["conditions"] = JsonNode.Parse("""[{ "type": "class", "classes": ["business"] }]""");
        Variant(ev, "s1", "b")["conditions"] = JsonNode.Parse("""[{ "type": "class", "classes": ["first"] }]""");

        AssertSingleWarning(Validate(ev), "Шаг s1");
    }

    [Fact]
    public void Transitions_after_unconditional_one_are_a_warning()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""[{ "to": "fail" }, { "to": "ok" }]""");

        AssertSingleWarning(Validate(ev), "Шаг s1 → Вариант b");
    }

    [Fact]
    public void Timeout_branch_without_timer_is_a_warning()
    {
        var ev = ValidEvent();
        Step(ev, "s1").AsObject().Remove("timerSec");

        AssertSingleWarning(Validate(ev), "Шаг s1");
    }

    [Fact]
    public void Transitions_of_critical_error_are_a_warning()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["criticalError"] = true;

        AssertSingleWarning(Validate(ev), "Шаг s1 → Вариант b");
    }

    [Fact]
    public void Broken_draft_from_prototype_gives_eight_errors_and_one_warning()
    {
        var draft = JsonNode.Parse(TestData.BrokenDraftJson())!;

        var report = Validate(draft, "did_not_report_drunk");

        (string Where, string Rule)[] expected =
            [
                ("Шаг s1", "ADR-0001"),                          // таймер без ветки таймаута
                ("Шаг s1 → Вариант b", "ADR-0001"),              // Шкала politeness не из справочника
                ("Шаг s1 → Вариант b", "ADR-0001"),              // переход в несуществующий Шаг s2_
                ("Шаг s3 → Вариант b", "ADR-0002"),              // класс premium не из справочника
                ("Шаг s4 → Вариант a, переход 1", "ADR-0002"),   // оператор «меньше»
                ("Шаг s4 → Вариант b", "ADR-0001"),              // все переходы с Условиями — тупик
                ("Шаг s5", "ADR-0001"),                          // недостижим от старта
                ("Шаг s5", "ADR-0001"),                          // петля без выхода
            ];
        Assert.Equal(
            SortedOrdinal(expected),
            SortedOrdinal(report.Errors.Select(e => (e.Where, e.Rule))));
        var warning = Assert.Single(report.Warnings);
        Assert.Equal("Шаг s2 → Вариант a, переход 1", warning.Where);   // Флаг с опечаткой
    }

    [Fact]
    public void Unknown_field_is_an_error_instead_of_silent_loss()
    {
        var ev = ValidEvent();
        Step(ev, "s1")["timer_sec"] = 20;

        var error = Assert.Single(Validate(ev).Errors);
        Assert.Equal("Событие", error.Where);
        Assert.Contains("timer_sec", error.Message);
    }

    [Fact]
    public void Missing_required_value_is_an_error_not_a_crash()
    {
        var ev = ValidEvent();
        Step(ev, "s1")["id"] = null;

        var error = Assert.Single(Validate(ev).Errors);
        Assert.Contains("$.steps[0].id", error.Message);
    }

    [Fact]
    public void Unknown_required_role_stage_of_voice_step_is_an_error()
    {
        var ev = ValidEvent();
        Step(ev, "s1")["requiredRoleStages"] = JsonNode.Parse("""["признание"]""");

        AssertSingleError(Validate(ev), "Шаг s1", "PRD §7.2");
    }

    [Fact]
    public void Issue_points_to_step_variant_and_transition_for_highlighting_in_graph()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""
            [{ "to": "ok", "conditions": [{ "type": "class", "classes": ["premium"] }] }, { "to": "fail" }]
            """);
        Step(ev, "s1")["timeout"]!["scaleDeltas"] = JsonNode.Parse("""{ "politeness": -5 }""");

        var report = Validate(ev);

        Assert.Equal(
            [new IssueLocation("s1", "b", Timeout: false, Transition: 1), new IssueLocation("s1", Timeout: true)],
            report.Errors.Select(e => e.Location));
    }

    [Fact]
    public void Conditions_and_transitions_from_prd_example_are_read_without_loss()
    {
        // Условия — дословно пример из PRD v7 §5.2.
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""
            [
              {
                "to": "ok",
                "conditions": [
                  { "type": "scale", "scale": "safety", "op": "<", "value": 40 },
                  { "type": "flag",  "flag": "called_train_manager", "present": true },
                  { "type": "class", "classes": ["business", "first"] }
                ]
              },
              { "to": "fail" }
            ]
            """);

        var report = Validate(ev, "called_train_manager");

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Critical_error_needs_no_transition_and_ends_the_event()
    {
        var ev = ValidEvent();
        Variant(ev, "s1", "b")["transitions"] = JsonNode.Parse("""[{ "to": "escalated" }]""");
        ev["steps"]!.AsArray().Add(JsonNode.Parse("""
            {
              "id": "escalated", "answerType": "buttons", "situation": "Пассажир задевает соседей.",
              "variants": [{
                "id": "a", "text": "Вывести пассажира из вагона самому",
                "scaleDeltas": { "loyalty": -20, "safety": -30 }, "criticalError": true
              }]
            }
            """));

        var report = Validate(ev);

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }
}
