using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Voice;

/// <summary>
/// Детерминированный набор фактов, который разрешено видеть генератору реплики.
/// StepBrief и StepSituation — Шаг переданного состояния: для реплики после применённого
/// Варианта это следующий Шаг, для уточнения — текущий.
/// </summary>
public sealed record PassengerReplyContext(
    string? StepBrief,
    string? StepSituation,
    string? LayaChoice,
    string? LayaChoiceText,
    string? LayaChoiceCriterion,
    double? LayaConfidence,
    string? ConductorTranscript,
    IReadOnlyList<DialogueLine> RecentLines,
    IReadOnlyList<string> Flags,
    IReadOnlyList<ScaleSnapshot> Scales)
{
    private const string Role = "Ты играешь роль пассажира в учебном диалоге проводника. ";
    private const string Format =
        "Используй только переданный контекст. Не добавляй пояснения, оценку, метаданные или кавычки.";

    // Кириллица без \uXXXX: промпт уходит в LLM, а не в HTML, и так в разы короче в токенах.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static PassengerReplyContext From(TripState state, VoiceAttempt attempt)
    {
        var lines = state.Journal
            .OfType<VoiceAttempt>()
            .Where(item => !string.Equals(item.AttemptId, attempt.AttemptId, StringComparison.Ordinal))
            .SelectMany(item => new[]
            {
                item.Transcript is null ? null : new DialogueLine("conductor", item.Transcript),
                item.PassengerReply is null ? null : new DialogueLine("passenger", item.PassengerReply),
            })
            .Where(item => item is not null)
            .Cast<DialogueLine>()
            .TakeLast(2)
            .ToList();

        var scales = state.Content!.Directory.Scales
            .OrderBy(scale => scale.Code, StringComparer.Ordinal)
            .Select(scale => new ScaleSnapshot(scale.Code, state.Scales[scale.Code]))
            .ToList();
        var choice = attempt.Choice is null
            ? null
            : state.Content.Event(attempt.EventId).Steps
                .Single(step => step.Id == attempt.StepId).Variants?
                .SingleOrDefault(variant => variant.Id == attempt.Choice);

        return new PassengerReplyContext(
            state.CurrentStep?.Brief,
            state.CurrentStep?.Situation,
            attempt.Choice,
            choice?.Text,
            choice?.LayaText,
            attempt.Confidence,
            attempt.Transcript,
            lines,
            state.Flags.Order(StringComparer.Ordinal).ToList(),
            scales);
    }

    public LlmRequest ToLlmRequest() => new(
        Role + "Сформулируй одну короткую естественную реплику на русском языке. " + Tone + Format,
        JsonSerializer.Serialize(new
        {
            nextStepBrief = StepBrief,
            nextStepSituation = StepSituation,
            laya = new { choice = LayaChoice, choiceText = LayaChoiceText, confidence = LayaConfidence },
            conductorTranscript = ConductorTranscript,
            recentLines = RecentLines,
            flags = Flags,
            scales = Scales,
        }, JsonOptions));

    /// <summary>
    /// Laya не уверена в Варианте: пассажир переспрашивает, правильно ли понял проводника,
    /// называя самый вероятный Вариант. Рейс при этом не продвигается.
    /// </summary>
    // Намерение вписано прямо в инструкцию: из отдельного поля JSON модели 27.09.2026 его часто теряли.
    public LlmRequest ToClarificationRequest() => new(
        Role + "Проводник ответил так, что непонятно, что он собирается делать. " +
        (string.IsNullOrWhiteSpace(LayaChoiceCriterion)
            ? "Попроси его одной короткой репликой на русском языке уточнить, что он имеет в виду. "
            : "Переспроси одной короткой репликой на русском языке, правильно ли ты понял, что " +
              char.ToLowerInvariant(LayaChoiceCriterion[0]) + LayaChoiceCriterion[1..].TrimEnd('.') + ". ") +
        "Говори с проводником на «вы», как пассажир, а не пересказывай его слова в третьем лице. " +
        "Не соглашайся и не возражай заранее, не подсказывай, как правильно. " + Tone + Format,
        JsonSerializer.Serialize(new
        {
            situation = StepSituation,
            conductorTranscript = ConductorTranscript,
            recentLines = RecentLines,
        }, JsonOptions));

    private string Tone =>
        Scales.FirstOrDefault(scale => scale.Code == PassengerTone.LoyaltyScale) is { } loyalty
            ? PassengerTone.For(loyalty.Value) + " "
            : "";
}

/// <summary>Тон пассажира по Лояльности (0–100, старт 70): чем она ниже, тем резче пассажир.</summary>
public static class PassengerTone
{
    public const string LoyaltyScale = "loyalty";

    public static string For(int loyalty) => loyalty switch
    {
        >= 70 => "Тон: ты настроен доброжелательно, говоришь спокойно и вежливо.",
        >= 40 => "Тон: ты недоволен, в голосе слышно раздражение, говоришь сдержанно и коротко.",
        _ => "Тон: ты на грани конфликта, говоришь резко и нетерпеливо, но без оскорблений и мата.",
    };
}

public sealed record DialogueLine(string Speaker, string Text);

public sealed record ScaleSnapshot(string Code, int Value);
