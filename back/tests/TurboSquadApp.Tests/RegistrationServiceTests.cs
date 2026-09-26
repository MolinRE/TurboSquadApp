using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;

namespace TurboSquadApp.Tests;

public sealed class RegistrationServiceTests
{
    private static readonly Guid DepotId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid BrigadeId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Registers_conductor_with_valid_organization_selection()
    {
        await using var context = CreateContext();
        var service = new RegistrationService(context, new PasswordHasher<AppUser>());

        var result = await service.RegisterConductorAsync(
            new RegistrationRequest("conductor.one", "Проводник Один", DepotId, BrigadeId, "correct horse"),
            CancellationToken.None);

        Assert.True(result.IsValid);
        var user = await context.Users.SingleAsync();
        Assert.NotEqual("correct horse", user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, "correct horse"));
    }

    [Fact]
    public async Task Rejects_brigade_from_another_depot()
    {
        await using var context = CreateContext();
        var service = new RegistrationService(context, new PasswordHasher<AppUser>());

        var result = await service.RegisterConductorAsync(
            new RegistrationRequest(
                "conductor.one",
                "Проводник Один",
                DepotId,
                Guid.Parse("20000000-0000-0000-0000-000000000004"),
                "correct horse"),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("brigadeId", result.Errors!.Keys);
    }

    [Fact]
    public async Task Rejects_duplicate_username_case_insensitively()
    {
        await using var context = CreateContext();
        var service = new RegistrationService(context, new PasswordHasher<AppUser>());
        var request = new RegistrationRequest("conductor.one", "Проводник Один", DepotId, BrigadeId, "correct horse");

        Assert.True((await service.RegisterConductorAsync(request, CancellationToken.None)).IsValid);
        var duplicate = await service.RegisterConductorAsync(
            request with { Username = "CONDUCTOR.ONE", DisplayName = "Другой" },
            CancellationToken.None);

        Assert.False(duplicate.IsValid);
        Assert.Contains("username", duplicate.Errors!.Keys);
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
