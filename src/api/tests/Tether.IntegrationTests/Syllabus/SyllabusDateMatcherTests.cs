using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tether.IntegrationTests.Infrastructure;
using Tether.Syllabus.Extraction;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// SBV-18, SBV-29, SBV-30 through the API. One syllabus holds the mock-reachable misreads and a stand-in extractor (the mock, with one lie):
/// "Reading week Oct 16-12, 2026" claimed as Oct 16 to Oct 12. Before the fix the reversed end passed the SBV-12 check and then
/// ck_items_end_date failed the whole syllabus, and a fullwidth digit after a month name ("Quiz Dec ９") threw FormatException, also failing the
/// whole syllabus (invalid_output). Now the syllabus is ready, the lie is dropped and counted with its quote kept as an unclaimed date,
/// "Sept 2026" is not Sep 20, "5 October 2026" is Oct 5 (not Oct 20), and "Dec 9-10am" is Dec 9 at 10 AM, not Dec 9 to Dec 10 (and, SBV-32,
/// not 9 to 10 AM: the time is read only after the date's own text).
/// </summary>
public class SyllabusDateMatcherTests(SyllabusDateMatcherTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusDateMatcherTests.Factory>
{
    internal const string Text =
        "Midterm exam: Oct 19, 2026 at 10:00 AM\nReading week Oct 16-12, 2026\nQuiz Dec ９\nQuiz on 5 October 2026\nPaper due by Sept 2026\nQuiz Dec 9-10am\nStudents may 3 times retake a quiz\nQuiz worth 5/20 points";

    internal sealed class ReversedRangeExtractor : ISyllabusExtractor
    {
        private readonly MockRuleExtractor _inner = new();
        public string Version => _inner.Version;
        public string Model => _inner.Model;
        public ExtractorOutput Extract(ExtractorInput input)
        {
            var o = _inner.Extract(input);
            return o with
            {
                Items = [.. o.Items.Select(i => i.Snippet.Text == "Reading week Oct 16-12, 2026" ? i with { EndDate = new DateOnly(2026, 10, 12) } : i)],
            };
        }
    }

    public sealed class Factory : ApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => { s.RemoveAll<ISyllabusExtractor>(); s.AddSingleton<ISyllabusExtractor, ReversedRangeExtractor>(); });
        }
    }

    [Fact]
    public async Task SBV18_29_30_misreads_are_gone_and_a_reversed_range_or_a_unicode_digit_no_longer_fails_the_whole_syllabus()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var done = await CreateReadyAsync(a.Client, course, Paste(Text));
        Assert.Equal("ready", done.GetProperty("status").GetString());                         // was invalid_output (FormatException, ck_items_end_date)
        SyllabusSchemas.AssertValid("syllabus", done);
        Assert.Equal(1, done.GetProperty("counts").GetProperty("droppedInvalid").GetInt32());   // the reversed range, counted
        var sid = done.GetProperty("id").GetString()!; var g = Guid32(sid);

        var page = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?limit=200")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-item-page", page);
        foreach (var item in page.GetProperty("items").EnumerateArray()) SyllabusSchemas.AssertValid("syllabus-item", item);

        // exactly the honest dates: Oct 19 (midterm), Oct 5 (day-first, written year), Dec 9 at 10 AM with no end date.
        // SBV-32 (Lead ruling, 2026-11-02): this used to expect 'Quiz Dec 9-10am' stored 09:00 to 10:00, which read the day 9 as a start hour.
        // The time is read only after the date's own text 'Dec 9', so it is 10:00 with no end time and no inferred start-end range.
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}' and date_local = '2026-10-19'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}' and date_local = '2026-10-05' and end_date_local is null"));
        Assert.Equal(1, await F.ScalarAsync($"""
            select count(*) from syllabus.items where syllabus_id = '{g}' and date_local = '2026-12-09' and end_date_local is null
              and time_local = '10:00' and end_time_local is null
            """));
        // none of the misreads: Sep 20 / Oct 20 (the year's first two digits), Dec 10 as an end, Oct 12 as an end, May 3, May 20
        Assert.Equal(0, await F.ScalarAsync($"""
            select count(*) from syllabus.items where syllabus_id = '{g}'
              and (date_local in ('2026-09-20','2026-10-20','2026-10-16','2026-05-03','2026-05-20') or end_date_local is not null)
            """));

        // the lie's verbatim quote is shown as a date we noticed
        var unclaimed = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/unclaimed-dates")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-unclaimed-date-page", unclaimed);
        Assert.Equal(
            ["Reading week Oct 16-12, 2026"],
            unclaimed.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("snippet").GetProperty("text").GetString()!));
    }
}
