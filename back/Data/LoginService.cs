using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

public sealed class LoginService(AppDbContext dbContext, IPasswordHasher<AppUser> passwordHasher, JwtTokenService tokenService)
{
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedUsername = request.Username.Trim().ToUpperInvariant();
        var user = await dbContext.Users
            .Include(item => item.Roles)
            .SingleOrDefaultAsync(item => item.NormalizedUsername == normalizedUsername, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(request.Password) ||
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return LoginResult.Invalid;
        }

        return LoginResult.Success(tokenService.CreateToken(user));
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed class LoginResult
{
    private LoginResult(TokenResponse? response)
    {
        Response = response;
    }

    public TokenResponse? Response { get; }
    public bool IsValid => Response is not null;
    public static LoginResult Invalid { get; } = new(null);
    public static LoginResult Success(TokenResponse response) => new(response);
}
