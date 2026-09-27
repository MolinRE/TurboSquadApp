using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TurboSquadApp.Voice;

namespace TurboSquadApp.Tests.Voice;

public class VoiceClientTests
{
    [Fact]
    public async Task Polza_stt_sends_audio_as_multipart_and_returns_text()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new CapturingHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, "{\"text\":\"Прошу соблюдать спокойствие\"}");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://polza.test/api/v1/") };
        var client = new PolzaSttClient(http, new VoiceOptions { PolzaApiKey = "polza-test" });
        await using var audio = new MemoryStream([1, 2, 3]);

        var result = await client.TranscribeAsync(audio, "answer.wav", "audio/wav", CancellationToken.None);

        Assert.Equal("Прошу соблюдать спокойствие", result.Text);
        Assert.Null(result.ErrorCode);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("polza-test", captured.Headers.Authorization.Parameter);
        Assert.Contains("name=file", capturedBody);
        Assert.Contains("name=model", capturedBody);
        Assert.Contains("ai-sage/gigaam-v3", capturedBody);
        Assert.Contains("name=language", capturedBody);
    }

    [Fact]
    public async Task Laya_client_sends_structured_state_and_reads_choice_confidence()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new CapturingHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, "{\"answers\":{\"choice\":{\"choice\":\"a\",\"answer_confidence\":0.91}}}");
        });
        using var http = new HttpClient(handler);
        var client = new LayaClient(http, new VoiceOptions { LayaToken = "laya-test", LayaEndpoint = "http://laya.test/v1/systemone" });

        var result = await client.DecideAsync(
            "Пассажир шумит", "Спокойно обозначить правило", "Прошу соблюдать спокойствие",
            [new VoiceQuestion("a", "Спокойно подойти"), new VoiceQuestion("b", "Не подходить")],
            CancellationToken.None);

        Assert.Equal(("a", 0.91, null), (result.Choice, result.Confidence, result.RequestId));
        Assert.Null(result.ErrorCode);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        var json = JsonDocument.Parse(capturedBody!).RootElement;
        Assert.Equal("multilingual", json.GetProperty("model").GetString());
        Assert.Equal("Прошу соблюдать спокойствие", json.GetProperty("state").GetProperty("conductor_response").GetString());
        Assert.Equal("choice", json.GetProperty("questions").EnumerateObject().Single().Name);
        Assert.Equal("Спокойно подойти", json.GetProperty("questions").GetProperty("choice").GetProperty("criteria").GetProperty("a").GetString());
    }

    [Fact]
    public async Task Malformed_provider_json_is_reported_as_invalid_response()
    {
        var handler = new CapturingHandler(_ => Json(HttpStatusCode.OK, "not-json"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://polza.test/api/v1/") };
        var stt = new PolzaSttClient(http, new VoiceOptions { PolzaApiKey = "polza-test" });
        await using var audio = new MemoryStream([1]);

        var sttResult = await stt.TranscribeAsync(audio, "answer.wav", "audio/wav", CancellationToken.None);
        Assert.Equal("SttInvalidResponse", sttResult.ErrorCode);

        var laya = new LayaClient(http, new VoiceOptions { LayaToken = "laya-test", LayaEndpoint = "http://laya.test/v1/systemone" });
        var layaResult = await laya.DecideAsync("Ситуация", null, "Ответ", [new VoiceQuestion("a", "Критерий")], CancellationToken.None);
        Assert.Equal("LayaInvalidResponse", layaResult.ErrorCode);
    }

    [Fact]
    public async Task Polza_qwen_stream_reads_content_only_and_sends_deterministic_options()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new CapturingHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"id\":\"qwen-1\",\"choices\":[{\"delta\":{\"reasoning\":\"hidden\",\"content\":\"Добрый день.\"}}]}\n\n" +
                    "data: {\"id\":\"qwen-1\",\"choices\":[{\"delta\":{\"content\":\" Прошу пройти.\"}}]}\n\n" +
                    "data: [DONE]\n\n",
                    new System.Text.UTF8Encoding(false), "text/event-stream")
            };
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://polza.test/api/v1/") };
        var client = new PolzaLlmClient(http, new VoiceOptions { PolzaApiKey = "polza-test" });

        var tokens = new List<LlmToken>();
        await foreach (var token in client.StreamAsync(new LlmRequest("system", "context"), CancellationToken.None))
            tokens.Add(token);

        Assert.Equal("Добрый день. Прошу пройти.", string.Concat(tokens.Select(token => token.Text)));
        Assert.NotNull(captured);
        var json = JsonDocument.Parse(capturedBody!).RootElement;
        Assert.Equal("qwen/qwen3.6-35b-a3b", json.GetProperty("model").GetString());
        Assert.True(json.GetProperty("stream").GetBoolean());
        Assert.Equal("low", json.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Qwen_stream_without_done_is_reported_as_interrupted()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"id\":\"qwen-1\",\"choices\":[{\"delta\":{\"content\":\"текст\"}}]}\n\n",
                new System.Text.UTF8Encoding(false), "text/event-stream")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://polza.test/api/v1/") };
        var client = new PolzaLlmClient(http, new VoiceOptions { PolzaApiKey = "polza-test" });

        var error = await Assert.ThrowsAsync<LlmProviderException>(async () =>
        {
            await foreach (var _ in client.StreamAsync(new LlmRequest("system", "context"), CancellationToken.None)) { }
        });
        Assert.Equal("LlmStreamInterrupted", error.Code);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, new System.Text.UTF8Encoding(false), "application/json")
    };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
