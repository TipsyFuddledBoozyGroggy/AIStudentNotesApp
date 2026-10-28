using System.Collections.Concurrent;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.IntegrationTests;

// N-05: rate-limit overrides above the shipped defaults are a deployment smell outside Development (the dev overlay raises them for E2E).
public sealed class WarnLogs : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new L(this);
    public void Dispose() { }
    private sealed class L(WarnLogs p) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt) => p.Lines.Enqueue((level, fmt(state, ex)));
    }
}

public sealed class RaisedLimitsProductionFactory : ApiFactory
{
    public WarnLogs Logs { get; } = new();
    protected override string EnvironmentName => "Production";
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false")]; // ApiFactory raises all four to 100000
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
    }
}

public sealed class RaisedLimitsDevelopmentFactory : ApiFactory
{
    public WarnLogs Logs { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
    }
}

public sealed class ZeroPermitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("RateLimiting:ConsentReversalPermitPerMinute", "0")];
}

public class RateLimitConfigTests
{
    public class Production(RaisedLimitsProductionFactory f) : IClassFixture<RaisedLimitsProductionFactory>
    {
        [Fact]
        public void Overrides_above_the_defaults_outside_development_log_a_startup_warning_naming_the_keys()
        {
            _ = f.CreateClient(); // boots the host
            var warnings = f.Logs.Lines.Where(l => l.Level == LogLevel.Warning && l.Message.Contains("RateLimiting")).Select(l => l.Message).ToList();
            Assert.Contains(warnings, m => m.Contains("RateLimiting:ApiPermitPerMinute") && m.Contains("RateLimiting:WritePermitPerMinute"));
        }
    }

    public class Development(RaisedLimitsDevelopmentFactory f) : IClassFixture<RaisedLimitsDevelopmentFactory>
    {
        [Fact]
        public void Development_does_not_warn_about_raised_limits()
        {
            _ = f.CreateClient();
            Assert.DoesNotContain(f.Logs.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("RateLimiting"));
        }
    }

    [Fact]
    public async Task A_zero_or_negative_permit_is_a_startup_error()
    {
        var f = new ZeroPermitFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
            var chain = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException) chain.Add(e.Message);
            Assert.Contains(chain, m => m.Contains("RateLimiting:ConsentReversalPermitPerMinute", StringComparison.Ordinal));
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }
}
