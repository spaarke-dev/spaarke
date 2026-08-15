using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Forcing-function (code-quality-and-assurance-r3 task 040 / FR-14): locks the layering of the
/// shared server libraries relative to the BFF application.
/// </summary>
/// <remarks>
/// <para>
/// IMPORTANT — the ACTUAL dependency direction differs from the legacy ideal documented in
/// <c>src/server/shared/CLAUDE.md</c> ("Spaarke.Core has no Spaarke dependencies; Spaarke.Dataverse
/// depends on Spaarke.Core"). At head, the real project graph is the reverse:
/// </para>
/// <code>
///   Spaarke.Dataverse   -> (no Spaarke project references) — the BASE layer
///   Spaarke.Core        -> Spaarke.Dataverse
///   Sprk.Bff.Api        -> Spaarke.Core (+ Spaarke.Dataverse)
/// </code>
/// <para>
/// Per root CLAUDE.md §2 ("Code wins. Docs lag."), this test encodes the invariants that are TRUE
/// at head and are the load-bearing ones regardless of which of Core/Dataverse is the base:
/// </para>
/// <list type="number">
///   <item>Spaarke.Dataverse (the base) references NO other Spaarke project — so the Core/Dataverse
///         pair stays acyclic.</item>
///   <item>NO shared library (Spaarke.Core, Spaarke.Dataverse) references the BFF application
///         (Sprk.Bff.Api) — libraries never depend "up" onto the app composition root.</item>
/// </list>
/// <para>
/// The stale <c>src/server/shared/CLAUDE.md</c> direction (Core-is-base) is a documentation-drift
/// finding surfaced by task 040; it does not change what is enforceable here.
/// </para>
/// </remarks>
public class LayerDependencyTests
{
    private static readonly string SourceRoot = ResolveSourceRoot();

    private const string CoreCsproj = "src/server/shared/Spaarke.Core/Spaarke.Core.csproj";
    private const string DataverseCsproj = "src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj";

    [Fact(DisplayName = "FR-14: Spaarke.Dataverse (base layer) references no other Spaarke project")]
    public void DataverseReferencesNoOtherSpaarkeProject()
    {
        var refs = ReadProjectReferences(DataverseCsproj);

        var forbidden = refs
            .Where(r => r.Contains("Spaarke.Core", StringComparison.Ordinal) ||
                        r.Contains("Sprk.Bff.Api", StringComparison.Ordinal))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            forbidden.Count == 0,
            "FR-14 layer violation: Spaarke.Dataverse (the base shared library) added a ProjectReference " +
            "to another Spaarke project. It must reference no other Spaarke project to keep the " +
            "Core/Dataverse layering acyclic. Offending references: " + string.Join(", ", forbidden));
    }

    [Fact(DisplayName = "FR-14: no shared library references the BFF application (Sprk.Bff.Api)")]
    public void SharedLibrariesDoNotReferenceTheBffApplication()
    {
        var violations = new List<string>();
        foreach (var csproj in new[] { CoreCsproj, DataverseCsproj })
        {
            var appRefs = ReadProjectReferences(csproj)
                .Where(r => r.Contains("Sprk.Bff.Api", StringComparison.Ordinal));
            violations.AddRange(appRefs.Select(r => $"{csproj} -> {r}"));
        }

        Assert.True(
            violations.Count == 0,
            "FR-14 layer violation: a shared library references the BFF application (Sprk.Bff.Api). " +
            "Shared libraries are consumed BY the app; they must never depend on it. " +
            "Offending references:\n" + string.Join("\n", violations));
    }

    // --- Negative control: prove the csproj reference detector catches a seeded violation ---

    [Fact(DisplayName = "FR-14: layer detector flags a seeded forbidden ProjectReference")]
    public void ProjectReferenceDetector_NegativeControl()
    {
        const string seeded = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\..\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj" />
                <ProjectReference Include="..\Spaarke.Core\Spaarke.Core.csproj" />
              </ItemGroup>
            </Project>
            """;

        var refs = ExtractProjectReferences(seeded);

        Assert.Contains(refs, r => r.Contains("Sprk.Bff.Api", StringComparison.Ordinal));
        Assert.Contains(refs, r => r.Contains("Spaarke.Core", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.Contains("Spaarke.Dataverse", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ReadProjectReferences(string relativeCsprojPath)
    {
        var full = Path.Combine(SourceRoot, relativeCsprojPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"csproj not found: {full}");
        return ExtractProjectReferences(File.ReadAllText(full));
    }

    /// <summary>Extracts the <c>Include</c> path of every <c>&lt;ProjectReference&gt;</c> element.</summary>
    private static IReadOnlyList<string> ExtractProjectReferences(string csprojText)
        => Regex.Matches(csprojText, @"<ProjectReference\s+Include\s*=\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

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
