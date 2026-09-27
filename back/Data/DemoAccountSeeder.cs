using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Questions;

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

            // Имя — как на экране «Войти как…»: засев выравнивает его и у уже созданных аккаунтов, пароль не трогает.
            user.DisplayName = definition.DisplayName;

            foreach (var role in definition.Roles)
            {
                if (user.Roles.All(existing => existing.Role != role))
                {
                    user.Roles.Add(new AppUserRole { UserId = user.Id, Role = role });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SeedLeaderboardHistoryAsync(cancellationToken);
    }

    private async Task SeedLeaderboardHistoryAsync(CancellationToken cancellationToken)
    {
        var questions = await dbContext.Questions.AsNoTracking()
            .Where(question => question.Status == QuestionStatuses.Published && question.KnowledgeCost > 0)
            .OrderBy(question => question.Id)
            .Select(question => new { question.Id, question.KnowledgeCost })
            .Take(12)
            .ToListAsync(cancellationToken);

        var names = new[]
        {
            "Алексей Морозов", "Елена Павлова", "Никита Волков", "Дарья Орлова", "Сергей Кузнецов", "Анна Васильева",
            "Дмитрий Фёдоров", "Татьяна Миронова", "Максим Беляев", "Ольга Захарова", "Ирина Сергеева", "Павел Семёнов",
            "Виктория Крылова", "Артём Новиков", "Мария Попова", "Роман Егоров", "Наталья Киселёва", "Кирилл Соколов",
        };
        for (var index = 0; index < names.Length; index++)
        {
            var id = Guid.Parse($"40000000-0000-0000-0000-{index + 1:000000000000}");
            if (!await dbContext.Users.AnyAsync(user => user.Id == id, cancellationToken))
            {
                var brigadeNumber = index / 3 + 1;
                var user = new AppUser
                {
                    Id = id, Username = $"leaderboard-{index + 1:00}",
                    NormalizedUsername = $"LEADERBOARD-{index + 1:00}", DisplayName = names[index],
                    PasswordHash = passwordHasher.HashPassword(new AppUser(), Guid.NewGuid().ToString("N")),
                    DepotId = Guid.Parse($"10000000-0000-0000-0000-{(brigadeNumber <= 3 ? 1 : 2):000000000000}"),
                    BrigadeId = Guid.Parse($"20000000-0000-0000-0000-{brigadeNumber:000000000000}"),
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                user.Roles.Add(new AppUserRole { UserId = id, Role = UserRoles.Conductor });
                dbContext.Users.Add(user);
            }
            if (await dbContext.KnowledgeMasteries.AnyAsync(item => item.UserId == id, cancellationToken)) continue;
            foreach (var question in questions.Take(index % 8 + 1))
                dbContext.KnowledgeMasteries.Add(new KnowledgeMasteryRecord
                {
                    UserId = id, UnitType = "question", UnitId = question.Id, Competence = "knowledge",
                    IsMastered = true, AwardedCost = question.KnowledgeCost, LastAttemptAt = DateTimeOffset.UtcNow,
                });
        }

        foreach (var (username, count) in new[] { ("conductor-star", 12), ("conductor-novice", 2) })
        {
            var userId = await dbContext.Users.Where(user => user.Username == username)
                .Select(user => user.Id).SingleAsync(cancellationToken);
            if (await dbContext.KnowledgeMasteries.AnyAsync(item => item.UserId == userId, cancellationToken)) continue;
            foreach (var question in questions.Take(count))
                dbContext.KnowledgeMasteries.Add(new KnowledgeMasteryRecord
                {
                    UserId = userId, UnitType = "question", UnitId = question.Id, Competence = "knowledge",
                    IsMastered = true, AwardedCost = question.KnowledgeCost, LastAttemptAt = DateTimeOffset.UtcNow,
                });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Логины демо-аккаунтов: только они входят без пароля через демо-вход.</summary>
    public static IReadOnlyList<string> Usernames { get; } = CreateDefinitions().Select(account => account.Username).ToList();

    private static IReadOnlyList<DemoAccountDefinition> CreateDefinitions() =>
    [
        new(
            "conductor-star",
            "Марина Соколова",
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            NorthDepotId,
            NorthBrigadeOneId,
            [UserRoles.Conductor]),
        new(
            "conductor-novice",
            "Игорь Лебедев",
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            NorthDepotId,
            NorthBrigadeTwoId,
            [UserRoles.Conductor]),
        new(
            "manager-methodologist",
            "Ольга Верещагина",
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
