using System.Security.Claims;
using TurboSquadApp.Data;

namespace TurboSquadApp.Achievements;

public static class AchievementEndpoints
{
    public static IEndpointRouteBuilder MapAchievementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/achievements", async (HttpContext http, AchievementService achievements, CancellationToken ct) =>
            {
                var userId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                return Results.Ok(await achievements.ListAsync(userId, ct));
            })
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Conductor))
            .WithName("GetConductorAchievements")
            .WithSummary("Ачивки Проводника и даты получения");
        return app;
    }
}
