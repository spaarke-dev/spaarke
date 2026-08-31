using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Forcing-function (code-quality-and-assurance-r3 task 040 / FR-14): locks the task-028
/// consolidation of every <see cref="IDataverseService"/> → <c>ServiceClient</c> unwrap into the
/// single sanctioned extension <c>Spaarke.Dataverse/DataverseServiceClientExtensions.cs</c>
/// (<c>UnwrapServiceClient</c> / <c>TryUnwrapServiceClient</c>).
///
/// The registered <c>IDataverseService</c> implementation (<c>DataverseServiceClientImpl</c>) WRAPS
/// a <c>ServiceClient</c>; it does NOT derive from it. A direct <c>is</c> / <c>as</c> / <c>(cast)</c>
/// of <c>IDataverseService</c> to <c>ServiceClient</c> therefore ALWAYS fails at runtime — the
/// historical "Bug-1" class that shipped broken on the invoice-totals path. Task 028 removed all 17
/// ad-hoc downcast sites and funnelled them through the one extension; this test prevents a new
/// per-file downcast helper from silently reappearing.
/// </summary>
/// <remarks>
/// Source-scan (not assembly-scan) because the idiom to forbid is a SYNTACTIC one (`as`/`is`/`cast`
/// against a facade), which is what re-drifts. See ADR010_DITests for the same source-scan style.
/// The scan strips line comments so the doc-comment inside the sanctioned extension (which
/// literally spells out "a direct is/as ServiceClient cast") is not itself flagged; the sanctioned
/// file is additionally excluded by name.
/// </remarks>
public class DataverseServiceClientDowncastTests
{
    private static readonly string SourceRoot = ResolveSourceRoot();

    /// <summary>The ONE file allowed to materialize a <c>ServiceClient</c> from the facade.</summary>
    private const string SanctionedExtensionFileName = "DataverseServiceClientExtensions.cs";

    [Fact(DisplayName = "FR-14: no `as/is/(cast)` of a facade to ServiceClient outside DataverseServiceClientExtensions.cs")]
    public void NoServiceClientDowncastOutsideSanctionedExtension()
    {
        // Arrange
        var srcRoot = Path.Combine(SourceRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src directory not found: {srcRoot}");

        var csFiles = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !string.Equals(Path.GetFileName(f), SanctionedExtensionFileName, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();
        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (FindServiceClientCast(lines[i]))
                {
                    var rel = file.Replace(SourceRoot + Path.DirectorySeparatorChar, string.Empty);
                    violations.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        // Assert
        Assert.True(
            violations.Count == 0,
            "FR-14 violation: a `ServiceClient` downcast/type-test was found outside the sanctioned " +
            "Spaarke.Dataverse/DataverseServiceClientExtensions.cs. IDataverseService is a WRAPPER, " +
            "not a ServiceClient — a direct is/as/(cast) fails at runtime (the invoice-totals Bug-1). " +
            "Use IDataverseService.UnwrapServiceClient(...) / TryUnwrapServiceClient(...) instead. " +
            $"Sites ({violations.Count}):\n{string.Join("\n", violations)}");
    }

    // --- Negative controls: prove the detector catches the idiom and ignores benign lines ---

    [Fact(DisplayName = "FR-14: downcast detector flags the seeded idioms and ignores benign uses")]
    public void ServiceClientCastDetector_NegativeControl()
    {
        // Seeded violations — each MUST be detected.
        Assert.True(FindServiceClientCast("var sc = service as ServiceClient;"), "`as ServiceClient` must be detected.");
        Assert.True(FindServiceClientCast("if (service is ServiceClient sc) { }"), "`is ServiceClient` must be detected.");
        Assert.True(FindServiceClientCast("var sc = (ServiceClient)service;"), "`(ServiceClient)` cast must be detected.");

        // Benign lines — MUST NOT be flagged (false-positive guards).
        Assert.False(FindServiceClientCast("private readonly ServiceClient _client;"), "A field declaration is not a downcast.");
        Assert.False(FindServiceClientCast("_client = new ServiceClient(connectionString);"), "Constructing the singleton is not a downcast.");
        Assert.False(FindServiceClientCast("var t = typeof(ServiceClient);"), "typeof(ServiceClient) is not a downcast.");
        Assert.False(FindServiceClientCast("if (service is not DataverseServiceClientImpl impl) { }"), "The sanctioned impl type-test is not a ServiceClient downcast.");
        Assert.False(FindServiceClientCast("// a direct is/as ServiceClient cast always fails"), "A comment must be ignored.");
    }

    /// <summary>
    /// Detects a <c>ServiceClient</c> downcast idiom on a single source line, ignoring content after
    /// a line comment. Matches <c>as ServiceClient</c>, <c>is ServiceClient</c>, and a
    /// <c>(ServiceClient)</c> cast — while excluding <c>typeof/nameof/default/sizeof(ServiceClient)</c>
    /// (the cast pattern requires the <c>(</c> NOT be preceded by an identifier character).
    /// </summary>
    private static bool FindServiceClientCast(string line)
    {
        var code = StripLineComment(line);
        if (code.Length == 0)
            return false;

        // `as ServiceClient` / `is ServiceClient` (followed by a word boundary, so it does not
        // match `ServiceClientImpl` / `ServiceClientExtensions`).
        if (Regex.IsMatch(code, @"\b(?:as|is)\s+ServiceClient\b") &&
            !Regex.IsMatch(code, @"\b(?:as|is)\s+ServiceClient\w"))
        {
            return true;
        }

        // `(ServiceClient)` cast — `(` not preceded by an identifier char (excludes typeof(...)
        // etc.), and `ServiceClient` closed by `)` (excludes `(ServiceClient client)` params).
        if (Regex.IsMatch(code, @"(?<![A-Za-z0-9_])\(\s*ServiceClient\s*\)"))
            return true;

        return false;
    }

    /// <summary>Removes a <c>//</c> line comment (naive; adequate for arch-fitness scanning).</summary>
    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    private static string ResolveSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
