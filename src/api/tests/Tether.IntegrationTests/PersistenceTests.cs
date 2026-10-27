using Tether.Courses.Domain;
using Tether.Courses.Persistence;
using Tether.IntegrationTests.Infrastructure;
using Tether.Identity.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tether.IntegrationTests;

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

    /// <summary>ADR-014: courses.user_id has a database FK, so owners must exist as identity.users rows.</summary>
    private async Task<Guid> UserAsync()
    {
        var id = Ids.New();
        await factory.ExecAsync($"insert into identity.users (id, external_subject, email, display_name, created_at) values ('{id}', 'test:{id}', '{id:N}@test.local', 'T', now())");
        return id;
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
        var alice = await UserAsync();
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
        var alice = await UserAsync();
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
        var alice = await UserAsync(); var bob = await UserAsync();
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

    // ---- ADR-014: the one allow-listed cross-schema FK ----

    [Fact]
    public async Task Cross_schema_foreign_keys_are_exactly_the_allow_listed_set()
    {
        _ = factory.CreateClient();
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            select con.conname, ns.nspname, rns.nspname, con.confdeltype::text
            from pg_constraint con
            join pg_class c on c.oid = con.conrelid join pg_namespace ns on ns.oid = c.relnamespace
            join pg_class rc on rc.oid = con.confrelid join pg_namespace rns on rns.oid = rc.relnamespace
            where con.contype = 'f' and ns.nspname <> rns.nspname
            order by con.conname
            """, conn);
        var found = new List<(string, string, string, string)>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) found.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));

        // Allow-list (docs/architecture/backend-foundation.md): courses.user_id -> identity.users, ON DELETE CASCADE ('c').
        Assert.Equal([("fk_courses_user_id", "courses", "identity", "c")], found);
    }

    [Fact]
    public async Task Database_rejects_a_course_for_an_unknown_user()
    {
        _ = factory.CreateClient();
        await Assert.ThrowsAsync<PostgresException>(() => factory.ExecAsync(
            $"insert into courses.courses (id, user_id, name, created_at, updated_at) values ('{Ids.New()}', '{Ids.New()}', 'orphan', now(), now())"));
    }

    [Fact]
    public async Task Hard_deleting_a_user_cascades_to_their_courses_and_only_theirs()
    {
        _ = factory.CreateClient();
        var gone = await UserAsync(); var kept = await UserAsync();
        await using (var ctx = Ctx(gone)) { ctx.Courses.Add(NewCourse("g1")); ctx.Courses.Add(NewCourse("g2")); await ctx.SaveChangesAsync(); }
        await using (var ctx = Ctx(kept)) { ctx.Courses.Add(NewCourse("k1")); await ctx.SaveChangesAsync(); }
        await factory.ExecAsync($"delete from identity.users where id = '{gone}'");
        Assert.Equal(0, await factory.ScalarAsync($"select count(*) from courses.courses where user_id = '{gone}'"));
        Assert.Equal(1, await factory.ScalarAsync($"select count(*) from courses.courses where user_id = '{kept}'"));
    }

    // ---- item 9: full rollback is complete. The schemas themselves cannot be dropped by Down: each schema hosts its
    // own __ef_migrations_history table, which EF still needs after Down runs (DROP SCHEMA fails with 2BP01, verified). ----

    [Fact]
    public async Task Rolling_back_every_migration_leaves_no_module_tables_behind()
    {
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var courses = scope.ServiceProvider.GetRequiredService<CoursesDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        try
        {
            await courses.GetService<IMigrator>().MigrateAsync("0");
            await identity.GetService<IMigrator>().MigrateAsync("0");
            Assert.Equal(0, await factory.ScalarAsync(
                "select count(*) from pg_tables where schemaname in ('identity','courses') and tablename <> '__ef_migrations_history'"));
        }
        finally
        {
            await identity.GetService<IMigrator>().MigrateAsync();
            await courses.GetService<IMigrator>().MigrateAsync();
        }
    }
}
