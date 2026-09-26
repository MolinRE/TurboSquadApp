using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>Опубликованная версия События: JSON-документ целиком в jsonb (ADR-0001). Ключ — id События и версия.</summary>
public sealed class EventDocumentRecord
{
    public string EventId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Document { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
}

/// <summary>Шкала из справочника (PRD §5.3).</summary>
public sealed class ScaleRecord
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Min { get; set; }
    public int Max { get; set; }
    public int Start { get; set; }
    public int FailureThreshold { get; set; }
    public bool Mandatory { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>Класс обслуживания из справочника (PRD §5.5): неизменный код, название, текст для проводника.</summary>
public sealed class ServiceClassRecord
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>Настройка Рейса: JSON-документ в jsonb, пока одна запись "default".</summary>
public sealed class TripSettingsRecord
{
    public string Id { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
}

internal static class ContentModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventDocumentRecord>(entity =>
        {
            entity.HasKey(e => new { e.EventId, e.Version });
            entity.Property(e => e.EventId).HasMaxLength(100);
            entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<ScaleRecord>(entity =>
        {
            entity.HasKey(s => s.Code);
            entity.Property(s => s.Code).HasMaxLength(50);
            entity.Property(s => s.Name).HasMaxLength(200).IsRequired();
            entity.Property(s => s.FailureReason).HasMaxLength(200);
        });

        modelBuilder.Entity<ServiceClassRecord>(entity =>
        {
            entity.HasKey(c => c.Code);
            entity.Property(c => c.Code).HasMaxLength(50);
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Description).IsRequired();
        });

        modelBuilder.Entity<TripSettingsRecord>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasMaxLength(50);
            entity.Property(t => t.Document).HasColumnType("jsonb").IsRequired();
        });
    }
}
