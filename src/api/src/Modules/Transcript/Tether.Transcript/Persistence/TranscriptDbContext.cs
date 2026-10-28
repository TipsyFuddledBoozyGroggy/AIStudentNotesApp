using Tether.SharedKernel;
using Tether.Transcript.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Transcript.Persistence;

internal sealed class TranscriptDbContext(DbContextOptions<TranscriptDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string Schema = "transcript";
    public DbSet<TranscriptEvent> Events => Set<TranscriptEvent>();
    public DbSet<AudioProgress> Progress => Set<AudioProgress>();

    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TranscriptEvent>(b =>
        {
            b.ToTable("transcript_events", t =>
            {
                t.HasCheckConstraint("ck_transcript_events_seq", "seq > 0");
                t.HasCheckConstraint("ck_transcript_events_kind", "kind IN ('Final','Correction','Gap')");
                t.HasCheckConstraint("ck_transcript_events_shape", """
                    (kind = 'Final' AND segment_no IS NOT NULL AND start_ms IS NOT NULL AND end_ms >= start_ms AND text IS NOT NULL AND provider IS NOT NULL)
                    OR (kind = 'Correction' AND segment_no IS NOT NULL AND revision IS NOT NULL AND supersedes IS NOT NULL AND supersedes < revision AND text IS NOT NULL)
                    OR (kind = 'Gap' AND from_ms IS NOT NULL AND to_ms >= from_ms AND gap_reason IN ('paused','network','device','provider'))
                    """);
            });
            b.HasKey(x => new { x.SessionId, x.Seq });
            b.Property(x => x.SessionId).HasColumnName("session_id");
            b.Property(x => x.Seq).HasColumnName("seq");
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(x => x.SegmentNo).HasColumnName("segment_no");
            b.Property(x => x.Revision).HasColumnName("revision");
            b.Property(x => x.Supersedes).HasColumnName("supersedes");
            b.Property(x => x.StartMs).HasColumnName("start_ms");
            b.Property(x => x.EndMs).HasColumnName("end_ms");
            b.Property(x => x.Text).HasColumnName("text").HasMaxLength(4000);
            b.Property(x => x.Confidence).HasColumnName("confidence");
            b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
            b.Property(x => x.FromMs).HasColumnName("from_ms");
            b.Property(x => x.ToMs).HasColumnName("to_ms");
            b.Property(x => x.GapReason).HasColumnName("gap_reason").HasMaxLength(16);
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(x => new { x.SessionId, x.SegmentNo }).HasDatabaseName("ix_transcript_events_session_segment");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<AudioProgress>(b =>
        {
            b.ToTable("audio_progress", t => t.HasCheckConstraint("ck_audio_progress_ms", "accepted_ms >= 0"));
            b.HasKey(x => x.SessionId);
            b.Property(x => x.SessionId).HasColumnName("session_id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.AcceptedMs).HasColumnName("accepted_ms");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });
    }
}

/// <summary>Design-time only (dotnet ef).</summary>
internal sealed class TranscriptDbContextFactory : IDesignTimeDbContextFactory<TranscriptDbContext>
{
    private sealed class NoUser : ICurrentUser { public Guid? UserId => null; }

    public TranscriptDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<TranscriptDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", TranscriptDbContext.Schema)).Options;
        return new TranscriptDbContext(o, new NoUser());
    }
}
