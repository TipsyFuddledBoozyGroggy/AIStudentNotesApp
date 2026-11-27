using Tether.Identity.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Identity.Persistence;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<OAuthState> OAuthStates => Set<OAuthState>();
    /// <summary>Cookie key ring (ADR-022 R4). Each key's secret is wrapped with the configured certificate outside Development.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users", t => t.HasCheckConstraint("ck_users_email_lower", "email = lower(email)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ExternalSubject).HasColumnName("external_subject").HasMaxLength(256).IsRequired();
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.HostedDomainPresent).HasColumnName("hosted_domain_present");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => x.ExternalSubject).IsUnique();
            // ADR-022 / CAL-23: email is display only. Same address under two Google subjects = two users, so the index is not unique.
            b.HasIndex(x => x.Email);
        });
        modelBuilder.Entity<Session>(b =>
        {
            b.ToTable("sessions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            // Same schema, so a real FK: purging a user removes their sessions.
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("fk_sessions_user_id").OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).HasDatabaseName("ix_sessions_user_id");
        });
        modelBuilder.Entity<OAuthState>(b =>
        {
            b.ToTable("oauth_states", t =>
            {
                t.HasCheckConstraint("ck_oauth_states_hash_lengths", "octet_length(state_hash) = 32 and octet_length(binder_hash) = 32");
                t.HasCheckConstraint("ck_oauth_states_purpose", "purpose in ('signin')");
            });
            b.HasKey(x => x.StateHash);
            b.Property(x => x.StateHash).HasColumnName("state_hash");
            b.Property(x => x.BinderHash).HasColumnName("binder_hash").IsRequired();
            b.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(16).IsRequired();
            b.Property(x => x.Landing).HasColumnName("landing").HasMaxLength(16).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.ConsumedAt).HasColumnName("consumed_at").IsConcurrencyToken(); // single use (auth.v1 section 3)
            b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_oauth_states_expires_at");
        });
        modelBuilder.Entity<DataProtectionKey>(b =>
        {
            b.ToTable("data_protection_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.FriendlyName).HasColumnName("friendly_name");
            b.Property(x => x.Xml).HasColumnName("xml");
        });
    }
}

/// <summary>Design-time only (dotnet ef). Uses ConnectionStrings__Default or a throwaway local default.</summary>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema)).Options;
        return new IdentityDbContext(o);
    }
}
