using System.Reflection;
using System.Text.Json;
using TurboSquadApp.Events;

namespace TurboSquadApp.Content;

/// <summary>
/// Настройка Рейса этапа 1 (PRD §5.1): Заступ на смену, затем Проактивный выбор определяет порядок Событий.
/// Формат Проактивного выбора — как в прототипе движка; окончательный формат — открытый вопрос (docs/open-questions.md).
/// </summary>
public sealed record TripSettings
{
    public string ShiftStartEvent { get; init; } = "";
    public int EventsAfterShiftStart { get; init; }
    public ProactiveChoice ProactiveChoice { get; init; } = new();
}

public sealed record ProactiveChoice
{
    public string Situation { get; init; } = "";
    public IReadOnlyList<ProactiveOption> Options { get; init; } = [];
}

/// <summary>Вариант Проактивного выбора: Pool — порядок Событий после него.</summary>
public sealed record ProactiveOption
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Pool { get; init; } = [];
}

/// <summary>Событие из сидов: разобранный документ и JSON в том виде, в каком он хранится в базе.</summary>
public sealed record SeedEvent(EventDocument Document, string Json);

/// <summary>Вопрос из сидов (PRD §9.1). Options — варианты по типу Вопроса, для свайпа — SwipeOptions.</summary>
public sealed record SeedQuestion(
    string Id, string Type, string Statement, JsonElement Options, SeedExplanation Explanation, string Topic,
    IReadOnlyList<string> Categories, IReadOnlyList<string> ServiceClasses, double BaseFrequency = 1, int? TimeLimitSec = null,
    int KnowledgeCost = 10);

/// <summary>Пояснение: текст с ключевым фактом, пункт Источника и цитата из него.</summary>
public sealed record SeedExplanation(string Text, string KeyFact, string Source, string? Quote = null);

/// <summary>
/// Стартовый контент этапа 1: справочники, Заступ на смену, №6 и №33 из «Ситуаций на борту», настройка Рейса —
/// перенесены из прототипа движка (ветка prototype/trip-engine) в формат PRD v7 §5.2; Вопросы-свайпы — из
/// локальных данных экрана Смены на свайпах (#24), Вопросы Блица (single/multiple/sequence) — по «Ситуациям на борту» (#40).
/// Файлы — ресурсы сборки, Content/Seeds.
/// </summary>
public static class SeedContent
{
    private static readonly string[] EventFiles = ["zastup", "sit-06", "sit-33"];

    public static ContentDirectory Directory => ContentDirectory.Default;

    public static IReadOnlyList<SeedEvent> Events { get; } = EventFiles
        .Select(name => ReadResource($"{name}.json"))
        .Select(json => new SeedEvent(JsonSerializer.Deserialize<EventDocument>(json, EventJson.Options)!, json))
        .ToList();

    public static TripSettings Trip { get; } =
        JsonSerializer.Deserialize<TripSettings>(ReadResource("trip.json"), EventJson.Options)!;

    public static IReadOnlyList<SeedQuestion> Questions { get; } =
        JsonSerializer.Deserialize<List<SeedQuestion>>(ReadResource("questions.json"), EventJson.Options)!;

    /// <summary>Флаги, которые ставят остальные сиды: «Флаги между Событиями» внутри стартового контента.</summary>
    public static IReadOnlyCollection<string> FlagsSetElsewhere(string eventId) => Events
        .Where(seed => seed.Document.Id != eventId)
        .SelectMany(seed => seed.Document.FlagsSet)
        .ToHashSet();

    private static string ReadResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith($".Content.Seeds.{fileName}", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
