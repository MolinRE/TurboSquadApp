using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using TurboSquadApp.Data;
using TurboSquadApp.Scoring;

namespace TurboSquadApp.Integration;

/// <summary>
/// Интеграционный API для HR (PRD v7 §15): успеваемость Проводника по ключу сервиса, без JWT.
/// externalId — логин (Username): отдельной колонки ExternalId в схеме нет.
/// </summary>
public static class IntegrationEndpoints
{
    public const string ApiKeyHeader = "X-Api-Key";
    private const string NoTopic = "Без темы";

    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integration")
            .AddEndpointFilter(RequireApiKey)
            .AddOpenApiOperationTransformer(RequireApiKeyInOpenApi)
            .WithTags("Интеграция");

        group.MapGet("/employees/{externalId}", async (string externalId, AppDbContext db, CancellationToken ct) =>
            {
                var normalized = externalId.Trim().ToUpperInvariant();
                var user = await Conductors(db).SingleOrDefaultAsync(item => item.NormalizedUsername == normalized, ct);
                return user is null
                    ? Results.NotFound()
                    : Results.Ok((await ProgressAsync(db, [user], withTopics: true, ct))[0]);
            })
            .Produces<EmployeeProgressView>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetEmployeeProgress")
            .WithSummary("Успеваемость Проводника по логину")
            .WithDescription("Для HR-систем. externalId — логин Проводника. Возвращает Очки компетенций, Звание, " +
                "Процент успеваемости в целом и по Темам, итоги Рейсов и время последней активности. " +
                "Нужен заголовок X-Api-Key с ключом сервиса, иначе 401; нет Проводника с таким логином — 404.");

        group.MapGet("/employees", async (string? depot, string? brigade, AppDbContext db, CancellationToken ct) =>
            {
                var users = await Conductors(db)
                    .Where(item => depot == null || item.Depot.Name == depot)
                    .Where(item => brigade == null || item.Brigade.Name == brigade)
                    .OrderBy(item => item.Username)
                    .ToListAsync(ct);
                return Results.Ok(await ProgressAsync(db, users, withTopics: false, ct));
            })
            .Produces<List<EmployeeProgressView>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("ListEmployeeProgress")
            .WithSummary("Успеваемость Проводников подразделения")
            .WithDescription("Для HR-систем. Фильтры depot и brigade — названия Депо и Бригады, оба необязательны. " +
                "Формат как у метода по логину, но без разбивки по Темам. Нужен заголовок X-Api-Key, иначе 401.");
        return app;
    }

    private static async ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Integration:ApiKey"];
        var actual = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
        var valid = !string.IsNullOrEmpty(expected) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
        return valid ? await next(context) : Results.Unauthorized();
    }

    private static Task RequireApiKeyInOpenApi(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var document = context.Document!;
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyHeader,
            In = ParameterLocation.Header,
            Description = "Ключ сервиса интеграции (Integration:ApiKey)."
        };
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
        });
        return Task.CompletedTask;
    }

    private static IQueryable<AppUser> Conductors(AppDbContext db) => db.Users.AsNoTracking()
        .Include(item => item.Depot)
        .Include(item => item.Brigade)
        .Where(item => item.Roles.Any(role => role.Role == UserRoles.Conductor));

    /// <summary>Очки и Звание — как в /api/profile; Процент успеваемости — освоено / встречено Единиц знания.</summary>
    private static async Task<List<EmployeeProgressView>> ProgressAsync(
        AppDbContext db, IReadOnlyList<AppUser> users, bool withTopics, CancellationToken ct)
    {
        var ids = users.Select(user => user.Id).ToList();
        var masteries = await db.KnowledgeMasteries.AsNoTracking()
            .Where(item => ids.Contains(item.UserId))
            .ToListAsync(ct);
        var rankIndexes = await db.ConductorProfiles.AsNoTracking()
            .Where(item => ids.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, item => item.RankIndex, ct);
        var trips = await db.Trips.AsNoTracking()
            .Where(trip => ids.Contains(trip.UserId))
            .Select(trip => new { trip.UserId, trip.Status })
            .ToListAsync(ct);
        var topicOf = withTopics ? await TopicResolverAsync(db, masteries, ct) : null;

        return users.Select(user =>
        {
            var own = masteries.Where(item => item.UserId == user.Id).ToList();
            var points = own.Where(item => item.IsMastered).Sum(item => item.AwardedCost);
            var rankIndex = Math.Max(rankIndexes.GetValueOrDefault(user.Id), KnowledgeRanks.IndexFor(points));
            var ownTrips = trips.Where(trip => trip.UserId == user.Id).ToList();
            var arrived = ownTrips.Count(trip => trip.Status == "arrived");
            var failed = ownTrips.Count(trip => trip.Status == "failed");
            return new EmployeeProgressView(
                user.Username, user.DisplayName, user.Depot?.Name, user.Brigade?.Name,
                points, KnowledgeRanks.Values[rankIndex].Name,
                Knowledge(own),
                topicOf is null
                    ? null
                    : own.GroupBy(topicOf)
                        .Select(group =>
                        {
                            var total = Knowledge(group);
                            return new TopicProgressView(group.Key, total.Encountered, total.Mastered, total.SuccessRate);
                        })
                        .OrderBy(topic => topic.Topic, StringComparer.Ordinal)
                        .ToList(),
                new TripProgressView(arrived + failed, arrived, failed),
                own.Count == 0 ? null : own.Max(item => item.LastAttemptAt));
        }).ToList();
    }

    private static KnowledgeProgressView Knowledge(IEnumerable<KnowledgeMasteryRecord> records)
    {
        var list = records.ToList();
        var mastered = list.Count(item => item.IsMastered);
        return new KnowledgeProgressView(list.Count, mastered,
            list.Count == 0 ? null : Math.Round((double)mastered / list.Count, 2));
    }

    /// <summary>Тема Вопроса — из банка; Тема Шага — поле topic последней опубликованной версии его События.</summary>
    private static async Task<Func<KnowledgeMasteryRecord, string>> TopicResolverAsync(
        AppDbContext db, IReadOnlyList<KnowledgeMasteryRecord> masteries, CancellationToken ct)
    {
        var questionIds = masteries.Where(item => item.UnitType == "question").Select(item => item.UnitId).Distinct().ToList();
        var questionTopics = await db.Questions.AsNoTracking()
            .Where(question => questionIds.Contains(question.Id))
            .ToDictionaryAsync(question => question.Id, question => question.Topic, ct);
        var eventIds = masteries.Where(item => item.UnitType == "step").Select(item => EventIdOf(item.UnitId)).Distinct().ToList();
        var documents = await db.EventDocuments.AsNoTracking()
            .Where(document => eventIds.Contains(document.EventId))
            .ToListAsync(ct);
        var eventTopics = documents.GroupBy(document => document.EventId)
            .ToDictionary(group => group.Key, group => TopicOf(group.MaxBy(document => document.Version)!.Document));

        return item => (item.UnitType switch
        {
            "question" => questionTopics.GetValueOrDefault(item.UnitId),
            "step" => eventTopics.GetValueOrDefault(EventIdOf(item.UnitId)),
            _ => null,
        }) is { Length: > 0 } topic ? topic : NoTopic;
    }

    /// <summary>UnitId Шага — JSON-массив [eventId, stepId] (KnowledgeUnit.EventStep).</summary>
    private static string EventIdOf(string unitId) => JsonSerializer.Deserialize<string[]>(unitId)![0];

    private static string? TopicOf(string document)
    {
        using var json = JsonDocument.Parse(document);
        return json.RootElement.TryGetProperty("topic", out var topic) && topic.ValueKind == JsonValueKind.String
            ? topic.GetString()
            : null;
    }
}

public sealed record EmployeeProgressView(
    string ExternalId, string DisplayName, string? Depot, string? Brigade,
    int CompetencePoints, string Rank,
    KnowledgeProgressView Knowledge,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TopicProgressView>? Topics,
    TripProgressView Trips,
    DateTimeOffset? LastActivityAt);

/// <summary>SuccessRate — доля 0..1 с точностью 0.01; null, если не встречено ни одной Единицы знания.</summary>
public sealed record KnowledgeProgressView(int Encountered, int Mastered, double? SuccessRate);

public sealed record TopicProgressView(string Topic, int Encountered, int Mastered, double? SuccessRate);

/// <summary>Finished — завершённые Рейсы: Прибытие (arrived) и Срыв (failed); идущие не считаются.</summary>
public sealed record TripProgressView(int Finished, int Arrived, int Failed);
