using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TurboSquadApp.Data;

namespace TurboSquadApp.Tests.Auth;

public class DemoAccountSeederTests
{
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private readonly IConfiguration _passwords = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoAccounts:conductor-star:Password"] = "star-password",
            ["DemoAccounts:conductor-novice:Password"] = "novice-password",
            ["DemoAccounts:manager-methodologist:Password"] = "manager-password",
        })
        .Build();

    [Fact]
    public async Task Demo_accounts_get_names_of_people_and_existing_ones_are_renamed_without_new_password()
    {
        await using (var db = new AppDbContext(_options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new AppUser
            {
                Id = Guid.Parse("30000000-0000-0000-0000-000000000002"), Username = "conductor-novice",
                NormalizedUsername = "CONDUCTOR-NOVICE", DisplayName = "Проводник-новичок", PasswordHash = "old-hash",
                DepotId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                BrigadeId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(_options))
            await new DemoAccountSeeder(db, new PasswordHasher<AppUser>(), _passwords).SeedAsync(CancellationToken.None);

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(
                [("conductor-novice", "Игорь Лебедев"), ("conductor-star", "Марина Соколова"), ("manager-methodologist", "Ольга Верещагина")],
                (await db.Users.Where(u => DemoAccountSeeder.Usernames.Contains(u.Username))
                    .OrderBy(u => u.Username).ToListAsync()).Select(u => (u.Username, u.DisplayName)));
            Assert.Equal("old-hash", (await db.Users.SingleAsync(u => u.Username == "conductor-novice")).PasswordHash);
        }
    }
}
