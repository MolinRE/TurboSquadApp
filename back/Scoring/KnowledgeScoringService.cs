using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;

namespace TurboSquadApp.Scoring;

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
    public async Task<int> ApplyAsync(Guid userId, string unitType, string unitId, bool correct, int cost,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var mastery = await db.KnowledgeMasteries.SingleOrDefaultAsync(item => item.UserId == userId
            && item.UnitType == unitType && item.UnitId == unitId && item.Competence == "knowledge", cancellationToken);
        var oldAward = mastery is { IsMastered: true } ? mastery.AwardedCost : 0;
        var delta = correct ? (oldAward == 0 ? cost : 0) : -oldAward;
        var currentPoints = await db.KnowledgeMasteries.Where(item => item.UserId == userId && item.IsMastered)
            .SumAsync(item => item.AwardedCost, cancellationToken);

        mastery ??= new KnowledgeMasteryRecord
        {
            UserId = userId, UnitType = unitType, UnitId = unitId, Competence = "knowledge",
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
