using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Integration;

public class IntegrationApiTests
{
    private const string ApiKey = "test-integration-key";

    [Fact]
    public async Task Hr_reads_conductor_progress_by_login_with_service_key()
    {
        using var trips = new TripApiFactory();
        using var app = trips.WithWebHostBuilder(builder => builder.UseSetting("Integration:ApiKey", ApiKey));
        var start = DateTimeOffset.Parse("2026-09-27T10:00:00+03:00");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var depot = new Depot { Id = Guid.NewGuid(), Name = "Северное депо" };
            var brigade = new Brigade { Id = Guid.NewGuid(), DepotId = depot.Id, Name = "Бригада 1" };
            db.Depots.Add(depot);
            db.Brigades.Add(brigade);
            var conductor = User("hr-ivanov", "Иван Иванов", UserRoles.Conductor, depot, brigade);
            db.Users.AddRange(conductor, User("hr-manager", "Руководитель", UserRoles.Manager, depot, brigade));
            db.ConductorProfiles.Add(new ConductorProfileRecord { UserId = conductor.Id, RankIndex = 1 });

            db.Questions.AddRange(Question("hr-safe-1", "Безопасность"), Question("hr-safe-2", "Безопасность"),
                Question("hr-safe-3", "Безопасность"), Question("hr-ticket", "Билеты"));
            db.EventDocuments.AddRange(
                new EventDocumentRecord { EventId = "hr-event", Version = 1, Document = """{"topic":"Старая тема"}""" },
                new EventDocumentRecord { EventId = "hr-event", Version = 2, Document = """{"topic":"Посадка"}""" });
            db.KnowledgeMasteries.AddRange(
                Mastery(conductor, "question", "hr-safe-1", true, 10, start),
                Mastery(conductor, "question", "hr-safe-2", false, 0, start.AddMinutes(1)),
                Mastery(conductor, "question", "hr-safe-3", false, 0, start.AddMinutes(2)),
                Mastery(conductor, "question", "hr-ticket", true, 15, start.AddMinutes(5)),
                Mastery(conductor, "step", """["hr-event","s1"]""", false, 0, start.AddMinutes(3)));
            db.Trips.AddRange(Trip(conductor, "arrived"), Trip(conductor, "failed"), Trip(conductor, "running"));
            await db.SaveChangesAsync();
        }
        using var http = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/integration/employees/hr-ivanov")).StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/integration/employees/hr-ivanov")).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Api-Key");
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/integration/employees/nobody")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/integration/employees/hr-manager")).StatusCode);

        var response = await http.GetAsync("/api/integration/employees/hr-ivanov");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employee = await response.Content.ReadFromJsonAsync<Employee>();
        Assert.NotNull(employee);
        Assert.Equal("hr-ivanov", employee.ExternalId);
        Assert.Equal("Северное депо", employee.Depot);
        Assert.Equal("Бригада 1", employee.Brigade);
        Assert.Equal(25, employee.CompetencePoints);
        Assert.Equal("Проводник", employee.Rank);   // RankIndex профиля выше, чем по 25 Очкам
        Assert.Equal(new Knowledge(5, 2, 0.4), employee.Knowledge);
        Assert.Equal(new[]
        {
            new TopicEntry("Безопасность", 3, 1, 0.33),
            new TopicEntry("Билеты", 1, 1, 1),
            new TopicEntry("Посадка", 1, 0, 0),
        }, employee.Topics);
        Assert.Equal(new Trips(2, 1, 1), employee.Trips);
        Assert.Equal(start.AddMinutes(5), employee.LastActivityAt);

        var list = await http.GetFromJsonAsync<JsonElement>(
            "/api/integration/employees?depot=Северное депо&brigade=Бригада 1");
        var only = Assert.Single(list.EnumerateArray());
        Assert.Equal("hr-ivanov", only.GetProperty("externalId").GetString());
        Assert.Equal(25, only.GetProperty("competencePoints").GetInt32());
        Assert.False(only.TryGetProperty("topics", out _));
    }

    private static AppUser User(string username, string name, string role, Depot depot, Brigade brigade)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = name, DepotId = depot.Id, BrigadeId = brigade.Id,
        };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = role });
        return user;
    }

    private static QuestionRecord Question(string id, string topic) => new()
    {
        Id = id, Type = "swipe", Status = "published", Statement = id, Topic = topic,
        ExplanationText = "-", ExplanationKeyFact = "-", Source = "-",
    };

    private static KnowledgeMasteryRecord Mastery(AppUser user, string type, string id, bool mastered, int cost,
        DateTimeOffset at) => new()
    {
        UserId = user.Id, UnitType = type, UnitId = id, Competence = "knowledge",
        IsMastered = mastered, AwardedCost = cost, LastAttemptAt = at,
    };

    private static TripRecord Trip(AppUser user, string status) => new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, ServiceClass = "standard", Status = status,
        Directory = "{}", Settings = "{}", EventVersions = "{}",
    };

    private sealed record Employee(string ExternalId, string DisplayName, string? Depot, string? Brigade,
        int CompetencePoints, string Rank, Knowledge Knowledge, List<TopicEntry> Topics, Trips Trips,
        DateTimeOffset? LastActivityAt);
    private sealed record Knowledge(int Encountered, int Mastered, double? SuccessRate);
    private sealed record TopicEntry(string Topic, int Encountered, int Mastered, double? SuccessRate);
    private sealed record Trips(int Finished, int Arrived, int Failed);
}
