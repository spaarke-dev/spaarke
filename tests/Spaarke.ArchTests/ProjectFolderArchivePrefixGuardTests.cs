using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// A finished project is archived with a <c>.archived</c> marker and its board Status; its folder is never renamed.
/// </summary>
/// <remarks>
/// <para><b>Why a fitness function.</b> Projects used to be archived by renaming <c>projects/&lt;slug&gt;</c> to
/// <c>projects/x-&lt;slug&gt;</c>. The rename broke every link to the project (the Issue's Quick Links, the board's
/// Project Folder field, links from other projects and from skills) and made history-based tools see a brand-new
/// project on archive day (<c>scripts/ai-cost/backfill-tasks-planned.py</c> had to learn to follow renames). Owner
/// decision 2026-10-14: stop renaming; see <c>.claude/skills/devops-project-archive/SKILL.md</c> step 5.</para>
///
/// <para><b>Ratchet.</b> The 37 <c>x-</c> folders that existed on 2026-10-14 are listed in
/// <c>ProjectFolderArchivePrefix.baseline.txt</c> and keep their names (renaming them back would break the links that
/// now point at them). The guard fails on an <c>x-</c> folder not in the baseline, and on a baseline line whose folder
/// no longer exists, so the list never silently grows or rots.</para>
///
/// <para><b>Enforced by:</b> this class. MAINTAIN-class structural fitness function (<c>tests/CLAUDE.md</c>).</para>
/// </remarks>
public sealed class ProjectFolderArchivePrefixGuardTests
{
    private const string Prefix = "x-";
    private const string BaselineFile = "tests/Spaarke.ArchTests/ProjectFolderArchivePrefix.baseline.txt";

    // ── Pure logic ────────────────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<string> ParseBaseline(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    /// <summary>Prefixed folders that are not baselined (new renames), and baseline entries with no folder (stale).</summary>
    internal static (IReadOnlyList<string> NewlyPrefixed, IReadOnlyList<string> Stale) Evaluate(
        IEnumerable<string> folderNames, IEnumerable<string> baseline)
    {
        var folders = folderNames.ToList();
        var known = new HashSet<string>(baseline, StringComparer.Ordinal);
        var present = new HashSet<string>(folders, StringComparer.Ordinal);
        var newlyPrefixed = folders
            .Where(f => f.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && !known.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        var stale = known.Where(b => !present.Contains(b)).OrderBy(b => b, StringComparer.Ordinal).ToList();
        return (newlyPrefixed, stale);
    }

    // ── Real tree ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "No project folder is newly archived by renaming it to x-<slug>; every baselined x- folder still exists")]
    public void RealTree_NoNewArchivePrefix()
    {
        var projectsDir = Path.Combine(SourceScan.RepoRoot, "projects");
        Assert.True(Directory.Exists(projectsDir), "projects/ not found under the repo root");
        var baselinePath = Path.Combine(SourceScan.RepoRoot, BaselineFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(baselinePath), $"{BaselineFile} is missing");

        var folders = Directory.GetDirectories(projectsDir).Select(d => Path.GetFileName(d)!);
        var (newlyPrefixed, stale) = Evaluate(folders, ParseBaseline(File.ReadAllLines(baselinePath)));

        Assert.True(newlyPrefixed.Count == 0,
            "Archive a project with /devops-project-archive (.archived marker + board Status); do not rename its folder. "
            + "Rename these back to their original names: " + string.Join(", ", newlyPrefixed));
        Assert.True(stale.Count == 0,
            "These baselined x- folders no longer exist; delete their lines from " + BaselineFile + ": " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "The real projects/ scan finds the baselined x- folders (the scan is not empty)")]
    public void RealTree_ScanSeesBaselinedFolders()
    {
        var baseline = ParseBaseline(File.ReadAllLines(
            Path.Combine(SourceScan.RepoRoot, BaselineFile.Replace('/', Path.DirectorySeparatorChar))));
        Assert.NotEmpty(baseline);
        var first = Path.Combine(SourceScan.RepoRoot, "projects", baseline[0]);
        Assert.True(Directory.Exists(first), $"expected {first} to exist");
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Control (must fire): an x- folder that is not baselined")]
    public void Control_NewPrefixedFolder_Fires()
    {
        var (newlyPrefixed, _) = Evaluate(["x-old", "x-new", "active-r1"], ["x-old"]);
        Assert.Equal(["x-new"], newlyPrefixed);
    }

    [Fact(DisplayName = "Control (must fire): an X- folder in another case is still a prefixed folder")]
    public void Control_UpperCasePrefix_Fires()
    {
        var (newlyPrefixed, _) = Evaluate(["X-new"], []);
        Assert.Equal(["X-new"], newlyPrefixed);
    }

    [Fact(DisplayName = "Control (must fire): a baselined folder that no longer exists is stale")]
    public void Control_StaleBaseline_Fires()
    {
        var (_, stale) = Evaluate(["active-r1"], ["x-gone"]);
        Assert.Equal(["x-gone"], stale);
    }

    [Fact(DisplayName = "Control (must not fire): baselined x- folders and ordinary folders, including names containing x-")]
    public void Control_BaselinedAndOrdinary_DoNotFire()
    {
        var (newlyPrefixed, stale) = Evaluate(["x-old", "active-r1", "spaarke-x-ray", "box-r1"], ["x-old"]);
        Assert.Empty(newlyPrefixed);
        Assert.Empty(stale);
    }

    [Fact(DisplayName = "Baseline parsing ignores comments and blank lines")]
    public void Baseline_IgnoresCommentsAndBlanks()
    {
        Assert.Equal(["x-a", "x-b"], ParseBaseline(["# header", "", "  x-a  ", "x-b", "   "]));
    }
}
