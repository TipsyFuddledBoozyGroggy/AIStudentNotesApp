using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Tether.Identity.Google;

/// <summary>
/// Sign-in budgets (auth.v1 section 8, ADR-022 R6). The two Google routes are exempt from the host's per-IP anonymous bucket (in Azure every
/// caller shares one address, so that bucket would be one global 60/min for all sign-ins) and use these instead.
/// <list type="bullet">
/// <item>Start: a global bucket, then a per-binder bucket when the browser already carries a binder cookie.</item>
/// <item>Callback, VALID (the state row is live and the binder cookie matches it; only these go on to call Google): a global bucket, then a
/// per-binder bucket. Nothing a caller can forge reaches this budget: a random well-formed cookie with an unknown state is not valid.</item>
/// <item>Callback, INVALID (anything else: unknown, expired or consumed state, missing or mismatched binder): one small bucket of its own,
/// so a flood of forged callbacks can never starve real returns from Google.</item>
/// </list>
/// The global permit is always taken BEFORE a per-binder partition is touched, so the number of live partitions is bounded by the global
/// budget, not by how many random cookie values a caller sends. Forwarded headers are not trusted (R6).
/// </summary>
internal sealed class SignInLimiter : IDisposable
{
    public const string StartKey = "RateLimiting:AuthStartPermitPerMinute";
    public const string StartPerBinderKey = "RateLimiting:AuthStartPerBinderPermitPerMinute";
    public const string CallbackKey = "RateLimiting:AuthCallbackPermitPerMinute";
    public const string CallbackPerBinderKey = "RateLimiting:AuthCallbackPerBinderPermitPerMinute";
    public const string CallbackInvalidKey = "RateLimiting:AuthCallbackInvalidPermitPerMinute";

    /// <summary>The shipped defaults (also read by the host's N-05 "raised above the default" warning).</summary>
    public static readonly (string Key, int Default)[] Defaults =
    [
        (StartKey, 300), (StartPerBinderKey, 10), (CallbackKey, 300), (CallbackPerBinderKey, 10), (CallbackInvalidKey, 30),
    ];

    private readonly RateLimiter _start, _callback, _invalid;
    private readonly PartitionedRateLimiter<string> _startPerBinder, _callbackPerBinder;

    public SignInLimiter(IConfiguration config)
    {
        int Read(string key) => Permits(config, key, Defaults.First(d => d.Key == key).Default);
        _start = Window(Read(StartKey));
        _callback = Window(Read(CallbackKey));
        _invalid = Window(Read(CallbackInvalidKey));
        var startPer = Read(StartPerBinderKey);
        var callbackPer = Read(CallbackPerBinderKey);
        _startPerBinder = PartitionedRateLimiter.Create<string, string>(k => RateLimitPartition.GetFixedWindowLimiter(k, _ => Options(startPer)));
        _callbackPerBinder = PartitionedRateLimiter.Create<string, string>(k => RateLimitPartition.GetFixedWindowLimiter(k, _ => Options(callbackPer)));
    }

    internal static int Permits(IConfiguration config, string key, int dflt)
    {
        var v = config.GetValue(key, dflt);
        if (v <= 0) throw new InvalidOperationException($"{key} must be a positive number (was {v}).");
        return v;
    }

    private static FixedWindowRateLimiterOptions Options(int permits) =>
        new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true };

    private static FixedWindowRateLimiter Window(int permits) => new(Options(permits));

    /// <param name="binderKey">Hex SHA-256 of a well-formed binder cookie, or null when the request carries none.</param>
    public bool TryStart(string? binderKey) => GlobalThenPerBinder(_start, _startPerBinder, binderKey);

    /// <summary>A callback whose state row is live and whose binder matched it. <paramref name="binderKey"/> is that binder's hash.</summary>
    public bool TryValidCallback(string binderKey) => GlobalThenPerBinder(_callback, _callbackPerBinder, binderKey);

    /// <summary>Every other callback.</summary>
    public bool TryInvalidCallback()
    {
        using var lease = _invalid.AttemptAcquire();
        return lease.IsAcquired;
    }

    private static bool GlobalThenPerBinder(RateLimiter global, PartitionedRateLimiter<string> perBinder, string? binderKey)
    {
        using var g = global.AttemptAcquire();
        if (!g.IsAcquired) return false;
        if (binderKey is null) return true;
        using var p = perBinder.AttemptAcquire(binderKey);
        return p.IsAcquired;
    }

    public void Dispose()
    {
        _start.Dispose(); _callback.Dispose(); _invalid.Dispose(); _startPerBinder.Dispose(); _callbackPerBinder.Dispose();
    }
}
