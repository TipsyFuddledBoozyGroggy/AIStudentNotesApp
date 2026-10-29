using System.Text;
using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Syllabus.Persistence;

/// <summary>
/// One DbContext, schema "syllabus", history table syllabus.__ef_migrations_history, migrations prefixed S.
/// The EF model maps tables and columns and carries the ownership query filters. The authoritative DDL (every CHECK, trigger,
/// composite FK, partial and NULLS NOT DISTINCT index) is the raw SQL in S001/S002, copied from docs/data/schema-syllabus-p2.md;
/// EF does not model it, so the snapshot is generated from this model and the integration tests exercise the real constraints.
/// </summary>
internal sealed class SyllabusDbContext(DbContextOptions<SyllabusDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    public const string Schema = "syllabus";

    public DbSet<SyllabusDoc> Syllabi => Set<SyllabusDoc>();
    public DbSet<CanonicalText> CanonicalTexts => Set<CanonicalText>();
    public DbSet<ExtractionJob> Jobs => Set<ExtractionJob>();
    public DbSet<SyllabusItem> Items => Set<SyllabusItem>();
    public DbSet<ItemProposal> Proposals => Set<ItemProposal>();
    public DbSet<ItemAlternative> Alternatives => Set<ItemAlternative>();
    public DbSet<UnclaimedDate> Unclaimed => Set<UnclaimedDate>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncRunItem> SyncRunItems => Set<SyncRunItem>();
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<DecisionAudit> Audit => Set<DecisionAudit>();
    public DbSet<CreationCounter> Counters => Set<CreationCounter>();

    // Evaluated per context instance by EF; anonymous callers (and the system maintenance context) match nothing.
    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.HasDefaultSchema(Schema);

        mb.Entity<SyllabusDoc>(b =>
        {
            b.ToTable("syllabi"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.InputKind).HasMaxLength(8); b.Property(x => x.FileType).HasMaxLength(4);
            b.Property(x => x.Status).HasMaxLength(12); b.Property(x => x.StatusReason).HasMaxLength(24);
            b.Property(x => x.AiSource).HasMaxLength(5);
            b.Property(x => x.ContentSha256).HasColumnType("character(64)");
            b.Property(x => x.NoticeVersion).HasMaxLength(64);
            b.Property(x => x.UnprocessedRanges).HasColumnType("jsonb");
            b.Property(x => x.DefaultTimezone).HasMaxLength(64);
            b.Property(x => x.ExtractorVersion).HasMaxLength(32); b.Property(x => x.PromptVersion).HasMaxLength(64);
            b.Property(x => x.SchemaVersion).HasMaxLength(32); b.Property(x => x.Model).HasMaxLength(64);
            b.Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_syllabi_id_user_id");
            b.HasIndex(x => new { x.Id, x.UserId, x.CourseId }).IsUnique().HasDatabaseName("ux_syllabi_id_user_course");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<CanonicalText>(b =>
        {
            b.ToTable("canonical_texts"); b.HasKey(x => x.SyllabusId); b.Property(x => x.SyllabusId).ValueGeneratedNever();
            b.Property(x => x.Lines).HasColumnType("jsonb"); b.Property(x => x.Pages).HasColumnType("jsonb");
            b.Property(x => x.ExtractorVersion).HasMaxLength(32);
            b.Property(x => x.TextSha256).HasColumnType("character(64)");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<ExtractionJob>(b =>
        {
            b.ToTable("extraction_jobs"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.State).HasMaxLength(10); b.Property(x => x.LeaseOwner).HasMaxLength(64);
            b.Property(x => x.Stage).HasMaxLength(12); b.Property(x => x.LastErrorCode).HasMaxLength(40);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<SyllabusItem>(b =>
        {
            b.ToTable("items"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ItemKey).HasColumnType("character(35)");
            b.Property(x => x.Origin).HasMaxLength(7); b.Property(x => x.Kind).HasMaxLength(12);
            b.Property(x => x.Title).HasMaxLength(200); b.Property(x => x.Notes).HasMaxLength(500);
            b.Property(x => x.WrittenDateText).HasMaxLength(120); b.Property(x => x.WrittenTimeText).HasMaxLength(60);
            b.Property(x => x.WrittenWeekday).HasColumnType("character(2)");
            b.Property(x => x.WhenStatus).HasMaxLength(14);
            b.Property(x => x.TimeLocal).HasColumnType("time(0)"); b.Property(x => x.EndTimeLocal).HasColumnType("time(0)");
            b.Property(x => x.RecStartTime).HasColumnType("time(0)"); b.Property(x => x.RecEndTime).HasColumnType("time(0)");
            b.Property(x => x.Timezone).HasMaxLength(64); b.Property(x => x.TimezoneSource).HasMaxLength(14);
            b.Property(x => x.Derivation).HasMaxLength(20); b.Property(x => x.UnknownReason).HasMaxLength(24);
            b.Property(x => x.RecUntilSource).HasMaxLength(8);
            b.Property(x => x.Confidence).HasMaxLength(14); b.Property(x => x.ConfidenceReason).HasMaxLength(200);
            b.Property(x => x.SnippetPrecision).HasMaxLength(5);
            b.Property(x => x.SnippetText).HasMaxLength(600); b.Property(x => x.ContextText).HasMaxLength(1500);
            b.Property(x => x.Decision).HasMaxLength(9); b.Property(x => x.SyncState).HasMaxLength(18);
            b.Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
            b.Property(x => x.SortDate).HasComputedColumnSql("COALESCE(date_local, DATE '9999-12-31')", stored: true);
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_items_id_user_id");
            b.HasIndex(x => x.ItemKey).IsUnique().HasDatabaseName("ux_items_item_key");
            b.HasIndex(x => new { x.UserId, x.SyllabusId, x.SortDate, x.Id }).HasDatabaseName("ix_items_syllabus_sort");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<ItemProposal>(b =>
        {
            b.ToTable("item_proposals"); b.HasKey(x => x.ItemId); b.Property(x => x.ItemId).ValueGeneratedNever();
            b.Property(x => x.Proposal).HasColumnType("jsonb");
            b.Property(x => x.ValidatorVersion).HasMaxLength(32); b.Property(x => x.PromptVersion).HasMaxLength(64);
            b.Property(x => x.SchemaVersion).HasMaxLength(32); b.Property(x => x.Model).HasMaxLength(64);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<ItemAlternative>(b =>
        {
            b.ToTable("item_alternatives"); b.HasKey(x => new { x.ItemId, x.Ordinal });
            b.Property(x => x.TimeLocal).HasColumnType("time(0)");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<UnclaimedDate>(b =>
        {
            b.ToTable("unclaimed_dates"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.SnippetPrecision).HasMaxLength(5);
            b.Property(x => x.SnippetText).HasMaxLength(600); b.Property(x => x.ContextText).HasMaxLength(1500);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<SyncRun>(b =>
        {
            b.ToTable("sync_runs"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Provider).HasMaxLength(4); b.Property(x => x.Kind).HasMaxLength(6); b.Property(x => x.Status).HasMaxLength(15);
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_sync_runs_id_user");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<SyncRunItem>(b =>
        {
            b.ToTable("sync_run_items"); b.HasKey(x => new { x.RunId, x.ItemId });
            b.Property(x => x.Outcome).HasMaxLength(14); b.Property(x => x.ErrorCode).HasMaxLength(32);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_keys"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Method).HasMaxLength(6); b.Property(x => x.RouteTemplate).HasMaxLength(64);
            b.Property(x => x.ResultKind).HasMaxLength(12);
            b.HasIndex(x => new { x.UserId, x.Method, x.RouteTemplate, x.KeyHash }).IsUnique().HasDatabaseName("ux_idempotency_keys_scope");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<CalendarEvent>(b =>
        {
            b.ToTable("calendar_events"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Provider).HasMaxLength(4); b.Property(x => x.ItemKey).HasColumnType("character(35)");
            b.Property(x => x.Title).HasMaxLength(200); b.Property(x => x.Kind).HasMaxLength(12);
            b.Property(x => x.StartTime).HasColumnType("time(0)"); b.Property(x => x.EndTime).HasColumnType("time(0)");
            b.Property(x => x.StartTimezone).HasMaxLength(64);
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_calendar_events_id_user");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<DecisionAudit>(b =>
        {
            b.ToTable("decision_audit"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.NoticeVersion).HasMaxLength(64); b.Property(x => x.Action).HasMaxLength(32);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        mb.Entity<CreationCounter>(b =>
        {
            b.ToTable("creation_counters"); b.HasKey(x => new { x.UserId, x.Day });
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        // snake_case columns for every property
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
internal sealed class SyllabusDbContextFactory : IDesignTimeDbContextFactory<SyllabusDbContext>
{
    private sealed class NoUser : ICurrentUser { public Guid? UserId => null; }

    public SyllabusDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<SyllabusDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", SyllabusDbContext.Schema)).Options;
        return new SyllabusDbContext(o, new NoUser());
    }
}
