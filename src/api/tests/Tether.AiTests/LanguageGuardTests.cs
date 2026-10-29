using Microsoft.Extensions.Logging;
using Tether.Ai.Provider;
using Tether.Ai.Validation;

namespace Tether.AiTests;

/// <summary>
/// LanguageGuard (docs/ux/language-guide.md section 5.2). The guard is a regex FLOOR over text the model wrote. It is log-only by
/// default, never logs the text, and exempts a match that also appears verbatim in the cited source (quoted lecture content).
/// </summary>
public class LanguageGuardRuleTests
{
    private static IReadOnlyDictionary<string, int> Hits(string text, string? source = null) => LanguageGuard.ScanText(text, source).Hits;

    [Theory]
    [InlineData("You missed the part about mitosis.", "blame.second_person")]
    [InlineData("you've forgotten the definition", "blame.second_person")]
    [InlineData("You didn't understand the proof.", "blame.second_person")]
    [InlineData("In case you zoned out, the lecturer moved on.", "blame.lost_track")]
    [InlineData("It is easy to lose the thread here.", "blame.lost_track")]
    [InlineData("Students falling behind can reread.", "blame.lost_track")]
    [InlineData("This is your weakness in limits.", "blame.deficit")]
    [InlineData("You are struggling with derivatives.", "blame.deficit")]
    [InlineData("Symptoms of inattention were listed.", "clinical")]
    [InlineData("This may point to ADHD.", "clinical")]
    [InlineData("Please hurry to finish.", "pressure.urgency")]
    [InlineData("Do this immediately.", "pressure.urgency")]
    [InlineData("Don't forget the exam.", "pressure.urgency")]
    [InlineData("You should study this.", "pressure.urgency")]
    [InlineData("The essay is overdue.", "pressure.deadline")]
    [InlineData("Only 3 days left.", "pressure.deadline")]
    [InlineData("Great job, that is amazing.", "tone.filler")]
    [InlineData("Simply read chapter 2.", "tone.filler")]
    [InlineData("Obviously the limit exists.", "tone.filler")]
    [InlineData("Oops, that is wrong.", "tone.filler")]
    [InlineData("The answer is definitely B.", "certainty")]
    [InlineData("Press I lost track to see more.", "name.old")]
    [InlineData("This helps you catch up.", "name.catch_up")]
    [InlineData("A catch-up summary.", "name.catch_up")]
    public void Each_rule_reports_its_id_for_a_typical_hit(string text, string ruleId) => Assert.Contains(ruleId, Hits(text).Keys);

    [Fact]
    public void An_exclamation_mark_and_an_emoji_are_reported()
    {
        Assert.Equal(1, Hits("Nice work!")["tone.exclaim"]);
        Assert.Equal(1, Hits("Cells divide \U0001F600")["tone.emoji"]);
        Assert.Equal(2, Hits("Wow! Really!")["tone.exclaim"]);
    }

    [Fact]
    public void Counts_are_per_match_and_summed_across_fields()
    {
        var scan = LanguageGuard.Scan([new GuardText("You missed it. You forgot it."), new GuardText("You failed.")]);
        Assert.Equal(3, scan.Hits["blame.second_person"]);
        Assert.False(scan.Clean);
        Assert.Equal("blame.second_person=3", scan.Summary);
    }

    [Fact]
    public void Curly_apostrophes_and_odd_spacing_and_case_do_not_hide_a_hit()
    {
        Assert.Contains("pressure.urgency", Hits("Don’t forget the quiz"));
        Assert.Contains("blame.second_person", Hits("YOU MISSED it"));
        Assert.Contains("pressure.urgency", Hits("Don't    FORGET"));
    }

    // ---- false positives the guide says must NOT be flagged ----

    [Theory]
    [InlineData("The just noticeable difference is the smallest change a person can detect.")]
    [InlineData("Justice was a theme in the novel.")]
    [InlineData("Add the two vectors tip to tail.")]
    [InlineData("To add a numerator, first find a common denominator.")]
    [InlineData("5! equals 120.")]
    [InlineData("n! grows faster than 2^n.")]
    [InlineData("Compute (n-1)! first.")]
    [InlineData("In code, x != y tests inequality.")]
    [InlineData("The experiment failed to replicate.")]
    [InlineData("The first trial was wrong because of a miscalibrated scale.")]
    [InlineData("Attention is a topic in cognitive science.")]
    [InlineData("Mitosis was covered at 03:41.")]
    [InlineData("Most recent: derivatives.")]
    [InlineData("Here is what was said in the last 5 minutes.")]
    [InlineData("Could you explain entropy again?")]
    public void Plain_subject_matter_and_notation_are_not_flagged(string text) => Assert.Empty(Hits(text));

    [Fact]
    public void Lowercase_add_is_never_flagged_but_the_uppercase_acronym_is_a_known_false_positive()
    {
        Assert.Empty(Hits("You can add a topic to your notes."));
        Assert.Contains("clinical", Hits("The ADD instruction sums two registers.")); // documented limitation: case-sensitive ADD collides with the assembly mnemonic
    }

    [Theory]
    [InlineData("The lecturer said you should remember the formula.", "You should remember the formula and then derive it.", "pressure.urgency")]
    [InlineData("Don't forget the exam is on Friday.", "Okay everyone, don't forget the exam is on Friday.", "pressure.urgency")]
    [InlineData("ADHD is covered in week 3.", "Next week we cover ADHD and related conditions.", "clinical")]
    [InlineData("Symptoms include fever.", "Common symptoms include fever and cough.", "clinical")]
    [InlineData("Evaluate the field at the origin right now.", "So at time zero, right now, the field is zero.", "pressure.urgency")]
    [InlineData("Eureka! was shouted.", "He shouted Eureka! and ran out.", "tone.exclaim")]
    public void A_match_that_appears_verbatim_in_the_cited_source_is_quoted_lecture_content_and_is_exempt(string modelText, string source, string ruleId)
    {
        Assert.Contains(ruleId, Hits(modelText)); // without the source it is flagged
        Assert.DoesNotContain(ruleId, Hits(modelText, source).Keys);
    }

    [Fact]
    public void The_exemption_is_per_match_not_per_text()
    {
        var hits = Hits("You should read this and you must also read that.", "You should read this carefully.");
        Assert.Equal(1, hits["pressure.urgency"]); // "you should" is quoted, "you must" is not
    }

    // ---- adversarial: what it does and does not catch (pins current behaviour; improving the guard means updating these) ----

    [Theory]
    [InlineData("you may have drifted off")]
    [InlineData("you might have stepped away for a moment")]
    [InlineData("do not forget the quiz")]
    [InlineData("dont forget the quiz")]
    [InlineData("you ought to review this")]
    [InlineData("you were not following along")]
    [InlineData("y o u   m i s s e d")]
    [InlineData("you míssed")]
    [InlineData("Your focus seems to have slipped.")]
    public void KNOWN_MISSES_paraphrase_and_obfuscation_are_not_caught_by_a_regex_floor(string text) => Assert.Empty(Hits(text));

    [Fact]
    public void KNOWN_MISS_a_hit_is_exempt_whenever_the_source_contains_the_same_words_even_if_the_model_used_them_to_blame()
    {
        // The source says "you missed the bus" (a story). A model line "You missed the part ..." is a different match string only if it differs.
        Assert.Empty(Hits("you missed", "The character said you missed the bus."));
    }

    [Fact]
    public void Matching_the_source_is_case_insensitive_and_needs_the_whole_matched_phrase()
    {
        Assert.Empty(Hits("Don't forget", "DON'T FORGET the quiz"));
        Assert.Contains("pressure.urgency", Hits("Don't forget", "I forgot the quiz and don't care")); // "don't forget" is not in the source
    }

    [Fact]
    public void Invisible_and_confusable_input_does_not_make_the_scanner_throw()
    {
        var scan = LanguageGuard.Scan([new GuardText(null!), new GuardText(""), new GuardText("​you​ missed"), new GuardText("\ud800 lone surrogate you missed")]);
        Assert.True(scan.Hits.ContainsKey("blame.second_person")); // the lone-surrogate field is still scanned
        Assert.False(scan.Incomplete);
    }

    // ---- bounded work and fail-safe ----

    [Fact]
    public void A_hostile_100k_input_finishes_quickly_and_is_marked_incomplete_without_throwing()
    {
        var hostile = string.Concat(Enumerable.Repeat("you ", 25_000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var scan = LanguageGuard.ScanText(hostile, null);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
        Assert.True(scan.Incomplete);
        Assert.False(scan.Clean);
    }

    [Theory]
    [InlineData("a-")]
    [InlineData("just ")]
    [InlineData("you have ")]
    [InlineData("don't ")]
    [InlineData("attention deficit ")]
    public void Repetitive_input_at_the_largest_scanned_size_stays_bounded(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, LanguageGuard.MaxScanChars / unit.Length));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var scan = LanguageGuard.ScanText(text, null);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
        Assert.NotNull(scan);
    }
}

public class LanguageGuardGateTests
{
    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> kv) Lines.AddRange(kv.Select(p => $"{p.Key}={p.Value}"));
        }
    }

    private static readonly SummaryContext Ctx = new(0, T.Allowed(T.Line(1, 0, "Mitosis is cell division.")));

    private static Validated<SummaryDelta> Delta(string tldr) => Validated<SummaryDelta>.Success(
        new SummaryDelta(tldr, [new TopicState("Mitosis", "Cells divide.", [new CitationRef("seg_1", 0)])], [], 0, 0, false));

    private static IEnumerable<GuardText> Texts(SummaryDelta d) => LanguageGuard.Texts(d, Ctx);

    [Fact]
    public void Log_only_is_the_default_and_never_changes_the_result()
    {
        Assert.False(T.Options().LanguageGuardEnforce);
        var log = new ListLogger();
        var v = Delta("You missed the part about mitosis.");
        var res = LanguageGuard.Apply(v, Texts, enforce: false, log, "RollingSummary");
        Assert.Same(v, res);
        Assert.True(res.Ok);
        Assert.Contains(log.Lines, l => l.Contains("blame.second_person=1"));
    }

    [Fact]
    public void Enforce_turns_a_hit_into_a_rejection_with_a_fixed_code_so_the_gateway_repairs_once_then_falls_back()
    {
        var res = LanguageGuard.Apply(Delta("You missed the part about mitosis."), Texts, enforce: true, new ListLogger(), "RollingSummary");
        Assert.False(res.Ok);
        Assert.Equal(ValidationCodes.LanguageViolation, res.Error);
        Assert.Equal("language_violation", ValidationCodes.LanguageViolation);
    }

    [Fact]
    public void Clean_text_passes_in_both_modes_and_logs_nothing()
    {
        foreach (var enforce in new[] { false, true })
        {
            var log = new ListLogger();
            var v = Delta("Mitosis is cell division.");
            Assert.Same(v, LanguageGuard.Apply(v, Texts, enforce, log, "RollingSummary"));
            Assert.Empty(log.Lines);
        }
    }

    [Fact]
    public void An_already_rejected_result_is_passed_through_untouched()
    {
        var failed = Validated<SummaryDelta>.Fail(ValidationCodes.Ungrounded);
        Assert.Same(failed, LanguageGuard.Apply(failed, Texts, enforce: true, new ListLogger(), "RollingSummary"));
    }

    [Fact]
    public void The_log_carries_rule_ids_and_counts_and_never_the_text_log_canary()
    {
        var log = new ListLogger();
        const string canary = "CANARY-8841-lecture-secret";
        LanguageGuard.Apply(Delta($"You missed {canary}. Don't forget {canary}!"), Texts, enforce: false, log, "RollingSummary");
        Assert.NotEmpty(log.Lines);
        var all = string.Join("\n", log.Lines);
        Assert.DoesNotContain("CANARY", all);
        Assert.DoesNotContain("missed", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("forget", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blame.second_person=1", all);
        Assert.Contains("pressure.urgency=1", all);
    }

    [Fact]
    public void A_guard_failure_is_fail_safe_log_only_passes_and_enforce_rejects()
    {
        static IEnumerable<GuardText> Boom(SummaryDelta _) => throw new InvalidOperationException("secret text CANARY");
        var log = new ListLogger();
        var v = Delta("Mitosis is cell division.");
        Assert.Same(v, LanguageGuard.Apply(v, Boom, enforce: false, log, "RollingSummary"));
        var enforced = LanguageGuard.Apply(v, Boom, enforce: true, log, "RollingSummary");
        Assert.Equal(ValidationCodes.LanguageViolation, enforced.Error);
        Assert.DoesNotContain("CANARY", string.Join("\n", log.Lines));
    }

    [Fact]
    public void Summary_extraction_uses_the_cited_lines_as_the_quote_source()
    {
        var ctx = new SummaryContext(0, T.Allowed(T.Line(1, 0, "Don't forget the exam."), T.Line(2, 5000, "Cells divide.")));
        var delta = new SummaryDelta("Don't forget the exam.", [new TopicState("Exam", "Don't forget the exam.", [new CitationRef("seg_1", 0)])], [], 0, 0, false);
        Assert.True(LanguageGuard.Scan(LanguageGuard.Texts(delta, ctx)).Clean);
        var wrongCite = delta with { Topics = [new TopicState("Exam", "Don't forget the exam.", [new CitationRef("seg_2", 5000)])] };
        Assert.Contains("pressure.urgency", LanguageGuard.Scan(LanguageGuard.Texts(wrongCite with { Tldr = "Cells divide." }, ctx)).Hits.Keys);
    }

    [Fact]
    public void Quick_recap_extraction_covers_every_student_visible_text_field()
    {
        var c = new Claim("You missed this.", Basis.Lecture, [new CitationRef("seg_1", 0)]);
        var r = new CatchUpResult(c, [c], [new DetailItem(c, "Don't forget it.")], [new KeyTerm("ADHD", "A disorder.", Basis.Lecture, [])], ["Could you explain it once more! ok"], "low", ["You lost track here."], 0, false);
        var ctx = new CatchUpContext(T.Allowed(T.Line(1, 0, "Mitosis.")), true);
        var scan = LanguageGuard.Scan(LanguageGuard.Texts(r, ctx));
        Assert.True(scan.Hits["blame.second_person"] >= 3);   // now, short, detail claim
        Assert.True(scan.Hits.ContainsKey("pressure.urgency")); // whyItMatters
        Assert.True(scan.Hits.ContainsKey("clinical"));        // key term and definition
        Assert.True(scan.Hits.ContainsKey("tone.exclaim"));    // question
        Assert.True(scan.Hits.ContainsKey("blame.lost_track")); // gap
    }

    [Fact]
    public void Enforce_is_a_config_flag_off_by_default()
    {
        Assert.True(T.Options(KeyValuePair.Create("Ai:LanguageGuard:Enforce", (string?)"true")).LanguageGuardEnforce);
        Assert.False(T.Options(KeyValuePair.Create("Ai:LanguageGuard:Enforce", (string?)"false")).LanguageGuardEnforce);
    }
}
