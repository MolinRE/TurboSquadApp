using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;

namespace TurboSquadApp.Scoring;

public static class LeaderboardEndpoints
{
    public static IEndpointRouteBuilder MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/leaderboard", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
            {
                var currentUserId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var current = await db.Users.AsNoTracking()
                    .Where(user => user.Id == currentUserId)
                    .Select(user => new { user.BrigadeId, user.DepotId })
                    .SingleAsync(ct);
                var conductors = await db.Users.AsNoTracking()
                    .Include(user => user.Brigade)
                    .Include(user => user.Depot)
                    .Where(user => user.Roles.Any(role => role.Role == UserRoles.Conductor))
                    .ToListAsync(ct);
                var awarded = await db.KnowledgeMasteries.AsNoTracking()
                    .Where(item => item.IsMastered)
                    .Select(item => new { item.UserId, item.AwardedCost })
                    .ToListAsync(ct);
                var points = awarded.GroupBy(item => item.UserId)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.AwardedCost));
                var brigades = await db.Brigades.AsNoTracking().Include(brigade => brigade.Depot).ToListAsync(ct);

                return Results.Ok(new LeaderboardView(
                    currentUserId,
                    RankConductors(conductors.Where(user => user.BrigadeId == current.BrigadeId), points),
                    RankConductors(conductors.Where(user => user.DepotId == current.DepotId), points),
                    RankConductors(conductors, points),
                    RankBrigades(brigades, conductors, points)));
            })
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Conductor))
            .WithName("GetLeaderboard")
            .WithSummary("Общий лидерборд Проводников и рейтинг бригад по Очкам компетенций");
        return app;
    }

    private static List<ConductorLeaderboardEntry> RankConductors(
        IEnumerable<AppUser> conductors, IReadOnlyDictionary<Guid, int> points)
    {
        return conductors.Select(user => new ConductorLeaderboardEntry(
                0, user.Id, user.DisplayName, user.Brigade.Name, user.Depot.Name,
                points.GetValueOrDefault(user.Id)))
            .OrderByDescending(entry => entry.CompetencePoints)
            .ThenBy(entry => entry.DisplayName, StringComparer.Ordinal)
            .ThenBy(entry => entry.UserId)
            .Select((entry, index) => entry with { Position = index + 1 })
            .ToList();
    }

    private static List<BrigadeLeaderboardEntry> RankBrigades(
        IEnumerable<Brigade> brigades, IReadOnlyList<AppUser> conductors, IReadOnlyDictionary<Guid, int> points)
    {
        var members = conductors.GroupBy(user => user.BrigadeId)
            .ToDictionary(group => group.Key, group => group.ToList());
        return brigades.Select(brigade =>
            {
                var brigadeMembers = members.GetValueOrDefault(brigade.Id) ?? [];
                return new BrigadeLeaderboardEntry(
                    0, brigade.Id, brigade.Name, brigade.Depot.Name,
                    brigadeMembers.Count == 0 ? null : brigadeMembers.Average(user => points.GetValueOrDefault(user.Id)),
                    brigadeMembers.Count);
            })
            .OrderByDescending(entry => entry.AverageCompetencePoints)
            .ThenBy(entry => entry.DepotName, StringComparer.Ordinal)
            .ThenBy(entry => entry.BrigadeName, StringComparer.Ordinal)
            .ThenBy(entry => entry.BrigadeId)
            .Select((entry, index) => entry with { Position = index + 1 })
            .ToList();
    }
}

public sealed record LeaderboardView(Guid CurrentUserId,
    IReadOnlyList<ConductorLeaderboardEntry> Brigade,
    IReadOnlyList<ConductorLeaderboardEntry> Depot,
    IReadOnlyList<ConductorLeaderboardEntry> Company,
    IReadOnlyList<BrigadeLeaderboardEntry> Brigades);

public sealed record ConductorLeaderboardEntry(int Position, Guid UserId, string DisplayName,
    string BrigadeName, string DepotName, int CompetencePoints);

public sealed record BrigadeLeaderboardEntry(int Position, Guid BrigadeId, string BrigadeName,
    string DepotName, double? AverageCompetencePoints, int MemberCount);
