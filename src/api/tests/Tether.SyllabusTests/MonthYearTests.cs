using Tether.Syllabus.Extraction;
using Tether.Syllabus.Text;

namespace Tether.SyllabusTests;

/// <summary>PO-20av (T-MONTH-ONLY, option A): a line with a month and a year but no day is a "Needs a date" item with no invented day.</summary>
public class MonthYearTests
{
    private static readonly MockRuleExtractor X = new();
    private static readonly DateOnly TermStart = new(2026, 8, 24), TermEnd = new(2026, 12, 11);

    private static (ExtractorOutput Out, IReadOnlyList<ProposedItem> Stored, SyllabusLimits.Kept Kept) Run(string raw, bool term = true)
    {
        var doc = DocumentText.Canonicalize(raw);
        var input = new ExtractorInput(doc.Text, term ? TermStart : null, term ? TermEnd : null, "America/New_York", false, doc.InjectionSuspect);
        var o = X.Extract(input);
        var kept = SyllabusLimits.Apply(o, 200, input.Text);
        var sdoc = ServerFlags.Document(input);
        return (o, [.. kept.Items.Select(p => ServerFlags.Rederive(p, input, sdoc))], kept);
    }

    [Theory]
    [InlineData("Paper due by Sept 2026", "assignment")]
    [InlineData("Final project due December 2026", "exam")]   // "final" classifies first, as for any dated line
    [InlineData("Final exam: December 2026", "exam")]
    [InlineData("Paper due Sep. 2026", "assignment")]
    [InlineData("Paper due by Sept, 2026", "assignment")]
    [InlineData("Paper due May 2026", "assignment")]
    public void MonthAndYearWithNoDayIsANeedsDateItem(string line, string kind)
    {
        Assert.Empty(SyllabusSignals.FindDates(line));                               // the matcher still reads no day
        var (_, stored, kept) = Run(line);
        var item = Assert.Single(stored);
        Assert.Equal(kind, item.Kind);
        Assert.Equal("needs_date", item.WhenStatus);
        Assert.Null(item.Date); Assert.Null(item.Day); Assert.Null(item.Month); Assert.Null(item.Year); Assert.Null(item.Recurrence);
        Assert.Empty(item.Alternatives);
        Assert.Equal("not_stated", item.UnknownReason);
        Assert.Equal("please_check", item.Confidence);
        Assert.Equal(SyllabusSignals.ReasonNotStated, item.ConfidenceReason);
        Assert.Equal(line, item.Snippet.Text);                                       // the quote is kept
        Assert.Equal(0, kept.DroppedInvalid);
    }

    [Fact]
    public void WithNoTermTheItemIsTheSame()
    {
        var item = Assert.Single(Run("Paper due by Sept 2026", term: false).Stored);
        Assert.Equal("needs_date", item.WhenStatus);
        Assert.Null(item.Date);
    }

    [Fact]
    public void ForcingFlagsStillApply()
    {
        var item = Assert.Single(Run("Paper due by Sept 2026 (tentative)").Stored);
        Assert.Contains("tentative_language", item.Flags);
        Assert.Equal("please_check", item.Confidence);
    }

    [Theory]
    [InlineData("Paper due September")]
    [InlineData("Final project due in 2026")]
    [InlineData("Paper due 2026")]
    [InlineData("Students may 2026 submit early and the paper is due")]
    [InlineData("The paper is due mar 2026")]
    [InlineData("Paper due Septembers 2026")]
    [InlineData("Paper due Sept 2026x")]
    [InlineData("Lecture notes from the 2026 September meeting")]
    [InlineData("Reading Dec 12026")]
    public void NotAMonthAndYear(string line)
    {
        Assert.Empty(Run(line).Stored);
        Assert.Empty(Run(line).Out.Unclaimed);
    }

    [Fact]
    public void ALineWithADayIsStillADatedItem()
    {
        var item = Assert.Single(Run("Paper due Sept 15 2026").Stored);
        Assert.Equal("resolved", item.WhenStatus);
        Assert.Equal(new DateOnly(2026, 9, 15), item.Date);
    }

    [Fact]
    public void ALineWithNoKindMakesNoItem() => Assert.Empty(Run("Welcome to the course, September 2026").Stored);

    [Fact]
    public void AWeeklyLineIsNotDoubled()
    {
        var items = Run("Office hours Tuesdays 10:00-11:00 through Sept 2026").Stored;
        Assert.Single(items);
        Assert.NotNull(items[0].Recurrence);
    }
}
