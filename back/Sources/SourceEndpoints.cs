using TurboSquadApp.Data;

namespace TurboSquadApp.Sources;

public static class SourceEndpoints
{
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var sources = app.MapGroup("/api/cms/sources")
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Methodologist));

        sources.MapGet("/", (SourceService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListCmsSources");
        sources.MapPost("/", (CreateSourceInput input, SourceService service, CancellationToken ct) =>
                service.CreateAsync(input, ct))
            .WithName("CreateCmsSource");
        sources.MapGet("/{id:guid}", (Guid id, SourceService service, CancellationToken ct) =>
                service.GetAsync(id, ct))
            .WithName("GetCmsSource");
        sources.MapPost("/{id:guid}/generate", async (Guid id, HttpRequest request,
                QuestionGenerationService service, CancellationToken ct) =>
            {
                var input = request.ContentLength > 0
                    ? await request.ReadFromJsonAsync<GenerationModelRequest>(ct) : null;
                return await service.GenerateAsync(id, input?.Model, ct);
            })
            .WithName("GenerateCmsSourceQuestions");
        sources.MapGet("/{id:guid}/drafts", (Guid id, QuestionGenerationService service, CancellationToken ct) =>
                service.DraftsAsync(id, ct))
            .WithName("ListCmsSourceDrafts");
        sources.MapPost("/{id:guid}/generate-events", async (Guid id, GenerationModelRequest input,
                EventGenerationService service, CancellationToken ct) =>
                await service.GenerateAsync(id, input.Model, ct))
            .WithName("GenerateCmsSourceEvents");
        return app;
    }
}
