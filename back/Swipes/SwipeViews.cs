using TurboSquadApp.Data;
using TurboSquadApp.Questions;

namespace TurboSquadApp.Swipes;

// Ответы API Смены на свайпах — поле в поле как front/src/lib/swipes/contract.ts (#24).

public sealed record ShiftStateView(
    Guid ShiftId, ShiftMode Mode, CycleInfoView? Cycle, ShiftStatus Status, IReadOnlyList<ShiftScaleView> Scales,
    ShiftProgressView Progress, ShiftCardView? Card, ShiftResultView? Result)
{
    /// <param name="previousCycles">Итоги прошлых Циклов той же колоды, по порядку.</param>
    public static ShiftStateView Of(SwipeShiftRecord record, ShiftPlay play, IReadOnlyList<CycleResultView> previousCycles)
    {
        var running = play.Status == ShiftStatus.Running;
        var cycle = record.Cycle is { } number
            ? new CycleInfoView(number, ShiftTiming.Cycles.Select(c => c.TimeLimitMs).ToList(), previousCycles)
            : null;
        var card = running && record.CardShownAt is not null ? ShiftCardView.Of(play.Current!, record.Cycle) : null;
        return new ShiftStateView(
            record.Id, play.Mode, cycle, play.Status,
            play.ScaleDefinitions
                .Select(s => new ShiftScaleView(s.Code, s.Name, play.Scales[s.Code], s.Min, s.Max, s.Start, s.FailureThreshold))
                .ToList(),
            new ShiftProgressView(play.Done, play.Deck.Count, play.FirstAnswers.Select(a => a.Verdict).ToList()),
            card,
            running ? null : ShiftResultView.Of(play));
    }
}

/// <summary>Шкала Смены: срыв, когда значение дошло до порога (≤).</summary>
public sealed record ShiftScaleView(string Code, string Name, int Value, int Min, int Max, int Start, int FailureThreshold);

/// <summary>
/// Карточка без верной стороны. ReadingMs — сколько печатается формулировка: время ответа сервер считает
/// от её конца. TimeLimitMs — лимит на ответ после печати, null — без лимита.
/// </summary>
public sealed record ShiftCardView(
    string QuestionId, string Statement, string RightLabel, string LeftLabel, string Topic,
    IReadOnlyList<ServiceClassRef> ServiceClasses, bool IsRepeat, int ReadingMs, int? TimeLimitMs)
{
    public static ShiftCardView Of(QueuedCard card, int? cycle)
    {
        var question = card.Question;
        return new ShiftCardView(
            question.Id, question.Statement, question.Options.Right.Label, question.Options.Left.Label, question.Topic,
            question.ServiceClasses, card.IsRepeat, ShiftTiming.ReadingMs(question.Statement, cycle),
            ShiftTiming.Of(cycle)?.TimeLimitMs);
    }
}

/// <summary>Done — Вопросы, с которыми Смена закончила; Verdicts — первый ответ на каждый Вопрос, по порядку.</summary>
public sealed record ShiftProgressView(int Done, int Total, IReadOnlyList<Verdict> Verdicts);

/// <summary>Вопрос с ошибкой или «Не знаю» — строка «Что повторить» в итоге Смены.</summary>
public sealed record MistakeView(string QuestionId, string Statement, Explanation Explanation);

public sealed record ShiftResultView(
    string? FailedScale, int FirstTryCorrect, int Total, int? AverageAnswerMs, IReadOnlyList<MistakeView> Mistakes)
{
    public static ShiftResultView Of(ShiftPlay play) => new(
        play.FailedScale, play.FirstTryCorrect, play.Deck.Count, play.AverageAnswerMs,
        play.Mistakes.Select(q => new MistakeView(q.Id, q.Statement, q.Explanation)).ToList());
}

/// <summary>Итог одного Цикла — строка сравнения Циклов.</summary>
public sealed record CycleResultView(
    int Number, int TimeLimitMs, string? FailedScale, int FirstTryCorrect, int Total, int? AverageAnswerMs)
{
    public static CycleResultView Of(int number, ShiftPlay play) => new(
        number, ShiftTiming.Cycles[number - 1].TimeLimitMs, play.FailedScale, play.FirstTryCorrect, play.Deck.Count,
        play.AverageAnswerMs);
}

/// <summary>Цикл «На скорость»: номер с 1, лимиты на карточку по Циклам (их число — сколько всего Циклов), итоги прошлых.</summary>
public sealed record CycleInfoView(int Number, IReadOnlyList<int> TimeLimitsMs, IReadOnlyList<CycleResultView> Previous);

/// <summary>
/// Ответ на карточку: вердикт, верная сторона, Пояснение, фактические изменения Шкал и время ответа по часам
/// сервера. Shift — Смена после ответа: без карточки, пока экран не попросит следующую, или итог.
/// </summary>
public sealed record AnswerOutcomeView(
    Verdict Verdict, SwipeSide CorrectSide, Explanation Explanation, IReadOnlyDictionary<string, int> ScaleChanges,
    int ElapsedMs, bool TimedOut, ShiftStateView Shift);
