using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// Multipart FILE upload, intake validation only (P2-B1). Plain text is decoded and follows the paste path; PDF and DOCX are sniffed,
/// counted, hashed and discarded and the syllabus ends failed/disabled because the sandboxed parser is a later increment (P2-B3).
/// </summary>
public class SyllabusUploadTests(ApiFactory factory) : SyllabusTestBase(factory), IClassFixture<ApiFactory>
{
    private static string Meta(bool dup = false) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["defaultTimezone"] = "America/New_York", ["termStart"] = "2026-08-24", ["termEnd"] = "2026-12-11", ["allowDuplicate"] = dup,
        ["noticeVersion"] = "draft-2026-10", ["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.ToString("O") },
    });

    private static MultipartFormDataContent Form(byte[] file, string fileName = "syllabus.txt", string? partType = "text/plain", string? metadata = null, bool omitMetadata = false)
    {
        var mp = new MultipartFormDataContent();
        if (!omitMetadata) mp.Add(new StringContent(metadata ?? Meta(), Encoding.UTF8, "application/json"), "metadata");
        var f = new ByteArrayContent(file);
        if (partType is not null) f.Headers.ContentType = MediaTypeHeaderValue.Parse(partType);
        mp.Add(f, "file", fileName);
        return mp;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient c, string course, HttpContent content, string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/courses/{course}/syllabi") { Content = content };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? NewKey());
        return c.SendAsync(req);
    }

    private static byte[] Pdf() => [.. "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n"u8];

    private static byte[] Docx()
    {
        var head = new byte[200];
        "PK\x03\x04"u8.CopyTo(head); head[26] = 19; "[Content_Types].xml"u8.CopyTo(head.AsSpan(30));
        return head;
    }

    [Fact]
    public async Task A_text_file_follows_the_paste_path_end_to_end()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var res = await Upload(a.Client, course, Form(Encoding.UTF8.GetBytes(Sample)));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var b = await res.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus", b);
        Assert.Equal("upload", b.GetProperty("inputKind").GetString());
        Assert.Equal("txt", b.GetProperty("fileType").GetString());
        Assert.False(b.GetProperty("originalRetained").GetBoolean());
        var done = await WaitDoneAsync(a.Client, b.GetProperty("id").GetString()!);
        Assert.Equal("ready", done.GetProperty("status").GetString());
        Assert.True(done.GetProperty("counts").GetProperty("total").GetInt32() >= 7);
        Assert.Equal(64, done.GetProperty("contentSha256").GetString()!.Length);
    }

    [Fact]
    public async Task A_utf16_file_with_a_bom_is_decoded_and_a_utf8_bom_is_stripped()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Quiz due Oct 20, 2026 at 9:00 AM")).ToArray();
        Assert.Equal(HttpStatusCode.Accepted, (await Upload(a.Client, course, Form(utf16))).StatusCode);
        var done = await WaitDoneAsync(a.Client, (await a.Client.GetAsync($"{Api}/courses/{course}/syllabi")).AssertSingle());
        Assert.Equal("ready", done.GetProperty("status").GetString());
        var utf8 = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Exam due Oct 21, 2026")).ToArray();
        Assert.Equal(HttpStatusCode.Accepted, (await Upload(a.Client, await CourseAsync(a.Client, "B"), Form(utf8))).StatusCode);
    }

    [Fact]
    public async Task A_valid_pdf_or_docx_is_accepted_at_intake_and_ends_failed_disabled_with_no_bytes_kept()
    {
        var a = await NewActorAsync();
        foreach (var (name, bytes, type, ext) in new[] { ("pdf", Pdf(), "application/pdf", "s.pdf"), ("docx", Docx(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "s.docx") })
        {
            var course = await CourseAsync(a.Client, name);
            var res = await Upload(a.Client, course, Form(bytes, ext, type));
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            var b = await res.BodyAsync();
            Assert.Equal(name, b.GetProperty("fileType").GetString());
            var done = await WaitDoneAsync(a.Client, b.GetProperty("id").GetString()!);
            SyllabusSchemas.AssertValid("syllabus", done);
            Assert.Equal("failed", done.GetProperty("status").GetString());
            Assert.Equal("disabled", done.GetProperty("statusReason").GetString());          // honest: no parser exists yet
            Assert.Equal(JsonValueKind.Null, done.GetProperty("pollAfterSeconds").ValueKind);
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.canonical_texts where syllabus_id = '{Guid32(b.GetProperty("id").GetString()!)}'"));
        }
    }

    [Fact]
    public async Task Signature_extension_and_content_type_must_agree_and_unknown_types_are_415()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var exe = new byte[200]; exe[0] = (byte)'M'; exe[1] = (byte)'Z';
        var cases = new (string Name, byte[] Bytes, string File, string? Type)[]
        {
            ("pdf bytes named .txt", Pdf(), "s.txt", "text/plain"), ("pdf bytes as docx type", Pdf(), "s.pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ("text named .pdf", Encoding.UTF8.GetBytes("hello world, this is plain text that is long enough to be sniffed as such."), "s.pdf", "application/pdf"),
            ("exe", exe, "s.exe", "application/octet-stream"), ("zip that is not docx", [.. "PK\x03\x04"u8, .. new byte[100]], "s.zip", "application/zip"),
            ("binary named .txt", [.. new byte[] { 1, 2, 3, 0, 0, 0, 7 }, .. new byte[100]], "s.txt", "text/plain"), ("invalid utf-8 text", [.. "ok text "u8, 0xFF, 0xFE, 0xFD, .. "more text here to pad"u8], "s.txt", "text/plain"),
            ("empty file", [], "s.txt", "text/plain"),
        };
        foreach (var c in cases)
        {
            var res = await Upload(a.Client, course, Form(c.Bytes, c.File, c.Type));
            await res.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
            var raw = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain(c.File, raw);                                                  // neither is the filename
        }
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task Text_containing_nul_is_rejected_and_ten_megabytes_is_the_streaming_cap()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        await (await Upload(a.Client, course, Form(Encoding.UTF8.GetBytes("Quiz due Oct 20\0, 2026")))).AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        var big = new byte[10 * 1024 * 1024 + 1];
        Array.Fill(big, (byte)'a');
        await (await Upload(a.Client, course, Form(big))).AssertProblemAsync((HttpStatusCode)413, "too_large");
        var textOver = new byte[2 * 1024 * 1024];
        Array.Fill(textOver, (byte)'a');
        await (await Upload(a.Client, course, Form(textOver))).AssertProblemAsync((HttpStatusCode)413, "too_large");     // text can never fit 50,000 characters
        var ok = new byte[10 * 1024 * 1024];
        "%PDF-1.7\n"u8.CopyTo(ok);
        Assert.Equal(HttpStatusCode.Accepted, (await Upload(a.Client, course, Form(ok, "s.pdf", "application/pdf"))).StatusCode);     // exactly at the cap is accepted
    }

    [Fact]
    public async Task Request_shape_exactly_one_file_part_and_one_small_metadata_part_anything_else_is_400()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var text = Encoding.UTF8.GetBytes("Quiz due Oct 20, 2026");
        await (await Upload(a.Client, course, Form(text, omitMetadata: true))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var two = Form(text); two.Add(new ByteArrayContent(text), "file", "again.txt");
        await (await Upload(a.Client, course, two)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var three = Form(text); three.Add(new StringContent("x"), "extra");
        await (await Upload(a.Client, course, three)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var giant = JsonSerializer.Serialize(new Dictionary<string, object?> { ["defaultTimezone"] = "America/New_York", ["noticeVersion"] = "draft-2026-10", ["title"] = new string('t', 5000), ["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.ToString("O") } });
        await (await Upload(a.Client, course, Form(text, metadata: giant))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var withText = Meta().Replace("{", "{\"text\":\"inline\",");
        await (await Upload(a.Client, course, Form(text, metadata: withText))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var withFilename = Meta().Replace("{", "{\"filename\":\"x.txt\",");
        await (await Upload(a.Client, course, Form(text, metadata: withFilename))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var notMultipart = new HttpRequestMessage(HttpMethod.Post, $"{Api}/courses/{course}/syllabi") { Content = new StringContent("x", Encoding.UTF8, "text/plain") };
        notMultipart.Headers.TryAddWithoutValidation("Idempotency-Key", NewKey());
        await (await a.Client.SendAsync(notMultipart)).AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task A_retry_of_an_upload_with_the_same_key_and_bytes_is_a_replay_and_other_bytes_are_400()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var key = NewKey(); var meta = Meta();
        var bytes = Encoding.UTF8.GetBytes("Quiz due Oct 20, 2026");
        var first = await Upload(a.Client, course, Form(bytes, metadata: meta), key);
        var second = await Upload(a.Client, course, Form(bytes, metadata: meta), key);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode); Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal((await first.BodyAsync()).GetProperty("id").GetString(), (await second.BodyAsync()).GetProperty("id").GetString());
        await (await Upload(a.Client, course, Form(Encoding.UTF8.GetBytes("Quiz due Oct 21, 2026"), metadata: meta), key)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task The_client_filename_is_never_stored_anywhere()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        const string canary = "CANARY-FILENAME-7f3a";
        var res = await Upload(a.Client, course, Form(Encoding.UTF8.GetBytes("Quiz due Oct 20, 2026"), canary + ".txt"));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        await WaitDoneAsync(a.Client, (await res.BodyAsync()).GetProperty("id").GetString()!);
        Assert.Equal(0, await SchemaScan.CountAsync(F, canary));
    }
}

internal static class SchemaScan
{
    /// <summary>Counts rows in every text-like column of schema "syllabus" (and its audit) whose value contains <paramref name="needle"/>.</summary>
    public static async Task<long> CountAsync(ApiFactory f, string needle)
    {
        var cols = new List<(string T, string C)>();
        await using (var conn = new Npgsql.NpgsqlConnection(f.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("select table_name, column_name from information_schema.columns where table_schema = 'syllabus' and table_name <> '__ef_migrations_history'", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) cols.Add((r.GetString(0), r.GetString(1)));
        }
        long total = 0;
        foreach (var (t, c) in cols)
            total += await f.ScalarAsync($"select count(*) from syllabus.\"{t}\" where \"{c}\"::text ilike '%{needle.Replace("'", "''")}%'");
        return total;
    }
}

internal static class ResponseExtensions
{
    /// <summary>The id of the only syllabus in a list response.</summary>
    public static string AssertSingle(this HttpResponseMessage r)
    {
        var body = JsonDocument.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        return Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()!;
    }
}

