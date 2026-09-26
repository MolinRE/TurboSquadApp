using System.Globalization;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

/// <summary>
/// Проверка Условий (ADR-0002) против состояния Рейса. Пустой список выполнен всегда, «ИЛИ» нет.
/// Новый тип Условия — одна ветка в <see cref="Check"/>.
/// </summary>
internal static class Conditions
{
    public static bool AllHold(IReadOnlyList<Condition>? conditions, TripState state) => Failed(conditions, state).Count == 0;

    /// <summary>Невыполненные Условия текстом для человека: «Класс обслуживания — standard, comfort (сейчас first)».</summary>
    public static IReadOnlyList<string> Failed(IReadOnlyList<Condition>? conditions, TripState state) =>
        (conditions ?? []).Select(condition => Check(condition, state)).Where(c => !c.Holds).Select(c => c.Text).ToList();

    private static (bool Holds, string Text) Check(Condition condition, TripState state)
    {
        switch (condition.Type)
        {
            case "scale":
                var scale = state.Content!.Directory.Scale(condition.Scale!);
                var now = state.Scales[scale.Code];
                var value = condition.Value!.Value;
                return (Compare(now, condition.Op!, value),
                    $"{scale.Name} {condition.Op} {value.ToString(CultureInfo.InvariantCulture)} (сейчас {now})");
            case "flag":
                var present = state.Flags.Contains(condition.Flag!);
                return (present == condition.Present,
                    $"Флаг «{condition.Flag}» {Presence(condition.Present!.Value)} (сейчас {Presence(present)})");
            case "class":
                return (condition.Classes!.Contains(state.ServiceClass),
                    $"Класс обслуживания — {string.Join(", ", condition.Classes!)} (сейчас {state.ServiceClass})");
            default:
                throw new InvalidOperationException($"Неизвестный тип Условия «{condition.Type}»: валидатор должен был это поймать");
        }

        static string Presence(bool present) => present ? "есть" : "нет";
    }

    private static bool Compare(int now, string op, double value) => op switch
    {
        "<" => now < value,
        "<=" => now <= value,
        ">" => now > value,
        ">=" => now >= value,
        "=" => now == value,
        _ => throw new InvalidOperationException($"Неизвестный оператор «{op}»: валидатор должен был это поймать"),
    };
}
