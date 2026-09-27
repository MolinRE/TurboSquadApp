using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Auth;

// Тикет #25: кнопки «Войти как…» входят в демо-аккаунт без пароля. База — в памяти.
public class DemoLoginTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Demo_account_logs_in_without_password_and_gets_its_roles()
    {
        await AddUser("conductor-novice", UserRoles.Conductor, "Игорь Лебедев");

        var token = await DemoLogin(factory.CreateClient(), "conductor-novice");

        // Экраны берут пользователя из API: имя, роли, бригада и депо.
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = (await http.GetFromJsonAsync<JsonNode>("/api/auth/me"))!;
        Assert.Equal(("conductor-novice", "Игорь Лебедев"), ((string)me["username"]!, (string)me["displayName"]!));
        Assert.Equal(["conductor"], me["roles"]!.AsArray().Select(r => (string)r!));
        Assert.Equal(("Бригада 2", "Северное депо"), ((string)me["brigade"]!, (string)me["depot"]!));
        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" })).StatusCode);
    }

    [Fact]
    public async Task Other_accounts_and_missing_demo_accounts_get_no_token()
    {
        await AddUser("somebody", UserRoles.Conductor);

        var anyone = await factory.CreateClient().PostAsJsonAsync("/api/auth/demo-login", new { username = "somebody" });
        Assert.Equal(HttpStatusCode.Unauthorized, anyone.StatusCode);
        var notSeeded = await factory.CreateClient().PostAsJsonAsync("/api/auth/demo-login", new { username = "manager-methodologist" });
        Assert.Equal(HttpStatusCode.Unauthorized, notSeeded.StatusCode);
    }

    [Fact]
    public async Task Demo_login_is_hidden_when_turned_off()
    {
        await AddUser("conductor-star", UserRoles.Conductor);
        var turnedOff = factory.WithWebHostBuilder(builder => builder.UseSetting("DemoAccounts:LoginEnabled", "false"));

        var response = await turnedOff.CreateClient().PostAsJsonAsync("/api/auth/demo-login", new { username = "conductor-star" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> DemoLogin(HttpClient http, string username)
    {
        var response = await http.PostAsJsonAsync("/api/auth/demo-login", new { username });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["accessToken"]!;
    }

    /// <summary>Пользователь во второй бригаде Северного депо: в базе в памяти их кладёт тест, в Postgres — миграция.</summary>
    private Task AddUser(string username, string role, string? displayName = null) => factory.Database(async db =>
    {
        var depotId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var brigadeId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        if (!await db.Depots.AnyAsync(d => d.Id == depotId)) db.Depots.Add(new Depot { Id = depotId, Name = "Северное депо" });
        if (!await db.Brigades.AnyAsync(b => b.Id == brigadeId))
            db.Brigades.Add(new Brigade { Id = brigadeId, DepotId = depotId, Name = "Бригада 2" });
        if (await db.Users.AnyAsync(u => u.Username == username)) return await db.SaveChangesAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = displayName ?? username, DepotId = depotId, BrigadeId = brigadeId,
        };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = role });
        db.Users.Add(user);
        return await db.SaveChangesAsync();
    });
}
