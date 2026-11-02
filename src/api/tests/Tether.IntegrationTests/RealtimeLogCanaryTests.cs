using System.Net;
using System.Text;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.IntegrationTests;

/// <summary>
/// C-23 / F-08 for the realtime path. Trace level on every category and every sink, a sentinel in the transcript text, a
/// recognisable audio byte pattern, a failing database row that echoes the sentinel, and a hub client. None of transcript text,
/// audio bytes (raw, base64 or hex), tickets or cookies may appear in any log line, and error responses stay in the fixed vocabulary.
/// </summary>
public class RealtimeLogCanaryTests(RealtimeLogCanaryTests.Factory f) : LectureTestBase(f), IClassFixture<RealtimeLogCanaryTests.Factory>
{
    public const string TextSentinel = "CANARY-9b1c-transcript-text";
    public const string CorrectionSentinel = "CANARY-9b1c-corrected-text";
    private const string AudioPattern = "CANARYAUDIO";

    public sealed class Factory : RealtimeFactory
    {
        public LogCanaryTests.CapturingProvider Logs { get; } = new();

        protected override IEnumerable<KeyValuePair<string, string?>> Extra =>
        [
            new("ConnectionStrings:Default", ConnectionString + ";Include Error Detail=true"),
            new("Logging:LogLevel:Default", "Trace"),
            new("Logging:LogLevel:Microsoft", "Trace"),
            new("Logging:LogLevel:Npgsql", "Trace"),
            new("Speech:Replay:Script", $$"""[{"startMs":0,"endMs":2500,"text":"{{TextSentinel}}","correction":"{{CorrectionSentinel}}"},{"startMs":3000,"endMs":5500,"text":"{{TextSentinel}} two"}]"""),
        ];

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    private static byte[] CanaryFrame(uint seq)
    {
        var frame = Rt.AudioClient.Frame(seq);
        var pattern = Encoding.ASCII.GetBytes(AudioPattern);
        for (var i = 12; i < frame.Length; i++) frame[i] = pattern[(i - 12) % pattern.Length];
        return frame;
    }

    private static IEnumerable<string> Forms(string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        yield return s;
        yield return Convert.ToBase64String(bytes);
        yield return Convert.ToHexString(bytes);
        yield return Convert.ToHexString(bytes).ToLowerInvariant();
        yield return string.Join(",", bytes);
    }

    [Fact]
    public async Task Transcript_text_audio_bytes_tickets_and_cookies_never_appear_in_any_log_even_when_a_row_write_fails()
    {
        var a = await NewActorAsync();
        var cookie = Rt.CookieOf(a.Client);
        var cookieValue = cookie.Split('=', 2)[1];
        var course = await CourseAsync(a.Client);
        var id = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        var ticket = (await (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).BodyAsync()).GetProperty("ticket").GetString()!;

        await using var hub = await Rt.HubProbe.StartAsync(F, cookie);
        await hub.JoinAsync(id);
        await using (var audio = await Rt.AudioClient.ConnectAsync(F, cookie, id))
        {
            await audio.HelloAsync(ticket);
            for (uint i = 0; i < 24; i++) { await audio.SendBinaryAsync(CanaryFrame(i)); await Task.Delay(1); }
            Assert.True(await Rt.WaitAsync(() => hub.Of("transcript.final").Count == 2 && hub.Of("transcript.correction").Count == 1),
                "the healthy path must actually carry the sentinel to the client");
            Assert.Contains(hub.Of("transcript.final"), e => e.GetProperty("payload").GetProperty("text").GetString() == TextSentinel);
        }
        var restPage = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript")).BodyAsync();
        Assert.Contains(TextSentinel, restPage.GetRawText()); // proof the sentinel is real data that could have leaked

        // Failure path: every later INSERT of the sentinel text fails with a PostgresException whose Detail echoes the failing row.
        await PostAsync(a.Client, $"/sessions/{id}/stop");
        var id2 = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        await F.ExecAsync($"alter table transcript.transcript_events add constraint ck_canary check (coalesce(text,'') not like '%{TextSentinel}%') not valid");
        var ticket2 = (await (await PostAsync(a.Client, $"/sessions/{id2}/audio-ticket")).BodyAsync()).GetProperty("ticket").GetString()!;
        await using (var audio2 = await Rt.AudioClient.ConnectAsync(F, cookie, id2))
        {
            await audio2.HelloAsync(ticket2);
            // Exactly 10 frames of 250 ms: frame 9 ends at 2500 ms, the point where the replay emits the failing final. The server
            // cannot fail the pump (and close) before frame 9 arrives, and nothing is sent after it. Sending more frames raced that
            // expected close: when the close won, the client's send hit a disposed TestWebSocket (ObjectDisposedException, seen
            // intermittently under full-suite load, captured 2026-11-01).
            for (uint i = 0; i < 10; i++) { await audio2.SendBinaryAsync(CanaryFrame(i)); await Task.Delay(1); }
            Assert.Equal(1011, await audio2.WaitCloseAsync()); // the pump failed and closed the socket
        }

        // Error responses: fixed vocabulary, no input echo.
        var bad = await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?afterSeq={TextSentinel}");
        await bad.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.DoesNotContain(TextSentinel, await bad.Content.ReadAsStringAsync());
        var missing = await a.Client.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}/transcript");
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        var hubError = await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => hub.JoinAsync(Guid.NewGuid().ToString()));
        Assert.DoesNotContain(TextSentinel, hubError.Message);

        var lines = f.Logs.Lines.ToArray();
        Assert.True(lines.Length > 200, $"Trace logging must be on for this test to mean anything (saw {lines.Length} lines)");
        Assert.Contains(lines, l => l.Contains("Transcript pump failed", StringComparison.Ordinal)); // the failure was logged, by type only
        foreach (var secret in new[] { TextSentinel, CorrectionSentinel, "ordinal-text" }.Where(s => s != "ordinal-text").Concat([ticket, ticket2, cookieValue]))
            Assert.DoesNotContain(lines, l => l.Contains(secret, StringComparison.Ordinal));
        foreach (var form in Forms(AudioPattern))
            Assert.DoesNotContain(lines, l => l.Contains(form, StringComparison.Ordinal));
    }
}
