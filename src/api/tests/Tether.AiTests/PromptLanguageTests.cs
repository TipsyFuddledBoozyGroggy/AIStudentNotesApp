using System.Text.Json;
using Tether.Ai.CatchUp;
using Tether.Ai.Prompts;
using Tether.Ai.Provider;
using Tether.Ai.Validation;

namespace Tether.AiTests;

/// <summary>docs/ux/language-guide.md sections 5.1 and 8.2: prompt wording, version bumps, mock output tone.</summary>
public class PromptLanguageTests
{
    public static TheoryData<string, string> Prompts => new()
    {
        { "RollingSummary", PromptLibrary.RollingSummary }, { "QuickRecap", PromptLibrary.QuickRecap },
    };

    [Fact]
    public void Prompt_versions_were_bumped_because_the_text_changed()
    {
        Assert.Equal("rolling-summary.v2", PromptLibrary.RollingSummaryVersion);
        Assert.Equal("quick-recap.v2", PromptLibrary.QuickRecapVersion);
        Assert.Same(PromptLibrary.RollingSummary, PromptLibrary.Compaction);
    }

    [Theory]
    [MemberData(nameof(Prompts))]
    public void Every_student_text_prompt_carries_the_language_clause_block_verbatim(string name, string prompt)
    {
        Assert.True(prompt.Contains(PromptLibrary.LanguageRules, StringComparison.Ordinal), name);
        foreach (var must in new[]
        {
            "Language rules (apply to every text field):", "Never write about the student's attention", "Do not mention ADHD, focus, distraction or attention",
            "No exclamation marks, no emoji", "Call the feature \"Quick recap\" if you must name it. Never write \"I lost track\" or \"catch up\".",
            "One wrong answer is one answer.", "\"my best estimate\"",
        })
            Assert.Contains(must, PromptLibrary.LanguageRules);
    }

    [Theory]
    [MemberData(nameof(Prompts))]
    public void Prompts_no_longer_frame_the_student_as_losing_the_thread_or_needing_to_catch_up(string name, string prompt)
    {
        _ = name;
        Assert.DoesNotContain("lose the thread", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("catching up", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discussed right now", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_rolling_summary_and_recap_prompts_use_the_guides_replacement_wording()
    {
        Assert.Contains("for a student who wants a running overview of a live university lecture.", PromptLibrary.RollingSummary);
        Assert.Contains("A student asked for a quick recap of the last windowMinutes minutes of a live lecture.", PromptLibrary.QuickRecap);
        Assert.Contains("what was said most recently (the last minute)", PromptLibrary.QuickRecap);
        Assert.Contains("Say nothing about the student's attention or state of mind.", PromptLibrary.QuickRecap);
    }

    [Fact]
    public void The_prompt_wire_keys_are_unchanged()
    {
        foreach (var key in new[] { "nowDiscussing", "shortCatchUp", "detail", "keyTerms", "clarifyingQuestions", "confidence", "gaps" })
            Assert.Contains($"\"{key}\"", PromptLibrary.QuickRecap);
        foreach (var key in new[] { "baseRevision", "tldr", "topicUpdates", "newTerms" })
            Assert.Contains($"\"{key}\"", PromptLibrary.RollingSummary);
    }

    [Fact]
    public void Prompts_stay_free_of_secrets_urls_and_tools_after_the_edit()
    {
        foreach (var p in new[] { PromptLibrary.RollingSummary, PromptLibrary.QuickRecap })
        {
            Assert.DoesNotContain("http", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", p, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class MockToneTests
{
    private static readonly JsonElement Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone-eval.v1.json"))).RootElement;

    public static TheoryData<string> LectureIds
    {
        get { var d = new TheoryData<string>(); foreach (var l in Fixture.GetProperty("lectures").EnumerateArray()) d.Add(l.GetProperty("id").GetString()!); return d; }
    }

    public static TheoryData<string> OutputIds
    {
        get { var d = new TheoryData<string>(); foreach (var l in Fixture.GetProperty("outputs").EnumerateArray()) d.Add(l.GetProperty("id").GetString()!); return d; }
    }

    private static SourceLine[] LinesOf(string id) => Fixture.GetProperty("lectures").EnumerateArray().Single(l => l.GetProperty("id").GetString() == id)
        .GetProperty("lines").EnumerateArray().Select((t, i) => T.Line(i + 1, i * 20_000L, t.GetString()!)).ToArray();

    [Fact]
    public void The_fixture_is_marked_synthetic_and_versioned()
    {
        Assert.Equal("tone-eval.v1", Fixture.GetProperty("schema").GetString());
        Assert.Contains("SYNTHETIC ONLY", Fixture.GetProperty("note").GetString());
    }

    [Theory]
    [MemberData(nameof(LectureIds))]
    public async Task Mock_quick_recap_text_passes_the_language_guard_for_every_window(string lectureId)
    {
        var lines = LinesOf(lectureId);
        foreach (var w in Fixture.GetProperty("windows").EnumerateArray().Select(x => x.GetInt32()))
        {
            var req = T.Request(AiWorkflow.QuickRecap, $"windowMinutes={w}", new UntrustedBlock(BlockKinds.WindowSegments, lines));
            var resp = await new MockAiClient(T.Options()).CompleteAsync(req, default);
            var ctx = new CatchUpContext(T.Allowed(lines), true);
            var v = CatchUpValidator.Validate(resp.Json, ctx);
            Assert.True(v.Ok, v.Error);
            var scan = LanguageGuard.Scan(LanguageGuard.Texts(v.Value!, ctx));
            Assert.True(scan.Clean, $"{lectureId} window {w}: {scan.Summary}");
        }
    }

    [Theory]
    [MemberData(nameof(LectureIds))]
    public async Task Mock_rolling_summary_text_passes_the_language_guard(string lectureId)
    {
        var lines = LinesOf(lectureId);
        var req = T.Request(AiWorkflow.RollingSummary, "baseRevision=0", new UntrustedBlock(BlockKinds.NewSegments, lines));
        var resp = await new MockAiClient(T.Options()).CompleteAsync(req, default);
        var ctx = new SummaryContext(0, T.Allowed(lines));
        var v = SummaryValidator.Validate(resp.Json, ctx);
        Assert.True(v.Ok, v.Error);
        var scan = LanguageGuard.Scan(LanguageGuard.Texts(v.Value!, ctx));
        Assert.True(scan.Clean, $"{lectureId}: {scan.Summary}");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task An_empty_window_says_what_was_said_in_the_last_N_minutes_and_nothing_about_the_student(int w)
    {
        var req = T.Request(AiWorkflow.QuickRecap, $"windowMinutes={w}", new UntrustedBlock(BlockKinds.WindowSegments, []));
        var json = (await new MockAiClient(T.Options()).CompleteAsync(req, default)).Json;
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal($"Nothing was said in the last {w} minutes.", doc.GetProperty("nowDiscussing").GetProperty("text").GetString());
        Assert.Equal($"No transcript text in the last {w} minutes.", doc.GetProperty("shortCatchUp")[0].GetProperty("text").GetString());
        Assert.Equal($"No transcript text in the last {w} minutes.", doc.GetProperty("gaps")[0].GetString());
        Assert.DoesNotContain("this window", json);
    }

    [Fact]
    public async Task A_single_line_window_says_little_was_said_in_the_last_N_minutes()
    {
        var req = T.Request(AiWorkflow.QuickRecap, "windowMinutes=5", new UntrustedBlock(BlockKinds.WindowSegments, LinesOf("single_line")));
        var doc = JsonDocument.Parse((await new MockAiClient(T.Options()).CompleteAsync(req, default)).Json).RootElement;
        Assert.Equal("Very little was said in the last 5 minutes.", doc.GetProperty("gaps")[0].GetString());
    }

    [Fact]
    public async Task Suggested_questions_do_not_imply_the_student_missed_it()
    {
        var req = T.Request(AiWorkflow.QuickRecap, "windowMinutes=5", new UntrustedBlock(BlockKinds.WindowSegments, LinesOf("noisy_asr")));
        var doc = JsonDocument.Parse((await new MockAiClient(T.Options()).CompleteAsync(req, default)).Json).RootElement;
        var qs = doc.GetProperty("clarifyingQuestions").EnumerateArray().Select(q => q.GetString()!).ToList();
        Assert.NotEmpty(qs);
        Assert.All(qs, q => { Assert.DoesNotContain("once more", q); Assert.StartsWith("Could you explain ", q); Assert.EndsWith(" again?", q); });
    }

    [Fact]
    public async Task The_fallback_question_for_a_term_less_window_is_go_over_the_last_point()
    {
        var req = T.Request(AiWorkflow.QuickRecap, "windowMinutes=5", new UntrustedBlock(BlockKinds.WindowSegments, [T.Line(1, 0, "short words only here")]));
        var doc = JsonDocument.Parse((await new MockAiClient(T.Options()).CompleteAsync(req, default)).Json).RootElement;
        Assert.Equal("Could you go over the last point?", doc.GetProperty("clarifyingQuestions")[0].GetString());
    }

    [Theory]
    [MemberData(nameof(OutputIds))]
    public void Hand_written_outputs_get_the_expected_rule_ids(string id)
    {
        var o = Fixture.GetProperty("outputs").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        var src = o.GetProperty("source").GetString();
        var scan = LanguageGuard.ScanText(o.GetProperty("text").GetString()!, string.IsNullOrEmpty(src) ? null : src);
        var expected = o.GetProperty("expectedRules").EnumerateArray().Select(x => x.GetString()!).Order().ToArray();
        Assert.Equal(expected, scan.Hits.Keys.Order().ToArray());
    }

    [Fact]
    public void The_fixture_documents_at_least_one_known_miss()
    {
        Assert.Contains(Fixture.GetProperty("outputs").EnumerateArray(), o => o.TryGetProperty("knownMiss", out var k) && k.GetBoolean());
    }

    [Fact]
    public void Fixed_server_wire_strings_pass_the_guard()
    {
        foreach (var s in new[] { CatchUpJson.GapDowngraded, CatchUpJson.GapTruncated, CatchUpJson.NothingYet })
            Assert.True(LanguageGuard.ScanText(s, null).Clean, s);
    }
}
