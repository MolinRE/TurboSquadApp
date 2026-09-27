using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Swipes;

/// <summary>
/// Смена на свайпах через API (контракт #24, правила #26): колода из базы, время по часам сервера, ответы в базе.
/// Вердикт и Шкалы считают правила (ShiftPlay), клиент только показывает. Состояние Смены отдельно не хранится:
/// правила заново проигрывают записанные ответы на колоде и снимке Шкал со старта.
/// </summary>
public sealed class SwipeShiftService(AppDbContext dbContext, TimeProvider clock, Random random)
{
    private static readonly JsonSerializerOptions SnapshotJson = JsonSerializerOptions.Web;

    /// <summary>Как в записи ответа отмечено «Время вышло».</summary>
    private const string TimedOutAnswer = "timeout";

    public async Task<IResult> StartAsync(Guid userId, ShiftMode mode, CancellationToken cancellationToken)
    {
        var published = await dbContext.Questions
            .Where(q => q.Type == QuestionTypes.Swipe && q.Status == QuestionStatuses.Published)
            .OrderBy(q => q.Id)
            .Select(q => q.Id)
            .ToArrayAsync(cancellationToken);
        if (published.Length == 0) return Rejected("NoPublishedQuestions", "Нет опубликованных Вопросов-свайпов");

        random.Shuffle(published);
        return await CreateAsync(userId, mode, mode == ShiftMode.Woodpecker ? 1 : null, null,
            published.Take(ShiftTiming.DeckSize).ToList(), cancellationToken);
    }

    public async Task<IResult> GetAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        return Results.Ok(await ViewAsync(record, play, cancellationToken));
    }

    /// <summary>Показать следующую карточку: с этого момента идёт время на неё. Повторный вызов её не меняет.</summary>
    public async Task<IResult> ShowNextCardAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        if (play.Status == ShiftStatus.Running && record.CardShownAt is null)
        {
            record.CardShownAt = clock.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return Results.Ok(await ViewAsync(record, play, cancellationToken));
    }

    /// <param name="questionId">Карточка, на которую отвечает проводник: ответ на уже отвеченную отклоняется.</param>
    public async Task<IResult> AnswerAsync(
        Guid userId, Guid shiftId, string questionId, SwipeAnswer answer, CancellationToken cancellationToken)
    {
        var answeredAt = clock.GetUtcNow();
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        if (RejectAnswer(record, play, questionId) is { } rejection) return rejection;

        // Ответ позже лимита больше чем на допуск сети — «Время вышло», как у таймера Рейса.
        var elapsedMs = ElapsedMs(record, play, answeredAt);
        if (TimeLimitMs(record) is { } limitMs && elapsedMs > limitMs + TimeoutToleranceMs)
            return await SettleAsync(record, play, SwipeAnswer.Unknown, timedOut: true, limitMs, answeredAt, cancellationToken);
        return await SettleAsync(record, play, answer, timedOut: false, elapsedMs, answeredAt, cancellationToken);
    }

    /// <summary>
    /// Время вышло: засчитывается как «Не знаю». Сервер сверяет лимит по своим часам: раньше лимита
    /// больше чем на допуск сети — отказ.
    /// </summary>
    public async Task<IResult> TimeOutAsync(Guid userId, Guid shiftId, string questionId, CancellationToken cancellationToken)
    {
        var answeredAt = clock.GetUtcNow();
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        if (RejectAnswer(record, play, questionId) is { } rejection) return rejection;
        if (TimeLimitMs(record) is not { } limitMs) return Rejected("NoTimeLimit", "«В своём темпе» лимита на карточку нет");
        if (ElapsedMs(record, play, answeredAt) < limitMs - TimeoutToleranceMs)
            return Rejected("TimeNotExpired", "Время на карточку ещё не вышло");
        return await SettleAsync(record, play, SwipeAnswer.Unknown, timedOut: true, limitMs, answeredAt, cancellationToken);
    }

    /// <summary>Следующий Цикл «На скорость»: та же колода в новом порядке, лимит короче, Шкалы с начала.</summary>
    public async Task<IResult> StartNextCycleAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        if (record.Cycle is not { } cycle || play.Status == ShiftStatus.Running || cycle >= ShiftTiming.Cycles.Count)
            return Rejected("NoNextCycle", "Следующий Цикл — после законченного Цикла «На скорость», кроме последнего");

        var deck = play.Deck.Select(question => question.Id).ToArray();
        random.Shuffle(deck);
        return await CreateAsync(userId, ShiftMode.Woodpecker, cycle + 1, record.Id, deck, cancellationToken);
    }

    /// <summary>Работа над ошибками: новая Смена «В своём темпе» из Вопросов с ошибкой или «Не знаю», в том числе после Срыва.</summary>
    public async Task<IResult> StartWorkOnMistakesAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        if (play.Status == ShiftStatus.Running) return Rejected("ShiftNotFinished", "Работа над ошибками — после Смены: Смена ещё идёт");
        if (play.Mistakes.Count == 0) return Rejected("NoMistakes", "В Смене не было ошибок и «Не знаю»");

        var deck = play.Mistakes.Select(question => question.Id).ToArray();
        random.Shuffle(deck);
        return await CreateAsync(userId, ShiftMode.Calm, null, null, deck, cancellationToken);
    }

    private static IResult? RejectAnswer(SwipeShiftRecord record, ShiftPlay play, string questionId)
    {
        if (play.Current is not { } card) return Rejected("ShiftNotRunning", "Смена уже закончена");
        if (card.Question.Id != questionId) return Rejected("StaleCard", "На эту карточку уже ответили");
        if (record.CardShownAt is null) return Rejected("CardNotShown", "Карточку ещё не показали: сначала next-card");
        return null;
    }

    private static readonly int TimeoutToleranceMs = (int)TripService.TimerTolerance.TotalMilliseconds;

    private static int? TimeLimitMs(SwipeShiftRecord record) => ShiftTiming.Of(record.Cycle)?.TimeLimitMs;

    /// <summary>Время ответа — от конца печати формулировки: длинный Вопрос не даёт форы короткому.</summary>
    private static int ElapsedMs(SwipeShiftRecord record, ShiftPlay play, DateTimeOffset answeredAt)
    {
        var readingMs = ShiftTiming.ReadingMs(play.Current!.Question.Statement, record.Cycle);
        return Math.Max(0, (int)(answeredAt - record.CardShownAt!.Value).TotalMilliseconds - readingMs);
    }

    private async Task<IResult> SettleAsync(
        SwipeShiftRecord record, ShiftPlay play, SwipeAnswer answer, bool timedOut, int elapsedMs, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settled = play.Settle(answer, timedOut, elapsedMs);
        dbContext.SwipeAnswers.Add(new SwipeAnswerRecord
        {
            ShiftId = record.Id, Seq = play.Answers.Count, QuestionId = settled.Question.Id,
            Answer = timedOut ? TimedOutAnswer : Code(answer), Verdict = Code(settled.Verdict), IsRepeat = settled.IsRepeat,
            ElapsedMs = elapsedMs, ScaleChanges = JsonSerializer.Serialize(settled.ScaleChanges, SnapshotJson), AnsweredAt = now,
        });
        record.CardShownAt = null;
        record.Status = Code(play.Status);
        record.FailureScale = play.FailedScale;
        if (play.Status != ShiftStatus.Running) record.FinishedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new AnswerOutcomeView(
            settled.Verdict, settled.Question.Options.Correct, settled.Question.Explanation, settled.ScaleChanges,
            elapsedMs, timedOut, await ViewAsync(record, play, cancellationToken)));
    }

    /// <summary>Новая Смена: первая карточка показана сразу, Шкалы — с начала по текущему справочнику.</summary>
    private async Task<IResult> CreateAsync(
        Guid userId, ShiftMode mode, int? cycle, Guid? previousCycleId, IReadOnlyList<string> deck,
        CancellationToken cancellationToken)
    {
        var scales = await dbContext.Scales.OrderBy(s => s.Code)
            .Select(s => new ScaleDefinition(s.Code, s.Name, s.Min, s.Max, s.Start, s.FailureThreshold, s.Mandatory, s.FailureReason))
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var record = new SwipeShiftRecord
        {
            Id = Guid.NewGuid(), UserId = userId, Mode = Code(mode), Cycle = cycle, PreviousCycleId = previousCycleId,
            Status = Code(ShiftStatus.Running), Deck = JsonSerializer.Serialize(deck, SnapshotJson),
            Scales = JsonSerializer.Serialize(scales, SnapshotJson), StartedAt = now, CardShownAt = now,
        };
        dbContext.SwipeShifts.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        var play = await ReplayAsync(record, cancellationToken);
        return Results.Created($"/api/swipe-shifts/{record.Id}", await ViewAsync(record, play, cancellationToken));
    }

    private async Task<ShiftStateView> ViewAsync(SwipeShiftRecord record, ShiftPlay play, CancellationToken cancellationToken) =>
        ShiftStateView.Of(record, play, await PreviousCyclesAsync(record, cancellationToken));

    /// <summary>Итоги прошлых Циклов той же колоды, от первого.</summary>
    private async Task<IReadOnlyList<CycleResultView>> PreviousCyclesAsync(SwipeShiftRecord record, CancellationToken cancellationToken)
    {
        var results = new List<CycleResultView>();
        for (var previousId = record.PreviousCycleId; previousId is { } id;)
        {
            var previous = await dbContext.SwipeShifts.SingleAsync(shift => shift.Id == id, cancellationToken);
            results.Insert(0, CycleResultView.Of(previous.Cycle!.Value, await ReplayAsync(previous, cancellationToken)));
            previousId = previous.PreviousCycleId;
        }
        return results;
    }

    private async Task<(SwipeShiftRecord Record, ShiftPlay Play)?> LoadAsync(
        Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        var record = await dbContext.SwipeShifts.SingleOrDefaultAsync(
            shift => shift.Id == shiftId && shift.UserId == userId, cancellationToken);
        return record is null ? null : (record, await ReplayAsync(record, cancellationToken));
    }

    /// <summary>Проигрывает записанные ответы Смены на её колоде и снимке Шкал.</summary>
    private async Task<ShiftPlay> ReplayAsync(SwipeShiftRecord record, CancellationToken cancellationToken)
    {
        var deckIds = JsonSerializer.Deserialize<List<string>>(record.Deck, SnapshotJson)!;
        var questions = await dbContext.Questions.Where(q => deckIds.Contains(q.Id)).ToListAsync(cancellationToken);
        var classNames = await dbContext.ServiceClasses.ToDictionaryAsync(c => c.Code, c => c.Name, cancellationToken);
        var deck = deckIds.Select(id => ShiftQuestionOf(questions.Single(q => q.Id == id), classNames)).ToList();
        var play = new ShiftPlay(
            Enum.Parse<ShiftMode>(record.Mode, ignoreCase: true), deck,
            JsonSerializer.Deserialize<List<ScaleDefinition>>(record.Scales, SnapshotJson)!);

        var answers = await dbContext.SwipeAnswers.Where(a => a.ShiftId == record.Id).OrderBy(a => a.Seq).ToListAsync(cancellationToken);
        foreach (var row in answers)
        {
            if (play.Current?.Question.Id != row.QuestionId)
                throw new InvalidOperationException($"Ответы Смены {record.Id} не проигрываются: ответ {row.Seq} не на текущую карточку");
            var timedOut = row.Answer == TimedOutAnswer;
            play.Settle(timedOut ? SwipeAnswer.Unknown : Enum.Parse<SwipeAnswer>(row.Answer, ignoreCase: true), timedOut, row.ElapsedMs);
        }
        return play;
    }

    private static ShiftQuestion ShiftQuestionOf(QuestionRecord record, IReadOnlyDictionary<string, string> classNames) => new(
        record.Id, record.Statement, JsonSerializer.Deserialize<SwipeOptions>(record.Options, EventJson.Options)!,
        new Explanation(record.ExplanationText, record.ExplanationKeyFact, record.Source), record.Topic,
        JsonSerializer.Deserialize<List<string>>(record.ServiceClasses)!
            .Select(code => new ServiceClassRef(code, classNames.GetValueOrDefault(code, code)))
            .ToList());

    /// <summary>Значение перечисления так же, как в JSON API: calm, running, correct.</summary>
    private static string Code(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static IResult Rejected(string reason, string message) => Results.Problem(
        title: "Действие отклонено", detail: message, statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["reason"] = reason });
}
