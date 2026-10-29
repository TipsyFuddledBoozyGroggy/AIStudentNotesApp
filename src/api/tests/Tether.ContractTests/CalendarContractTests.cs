using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Tether.ContractTests;

/// <summary>
/// Schema and semantic checks for the DRAFT calendar.v1 contract (docs/API_CONTRACTS_CALENDAR.md). It mirrors the syllabus tests but keeps
/// its fixtures under calendar.v1/fixtures/{valid,invalid} so the frozen top-level fixture set (and ContractFixtureTests, which
/// registers only lecture.v1 and syllabus.v1) is untouched. Fixture naming: "{schema-basename}--{case}.json".
/// Everything here is mock / fake-Google only; no test calls Google.
/// </summary>
public sealed class CalendarContractTests
{
    private static readonly string ContractsDir = FindContractsDir();
    private static readonly string SchemaDir = Path.Combine(ContractsDir, "calendar.v1");
    private static readonly string FixtureDir = Path.Combine(SchemaDir, "fixtures");
    private static readonly Lazy<(EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas)> Registry = new(BuildRegistry);

    private static string FindContractsDir()
    {
        var env = Environment.GetEnvironmentVariable("TETHER_CONTRACTS_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src", "shared", "contracts");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(d.FullName, "shared", "contracts");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/shared/contracts not found; set TETHER_CONTRACTS_DIR.");
    }

    private static string? FindDoc()
    {
        for (var d = new DirectoryInfo(ContractsDir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "docs", "API_CONTRACTS_CALENDAR.md");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string DocText()
    {
        var p = FindDoc();
        Assert.True(p is not null, "docs/API_CONTRACTS_CALENDAR.md was not found; mount the repository root (this test must not pass vacuously).");
        return File.ReadAllText(p!);
    }

    private static (EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas) BuildRegistry()
    {
        Assert.True(Directory.Exists(SchemaDir), "src/shared/contracts/calendar.v1 does not exist");
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var file in Directory.GetFiles(SchemaDir, "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(file));
            schemas[Path.GetFileName(file)[..^".schema.json".Length]] = schema;
            options.SchemaRegistry.Register(schema);
        }
        return (options, schemas);
    }

    private static IEnumerable<string> SchemaNames() =>
        Directory.GetFiles(SchemaDir, "*.schema.json").Select(f => Path.GetFileName(f)[..^".schema.json".Length]).Order();

    private static JsonNode Schema(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, name + ".schema.json")))!;
    private static JsonNode Defs() => Schema("calendar-common")["$defs"]!;

    public static IEnumerable<object[]> Fixtures(string kind) =>
        Directory.GetFiles(Path.Combine(FixtureDir, kind), "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> ValidFixtures() => Fixtures("valid");
    public static IEnumerable<object[]> InvalidFixtures() => Fixtures("invalid");

    private static IEnumerable<(string File, JsonNode Node)> FixtureNodes(string kind, string? schema = null) =>
        Directory.GetFiles(Path.Combine(FixtureDir, kind), "*.json").Order()
            .Where(f => schema is null || Path.GetFileName(f).StartsWith(schema + "--", StringComparison.Ordinal))
            .Select(f => (Path.GetFileName(f), JsonNode.Parse(File.ReadAllText(f))!));

    private static string SchemaOf(string file)
    {
        var i = file.IndexOf("--", StringComparison.Ordinal);
        Assert.True(i > 0, $"Fixture {file} must be named '{{schema}}--{{case}}.json'");
        return file[..i];
    }

    private static bool Validate(string fixtureFile, string kind)
    {
        var (options, schemas) = Registry.Value;
        var name = SchemaOf(fixtureFile);
        Assert.True(schemas.TryGetValue(name, out var schema), $"No schema '{name}' for fixture {fixtureFile}");
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir, kind, fixtureFile)));
        return schema!.Evaluate(node, options).IsValid;
    }

    [Theory, MemberData(nameof(ValidFixtures))]
    public void Valid_fixture_passes(string file) => Assert.True(Validate(file, "valid"), $"{file} should be valid");

    [Theory, MemberData(nameof(InvalidFixtures))]
    public void Invalid_fixture_fails(string file) => Assert.False(Validate(file, "invalid"), $"{file} should be rejected");

    // ---- structure -------------------------------------------------------------------------------------------------

    [Fact]
    public void Schema_set_is_present_and_every_name_is_calendar_prefixed()
    {
        var names = SchemaNames().ToList();
        Assert.True(names.Count >= 26, $"expected the full calendar.v1 set, found {names.Count}");
        Assert.All(names, n => Assert.StartsWith("calendar", n));
        foreach (var n in names)
            Assert.Equal($"https://schemas.tether.invalid/calendar.v1/{n}.schema.json", Schema(n)["$id"]!.GetValue<string>());
    }

    [Fact]
    public void Every_schema_has_at_least_one_valid_and_one_invalid_fixture()
    {
        var valid = FixtureNodes("valid").Select(f => SchemaOf(f.File)).ToHashSet();
        var invalid = FixtureNodes("invalid").Select(f => SchemaOf(f.File)).ToHashSet();
        foreach (var n in SchemaNames().Where(n => n != "calendar-common"))
        {
            Assert.Contains(n, valid);
            Assert.Contains(n, invalid);
        }
    }

    [Fact]
    public void Every_object_schema_is_strict()
    {
        foreach (var name in SchemaNames()) AssertStrict(Schema(name), name);
    }

    private static void AssertStrict(JsonNode? node, string where)
    {
        switch (node)
        {
            case JsonObject o:
                if (o.ContainsKey("properties") && o["type"]?.GetValue<string>() == "object")
                    Assert.True(o["additionalProperties"]?.GetValue<bool>() == false, $"{where}: object with properties must set additionalProperties:false");
                foreach (var kv in o) AssertStrict(kv.Value, where);
                break;
            case JsonArray a:
                foreach (var i in a) AssertStrict(i, where);
                break;
        }
    }

    [Fact]
    public void Shared_definitions_are_identical_to_the_frozen_syllabus_v1_definitions()
    {
        var syllabus = JsonNode.Parse(File.ReadAllText(Path.Combine(ContractsDir, "syllabus.v1", "syllabus-common.schema.json")))!["$defs"]!;
        var calendar = Defs();
        string[] shared = { "id", "itemKey", "timestamp", "localDate", "localTime", "ianaZone", "etag", "sha256Hex", "editableKind", "itemKind",
            "flag", "timezoneSource", "title", "textLine", "idempotencyKey", "syncState", "noticeVersion" };
        foreach (var d in shared)
        {
            Assert.True(calendar[d] is not null, $"calendar-common is missing {d}");
            // Descriptions may differ; the validating members must not.
            Assert.Equal(Strip(syllabus[d]!), Strip(calendar[d]!));
        }
    }

    private static string Strip(JsonNode n)
    {
        var copy = JsonNode.Parse(n.ToJsonString())!.AsObject();
        copy.Remove("description");
        return copy.ToJsonString();
    }

    [Fact]
    public void Calendar_v1_does_not_modify_or_depend_on_the_frozen_contracts()
    {
        foreach (var name in SchemaNames())
            Assert.DoesNotContain("syllabus.v1/", File.ReadAllText(Path.Combine(SchemaDir, name + ".schema.json")));
        Assert.False(Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", "valid"), "calendar*").Any(), "calendar fixtures live under calendar.v1/fixtures");
    }

    // ---- secrets and remote ids never cross the wire ---------------------------------------------------------------

    // CV-24: matched case-insensitively as substrings, so Token, calendar_id, eventID and accessTokenHash are all caught.
    private static readonly string[] ForbiddenNameParts =
    {
        "token", "secret", "verifier", "authcode", "authorizationcode", "password", "kek", "ciphertext", "extendedproperties", "snippet", "notes",
        "description", "calendarid", "calendar_id", "eventid", "event_id", "remoteevent", "googleevent", "googlesub", "fingerprint", "etagremote",
    };

    [Theory]
    [InlineData("Token", true)] [InlineData("accessTokenHash", true)] [InlineData("calendar_id", true)] [InlineData("eventID", true)]
    [InlineData("googleSub", true)] [InlineData("sub", true)] [InlineData("syllabusId", false)] [InlineData("entryId", false)] [InlineData("previewDigest", false)]
    public void Forbidden_name_matcher_is_case_insensitive_and_substring_based(string name, bool forbidden) =>
        Assert.Equal(forbidden, IsForbiddenName(name));

    private static bool IsForbiddenName(string n) =>
        n.Equals("sub", StringComparison.OrdinalIgnoreCase) || ForbiddenNameParts.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void No_schema_defines_a_token_secret_remote_id_or_free_text_property()
    {
        foreach (var name in SchemaNames()) Walk(Schema(name), name);

        void Walk(JsonNode? n, string where)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["properties"] is JsonObject props)
                        foreach (var p in props.Select(kv => kv.Key))
                            Assert.False(IsForbiddenName(p), $"{where}: forbidden property name {p}");
                    foreach (var kv in o) Walk(kv.Value, where);
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i, where);
                    break;
            }
        }
    }

    [Fact]
    public void No_fixture_or_schema_text_contains_a_token_shaped_string()
    {
        var shapes = new[] { new Regex(@"ya29\.[A-Za-z0-9_-]{10,}"), new Regex(@"\b1//0[A-Za-z0-9_-]{20,}"), new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\."),
            new Regex(@"GOCSPX-[A-Za-z0-9_-]{10,}"), new Regex(@"refresh_token", RegexOptions.IgnoreCase), new Regex(@"access_token", RegexOptions.IgnoreCase),
            new Regex(@"client_secret", RegexOptions.IgnoreCase) };
        var files = Directory.GetFiles(SchemaDir, "*.json", SearchOption.AllDirectories);
        Assert.True(files.Length > 40);
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (var s in shapes) Assert.False(s.IsMatch(text), $"{Path.GetRelativePath(SchemaDir, f)} matches {s}");
        }
    }

    // ---- Google sign-in / connect surface ---------------------------------------------------------------------------

    private static readonly string[] AllowedAuthParams =
        { "client_id", "redirect_uri", "response_type", "scope", "state", "nonce", "code_challenge", "code_challenge_method", "access_type", "prompt", "login_hint" };      // CV-10: include_granted_scopes is NOT allowed

    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.app.created";
    private const string ExactRedirect = "https://app.tether.invalid/api/v1/auth/google/callback";

    [Fact]
    public void Auth_start_urls_request_only_allowlisted_scopes_params_and_the_exact_redirect_uri()
    {
        var seen = new HashSet<string>();
        foreach (var (file, node) in FixtureNodes("valid", "calendar-google-auth-start-response"))
        {
            var url = new Uri(node["authorizationUrl"]!.GetValue<string>());
            Assert.Equal("accounts.google.com", url.Host);
            var q = ParseQuery(url.Query);
            Assert.All(q.Keys, k => Assert.Contains(k, AllowedAuthParams));
            foreach (var req in new[] { "client_id", "redirect_uri", "response_type", "scope", "state", "nonce", "code_challenge", "code_challenge_method" })
                Assert.True(q.ContainsKey(req), $"{file}: missing {req}");
            Assert.Equal("code", q["response_type"]);
            Assert.Equal("S256", q["code_challenge_method"]);                 // Google defaults to plain when omitted (vendor report 9.8)
            Assert.Equal(ExactRedirect, q["redirect_uri"]);
            Assert.Matches("^[A-Za-z0-9_-]{43,128}$", q["state"]);
            Assert.Matches("^[A-Za-z0-9_-]{43,128}$", q["nonce"]);
            Assert.Matches("^[A-Za-z0-9_-]{43}$", q["code_challenge"]);
            var scopes = q["scope"].Split(' ');
            Assert.Equal(scopes.Length, scopes.Distinct().Count());
            var purpose = node["purpose"]!.GetValue<string>();
            seen.Add(purpose);
            if (purpose == "signin")
            {
                Assert.Equal(new[] { "email", "openid", "profile" }, scopes.Order().ToArray());      // identity only, no data scope at sign-in
                Assert.False(q.ContainsKey("access_type"));
                Assert.DoesNotContain("calendar", q["scope"]);
                Assert.Equal("select_account", q["prompt"]);                                          // shared lab computers (CV-04)
            }
            else
            {
                // openid is needed so the ID token's sub can be compared with the signed-in sub (PO-9d); the single data scope is calendar.app.created.
                Assert.Equal(new[] { "openid", CalendarScope }.Order().ToArray(), scopes.Order().ToArray());
                Assert.Equal("offline", q["access_type"]);
                Assert.False(q.ContainsKey("include_granted_scopes"));                              // CV-10: returned scope is exactly the requested set
                Assert.Equal("select_account consent", q["prompt"]);                                // CV-04: never auto-pick the other account
                Assert.Matches("^[0-9]{10,30}$", q["login_hint"]);                                  // the stored sub, not the email (CV-27)
            }
        }
        Assert.Equal(new[] { "connect", "signin" }, seen.Order().ToArray());
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public void Granted_scope_vocabulary_is_exactly_identity_plus_calendar_app_created()
    {
        var scopes = Defs()["googleScope"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).Order().ToArray();
        Assert.Equal(new[] { "calendar.app.created", "email", "openid", "profile" }, scopes);
    }

    [Fact]
    public void Callback_outcome_enum_pairs_signed_in_with_signin_and_connected_with_connect()
    {
        var seen = new HashSet<string>();
        foreach (var (file, node) in FixtureNodes("valid", "calendar-google-callback-outcome"))
        {
            var o = node["outcome"]!.GetValue<string>();
            seen.Add(o);
            if (o == "signed_in") Assert.Equal("signin", node["purpose"]!.GetValue<string>());
            if (o == "connected") Assert.Equal("connect", node["purpose"]!.GetValue<string>());
        }
        Assert.Contains("account_mismatch", seen);
        Assert.Contains("access_denied", seen);
    }

    // ---- uncertain dates cannot be expressed as events or reminders (invariant 4) -----------------------------------

    private static IEnumerable<JsonObject> EventLike(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                if (o.ContainsKey("allDay") && o.ContainsKey("start") && o.ContainsKey("end")) yield return o;
                foreach (var kv in o)
                    foreach (var e in EventLike(kv.Value)) yield return e;
                break;
            case JsonArray a:
                foreach (var i in a)
                    foreach (var e in EventLike(i)) yield return e;
                break;
        }
    }

    [Fact]
    public void Every_event_in_every_valid_fixture_has_a_resolved_date_a_timezone_and_a_course_prefixed_title()
    {
        var n = 0;
        foreach (var (file, node) in FixtureNodes("valid"))
            foreach (var e in EventLike(node))
            {
                n++;
                var start = e["start"]!;
                Assert.NotNull(start["date"]);
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", start["date"]!.GetValue<string>());
                Assert.False(string.IsNullOrEmpty(start["timezone"]?.GetValue<string>()), $"{file}: timezone must be shown");
                Assert.Contains(" · ", e["title"]!.GetValue<string>());        // "BIO 201 · Midterm exam" (PO-9i)
                if (e["allDay"]!.GetValue<bool>())
                {
                    Assert.Null(start["time"]);
                    Assert.Null(e["end"]!["time"]);
                    Assert.False(e["endDerived"]!.GetValue<bool>());
                }
                else
                {
                    Assert.NotNull(start["time"]);
                    Assert.NotNull(e["end"]!["time"]);
                }
            }
        Assert.True(n >= 12, $"checked only {n} events");
    }

    [Fact]
    public void Preview_lists_excluded_items_with_a_reason_and_never_turns_an_excluded_item_into_an_entry()
    {
        var reasons = new HashSet<string>();
        foreach (var (file, node) in FixtureNodes("valid", "calendar-preview"))
        {
            var entryItems = node["entries"]!.AsArray().Select(e => e!["itemId"]!.GetValue<string>()).ToHashSet();
            foreach (var x in node["excluded"]!.AsArray())
            {
                Assert.DoesNotContain(x!["itemId"]!.GetValue<string>(), entryItems);
                reasons.Add(x["reason"]!.GetValue<string>());
            }
        }
        Assert.Contains("needs_date", reasons);
        Assert.Contains("not_approved", reasons);
    }

    // ---- reminders --------------------------------------------------------------------------------------------------

    [Fact]
    public void Reminder_rules_hold_in_every_valid_fixture()
    {
        var reminders = 0;
        foreach (var (file, node) in FixtureNodes("valid"))
        {
            Walk(node, file);
        }
        Assert.True(reminders >= 6, $"checked only {reminders} reminders");

        void Walk(JsonNode? n, string file)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["reminders"] is JsonArray arr && o.ContainsKey("allDay"))
                    {
                        Assert.True(arr.Count <= 3, $"{file}: max 3 reminders per event (Extra runway); Google allows 5");
                        foreach (var r in arr)
                        {
                            reminders++;
                            var minutes = r!["minutesBefore"]!.GetValue<int>();
                            Assert.InRange(minutes, 0, 40320);                         // Google: 0..40320 minutes (4 weeks)
                            Assert.Equal("popup", r["method"]!.GetValue<string>());      // popup only, never email
                            if (r["fireAt"] is JsonObject f)
                            {
                                var t = f["time"]!.GetValue<string>();
                                Assert.True(string.CompareOrdinal(t, "08:00") >= 0 && string.CompareOrdinal(t, "20:00") <= 0, $"{file}: no reminder between 21:00 and 08:00 (got {t})");
                            }
                        }
                    }
                    foreach (var kv in o) Walk(kv.Value, file);
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i, file);
                    break;
            }
        }
    }

    [Fact]
    public void Preview_never_plans_more_than_three_reminders_on_one_day_and_none_for_the_tether_calendar()
    {
        foreach (var (file, node) in FixtureNodes("valid", "calendar-preview"))
        {
            var perDay = node["entries"]!.AsArray()
                .SelectMany(e => e!["reminders"]!.AsArray())
                .GroupBy(r => r!["fireAt"]!["date"]!.GetValue<string>())
                .ToDictionary(g => g.Key, g => g.Count());
            Assert.All(perDay, kv => Assert.True(kv.Value <= 3, $"{file}: {kv.Key} has {kv.Value} reminders"));
            Assert.Equal(perDay.Values.Sum(), node["counts"]!["reminders"]!.GetValue<int>());
            if (node["destination"]!.GetValue<string>() == "tether")
            {
                Assert.Empty(perDay);
                Assert.Equal("none", node["reminders"]!["appliesTo"]!.GetValue<string>());
                Assert.Null(node["reminders"]!["preset"]);
            }
            else
            {
                Assert.Equal("google_events", node["reminders"]!["appliesTo"]!.GetValue<string>());
            }
        }
    }

    // ---- counts and state rules -------------------------------------------------------------------------------------

    [Fact]
    public void Preview_counts_match_the_listed_entries_and_the_digest_is_hex()
    {
        var checkedPreviews = 0;
        foreach (var (file, node) in FixtureNodes("valid", "calendar-preview"))
        {
            var entries = node["entries"]!.AsArray();
            var by = entries.GroupBy(e => e!["action"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.Count());
            var counts = node["counts"]!;
            Assert.Equal(by.GetValueOrDefault("create"), counts["create"]!.GetValue<int>());
            Assert.Equal(by.GetValueOrDefault("update"), counts["update"]!.GetValue<int>());
            Assert.Equal(by.GetValueOrDefault("unchanged"), counts["unchanged"]!.GetValue<int>());
            Assert.Equal(node["excluded"]!.AsArray().Count, counts["excluded"]!.GetValue<int>());
            Assert.Equal(node["leftAlone"]!.AsArray().Count, counts["leftAlone"]!.GetValue<int>());
            checkedPreviews++;
        }
        Assert.True(checkedPreviews >= 3);
    }

    [Fact]
    public void Run_state_and_counts_are_consistent_in_every_valid_run()
    {
        var states = new HashSet<string>();
        foreach (var (file, node) in FixtureNodes("valid", "calendar-run"))
        {
            var state = node["state"]!.GetValue<string>();
            states.Add(state);
            var c = node["counts"]!;
            var results = node["results"]!.AsArray();
            if (node["kind"]!.GetValue<string>() != "reminders" && results.Count > 0)
            {
                foreach (var (key, outcome) in new[] { ("created", "created"), ("updated", "updated"), ("unchanged", "unchanged"), ("removed", "removed"),
                             ("notPresent", "not_present"), ("leftAlone", "left_alone"), ("retrying", "retrying"), ("failed", "failed"), ("notEligible", "not_eligible") })
                    Assert.Equal(results.Count(r => r!["outcome"]!.GetValue<string>() == outcome), c[key]!.GetValue<int>());
            }
            var terminal = state is "succeeded" or "partial" or "failed" or "nothing_to_do" or "cancelled";
            if (terminal) Assert.Equal(0, c["retrying"]!.GetValue<int>());
            var okCount = c["created"]!.GetValue<int>() + c["updated"]!.GetValue<int>() + c["removed"]!.GetValue<int>();
            switch (state)
            {
                case "succeeded": Assert.Equal(0, c["failed"]!.GetValue<int>()); Assert.True(okCount > 0); break;
                case "partial": Assert.True(c["failed"]!.GetValue<int>() > 0 && okCount > 0); break;
                case "failed": Assert.Equal(0, okCount); Assert.True(c["failed"]!.GetValue<int>() > 0); break;
                case "nothing_to_do": Assert.Equal(0, okCount + c["failed"]!.GetValue<int>()); break;
            }
            if (state is "planned") Assert.Null(node["startedAt"]);
            var progress = node["progress"]!;
            Assert.True(progress["done"]!.GetValue<int>() <= progress["total"]!.GetValue<int>());
            if (terminal && state != "cancelled") Assert.Equal(progress["total"]!.GetValue<int>(), progress["done"]!.GetValue<int>());
        }
        foreach (var s in new[] { "planned", "executing", "succeeded", "partial", "failed", "nothing_to_do", "cancelled" })
            Assert.Contains(s, states);
    }

    [Fact]
    public void Run_and_removal_requests_key_items_by_object_or_unique_array_so_duplicates_are_not_representable()
    {
        Assert.Contains("\"itemRevisions\"", File.ReadAllText(Path.Combine(SchemaDir, "calendar-preview-request.schema.json")));
        Assert.Contains("\"itemRevisions\"", File.ReadAllText(Path.Combine(SchemaDir, "calendar-run-request.schema.json")));
        var removal = Schema("calendar-removal-request")["properties"]!["itemIds"]!;
        Assert.True(removal["uniqueItems"]!.GetValue<bool>());
        var dup = "{\"itemRevisions\":{\"itm_1\":1,\"itm_1\":2}}";
        Assert.ThrowsAny<Exception>(() =>
            System.Text.Json.JsonDocument.Parse(dup, new System.Text.Json.JsonDocumentOptions { AllowDuplicateProperties = false }));
    }

    [Fact]
    public void Entries_carry_the_item_key_derived_exactly_as_in_syllabus_v1()
    {
        var checkedKeys = 0;
        foreach (var (file, node) in FixtureNodes("valid"))
            Walk(node);
        Assert.True(checkedKeys >= 8, $"verified only {checkedKeys} item keys");

        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["itemKey"]?.GetValue<string>() is { } key && o["syllabusId"]?.GetValue<string>() is { } sid && o["itemId"]?.GetValue<string>() is { } iid)
                    {
                        Assert.Equal(DeriveItemKey(sid, iid), key);
                        checkedKeys++;
                    }
                    foreach (var kv in o) Walk(kv.Value);
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i);
                    break;
            }
        }
    }

    private static string DeriveItemKey(string syllabusId, string itemId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"tether.syllabus.item.v1\n{syllabusId}\n{itemId}"));
        return "ik_" + Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..32];
    }

    // ---- Google event id derivation (pinned vector) -----------------------------------------------------------------

    /// <summary>
    /// The deterministic Google event id: lowercase base32hex (a-v, 0-9) of SHA-256 over a domain-separated string, first 52 characters.
    /// Unkeyed on purpose: losing a server key must not change ids and cause duplicates. `generation` is bumped only when Tether itself
    /// deleted the event and must re-create it (reuse of a deleted id is undocumented, vendor report 3.4).
    /// </summary>
    internal static string GoogleEventId(string remoteCalendarId, string itemKey, string? occurrenceDate, int generation)
    {
        var input = $"tether.calendar.event.v1\n{remoteCalendarId}\n{itemKey}\n{occurrenceDate ?? "-"}\n{generation}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        const string alphabet = "0123456789abcdefghijklmnopqrstuv";
        var sb = new StringBuilder();
        var bits = string.Concat(bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        bits = bits.PadRight((bits.Length + 4) / 5 * 5, '0');
        for (var i = 0; i < bits.Length; i += 5) sb.Append(alphabet[Convert.ToInt32(bits.Substring(i, 5), 2)]);
        return sb.ToString()[..52];
    }

    [Fact]
    public void Google_event_id_derivation_has_a_pinned_vector_and_meets_google_format_rules()
    {
        const string cal = "cal_fake_0001@group.calendar.google.com";
        const string key = "ik_zmzduAT9Xtu-VKSA5d29Tq_CIpIywcbD";
        var id = GoogleEventId(cal, key, "2026-11-12", 1);
        Assert.Equal("blelvrn2dn22ig5gqu6s8nmdmbek7ts2orsj5230f3o7n1b98dig", id);        // also written in docs/API_CONTRACTS_CALENDAR.md
        Assert.Matches("^[a-v0-9]{52}$", id);                                              // events resource: a-v and 0-9, 5..1024 chars
        Assert.NotEqual(id, GoogleEventId(cal, key, "2026-11-12", 2));                      // generation bump gives a fresh id
        Assert.NotEqual(id, GoogleEventId(cal, key, "2026-11-19", 1));
        Assert.NotEqual(id, GoogleEventId("cal_fake_0002@group.calendar.google.com", key, "2026-11-12", 1));
        Assert.Matches("^[a-v0-9]{52}$", GoogleEventId(cal, key, null, 1));
        Assert.Contains(id, DocText());
    }

    [Theory]
    [InlineData("abcdefgh", true)]
    [InlineData("short", false)]
    [InlineData("new\nline-key-1", false)]
    [InlineData("has space in it", false)]
    public void Idempotency_key_format_is_the_syllabus_format(string key, bool ok)
    {
        var re = new Regex(Defs()["idempotencyKey"]!["pattern"]!.GetValue<string>());
        Assert.Equal(ok, re.IsMatch(key));
    }

    // ---- caps and vocabulary agree with the document -----------------------------------------------------------------

    [Fact]
    public void Caps_in_schemas_match_the_documented_constants()
    {
        Assert.Equal(500, Schema("calendar-preview")["properties"]!["entries"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(5000, Schema("calendar-export")["properties"]!["entries"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(100, Schema("calendar-run-request")["properties"]!["itemRevisions"]!["maxProperties"]!.GetValue<int>());
        Assert.Equal(100, Schema("calendar-removal-request")["properties"]!["itemIds"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(3, Defs()["entryReminders"]!["maxItems"]!.GetValue<int>());
        var doc = DocText();
        Assert.Contains("500 events per run", doc);
        Assert.Contains("5,000 entries per user", doc);
        Assert.Contains("100 items per request", doc);
    }

    [Fact]
    public void Doc_is_a_draft_not_frozen_and_documents_every_enum_value_and_error_code()
    {
        var doc = DocText();
        Assert.Contains("DRAFT", doc);
        Assert.Contains("NOT frozen", doc);
        Assert.DoesNotContain("FROZEN v1", doc);
        Assert.Contains("Open questions", doc);
        Assert.Contains("security-engineer", doc);
        var defs = Defs();
        foreach (var def in new[] { "errorCode", "entryErrorCode", "destination", "settingsDestination", "reminderPreset", "courseReminderSetting", "entryState",
                     "connectionStatus", "runKind", "runState", "outcome", "skippedReminderReason", "excludedReason", "leftAloneReason", "callbackOutcome", "googleScope",
                     "previewAction", "calendarState", "eventsAction", "revocation", "actionPreviewKind", "resumeAction" })
        {
            Assert.True(defs[def]?["enum"] is JsonArray, $"calendar-common must define enum {def}");
            foreach (var v in defs[def]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()))
                Assert.Contains($"`{v}`", doc);
        }
    }

    [Fact]
    public void Doc_documents_every_route_in_the_contract()
    {
        var doc = DocText();
        string[] routes =
        {
            "POST /api/v1/auth/google/signin/start", "POST /api/v1/auth/google/connect/start", "GET /api/v1/auth/google/callback",
            "GET /api/v1/integrations/google/connection", "POST /api/v1/integrations/google/connection/disconnect",
            "GET /api/v1/schedule/settings", "PUT /api/v1/schedule/settings", "GET /api/v1/schedule/entries", "GET /api/v1/schedule/entries/{entryId}",
            "POST /api/v1/schedule/previews", "POST /api/v1/schedule/runs", "GET /api/v1/schedule/runs/{runId}", "GET /api/v1/schedule/runs/active",
            "POST /api/v1/schedule/runs/{runId}/cancel", "POST /api/v1/schedule/removals", "POST /api/v1/schedule/reminder-updates", "GET /api/v1/schedule/export",
            "POST /api/v1/schedule/removal-previews", "POST /api/v1/schedule/reminder-update-previews", "POST /api/v1/schedule/resume-previews",
            "POST /api/v1/schedule/resumptions", "POST /api/v1/integrations/google/connection/disconnect-preview",
        };
        foreach (var r in routes) Assert.Contains(r, doc);
    }

    // ---- CV-24: rule rows are anchored by id; deleting a rule's text fails its test --------------------------------------------------

    private static readonly Dictionary<string, string[]> RulePhrases = new()
    {
        ["CAL-01"] = new[] { "openid email profile", "calendar.app.created", "S256", "select_account", "include_granted_scopes" },
        ["CAL-02"] = new[] { "calendar id", "remote event id", "sub", "token" },
        ["CAL-03"] = new[] { "needs_date", "zero", "gateway" },
        ["CAL-04"] = new[] { "previewDigest", "plan_changed", "zero writes", "removal", "reminder-update" },
        ["CAL-05"] = new[] { "callback", "settings save", "before each Google write", "not_eligible" },
        ["CAL-06"] = new[] { "account_mismatch", "another Tether user", "does not call revoke", "select_account" },
        ["CAL-07"] = new[] { "single use", "binder", "state_invalid", "10 minutes" },
        ["CAL-08"] = new[] { "POST", "Origin", "login CSRF", "X-Tether-CSRF" },
        ["CAL-09"] = new[] { "AAD", "ciphertext", "export" },
        ["CAL-10"] = new[] { "AI never", "log-canary", "code", "state", "id_token", "calendar id", "event title", "Npgsql", "System.Net.Http", "ProblemDetails" },
        ["CAL-11"] = new[] { "404", "two users", "courseOverrides", "cursor" },
        ["CAL-12"] = new[] { "replay", "Idempotency-Key", "crash", "fresh key" },
        ["CAL-13"] = new[] { "base32hex", "generation", "ledger", "add-remove-add" },
        ["CAL-14"] = new[] { "never recreate", "If-Match", "extendedProperties", "edited_in_google", "intended fingerprint" },
        ["CAL-15"] = new[] { "popup", "40320", "quiet hours", "3 reminders", "assignment" },
        ["CAL-16"] = new[] { "invalid_grant", "confirmed", "circuit breaker" },
        ["CAL-17"] = new[] { "remove", "revoke", "24 h", "account deletion", "never blocks" },
        ["CAL-18"] = new[] { "TextPolicy", "composed title", "allowlist", "no query" },
        ["CAL-19"] = new[] { "allowlist", "host", "AllowAutoRedirect" },
        ["CAL-20"] = new[] { "AllowDuplicateProperties", "UnmappedMemberHandling" },
        ["CAL-21"] = new[] { "7-day", "invalid_grant", "fake" },
        ["CAL-22"] = new[] { "Mode=Fake", "Development" },
        ["CAL-23"] = new[] { "iss", "sub", "email", "session id", "unique index", "nonce" },
        ["CAL-24"] = new[] { "invalid_grant", "invalid_client", "accessNotConfigured", "unrecognised 403", "calendar-policy.json" },
        ["CAL-25"] = new[] { "daily", "per-user", "fair", "shed", "calendar-policy.json" },
        ["CAL-26"] = new[] { "hard-delete", "syllabus", "course", "account", "content-free", "event id", "generation", "remain in Google" },
    };

    [Fact]
    public void Every_security_rule_row_exists_carries_its_required_phrases_and_names_its_test_status()
    {
        var doc = DocText();
        var rows = Regex.Matches(doc, @"^\| (CAL-\d{2}) \|(.*)$", RegexOptions.Multiline).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.Equal(RulePhrases.Keys.Order().ToArray(), rows.Keys.Order().ToArray());
        foreach (var (id, phrases) in RulePhrases)
        {
            foreach (var ph in phrases) Assert.True(rows[id].Contains(ph, StringComparison.OrdinalIgnoreCase), $"{id} row must contain \"{ph}\"");
            Assert.True(rows[id].Contains("Contract") || rows[id].Contains("Required"), $"{id} must state Contract or Required test status");
        }
    }

    [Fact]
    public void Doc_states_the_remaining_normative_behaviour_under_its_own_headings()
    {
        var doc = DocText();
        string[] headings =
        {
            "## 5.1 Start and callback", "## 5.3 Closed", "## 5.4 Disconnect", "### 9.1 Connection", "### 9.2 Run", "### 9.3 Entry", "## 10. Idempotency",
            "## 11. What is stored", "## 13. Google call behaviour", "## 15. Deletion and export semantics", "## 16. Security requirements", "## 18. Judgement calls",
            "## 19. Conflicts between inputs", "## 20. Needs security-engineer review before freeze", "## 21. Open questions", "## 24. Review disposition",
        };
        foreach (var h in headings) Assert.Contains(h, doc);
        Assert.DoesNotContain("production-ready", doc, StringComparison.OrdinalIgnoreCase);
        foreach (var d in new[] { "PO-9o", "PO-10a", "PO-10b", "PO-10c", "PO-10d", "PO-10e", "PO-10f", "PO-10g", "PO-10h", "PO-10i", "PO-10j", "PO-10m", "PO-10n", "PO-10o", "PO-10q" })
            Assert.Contains(d, doc);
        for (var i = 1; i <= 28; i++) Assert.Contains($"CV-{i:00}", doc);
        for (var i = 1; i <= 13; i++) Assert.Contains($"S{i}", doc);
    }

    // ---- executable rules from the review ---------------------------------------------------------------------------------------------

    private static JsonNode Policy() => JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "calendar-policy.json")))!;

    [Fact]
    public void Policy_error_map_deletes_the_token_only_on_a_confirmed_invalid_grant_CAL_24()
    {
        var map = Policy()["googleErrorMap"]!.AsArray();
        var signals = map.Select(m => m!["signal"]!.GetValue<string>() + "|" + m["where"]!.GetValue<string>()).ToList();
        Assert.Equal(signals.Count, signals.Distinct().Count());
        var deleting = map.Where(m => m!["deletesToken"]!.GetValue<bool>()).ToList();
        var d = Assert.Single(deleting);
        Assert.Equal("invalid_grant", d!["signal"]!.GetValue<string>());
        Assert.True(d["confirmedByRetry"]!.GetValue<bool>());
        foreach (var must in new[] { "invalid_client", "unauthorized_client", "accessNotConfigured", "SERVICE_DISABLED", "quotaExceeded", "unknown_403", "decrypt_failure", "http_5xx", "admin_policy_enforced" })
        {
            var e = map.Single(m => m!["signal"]!.GetValue<string>() == must);
            Assert.False(e!["deletesToken"]!.GetValue<bool>(), $"{must} must never delete a token");
        }
        // admin_blocked is not derivable from a refresh-time signal until test T9 has run (CV-22).
        Assert.DoesNotContain(map, m => m!["connection"]?.GetValue<string>() == "admin_blocked" && m["where"]!.GetValue<string>() != "consent_error");
        foreach (var m in map) Assert.False(string.IsNullOrEmpty(m!["outcome"]!.GetValue<string>()));
        var cb = Policy()["circuitBreaker"]!;
        Assert.Equal("stop_deleting_and_alert", cb["action"]!.GetValue<string>());
        Assert.True(cb["windowMinutes"]!.GetValue<int>() > 0 && cb["minConnections"]!.GetValue<int>() > 1);
        var doc = DocText();
        foreach (var m in map) Assert.Contains($"`{m!["signal"]!.GetValue<string>()}`", doc);
    }

    [Fact]
    public void Policy_limits_have_a_per_user_daily_budget_fair_share_and_no_global_outage_CAL_25_and_CV_13()
    {
        var l = Policy()["limits"]!;
        Assert.StartsWith("PROPOSAL", l["status"]!.GetValue<string>());                       // PO-10q: numbers pending devops/PO
        Assert.True(l["googleWritesPerUserPerDay"]!.GetValue<int>() is > 0 and < 100000);
        Assert.True(l["runsPerUserPerDay"]!.GetValue<int>() > 0);
        Assert.Equal("per_user_weighted_round_robin", l["egressFairness"]!.GetValue<string>());
        Assert.Equal("shed_heaviest_users_first", l["globalStop"]!["behaviour"]!.GetValue<string>());
        Assert.False(l["globalStop"]!["disablesCalendarForEveryone"]!.GetValue<bool>());
        Assert.True(l["globalStop"]!["alertAtPercentOfBillingThreshold"]!.GetValue<int>() <= 50);
        var a = l["auth"]!;                                                                          // CV-13: a lecture hall behind one NAT must still sign in
        Assert.True(a["startPerIpPerMinute"]!.GetValue<int>() >= 300);
        Assert.True(a["startPerBinderCookiePerMinute"]!.GetValue<int>() > 0);
        Assert.True(a["maxUnconsumedStatesPerIpOrCookie"]!.GetValue<int>() <= 20);
        Assert.True(a["rejectBeforeAnyDatabaseWrite"]!.GetValue<bool>());
        Assert.Equal(3, l["googleWritesPerSecondPerCalendar"]!.GetValue<int>());
    }

    [Fact]
    public void Policy_retention_follows_PO_10h_and_the_content_free_record_holds_only_event_id_and_generation_CAL_26()
    {
        var r = Policy()["retention"]!;
        Assert.Equal(20, r["syncRunsPerSyllabus"]!.GetValue<int>());
        Assert.Equal(400, r["auditDays"]!.GetValue<int>());
        var ledger = Policy()["contentFreeEventRecord"]!;
        Assert.Equal(new[] { "calendarTargetId", "eventId", "generation", "lastUsedAt" }.Order().ToArray(),
            ledger["fields"]!.AsArray().Select(f => f!.GetValue<string>()).Order().ToArray());
        Assert.InRange(ledger["upperLimitDays"]!.GetValue<int>(), 1, 400);
        var deletedOn = ledger["deletedOn"]!.AsArray().Select(f => f!.GetValue<string>()).ToHashSet();
        Assert.Contains("disconnect", deletedOn);
        Assert.Contains("account_deletion", deletedOn);
        Assert.Contains("calendar_target_deletion", deletedOn);
        foreach (var banned in new[] { "title", "date", "course", "itemKey", "syllabusId", "itemId", "fingerprint", "etag" })
            Assert.DoesNotContain(ledger["fields"]!.AsArray().Select(f => f!.GetValue<string>()), f => f.Contains(banned, StringComparison.OrdinalIgnoreCase));
        var hard = Policy()["hardDeleteOn"]!.AsArray().Select(f => f!.GetValue<string>()).ToHashSet();
        foreach (var e in new[] { "syllabus_delete", "course_delete", "course_archive", "account_deletion", "disconnect_keep" }) Assert.Contains(e, hard);
        Assert.Equal(24, Policy()["disconnecting"]!["deadlineHours"]!.GetValue<int>());
        Assert.True(Policy()["accountDeletion"]!["blocksOnGoogle"]!.GetValue<bool>() == false);
        Assert.InRange(Policy()["accountDeletion"]!["googleAttemptSeconds"]!.GetValue<int>(), 1, 600);
    }

    [Fact]
    public void Policy_event_body_is_a_positive_allowlist_and_dialogs_must_say_events_remain_in_Google()
    {
        var p = Policy();
        Assert.Equal(new[] { "description", "end", "extendedProperties.private", "id", "reminders", "start", "status", "summary" },
            p["eventBodyAllowlist"]!.AsArray().Select(x => x!.GetValue<string>()).Order().ToArray());
        var forbidden = p["eventBodyForbidden"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
        foreach (var f in new[] { "attendees", "conferenceData", "attachments", "source", "guestsCanModify", "location" }) Assert.Contains(f, forbidden);
        Assert.Equal("none", p["sendUpdates"]!.GetValue<string>());
        Assert.Equal("no_query_no_ids", p["descriptionLink"]!.GetValue<string>());
        var statements = p["dialogsMustState"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains("events_remain_in_google", statements);
        Assert.Contains("also_remove_from_google_choice", statements);
        Assert.Contains("next_sign_in_asks_consent_again", statements);
        var doc = DocText();
        Assert.Contains("events remain in Google", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_digest_policy_names_key_derivation_canonical_form_and_every_binding()
    {
        var d = Policy()["previewDigest"]!;
        Assert.Equal("calendar-preview-digest.v1", d["hkdfLabel"]!.GetValue<string>());
        Assert.True(d["keyRing"]!.GetValue<bool>());
        Assert.Equal("length_prefixed_utf8", d["canonicalForm"]!.GetValue<string>());
        Assert.Equal("CryptographicOperations.FixedTimeEquals", d["compare"]!.GetValue<string>());
        Assert.False(d["stored"]!.GetValue<bool>());
        Assert.Equal("consistency_check_not_authorisation", d["role"]!.GetValue<string>());
        var binds = d["binds"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
        foreach (var b in new[] { "version", "userId", "kind", "syllabusId", "connectionId", "remoteCalendarId", "destination", "preset", "reminderTime", "pause",
                     "courseOverrides", "entries", "generation", "reminders", "exclusions", "leftAlone", "skippedReminders", "counts" })
            Assert.Contains(b, binds);
    }

    [Fact]
    public void Every_state_changing_request_that_touches_events_is_bound_to_a_plan_digest_CV_08()
    {
        foreach (var n in new[] { "calendar-run-request", "calendar-removal-request", "calendar-reminder-update-request", "calendar-resume-request" })
        {
            var s = Schema(n);
            Assert.Contains("previewDigest", s["required"]!.AsArray().Select(x => x!.GetValue<string>()));
            Assert.Equal("calendar-common.schema.json#/$defs/sha256Hex", s["properties"]!["previewDigest"]!["$ref"]!.GetValue<string>());
        }
        Assert.Contains("entryCount", Schema("calendar-google-disconnect-request")["required"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("previewDigest", Schema("calendar-google-disconnect-request")["required"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("noticeVersion", Schema("calendar-google-connect-start-request")["required"]!.AsArray().Select(x => x!.GetValue<string>()));   // CV-11
        foreach (var (file, node) in FixtureNodes("valid", "calendar-run"))
            Assert.Matches("^[0-9a-f]{64}$", node["previewDigest"]!.GetValue<string>());                                  // every kind carries its digest
    }

    [Fact]
    public void Disconnect_remove_events_needs_count_and_digest_and_keep_has_neither_and_detached_no_longer_exists()
    {
        foreach (var (file, node) in FixtureNodes("valid", "calendar-google-disconnect-request"))
        {
            if (node["eventsAction"]!.GetValue<string>() == "remove_events")
            {
                Assert.NotNull(node["entryCount"]);
                Assert.NotNull(node["previewDigest"]);
            }
            else
            {
                Assert.Null(node["entryCount"]);
                Assert.Null(node["previewDigest"]);
            }
        }
        var states = Defs()["entryState"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.DoesNotContain("detached", states);               // PO-9o option C: content is deleted, not kept in a detached row
        Assert.Equal(new[] { "deleted_in_google", "edited_in_google", "removal_pending", "retry_later", "scheduled" }, states.Order().ToArray());
        Assert.DoesNotContain("detached", Schema("calendar-google-connection").ToJsonString());
    }

    [Fact]
    public void Connection_resource_reports_unconfirmed_revocation_and_how_many_events_remain_in_Google()
    {
        var props = Schema("calendar-google-connection")["properties"]!.AsObject().Select(k => k.Key).ToList();
        Assert.Contains("revocation", props);
        Assert.Contains("eventsInGoogle", Schema("calendar-settings")["properties"]!.AsObject().Select(k => k.Key));   // CV-20
        var seen = FixtureNodes("valid", "calendar-google-connection").Select(f => f.Node["revocation"]?.GetValue<string>()).ToHashSet();
        Assert.Contains("unconfirmed", seen);
        Assert.Contains("confirmed", seen);
    }

    [Fact]
    public void Titles_have_no_type_word_prefix_and_fit_the_title_policy_PO_10b_CV_17()
    {
        foreach (var (file, node) in FixtureNodes("valid"))
            foreach (var e in EventLike(node))
            {
                var title = e["title"]!.GetValue<string>();
                var parts = title.Split(" · ", 2);
                Assert.Equal(2, parts.Length);
                Assert.InRange(parts[0].Length, 1, 40);
                Assert.True(title.EnumerateRunes().Count() <= 200);
                Assert.DoesNotMatch(@"^(Exam|Due|Quiz|Reading):", parts[1]);
            }
    }

    [Fact]
    public void Add_remove_add_bumps_the_generation_using_the_content_free_record_CV_09()
    {
        const string cal = "cal_fake_0001@group.calendar.google.com";
        const string key = "ik_zmzduAT9Xtu-VKSA5d29Tq_CIpIywcbD";
        var ledger = new HashSet<string>();
        int NextGeneration()
        {
            var g = 1;
            while (ledger.Contains(GoogleEventId(cal, key, "2026-11-12", g))) g++;
            return g;
        }
        var g1 = NextGeneration();
        Assert.Equal(1, g1);
        ledger.Add(GoogleEventId(cal, key, "2026-11-12", g1));          // add; the row is later hard-deleted but the content-free record stays
        var g2 = NextGeneration();
        Assert.Equal(2, g2);
        ledger.Add(GoogleEventId(cal, key, "2026-11-12", g2));
        Assert.Equal(3, NextGeneration());
        ledger.Clear();                                                  // disconnect or account deletion removes the record
        Assert.Equal(1, NextGeneration());
    }

    [Fact]
    public void Action_previews_cover_removal_reminders_disconnect_and_resume_with_a_digest()
    {
        var kinds = new HashSet<string>();
        foreach (var (file, node) in FixtureNodes("valid", "calendar-action-preview"))
        {
            kinds.Add(node["kind"]!.GetValue<string>());
            Assert.Matches("^[0-9a-f]{64}$", node["previewDigest"]!.GetValue<string>());
            Assert.Equal(node["entryCount"]!.GetValue<int>(), node["entries"]!.AsArray().Count(e => e!["willChange"]!.GetValue<bool>()));
        }
        Assert.Equal(new[] { "disconnect", "reminders", "removal", "resume" }, kinds.Order().ToArray());
    }
}
