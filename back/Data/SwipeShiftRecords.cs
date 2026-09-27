using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>
/// Смена на свайпах: проводник, Режим, Цикл и прошлый Цикл, колода по порядку, снимок справочника Шкал
/// на старте, исход и момент показа текущей карточки — от него сервер считает время ответа.
/// Состояние Смены отдельно не хранится: правила заново проигрывают ответы по порядку.
/// </summary>
public sealed class SwipeShiftRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>calm — «В своём темпе», woodpecker — «На скорость».</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Номер Цикла «На скорость», с 1; null — «В своём темпе».</summary>
    public int? Cycle { get; set; }

    public Guid? PreviousCycleId { get; set; }

    /// <summary>running, passed или failed.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Причина Срыва: код Шкалы, дошедшей до порога.</summary>
    public string? FailureScale { get; set; }

    /// <summary>Колода: id Вопросов в порядке показа (jsonb).</summary>
    public string Deck { get; set; } = string.Empty;

    /// <summary>Снимок справочника Шкал на старте (jsonb): правка справочника не меняет идущие и прошлые Смены.</summary>
    public string Scales { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Когда показали текущую карточку; null — ответ уже дан, а следующую ещё не просили.</summary>
    public DateTimeOffset? CardShownAt { get; set; }
}

/// <summary>Ответ на карточку Смены, по порядку Seq. Время ответа засекает сервер — от конца печати формулировки.</summary>
public sealed class SwipeAnswerRecord
{
    public Guid ShiftId { get; set; }
    public int Seq { get; set; }
    public string QuestionId { get; set; } = string.Empty;

    /// <summary>right, left, unknown («Не знаю») или timeout («Время вышло», засчитано как «Не знаю»).</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>correct, wrong или unknown.</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>Карточка вернулась Повтором после ошибки или «Не знаю».</summary>
    public bool IsRepeat { get; set; }

    public int ElapsedMs { get; set; }

    /// <summary>Фактические изменения Шкал после обрезки по границам: код Шкалы → изменение (jsonb).</summary>
    public string ScaleChanges { get; set; } = string.Empty;

    public DateTimeOffset AnsweredAt { get; set; }
}

internal static class SwipeShiftModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SwipeShiftRecord>(entity =>
        {
            entity.HasKey(shift => shift.Id);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(shift => shift.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SwipeShiftRecord>().WithMany().HasForeignKey(shift => shift.PreviousCycleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(shift => shift.UserId);
            entity.Property(shift => shift.Mode).HasMaxLength(20).IsRequired();
            entity.Property(shift => shift.Status).HasMaxLength(20).IsRequired();
            entity.Property(shift => shift.FailureScale).HasMaxLength(50);
            entity.Property(shift => shift.Deck).HasColumnType("jsonb").IsRequired();
            entity.Property(shift => shift.Scales).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<SwipeAnswerRecord>(entity =>
        {
            entity.HasKey(answer => new { answer.ShiftId, answer.Seq });
            entity.HasOne<SwipeShiftRecord>().WithMany().HasForeignKey(answer => answer.ShiftId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<QuestionRecord>().WithMany().HasForeignKey(answer => answer.QuestionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(answer => answer.QuestionId).HasMaxLength(100).IsRequired();
            entity.Property(answer => answer.Answer).HasMaxLength(20).IsRequired();
            entity.Property(answer => answer.Verdict).HasMaxLength(20).IsRequired();
            entity.Property(answer => answer.ScaleChanges).HasColumnType("jsonb").IsRequired();
        });
    }
}
