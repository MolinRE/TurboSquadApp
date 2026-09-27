using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;
using Microsoft.Extensions.DependencyInjection;

namespace TurboSquadApp.Tests.Content;

public class SourceGenerationApiTests
{
    [Fact]
    public async Task Methodologist_can_save_and_reopen_pasted_source()
    {
        await using var factory = new TripApiFactory();
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await client.PostAsJsonAsync("/api/cms/sources", new
        {
            title = "СТО 03.011",
            text = "1. Проверить билет пассажира.\n2. Сообщить о задержке.",
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var source = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        var id = (string)source["id"]!;
        Assert.Equal("СТО 03.011", (string)source["title"]!);
        Assert.Equal("1. Проверить билет пассажира.\n2. Сообщить о задержке.", (string)source["text"]!);
        var reopened = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}"))!;
        Assert.Equal((string)source["text"]!, (string)reopened["text"]!);
        var list = (await client.GetFromJsonAsync<JsonNode>("/api/cms/sources"))!.AsArray();
        Assert.Contains(list, item => (string)item!["id"]! == id);
    }

    [Fact]
    public async Task Generation_saves_section_linked_drafts_after_scrubbing_personal_data()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = prompt => $$"""
            {"questions":[{"type":"single","statement":"{{(prompt.Section.StartsWith('1') ? "Как проверить билет?" : "Как сообщить о задержке?")}}","options":{"options":[{"id":"a","text":"Верно","correct":true},{"id":"b","text":"Неверно","correct":false}]},"explanationText":"Следуйте правилу","explanationKeyFact":"правилу","quote":"{{(prompt.Section.StartsWith('1') ? "Проверить билет пассажира." : "Сообщить о задержке.")}}","topic":"Посадка и документы","categories":["Штатная"],"serviceClasses":[],"baseFrequency":1,"timeLimitSec":10}]}
            """;
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await client.PostAsJsonAsync("/api/cms/sources", new
        {
            title = "СТО",
            text = "1. Проверить билет пассажира. Иванов Иван Иванович, ivan@example.com, +7 999 123-45-67.\n2. Сообщить о задержке. Серия 1234 номер 567890.",
        });
        var id = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;

        var generation = await client.PostAsync($"/api/cms/sources/{id}/generate", null);
        Assert.Equal(HttpStatusCode.OK, generation.StatusCode);
        var result = (await generation.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True((int)result["created"]! == 2, result.ToJsonString());
        Assert.Equal(2, generator.Prompts.Count);
        Assert.All(generator.Prompts, prompt =>
        {
            Assert.DoesNotContain("Иванов", prompt.Text);
            Assert.DoesNotContain("ivan@example.com", prompt.Text);
            Assert.DoesNotContain("+7 999", prompt.Text);
            Assert.DoesNotContain("1234", prompt.Text);
        });
        var drafts = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}/drafts"))!.AsArray();
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, draft => Assert.Equal("draft", (string)draft!["status"]!));
        Assert.Contains(drafts, draft => ((string)draft!["source"]!).Contains("1."));
        Assert.Contains(drafts, draft => ((string)draft!["source"]!).Contains("2."));
        foreach (var draft in drafts)
        {
            var reopened = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/questions/{(string)draft!["id"]!}"))!;
            Assert.Equal("draft", (string)reopened["status"]!);
            Assert.NotNull(reopened["quote"]);
            foreach (var variant in reopened["options"]!["options"]!.AsArray())
            {
                Assert.Equal((string)reopened["quote"]!, (string)variant!["quote"]!);
                Assert.Equal((string)reopened["source"]!, (string)variant["source"]!);
            }
            reopened["statement"] = "Исправленная формулировка";
            reopened["quote"] = "Исправленная цитата";
            reopened["source"] = "Исправленный пункт";
            Assert.Equal(HttpStatusCode.OK,
                (await client.PutAsJsonAsync($"/api/cms/questions/{(string)draft["id"]!}", reopened)).StatusCode);
            var edited = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/questions/{(string)draft["id"]!}"))!;
            Assert.Equal((string)edited["source"]!, (string)edited["options"]!["options"]![0]!["source"]!);
            Assert.Equal((string)edited["quote"]!, (string)edited["options"]!["options"]![0]!["quote"]!);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generation_from_long_markdown_table_creates_row_linked_drafts(bool outerPipes)
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        var header = outerPipes ? "| Ситуация | Правило |" : "Ситуация | Правило";
        var separator = outerPipes ? "| --- | --- |" : "--- | ---";
        generator.Response = prompt =>
        {
            var row = Regex.Match(prompt.Text, @"Проверить билет пассажира (\d+)\.");
            if (!row.Success || !prompt.Text.Contains(header))
                return "{\"questions\":[]}";
            var number = row.Groups[1].Value;
            return $$"""{"questions":[{{ValidQuestion($"Как проверить билет {number}?", $"Проверить билет пассажира {number}.")}}]}""";
        };
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var filler = string.Concat(Enumerable.Repeat("Дополнительное правило для проводника. ", 12));
        var rows = Enumerable.Range(1, 35)
            .Select(number => outerPipes
                ? $"| Ситуация {number} | Проверить билет пассажира {number}. {filler}|"
                : $"Ситуация {number} | Проверить билет пассажира {number}. {filler}");
        var source = $"## Посадка\n{header}\n{separator}\n" + string.Join("\n", rows);
        var id = await CreateSource(client, source);

        var result = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;

        Assert.Equal(35, (int)result["created"]!);
        Assert.Empty(result["errors"]!.AsArray());
        Assert.Equal(35, generator.Prompts.Count);
        Assert.All(generator.Prompts, prompt =>
        {
            Assert.True(prompt.Text.Length < 2500);
            Assert.Contains("## Посадка", prompt.Text);
            Assert.Contains(header, prompt.Text);
        });
        var drafts = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}/drafts"))!.AsArray();
        Assert.Equal(35, drafts.Count);
        Assert.All(drafts, draft =>
        {
            Assert.Contains("строка", (string)draft!["source"]!);
            Assert.Contains((string)draft["quote"]!, source);
            Assert.Equal("draft", (string)draft["status"]!);
        });
    }

    [Fact]
    public async Task Long_numbered_point_is_split_without_losing_quote_provenance()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = prompt =>
        {
            var match = Regex.Match(prompt.Text, @"Проверить билет пассажира (\d+)\.");
            var number = match.Groups[1].Value;
            return $$"""{"questions":[{{ValidQuestion($"Как проверить билет {number}?", $"Проверить билет пассажира {number}.")}}]}""";
        };
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var sentence = string.Concat(Enumerable.Repeat("Проводник сверяет документ. ", 12));
        var source = "1. " + string.Join(" ", Enumerable.Range(1, 18)
            .Select(number => $"Проверить билет пассажира {number}. {sentence}"));
        var id = await CreateSource(client, source);

        var result = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;

        Assert.True(generator.Prompts.Count > 1);
        Assert.All(generator.Prompts, prompt => Assert.True(prompt.Text.Length < 2500));
        Assert.Equal(generator.Prompts.Count, (int)result["created"]!);
        Assert.Empty(result["errors"]!.AsArray());
        Assert.All(result["drafts"]!.AsArray(), draft =>
        {
            Assert.Contains("часть", (string)draft!["source"]!);
            Assert.Contains((string)draft["quote"]!, source);
        });
    }

    [Fact]
    public async Task Invalid_model_output_gets_two_repairs_and_keeps_valid_draft_unpublished()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        var valid = ValidQuestion("Как проверить билет?");
        generator.Response = prompt => generator.Prompts.Count == 1
            ? $$"""{"questions":[{{valid}}, {"type":"single","statement":"Неполный"}]}"""
            : "это не JSON";
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.");

        var result = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(1, (int)result["created"]!);
        Assert.NotEmpty(result["errors"]!.AsArray());
        Assert.Equal(3, generator.Prompts.Count);
        Assert.Null(generator.Prompts[0].Feedback);
        Assert.NotNull(generator.Prompts[1].Feedback);
        Assert.NotNull(generator.Prompts[2].Feedback);
        var drafts = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}/drafts"))!.AsArray();
        Assert.Single(drafts);
        Assert.Equal("draft", (string)drafts[0]!["status"]!);
    }

    [Fact]
    public async Task Repeated_generation_skips_duplicate_question()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = _ => $$"""{"questions":[{{ValidQuestion(generator.Prompts.Count == 1 ? "Как проверить билет?" : "как проверить билет")}}]}""";
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.");
        await client.PostAsync($"/api/cms/sources/{id}/generate", null);

        var repeated = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(0, (int)repeated["created"]!);
        Assert.Equal(1, (int)repeated["duplicates"]!);
        Assert.Single((await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}/drafts"))!.AsArray());
    }

    [Fact]
    public async Task Invalid_json_is_repaired_into_a_draft_on_second_request()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = _ => generator.Prompts.Count == 1
            ? "не JSON"
            : $$"""{"questions":[{{ValidQuestion("Как проверить билет?")}}]}""";
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.");

        var result = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(1, (int)result["created"]!);
        Assert.Empty(result["errors"]!.AsArray());
        Assert.Equal(2, generator.Prompts.Count);
        Assert.Contains("Некорректный JSON", generator.Prompts[1].Feedback);
        Assert.Equal("не JSON", generator.Prompts[1].PreviousOutput);
    }

    [Fact]
    public async Task Output_limit_keeps_other_drafts_and_reports_the_exhausted_section()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = prompt => prompt.Section.StartsWith('1')
            ? $$"""{"questions":[{{ValidQuestion("Как проверить билет?")}}]}"""
            : string.Empty;
        generator.FinishReason = prompt => prompt.Section.StartsWith('1') ? "stop" : "length";
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.\n2. Сообщить о задержке.");

        var result = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;

        Assert.Equal(1, (int)result["created"]!);
        var error = Assert.Single(result["errors"]!.AsArray());
        Assert.Contains("Пункт 2.", (string)error!);
        Assert.Contains("лимит", (string)error!);
        Assert.DoesNotContain("Некорректный JSON", (string)error!);
        var retries = generator.Prompts.Where(prompt => prompt.Section.StartsWith('2')).ToList();
        Assert.Equal(3, retries.Count);
        Assert.Equal([8_000, 12_000, 16_000], retries.Select(prompt => prompt.MaxTokens));
        Assert.Single((await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}/drafts"))!.AsArray());
    }

    [Fact]
    public async Task Empty_model_content_is_reported_separately_from_invalid_json()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = _ => string.Empty;
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.");

        var result = (await (await client.PostAsync($"/api/cms/sources/{id}/generate", null))
            .Content.ReadFromJsonAsync<JsonNode>())!;

        var error = (string)Assert.Single(result["errors"]!.AsArray())!;
        Assert.Contains("пустой ответ", error);
        Assert.DoesNotContain("Некорректный JSON", error);
        Assert.Equal([8_000, 12_000, 16_000], generator.Prompts.Select(prompt => prompt.MaxTokens));
    }

    [Fact]
    public async Task Provider_error_returns_clear_failure_without_creating_questions()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = _ => throw new HttpRequestException("provider failed");
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.");

        var response = await client.PostAsync($"/api/cms/sources/{id}/generate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(0, (int)result["created"]!);
        Assert.Single(result["errors"]!.AsArray());
        Assert.Equal(3, generator.Prompts.Count);
        Assert.DoesNotContain("provider failed", result.ToJsonString());
        Assert.Empty((await client.GetFromJsonAsync<JsonNode>($"/api/cms/sources/{id}/drafts"))!.AsArray());
    }

    [Fact]
    public async Task Methodologist_can_compare_question_models_but_unknown_model_is_rejected()
    {
        await using var factory = new TripApiFactory();
        var generator = factory.Services.GetRequiredService<FakeQuestionGenerationClient>();
        generator.Response = _ => $$"""{"questions":[{{ValidQuestion("Как проверить билет?")}}]}""";
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(client, "1. Проверить билет пассажира.");

        var generated = await client.PostAsJsonAsync($"/api/cms/sources/{id}/generate",
            new { model = "deepseek/deepseek-v3.2" });
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        Assert.Equal(1, (int)(await generated.Content.ReadFromJsonAsync<JsonNode>())!["created"]!);
        Assert.Equal("deepseek/deepseek-v3.2", Assert.Single(generator.Prompts).Model);

        var rejected = await client.PostAsJsonAsync($"/api/cms/sources/{id}/generate",
            new { model = "untrusted/model" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Single(generator.Prompts);
    }

    [Fact]
    public async Task Only_methodologist_can_access_sources_and_generation()
    {
        await using var factory = new TripApiFactory();
        using var methodologist = await factory.CreateUserClient(UserRoles.Methodologist);
        var id = await CreateSource(methodologist, "1. Проверить билет пассажира.");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/cms/sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/cms/sources/{id}/generate", null)).StatusCode);
        using var conductor = await factory.CreateConductorClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.GetAsync($"/api/cms/sources/{id}/drafts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.PostAsync($"/api/cms/sources/{id}/generate", null)).StatusCode);
        using var manager = await factory.CreateManagerClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/cms/sources", new { title = "x", text = "x" })).StatusCode);
    }

    private static async Task<string> CreateSource(HttpClient client, string text)
    {
        var created = await client.PostAsJsonAsync("/api/cms/sources", new { title = "СТО", text });
        return (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;
    }

    private static string ValidQuestion(string statement, string quote = "Проверить билет пассажира.") => $$"""
        {"type":"single","statement":"{{statement}}","options":{"options":[{"id":"a","text":"Да","correct":true},{"id":"b","text":"Нет","correct":false}]},"explanationText":"Проверьте билет","explanationKeyFact":"билет","quote":"{{quote}}","topic":"Посадка и документы","categories":["Штатная"],"serviceClasses":[],"baseFrequency":1,"timeLimitSec":10}
        """;
}
