using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;

namespace TurboSquadApp.Scoring;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
            {
                var userId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var points = await db.KnowledgeMasteries.AsNoTracking()
                    .Where(item => item.UserId == userId && item.IsMastered)
                    .SumAsync(item => item.AwardedCost, ct);
                var profile = await db.ConductorProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.UserId == userId, ct);
                var rankIndex = Math.Max(profile?.RankIndex ?? 0, KnowledgeRanks.IndexFor(points));
                return Results.Ok(new ProfileView(points, points, KnowledgeRanks.Values[rankIndex].Name));
            })
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Conductor))
            .WithName("GetConductorProfile")
            .WithSummary("Очки компетенций и Звание Проводника");
        return app;
    }
}

public sealed record ProfileView(int KnowledgePoints, int CompetencePoints, string Rank);
