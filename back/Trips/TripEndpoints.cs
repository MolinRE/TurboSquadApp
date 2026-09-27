using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

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
                service.ActAsync(UserId(http), id, new ChooseVariant(request.VariantId), new StepPosition(request.EventId, request.StepId), ct))
            .Produces<TripView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ChooseVariant")
            .WithSummary("Выбрать Вариант")
            .WithDescription("eventId и stepId — Шаг, на который отвечает проводник: ответ на уже пройденный Шаг (повторный клик) отклоняется. Сервер перепроверяет Условия Варианта. Ответ позже таймера больше чем на 1 с засчитывается как таймаут.");

        trips.MapPost("/{id:guid}/voice", async (
                Guid id, [FromForm] string eventId, [FromForm] string stepId, [FromForm] string attemptId, IFormFile audio,
                HttpContext http, TripService service, CancellationToken ct) =>
                await service.VoiceAsync(UserId(http), id, eventId, stepId, attemptId, audio, ct))
            .Accepts<VoiceStepRequest>("multipart/form-data")
            .Produces<TripView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("AnswerVoiceStep")
            .WithSummary("Ответить голосом")
            .WithDescription("Принимает готовый аудиофрагмент голосового Шага с idempotency attemptId, распознаёт его через GigaAM-v3, получает полный вердикт Laya и возвращает pending-реакцию до завершения реплики пассажира. Аудио не сохраняется.")
            .DisableAntiforgery();

        trips.MapGet("/{id:guid}/voice/{attemptId}/reply", async (
                Guid id, string attemptId, HttpContext http, TripService service, CancellationToken ct) =>
                await service.StreamVoiceReplyAsync(UserId(http), id, attemptId, http.Response, ct))
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces(StatusCodes.Status404NotFound)
            .WithName("StreamPassengerReply")
            .WithSummary("Поток реплики пассажира")
            .WithDescription("Потоково передаёт токены реплики пассажира от Qwen после вердикта Laya. Повторное подключение отдаёт сохранённый итог или явную ошибку.");

        trips.MapPost("/{id:guid}/timeout", (Guid id, TimeOutRequest request, HttpContext http, TripService service, CancellationToken ct) =>
                service.ActAsync(UserId(http), id, new TimeOut(), new StepPosition(request.EventId, request.StepId), ct))
            .Produces<TripView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("TimeOutStep")
            .WithSummary("Время Шага вышло")
            .WithDescription("Клиент вызывает, когда обратный отсчёт дошёл до нуля. Сервер принимает, только если таймер истёк по его часам.");

        trips.MapPost("/{id:guid}/proactive", (Guid id, ChooseProactiveRequest request, HttpContext http, TripService service, CancellationToken ct) =>
                service.ActAsync(UserId(http), id, new ChooseProactive(request.OptionId), at: null, ct))
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

        trips.MapGet("/debriefs", (HttpContext http, TripService service, CancellationToken ct) =>
                service.ListDebriefsAsync(UserId(http), ct))
            .Produces<IReadOnlyList<TripDebriefListItem>>()
            .WithName("ListTripDebriefs")
            .WithSummary("История Разборов завершённых Рейсов")
            .WithDescription("Только Рейсы текущего Проводника, новые сначала.");

        return app;
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record StartTripRequest(string ServiceClass);

public sealed record ChooseVariantRequest(string EventId, string StepId, string VariantId);

public sealed record VoiceStepRequest(string EventId, string StepId, string AttemptId, IFormFile Audio);

public sealed record TimeOutRequest(string EventId, string StepId);

public sealed record ChooseProactiveRequest(string OptionId);
