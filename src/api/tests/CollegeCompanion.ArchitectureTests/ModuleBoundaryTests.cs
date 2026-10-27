using System.Reflection;
using CollegeCompanion.Courses;
using CollegeCompanion.Courses.Contracts;
using CollegeCompanion.Identity;
using CollegeCompanion.SharedKernel;

namespace CollegeCompanion.ArchitectureTests;

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
    private static readonly Assembly SharedKernel = typeof(ICurrentUser).Assembly;

    private static readonly string[] ModuleImplementations = ["CollegeCompanion.Identity", "CollegeCompanion.Courses"];

    private static IEnumerable<string> ProjectRefs(Assembly a) =>
        a.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("CollegeCompanion.", StringComparison.Ordinal));

    [Fact]
    public void Module_implementations_do_not_reference_other_module_implementations()
    {
        foreach (var asm in new[] { Identity, Courses })
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
            ["CollegeCompanion.Identity"] = ["CollegeCompanion.SharedKernel"],
            ["CollegeCompanion.Courses"] = ["CollegeCompanion.SharedKernel", "CollegeCompanion.Courses.Contracts"],
        };
        foreach (var asm in new[] { Identity, Courses })
        {
            var self = asm.GetName().Name!;
            var extra = ProjectRefs(asm).Except(allowed[self]).ToList();
            Assert.True(extra.Count == 0, $"{self} has unexpected solution references: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void Contracts_and_shared_kernel_do_not_depend_on_any_module_implementation_or_host()
    {
        foreach (var asm in new[] { CoursesContracts, SharedKernel })
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
        Assert.Contains("CollegeCompanion.Identity", refs);
        Assert.Contains("CollegeCompanion.Courses", refs);
    }

    [Fact]
    public void Query_filters_are_never_bypassed_in_source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CollegeCompanion.slnx"))) dir = dir.Parent;
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
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CollegeCompanion.slnx"))) dir = dir.Parent;
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("FromSqlRaw") || t.Contains("ExecuteSqlRaw"); })
            .ToList();
        Assert.True(offenders.Count == 0, "Raw SQL APIs used in: " + string.Join(", ", offenders));
    }
}
