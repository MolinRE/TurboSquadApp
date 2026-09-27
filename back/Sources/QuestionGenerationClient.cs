namespace TurboSquadApp.Sources;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TurboSquadApp.Voice;

public sealed record QuestionGenerationPrompt(
    string SourceTitle, string Section, string Text, IReadOnlyList<string> Topics,
    string? Feedback, string? PreviousOutput = null);

public interface IQuestionGenerationClient
{
    Task<string> GenerateAsync(QuestionGenerationPrompt prompt, CancellationToken cancellationToken);
}

/// <summary>Отдельный JSON-запрос к тому же провайдеру, который используется для голоса.</summary>
public sealed class PolzaQuestionGenerationClient(HttpClient httpClient, VoiceOptions options) : IQuestionGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string SystemPrompt = """
        Ты помогаешь Методисту составить Вопросы для обучения проводников. Верни только JSON-объект вида
        {"questions":[{"type":"single|multiple|sequence|swipe","statement":"...","options":{},
        "explanationText":"...","explanationKeyFact":"...","quote":"дословная цитата из пункта",
        "topic":"тема из списка","categories":[],"serviceClasses":[],"baseFrequency":1,"timeLimitSec":10}]}.
        Для single/multiple options={"options":[{"id":"a","text":"...","correct":true},...]};
        для sequence options={"steps":[{"id":"a","text":"..."},...]};
        для swipe options={"right":{"label":"...","scaleDeltas":{}},"left":{"label":"...","scaleDeltas":{}},"correct":"right"}.
        Не выдумывай цитаты и пункты. Не включай персональные данные. Если материала недостаточно, верни {"questions":[]}.
        """;

    public async Task<string> GenerateAsync(QuestionGenerationPrompt prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.PolzaApiKey))
            throw new InvalidOperationException("Не задан ключ polza.ai для генерации Вопросов");
        var userPrompt = $"Источник: {prompt.SourceTitle}\nПункт: {prompt.Section}\nТемы: {string.Join(", ", prompt.Topics)}\nТекст:\n{prompt.Text}";
        if (!string.IsNullOrWhiteSpace(prompt.Feedback))
            userPrompt += $"\nПредыдущий ответ:\n{prompt.PreviousOutput}\nИсправь ошибки: {prompt.Feedback}";
        var payload = new
        {
            model = options.LlmModel,
            stream = false,
            max_tokens = 3000,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.PolzaApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"polza.ai вернул {(int)response.StatusCode}");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            throw new JsonException("Модель не вернула JSON Вопросов");
        return content.GetString()!;
    }
}
