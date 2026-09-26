using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TurboSquadApp.Data;

public sealed class JwtOptions
{
    public required string Key { get; init; }
    public string Issuer { get; init; } = "TurboSquadApp";
    public string Audience { get; init; } = "TurboSquadApp";
    public int ExpirationMinutes { get; init; } = 60;

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");
        }

        var expiration = configuration.GetValue("Jwt:ExpirationMinutes", 60);
        if (expiration is < 1 or > 1440)
        {
            throw new InvalidOperationException("Jwt:ExpirationMinutes must be between 1 and 1440.");
        }

        return new JwtOptions
        {
            Key = key,
            Issuer = configuration["Jwt:Issuer"] ?? "TurboSquadApp",
            Audience = configuration["Jwt:Audience"] ?? "TurboSquadApp",
            ExpirationMinutes = expiration
        };
    }
}

public sealed class JwtTokenService(JwtOptions options)
{
    public TokenResponse CreateToken(AppUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(options.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.Role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expiresAt);
    }
}
