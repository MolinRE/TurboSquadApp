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
            entity.HasKey(record => new { record.EventId, record.Version });
            entity.Property(record => record.EventId).HasMaxLength(100);
            entity.Property(record => record.Document).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<ScaleRecord>(entity =>
        {
            entity.HasKey(scale => scale.Code);
            entity.Property(scale => scale.Code).HasMaxLength(50);
            entity.Property(scale => scale.Name).HasMaxLength(200).IsRequired();
            entity.Property(scale => scale.FailureReason).HasMaxLength(200);
        });

        modelBuilder.Entity<ServiceClassRecord>(entity =>
        {
            entity.HasKey(serviceClass => serviceClass.Code);
            entity.Property(serviceClass => serviceClass.Code).HasMaxLength(50);
            entity.Property(serviceClass => serviceClass.Name).HasMaxLength(200).IsRequired();
            entity.Property(serviceClass => serviceClass.Description).IsRequired();
        });

        modelBuilder.Entity<TripSettingsRecord>(entity =>
        {
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).HasMaxLength(50);
            entity.Property(settings => settings.Document).HasColumnType("jsonb").IsRequired();
        });
    }
}
