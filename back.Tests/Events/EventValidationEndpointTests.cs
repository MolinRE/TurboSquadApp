using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TurboSquadApp.Tests.Events;

public class EventValidationEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<JsonNode> PostForValidation(string eventJson)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/api/events/validate", new StringContent(eventJson, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Fact]
    public async Task Correct_event_is_reported_valid()
    {
        var report = await PostForValidation(EventValidatorTests.ValidEventJson);

        Assert.True((bool)report["isValid"]!);
        Assert.Empty(report["errors"]!.AsArray());
        Assert.Empty(report["warnings"]!.AsArray());
    }

    [Fact]
    public async Task Broken_draft_is_reported_with_errors_and_warning()
    {
        var draft = File.ReadAllText(Path.Combine("Events", "TestData", "sit-33-v2-broken.json"));

        var report = await PostForValidation(draft);

        Assert.False((bool)report["isValid"]!);
        Assert.Equal(8, report["errors"]!.AsArray().Count);
        var warning = Assert.Single(report["warnings"]!.AsArray());
        Assert.Equal("Шаг s2 → Вариант a, переход 1", (string)warning!["where"]!);
    }
}
