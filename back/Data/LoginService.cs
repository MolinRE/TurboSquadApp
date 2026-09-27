using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

public sealed class LoginService(AppDbContext dbContext, IPasswordHasher<AppUser> passwordHasher, JwtTokenService tokenService)
{
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(request.Username, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(request.Password) ||
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return LoginResult.Invalid;
        }

        return LoginResult.Success(tokenService.CreateToken(user));
    }

    /// <summary>Вход без пароля для кнопок «Войти как…»: пускает только засеянные демо-аккаунты.</summary>
    public async Task<LoginResult> DemoLoginAsync(DemoLoginRequest request, CancellationToken cancellationToken)
    {
        var isDemoAccount = DemoAccountSeeder.Usernames.Contains(request.Username.Trim(), StringComparer.OrdinalIgnoreCase);
        var user = isDemoAccount ? await FindAsync(request.Username, cancellationToken) : null;
        return user is null ? LoginResult.Invalid : LoginResult.Success(tokenService.CreateToken(user));
    }

    private Task<AppUser?> FindAsync(string username, CancellationToken cancellationToken)
    {
        var normalizedUsername = username.Trim().ToUpperInvariant();
        return dbContext.Users
            .Include(item => item.Roles)
            .SingleOrDefaultAsync(item => item.NormalizedUsername == normalizedUsername, cancellationToken);
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record DemoLoginRequest(string Username);

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
