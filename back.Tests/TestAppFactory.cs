using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TurboSquadApp.Tests;

/// <summary>Приложение для тестов через HTTP: подставляет настройки, которые в работе приходят из окружения.</summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-only-signing-key-at-least-32-characters";

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseSetting("Jwt:Key", JwtKey)
        .UseSetting("ContentSeed:SeedOnStartup", "false");   // базы в HTTP-тестах нет
}
