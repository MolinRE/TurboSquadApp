using System.Text.Json.Serialization;
using System.Text.Json;
using TurboSquadApp.Events;
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
public sealed record SwipeOptions(SwipeSideOption Right, SwipeSideOption Left, SwipeSide Correct) : IQuestionOptions
{
    public SwipeSideOption Side(SwipeSide side) => side == SwipeSide.Right ? Right : Left;

    [JsonIgnore]
    public SwipeSideOption Wrong => Correct == SwipeSide.Right ? Left : Right;
}

/// <summary>Сторона свайпа: подпись и изменения Шкал по коду Шкалы.</summary>
public sealed record SwipeSideOption(string Label, IReadOnlyDictionary<string, int> ScaleDeltas);

/// <summary>Один ответ или несколько: варианты с отметкой верных. Пока не играется.</summary>
public sealed record ChoiceOptions(IReadOnlyList<ChoiceOption> Options) : IQuestionOptions;

public sealed record ChoiceOption(string Id, string Text, bool Correct);

/// <summary>Последовательность: шаги в верном порядке. Пока не играется.</summary>
public sealed record SequenceOptions(IReadOnlyList<SequenceStep> Steps) : IQuestionOptions;

public sealed record SequenceStep(string Id, string Text);

/// <summary>Типизированные варианты общего банка Вопросов.</summary>
public interface IQuestionOptions;

/// <summary>Читает и пишет варианты в существующей jsonb-колонке по типу Вопроса.</summary>
public static class QuestionOptionsCodec
{
    public static IQuestionOptions Read(string type, string json) => type switch
    {
        QuestionTypes.Single or QuestionTypes.Multiple =>
            JsonSerializer.Deserialize<ChoiceOptions>(json, EventJson.Options) ?? throw new JsonException("Варианты пусты"),
        QuestionTypes.Sequence =>
            JsonSerializer.Deserialize<SequenceOptions>(json, EventJson.Options) ?? throw new JsonException("Шаги пусты"),
        QuestionTypes.Swipe =>
            JsonSerializer.Deserialize<SwipeOptions>(json, EventJson.Options) ?? throw new JsonException("Стороны свайпа пусты"),
        _ => throw new ArgumentException($"Неизвестный тип Вопроса: {type}", nameof(type)),
    };

    public static string Write(string type, IQuestionOptions options) => (type, options) switch
    {
        (QuestionTypes.Single or QuestionTypes.Multiple, ChoiceOptions choice) => JsonSerializer.Serialize(choice, EventJson.Options),
        (QuestionTypes.Sequence, SequenceOptions sequence) => JsonSerializer.Serialize(sequence, EventJson.Options),
        (QuestionTypes.Swipe, SwipeOptions swipe) => JsonSerializer.Serialize(swipe, EventJson.Options),
        _ => throw new ArgumentException($"Варианты не соответствуют типу Вопроса {type}", nameof(options)),
    };
}
