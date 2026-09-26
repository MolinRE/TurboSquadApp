using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TurboSquadApp.Data;
using Microsoft.AspNetCore.Identity;
using TurboSquadApp.Events;
using TurboSquadApp.Content;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(PostgresConnection.Build(builder.Configuration)));
var jwtOptions = JwtOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<DatabaseSession>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<DemoAccountSeeder>();
builder.Services.AddScoped<ContentSeeder>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Стартовый контент Рейса (справочники, Заступ, №6, №33): добавляется только недостающее.
if (app.Configuration.GetValue("ContentSeed:SeedOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<ContentSeeder>().SeedAsync(CancellationToken.None);
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
    {
        throw new InvalidOperationException(
            "Content tables are missing: apply migrations (dotnet ef database update --project back) " +
            "or disable seeding with ContentSeed__SeedOnStartup=false.", ex);
    }
}

if (app.Configuration.GetValue("DemoAccounts:SeedOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoAccountSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

app.MapPost("/api/auth/register", async (
    RegistrationRequest request,
    RegistrationService registrationService,
    CancellationToken cancellationToken) =>
{
    var result = await registrationService.RegisterConductorAsync(request, cancellationToken);
    return result.IsValid
        ? Results.Created($"/api/users/{result.Response!.Id}", result.Response)
        : Results.ValidationProblem(result.Errors!);
});

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    LoginService loginService,
    CancellationToken cancellationToken) =>
{
    var result = await loginService.LoginAsync(request, cancellationToken);
    return result.IsValid
        ? Results.Ok(result.Response)
        : Results.Unauthorized();
});

app.MapGet("/api/auth/me", (HttpContext context) =>
    Results.Ok(new
    {
        UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
        Username = context.User.Identity?.Name,
        Roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().ToArray()
    }))
    .RequireAuthorization();

app.MapGet("/api/auth/role-check/manager", () => Results.Ok(new { Role = UserRoles.Manager }))
    .RequireAuthorization(policy => policy.RequireRole(UserRoles.Manager));

app.MapGet("/api/auth/role-check/methodologist", () => Results.Ok(new { Role = UserRoles.Methodologist }))
    .RequireAuthorization(policy => policy.RequireRole(UserRoles.Methodologist));

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast");

app.MapEventEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
