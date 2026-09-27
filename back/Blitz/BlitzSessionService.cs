using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Questions;
using TurboSquadApp.Swipes;

namespace TurboSquadApp.Blitz;

/// <summary>
/// Блиц через API (#41–#43): колода — снимок опубликованных Вопросов single, multiple и sequence на старте, время по часам
/// сервера, ответы в базе. Вердикт считает сервер, клиент только показывает; верные варианты уходят клиенту лишь в ответе на Вопрос.
/// multiple верен только за точный набор верных вариантов в любом порядке, sequence — только за полный верный порядок шагов:
/// частичного зачёта нет.
/// </summary>
public sealed class BlitzSessionService(AppDbContext dbContext, TimeProvider clock, Random random)
{
    public const int DeckSize = 10;

    /// <summary>Типы Вопросов, которые играются в Блице.</summary>
    private static readonly string[] PlayableTypes = [QuestionTypes.Single, QuestionTypes.Multiple, QuestionTypes.Sequence];

    /// <summary>Лимит на ответ, если у Вопроса своего нет.</summary>
    public const int DefaultTimeLimitMs = 20_000;

    /// <summary>Допуск на сеть: столько сервер прощает клиенту в обе стороны от лимита.</summary>
    public const int TimeoutToleranceMs = 1_000;

    /// <summary>JSON колонок jsonb: снимок колоды и выбранные варианты.</summary>
    private static readonly JsonSerializerOptions ColumnJson = JsonSerializerOptions.Web;

    public async Task<IResult> StartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var published = await dbContext.Questions
            .Where(q => PlayableTypes.Contains(q.Type) && q.Status == QuestionStatuses.Published)
            .OrderBy(q => q.Id)
            .ToArrayAsync(cancellationToken);
        if (published.Length == 0) return Rejected("NoPublishedQuestions", "В Блиц некого взять: нет опубликованных Вопросов с выбором ответа");
        random.Shuffle(published);

        var now = clock.GetUtcNow();
        var record = new BlitzSessionRecord
        {
            Id = Guid.NewGuid(), UserId = userId, Status = Code(BlitzStatus.Running),
            Deck = JsonSerializer.Serialize(published.Take(DeckSize).Select(Snapshot).ToList(), ColumnJson),
            StartedAt = now, QuestionShownAt = now,
        };
        dbContext.BlitzSessions.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/blitz-sessions/{record.Id}", View(new BlitzPlay(record, [])));
    }

    public async Task<IResult> GetAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        await LoadAsync(userId, sessionId, cancellationToken) is { } play ? Results.Ok(View(play)) : Results.NotFound();

    /// <summary>Показать следующий Вопрос: с этого момента идёт время на него. Повторный вызов его не меняет.</summary>
    public async Task<IResult> ShowNextQuestionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(userId, sessionId, cancellationToken) is not { } play) return Results.NotFound();
        if (play.Current is not null && play.Record.QuestionShownAt is null)
        {
            play.Record.QuestionShownAt = clock.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return Results.Ok(View(play));
    }

    /// <summary>selectedOptionIds — ответ на single и multiple, orderedStepIds — на sequence; второй список не читается.</summary>
    public async Task<IResult> AnswerAsync(
        Guid userId, Guid sessionId, string questionId, IReadOnlyList<string> selectedOptionIds, IReadOnlyList<string> orderedStepIds,
        CancellationToken cancellationToken)
    {
        var answeredAt = clock.GetUtcNow();
        if (await LoadAsync(userId, sessionId, cancellationToken) is not { } play) return Results.NotFound();
        if (RejectAnswer(play, questionId) is { } rejection) return rejection;
        var question = play.Current!;
        var answerIds = question.Type == QuestionTypes.Sequence ? orderedStepIds : selectedOptionIds;
        if (InvalidSelection(question, answerIds) is { } invalid) return invalid;

        // Ответ позже лимита больше чем на допуск сети — «Время вышло», как у таймера Рейса и Смены.
        var elapsedMs = ElapsedMs(play, answeredAt);
        if (elapsedMs > question.TimeLimitMs + TimeoutToleranceMs)
            return await SettleAsync(play, [], timedOut: true, question.TimeLimitMs, answeredAt, cancellationToken);
        return await SettleAsync(play, answerIds, timedOut: false, elapsedMs, answeredAt, cancellationToken);
    }

    /// <summary>Время вышло: засчитывается как «Не знаю». Раньше лимита больше чем на допуск сети — отказ.</summary>
    public async Task<IResult> TimeOutAsync(Guid userId, Guid sessionId, string questionId, CancellationToken cancellationToken)
    {
        var answeredAt = clock.GetUtcNow();
        if (await LoadAsync(userId, sessionId, cancellationToken) is not { } play) return Results.NotFound();
        if (RejectAnswer(play, questionId) is { } rejection) return rejection;
        var limitMs = play.Current!.TimeLimitMs;
        if (ElapsedMs(play, answeredAt) < limitMs - TimeoutToleranceMs)
            return Rejected("TimeNotExpired", "Время на Вопрос ещё не вышло");
        return await SettleAsync(play, [], timedOut: true, limitMs, answeredAt, cancellationToken);
    }

    private static IResult? RejectAnswer(BlitzPlay play, string questionId)
    {
        if (play.Current is not { } question) return Rejected("SessionNotRunning", "Блиц уже закончен");
        if (question.Id != questionId) return Rejected("StaleQuestion", "На этот Вопрос уже ответили");
        if (play.Record.QuestionShownAt is null) return Rejected("QuestionNotShown", "Вопрос ещё не показали: сначала next-question");
        return null;
    }

    /// <summary>
    /// Выбор, который нельзя проверить: не варианты этого Вопроса, повтор варианта, пустой выбор, у single — не ровно один,
    /// у sequence — не все шаги. Неполный или лишний набор у multiple и неверный порядок у sequence — не отказ, а «неверно».
    /// </summary>
    private static IResult? InvalidSelection(BlitzQuestion question, IReadOnlyList<string> selectedOptionIds)
    {
        var validIds = selectedOptionIds.All(id => question.Options.Any(option => option.Id == id))
            && selectedOptionIds.Distinct().Count() == selectedOptionIds.Count;
        if (question.Type == QuestionTypes.Sequence && (selectedOptionIds.Count != question.Options.Count || !validIds))
            return Rejected("InvalidSelection", "Расставьте по порядку все шаги этого Вопроса, каждый один раз");
        if (question.Type == QuestionTypes.Single && (selectedOptionIds.Count != 1 || !validIds))
            return Rejected("InvalidSelection", "Для Вопроса с одним ответом выберите ровно один из его вариантов");
        if (selectedOptionIds.Count == 0 || !validIds)
            return Rejected("InvalidSelection", "Отметьте хотя бы один вариант этого Вопроса, каждый не больше раза");
        return null;
    }

    private static int ElapsedMs(BlitzPlay play, DateTimeOffset answeredAt) =>
        Math.Max(0, (int)(answeredAt - play.Record.QuestionShownAt!.Value).TotalMilliseconds);

    private async Task<IResult> SettleAsync(
        BlitzPlay play, IReadOnlyList<string> selectedOptionIds, bool timedOut, int elapsedMs, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var question = play.Current!;
        var verdict = timedOut ? Verdict.Unknown : question.IsCorrect(selectedOptionIds) ? Verdict.Correct : Verdict.Wrong;
        var answer = new BlitzAnswerRecord
        {
            SessionId = play.Record.Id, Seq = play.Answers.Count, QuestionId = question.Id,
            SelectedOptionIds = JsonSerializer.Serialize(selectedOptionIds, ColumnJson), TimedOut = timedOut,
            Verdict = Code(verdict), ElapsedMs = elapsedMs, AnsweredAt = now,
        };
        dbContext.BlitzAnswers.Add(answer);
        play = new BlitzPlay(play.Record, [.. play.Answers, answer]);
        play.Record.QuestionShownAt = null;
        if (play.Current is null)
        {
            play.Record.Status = Code(BlitzStatus.Finished);
            play.Record.FinishedAt = now;
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Параллельный ответ на тот же Вопрос успел раньше: второго результата нет.
            return Rejected("StaleQuestion", "На этот Вопрос уже ответили");
        }

        return Results.Ok(new BlitzAnswerOutcomeView(
            verdict, timedOut, question.CorrectOptionIds, question.Explanation, elapsedMs, View(play)));
    }

    private async Task<BlitzPlay?> LoadAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var record = await dbContext.BlitzSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId && session.UserId == userId, cancellationToken);
        if (record is null) return null;
        var answers = await dbContext.BlitzAnswers.Where(a => a.SessionId == sessionId).OrderBy(a => a.Seq).ToListAsync(cancellationToken);
        return new BlitzPlay(record, answers);
    }

    private static BlitzSessionView View(BlitzPlay play)
    {
        var deck = play.Deck;
        var verdicts = play.Answers.Select(a => Enum.Parse<Verdict>(a.Verdict, ignoreCase: true)).ToList();
        var question = play.Current is { } current && play.Record.QuestionShownAt is not null ? BlitzQuestionView.Of(current) : null;
        BlitzResultView? result = null;
        if (play.Current is null)
        {
            var mistakes = play.Answers.Zip(verdicts)
                .Where(pair => pair.Second != Verdict.Correct)
                .Select(pair => deck.Single(q => q.Id == pair.First.QuestionId))
                .Select(q => new MistakeView(q.Id, q.Statement, q.Explanation))
                .ToList();
            result = new BlitzResultView(
                verdicts.Count(v => v == Verdict.Correct), deck.Count,
                play.Answers.Count == 0 ? null : (int)play.Answers.Average(a => a.ElapsedMs), mistakes);
        }
        return new BlitzSessionView(
            play.Record.Id, play.Current is null ? BlitzStatus.Finished : BlitzStatus.Running,
            new BlitzProgressView(play.Answers.Count, deck.Count, verdicts), question, result);
    }

    private BlitzQuestion Snapshot(QuestionRecord record)
    {
        var explanation = new Explanation(record.ExplanationText, record.ExplanationKeyFact, record.Source);
        var timeLimitMs = record.TimeLimitSec is { } seconds ? seconds * 1000 : DefaultTimeLimitMs;
        var recordOptions = record.ReadOptions();
        if (recordOptions is ChoiceOptions choice)
            return new(record.Id, record.Type, record.Statement, record.Topic, choice.Options, explanation, timeLimitMs);

        // Шаги в верном порядке перемешиваются так, чтобы порядок показа не совпал с верным, и получают id по месту показа:
        // в сидах и CMS id шагов идут по алфавиту в верном порядке и выдали бы ответ.
        var steps = ((SequenceOptions)recordOptions).Steps.ToArray();
        var shown = steps.ToArray();
        do random.Shuffle(shown);
        while (shown.SequenceEqual(steps));
        var options = shown.Select((step, at) => new ChoiceOption(((char)('a' + at)).ToString(), step.Text, Correct: false)).ToList();
        var correctOrder = steps.Select(step => options[Array.IndexOf(shown, step)].Id).ToList();
        return new(record.Id, record.Type, record.Statement, record.Topic, options, explanation, timeLimitMs, correctOrder);
    }

    /// <summary>Значение перечисления так же, как в JSON API: running, correct.</summary>
    private static string Code(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static IResult Rejected(string reason, string message) => Results.Problem(
        title: "Действие отклонено", detail: message, statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["reason"] = reason });

    /// <summary>Сессия с ответами по порядку; текущий Вопрос — первый в колоде без ответа.</summary>
    private sealed record BlitzPlay(BlitzSessionRecord Record, IReadOnlyList<BlitzAnswerRecord> Answers)
    {
        public IReadOnlyList<BlitzQuestion> Deck { get; } = JsonSerializer.Deserialize<List<BlitzQuestion>>(Record.Deck, ColumnJson)!;

        public BlitzQuestion? Current => Answers.Count < Deck.Count ? Deck[Answers.Count] : null;
    }
}
