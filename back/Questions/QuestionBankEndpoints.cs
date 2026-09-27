using TurboSquadApp.Data;

namespace TurboSquadApp.Questions;

public static class QuestionBankEndpoints
{
    public static IEndpointRouteBuilder MapQuestionBankEndpoints(this IEndpointRouteBuilder app)
    {
        var questions = app.MapGroup("/api/cms/questions")
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Methodologist));

        questions.MapGet("/", (string? topic, string? category, string? serviceClass, string? type,
                string? status, QuestionBankService service, CancellationToken ct) =>
                service.ListAsync(topic, category, serviceClass, type, status, ct))
            .WithName("ListCmsQuestions").WithSummary("Банк Вопросов Методиста");
        questions.MapGet("/catalog", (QuestionBankService service, CancellationToken ct) =>
                service.CatalogAsync(ct))
            .WithName("GetCmsQuestionCatalog").WithSummary("Справочники для формы Вопроса");
        questions.MapGet("/{id}", (string id, QuestionBankService service, CancellationToken ct) =>
                service.GetAsync(id, ct))
            .WithName("GetCmsQuestion").WithSummary("Открыть Вопрос");
        questions.MapPost("/", (QuestionEditorInput input, QuestionBankService service, CancellationToken ct) =>
                service.CreateAsync(input, ct))
            .WithName("CreateCmsQuestion").WithSummary("Создать Черновик Вопроса");
        questions.MapPut("/{id}", (string id, QuestionEditorInput input, QuestionBankService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateCmsQuestion").WithSummary("Сохранить правку Вопроса");
        questions.MapPost("/{id}/publish", (string id, QuestionBankService service, CancellationToken ct) =>
                service.PublishAsync(id, ct))
            .WithName("PublishCmsQuestion").WithSummary("Опубликовать Вопрос");
        questions.MapPost("/{id}/unpublish", (string id, QuestionBankService service, CancellationToken ct) =>
                service.UnpublishAsync(id, ct))
            .WithName("UnpublishCmsQuestion").WithSummary("Снять Вопрос с публикации");
        questions.MapDelete("/{id}", (string id, QuestionBankService service, CancellationToken ct) =>
                service.DeleteAsync(id, ct))
            .WithName("DeleteCmsQuestion").WithSummary("Удалить Черновик Вопроса");
        return app;
    }
}
