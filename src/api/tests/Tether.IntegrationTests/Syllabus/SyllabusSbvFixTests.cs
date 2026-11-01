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
