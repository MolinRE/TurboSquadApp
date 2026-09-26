using System.Collections.Immutable;
using TurboSquadApp.Content;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

/// <summary>Контент Рейса, зафиксированный на старте: справочники, События тех версий, что будут играться, и настройка Рейса.</summary>
public sealed record TripContent(ContentDirectory Directory, IReadOnlyList<EventDocument> Events, TripSettings Settings)
{
    public EventDocument Event(string id) => Events.Single(e => e.Id == id);
}

/// <summary>Действие над Рейсом: его присылает проводник, а «время вышло» — серверный таймер.</summary>
public abstract record TripAction;

/// <summary>Начать Рейс с контентом и Классом обслуживания на весь Рейс.</summary>
public sealed record StartTrip(TripContent Content, string ServiceClass) : TripAction;

/// <summary>Выбрать Вариант текущего Шага.</summary>
public sealed record ChooseVariant(string VariantId) : TripAction;

/// <summary>Таймер Шага истёк: срабатывает ветка таймаута.</summary>
public sealed record TimeOut : TripAction;

/// <summary>Проактивный выбор между Событиями: задаёт порядок следующих Событий.</summary>
public sealed record ChooseProactive(string OptionId) : TripAction;

public enum TripStatus { NotStarted, Running, Arrived, Failed }

/// <summary>Чего Рейс ждёт от проводника: реакции на Шаг События или Проактивного выбора.</summary>
public enum TripPhase { Event, ProactiveChoice }

public sealed record StepPosition(string EventId, string StepId);

/// <summary>Состояние Рейса. Меняется только через <see cref="TripEngine.Reduce"/>.</summary>
public sealed record TripState
{
    public static readonly TripState Initial = new();

    public TripStatus Status { get; init; } = TripStatus.NotStarted;
    public TripPhase? Phase { get; init; }
    public TripContent? Content { get; init; }
    public string? ServiceClass { get; init; }

    /// <summary>Код Шкалы → значение.</summary>
    public ImmutableDictionary<string, int> Scales { get; init; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>Флаги Рейса: только добавляются.</summary>
    public ImmutableHashSet<string> Flags { get; init; } = [];

    /// <summary>Порядок Событий после Проактивного выбора.</summary>
    public ImmutableList<string> Plan { get; init; } = [];

    /// <summary>Где проводник сейчас; вне Шага События — null.</summary>
    public StepPosition? Current { get; init; }

    /// <summary>Журнал Рейса (ADR-0001): из него строится Разбор.</summary>
    public ImmutableList<JournalEntry> Journal { get; init; } = [];

    /// <summary>Причина Срыва рейса; у идущего Рейса и Прибытия — null.</summary>
    public TripFailure? Failure { get; init; }

    public EventDocument? CurrentEvent => Current is null ? null : Content!.Event(Current.EventId);

    public Step? CurrentStep => Current is null ? null : CurrentEvent!.Steps.Single(s => s.Id == Current.StepId);

    /// <summary>Варианты текущего Шага: доступные и скрытые Условиями, с причиной. Сервер перепроверяет выбор по ним же.</summary>
    public IReadOnlyList<Choice> Choices => (CurrentStep?.Variants ?? []).Select(ToChoice).ToList();

    private Choice ToChoice(Variant variant)
    {
        var failed = Conditions.Failed(variant.Conditions, this);
        return new Choice(variant, failed.Count == 0, failed);
    }
}

/// <summary>Вариант Шага для проводника: доступен или скрыт, и какие Условия не выполнены.</summary>
public sealed record Choice(Variant Variant, bool Available, IReadOnlyList<string> HiddenBecause);

public enum FailureCause { CriticalError, Scale }

/// <summary>Срыв рейса: Критическая ошибка или обязательная Шкала Scale дошла до порога.</summary>
public sealed record TripFailure(FailureCause Cause, string? Scale = null);

public enum EventResult { Success, Failure, Interrupted }

/// <summary>Запись журнала Рейса.</summary>
public abstract record JournalEntry;

public sealed record ProactiveChosen(string OptionId) : JournalEntry;

/// <summary>
/// Решение проводника на Шаге: Вариант или таймаут (тогда VariantId пуст). Ссылается на версию События;
/// тексты берутся из контента. FlagsSet — Флаги, которых до решения не было. To — куда ведёт переход, при Срыве пусто.
/// </summary>
public sealed record Decision(
    string EventId, int EventVersion, string StepId, string? VariantId, bool TimedOut,
    IReadOnlyList<ScaleChange> Changes, IReadOnlyList<string> FlagsSet, bool CriticalError, string? To) : JournalEntry;

/// <summary>Изменение Шкалы: из контента (Nominal) и фактическое после обрезки по диапазону (Applied).</summary>
public sealed record ScaleChange(string Scale, int Nominal, int Applied, int Before, int After);

/// <summary>Событие закончилось Исходом или прервано Срывом рейса (тогда без Исхода).</summary>
public sealed record EventFinished(string EventId, int EventVersion, EventResult Result, string? OutcomeStepId) : JournalEntry;
