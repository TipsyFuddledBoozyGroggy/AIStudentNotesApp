using System.Reflection;
using System.Text.RegularExpressions;
using Tether.Ai;
using Tether.Ai.Contracts;
using Tether.Docs;
using Tether.Docs.Contracts;
using Tether.Lectures.Contracts;

namespace Tether.ArchitectureTests;

/// <summary>
/// Item 3 (docs.export.v1, mock only): the Docs module's boundary rules (DXD-08, DXD-09, DXD-14, DXD-17; tasks B01, B02a, B02b, B05a, B31). Docs sees the
/// lecture facts and a read-only notes snapshot, never the AI implementation, Identity, Courses or the host; it has no HTTP client, no Google code and no
/// token type anywhere, and the AI module has no path to it. The allow-lists and the pinned public surfaces are in ModuleBoundaryTests.
/// </summary>
public class DocsBoundaryTests
{
    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    private static string RepoSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    private static List<string> Sources(string moduleFolder) =>
        Directory.EnumerateFiles(Path.Combine(RepoSrc(), "Modules", moduleFolder), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Sep}obj{Sep}") && !f.Contains($"{Sep}bin{Sep}") && !f.Contains($"{Sep}Migrations{Sep}")).ToList();

    /// <summary>Code without comments: the comments are allowed to explain what the module does NOT do (no token, no OAuth).</summary>
    private static string Code(string file) => Regex.Replace(File.ReadAllText(file), @"//.*", "");

    private static IEnumerable<string> RefsOf(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!);

    [Fact]
    public void B01_Docs_references_only_SharedKernel_Lectures_Contracts_Ai_Contracts_and_Docs_Contracts_and_no_http_or_google_assembly()
    {
        var docs = typeof(DocsModule).Assembly;
        var mine = RefsOf(docs).Where(n => n.StartsWith("Tether.", StringComparison.Ordinal)).Order().ToList();
        Assert.Equal(new[] { "Tether.Ai.Contracts", "Tether.Docs.Contracts", "Tether.Lectures.Contracts", "Tether.SharedKernel" }.Order().ToList(), mine);
        foreach (var banned in new[] { "Tether.Ai", "Tether.Courses", "Tether.Courses.Contracts", "Tether.Identity", "Tether.Host", "Tether.Transcript", "Tether.Lectures", "Tether.Syllabus" })
            Assert.DoesNotContain(banned, RefsOf(docs));
        Assert.DoesNotContain(RefsOf(docs), r => r.StartsWith("Google", StringComparison.OrdinalIgnoreCase) || r.StartsWith("Anthropic", StringComparison.OrdinalIgnoreCase)
            || r is "System.Net.Http" or "Microsoft.Extensions.Http" || r.StartsWith("Azure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void B01_Docs_sources_have_no_http_client_google_or_vendor_code_and_no_token_type()
    {
        var banned = new[] { "HttpClient", "IHttpClientFactory", "System.Net.Http", "Microsoft.Extensions.Http", "googleapis", "docs.google", "api.anthropic", "api.openai", "WebRequest", "OAuth", "RefreshToken", "AccessToken" };
        var files = Sources("Docs");
        Assert.True(files.Count >= 8, "the scan found too few files to mean anything");
        var hits = files.Where(f => { var t = Code(f); return banned.Any(b => t.Contains(b, StringComparison.OrdinalIgnoreCase)); }).ToList();
        Assert.True(hits.Count == 0, "Docs must be mock only (no outbound network, no tokens): " + string.Join(", ", hits));
    }

    [Fact]
    public void B01_Docs_Contracts_has_no_member_named_token_secret_credential_or_refresh()
    {
        var words = new[] { "token", "secret", "credential", "refresh" };
        var offenders = new List<string>();
        foreach (var t in typeof(IDocsProvider).Assembly.GetTypes())
        {
            if (words.Any(w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase))) offenders.Add(t.FullName!);
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (words.Any(w => m.Name.Contains(w, StringComparison.OrdinalIgnoreCase))) offenders.Add(t.FullName + "." + m.Name);
        }
        Assert.True(offenders.Count == 0, "token-like member in Docs.Contracts: " + string.Join(", ", offenders));
    }

    [Fact]
    public void B01_The_Ai_module_and_its_contracts_reference_no_Docs_or_Google_assembly_and_no_source_names_them()
    {
        foreach (var asm in new[] { typeof(AiModule).Assembly, typeof(INotesSnapshotReader).Assembly })
            Assert.DoesNotContain(RefsOf(asm), r => r.StartsWith("Tether.Docs", StringComparison.Ordinal) || r.StartsWith("Tether.Google", StringComparison.Ordinal));
        var hits = Sources("Ai").Where(f => { var t = File.ReadAllText(f); return t.Contains("Tether.Docs", StringComparison.Ordinal) || t.Contains("Tether.Google", StringComparison.Ordinal); }).ToList();
        Assert.True(hits.Count == 0, "the AI module names the Docs module: " + string.Join(", ", hits));
    }

    [Fact]
    public void B02a_The_notes_reader_takes_the_ambient_user_and_its_snapshot_has_no_segment_id_transcript_text_prompt_or_AI_type()
    {
        foreach (var m in typeof(INotesSnapshotReader).GetMethods())
        {
            Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(Guid) && !string.Equals(p.Name, "sessionId", StringComparison.Ordinal));
            Assert.DoesNotContain(m.GetParameters(), p => p.Name!.Contains("user", StringComparison.OrdinalIgnoreCase));
        }
        var banned = new[] { "segment", "transcript", "prompt", "model", "token", "client" };
        foreach (var t in typeof(INotesSnapshotReader).Assembly.GetTypes().Where(t => t.IsVisible))
            foreach (var p in t.GetProperties())
            {
                Assert.DoesNotContain(banned, w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
                var inner = p.PropertyType.IsGenericType ? p.PropertyType.GetGenericArguments()[0] : p.PropertyType;
                var ns = inner.Namespace ?? "";
                Assert.True(ns is "System" or "System.Collections.Generic" or "Tether.Ai.Contracts", $"{t.Name}.{p.Name} has a type from {ns}");
            }
    }

    [Fact]
    public void B02b_Ai_Transcript_Syllabus_and_Courses_never_name_the_Docs_only_session_port_and_SessionInfo_has_no_Title()
    {
        Assert.DoesNotContain(typeof(SessionInfo).GetProperties(), p => p.Name.Contains("Title", StringComparison.OrdinalIgnoreCase));
        foreach (var folder in new[] { "Ai", "Transcript", "Syllabus", "Courses" })
        {
            var hits = Sources(folder).Where(f => { var t = File.ReadAllText(f); return t.Contains("IDocsSessionSource", StringComparison.Ordinal) || t.Contains("DocsSessionFacts", StringComparison.Ordinal); }).ToList();
            Assert.True(hits.Count == 0, folder + " reads the Docs-only port: " + string.Join(", ", hits));
        }
    }

    // ---- B05a: every raw SQL statement that touches a table carries a user_id predicate (the EF filter and the ownership interceptor do not see raw SQL) ----

    private static readonly Regex SqlLiteral = new(
        "\"(?<first>(?:SELECT|INSERT|UPDATE|DELETE)\\b[^\"]*)\"(?<rest>(?:\\s*\\+\\s*\"[^\"]*\")*)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TouchesTable = new(@"\b(FROM|INTO|UPDATE|JOIN)\s+[a-z_]+\.[a-z_]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static List<string> SqlStatements(string source)
    {
        var list = new List<string>();
        foreach (Match m in SqlLiteral.Matches(source))
            list.Add(m.Groups["first"].Value + string.Concat(Regex.Matches(m.Groups["rest"].Value, "\"([^\"]*)\"").Select(x => x.Groups[1].Value)));
        return list;
    }

    internal static List<string> WithoutUserId(IEnumerable<string> statements) =>
        statements.Where(s => TouchesTable.IsMatch(s) && !s.Contains("user_id", StringComparison.Ordinal)).ToList();

    [Fact]
    public void B05a_Every_raw_SQL_statement_in_Docs_that_touches_a_table_has_a_user_id_predicate()
    {
        var all = Sources("Docs").SelectMany(f => SqlStatements(File.ReadAllText(f))).ToList();
        Assert.True(all.Count(s => TouchesTable.IsMatch(s)) >= 6, "the scan found too few statements to mean anything: " + all.Count);
        var bad = WithoutUserId(all);
        Assert.True(bad.Count == 0, "raw SQL without user_id: " + string.Join(" || ", bad));
    }

    [Fact]
    public void B05a_The_scan_detects_a_statement_without_user_id_and_accepts_one_with_it_and_the_advisory_lock_calls()
    {
        Assert.Single(WithoutUserId(SqlStatements("await sql.ExecAsync(\"DELETE FROM docs.demo_docs WHERE id = @p0\", ct, id);")));
        Assert.Single(WithoutUserId(SqlStatements("var s = \"SELECT id FROM docs.doc_exports \" + \"WHERE id = @p0\";")));
        Assert.Empty(WithoutUserId(SqlStatements("await sql.ExecAsync(\"DELETE FROM docs.demo_docs WHERE id = @p0 AND user_id = @p1\", ct, id, u);")));
        Assert.Empty(WithoutUserId(SqlStatements("\"SELECT pg_advisory_xact_lock(hashtextextended(@p0, 0))\"")));
    }

    [Fact]
    public void B31_B05_Docs_never_emits_export_in_progress_or_a_pending_status_and_uses_no_bulk_calls_or_filter_bypass()
    {
        foreach (var f in Sources("Docs"))
        {
            var t = File.ReadAllText(f);
            Assert.DoesNotContain("export_in_progress", t);
            Assert.DoesNotContain("\"pending\"", t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ExecuteUpdate", t);
            Assert.DoesNotContain("ExecuteDelete", t);
            Assert.DoesNotContain("IgnoreQueryFilters", t);
        }
    }

    [Fact]
    public void Docs_logging_never_passes_content()
    {
        // Every log call may name only fixed codes, counts and exception type names; never a title, document text, digest, name or message.
        var call = new Regex(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)\(([^;]*)\);", RegexOptions.Singleline);
        var forbidden = new[] { "Title", ".Text", "Digest", "Heading", "Bullet", "Notes", "Body", ".Message", "ToString()", "Key" };
        var offenders = new List<string>();
        var seen = 0;
        foreach (var f in Sources("Docs"))
            foreach (Match m in call.Matches(File.ReadAllText(f)))
            {
                seen++;
                // a document's block COUNT is fine; the blocks themselves are not
                var args = m.Groups[2].Value.Replace("Blocks.Count", "BlockCount");
                if (forbidden.Any(x => args.Contains(x, StringComparison.Ordinal)) || args.Contains("Blocks", StringComparison.Ordinal)) offenders.Add(f + ": " + m.Value);
            }
        Assert.True(seen >= 3, "the scan found too few log calls to mean anything");
        Assert.True(offenders.Count == 0, "Content in a log call: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Docs_registers_no_provider_other_than_the_demo_one()
    {
        var impls = typeof(DocsModule).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => i.Name == "IDocsProvider")).Select(t => t.Name).ToList();
        Assert.Equal(["DemoDocsProvider"], impls);
    }
}
