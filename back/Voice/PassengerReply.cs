using System.Text.Json;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Voice;

/// <summary>Детерминированный набор фактов, который разрешено видеть генератору реплики.</summary>
public sealed record PassengerReplyContext(
    string? NextStepBrief,
    string? NextStepSituation,
    string? LayaChoice,
    double? LayaConfidence,
    string? ConductorTranscript,
    IReadOnlyList<DialogueLine> RecentLines,
    IReadOnlyList<string> Flags,
    IReadOnlyList<ScaleSnapshot> Scales)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PassengerReplyContext From(TripState state, VoiceAttempt attempt)
    {
        var lines = state.Journal
            .OfType<VoiceAttempt>()
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

        return new PassengerReplyContext(
            state.CurrentStep?.Brief,
            state.CurrentStep?.Situation,
            attempt.Choice,
            attempt.Confidence,
            attempt.Transcript,
            lines,
            state.Flags.Order(StringComparer.Ordinal).ToList(),
            scales);
    }

    public LlmRequest ToLlmRequest() => new(
        "Ты играешь роль пассажира в учебном диалоге проводника. " +
        "Сформулируй одну короткую естественную реплику на русском языке. " +
        "Используй только переданный контекст. Не добавляй пояснения, оценку, метаданные или кавычки.",
        JsonSerializer.Serialize(new
        {
            nextStepBrief = NextStepBrief,
            nextStepSituation = NextStepSituation,
            laya = new { choice = LayaChoice, confidence = LayaConfidence },
            conductorTranscript = ConductorTranscript,
            recentLines = RecentLines,
            flags = Flags,
            scales = Scales,
        }, JsonOptions));
}

public sealed record DialogueLine(string Speaker, string Text);

public sealed record ScaleSnapshot(string Code, int Value);
