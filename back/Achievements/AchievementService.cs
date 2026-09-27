using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Scoring;

namespace TurboSquadApp.Achievements;

public sealed record AchievementDefinition(string Id, string Name, string Description);
public sealed record AchievementView(string Id, string Name, string Description, DateTimeOffset? EarnedAt);

/// <summary>Фиксированные правила MVP; выдачи хранятся отдельно от истории тренировок.</summary>
public sealed class AchievementService(AppDbContext db, TimeProvider clock)
{
    public static readonly IReadOnlyList<AchievementDefinition> Definitions =
    [
        new("first-shift", "Первая Смена", "Завершить одну Смену на свайпах без Срыва."),
        new("three-shifts", "Три Смены", "Завершить три разные Смены на свайпах без Срыва."),
        new("clean-shift", "Чистая Смена", "Завершить Смену, ответив верно с первой попытки на каждую карточку, без «Не знаю» и таймаутов."),
        new("knowledge-10", "Знаток 10", "Хотя бы раз верно ответить на 10 разных Вопросов в Смене на свайпах."),
        new("knowledge-20", "Знаток 20", "Хотя бы раз верно ответить на 20 разных Вопросов в Смене на свайпах."),
        new("first-arrival", "Первое Прибытие", "Довести один Рейс до Прибытия."),
        new("three-arrivals", "Три Прибытия", "Довести три разных Рейса до Прибытия."),
        new("flawless-trip", "Без ошибок в Рейсе", "Довести Рейс до Прибытия, верно пройдя каждый оцениваемый Шаг."),
        new("first-class", "Первый класс", "Довести до Прибытия Рейс в Первом классе обслуживания."),
        new("two-formats", "Два формата", "Завершить Смену без Срыва и довести Рейс до Прибытия."),
    ];

    public async Task<IReadOnlyList<AchievementView>> ListAsync(Guid userId, CancellationToken ct)
    {
        // В том числе выдаёт по уже сохранённым тренировкам, если они были до появления Ачивок.
        await EvaluateAsync(userId, ct);
        var earned = await db.AchievementAwards.AsNoTracking()
            .Where(award => award.UserId == userId)
            .ToDictionaryAsync(award => award.AchievementId, award => award.EarnedAt, ct);
        return Definitions.Select(definition => new AchievementView(
            definition.Id, definition.Name, definition.Description,
            earned.GetValueOrDefault(definition.Id) is { } at && at != default ? at : null)).ToList();
    }

    public async Task EvaluateAsync(Guid userId, CancellationToken ct)
    {
        var earned = (await db.AchievementAwards.AsNoTracking()
            .Where(award => award.UserId == userId)
            .Select(award => award.AchievementId)
            .ToListAsync(ct)).ToHashSet();
        if (earned.Count == Definitions.Count) return;

        var shifts = await db.SwipeShifts.AsNoTracking()
            .Where(shift => shift.UserId == userId)
            .Select(shift => new { shift.Id, shift.Status })
            .ToListAsync(ct);
        var passed = shifts.Where(shift => shift.Status == "passed").Select(shift => shift.Id).ToHashSet();
        var shiftIds = shifts.Select(shift => shift.Id).ToArray();
        var answers = await db.SwipeAnswers.AsNoTracking()
            .Where(answer => shiftIds.Contains(answer.ShiftId))
            .Select(answer => new { answer.ShiftId, answer.QuestionId, answer.Answer, answer.Verdict, answer.IsRepeat })
            .ToListAsync(ct);
        var eligible = new HashSet<string>();
        if (passed.Count >= 1) eligible.Add("first-shift");
        if (passed.Count >= 3) eligible.Add("three-shifts");
        if (passed.Any(id =>
            {
                var attempts = answers.Where(answer => answer.ShiftId == id).ToList();
                return attempts.Count > 0 && attempts.All(answer => answer.Verdict == "correct" &&
                    !answer.IsRepeat && answer.Answer is not ("unknown" or "timeout"));
            })) eligible.Add("clean-shift");
        var distinctCorrect = answers.Where(answer => answer.Verdict == "correct")
            .Select(answer => answer.QuestionId).Distinct().Count();
        if (distinctCorrect >= 10) eligible.Add("knowledge-10");
        if (distinctCorrect >= 20) eligible.Add("knowledge-20");

        var arrived = await db.Trips.AsNoTracking()
            .Where(trip => trip.UserId == userId && trip.Status == "arrived")
            .Select(trip => new { trip.Id, trip.ServiceClass })
            .ToListAsync(ct);
        if (arrived.Count >= 1) eligible.Add("first-arrival");
        if (arrived.Count >= 3) eligible.Add("three-arrivals");
        if (arrived.Any(trip => trip.ServiceClass == "first")) eligible.Add("first-class");
        if (passed.Count > 0 && arrived.Count > 0) eligible.Add("two-formats");
        if (arrived.Count > 0 && !earned.Contains("flawless-trip") &&
            await HasFlawlessTripAsync(arrived.Select(trip => trip.Id).ToArray(), ct))
            eligible.Add("flawless-trip");

        foreach (var id in eligible.Except(earned))
            db.AchievementAwards.Add(new AchievementAwardRecord
            {
                UserId = userId, AchievementId = id, EarnedAt = clock.GetUtcNow(),
            });
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    private async Task<bool> HasFlawlessTripAsync(Guid[] tripIds, CancellationToken ct)
    {
        var decisions = await db.TripJournal.AsNoTracking()
            .Where(row => tripIds.Contains(row.TripId) && row.Kind == TripJournalKinds.Decision)
            .Select(row => new { row.TripId, row.EventId, row.EventVersion, row.StepId, row.VariantId, row.TimedOut })
            .ToListAsync(ct);
        var eventIds = decisions.Select(row => row.EventId).Where(id => id is not null).Distinct().ToArray();
        var documents = await db.EventDocuments.AsNoTracking()
            .Where(document => eventIds.Contains(document.EventId))
            .ToListAsync(ct);
        var events = documents.ToDictionary(document => (document.EventId, document.Version),
            document => JsonSerializer.Deserialize<EventDocument>(document.Document, EventJson.Options)!);

        foreach (var tripId in tripIds)
        {
            var evaluated = 0;
            var flawless = true;
            foreach (var row in decisions.Where(row => row.TripId == tripId))
            {
                if (row.EventId is null || row.EventVersion is null || row.StepId is null ||
                    !events.TryGetValue((row.EventId, row.EventVersion.Value), out var document))
                {
                    flawless = false;
                    break;
                }
                var step = document.Steps.SingleOrDefault(step => step.Id == row.StepId);
                if (step is null)
                {
                    flawless = false;
                    break;
                }
                if (KnowledgeScoringService.TripKnowledgeCost(step, row.VariantId, row.TimedOut) is not { } cost)
                    continue;
                evaluated++;
                if (cost <= 0)
                {
                    flawless = false;
                    break;
                }
            }
            if (flawless && evaluated > 0) return true;
        }
        return false;
    }
}
