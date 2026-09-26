using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

public sealed class RegistrationService(AppDbContext dbContext, IPasswordHasher<AppUser> passwordHasher)
{
    public async Task<RegistrationResult> RegisterConductorAsync(
        RegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var displayName = request.DisplayName.Trim();
        var normalizedUsername = username.ToUpperInvariant();

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (username.Length is < 3 or > 100)
        {
            errors["username"] = ["Username must be between 3 and 100 characters."];
        }

        if (displayName.Length is < 1 or > 200)
        {
            errors["displayName"] = ["Display name is required and must be at most 200 characters."];
        }

        if (request.Password.Length < 8)
        {
            errors["password"] = ["Password must contain at least 8 characters."];
        }

        var depot = await dbContext.Depots.SingleOrDefaultAsync(
            item => item.Id == request.DepotId,
            cancellationToken);
        var brigade = await dbContext.Brigades.SingleOrDefaultAsync(
            item => item.Id == request.BrigadeId && item.DepotId == request.DepotId,
            cancellationToken);

        if (depot is null)
        {
            errors["depotId"] = ["Selected depot does not exist."];
        }

        if (brigade is null)
        {
            errors["brigadeId"] = ["Selected brigade does not exist in the selected depot."];
        }

        if (await dbContext.Users.AnyAsync(user => user.NormalizedUsername == normalizedUsername, cancellationToken))
        {
            errors["username"] = ["This username is already registered."];
        }

        if (errors.Count > 0)
        {
            return RegistrationResult.Invalid(errors);
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalizedUsername,
            DisplayName = displayName,
            DepotId = depot!.Id,
            BrigadeId = brigade!.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return RegistrationResult.Success(new RegistrationResponse(
            user.Id,
            user.Username,
            user.DisplayName,
            user.DepotId,
            user.BrigadeId));
    }
}

public sealed record RegistrationRequest(
    string Username,
    string DisplayName,
    Guid DepotId,
    Guid BrigadeId,
    string Password);

public sealed record RegistrationResponse(
    Guid Id,
    string Username,
    string DisplayName,
    Guid DepotId,
    Guid BrigadeId);

public sealed class RegistrationResult
{
    private RegistrationResult(RegistrationResponse? response, IReadOnlyDictionary<string, string[]>? errors)
    {
        Response = response;
        Errors = errors;
    }

    public RegistrationResponse? Response { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }
    public bool IsValid => Response is not null;

    public static RegistrationResult Success(RegistrationResponse response) => new(response, null);
    public static RegistrationResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
