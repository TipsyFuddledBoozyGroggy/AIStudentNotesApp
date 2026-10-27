using CollegeCompanion.Courses.Domain;
using CollegeCompanion.Courses.Persistence;
using CollegeCompanion.IntegrationTests.Infrastructure;
using CollegeCompanion.Identity.Persistence;
using CollegeCompanion.SharedKernel;
using CollegeCompanion.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CollegeCompanion.IntegrationTests;

public class PersistenceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class FakeUser(Guid? id) : ICurrentUser { public Guid? UserId { get; } = id; }

    private CoursesDbContext Ctx(Guid? user)
    {
        var fake = new FakeUser(user);
        var options = new DbContextOptionsBuilder<CoursesDbContext>()
            .UseNpgsql(factory.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", CoursesDbContext.Schema))
            .AddInterceptors(new OwnershipSaveChangesInterceptor(fake)).Options;
        return new CoursesDbContext(options, fake);
    }

    private static Course NewCourse(string name, Guid? owner = null) => new()
    {
        Id = Ids.New(), Name = name, UserId = owner ?? Guid.Empty,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Migrations_have_been_applied_to_the_throwaway_database()
    {
        factory.AssertUsesTestDatabase();
        _ = factory.CreateClient(); // boots host, which migrates in Development
        await using var ctx = Ctx(Guid.NewGuid());
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Interceptor_stamps_owner_on_insert()
    {
        _ = factory.CreateClient();
        var alice = Guid.NewGuid();
        var course = NewCourse("stamped");
        await using (var ctx = Ctx(alice)) { ctx.Courses.Add(course); await ctx.SaveChangesAsync(); }
        await using (var ctx = Ctx(alice)) Assert.Equal(alice, (await ctx.Courses.SingleAsync(c => c.Id == course.Id)).UserId);
    }

    [Fact]
    public async Task Interceptor_rejects_insert_for_another_user_and_anonymous_writes()
    {
        _ = factory.CreateClient();
        await using (var ctx = Ctx(Guid.NewGuid()))
        {
            ctx.Courses.Add(NewCourse("forged", owner: Guid.NewGuid()));
            await Assert.ThrowsAsync<OwnershipViolationException>(() => ctx.SaveChangesAsync());
        }
        await using (var ctx = Ctx(null))
        {
            ctx.Courses.Add(NewCourse("anon"));
            await Assert.ThrowsAsync<OwnershipViolationException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Interceptor_rejects_reassigning_ownership()
    {
        _ = factory.CreateClient();
        var alice = Guid.NewGuid();
        var course = NewCourse("mine");
        await using (var ctx = Ctx(alice)) { ctx.Courses.Add(course); await ctx.SaveChangesAsync(); }
        await using (var ctx = Ctx(alice))
        {
            var c = await ctx.Courses.SingleAsync(x => x.Id == course.Id);
            c.UserId = Guid.NewGuid();
            await Assert.ThrowsAsync<OwnershipViolationException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Query_filter_scopes_every_query_to_the_current_user_and_anonymous_sees_nothing()
    {
        _ = factory.CreateClient();
        var alice = Guid.NewGuid(); var bob = Guid.NewGuid();
        var a = NewCourse("a-only"); var b = NewCourse("b-only");
        await using (var ctx = Ctx(alice)) { ctx.Courses.Add(a); await ctx.SaveChangesAsync(); }
        await using (var ctx = Ctx(bob)) { ctx.Courses.Add(b); await ctx.SaveChangesAsync(); }

        await using (var ctx = Ctx(alice))
        {
            Assert.Equal([a.Id], await ctx.Courses.Select(c => c.Id).ToListAsync());
            Assert.Null(await ctx.Courses.FirstOrDefaultAsync(c => c.Id == b.Id));
            Assert.Equal(0, await ctx.Courses.CountAsync(c => c.Id == b.Id));
        }
        await using (var ctx = Ctx(null)) Assert.Empty(await ctx.Courses.ToListAsync());
    }

    [Fact]
    public void Every_user_owned_entity_has_a_global_query_filter()
    {
        _ = factory.CreateClient();
        using var ctx = Ctx(Guid.NewGuid());
        var owned = ctx.Model.GetEntityTypes().Where(t => typeof(IUserOwned).IsAssignableFrom(t.ClrType)).ToList();
        Assert.NotEmpty(owned);
        foreach (var t in owned) Assert.NotEmpty(t.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task Courses_table_exposes_unique_id_user_id_for_composite_fks_in_later_milestones()
    {
        _ = factory.CreateClient();
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*) from pg_indexes where schemaname='courses' and indexname='ux_courses_id_user_id'", conn);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Migrations_apply_roll_back_and_reapply_cleanly()
    {
        _ = factory.CreateClient();
        var sp = factory.Services;
        using var scope = sp.CreateScope();
        var courses = scope.ServiceProvider.GetRequiredService<CoursesDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await courses.GetService<IMigrator>().MigrateAsync("0");
        await identity.GetService<IMigrator>().MigrateAsync("0");
        Assert.Empty(await courses.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await identity.Database.GetAppliedMigrationsAsync());

        await identity.GetService<IMigrator>().MigrateAsync();
        await courses.GetService<IMigrator>().MigrateAsync();
        Assert.Empty(await courses.Database.GetPendingMigrationsAsync());
        Assert.Empty(await identity.Database.GetPendingMigrationsAsync());
    }
}
