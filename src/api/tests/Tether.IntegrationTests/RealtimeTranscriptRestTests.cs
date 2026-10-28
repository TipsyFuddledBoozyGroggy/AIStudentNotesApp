using System.Net;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>GET /sessions/{id}/transcript: all persisted events in seq order, paged, owner-scoped (API_CONTRACTS 13.1).</summary>
public class RealtimeTranscriptRestTests(RealtimeFactory f) : LectureTestBase(f), IClassFixture<RealtimeFactory>
{
    private async Task<(Actor A, string Id)> StreamedAsync()
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        var ticket = (await (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).BodyAsync()).GetProperty("ticket").GetString()!;
        await using var audio = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
        await audio.HelloAsync(ticket);
        await audio.SendAudioAsync(48);
        Assert.True(await Rt.WaitAsync(async () => await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}'") >= 5));
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        return (a, id);
    }

    [Fact]
    public async Task Pages_cover_every_persisted_event_once_in_order_including_session_state_and_corrections()
    {
        var (a, id) = await StreamedAsync(); // session.state x2, 4 finals, 1 correction = 7 events
        var seen = new List<string>();
        string after = "0";
        for (var guard = 0; guard < 10; guard++)
        {
            var page = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?afterSeq={after}&limit=3")).BodyAsync();
            ContractSchemas.AssertValid("transcript-page", page);
            var events = page.GetProperty("events").EnumerateArray().ToList();
            Assert.True(events.Count <= 3);
            seen.AddRange(events.Select(e => e.GetProperty("seq").GetString()!));
            if (!page.GetProperty("hasMore").GetBoolean()) break;
            Assert.Equal(3, events.Count);
            after = events[^1].GetProperty("seq").GetString()!;
        }
        Assert.Equal(Enumerable.Range(1, 7).Select(i => i.ToString()), seen);

        var types = (await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?limit=500")).BodyAsync())
            .GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()!).ToList();
        Assert.Contains("session.state", types);
        Assert.Contains("transcript.correction", types);
        Assert.Equal(2, types.Count(t => t == "session.state"));
    }

    [Fact]
    public async Task Default_limit_is_200_and_afterSeq_past_the_end_is_an_empty_last_page()
    {
        var (a, id) = await StreamedAsync();
        var all = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript")).BodyAsync();
        Assert.Equal(7, all.GetProperty("events").GetArrayLength());
        var past = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?afterSeq=999")).BodyAsync();
        ContractSchemas.AssertValid("transcript-page", past);
        Assert.Equal(0, past.GetProperty("events").GetArrayLength());
        Assert.False(past.GetProperty("hasMore").GetBoolean());
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    [InlineData("afterSeq=abc")]
    [InlineData("afterSeq=-1")]
    [InlineData("afterSeq=99999999999999999999")]
    public async Task Bad_paging_parameters_are_400(string query)
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?{query}")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Another_users_session_and_a_missing_one_both_return_the_same_404_and_anonymous_is_401()
    {
        var (_, id) = await StreamedAsync();
        var b = await NewActorAsync();
        var foreign = await b.Client.GetAsync($"/api/v1/sessions/{id}/transcript");
        var missing = await b.Client.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}/transcript");
        await foreign.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal((await foreign.BodyAsync()).GetProperty("title").GetString(), (await missing.BodyAsync()).GetProperty("title").GetString());
        await (await F.NewClient().GetAsync($"/api/v1/sessions/{id}/transcript")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Two_users_with_identical_segment_ids_and_seq_see_only_their_own_text()
    {
        var (a, aId) = await StreamedAsync();
        var (b, bId) = await StreamedAsync();
        var pa = await (await a.Client.GetAsync($"/api/v1/sessions/{aId}/transcript")).BodyAsync();
        var pb = await (await b.Client.GetAsync($"/api/v1/sessions/{bId}/transcript")).BodyAsync();
        Assert.Equal(pa.GetProperty("events").GetArrayLength(), pb.GetProperty("events").GetArrayLength()); // same seq and seg_n values, two owners
        Assert.All(pa.GetProperty("events").EnumerateArray(), e => Assert.Equal(aId, e.GetProperty("sessionId").GetString()));
        Assert.All(pb.GetProperty("events").EnumerateArray(), e => Assert.Equal(bId, e.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Deleting_the_session_removes_the_source_transcript_and_the_endpoint_then_404s()
    {
        var (a, id) = await StreamedAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}'"));
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Deleting_the_user_removes_their_source_transcripts_by_cascade_and_leaves_other_users_alone()
    {
        var (gone, goneId) = await StreamedAsync();
        var (_, keptId) = await StreamedAsync();
        await F.ExecAsync($"delete from identity.users where id = '{gone.UserId}'");
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{goneId}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.audio_progress where session_id = '{goneId}'"));
        Assert.True(await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{keptId}'") > 0);
    }

    [Fact]
    public async Task The_database_rejects_a_transcript_row_for_the_wrong_user_and_malformed_rows()
    {
        var (a, id) = await StreamedAsync();
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => F.ExecAsync(
            $"insert into transcript.transcript_events (session_id, seq, user_id, kind, from_ms, to_ms, gap_reason, occurred_at) values ('{id}', 9000, '{other}', 'Gap', 1, 2, 'provider', now())"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => F.ExecAsync(
            $"insert into transcript.transcript_events (session_id, seq, user_id, kind, from_ms, to_ms, gap_reason, occurred_at) values ('{id}', 9000, '{a.UserId}', 'Gap', 5, 2, 'provider', now())"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => F.ExecAsync(
            $"insert into transcript.transcript_events (session_id, seq, user_id, kind, occurred_at) values ('{id}', 9001, '{a.UserId}', 'Final', now())"));
    }
}
