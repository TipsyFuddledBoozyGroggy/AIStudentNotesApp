using Tether.Ai.CatchUp;

namespace Tether.AiTests;

public class CatchUpLimiterTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Idle_sessions_are_pruned_so_the_map_does_not_grow_with_every_session_ever_used()
    {
        var clock = new Clock();
        var limiter = new CatchUpLimiter(T.Options(), clock);
        for (var i = 0; i < 500; i++) Assert.True(limiter.TryAcquire(Guid.NewGuid()));
        Assert.Equal(500, limiter.TrackedSessions);
        clock.Now = clock.Now.AddMinutes(2);
        Assert.True(limiter.TryAcquire(Guid.NewGuid()));
        Assert.Equal(1, limiter.TrackedSessions);
    }

    [Fact]
    public void Release_gives_back_the_newest_token_and_the_limit_still_holds()
    {
        var limiter = new CatchUpLimiter(T.Options(), new Clock());
        var s = Guid.NewGuid();
        for (var i = 0; i < 6; i++) Assert.True(limiter.TryAcquire(s));
        Assert.False(limiter.TryAcquire(s));
        limiter.Release(s);
        Assert.True(limiter.TryAcquire(s));
        Assert.False(limiter.TryAcquire(s));
    }
}
