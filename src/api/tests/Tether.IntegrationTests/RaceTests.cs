using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Tether.Courses.Persistence;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// Fires a competing write on a separate connection exactly when the API is about to run its own UPDATE/DELETE,
/// so the read-then-write race in the endpoint is deterministic.
/// </summary>
public sealed class RaceInterceptor : DbCommandInterceptor
{
    private string? _prefix;
    private Func<Task>? _competing;

    public void Arm(string sqlPrefix, Func<Task> competing) { _competing = competing; _prefix = sqlPrefix; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        var prefix = _prefix;
        if (prefix is not null && command.CommandText.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
        {
            _prefix = null;
            await _competing!();
        }
        return result;
    }
}

public sealed class RaceFactory : ApiFactory
{
    public RaceInterceptor Race { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<CoursesDbContext>(o => o.AddInterceptors(Race)));
    }
}

public class DeleteRaceTests(RaceFactory factory) : IClassFixture<RaceFactory>
{
    private async Task<(HttpClient c, string id)> CourseAsync()
    {
        var c = await factory.LoginAsync($"r{Guid.NewGuid():N}@dev.local");
        var b = await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "race" })).BodyAsync();
        return (c, b.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Purge_racing_a_concurrent_delete_is_404_not_500()
    {
        var (c, id) = await CourseAsync();
        factory.Race.Arm("DELETE", () => factory.ExecAsync($"delete from courses.courses where id = '{id}'"));
        await (await c.DeleteAsync($"/api/v1/courses/{id}?purge=true")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Archive_racing_a_concurrent_delete_is_404()
    {
        var (c, id) = await CourseAsync();
        factory.Race.Arm("UPDATE", () => factory.ExecAsync($"delete from courses.courses where id = '{id}'"));
        await (await c.DeleteAsync($"/api/v1/courses/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Archive_racing_a_concurrent_update_is_409()
    {
        var (c, id) = await CourseAsync();
        factory.Race.Arm("UPDATE", () => factory.ExecAsync($"update courses.courses set name = 'other' where id = '{id}'"));
        await (await c.DeleteAsync($"/api/v1/courses/{id}")).AssertProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        Assert.Equal(0, await factory.ScalarAsync($"select count(*) from courses.courses where id = '{id}' and archived_at is not null"));
    }

    [Fact]
    public async Task Purge_racing_a_concurrent_update_is_409_and_the_course_survives()
    {
        var (c, id) = await CourseAsync();
        factory.Race.Arm("DELETE", () => factory.ExecAsync($"update courses.courses set name = 'other' where id = '{id}'"));
        await (await c.DeleteAsync($"/api/v1/courses/{id}?purge=true")).AssertProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        Assert.Equal(1, await factory.ScalarAsync($"select count(*) from courses.courses where id = '{id}'"));
    }
}
