using Microsoft.Extensions.Configuration;

namespace Tether.Ai.Provider;

/// <summary>Token prices in micro-USD per million tokens. UNVERIFIED (vendor gate G2): placeholders, replace from the Console after PO-4.</summary>
internal sealed record TierPrice(long InputMicroPerMTok, long CachedInputMicroPerMTok, long OutputMicroPerMTok, string Model);

/// <summary>The three caps of one budget pool: per scope (a lecture session, or a document job), per user per UTC day, and global per UTC month.</summary>
internal sealed record PoolBudget(long PerScopeMicro, long PerUserDailyMicro, long GlobalMonthlyMicro);

internal sealed record BudgetOptions(
    long PerSessionMicro, long PerUserDailyMicro, long GlobalMonthlyMicro,
    double SlowdownAt, double DropCompactionAt, double FastOnlyAt, double StopSummaryAt, bool ExplicitlyConfigured,
    PoolBudget? SyllabusPool = null)
{
    /// <summary>
    /// Caps for a budget pool. Lecture and syllabus pools are independent: spend in one never counts toward the other's caps, so a large
    /// syllabus can exhaust only the syllabus pool and never the lecture AI allowance (and the reverse).
    /// </summary>
    public PoolBudget For(string pool) => pool switch
    {
        Tether.SharedKernel.Ai.AiScopeKinds.Lecture => new PoolBudget(PerSessionMicro, PerUserDailyMicro, GlobalMonthlyMicro),
        Tether.SharedKernel.Ai.AiScopeKinds.Syllabus => SyllabusPool ?? DefaultSyllabusPool,
        _ => throw new ArgumentOutOfRangeException(nameof(pool)),
    };

    /// <summary>Placeholders, like the lecture ones (vendor gate G2): $0.10 per job, $0.50 per user-day, $10 per month.</summary>
    public static PoolBudget DefaultSyllabusPool { get; } = new(100_000, 500_000, 10_000_000);
}

internal sealed record SummaryOptions(
    int FlushSegments, TimeSpan FlushAfter, TimeSpan MinInterval, TimeSpan Tick, int MaxNewChars, int RecentWindowChars,
    int RollingSummaryChars, int CompactEveryRevisions, int CompactionChunkChars, int CompactionMaxChunks, int MaxOutputTokens, int MaxConcurrent,
    int MaxTrackedSessions);

internal sealed record CatchUpOptions(int PerMinutePerSession, int WindowMaxChars, int MaxOutputTokens, TimeSpan Timeout, int Workers, int QueueCapacity, TimeSpan CoalesceWindow, TimeSpan StaleAfter);

internal sealed record BreakerOptions(int FailuresToOpen, TimeSpan OpenFor, TimeSpan ProviderCapOpenFor);

/// <summary>
/// PO-18d "Make notes" after Stop. Every figure is a PROPOSAL (placeholders like the budgets, gate G2). <see cref="PerRequestMicro"/> is the cap
/// on the ESTIMATED cost of everything one press may send (notes chunks plus the final summary); a press over it makes no call at all.
/// The calls are also held against the ordinary lecture-pool caps (per session, per user-day, per month) one by one.
/// </summary>
/// <param name="StaleAfter">NAS-03: the gateway permission's lifetime, counted from the moment the job STARTS running (queue wait does not
/// count). Defaults to the computed worst case of one press (<see cref="WorstCase"/>) plus a minute; an explicit smaller value is refused at startup.</param>
/// <param name="PerRequestMicro">Cap on the estimated cost of one press, INCLUDING the one repair attempt every call may make (NAS-07).</param>
/// <param name="PerUserDailyMicro">NAS-02: the notes sub-cap, the most Notes + FinalSummary may spend per user per UTC day inside the lecture pool.
/// Configured default: 50% of <c>Ai:Budget:PerUserDailyMicroUsd</c> (the record default matches the default 1,000,000 cap).</param>
/// <param name="LiveFloorPerUserDailyMicro">NAS-02: the part of the user's daily lecture cap that notes may never use (reserved for live TL;DR and Quick recap). Default 40% of that cap.</param>
/// <param name="LiveFloorGlobalMonthlyMicro">NAS-02: the part of the global monthly lecture cap that notes may never use. Default 40% of that cap.</param>
/// <param name="MaxQueueWait">NAS-03: a press still waiting in the queue this long after it was pressed ends Unavailable (the student may press again).</param>
/// <param name="NoticeVersions">NAS-05: notice ids the server accepts in <c>noticeVersion</c>. DRAFT placeholder ids (G1 owns the wording).</param>
internal sealed record NotesOptions(
    int ChunkChars, int MaxChunks, int MaxSegments, int MaxOutputTokens, TimeSpan Timeout, int Workers, int QueueCapacity, TimeSpan StaleAfter,
    int PerMinutePerUser, int MaxAttemptsPerSession, long PerRequestMicro,
    long PerUserDailyMicro = 500_000, long LiveFloorPerUserDailyMicro = 400_000, long LiveFloorGlobalMonthlyMicro = 20_000_000,
    BreakerOptions? BreakerConfig = null, TimeSpan? MaxQueueWaitConfig = null, IReadOnlyList<string>? NoticeVersionsConfig = null,
    IReadOnlyList<string>? RetiredNoticeVersionsConfig = null)
{
    public const string DraftNoticeVersion = "notes-notice-draft-1";

    /// <summary>NAS-R07: the A007 backfill for presses made before any notice was recorded. Never configurable, never accepted.</summary>
    public const string UnrecordedNotice = "none-recorded";

    public static NotesOptions Default { get; } = new(12_000, 10, 6_000, 1_500, TimeSpan.FromSeconds(30), 1, 16, TimeSpan.FromMinutes(15), 6, 5, 600_000);

    public BreakerOptions Breaker => BreakerConfig ?? new BreakerOptions(3, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(600));

    public TimeSpan MaxQueueWait => MaxQueueWaitConfig ?? TimeSpan.FromMinutes(30);

    /// <summary>NAS-05/NAS-R07: the ACTIVE notice ids only (the wording a client may show now). A wording change is enforced by moving the old id
    /// from here to <see cref="RetiredNoticeVersions"/>: presses naming it are then refused, while stored rows keep the id they were made under.</summary>
    public IReadOnlyList<string> NoticeVersions => NoticeVersionsConfig ?? [DraftNoticeVersion];

    /// <summary>NAS-R07: ids of notices whose wording was replaced. Never accepted; the startup guard refuses one that is also listed as active.</summary>
    public IReadOnlyList<string> RetiredNoticeVersions => RetiredNoticeVersionsConfig ?? [];

    public bool IsKnownNotice(string v) =>
        NoticeVersions.Contains(v, StringComparer.Ordinal) && !RetiredNoticeVersions.Contains(v, StringComparer.Ordinal) && v != UnrecordedNotice;

    /// <summary>
    /// NAS-03: the longest one press can run once started: every notes part twice (one repair) at the notes timeout, plus every final-summary
    /// chunk twice at the general call timeout. Defaults: 10 x 2 x 30 s + 4 x 2 x 20 s = 760 s.
    /// </summary>
    public TimeSpan WorstCase(TimeSpan callTimeout, int finalChunks) =>
        Timeout * (2 * Math.Max(1, MaxChunks)) + callTimeout * (2 * Math.Max(1, finalChunks));
}

/// <summary>
/// Configuration for the AI module (keys <c>Ai:*</c>). Nothing here is a verified vendor figure. The API key, when one is ever
/// configured, is read by <see cref="AiProviderGuard"/> only to check that it is present; it is never stored in this record.
/// </summary>
internal sealed record AiOptions(
    bool Enabled, string Provider, TimeSpan CallTimeout, BudgetOptions Budget, TierPrice Fast, TierPrice Balanced,
    SummaryOptions Summary, CatchUpOptions CatchUp, BreakerOptions Breaker, MockFault MockFault, TimeSpan MockLatency,
    Tether.Ai.Budget.ReaperOptions? Reaper = null, bool LanguageGuardEnforce = false, NotesOptions? NotesConfig = null)
{
    public TierPrice Price(AiTier tier) => tier == AiTier.Fast ? Fast : Balanced;

    public NotesOptions Notes => NotesConfig ?? NotesOptions.Default;

    /// <summary>The longest any single provider call may run (the reservation reaper waits at least this long before reclaiming a hold).</summary>
    public TimeSpan LongestCallTimeout => new[] { CallTimeout, CatchUp.Timeout, Notes.Timeout }.Max();

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
            ExplicitlyConfigured: b["PerSessionMicroUsd"] is not null && b["PerUserDailyMicroUsd"] is not null && b["GlobalMonthlyMicroUsd"] is not null,
            // Separate pool for document (syllabus) work: its own per-job, per-user-day and global-month caps (docs/data/schema-syllabus-p2.md section 11).
            SyllabusPool: new PoolBudget(
                c.GetValue("Ai:Pools:syllabus:PerJobMicroUsd", BudgetOptions.DefaultSyllabusPool.PerScopeMicro),
                c.GetValue("Ai:Pools:syllabus:PerUserDailyMicroUsd", BudgetOptions.DefaultSyllabusPool.PerUserDailyMicro),
                c.GetValue("Ai:Pools:syllabus:GlobalMonthlyMicroUsd", BudgetOptions.DefaultSyllabusPool.GlobalMonthlyMicro)));

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
        var callTimeout = Secs(c, "Ai:CallTimeoutSeconds", 20);
        return new AiOptions(
            Enabled: c.GetValue("Ai:Enabled", true), Provider: (c["Ai:Provider"] ?? "mock").Trim().ToLowerInvariant(),
            CallTimeout: callTimeout, Budget: budget,
            Fast: Price("Fast", 100_000, 10_000, 500_000, "claude-haiku-5-5"),
            Balanced: Price("Balanced", 2_000_000, 100_000, 10_000_000, "claude-sonnet-5-5"),
            Summary: summary, CatchUp: catchUp, Breaker: breaker, MockFault: fault, MockLatency: TimeSpan.FromMilliseconds(c.GetValue("Ai:Mock:LatencyMs", 0)),
            Reaper: new Tether.Ai.Budget.ReaperOptions(Secs(c, "Ai:Reaper:IntervalSeconds", 60), Secs(c, "Ai:Reaper:GraceSeconds", 180)),
            // LanguageGuard (docs/ux/language-guide.md 5.2) is LOG-ONLY unless this is true; measure the rule hit rates before enforcing.
            LanguageGuardEnforce: c.GetValue("Ai:LanguageGuard:Enforce", false),
            NotesConfig: NotesFrom(c, callTimeout, summary.CompactionMaxChunks, budget));
    }

    /// <summary>NAS-02 defaults, as shares of the configured lecture caps so they scale with them: notes sub-cap 50% of the user-day cap; live floors 40% of the user-day and of the global month.</summary>
    public const double NotesShareOfDay = 0.5, LiveFloorShare = 0.4;

    private static NotesOptions NotesFrom(IConfiguration c, TimeSpan callTimeout, int finalChunks, BudgetOptions budget)
    {
        static long Share(long cap, double share) => (long)Math.Floor(cap * share);
        var d = NotesOptions.Default;
        var n = c.GetSection("Ai:Notes");
        var o = new NotesOptions(
            ChunkChars: n.GetValue("ChunkChars", d.ChunkChars), MaxChunks: n.GetValue("MaxChunks", d.MaxChunks), MaxSegments: n.GetValue("MaxSegments", d.MaxSegments),
            MaxOutputTokens: n.GetValue("MaxOutputTokens", d.MaxOutputTokens), Timeout: TimeSpan.FromSeconds(n.GetValue("TimeoutSeconds", d.Timeout.TotalSeconds)),
            Workers: n.GetValue("Workers", d.Workers), QueueCapacity: n.GetValue("QueueCapacity", d.QueueCapacity),
            StaleAfter: d.StaleAfter, PerMinutePerUser: n.GetValue("PerMinutePerUser", d.PerMinutePerUser),
            MaxAttemptsPerSession: n.GetValue("MaxAttemptsPerSession", d.MaxAttemptsPerSession), PerRequestMicro: n.GetValue("PerRequestMicroUsd", d.PerRequestMicro),
            PerUserDailyMicro: n.GetValue("PerUserDailyMicroUsd", Share(budget.PerUserDailyMicro, NotesShareOfDay)),
            LiveFloorPerUserDailyMicro: n.GetValue("LiveFloorPerUserDailyMicroUsd", Share(budget.PerUserDailyMicro, LiveFloorShare)),
            LiveFloorGlobalMonthlyMicro: n.GetValue("LiveFloorGlobalMonthlyMicroUsd", Share(budget.GlobalMonthlyMicro, LiveFloorShare)),
            BreakerConfig: new BreakerOptions(n.GetValue("Breaker:FailuresToOpen", d.Breaker.FailuresToOpen),
                TimeSpan.FromSeconds(n.GetValue("Breaker:OpenSeconds", d.Breaker.OpenFor.TotalSeconds)), d.Breaker.ProviderCapOpenFor),
            MaxQueueWaitConfig: TimeSpan.FromSeconds(n.GetValue("MaxQueueWaitSeconds", d.MaxQueueWait.TotalSeconds)),
            NoticeVersionsConfig: n.GetSection("NoticeVersions").Get<string[]>() is { Length: > 0 } nv ? nv : null,
            RetiredNoticeVersionsConfig: n.GetSection("RetiredNoticeVersions").Get<string[]>() is { Length: > 0 } rv ? rv : null);
        // NAS-03: unless set explicitly, the permission lasts the computed worst case of one started press plus a minute of slack.
        var stale = n["StaleSeconds"] is { } s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs)
            ? TimeSpan.FromSeconds(secs) : o.WorstCase(callTimeout, finalChunks) + TimeSpan.FromMinutes(1);
        return o with { StaleAfter = stale };
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
        var syl = options.Budget.For(Tether.SharedKernel.Ai.AiScopeKinds.Syllabus);
        if (options.Budget.PerSessionMicro <= 0 || options.Budget.PerUserDailyMicro <= 0 || options.Budget.GlobalMonthlyMicro <= 0
            || syl.PerScopeMicro <= 0 || syl.PerUserDailyMicro <= 0 || syl.GlobalMonthlyMicro <= 0 || options.Notes.PerRequestMicro <= 0)
            throw new InvalidOperationException("Ai:Budget caps must be positive. To switch AI off set Ai:Enabled=false.");

        // NAS-02: the notes sub-cap must be positive and the live floors must leave notes something without exceeding the caps they sit in.
        var n = options.Notes;
        if (n.PerUserDailyMicro <= 0 || n.LiveFloorPerUserDailyMicro < 0 || n.LiveFloorGlobalMonthlyMicro < 0
            || n.LiveFloorPerUserDailyMicro >= options.Budget.PerUserDailyMicro || n.LiveFloorGlobalMonthlyMicro >= options.Budget.GlobalMonthlyMicro)
            throw new InvalidOperationException("Ai:Notes:PerUserDailyMicroUsd must be positive and the Ai:Notes:LiveFloor* values must be at least 0 and below the lecture caps they reserve.");
        // NAS-R03: a per-press cap above the user's whole lecture day could never be spent; the press would always stop part way.
        if (n.PerRequestMicro > options.Budget.PerUserDailyMicro)
            throw new InvalidOperationException("Ai:Notes:PerRequestMicroUsd must not exceed Ai:Budget:PerUserDailyMicroUsd.");
        if (n.Breaker.FailuresToOpen < 1 || n.Breaker.OpenFor <= TimeSpan.Zero || n.MaxQueueWait <= TimeSpan.Zero)
            throw new InvalidOperationException("Ai:Notes:Breaker:* and Ai:Notes:MaxQueueWaitSeconds must be positive.");
        if (n.NoticeVersions.Any(v => !NoticeVersionFormat.IsValid(v)) || n.RetiredNoticeVersions.Any(v => !NoticeVersionFormat.IsValid(v)))
            throw new InvalidOperationException("Ai:Notes:NoticeVersions and Ai:Notes:RetiredNoticeVersions entries must be 1 to 64 of [A-Za-z0-9._-].");
        // NAS-R07: the allow-list carries ACTIVE ids only. A retired id (or the A007 backfill id) listed as active is a configuration error.
        if (n.NoticeVersions.Any(v => v == NotesOptions.UnrecordedNotice || n.RetiredNoticeVersions.Contains(v, StringComparer.Ordinal)))
            throw new InvalidOperationException("Ai:Notes:NoticeVersions must hold only active notice ids: remove retired ids (Ai:Notes:RetiredNoticeVersions) and 'none-recorded'.");
        // NAS-03: a permission shorter than the longest a started press can run would skip the final summary (or notes) without any sign.
        var worst = n.WorstCase(options.CallTimeout, options.Summary.CompactionMaxChunks);
        if (n.StaleAfter < worst)
            throw new InvalidOperationException($"Ai:Notes:StaleSeconds ({n.StaleAfter.TotalSeconds:0}) is below the worst case of one press ({worst.TotalSeconds:0} s = MaxChunks x 2 x TimeoutSeconds + CompactionMaxChunks x 2 x CallTimeoutSeconds). Raise it or leave it unset.");
    }
}

/// <summary>NAS-05: the shape of a notice id (same as the syllabus <c>noticeVersion</c>): 1 to 64 of <c>[A-Za-z0-9._-]</c>, nothing else.</summary>
internal static class NoticeVersionFormat
{
    public static bool IsValid(string? v) => v is { Length: >= 1 and <= 64 } && v.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-');
}
