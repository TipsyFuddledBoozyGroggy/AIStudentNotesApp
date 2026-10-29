namespace Tether.ArchitectureTests;

/// <summary>B1-02: EF Core sensitive-data logging and detailed errors would put parameter values (student text) into logs and exceptions.</summary>
public class SensitiveLoggingGuardTests
{
    private static string SrcRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Tether.Host"))) return Path.Combine(d.FullName, "src");
        throw new InvalidOperationException("src/api/src not found from " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("EnableSensitiveDataLogging")]
    [InlineData("EnableDetailedErrors")]
    public void Production_code_never_enables(string call)
    {
        var hits = Directory.EnumerateFiles(SrcRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(call, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(hits);
    }

    [Fact]
    public void Scanner_finds_the_host_so_the_guard_cannot_pass_vacuously()
    {
        Assert.True(File.Exists(Path.Combine(SrcRoot(), "Tether.Host", "Program.cs")));
        Assert.True(Directory.EnumerateFiles(SrcRoot(), "*.cs", SearchOption.AllDirectories).Count() > 100);
    }
}
