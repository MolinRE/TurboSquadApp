using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TurboSquadApp.Data;

namespace TurboSquadApp.Tests;

public sealed class DemoAccountSeederTests
{
    [Fact]
    public async Task Seeds_three_demo_accounts_and_is_idempotent()
    {
        await using var context = CreateContext();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoAccounts:conductor-star:Password"] = "star-password",
                ["DemoAccounts:conductor-novice:Password"] = "novice-password",
                ["DemoAccounts:manager-methodologist:Password"] = "manager-password"
            })
            .Build();
        var seeder = new DemoAccountSeeder(context, new PasswordHasher<AppUser>(), configuration);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        var users = await context.Users.Include(user => user.Roles).ToListAsync();
        Assert.Equal(3, users.Count);
        Assert.Equal(4, users.SelectMany(user => user.Roles).Count());

        var manager = users.Single(user => user.Username == "manager-methodologist");
        Assert.Equal(
            [UserRoles.Manager, UserRoles.Methodologist],
            manager.Roles.Select(role => role.Role).OrderBy(role => role));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
