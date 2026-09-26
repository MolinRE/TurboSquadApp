namespace TurboSquadApp.Trips;

/// <summary>
/// Разбор, слой А (PRD v7 §8): факты Рейса по порядку, без ИИ. Строится из журнала и контента,
/// зафиксированного на старте Рейса, поэтому тексты — той версии События, которую реально играли.
/// </summary>
public sealed record Debrief(TripStatus Result, string Summary, IReadOnlyList<DebriefItem> Items)
{
    public IEnumerable<DebriefEvent> Events => Items.OfType<DebriefEvent>();

    public static Debrief Build(TripState state)
    {
        if (state.Status is not (TripStatus.Arrived or TripStatus.Failed))
            throw new InvalidOperationException("Разбор строится после Рейса: Рейс ещё не закончен");
        var content = state.Content!;

        var items = new List<DebriefItem>();
        var decisions = new List<DebriefDecision>();
        foreach (var entry in state.Journal)
        {
            switch (entry)
            {
                case ProactiveChosen chosen:
                    var proactive = content.Settings.ProactiveChoice;
                    items.Add(new DebriefProactiveChoice(proactive.Situation, proactive.Options.Single(o => o.Id == chosen.OptionId).Text));
                    break;
                case Decision decision:
                    decisions.Add(Row(content, decision));
                    break;
                case EventFinished finished:
                    var ev = content.Event(finished.EventId);
                    var outcome = ev.Steps.SingleOrDefault(s => s.Id == finished.OutcomeStepId);
                    items.Add(new DebriefEvent(ev.Id, ev.Version, ev.Title, finished.Result, outcome?.Id, outcome?.Situation, decisions));
                    decisions = [];
                    break;
            }
        }
        return new Debrief(state.Status, SummaryOf(state), items);
    }

    /// <summary>Итог Рейса одной фразой.</summary>
    private static string SummaryOf(TripState state)
    {
        if (state.Failure is not { } failure) return "Прибытие: все запланированные События пройдены";
        if (failure.Cause == FailureCause.CriticalError)
        {
            var decision = state.Journal.OfType<Decision>().Last(d => d.CriticalError);
            return $"Срыв рейса: Критическая ошибка — «{Row(state.Content!, decision).Text}»";
        }
        var scale = state.Content!.Directory.Scales.Single(s => s.Code == failure.Scale);
        var reason = scale.FailureReason is null ? "" : $" — {scale.FailureReason}";
        return $"Срыв рейса: Шкала «{scale.Name}» упала до {state.Scales[scale.Code]}{reason}";
    }

    private static DebriefDecision Row(TripContent content, Decision decision)
    {
        var ev = content.Event(decision.EventId);
        var step = ev.Steps.Single(s => s.Id == decision.StepId);
        var reaction = decision.TimedOut ? step.Timeout! : step.Variants!.Single(v => v.Id == decision.VariantId);
        var changes = decision.Changes
            .Select(c => new DebriefScaleChange(
                c.Scale, content.Directory.Scales.Single(s => s.Code == c.Scale).Name, c.Nominal, c.Applied, c.Before, c.After))
            .ToList();
        return new DebriefDecision(
            step.Id, step.Situation, decision.VariantId, decision.TimedOut, reaction.Text, changes, decision.FlagsSet,
            reaction.RoleStages ?? [], reaction.Comment, reaction.Source ?? ev.Source, decision.CriticalError);
    }
}

public abstract record DebriefItem;

public sealed record DebriefProactiveChoice(string Situation, string Choice) : DebriefItem;

/// <summary>Событие Рейса: Исход или «прервано», если в нём случился Срыв рейса.</summary>
public sealed record DebriefEvent(
    string EventId, int Version, string Title, EventResult Result, string? OutcomeStepId, string? OutcomeSituation,
    IReadOnlyList<DebriefDecision> Decisions) : DebriefItem;

/// <summary>Решение проводника или таймаут: что изменилось и почему так, как лучше.</summary>
public sealed record DebriefDecision(
    string StepId, string Situation, string? VariantId, bool TimedOut, string Text,
    IReadOnlyList<DebriefScaleChange> Changes, IReadOnlyList<string> FlagsSet, IReadOnlyList<string> RoleStages,
    string? Comment, string? Source, bool CriticalError);

/// <summary>Изменение Шкалы: по контенту (Nominal) и фактически после обрезки по диапазону (Applied).</summary>
public sealed record DebriefScaleChange(string Scale, string Name, int Nominal, int Applied, int Before, int After);
