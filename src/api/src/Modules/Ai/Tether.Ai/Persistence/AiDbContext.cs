using Tether.Ai.Domain;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Ai.Persistence;

internal sealed class AiDbContext(DbContextOptions<AiDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string Schema = "ai";
    public DbSet<SummaryRevisionRow> Summaries => Set<SummaryRevisionRow>();
    public DbSet<CatchUpRow> CatchUps => Set<CatchUpRow>();
    public DbSet<AiUsageRow> Usage => Set<AiUsageRow>();

    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        var outcomes = string.Join(",", Outcomes.All.Select(o => $"'{o}'"));

        modelBuilder.Entity<SummaryRevisionRow>(b =>
        {
            b.ToTable("summary_revisions", t =>
            {
                t.HasCheckConstraint("ck_summary_revisions_revision", "revision > 0");
                t.HasCheckConstraint("ck_summary_revisions_kind", "kind IN ('Delta','Compaction')");
                t.HasCheckConstraint("ck_summary_revisions_status", "status IN ('Draft','Final')");
                t.HasCheckConstraint("ck_summary_revisions_range", "source_from_seq <= source_to_seq AND covered_from_ms <= covered_to_ms");
            });
            b.HasKey(x => new { x.SessionId, x.Revision });
            b.Property(x => x.SessionId).HasColumnName("session_id");
            b.Property(x => x.Revision).HasColumnName("revision");
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(8).IsRequired();
            b.Property(x => x.BasedOnSeq).HasColumnName("based_on_seq");
            b.Property(x => x.SourceFromSeq).HasColumnName("source_from_seq");
            b.Property(x => x.SourceToSeq).HasColumnName("source_to_seq");
            b.Property(x => x.CoveredFromMs).HasColumnName("covered_from_ms");
            b.Property(x => x.CoveredToMs).HasColumnName("covered_to_ms");
            b.Property(x => x.Tldr).HasColumnName("tldr").HasMaxLength(2000).IsRequired();
            b.Property(x => x.TopicsJson).HasColumnName("topics").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.TermsJson).HasColumnName("terms").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.CitationsJson).HasColumnName("citations").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
            b.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<CatchUpRow>(b =>
        {
            b.ToTable("catch_ups", t =>
            {
                t.HasCheckConstraint("ck_catch_ups_window", "window_minutes IN (2,5,10)");
                t.HasCheckConstraint("ck_catch_ups_kind", "kind IN ('Extractive','Ai')");
                t.HasCheckConstraint("ck_catch_ups_ai_state", "ai_state IN ('Pending','Ready','Unavailable','BudgetLimited','NotNeeded')");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.SessionId).HasColumnName("session_id");
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.WindowMinutes).HasColumnName("window_minutes");
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
            b.Property(x => x.AiState).HasColumnName("ai_state").HasMaxLength(16).IsRequired();
            b.Property(x => x.FromMs).HasColumnName("from_ms");
            b.Property(x => x.ToMs).HasColumnName("to_ms");
            b.Property(x => x.ResultJson).HasColumnName("result").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64);
            b.Property(x => x.Model).HasColumnName("model").HasMaxLength(64);
            b.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => new { x.SessionId, x.CreatedAt }).HasDatabaseName("ix_catch_ups_session_created");
            b.HasIndex(x => new { x.UserId, x.SessionId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ux_catch_ups_idempotency");
            // One in-flight catch-up per session and window (B3-05a): parallel clicks without an Idempotency-Key cannot create two paid calls.
            b.HasIndex(x => new { x.SessionId, x.WindowMinutes }).IsUnique().HasFilter("ai_state = 'Pending'").HasDatabaseName("ux_catch_ups_pending");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<AiUsageRow>(b =>
        {
            b.ToTable("ai_usage", t =>
            {
                t.HasCheckConstraint("ck_ai_usage_tokens", "input_tokens >= 0 AND cached_tokens >= 0 AND output_tokens >= 0 AND est_cost_micros >= 0 AND latency_ms >= 0");
                t.HasCheckConstraint("ck_ai_usage_outcome", $"outcome IN ({outcomes})");
                t.HasCheckConstraint("ck_ai_usage_workflow", "workflow IN ('RollingSummary','Compaction','LostTrack')");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.SessionId).HasColumnName("session_id");
            b.Property(x => x.Workflow).HasColumnName("workflow").HasMaxLength(24).IsRequired();
            b.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64).IsRequired();
            b.Property(x => x.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
            b.Property(x => x.InputTokens).HasColumnName("input_tokens");
            b.Property(x => x.CachedTokens).HasColumnName("cached_tokens");
            b.Property(x => x.OutputTokens).HasColumnName("output_tokens");
            b.Property(x => x.EstCostMicros).HasColumnName("est_cost_micros");
            b.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(16).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_ai_usage_user_created");
            b.HasIndex(x => new { x.SessionId, x.CreatedAt }).HasDatabaseName("ix_ai_usage_session_created");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });
    }
}

/// <summary>Design-time only (dotnet ef).</summary>
internal sealed class AiDbContextFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    private sealed class NoUser : ICurrentUser { public Guid? UserId => null; }

    public AiDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", AiDbContext.Schema)).Options;
        return new AiDbContext(o, new NoUser());
    }
}
