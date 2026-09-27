using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;
using TurboSquadApp.Scoring;
using TurboSquadApp.Achievements;

namespace TurboSquadApp.Swipes;

/// <summary>
/// Смена на свайпах через API (контракт #24, правила #26): колода из базы, время по часам сервера, ответы в базе.
/// Вердикт и Шкалы считают правила (ShiftPlay), клиент только показывает. Состояние Смены отдельно не хранится:
/// правила заново проигрывают записанные ответы на колоде и снимке Шкал со старта.
/// </summary>
public sealed class SwipeShiftService(AppDbContext dbContext, TimeProvider clock, Random random,
    KnowledgeScoringService scoring, AchievementService achievements)
{
    /// <summary>JSON колонок jsonb Смены и ответов: колода, снимок Шкал, изменения Шкал.</summary>
    private static readonly JsonSerializerOptions ColumnJson = JsonSerializerOptions.Web;

    /// <summary>Как в записи ответа отмечено «Время вышло».</summary>
    private const string TimedOutAnswer = "timeout";

    public async Task<IResult> StartAsync(Guid userId, ShiftMode mode, CancellationToken cancellationToken)
    {
        var published = await dbContext.Questions
            .Where(q => q.Type == QuestionTypes.Swipe)
            .OrderBy(q => q.Id)
            .Select(q => q.Id)
            .ToArrayAsync(cancellationToken);
        random.Shuffle(published);
        return await CreateAsync(userId, mode == ShiftMode.Woodpecker ? 1 : null, null, published, cancellationToken);
    }

    public async Task<IResult> GetAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        return Results.Ok(await ViewAsync(record, play, cancellationToken));
    }

    public async Task<IResult> ListDebriefsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var history = await dbContext.SwipeShifts
            .Where(shift => shift.UserId == userId && shift.FinishedAt != null)
            .OrderByDescending(shift => shift.FinishedAt)
            .Select(shift => new SwipeDebriefListItem(
                shift.Id, shift.Status, shift.Cycle, shift.StartedAt, shift.FinishedAt!.Value))
            .ToListAsync(cancellationToken);
        return Results.Ok(history);
    }

    public async Task<IResult> DebriefAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        var record = await dbContext.SwipeShifts.SingleOrDefaultAsync(
            shift => shift.Id == shiftId && shift.UserId == userId, cancellationToken);
        if (record is null) return Results.NotFound();
        if (record.FinishedAt is null)
            return Rejected("ShiftNotFinished", "Разбор строится после Смены: Смена ещё не закончена");

        var rows = await dbContext.SwipeAnswers.Where(answer => answer.ShiftId == shiftId)
            .OrderBy(answer => answer.Seq).ToListAsync(cancellationToken);
        var answers = new List<SwipeDebriefAnswer>();
        foreach (var row in rows)
        {
            var snapshot = row.QuestionSnapshot is { } json
                ? JsonSerializer.Deserialize<SwipeAnswerSnapshot>(json, ColumnJson)!
                : SnapshotOf(ShiftQuestionOf(
                    await dbContext.Questions.SingleAsync(q => q.Id == row.QuestionId, cancellationToken),
                    new Dictionary<string, string>()));
            answers.Add(new SwipeDebriefAnswer(
                row.Seq, row.QuestionId, snapshot.Statement, snapshot.RightLabel, snapshot.LeftLabel,
                snapshot.CorrectSide, row.Answer, row.Verdict, row.IsRepeat, row.ElapsedMs, row.KnowledgeDelta,
                JsonSerializer.Deserialize<Dictionary<string, int>>(row.ScaleChanges, ColumnJson)!, snapshot.Explanation));
        }
        var scaleNames = JsonSerializer.Deserialize<List<ScaleDefinition>>(record.Scales, ColumnJson)!
            .ToDictionary(scale => scale.Code, scale => scale.Name);
        return Results.Ok(new SwipeDebrief(record.Id, record.Status, record.FailureScale, record.Cycle, scaleNames, answers));
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
        if (TimeLimitMs(play) is { } limitMs && elapsedMs > limitMs + ShiftTiming.TimeoutToleranceMs)
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
        if (TimeLimitMs(play) is not { } limitMs) return Rejected("NoTimeLimit", "«В своём темпе» лимита на карточку нет");
        if (ElapsedMs(record, play, answeredAt) < limitMs - ShiftTiming.TimeoutToleranceMs)
            return Rejected("TimeNotExpired", "Время на карточку ещё не вышло");
        return await SettleAsync(record, play, SwipeAnswer.Unknown, timedOut: true, limitMs, answeredAt, cancellationToken);
    }

    /// <summary>Следующий Цикл «На скорость»: та же колода в новом порядке, лимит короче, Шкалы с начала.</summary>
    public async Task<IResult> StartNextCycleAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, shiftId, cancellationToken) is not { } shift) return Results.NotFound();
        var (record, play) = shift;
        if (play.Cycle is not { } cycle || play.Status == ShiftStatus.Running || cycle >= ShiftTiming.Cycles.Count)
            return Rejected("NoNextCycle", "Следующий Цикл — после законченного Цикла «На скорость», кроме последнего");

        var deck = play.Deck.Select(question => question.Id).ToArray();
        random.Shuffle(deck);
        return await CreateAsync(userId, cycle + 1, record.Id, deck, cancellationToken);
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
        return await CreateAsync(userId, null, null, deck, cancellationToken);
    }

    private static IResult? RejectAnswer(SwipeShiftRecord record, ShiftPlay play, string questionId)
    {
        if (play.Current is not { } card) return Rejected("ShiftNotRunning", "Смена уже закончена");
        if (card.Question.Id != questionId) return Rejected("StaleCard", "На эту карточку уже ответили");
        if (record.CardShownAt is null) return Rejected("CardNotShown", "Карточку ещё не показали: сначала next-card");
        return null;
    }

    private static int? TimeLimitMs(ShiftPlay play) => ShiftTiming.ForCycle(play.Cycle)?.TimeLimitMs;

    /// <summary>Время ответа — от конца печати формулировки: длинный Вопрос не даёт форы короткому.</summary>
    private static int ElapsedMs(SwipeShiftRecord record, ShiftPlay play, DateTimeOffset answeredAt)
    {
        var readingMs = ShiftTiming.ReadingMs(play.Current!.Question.Statement, play.Cycle);
        return Math.Max(0, (int)(answeredAt - record.CardShownAt!.Value).TotalMilliseconds - readingMs);
    }

    private async Task<IResult> SettleAsync(
        SwipeShiftRecord record, ShiftPlay play, SwipeAnswer answer, bool timedOut, int elapsedMs, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settled = play.Settle(answer, timedOut, elapsedMs);
        var knowledgeDelta = await scoring.ApplyAsync(record.UserId, KnowledgeUnit.Question(settled.Question.Id),
            settled.Verdict == Verdict.Correct, settled.Question.KnowledgeCost, now, cancellationToken);
        dbContext.SwipeAnswers.Add(new SwipeAnswerRecord
        {
            ShiftId = record.Id, Seq = play.Answers.Count, QuestionId = settled.Question.Id,
            Answer = timedOut ? TimedOutAnswer : Code(answer), Verdict = Code(settled.Verdict), IsRepeat = settled.IsRepeat,
            ElapsedMs = elapsedMs, ScaleChanges = JsonSerializer.Serialize(settled.ScaleChanges, ColumnJson), AnsweredAt = now,
            KnowledgeDelta = knowledgeDelta,
            QuestionSnapshot = JsonSerializer.Serialize(SnapshotOf(settled.Question), ColumnJson),
        });
        record.CardShownAt = null;
        record.Status = Code(play.Status);
        record.FailureScale = play.FailedScale;
        if (play.Status != ShiftStatus.Running) record.FinishedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await achievements.EvaluateAsync(record.UserId, cancellationToken);

        return Results.Ok(new AnswerOutcomeView(
            settled.Verdict, settled.Question.Options.Correct, settled.Question.Explanation, settled.ScaleChanges,
            elapsedMs, timedOut, await ViewAsync(record, play, cancellationToken)));
    }

    /// <summary>
    /// Новая Смена: из колоды остаются только опубликованные Вопросы, не больше DeckSize; первая карточка
    /// показана сразу, Шкалы — с начала по текущему справочнику.
    /// </summary>
    /// <param name="cycle">Номер Цикла «На скорость»; null — «В своём темпе».</param>
    private async Task<IResult> CreateAsync(
        Guid userId, int? cycle, Guid? previousCycleId, IReadOnlyList<string> candidates, CancellationToken cancellationToken)
    {
        var published = await dbContext.Questions
            .Where(q => candidates.Contains(q.Id) && q.Status == QuestionStatuses.Published)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken);
        var deck = candidates.Where(published.Contains).Take(ShiftTiming.DeckSize).ToList();
        if (deck.Count == 0) return Rejected("NoPublishedQuestions", "В колоду некого взять: нет опубликованных Вопросов");

        var scales = await dbContext.Scales.OrderBy(s => s.Code)
            .Select(s => new ScaleDefinition(s.Code, s.Name, s.Min, s.Max, s.Start, s.FailureThreshold, s.Mandatory, s.FailureReason))
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var record = new SwipeShiftRecord
        {
            Id = Guid.NewGuid(), UserId = userId, Mode = Code(cycle is null ? ShiftMode.Calm : ShiftMode.Woodpecker),
            Cycle = cycle, PreviousCycleId = previousCycleId,
            Status = Code(ShiftStatus.Running), Deck = JsonSerializer.Serialize(deck, ColumnJson),
            Scales = JsonSerializer.Serialize(scales, ColumnJson), StartedAt = now, CardShownAt = now,
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

    /// <summary>Проигрывает записанные ответы Смены — их вердикты и изменения Шкал — на её колоде и снимке Шкал.</summary>
    private async Task<ShiftPlay> ReplayAsync(SwipeShiftRecord record, CancellationToken cancellationToken)
    {
        var deckIds = JsonSerializer.Deserialize<List<string>>(record.Deck, ColumnJson)!;
        var questions = await dbContext.Questions.Where(q => deckIds.Contains(q.Id)).ToListAsync(cancellationToken);
        var classNames = await dbContext.ServiceClasses.ToDictionaryAsync(c => c.Code, c => c.Name, cancellationToken);
        var deck = deckIds.Select(id => ShiftQuestionOf(questions.Single(q => q.Id == id), classNames)).ToList();
        var play = new ShiftPlay(record.Cycle, deck, JsonSerializer.Deserialize<List<ScaleDefinition>>(record.Scales, ColumnJson)!);

        var answers = await dbContext.SwipeAnswers.Where(a => a.ShiftId == record.Id).OrderBy(a => a.Seq).ToListAsync(cancellationToken);
        foreach (var row in answers)
        {
            if (play.Current?.Question.Id != row.QuestionId)
                throw new InvalidOperationException($"Ответы Смены {record.Id} не проигрываются: ответ {row.Seq} не на текущую карточку");
            var timedOut = row.Answer == TimedOutAnswer;
            play.Replay(
                timedOut ? SwipeAnswer.Unknown : Enum.Parse<SwipeAnswer>(row.Answer, ignoreCase: true), timedOut,
                Enum.Parse<Verdict>(row.Verdict, ignoreCase: true), row.ElapsedMs,
                JsonSerializer.Deserialize<Dictionary<string, int>>(row.ScaleChanges, ColumnJson)!);
        }
        return play;
    }

    private static ShiftQuestion ShiftQuestionOf(QuestionRecord record, IReadOnlyDictionary<string, string> classNames) => new(
        record.Id, record.Statement, (SwipeOptions)record.ReadOptions(),
        new Explanation(record.ExplanationText, record.ExplanationKeyFact, record.Source), record.Topic,
        JsonSerializer.Deserialize<List<string>>(record.ServiceClasses)!
            .Select(code => new ServiceClassRef(code, classNames.GetValueOrDefault(code, code)))
        .ToList(), record.KnowledgeCost);

    private static SwipeAnswerSnapshot SnapshotOf(ShiftQuestion question) => new(
        question.Statement, question.Options.Right.Label, question.Options.Left.Label,
        Code(question.Options.Correct), question.Explanation);

    /// <summary>Значение перечисления так же, как в JSON API: calm, running, correct.</summary>
    private static string Code(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static IResult Rejected(string reason, string message) => Results.Problem(
        title: "Действие отклонено", detail: message, statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["reason"] = reason });
}
