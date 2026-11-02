using Tether.Syllabus.Extraction;
using Tether.Syllabus.Text;

namespace Tether.SyllabusTests;

/// <summary>
/// SBV-12 (docs/ai/syllabus-extraction-design.md section 17, condition 5): the server does not trust the extractor's RESOLVED date. After the
/// quote and context are fixed (SBV-01, SBV-06) and before the flags are re-derived (SBV-10), every item's date fields are checked against the
/// stored quote with the shared matchers (<see cref="SyllabusSignals.FindDates"/>, <see cref="SyllabusSignals.MonthDayOf"/>):
/// (a) a resolved single date's month and day (and its year, when a year is written) are a date written in the quote; (b) <c>explicit_full</c>
/// needs a year written in the quote; (c) an ambiguous numeric date ("3/4") is never resolved; (d) a recurrence needs its weekdays written in
/// the quote and no written date (a written date is a single occurrence, as in the mock). Lead ruling: a mismatch is an invalid model-written
/// field, so the item is dropped and counted in droppedInvalid, and its verbatim snippet stays visible as an unclaimed date (the SBR-06
/// demotion path). The claimed date is never stored and never corrected. The mock's own output is unchanged.
/// </summary>
public class ServerDatesTests
{
    private static readonly MockRuleExtractor X = new();
    private static readonly DateOnly TermStart = new(2026, 8, 24), TermEnd = new(2026, 12, 11);

    private static ExtractorInput Input(string raw, bool term = true, bool hidden = false)
    {
        var doc = DocumentText.Canonicalize(raw);
        return new ExtractorInput(doc.Text, term ? TermStart : null, term ? TermEnd : null, "America/New_York", hidden, doc.InjectionSuspect);
    }

    /// <summary>The orchestrator's path (Jobs.PersistAsync): the SBV-01/06/12 backstop, then the SBV-10 re-derivation of every kept item.</summary>
    private static IReadOnlyList<ProposedItem> Store(ExtractorInput input, ExtractorOutput o)
    {
        var kept = SyllabusLimits.Apply(o, maxItems: 200, input.Text);
        var doc = ServerFlags.Document(input);
        return [.. kept.Items.Select(p => ServerFlags.Rederive(p, input, doc))];
    }

    /// <summary>A steered extractor that resolves the item to the given date, whatever the quote says.</summary>
    private static ProposedItem Resolve(ProposedItem i, DateOnly date, DateOnly? endDate, string derivation) => i with
    {
        WhenStatus = "resolved", Date = date, EndDate = endDate, Derivation = derivation, UnknownReason = null, Alternatives = [], Recurrence = null,
        Timezone = "America/New_York", TimezoneSource = "course_default", AllDay = i.Time is null,
    };

    private static ProposedSnippet Narrowed(string canon, string needle, int from = 0)
    {
        var s = canon.IndexOf(needle, from, StringComparison.Ordinal);
        Assert.True(s >= 0, "fixture must contain the needle");
        return new ProposedSnippet(s, s + needle.Length, needle, s, s + needle.Length, needle, "exact");
    }

    // ---------------------------------------------------------------- the attack in the finding

    [Fact]
    public void SBV12_a_verbatim_quote_of_Dec_9_with_a_claimed_Dec_19_is_dropped_counted_and_kept_only_as_an_unclaimed_date()
    {
        var input = Input("Midterm exam: Oct 19, 2026 at 10:00 AM\nFinal exam: Dec 9, 2026");
        var o = X.Extract(input);
        Assert.Equal(0, o.DroppedInvalid);
        var final = o.Items.Single(i => i.Snippet.Text.StartsWith("Final", StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2026, 12, 9), final.Date);                                     // the mock reads what is written
        var steered = final with { Date = new DateOnly(2026, 12, 19), Confidence = "looks_right" };

        var kept = SyllabusLimits.Apply(o with { Items = [.. o.Items.Select(i => i == final ? steered : i)] }, maxItems: 200, input.Text);
        Assert.Equal(1, kept.DroppedInvalid);                                                    // counted, not silent
        var item = Assert.Single(kept.Items);
        Assert.StartsWith("Midterm", item.Snippet.Text, StringComparison.Ordinal);               // the honest neighbour is untouched
        var trace = Assert.Single(kept.Unclaimed);                                               // SBR-06 demotion: the quote stays visible
        Assert.Equal("Final exam: Dec 9, 2026", trace.Text);
        Assert.Equal((final.Snippet.Start, final.Snippet.End), (trace.Start, trace.End));
        Assert.Equal(input.Text.Substring(trace.Start, trace.End - trace.Start), trace.Text);

        var stored = Store(input, o with { Items = [.. o.Items.Select(i => i == final ? steered : i)] });
        Assert.DoesNotContain(stored, i => i.Date == new DateOnly(2026, 12, 19));                 // the claimed date is never stored
        Assert.DoesNotContain(stored, i => i.Date == new DateOnly(2026, 12, 9));                  // and never silently corrected
    }

    // ---------------------------------------------------------------- (a) (b) (c): a single resolved date

    /// <summary>(line, claimed year, month, day, claimed end day or 0, derivation, kept?)</summary>
    public static TheoryData<string, int, int, int, int, string, bool> SingleDates => new()
    {
        // (a) month and day, and the year when one is written
        { "Final exam: Dec 9, 2026", 2026, 12, 19, 0, "explicit_full", false },
        { "Final exam: Dec 9, 2026", 2026, 12, 9, 0, "explicit_full", true },
        { "Final exam: Dec 9, 2026", 2026, 11, 9, 0, "explicit_full", false },
        { "Final exam: Dec 9, 2026", 2027, 12, 9, 0, "explicit_full", false },
        { "Final exam: Dec 9, 2026", 2026, 12, 9, 0, "year_from_term", true },                // a weaker claim than the text allows is not a forgery
        { "Final exam: Dec 9, 2026", 2027, 12, 9, 0, "year_from_term", false },               // but a written year must still agree
        { "Final exam: Dec 19, 2026", 2026, 12, 1, 0, "explicit_full", false },
        { "Midterm exam: October 5, 2026", 2026, 10, 5, 0, "explicit_full", true },
        { "Midterm exam: October 5, 2026", 2026, 10, 15, 0, "explicit_full", false },
        { "Midterm exam: Oct. 5th, 2026", 2026, 10, 5, 0, "explicit_full", true },
        { "Midterm exam: Oct 5th", 2026, 10, 5, 0, "year_from_term", true },
        { "Midterm exam: Oct 5th", 2026, 10, 6, 0, "year_from_term", false },
        { "Quiz on 10/15", 2026, 10, 15, 0, "year_from_term", true },
        { "Quiz on 10/15", 2026, 10, 16, 0, "year_from_term", false },
        { "Quiz on 10/15", 2026, 1, 5, 0, "year_from_term", false },
        { "Quiz on 10/15/2026", 2026, 10, 15, 0, "explicit_full", true },
        { "Quiz on 10/15/26", 2026, 10, 15, 0, "explicit_full", true },                       // the shared matcher reads "26" as 2026, as the mock does
        { "Quiz on 10/15/26", 2027, 10, 15, 0, "explicit_full", false },
        { "Quiz on 2026-10-05", 2026, 10, 5, 0, "explicit_full", true },
        { "Quiz on 2026-10-05", 2026, 10, 6, 0, "explicit_full", false },
        { "Quiz 2 due Oct 21, 2026, also Nov 3, 2026", 2026, 11, 3, 0, "explicit_full", true }, // a date written in the quote, not only the first
        { "Quiz 2 due Oct 21, 2026, also Nov 3, 2026", 2026, 11, 21, 0, "explicit_full", false },
        // (b) explicit_full needs a written year
        { "Final exam: Dec 9", 2026, 12, 9, 0, "explicit_full", false },
        { "Quiz on 10/15", 2026, 10, 15, 0, "explicit_full", false },
        { "Midterm exam: Oct 5th", 2026, 10, 5, 0, "explicit_full", false },
        // (c) an ambiguous numeric date is never resolved, under either reading or with a year
        { "Project due 3/4", 2026, 3, 4, 0, "year_from_term", false },
        { "Project due 3/4", 2026, 4, 3, 0, "year_from_term", false },
        { "Project due 10/5", 2026, 10, 5, 0, "year_from_term", false },
        { "Project due 10/5", 2026, 5, 10, 0, "year_from_term", false },
        { "Project due 3/4/2026", 2026, 3, 4, 0, "explicit_full", false },
        { "Project due 14/9", 2026, 9, 14, 0, "year_from_term", false },                      // never guessed day-first
        // a range, as the mock reads it: the start is the date, the written second day the end
        { "Reading week Oct 12-16, 2026", 2026, 10, 12, 16, "explicit_full", true },
        { "Reading week Oct 12-16, 2026", 2026, 10, 12, 0, "explicit_full", true },
        { "Reading week Oct 12-16, 2026", 2026, 10, 12, 17, "explicit_full", false },
        { "Reading week Oct 12-16, 2026", 2026, 10, 13, 16, "explicit_full", false },
        { "Reading week Oct 12-16, 2026", 2026, 10, 16, 0, "explicit_full", false },
        { "Reading week Oct 12–16", 2026, 10, 12, 16, "year_from_term", true },
        { "Final exam: Dec 9, 2026", 2026, 12, 9, 10, "explicit_full", false },               // an end date where none is written
        // a derivation the server cannot check against the quote fails closed
        { "Final exam: two weeks after the midterm on Oct 19, 2026", 2026, 11, 2, 0, "relative_to_anchor", false },
    };

    [Theory]
    [MemberData(nameof(SingleDates))]
    public void SBV12_a_resolved_single_date_must_be_a_date_written_in_the_quote(string raw, int y, int m, int d, int endDay, string derivation, bool kept)
    {
        var input = Input(raw);
        var o = X.Extract(input);
        var mock = Assert.Single(o.Items);
        var claim = new DateOnly(y, m, d);
        var steered = Resolve(mock, claim, endDay == 0 ? null : new DateOnly(y, m, endDay), derivation);

        var result = SyllabusLimits.Apply(o with { Items = [steered] }, maxItems: 200, input.Text);
        if (kept)
        {
            Assert.Equal(claim, Assert.Single(result.Items).Date);
            Assert.Equal(o.DroppedInvalid, result.DroppedInvalid);
            return;
        }
        Assert.Empty(result.Items);
        Assert.Equal(o.DroppedInvalid + 1, result.DroppedInvalid);
        Assert.Contains(result.Unclaimed, u => (u.Start, u.End) == (mock.Snippet.Start, mock.Snippet.End) && u.Text == mock.Snippet.Text);
    }

    [Fact]
    public void SBV12_a_date_counts_only_when_it_is_written_whole_inside_the_quote()
    {
        // "Dec 1" cut out of "Dec 19, 2026": the quote shows a date that is not the one written
        var input = Input("Final exam: Dec 19, 2026");
        var o = X.Extract(input);
        var mock = o.Items.Single();
        var cut = Resolve(mock, new DateOnly(2026, 12, 1), null, "year_from_term") with { Snippet = Narrowed(input.Text, "Final exam: Dec 1") };
        Assert.Empty(SyllabusLimits.Apply(o with { Items = [cut] }, 200, input.Text).Items);

        // the date is in the context but not in the quote
        var input2 = Input("Final exam: Dec 9, 2026");
        var o2 = X.Extract(input2);
        var noDate = o2.Items.Single() with { Snippet = Narrowed(input2.Text, "Final exam") };
        var r2 = SyllabusLimits.Apply(o2 with { Items = [noDate] }, 200, input2.Text);
        Assert.Empty(r2.Items);
        Assert.Equal(1, r2.DroppedInvalid);
        Assert.Equal("Final exam", Assert.Single(r2.Unclaimed).Text);

        // the quote cuts the written date "Dec 9, 2026" to "Dec 9": the date as written is not whole in the quote, whatever the derivation
        var full = o2.Items.Single() with { Snippet = Narrowed(input2.Text, "Dec 9") };
        Assert.Empty(SyllabusLimits.Apply(o2 with { Items = [full] }, 200, input2.Text).Items);
        Assert.Empty(SyllabusLimits.Apply(o2 with { Items = [full with { Derivation = "year_from_term" }] }, 200, input2.Text).Items);
        // the whole written date as the quote is enough
        Assert.Single(SyllabusLimits.Apply(o2 with { Items = [o2.Items.Single() with { Snippet = Narrowed(input2.Text, "Dec 9, 2026") }] }, 200, input2.Text).Items);
    }

    // ---------------------------------------------------------------- (d) recurrence

    private static ProposedRecurrence Weekly(string[] byDay, DateOnly from) => new(byDay, new TimeOnly(14, 0), new TimeOnly(15, 0), from, TermEnd, "term_end");

    [Fact]
    public void SBV12_a_single_written_date_presented_as_a_weekly_recurrence_is_dropped()
    {
        var input = Input("Final exam: Wed Dec 9, 2026 at 2:00 PM");                            // Dec 9, 2026 is a Wednesday
        var o = X.Extract(input);
        var mock = o.Items.Single();
        Assert.Null(mock.Recurrence);
        var weekly = mock with { Recurrence = Weekly(["WE"], new DateOnly(2026, 12, 9)), Derivation = "year_from_term" };
        var r = SyllabusLimits.Apply(o with { Items = [weekly] }, 200, input.Text);
        Assert.Empty(r.Items);
        Assert.Equal(1, r.DroppedInvalid);
        Assert.Equal(mock.Snippet.Text, Assert.Single(r.Unclaimed).Text);

        // also with the date kept as written (explicit_full): a written date is a single occurrence
        Assert.Empty(SyllabusLimits.Apply(o with { Items = [mock with { Recurrence = Weekly(["WE"], new DateOnly(2026, 12, 9)) }] }, 200, input.Text).Items);
    }

    [Fact]
    public void SBV12_a_recurrence_needs_its_weekdays_written_in_the_quote_and_starts_on_one_of_them()
    {
        var input = Input("Office hours Tuesdays 2:00-3:00 PM");
        var o = X.Extract(input);
        var mock = o.Items.Single();
        Assert.Equal(["TU"], mock.Recurrence!.ByDay);
        Assert.Single(SyllabusLimits.Apply(o, 200, input.Text).Items);                           // the mock's own item: kept

        var monday = new DateOnly(2026, 8, 24);
        foreach (var bad in new[]
        {
            mock with { Recurrence = mock.Recurrence with { ByDay = ["MO"], From = monday }, Date = monday },        // a weekday not written
            mock with { Recurrence = mock.Recurrence with { ByDay = ["TU", "TH"] } },                               // one written, one not
            mock with { Recurrence = mock.Recurrence with { ByDay = [] } },                                         // no weekday at all
            mock with { Recurrence = mock.Recurrence with { From = new DateOnly(2026, 8, 26) }, Date = new DateOnly(2026, 8, 26) },   // starts on a Wednesday
            mock with { Date = new DateOnly(2026, 9, 1) },                                                          // when.date is not the first occurrence
        })
        {
            var r = SyllabusLimits.Apply(o with { Items = [bad] }, 200, input.Text);
            Assert.Empty(r.Items);
            Assert.Equal(1, r.DroppedInvalid);
            Assert.Equal(mock.Snippet.Text, Assert.Single(r.Unclaimed).Text);
        }

        // a line with a time and no weekday is not a recurrence
        var input2 = Input("Office hours 2:00-3:00 PM in room 5");
        var o2 = X.Extract(input2);
        Assert.Empty(o2.Items);                                                                  // the mock does not make one
        var forged = mock with { Snippet = Narrowed(input2.Text, "Office hours 2:00-3:00 PM in room 5") };
        Assert.Empty(SyllabusLimits.Apply(o2 with { Items = [forged] }, 200, input2.Text).Items);
    }

    [Theory]
    [InlineData("Office hours Tuesdays 2:00-3:00 PM", new[] { "TU" })]
    [InlineData("Lecture MWF 10:00 AM", new[] { "MO", "WE", "FR" })]
    [InlineData("Lecture TR 9:30 AM", new[] { "TU", "TH" })]
    [InlineData("Office hours: Mon and Wed 1:00-2:00 PM", new[] { "MO", "WE" })]
    public void SBV12_the_mocks_recurrences_are_kept(string raw, string[] byDay)
    {
        var input = Input(raw);
        var o = X.Extract(input);
        var mock = o.Items.Single();
        Assert.Equal(byDay, mock.Recurrence!.ByDay);
        var r = SyllabusLimits.Apply(o, 200, input.Text);
        Assert.Equal(0, r.DroppedInvalid);
        Assert.Equal(mock with { Snippet = r.Items.Single().Snippet }, r.Items.Single());
    }

    [Fact]
    public void SBV12_known_mock_edge_a_recurrence_whose_weekday_lies_beyond_the_mocks_600_code_point_quote_is_demoted_and_counted()
    {
        // the mock quotes the first 580 UTF-16 units of a long dateless line, so "Tuesdays" at 700 is not in its own quote: the server cannot
        // tie the rule to the quote and demotes it (counted, quote kept). Recorded, not changed: no existing fixture has such a line.
        var input = Input(new string('z', 700) + " Office hours Tuesdays 2:00-3:00 PM");
        var o = X.Extract(input);
        var mock = o.Items.Single();
        Assert.NotNull(mock.Recurrence);
        Assert.DoesNotContain("Tuesdays", mock.Snippet.Text, StringComparison.Ordinal);
        var r = SyllabusLimits.Apply(o, 200, input.Text);
        Assert.Empty(r.Items);
        Assert.Equal(1, r.DroppedInvalid);
        Assert.Equal(mock.Snippet.Text, Assert.Single(r.Unclaimed).Text);
    }

    // ---------------------------------------------------------------- what is not touched

    [Fact]
    public void SBV12_needs_date_and_policy_items_are_not_date_checked_and_a_demoted_span_is_not_duplicated()
    {
        var input = Input("Final exam: TBA\nLate work policy: 10% per day.\nProject due 3/4\nQuiz due Fri Sept 14, 2026");
        var o = X.Extract(input);
        Assert.Equal(4, o.Items.Count);
        var r = SyllabusLimits.Apply(o, 200, input.Text);
        Assert.Equal(4, r.Items.Count);
        Assert.Equal(0, r.DroppedInvalid);

        // the same span offered twice (an unclaimed date and a forged item over it) leaves one unclaimed date, and the item is counted
        var input2 = Input("See you Dec 9, 2026");
        var o2 = X.Extract(input2);
        var u = Assert.Single(o2.Unclaimed);
        var item = Resolve(X.Extract(Input("Final exam: Dec 9, 2026")).Items.Single(), new DateOnly(2026, 12, 19), null, "explicit_full")
            with { Snippet = u };
        var r2 = SyllabusLimits.Apply(o2 with { Items = [item] }, 200, input2.Text);
        Assert.Empty(r2.Items);
        Assert.Equal(1, r2.DroppedInvalid);
        Assert.Single(r2.Unclaimed);
    }

    // ---------------------------------------------------------------- the mock is unchanged

    private static readonly string[] SweepLines =
    [
        "Midterm exam: Oct 5", "Midterm exam: October 5, 2026", "Quiz on Oct. 5th", "Quiz on Sept 30th, 2026", "Quiz on 10/15", "Quiz on 10/15/2026",
        "Quiz on 10/15/26", "Quiz on 10/5", "Project due 3/4/2026", "Project due 14/9", "See you 2026-10-05 for the quiz", "Quiz on 2026-10-05",
        "Reading week Oct 12-16, 2026", "Reading week Oct 12–16", "Homework due Mon Oct 5, 2026", "Homework due Tue Oct 5, 2026",
        "Exam Thursday, October 10", "Office hours Tuesdays 2:00-3:00 PM", "Office hours: Mon and Wed 1:00-2:00 PM", "Lecture TR 9:30 AM",
        "Lecture MWF 10:00 AM", "No class Nov 26 (holiday)", "Thanksgiving break Nov 25-27", "Final exam: Dec 9, 2026 at 9:00 AM",
        "Final exam: Dec 9 at 9:00 AM", "Paper due Dec 31, 2026", "Paper due Jan 5", "Quiz Feb 29, 2028", "Quiz Feb 29, 2027", "Lab due Sep 31",
        "Essay due Nov 3rd, 2026 (tentative)", "Exam 1 was rescheduled: now Oct 21, 2026", "Quiz 2 due Oct 21, 2026, also Nov 3, 2026",
        "Final exam: TBA", "Late work policy: 10% per day.", "Midterm exam: Mar 8, 2026 at 2:30 AM", "Project due 12/12", "Project due 1/1/2027",
        "Homework 4 due 9/30 and 10/2", "Office hours Fridays 3-4pm", "Lecture Monday 10:00 AM", "Reading: Chapter 3 by Sept 21",
        new string('x', 1000) + " Quiz due Oct 20, 2026 " + new string('y', 1000),
        "Office hours Tuesdays 2:00-3:00 PM " + new string('z', 1000),
    ];

    public static TheoryData<string> MockSweep()
    {
        var d = new TheoryData<string>();
        foreach (string f in ServerFlagsTests.Fixtures()) d.Add(f);
        foreach (var line in SweepLines) d.Add(line);
        d.Add(string.Join("\n", SweepLines));
        return d;
    }

    [Theory]
    [MemberData(nameof(MockSweep))]
    public void SBV12_the_mock_extractors_items_all_pass_the_date_check_and_are_stored_byte_identical(string raw)
    {
        foreach (var (term, hidden) in new[] { (true, false), (false, false), (true, true) })
        {
            var input = Input(raw, term, hidden);
            var o = X.Extract(input);
            var kept = SyllabusLimits.Apply(o, maxItems: 200, input.Text);
            Assert.Equal(o.DroppedInvalid, kept.DroppedInvalid);                                 // nothing the mock proposes is dropped
            Assert.Equal(o.Items.Count, kept.Items.Count);
            foreach (var (mock, k) in o.Items.Zip(kept.Items))
            {
                Assert.True(SyllabusLimits.TryCanonical(mock.Snippet, input.Text, out var sliced));
                Assert.Equal(mock with { Snippet = sliced }, k);                                 // record equality: every date field as the mock wrote it
            }
            Assert.Equal(o.Unclaimed.Count, kept.Unclaimed.Count);
        }
    }
}
