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
