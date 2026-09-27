using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Scoring;

public class LeaderboardApiTests
{
    [Fact]
    public async Task Conductor_sees_all_scopes_and_brigade_averages_from_current_points()
    {
        using var factory = new TripApiFactory();
        using var http = await factory.CreateConductorClient();
        var callerId = await factory.Database(db => db.Users
            .Where(user => user.DepotId == Guid.Empty && user.Roles.Any(role => role.Role == UserRoles.Conductor))
            .Select(user => user.Id).SingleAsync());

        var north = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var south = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var brigades = Enumerable.Range(1, 6)
            .Select(number => Guid.Parse($"20000000-0000-0000-0000-{number:000000000000}"))
            .ToArray();
        await factory.Database(async db =>
        {
            db.Depots.AddRange(new Depot { Id = north, Name = "Северное депо" },
                new Depot { Id = south, Name = "Южное депо" });
            for (var index = 0; index < brigades.Length; index++)
                db.Brigades.Add(new Brigade
                {
                    Id = brigades[index], DepotId = index < 3 ? north : south,
                    Name = $"Бригада {index % 3 + 1}",
                });
            var current = await db.Users.SingleAsync(user => user.Id == callerId);
            current.DepotId = north;
            current.BrigadeId = brigades[0];
            current.DisplayName = "Проводник А";
            for (var index = 0; index < brigades.Length; index++)
            {
                var user = new AppUser
                {
                    Id = Guid.NewGuid(), Username = $"leader-{index}", NormalizedUsername = $"LEADER-{index}",
                    DisplayName = $"Проводник {index + 1}",
                    DepotId = index < 3 ? north : south, BrigadeId = brigades[index],
                };
                user.Roles.Add(new AppUserRole { UserId = user.Id, Role = UserRoles.Conductor });
                db.Users.Add(user);
                db.KnowledgeMasteries.Add(new KnowledgeMasteryRecord
                {
                    UserId = user.Id, UnitType = "question", UnitId = $"q-{index}",
                    Competence = "knowledge", IsMastered = true, AwardedCost = (index + 1) * 10,
                });
            }
            db.KnowledgeMasteries.Add(new KnowledgeMasteryRecord
            {
                UserId = callerId, UnitType = "question", UnitId = "caller",
                Competence = "knowledge", IsMastered = true, AwardedCost = 20,
            });
            var manager = new AppUser
            {
                Id = Guid.NewGuid(), Username = "manager-leader", NormalizedUsername = "MANAGER-LEADER",
                DisplayName = "Руководитель", DepotId = north, BrigadeId = brigades[0],
            };
            manager.Roles.Add(new AppUserRole { UserId = manager.Id, Role = UserRoles.Manager });
            db.Users.Add(manager);
            db.KnowledgeMasteries.Add(new KnowledgeMasteryRecord
            {
                UserId = manager.Id, UnitType = "question", UnitId = "manager",
                Competence = "knowledge", IsMastered = true, AwardedCost = 100,
            });
            await db.SaveChangesAsync();
            return true;
        });

        var response = await http.GetAsync("/api/leaderboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var board = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(board);
        Assert.Equal(callerId, board.CurrentUserId);
        Assert.Equal(new[] { 20, 10 }, board.Brigade.Select(entry => entry.CompetencePoints));
        Assert.Equal(new[] { 30, 20, 20, 10 }, board.Depot.Select(entry => entry.CompetencePoints));
        Assert.Equal(new[] { 60, 50, 40, 30, 20, 20, 10 }, board.Company.Select(entry => entry.CompetencePoints));
        Assert.Equal(6, board.Brigades.Count);
        Assert.Equal(15, board.Brigades.Single(entry => entry.BrigadeId == brigades[0]).AverageCompetencePoints);
        Assert.Equal(2, board.Brigades.Single(entry => entry.BrigadeId == brigades[0]).MemberCount);

        await factory.Database(async db =>
        {
            (await db.KnowledgeMasteries.SingleAsync(item => item.UserId == callerId)).AwardedCost = 70;
            await db.SaveChangesAsync();
            return true;
        });
        var refreshed = await http.GetFromJsonAsync<LeaderboardResponse>("/api/leaderboard");
        Assert.NotNull(refreshed);
        Assert.Equal(callerId, refreshed.Company[0].UserId);
        Assert.Equal(70, refreshed.Company[0].CompetencePoints);
        Assert.Equal(40, refreshed.Brigades.Single(entry => entry.BrigadeId == brigades[0]).AverageCompetencePoints);
        using var manager = await factory.CreateManagerClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/leaderboard")).StatusCode);
    }

    private sealed record LeaderboardResponse(Guid CurrentUserId, List<ConductorEntry> Brigade,
        List<ConductorEntry> Depot, List<ConductorEntry> Company, List<BrigadeEntry> Brigades);
    private sealed record ConductorEntry(Guid UserId, int CompetencePoints);
    private sealed record BrigadeEntry(Guid BrigadeId, double AverageCompetencePoints, int MemberCount);
}
