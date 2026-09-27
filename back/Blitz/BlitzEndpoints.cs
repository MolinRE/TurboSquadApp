using System.Security.Claims;
using TurboSquadApp.Data;

namespace TurboSquadApp.Blitz;

public static class BlitzEndpoints
{
    public static IEndpointRouteBuilder MapBlitzEndpoints(this IEndpointRouteBuilder app)
    {
        // Сессию видит только Проводник, который её начал: чужая сессия — 404. Отказ правил — 409 с кодом причины в поле reason.
        var sessions = app.MapGroup("/api/blitz-sessions").RequireAuthorization(policy => policy.RequireRole(UserRoles.Conductor));

        sessions.MapPost("/", (HttpContext http, BlitzSessionService service, CancellationToken ct) =>
                service.StartAsync(UserId(http), ct))
            .Produces<BlitzSessionView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("StartBlitzSession")
            .WithSummary("Начать Блиц")
            .WithDescription("Колода — до 10 опубликованных Вопросов single, multiple и sequence в случайном порядке; шаги sequence перемешаны и получают id по месту показа, чтобы ни порядок, ни id не выдавали ответ; содержимое Вопросов сохраняется снимком. Первый Вопрос показан сразу, время на него уже идёт.");

        sessions.MapGet("/{id:guid}", (Guid id, HttpContext http, BlitzSessionService service, CancellationToken ct) =>
                service.GetAsync(UserId(http), id, ct))
            .Produces<BlitzSessionView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetBlitzSession")
            .WithSummary("Текущее состояние Блица")
            .WithDescription("Прогресс, показанный Вопрос без верного варианта или итог: чтобы продолжить сессию после перезагрузки.");

        sessions.MapPost("/{id:guid}/next-question", (Guid id, HttpContext http, BlitzSessionService service, CancellationToken ct) =>
                service.ShowNextQuestionAsync(UserId(http), id, ct))
            .Produces<BlitzSessionView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ShowNextBlitzQuestion")
            .WithSummary("Показать следующий Вопрос")
            .WithDescription("Экран просит Вопрос после панели с Пояснением: с этого момента сервер засекает время на него. Повторный вызов Вопрос и время не меняет.");

        sessions.MapPost("/{id:guid}/answer", (Guid id, BlitzAnswerRequest request, HttpContext http, BlitzSessionService service, CancellationToken ct) =>
                service.AnswerAsync(UserId(http), id, request.QuestionId, request.SelectedOptionIds ?? [], request.OrderedStepIds ?? [], ct))
            .Produces<BlitzAnswerOutcomeView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("AnswerBlitzQuestion")
            .WithSummary("Ответить на Вопрос")
            .WithDescription("questionId — Вопрос, на который отвечают: повторный ответ отклоняется. single — ровно один id в selectedOptionIds; multiple — один или несколько id без повторов, «верно» только за точный набор верных в любом порядке, неполный или лишний набор — «неверно»; sequence — все шаги в orderedStepIds, «верно» только за полный верный порядок, в ответе correctOptionIds — id шагов в верном порядке. Ответ позже лимита больше чем на 1 с по часам сервера засчитывается как «Время вышло».");

        sessions.MapPost("/{id:guid}/timeout", (Guid id, BlitzTimeOutRequest request, HttpContext http, BlitzSessionService service, CancellationToken ct) =>
                service.TimeOutAsync(UserId(http), id, request.QuestionId, ct))
            .Produces<BlitzAnswerOutcomeView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("TimeOutBlitzQuestion")
            .WithSummary("Время вышло")
            .WithDescription("Клиент вызывает, когда обратный отсчёт дошёл до нуля; засчитывается как «Не знаю». Сервер сверяет лимит по своим часам с допуском в 1 с.");

        return app;
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record BlitzAnswerRequest(string QuestionId, IReadOnlyList<string>? SelectedOptionIds, IReadOnlyList<string>? OrderedStepIds);

public sealed record BlitzTimeOutRequest(string QuestionId);
