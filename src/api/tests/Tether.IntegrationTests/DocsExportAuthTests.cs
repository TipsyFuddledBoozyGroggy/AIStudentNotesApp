using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>
/// DX-B08 (B36 to B39): the authorization boundary of the five docs routes. Every route has an ownership (IDOR) test with two users and overlapping
/// ids; a caller who does not own the session never sees 409, 412, 413, 415 or 429 caused by someone else's data (API_CONTRACTS_P2 2.1 order).
/// </summary>
[Collection(RacePool.Name)]
public class DocsExportAuthTests(DocsFactory factory) : DocsExportTestBase(factory), IClassFixture<DocsFactory>
{
    private static string Strip(JsonElement e) => StripTrace(e);

    [Fact]
    public async Task B36_Another_users_session_and_document_are_404_on_all_five_routes_and_look_like_a_missing_id()
    {
        var alice = await ReadyAsync();
        var rec = await ExportOnceAsync(alice);
        var bob = await NewActorAsync();
        var missingSession = Guid.NewGuid().ToString("D");
        var missingDoc = "ddc_" + Guid.NewGuid().ToString("D");

        async Task<JsonElement> Expect404(HttpResponseMessage r)
        {
            await r.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            var b = await r.BodyAsync();
            DocsSchemas.AssertValid("docs-problem", b);
            return b;
        }

        // preview and confirm: Alice's session and a missing one are the same 404, whatever the body is (no 400, 409, 412, 413, 415 first)
        var good = new { notesId = alice.NotesId };
        var p1 = await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{alice.SessionId}/docs-export/preview", good)));
        var p2 = await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{missingSession}/docs-export/preview", good)));
        Assert.Equal(Strip(p2), Strip(p1));
        foreach (var body in new object[] { good, "not json", new string('x', 5000), "{\"notesId\":\"nt_" + Guid.NewGuid().ToString("N") + "\"}" })
        {
            Assert.Equal(Strip(p1), Strip(await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{alice.SessionId}/docs-export/preview", body)))));
            var c1 = await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{alice.SessionId}/docs-export", body, NewKey())));
            var c2 = await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{missingSession}/docs-export", body, NewKey())));
            Assert.Equal(Strip(c2), Strip(c1));
        }
        // a body with the right shape and Alice's real digest, from Bob, on Alice's session
        var real = ConfirmBody(await PreviewOkAsync(alice.A.Client, alice.SessionId, alice.NotesId), "update");
        await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{alice.SessionId}/docs-export", real, NewKey())));
        // a wrong content type must not be told apart either (no 415 for a session that is not yours)
        var wrongType = Req(HttpMethod.Post, $"/sessions/{alice.SessionId}/docs-export", null, NewKey());
        wrongType.Content = new StringContent("{}", System.Text.Encoding.UTF8, "text/plain");
        await Expect404(await bob.Client.SendAsync(wrongType));

        var s1 = await Expect404(await bob.Client.GetAsync($"{Api}/sessions/{alice.SessionId}/docs-export"));
        var s2 = await Expect404(await bob.Client.GetAsync($"{Api}/sessions/{missingSession}/docs-export"));
        Assert.Equal(Strip(s2), Strip(s1));

        var d1 = await Expect404(await bob.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}"));
        var d2 = await Expect404(await bob.Client.GetAsync($"{Api}/demo-docs/{missingDoc}"));
        Assert.Equal(Strip(d2), Strip(d1));
        var x1 = await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(rec)}")));
        var x2 = await Expect404(await bob.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{missingDoc}")));
        Assert.Equal(Strip(x2), Strip(x1));

        // nothing of Alice's changed, and Bob has no row anywhere
        Assert.Equal(1, await DocRowsAsync("demo_docs", alice.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", alice.A.UserId));
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys", "audit_events" })
            Assert.Equal(0, await DocRowsAsync(t, bob.UserId));
        Assert.Equal(HttpStatusCode.OK, (await alice.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).StatusCode);
    }

    [Fact]
    public async Task B36_Overlapping_ids_Bob_cannot_replay_Alices_key_and_her_document_id_in_his_own_confirm_reveals_nothing()
    {
        var alice = await ReadyAsync();
        var aliceKey = NewKey();
        var rec = await ExportOnceAsync(alice, aliceKey);
        var bob = await ReadyAsync();
        var bp = await PreviewOkAsync(bob.A.Client, bob.SessionId, bob.NotesId);

        // Bob sends Alice's key and Alice's document id on his OWN lecture: he gets his own answers, never hers
        var withHerDoc = ConfirmBody(bp, "update");
        withHerDoc["expectedExistingVersion"] = 1;
        withHerDoc["expectedExistingDocId"] = DocIdOf(rec);
        var r1 = await ConfirmAsync(bob.A.Client, bob.SessionId, withHerDoc, aliceKey);
        var r2 = await ConfirmAsync(bob.A.Client, bob.SessionId, withHerDoc.with2(DocIdOf(rec), "ddc_" + Guid.NewGuid().ToString("D")), NewKey());
        await r1.AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        await r2.AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        Assert.Equal(Strip(await r2.BodyAsync()), Strip(await r1.BodyAsync()));
        Assert.Equal(0, await DocRowsAsync("idempotency_keys", bob.A.UserId));

        // Bob's own create with Alice's key is a first write of his own, not a replay of hers
        var mine = await ConfirmAsync(bob.A.Client, bob.SessionId, ConfirmBody(bp, "create"), aliceKey);
        Assert.False(Replayed(mine));
        var bobRec = await OkRecordAsync(mine);
        Assert.NotEqual(rec.GetProperty("exportId").GetString(), bobRec.GetProperty("exportId").GetString());
        Assert.NotEqual(DocIdOf(rec), DocIdOf(bobRec));

        // and Bob cannot read, delete or see Alice's notes id as his own
        await (await bob.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await PreviewAsync(bob.A.Client, bob.SessionId, alice.NotesId)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.OK, (await alice.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).StatusCode);
    }

    [Fact]
    public async Task B37_A_missing_or_foreign_Origin_on_POST_and_DELETE_is_403_csrf_failed_and_a_GET_needs_none()
    {
        var l = await ReadyAsync();
        var rec = await ExportOnceAsync(l);
        var cookie = l.A.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        foreach (var origin in new string?[] { null, "https://evil.example", "null" })
        {
            var c = F.NewClient(withOrigin: false);
            c.DefaultRequestHeaders.Add("Cookie", cookie);
            if (origin is not null) c.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
            await (await c.SendAsync(Req(HttpMethod.Post, $"/sessions/{l.SessionId}/docs-export/preview", new { notesId = l.NotesId }))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
            await (await c.SendAsync(Req(HttpMethod.Post, $"/sessions/{l.SessionId}/docs-export", new { }, NewKey()))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
            await (await c.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(rec)}"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        }
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        var noOrigin = F.NewClient(withOrigin: false);
        noOrigin.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await noOrigin.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await noOrigin.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).StatusCode);
    }

    [Fact]
    public async Task B38_Unauthenticated_requests_to_all_five_routes_are_401()
    {
        var anon = F.NewClient();
        var sid = Guid.NewGuid().ToString("D");
        var doc = "ddc_" + Guid.NewGuid().ToString("D");
        await (await anon.SendAsync(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export/preview", new { notesId = "nt_" + new string('a', 32) }))).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await anon.SendAsync(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export", new { }, NewKey()))).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await anon.GetAsync($"{Api}/sessions/{sid}/docs-export")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await anon.GetAsync($"{Api}/demo-docs/{doc}")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await anon.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{doc}"))).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task B39_Non_canonical_ids_are_400_before_any_lookup_for_the_Demo_Doc_routes_and_the_Idempotency_Key_is_checked_for_form()
    {
        var l = await ReadyAsync();
        var rec = await ExportOnceAsync(l);
        var id = DocIdOf(rec);
        var guidPart = id[4..];
        foreach (var bad in new[] { "DDC_" + guidPart, "ddc_" + guidPart.ToUpperInvariant(), "ddc_{" + guidPart + "}", "ddc_" + guidPart.Replace("-", ""), "ses_" + guidPart, guidPart, "ddc_" + guidPart + "x", "ddc_%2e%2e" })
        {
            await (await l.A.Client.GetAsync($"{Api}/demo-docs/{bad}")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            await (await l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{bad}"))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        }
        Assert.Equal(HttpStatusCode.OK, (await l.A.Client.GetAsync($"{Api}/demo-docs/{id}")).StatusCode);   // control: the canonical form works

        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        foreach (var badKey in new[] { "short", new string('k', 129), "has space in it", "key\twith/tab/xx" })
            await (await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "update"), badKey)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var noKey = Req(HttpMethod.Post, $"/sessions/{l.SessionId}/docs-export", ConfirmBody(p, "update"));
        await (await l.A.Client.SendAsync(noKey)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }
}

internal static class DocsAuthTestExtensions
{
    /// <summary>A copy of a confirm body with the expected document id replaced (so the same shape is sent for a real and for a random id).</summary>
    public static Dictionary<string, object?> with2(this Dictionary<string, object?> body, string from, string to)
    {
        var copy = new Dictionary<string, object?>(body);
        if (copy["expectedExistingDocId"] as string == from) copy["expectedExistingDocId"] = to;
        return copy;
    }
}
