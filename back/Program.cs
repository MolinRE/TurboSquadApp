using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using TurboSquadApp.Data;
using Microsoft.AspNetCore.Identity;
using TurboSquadApp.Events;
using TurboSquadApp.Content;
using TurboSquadApp.Swipes;
using TurboSquadApp.Blitz;
using TurboSquadApp.Trips;
using TurboSquadApp.Voice;
using TurboSquadApp.Analytics;
using TurboSquadApp.Questions;
using TurboSquadApp.Sources;
using TurboSquadApp.Scoring;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(PostgresConnection.Build(builder.Configuration))
        .UseSnakeCaseNamingConvention());   // как в AppDbContextFactory: схема после миграции RenameToSnakeCase
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
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins("http://localhost:3000", "https://localhost:3000", "http://127.0.0.1:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddScoped<DatabaseSession>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<DemoAccountSeeder>();
builder.Services.AddScoped<ContentSeeder>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<VoiceAnalyticsService>();
builder.Services.AddScoped<SwipeShiftService>();
builder.Services.AddScoped<BlitzSessionService>();
builder.Services.AddScoped<KnowledgeScoringService>();
builder.Services.AddScoped<QuestionBankService>();
builder.Services.AddScoped<EventCmsService>();
builder.Services.AddScoped<SourceService>();
builder.Services.AddScoped<QuestionGenerationService>();
builder.Services.AddSingleton(Random.Shared);   // колоды Смены на свайпах и Блица; в тестах — с зерном
var voiceOptions = VoiceOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(voiceOptions);
builder.Services.AddHttpClient<PolzaSttClient>(client =>
{
    client.BaseAddress = new Uri(voiceOptions.PolzaBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<LayaClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<PolzaLlmClient>(client =>
{
    client.BaseAddress = new Uri(voiceOptions.PolzaBaseUrl.TrimEnd('/') + "/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<PolzaQuestionGenerationClient>(client =>
{
    client.BaseAddress = new Uri(voiceOptions.PolzaBaseUrl.TrimEnd('/') + "/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddScoped<IQuestionGenerationClient>(sp => sp.GetRequiredService<PolzaQuestionGenerationClient>());
builder.Services.AddScoped<IVoicePipeline, VoicePipelineService>();
builder.Services.AddScoped<ISttClient>(sp => sp.GetRequiredService<PolzaSttClient>());
builder.Services.AddScoped<ILayaClient>(sp => sp.GetRequiredService<LayaClient>());
builder.Services.AddScoped<ILlmClient>(sp => sp.GetRequiredService<PolzaLlmClient>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "TurboSquadApp API v1");
    });
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// Compose applies committed EF Core migrations before startup seeders use the tables.
if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

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
})
    .Accepts<RegistrationRequest>("application/json")
    .Produces<RegistrationResponse>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .WithName("RegisterConductor")
    .WithSummary("Зарегистрировать Проводника")
    .WithDescription("Создаёт учётную запись Проводника с выбранными Депо и Бригадой. Пароль принимается только для хеширования и никогда не возвращается.")
    .AllowAnonymous();

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    LoginService loginService,
    CancellationToken cancellationToken) =>
{
    var result = await loginService.LoginAsync(request, cancellationToken);
    return result.IsValid
        ? Results.Ok(result.Response)
        : Results.Unauthorized();
})
    .Accepts<LoginRequest>("application/json")
    .Produces<TokenResponse>()
    .Produces(StatusCodes.Status401Unauthorized)
    .WithName("Login")
    .WithSummary("Войти и получить токен")
    .WithDescription("Проверяет логин и пароль и возвращает Bearer-токен для защищённых API-методов. Ошибки входа не раскрывают причину отказа.")
    .AllowAnonymous();

app.MapPost("/api/auth/demo-login", async (
    DemoLoginRequest request,
    LoginService loginService,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!configuration.GetValue("DemoAccounts:LoginEnabled", true)) return Results.NotFound();
    var result = await loginService.DemoLoginAsync(request, cancellationToken);
    return result.IsValid ? Results.Ok(result.Response) : Results.Unauthorized();
})
    .Accepts<DemoLoginRequest>("application/json")
    .Produces<TokenResponse>()
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status404NotFound)
    .WithName("DemoLogin")
    .WithSummary("Войти в демо-аккаунт без пароля")
    .WithDescription("Для кнопок «Войти как…» и жюри: выдаёт Bearer-токен демо-аккаунтам conductor-star, conductor-novice и manager-methodologist без пароля. Другие логины и не засеянные аккаунты — 401. Настройка DemoAccounts:LoginEnabled=false выключает метод — 404.")
    .AllowAnonymous();

app.MapGet("/api/auth/me", async (HttpContext context, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var user = await dbContext.Users
        .Include(item => item.Brigade)
        .Include(item => item.Depot)
        .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
    if (user is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        UserId = user.Id,
        user.Username,
        user.DisplayName,
        Roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().ToArray(),
        Brigade = user.Brigade?.Name,
        Depot = user.Depot?.Name,
    });
})
    .RequireAuthorization()
    .WithName("GetCurrentUser")
    .WithSummary("Получить текущего пользователя")
    .WithDescription("Возвращает идентификатор, логин, роли пользователя из проверенного Bearer-токена, а также его имя, бригаду и депо из базы — их показывают экраны.")
    .Produces(StatusCodes.Status401Unauthorized);

app.MapGet("/api/auth/role-check/manager", () => Results.Ok(new { Role = UserRoles.Manager }))
    .RequireAuthorization(policy => policy.RequireRole(UserRoles.Manager))
    .WithName("CheckManagerRole")
    .WithSummary("Проверить роль Руководителя")
    .WithDescription("Тестовый защищённый метод: доступен только пользователю с ролью Руководителя.")
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/auth/role-check/methodologist", () => Results.Ok(new { Role = UserRoles.Methodologist }))
    .RequireAuthorization(policy => policy.RequireRole(UserRoles.Methodologist))
    .WithName("CheckMethodologistRole")
    .WithSummary("Проверить роль Методиста")
    .WithDescription("Тестовый защищённый метод: доступен только пользователю с ролью Методиста.")
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapEventEndpoints();
app.MapTripEndpoints();
app.MapAnalyticsEndpoints();
app.MapSwipeEndpoints();
app.MapBlitzEndpoints();
app.MapProfileEndpoints();
app.MapQuestionBankEndpoints();
app.MapSourceEndpoints();
app.MapEventCmsEndpoints();

app.Run();

sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
            Description = "Введите JWT-токен без префикса Bearer."
        };

        return Task.CompletedTask;
    }
}

sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (endpointMetadata?.OfType<IAllowAnonymous>().Any() == true ||
            endpointMetadata?.OfType<IAuthorizeData>().Any() != true)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });

        return Task.CompletedTask;
    }
}
