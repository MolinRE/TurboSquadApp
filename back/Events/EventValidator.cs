using System.Text.Json.Serialization;

namespace TurboSquadApp.Events;

[JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
public enum Severity { Error, Warning }

/// <summary>Замечание валидатора: где, что не так и из какого правила проверка.</summary>
public sealed record ValidationIssue(Severity Severity, string Where, string Message, string Rule);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Errors, IReadOnlyList<ValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Проверка документа События при сохранении (ADR-0001, ADR-0002).</summary>
public static class EventValidator
{
    /// <summary>Операторы Условия по Шкале.</summary>
    public static readonly IReadOnlyList<string> Operators = ["<", "<=", ">", ">=", "="];

    /// <summary>Этапы Ролевой модели общения: Признание, Правило, Решение, Заверение.</summary>
    public static readonly IReadOnlyList<string> RoleStages = ["acknowledge", "rule", "solution", "reassure"];

    private static IEnumerable<string> Duplicates(IEnumerable<string> ids) =>
        ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key);

    /// <param name="flagsSetElsewhere">
    /// Флаги, которые ставят другие опубликованные События Рейса: без них Флаг из соседнего События не отличить от опечатки.
    /// </param>
    public static ValidationReport Validate(
        EventDocument ev, ContentDirectory directory, IReadOnlyCollection<string> flagsSetElsewhere)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();
        void Error(string where, string message, string rule) =>
            errors.Add(new ValidationIssue(Severity.Error, where, message, rule));
        void Warn(string where, string message, string rule) =>
            warnings.Add(new ValidationIssue(Severity.Warning, where, message, rule));
        var checkedFlags = new List<(string Flag, string Where)>();

        var scaleCodes = directory.Scales.Select(s => s.Code).ToHashSet();
        var classCodes = directory.Classes.Select(c => c.Code).ToHashSet();

        void CheckConditions(IReadOnlyList<Condition>? conditions, string where)
        {
            foreach (var c in conditions ?? [])
            {
                switch (c.Type)
                {
                    case "scale":
                        if (c.Scale is null || !scaleCodes.Contains(c.Scale))
                            Error(where, $"Условие: Шкалы «{c.Scale}» нет в справочнике", "ADR-0002");
                        if (c.Op is null || !Operators.Contains(c.Op))
                            Error(where, $"Условие: оператор «{c.Op}» не поддерживается, есть только {string.Join(" ", Operators)}", "ADR-0002");
                        if (c.Value is null)
                            Error(where, "Условие: у Шкалы не задано «value»", "ADR-0002");
                        break;
                    case "flag":
                        if (string.IsNullOrEmpty(c.Flag))
                            Error(where, "Условие: не указан Флаг", "ADR-0002");
                        if (c.Present is null)
                            Error(where, "Условие: у Флага поле «present» должно быть true или false", "ADR-0002");
                        if (!string.IsNullOrEmpty(c.Flag))
                            checkedFlags.Add((c.Flag, where));
                        break;
                    case "class":
                        if (c.Classes is not { Count: > 0 })
                            Error(where, "Условие: «classes» должен быть непустым списком кодов", "ADR-0002");
                        foreach (var code in c.Classes ?? [])
                            if (!classCodes.Contains(code))
                                Error(where, $"Условие: Класса обслуживания с кодом «{code}» нет в справочнике", "ADR-0002");
                        break;
                    default:
                        Error(where, $"Неизвестный тип Условия «{c.Type}»: допустимы scale, flag, class", "ADR-0002");
                        break;
                }
            }
        }

        var stepIds = ev.Steps.Select(s => s.Id).ToHashSet();
        foreach (var id in Duplicates(ev.Steps.Select(s => s.Id)))
            Error($"Шаг {id}", "Два Шага с одинаковым id", "ADR-0001");
        if (!stepIds.Contains(ev.Start))
            Error("Событие", $"Стартового Шага «{ev.Start}» нет", "ADR-0001");
        CheckConditions(ev.Conditions, "Условия События");
        var edges = new Dictionary<string, List<string>>();   // Шаг → куда из него можно перейти (без учёта Условий)
        var exits = ev.Steps.Where(s => s.IsOutcome).Select(s => s.Id).ToList();   // где Событие может кончиться

        foreach (var step in ev.Steps)
        {
            var targets = new List<string>();
            edges[step.Id] = targets;
            var at = $"Шаг {step.Id}";

            if (step.IsOutcome)
            {
                if (step.Outcome is not ("success" or "failure"))
                    Error(at, $"Исход должен быть «success» или «failure», а не «{step.Outcome}»", "PRD §5.2");
                if (step.Variants is { Count: > 0 } || step.Timeout is not null)
                    Error(at, "У Исхода не может быть Вариантов и таймаута: это конечный Шаг", "ADR-0001");
                if (step.ScaleDeltas is { Count: > 0 })
                    Error(at, "Исход не меняет Шкалы: их меняют Варианты, которые к нему ведут", "ADR-0001");
                continue;
            }

            if (step.AnswerType is not ("buttons" or "voice"))
                Error(at, $"Тип ответа должен быть «buttons» или «voice», а не «{step.AnswerType}»", "PRD §5.2");
            if (step.Variants is not { Count: > 0 })
                Error(at, "У Шага нет Вариантов: проводнику нечего выбрать", "ADR-0001");
            foreach (var id in Duplicates((step.Variants ?? []).Select(v => v.Id)))
                Error(at, $"Два Варианта с id «{id}»", "ADR-0001");
            if (step.TimerSec is not null && step.Timeout is null)
                Error(at, "Шаг с таймером без ветки таймаута", "ADR-0001");
            if (step.TimerSec is null && step.Timeout is not null)
                Warn(at, "Ветка таймаута есть, а таймера нет: она никогда не сработает", "ADR-0001");
            if (step.Variants is { Count: > 0 } && step.Variants.All(v => v.Conditions is { Count: > 0 }))
                Warn(at, "У всех Вариантов есть Условия: при каком-то состоянии Рейса проводнику будет нечего выбрать", "ADR-0001");

            foreach (var (reaction, where) in ReactionsOf(step))
            {
                CheckConditions(reaction.Conditions, where);
                foreach (var code in (reaction.ScaleDeltas ?? new Dictionary<string, int>()).Keys)
                    if (!scaleCodes.Contains(code))
                        Error(where, $"Шкалы «{code}» нет в справочнике", "ADR-0001");
                foreach (var stage in reaction.RoleStages ?? [])
                    if (!RoleStages.Contains(stage))
                        Error(where, $"Неизвестный этап Ролевой модели «{stage}»: допустимы {string.Join(", ", RoleStages)}", "PRD §7.2");

                if (reaction.CriticalError)
                {
                    // Критическая ошибка сразу вызывает Срыв рейса: здесь Событие кончается.
                    exits.Add(step.Id);
                    if (reaction.Transitions is { Count: > 0 })
                        Warn(where, "Критическая ошибка сразу завершает Рейс: переходы никогда не сработают", "ADR-0001");
                    continue;
                }

                var transitions = reaction.Transitions ?? [];
                if (transitions.Count == 0)
                    Error(where, "Нет перехода", "ADR-0001");
                else if (transitions.All(t => t.Conditions is { Count: > 0 }))
                    Error(where,
                        "У всех переходов есть Условия: если ни одно не выполнится, будет тупик. Последний переход должен быть без Условий",
                        "ADR-0001");
                else if (transitions.TakeWhile(t => t.Conditions is { Count: > 0 }).Count() < transitions.Count - 1)
                    Warn(where, "Переходы после перехода без Условий никогда не сработают", "ADR-0001");

                for (var i = 0; i < transitions.Count; i++)
                {
                    var transition = transitions[i];
                    var tw = transitions.Count > 1 ? $"{where}, переход {i + 1}" : where;
                    if (!stepIds.Contains(transition.To))
                        Error(tw, $"Переход в несуществующий Шаг «{transition.To}»", "ADR-0001");
                    else
                        targets.Add(transition.To);
                    CheckConditions(transition.Conditions, tw);
                }
            }
        }

        // Все Шаги достижимы от старта. Проверка структурная: Условия переходов не учитываются.
        var reachable = Walk([ev.Start], id => edges.GetValueOrDefault(id) ?? []);
        foreach (var step in ev.Steps.Where(s => !reachable.Contains(s.Id)))
            Error($"Шаг {step.Id}", "Шаг недостижим от старта", "ADR-0001");

        // Из каждого Шага можно дойти до места, где Событие кончается: нет петель без выхода.
        var reverse = stepIds.ToDictionary(id => id, _ => new List<string>());
        foreach (var (from, targets) in edges)
            foreach (var to in targets)
                reverse[to].Add(from);
        var leadsOut = Walk(exits, id => reverse.GetValueOrDefault(id) ?? []);
        foreach (var step in ev.Steps.Where(s => !s.IsOutcome && !leadsOut.Contains(s.Id)))
            Error($"Шаг {step.Id}", "Из Шага не достижим ни один Исход: петля без выхода", "ADR-0001");

        // Флаг проверяется, но его никто не ставит — почти всегда опечатка, ветка мертва.
        var knownFlags = ev.Steps
            .SelectMany(s => ReactionsOf(s))
            .SelectMany(r => r.Reaction.SetsFlags ?? [])
            .Concat(flagsSetElsewhere)
            .ToHashSet();
        foreach (var (flag, where) in checkedFlags.Where(f => !knownFlags.Contains(f.Flag)))
            Warn(where, $"Флаг «{flag}» проверяется, но его не ставит ни один Вариант опубликованных Событий: ветка мертва (опечатка?)", "ADR-0001");

        return new ValidationReport(errors, warnings);
    }

    private static HashSet<string> Walk(IEnumerable<string> seeds, Func<string, IEnumerable<string>> next)
    {
        var seen = new HashSet<string>();
        var queue = new Queue<string>(seeds);
        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            foreach (var n in next(id)) queue.Enqueue(n);
        }
        return seen;
    }

    /// <summary>Варианты Шага и его ветка таймаута — она устроена как Вариант.</summary>
    private static IEnumerable<(Variant Reaction, string Where)> ReactionsOf(Step step)
    {
        foreach (var variant in step.Variants ?? [])
            yield return (variant, $"Шаг {step.Id} → Вариант {variant.Id}");
        if (step.Timeout is not null)
            yield return (step.Timeout, $"Шаг {step.Id} → таймаут");
    }
}
