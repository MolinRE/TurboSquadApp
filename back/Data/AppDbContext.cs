using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<Brigade> Brigades => Set<Brigade>();
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).HasMaxLength(100).IsRequired();
            entity.Property(user => user.NormalizedUsername).HasMaxLength(100).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.HasIndex(user => user.NormalizedUsername).IsUnique();
            entity.HasOne(user => user.Depot)
                .WithMany()
                .HasForeignKey(user => user.DepotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(user => user.Brigade)
                .WithMany()
                .HasForeignKey(user => user.BrigadeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Depot>(entity =>
        {
            entity.HasKey(depot => depot.Id);
            entity.Property(depot => depot.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Brigade>(entity =>
        {
            entity.HasKey(brigade => brigade.Id);
            entity.Property(brigade => brigade.Name).HasMaxLength(200).IsRequired();
            entity.HasOne(brigade => brigade.Depot)
                .WithMany(depot => depot.Brigades)
                .HasForeignKey(brigade => brigade.DepotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppUserRole>(entity =>
        {
            entity.HasKey(role => new { role.UserId, role.Role });
            entity.Property(role => role.Role).HasMaxLength(50).IsRequired();
            entity.HasOne(role => role.User)
                .WithMany(user => user.Roles)
                .HasForeignKey(role => role.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        SeedOrganization(modelBuilder);
    }

    private static void SeedOrganization(ModelBuilder modelBuilder)
    {
        var depotNorthId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var depotSouthId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        modelBuilder.Entity<Depot>().HasData(
            new Depot { Id = depotNorthId, Name = "Северное депо" },
            new Depot { Id = depotSouthId, Name = "Южное депо" });

        modelBuilder.Entity<Brigade>().HasData(
            new Brigade { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), DepotId = depotNorthId, Name = "Бригада 1" },
            new Brigade { Id = Guid.Parse("20000000-0000-0000-0000-000000000002"), DepotId = depotNorthId, Name = "Бригада 2" },
            new Brigade { Id = Guid.Parse("20000000-0000-0000-0000-000000000003"), DepotId = depotNorthId, Name = "Бригада 3" },
            new Brigade { Id = Guid.Parse("20000000-0000-0000-0000-000000000004"), DepotId = depotSouthId, Name = "Бригада 1" },
            new Brigade { Id = Guid.Parse("20000000-0000-0000-0000-000000000005"), DepotId = depotSouthId, Name = "Бригада 2" },
            new Brigade { Id = Guid.Parse("20000000-0000-0000-0000-000000000006"), DepotId = depotSouthId, Name = "Бригада 3" });
    }
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Guid DepotId { get; set; }
    public Depot Depot { get; set; } = null!;
    public Guid BrigadeId { get; set; }
    public Brigade Brigade { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<AppUserRole> Roles { get; set; } = new List<AppUserRole>();
}

public sealed class AppUserRole
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
}

public static class UserRoles
{
    public const string Conductor = "conductor";
    public const string Manager = "manager";
    public const string Methodologist = "methodologist";
}

public sealed class Depot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Brigade> Brigades { get; set; } = new List<Brigade>();
}

public sealed class Brigade
{
    public Guid Id { get; set; }
    public Guid DepotId { get; set; }
    public Depot Depot { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}
