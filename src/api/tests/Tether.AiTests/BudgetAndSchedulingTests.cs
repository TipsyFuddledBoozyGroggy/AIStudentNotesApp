using Tether.Ai.Budget;
using Tether.Ai.CatchUp;
using Tether.Ai.Provider;
using Tether.Ai.Summary;

namespace Tether.AiTests;

public class PricingTests
{
    private static readonly TierPrice P = new(100_000, 10_000, 500_000, "m");

    [Fact]
    public void Cost_is_tokens_times_configured_price_rounded_up_and_never_zero_for_nonzero_usage()
    {
        // 1M in at 100k micro/MTok = 100_000; 1M out at 500k = 500_000
        Assert.Equal(600_000, Pricing.CostMicro(new AiTokenUsage(1_000_000, 0, 1_000_000), P));
        Assert.Equal(1, Pricing.CostMicro(new AiTokenUsage(1, 0, 0), P));
        Assert.Equal(0, Pricing.CostMicro(new AiTokenUsage(0, 0, 0), P));
        Assert.Equal(10_000, Pricing.CostMicro(new AiTokenUsage(0, 1_000_000, 0), P)); // cached input uses its own price
    }
}

public class BudgetMathTests
{
    private static readonly BudgetOptions B = T.Options().Budget;

    [Fact]
    public void Pressure_is_spent_over_cap_and_a_zero_cap_is_fully_pressured()
    {
        Assert.Equal(0.5, BudgetMath.Pressure(50, 100), 3);
        Assert.Equal(1.0, BudgetMath.Pressure(0, 0));
    }

    [Theory]
    [InlineData("Compaction", 0.74, true)]
    [InlineData("Compaction", 0.75, false)]   // consolidation is dropped first
    [InlineData("RollingSummary", 0.89, true)]
    [InlineData("RollingSummary", 0.90, false)] // then the rolling TL;DR
    [InlineData("LostTrack", 0.99, true)]
    [InlineData("LostTrack", 1.00, false)]     // user-initiated catch-up is last to go
    public void Degrade_order_is_compaction_then_tldr_then_catch_up(string workflow, double pressure, bool allowed)
    {
        Assert.Equal(allowed, BudgetMath.Decide(Enum.Parse<AiWorkflow>(workflow), pressure, wouldExceed: false, B).Allowed);
    }

    [Fact]
    public void A_call_that_would_cross_a_cap_is_denied_whatever_the_workflow()
    {
        Assert.False(BudgetMath.Decide(AiWorkflow.LostTrack, 0.1, wouldExceed: true, B).Allowed);
    }

    [Fact]
    public void Haiku_only_and_slowdown_kick_in_before_anything_is_stopped()
    {
        Assert.Null(BudgetMath.Decide(AiWorkflow.LostTrack, 0.5, false, B).ForceTier);
        Assert.Equal(AiTier.Fast, BudgetMath.Decide(AiWorkflow.LostTrack, 0.86, false, B).ForceTier);
        Assert.Equal(1.0, BudgetMath.IntervalMultiplier(0.59, B));
        Assert.Equal(2.0, BudgetMath.IntervalMultiplier(0.60, B));
    }
}

public class FlushPolicyTests
{
    private static readonly SummaryOptions O = T.Options().Summary;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Defaults_are_8_segments_90_seconds_and_a_45_second_minimum_interval()
    {
        Assert.Equal(8, O.FlushSegments);
        Assert.Equal(TimeSpan.FromSeconds(90), O.FlushAfter);
        Assert.Equal(TimeSpan.FromSeconds(45), O.MinInterval);
    }

    [Theory]
    [InlineData(7, 89, 1000, false)]  // neither threshold
    [InlineData(8, 10, 1000, true)]   // 8 segments
    [InlineData(2, 90, 1000, true)]   // 90 s
    [InlineData(8, 10, 44, false)]    // thresholds met but min interval not elapsed
    [InlineData(8, 10, 45, true)]
    [InlineData(0, 500, 1000, false)] // nothing pending
    public void Flush_at_8_segments_or_90_seconds_but_never_inside_the_minimum_interval(int pending, int ageSeconds, int sinceLastRunSeconds, bool due)
    {
        Assert.Equal(due, FlushPolicy.IsDue(pending, Now.AddSeconds(-ageSeconds), Now.AddSeconds(-sinceLastRunSeconds), Now, O));
    }

    [Fact]
    public void Budget_slowdown_doubles_the_minimum_interval()
    {
        Assert.False(FlushPolicy.IsDue(8, Now.AddSeconds(-10), Now.AddSeconds(-60), Now, O, intervalMultiplier: 2));
        Assert.True(FlushPolicy.IsDue(8, Now.AddSeconds(-10), Now.AddSeconds(-90), Now, O, intervalMultiplier: 2));
    }
}

public class CatchUpWindowTests
{
    [Theory]
    [InlineData(600_000, 2, 480_000, 600_000)]
    [InlineData(600_000, 5, 300_000, 600_000)]
    [InlineData(600_000, 10, 0, 600_000)]
    [InlineData(90_000, 10, 0, 90_000)]   // shorter than the window
    [InlineData(0, 5, 0, 0)]
    public void The_window_ends_at_the_newest_segment_and_never_starts_before_zero(long latest, int minutes, long from, long to)
    {
        Assert.Equal((from, to), CatchUpWindow.Bounds(latest, minutes));
    }
}
