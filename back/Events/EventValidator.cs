using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboSquadApp.Events;

[JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
public enum Severity { Error, Warning }

/// <summary>
/// Место замечания в графе События: по нему CMS подсвечивает узел. Без StepId — Событие целиком.
/// Transition — номер перехода с 1, указывается, когда у Варианта больше одного перехода.
/// </summary>
public sealed record IssueLocation(string? StepId = null, string? VariantId = null, bool Timeout = false, int? Transition = null)
{
    public static readonly IssueLocation Event = new();

    public static IssueLocation Of(Step step) => new(step.Id);

    public static IssueLocation Of(Step step, Variant reaction) =>
        ReferenceEquals(reaction, step.Timeout) ? new(step.Id, Timeout: true) : new(step.Id, reaction.Id);

    public IssueLocation WithTransition(int number) => this with { Transition = number };

    /// <summary>Текст для человека: «Шаг s4 → Вариант b, переход 1».</summary>
    public override string ToString()
    {
        if (StepId is null) return "Событие";
        var text = $"Шаг {StepId}";
        if (Timeout) text += " → таймаут";
        else if (VariantId is not null) text += $" → Вариант {VariantId}";
        if (Transition is not null) text += $", переход {Transition}";
        return text;
    }
}

/// <summary>Замечание валидатора: где, что не так и из какого правила проверка.</summary>
public sealed record ValidationIssue(Severity Severity, IssueLocation Location, string Message, string Rule)
{
    public string Where => Location.ToString();
}

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Errors, IReadOnlyList<ValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Проверка документа События при сохранении (ADR-0001, ADR-0002).</summary>
public static class EventValidator
{
    /// <summary>Операторы Условия по Шкале.</summary>
    public static readonly IReadOnlyList<string> Operators = ["<", "<=", ">", ">=", "="];

    /// <summary>Этапы Ролевой модели общения: Признание, Правило, Решение, Заверение (PRD §5.2).</summary>
    public static readonly IReadOnlyList<string> RoleStages = ["acknowledge", "rule", "solution", "reassure"];

    /// <summary>
    /// Проверка JSON как его прислали. Документ, который не читается (незнакомое поле, null в обязательном поле,
    /// не тот тип значения), даёт одну ошибку с путём в JSON вместо молчаливой потери данных.
    /// </summary>
    public static ValidationReport ValidateJson(
        string json, ContentDirectory directory, IReadOnlyCollection<string> flagsSetElsewhere)
    {
        EventDocument? ev;
        try
        {
            ev = JsonSerializer.Deserialize<EventDocument>(json, EventJson.Options);
        }
        catch (JsonException ex)
        {
            return Unreadable(ex.Message);
        }
        return ev is null ? Unreadable("документ пуст") : Validate(ev, directory, flagsSetElsewhere);

        static ValidationReport Unreadable(string reason) =>
            new([new ValidationIssue(Severity.Error, IssueLocation.Event, $"Документ не читается: {reason}", "PRD §5.2")], []);
    }

    /// <param name="flagsSetElsewhere">
    /// Флаги, которые ставят другие опубликованные События Рейса: без них Флаг из соседнего События не отличить от опечатки.
    /// </param>
    public static ValidationReport Validate(
        EventDocument ev, ContentDirectory directory, IReadOnlyCollection<string> flagsSetElsewhere)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();
        void Error(IssueLocation location, string message, string rule) =>
            errors.Add(new ValidationIssue(Severity.Error, location, message, rule));
        void Warn(IssueLocation location, string message, string rule) =>
            warnings.Add(new ValidationIssue(Severity.Warning, location, message, rule));

        var scaleCodes = directory.Scales.Select(s => s.Code).ToHashSet();
        var classCodes = directory.Classes.Select(c => c.Code).ToHashSet();
        var checkedFlags = new List<(string Flag, IssueLocation Location)>();

        void CheckConditions(IReadOnlyList<Condition>? conditions, IssueLocation location)
        {
            foreach (var condition in conditions ?? [])
            {
                switch (condition.Type)
                {
                    case "scale":
                        if (condition.Scale is null || !scaleCodes.Contains(condition.Scale))
                            Error(location, $"Условие: Шкалы «{condition.Scale}» нет в справочнике", "ADR-0002");
                        if (condition.Op is null || !Operators.Contains(condition.Op))
                            Error(location, $"Условие: оператор «{condition.Op}» не поддерживается, есть только {string.Join(" ", Operators)}", "ADR-0002");
                        if (condition.Value is null)
                            Error(location, "Условие: у Шкалы не задано «value»", "ADR-0002");
                        break;
                    case "flag":
                        if (string.IsNullOrEmpty(condition.Flag))
                            Error(location, "Условие: не указан Флаг", "ADR-0002");
                        else
                            checkedFlags.Add((condition.Flag, location));
                        if (condition.Present is null)
                            Error(location, "Условие: у Флага поле «present» должно быть true или false", "ADR-0002");
                        break;
                    case "class":
                        if (condition.Classes is not { Count: > 0 })
                            Error(location, "Условие: «classes» должен быть непустым списком кодов", "ADR-0002");
                        foreach (var code in (condition.Classes ?? []).Where(code => !classCodes.Contains(code)))
                            Error(location, $"Условие: Класса обслуживания с кодом «{code}» нет в справочнике", "ADR-0002");
                        break;
                    default:
                        Error(location, $"Неизвестный тип Условия «{condition.Type}»: допустимы scale, flag, class", "ADR-0002");
                        break;
                }
            }
        }

        void CheckRoleStages(IReadOnlyList<string>? stages, IssueLocation location)
        {
            foreach (var stage in (stages ?? []).Where(stage => !RoleStages.Contains(stage)))
                Error(location, $"Неизвестный этап Ролевой модели «{stage}»: допустимы {string.Join(", ", RoleStages)}", "PRD §7.2");
        }

        var stepIds = ev.Steps.Select(s => s.Id).ToHashSet();
        foreach (var id in Duplicates(ev.Steps.Select(s => s.Id)))
            Error(new IssueLocation(id), "Два Шага с одинаковым id", "ADR-0001");
        if (!stepIds.Contains(ev.Start))
            Error(IssueLocation.Event, $"Стартового Шага «{ev.Start}» нет", "ADR-0001");
        CheckConditions(ev.Conditions, IssueLocation.Event);

        var edges = new Dictionary<string, List<string>>();   // Шаг → куда из него можно перейти (без учёта Условий)
        var exits = ev.Steps.Where(s => s.IsOutcome).Select(s => s.Id).ToList();   // где Событие может кончиться

        foreach (var step in ev.Steps)
        {
            var targets = new List<string>();
            edges[step.Id] = targets;
            var atStep = IssueLocation.Of(step);

            if (step.IsOutcome)
            {
                if (step.Outcome is not ("success" or "failure"))
                    Error(atStep, $"Исход должен быть «success» или «failure», а не «{step.Outcome}»", "PRD §5.2");
                if (step.Variants is { Count: > 0 } || step.Timeout is not null)
                    Error(atStep, "У Исхода не может быть Вариантов и таймаута: это конечный Шаг", "ADR-0001");
                if (step.ScaleDeltas is { Count: > 0 })
                    Error(atStep, "Исход не меняет Шкалы: их меняют Варианты, которые к нему ведут", "ADR-0001");
                continue;
            }

            if (step.AnswerType is not ("buttons" or "voice"))
                Error(atStep, $"Тип ответа должен быть «buttons» или «voice», а не «{step.AnswerType}»", "PRD §5.2");
            CheckRoleStages(step.RequiredRoleStages, atStep);
            if (step.Variants is not { Count: > 0 })
                Error(atStep, "У Шага нет Вариантов: проводнику нечего выбрать", "ADR-0001");
            foreach (var id in Duplicates((step.Variants ?? []).Select(v => v.Id)))
                Error(atStep, $"Два Варианта с id «{id}»", "ADR-0001");
            if (step.TimerSec is not null && step.Timeout is null)
                Error(atStep, "Шаг с таймером без ветки таймаута", "ADR-0001");
            if (step.TimerSec is null && step.Timeout is not null)
                Warn(atStep, "Ветка таймаута есть, а таймера нет: она никогда не сработает", "ADR-0001");
            if (step.AnswerType == "voice")
                foreach (var variant in (step.Variants ?? []).Where(v => string.IsNullOrWhiteSpace(v.LayaCriterion)))
                    Warn(IssueLocation.Of(step, variant),
                        "Нет описания для Laya (layaCriterion): она сравнит ответ с текстом Варианта и чаще будет ошибаться", "ADR-0003");
            if (step.Variants is { Count: > 0 } && step.Variants.All(v => v.Conditions is { Count: > 0 }))
                Warn(atStep, "У всех Вариантов есть Условия: при каком-то состоянии Рейса проводнику будет нечего выбрать", "ADR-0001");

            var knowledgeCosts = step.Reactions.Select(reaction => reaction.Competencies?.GetValueOrDefault("knowledge") ?? 0)
                .Where(cost => cost > 0).Distinct().ToList();
            if (knowledgeCosts.Count > 1)
                Error(atStep, "Верные Варианты одного Шага должны иметь одинаковую стоимость по Знанию", "PRD §7.3");

            foreach (var reaction in step.Reactions)
            {
                var atReaction = IssueLocation.Of(step, reaction);
                if (reaction.Competencies?.GetValueOrDefault("knowledge") < 0)
                    Error(atReaction, "Стоимость по Знанию не может быть отрицательной", "PRD §7.3");
                CheckConditions(reaction.Conditions, atReaction);
                foreach (var code in (reaction.ScaleDeltas?.Keys ?? []).Where(code => !scaleCodes.Contains(code)))
                    Error(atReaction, $"Шкалы «{code}» нет в справочнике", "ADR-0001");
                CheckRoleStages(reaction.RoleStages, atReaction);

                if (reaction.CriticalError)
                {
                    // Критическая ошибка сразу вызывает Срыв рейса: Событие кончается здесь, без Исхода (ADR-0001).
                    exits.Add(step.Id);
                    if (reaction.Transitions is { Count: > 0 })
                        Warn(atReaction, "Критическая ошибка сразу завершает Рейс: переходы никогда не сработают", "ADR-0001");
                    continue;
                }

                var transitions = reaction.Transitions ?? [];
                var firstUnconditional = transitions.TakeWhile(t => t.Conditions is { Count: > 0 }).Count();
                if (transitions.Count == 0)
                    Error(atReaction, "Нет перехода", "ADR-0001");
                else if (firstUnconditional == transitions.Count)
                    Error(atReaction,
                        "У всех переходов есть Условия: если ни одно не выполнится, будет тупик. Последний переход должен быть без Условий",
                        "ADR-0001");
                else if (firstUnconditional < transitions.Count - 1)
                    Warn(atReaction, "Переходы после перехода без Условий никогда не сработают", "ADR-0001");

                for (var i = 0; i < transitions.Count; i++)
                {
                    var transition = transitions[i];
                    var atTransition = transitions.Count > 1 ? atReaction.WithTransition(i + 1) : atReaction;
                    if (stepIds.Contains(transition.To))
                        targets.Add(transition.To);
                    else
                        Error(atTransition, $"Переход в несуществующий Шаг «{transition.To}»", "ADR-0001");
                    CheckConditions(transition.Conditions, atTransition);
                }
            }
        }

        // Все Шаги достижимы от старта. Проверка структурная: Условия переходов не учитываются.
        var reachable = Walk([ev.Start], id => edges.GetValueOrDefault(id) ?? []);
        foreach (var step in ev.Steps.Where(s => !reachable.Contains(s.Id)))
            Error(IssueLocation.Of(step), "Шаг недостижим от старта", "ADR-0001");

        // Из каждого Шага можно дойти до места, где Событие кончается: нет петель без выхода.
        var reverse = stepIds.ToDictionary(id => id, _ => new List<string>());
        foreach (var (from, targets) in edges)
            foreach (var to in targets)
                reverse[to].Add(from);
        var leadsOut = Walk(exits, id => reverse.GetValueOrDefault(id) ?? []);
        foreach (var step in ev.Steps.Where(s => !s.IsOutcome && !leadsOut.Contains(s.Id)))
            Error(IssueLocation.Of(step), "Из Шага не достижим ни один Исход: петля без выхода", "ADR-0001");

        // Флаг проверяется, но его никто не ставит — почти всегда опечатка, ветка мертва.
        var knownFlags = ev.FlagsSet.Concat(flagsSetElsewhere).ToHashSet();
        foreach (var (flag, location) in checkedFlags.Where(f => !knownFlags.Contains(f.Flag)))
            Warn(location, $"Флаг «{flag}» проверяется, но его не ставит ни один Вариант опубликованных Событий: ветка мертва (опечатка?)", "ADR-0001");

        return new ValidationReport(errors, warnings);
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> ids) =>
        ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key);

    private static HashSet<string> Walk(IEnumerable<string> seeds, Func<string, IEnumerable<string>> next)
    {
        var seen = new HashSet<string>();
        var queue = new Queue<string>(seeds);
        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            foreach (var neighbour in next(id)) queue.Enqueue(neighbour);
        }
        return seen;
    }
}
