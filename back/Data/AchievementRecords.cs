using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

/// <summary>Бессрочная выдача Ачивки Проводнику; повторная выдача запрещена составным ключом.</summary>
public sealed class AchievementAwardRecord
{
    public Guid UserId { get; set; }
    public string AchievementId { get; set; } = string.Empty;
    public DateTimeOffset EarnedAt { get; set; }
}

internal static class AchievementModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AchievementAwardRecord>(entity =>
        {
            entity.HasKey(award => new { award.UserId, award.AchievementId });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(award => award.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(award => award.AchievementId).HasMaxLength(50).IsRequired();
        });
    }
}
