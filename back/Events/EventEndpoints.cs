namespace TurboSquadApp.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // Проверка События до публикации: отчёт валидатора с ошибками и предупреждениями.
        // Пока опубликованных Событий нет (тикет #5), Флаги сверяются только с самим Событием.
        app.MapPost("/api/events/validate", (EventDocument ev) =>
                EventValidator.Validate(ev, ContentDirectory.Default, flagsSetElsewhere: []))
            .WithName("ValidateEvent")
            .WithSummary("Проверить Событие валидатором")
            .WithDescription("Возвращает ошибки и предупреждения с местом в графе и правилом (ADR-0001, ADR-0002, PRD).");

        return app;
    }
}
