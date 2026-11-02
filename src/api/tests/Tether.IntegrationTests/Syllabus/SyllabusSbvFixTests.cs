using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tether.IntegrationTests.Infrastructure;
using Tether.Syllabus.Extraction;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// SBV-01 (docs/security/sbr-fixes-verification.md): the orchestrator, not the extractor, decides what is stored as a quote of the student's
/// syllabus. A stand-in extractor wraps the mock and (a) replaces one item's snippet text with words it wrote itself at the right offsets, and
/// (b) adds an unclaimed date whose offsets run past the end of the text. Both are dropped and counted; nothing stored differs from the
/// canonical slice at its offsets.
/// </summary>
public class SyllabusForgedSnippetTests(SyllabusForgedSnippetTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusForgedSnippetTests.Factory>
{
    internal sealed class ForgingExtractor : ISyllabusExtractor
    {
        private readonly MockRuleExtractor _inner = new();
        public string Version => _inner.Version;
        public string Model => _inner.Model;
        public ExtractorOutput Extract(ExtractorInput input)
        {
            var o = _inner.Extract(input);
            var items = o.Items.Select(i => i.Snippet.ContextText.Contains("FORGE", StringComparison.Ordinal)
                ? i with { Snippet = i.Snippet with { Text = "Exam moved to Nov 30, 2026" } } : i).ToList();
            var n = input.Text.Length;
            var pastEnd = new ProposedSnippet(n - 4, n + 20, "Exam moved to Nov 30, 2026", 0, n + 20, "Exam moved to Nov 30, 2026", "exact");
            return o with { Items = items, Unclaimed = [.. o.Unclaimed, pastEnd] };
        }
    }

    public sealed class Factory : ApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => { s.RemoveAll<ISyllabusExtractor>(); s.AddSingleton<ISyllabusExtractor, ForgingExtractor>(); });
        }
    }

    [Fact]
    public async Task SBV01_a_forged_snippet_and_an_out_of_range_unclaimed_date_are_dropped_counted_and_never_stored()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var done = await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM\nQuiz 2 due Oct 21, 2026 FORGE\nGuest talk on Nov 3, 2026 also Nov 5, 2026"));
        Assert.Equal("ready", done.GetProperty("status").GetString());
        SyllabusSchemas.AssertValid("syllabus", done);
        var sid = done.GetProperty("id").GetString()!; var g = Guid32(sid);

        var items = await ItemsAsync(a.Client, sid);
        Assert.DoesNotContain(items, i => i.GetProperty("title").GetString()!.StartsWith("Quiz 2", StringComparison.Ordinal));
        var counts = done.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("droppedInvalid").GetInt32());         // the forged item and the out-of-range unclaimed date

        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.items where syllabus_id = '{g}' and (snippet_text like '%moved%' or context_text like '%moved%')
            """));
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.unclaimed_dates where syllabus_id = '{g}' and (snippet_text like '%moved%' or context_text like '%moved%')
            """));
        // every stored quote is the canonical slice at its offsets (the fixture is ASCII, so UTF-16 offsets equal code-point offsets)
        Assert.True(await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}'") > 0);
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.items i join syllabus.canonical_texts t on t.syllabus_id = i.syllabus_id
            where i.syllabus_id = '{g}' and (i.snippet_text <> substr(t.text, i.snippet_start + 1, i.snippet_end - i.snippet_start)
               or i.context_text <> substr(t.text, i.context_start + 1, i.context_end - i.context_start))
            """));
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.unclaimed_dates u join syllabus.canonical_texts t on t.syllabus_id = u.syllabus_id
            where u.syllabus_id = '{g}' and (u.snippet_text <> substr(t.text, u.snippet_start + 1, u.snippet_end - u.snippet_start)
               or u.context_text <> substr(t.text, u.context_start + 1, u.context_end - u.context_start) or u.snippet_end > t.utf16_length)
            """));
    }
}

/// <summary>
/// SBV-06 (docs/ai/syllabus-extraction-design.md section 17, condition 1): the server, not the extractor, chooses the context of a stored quote.
/// A stand-in extractor wraps the mock and narrows one item's snippet AND context to "on Dec 9, 2026", hiding the "NOT" of
/// "The final is NOT on Dec 9, 2026", and narrows an unclaimed date's context to the date itself. Both quotes are verbatim, so SBV-01 accepts
/// them; the server then replaces each context with the whole canonical line, so the stored and served context shows the negation.
/// </summary>
public class SyllabusNarrowedContextTests(SyllabusNarrowedContextTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusNarrowedContextTests.Factory>
{
    internal sealed class NarrowingExtractor : ISyllabusExtractor
    {
        private readonly MockRuleExtractor _inner = new();
        public string Version => _inner.Version;
        public string Model => _inner.Model;
        public ExtractorOutput Extract(ExtractorInput input)
        {
            var o = _inner.Extract(input);
            ProposedSnippet Narrow(string needle)
            {
                var s = input.Text.IndexOf(needle, StringComparison.Ordinal);
                return new ProposedSnippet(s, s + needle.Length, needle, s, s + needle.Length, needle, "exact");
            }
            var items = o.Items.Select(i => i.Snippet.ContextText.Contains("NOT on Dec 9", StringComparison.Ordinal)
                ? i with { Snippet = Narrow("on Dec 9, 2026") } : i).ToList();
            var unclaimed = o.Unclaimed.Select(u => u.Text == "Nov 3, 2026" ? Narrow("Nov 3, 2026") : u).ToList();
            return o with { Items = items, Unclaimed = unclaimed };
        }
    }

    public sealed class Factory : ApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => { s.RemoveAll<ISyllabusExtractor>(); s.AddSingleton<ISyllabusExtractor, NarrowingExtractor>(); });
        }
    }

    [Fact]
    public async Task SBV06_a_context_narrowed_to_hide_NOT_is_stored_and_served_as_the_whole_line()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var done = await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM\nThe final is NOT on Dec 9, 2026\nQuiz 2 due Oct 21, 2026, also Nov 3, 2026"));
        Assert.Equal("ready", done.GetProperty("status").GetString());
        SyllabusSchemas.AssertValid("syllabus", done);
        Assert.Equal(0, done.GetProperty("counts").GetProperty("droppedInvalid").GetInt32());   // replaced, not dropped
        var sid = done.GetProperty("id").GetString()!; var g = Guid32(sid);

        var final = Assert.Single(await ItemsAsync(a.Client, sid), i => i.GetProperty("snippet").GetProperty("text").GetString() == "on Dec 9, 2026");
        Assert.Equal("The final is NOT on Dec 9, 2026", final.GetProperty("snippet").GetProperty("context").GetProperty("text").GetString());

        var page = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/unclaimed-dates")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-unclaimed-date-page", page);
        var nov = Assert.Single(page.GetProperty("items").EnumerateArray(), u => u.GetProperty("snippet").GetProperty("text").GetString() == "Nov 3, 2026");
        Assert.Equal("Quiz 2 due Oct 21, 2026, also Nov 3, 2026", nov.GetProperty("snippet").GetProperty("context").GetProperty("text").GetString());

        // the narrowed rows are stored with the whole line, and every stored context is the canonical slice that contains its snippet
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}' and snippet_text = 'on Dec 9, 2026' and context_text = 'The final is NOT on Dec 9, 2026'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.unclaimed_dates where syllabus_id = '{g}' and snippet_text = 'Nov 3, 2026' and context_text = 'Quiz 2 due Oct 21, 2026, also Nov 3, 2026'"));
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.items i join syllabus.canonical_texts t on t.syllabus_id = i.syllabus_id
            where i.syllabus_id = '{g}' and (i.context_text <> substr(t.text, i.context_start + 1, i.context_end - i.context_start)
               or i.context_start > i.snippet_start or i.context_end < i.snippet_end)
            """));
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.unclaimed_dates u join syllabus.canonical_texts t on t.syllabus_id = u.syllabus_id
            where u.syllabus_id = '{g}' and (u.context_text <> substr(t.text, u.context_start + 1, u.context_end - u.context_start)
               or u.context_start > u.snippet_start or u.context_end < u.snippet_end)
            """));
    }
}

/// <summary>
/// SBV-04: a stripped hidden character between a letter and its combining mark used to leave the stored canonical text non-NFC, although the
/// contract (API_CONTRACTS_P2.md section 2.2) says it is NFC. The stored text, the paste content hash and every stored quote now agree on the
/// final NFC string, so the decomposed-with-hidden-character paste and the precomposed paste are the same syllabus for duplicate detection.
/// </summary>
public class SyllabusCanonicalNfcTests(ApiFactory f) : SyllabusTestBase(f), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SBV04_the_stored_canonical_text_hash_and_quotes_are_nfc_after_a_stripped_character()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var done = await CreateReadyAsync(a.Client, course, Paste("Room: Salle Ve\u200B\u0301rone\nMidterm exam in Ve\u200B\u0301rone: Oct 19, 2026 at 10:00 AM"));
        Assert.Equal("ready", done.GetProperty("status").GetString());
        var sid = done.GetProperty("id").GetString()!; var g = Guid32(sid);

        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.canonical_texts where syllabus_id = '{g}' and text is nfc normalized"));
        Assert.Equal(1, await F.ScalarAsync($"""
            select count(*) from syllabus.canonical_texts
            where syllabus_id = '{g}' and text = 'Room: Salle V' || U&'\00E9' || 'rone' || chr(10) || 'Midterm exam in V' || U&'\00E9' || 'rone: Oct 19, 2026 at 10:00 AM'
            """));
        // the paste content hash is the SHA-256 of the stored (final) canonical text
        Assert.Equal(1, await F.ScalarAsync($"""
            select count(*) from syllabus.syllabi s join syllabus.canonical_texts t on t.syllabus_id = s.id
            where s.id = '{g}' and s.content_sha256 = encode(sha256(convert_to(t.text, 'UTF8')), 'hex')
            """));
        // the quote is the canonical slice at its offsets (BMP only, so UTF-16 offsets equal code-point offsets), and nothing was dropped
        Assert.Equal(0, done.GetProperty("counts").GetProperty("droppedInvalid").GetInt32());
        Assert.Equal(1, await F.ScalarAsync($"""
            select count(*) from syllabus.items i join syllabus.canonical_texts t on t.syllabus_id = i.syllabus_id
            where i.syllabus_id = '{g}' and i.snippet_text = substr(t.text, i.snippet_start + 1, i.snippet_end - i.snippet_start)
              and i.context_text = substr(t.text, i.context_start + 1, i.context_end - i.context_start) and i.context_text is nfc normalized
            """));

        // the same syllabus typed with the precomposed letter is a duplicate of the first (before the fix the hashes differed)
        var dup = await CreateAsync(a.Client, course, Paste("Room: Salle V\u00E9rone\nMidterm exam in V\u00E9rone: Oct 19, 2026 at 10:00 AM"));
        await dup.AssertProblemAsync(System.Net.HttpStatusCode.Conflict, "duplicate_syllabus");
        Assert.Equal(sid, (await dup.BodyAsync()).GetProperty("existingSyllabusId").GetString());
    }
}

/// <summary>SBV-02: dates beyond the 200 unclaimed-date cap are counted in counts.droppedInvalid, not lost silently (real mock extractor).</summary>
public class SyllabusCapOverflowTests(ApiFactory f) : SyllabusTestBase(f), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SBV02_the_201st_unclaimed_date_is_counted_in_droppedInvalid()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var text = string.Join("\n", Enumerable.Range(0, 201).Select(i => $"See {new DateOnly(2026, 9, 1).AddDays(i % 90):yyyy-MM-dd}"));
        var done = await CreateReadyAsync(a.Client, course, Paste(text));
        Assert.Equal("ready", done.GetProperty("status").GetString());
        SyllabusSchemas.AssertValid("syllabus", done);
        var counts = done.GetProperty("counts");
        Assert.Equal(200, counts.GetProperty("unclaimedDates").GetInt32());
        Assert.Equal(1, counts.GetProperty("droppedInvalid").GetInt32());
    }
}
