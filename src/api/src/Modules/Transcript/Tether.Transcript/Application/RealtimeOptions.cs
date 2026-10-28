using Microsoft.Extensions.Configuration;

namespace Tether.Transcript.Application;

/// <summary>
/// Realtime limits. Values are the contract defaults or the reviewer proposals (section 10), configurable, not measured.
/// Keys: Realtime:*.
/// </summary>
internal sealed record RealtimeOptions(
    TimeSpan HubRevalidate, TimeSpan RelayCheck, TimeSpan MaxHubConnectionAge, int MaxHubConnectionsPerUser, int MaxSocketsPerUser,
    int ReplayCap, int JoinPermitPerMinute, TimeSpan HelloTimeout, int UpgradePermitPerMinute, int FailedHelloPerMinute,
    int ChannelFrames, TimeSpan Backpressure, double MaxRateFactor, int EventsPerSecond, TimeSpan SilenceGap, double RateBurstSeconds)
{
    public const int MaxHelloBytes = 1024;
    public const int MaxTextBytes = 4096;
    public const int SampleRate = 16000;
    public const int BytesPerMs = SampleRate * 2 / 1000; // pcm_s16le mono
    public const int HeaderBytes = 12;
    public const int MaxFramePcmBytes = 250 * BytesPerMs; // 250 ms
    public const int MaxFrameBytes = HeaderBytes + MaxFramePcmBytes;

    public static RealtimeOptions From(IConfiguration c) => new(
        HubRevalidate: TimeSpan.FromSeconds(Math.Max(1, c.GetValue("Realtime:RevalidateSeconds", 60))),
        RelayCheck: TimeSpan.FromSeconds(Math.Max(1, c.GetValue("Realtime:RelayCheckSeconds", 5))),
        MaxHubConnectionAge: TimeSpan.FromSeconds(c.GetValue("Realtime:MaxConnectionSeconds", 3600)),
        MaxHubConnectionsPerUser: c.GetValue("Realtime:MaxHubConnectionsPerUser", 3),
        MaxSocketsPerUser: c.GetValue("Realtime:MaxAudioSocketsPerUser", 1),
        ReplayCap: c.GetValue("Realtime:ReplayCap", 500),
        JoinPermitPerMinute: c.GetValue("Realtime:JoinPermitPerMinute", 10),
        HelloTimeout: TimeSpan.FromMilliseconds(c.GetValue("Realtime:HelloTimeoutMs", 5000)),
        UpgradePermitPerMinute: c.GetValue("Realtime:UpgradePermitPerMinute", 10),
        FailedHelloPerMinute: c.GetValue("Realtime:FailedHelloPerMinute", 10),
        ChannelFrames: c.GetValue("Realtime:ChannelFrames", 20),
        Backpressure: TimeSpan.FromMilliseconds(c.GetValue("Realtime:BackpressureMs", 1000)),
        MaxRateFactor: c.GetValue("Realtime:MaxRateFactor", 1.1),
        EventsPerSecond: c.GetValue("Realtime:EventsPerSecond", 50),
        SilenceGap: TimeSpan.FromSeconds(c.GetValue("Realtime:SilenceGapSeconds", 60)),
        RateBurstSeconds: c.GetValue("Realtime:RateBurstSeconds", 5.0));
}
