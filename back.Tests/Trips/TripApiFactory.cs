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

namespace TurboSquadApp.Tests.Trips;

/// <summary>Приложение для тестов API Рейса: база в памяти с засеянным контентом и часы, которые двигает тест.</summary>
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
            services.AddSingleton<FakeVoicePipeline>();
            services.AddSingleton<IVoicePipeline>(sp => sp.GetRequiredService<FakeVoicePipeline>());
            services.AddSingleton<FakeLlmClient>();
            services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<FakeLlmClient>());
        });

    /// <summary>Клиент от имени нового Проводника.</summary>
    public async Task<HttpClient> CreateConductorClient()
    {
        var user = new AppUser { Id = Guid.NewGuid(), Username = $"conductor-{Guid.NewGuid():N}", DisplayName = "Проводник" };
        user.Roles.Add(new AppUserRole { UserId = user.Id, Role = UserRoles.Conductor });
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

/// <summary>Детерминированный провайдер для API-тестов: первый байт аудио задаёт choice.</summary>
public sealed class FakeVoicePipeline : IVoicePipeline
{
    public async Task<VoicePipelineResult> ProcessAsync(
        VoicePipelineRequest request, Stream audio, string fileName, string? contentType,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var marker = await audio.ReadAsync(buffer, cancellationToken) == 0 ? -1 : buffer[0];
        return marker switch
        {
            (int)'x' => new VoicePipelineResult("Невнятный ответ", "a", 0.2, 8, false, "LowConfidence", "Низкая уверенность", "fake-laya"),
            (int)'y' => VoicePipelineResult.Failure("SttUnavailable", "STT недоступен", 7),
            (int)'a' or (int)'b' or (int)'c' => new VoicePipelineResult(
                $"Голосовой ответ {(char)marker}", ((char)marker).ToString(), 0.95, 12, true, null, null, "fake-laya"),
            _ => VoicePipelineResult.Failure("SttInvalidResponse", "Пустой тестовый ответ", 4),
        };
    }
}

public sealed class FakeLlmClient : ILlmClient
{
    public bool Fail { get; set; }
    public int Calls { get; private set; }

    public async IAsyncEnumerable<LlmToken> StreamAsync(
        LlmRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Calls++;
        await Task.CompletedTask;
        if (request.UserPrompt.Contains("\"choice\":\"c\"", StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
        if (Fail || request.UserPrompt.Contains("\"choice\":\"b\"", StringComparison.Ordinal))
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
