using System.Text.Json;

namespace TurboSquadApp.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // Проверка События до публикации: отчёт валидатора с ошибками и предупреждениями.
        // Тело принимается как произвольный JSON, чтобы нечитаемый документ тоже вернулся отчётом, а не ответом 400.
        // Флаги сверяются только с самим Событием: «Флаги между Событиями» — этап 2 (docs/mvp-priorities.md).
        app.MapPost("/api/events/validate", (JsonElement body) =>
                EventValidator.ValidateJson(body.GetRawText(), ContentDirectory.Default, flagsSetElsewhere: []))
            .Accepts<EventDocument>("application/json")
            .Produces<ValidationReport>()
            .WithName("ValidateEvent")
            .WithSummary("Проверить Событие валидатором")
            .WithDescription("Возвращает ошибки и предупреждения: место в графе (Шаг, Вариант, номер перехода) и правило (ADR-0001, ADR-0002, PRD).");

        return app;
    }
}
