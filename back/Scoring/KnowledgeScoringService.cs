using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TurboSquadApp.Data;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Scoring;

/// <summary>Типизированный ключ Единицы знания; JSON-массив сохраняет пару Событие/Шаг без коллизий разделителя.</summary>
public sealed record KnowledgeUnit
{
    private KnowledgeUnit(string type, string id) { Type = type; Id = id; }
    public string Type { get; }
    public string Id { get; }

    public static KnowledgeUnit Question(string questionId) => new("question", questionId);
    public static KnowledgeUnit EventStep(string eventId, string stepId) =>
        new("step", JsonSerializer.Serialize(new[] { eventId, stepId }));
}

public static class KnowledgeRanks
{
    // Мини-MVP: пороги откалиброваны под сидовую колоду. Новые Компетенции и Бонус регулярности придут позже.
    public static readonly (int Threshold, string Name)[] Values =
    [
        (0, "Стажёр"), (50, "Проводник"), (120, "Старший проводник"),
        (250, "Наставник"), (500, "Эксперт ВСМ"),
    ];

    public static int IndexFor(int points) => Array.FindLastIndex(Values, rank => points >= rank.Threshold);
}

/// <summary>Освоение по последней попытке, общее для Смены, Рейса и Блица.</summary>
public sealed class KnowledgeScoringService(AppDbContext db)
{
    public Task<int> ApplyTripDecisionAsync(Guid userId, TripState before, Decision decision,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (before.CurrentStep is not { } step ||
            !step.Reactions.Any(reaction => reaction.Competencies?.GetValueOrDefault("knowledge") > 0))
            return Task.FromResult(0);

        var reaction = decision.TimedOut ? step.Timeout : step.Variants?.Single(v => v.Id == decision.VariantId);
        var cost = reaction?.Competencies?.GetValueOrDefault("knowledge") ?? 0;
        return ApplyAsync(userId, KnowledgeUnit.EventStep(decision.EventId, decision.StepId),
            cost > 0, cost, now, cancellationToken);
    }

    public async Task<int> ApplyAsync(Guid userId, KnowledgeUnit unit, bool correct, int cost,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var mastery = await db.KnowledgeMasteries.SingleOrDefaultAsync(item => item.UserId == userId
            && item.UnitType == unit.Type && item.UnitId == unit.Id && item.Competence == "knowledge", cancellationToken);
        var oldAward = mastery is { IsMastered: true } ? mastery.AwardedCost : 0;
        var delta = correct ? (oldAward == 0 ? cost : 0) : -oldAward;
        var currentPoints = await db.KnowledgeMasteries.Where(item => item.UserId == userId && item.IsMastered)
            .SumAsync(item => item.AwardedCost, cancellationToken);

        mastery ??= new KnowledgeMasteryRecord
        {
            UserId = userId, UnitType = unit.Type, UnitId = unit.Id, Competence = "knowledge",
        };
        if (db.Entry(mastery).State == EntityState.Detached) db.KnowledgeMasteries.Add(mastery);
        mastery.IsMastered = correct;
        mastery.AwardedCost = correct ? (oldAward == 0 ? cost : oldAward) : 0;
        mastery.LastAttemptAt = now;

        var profile = await db.ConductorProfiles.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (profile is null)
        {
            profile = new ConductorProfileRecord { UserId = userId };
            db.ConductorProfiles.Add(profile);
        }
        profile.RankIndex = Math.Max(profile.RankIndex, KnowledgeRanks.IndexFor(currentPoints + delta));
        return delta;
    }
}
