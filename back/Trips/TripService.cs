using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

/// <summary>
/// Рейс через API (ADR-0001, PRD v7 §5.2): контент из базы, серверный таймер, движок, журнал в базе.
/// Состояние Рейса отдельно не хранится: движок детерминирован и заново проигрывает журнал на контенте,
/// зафиксированном на старте, — справочники и настройка Рейса снимком, События ссылками на версии.
/// </summary>
public sealed class TripService(AppDbContext dbContext, TimeProvider clock)
{
    /// <summary>Допуск на задержку сети: ответ позже таймера больше чем на него засчитывается как таймаут.</summary>
    public static readonly TimeSpan TimerTolerance = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions SnapshotJson = JsonSerializerOptions.Web;

    public async Task<IResult> StartAsync(Guid userId, string serviceClass, CancellationToken cancellationToken)
    {
        var content = await LatestContentAsync(cancellationToken);
        var result = TripEngine.Reduce(TripState.Initial, new StartTrip(content, serviceClass));
        if (result.Rejection is { } rejection) return Rejected(rejection.Reason.ToString(), rejection.Message);

        var now = clock.GetUtcNow();
        var record = new TripRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ServiceClass = serviceClass,
            Directory = JsonSerializer.Serialize(content.Directory, SnapshotJson),
            Settings = JsonSerializer.Serialize(content.Settings, EventJson.Options),
            EventVersions = JsonSerializer.Serialize(content.Events.ToDictionary(ev => ev.Id, ev => ev.Version)),
            StartedAt = now,
        };
        dbContext.Trips.Add(record);
        AppendToJournal(record, TripState.Initial, result.State, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/trips/{record.Id}", View(record, result.State));
    }

    public async Task<IResult> GetAsync(Guid userId, Guid tripId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, tripId, cancellationToken) is not { } trip) return Results.NotFound();
        return Results.Ok(View(trip.Record, trip.State));
    }

    /// <summary>
    /// Ход проводника. Ответ позже таймера больше чем на допуск становится таймаутом;
    /// «время вышло» раньше срока отклоняется — таймер считает сервер, а не клиент.
    /// </summary>
    /// <param name="at">Шаг, на который отвечает проводник: ответ на уже пройденный Шаг (повторный клик) отклоняется.</param>
    public async Task<IResult> ActAsync(Guid userId, Guid tripId, TripAction action, StepPosition? at, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, tripId, cancellationToken) is not { } trip) return Results.NotFound();
        var (record, state) = trip;
        if (at is not null && state.Status == TripStatus.Running && state.Current != at)
            return Rejected("StaleStep", $"Ответ на Шаг {at.StepId} События {at.EventId}, а Рейс уже на другом Шаге");

        var now = clock.GetUtcNow();
        var expiresAt = ExpiresAt(record, state);
        if (action is ChooseVariant && now > expiresAt + TimerTolerance)
            action = new TimeOut();
        else if (action is TimeOut && now < expiresAt)
            return Rejected("TimerNotExpired", $"Время Шага ещё не вышло: таймер истекает в {expiresAt:O}");

        var result = TripEngine.Reduce(state, action);
        if (result.Rejection is { } rejection) return Rejected(rejection.Reason.ToString(), rejection.Message);

        AppendToJournal(record, state, result.State, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(View(record, result.State));
    }

    public async Task<IResult> DebriefAsync(Guid userId, Guid tripId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, tripId, cancellationToken) is not { } trip) return Results.NotFound();
        return trip.State.IsFinished
            ? Results.Ok(Debrief.Build(trip.State))
            : Rejected("TripNotFinished", "Разбор строится после Рейса: Рейс ещё не закончен");
    }

    private static TripView View(TripRecord record, TripState state) => TripView.Of(record.Id, state, ExpiresAt(record, state));

    /// <summary>Таймер Шага идёт с момента, когда Шаг показан; у Шага без таймера — null.</summary>
    private static DateTimeOffset? ExpiresAt(TripRecord record, TripState state) =>
        state.CurrentStep?.TimerSec is { } seconds ? record.StepStartedAt.AddSeconds(seconds) : null;

    private static IResult Rejected(string reason, string message) => Results.Problem(
        title: "Действие отклонено", detail: message, statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["reason"] = reason });

    private async Task<(TripRecord Record, TripState State)?> LoadAsync(Guid userId, Guid tripId, CancellationToken cancellationToken)
    {
        var record = await dbContext.Trips.SingleOrDefaultAsync(trip => trip.Id == tripId && trip.UserId == userId, cancellationToken);
        if (record is null) return null;

        var versions = JsonSerializer.Deserialize<Dictionary<string, int>>(record.EventVersions)!;
        var eventIds = versions.Keys.ToList();
        var events = (await dbContext.EventDocuments.Where(r => eventIds.Contains(r.EventId)).ToListAsync(cancellationToken))
            .Where(r => versions[r.EventId] == r.Version)
            .Select(r => JsonSerializer.Deserialize<EventDocument>(r.Document, EventJson.Options)!)
            .ToList();
        var content = new TripContent(
            JsonSerializer.Deserialize<ContentDirectory>(record.Directory, SnapshotJson)!,
            events,
            JsonSerializer.Deserialize<TripSettings>(record.Settings, EventJson.Options)!);

        var journal = await dbContext.TripJournal.Where(row => row.TripId == tripId).OrderBy(row => row.Seq).ToListAsync(cancellationToken);
        return (record, Replay(content, record.ServiceClass, journal));
    }

    /// <summary>Проигрывает ходы из журнала. Итоги Событий движок выводит сам, их строки пропускаются.</summary>
    private static TripState Replay(TripContent content, string serviceClass, IEnumerable<TripJournalRecord> journal)
    {
        var state = Expect(TripEngine.Reduce(TripState.Initial, new StartTrip(content, serviceClass)));
        foreach (var row in journal)
        {
            TripAction? action = row.Kind switch
            {
                TripJournalKinds.ProactiveChoice => new ChooseProactive(row.OptionId!),
                TripJournalKinds.Decision => row.TimedOut ? new TimeOut() : new ChooseVariant(row.VariantId!),
                _ => null,
            };
            if (action is not null) state = Expect(TripEngine.Reduce(state, action));
        }
        return state;

        static TripState Expect(TripResult result) => result.Rejection is null
            ? result.State
            : throw new InvalidOperationException($"Журнал Рейса не проигрывается: {result.Rejection.Message}");
    }

    /// <summary>
    /// Новые записи журнала движка — строками в контекст базы; итог Рейса и момент показа следующего Шага — в запись Рейса.
    /// Сохраняет вызывающий.
    /// </summary>
    private void AppendToJournal(TripRecord record, TripState before, TripState after, DateTimeOffset now)
    {
        var elapsedMs = (int)(now - record.StepStartedAt).TotalMilliseconds;
        var seq = before.Journal.Count;
        foreach (var entry in after.Journal.Skip(before.Journal.Count))
            dbContext.TripJournal.Add(JournalRow(record.Id, ++seq, entry, elapsedMs, now));

        record.Status = Code(after.Status);
        record.FailureCause = after.Failure is { } failure ? Code(failure.Cause) : null;
        record.FailureScale = after.Failure?.Scale;
        record.FinishedAt = after.IsFinished ? now : null;
        record.StepStartedAt = now;
    }

    private static TripJournalRecord JournalRow(Guid tripId, int seq, JournalEntry entry, int elapsedMs, DateTimeOffset now) => entry switch
    {
        ProactiveChosen chosen => new()
        {
            TripId = tripId, Seq = seq, CreatedAt = now, Kind = TripJournalKinds.ProactiveChoice,
            OptionId = chosen.OptionId, ElapsedMs = elapsedMs,
        },
        Decision decision => new()
        {
            TripId = tripId, Seq = seq, CreatedAt = now, Kind = TripJournalKinds.Decision,
            EventId = decision.EventId, EventVersion = decision.EventVersion, StepId = decision.StepId,
            VariantId = decision.VariantId, TimedOut = decision.TimedOut, ElapsedMs = elapsedMs,
            ScaleChanges = JsonSerializer.Serialize(decision.Changes, SnapshotJson),
            FlagsSet = JsonSerializer.Serialize(decision.FlagsSet, SnapshotJson),
            CriticalError = decision.CriticalError, ToStepId = decision.To,
        },
        EventFinished finished => new()
        {
            TripId = tripId, Seq = seq, CreatedAt = now, Kind = TripJournalKinds.EventFinished,
            EventId = finished.EventId, EventVersion = finished.EventVersion,
            Result = Code(finished.Result), OutcomeStepId = finished.OutcomeStepId,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "Неизвестная запись журнала"),
    };

    /// <summary>Значение перечисления движка так же, как в JSON API: running, criticalError, interrupted.</summary>
    private static string Code(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>Контент для нового Рейса: текущие справочники и настройка, последние опубликованные версии Событий.</summary>
    private async Task<TripContent> LatestContentAsync(CancellationToken cancellationToken)
    {
        var scales = await dbContext.Scales.OrderBy(s => s.Code).ToListAsync(cancellationToken);
        var classes = await dbContext.ServiceClasses.OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var directory = new ContentDirectory(
            scales.Select(s => new ScaleDefinition(s.Code, s.Name, s.Min, s.Max, s.Start, s.FailureThreshold, s.Mandatory, s.FailureReason)).ToList(),
            classes.Select(c => new ServiceClass(c.Code, c.Name, c.Description)).ToList());

        var settings = await dbContext.TripSettings.SingleAsync(s => s.Id == ContentSeeder.DefaultTripSettingsId, cancellationToken);
        var events = (await dbContext.EventDocuments.ToListAsync(cancellationToken))
            .GroupBy(r => r.EventId)
            .Select(g => JsonSerializer.Deserialize<EventDocument>(g.MaxBy(r => r.Version)!.Document, EventJson.Options)!)
            .ToList();

        return new TripContent(directory, events, JsonSerializer.Deserialize<TripSettings>(settings.Document, EventJson.Options)!);
    }
}
