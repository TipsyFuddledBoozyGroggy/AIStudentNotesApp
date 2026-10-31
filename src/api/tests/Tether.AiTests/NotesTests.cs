using Tether.Ai.Notes;
using Tether.Ai.Prompts;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Transcript.Contracts;

namespace Tether.AiTests;

/// <summary>PO-18d notes after Stop: the validator's grounding rule, the mock, the request plan and the prompt. Pure; no database.</summary>
public class NotesValidatorTests
{
    private static NotesContext Ctx() => new(T.Allowed(T.Line(1, 1000, "Mitosis has four phases."), T.Line(2, 5000, "Prophase is first."), T.Line(3, 9000, "Then metaphase.")));

    private static string Reply(params (string heading, (string text, string[] ids)[] bullets)[] sections) => T.Json(new
    {
        sections = sections.Select(s => new { heading = s.heading, bullets = s.bullets.Select(b => new { text = b.text, segmentIds = b.ids }).ToArray() }).ToArray(),
    });

    [Fact]
    public void A_grounded_reply_is_accepted_and_timecodes_come_from_the_stored_segments()
    {
        var v = NotesValidator.Validate(Reply(("Mitosis", [("Four phases.", ["seg_1"]), ("Prophase first.", ["seg_2", "seg_2"])])), Ctx());
        Assert.True(v.Ok, v.Error);
        var s = Assert.Single(v.Value!.Sections);
        Assert.Equal(new CitationRef("seg_1", 1000), Assert.Single(s.Bullets[0].Citations));
        Assert.Equal(new CitationRef("seg_2", 5000), Assert.Single(s.Bullets[1].Citations)); // duplicate id collapsed
    }

    [Fact]
    public void A_bullet_without_a_valid_citation_is_dropped_and_a_section_left_empty_is_dropped()
    {
        var v = NotesValidator.Validate(Reply(
            ("Kept", [("cited", ["seg_3", "seg_999"]), ("uncited claim", ["seg_404"]), ("no ids", [])]),
            ("Gone", [("only an invented id", ["seg_77"])])), Ctx());
        Assert.True(v.Ok);
        var s = Assert.Single(v.Value!.Sections);
        Assert.Equal("Kept", s.Heading);
        Assert.Equal("cited", Assert.Single(s.Bullets).Text);
        Assert.Equal(new CitationRef("seg_3", 9000), Assert.Single(s.Bullets[0].Citations));
        Assert.Equal(3, v.Value.DroppedBullets);
        Assert.Equal(1, v.Value.DroppedSections);
    }

    [Fact]
    public void A_reply_with_nothing_grounded_is_rejected()
    {
        Assert.Equal(ValidationCodes.Ungrounded, NotesValidator.Validate(Reply(("x", [("y", ["seg_404"])])), Ctx()).Error);
        Assert.Equal(ValidationCodes.Ungrounded, NotesValidator.Validate("""{"sections":[]}""", Ctx()).Error);
    }

    [Theory]
    [InlineData("""{"sections":[{"heading":"h","bullets":[{"text":"t","segmentIds":["seg_1"],"startMs":5}]}]}""")]  // model timecodes are not accepted
    [InlineData("""{"sections":[{"heading":"h","bullets":[{"text":"t","segmentIds":["seg_1"]}],"x":1}]}""")]
    [InlineData("""{"sections":[],"tldr":"x"}""")]
    [InlineData("""{"sections":{}}""")]
    [InlineData("""{"sections":[{"heading":7,"bullets":[]}]}""")]
    [InlineData("""{"sections":[{"heading":"h","bullets":[{"text":"t","segmentIds":[1]}]}]}""")]
    [InlineData("""{"sections":[{"heading":"h","heading":"g","bullets":[{"text":"t","segmentIds":["seg_1"]}]}]}""")]
    public void Closed_shapes_reject_anything_else(string json) =>
        Assert.Equal(ValidationCodes.SchemaInvalid, NotesValidator.Validate(json, Ctx()).Error);

    [Fact]
    public void Not_json_and_overlong_are_rejected_with_their_codes()
    {
        Assert.Equal(ValidationCodes.NotJson, NotesValidator.Validate("{\"sections\": [", Ctx()).Error);
        Assert.Equal(ValidationCodes.Overlong, NotesValidator.Validate(Reply(("h", [(new string('x', NotesValidator.MaxText + 1), ["seg_1"])])), Ctx()).Error);
        Assert.Equal(ValidationCodes.Overlong, NotesValidator.Validate(Reply((new string('h', NotesValidator.MaxHeading + 1), [("t", ["seg_1"])])), Ctx()).Error);
        var many = Enumerable.Range(0, NotesValidator.MaxSections + 1).Select(i => ($"h{i}", new[] { ("t", new[] { "seg_1" }) })).ToArray();
        Assert.Equal(ValidationCodes.Overlong, NotesValidator.Validate(Reply(many), Ctx()).Error);
    }

    [Fact]
    public void Markup_links_and_scripts_are_removed_from_headings_and_bullets()
    {
        var v = NotesValidator.Validate(Reply(("<script>alert(1)</script>Phases", [("[click](https://evil.example/x) ![i](https://evil.example/p.png) four phases", ["seg_1"])])), Ctx());
        Assert.True(v.Ok);
        Assert.True(v.Value!.Sanitized);
        var s = Assert.Single(v.Value.Sections);
        Assert.DoesNotContain("<", s.Heading);
        Assert.DoesNotContain("evil.example", s.Bullets[0].Text);
    }
}

public class NotesMockAndPlanTests
{
    private static readonly Guid User = Guid.NewGuid(), Session = Guid.NewGuid(), Press = Guid.NewGuid();

    private static FinalSegment Seg(int n, string? text = null) => new(Session, n, n, n * 2000L, n * 2000L + 1500, text ?? $"Point number {n} about mitosis and the cell cycle.", null);

    [Fact]
    public async Task The_mock_answers_a_notes_part_with_output_the_validator_accepts_and_every_bullet_cited()
    {
        var plan = NotesRequests.Build(User, Session, Press, Enumerable.Range(1, 9).Select(n => Seg(n)).ToList(), false, NotesOptions.Default);
        var part = Assert.Single(plan.Parts);
        var response = await new MockAiClient(T.Options()).CompleteAsync(part.Request, default);
        var v = NotesValidator.Validate(response.Json, part.Context);
        Assert.True(v.Ok, v.Error);
        Assert.Equal(0, v.Value!.DroppedBullets);
        Assert.Equal(9, v.Value.Sections.Sum(s => s.Bullets.Count));
        Assert.StartsWith("mock", response.Model);
        Assert.Equal(AiSource.Mock, AiSource.Of(response.Model));
    }

    [Theory]
    [InlineData("BadCitations", ValidationCodes.Ungrounded)]
    [InlineData("Overlong", ValidationCodes.Overlong)]
    [InlineData("Malformed", ValidationCodes.NotJson)]
    public async Task Mock_faults_are_caught_by_the_validator(string faultName, string code)
    {
        var fault = Enum.Parse<MockFault>(faultName);
        var part = NotesRequests.Build(User, Session, Press, [Seg(1), Seg(2)], false, NotesOptions.Default).Parts.Single();
        var mock = new MockAiClient(T.Options()) { Fault = fault };
        Assert.Equal(code, NotesValidator.Validate((await mock.CompleteAsync(part.Request, default)).Json, part.Context).Error);
    }

    [Fact]
    public async Task The_injection_fault_renders_inert_and_its_uncited_claim_is_dropped()
    {
        var part = NotesRequests.Build(User, Session, Press, [Seg(1), Seg(2)], false, NotesOptions.Default).Parts.Single();
        var mock = new MockAiClient(T.Options()) { Fault = MockFault.Injection };
        var v = NotesValidator.Validate((await mock.CompleteAsync(part.Request, default)).Json, part.Context);
        Assert.True(v.Ok, v.Error);
        var s = Assert.Single(v.Value!.Sections);
        var b = Assert.Single(s.Bullets); // the "SYSTEM PROMPT" bullet cited only an invented id
        Assert.Equal(1, v.Value.DroppedBullets);
        foreach (var text in new[] { s.Heading, b.Text })
        {
            Assert.DoesNotContain("<script", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", text);
        }
    }

    [Fact]
    public void Every_part_is_a_notes_request_bound_to_the_press_with_only_its_own_segments_citable()
    {
        var o = NotesOptions.Default with { ChunkChars = 200 };
        var plan = NotesRequests.Build(User, Session, Press, Enumerable.Range(1, 12).Select(n => Seg(n)).ToList(), false, o);
        Assert.True(plan.Parts.Count > 1);
        Assert.False(plan.Truncated);
        Assert.Equal(12, plan.Used.Count);
        foreach (var p in plan.Parts)
        {
            Assert.Equal(AiWorkflow.Notes, p.Request.Workflow);
            Assert.True(p.Request.IsAfterStop);
            Assert.Equal(Press, p.Request.PressId);
            Assert.Equal(Session, p.Request.SessionId);
            Assert.True(p.Request.EffectiveScope.IsLecture); // lecture budget pool
            Assert.Equal(PromptLibrary.Notes, p.Request.System);
            Assert.Equal(p.Segments.Select(s => s.SegmentId).Order(), p.Context.Allowed.Keys.Order());
        }
        Assert.Equal(12, plan.Parts.Sum(p => p.Segments.Count));
    }

    [Fact]
    public void A_lecture_longer_than_the_part_limit_is_cut_at_the_end_and_marked_truncated()
    {
        var o = NotesOptions.Default with { ChunkChars = 200, MaxChunks = 2 };
        var plan = NotesRequests.Build(User, Session, Press, Enumerable.Range(1, 30).Select(n => Seg(n)).ToList(), false, o);
        Assert.Equal(2, plan.Parts.Count);
        Assert.True(plan.Truncated);
        Assert.Equal(1, plan.Used[0].SegmentNo); // the start is kept
        Assert.True(plan.Used.Count < 30);
    }

    [Fact]
    public void The_notes_prompt_carries_the_shared_language_rules_and_its_version_is_pinned()
    {
        Assert.Equal("lecture-notes.v1", PromptLibrary.NotesVersion);
        Assert.Contains(PromptLibrary.LanguageRules, PromptLibrary.Notes, StringComparison.Ordinal);
        Assert.Contains("\"segmentIds\"", PromptLibrary.Notes);
        Assert.Contains("never write timestamps", PromptLibrary.Notes);
    }

    [Fact]
    public void Notes_options_are_bounded_and_the_reaper_waits_for_the_longest_call()
    {
        var o = T.Options(new KeyValuePair<string, string?>("Ai:Notes:TimeoutSeconds", "90"));
        Assert.Equal(TimeSpan.FromSeconds(90), o.LongestCallTimeout);
        Assert.True(o.Notes.PerRequestMicro > 0);
        Assert.Throws<InvalidOperationException>(() => AiProviderGuard.Validate(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection([new("Ai:Notes:PerRequestMicroUsd", "0")]).Build(),
            T.Options(new KeyValuePair<string, string?>("Ai:Notes:PerRequestMicroUsd", "0")), true));
    }
}
