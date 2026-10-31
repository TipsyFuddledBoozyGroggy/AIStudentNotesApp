using Tether.Ai.Provider;

namespace Tether.Ai.Budget;

internal sealed record BudgetDecision(bool Allowed, AiTier? ForceTier, string? Reason);

/// <summary>Cost estimate from token usage and configured prices. Prices are UNVERIFIED placeholders (gate G2).</summary>
internal static class Pricing
{
    /// <summary>Micro-USD, rounded up so any non-zero usage costs at least 1 and the ledger never under-counts.</summary>
    public static long CostMicro(AiTokenUsage u, TierPrice p)
    {
        var numerator = (long)u.InputTokens * p.InputMicroPerMTok + (long)u.CachedTokens * p.CachedInputMicroPerMTok + (long)u.OutputTokens * p.OutputMicroPerMTok;
        return (numerator + 999_999) / 1_000_000;
    }
}

/// <summary>
/// The degrade order from docs/ai/live-lecture-ai-design.md section 10, as thresholds on the highest cap pressure of the session,
/// the user's day and the global month. As pressure rises: summary interval doubles, consolidation stops, everything moves to the
/// cheap tier, the rolling TL;DR stops (leaving headroom), and only at the cap does user-initiated Quick recap fall back to the
/// extractive view. Clarification suggestions (dropped before Haiku-only in the design) do not exist yet. Pure.
/// </summary>
internal static class BudgetMath
{
    public static double Pressure(long spent, long cap) => cap <= 0 ? 1.0 : (double)spent / cap;

    public static BudgetDecision Decide(AiWorkflow w, double pressure, bool wouldExceed, BudgetOptions o)
    {
        if (wouldExceed) return new BudgetDecision(false, null, "budget_limited");
        var allowed = w switch
        {
            AiWorkflow.Compaction => pressure < o.DropCompactionAt,
            AiWorkflow.RollingSummary => pressure < o.StopSummaryAt,
            _ => pressure < 1.0,
        };
        if (!allowed) return new BudgetDecision(false, null, "budget_limited");
        return new BudgetDecision(true, pressure >= o.FastOnlyAt ? AiTier.Fast : null, null);
    }

    public static double IntervalMultiplier(double pressure, BudgetOptions o) => pressure >= o.SlowdownAt ? 2.0 : 1.0;

    public static bool IsNotesWorkflow(AiWorkflow w) => w is AiWorkflow.Notes or AiWorkflow.FinalSummary;

    /// <summary>
    /// NAS-02: the notes lane's own limits inside the lecture pool, on top of the ordinary pool caps. A notes call is refused when it would take
    /// (a) the user's notes spend today over the notes sub-cap, (b) the user's whole lecture spend today into the live floor (the last
    /// <see cref="NotesOptions.LiveFloorPerUserDailyMicro"/> of the daily cap, kept for live TL;DR and Quick recap), or (c) the global month
    /// into its live floor. Live workflows are never checked against these, so old-lecture presses cannot starve a live lecture. Pure.
    /// </summary>
    public static bool NotesWouldExceed(long notesToday, long lectureToday, long lectureMonth, long estimateMicro, BudgetOptions b, NotesOptions n) =>
        notesToday + estimateMicro > n.PerUserDailyMicro
        || lectureToday + estimateMicro > b.PerUserDailyMicro - n.LiveFloorPerUserDailyMicro
        || lectureMonth + estimateMicro > b.GlobalMonthlyMicro - n.LiveFloorGlobalMonthlyMicro;
}
