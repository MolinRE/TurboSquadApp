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
        await AddUser("conductor-novice", UserRoles.Conductor);

        var token = await DemoLogin(factory.CreateClient(), "conductor-novice");

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = (await http.GetFromJsonAsync<JsonNode>("/api/auth/me"))!;
        Assert.Equal("conductor-novice", (string)me["username"]!);
        Assert.Equal(["conductor"], me["roles"]!.AsArray().Select(r => (string)r!));
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

    private Task AddUser(string username, string role) => factory.Database(async db =>
    {
        if (await db.Users.AnyAsync(u => u.Username == username)) return 0;
        var user = new AppUser { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), DisplayName = username };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = role });
        db.Users.Add(user);
        return await db.SaveChangesAsync();
    });
}
