using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tether.IntegrationTests.Infrastructure;

/// <summary>
/// Makes a check-then-act race deterministic: the first command whose SQL matches the armed predicate first runs the competing action
/// (which commits on its own connection), then executes. So the request under test reads state that changed between two of its own
/// statements, exactly as under real concurrency. Fires once per Arm.
/// </summary>
public sealed class CommandTrap : DbCommandInterceptor
{
    private Func<string, bool>? _match;
    private Func<DbCommand, Task>? _competing;

    public void Arm(Func<string, bool> match, Func<Task> competing) => Arm(match, _ => competing());

    /// <summary>As above; the competing action also sees the trapped command (for example its connection's backend process id).</summary>
    public void Arm(Func<string, bool> match, Func<DbCommand, Task> competing) { _competing = competing; _match = match; }

    public bool Fired { get; private set; }

    private async Task FireAsync(DbCommand command)
    {
        var match = _match;
        if (match is null || !match(command.CommandText)) return;
        _match = null;
        Fired = true;
        await _competing!(command);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        await FireAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
    {
        await FireAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        await FireAsync(command);
        return result;
    }
}
