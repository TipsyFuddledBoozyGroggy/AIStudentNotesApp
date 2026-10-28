using Tether.Ai.Validation;
using Tether.Ai.Provider;
using Tether.Ai.Summary;

namespace Tether.AiTests;

public class SummaryValidatorTests
{
    private static SummaryContext Ctx(int rev = 3) => new(rev, T.Allowed(T.Line(1, 1000, "a"), T.Line(2, 5000, "b"), T.Line(3, 9000, "c")));

    [Fact]
    public void A_grounded_delta_is_accepted_and_timecodes_come_from_the_stored_segments()
    {
        var v = SummaryValidator.Validate(T.SummaryJson(3, "Cells divide.", ("mitosis", "Mitosis has phases.", ["seg_2"])), Ctx());
        Assert.True(v.Ok);
        var t = Assert.Single(v.Value!.Topics);
        Assert.Equal(new CitationRef("seg_2", 5000), Assert.Single(t.Citations));
    }

    [Fact]
    public void Model_supplied_timecodes_are_rejected_as_unknown_fields()
    {
        var json = """{"baseRevision":3,"tldr":"x","topicUpdates":[{"topic":"t","text":"y","segmentIds":["seg_1"],"startMs":99}],"newTerms":[]}""";
        Assert.Equal(ValidationCodes.SchemaInvalid, SummaryValidator.Validate(json, Ctx()).Error);
    }

    [Fact]
    public void Citations_to_segments_that_were_not_in_the_request_are_dropped_and_a_topic_left_without_any_is_dropped()
    {
        var v = SummaryValidator.Validate(T.SummaryJson(3, "x", ("a", "kept", ["seg_1", "seg_999"]), ("b", "gone", ["seg_404"])), Ctx());
        Assert.True(v.Ok);
        Assert.Equal("a", Assert.Single(v.Value!.Topics).Key);
        Assert.Equal(1, v.Value.DroppedTopics);
        Assert.Equal(2, v.Value.DroppedCitations);
    }

    [Fact]
    public void A_delta_with_no_valid_citation_at_all_is_ungrounded()
    {
        Assert.Equal(ValidationCodes.Ungrounded, SummaryValidator.Validate(T.SummaryJson(3, "x", ("b", "y", ["seg_404"])), Ctx()).Error);
    }

    [Fact]
    public void A_stale_base_revision_is_rejected()
    {
        Assert.Equal(ValidationCodes.StaleBase, SummaryValidator.Validate(T.SummaryJson(2, "x", ("a", "y", ["seg_1"])), Ctx(3)).Error);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"baseRevision":"3","tldr":"x","topicUpdates":[],"newTerms":[]}""")]
    [InlineData("""{"baseRevision":3,"topicUpdates":[],"newTerms":[]}""")]
    [InlineData("""{"baseRevision":3,"tldr":"x","topicUpdates":[],"newTerms":[],"tools":["calendar"]}""")]
    public void Malformed_or_shape_violating_output_is_rejected(string json)
    {
        Assert.False(SummaryValidator.Validate(json, Ctx()).Ok);
    }

    [Fact]
    public void Overlong_fields_are_rejected_with_the_overlong_code()
    {
        Assert.Equal(ValidationCodes.Overlong, SummaryValidator.Validate(T.SummaryJson(3, new string('x', 5000), ("a", "y", ["seg_1"])), Ctx()).Error);
    }

    [Fact]
    public void Links_markup_and_control_characters_in_output_are_sanitised_not_passed_through()
    {
        var v = SummaryValidator.Validate(T.SummaryJson(3, "See https://evil.example/x <b>now</b>", ("a", "![i](https://evil.example/p.png) ok", ["seg_1"])), Ctx());
        Assert.True(v.Ok);
        Assert.True(v.Value!.Sanitized);
        Assert.DoesNotContain("evil.example", v.Value.Tldr);
        Assert.DoesNotContain("<", v.Value.Tldr);
        Assert.DoesNotContain("evil.example", v.Value.Topics[0].Text);
    }
}

public class CatchUpValidatorTests
{
    private static CatchUpContext Ctx() => new(T.Allowed(T.Line(1, 1000, "a"), T.Line(2, 5000, "b"), T.Line(3, 9000, "c")), true);

    private static string Json(string nowBasis = "lecture", string[]? nowIds = null, object? shortList = null, string now = "Discussing mitosis phases.", object? extra = null)
    {
        var o = new Dictionary<string, object?>
        {
            ["nowDiscussing"] = new { text = now, basis = nowBasis, segmentIds = nowIds ?? ["seg_3"] },
            ["shortCatchUp"] = shortList ?? new[] { new { text = "Cells copy DNA first.", basis = "lecture", segmentIds = new[] { "seg_1" } } },
            ["detail"] = Array.Empty<object>(),
            ["keyTerms"] = Array.Empty<object>(),
            ["clarifyingQuestions"] = new[] { "Could you repeat the last step?" },
            ["confidence"] = "medium",
            ["gaps"] = Array.Empty<string>(),
        };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) o[p.Name] = p.GetValue(extra);
        return T.Json(o);
    }

    [Fact]
    public void A_valid_result_resolves_timecodes_from_segment_ids()
    {
        var v = CatchUpValidator.Validate(Json(), Ctx());
        Assert.True(v.Ok);
        Assert.Equal(new CitationRef("seg_3", 9000), Assert.Single(v.Value!.NowDiscussing.Citations));
        Assert.Equal(0, v.Value.Downgraded);
    }

    [Fact]
    public void A_lecture_claim_whose_citations_do_not_resolve_is_downgraded_to_uncertain_and_loses_them()
    {
        var v = CatchUpValidator.Validate(Json(nowIds: ["seg_404"]), Ctx());
        Assert.True(v.Ok);
        Assert.Equal(Basis.Uncertain, v.Value!.NowDiscussing.Basis);
        Assert.Empty(v.Value.NowDiscussing.Citations);
        Assert.Equal(1, v.Value.Downgraded);
    }

    [Fact]
    public void General_knowledge_and_uncertain_claims_never_carry_lecture_citations()
    {
        var v = CatchUpValidator.Validate(Json(nowBasis: "general_knowledge", nowIds: ["seg_1"]), Ctx());
        Assert.True(v.Ok);
        Assert.Equal(Basis.GeneralKnowledge, v.Value!.NowDiscussing.Basis);
        Assert.Empty(v.Value.NowDiscussing.Citations);
    }

    [Fact]
    public void Unknown_basis_labels_are_invalid()
    {
        Assert.False(CatchUpValidator.Validate(Json(nowBasis: "certain"), Ctx()).Ok);
    }

    [Fact]
    public void Word_limits_are_enforced()
    {
        Assert.Equal(ValidationCodes.Overlong, CatchUpValidator.Validate(Json(now: string.Join(' ', Enumerable.Repeat("word", 26))), Ctx()).Error);
        var longBullet = new[] { new { text = string.Join(' ', Enumerable.Repeat("w", 21)), basis = "lecture", segmentIds = new[] { "seg_1" } } };
        Assert.Equal(ValidationCodes.Overlong, CatchUpValidator.Validate(Json(shortList: longBullet), Ctx()).Error);
    }

    [Fact]
    public void More_than_three_bullets_or_questions_is_invalid()
    {
        var four = Enumerable.Range(0, 4).Select(_ => new { text = "x", basis = "lecture", segmentIds = new[] { "seg_1" } }).ToArray();
        Assert.False(CatchUpValidator.Validate(Json(shortList: four), Ctx()).Ok);
        var q = Json(extra: new { clarifyingQuestions = new[] { "a?", "b?", "c?", "d?" } });
        Assert.False(CatchUpValidator.Validate(q, Ctx()).Ok);
    }

    [Fact]
    public void When_the_window_has_segments_but_nothing_is_grounded_the_result_is_ungrounded()
    {
        var allBad = new[] { new { text = "x", basis = "lecture", segmentIds = new[] { "seg_404" } } };
        Assert.Equal(ValidationCodes.Ungrounded, CatchUpValidator.Validate(Json(nowIds: ["seg_404"], shortList: allBad), Ctx()).Error);
    }

    [Fact]
    public void Unknown_fields_such_as_a_tool_request_are_rejected()
    {
        Assert.False(CatchUpValidator.Validate(Json(extra: new { toolCalls = new[] { "calendar.create" } }), Ctx()).Ok);
    }

    [Fact]
    public void Active_content_in_claims_is_sanitised()
    {
        var v = CatchUpValidator.Validate(Json(now: "Visit https://evil.example now"), Ctx());
        Assert.True(v.Ok);
        Assert.True(v.Value!.Sanitized);
        Assert.DoesNotContain("evil.example", v.Value.NowDiscussing.Text);
    }
}

public class SummaryMergerTests
{
    private static TopicState Topic(string key, string text, string seg = "seg_1") => new(key, text, [new CitationRef(seg, 1)]);

    [Fact]
    public void Updates_replace_in_place_by_key_and_new_topics_append()
    {
        var existing = new[] { Topic("a", "old a"), Topic("b", "old b") };
        var delta = new SummaryDelta("t", [Topic("A", "new a", "seg_2"), Topic("c", "new c", "seg_3")], [], 0, 0, false);
        var merged = SummaryMerger.Merge(existing, delta);
        Assert.Equal(["new a", "old b", "new c"], merged.Select(m => m.Text));
    }

    [Fact]
    public void The_topic_list_is_capped_dropping_the_oldest()
    {
        var existing = Enumerable.Range(0, 20).Select(i => Topic($"t{i}", $"text {i}")).ToArray();
        var merged = SummaryMerger.Merge(existing, new SummaryDelta("t", [Topic("new", "n")], [], 0, 0, false), 20);
        Assert.Equal(20, merged.Count);
        Assert.Equal("t1", merged[0].Key);
        Assert.Equal("new", merged[^1].Key);
    }

    [Fact]
    public void Citations_are_the_distinct_union_in_order_and_capped_to_the_newest()
    {
        var topics = new[] { Topic("a", "a", "seg_1"), Topic("b", "b", "seg_1"), Topic("c", "c", "seg_2") };
        Assert.Equal(["seg_1", "seg_2"], SummaryMerger.Citations(topics).Select(c => c.SegmentId));
        var many = Enumerable.Range(0, 80).Select(i => Topic($"k{i}", "x", $"seg_{i}")).ToArray();
        var cited = SummaryMerger.Citations(many, 50);
        Assert.Equal(50, cited.Count);
        Assert.Equal("seg_79", cited[^1].SegmentId);
    }
}
