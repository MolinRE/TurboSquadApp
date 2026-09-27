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
    public double MinimumConfidence { get; init; } = 0.55;
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
        MinimumConfidence = configuration.GetValue("Voice:MinimumConfidence", 0.55),
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

/// <param name="RoleStages">Этапы Ролевой модели, которые Laya оценивает на этом Шаге.</param>
public sealed record VoicePipelineRequest(
    string EventId, int EventVersion, string StepId, string Situation, string? Brief,
    IReadOnlyList<VoiceQuestion> Questions, IReadOnlyList<string> RoleStages);

public sealed record LayaAssessment(
    double? Score,
    double? ScoreConfidence,
    IReadOnlyDictionary<string, double> RoleStages,
    double? SafetyViolation,
    double? SafetyConfidence)
{
    public static readonly IReadOnlyList<string> RoleStageCodes =
        ["acknowledge", "rule", "solution", "reassure"];
}

public sealed record VoicePipelineResult(
    string? Transcript, string? Choice, double? Confidence, int LatencyMs,
    bool Applied, string? ErrorCode, string? ErrorMessage, string? ProviderRequestId,
    LayaAssessment? Assessment = null, int? SttLatencyMs = null, int? LayaLatencyMs = null)
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

public sealed record SttResult(
    string? Text, string? ErrorCode, string? ErrorMessage, string? RequestId, int LatencyMs = 0);

public interface ISttClient
{
    Task<SttResult> TranscribeAsync(Stream audio, string fileName, string? contentType, CancellationToken cancellationToken);
}

public sealed record LayaResult(
    string? Choice, double? Confidence, string? ErrorCode, string? ErrorMessage, string? RequestId,
    LayaAssessment? Assessment = null, int LatencyMs = 0);

public interface ILayaClient
{
    Task<LayaResult> DecideAsync(
        string situation, string? brief, string transcript, IReadOnlyList<VoiceQuestion> questions,
        IReadOnlyList<string> roleStages, CancellationToken cancellationToken);
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
                (int)timer.ElapsedMilliseconds) with { SttLatencyMs = stt.LatencyMs };

        var laya = await layaClient.DecideAsync(
            request.Situation, request.Brief, stt.Text, request.Questions, request.RoleStages, cancellationToken);
        if (laya.ErrorCode is not null || string.IsNullOrWhiteSpace(laya.Choice))
            return VoicePipelineResult.Failure(
                laya.ErrorCode ?? "LayaInvalidResponse",
                laya.ErrorMessage ?? "Laya не вернула Вариант",
                (int)timer.ElapsedMilliseconds,
                stt.Text) with { SttLatencyMs = stt.LatencyMs, LayaLatencyMs = laya.LatencyMs, Assessment = laya.Assessment };

        if (laya.Confidence is null || laya.Confidence < options.MinimumConfidence)
            return VoicePipelineResult.Failure(
                "LowConfidence",
                $"Уверенность Laya ниже порога {options.MinimumConfidence:0.##}",
                (int)timer.ElapsedMilliseconds,
                stt.Text) with
            {
                Choice = laya.Choice, Confidence = laya.Confidence, ProviderRequestId = laya.RequestId,
                Assessment = laya.Assessment, SttLatencyMs = stt.LatencyMs, LayaLatencyMs = laya.LatencyMs,
            };

        if (request.Questions.All(question => question.Id != laya.Choice))
            return VoicePipelineResult.Failure(
                "LayaUnknownChoice",
                $"Laya вернула неизвестный Вариант «{laya.Choice}»",
                (int)timer.ElapsedMilliseconds,
                stt.Text) with
            {
                Choice = laya.Choice, Confidence = laya.Confidence, ProviderRequestId = laya.RequestId,
                Assessment = laya.Assessment, SttLatencyMs = stt.LatencyMs, LayaLatencyMs = laya.LatencyMs,
            };

        return new VoicePipelineResult(
            stt.Text, laya.Choice, laya.Confidence, (int)timer.ElapsedMilliseconds,
            true, null, null, laya.RequestId, laya.Assessment, stt.LatencyMs, laya.LatencyMs);
    }
}

public sealed class PolzaSttClient(HttpClient httpClient, VoiceOptions options) : ISttClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SttResult> TranscribeAsync(
        Stream audio, string fileName, string? contentType, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(options.PolzaApiKey))
            return new(null, "SttConfigurationMissing", "Не задан ключ polza.ai", null, (int)timer.ElapsedMilliseconds);

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
                return new(null, "SttProviderError", $"polza.ai вернул {(int)response.StatusCode}", response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null, (int)timer.ElapsedMilliseconds);

            SttResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<SttResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return new(null, "SttInvalidResponse", "polza.ai вернул некорректный JSON", null, (int)timer.ElapsedMilliseconds);
            }
            return string.IsNullOrWhiteSpace(result?.Text)
                ? new(null, "SttInvalidResponse", "polza.ai вернул ответ без текста", null, (int)timer.ElapsedMilliseconds)
                : new(result.Text, null, null, response.Headers.TryGetValues("x-request-id", out var requestIds) ? requestIds.FirstOrDefault() : null, (int)timer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, "SttTimeout", "Истёк таймаут распознавания", null, (int)timer.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return new(null, "SttUnavailable", "Сервис распознавания недоступен", null, (int)timer.ElapsedMilliseconds);
        }
    }

    private sealed record SttResponse(string? Text);
}

public sealed class LayaClient(HttpClient httpClient, VoiceOptions options) : ILayaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Три описательных уровня вместо одиннадцати процентных: различают ответы не хуже,
    // а запрос к Laya на CPU короче примерно на 250 токенов и 400 мс.
    private static readonly IReadOnlyList<string> ScoreCriteria =
    [
        "Проводник грубит или пренебрегает пассажиром",
        "Проводник отвечает сухо, без вежливых слов",
        "Проводник отвечает вежливо: извиняется, благодарит, просит, обращается на Вы",
    ];

    public async Task<LayaResult> DecideAsync(
        string situation, string? brief, string transcript, IReadOnlyList<VoiceQuestion> questions,
        IReadOnlyList<string> roleStages, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(options.LayaToken))
            return new(null, null, "LayaConfigurationMissing", "Не задан токен Laya", null, LatencyMs: (int)timer.ElapsedMilliseconds);

        var layaQuestions = new Dictionary<string, object>
        {
            ["choice"] = new
            {
                type = "choice",
                instructions = "Выбери Вариант, которому соответствует ответ проводника.",
                criteria = questions.ToDictionary(question => question.Id, question => question.Text),
            },
            ["score"] = new
            {
                type = "score",
                instructions = "Оцени вежливость ответа проводника.",
                criteria = ScoreCriteria,
            },
            ["safety"] = new
            {
                type = "noul",
                instructions = "Есть ли в ответе нарушение требований безопасности?",
            },
        };
        foreach (var stage in roleStages)
            layaQuestions[stage] = new
            {
                type = "noul",
                instructions = stage switch
                {
                    "acknowledge" => "Признал ли проводник ситуацию пассажира?",
                    "rule" => "Обозначил ли проводник применимое правило?",
                    "solution" => "Предложил ли проводник решение?",
                    "reassure" => "Заверил ли проводник пассажира?",
                    _ => stage,
                },
            };

        var payload = new
        {
            model = options.LayaModel,
            state = new { situation, conductor_response = transcript, brief },
            questions = layaQuestions,
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
                return new(null, null, "LayaProviderError", $"Laya вернула {(int)response.StatusCode}", null, LatencyMs: (int)timer.ElapsedMilliseconds);

            JsonDocument responseBody;
            try
            {
                responseBody = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return new(null, null, "LayaInvalidResponse", "Laya вернула некорректный JSON", null, LatencyMs: (int)timer.ElapsedMilliseconds);
            }
            using (responseBody)
            {
                if (responseBody.RootElement.ValueKind != JsonValueKind.Object ||
                    !responseBody.RootElement.TryGetProperty("answers", out var answers) ||
                    answers.ValueKind != JsonValueKind.Object ||
                    !answers.TryGetProperty("choice", out var choiceAnswer))
                    return new(null, null, "LayaInvalidResponse", "Laya вернула пустой ответ", null, LatencyMs: (int)timer.ElapsedMilliseconds);

                var choice = GetString(choiceAnswer, "choice");
                var confidence = GetDouble(choiceAnswer, "answer_confidence") ?? GetDouble(choiceAnswer, "confidence");
                if (string.IsNullOrWhiteSpace(choice))
                    return new(null, null, "LayaInvalidResponse", "Laya не вернула Вариант", null, LatencyMs: (int)timer.ElapsedMilliseconds);

                var assessment = ParseAssessment(answers, roleStages);
                if (assessment.Score is null || assessment.SafetyViolation is null ||
                    roleStages.Any(stage => !assessment.RoleStages.ContainsKey(stage)))
                    return new(null, null, "LayaInvalidResponse", "Laya не вернула полную оценку Коммуникации", null, LatencyMs: (int)timer.ElapsedMilliseconds);
                return new(choice, confidence, null, null, null, assessment, (int)timer.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, null, "LayaTimeout", "Истёк таймаут Laya", null, LatencyMs: (int)timer.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return new(null, null, "LayaUnavailable", "Сервис Laya недоступен", null, LatencyMs: (int)timer.ElapsedMilliseconds);
        }
    }

    private static LayaAssessment ParseAssessment(JsonElement answers, IReadOnlyList<string> roleStages)
    {
        var score = answers.TryGetProperty("score", out var scoreAnswer)
            ? GetDouble(scoreAnswer, "score") / (ScoreCriteria.Count - 1)
            : null;
        var scoreConfidence = answers.TryGetProperty("score", out scoreAnswer)
            ? GetDouble(scoreAnswer, "answer_confidence") ?? GetDouble(scoreAnswer, "confidence")
            : null;
        var stages = new Dictionary<string, double>();
        foreach (var stage in roleStages)
            if (answers.TryGetProperty(stage, out var stageAnswer) && GetDouble(stageAnswer, "noul") is { } value)
                stages.Add(stage, value);
        var safety = answers.TryGetProperty("safety", out var safetyAnswer)
            ? GetDouble(safetyAnswer, "noul")
            : null;
        var safetyConfidence = answers.TryGetProperty("safety", out safetyAnswer)
            ? GetDouble(safetyAnswer, "answer_confidence") ?? GetDouble(safetyAnswer, "confidence")
            : null;
        return new(score, scoreConfidence, stages, safety, safetyConfidence);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;
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
