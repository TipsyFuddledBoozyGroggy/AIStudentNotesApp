using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Tether.DocReader;

namespace Tether.ArchitectureTests;

/// <summary>
/// P2-B3 stage A (docs/architecture/document-reader.md section 4.1 item 2; docs/API_CONTRACTS_P2.md 5.1 item 8). The DOCX reader is
/// pure: it references only the BCL, uses no network, filesystem, process, console or logging type, and no API or module assembly
/// references it (the API never parses; the parser will run only in the sandbox). Type references are read from metadata with
/// System.Reflection.Metadata, so a use hidden behind an in-box assembly name (System.Runtime holds File and Socket facades) is still found.
/// </summary>
public class DocReaderBoundaryTests
{
    private static readonly Assembly DocReader = typeof(DocxReader).Assembly;

    /// <summary>Whole namespaces that are banned.</summary>
    private static readonly string[] BannedNamespacePrefixes =
    [
        "System.Net.",                          // HTTP, sockets, DNS, WebRequest
        "System.IO.Pipes.", "System.IO.MemoryMappedFiles.", "System.IO.IsolatedStorage.",
        "System.Xml.Resolvers.", "System.Xml.Linq.", "System.Xml.Serialization.", "System.Xml.Xsl.", "System.Xml.Schema.",
        "System.Reflection.Emit.",
        "Microsoft.Extensions.Logging.", "Microsoft.EntityFrameworkCore.", "Npgsql.",
    ];

    /// <summary>Single banned types (exact names, so compiler-emitted types such as DebuggableAttribute are not caught by accident).</summary>
    private static readonly HashSet<string> BannedTypes =
    [
        "System.IO.File", "System.IO.FileStream", "System.IO.FileInfo", "System.IO.Directory", "System.IO.DirectoryInfo",
        "System.IO.Path", "System.IO.DriveInfo", "System.IO.FileSystemInfo", "System.IO.FileSystemWatcher", "System.IO.RandomAccess",
        "System.IO.Compression.ZipFile", "System.IO.Compression.ZipFileExtensions",   // extract to disk
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo", "System.Diagnostics.Debug", "System.Diagnostics.Trace",
        "System.Console", "System.Environment",
        "System.Xml.XmlUrlResolver", "System.Xml.XmlSecureResolver", "System.Xml.XmlDocument", "System.Xml.XmlTextReader",
        "System.Runtime.InteropServices.Marshal",
    ];

    private static List<string> ReferencedTypeNames(Assembly asm)
    {
        using var fs = File.OpenRead(asm.Location);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        return md.TypeReferences.Select(h =>
        {
            var t = md.GetTypeReference(h);
            return $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}";
        }).ToList();
    }

    private static List<string> Banned(IEnumerable<string> typeNames) =>
        typeNames.Where(n => BannedTypes.Contains(n) || BannedNamespacePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public void DocReader_references_only_base_class_library_assemblies()
    {
        var refs = DocReader.GetReferencedAssemblies().Select(r => r.Name!).ToList();
        Assert.Contains("System.IO.Compression", refs);           // the in-box ZIP reader
        var foreign = refs.Where(n => !(n.StartsWith("System.", StringComparison.Ordinal) || n is "System" or "netstandard" or "mscorlib")).ToList();
        Assert.True(foreign.Count == 0, "Tether.DocReader references non-BCL assemblies: " + string.Join(", ", foreign));
        Assert.DoesNotContain(refs, n => n.StartsWith("System.Net", StringComparison.Ordinal) || n == "System.Diagnostics.Process" || n == "System.Console"
                                         || n == "System.IO.Compression.ZipFile" || n == "System.IO.FileSystem");
    }

    [Fact]
    public void DocReader_uses_no_network_filesystem_process_console_or_logging_type()
    {
        var hits = Banned(ReferencedTypeNames(DocReader));
        Assert.True(hits.Count == 0, "Tether.DocReader references banned types: " + string.Join(", ", hits));
    }

    [Fact]
    public void The_type_scan_finds_banned_types_where_they_exist()
    {
        // Positive control so the scan cannot pass vacuously: the Syllabus module uses System.IO.Path (UploadIntake) and this test assembly uses File.
        Assert.Contains("System.IO.Path", Banned(ReferencedTypeNames(typeof(Tether.Syllabus.SyllabusModule).Assembly)));
        Assert.Contains("System.IO.File", Banned(ReferencedTypeNames(typeof(DocReaderBoundaryTests).Assembly)));
        Assert.Contains(ReferencedTypeNames(DocReader), n => n == "System.IO.Compression.ZipArchive");
        Assert.Contains(ReferencedTypeNames(DocReader), n => n == "System.Xml.XmlReader");
    }

    [Fact]
    public void No_api_or_module_assembly_references_the_reader()
    {
        var hostAsm = typeof(Program).Assembly;
        var assemblies = new[] { hostAsm }
            .Concat(hostAsm.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("Tether.", StringComparison.Ordinal)).Select(Assembly.Load))
            .ToList();
        Assert.Contains(assemblies, a => a.GetName().Name == "Tether.Syllabus");     // the walk really reached the modules
        var offenders = assemblies.Where(a => a.GetReferencedAssemblies().Any(r => r.Name == "Tether.DocReader")).Select(a => a.GetName().Name).ToList();
        Assert.True(offenders.Count == 0, "These assemblies reference Tether.DocReader (the API must never parse; contract 5.1 item 8): " + string.Join(", ", offenders));
    }

    [Fact]
    public void DocReader_exposes_only_its_documented_public_surface()
    {
        var leaked = DocReader.GetExportedTypes().Select(t => t.Name)
            .Except(["DocxReader", "DocxLimits", "DocxResult", "DocxDropCounts", "DocxFailure", "DocxFailures"]).ToList();
        Assert.True(leaked.Count == 0, "Tether.DocReader leaks public types: " + string.Join(", ", leaked));
    }
}
