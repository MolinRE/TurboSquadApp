using System.Text.Json;
using TurboSquadApp.Data;
using TurboSquadApp.Events;

namespace TurboSquadApp.Questions;

/// <summary>Ошибка публикации с путём к полю формы CMS.</summary>
public sealed record QuestionValidationError(string Path, string Message);

public sealed record QuestionValidationReport(IReadOnlyList<QuestionValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Единые правила публикации Вопроса для сидера и будущей CMS.</summary>
public static class QuestionValidator
{
    public static QuestionValidationReport ValidateForPublication(QuestionRecord question, ContentDirectory? directory = null)
    {
        var errors = new List<QuestionValidationError>();
        void Error(string path, string reason) => errors.Add(new(path, reason));
        void Required(string path, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) Error(path, "Обязательное поле не заполнено");
        }

        Required("id", question.Id);
        Required("statement", question.Statement);
        Required("topic", question.Topic);
        Required("explanationText", question.ExplanationText);
        Required("explanationKeyFact", question.ExplanationKeyFact);
        Required("source", question.Source);
        if (question.TimeLimitSec is null or <= 0) Error("timeLimitSec", "Лимит времени должен быть положительным");

        if (question.Type is not (QuestionTypes.Single or QuestionTypes.Multiple or QuestionTypes.Sequence or QuestionTypes.Swipe))
        {
            Error("type", "Неизвестный тип Вопроса");
            return new(errors);
        }

        IQuestionOptions options;
        try
        {
            options = question.ReadOptions();
        }
        catch (JsonException ex)
        {
            var path = ex.Path is { Length: > 1 } jsonPath ? "options" + jsonPath[1..] : "options";
            Error(path, $"Варианты не читаются: {ex.Message}");
            return new(errors);
        }

        switch (options)
        {
            case ChoiceOptions choice:
                CheckItems(choice.Options, "options.options", item => item.Id, item => item.Text);
                using (var document = JsonDocument.Parse(question.Options))
                    if (document.RootElement.TryGetProperty("options", out var itemsJson) && itemsJson.ValueKind == JsonValueKind.Array)
                    {
                        var index = 0;
                        foreach (var item in itemsJson.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object && !item.TryGetProperty("correct", out _))
                                Error($"options.options[{index}].correct", "Нужно указать, верен ли вариант");
                            index++;
                        }
                    }
                var count = choice.Options?.Count ?? 0;
                var correct = choice.Options?.Count(item => item?.Correct == true) ?? 0;
                if (question.Type == QuestionTypes.Single && (count < 2 || correct != 1))
                    Error("options.options", "Нужно минимум два варианта и ровно один верный");
                if (question.Type == QuestionTypes.Multiple && (correct < 2 || count - correct < 1))
                    Error("options.options", "Нужно минимум два верных варианта и один неверный");
                break;
            case SequenceOptions sequence:
                CheckItems(sequence.Steps, "options.steps", item => item.Id, item => item.Text);
                if (sequence.Steps is not { Count: >= 3 and <= 5 })
                    Error("options.steps", "Последовательность должна содержать от 3 до 5 шагов");
                break;
            case SwipeOptions swipe:
                if (swipe.Right is null) Error("options.right", "Правая сторона не задана");
                else CheckSide(swipe.Right, "options.right");
                if (swipe.Left is null) Error("options.left", "Левая сторона не задана");
                else CheckSide(swipe.Left, "options.left");
                using (var document = JsonDocument.Parse(question.Options))
                    if (!document.RootElement.TryGetProperty("correct", out _))
                        Error("options.correct", "Верная сторона не задана");
                break;
        }

        if (directory is not null)
        {
            if (options is SwipeOptions swipeOptions)
                foreach (var (side, value) in new[] { ("right", swipeOptions.Right), ("left", swipeOptions.Left) })
                    if (value?.ScaleDeltas is not null)
                        foreach (var code in value.ScaleDeltas.Keys.Where(code => directory.Scales.All(scale => scale.Code != code)))
                            Error($"options.{side}.scaleDeltas.{code}", $"Шкалы «{code}» нет в справочнике");

            try
            {
                foreach (var code in JsonSerializer.Deserialize<List<string>>(question.ServiceClasses) ?? [])
                    if (directory.Classes.All(serviceClass => serviceClass.Code != code))
                        Error("serviceClasses", $"Класса обслуживания «{code}» нет в справочнике");
            }
            catch (JsonException)
            {
                Error("serviceClasses", "Классы обслуживания не читаются");
            }
        }

        return new(errors);

        void CheckSide(SwipeSideOption side, string path)
        {
            Required($"{path}.label", side.Label);
            if (side.ScaleDeltas is null) Error($"{path}.scaleDeltas", "Изменения Шкал не заданы");
        }

        void CheckItems<T>(IReadOnlyList<T>? items, string path, Func<T, string> idOf, Func<T, string> textOf) where T : class
        {
            var ids = new HashSet<string>();
            if (items is null)
            {
                Error(path, "Список не задан");
                return;
            }
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item is null)
                {
                    Error($"{path}[{index}]", "Элемент не задан");
                    continue;
                }
                var id = idOf(item);
                Required($"{path}[{index}].id", id);
                Required($"{path}[{index}].text", textOf(item));
                if (!string.IsNullOrWhiteSpace(id) && !ids.Add(id))
                    Error($"{path}[{index}].id", "ID должен быть уникальным");
            }
        }
    }
}
