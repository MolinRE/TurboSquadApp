using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboSquadApp.Voice;

public sealed class VoiceOptions
{
    public string PolzaBaseUrl { get; init; } = "https://polza.ai/api/v1";
    public string PolzaApiKey { get; init; } = string.Empty;
    public string SttModel { get; init; } = "ai-sage/gigaam-v3";
    public string LayaEndpoint { get; init; } = "http://176.108.247.22:8000/v1/systemone";
    public string LayaToken { get; init; } = string.Empty;
    public string LayaModel { get; init; } = "multilingual";
    public string LlmModel { get; init; } = "qwen/qwen3.6-35b-a3b";
    public string LlmReasoningEffort { get; init; } = "low";
    public int LlmMaxTokens { get; init; } = 256;
    public TimeSpan LlmTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public double MinimumConfidence { get; init; } = 0.7;
    public long MaxAudioBytes { get; init; } = 10 * 1024 * 1024;

    public static VoiceOptions FromConfiguration(IConfiguration configuration) => new()
    {
        PolzaBaseUrl = configuration["Voice:PolzaBaseUrl"] ?? "https://polza.ai/api/v1",
        PolzaApiKey = configuration["Voice:PolzaApiKey"] ?? Environment.GetEnvironmentVariable("POLZA_API_KEY") ?? DotEnv("POLZA_API_KEY") ?? string.Empty,
        SttModel = configuration["Voice:SttModel"] ?? "ai-sage/gigaam-v3",
        LayaEndpoint = configuration["Voice:LayaEndpoint"] ?? "http://176.108.247.22:8000/v1/systemone",
        LayaToken = configuration["Voice:LayaToken"]
            ?? Environment.GetEnvironmentVariable("LAYA_API_TOKEN")
            ?? Environment.GetEnvironmentVariable("LAYA_API_KEY")
            ?? DotEnv("LAYA_API_TOKEN")
            ?? DotEnv("LAYA_API_KEY")
            ?? string.Empty,
        LayaModel = configuration["Voice:LayaModel"] ?? "multilingual",
        LlmModel = configuration["Voice:LlmModel"] ?? "qwen/qwen3.6-35b-a3b",
        LlmReasoningEffort = configuration["Voice:LlmReasoningEffort"] ?? "low",
        LlmMaxTokens = configuration.GetValue("Voice:LlmMaxTokens", 256),
        LlmTimeout = TimeSpan.FromSeconds(configuration.GetValue("Voice:LlmTimeoutSeconds", 15)),
        MinimumConfidence = configuration.GetValue("Voice:MinimumConfidence", 0.7),
        MaxAudioBytes = configuration.GetValue("Voice:MaxAudioBytes", 10 * 1024 * 1024L),
    };

    private static string? DotEnv(string key)
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, ".env");
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var separator = trimmed.IndexOf('=');
                if (separator <= 0 || !string.Equals(trimmed[..separator].Trim(), key, StringComparison.Ordinal)) continue;
                return trimmed[(separator + 1)..].Trim().Trim('"', '\'');
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            for (var i = 0; directory is not null && i < 6; i++, directory = directory.Parent)
                if (seen.Add(directory.FullName)) yield return directory.FullName;
        }
    }
}

public sealed record VoiceQuestion(string Id, string Text);

public sealed record VoicePipelineRequest(
    string EventId, int EventVersion, string StepId, string Situation, string? Brief,
    IReadOnlyList<VoiceQuestion> Questions);

public sealed record VoicePipelineResult(
    string? Transcript, string? Choice, double? Confidence, int LatencyMs,
    bool Applied, string? ErrorCode, string? ErrorMessage, string? ProviderRequestId)
{
    public static VoicePipelineResult Failure(string code, string message, int latencyMs = 0, string? transcript = null) =>
        new(transcript, null, null, latencyMs, false, code, message, null);
}

public interface IVoicePipeline
{
    Task<VoicePipelineResult> ProcessAsync(
        VoicePipelineRequest request, Stream audio, string fileName, string? contentType,
        CancellationToken cancellationToken);
}

public sealed record SttResult(string? Text, string? ErrorCode, string? ErrorMessage, string? RequestId);

public interface ISttClient
{
    Task<SttResult> TranscribeAsync(Stream audio, string fileName, string? contentType, CancellationToken cancellationToken);
}

public sealed record LayaResult(
    string? Choice, double? Confidence, string? ErrorCode, string? ErrorMessage, string? RequestId);

public interface ILayaClient
{
    Task<LayaResult> DecideAsync(
        string situation, string? brief, string transcript, IReadOnlyList<VoiceQuestion> questions,
        CancellationToken cancellationToken);
}

public sealed record LlmRequest(string SystemPrompt, string UserPrompt);

public sealed record LlmToken(string Text, string? RequestId);

public sealed class LlmProviderException(string code, string message, string? requestId = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? RequestId { get; } = requestId;
}

public interface ILlmClient
{
    IAsyncEnumerable<LlmToken> StreamAsync(
        LlmRequest request, CancellationToken cancellationToken);
}

public sealed class VoicePipelineService(
    ISttClient sttClient,
    ILayaClient layaClient,
    VoiceOptions options) : IVoicePipeline
{
    public async Task<VoicePipelineResult> ProcessAsync(
        VoicePipelineRequest request, Stream audio, string fileName, string? contentType,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var stt = await sttClient.TranscribeAsync(audio, fileName, contentType, cancellationToken);
        if (stt.ErrorCode is not null || string.IsNullOrWhiteSpace(stt.Text))
            return VoicePipelineResult.Failure(
                stt.ErrorCode ?? "SttInvalidResponse",
                stt.ErrorMessage ?? "Распознавание не вернуло текст",
                (int)timer.ElapsedMilliseconds);

        var laya = await layaClient.DecideAsync(request.Situation, request.Brief, stt.Text, request.Questions, cancellationToken);
        if (laya.ErrorCode is not null || string.IsNullOrWhiteSpace(laya.Choice))
            return VoicePipelineResult.Failure(
                laya.ErrorCode ?? "LayaInvalidResponse",
                laya.ErrorMessage ?? "Laya не вернула Вариант",
                (int)timer.ElapsedMilliseconds,
                stt.Text);

        if (laya.Confidence is null || laya.Confidence < options.MinimumConfidence)
            return VoicePipelineResult.Failure(
                "LowConfidence",
                $"Уверенность Laya ниже порога {options.MinimumConfidence:0.##}",
                (int)timer.ElapsedMilliseconds,
                stt.Text) with { Choice = laya.Choice, Confidence = laya.Confidence, ProviderRequestId = laya.RequestId };

        if (request.Questions.All(question => question.Id != laya.Choice))
            return VoicePipelineResult.Failure(
                "LayaUnknownChoice",
                $"Laya вернула неизвестный Вариант «{laya.Choice}»",
                (int)timer.ElapsedMilliseconds,
                stt.Text) with { Choice = laya.Choice, Confidence = laya.Confidence, ProviderRequestId = laya.RequestId };

        return new VoicePipelineResult(
            stt.Text, laya.Choice, laya.Confidence, (int)timer.ElapsedMilliseconds,
            true, null, null, laya.RequestId);
    }
}

public sealed class PolzaSttClient(HttpClient httpClient, VoiceOptions options) : ISttClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SttResult> TranscribeAsync(
        Stream audio, string fileName, string? contentType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.PolzaApiKey))
            return new(null, "SttConfigurationMissing", "Не задан ключ polza.ai", null);

        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(audio);
        try
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/octet-stream");
        }
        catch (FormatException)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        form.Add(content, "file", fileName);
        form.Add(new StringContent(options.SttModel), "model");
        form.Add(new StringContent("ru"), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.PolzaApiKey);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(null, "SttProviderError", $"polza.ai вернул {(int)response.StatusCode}", response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null);

            SttResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<SttResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return new(null, "SttInvalidResponse", "polza.ai вернул некорректный JSON", null);
            }
            return string.IsNullOrWhiteSpace(result?.Text)
                ? new(null, "SttInvalidResponse", "polza.ai вернул ответ без текста", null)
                : new(result.Text, null, null, response.Headers.TryGetValues("x-request-id", out var requestIds) ? requestIds.FirstOrDefault() : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, "SttTimeout", "Истёк таймаут распознавания", null);
        }
        catch (HttpRequestException)
        {
            return new(null, "SttUnavailable", "Сервис распознавания недоступен", null);
        }
    }

    private sealed record SttResponse(string? Text);
}

public sealed class LayaClient(HttpClient httpClient, VoiceOptions options) : ILayaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LayaResult> DecideAsync(
        string situation, string? brief, string transcript, IReadOnlyList<VoiceQuestion> questions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.LayaToken))
            return new(null, null, "LayaConfigurationMissing", "Не задан токен Laya", null);

        var payload = new
        {
            model = options.LayaModel,
            state = new { situation, conductor_response = transcript, brief },
            questions = new
            {
                choice = new
                {
                    type = "choice",
                    instructions = "Выбери Вариант, которому соответствует ответ проводника.",
                    criteria = questions.ToDictionary(question => question.Id, question => question.Text),
                }
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, options.LayaEndpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.LayaToken);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(null, null, "LayaProviderError", $"Laya вернула {(int)response.StatusCode}", null);

            LayaResponse? responseBody;
            try
            {
                responseBody = JsonSerializer.Deserialize<LayaResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return new(null, null, "LayaInvalidResponse", "Laya вернула некорректный JSON", null);
            }
            var result = responseBody?.Answers?.Choice;
            var confidence = result?.AnswerConfidence ?? result?.Confidence;
            return result is null
                ? new(null, null, "LayaInvalidResponse", "Laya вернула пустой ответ", null)
                : new(result.Choice, confidence, null, null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, null, "LayaTimeout", "Истёк таймаут Laya", null);
        }
        catch (HttpRequestException)
        {
            return new(null, null, "LayaUnavailable", "Сервис Laya недоступен", null);
        }
    }

    private sealed record LayaResponse(Answers? Answers);

    private sealed record Answers(ChoiceAnswer? Choice);

    private sealed record ChoiceAnswer(
        string? Choice,
        double? Confidence,
        [property: JsonPropertyName("answer_confidence")] double? AnswerConfidence);
}

public sealed class PolzaLlmClient(HttpClient httpClient, VoiceOptions options) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<LlmToken> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.PolzaApiKey))
            throw new LlmProviderException("LlmConfigurationMissing", "Не задан ключ polza.ai");

        var payload = new
        {
            model = options.LlmModel,
            stream = true,
            reasoning = new { effort = options.LlmReasoningEffort },
            max_tokens = options.LlmMaxTokens,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.PolzaApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.LlmTimeout);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmProviderException("LlmTimeout", "Истёк таймаут генерации реплики пассажира");
        }
        catch (HttpRequestException)
        {
            throw new LlmProviderException("LlmUnavailable", "Сервис генерации реплики недоступен");
        }

        using (response)
        {
            var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new LlmProviderException("LlmProviderError", $"polza.ai вернул {status}", requestId);
            }

            if (response.Content.Headers.ContentType?.MediaType is not "text/event-stream")
                throw new LlmProviderException("LlmInvalidResponse", "polza.ai не вернул поток SSE", requestId);

            Stream stream;
            try
            {
                stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LlmProviderException("LlmTimeout", "Истёк таймаут генерации реплики пассажира", requestId);
            }
            catch (HttpRequestException)
            {
                throw new LlmProviderException("LlmUnavailable", "Поток Qwen оборвался", requestId);
            }

            await using (stream)
            using (var reader = new StreamReader(stream))
            {
                var emitted = false;
                var completed = false;
                while (true)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new LlmProviderException("LlmTimeout", "Истёк таймаут генерации реплики пассажира", requestId);
                    }
                    catch (HttpRequestException)
                    {
                        throw new LlmProviderException("LlmUnavailable", "Поток Qwen оборвался", requestId);
                    }
                    if (line is null) break;
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    var data = line[5..].Trim();
                    if (data == "[DONE]")
                    {
                        completed = true;
                        break;
                    }

                    LlmChunk? chunk;
                    try
                    {
                        chunk = JsonSerializer.Deserialize<LlmChunk>(data, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        throw new LlmProviderException("LlmInvalidResponse", "polza.ai вернул некорректный SSE JSON", requestId);
                    }

                    var chunkId = chunk?.Id ?? requestId;
                    var text = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                    if (string.IsNullOrEmpty(text)) continue;
                    emitted = true;
                    yield return new LlmToken(text, chunkId);
                }

                if (!completed)
                    throw new LlmProviderException("LlmStreamInterrupted", "Поток Qwen завершился без сигнала окончания", requestId);
                if (!emitted)
                    throw new LlmProviderException("LlmInvalidResponse", "Qwen не вернул текст реплики", requestId);
            }
        }
    }

    private sealed record LlmChunk(string? Id, IReadOnlyList<LlmChoice>? Choices);

    private sealed record LlmChoice(LlmDelta? Delta);

    private sealed record LlmDelta(string? Content);
}
