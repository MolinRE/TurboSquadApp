using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

/// <summary>
/// Рейс через API (ADR-0001, PRD v7 §5.2): контент из базы, серверный таймер, движок, журнал в базе.
/// Состояние Рейса отдельно не хранится: движок детерминирован и заново проигрывает журнал
/// на контенте тех версий Событий, что зафиксированы на старте.
/// </summary>
public sealed class TripService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Допуск на задержку сети: ответ позже таймера больше чем на него засчитывается как таймаут.</summary>
    public static readonly TimeSpan TimerTolerance = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<IResult> StartAsync(Guid userId, string serviceClass, CancellationToken ct)
    {
        var content = await LoadContentAsync(versions: null, ct);
        var result = TripEngine.Reduce(TripState.Initial, new StartTrip(content, serviceClass));
        if (result.Rejection is { } rejection) return Rejected(rejection.Reason.ToString(), rejection.Message);

        var now = clock.GetUtcNow();
        var record = new TripRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ServiceClass = serviceClass,
            EventVersions = JsonSerializer.Serialize(content.Events.ToDictionary(ev => ev.Id, ev => ev.Version)),
            StartedAt = now,
        };
        db.Trips.Add(record);
        Save(record, TripState.Initial, result.State, now);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/trips/{record.Id}", TripView.Of(record.Id, result.State, record.StepStartedAt));
    }

    public async Task<IResult> GetAsync(Guid userId, Guid tripId, CancellationToken ct)
    {
        if (await LoadAsync(userId, tripId, ct) is not { } trip) return Results.NotFound();
        return Results.Ok(TripView.Of(trip.Record.Id, trip.State, trip.Record.StepStartedAt));
    }

    /// <summary>
    /// Ход проводника. Ответ позже таймера больше чем на допуск становится таймаутом;
    /// «время вышло» раньше срока отклоняется — таймер считает сервер, а не клиент.
    /// </summary>
    public async Task<IResult> ActAsync(Guid userId, Guid tripId, TripAction action, CancellationToken ct)
    {
        if (await LoadAsync(userId, tripId, ct) is not { } trip) return Results.NotFound();
        var (record, state) = trip;

        var now = clock.GetUtcNow();
        var expiresAt = TripView.ExpiresAt(state, record.StepStartedAt);
        if (action is ChooseVariant && now > expiresAt + TimerTolerance)
            action = new TimeOut();
        else if (action is TimeOut && now < expiresAt - TimerTolerance)
            return Rejected("TimerNotExpired", $"Время Шага ещё не вышло: таймер истекает в {expiresAt:O}");

        var result = TripEngine.Reduce(state, action);
        if (result.Rejection is { } rejection) return Rejected(rejection.Reason.ToString(), rejection.Message);

        Save(record, state, result.State, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(TripView.Of(record.Id, result.State, record.StepStartedAt));
    }

    public async Task<IResult> DebriefAsync(Guid userId, Guid tripId, CancellationToken ct)
    {
        if (await LoadAsync(userId, tripId, ct) is not { } trip) return Results.NotFound();
        return trip.State.Status is TripStatus.Arrived or TripStatus.Failed
            ? Results.Ok(Debrief.Build(trip.State))
            : Rejected("TripNotFinished", "Разбор строится после Рейса: Рейс ещё не закончен");
    }

    private static IResult Rejected(string reason, string message) => Results.Problem(
        title: "Действие отклонено", detail: message, statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["reason"] = reason });

    private async Task<(TripRecord Record, TripState State)?> LoadAsync(Guid userId, Guid tripId, CancellationToken ct)
    {
        var record = await db.Trips.SingleOrDefaultAsync(trip => trip.Id == tripId && trip.UserId == userId, ct);
        if (record is null) return null;

        var versions = JsonSerializer.Deserialize<Dictionary<string, int>>(record.EventVersions)!;
        var content = await LoadContentAsync(versions, ct);
        var journal = await db.TripJournal.Where(row => row.TripId == tripId).OrderBy(row => row.Seq).ToListAsync(ct);
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

    /// <summary>Новые записи журнала движка — в базу; итог Рейса и момент показа следующего Шага — в запись Рейса.</summary>
    private void Save(TripRecord record, TripState before, TripState after, DateTimeOffset now)
    {
        var elapsedMs = (int)(now - record.StepStartedAt).TotalMilliseconds;
        var seq = before.Journal.Count;
        foreach (var entry in after.Journal.Skip(before.Journal.Count))
            db.TripJournal.Add(Row(record.Id, ++seq, entry, elapsedMs, now));

        record.Status = JsonNamingPolicy.CamelCase.ConvertName(after.Status.ToString());
        record.FailureCause = after.Failure is { } failure ? JsonNamingPolicy.CamelCase.ConvertName(failure.Cause.ToString()) : null;
        record.FailureScale = after.Failure?.Scale;
        record.FinishedAt = after.Status is TripStatus.Arrived or TripStatus.Failed ? now : null;
        record.StepStartedAt = now;
    }

    private static TripJournalRecord Row(Guid tripId, int seq, JournalEntry entry, int elapsedMs, DateTimeOffset now) => entry switch
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
            ScaleChanges = JsonSerializer.Serialize(decision.Changes, Json), FlagsSet = JsonSerializer.Serialize(decision.FlagsSet, Json),
            CriticalError = decision.CriticalError, ToStepId = decision.To,
        },
        EventFinished finished => new()
        {
            TripId = tripId, Seq = seq, CreatedAt = now, Kind = TripJournalKinds.EventFinished,
            EventId = finished.EventId, EventVersion = finished.EventVersion,
            Result = JsonNamingPolicy.CamelCase.ConvertName(finished.Result.ToString()), OutcomeStepId = finished.OutcomeStepId,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "Неизвестная запись журнала"),
    };

    /// <summary>Справочники и настройка Рейса — текущие; События — зафиксированных версий или последние опубликованные.</summary>
    private async Task<TripContent> LoadContentAsync(IReadOnlyDictionary<string, int>? versions, CancellationToken ct)
    {
        var scales = await db.Scales.OrderBy(s => s.Code).ToListAsync(ct);
        var classes = await db.ServiceClasses.OrderBy(c => c.SortOrder).ToListAsync(ct);
        var directory = new ContentDirectory(
            scales.Select(s => new ScaleDefinition(s.Code, s.Name, s.Min, s.Max, s.Start, s.FailureThreshold, s.Mandatory, s.FailureReason)).ToList(),
            classes.Select(c => new ServiceClass(c.Code, c.Name, c.Description)).ToList());

        var settings = await db.TripSettings.SingleAsync(s => s.Id == ContentSeeder.DefaultTripSettingsId, ct);

        var records = await db.EventDocuments.ToListAsync(ct);
        var played = versions is null
            ? records.GroupBy(r => r.EventId).Select(g => g.MaxBy(r => r.Version)!)
            : records.Where(r => versions.GetValueOrDefault(r.EventId) == r.Version);
        var events = played.Select(r => JsonSerializer.Deserialize<EventDocument>(r.Document, EventJson.Options)!).ToList();

        return new TripContent(directory, events, JsonSerializer.Deserialize<TripSettings>(settings.Document, EventJson.Options)!);
    }
}
