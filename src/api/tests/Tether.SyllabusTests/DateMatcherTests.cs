using System.Globalization;
using System.Text;
using Tether.Syllabus.Extraction;
using Tether.Syllabus.Text;

namespace Tether.SyllabusTests;

/// <summary>
/// SBV-18, SBV-29, SBV-30: the shared date matcher (<see cref="SyllabusSignals.FindDates"/>, <see cref="SyllabusSignals.MonthDayOf"/>) that the
/// mock extractor and the server's SBV-12 date check both use. It must read a day only where a day is written, read the day as written,
/// never throw on any input, and never call a reversed or time-like range a date range.
/// </summary>
public class DateMatcherTests
{
    private static readonly MockRuleExtractor X = new();
    private static readonly DateOnly TermStart = new(2026, 8, 24), TermEnd = new(2026, 12, 11);

    private static ExtractorInput Input(string raw, bool term = true)
    {
        var doc = DocumentText.Canonicalize(raw);
        return new ExtractorInput(doc.Text, term ? TermStart : null, term ? TermEnd : null, "America/New_York", false, doc.InjectionSuspect);
    }

    /// <summary>The orchestrator's path (Jobs.PersistAsync): the SBV-01/06/12 backstop, then the SBV-10 re-derivation.</summary>
    private static (SyllabusLimits.Kept Kept, IReadOnlyList<ProposedItem> Stored) Store(ExtractorInput input, ExtractorOutput o)
    {
        var kept = SyllabusLimits.Apply(o, maxItems: 200, input.Text);
        var doc = ServerFlags.Document(input);
        return (kept, [.. kept.Items.Select(p => ServerFlags.Rederive(p, input, doc))]);
    }

    private static IReadOnlyList<ProposedItem> StoredItems(string raw, bool term = true)
    {
        var input = Input(raw, term);
        return Store(input, X.Extract(input)).Stored;
    }

    // ---------------------------------------------------------------- SBV-18: the finding's examples

    [Theory]
    [InlineData("Paper due by Sept 2026")]
    [InlineData("Final project due Dec 2026")]
    [InlineData("Quiz in Oct 2026")]
    [InlineData("Paper due by September 2026")]
    [InlineData("Exam in May 2026")]
    public void SBV18_a_month_and_year_with_no_day_is_never_read_as_a_day(string raw)
    {
        Assert.DoesNotContain(SyllabusSignals.FindDates(raw), h => h.Day is not null);
        Assert.DoesNotContain(StoredItems(raw), i => i.Date is not null);                        // was Sep 20 / Dec 20 / Oct 20 ... 2026
    }

    [Fact]
    public void SBV18_paper_due_by_sept_2026_is_not_stored_as_sep_20()
    {
        Assert.DoesNotContain(StoredItems("Paper due by Sept 2026"), i => i.Date == new DateOnly(2026, 9, 20));
    }

    [Fact]
    public void SBV18_a_day_first_date_with_a_written_year_is_read_as_the_written_day_not_the_first_two_digits_of_the_year()
    {
        var item = Assert.Single(StoredItems("Quiz on 5 October 2026"));
        Assert.Equal(new DateOnly(2026, 10, 5), item.Date);                                      // was Oct 20, 2026
        Assert.Equal("explicit_full", item.Derivation);
        Assert.Equal("5 October 2026", item.DateText);
        Assert.DoesNotContain(StoredItems("Quiz on 5 October 2026", term: false), i => i.Date == new DateOnly(2026, 10, 20));
    }

    [Theory]
    [InlineData("Quiz Dec 123")]
    [InlineData("Quiz on Dec 1234")]
    [InlineData("Lab Oct 100 points")]
    public void SBV18_a_number_longer_than_two_digits_after_a_month_is_not_a_day(string raw)
    {
        Assert.DoesNotContain(SyllabusSignals.FindDates(raw), h => h.Style == "month");
        Assert.DoesNotContain(StoredItems(raw), i => i.Date is not null);                        // was Dec 12 / Dec 12 / Oct 10
    }

    [Theory]
    [InlineData("Students may 3 times retake the quiz")]
    [InlineData("Late homework may 2 days late be accepted for the lab")]
    [InlineData("Lab groups mar 2 desks per lab")]
    public void SBV18_the_verbs_may_and_mar_followed_by_a_number_are_not_dates(string raw)
    {
        Assert.Empty(SyllabusSignals.FindDates(raw));
        Assert.DoesNotContain(StoredItems(raw), i => i.Date is not null || i.UnknownReason is not null);
    }

    [Theory]
    [InlineData("Quiz on May 3", 5, 3)]
    [InlineData("Quiz on MAY 3", 5, 3)]
    [InlineData("Quiz on Mar 3", 3, 3)]
    [InlineData("Quiz on March 3", 3, 3)]
    [InlineData("Quiz on march 3", 3, 3)]
    [InlineData("Quiz on may 3rd", 5, 3)]
    [InlineData("Quiz on may 3, 2027", 5, 3)]
    public void SBV18_a_capitalised_may_or_mar_or_a_lowercase_one_with_an_ordinal_or_year_is_still_a_date(string raw, int month, int day)
    {
        var hit = Assert.Single(SyllabusSignals.FindDates(raw));
        Assert.Equal((month, day), SyllabusSignals.MonthDayOf(hit));
    }

    [Theory]
    [InlineData("Homework: problems 9/30")]
    [InlineData("Quiz worth 5/20 points")]
    [InlineData("Quiz worth 5/20 pts")]
    [InlineData("Lab score 8/13/20 points")]
    [InlineData("Read pages 9/30 of the lab manual")]
    [InlineData("Exam: questions 3/10 are optional")]
    [InlineData("Quiz worth 5/20%")]
    public void SBV18_a_score_or_a_problem_number_written_with_a_slash_is_not_a_date(string raw)
    {
        Assert.Empty(SyllabusSignals.FindDates(raw));
        Assert.DoesNotContain(StoredItems(raw), i => i.Date is not null);
    }

    [Theory]
    [InlineData("Homework due 9/30", 9, 30)]
    [InlineData("Problem set due 9/30", 9, 30)]
    [InlineData("Quiz on 9/30 worth 20 points", 9, 30)]
    public void SBV18_controls_a_slash_date_next_to_ordinary_words_is_still_read(string raw, int month, int day)
    {
        var hit = Assert.Single(SyllabusSignals.FindDates(raw));
        Assert.Equal((month, day), SyllabusSignals.MonthDayOf(hit));
    }

    [Theory]
    [InlineData("Midterm 2 Dec 2026")]
    [InlineData("Exam 3 May 2026")]
    [InlineData("Chapter 12 December 2026")]
    public void SBV18_a_label_number_before_a_month_and_year_is_not_read_as_a_day_first_date(string raw)
    {
        Assert.DoesNotContain(SyllabusSignals.FindDates(raw), h => h.Day is not null);
    }

    // ---------------------------------------------------------------- SBV-18: the property sweep

    private static readonly string[][] MonthNames =
    [
        ["January", "Jan", "Jan."], ["February", "Feb"], ["March", "Mar", "Mar."], ["April", "Apr"], ["May"], ["June", "Jun"], ["July", "Jul"],
        ["August", "Aug"], ["September", "Sept", "Sep", "Sept."], ["October", "Oct", "Oct."], ["November", "Nov"], ["December", "Dec"],
    ];

    private static string Ordinal(int d) => d + (d % 100 is 11 or 12 or 13 ? "th" : (d % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>(text, month, day or 0 when no day is written, year or 0 when none is written)</summary>
    private static IEnumerable<(string Text, int Month, int Day, int Year)> Written()
    {
        int[] days = [1, 2, 3, 9, 10, 11, 12, 19, 20, 21, 22, 23, 28, 29, 30, 31];
        int[] years = [2026, 2027, 2019, 2100];
        for (var m = 1; m <= 12; m++)
            foreach (var name in MonthNames[m - 1])
            {
                foreach (var y in years) yield return ($"{name} {y}", m, 0, y);                // Month YYYY: never a day
                foreach (var d in days)
                {
                    if (d > DateTime.DaysInMonth(2028, m)) continue;
                    foreach (var dt in new[] { d.ToString(CultureInfo.InvariantCulture), Ordinal(d) })
                    {
                        yield return ($"{name} {dt}", m, d, 0);
                        foreach (var y in years)
                        {
                            yield return ($"{name} {dt}, {y}", m, d, y);
                            yield return ($"{name} {dt} {y}", m, d, y);
                            yield return ($"{dt} {name} {y}", m, d, y);                     // D Month YYYY
                        }
                    }
                }
            }
    }

    [Fact]
    public void SBV18_sweep_every_written_month_day_and_year_is_read_exactly_as_written()
    {
        var failures = new List<string>();
        var n = 0;
        foreach (var (text, month, day, year) in Written())
            foreach (var prefix in new[] { "", "Quiz on ", "Final exam: ", "Paper due by " })
                foreach (var suffix in new[] { "", " at 10:00 AM", " (room 5)", "." })
                {
                    n++;
                    var line = prefix + text + suffix;
                    var hits = SyllabusSignals.FindDates(line);
                    if (day == 0)
                    {
                        if (hits.Any(h => h.Day is not null)) failures.Add($"{line}: read a day");
                        continue;
                    }
                    if (hits.Count != 1) { failures.Add($"{line}: {hits.Count} hits"); continue; }
                    var h = hits[0];
                    if (SyllabusSignals.MonthDayOf(h) != (month, day)) failures.Add($"{line}: read {h.Month}/{h.Day}");
                    if ((h.Year ?? 0) != year) failures.Add($"{line}: read year {h.Year}");
                    if (h.Day2 is not null) failures.Add($"{line}: read a range");
                    if (line.Substring(h.Index, h.Length) != h.Text) failures.Add($"{line}: span");
                }
        Assert.True(n > 20_000, $"sweep too small: {n}");
        Assert.True(failures.Count == 0, $"{failures.Count} of {n}: " + string.Join(" | ", failures.Take(25)));
    }

    [Fact]
    public void SBV18_sweep_the_mock_resolves_a_date_only_to_the_written_day_and_year()
    {
        var failures = new List<string>();
        foreach (var (text, month, day, year) in Written())
            foreach (var term in new[] { true, false })
            {
                var line = "Quiz on " + text;
                var input = Input(line, term);
                var o = X.Extract(input);
                var (kept, stored) = Store(input, o);
                if (kept.DroppedInvalid != o.DroppedInvalid) failures.Add($"{line}: the server dropped the mock's item");
                foreach (var i in stored.Where(i => i.Date is not null))
                {
                    var d = i.Date!.Value;
                    if (day == 0) { failures.Add($"{line}: resolved {d} with no day written"); continue; }
                    if ((d.Month, d.Day) != (month, day)) failures.Add($"{line}: resolved {d}");
                    if (year != 0 && d.Year != year) failures.Add($"{line}: resolved year {d.Year}");
                    if (year != 0 && i.Derivation != "explicit_full") failures.Add($"{line}: {i.Derivation}");
                    if (i.EndDate is not null) failures.Add($"{line}: end date {i.EndDate}");
                }
            }
        Assert.True(failures.Count == 0, $"{failures.Count}: " + string.Join(" | ", failures.Take(25)));
    }

    [Theory]
    [InlineData("Reading week Oct 12-16, 2026", 10, 12, 16, 2026)]
    [InlineData("Reading week Oct 12–16", 10, 12, 16, 0)]
    [InlineData("Thanksgiving break Nov 25-27", 11, 25, 27, 0)]
    [InlineData("Break Nov 25th-27th, 2026", 11, 25, 27, 2026)]
    public void SBV18_controls_a_forward_day_range_is_still_a_range(string raw, int month, int day, int day2, int year)
    {
        var h = Assert.Single(SyllabusSignals.FindDates(raw));
        Assert.Equal((month, day), SyllabusSignals.MonthDayOf(h));
        Assert.Equal(day2, h.Day2);
        Assert.Equal(year, h.Year ?? 0);
    }

    // ---------------------------------------------------------------- SBV-29: Unicode digits

    [Theory]
    [InlineData("Quiz Dec ９")]                      // fullwidth 9
    [InlineData("Quiz Dec ٩")]                      // Arabic-Indic 9
    [InlineData("Quiz Dec ۹, 2026")]                // extended Arabic-Indic 9
    [InlineData("Quiz Dec 9, ２０２６")] // a fullwidth year
    [InlineData("Quiz Dec 9-١٠")]              // an Arabic-Indic range end
    [InlineData("Quiz ９/１４")]              // a fullwidth numeric date
    [InlineData("Quiz 9/١٤/2026")]
    [InlineData("Quiz २०२०-१०-०५")]   // Devanagari ISO date
    [InlineData("Quiz ٩ Dec 2026")]                 // a day-first Arabic-Indic day
    [InlineData("Office hours Tuesdays ٢:00-٣:00 PM")]
    [InlineData("Quiz Dec 9 at １０:00 AM")]
    public void SBV29_a_non_ascii_digit_is_never_read_as_part_of_a_date_and_never_throws(string raw)
    {
        var hits = SyllabusSignals.FindDates(raw);
        foreach (var h in hits)
        {
            Assert.DoesNotContain(h.Text, c => char.IsDigit(c) && c is < '0' or > '9');
            _ = SyllabusSignals.MonthDayOf(h);
        }
        var input = Input(raw);
        var o = X.Extract(input);                                                                // used to throw FormatException
        var (kept, _) = Store(input, o);
        Assert.Equal(o.DroppedInvalid, kept.DroppedInvalid);
    }

    [Fact]
    public void SBV29_fuzz_thousands_of_random_lines_with_unicode_digits_never_throw_out_of_the_matcher_or_the_mock()
    {
        string[] pieces =
        [
            "Dec", "dec", "May", "may", "Mar", "Sept", "October", "Oct.", " ", "  ", ",", ", ", "-", "–", "/", ":", ".", "am", "pm", "a.m.", "th", "st",
            "points", "problems", "pts", "%", "due", "Quiz", "exam", "Office hours", "Tuesdays", "MWF", "on", "\n", "TBA", "2026", "20", "19", "21",
            "０", "１", "９", "٠", "١", "٩", "۰", "۹", "०", "९", "๑", "\U0001D7D9", "\U0001D7E0",
            "0", "1", "2", "3", "9", "12", "30", "31", "123", "x", "²", "⑨",
        ];
        char[] digits = ['0', '1', '2', '3', '9', '0', '1', '2', '０', '１', '９', '٠', '١', '٩', '۱', '०', '१', '๑'];
        string[] months = ["Dec", "dec", "May", "may", "Mar", "mar", "March", "Sept", "Sept.", "October", "Jan"];
        string[] lead = ["Quiz on ", "Final exam: ", "Paper due by ", "Office hours Tuesdays ", "Students ", "", "Fri ", "problems ", "worth "];
        string[] tail = ["", " at 10:00 AM", "am", " pm", ":30", " points", "%", " (room 5)", ", ", "-", "th"];
        var rng = new Random(18_29_30);
        string Num(int maxLen)
        {
            var len = rng.Next(1, maxLen + 1);
            var s = new StringBuilder();
            for (var i = 0; i < len; i++) s.Append(digits[rng.Next(digits.Length)]);
            return s.ToString();
        }
        string Structured() => rng.Next(6) switch
        {
            0 => $"{lead[rng.Next(lead.Length)]}{months[rng.Next(months.Length)]} {Num(3)}{tail[rng.Next(tail.Length)]}",
            1 => $"{lead[rng.Next(lead.Length)]}{months[rng.Next(months.Length)]} {Num(2)}-{Num(2)}{tail[rng.Next(tail.Length)]}",
            2 => $"{lead[rng.Next(lead.Length)]}{months[rng.Next(months.Length)]} {Num(2)}, {Num(4)}{tail[rng.Next(tail.Length)]}",
            3 => $"{lead[rng.Next(lead.Length)]}{Num(2)} {months[rng.Next(months.Length)]} {Num(4)}{tail[rng.Next(tail.Length)]}",
            4 => $"{lead[rng.Next(lead.Length)]}{Num(2)}/{Num(2)}{(rng.Next(2) == 0 ? "/" + Num(4) : "")}{tail[rng.Next(tail.Length)]}",
            _ => $"{lead[rng.Next(lead.Length)]}{Num(4)}-{Num(2)}-{Num(2)} {Num(2)}:{Num(2)}{tail[rng.Next(tail.Length)]}",
        };
        var sb = new StringBuilder();
        var hitsSeen = 0;
        for (var n = 0; n < 6000; n++)
        {
            sb.Clear();
            if (n % 3 == 0)
            {
                var len = rng.Next(1, 24);
                for (var k = 0; k < len; k++) sb.Append(pieces[rng.Next(pieces.Length)]);
            }
            else for (var k = rng.Next(1, 4); k > 0; k--) sb.Append(Structured()).Append(rng.Next(4) == 0 ? "\n" : " ");
            var raw = sb.ToString();
            try
            {
                foreach (var h in SyllabusSignals.FindDates(raw))
                {
                    hitsSeen++;
                    Assert.DoesNotContain(h.Text, c => char.IsDigit(c) && c is < '0' or > '9');
                    _ = SyllabusSignals.MonthDayOf(h);
                }
                var input = Input(raw);
                var o = X.Extract(input);
                var (kept, stored) = Store(input, o);
                Assert.Equal(o.DroppedInvalid, kept.DroppedInvalid);                             // the server keeps everything the mock proposes
                foreach (var i in stored) Assert.True(i.EndDate is null || i.EndDate > i.Date, $"reversed range stored for {raw}");
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                Assert.Fail($"{e.GetType().Name} for input {string.Join(" ", raw.Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture)))}");
            }
        }
        Assert.True(hitsSeen > 1000,$"the fuzz should reach the matcher: {hitsSeen} hits");
    }

    // ---------------------------------------------------------------- SBV-30: range ends

    [Theory]
    [InlineData("Reading week Oct 16-12", 10, 16)]
    [InlineData("Final exam: Dec 10-9", 12, 10)]
    [InlineData("Final exam: Dec 10-10", 12, 10)]
    [InlineData("Break Nov 27th-25th, 2026", 11, 27)]
    public void SBV30_a_reversed_or_empty_range_is_not_a_range(string raw, int month, int day)
    {
        var h = SyllabusSignals.FindDates(raw).First();
        Assert.Equal((month, day), SyllabusSignals.MonthDayOf(h));
        if (h.Day2 is { } d2) Assert.True(d2 > day, $"day2 {d2} must be after day {day}");
    }

    [Theory]
    [InlineData("Reading week Oct 16-12, 2026", 2026, 10, 16, 12)]
    [InlineData("Final exam: Dec 10-9", 2026, 12, 10, 9)]
    [InlineData("Final exam: Dec 10-10", 2026, 12, 10, 10)]
    public void SBV30_a_claimed_end_date_that_is_not_after_the_date_is_dropped_and_counted_not_failing_the_syllabus(string raw, int y, int m, int d, int endDay)
    {
        var input = Input(raw);
        var o = X.Extract(input);
        var mock = Assert.Single(o.Items);
        var steered = mock with
        {
            WhenStatus = "resolved", Date = new DateOnly(y, m, d), EndDate = new DateOnly(y, m, endDay), UnknownReason = null, Alternatives = [],
            Timezone = "America/New_York", TimezoneSource = "course_default", AllDay = true, Derivation = mock.Derivation ?? "year_from_term",
        };
        var r = SyllabusLimits.Apply(o with { Items = [steered] }, maxItems: 200, input.Text);
        Assert.Empty(r.Items);                                                                   // would break ck_items_end_date (end >= date)
        Assert.Equal(o.DroppedInvalid + 1, r.DroppedInvalid);
        Assert.Contains(r.Unclaimed, u => u.Text == mock.Snippet.Text);
    }

    [Theory]
    [InlineData("Quiz Dec 9-10am", 9, 0, 10, 0)]
    [InlineData("Quiz Dec 9-10 am", 9, 0, 10, 0)]
    [InlineData("Quiz Dec 9-10 a.m.", 9, 0, 10, 0)]
    [InlineData("Quiz Dec 9 - 11pm", 21, 0, 23, 0)]
    [InlineData("Quiz Dec 9-10:30", -1, 0, -1, 0)]
    [InlineData("Quiz Dec 9-10:30 AM", 9, 0, 10, 30)]
    public void SBV30_a_time_after_a_hyphen_is_a_time_range_not_a_date_range(string raw, int h1, int m1, int h2, int m2)
    {
        var hit = SyllabusSignals.FindDates(raw).First();
        Assert.Equal((12, 9), SyllabusSignals.MonthDayOf(hit));
        Assert.Null(hit.Day2);
        var item = Assert.Single(StoredItems(raw));
        Assert.Equal(new DateOnly(2026, 12, 9), item.Date);
        Assert.Null(item.EndDate);                                                               // was Dec 9 to Dec 10
        if (h1 >= 0)
        {
            Assert.Equal(new TimeOnly(h1, m1), item.Time);
            Assert.Equal(new TimeOnly(h2, m2), item.EndTime);
        }

        // a steered extractor that claims the two-day range is dropped and counted
        var input = Input(raw);
        var o = X.Extract(input);
        var claim = o.Items.Single() with { EndDate = new DateOnly(2026, 12, 10) };
        var r = SyllabusLimits.Apply(o with { Items = [claim] }, 200, input.Text);
        Assert.Empty(r.Items);
        Assert.Equal(o.DroppedInvalid + 1, r.DroppedInvalid);
    }
}
