using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nook.Models;

namespace Nook.Data;

/// <summary>
/// EF Core database context for Nook. Resolved through an
/// <see cref="IDbContextFactory{TContext}"/> so services can create short-lived
/// contexts — the recommended pattern for Blazor Server, where a single
/// circuit-scoped context is not safe across concurrent component renders.
/// </summary>
public class NookContext : IdentityDbContext<ApplicationUser>
{
    public NookContext(DbContextOptions<NookContext> options)
        : base(options)
    {
    }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<ItemLink> ItemLinks => Set<ItemLink>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Item>(entity =>
        {
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();

            // Enums stored as readable strings (matches the nvarchar(50) schema).
            entity.Property(e => e.ItemType).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(50);

            entity.Property(e => e.Url).HasMaxLength(1000);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("sysutcdatetime()");

            // Self-reference for parent/child items. Restrict to avoid cascade cycles.
            entity.HasOne(e => e.Parent)
                  .WithMany(e => e.Children)
                  .HasForeignKey(e => e.ParentItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Indexes for the most common filters.
            entity.HasIndex(e => e.ItemType);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DueDate);

            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.UserId).IsRequired();
            // Tag names are unique PER USER, not globally.
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<ItemTag>(entity =>
        {
            entity.HasKey(it => new { it.ItemId, it.TagId });

            entity.HasOne(it => it.Item)
                  .WithMany(i => i.ItemTags)
                  .HasForeignKey(it => it.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(it => it.Tag)
                  .WithMany(t => t.ItemTags)
                  .HasForeignKey(it => it.TagId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemLink>(entity =>
        {
            entity.Property(e => e.LinkType).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("sysutcdatetime()");

            // Two FKs to Items — both Restrict to avoid multiple-cascade-path errors.
            entity.HasOne(e => e.SourceItem)
                  .WithMany(i => i.OutgoingLinks)
                  .HasForeignKey(e => e.SourceItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TargetItem)
                  .WithMany(i => i.IncomingLinks)
                  .HasForeignKey(e => e.TargetItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            // A pair of items can only be linked once in a given direction.
            entity.HasIndex(e => new { e.SourceItemId, e.TargetItemId }).IsUnique();
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.ItemTitle).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Detail).HasMaxLength(500);
            entity.HasIndex(e => new { e.UserId, e.Timestamp });
        });
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

    /// <summary>Stamps CreatedAt/UpdatedAt on Item inserts and updates.</summary>
    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Item>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                // CreatedAt is immutable once the row exists.
                entry.Property(nameof(Item.CreatedAt)).IsModified = false;
            }
        }
    }
}
