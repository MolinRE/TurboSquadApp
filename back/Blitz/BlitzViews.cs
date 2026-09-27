using System.Text.Json.Serialization;
using TurboSquadApp.Questions;
using TurboSquadApp.Swipes;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Blitz;

// Ответы API Блица — поле в поле как front/src/lib/blitz/contract.ts (#41).

[JsonConverter(typeof(CamelCaseEnumConverter<BlitzStatus>))]
public enum BlitzStatus
{
    Running,
    Finished,
}

/// <summary>
/// Снимок Вопроса в колоде сессии: содержимое на момент старта, с верными вариантами. Правка Вопроса в CMS
/// начатую сессию не меняет. TimeLimitMs — лимит на ответ от показа.
/// </summary>
public sealed record BlitzQuestion(
    string Id, string Type, string Statement, string Topic, IReadOnlyList<ChoiceOption> Options, Explanation Explanation,
    int TimeLimitMs)
{
    public IReadOnlyList<string> CorrectOptionIds => Options.Where(option => option.Correct).Select(option => option.Id).ToList();
}

public sealed record BlitzSessionView(
    Guid SessionId, BlitzStatus Status, BlitzProgressView Progress, BlitzQuestionView? Question, BlitzResultView? Result);

/// <summary>Done — Вопросы с ответом; Verdicts — вердикты по порядку ответов.</summary>
public sealed record BlitzProgressView(int Done, int Total, IReadOnlyList<Verdict> Verdicts);

/// <summary>Показанный Вопрос без признака верного варианта.</summary>
public sealed record BlitzQuestionView(
    string QuestionId, string Type, string Statement, string Topic, IReadOnlyList<BlitzOptionView> Options, int TimeLimitMs)
{
    public static BlitzQuestionView Of(BlitzQuestion question) => new(
        question.Id, question.Type, question.Statement, question.Topic,
        question.Options.Select(option => new BlitzOptionView(option.Id, option.Text)).ToList(), question.TimeLimitMs);
}

public sealed record BlitzOptionView(string Id, string Text);

/// <summary>Итог: верных из всех, среднее время ответа по часам сервера и Вопросы с ошибкой или «Время вышло».</summary>
public sealed record BlitzResultView(int Correct, int Total, int? AverageAnswerMs, IReadOnlyList<MistakeView> Mistakes);

/// <summary>
/// Ответ на Вопрос: вердикт, верные варианты, Пояснение и время ответа по часам сервера. Session — сессия
/// после ответа: без Вопроса, пока экран не попросит следующий, или итог.
/// </summary>
public sealed record BlitzAnswerOutcomeView(
    Verdict Verdict, bool TimedOut, IReadOnlyList<string> CorrectOptionIds, Explanation Explanation, int ElapsedMs,
    BlitzSessionView Session);
