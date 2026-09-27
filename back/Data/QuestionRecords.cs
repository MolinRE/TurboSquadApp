using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>
/// Вопрос банка (PRD §9.1), общий для Смены на свайпах, Блица и будущего банка в CMS. Вычисляемое
/// (% успешных ответов, реальное время ответа, Личная частота показа) здесь не хранится.
/// Стоимость по Компетенциям придёт вместе с Оценкой.
/// </summary>
public sealed class QuestionRecord
{
    /// <summary>Код латиницей, как id Событий.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>single, multiple, sequence или swipe (QuestionTypes).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>draft — Черновик, published — опубликован (QuestionStatuses).</summary>
    public string Status { get; set; } = string.Empty;

    public string Statement { get; set; } = string.Empty;

    /// <summary>
    /// Варианты и верные ответы по типу (jsonb): свайп — SwipeOptions, один или несколько ответов —
    /// ChoiceOptions, последовательность — SequenceOptions.
    /// </summary>
    public string Options { get; set; } = string.Empty;

    /// <summary>Пояснение: одна-две фразы и ключевой факт — дословный фрагмент текста.</summary>
    public string ExplanationText { get; set; } = string.Empty;
    public string ExplanationKeyFact { get; set; } = string.Empty;

    /// <summary>Цитата из Источника.</summary>
    public string? Quote { get; set; }

    /// <summary>Пункт Источника.</summary>
    public string Source { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    /// <summary>Категории (jsonb, массив строк).</summary>
    public string Categories { get; set; } = "[]";

    /// <summary>Коды Классов обслуживания (jsonb); пустой массив — все классы.</summary>
    public string ServiceClasses { get; set; } = "[]";

    public double BaseFrequency { get; set; } = 1;

    /// <summary>Время на ответ, с; null — лимит игры (для Циклов этапа 2).</summary>
    public int? TimeLimitSec { get; set; }
}

internal static class QuestionModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuestionRecord>(entity =>
        {
            entity.HasKey(question => question.Id);
            entity.Property(question => question.Id).HasMaxLength(100);
            entity.Property(question => question.Type).HasMaxLength(20).IsRequired();
            entity.Property(question => question.Status).HasMaxLength(20).IsRequired();
            entity.Property(question => question.Statement).IsRequired();
            entity.Property(question => question.Options).HasColumnType("jsonb").IsRequired();
            entity.Property(question => question.ExplanationText).IsRequired();
            entity.Property(question => question.ExplanationKeyFact).IsRequired();
            entity.Property(question => question.Source).HasMaxLength(300).IsRequired();
            entity.Property(question => question.Topic).HasMaxLength(100).IsRequired();
            entity.Property(question => question.Categories).HasColumnType("jsonb").IsRequired();
            entity.Property(question => question.ServiceClasses).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(question => new { question.Type, question.Status });
        });
    }
}
