using System.Text;
using Tether.Docs.Domain;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Docs.Persistence;

/// <summary>
/// One DbContext, schema "docs", history table docs.__ef_migrations_history, migrations prefixed D. The EF model maps tables and columns and
/// carries the ownership query filters. The authoritative DDL (CHECKs, composite cascade FKs, unique indexes) is the raw SQL in D001, because EF
/// does not model the cross-schema composite FKs; the integration tests exercise the real constraints. No RESTRICT between docs tables (an
/// immediate check inside a cascade could fail a lecture delete, DXD-09).
/// </summary>
internal sealed class DocsDbContext(DbContextOptions<DocsDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string Schema = "docs";

    public DbSet<DemoDoc> Docs => Set<DemoDoc>();
    public DbSet<DocExport> Exports => Set<DocExport>();
    public DbSet<DocTarget> Targets => Set<DocTarget>();
    public DbSet<DocIdempotencyKey> IdempotencyKeys => Set<DocIdempotencyKey>();
    public DbSet<DocAuditEvent> Audit => Set<DocAuditEvent>();

    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.HasDefaultSchema(Schema);

        mb.Entity<DemoDoc>(b =>
        {
            b.ToTable("demo_docs"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Blocks).HasColumnType("jsonb");
            b.HasIndex(x => x.OriginExportId).IsUnique().HasDatabaseName("ux_demo_docs_origin_export_id");
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_demo_docs_id_user_id");
            b.HasIndex(x => new { x.UserId, x.SessionId }).HasDatabaseName("ix_demo_docs_user_session");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<DocExport>(b =>
        {
            b.ToTable("doc_exports"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Provider).HasMaxLength(16); b.Property(x => x.Mode).HasMaxLength(16);
            b.Property(x => x.Status).HasMaxLength(12); b.Property(x => x.Outcome).HasMaxLength(12);
            b.Property(x => x.ContentDigest).HasMaxLength(64); b.Property(x => x.ErrorCode).HasMaxLength(32);
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_doc_exports_id_user_id");
            b.HasIndex(x => new { x.UserId, x.SessionId, x.CreatedAt }).HasDatabaseName("ix_doc_exports_user_session_created");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<DocTarget>(b =>
        {
            b.ToTable("doc_targets"); b.HasKey(x => new { x.UserId, x.SessionId, x.Provider });
            b.Property(x => x.Provider).HasMaxLength(16); b.Property(x => x.LastDigest).HasMaxLength(64);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<DocIdempotencyKey>(b =>
        {
            b.ToTable("idempotency_keys"); b.HasKey(x => new { x.UserId, x.Method, x.RouteTemplate, x.KeyHash });
            b.Property(x => x.Method).HasMaxLength(6); b.Property(x => x.RouteTemplate).HasMaxLength(64);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<DocAuditEvent>(b =>
        {
            b.ToTable("audit_events"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Action).HasMaxLength(16); b.Property(x => x.Provider).HasMaxLength(16); b.Property(x => x.ErrorCode).HasMaxLength(32);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        foreach (var et in mb.Model.GetEntityTypes())
            foreach (var p in et.GetProperties())
                p.SetColumnName(Snake(p.Name));
    }

    internal static string Snake(string name)
    {
        var sb = new StringBuilder(name.Length + 6);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]))) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Design-time only (dotnet ef).</summary>
internal sealed class DocsDbContextFactory : IDesignTimeDbContextFactory<DocsDbContext>
{
    private sealed class NoUser : ICurrentUser { public Guid? UserId => null; }

    public DocsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<DocsDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", DocsDbContext.Schema)).Options;
        return new DocsDbContext(o, new NoUser());
    }
}
