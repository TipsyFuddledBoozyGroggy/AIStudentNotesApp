namespace Tether.Ai.CatchUp;

internal static class CatchUpWindow
{
    /// <summary>
    /// The window ends at the newest final segment and reaches back <paramref name="windowMinutes"/> on the SESSION clock (paused time
    /// excluded, because timecodes already exclude it). Never starts before zero.
    /// </summary>
    public static (long FromMs, long ToMs) Bounds(long latestEndMs, int windowMinutes) =>
        (Math.Max(0, latestEndMs - windowMinutes * 60_000L), latestEndMs);

    public static bool IsAllowed(int windowMinutes) => windowMinutes is 2 or 5 or 10;
}
