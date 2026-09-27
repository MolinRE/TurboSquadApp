using System.Text.Json.Serialization;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Questions;

/// <summary>Тип Вопроса (PRD §9.1). Описаны все, играется пока только свайп.</summary>
public static class QuestionTypes
{
    public const string Single = "single";
    public const string Multiple = "multiple";
    public const string Sequence = "sequence";
    public const string Swipe = "swipe";
}

/// <summary>Черновик не виден проводникам: в колоду попадают только опубликованные Вопросы.</summary>
public static class QuestionStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
}

/// <summary>Сторона свайпа: вправо — «да / можно / сделать», влево — «нет / нельзя / не делать».</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<SwipeSide>))]
public enum SwipeSide
{
    Right,
    Left,
}

/// <summary>Варианты свайпа из двух: подписи сторон, изменения Шкал у каждой и верная сторона.</summary>
public sealed record SwipeOptions(SwipeSideOption Right, SwipeSideOption Left, SwipeSide Correct)
{
    public SwipeSideOption Side(SwipeSide side) => side == SwipeSide.Right ? Right : Left;
}

/// <summary>Сторона свайпа: подпись и изменения Шкал по коду Шкалы.</summary>
public sealed record SwipeSideOption(string Label, IReadOnlyDictionary<string, int> ScaleDeltas);

/// <summary>Один ответ или несколько: варианты с отметкой верных. Пока не играется.</summary>
public sealed record ChoiceOptions(IReadOnlyList<ChoiceOption> Options);

public sealed record ChoiceOption(string Id, string Text, bool Correct);

/// <summary>Последовательность: шаги в верном порядке. Пока не играется.</summary>
public sealed record SequenceOptions(IReadOnlyList<SequenceStep> Steps);

public sealed record SequenceStep(string Id, string Text);
