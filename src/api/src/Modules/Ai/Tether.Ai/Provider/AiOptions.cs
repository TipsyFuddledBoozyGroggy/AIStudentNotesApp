using Microsoft.Extensions.Configuration;

namespace Tether.Ai.Provider;

/// <summary>Token prices in micro-USD per million tokens. UNVERIFIED (vendor gate G2): placeholders, replace from the Console after PO-4.</summary>
internal sealed record TierPrice(long InputMicroPerMTok, long CachedInputMicroPerMTok, long OutputMicroPerMTok, string Model);

internal sealed record BudgetOptions(
    long PerSessionMicro, long PerUserDailyMicro, long GlobalMonthlyMicro,
    double SlowdownAt, double DropCompactionAt, double FastOnlyAt, double StopSummaryAt, bool ExplicitlyConfigured);

internal sealed record SummaryOptions(
    int FlushSegments, TimeSpan FlushAfter, TimeSpan MinInterval, TimeSpan Tick, int MaxNewChars, int RecentWindowChars,
    int RollingSummaryChars, int CompactEveryRevisions, int CompactionChunkChars, int CompactionMaxChunks, int MaxOutputTokens, int MaxConcurrent,
    int MaxTrackedSessions);

internal sealed record CatchUpOptions(int PerMinutePerSession, int WindowMaxChars, int MaxOutputTokens, TimeSpan Timeout, int Workers, int QueueCapacity, TimeSpan CoalesceWindow, TimeSpan StaleAfter);

internal sealed record BreakerOptions(int FailuresToOpen, TimeSpan OpenFor, TimeSpan ProviderCapOpenFor);

/// <summary>
/// Configuration for the AI module (keys <c>Ai:*</c>). Nothing here is a verified vendor figure. The API key, when one is ever
/// configured, is read by <see cref="AiProviderGuard"/> only to check that it is present; it is never stored in this record.
/// </summary>
internal sealed record AiOptions(
    bool Enabled, string Provider, TimeSpan CallTimeout, BudgetOptions Budget, TierPrice Fast, TierPrice Balanced,
    SummaryOptions Summary, CatchUpOptions CatchUp, BreakerOptions Breaker, MockFault MockFault, TimeSpan MockLatency)
{
    public TierPrice Price(AiTier tier) => tier == AiTier.Fast ? Fast : Balanced;

    public static AiOptions From(IConfiguration c)
    {
        static TimeSpan Secs(IConfiguration c, string key, double def) => TimeSpan.FromSeconds(c.GetValue(key, def));
        var b = c.GetSection("Ai:Budget");
        var budget = new BudgetOptions(
            PerSessionMicro: b.GetValue("PerSessionMicroUsd", 200_000L),        // $0.20, placeholder
            PerUserDailyMicro: b.GetValue("PerUserDailyMicroUsd", 1_000_000L),   // $1.00, placeholder
            GlobalMonthlyMicro: b.GetValue("GlobalMonthlyMicroUsd", 50_000_000L), // $50, placeholder
            SlowdownAt: b.GetValue("SlowdownAt", 0.60), DropCompactionAt: b.GetValue("DropCompactionAt", 0.75),
            FastOnlyAt: b.GetValue("FastOnlyAt", 0.85), StopSummaryAt: b.GetValue("StopSummaryAt", 0.90),
            ExplicitlyConfigured: b["PerSessionMicroUsd"] is not null && b["PerUserDailyMicroUsd"] is not null && b["GlobalMonthlyMicroUsd"] is not null);

        // Model ids come from the vendor report (claude-haiku-5-5 / claude-sonnet-5-5); prices are the report's figures, UNVERIFIED (G2).
        TierPrice Price(string tier, long inp, long cached, long outp, string model) => new(
            c.GetValue($"Ai:Prices:{tier}:InputMicroUsdPerMTok", inp), c.GetValue($"Ai:Prices:{tier}:CachedInputMicroUsdPerMTok", cached),
            c.GetValue($"Ai:Prices:{tier}:OutputMicroUsdPerMTok", outp), c.GetValue($"Ai:Models:{tier}", model)!);

        var s = c.GetSection("Ai:Summary");
        var summary = new SummaryOptions(
            FlushSegments: s.GetValue("FlushSegments", 8), FlushAfter: Secs(c, "Ai:Summary:FlushSeconds", 90), MinInterval: Secs(c, "Ai:Summary:MinIntervalSeconds", 45),
            Tick: Secs(c, "Ai:Summary:TickSeconds", 5), MaxNewChars: s.GetValue("MaxNewChars", 16_000), RecentWindowChars: s.GetValue("RecentWindowChars", 6_000),
            RollingSummaryChars: s.GetValue("RollingSummaryChars", 2_400), CompactEveryRevisions: s.GetValue("CompactEveryRevisions", 10),
            CompactionChunkChars: s.GetValue("CompactionChunkChars", 12_000), CompactionMaxChunks: s.GetValue("CompactionMaxChunks", 4),
            MaxOutputTokens: s.GetValue("MaxOutputTokens", 700), MaxConcurrent: s.GetValue("MaxConcurrent", 4), MaxTrackedSessions: s.GetValue("MaxTrackedSessions", 2000));

        var k = c.GetSection("Ai:CatchUp");
        var catchUp = new CatchUpOptions(
            PerMinutePerSession: k.GetValue("PerMinute", 6), WindowMaxChars: k.GetValue("WindowMaxChars", 12_000), MaxOutputTokens: k.GetValue("MaxOutputTokens", 1_200),
            Timeout: Secs(c, "Ai:CatchUp:TimeoutSeconds", 15), Workers: k.GetValue("Workers", 2), QueueCapacity: k.GetValue("QueueCapacity", 32),
            CoalesceWindow: Secs(c, "Ai:CatchUp:CoalesceSeconds", 30), StaleAfter: Secs(c, "Ai:CatchUp:StaleSeconds", 120));

        var br = c.GetSection("Ai:Breaker");
        var breaker = new BreakerOptions(br.GetValue("FailuresToOpen", 3), Secs(c, "Ai:Breaker:OpenSeconds", 60), Secs(c, "Ai:Breaker:ProviderCapOpenSeconds", 600));

        Enum.TryParse<MockFault>(c["Ai:Mock:Fault"], ignoreCase: true, out var fault);
        return new AiOptions(
            Enabled: c.GetValue("Ai:Enabled", true), Provider: (c["Ai:Provider"] ?? "mock").Trim().ToLowerInvariant(),
            CallTimeout: Secs(c, "Ai:CallTimeoutSeconds", 20), Budget: budget,
            Fast: Price("Fast", 100_000, 10_000, 500_000, "claude-haiku-5-5"),
            Balanced: Price("Balanced", 2_000_000, 100_000, 10_000_000, "claude-sonnet-5-5"),
            Summary: summary, CatchUp: catchUp, Breaker: breaker, MockFault: fault, MockLatency: TimeSpan.FromMilliseconds(c.GetValue("Ai:Mock:LatencyMs", 0)));
    }
}

/// <summary>
/// Startup guard. The mock is the only provider that can run. Anything else is refused: first for missing explicit key and budget
/// configuration (so a half-configured deployment cannot spend), and then, even with both present, because no real adapter exists
/// in this increment (vendor gate G2, PO-4 open).
/// </summary>
internal static class AiProviderGuard
{
    public static void Validate(IConfiguration config, AiOptions options, bool developmentOrTest)
    {
        if (options.Provider != "mock")
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(config["Ai:Anthropic:ApiKey"])) missing.Add("Ai:Anthropic:ApiKey");
            if (!options.Budget.ExplicitlyConfigured) missing.Add("Ai:Budget:PerSessionMicroUsd/PerUserDailyMicroUsd/GlobalMonthlyMicroUsd (all three, explicitly)");
            if (missing.Count > 0)
                throw new InvalidOperationException("Ai:Provider is not 'mock' but required configuration is missing: " + string.Join("; ", missing) + ". Real Claude calls need an explicit key and explicit budgets (PO-4).");
            throw new InvalidOperationException("Ai:Provider must be 'mock'. No real AI provider adapter is implemented or authorized (gate G2 and PO-4 are open); see docs/ai/b3-implementation.md.");
        }
        if (!developmentOrTest && options.MockFault != MockFault.None)
            throw new InvalidOperationException("Ai:Mock:Fault is set but the environment is not Development or Test. Fault injection is demo/test only.");
        if (!developmentOrTest && options.MockLatency > TimeSpan.Zero)
            throw new InvalidOperationException("Ai:Mock:LatencyMs is set but the environment is not Development or Test.");
        if (options.Budget.PerSessionMicro <= 0 || options.Budget.PerUserDailyMicro <= 0 || options.Budget.GlobalMonthlyMicro <= 0)
            throw new InvalidOperationException("Ai:Budget caps must be positive. To switch AI off set Ai:Enabled=false.");
    }
}
