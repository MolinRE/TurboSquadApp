using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>
/// Запись Рейса (PRD v7 §8): владелец, Класс обслуживания, контент, зафиксированный на старте
/// (справочники и настройка Рейса снимком, События — версиями), итог и момент показа текущего Шага —
/// от него сервер считает таймер.
/// </summary>
public sealed class TripRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ServiceClass { get; set; } = string.Empty;

    /// <summary>running, arrived или failed.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Причина Срыва: criticalError или scale (тогда FailureScale — код Шкалы).</summary>
    public string? FailureCause { get; set; }
    public string? FailureScale { get; set; }

    /// <summary>Снимок справочников Шкал и Классов на старте (jsonb): правка справочника не меняет идущие и прошлые Рейсы.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Снимок настройки Рейса на старте (jsonb): Заступ, число Событий, Проактивный выбор.</summary>
    public string Settings { get; set; } = string.Empty;

    /// <summary>id События → версия, которую играют в этом Рейсе (jsonb).</summary>
    public string EventVersions { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset StepStartedAt { get; set; }
}

/// <summary>
/// Строка журнала Рейса (ADR-0001), по порядку Seq: Проактивный выбор, решение на Шаге или итог События.
/// Решение ссылается на версию События; таймаут — TimedOut без VariantId.
/// </summary>
public sealed class TripJournalRecord
{
    public Guid TripId { get; set; }
    public int Seq { get; set; }

    /// <summary>proactiveChoice, decision или eventFinished.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public string? OptionId { get; set; }
    public string? EventId { get; set; }
    public int? EventVersion { get; set; }
    public string? StepId { get; set; }
    public string? VariantId { get; set; }
    public bool TimedOut { get; set; }

    /// <summary>Сколько прошло с показа Шага или Проактивного выбора до ответа.</summary>
    public int? ElapsedMs { get; set; }

    /// <summary>Изменения Шкал: из контента и фактические после обрезки (jsonb).</summary>
    public string? ScaleChanges { get; set; }

    /// <summary>Флаги, поставленные решением (jsonb).</summary>
    public string? FlagsSet { get; set; }

    public bool CriticalError { get; set; }
    public string? ToStepId { get; set; }

    /// <summary>Поля голосовой попытки. Аудио никогда не сохраняется.</summary>
    public string? VoiceTranscript { get; set; }
    public string? VoiceChoice { get; set; }
    public double? VoiceConfidence { get; set; }
    public int? VoiceLatencyMs { get; set; }
    public bool VoiceApplied { get; set; }
    public string? VoiceError { get; set; }
    public string? VoiceRequestId { get; set; }
    public string? VoiceAttemptId { get; set; }
    public string? VoicePassengerReply { get; set; }
    public string? VoiceReplyError { get; set; }

    /// <summary>Итог События: success, failure или interrupted — Событие прервано Срывом рейса.</summary>
    public string? Result { get; set; }
    public string? OutcomeStepId { get; set; }
}

public static class TripJournalKinds
{
    public const string ProactiveChoice = "proactiveChoice";
    public const string Decision = "decision";
    public const string VoiceAttempt = "voiceAttempt";
    public const string EventFinished = "eventFinished";
}

internal static class TripModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TripRecord>(entity =>
        {
            entity.HasKey(trip => trip.Id);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(trip => trip.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(trip => trip.UserId);
            entity.Property(trip => trip.ServiceClass).HasMaxLength(50).IsRequired();
            entity.Property(trip => trip.Status).HasMaxLength(20).IsRequired();
            entity.Property(trip => trip.FailureCause).HasMaxLength(20);
            entity.Property(trip => trip.FailureScale).HasMaxLength(50);
            entity.Property(trip => trip.Directory).HasColumnType("jsonb").IsRequired();
            entity.Property(trip => trip.Settings).HasColumnType("jsonb").IsRequired();
            entity.Property(trip => trip.EventVersions).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<TripJournalRecord>(entity =>
        {
            entity.HasKey(row => new { row.TripId, row.Seq });
            entity.HasOne<TripRecord>().WithMany().HasForeignKey(row => row.TripId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(row => row.Kind).HasMaxLength(20).IsRequired();
            entity.Property(row => row.OptionId).HasMaxLength(100);
            entity.Property(row => row.EventId).HasMaxLength(100);
            entity.Property(row => row.StepId).HasMaxLength(100);
            entity.Property(row => row.VariantId).HasMaxLength(100);
            entity.Property(row => row.ToStepId).HasMaxLength(100);
            entity.Property(row => row.Result).HasMaxLength(20);
            entity.Property(row => row.OutcomeStepId).HasMaxLength(100);
            entity.Property(row => row.ScaleChanges).HasColumnType("jsonb");
            entity.Property(row => row.FlagsSet).HasColumnType("jsonb");
            entity.Property(row => row.VoiceTranscript).HasColumnType("text");
            entity.Property(row => row.VoiceChoice).HasMaxLength(100);
            entity.Property(row => row.VoiceError).HasMaxLength(80);
            entity.Property(row => row.VoiceRequestId).HasMaxLength(200);
            entity.Property(row => row.VoiceAttemptId).HasMaxLength(100);
            entity.Property(row => row.VoicePassengerReply).HasColumnType("text");
            entity.Property(row => row.VoiceReplyError).HasMaxLength(80);
            entity.HasIndex(row => new { row.TripId, row.VoiceAttemptId })
                .HasFilter("\"kind\" = 'voiceAttempt'")
                .IsUnique();
        });
    }
}
