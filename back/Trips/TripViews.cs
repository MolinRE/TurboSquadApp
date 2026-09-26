namespace TurboSquadApp.Trips;

/// <summary>Состояние Рейса для клиента: Шкалы, Флаги, текущий Шаг или Проактивный выбор, итог.</summary>
public sealed record TripView(
    Guid Id, TripStatus Status, string ServiceClass, IReadOnlyList<ScaleView> Scales, IReadOnlyList<string> Flags,
    StepView? Step, ProactiveChoiceView? ProactiveChoice, TripEndView? Result)
{
    /// <param name="expiresAt">Когда истекает таймер текущего Шага — его считает сервер.</param>
    public static TripView Of(Guid id, TripState state, DateTimeOffset? expiresAt)
    {
        var content = state.Content!;
        var scales = content.Directory.Scales
            .Select(s => new ScaleView(s.Code, s.Name, state.Scales[s.Code], s.Min, s.Max))
            .ToList();

        StepView? step = null;
        if (state.CurrentStep is { } current)
        {
            var ev = state.CurrentEvent!;
            step = new StepView(
                ev.Id, ev.Title, current.Id, current.Situation, current.AnswerType ?? "", current.TimerSec, expiresAt,
                state.Choices.Where(c => c.Available).Select(c => new VariantView(c.Variant.Id, c.Variant.Text)).ToList());
        }

        var proactive = state.Phase == TripPhase.ProactiveChoice
            ? new ProactiveChoiceView(
                content.Settings.ProactiveChoice.Situation,
                content.Settings.ProactiveChoice.Options.Select(o => new ProactiveOptionView(o.Id, o.Text)).ToList())
            : null;

        var result = state.IsFinished
            ? new TripEndView(state.Status, Debrief.SummaryOf(state))
            : null;

        return new TripView(id, state.Status, state.ServiceClass!, scales, state.Flags.Order().ToList(), step, proactive, result);
    }
}

public sealed record ScaleView(string Code, string Name, int Value, int Min, int Max);

/// <summary>Шаг События. Variants — только доступные проводнику: скрытые Условиями не отдаются.</summary>
public sealed record StepView(
    string EventId, string EventTitle, string StepId, string Situation, string AnswerType, int? TimerSec,
    DateTimeOffset? ExpiresAt, IReadOnlyList<VariantView> Variants);

public sealed record VariantView(string Id, string Text);

public sealed record ProactiveChoiceView(string Situation, IReadOnlyList<ProactiveOptionView> Options);

public sealed record ProactiveOptionView(string Id, string Text);

/// <summary>Итог Рейса: Прибытие или Срыв и итог одной фразой.</summary>
public sealed record TripEndView(TripStatus Status, string Summary);
