using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TurboSquadApp.Tests;

/// <summary>Приложение для тестов через HTTP: подставляет настройки, которые в работе приходят из окружения.</summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Jwt:Key", "test-only-signing-key-at-least-32-characters");
}
