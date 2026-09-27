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
        sources.MapPost("/{id:guid}/generate", (Guid id, QuestionGenerationService service, CancellationToken ct) =>
                service.GenerateAsync(id, ct))
            .WithName("GenerateCmsSourceQuestions");
        sources.MapGet("/{id:guid}/drafts", (Guid id, QuestionGenerationService service, CancellationToken ct) =>
                service.DraftsAsync(id, ct))
            .WithName("ListCmsSourceDrafts");
        return app;
    }
}
