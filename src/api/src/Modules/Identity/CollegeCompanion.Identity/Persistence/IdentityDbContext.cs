using CollegeCompanion.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CollegeCompanion.Identity.Persistence;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";
    public DbSet<User> Users => Set<User>();

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
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.HasIndex(x => x.ExternalSubject).IsUnique();
            b.HasIndex(x => x.Email).IsUnique();
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
