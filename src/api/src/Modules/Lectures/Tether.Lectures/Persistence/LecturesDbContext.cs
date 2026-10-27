using Tether.Lectures.Domain;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Lectures.Persistence;

internal sealed class LecturesDbContext(DbContextOptions<LecturesDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    public const string Schema = "lectures";
    public DbSet<LectureSession> Sessions => Set<LectureSession>();
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<ConsentEvent> ConsentEvents => Set<ConsentEvent>();
    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();
    public DbSet<SessionCounter> Counters => Set<SessionCounter>();
    public DbSet<AudioTicket> AudioTickets => Set<AudioTicket>();
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // Evaluated per context instance by EF; anonymous callers match nothing (Guid.Empty is never stored).
    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<LectureSession>(b =>
        {
            b.ToTable("lecture_sessions", t =>
            {
                t.HasCheckConstraint("ck_lecture_sessions_state", "state IN ('Active','Paused','Stopped','Failed')");
                t.HasCheckConstraint("ck_lecture_sessions_stopped_at", "(state NOT IN ('Active','Paused') OR stopped_at IS NULL) AND (state <> 'Stopped' OR stopped_at IS NOT NULL)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.CourseId).HasColumnName("course_id").IsRequired();
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(x => x.StartedAt).HasColumnName("started_at");
            b.Property(x => x.StoppedAt).HasColumnName("stopped_at");
            b.Property(x => x.ConsentRecordId).HasColumnName("consent_record_id").IsRequired();
            b.Property(x => x.Version).IsRowVersion(); // xmin
            b.HasOne<ConsentRecord>().WithMany().HasForeignKey(x => x.ConsentRecordId)
                .HasConstraintName("fk_lecture_sessions_consent_record_id").OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_lecture_sessions_id_user_id");
            b.HasIndex(x => new { x.UserId, x.CourseId, x.Id }).HasDatabaseName("ix_lecture_sessions_user_course_id");
            // C-18: one recording per user.
            b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("ux_lecture_sessions_one_live_per_user")
                .HasFilter("state IN ('Active','Paused')");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<ConsentRecord>(b =>
        {
            b.ToTable("consent_records", t =>
            {
                t.HasCheckConstraint("ck_consent_records_kind", "kind IN ('mic_recording')");
                t.HasCheckConstraint("ck_consent_records_attested", "course_attested");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.CourseId).HasColumnName("course_id").IsRequired();
            b.Property(x => x.SessionId).HasColumnName("session_id").IsRequired(); // not a FK: survives session deletion
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
            b.Property(x => x.TextVersion).HasColumnName("text_version").HasMaxLength(64).IsRequired();
            b.Property(x => x.CourseAttested).HasColumnName("course_attested");
            b.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
            b.HasIndex(x => new { x.UserId, x.CourseId }).HasDatabaseName("ix_consent_records_user_course");
            b.HasIndex(x => x.SessionId).HasDatabaseName("ix_consent_records_session_id");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<ConsentEvent>(b =>
        {
            b.ToTable("consent_events", t => t.HasCheckConstraint("ck_consent_events_kind", "kind IN ('Start','Pause','Resume','Stop')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.ConsentRecordId).HasColumnName("consent_record_id").IsRequired();
            b.Property(x => x.SessionId).HasColumnName("session_id").IsRequired(); // not a FK: survives session deletion
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(x => x.TextVersion).HasColumnName("text_version").HasMaxLength(64).IsRequired();
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.HasOne<ConsentRecord>().WithMany().HasForeignKey(x => x.ConsentRecordId)
                .HasConstraintName("fk_consent_events_consent_record_id").OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.SessionId, x.OccurredAt }).HasDatabaseName("ix_consent_events_user_session");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<SessionEvent>(b =>
        {
            b.ToTable("session_events", t =>
            {
                t.HasCheckConstraint("ck_session_events_seq", "seq > 0");
                t.HasCheckConstraint("ck_session_events_state", "state IN ('Active','Paused','Stopped','Failed')");
            });
            b.HasKey(x => new { x.SessionId, x.Seq });
            b.Property(x => x.SessionId).HasColumnName("session_id");
            b.Property(x => x.Seq).HasColumnName("seq");
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(100);
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.HasOne<LectureSession>().WithMany().HasForeignKey(x => x.SessionId)
                .HasConstraintName("fk_session_events_session_id").OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<SessionCounter>(b =>
        {
            b.ToTable("session_counters", t => t.HasCheckConstraint("ck_session_counters_last_seq", "last_seq >= 0"));
            b.HasKey(x => x.SessionId);
            b.Property(x => x.SessionId).HasColumnName("session_id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.LastSeq).HasColumnName("last_seq").IsConcurrencyToken();
            b.HasOne<LectureSession>().WithOne().HasForeignKey<SessionCounter>(x => x.SessionId)
                .HasConstraintName("fk_session_counters_session_id").OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<AudioTicket>(b =>
        {
            b.ToTable("audio_tickets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
            b.Property(x => x.AuthSessionId).HasColumnName("auth_session_id").IsRequired();
            b.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.HasOne<LectureSession>().WithMany().HasForeignKey(x => x.SessionId)
                .HasConstraintName("fk_audio_tickets_session_id").OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.SessionId).IsUnique().HasDatabaseName("ux_audio_tickets_session_id"); // one outstanding per session
            b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_audio_tickets_token_hash");
            b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_audio_tickets_expires_at");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.KeyHash).HasColumnName("key_hash").IsRequired();
            b.Property(x => x.Fingerprint).HasColumnName("fingerprint").IsRequired();
            b.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasOne<LectureSession>().WithMany().HasForeignKey(x => x.SessionId)
                .HasConstraintName("fk_idempotency_keys_session_id").OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.KeyHash }).IsUnique().HasDatabaseName("ux_idempotency_keys_user_key");
            b.HasIndex(x => x.SessionId).HasDatabaseName("ix_idempotency_keys_session_id");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_idempotency_keys_created_at");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("audit_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.SessionId).HasColumnName("session_id"); // not a FK: survives session deletion
            b.Property(x => x.Action).HasColumnName("action").HasMaxLength(48).IsRequired();
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.HasIndex(x => new { x.UserId, x.OccurredAt }).HasDatabaseName("ix_audit_events_user_time");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });
    }
}

/// <summary>Design-time only (dotnet ef).</summary>
internal sealed class LecturesDbContextFactory : IDesignTimeDbContextFactory<LecturesDbContext>
{
    private sealed class NoUser : ICurrentUser { public Guid? UserId => null; }

    public LecturesDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<LecturesDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema)).Options;
        return new LecturesDbContext(o, new NoUser());
    }
}
