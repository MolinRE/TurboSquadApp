using TurboSquadApp.Content;
using TurboSquadApp.Events;

namespace TurboSquadApp.Trips;

/// <summary>Контент Рейса, зафиксированный на старте: справочники, События тех версий, что будут играться, и настройка Рейса.</summary>
public sealed record TripContent(ContentDirectory Directory, IReadOnlyList<EventDocument> Events, TripSettings Settings)
{
    public EventDocument Event(string id) => Events.Single(e => e.Id == id);

    /// <summary>
    /// Ошибки, из-за которых движок не начинает Рейс: настройка Рейса ссылается на несуществующие События,
    /// id Событий повторяются, Событие не проходит валидатор. Флаги сверяются с остальными Событиями Рейса.
    /// </summary>
    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>();
        var ids = Events.Select(ev => ev.Id).ToList();
        errors.AddRange(ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => $"Два События с id «{g.Key}»"));
        if (!ids.Contains(Settings.ShiftStartEvent))
            errors.Add($"События Заступа «{Settings.ShiftStartEvent}» нет");
        foreach (var option in Settings.ProactiveChoice.Options)
            errors.AddRange(option.Pool.Where(id => !ids.Contains(id))
                .Select(id => $"В пуле Проактивного выбора «{option.Id}» нет События «{id}»"));

        errors.AddRange(Events.SelectMany(ev => EventValidator
            .Validate(ev, Directory, FlagsSetElsewhere(ev.Id))
            .Errors.Select(error => $"{ev.Id} v{ev.Version}, {error.Where}: {error.Message}")));
        return errors;
    }

    private HashSet<string> FlagsSetElsewhere(string eventId) =>
        Events.Where(ev => ev.Id != eventId).SelectMany(ev => ev.FlagsSet).ToHashSet();
}
