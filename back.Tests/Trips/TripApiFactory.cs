using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TurboSquadApp.Data;

namespace TurboSquadApp.Tests.Trips;

/// <summary>Приложение для тестов API Рейса: база в памяти с засеянным контентом и часы, которые двигает тест.</summary>
public sealed class TripApiFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Guid.NewGuid().ToString();

    public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseSetting("Jwt:Key", "test-only-signing-key-at-least-32-characters")
        .UseSetting("ContentSeed:SeedOnStartup", "true")
        .ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_database));
            services.AddSingleton<TimeProvider>(Clock);
        });

    /// <summary>Клиент от имени нового Проводника.</summary>
    public async Task<HttpClient> CreateConductorClient()
    {
        var user = new AppUser { Id = Guid.NewGuid(), Username = $"conductor-{Guid.NewGuid():N}", DisplayName = "Проводник" };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = UserRoles.Conductor });
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        var token = Services.GetRequiredService<JwtTokenService>().CreateToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    public AppDbContext OpenDatabase() => Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
}

/// <summary>Часы, которые двигает тест: серверный таймер Шага считает время по ним.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
