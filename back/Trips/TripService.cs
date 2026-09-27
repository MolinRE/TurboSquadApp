using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Voice;
using TurboSquadApp.Scoring;

namespace TurboSquadApp.Trips;

/// <summary>
/// Рейс через API (ADR-0001, PRD v7 §5.2): контент из базы, серверный таймер, движок, журнал в базе.
/// Состояние Рейса отдельно не хранится: движок детерминирован и заново проигрывает журнал на контенте,
/// зафиксированном на старте, — справочники и настройка Рейса снимком, События ссылками на версии.
/// </summary>
public sealed class TripService(
    AppDbContext dbContext, TimeProvider clock, IVoicePipeline voicePipeline, ILlmClient llmClient, VoiceOptions voiceOptions,
    KnowledgeScoringService scoring)
{
    /// <summary>Допуск на задержку сети: ответ позже таймера больше чем на него засчитывается как таймаут.</summary>
    public static readonly TimeSpan TimerTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Таймер голосового Шага — время на то, чтобы начать отвечать (PRD §6). Начало ответа —
    /// момент прихода аудио минус длительность записи, которую сообщает клиент; допуск покрывает загрузку.
    /// </summary>
    public static readonly TimeSpan VoiceStartTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRecording = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions SnapshotJson = JsonSerializerOptions.Web;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryReplyClaims = new();

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
        await AppendToJournalAsync(record, TripState.Initial, result.State, now, cancellationToken);
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
        if (action is ChooseVariant && state.CurrentStep?.AnswerType == "voice")
            return Rejected("VoiceStepRequiresVoice", "Этот Шаг принимает только голосовой ответ");

        var now = clock.GetUtcNow();
        var expiresAt = ExpiresAt(record, state);
        if (state.Journal.OfType<VoiceAttempt>().Any(IsPending) &&
            (action is not TimeOut || now < expiresAt))
            return Rejected("VoiceReplyPending", "Реплика пассажира ещё генерируется");
        if (action is ChooseVariant && now > expiresAt + TimerTolerance)
            action = new TimeOut();
        else if (action is TimeOut && now < expiresAt)
            return Rejected("TimerNotExpired", $"Время Шага ещё не вышло: таймер истекает в {expiresAt:O}");

        var result = TripEngine.Reduce(state, action);
        if (result.Rejection is { } rejection) return Rejected(rejection.Reason.ToString(), rejection.Message);

        await AppendToJournalAsync(record, state, result.State, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(View(record, result.State));
    }

    public async Task<IResult> VoiceAsync(
        Guid userId, Guid tripId, string eventId, string stepId, string attemptId, IFormFile audio, int? recordingMs,
        CancellationToken cancellationToken)
    {
        var answerStartedAt = clock.GetUtcNow()
            - TimeSpan.FromMilliseconds(Math.Clamp(recordingMs ?? 0, 0, (int)MaxRecording.TotalMilliseconds));
        if (await LoadAsync(userId, tripId, cancellationToken) is not { } trip) return Results.NotFound();
        var (record, state) = trip;
        if (string.IsNullOrWhiteSpace(attemptId))
            return Results.UnprocessableEntity(new { reason = "AttemptIdRequired", message = "Нужен идентификатор голосовой попытки" });
        if (attemptId.Length > 100)
            return Results.UnprocessableEntity(new { reason = "AttemptIdInvalid", message = "Идентификатор голосовой попытки слишком длинный" });
        if (state.Journal.OfType<VoiceAttempt>().FirstOrDefault(attempt => attempt.AttemptId == attemptId) is { } existing)
            return Results.Ok(VoiceResponse(record, state, existing));
        if (state.Journal.OfType<VoiceAttempt>().Any(attempt => IsPending(attempt)))
            return Rejected("VoiceReplyPending", "Реплика предыдущей голосовой попытки ещё генерируется");
        var position = new StepPosition(eventId, stepId);
        if (state.Status != TripStatus.Running)
            return Rejected("TripNotRunning", "Рейс не идёт: он не начат или уже закончился");
        if (state.Current != position)
            return Rejected("StaleStep", $"Ответ на Шаг {stepId} События {eventId}, а Рейс уже на другом Шаге");
        if (state.CurrentStep?.AnswerType != "voice")
            return Rejected("NotVoiceStep", "Текущий Шаг не принимает голосовой ответ");
        if (audio.Length <= 0)
            return Rejected("EmptyAudio", "Аудиофайл пустой");
        if (audio.Length > voiceOptions.MaxAudioBytes)
            return Rejected("AudioTooLarge", $"Аудиофайл больше допустимого размера {voiceOptions.MaxAudioBytes} байт");

        var currentEvent = state.CurrentEvent!;
        var currentStep = state.CurrentStep!;
        var available = currentStep.Variants!
            .Where(variant => state.Choices.Single(choice => choice.Variant.Id == variant.Id).Available)
            .ToList();
        var questions = available.Select(variant => new VoiceQuestion(variant.Id, variant.LayaText)).ToList();
        var expectedStages = (currentStep.RequiredRoleStages ?? [])
            .Concat(available.SelectMany(variant => variant.RoleStages ?? []))
            .ToHashSet();
        var roleStages = LayaAssessment.RoleStageCodes.Where(expectedStages.Contains).ToList();
        VoicePipelineResult pipeline;
        await using (var stream = audio.OpenReadStream())
        {
            pipeline = await voicePipeline.ProcessAsync(
                new VoicePipelineRequest(
                    currentEvent.Id, currentEvent.Version, currentStep.Id, currentStep.Situation,
                    currentStep.Brief, questions, roleStages),
                stream, audio.FileName, audio.ContentType, cancellationToken);
        }

        var attempt = new VoiceAttempt(
            currentEvent.Id, currentEvent.Version, currentStep.Id, pipeline.Transcript, pipeline.Choice,
            pipeline.Confidence, pipeline.LatencyMs, false, pipeline.ErrorCode, pipeline.ProviderRequestId, attemptId,
            Assessment: pipeline.Assessment, SttLatencyMs: pipeline.SttLatencyMs, LayaLatencyMs: pipeline.LayaLatencyMs);
        var attemptResult = TripEngine.Reduce(state, new RecordVoiceAttempt(attempt));
        if (attemptResult.Rejection is { } rejection)
            return Rejected(rejection.Reason.ToString(), rejection.Message);

        var afterAttempt = attemptResult.State;
        var now = clock.GetUtcNow();
        var expiresAt = ExpiresAt(record, state);
        if (expiresAt is { } expires && answerStartedAt > expires + VoiceStartTolerance)
        {
            var timedOutAttempt = attempt with { ErrorCode = "VoiceDeadlineExceeded" };
            var timedOutState = TripEngine.Reduce(state, new RecordVoiceAttempt(timedOutAttempt)).State;
            var timeoutResult = TripEngine.Reduce(timedOutState, new TimeOut());
            await AppendToJournalAsync(record, state, timeoutResult.State, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(View(record, timeoutResult.State, VoiceView(timedOutAttempt)));
        }

        if (!pipeline.Applied || pipeline.Choice is null)
        {
            await AppendToJournalAsync(record, state, afterAttempt, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            var failedView = VoiceView(attempt, pending: NeedsClarification(attempt));
            return Results.UnprocessableEntity(new
            {
                reason = pipeline.ErrorCode ?? "VoiceAttemptFailed",
                message = pipeline.ErrorMessage ?? "Голосовая попытка не прошла",
                attempt = failedView,
                trip = View(record, afterAttempt, failedView)
            });
        }

        var result = TripEngine.Reduce(afterAttempt, new ChooseVariant(pipeline.Choice));
        if (result.Rejection is { } variantRejection)
        {
            var rejectedAttempt = attempt with { ErrorCode = variantRejection.Reason.ToString() };
            var rejectedState = TripEngine.Reduce(state, new RecordVoiceAttempt(rejectedAttempt)).State;
            await AppendToJournalAsync(record, state, rejectedState, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Rejected(variantRejection.Reason.ToString(), variantRejection.Message);
        }

        await AppendToJournalAsync(record, state, afterAttempt, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(TripView.Of(record.Id, result.State, null, VoiceView(attempt, pending: true)));
    }

    public async Task StreamVoiceReplyAsync(
        Guid userId, Guid tripId, string attemptId, HttpResponse response, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, tripId, cancellationToken) is not { } trip)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var (record, state) = trip;
        var attempt = state.Journal.OfType<VoiceAttempt>().FirstOrDefault(item => item.AttemptId == attemptId);
        if (attempt is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        if (attempt.ReplyError is not null)
        {
            await WriteSseAsync(response, "error", new { reason = attempt.ReplyError, message = "Реплика пассажира не сгенерирована", trip = View(record, state, VoiceView(attempt)) }, cancellationToken);
            return;
        }
        if (attempt.PassengerReply is not null)
        {
            await WriteSseAsync(response, "done", new { reply = attempt.PassengerReply, trip = View(record, state, VoiceView(attempt)) }, cancellationToken);
            return;
        }
        if (NeedsClarification(attempt))
        {
            await StreamClarificationAsync(record, state, attempt, response, cancellationToken);
            return;
        }
        if (!IsPending(attempt))
        {
            await WriteSseAsync(response, "error", new { reason = attempt.ErrorCode ?? "VoiceAttemptFailed", message = "Голосовая попытка не применена", trip = View(record, state, VoiceView(attempt)) }, cancellationToken);
            return;
        }

        var preview = TripEngine.Reduce(state, new ChooseVariant(attempt.Choice!));
        if (preview.Rejection is { } previewError)
        {
            await SaveReplyAsync(record.Id, attemptId, null, previewError.Reason.ToString());
            await WriteSseAsync(response, "error", new { reason = previewError.Reason.ToString(), message = previewError.Message, trip = View(record, state, VoiceView(attempt)) }, cancellationToken);
            return;
        }

        var context = PassengerReplyContext.From(preview.State, attempt);
        if (!await TryClaimReplyAsync(record.Id, attemptId))
        {
            await WriteSseAsync(response, "error", new
            {
                reason = "VoiceReplyPending",
                message = "Реплика уже генерируется другим подключением"
            }, cancellationToken);
            return;
        }

        var committed = false;
        var llmTimer = Stopwatch.StartNew();
        try
        {
            // Ответ уже принят вовремя: медленная генерация реплики его не отменяет.
            var (reply, requestId) = await StreamTokensAsync(response, attemptId, context.ToLlmRequest(), cancellationToken);

            var row = await dbContext.TripJournal.SingleAsync(
                item => item.TripId == record.Id && item.Kind == TripJournalKinds.VoiceAttempt && item.VoiceAttemptId == attemptId,
                CancellationToken.None);
            row.VoiceApplied = true;
            row.VoicePassengerReply = reply;
            row.VoiceLlmLatencyMs = (int)llmTimer.ElapsedMilliseconds;
            await AppendToJournalAsync(record, state, preview.State, clock.GetUtcNow(), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            committed = true;
            var appliedAttempt = attempt with { Applied = true, PassengerReply = reply, LlmLatencyMs = row.VoiceLlmLatencyMs };
            await WriteSseAsync(response, "done", new { reply, requestId, trip = View(record, preview.State, VoiceView(appliedAttempt)) }, cancellationToken);
        }
        catch (LlmProviderException ex)
        {
            await SaveReplyAsync(record.Id, attemptId, null, ex.Code, (int)llmTimer.ElapsedMilliseconds);
            await WriteSseAsync(response, "error", new { reason = ex.Code, message = ex.Message, trip = View(record, state, VoiceView(attempt with { ReplyError = ex.Code })) }, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!committed) await SaveReplyAsync(record.Id, attemptId, null, "LlmClientDisconnected", (int)llmTimer.ElapsedMilliseconds);
        }
        catch (IOException)
        {
            if (!committed) await SaveReplyAsync(record.Id, attemptId, null, "LlmClientDisconnected", (int)llmTimer.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Уточнение пассажира после попытки с низкой уверенностью Laya: Вариант не применяется,
    /// реплика сохраняется в попытке, проводник отвечает снова на том же Шаге.
    /// </summary>
    private async Task StreamClarificationAsync(
        TripRecord record, TripState state, VoiceAttempt attempt, HttpResponse response, CancellationToken cancellationToken)
    {
        if (!await TryClaimReplyAsync(record.Id, attempt.AttemptId))
        {
            await WriteSseAsync(response, "error", new { reason = "VoiceReplyPending", message = "Реплика уже генерируется другим подключением" }, cancellationToken);
            return;
        }

        var committed = false;
        var llmTimer = Stopwatch.StartNew();
        try
        {
            var request = PassengerReplyContext.From(state, attempt).ToClarificationRequest();
            var (reply, requestId) = await StreamTokensAsync(response, attempt.AttemptId, request, cancellationToken);
            var latency = (int)llmTimer.ElapsedMilliseconds;
            record.StepStartedAt = clock.GetUtcNow();   // пассажир задал новый вопрос: время на ответ заново
            await SaveReplyAsync(record.Id, attempt.AttemptId, reply, null, latency);
            committed = true;
            var answered = attempt with { PassengerReply = reply, LlmLatencyMs = latency };
            await WriteSseAsync(response, "done", new { reply, requestId, trip = View(record, state, VoiceView(answered)) }, cancellationToken);
        }
        catch (LlmProviderException ex)
        {
            await SaveReplyAsync(record.Id, attempt.AttemptId, null, ex.Code, (int)llmTimer.ElapsedMilliseconds);
            await WriteSseAsync(response, "error", new { reason = ex.Code, message = ex.Message, trip = View(record, state, VoiceView(attempt with { ReplyError = ex.Code })) }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            if (!committed) await SaveReplyAsync(record.Id, attempt.AttemptId, null, "LlmClientDisconnected", (int)llmTimer.ElapsedMilliseconds);
        }
    }

    private async Task<(string Reply, string? RequestId)> StreamTokensAsync(
        HttpResponse response, string attemptId, LlmRequest request, CancellationToken cancellationToken)
    {
        await WriteSseAsync(response, "started", new { attemptId }, cancellationToken);
        var reply = new System.Text.StringBuilder();
        string? requestId = null;
        await foreach (var token in llmClient.StreamAsync(request, cancellationToken))
        {
            requestId ??= token.RequestId;
            reply.Append(token.Text);
            await WriteSseAsync(response, "token", new { text = token.Text }, cancellationToken);
        }
        return (reply.ToString(), requestId);
    }

    public async Task<IResult> DebriefAsync(Guid userId, Guid tripId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, tripId, cancellationToken) is not { } trip) return Results.NotFound();
        if (!trip.State.IsFinished)
            return Rejected("TripNotFinished", "Разбор строится после Рейса: Рейс ещё не закончен");
        var facts = await dbContext.TripJournal
            .Where(row => row.TripId == tripId && row.Kind == TripJournalKinds.Decision)
            .OrderBy(row => row.Seq)
            .Select(row => new DebriefDecisionFacts(row.KnowledgeDelta, row.ElapsedMs))
            .ToListAsync(cancellationToken);
        return Results.Ok(Debrief.Build(trip.State, facts));
    }

    public async Task<IResult> ListDebriefsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var history = await dbContext.Trips
            .Where(trip => trip.UserId == userId && trip.FinishedAt != null)
            .OrderByDescending(trip => trip.FinishedAt)
            .Select(trip => new TripDebriefListItem(trip.Id, trip.Status, trip.StartedAt, trip.FinishedAt!.Value))
            .ToListAsync(cancellationToken);
        return Results.Ok(history);
    }

    private static TripView View(TripRecord record, TripState state, VoiceAttemptView? voiceAttempt = null) =>
        TripView.Of(record.Id, state, ExpiresAt(record, state), voiceAttempt);

    private static VoiceAttemptView VoiceView(VoiceAttempt attempt, bool pending = false) => new(
        attempt.Transcript, attempt.Choice, attempt.Confidence, attempt.LatencyMs,
        attempt.Applied, attempt.ErrorCode, attempt.ProviderRequestId, attempt.AttemptId,
        attempt.PassengerReply, attempt.ReplyError, pending,
        attempt.Assessment?.Score, attempt.Assessment?.ScoreConfidence, attempt.Assessment?.RoleStages,
        attempt.Assessment?.SafetyViolation, attempt.Assessment?.SafetyConfidence,
        attempt.SttLatencyMs, attempt.LayaLatencyMs, attempt.LlmLatencyMs);

    private static bool IsPending(VoiceAttempt attempt) =>
        !attempt.Applied && attempt.Choice is not null && attempt.ErrorCode is null && attempt.ReplyError is null;

    /// <summary>Laya назвала Вариант, но ниже порога: ждём уточнения пассажира.</summary>
    private static bool NeedsClarification(VoiceAttempt attempt) =>
        !attempt.Applied && attempt.ErrorCode == "LowConfidence" && attempt.Choice is not null
        && attempt.PassengerReply is null && attempt.ReplyError is null;

    private static TripView VoiceResponse(TripRecord record, TripState state, VoiceAttempt attempt)
    {
        if (NeedsClarification(attempt)) return View(record, state, VoiceView(attempt, pending: true));
        if (!IsPending(attempt)) return View(record, state, VoiceView(attempt));
        var preview = TripEngine.Reduce(state, new ChooseVariant(attempt.Choice!));
        return preview.Rejection is null
            ? TripView.Of(record.Id, preview.State, null, VoiceView(attempt, pending: true))
            : View(record, state, VoiceView(attempt));
    }

    private async Task SaveReplyAsync(Guid tripId, string attemptId, string? reply, string? error, int? llmLatencyMs = null)
    {
        var row = await dbContext.TripJournal.SingleOrDefaultAsync(
            item => item.TripId == tripId && item.Kind == TripJournalKinds.VoiceAttempt && item.VoiceAttemptId == attemptId,
            CancellationToken.None);
        if (row is null) return;
        row.VoicePassengerReply = reply;
        row.VoiceReplyError = error;
        row.VoiceLlmLatencyMs = llmLatencyMs;
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Атомарно занимает генерацию реплики. PostgreSQL делает это условным UPDATE;
    /// InMemory-провайдер тестов не поддерживает ExecuteUpdate, поэтому для него
    /// используется локальный семафор с той же проверкой полей.
    /// </summary>
    private async Task<bool> TryClaimReplyAsync(Guid tripId, string attemptId)
    {
        var startedAt = clock.GetUtcNow();
        if (dbContext.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            var gate = InMemoryReplyClaims.GetOrAdd($"{tripId:N}:{attemptId}", _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                var row = await dbContext.TripJournal.SingleOrDefaultAsync(
                    item => item.TripId == tripId && item.Kind == TripJournalKinds.VoiceAttempt && item.VoiceAttemptId == attemptId,
                    CancellationToken.None);
                if (row is null || row.VoicePassengerReply is not null || row.VoiceReplyError is not null || row.VoiceReplyStartedAt is not null)
                    return false;
                row.VoiceReplyStartedAt = startedAt;
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return true;
            }
            finally
            {
                gate.Release();
            }
        }

        return await dbContext.TripJournal
            .Where(item => item.TripId == tripId && item.Kind == TripJournalKinds.VoiceAttempt && item.VoiceAttemptId == attemptId
                && item.VoicePassengerReply == null && item.VoiceReplyError == null && item.VoiceReplyStartedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.VoiceReplyStartedAt, startedAt), CancellationToken.None) == 1;
    }

    private static async Task WriteSseAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

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
            if (row.Kind == TripJournalKinds.VoiceAttempt)
            {
                state = Expect(TripEngine.Reduce(state, new RecordVoiceAttempt(new VoiceAttempt(
                    row.EventId!, row.EventVersion!.Value, row.StepId!, row.VoiceTranscript, row.VoiceChoice,
                    row.VoiceConfidence, row.VoiceLatencyMs ?? 0, row.VoiceApplied, row.VoiceError, row.VoiceRequestId,
                    row.VoiceAttemptId ?? $"legacy:{row.Seq}", row.VoicePassengerReply, row.VoiceReplyError,
                    AssessmentOf(row), row.VoiceSttLatencyMs, row.VoiceLayaLatencyMs, row.VoiceLlmLatencyMs))));
                continue;
            }
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

        static LayaAssessment? AssessmentOf(TripJournalRecord row) =>
            row.VoiceScore is null && row.VoiceSafetyViolation is null && row.VoiceRoleStages is null
                ? null
                : new(
                    row.VoiceScore,
                    row.VoiceScoreConfidence,
                    string.IsNullOrWhiteSpace(row.VoiceRoleStages)
                        ? new Dictionary<string, double>()
                        : JsonSerializer.Deserialize<Dictionary<string, double>>(row.VoiceRoleStages, SnapshotJson) ?? new Dictionary<string, double>(),
                    row.VoiceSafetyViolation,
                    row.VoiceSafetyConfidence);
    }

    /// <summary>
    /// Новые записи журнала движка — строками в контекст базы; итог Рейса и момент показа следующего Шага — в запись Рейса.
    /// Сохраняет вызывающий.
    /// </summary>
    private async Task AppendToJournalAsync(TripRecord record, TripState before, TripState after, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var elapsedMs = (int)(now - record.StepStartedAt).TotalMilliseconds;
        var seq = before.Journal.Count;
        foreach (var entry in after.Journal.Skip(before.Journal.Count))
        {
            var knowledgeDelta = entry is Decision decision
                ? await scoring.ApplyTripDecisionAsync(record.UserId, before, decision, now, cancellationToken)
                : 0;
            dbContext.TripJournal.Add(JournalRow(record.Id, ++seq, entry, elapsedMs, now, knowledgeDelta));
        }

        record.Status = Code(after.Status);
        record.FailureCause = after.Failure is { } failure ? Code(failure.Cause) : null;
        record.FailureScale = after.Failure?.Scale;
        record.FinishedAt = after.IsFinished ? now : record.FinishedAt;
        if (before.Current != after.Current || before.Phase != after.Phase || before.Status != after.Status)
            record.StepStartedAt = now;
    }

    private static TripJournalRecord JournalRow(Guid tripId, int seq, JournalEntry entry, int elapsedMs, DateTimeOffset now,
        int knowledgeDelta) => entry switch
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
            KnowledgeDelta = knowledgeDelta,
            ScaleChanges = JsonSerializer.Serialize(decision.Changes, SnapshotJson),
            FlagsSet = JsonSerializer.Serialize(decision.FlagsSet, SnapshotJson),
            CriticalError = decision.CriticalError, ToStepId = decision.To,
        },
        VoiceAttempt attempt => new()
        {
            TripId = tripId, Seq = seq, CreatedAt = now, Kind = TripJournalKinds.VoiceAttempt,
            EventId = attempt.EventId, EventVersion = attempt.EventVersion, StepId = attempt.StepId,
            VoiceTranscript = attempt.Transcript, VoiceChoice = attempt.Choice,
            VoiceConfidence = attempt.Confidence, VoiceLatencyMs = attempt.LatencyMs,
            VoiceApplied = attempt.Applied, VoiceError = attempt.ErrorCode,
            VoiceRequestId = attempt.ProviderRequestId, VoiceAttemptId = attempt.AttemptId,
            VoicePassengerReply = attempt.PassengerReply, VoiceReplyError = attempt.ReplyError,
            VoiceScore = attempt.Assessment?.Score, VoiceScoreConfidence = attempt.Assessment?.ScoreConfidence,
            VoiceRoleStages = attempt.Assessment is null ? null : JsonSerializer.Serialize(attempt.Assessment.RoleStages, SnapshotJson),
            VoiceSafetyViolation = attempt.Assessment?.SafetyViolation,
            VoiceSafetyConfidence = attempt.Assessment?.SafetyConfidence,
            VoiceSttLatencyMs = attempt.SttLatencyMs, VoiceLayaLatencyMs = attempt.LayaLatencyMs,
            VoiceLlmLatencyMs = attempt.LlmLatencyMs,
            ElapsedMs = elapsedMs,
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
