using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;

namespace TurboSquadApp.Tests;

public sealed class LoginServiceTests
{
    [Fact]
    public async Task Returns_token_with_user_and_role_claims_for_valid_credentials()
    {
        await using var context = CreateContext();
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "conductor.one",
            NormalizedUsername = "CONDUCTOR.ONE",
            DisplayName = "Проводник Один",
            PasswordHash = string.Empty,
            DepotId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            BrigadeId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "correct horse");
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = UserRoles.Conductor });
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new LoginService(
            context,
            hasher,
            new JwtTokenService(new JwtOptions
            {
                Key = "a-test-signing-key-with-at-least-32-characters",
                ExpirationMinutes = 30
            }));

        var result = await service.LoginAsync(
            new LoginRequest("CONDUCTOR.ONE", "correct horse"),
            CancellationToken.None);

        Assert.True(result.IsValid);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Response!.AccessToken);
        Assert.Equal(user.Id.ToString(), token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(UserRoles.Conductor, token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.Equal("Bearer", result.Response.TokenType);
    }

    [Fact]
    public async Task Returns_the_same_invalid_result_for_unknown_user_and_wrong_password()
    {
        await using var context = CreateContext();
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "conductor.one",
            NormalizedUsername = "CONDUCTOR.ONE",
            DisplayName = "Проводник Один",
            DepotId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            BrigadeId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "correct horse");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new LoginService(
            context,
            hasher,
            new JwtTokenService(new JwtOptions { Key = "a-test-signing-key-with-at-least-32-characters" }));

        var wrongPassword = await service.LoginAsync(
            new LoginRequest("conductor.one", "wrong password"), CancellationToken.None);
        var unknownUser = await service.LoginAsync(
            new LoginRequest("missing", "wrong password"), CancellationToken.None);

        Assert.False(wrongPassword.IsValid);
        Assert.False(unknownUser.IsValid);
        Assert.Null(wrongPassword.Response);
        Assert.Null(unknownUser.Response);
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
