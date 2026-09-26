namespace TurboSquadApp.Events;

/// <summary>Шкала из справочника: диапазон, старт и порог Срыва рейса.</summary>
public sealed record ScaleDefinition(
    string Code, string Name, int Min, int Max, int Start, int FailureThreshold, bool Mandatory, string? FailureReason);

/// <summary>Класс обслуживания: неизменный код, название и текст для проводника о том, чем класс отличается.</summary>
public sealed record ServiceClass(string Code, string Name, string Description);

/// <summary>Справочники, против которых проверяются События.</summary>
public sealed record ContentDirectory(IReadOnlyList<ScaleDefinition> Scales, IReadOnlyList<ServiceClass> Classes)
{
    /// <summary>Справочники по умолчанию (PRD §5.3, §5.5, приложение Б), пока они не лежат в БД.</summary>
    public static readonly ContentDirectory Default = new(
        [
            new("loyalty", "Лояльность пассажира", 0, 100, 70, 0, true, "конфликт с пассажиром"),
            new("safety", "Рейтинг безопасности", 0, 100, 70, 0, true, "авария / инцидент"),
        ],
        [
            new("standard", "Стандарт", "Доп. услуги за плату; кулер и торговые автоматы; ожидание персонала до 20 мин."),
            new("comfort", "Комфорт", "Часть услуг включена; дети 10–16 лет без сопровождения; ожидание персонала до 15 мин."),
            new("business", "Бизнес", "Напитки, закуски и десерт на месте; уборка каждые 30 мин; ожидание персонала до 10 мин."),
            new("first", "Первый", "Персональный подход, горячее питание со временем подачи по согласованию, режим «тишины»; ожидание персонала до 5 мин."),
        ]);
}
