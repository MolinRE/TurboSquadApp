using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>Вставленный Методистом текст Источника для генерации Черновиков.</summary>
public sealed class SourceRecord
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal static class SourceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceRecord>(entity =>
        {
            entity.HasKey(source => source.Id);
            entity.Property(source => source.Title).HasMaxLength(200).IsRequired();
            entity.Property(source => source.Text).IsRequired();
            entity.Property(source => source.CreatedAt).IsRequired();
        });
    }
}
