using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboSquadApp.Events;

// Документ События — граф Шагов (ADR-0001, PRD v7 §5.2).
// Ключи JSON английские, по-русски только тексты для людей.

/// <summary>Событие: законченная ситуация на одну Тему, граф Шагов с несколькими Исходами.</summary>
public sealed record EventDocument
{
    public string Id { get; init; } = "";
    public int Version { get; init; }
    public string Title { get; init; } = "";
    public string Topic { get; init; } = "";
    public IReadOnlyList<string>? Categories { get; init; }
    public string? Source { get; init; }

    /// <summary>Условия События: фильтр пула Рейса.</summary>
    public IReadOnlyList<Condition>? Conditions { get; init; }

    public string Start { get; init; } = "";
    public IReadOnlyList<Step> Steps { get; init; } = [];
}

/// <summary>Шаг: ситуация или реплика, на которую проводник реагирует. Шаг с полем Outcome — Исход.</summary>
public sealed record Step
{
    public string Id { get; init; } = "";

    /// <summary>Исход: "success" или "failure". У обычного Шага не заполнен.</summary>
    public string? Outcome { get; init; }

    /// <summary>"buttons" или "voice".</summary>
    public string? AnswerType { get; init; }

    public string Situation { get; init; } = "";
    public int? TimerSec { get; init; }

    // Только для голосовых Шагов.
    public string? Brief { get; init; }
    public string? ReferenceLine { get; init; }
    public IReadOnlyList<string>? RequiredRoleStages { get; init; }

    public IReadOnlyList<Variant>? Variants { get; init; }

    /// <summary>Ветка таймаута: устроена как Вариант, обязательна при таймере.</summary>
    public Variant? Timeout { get; init; }

    /// <summary>У Исхода Шкал быть не должно; поле есть, чтобы валидатор это заметил.</summary>
    public IReadOnlyDictionary<string, int>? ScaleDeltas { get; init; }

    [JsonIgnore]
    public bool IsOutcome => Outcome is not null;

    /// <summary>Реакции проводника на Шаг: Варианты и ветка таймаута — она устроена как Вариант.</summary>
    [JsonIgnore]
    public IEnumerable<Variant> Reactions =>
        Timeout is null ? Variants ?? [] : [.. Variants ?? [], Timeout];
}

/// <summary>Вариант: заранее описанная реакция проводника со своими последствиями и переходами.</summary>
public sealed record Variant
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";

    /// <summary>Условия показа Варианта.</summary>
    public IReadOnlyList<Condition>? Conditions { get; init; }

    public IReadOnlyDictionary<string, int>? ScaleDeltas { get; init; }
    public IReadOnlyDictionary<string, int>? Competencies { get; init; }
    public IReadOnlyList<string>? RoleStages { get; init; }
    public string? Comment { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<string>? SetsFlags { get; init; }

    /// <summary>Упорядоченный список: срабатывает первый переход с выполненными Условиями.</summary>
    public IReadOnlyList<Transition>? Transitions { get; init; }

    public bool CriticalError { get; init; }
}

public sealed record Transition
{
    public string To { get; init; } = "";
    public IReadOnlyList<Condition>? Conditions { get; init; }
}

/// <summary>
/// Условие (ADR-0002): "scale" (Scale, Op, Value), "flag" (Flag, Present) или "class" (Classes — коды классов).
/// Одна плоская запись, чтобы неизвестный тип стал ошибкой валидатора, а не падением разбора JSON.
/// Поэтому лишние поля здесь пропускаются: у неизвестного типа свои поля, а опечатку в известном
/// поле валидатор всё равно поймает — обязательное поле окажется пустым.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record Condition
{
    public string Type { get; init; } = "";
    public string? Scale { get; init; }
    public string? Op { get; init; }
    public double? Value { get; init; }
    public string? Flag { get; init; }
    public bool? Present { get; init; }
    public IReadOnlyList<string>? Classes { get; init; }
}

public static class EventJson
{
    /// <summary>
    /// Незнакомые поля и null в обязательных полях — ошибка разбора, а не молчаливая потеря:
    /// опечатка в ключе (timer_sec вместо timerSec) иначе пропала бы без следа.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
    };
}
