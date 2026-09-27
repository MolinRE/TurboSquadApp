using TurboSquadApp.Data;

namespace TurboSquadApp.Events;

public static class EventCmsEndpoints
{
    public static IEndpointRouteBuilder MapEventCmsEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/cms/events")
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Methodologist));

        events.MapGet("/", (EventCmsService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListCmsEvents").WithSummary("Опубликованные События и версии");
        events.MapGet("/drafts", (Guid? sourceId, EventCmsService service, CancellationToken ct) =>
                service.ListDraftsAsync(sourceId, ct))
            .WithName("ListCmsEventDrafts");
        events.MapPost("/drafts", (EventCmsService service, CancellationToken ct) =>
                service.CreateDraftAsync(ct))
            .WithName("CreateCmsEventDraft");
        events.MapGet("/drafts/{draftId:guid}", (Guid draftId, EventCmsService service, CancellationToken ct) =>
                service.GetDraftAsync(draftId, ct))
            .WithName("GetCmsEventDraft");
        events.MapPut("/drafts/{draftId:guid}",
                (Guid draftId, EventEditorRequest request, EventCmsService service, CancellationToken ct) =>
                    service.SaveDraftAsync(draftId, request, ct))
            .WithName("SaveCmsEventDraft");
        events.MapPost("/drafts/{draftId:guid}/validate",
                (Guid draftId, EventCmsService service, CancellationToken ct) => service.ValidateDraftAsync(draftId, ct))
            .WithName("ValidateCmsEventDraft");
        events.MapPost("/drafts/{draftId:guid}/publish",
                (Guid draftId, EventCmsService service, CancellationToken ct) => service.PublishDraftAsync(draftId, ct))
            .WithName("PublishCmsEventDraft");
        events.MapGet("/{id}/versions/{version:int}",
                (string id, int version, EventCmsService service, CancellationToken ct) =>
                    service.GetVersionAsync(id, version, ct))
            .WithName("GetCmsEventVersion").WithSummary("Документ опубликованной версии События");
        events.MapPost("/{id}/validate",
                (string id, EventEditorRequest request, EventCmsService service, CancellationToken ct) =>
                    service.ValidateAsync(id, request, ct))
            .WithName("ValidateCmsEvent").WithSummary("Проверить JSON События");
        events.MapPost("/{id}/publish",
                (string id, PublishEventRequest request, EventCmsService service, CancellationToken ct) =>
                    service.PublishAsync(id, request, ct))
            .WithName("PublishCmsEvent").WithSummary("Опубликовать следующую версию События");
        return app;
    }
}
