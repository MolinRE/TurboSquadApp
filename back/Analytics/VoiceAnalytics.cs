using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Voice;

namespace TurboSquadApp.Analytics;

public sealed class VoiceAnalyticsService(AppDbContext dbContext, VoiceOptions voiceOptions)
{
    public async Task<VoiceAnalyticsView> BuildAsync(CancellationToken cancellationToken)
    {
        var attempts = await dbContext.TripJournal
            .Where(row => row.Kind == TripJournalKinds.VoiceAttempt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var uncertain = attempts.Count(attempt => attempt.VoiceConfidence is { } confidence && confidence < voiceOptions.MinimumConfidence);
        var steps = attempts
            .Where(attempt => attempt.EventId is not null && attempt.EventVersion is not null && attempt.StepId is not null)
            .GroupBy(attempt => (attempt.EventId!, attempt.EventVersion!.Value, attempt.StepId!))
            .OrderBy(group => group.Key.Item1, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Item2)
            .ThenBy(group => group.Key.Item3, StringComparer.Ordinal)
            .Select(group => new VoiceStepAnalyticsView(
                group.Key.Item1,
                group.Key.Item2,
                group.Key.Item3,
                group.Count(),
                group.Count(attempt => attempt.VoiceApplied),
                group.Count(attempt => attempt.VoiceConfidence is { } confidence && confidence < voiceOptions.MinimumConfidence),
                group.Average(attempt => attempt.VoiceScore),
                Latencies(group.Select(attempt => attempt.VoiceSttLatencyMs)),
                Latencies(group.Select(attempt => attempt.VoiceLayaLatencyMs)),
                Latencies(group.Select(attempt => attempt.VoiceLlmLatencyMs))))
            .ToList();

        return new VoiceAnalyticsView(
            attempts.Count,
            attempts.Count(attempt => attempt.VoiceApplied),
            attempts.Count(attempt => !attempt.VoiceApplied),
            uncertain,
            Rate(uncertain, attempts.Count),
            // Product fallbacks are intentionally disabled; the metric remains explicit for analytics consumers.
            0,
            0,
            Latencies(attempts.Select(attempt => attempt.VoiceSttLatencyMs)),
            Latencies(attempts.Select(attempt => attempt.VoiceLayaLatencyMs)),
            Latencies(attempts.Select(attempt => attempt.VoiceLlmLatencyMs)),
            steps);
    }

    private static VoiceLatencyView Latencies(IEnumerable<int?> values)
    {
        var sorted = values.Where(value => value is not null).Select(value => value!.Value).OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return VoiceLatencyView.Empty;
        return new(
            sorted.Length,
            sorted.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95));
    }

    private static double Percentile(IReadOnlyList<int> sorted, double percentile)
    {
        var index = (sorted.Count - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }

    private static double Rate(int count, int total) => total == 0 ? 0 : (double)count / total;
}

public sealed record VoiceAnalyticsView(
    int Attempts,
    int AppliedAttempts,
    int FailedAttempts,
    int UncertainAttempts,
    double UncertainRate,
    int FallbackAttempts,
    double FallbackRate,
    VoiceLatencyView Stt,
    VoiceLatencyView Laya,
    VoiceLatencyView Llm,
    IReadOnlyList<VoiceStepAnalyticsView> Steps);

public sealed record VoiceLatencyView(int Count, double AverageMs, double P50Ms, double P95Ms)
{
    public static VoiceLatencyView Empty { get; } = new(0, 0, 0, 0);
}

public sealed record VoiceStepAnalyticsView(
    string EventId,
    int EventVersion,
    string StepId,
    int Attempts,
    int AppliedAttempts,
    int UncertainAttempts,
    double? AverageScore,
    VoiceLatencyView Stt,
    VoiceLatencyView Laya,
    VoiceLatencyView Llm);
