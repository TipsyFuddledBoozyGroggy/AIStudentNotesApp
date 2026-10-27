using Tether.Courses.Domain;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tether.Courses.Persistence;

internal sealed class CoursesDbContext(DbContextOptions<CoursesDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    public const string Schema = "courses";
    public DbSet<Course> Courses => Set<Course>();

    // Evaluated per context instance by EF; anonymous callers match nothing (Guid.Empty is never stored).
    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Course>(b =>
        {
            b.ToTable("courses", t => t.HasCheckConstraint("ck_courses_name_not_blank", "length(btrim(name)) > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired(); // DB-only FK to identity.users (ADR-014, migration M004); no EF navigation
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(32);
            b.Property(x => x.Term).HasColumnName("term").HasMaxLength(64);
            b.Property(x => x.Instructor).HasColumnName("instructor").HasMaxLength(120);
            b.Property(x => x.Color).HasColumnName("color").HasMaxLength(7);
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.ArchivedAt).HasColumnName("archived_at");
            b.Property(x => x.Version).IsRowVersion(); // xmin
            // Target of composite FKs from child tables (lecture sessions, M2): (course_id, user_id).
            b.HasIndex(x => new { x.Id, x.UserId }).IsUnique().HasDatabaseName("ux_courses_id_user_id");
            b.HasIndex(x => new { x.UserId, x.Id }).HasDatabaseName("ix_courses_user_id_id");
            b.HasQueryFilter(x => x.UserId == CurrentUserId);
        });
    }
}

/// <summary>Design-time only (dotnet ef).</summary>
internal sealed class CoursesDbContextFactory : IDesignTimeDbContextFactory<CoursesDbContext>
{
    private sealed class NoUser : ICurrentUser { public Guid? UserId => null; }

    public CoursesDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=design_time";
        var o = new DbContextOptionsBuilder<CoursesDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", CoursesDbContext.Schema)).Options;
        return new CoursesDbContext(o, new NoUser());
    }
}
