using System.Globalization;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

/// <summary>Проверка Условий (ADR-0002) против состояния Рейса. Пустой список выполнен всегда, «ИЛИ» нет.</summary>
internal static class Conditions
{
    public static bool Hold(IReadOnlyList<Condition>? conditions, TripState state) => Failed(conditions, state).Count == 0;

    /// <summary>Невыполненные Условия текстом для человека: «Класс обслуживания — standard, comfort (сейчас first)».</summary>
    public static IReadOnlyList<string> Failed(IReadOnlyList<Condition>? conditions, TripState state) =>
        (conditions ?? []).Where(condition => !Holds(condition, state)).Select(condition => Describe(condition, state)).ToList();

    private static bool Holds(Condition condition, TripState state) => condition.Type switch
    {
        "scale" => Compare(state.Scales[condition.Scale!], condition.Op!, condition.Value!.Value),
        "flag" => state.Flags.Contains(condition.Flag!) == condition.Present,
        "class" => condition.Classes!.Contains(state.ServiceClass),
        _ => throw new InvalidOperationException($"Неизвестный тип Условия «{condition.Type}»: валидатор должен был это поймать"),
    };

    private static bool Compare(int now, string op, double value) => op switch
    {
        "<" => now < value,
        "<=" => now <= value,
        ">" => now > value,
        ">=" => now >= value,
        "=" => now == value,
        _ => throw new InvalidOperationException($"Неизвестный оператор «{op}»: валидатор должен был это поймать"),
    };

    private static string Describe(Condition condition, TripState state)
    {
        switch (condition.Type)
        {
            case "scale":
                var scale = state.Content!.Directory.Scales.Single(s => s.Code == condition.Scale);
                return $"{scale.Name} {condition.Op} {condition.Value!.Value.ToString(CultureInfo.InvariantCulture)} (сейчас {state.Scales[scale.Code]})";
            case "flag":
                return $"Флаг «{condition.Flag}» {Presence(condition.Present!.Value)} (сейчас {Presence(state.Flags.Contains(condition.Flag!))})";
            default:
                return $"Класс обслуживания — {string.Join(", ", condition.Classes!)} (сейчас {state.ServiceClass})";
        }

        static string Presence(bool present) => present ? "есть" : "нет";
    }
}
