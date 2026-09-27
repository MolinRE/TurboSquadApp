using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TurboSquadApp.Voice;

namespace TurboSquadApp.Sources;

public sealed record EventGenerationPrompt(
    string SourceTitle, string Section, string Text, IReadOnlyList<string> Topics,
    string Model, string? Feedback = null, string? PreviousOutput = null);

public interface IEventGenerationClient
{
    Task<string> GenerateAsync(EventGenerationPrompt prompt, CancellationToken cancellationToken);
}

public sealed class PolzaEventGenerationClient(HttpClient httpClient, VoiceOptions options) : IEventGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string SystemPrompt = """
        Ты помогаешь Методисту создать Черновик События для тренировки проводника.
        Верни только JSON вида {"event":{...}} или {"event":null}, если пункт Источника не описывает ситуацию.
        event — граф с полями id (временный), version:1, title, topic, start и steps.
        Коды id Шагов и Вариантов — латиницей, тексты — по-русски.
        Каждый обычный Шаг имеет answerType:"buttons", situation и variants; каждый Вариант
        имеет id, text, source (дословная короткая цитата из пункта) и transitions:[{"to":"id шага"}].
        Добавь минимум два осмысленных Варианта и конечные Шаги с outcome:"success" или "failure".
        Все переходы ведут к существующим Шагам, из каждого Шага можно дойти до Исхода.
        Не выдумывай правила и цитаты, не включай персональные данные. Не публикуй Событие.
        """;

    public async Task<string> GenerateAsync(EventGenerationPrompt prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.PolzaApiKey))
            throw new InvalidOperationException("Не задан ключ polza.ai для генерации Событий");
        var userPrompt = $"Источник: {prompt.SourceTitle}\nПункт: {prompt.Section}\nТемы: {string.Join(", ", prompt.Topics)}\nТекст:\n{prompt.Text}";
        if (!string.IsNullOrWhiteSpace(prompt.Feedback))
            userPrompt += $"\nПредыдущий ответ:\n{prompt.PreviousOutput}\nИсправь ошибки: {prompt.Feedback}";
        var payload = new
        {
            model = prompt.Model,
            stream = false,
            max_tokens = 8_000,
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
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
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
            throw new JsonException("Модель не вернула JSON События");
        return content.GetString()!;
    }
}
