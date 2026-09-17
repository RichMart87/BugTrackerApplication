using BugTracker.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BugTracker.Api.Data;

public class BugTrackerDbContext(DbContextOptions<BugTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Bug> Bugs => Set<Bug>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var bug = modelBuilder.Entity<Bug>();

        bug.ToTable("Bugs");
        bug.HasKey(b => b.Id);

        bug.Property(b => b.Title).IsRequired().HasMaxLength(200);
        bug.Property(b => b.Description).IsRequired().HasMaxLength(4000);
        bug.Property(b => b.Notes).HasMaxLength(4000);

        // Enums are persisted as text so the tables stay readable to anyone
        // running ad-hoc SQL or plugging a BI tool into the database.
        bug.Property(b => b.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
        bug.Property(b => b.Environment).HasConversion<string>().HasMaxLength(20).IsRequired();

        bug.Property(b => b.DateFound).IsRequired();
        bug.Property(b => b.CreatedUtc).IsRequired();
        bug.Property(b => b.UpdatedUtc).IsRequired();

        // Supports the month-over-month rollup and the default "newest first" grid sort.
        bug.HasIndex(b => b.DateFound);
        bug.HasIndex(b => b.Resolved);
        bug.HasIndex(b => new { b.Severity, b.Environment });
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Bug>())
        {
            if (entry.State == EntityState.Added)
            {
                // Only fill in blanks, so back-dated rows (seed data, imports) keep their stamps.
                if (entry.Entity.CreatedUtc == default)
                {
                    entry.Entity.CreatedUtc = now;
                }

                if (entry.Entity.UpdatedUtc == default)
                {
                    entry.Entity.UpdatedUtc = entry.Entity.CreatedUtc;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedUtc = now;
            }
        }
    }
}
