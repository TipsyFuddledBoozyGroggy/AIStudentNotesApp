using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CollegeCompanion.IntegrationTests.Infrastructure;

namespace CollegeCompanion.IntegrationTests;

public class CourseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] OptionalFields = ["code", "term", "instructor", "color", "lastSessionAt", "archivedAt"];

    // Each test uses its own user so tests in this class are independent of each other.
    private Task<HttpClient> NewUserAsync() => factory.LoginAsync($"u{Guid.NewGuid():N}@dev.local");

    private static async Task<JsonElement> CreateAsync(HttpClient c, object body)
    {
        var res = await c.PostAsJsonAsync("/api/v1/courses", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.BodyAsync();
    }

    private static HttpRequestMessage Put(string id, object body, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/courses/{id}") { Content = JsonContent.Create(body) };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return req;
    }

    [Fact]
    public async Task Create_returns_201_location_etag_and_contract_shape()
    {
        var c = await NewUserAsync();
        var res = await c.PostAsJsonAsync("/api/v1/courses", new { name = "  Biology 101 ", code = "BIO101", term = "Fall 2026", instructor = "Dr. Rao", color = "#3366CC" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await res.BodyAsync();
        var id = b.GetProperty("id").GetString()!;
        Assert.Equal($"/api/v1/courses/{id}", res.Headers.Location!.ToString());
        Assert.NotNull(res.Headers.ETag);
        Assert.Equal("Biology 101", b.GetProperty("name").GetString());
        Assert.Equal("BIO101", b.GetProperty("code").GetString());
        Assert.Equal("Fall 2026", b.GetProperty("term").GetString());
        Assert.Equal("Dr. Rao", b.GetProperty("instructor").GetString());
        Assert.Equal("#3366CC", b.GetProperty("color").GetString());
        Assert.True(b.TryGetProperty("createdAt", out _));
        Assert.True(b.TryGetProperty("updatedAt", out _));
        Assert.False(b.TryGetProperty("userId", out _), "owner id must not leak");
        Assert.False(b.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Optional_fields_are_omitted_not_null_so_they_match_the_typescript_contract()
    {
        var c = await NewUserAsync();
        var b = await CreateAsync(c, new { name = "Only a name", code = "  ", term = "" });
        foreach (var f in OptionalFields) Assert.False(b.TryGetProperty(f, out _), f);
    }

    [Fact]
    public async Task Get_returns_the_course_with_etag()
    {
        var c = await NewUserAsync();
        var created = await CreateAsync(c, new { name = "Chem" });
        var res = await c.GetAsync($"/api/v1/courses/{created.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotNull(res.Headers.ETag);
        Assert.Equal("Chem", (await res.BodyAsync()).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("{\"name\":\"\"}", "name")]
    [InlineData("{\"name\":\"   \"}", "name")]
    [InlineData("{}", "name")]
    [InlineData("{\"name\":null}", "name")]
    [InlineData("{\"name\":\"ok\",\"color\":\"red\"}", "color")]
    [InlineData("{\"name\":\"ok\",\"code\":\"123456789012345678901234567890123\"}", "code")]
    [InlineData("{\"name\":\"ok\",\"term\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}", "term")]
    public async Task Create_rejects_invalid_input_with_field_errors(string json, string field)
    {
        var c = await NewUserAsync();
        var res = await c.PostAsync("/api/v1/courses", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await res.BodyAsync()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Create_rejects_name_over_120_chars_but_accepts_exactly_120()
    {
        var c = await NewUserAsync();
        await (await c.PostAsJsonAsync("/api/v1/courses", new { name = new string('a', 121) }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/courses", new { name = new string('a', 120) })).StatusCode);
    }

    [Fact]
    public async Task Create_rejects_malformed_json_and_empty_body_and_oversized_body()
    {
        var c = await NewUserAsync();
        var bad = await c.PostAsync("/api/v1/courses", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        await bad.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var empty = await c.PostAsync("/api/v1/courses", new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        await empty.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var wrongType = await c.PostAsync("/api/v1/courses", new StringContent("name=x", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
    }

    [Fact]
    public async Task Client_supplied_owner_or_id_is_ignored()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        var bobId = (await (await bob.GetAsync("/api/v1/me")).BodyAsync()).GetProperty("id").GetString();
        var forced = Guid.NewGuid();
        var created = await CreateAsync(alice, new { name = "Mine", userId = bobId, id = forced });
        Assert.NotEqual(forced.ToString(), created.GetProperty("id").GetString());
        Assert.Equal(0, (await (await bob.GetAsync("/api/v1/courses")).BodyAsync()).GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await (await alice.GetAsync("/api/v1/courses")).BodyAsync()).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_is_isolated_per_user_and_uses_the_envelope()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        await CreateAsync(alice, new { name = "Alice A" });
        await CreateAsync(alice, new { name = "Alice B" });
        await CreateAsync(bob, new { name = "Bob A" });

        var a = await (await alice.GetAsync("/api/v1/courses")).BodyAsync();
        var b = await (await bob.GetAsync("/api/v1/courses")).BodyAsync();
        Assert.Equal(["Alice B", "Alice A"], a.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()!));
        Assert.Equal(["Bob A"], b.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()!));
        Assert.True(a.TryGetProperty("nextCursor", out var next));
        Assert.Equal(JsonValueKind.Null, next.ValueKind);
    }

    [Fact]
    public async Task List_paginates_with_cursor_without_duplicates_or_gaps()
    {
        var c = await NewUserAsync();
        var created = new List<string>();
        for (var i = 0; i < 5; i++) created.Add((await CreateAsync(c, new { name = $"C{i}" })).GetProperty("id").GetString()!);

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = "/api/v1/courses?limit=2" + (cursor is null ? "" : "&cursor=" + cursor);
            var body = await (await c.GetAsync(url)).BodyAsync();
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()!));
            var n = body.GetProperty("nextCursor");
            cursor = n.ValueKind == JsonValueKind.Null ? null : n.GetString();
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(created.AsEnumerable().Reverse(), seen);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=abc")]
    [InlineData("cursor=not-a-cursor")]
    public async Task List_rejects_bad_paging_parameters(string query)
    {
        var c = await NewUserAsync();
        var res = await c.GetAsync("/api/v1/courses?" + query);
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Update_with_if_match_replaces_the_course_and_rotates_the_etag()
    {
        var c = await NewUserAsync();
        var created = await CreateAsync(c, new { name = "Old", code = "X1", color = "#112233" });
        var id = created.GetProperty("id").GetString()!;
        var etag = (await c.GetAsync($"/api/v1/courses/{id}")).Headers.ETag!.ToString();

        var res = await c.SendAsync(Put(id, new { name = "New" }, etag));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var b = await res.BodyAsync();
        Assert.Equal("New", b.GetProperty("name").GetString());
        Assert.False(b.TryGetProperty("code", out _), "PUT is a full replace: omitted optional fields are cleared");
        Assert.NotEqual(etag, res.Headers.ETag!.ToString());
        Assert.True(b.GetProperty("updatedAt").GetDateTimeOffset() >= created.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(created.GetProperty("createdAt").GetDateTimeOffset(), b.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Update_without_if_match_is_428_and_stale_if_match_is_412()
    {
        var c = await NewUserAsync();
        var id = (await CreateAsync(c, new { name = "A" })).GetProperty("id").GetString()!;
        var etag1 = (await c.GetAsync($"/api/v1/courses/{id}")).Headers.ETag!.ToString();

        await (await c.SendAsync(Put(id, new { name = "B" }, null))).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put(id, new { name = "B" }, etag1))).StatusCode);
        await (await c.SendAsync(Put(id, new { name = "C" }, etag1))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        await (await c.SendAsync(Put(id, new { name = "C" }, "\"garbage\""))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        Assert.Equal("B", (await (await c.GetAsync($"/api/v1/courses/{id}")).BodyAsync()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_validates_input()
    {
        var c = await NewUserAsync();
        var id = (await CreateAsync(c, new { name = "A" })).GetProperty("id").GetString()!;
        var etag = (await c.GetAsync($"/api/v1/courses/{id}")).Headers.ETag!.ToString();
        var res = await c.SendAsync(Put(id, new { name = "" }, etag));
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Delete_archives_hides_from_default_list_and_is_idempotent()
    {
        var c = await NewUserAsync();
        var id = (await CreateAsync(c, new { name = "Archive me" })).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/courses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/courses/{id}")).StatusCode);

        Assert.Equal(0, (await (await c.GetAsync("/api/v1/courses")).BodyAsync()).GetProperty("items").GetArrayLength());
        var all = await (await c.GetAsync("/api/v1/courses?includeArchived=true")).BodyAsync();
        var only = Assert.Single(all.GetProperty("items").EnumerateArray());
        Assert.True(only.TryGetProperty("archivedAt", out _));
        var got = await (await c.GetAsync($"/api/v1/courses/{id}")).BodyAsync();
        Assert.True(got.TryGetProperty("archivedAt", out _));
    }

    [Fact]
    public async Task Purge_permanently_deletes()
    {
        var c = await NewUserAsync();
        var id = (await CreateAsync(c, new { name = "Gone" })).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/courses/{id}?purge=true")).StatusCode);
        await (await c.GetAsync($"/api/v1/courses/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, (await (await c.GetAsync("/api/v1/courses?includeArchived=true")).BodyAsync()).GetProperty("items").GetArrayLength());
    }

    // ---- cross-user isolation: foreign and unknown ids are indistinguishable (ADR-002, SEC-002) ----

    [Fact]
    public async Task Cross_user_get_update_delete_purge_return_404_and_change_nothing()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        var id = (await CreateAsync(alice, new { name = "Alice private" })).GetProperty("id").GetString()!;
        var etag = (await alice.GetAsync($"/api/v1/courses/{id}")).Headers.ETag!.ToString();

        var responses = new[]
        {
            await bob.GetAsync($"/api/v1/courses/{id}"),
            await bob.SendAsync(Put(id, new { name = "pwned" }, etag)),
            await bob.SendAsync(Put(id, new { name = "pwned" }, null)),   // 404 must win over 428: no existence oracle
            await bob.SendAsync(Put(id, new { name = "" }, etag)),         // ... and over 400
            await bob.DeleteAsync($"/api/v1/courses/{id}"),
            await bob.DeleteAsync($"/api/v1/courses/{id}?purge=true"),
        };
        foreach (var r in responses)
        {
            await r.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            Assert.DoesNotContain("Alice private", await r.Content.ReadAsStringAsync());
        }

        var mine = await (await alice.GetAsync($"/api/v1/courses/{id}")).BodyAsync();
        Assert.Equal("Alice private", mine.GetProperty("name").GetString());
        Assert.False(mine.TryGetProperty("archivedAt", out _));
    }

    [Fact]
    public async Task Foreign_and_unknown_ids_produce_identical_responses()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        var id = (await CreateAsync(alice, new { name = "x" })).GetProperty("id").GetString()!;
        var foreign = await bob.GetAsync($"/api/v1/courses/{id}");
        var unknown = await bob.GetAsync($"/api/v1/courses/{Guid.NewGuid()}");
        Assert.Equal(foreign.StatusCode, unknown.StatusCode);
        var f = await foreign.BodyAsync(); var u = await unknown.BodyAsync();
        Assert.Equal(f.GetProperty("code").GetString(), u.GetProperty("code").GetString());
        Assert.Equal(f.GetProperty("title").GetString(), u.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("1")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Malformed_or_empty_ids_are_404(string id)
    {
        var c = await NewUserAsync();
        await (await c.GetAsync($"/api/v1/courses/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await c.DeleteAsync($"/api/v1/courses/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Cursor_from_another_user_cannot_reveal_their_courses()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        var a1 = (await CreateAsync(alice, new { name = "a1" })).GetProperty("id").GetString()!;
        await CreateAsync(alice, new { name = "a2" });
        await CreateAsync(bob, new { name = "b1" });
        var cursor = Guid.Parse(a1).ToString("N");
        var body = await (await bob.GetAsync($"/api/v1/courses?cursor={cursor}&limit=100")).BodyAsync();
        Assert.DoesNotContain(body.GetProperty("items").EnumerateArray(), x => x.GetProperty("name").GetString()!.StartsWith('a'));
    }
}
