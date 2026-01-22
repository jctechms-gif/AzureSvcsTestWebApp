using Microsoft.EntityFrameworkCore;

namespace SvcTestApp.Data;

/// <summary>
/// Application database context for Entity Framework Core operations
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// DbSet for connectivity probe operations
    /// </summary>
    public DbSet<ConnectivityProbe> ConnectivityProbes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the ConnectivityProbe entity
        modelBuilder.Entity<ConnectivityProbe>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Message).HasMaxLength(500);
        });
    }
}

/// <summary>
/// Simple entity for database connectivity testing
/// </summary>
public class ConnectivityProbe
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}