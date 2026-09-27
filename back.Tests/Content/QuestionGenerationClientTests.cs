using System.Net;
using System.Text.Json;
using TurboSquadApp.Sources;
using TurboSquadApp.Voice;

namespace TurboSquadApp.Tests.Content;

public class QuestionGenerationClientTests
{
    [Fact]
    public async Task Output_limit_is_preserved_and_uses_the_requested_question_budget()
    {
        string? requestBody = null;
        using var http = new HttpClient(new CapturingHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"finish_reason":"length","message":{"content":""}}]}"""),
            };
        })) { BaseAddress = new Uri("https://polza.test/api/v1/") };
        var client = new PolzaQuestionGenerationClient(http,
            new VoiceOptions { PolzaApiKey = "polza-test", LlmMaxTokens = 256 });
        var prompt = new QuestionGenerationPrompt("СТО", "1.", "Проверить билет.", ["Посадка и документы"], null,
            MaxTokens: 12_000);

        var response = await client.GenerateAsync(prompt, CancellationToken.None);

        Assert.Equal("length", response.FinishReason);
        Assert.Empty(response.Content);
        var payload = JsonDocument.Parse(requestBody!).RootElement;
        Assert.Equal(12_000, payload.GetProperty("max_tokens").GetInt32());
        Assert.Equal("json_schema", payload.GetProperty("response_format").GetProperty("type").GetString());
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
