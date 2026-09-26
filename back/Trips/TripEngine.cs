using System.Collections.Immutable;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

public enum RejectionReason
{
    /// <summary>Рейс не начат или уже закончился Прибытием или Срывом.</summary>
    TripNotRunning,

    /// <summary>Рейс уже начат: новый Рейс начинается с чистого состояния, журнал идущего не стирается.</summary>
    TripAlreadyStarted,

    /// <summary>Контент не проходит валидатор или не сходится с настройкой Рейса: движок не исполняет непроверенный граф.</summary>
    InvalidContent,

    /// <summary>Класса обслуживания нет в справочнике.</summary>
    UnknownServiceClass,

    /// <summary>Проактивный выбор посреди События: он делается только между Событиями.</summary>
    NotProactiveChoice,

    /// <summary>У Проактивного выбора нет такого варианта.</summary>
    UnknownProactiveOption,

    /// <summary>«Время вышло» там, где таймера нет: на Шаге без таймера или вне Шага.</summary>
    NoTimer,

    /// <summary>На текущем Шаге нет такого Варианта.</summary>
    UnknownVariant,

    /// <summary>Вариант скрыт от проводника Условием: сервер перепроверяет выбор.</summary>
    HiddenVariant,
}

/// <summary>Почему действие отклонено: код для программы, текст для человека.</summary>
public sealed record Rejection(RejectionReason Reason, string Message);

/// <summary>Результат действия. При отказе State — то же состояние, что пришло на вход.</summary>
public sealed record TripResult(TripState State, Rejection? Rejection);

/// <summary>
/// Движок Рейса (PRD v7 §5.1–5.5): редьюсер (состояние, действие) → состояние. Чистая логика без БД, HTTP и часов.
/// Эталон — модуль engine прототипа на ветке prototype/trip-engine.
/// </summary>
public static class TripEngine
{
    public static TripResult Reduce(TripState state, TripAction action)
    {
        if (action is StartTrip start)
            return Start(state, start.Content, start.ServiceClass);
        if (state.Status != TripStatus.Running)
            return Reject(state, RejectionReason.TripNotRunning, "Рейс не идёт: он не начат или уже закончился");
        return action switch
        {
            ChooseProactive choose => Proactive(state, choose.OptionId),
            ChooseVariant choose => Choose(state, choose.VariantId),
            TimeOut => state.CurrentStep is { TimerSec: not null } step
                ? Accept(React(state, step.Timeout!, timedOut: true))
                : Reject(state, RejectionReason.NoTimer, "Здесь нет таймера: «время вышло» невозможно"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Неизвестное действие"),
        };
    }

    private static TripResult Choose(TripState state, string variantId)
    {
        var choice = state.Choices.SingleOrDefault(c => c.Variant.Id == variantId);
        if (choice is null)
            return Reject(state, RejectionReason.UnknownVariant, $"Здесь нет Варианта «{variantId}»");
        if (!choice.Available)
            return Reject(state, RejectionReason.HiddenVariant,
                $"Вариант «{variantId}» скрыт от проводника, не выполнено Условие: {string.Join("; ", choice.HiddenBecause)}");
        return Accept(React(state, choice.Variant, timedOut: false));
    }

    private static TripResult Accept(TripState state) => new(state, null);

    private static TripResult Reject(TripState state, RejectionReason reason, string message) =>
        new(state, new Rejection(reason, message));

    private static TripResult Start(TripState state, TripContent content, string serviceClass)
    {
        if (state.Status != TripStatus.NotStarted)
            return Reject(state, RejectionReason.TripAlreadyStarted, "Рейс уже начат: новый Рейс начинается с чистого состояния");
        var errors = content.Errors();
        if (errors.Count > 0)
            return Reject(state, RejectionReason.InvalidContent,
                $"Рейс не начат: в контенте ошибок — {errors.Count}. {string.Join("; ", errors)}");
        if (content.Directory.Classes.All(c => c.Code != serviceClass))
            return Reject(state, RejectionReason.UnknownServiceClass, $"Класса обслуживания «{serviceClass}» нет в справочнике");

        var started = TripState.Initial with
        {
            Status = TripStatus.Running,
            Content = content,
            ServiceClass = serviceClass,
            Scales = content.Directory.Scales.ToImmutableDictionary(s => s.Code, s => s.Start),
        };
        return Accept(EnterEvent(started, content.Settings.ShiftStartEvent));
    }

    private static TripResult Proactive(TripState state, string optionId)
    {
        if (state.Phase != TripPhase.ProactiveChoice)
            return Reject(state, RejectionReason.NotProactiveChoice, "Сейчас идёт Шаг События, а не Проактивный выбор");
        var option = state.Content!.Settings.ProactiveChoice.Options.SingleOrDefault(o => o.Id == optionId);
        if (option is null)
            return Reject(state, RejectionReason.UnknownProactiveOption, $"У Проактивного выбора нет варианта «{optionId}»");
        return Accept(StartNextEventOrArrive(state with
        {
            Plan = [.. option.Pool],
            Journal = state.Journal.Add(new ProactiveChosen(option.Id)),
        }));
    }

    /// <summary>
    /// Последствия Варианта или ветки таймаута (она устроена как Вариант) по PRD v7 §5.2: Шкалы с обрезкой по диапазону → Флаги → журнал →
    /// Критическая ошибка → Срыв по Шкале → переход. Срыв наступает раньше перехода, Событие остаётся без Исхода.
    /// Условия перехода видят Шкалы и Флаги после изменений.
    /// </summary>
    private static TripState React(TripState state, Variant reaction, bool timedOut)
    {
        var directory = state.Content!.Directory;
        var scales = state.Scales;
        var changes = new List<ScaleChange>();
        foreach (var (code, delta) in reaction.ScaleDeltas ?? new Dictionary<string, int>())
        {
            var definition = directory.Scale(code);
            var before = scales[code];
            var after = Math.Clamp(before + delta, definition.Min, definition.Max);
            scales = scales.SetItem(code, after);
            changes.Add(new ScaleChange(code, delta, after - before, before, after));
        }
        var flagsSet = (reaction.SetsFlags ?? []).Where(flag => !state.Flags.Contains(flag)).Distinct().ToList();
        var next = state with { Scales = scales, Flags = state.Flags.Union(flagsSet) };

        var brokenScale = BrokenScale(next);
        var ends = reaction.CriticalError || brokenScale is not null;
        var transition = ends ? null : (reaction.Transitions ?? []).First(t => Conditions.AllHold(t.Conditions, next));
        var ev = state.CurrentEvent!;
        next = next with
        {
            Journal = next.Journal.Add(new Decision(
                ev.Id, ev.Version, state.Current!.StepId, timedOut ? null : reaction.Id, timedOut,
                changes, flagsSet, reaction.CriticalError, transition?.To)),
        };

        if (reaction.CriticalError) return Fail(next, new TripFailure(FailureCause.CriticalError));
        if (brokenScale is not null) return Fail(next, new TripFailure(FailureCause.Scale, brokenScale));
        return GoTo(next, transition!.To);
    }

    /// <summary>Первая обязательная Шкала, дошедшая до порога Срыва, или null.</summary>
    private static string? BrokenScale(TripState state) => state.Content!.Directory.Scales
        .FirstOrDefault(s => s.Mandatory && state.Scales[s.Code] <= s.FailureThreshold)?.Code;

    /// <summary>Срыв рейса: Событие, в котором он случился, прервано и остаётся без Исхода.</summary>
    private static TripState Fail(TripState state, TripFailure failure)
    {
        var ev = state.CurrentEvent!;
        return state with
        {
            Status = TripStatus.Failed,
            Phase = null,
            Current = null,
            Failure = failure,
            Journal = state.Journal.Add(new EventFinished(ev.Id, ev.Version, EventResult.Interrupted, OutcomeStepId: null)),
        };
    }

    private static TripState GoTo(TripState state, string stepId)
    {
        var next = state with { Current = state.Current! with { StepId = stepId } };
        var step = next.CurrentStep!;
        return step.IsOutcome ? FinishEvent(next, step) : next;
    }

    private static TripState FinishEvent(TripState state, Step outcome)
    {
        var ev = state.CurrentEvent!;
        var result = outcome.Outcome == "success" ? EventResult.Success : EventResult.Failure;
        var next = state with { Journal = state.Journal.Add(new EventFinished(ev.Id, ev.Version, result, outcome.Id)) };
        return ev.Id == state.Content!.Settings.ShiftStartEvent
            ? next with { Phase = TripPhase.ProactiveChoice, Current = null }
            : StartNextEventOrArrive(next);
    }

    /// <summary>Следующее Событие — первое из плана, ещё не сыгранное и прошедшее свои Условия (фильтр пула).</summary>
    private static TripState StartNextEventOrArrive(TripState state)
    {
        var settings = state.Content!.Settings;
        var played = state.Journal.OfType<EventFinished>().Select(e => e.EventId).ToHashSet();
        if (played.Count(id => id != settings.ShiftStartEvent) >= settings.EventsAfterShiftStart)
            return Arrive(state);
        var nextId = state.Plan.FirstOrDefault(id =>
            !played.Contains(id) && Conditions.AllHold(state.Content.Event(id).Conditions, state));
        return nextId is null ? Arrive(state) : EnterEvent(state, nextId);
    }

    private static TripState EnterEvent(TripState state, string eventId) =>
        state with { Phase = TripPhase.Event, Current = new(eventId, state.Content!.Event(eventId).Start) };

    private static TripState Arrive(TripState state) =>
        state with { Status = TripStatus.Arrived, Phase = null, Current = null };
}
