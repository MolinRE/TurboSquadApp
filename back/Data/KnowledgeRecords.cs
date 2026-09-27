using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>Последняя попытка по Единице знания и Компетенции; цена освоенной Единицы сохраняется до потери Освоения.</summary>
public sealed class KnowledgeMasteryRecord
{
    public Guid UserId { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string Competence { get; set; } = string.Empty;
    public bool IsMastered { get; set; }
    public int AwardedCost { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
}

/// <summary>Наивысшее достигнутое Звание не снижается вместе с Очками.</summary>
public sealed class ConductorProfileRecord
{
    public Guid UserId { get; set; }
    public int RankIndex { get; set; }
}

internal static class KnowledgeModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgeMasteryRecord>(entity =>
        {
            entity.HasKey(item => new { item.UserId, item.UnitType, item.UnitId, item.Competence });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(item => item.UnitType).HasMaxLength(20).IsRequired();
            entity.Property(item => item.UnitId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Competence).HasMaxLength(30).IsRequired();
            entity.HasIndex(item => item.UserId);
        });

        modelBuilder.Entity<ConductorProfileRecord>(entity =>
        {
            entity.HasKey(profile => profile.UserId);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(profile => profile.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
