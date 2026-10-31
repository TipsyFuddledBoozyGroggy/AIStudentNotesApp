using System.Text;
using Tether.Syllabus.Application;

namespace Tether.SyllabusTests;

/// <summary>Unit tests for the fixes of docs/security/syllabus-built-review.md (SB-04, SB-08, SB-09). Integration counterparts are in Tether.IntegrationTests/Syllabus/SyllabusBuiltReviewFixTests.cs.</summary>
public class BuiltReviewFixTests
{
    // ---------------------------------------------------------------- SB-04 metadata fingerprint

    private static CreateSyllabusRequest Meta(string? title = "BIO", string? ts = "2026-08-24", string? te = "2026-12-11", bool accepted = true, bool dup = false) => new()
    {
        Title = title, TermStart = ts, TermEnd = te, DefaultTimezone = "America/New_York", AllowDuplicate = dup, NoticeVersion = "draft-2026-10",
        Attestation = new AttestationDto { Accepted = accepted, AcceptedAt = "2026-10-31T12:00:00Z" },
    };

    [Fact]
    public void Metadata_canonical_form_has_no_separator_collision_and_keeps_null_empty_and_accepted_distinct()
    {
        static string C(CreateSyllabusRequest m) => UploadIntake.MetadataCanonical(m);
        // the review's collision: a "|" in the title against a "|" in a term value
        Assert.NotEqual(C(Meta(title: "X|ts=2026-08-24", ts: null, te: null)), C(Meta(title: "X", ts: "2026-08-24|ts=", te: null)));
        Assert.NotEqual(C(Meta(title: null)), C(Meta(title: "")));
        Assert.NotEqual(C(Meta(ts: null, te: null)), C(Meta(ts: "", te: "")));
        Assert.NotEqual(C(Meta(accepted: true)), C(Meta(accepted: false)));
        Assert.NotEqual(C(Meta(dup: true)), C(Meta(dup: false)));
        Assert.Equal(C(Meta()), C(Meta()));                                            // deterministic
    }

    // ---------------------------------------------------------------- SB-08 file decoder

    public static TheoryData<string, byte[]> RejectedFiles => new()
    {
        { "UTF-16LE BOM with a lone high surrogate", [0xFF, 0xFE, (byte)'A', 0, 0x00, 0xD8, (byte)'B', 0] },
        { "UTF-16LE BOM with a lone low surrogate", [0xFF, 0xFE, (byte)'A', 0, 0x00, 0xDC, (byte)'B', 0] },
        { "UTF-16LE BOM with a reversed pair", [0xFF, 0xFE, 0x00, 0xDC, 0x00, 0xD8] },
        { "UTF-16BE BOM with a lone high surrogate", [0xFE, 0xFF, 0, (byte)'A', 0xD8, 0x00, 0, (byte)'B'] },
        { "UTF-16LE BOM with an odd byte length", [0xFF, 0xFE, (byte)'A', 0, (byte)'B'] },
        { "UTF-16BE BOM with an odd byte length", [0xFE, 0xFF, 0, (byte)'A', 0] },
        { "CESU-8 encoded astral character", [(byte)'Q', 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80, (byte)'z'] },
        { "UTF-8 encoded lone surrogate", [(byte)'Q', 0xED, 0xA0, 0x80, (byte)'z'] },
        { "UTF-8 BOM then invalid bytes", [0xEF, 0xBB, 0xBF, (byte)'a', 0xC0, 0xAF] },
        { "UTF-16LE BOM with a NUL character", [0xFF, 0xFE, (byte)'A', 0, 0, 0, (byte)'B', 0] },
        { "UTF-32LE BOM (not supported, its zero bytes decode as NUL)", [0xFF, 0xFE, 0, 0, (byte)'A', 0, 0, 0] },
        { "UTF-32BE BOM (not supported)", [0, 0, 0xFE, 0xFF, 0, 0, 0, (byte)'A'] },
        { "BOM only", [0xFF, 0xFE] },
    };

    [Theory]
    [MemberData(nameof(RejectedFiles))]
    public void Malformed_text_files_are_refused_by_the_decoder_and_never_throw(string name, byte[] bytes)
    {
        Assert.False(UploadIntake.TryDecodeText(bytes, out var text), name);
        Assert.Equal("", text);
    }

    [Fact]
    public void Well_formed_utf16be_utf16le_and_utf8_files_decode_to_the_same_text_including_astral_characters()
    {
        const string s = "Quiz due Oct 20, 2026 \U0001F600";
        byte[] be = [0xFE, 0xFF, .. new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetBytes(s)];
        byte[] le = [0xFF, 0xFE, .. new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes(s)];
        byte[] utf8Bom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(s)];
        foreach (var b in new[] { be, le, Encoding.UTF8.GetBytes(s), utf8Bom })
        {
            Assert.True(UploadIntake.TryDecodeText(b, out var t));
            Assert.Equal(s, t);
        }
    }

    // ---------------------------------------------------------------- SB-09 the create gate (uploads and pastes)

    private static UploadGate Gate(long inFlight = 100L * 1024 * 1024, int waitMs = 2000) =>
        new(new SyllabusOptions { BytesInFlight = inFlight, CreateGateWaitMilliseconds = waitMs });

    [Fact]
    public async Task One_create_per_user_a_different_key_is_refused_at_once_and_another_user_is_not_affected()
    {
        var gate = Gate(); var u = Guid.NewGuid();
        Assert.True(await gate.EnterAsync(u, "key-one-1", 1024 * 1024, CancellationToken.None));
        var started = DateTime.UtcNow;
        Assert.False(await gate.EnterAsync(u, "key-two-2", 1024 * 1024, CancellationToken.None));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(500), "a different create is refused without waiting");
        Assert.True(await gate.EnterAsync(Guid.NewGuid(), "key-two-2", 1024 * 1024, CancellationToken.None));
        gate.Leave(u);
        Assert.True(await gate.EnterAsync(u, "key-two-2", 1024 * 1024, CancellationToken.None));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_waits_for_the_first_and_then_enters()
    {
        var gate = Gate(); var u = Guid.NewGuid();
        Assert.True(await gate.EnterAsync(u, "same-key-1", 1024, CancellationToken.None));
        var retry = gate.EnterAsync(u, "same-key-1", 1024, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(retry.IsCompleted, "the retry waits while the first create holds the slot");
        gate.Leave(u);
        Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(5)));
        gate.Leave(u);
    }

    [Fact]
    public async Task A_waiting_retry_gives_up_after_the_wait_limit_and_waiters_are_capped()
    {
        var gate = Gate(waitMs: 150); var u = Guid.NewGuid();
        Assert.True(await gate.EnterAsync(u, "same-key-1", 1024, CancellationToken.None));
        Assert.False(await gate.EnterAsync(u, "same-key-1", 1024, CancellationToken.None));                   // timed out: 429
        var waiters = Enumerable.Range(0, UploadGate.MaxWaitersPerUser + 3).Select(_ => gate.EnterAsync(u, "same-key-1", 1024, CancellationToken.None)).ToArray();
        var refusedAtOnce = waiters.Count(w => w.IsCompleted && !w.Result);
        Assert.True(refusedAtOnce >= 3, $"waiters beyond the cap are refused at once (saw {refusedAtOnce})");
        Assert.All(await Task.WhenAll(waiters), ok => Assert.False(ok));
        gate.Leave(u);
    }

    [Fact]
    public async Task The_bytes_in_flight_budget_counts_pastes_and_uploads_together_and_is_released()
    {
        var gate = Gate(inFlight: 12L * 1024 * 1024);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Assert.True(await gate.EnterAsync(a, "upload-key-1", 10L * 1024 * 1024, CancellationToken.None));     // an upload reserves its 10 MB cap
        Assert.True(await gate.EnterAsync(b, "paste-key-01", 1024 * 1024, CancellationToken.None));           // a paste its 1 MB cap
        Assert.False(await gate.EnterAsync(c, "paste-key-02", 2L * 1024 * 1024, CancellationToken.None));     // over budget
        gate.Leave(a);
        Assert.True(await gate.EnterAsync(c, "paste-key-02", 2L * 1024 * 1024, CancellationToken.None));
        gate.Leave(b); gate.Leave(c);
        gate.Leave(c);                                                                                          // a double leave is harmless
        Assert.True(await gate.EnterAsync(a, "upload-key-1", 12L * 1024 * 1024, CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_waiter_leaves_the_queue()
    {
        var gate = Gate(waitMs: 5000); var u = Guid.NewGuid();
        Assert.True(await gate.EnterAsync(u, "same-key-1", 1024, CancellationToken.None));
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnterAsync(u, "same-key-1", 1024, cts.Token));
        gate.Leave(u);
        Assert.True(await gate.EnterAsync(u, "other-key-1", 1024, CancellationToken.None));
    }
}
