using EverythingLabeled.Service.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Data;

public sealed class ElDbContext(DbContextOptions<ElDbContext> options) : DbContext(options)
{
    public const string DataVersionKey = "data_version";

    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagAlias> TagAliases => Set<TagAlias>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceTag> ResourceTags => Set<ResourceTag>();
    public DbSet<TagRelation> TagRelations => Set<TagRelation>();
    public DbSet<Meta> Meta => Set<Meta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.NameInitials).IsRequired();
            entity.Property(e => e.NameNormalized).IsRequired();
            // Store NULL namespace as empty string so UNIQUE(Namespace, Name) treats global as one key.
            entity.Property(e => e.Namespace)
                .HasConversion(
                    v => v ?? string.Empty,
                    v => string.IsNullOrEmpty(v) ? null : v);
            entity.HasIndex(e => new { e.Namespace, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.Seq, e.CreatedAt });
            entity.HasIndex(e => e.NameInitials);
        });

        modelBuilder.Entity<TagAlias>(entity =>
        {
            entity.ToTable("TagAliases");
            entity.HasKey(e => new { e.TagId, e.Alias });
            entity.Property(e => e.Alias).IsRequired();
            entity.Property(e => e.AliasNormalized).IsRequired();
            entity.HasIndex(e => e.Alias).IsUnique();
            entity.HasIndex(e => e.AliasNormalized).IsUnique();
            entity.HasIndex(e => e.TagId);
            entity.HasOne(e => e.Tag)
                .WithMany(t => t.Aliases)
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Resource>(entity =>
        {
            entity.ToTable("Resources");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uri).IsRequired();
            entity.Property(e => e.Kind).IsRequired();
            entity.HasIndex(e => e.Uri).IsUnique();
            entity.HasIndex(e => e.Kind);
        });

        modelBuilder.Entity<ResourceTag>(entity =>
        {
            entity.ToTable("ResourceTags");
            entity.HasKey(e => new { e.ResourceId, e.TagId });
            entity.HasIndex(e => new { e.TagId, e.ResourceId });
            entity.HasOne(e => e.Resource)
                .WithMany(r => r.ResourceTags)
                .HasForeignKey(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Tag)
                .WithMany(t => t.ResourceTags)
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TagRelation>(entity =>
        {
            entity.ToTable("TagRelations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).IsRequired();
            entity.HasIndex(e => new { e.FromTagId, e.ToTagId, e.Kind }).IsUnique();
            entity.HasIndex(e => new { e.FromTagId, e.Kind, e.Seq });
            // SQLite partial unique (ToTagId WHERE Kind=child) not used; ordinary index + app rules in #9.
            entity.HasIndex(e => new { e.ToTagId, e.Kind });
            entity.HasOne(e => e.FromTag)
                .WithMany(t => t.RelationsFrom)
                .HasForeignKey(e => e.FromTagId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ToTag)
                .WithMany(t => t.RelationsTo)
                .HasForeignKey(e => e.ToTagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Meta>(entity =>
        {
            entity.ToTable("Meta");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.Value).IsRequired();
        });
    }
}
