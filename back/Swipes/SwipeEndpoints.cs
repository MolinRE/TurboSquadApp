using System.Security.Claims;
using TurboSquadApp.Data;

namespace TurboSquadApp.Swipes;

public static class SwipeEndpoints
{
    public static IEndpointRouteBuilder MapSwipeEndpoints(this IEndpointRouteBuilder app)
    {
        // Смену видит только Проводник, который её начал: чужая Смена — 404. Отказ правил — 409 с кодом причины в поле reason.
        var shifts = app.MapGroup("/api/swipe-shifts").RequireAuthorization(policy => policy.RequireRole(UserRoles.Conductor));

        shifts.MapPost("/", (StartSwipeShiftRequest request, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.StartAsync(UserId(http), request.Mode, ct))
            .Produces<ShiftStateView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("StartSwipeShift")
            .WithSummary("Начать Смену на свайпах")
            .WithDescription("Режим calm — «В своём темпе», woodpecker — «На скорость», Цикл 1 из 3. Колода — до 10 опубликованных Вопросов-свайпов в случайном порядке. Первая карточка показана сразу.");

        shifts.MapGet("/{id:guid}", (Guid id, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.GetAsync(UserId(http), id, ct))
            .Produces<ShiftStateView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetSwipeShift")
            .WithSummary("Текущее состояние Смены")
            .WithDescription("Шкалы, прогресс, показанная карточка без верной стороны или итог: чтобы продолжить Смену после перезагрузки.");

        shifts.MapGet("/debriefs", (HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.ListDebriefsAsync(UserId(http), ct))
            .Produces<IReadOnlyList<SwipeDebriefListItem>>()
            .WithName("ListSwipeDebriefs")
            .WithSummary("История Разборов завершённых Смен");

        shifts.MapGet("/{id:guid}/debrief", (Guid id, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.DebriefAsync(UserId(http), id, ct))
            .Produces<SwipeDebrief>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetSwipeDebrief")
            .WithSummary("Сохранённый Разбор Смены, слой А");

        shifts.MapPost("/{id:guid}/next-card", (Guid id, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.ShowNextCardAsync(UserId(http), id, ct))
            .Produces<ShiftStateView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ShowNextSwipeCard")
            .WithSummary("Показать следующую карточку")
            .WithDescription("Экран просит карточку, когда готов её показать: с этого момента сервер засекает время на неё. Повторный вызов карточку не меняет.");

        shifts.MapPost("/{id:guid}/answer", (Guid id, SwipeAnswerRequest request, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.AnswerAsync(UserId(http), id, request.QuestionId, request.Answer, ct))
            .Produces<AnswerOutcomeView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("AnswerSwipeCard")
            .WithSummary("Ответить на карточку")
            .WithDescription("answer: right, left или unknown («Не знаю»). questionId — карточка, на которую отвечают: ответ на уже отвеченную отклоняется. Вердикт, Пояснение и Шкалы считает сервер; следующую карточку экран просит отдельно.");

        shifts.MapPost("/{id:guid}/timeout", (Guid id, SwipeTimeOutRequest request, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.TimeOutAsync(UserId(http), id, request.QuestionId, ct))
            .Produces<AnswerOutcomeView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("TimeOutSwipeCard")
            .WithSummary("Время вышло")
            .WithDescription("Клиент вызывает, когда обратный отсчёт Цикла дошёл до нуля; засчитывается как «Не знаю». Сервер сверяет лимит по своим часам с допуском в 1 с; ответ позже лимита больше чем на 1 с он и сам засчитывает как «Время вышло».");

        shifts.MapPost("/{id:guid}/next-cycle", (Guid id, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.StartNextCycleAsync(UserId(http), id, ct))
            .Produces<ShiftStateView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("StartNextSwipeCycle")
            .WithSummary("Следующий Цикл")
            .WithDescription("После законченного Цикла «На скорость»: та же колода в новом порядке, лимит на карточку 10 → 7 → 5 с, печать быстрее, Шкалы с начала; в ответе — итоги прошлых Циклов.");

        shifts.MapPost("/{id:guid}/work-on-mistakes", (Guid id, HttpContext http, SwipeShiftService service, CancellationToken ct) =>
                service.StartWorkOnMistakesAsync(UserId(http), id, ct))
            .Produces<ShiftStateView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("StartSwipeWorkOnMistakes")
            .WithSummary("Работа над ошибками")
            .WithDescription("Новая Смена «В своём темпе» только из Вопросов, на которые в законченной Смене была ошибка или «Не знаю», в том числе после Срыва. Шкалы — с начала.");

        return app;
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record StartSwipeShiftRequest(ShiftMode Mode);

public sealed record SwipeAnswerRequest(string QuestionId, SwipeAnswer Answer);

public sealed record SwipeTimeOutRequest(string QuestionId);
