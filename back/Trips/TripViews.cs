using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboSquadApp.Trips;

/// <summary>Состояние Рейса для клиента: Шкалы, Флаги, текущий Шаг или Проактивный выбор, итог.</summary>
public sealed record TripView(
    Guid Id, TripStatus Status, string ServiceClass, IReadOnlyList<ScaleView> Scales, IReadOnlyList<string> Flags,
    StepView? Step, ProactiveChoiceView? ProactiveChoice, TripEndView? Result)
{
    /// <param name="stepStartedAt">Когда Шаг показан проводнику: от этого момента сервер считает таймер.</param>
    public static TripView Of(Guid id, TripState state, DateTimeOffset stepStartedAt)
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
                ev.Id, ev.Title, current.Id, current.Situation, current.AnswerType ?? "", current.TimerSec,
                ExpiresAt(state, stepStartedAt),
                state.Choices.Where(c => c.Available).Select(c => new VariantView(c.Variant.Id, c.Variant.Text)).ToList());
        }

        var proactive = state.Phase == TripPhase.ProactiveChoice
            ? new ProactiveChoiceView(
                content.Settings.ProactiveChoice.Situation,
                content.Settings.ProactiveChoice.Options.Select(o => new ProactiveOptionView(o.Id, o.Text)).ToList())
            : null;

        var result = state.Status is TripStatus.Arrived or TripStatus.Failed
            ? new TripEndView(state.Status, Debrief.SummaryOf(state))
            : null;

        return new TripView(id, state.Status, state.ServiceClass!, scales, state.Flags.Order().ToList(), step, proactive, result);
    }

    /// <summary>Когда истекает таймер текущего Шага; у Шага без таймера — null.</summary>
    public static DateTimeOffset? ExpiresAt(TripState state, DateTimeOffset stepStartedAt) =>
        state.CurrentStep?.TimerSec is { } seconds ? stepStartedAt.AddSeconds(seconds) : null;
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

/// <summary>Перечисления движка в JSON — строками в camelCase: running, arrived, interrupted.</summary>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;
