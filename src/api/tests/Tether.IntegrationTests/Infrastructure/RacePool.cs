using Npgsql;

namespace Tether.IntegrationTests.Infrastructure;

/// <summary>
/// NAS-R09 (docs/security/notes-after-stop-rereview.md): test classes that race concurrent requests against a check-then-insert (one press per
/// user, one live session per user, idempotency keys, budget reservations, ticket consumption, syllabus sync) run with a pool of
/// <see cref="Size"/> connections so all of their parallel operations really overlap in PostgreSQL. They share this collection, so xunit runs
/// them one at a time: only one large pool is in use at once, which keeps the suite's peak under the dev Postgres max_connections (100)
/// without going back to unpooled connections (the ephemeral-port exhaustion of 2026-10-31).
/// </summary>
[CollectionDefinition(Name)]
public sealed class RacePool
{
    public const string Name = "race-pool";

    /// <summary>At least the largest race in these classes (20 concurrent reservations in AiBudgetGlobalCapTests), plus room for the host's own workers.</summary>
    public const int Size = 24;
}

/// <summary>The default host, with the race pool.</summary>
public sealed class RacePoolFactory : ApiFactory
{
    protected override int MaxPoolSize => RacePool.Size;
}

/// <summary>Checks that the race pool does what it is for: N operations overlap in the database, where the default pool serialises them.</summary>
[Collection(RacePool.Name)]
public class RacePoolTests(RacePoolFactory factory) : IClassFixture<RacePoolFactory>
{
    private static async Task<int> PeakOverlapAsync(string connectionString, int n)
    {
        var observer = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        var probe = "select pg_sleep(0.6) /* race-pool-probe " + Guid.NewGuid().ToString("N") + " */";
        var peak = 0;
        using var stop = new CancellationTokenSource();
        var watch = Task.Run(async () =>
        {
            await using var c = new NpgsqlConnection(observer);
            await c.OpenAsync();
            while (!stop.IsCancellationRequested)
            {
                await using var cmd = new NpgsqlCommand("select count(*) from pg_stat_activity where datname = current_database() and state = 'active' and query = @q", c);
                cmd.Parameters.AddWithValue("q", probe);
                peak = Math.Max(peak, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
                await Task.Delay(20);
            }
        });
        await Task.WhenAll(Enumerable.Range(0, n).Select(_ => Task.Run(async () =>
        {
            await using var c = new NpgsqlConnection(connectionString);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(probe, c);
            await cmd.ExecuteNonQueryAsync();
        })));
        stop.Cancel();
        await watch;
        return peak;
    }

    [Fact]
    public async Task Twenty_concurrent_operations_overlap_in_the_database_with_the_race_pool_but_not_with_the_default_pool()
    {
        const int n = 20;
        var race = await PeakOverlapAsync(factory.ConnectionString, n);
        Assert.True(race >= n, $"race pool: only {race} of {n} operations were in the database at the same time");

        var small = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { MaxPoolSize = ApiFactory.DefaultPoolSize }.ConnectionString;
        var serialised = await PeakOverlapAsync(small, n);
        Assert.True(serialised <= ApiFactory.DefaultPoolSize, $"default pool: {serialised} overlapped");
    }
}
