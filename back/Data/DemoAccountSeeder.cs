using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

public sealed class DemoAccountSeeder(
    AppDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IConfiguration configuration)
{
    private static readonly Guid NorthDepotId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid NorthBrigadeOneId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid NorthBrigadeTwoId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var accounts = CreateDefinitions();
        var missingPasswords = accounts
            .Where(account => string.IsNullOrWhiteSpace(GetPassword(account.Key)))
            .Select(account => account.Key)
            .ToArray();
        if (missingPasswords.Length > 0)
        {
            throw new InvalidOperationException(
                $"Demo account passwords are missing for: {string.Join(", ", missingPasswords)}.");
        }

        foreach (var definition in accounts)
        {
            var user = await dbContext.Users
                .Include(item => item.Roles)
                .SingleOrDefaultAsync(
                    item => item.NormalizedUsername == definition.Username.ToUpperInvariant(),
                    cancellationToken);

            if (user is null)
            {
                user = new AppUser
                {
                    Id = definition.Id,
                    Username = definition.Username,
                    NormalizedUsername = definition.Username.ToUpperInvariant(),
                    DisplayName = definition.DisplayName,
                    PasswordHash = passwordHasher.HashPassword(new AppUser(), GetPassword(definition.Key)!),
                    DepotId = definition.DepotId,
                    BrigadeId = definition.BrigadeId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.Users.Add(user);
            }

            foreach (var role in definition.Roles)
            {
                if (user.Roles.All(existing => existing.Role != role))
                {
                    user.Roles.Add(new AppUserRole { UserId = user.Id, Role = role });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Логины демо-аккаунтов: только они входят без пароля через демо-вход.</summary>
    public static IReadOnlyList<string> Usernames { get; } = CreateDefinitions().Select(account => account.Username).ToList();

    private static IReadOnlyList<DemoAccountDefinition> CreateDefinitions() =>
    [
        new(
            "conductor-star",
            "Проводник-отличник",
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            NorthDepotId,
            NorthBrigadeOneId,
            [UserRoles.Conductor]),
        new(
            "conductor-novice",
            "Проводник-новичок",
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            NorthDepotId,
            NorthBrigadeTwoId,
            [UserRoles.Conductor]),
        new(
            "manager-methodologist",
            "Руководитель-методист",
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            NorthDepotId,
            NorthBrigadeOneId,
            [UserRoles.Manager, UserRoles.Methodologist])
    ];

    private string? GetPassword(string key) => configuration[$"DemoAccounts:{key}:Password"];

    private sealed record DemoAccountDefinition(
        string Key,
        string DisplayName,
        Guid Id,
        Guid DepotId,
        Guid BrigadeId,
        IReadOnlyList<string> Roles)
    {
        public string Username => Key;
    }
}
