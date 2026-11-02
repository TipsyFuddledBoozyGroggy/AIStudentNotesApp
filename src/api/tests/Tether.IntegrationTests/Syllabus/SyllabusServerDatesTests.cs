using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tether.IntegrationTests.Infrastructure;
using Tether.Syllabus.Extraction;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// SBV-12 (docs/ai/syllabus-extraction-design.md section 17, condition 5): the server, not the extractor, decides whether a resolved date is
/// what the quote says. A stand-in extractor wraps the mock, keeps every quote verbatim, and lies about the date on four lines:
/// (a) "Final exam: Dec 9, 2026" claimed as Dec 19; (b) "PS1 due Oct 5" claimed as an explicit full date; (c) the ambiguous "10/5/2026"
/// resolved as Oct 5; (d) the single date "Wed Dec 2, 2026" presented as a weekly recurrence. Lead ruling: each is an invalid model-written
/// field, so the item is dropped and counted in droppedInvalid and its quote is shown as an unclaimed date. No claimed date is stored.
/// </summary>
public class SyllabusServerDatesTests(SyllabusServerDatesTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusServerDatesTests.Factory>
{
    internal const string Text =
        "Midterm exam: Oct 19, 2026 at 10:00 AM\nFinal exam: Dec 9, 2026\nPS1 due Oct 5\nProject due 10/5/2026\nExam review Wed Dec 2, 2026 at 3:00 PM";

    internal sealed class MisdatingExtractor : ISyllabusExtractor
    {
        private readonly MockRuleExtractor _inner = new();
        public string Version => _inner.Version;
        public string Model => _inner.Model;
        public ExtractorOutput Extract(ExtractorInput input)
        {
            var o = _inner.Extract(input);
            ProposedItem Lie(ProposedItem i) => i.Snippet.Text switch
            {
                "Final exam: Dec 9, 2026" => i with { Date = new DateOnly(2026, 12, 19), Confidence = "looks_right" },
                "PS1 due Oct 5" => i with { Derivation = "explicit_full", Flags = [], Confidence = "looks_right" },
                "Project due 10/5/2026" => i with
                {
                    WhenStatus = "resolved", Date = new DateOnly(2026, 10, 5), Derivation = "explicit_full", UnknownReason = null, AllDay = true,
                    Timezone = "America/New_York", TimezoneSource = "course_default", Confidence = "looks_right", Flags = [],
                },
                "Exam review Wed Dec 2, 2026 at 3:00 PM" => i with
                {
                    Derivation = "year_from_term",
                    Recurrence = new ProposedRecurrence(["WE"], new TimeOnly(15, 0), null, new DateOnly(2026, 12, 2), new DateOnly(2026, 12, 11), "term_end"),
                },
                _ => i,
            };
            return o with { Items = [.. o.Items.Select(Lie)] };
        }
    }

    public sealed class Factory : ApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => { s.RemoveAll<ISyllabusExtractor>(); s.AddSingleton<ISyllabusExtractor, MisdatingExtractor>(); });
        }
    }

    [Fact]
    public async Task SBV12_items_whose_date_is_not_written_in_their_quote_are_dropped_counted_and_shown_as_unclaimed_dates()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var done = await CreateReadyAsync(a.Client, course, Paste(Text));
        Assert.Equal("ready", done.GetProperty("status").GetString());
        SyllabusSchemas.AssertValid("syllabus", done);
        var counts = done.GetProperty("counts");
        Assert.Equal(4, counts.GetProperty("droppedInvalid").GetInt32());                       // one per lie, none silent
        Assert.Equal(4, counts.GetProperty("unclaimedDates").GetInt32());
        var sid = done.GetProperty("id").GetString()!; var g = Guid32(sid);

        // the item page as the web reads it: only the honest midterm, and the page and the item pass the schemas
        var res = await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?limit=200");
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        var page = await res.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-item-page", page);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        SyllabusSchemas.AssertValid("syllabus-item", item);
        Assert.StartsWith("Midterm", item.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal("2026-10-19", item.GetProperty("when").GetProperty("date").GetString());
        Assert.Equal("looks_right", item.GetProperty("confidence").GetString());

        // each lie's verbatim quote is shown as a date we noticed
        var unclaimedRes = await a.Client.GetAsync($"{Api}/syllabi/{sid}/unclaimed-dates");
        Assert.Equal(System.Net.HttpStatusCode.OK, unclaimedRes.StatusCode);
        var unclaimed = await unclaimedRes.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-unclaimed-date-page", unclaimed);
        Assert.Equal(
            ["Exam review Wed Dec 2, 2026 at 3:00 PM", "Final exam: Dec 9, 2026", "PS1 due Oct 5", "Project due 10/5/2026"],
            unclaimed.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("snippet").GetProperty("text").GetString()!).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("2026-12-19", unclaimed.GetRawText(), StringComparison.Ordinal);

        // nothing the extractor claimed is stored: no Dec 19, no resolved 10/5, no recurrence, no other item, no alternative
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}'"));
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.items where syllabus_id = '{g}'
              and (date_local in ('2026-12-19','2026-12-09','2026-10-05','2026-12-02') or rec_by_day is not null)
            """));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.item_alternatives a join syllabus.items i on i.id = a.item_id where i.syllabus_id = '{g}'"));
        // the unclaimed rows are the canonical slices at their offsets (ASCII fixture: UTF-16 offsets equal code-point offsets)
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.unclaimed_dates u join syllabus.canonical_texts t on t.syllabus_id = u.syllabus_id
            where u.syllabus_id = '{g}' and (u.snippet_text <> substr(t.text, u.snippet_start + 1, u.snippet_end - u.snippet_start)
               or u.context_text <> substr(t.text, u.context_start + 1, u.context_end - u.context_start))
            """));
    }
}
