using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Content;

public class EventGenerationApiTests
{
    [Fact]
    public async Task Invalid_event_response_reports_error_without_exposing_source_or_creating_a_draft()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeEventGenerationClient>();
        generator.Response = _ => "[]";
        using var methodologist = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await methodologist.PostAsJsonAsync("/api/cms/sources",
            new { title = "СТО", text = "1. Проверить билет пассажира. Иванов Иван Иванович, ivan@example.com." });
        var sourceId = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;

        var generated = await methodologist.PostAsJsonAsync($"/api/cms/sources/{sourceId}/generate-events",
            new { model = "deepseek/deepseek-v3.2" });

        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        var result = (await generated.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(0, (int)result["created"]!);
        Assert.NotEmpty(result["errors"]!.AsArray());
        Assert.DoesNotContain("Иванов", result.ToJsonString());
        Assert.DoesNotContain("ivan@example.com", result.ToJsonString());
        Assert.All(generator.Prompts, prompt =>
        {
            Assert.DoesNotContain("Иванов", prompt.Text);
            Assert.DoesNotContain("ivan@example.com", prompt.Text);
        });
        Assert.Empty((await methodologist.GetFromJsonAsync<JsonNode>(
            $"/api/cms/events/drafts?sourceId={sourceId}"))!.AsArray());
    }

    [Fact]
    public async Task Methodologist_generates_an_unpublished_event_draft_with_source_quote()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeEventGenerationClient>();
        generator.Response = _ => """
            {"event":{"id":"temporary","version":1,"title":"Проверка билета у пассажира","topic":"Посадка и документы","start":"start","steps":[{"id":"start","answerType":"buttons","situation":"Пассажир предъявил билет","variants":[{"id":"a","text":"Проверить билет","source":"Проверить билет пассажира.","transitions":[{"to":"success"}]},{"id":"b","text":"Пропустить без проверки","source":"Проверить билет пассажира.","transitions":[{"to":"failure"}]}]},{"id":"success","outcome":"success","situation":"Билет проверен"},{"id":"failure","outcome":"failure","situation":"Проверка пропущена"}]}}
            """;
        using var methodologist = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await methodologist.PostAsJsonAsync("/api/cms/sources",
            new { title = "СТО", text = "1. Проверить билет пассажира." });
        var sourceId = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;

        var generated = await methodologist.PostAsJsonAsync($"/api/cms/sources/{sourceId}/generate-events",
            new { model = "openai/gpt-oss-20b" });
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        var result = (await generated.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(1, (int)result["created"]!);
        var draft = Assert.Single((await methodologist.GetFromJsonAsync<JsonNode>(
            $"/api/cms/events/drafts?sourceId={sourceId}"))!.AsArray());
        var document = JsonNode.Parse((string)draft!["document"]!)!;
        Assert.Equal("СТО, п. 1.", (string)document["source"]!);
        Assert.Contains("Проверить билет пассажира.", (string)document["steps"]![0]!["variants"]![0]!["source"]!);
        Assert.Equal(1, (int)document["version"]!);
        Assert.Equal("openai/gpt-oss-20b", Assert.Single(generator.Prompts).Model);
        Assert.DoesNotContain((await methodologist.GetFromJsonAsync<JsonNode>("/api/cms/events"))!.AsArray(),
            item => (string)item!["id"]! == (string)document["id"]!);

        var unknown = await methodologist.PostAsJsonAsync($"/api/cms/sources/{sourceId}/generate-events",
            new { model = "untrusted/model" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Single(generator.Prompts);
        using var conductor = await factory.CreateConductorClient();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await conductor.PostAsJsonAsync($"/api/cms/sources/{sourceId}/generate-events",
                new { model = "openai/gpt-oss-20b" })).StatusCode);
    }
}
