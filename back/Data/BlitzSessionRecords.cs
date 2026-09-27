using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>
/// Сессия Блица: проводник, колода — снимок содержимого Вопросов на старте, исход и момент показа текущего
/// Вопроса — от него сервер считает время ответа. Правка Вопроса в CMS начатую сессию не меняет.
/// Текущий Вопрос отдельно не хранится: это Вопрос колоды с номером, равным числу ответов.
/// </summary>
public sealed class BlitzSessionRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>running или finished.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Колода по порядку показа (jsonb): снимки Вопросов с верными вариантами и Пояснением.</summary>
    public string Deck { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Когда показали текущий Вопрос; null — ответ уже дан, а следующий ещё не просили.</summary>
    public DateTimeOffset? QuestionShownAt { get; set; }
}

/// <summary>Ответ на Вопрос сессии, по порядку Seq. На каждый Вопрос сессии — не больше одного ответа.</summary>
public sealed class BlitzAnswerRecord
{
    public Guid SessionId { get; set; }
    public int Seq { get; set; }
    public string QuestionId { get; set; } = string.Empty;

    /// <summary>Выбранные варианты (jsonb, массив id); при «Время вышло» — пустой.</summary>
    public string SelectedOptionIds { get; set; } = "[]";

    /// <summary>«Время вышло»: лимит истёк, ответ засчитан как «Не знаю».</summary>
    public bool TimedOut { get; set; }

    /// <summary>correct, wrong или unknown.</summary>
    public string Verdict { get; set; } = string.Empty;

    public int ElapsedMs { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }
}

internal static class BlitzSessionModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlitzSessionRecord>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(session => session.UserId);
            entity.Property(session => session.Status).HasMaxLength(20).IsRequired();
            entity.Property(session => session.Deck).HasColumnType("jsonb").IsRequired();
        });

        // Без внешнего ключа на Вопрос: сессия живёт по своему снимку, даже если Черновик потом удалят.
        modelBuilder.Entity<BlitzAnswerRecord>(entity =>
        {
            entity.HasKey(answer => new { answer.SessionId, answer.Seq });
            entity.HasOne<BlitzSessionRecord>().WithMany().HasForeignKey(answer => answer.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(answer => new { answer.SessionId, answer.QuestionId }).IsUnique();
            entity.Property(answer => answer.QuestionId).HasMaxLength(100).IsRequired();
            entity.Property(answer => answer.SelectedOptionIds).HasColumnType("jsonb").IsRequired();
            entity.Property(answer => answer.Verdict).HasMaxLength(20).IsRequired();
        });
    }
}
