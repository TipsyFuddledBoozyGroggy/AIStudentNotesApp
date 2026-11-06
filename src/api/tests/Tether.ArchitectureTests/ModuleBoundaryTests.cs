using System.Reflection;
using Tether.Courses;
using Tether.Courses.Contracts;
using Tether.Identity;
using Tether.Lectures;
using Tether.Lectures.Contracts;
using Tether.Transcript;
using Tether.Ai;
using Tether.Ai.Contracts;
using Tether.Docs;
using Tether.Docs.Contracts;
using Tether.Syllabus;
using Tether.SharedKernel;

namespace Tether.ArchitectureTests;

/// <summary>
/// Enforces the module rules from docs/architecture/backend-foundation.md section 2:
/// a module exposes only its Contracts; Domain/Application/Infrastructure stay internal;
/// no module references another module's implementation assembly.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly Identity = typeof(IdentityModule).Assembly;
    private static readonly Assembly Courses = typeof(CoursesModule).Assembly;
    private static readonly Assembly CoursesContracts = typeof(ICourseOwnership).Assembly;
    private static readonly Assembly Lectures = typeof(LecturesModule).Assembly;
    private static readonly Assembly LecturesContracts = typeof(IAudioTicketConsumer).Assembly;
    private static readonly Assembly SharedKernel = typeof(ICurrentUser).Assembly;

    private static readonly Assembly Transcript = typeof(TranscriptModule).Assembly;
    private static readonly Assembly Ai = typeof(AiModule).Assembly;
    private static readonly Assembly Syllabus = typeof(SyllabusModule).Assembly;
    private static readonly Assembly Docs = typeof(DocsModule).Assembly;
    private static readonly Assembly DocsContracts = typeof(IDocsProvider).Assembly;
    private static readonly Assembly AiContracts = typeof(INotesSnapshotReader).Assembly;

    private static readonly string[] ModuleImplementations = ["Tether.Identity", "Tether.Courses", "Tether.Lectures", "Tether.Transcript", "Tether.Ai", "Tether.Syllabus", "Tether.Docs"];

    private static IEnumerable<string> ProjectRefs(Assembly a) =>
        a.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("Tether.", StringComparison.Ordinal));

    [Fact]
    public void Module_implementations_do_not_reference_other_module_implementations()
    {
        foreach (var asm in new[] { Identity, Courses, Lectures, Transcript, Ai, Syllabus, Docs })
        {
            var self = asm.GetName().Name!;
            var illegal = ProjectRefs(asm).Where(n => ModuleImplementations.Contains(n) && n != self).ToList();
            Assert.True(illegal.Count == 0, $"{self} references other module implementations: {string.Join(", ", illegal)}");
        }
    }

    [Fact]
    public void Module_implementations_only_reference_shared_kernel_and_contracts_from_this_solution()
    {
        var allowed = new Dictionary<string, string[]>
        {
            ["Tether.Identity"] = ["Tether.SharedKernel"],
            ["Tether.Courses"] = ["Tether.SharedKernel", "Tether.Courses.Contracts"],
            ["Tether.Lectures"] = ["Tether.SharedKernel", "Tether.Courses.Contracts", "Tether.Lectures.Contracts"],
            ["Tether.Transcript"] = ["Tether.SharedKernel", "Tether.Lectures.Contracts", "Tether.Transcript.Contracts"],
            ["Tether.Ai"] = ["Tether.SharedKernel", "Tether.Lectures.Contracts", "Tether.Transcript.Contracts", "Tether.Ai.Contracts"], // item 3: implements INotesSnapshotReader (DXD-08)
            // P2-B1: Syllabus needs only course ownership (ICourseOwnership). The AI document seam, when it arrives, comes through an Ai contract.
            ["Tether.Syllabus"] = ["Tether.SharedKernel", "Tether.Courses.Contracts"],
            // item 3 (DXD-08): Docs reads the lecture facts, the read-only notes snapshot and its own contract. NOT Courses.Contracts (the course name stays out of the title) and never the AI implementation.
            ["Tether.Docs"] = ["Tether.SharedKernel", "Tether.Lectures.Contracts", "Tether.Ai.Contracts", "Tether.Docs.Contracts"],
        };
        foreach (var asm in new[] { Identity, Courses, Lectures, Transcript, Ai, Syllabus, Docs })
        {
            var self = asm.GetName().Name!;
            var extra = ProjectRefs(asm).Except(allowed[self]).ToList();
            Assert.True(extra.Count == 0, $"{self} has unexpected solution references: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void Contracts_and_shared_kernel_do_not_depend_on_any_module_implementation_or_host()
    {
        foreach (var asm in new[] { CoursesContracts, LecturesContracts, SharedKernel, AiContracts, DocsContracts })
        {
            var bad = ProjectRefs(asm).Where(n => ModuleImplementations.Contains(n) || n.EndsWith(".Host")).ToList();
            Assert.True(bad.Count == 0, $"{asm.GetName().Name} references: {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void Module_implementations_expose_only_their_module_registration_class_publicly()
    {
        AssertPublicSurface(Identity, "IdentityModule");
        AssertPublicSurface(Courses, "CoursesModule");
        AssertPublicSurface(Lectures, "LecturesModule");
        AssertPublicSurface(Transcript, "TranscriptModule");
        AssertPublicSurface(Syllabus, "SyllabusModule");
        AssertPublicSurface(Docs, "DocsModule");
        // DXD-08: the two new contract assemblies are pinned to exactly these types (the snapshot record family, and the provider port with its value types)
        AssertPublicSurface(AiContracts, "INotesSnapshotReader", "NotesSnapshot", "NotesSnapshotSection", "NotesSnapshotBullet", "FinalSummarySnapshot");
        AssertPublicSurface(DocsContracts, "IDocsProvider", "DocsWriteRequest", "DocumentModel", "DocBlock", "DocRef", "ProviderFailure", "ProviderResult", "ProviderResult`1");
        AssertPublicSurface(LecturesContracts, "IAudioTicketConsumer", "LectureState", "SessionInfo", "SessionStateRecord", "SessionStateChange",
            "ISessionDirectory", "ISessionEventReader", "ISessionSequencer", "ISessionLifecycleObserver", "ISessionActivity", "IDemoSourceIndicator",
            "ISessionAuditLog", // PO-18d: content-free audit of the notes press, written by the AI module
            "IDocsSessionSource", "DocsSessionFacts"); // item 3 (DXD-17): the Docs-only port; SessionInfo is NOT changed
    }

    private static void AssertPublicSurface(Assembly asm, params string[] allowed)
    {
        var leaked = asm.GetTypes()
            .Where(t => t.IsVisible && !t.Name.StartsWith('<'))
            .Select(t => t.FullName!)
            .Where(n => !allowed.Any(a => n.EndsWith("." + a, StringComparison.Ordinal)))
            .ToList();
        Assert.True(leaked.Count == 0, $"{asm.GetName().Name} leaks public types: {string.Join(", ", leaked)}");
    }

    [Fact]
    public void Only_the_host_references_module_implementations()
    {
        // Host is the composition root; it is allowed. Contracts must stay free of them (tested above).
        var hostAsm = typeof(Program).Assembly;
        var refs = ProjectRefs(hostAsm).ToList();
        Assert.Contains("Tether.Identity", refs);
        Assert.Contains("Tether.Courses", refs);
        Assert.Contains("Tether.Lectures", refs);
        Assert.Contains("Tether.Transcript", refs);
        Assert.Contains("Tether.Syllabus", refs);
        Assert.Contains("Tether.Docs", refs);
    }

    [Fact]
    public void Query_filters_are_never_bypassed_in_source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            .ToList();
        Assert.True(offenders.Count == 0, "IgnoreQueryFilters used in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_raw_sql_concatenation_in_source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("FromSqlRaw") || t.Contains("ExecuteSqlRaw"); })
            .ToList();
        Assert.True(offenders.Count == 0, "Raw SQL APIs used in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Migrations_never_try_to_create_the_xmin_system_column()
    {
        // EF scaffolds `xmin = table.Column<uint>(type: "xid", rowVersion: true)` for IsRowVersion(); PostgreSQL rejects
        // it because xmin is a system column. See src/api/README.md "Re-scaffolding migrations".
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal)
                        && !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("type: \"xid\"", StringComparison.Ordinal))
            .ToList();
        Assert.True(offenders.Count == 0, "Migrations create an xmin column: " + string.Join(", ", offenders));
    }
}

/// <summary>
/// N-09: ExecuteUpdate/ExecuteDelete bypass the SaveChanges ownership interceptor. They are banned in source except on
/// identity.sessions (keyed by the authenticated sid; Session is not IUserOwned).
/// </summary>
public class BulkOperationBanTests
{
    private static readonly System.Text.RegularExpressions.Regex Bulk =
        new(@"\.Execute(Update|Delete)(Async)?\s*\(", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    /// <summary>Returns offending "file:line" entries; a use is allowed only if its statement starts at db.Sessions.</summary>
    private static List<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        files.SelectMany(f => Bulk.Matches(f.Text).Select(m => (f.Path, f.Text, m)))
            .Where(x =>
            {
                var stmtStart = Math.Max(0, x.Text.LastIndexOf(';', x.m.Index) + 1);
                var stmt = x.Text[stmtStart..x.m.Index];
                return !System.Text.RegularExpressions.Regex.IsMatch(stmt, @"\bdb\.Sessions\b");
            })
            .Select(x => $"{x.Path}:{x.Text[..x.m.Index].Count(c => c == '\n') + 1}")
            .ToList();

    [Fact]
    public void ExecuteUpdate_and_ExecuteDelete_are_only_used_on_identity_sessions()
    {
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}Migrations{sep}"))
            .Select(f => (f, File.ReadAllText(f)));
        var offenders = Offenders(files);
        Assert.True(offenders.Count == 0, "Bulk ExecuteUpdate/ExecuteDelete outside identity.sessions: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_ban_detects_a_bulk_call_on_an_owned_set_and_allows_sessions()
    {
        Assert.Single(Offenders([("x.cs", "await db.Courses.Where(c => true).ExecuteDeleteAsync(ct);")]));
        Assert.Empty(Offenders([("x.cs", "await db.Sessions.Where(s => s.Id == sid)\n  .ExecuteUpdateAsync(u => u, ct);")]));
    }
}

/// <summary>
/// Ownership rule (docs/data/schema-m1-m2.md section 6): every persisted entity in a user-data module is IUserOwned, so the
/// ownership interceptor and the per-entity query filter apply to it. Identity is exempt (users and login sessions are
/// keyed by the authenticated principal, not by an owner column).
/// </summary>
public class OwnershipConventionTests
{
    [Fact]
    public void Every_entity_in_Courses_and_Lectures_domain_namespaces_is_user_owned()
    {
        foreach (var asm in new[] { typeof(CoursesModule).Assembly, typeof(LecturesModule).Assembly, typeof(TranscriptModule).Assembly, typeof(SyllabusModule).Assembly, typeof(DocsModule).Assembly })
        {
            var entities = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested && !t.Name.StartsWith('<')
                            && t.Namespace is { } ns && ns.EndsWith(".Domain", StringComparison.Ordinal)
                            && !(t.IsAbstract && t.IsSealed)) // static helper classes
                .ToList();
            Assert.NotEmpty(entities);
            var notOwned = entities.Where(t => !typeof(IUserOwned).IsAssignableFrom(t)).Select(t => t.FullName).ToList();
            Assert.True(notOwned.Count == 0, $"{asm.GetName().Name} has non-IUserOwned domain classes: {string.Join(", ", notOwned)}");
        }
    }

    [Fact]
    public void Lectures_never_calls_into_other_modules_except_through_contracts()
    {
        var refs = typeof(LecturesModule).Assembly.GetReferencedAssemblies().Select(r => r.Name);
        Assert.DoesNotContain("Tether.Courses", refs);
        Assert.DoesNotContain("Tether.Identity", refs);
    }
}

/// <summary>B2: Transcript depends on Lectures only through its Contracts, never on Courses or Identity, and never on the Host.</summary>
public class TranscriptBoundaryTests
{
    [Fact]
    public void Transcript_never_references_Lectures_implementation_Courses_Identity_or_the_host()
    {
        var refs = typeof(TranscriptModule).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToList();
        foreach (var banned in new[] { "Tether.Lectures", "Tether.Courses", "Tether.Courses.Contracts", "Tether.Identity", "Tether.Host" })
            Assert.DoesNotContain(banned, refs);
        Assert.Contains("Tether.Lectures.Contracts", refs);
    }

    [Fact]
    public void No_speech_vendor_code_or_keys_exist_in_the_source_tree()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        var sep = Path.DirectorySeparatorChar;
        var vendors = new[] { "deepgram", "assemblyai", "speech.microsoft", "cognitiveservices", "api.openai", "amazonaws", "transcribestreaming", "speechmatics" };
        var hits = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => { var t = File.ReadAllText(f); return vendors.Any(v => t.Contains(v, StringComparison.OrdinalIgnoreCase)); })
            .ToList();
        Assert.True(hits.Count == 0, "Vendor references found (M2 is mock-backed only): " + string.Join(", ", hits));
    }

    [Fact]
    public void Raw_audio_is_never_written_to_disk_by_the_relay()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        var relay = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src", "Modules", "Transcript"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));
        foreach (var f in relay)
        {
            var t = File.ReadAllText(f);
            Assert.DoesNotContain("File.Write", t);
            Assert.DoesNotContain("FileStream", t);
            Assert.DoesNotContain("Path.GetTempFileName", t);
        }
    }
}

/// <summary>
/// P2-B1 Syllabus module rules (docs/API_CONTRACTS_P2.md sections 6.3, 8 and 15): mock only, no vendor or Google code, no AI module, and no
/// logging of anything but codes, counts and exception type names.
/// </summary>
public class SyllabusBoundaryTests
{
    private static IEnumerable<string> SyllabusSources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tether.slnx"))) dir = dir.Parent;
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src", "Modules", "Syllabus"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}Migrations{sep}"));
    }

    [Fact]
    public void Syllabus_does_not_reference_Ai_Identity_Lectures_Transcript_or_Google()
    {
        var refs = typeof(SyllabusModule).Assembly.GetReferencedAssemblies().Select(r => r.Name!).ToList();
        foreach (var banned in new[] { "Tether.Ai", "Tether.Identity", "Tether.Lectures", "Tether.Transcript", "Tether.Host" })
            Assert.DoesNotContain(banned, refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Google", StringComparison.OrdinalIgnoreCase) || r.StartsWith("Anthropic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Syllabus_source_has_no_http_client_google_or_vendor_code()
    {
        var banned = new[] { "HttpClient", "IHttpClientFactory", "googleapis", "calendar.google", "api.anthropic", "api.openai", "WebRequest" };
        var hits = SyllabusSources().Where(f => { var t = File.ReadAllText(f); return banned.Any(b => t.Contains(b, StringComparison.OrdinalIgnoreCase)); }).ToList();
        Assert.True(hits.Count == 0, "Syllabus must be mock only (no outbound network): " + string.Join(", ", hits));
    }

    [Fact]
    public void Syllabus_logging_never_passes_content()
    {
        // Every log call may name only fixed codes, counts and exception type names; never a title, text, snippet, name or message.
        var call = new System.Text.RegularExpressions.Regex(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)\(([^;]*)\);", System.Text.RegularExpressions.RegexOptions.Singleline);
        var forbidden = new[] { "Title", ".Text", "Snippet", "Context", "Notes", "FileName", "Filename", ".Message", "ToString()", "Body" };
        var offenders = new List<string>();
        foreach (var f in SyllabusSources())
            foreach (System.Text.RegularExpressions.Match m in call.Matches(File.ReadAllText(f)))
                if (forbidden.Any(x => m.Groups[2].Value.Contains(x, StringComparison.Ordinal))) offenders.Add(f + ": " + m.Value);
        Assert.True(offenders.Count == 0, "Content in a log call: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Syllabus_registers_no_calendar_provider_other_than_the_mock()
    {
        var impls = typeof(SyllabusModule).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => i.Name == "ICalendarProvider")).Select(t => t.Name).ToList();
        Assert.Equal(["MockCalendar"], impls);
    }
}
