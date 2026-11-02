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

    // SBV-32 (Lead ruling, 2026-11-02): these rows used to expect the date's day read as a start hour ('Quiz Dec 9-10am' = 09:00-10:00,
    // 'Quiz Dec 9 - 11pm' = 21:00-23:00, 'Quiz Dec 9-10:30 AM' = 09:00-10:30). That was the misread itself. The time is read only from the
    // text after the date's own text ('Dec 9'), so each is ONE time with no end time: 10:00, 23:00, 10:30. No start-end range is inferred.
    [Theory]
    [InlineData("Quiz Dec 9-10am", 10, 0)]
    [InlineData("Quiz Dec 9-10 am", 10, 0)]
    [InlineData("Quiz Dec 9-10 a.m.", 10, 0)]
    [InlineData("Quiz Dec 9 - 11pm", 23, 0)]
    [InlineData("Quiz Dec 9-10:30", 10, 30)]
    [InlineData("Quiz Dec 9-10:30 AM", 10, 30)]
    public void SBV30_a_time_after_a_hyphen_is_a_time_not_a_date_range(string raw, int h1, int m1)
    {
        var hit = SyllabusSignals.FindDates(raw).First();
        Assert.Equal((12, 9), SyllabusSignals.MonthDayOf(hit));
        Assert.Null(hit.Day2);
        var item = Assert.Single(StoredItems(raw));
        Assert.Equal(new DateOnly(2026, 12, 9), item.Date);
        Assert.Null(item.EndDate);                                                               // was Dec 9 to Dec 10
        Assert.Equal(new TimeOnly(h1, m1), item.Time);
        Assert.Null(item.EndTime);                                                               // SBV-32: the day 9 is not a start hour

        // a steered extractor that claims the two-day range is dropped and counted
        var input = Input(raw);
        var o = X.Extract(input);
        var claim = o.Items.Single() with { EndDate = new DateOnly(2026, 12, 10) };
        var r = SyllabusLimits.Apply(o with { Items = [claim] }, 200, input.Text);
        Assert.Empty(r.Items);
        Assert.Equal(o.DroppedInvalid + 1, r.DroppedInvalid);
    }

    // ---------------------------------------------------------------- SBV-32: the time is read only after the date

    [Theory]
    [InlineData("Homework due Dec 9 - 1pm", 9, 13, 0)]                                         // was 21:00 (the day 9 read as 9pm)
    [InlineData("Homework due Dec 10 - 2:00 PM", 10, 14, 0)]                                   // was 22:00
    [InlineData("Paper due Dec 11 - 9am", 11, 9, 0)]                                           // was 11:00
    [InlineData("Quiz Dec 9-10am", 9, 10, 0)]                                                  // was 09:00-10:00
    [InlineData("Homework due Dec 9 at 1pm", 9, 13, 0)]
    [InlineData("Lab 3 report due Dec 9 at 5 PM", 9, 17, 0)]                                   // a number before the date is not read
    public void SBV32_the_time_is_the_clock_time_written_after_the_date(string raw, int day, int h, int m)
    {
        var item = Assert.Single(StoredItems(raw));
        Assert.Equal(new DateOnly(2026, 12, day), item.Date);
        Assert.Equal(new TimeOnly(h, m), item.Time);
        Assert.Null(item.EndTime);
        Assert.False(item.AllDay);
    }

    [Theory]
    [InlineData("Exam Dec 9 Building 3 Amos Hall")]
    [InlineData("Exam Dec 9 in room 3 Pmb")]
    [InlineData("Exam Dec 9 section 3 a.mixed")]
    public void SBV32_am_or_pm_followed_by_a_letter_is_not_a_meridiem(string raw)
    {
        var item = Assert.Single(StoredItems(raw));
        Assert.Equal(new DateOnly(2026, 12, 9), item.Date);
        Assert.Null(item.Time);                                                                  // was 03:00 / 15:00
        Assert.True(item.AllDay);
    }

    [Fact]
    public void SBV32_a_weekly_line_with_a_word_starting_with_am_has_no_time_and_no_recurrence()
    {
        var input = Input("Office hours Tuesdays in Building 3 Amos Hall");
        Assert.DoesNotContain(X.Extract(input).Items, i => i.Recurrence is not null);           // was weekly at 03:00
    }

    [Theory]
    [InlineData("Office hours Tuesdays 2-3pm", 14, 15)]
    [InlineData("Office hours Tuesdays 2:00-3:00 PM", 14, 15)]
    public void SBV32_controls_a_weekly_time_range_with_no_date_is_still_read(string raw, int h1, int h2)
    {
        var rec = Assert.Single(X.Extract(Input(raw)).Items).Recurrence!;
        Assert.Equal(new TimeOnly(h1, 0), rec.Start);
        Assert.Equal(new TimeOnly(h2, 0), rec.End);
    }

    [Fact]
    public void SBV32_controls_a_time_range_written_after_the_date_is_still_a_range()
    {
        var item = Assert.Single(StoredItems("Final exam Dec 9 2-4pm"));
        Assert.Equal(new TimeOnly(14, 0), item.Time);
        Assert.Equal(new TimeOnly(16, 0), item.EndTime);
        item = Assert.Single(StoredItems("Reading week Oct 12-16 10am-noon"));
        Assert.Equal(new DateOnly(2026, 10, 16), item.EndDate);
    }

    /// <summary>(written clock text, start, end or null)</summary>
    private static IEnumerable<(string Text, TimeOnly Start, TimeOnly? End)> Clocks()
    {
        foreach (var h in new[] { 1, 2, 5, 9, 10, 11, 12 })
            foreach (var (ap, pm) in new[] { ("am", false), ("pm", true), (" AM", false), (" PM", true), ("a.m.", false), (" p.m.", true) })
            {
                var hh = pm ? (h == 12 ? 12 : h + 12) : (h == 12 ? 0 : h);
                yield return ($"{h}{ap}", new TimeOnly(hh, 0), null);
                yield return ($"{h}:30{ap}", new TimeOnly(hh, 30), null);
            }
        foreach (var (h, m) in new[] { (0, 15), (8, 0), (9, 45), (13, 5), (17, 30), (23, 59) })
            yield return ($"{h}:{m:00}", new TimeOnly(h, m), null);
        yield return ("2-4pm", new TimeOnly(14, 0), new TimeOnly(16, 0));
        yield return ("10:00-11:15 AM", new TimeOnly(10, 0), new TimeOnly(11, 15));
    }

    [Fact]
    public void SBV32_sweep_a_resolved_time_always_equals_the_clock_time_written_after_the_date()
    {
        var failures = new List<string>();
        var n = 0;
        var dates = new List<(string Text, DateOnly Date)>();
        for (var d = 1; d <= 31; d++) dates.Add(($"Oct {d}", new DateOnly(2026, 10, d)));                 // every day, so every hour 1-12 is a day
        for (var d = 1; d <= 11; d++) dates.Add(($"Dec {d}, 2026", new DateOnly(2026, 12, d)));
        dates.Add(("September 9th", new DateOnly(2026, 9, 9)));
        dates.Add(("Nov. 12", new DateOnly(2026, 11, 12)));
        string[] prefixes = ["Quiz ", "Homework due ", "Lab 3 report due ", "Exam 2 (room 101) on "];
        string[] separators = [" at ", " - ", " ", ", ", "-", " from "];
        foreach (var (dateText, date) in dates)
            foreach (var (clock, start, end) in Clocks())
                foreach (var sep in separators)
                {
                    if (sep == "-" && (end is not null || dateText.EndsWith("2026", StringComparison.Ordinal))) continue;   // 'Dec 9-2-4pm' is not a way to write it
                    var prefix = prefixes[n % prefixes.Length];
                    n++;
                    var line = prefix + dateText + sep + clock;
                    var items = StoredItems(line);
                    if (items.Count != 1) { failures.Add($"{line}: {items.Count} items"); continue; }
                    var i = items[0];
                    if (i.Date != date) failures.Add($"{line}: date {i.Date}");
                    if (i.Time != start) failures.Add($"{line}: time {i.Time}");
                    if (i.EndTime != end) failures.Add($"{line}: end time {i.EndTime}");
                    if (i.EndDate is not null) failures.Add($"{line}: end date {i.EndDate}");
                }
        Assert.True(n > 5_000, $"sweep too small: {n}");
        Assert.True(failures.Count == 0, $"{failures.Count} of {n}: " + string.Join(" | ", failures.Take(25)));
    }

    // ---------------------------------------------------------------- SBV-34: a comma is not a day-first date context

    [Theory]
    [InlineData("Read chapters 4, 5 May 2026")]
    [InlineData("Homework sets 1, 2 Dec 2026")]
    public void SBV34_a_list_number_after_a_comma_is_not_a_day(string raw)
    {
        Assert.DoesNotContain(SyllabusSignals.FindDates(raw), h => h.Day is not null);
        Assert.DoesNotContain(StoredItems(raw), i => i.Date is not null);                        // was May 5 / Dec 2, 2026 looks_right
    }

    [Theory]
    [InlineData("Quiz on Fri, 4 Dec 2026", 12, 4)]
    [InlineData("Quiz: 4 Dec 2026", 12, 4)]
    [InlineData("Quiz (4 Dec 2026)", 12, 4)]
    [InlineData("Quiz due 4 Dec 2026", 12, 4)]
    public void SBV34_controls_a_day_first_date_in_a_date_context_is_still_read(string raw, int month, int day)
    {
        var hit = Assert.Single(SyllabusSignals.FindDates(raw));
        Assert.Equal((month, day), SyllabusSignals.MonthDayOf(hit));
        Assert.Equal(2026, hit.Year);
    }

    // ---------------------------------------------------------------- SBV-33: a lowercase may/mar after a date word is a month

    [Theory]
    [InlineData("quiz on may 3", 5, 3)]
    [InlineData("exam due may 3", 5, 3)]
    [InlineData("quiz on mar. 3", 3, 3)]
    [InlineData("Paper due by may 3", 5, 3)]
    [InlineData("Paper due before mar 3", 3, 3)]
    [InlineData("Late labs accepted until may 3", 5, 3)]
    [InlineData("Exam window from may 3", 5, 3)]
    [InlineData("Reading through mar 3", 3, 3)]
    [InlineData("quiz On may 3", 5, 3)]
    public void SBV33_a_lowercase_may_or_mar_right_after_a_date_word_is_a_month(string raw, int month, int day)
    {
        var hit = Assert.Single(SyllabusSignals.FindDates(raw));
        Assert.Equal((month, day), SyllabusSignals.MonthDayOf(hit));
        var input = Input(raw, term: false);
        var o = X.Extract(input);
        Assert.True(o.Items.Count + o.Unclaimed.Count > 0, "the deadline must not be silent");    // was no item, no unclaimed date, no count
    }

    [Theory]
    [InlineData("Students may 3 times retake the quiz")]
    [InlineData("Late homework may 2 days late be accepted for the lab")]
    [InlineData("Lab groups mar 2 desks per lab")]
    [InlineData("Quizzes on time may 3 points earn")]
    [InlineData("Exam: you may 3 times ask")]
    [InlineData("Homework due soon may 2 days slip")]
    public void SBV33_controls_a_lowercase_may_or_mar_without_a_date_word_right_before_it_is_still_not_a_month(string raw)
    {
        Assert.Empty(SyllabusSignals.FindDates(raw));
    }
}
