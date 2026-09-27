using System.Text.Json.Serialization;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Swipes;

/// <summary>Режим Смены: «В своём темпе» — один проход без лимита; «На скорость» — три Цикла по методу Woodpecker.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ShiftMode>))]
public enum ShiftMode
{
    Calm,
    Woodpecker,
}

[JsonConverter(typeof(CamelCaseEnumConverter<ShiftStatus>))]
public enum ShiftStatus
{
    Running,
    Passed,
    Failed,
}

/// <summary>Ответ на карточку: вправо — «да / можно / сделать», влево — «нет / нельзя / не делать», вверх — «Не знаю».</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<SwipeAnswer>))]
public enum SwipeAnswer
{
    Right,
    Left,
    Unknown,
}

[JsonConverter(typeof(CamelCaseEnumConverter<Verdict>))]
public enum Verdict
{
    Correct,
    Wrong,
    Unknown,
}

/// <summary>Пояснение: ключевой факт — дословный фрагмент текста; Source — пункт Источника.</summary>
public sealed record Explanation(string Text, string KeyFact, string Source);

public sealed record ServiceClassRef(string Code, string Name);

/// <summary>Вопрос-свайп в колоде Смены: всё, что нужно правилам и карточке.</summary>
public sealed record ShiftQuestion(
    string Id, string Statement, SwipeOptions Options, Explanation Explanation, string Topic,
    IReadOnlyList<ServiceClassRef> ServiceClasses);

/// <summary>Карточка в очереди Смены; Repeats — сколько раз Вопрос уже вернулся Повтором, 0 — первый показ.</summary>
public sealed record QueuedCard(ShiftQuestion Question, int Repeats)
{
    public bool IsRepeat => Repeats > 0;
}

/// <summary>Сосчитанный ответ. TimedOut — «Время вышло», засчитано как «Не знаю».</summary>
public sealed record SettledAnswer(
    ShiftQuestion Question, SwipeAnswer Answer, bool TimedOut, Verdict Verdict, bool IsRepeat, int ElapsedMs,
    IReadOnlyDictionary<string, int> ScaleChanges);

/// <summary>Лимит на карточку и темп печати в Цикле.</summary>
public sealed record CycleTiming(int TimeLimitMs, int TypingMsPerChar);

/// <summary>Темп печати и Циклы метода Woodpecker — калибруются (open-questions §2).</summary>
public static class ShiftTiming
{
    public const int DeckSize = 10;
    public const int CalmTypingMsPerChar = 35;

    public static readonly IReadOnlyList<CycleTiming> Cycles = [new(10_000, 35), new(7_000, 21), new(5_000, 12)];

    public static CycleTiming? Of(int? cycle) => cycle is { } number ? Cycles[number - 1] : null;

    /// <summary>Сколько печатается формулировка: варианты появляются и время ответа идёт после неё.</summary>
    public static int ReadingMs(string statement, int? cycle) =>
        statement.Length * (Of(cycle)?.TypingMsPerChar ?? CalmTypingMsPerChar);
}

/// <summary>
/// Правила Смены на свайпах — те же, что у подменного модуля экрана (front/src/lib/swipes/fake-api.ts, #26):
/// очередь карточек с Повторами, Шкалы с обрезкой по границам и Срывом, итог. Без базы, HTTP и часов:
/// состояние выводится проигрыванием ответов по порядку, как журнал Рейса.
/// </summary>
public sealed class ShiftPlay
{
    /// <summary>Повтор: карточка с ошибкой или «Не знаю» возвращается через столько карточек…</summary>
    public const int RepeatAfterCards = 3;

    /// <summary>…и не больше стольких раз, потом Вопрос остаётся только в «Что повторить».</summary>
    public const int MaxRepeats = 2;

    private readonly List<QueuedCard> _queue;
    private readonly Dictionary<string, int> _scales;
    private readonly List<SettledAnswer> _answers = [];

    public ShiftPlay(ShiftMode mode, IReadOnlyList<ShiftQuestion> deck, IReadOnlyList<ScaleDefinition> scales)
    {
        Mode = mode;
        Deck = deck;
        ScaleDefinitions = scales;
        _queue = deck.Select(question => new QueuedCard(question, 0)).ToList();
        _scales = scales.ToDictionary(scale => scale.Code, scale => scale.Start);
    }

    public ShiftMode Mode { get; }
    public IReadOnlyList<ShiftQuestion> Deck { get; }
    public IReadOnlyList<ScaleDefinition> ScaleDefinitions { get; }
    public IReadOnlyDictionary<string, int> Scales => _scales;
    public ShiftStatus Status { get; private set; } = ShiftStatus.Running;

    /// <summary>Код Шкалы, дошедшей до порога Срыва; null, если Срыва нет.</summary>
    public string? FailedScale { get; private set; }

    public IReadOnlyList<SettledAnswer> Answers => _answers;

    /// <summary>Карточка, на которую ждут ответ; null — Смена закончена.</summary>
    public QueuedCard? Current => Status == ShiftStatus.Running ? _queue[0] : null;

    /// <summary>Вопросы, с которыми Смена закончила: незаконченный Вопрос стоит в очереди ровно раз.</summary>
    public int Done => Deck.Count - _queue.Count;

    /// <summary>Первый ответ на каждый Вопрос: по нему прогресс, «верно с первого раза» и «Что повторить».</summary>
    public IEnumerable<SettledAnswer> FirstAnswers => _answers.Where(answer => !answer.IsRepeat);

    public int FirstTryCorrect => FirstAnswers.Count(answer => answer.Verdict == Verdict.Correct);

    /// <summary>Вопросы с ошибкой или «Не знаю», по порядку первых ответов.</summary>
    public IReadOnlyList<ShiftQuestion> Mistakes =>
        FirstAnswers.Where(answer => answer.Verdict != Verdict.Correct).Select(answer => answer.Question).ToList();

    /// <summary>Среднее время ответа по всем ответам, включая Повторы; null, если ответов не было.</summary>
    public int? AverageAnswerMs => _answers.Count == 0
        ? null
        : (int)Math.Round(_answers.Average(answer => answer.ElapsedMs), MidpointRounding.AwayFromZero);

    /// <summary>Засчитывает ответ на текущую карточку: Шкалы, Повтор, Срыв или конец Смены.</summary>
    public SettledAnswer Settle(SwipeAnswer answer, bool timedOut, int elapsedMs)
    {
        var card = Current ?? throw new InvalidOperationException("Смена уже закончена");
        var options = card.Question.Options;
        var verdict = answer == SwipeAnswer.Unknown ? Verdict.Unknown
            : SideOf(answer) == options.Correct ? Verdict.Correct : Verdict.Wrong;
        var settled = new SettledAnswer(
            card.Question, answer, timedOut, verdict, card.IsRepeat, elapsedMs, ApplyDeltas(DeltasOf(options, answer)));
        _answers.Add(settled);

        _queue.RemoveAt(0);
        // Повтор только «В своём темпе»: в Циклах это открытый вопрос (open-questions §4).
        if (verdict != Verdict.Correct && Mode == ShiftMode.Calm && card.Repeats < MaxRepeats)
            _queue.Insert(Math.Min(RepeatAfterCards, _queue.Count), card with { Repeats = card.Repeats + 1 });

        var broken = ScaleDefinitions.FirstOrDefault(scale => scale.Mandatory && _scales[scale.Code] <= scale.FailureThreshold);
        if (broken is not null)
        {
            Status = ShiftStatus.Failed;
            FailedScale = broken.Code;
        }
        else if (_queue.Count == 0)
        {
            Status = ShiftStatus.Passed;
        }
        return settled;
    }

    private static SwipeSide SideOf(SwipeAnswer answer) => answer == SwipeAnswer.Right ? SwipeSide.Right : SwipeSide.Left;

    /// <summary>
    /// «Не знаю» — половина штрафов неверной стороны с округлением к нулю: столько в среднем стоит угадывание.
    /// Плюсов неверной стороны «Не знаю» не даёт.
    /// </summary>
    private static IReadOnlyDictionary<string, int> DeltasOf(SwipeOptions options, SwipeAnswer answer)
    {
        if (answer != SwipeAnswer.Unknown) return options.Side(SideOf(answer)).ScaleDeltas;
        var wrongSide = options.Side(options.Correct == SwipeSide.Right ? SwipeSide.Left : SwipeSide.Right);
        return wrongSide.ScaleDeltas
            .Where(delta => delta.Value < 0)
            .ToDictionary(delta => delta.Key, delta => delta.Value / 2);   // деление int округляет к нулю
    }

    /// <summary>Применяет изменения с обрезкой по границам Шкал и возвращает фактические.</summary>
    private Dictionary<string, int> ApplyDeltas(IReadOnlyDictionary<string, int> deltas)
    {
        var applied = new Dictionary<string, int>();
        foreach (var scale in ScaleDefinitions)
        {
            if (!deltas.TryGetValue(scale.Code, out var delta) || delta == 0) continue;
            var before = _scales[scale.Code];
            var after = Math.Clamp(before + delta, scale.Min, scale.Max);
            _scales[scale.Code] = after;
            if (after != before) applied[scale.Code] = after - before;
        }
        return applied;
    }
}
