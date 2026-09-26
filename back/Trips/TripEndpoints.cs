using System.Security.Claims;

namespace TurboSquadApp.Trips;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        // Рейс видит только его владелец: чужой Рейс — 404. Отказ движка — 409 с кодом причины в поле reason.
        var trips = app.MapGroup("/api/trips").RequireAuthorization();

        trips.MapPost("/", (StartTripRequest request, HttpContext http, TripService service, CancellationToken ct) =>
                service.StartAsync(UserId(http), request.ServiceClass, ct))
            .Produces<TripView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("StartTrip")
            .WithSummary("Начать Рейс")
            .WithDescription("Начинает Рейс с Классом обслуживания на последних опубликованных версиях Событий и фиксирует эти версии за Рейсом.");

        trips.MapGet("/{id:guid}", (Guid id, HttpContext http, TripService service, CancellationToken ct) =>
                service.GetAsync(UserId(http), id, ct))
            .Produces<TripView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetTrip")
            .WithSummary("Текущее состояние Рейса")
            .WithDescription("Шкалы, Флаги, текущий Шаг с доступными Вариантами и моментом истечения таймера или Проактивный выбор, итог Рейса.");

        trips.MapPost("/{id:guid}/variant", (Guid id, ChooseVariantRequest request, HttpContext http, TripService service, CancellationToken ct) =>
                service.ActAsync(UserId(http), id, new ChooseVariant(request.VariantId), ct))
            .Produces<TripView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ChooseVariant")
            .WithSummary("Выбрать Вариант")
            .WithDescription("Сервер перепроверяет Условия Варианта. Ответ позже таймера больше чем на 1 с засчитывается как таймаут.");

        trips.MapPost("/{id:guid}/timeout", (Guid id, HttpContext http, TripService service, CancellationToken ct) =>
                service.ActAsync(UserId(http), id, new TimeOut(), ct))
            .Produces<TripView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("TimeOutStep")
            .WithSummary("Время Шага вышло")
            .WithDescription("Клиент вызывает, когда обратный отсчёт дошёл до нуля. Сервер принимает, только если таймер истёк по его часам.");

        trips.MapPost("/{id:guid}/proactive", (Guid id, ChooseProactiveRequest request, HttpContext http, TripService service, CancellationToken ct) =>
                service.ActAsync(UserId(http), id, new ChooseProactive(request.OptionId), ct))
            .Produces<TripView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ChooseProactive")
            .WithSummary("Проактивный выбор")
            .WithDescription("Выбор между Событиями: задаёт порядок следующих Событий.");

        trips.MapGet("/{id:guid}/debrief", (Guid id, HttpContext http, TripService service, CancellationToken ct) =>
                service.DebriefAsync(UserId(http), id, ct))
            .Produces<Debrief>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetTripDebrief")
            .WithSummary("Разбор Рейса, слой А")
            .WithDescription("Только по законченному Рейсу: итог, Проактивный выбор, по каждому Событию — результат и решения с изменениями Шкал, комментарием и Источником.");

        return app;
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record StartTripRequest(string ServiceClass);

public sealed record ChooseVariantRequest(string VariantId);

public sealed record ChooseProactiveRequest(string OptionId);
