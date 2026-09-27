using System.Text.Json.Serialization;
using TurboSquadApp.Voice;

namespace TurboSquadApp.Trips;

/// <summary>
/// Разбор, слой А (PRD v7 §8): факты Рейса по порядку, без ИИ. Строится из журнала и контента,
/// зафиксированного на старте Рейса, поэтому тексты — той версии События, которую реально играли.
/// </summary>
public sealed record Debrief(
    TripStatus Result, string Summary, IReadOnlyList<DebriefItem> Items,
    IReadOnlyList<DebriefVoiceAttempt> VoiceAttempts)
{
    [JsonIgnore]
    public IEnumerable<DebriefEvent> Events => Items.OfType<DebriefEvent>();

    public static Debrief Build(TripState state, IReadOnlyList<DebriefDecisionFacts>? decisionFacts = null)
    {
        if (!state.IsFinished)
            throw new InvalidOperationException("Разбор строится после Рейса: Рейс ещё не закончен");
        var content = state.Content!;

        var items = new List<DebriefItem>();
        var decisions = new List<DebriefDecision>();
        var decisionIndex = 0;
        foreach (var entry in state.Journal)
        {
            switch (entry)
            {
                case ProactiveChosen chosen:
                    var proactive = content.Settings.ProactiveChoice;
                    items.Add(new DebriefProactiveChoice(proactive.Situation, proactive.Options.Single(o => o.Id == chosen.OptionId).Text));
                    break;
                case Decision decision:
                    decisions.Add(DecisionOf(content, decision,
                        decisionFacts is not null ? decisionFacts[decisionIndex] : null));
                    decisionIndex++;
                    break;
                case EventFinished finished:
                    var ev = content.Event(finished.EventId);
                    var outcome = ev.Steps.SingleOrDefault(s => s.Id == finished.OutcomeStepId);
                    items.Add(new DebriefEvent(ev.Id, ev.Version, ev.Title, finished.Result, outcome?.Id, outcome?.Situation, decisions));
                    decisions = [];
                    break;
            }
        }
        var voiceAttempts = state.Journal.OfType<VoiceAttempt>()
            .Select(attempt => new DebriefVoiceAttempt(
                attempt.EventId, attempt.EventVersion, attempt.StepId, attempt.Transcript, attempt.Choice,
                attempt.Confidence, attempt.LatencyMs, attempt.Applied, attempt.ErrorCode,
                attempt.ProviderRequestId, attempt.AttemptId, attempt.PassengerReply, attempt.ReplyError,
                attempt.Assessment?.Score, attempt.Assessment?.ScoreConfidence,
                attempt.Assessment?.RoleStages ?? new Dictionary<string, double>(),
                attempt.Assessment?.SafetyViolation, attempt.Assessment?.SafetyConfidence,
                attempt.SttLatencyMs, attempt.LayaLatencyMs, attempt.LlmLatencyMs))
            .ToList();
        return new Debrief(state.Status, SummaryOf(state), items, voiceAttempts);
    }

    /// <summary>Итог Рейса одной фразой.</summary>
    internal static string SummaryOf(TripState state)
    {
        if (state.Failure is not { } failure) return "Прибытие: все запланированные События пройдены";
        if (failure.Cause == FailureCause.CriticalError)
        {
            var decision = state.Journal.OfType<Decision>().Last(d => d.CriticalError);
            return $"Срыв рейса: Критическая ошибка — «{DecisionOf(state.Content!, decision).Text}»";
        }
        var scale = state.Content!.Directory.Scale(failure.Scale!);
        var reason = scale.FailureReason is null ? "" : $" — {scale.FailureReason}";
        return $"Срыв рейса: Шкала «{scale.Name}» упала до {state.Scales[scale.Code]}{reason}";
    }

    private static DebriefDecision DecisionOf(TripContent content, Decision decision, DebriefDecisionFacts? facts = null)
    {
        var ev = content.Event(decision.EventId);
        var step = ev.Steps.Single(s => s.Id == decision.StepId);
        var reaction = decision.TimedOut ? step.Timeout! : step.Variants!.Single(v => v.Id == decision.VariantId);
        var changes = decision.Changes
            .Select(c => new DebriefScaleChange(
                c.Scale, content.Directory.Scale(c.Scale).Name, c.Nominal, c.Applied, c.Before, c.After))
            .ToList();
        return new DebriefDecision(
            step.Id, step.Situation, decision.VariantId, decision.TimedOut, reaction.Text, changes, decision.FlagsSet,
            reaction.RoleStages ?? [], reaction.Comment, reaction.Source ?? ev.Source, decision.CriticalError,
            facts?.KnowledgeDelta ?? 0, facts?.ElapsedMs);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DebriefProactiveChoice), "proactiveChoice")]
[JsonDerivedType(typeof(DebriefEvent), "event")]
public abstract record DebriefItem;

public sealed record DebriefProactiveChoice(string Situation, string Choice) : DebriefItem;

/// <summary>Событие Рейса: Исход или «прервано», если в нём случился Срыв рейса.</summary>
public sealed record DebriefEvent(
    string EventId, int Version, string Title, EventResult Result, string? OutcomeStepId, string? OutcomeSituation,
    IReadOnlyList<DebriefDecision> Decisions) : DebriefItem;

/// <summary>Решение проводника или таймаут: что изменилось и почему так, как лучше.</summary>
public sealed record DebriefDecision(
    string StepId, string Situation, string? VariantId, bool TimedOut, string Text,
    IReadOnlyList<DebriefScaleChange> Changes, IReadOnlyList<string> FlagsSet, IReadOnlyList<string> RoleStages,
    string? Comment, string? Source, bool CriticalError, int KnowledgeDelta = 0, int? ElapsedMs = null);

public sealed record DebriefDecisionFacts(int KnowledgeDelta, int? ElapsedMs);

public sealed record TripDebriefListItem(Guid Id, string Result, DateTimeOffset StartedAt, DateTimeOffset FinishedAt);

/// <summary>Изменение Шкалы: по контенту (Nominal) и фактически после обрезки по диапазону (Applied).</summary>
public sealed record DebriefScaleChange(string Scale, string Name, int Nominal, int Applied, int Before, int After);

public sealed record DebriefVoiceAttempt(
    string EventId, int EventVersion, string StepId, string? Transcript, string? Choice,
    double? Confidence, int LatencyMs, bool Applied, string? ErrorCode, string? ProviderRequestId,
    string AttemptId, string? PassengerReply, string? ReplyError,
    double? Score, double? ScoreConfidence, IReadOnlyDictionary<string, double> RoleStages,
    double? SafetyViolation, double? SafetyConfidence,
    int? SttLatencyMs, int? LayaLatencyMs, int? LlmLatencyMs);
