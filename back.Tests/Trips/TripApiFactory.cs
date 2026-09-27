using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TurboSquadApp.Data;
using TurboSquadApp.Voice;
using TurboSquadApp.Sources;

namespace TurboSquadApp.Tests.Trips;

/// <summary>
/// Приложение для тестов API игр: база в памяти с засеянным контентом, часы, которые двигает тест,
/// и случайность с зерном — колода Смены на свайпах одна и та же от запуска к запуску.
/// </summary>
public sealed class TripApiFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Guid.NewGuid().ToString();

    public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseSetting("Jwt:Key", TestAppFactory.JwtKey)
        .UseSetting("ContentSeed:SeedOnStartup", "true")
        .ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_database));
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(new Random(27));
            services.AddSingleton<FakeVoicePipeline>();
            services.AddSingleton<IVoicePipeline>(sp => sp.GetRequiredService<FakeVoicePipeline>());
            services.AddSingleton<FakeLlmClient>();
            services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<FakeLlmClient>());
            services.AddSingleton<FakeQuestionGenerationClient>();
            services.AddSingleton<IQuestionGenerationClient>(sp => sp.GetRequiredService<FakeQuestionGenerationClient>());
        });

    /// <summary>Клиент от имени нового Проводника.</summary>
    public Task<HttpClient> CreateConductorClient() => CreateUserClient(UserRoles.Conductor);

    /// <summary>Клиент от имени нового пользователя с одной ролью.</summary>
    public async Task<HttpClient> CreateUserClient(string role)
    {
        var user = new AppUser { Id = Guid.NewGuid(), Username = $"{role}-{Guid.NewGuid():N}", DisplayName = "Тестовый пользователь" };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = role });
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        var token = Services.GetRequiredService<JwtTokenService>().CreateToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    public async Task<HttpClient> CreateManagerClient()
    {
        var user = new AppUser { Id = Guid.NewGuid(), Username = $"manager-{Guid.NewGuid():N}", DisplayName = "Руководитель" };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = UserRoles.Manager });
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        var token = Services.GetRequiredService<JwtTokenService>().CreateToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    /// <summary>Запрос к базе приложения напрямую: журнал Рейса и правка контента посреди Рейса.</summary>
    public async Task<T> Database<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

public sealed class FakeQuestionGenerationClient : IQuestionGenerationClient
{
    public List<QuestionGenerationPrompt> Prompts { get; } = [];
    public Func<QuestionGenerationPrompt, string> Response { get; set; } = _ => "{\"questions\":[]}";
    public Func<QuestionGenerationPrompt, string> FinishReason { get; set; } = _ => "stop";
    public Task<QuestionGenerationResponse> GenerateAsync(QuestionGenerationPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return Task.FromResult(new QuestionGenerationResponse(Response(prompt), FinishReason(prompt)));
    }
}

/// <summary>
/// Детерминированный провайдер для API-тестов: первый байт аудио задаёт choice,
/// «s» — тот же ответ «a», но обработка занимает 5 с по часам теста.
/// </summary>
public sealed class FakeVoicePipeline(TimeProvider clock) : IVoicePipeline
{
    public ConcurrentQueue<VoicePipelineRequest> Requests { get; } = new();

    public async Task<VoicePipelineResult> ProcessAsync(
        VoicePipelineRequest request, Stream audio, string fileName, string? contentType,
        CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var buffer = new byte[1];
        var marker = await audio.ReadAsync(buffer, cancellationToken) == 0 ? -1 : buffer[0];
        if (marker == 's')
        {
            ((TestClock)clock).Advance(TimeSpan.FromSeconds(5));
            marker = 'a';
        }
        return marker switch
        {
            (int)'x' => new VoicePipelineResult("Невнятный ответ", "a", 0.2, 8, false, "LowConfidence", "Низкая уверенность", "fake-laya"),
            (int)'y' => VoicePipelineResult.Failure("SttUnavailable", "STT недоступен", 7),
            (int)'a' or (int)'b' or (int)'c' => new VoicePipelineResult(
                $"Голосовой ответ {(char)marker}", ((char)marker).ToString(), 0.95, 12, true, null, null, "fake-laya",
                new LayaAssessment(0.8, 0.9,
                    new Dictionary<string, double>
                    {
                        ["acknowledge"] = 0.8, ["rule"] = 0.9, ["solution"] = 0.7, ["reassure"] = 0.6,
                    }, 0.05, 0.95),
                5, 7),
            _ => VoicePipelineResult.Failure("SttInvalidResponse", "Пустой тестовый ответ", 4),
        };
    }
}

public sealed class FakeLlmClient : ILlmClient
{
    public bool Fail { get; set; }
    public bool Block { get; set; }
    public int Calls { get; private set; }
    public ConcurrentQueue<LlmRequest> Requests { get; } = new();

    public async IAsyncEnumerable<LlmToken> StreamAsync(
        LlmRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Enqueue(request);
        await Task.CompletedTask;
        if (Block)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
        if (Fail)
            throw new LlmProviderException("LlmProviderError", "Тестовая ошибка Qwen");
        yield return new LlmToken("Пассажир отвечает", "fake-qwen");
    }
}

/// <summary>Часы, которые двигает тест: серверный таймер Шага считает время по ним.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
