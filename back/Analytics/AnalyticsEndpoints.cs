using TurboSquadApp.Data;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Analytics;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/analytics/voice", (VoiceAnalyticsService service, CancellationToken cancellationToken) =>
                service.BuildAsync(cancellationToken))
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Manager))
            .Produces<VoiceAnalyticsView>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetVoiceAnalytics")
            .WithSummary("Аналитика голосового конвейера")
            .WithDescription("Latency STT, Laya и LLM, доли неуверенных решений и продуктовых fallback-режимов, а также срезы по Шагам и версиям Событий.");

        return app;
    }
}
