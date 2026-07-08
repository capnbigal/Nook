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

    // ---- Legacy (retained as an immutable rollback snapshot after cutover) ----
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<ItemLink> ItemLinks => Set<ItemLink>();

    // ---- Shared ----
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    // ---- Graph model ----
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<NodeTag> NodeTags => Set<NodeTag>();
    public DbSet<RelationType> RelationTypes => Set<RelationType>();
    public DbSet<NodeRelation> NodeRelations => Set<NodeRelation>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionMembership> CollectionMemberships => Set<CollectionMembership>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<ActionContext> ActionContexts => Set<ActionContext>();
    public DbSet<Verb> Verbs => Set<Verb>();
    public DbSet<EventDetails> EventDetails => Set<EventDetails>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<MigrationAudit> MigrationAudits => Set<MigrationAudit>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

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

            // Restrict user-delete cascade to avoid multiple-cascade-path through ItemTag.
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.UserId).IsRequired();
            // Tag names are unique PER USER, not globally.
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

            // Restrict user-delete cascade to avoid multiple-cascade-path through ItemTag.
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
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
            entity.HasIndex(e => new { e.UserId, e.NodeId });

            // Restrict user-delete cascade to avoid multiple-cascade-path errors.
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            // ItemId and NodeId are denormalised references with no FK (rows must
            // survive deletion of their item/node), mirroring the legacy ItemId.
        });

        ConfigureGraph(modelBuilder);
    }

    /// <summary>Configuration for the knowledge-graph model (Nodes and everything hung off them).</summary>
    private static void ConfigureGraph(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Node>(entity =>
        {
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("sysutcdatetime()");

            entity.Property(e => e.UserId).IsRequired();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.UserId, e.State });
            entity.HasIndex(e => new { e.UserId, e.Kind });
            entity.HasIndex(e => new { e.UserId, e.UpdatedAt });
            entity.HasIndex(e => new { e.UserId, e.IsPinned }).HasFilter("[IsPinned] = 1");
        });

        // Optional lazy "self" Person on the user. Restrict both ways (no cascade cycle).
        modelBuilder.Entity<ApplicationUser>()
            .HasOne(u => u.SelfNode).WithMany()
            .HasForeignKey(u => u.SelfNodeId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RelationType>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(60).IsRequired();
            entity.Property(e => e.InverseName).HasMaxLength(60);
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);

            // SQL-Server-safe uniqueness for nullable UserId: one system type per
            // name, and one user-owned type per (user, name).
            entity.HasIndex(e => e.Name).IsUnique().HasFilter("[UserId] IS NULL")
                  .HasDatabaseName("UX_RelationType_System");
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique().HasFilter("[UserId] IS NOT NULL")
                  .HasDatabaseName("UX_RelationType_User");
        });

        modelBuilder.Entity<NodeRelation>(entity =>
        {
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceNode).WithMany(n => n.OutgoingRelations)
                  .HasForeignKey(e => e.SourceNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TargetNode).WithMany(n => n.IncomingRelations)
                  .HasForeignKey(e => e.TargetNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RelationType).WithMany(rt => rt.Relations)
                  .HasForeignKey(e => e.RelationTypeId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.UserId, e.SourceNodeId, e.TargetNodeId, e.RelationTypeId }).IsUnique();
            entity.HasIndex(e => e.SourceNodeId);
            entity.HasIndex(e => new { e.TargetNodeId, e.RelationTypeId });
        });

        modelBuilder.Entity<NodeTag>(entity =>
        {
            entity.HasKey(nt => new { nt.NodeId, nt.TagId });
            entity.HasOne(nt => nt.Node).WithMany(n => n.NodeTags)
                  .HasForeignKey(nt => nt.NodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(nt => nt.Tag).WithMany(t => t.NodeTags)
                  .HasForeignKey(nt => nt.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.NodeId);
            entity.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(c => c.Color).HasMaxLength(50);
            // 1:1 with its backing node; deleting the node removes the profile.
            entity.HasOne(c => c.Node).WithOne(n => n.Collection)
                  .HasForeignKey<Collection>(c => c.NodeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionMembership>(entity =>
        {
            entity.HasKey(m => new { m.CollectionNodeId, m.MemberNodeId });
            entity.Property(m => m.AddedAt).HasDefaultValueSql("sysutcdatetime()");
            entity.HasOne(m => m.Collection).WithMany(c => c.Memberships)
                  .HasForeignKey(m => m.CollectionNodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.MemberNode).WithMany()
                  .HasForeignKey(m => m.MemberNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(m => new { m.CollectionNodeId, m.SortOrder });
            entity.HasIndex(m => m.MemberNodeId);
        });

        modelBuilder.Entity<ActionItem>(entity =>
        {
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Verb).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TargetNode).WithMany()
                  .HasForeignKey(e => e.TargetNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ParentAction).WithMany(a => a.Children)
                  .HasForeignKey(e => e.ParentActionId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.UserId, e.Status, e.DueDate });
            entity.HasIndex(e => new { e.UserId, e.RemindAt });
            entity.HasIndex(e => e.TargetNodeId);
            entity.HasIndex(e => e.ParentActionId);
        });

        modelBuilder.Entity<ActionContext>(entity =>
        {
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasKey(e => new { e.ActionItemId, e.NodeId, e.Role });
            entity.HasOne(e => e.ActionItem).WithMany(a => a.Contexts)
                  .HasForeignKey(e => e.ActionItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Node).WithMany()
                  .HasForeignKey(e => e.NodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.UserId, e.NodeId, e.Role });
        });

        modelBuilder.Entity<Verb>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(60).IsRequired();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.Name).IsUnique().HasFilter("[UserId] IS NULL")
                  .HasDatabaseName("UX_Verb_System");
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique().HasFilter("[UserId] IS NOT NULL")
                  .HasDatabaseName("UX_Verb_User");
        });

        modelBuilder.Entity<EventDetails>(entity =>
        {
            entity.HasKey(e => e.NodeId);
            entity.HasOne(e => e.Node).WithOne(n => n.EventDetails)
                  .HasForeignKey<EventDetails>(e => e.NodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Verb).WithMany(v => v.Events)
                  .HasForeignKey(e => e.VerbId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SubjectNode).WithMany()
                  .HasForeignKey(e => e.SubjectNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ObjectNode).WithMany()
                  .HasForeignKey(e => e.ObjectNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PlaceNode).WithMany()
                  .HasForeignKey(e => e.PlaceNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<EventParticipant>(entity =>
        {
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasKey(e => new { e.EventNodeId, e.ParticipantNodeId, e.Role });
            entity.HasOne(e => e.Event).WithMany(ev => ev.Participants)
                  .HasForeignKey(e => e.EventNodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ParticipantNode).WithMany()
                  .HasForeignKey(e => e.ParticipantNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.ParticipantNodeId);
        });

        modelBuilder.Entity<MigrationAudit>(entity =>
        {
            entity.Property(e => e.Category).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Detail).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            entity.HasIndex(e => e.Category);
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.RecentNodeIdsCsv).HasMaxLength(200);
            entity.HasIndex(e => e.UserId).IsUnique();
            // FK to the identity user, Restrict per house style.
            entity.HasOne<ApplicationUser>().WithMany()
                  .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            // LastOpenedNodeId is denormalised — deliberately NO FK.
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

    /// <summary>Stamps CreatedAt/UpdatedAt on Item, Node and ActionItem inserts and updates.</summary>
    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Item>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(nameof(Item.CreatedAt)).IsModified = false;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Node>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(nameof(Node.CreatedAt)).IsModified = false;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ActionItem>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(nameof(ActionItem.CreatedAt)).IsModified = false;
            }
        }
    }
}
