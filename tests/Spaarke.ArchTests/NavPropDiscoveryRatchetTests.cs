using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Pattern D (issues #2803 P-3 and #2804 P-4): the Dataverse "nav-prop discovery" metadata query
/// (<c>EntityDefinitions(LogicalName='…')/ManyToOneRelationships?$select=ReferencingAttribute,…</c>) has ONE client
/// implementation, <c>discoverNavProps</c> in <c>@spaarke/ui-components</c>
/// (<c>PolymorphicResolverService.ts</c>). A private copy is a second cache and a second place the SRFR-050 fix
/// (lowercase the logical name; warn on an empty result) can be forgotten.
///
/// <para><b>Ratchet.</b> A client file that contains the literal <c>ManyToOneRelationships</c> must be either the shared
/// implementation or a named entry in <see cref="Baseline"/>. A NEW file that contains it fails the build (a new copy: call
/// <c>discoverNavProps</c> instead). A baseline file that no longer contains it also fails (the baseline is stale: delete
/// the entry), so the list can only shrink. #2803/#2804 folded in <c>ConnectionsWriteHandler</c>,
/// <c>ResolverWriteHandler</c> and <c>useInlineTodoCreate</c> (<c>discoverTodoNavProps</c> delegates too); #2909 folded in
/// the LegalWorkspace <c>matterService</c>, Notepad <c>discoverMemoNavProps</c> and <c>CreateProjectWizard</c>. The one
/// entry left is a copy that cannot import a module, with its reason.</para>
///
/// <para><b>Scope.</b> Every <c>.ts</c> / <c>.tsx</c> / <c>.js</c> file under <c>src/client</c> and <c>src/solutions</c>,
/// excluding tests, mocks, declaration files, installed packages and build output, and the committed PCF solution
/// bundles (<c>bundle.js</c>, which are build output of the sources this guard already reads).</para>
///
/// <para><b>Enforced by:</b> this guard (ArchTest ratchet). It is a source scan, so it cannot see a query built from a
/// split string; the literal is the only stable marker.</para>
/// </summary>
public sealed class NavPropDiscoveryRatchetTests
{
    /// <summary>The literal that marks a nav-prop discovery query.</summary>
    private const string Marker = "ManyToOneRelationships";

    /// <summary>The one sanctioned implementation.</summary>
    private static readonly string[] Allowed =
    [
        "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts",
    ];

    /// <summary>
    /// Known private copies, each with the reason it stays. May shrink, never grow: a new copy is a new failure, not a new
    /// entry (call <c>discoverNavProps</c>). Remove an entry in the change that removes its copy; the guard fails on a
    /// stale entry.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Baseline = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Permanent (#2909): a ribbon web resource is a plain script with no module system, so it cannot import the
        // TS package. Its copy follows the shared rules (lowercased logical name, a warning on an empty result), and
        // secureChildRibbonScript.test.ts pins both. Its SpaarkeMaster twin (src/dataverse/**) is outside this scan.
        ["src/client/webresources/js/sprk_secure_child_ribbon.js"] =
            "plain-JS ribbon web resource with no module system, cannot import the TS package; aligned with discoverNavProps (#2909)",
    };

    private static readonly string[] ExcludedSegments =
    [
        "node_modules", "dist", "out", "bin", "obj", "coverage", "__tests__", "__mocks__", ".vite",
    ];

    // =============================================================================================

    [Fact(DisplayName = "Pattern D: only the shared discoverNavProps (plus the filed baseline) queries ManyToOneRelationships")]
    public void NoNewNavPropDiscoveryCopies()
    {
        var withMarker = FilesContainingMarker();
        var problems = FindDrift(withMarker, Allowed, Baseline);

        Assert.True(
            problems.Count == 0,
            "The nav-prop discovery query has one client implementation: discoverNavProps in "
            + "@spaarke/ui-components (src/services/PolymorphicResolverService.ts).\n"
            + "NEW COPY: do not add a private copy or a baseline entry; import discoverNavProps (it lowercases the name, "
            + "caches per entity and warns on an empty result).\n"
            + "STALE BASELINE: the copy is gone; delete its entry from Baseline in this file.\n\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Pattern D: every baseline entry names a reason")]
    public void EveryBaselineEntryHasAReason()
    {
        var unexplained = Baseline.Where(kv => kv.Value.Trim().Length < 20).Select(kv => kv.Key).ToList();

        Assert.True(unexplained.Count == 0, "Baseline entries need a written reason: " + string.Join(", ", unexplained));
    }

    // =============================================================================================
    // Negative control: the detector fires on a seeded violation
    // =============================================================================================

    [Fact(DisplayName = "Pattern D: negative control — a new copy and a stale baseline entry are both reported")]
    public void FindDrift_NegativeControl_ReportsNewCopyAndStaleBaseline()
    {
        var found = new[]
        {
            "src/client/shared/Some.New.Package/src/hooks/useThing.ts", // a new private copy
            "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts", // allowed
        };
        var allowed = new[] { "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts" };
        var baseline = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/solutions/Old/src/oldCopy.ts"] = "was a copy, since folded into the shared one",
        };

        var problems = FindDrift(found, allowed, baseline);

        Assert.Contains(problems, p => p.StartsWith("NEW COPY", StringComparison.Ordinal) && p.Contains("useThing.ts", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("STALE BASELINE", StringComparison.Ordinal) && p.Contains("oldCopy.ts", StringComparison.Ordinal));
        Assert.Equal(2, problems.Count);
    }

    [Fact(DisplayName = "Pattern D: negative control — the marker matcher fires on a discovery query and the file scan reaches the shared file")]
    public void Scan_NegativeControl_FindsTheSharedImplementation()
    {
        Assert.True(ContainsMarker("`/api/data/v9.0/EntityDefinitions(LogicalName='x')/ManyToOneRelationships?$select=A`"));

        // A scan that silently found nothing would report every baseline entry as stale and point at the wrong cause.
        Assert.Contains(Allowed[0], FilesContainingMarker());
    }

    // =============================================================================================
    // Positive control: the detector stays silent on the sanctioned shape
    // =============================================================================================

    [Fact(DisplayName = "Pattern D: positive control — the shared file and exactly the baseline files report nothing")]
    public void FindDrift_PositiveControl_StaysSilentOnTheSanctionedShape()
    {
        var baseline = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/solutions/Known/src/knownCopy.ts"] = "a filed follow-up copy that still exists",
        };
        var found = new[]
        {
            "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts",
            "src/solutions/Known/src/knownCopy.ts",
        };
        var allowed = new[] { "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts" };

        Assert.Empty(FindDrift(found, allowed, baseline));
        Assert.False(ContainsMarker("callers use discoverNavProps('sprk_todo')"));
    }

    // =============================================================================================
    // Machinery
    // =============================================================================================

    private static bool ContainsMarker(string text) => text.Contains(Marker, StringComparison.Ordinal);

    /// <summary>Repo-relative, forward-slash paths of the client files that contain <see cref="Marker"/>.</summary>
    private static List<string> FilesContainingMarker()
    {
        var roots = new[] { "client", "solutions" }
            .Select(r => Path.Combine(SourceScan.RepoRoot, "src", r))
            .Where(Directory.Exists);

        return roots
            .SelectMany(Walk)
            .Where(IsClientSource)
            .Where(f => ContainsMarker(File.ReadAllText(f)))
            .Select(f => SourceScan.Relative(f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsClientSource(string file)
    {
        var name = Path.GetFileName(file);
        var ext = Path.GetExtension(file);
        var isScript = ext.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".js", StringComparison.OrdinalIgnoreCase);

        return isScript
               && !name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
               && !Regex.IsMatch(name, @"\.(test|spec)\.[jt]sx?$", RegexOptions.IgnoreCase)
               // The committed PCF solution bundles are build output of sources this scan already reads.
               && !name.Equals("bundle.js", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every file under <paramref name="directory"/>, never descending into an excluded directory.</summary>
    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (ExcludedSegments.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // Agent worktrees live under .claude/worktrees; never scan another checkout's copy.
            if (name.Equals("worktrees", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(Path.GetDirectoryName(child) ?? string.Empty).Equals(".claude", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Walk(child))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The drift between the files that contain the marker (<paramref name="found"/>) and the sanctioned set: a file in
    /// neither <paramref name="allowed"/> nor <paramref name="baseline"/> is a NEW COPY; a baseline entry whose file does
    /// not contain the marker is a STALE BASELINE.
    /// </summary>
    private static List<string> FindDrift(
        IReadOnlyCollection<string> found,
        IReadOnlyCollection<string> allowed,
        IReadOnlyDictionary<string, string> baseline)
    {
        var problems = new List<string>();
        var foundSet = new HashSet<string>(found, StringComparer.Ordinal);

        foreach (var file in found.Where(f => !allowed.Contains(f, StringComparer.Ordinal) && !baseline.ContainsKey(f)))
        {
            problems.Add($"NEW COPY: {file} queries ManyToOneRelationships itself. Use discoverNavProps from @spaarke/ui-components.");
        }

        foreach (var entry in baseline.Keys.Where(k => !foundSet.Contains(k)))
        {
            problems.Add($"STALE BASELINE: {entry} no longer contains ManyToOneRelationships (or the file is gone). Delete its Baseline entry.");
        }

        return problems;
    }
}
