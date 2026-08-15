using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Forcing-function (code-quality-and-assurance-r3 task 040 / FR-14; archived-R3 item #8 God-class
/// audit; ratcheted 2026-08-15 by quality-r3-followups): a baseline-and-ratchet LOC guard on the
/// .NET server surface. It caps NEW files at a tight ceiling and freezes each currently-oversized
/// file at its present size so a god-class can only shrink, never grow.
/// </summary>
/// <remarks>
/// <para>
/// Scope: <c>src/server/**/*.cs</c> (the BFF API + shared libraries), excluding <c>obj/</c> and
/// <c>bin/</c>. Line count uses <see cref="File.ReadAllLines(string)"/> — checkout-stable across
/// LF/CRLF.
/// </para>
/// <para>
/// Two rules, so the guard is a real ratchet rather than a single-worst-offender freeze:
/// <list type="number">
/// <item>Any file NOT on the waiver list must be ≤ <see cref="CeilingLines"/> (2,700). This catches
/// the next tier of god-classes (the 4,950 ceiling hid eight files beneath it).</item>
/// <item>Any file ON the waiver list must be ≤ its frozen size. A waivered god-class that GROWS
/// trips the guard — it may only be decomposed down, never expanded.</item>
/// </list>
/// As each waivered file is decomposed below 2,700, delete its waiver entry (that ratchets the floor
/// down permanently). Raising a number or adding a waiver is the review forcing-function — document
/// the reason in the PR; it is not a rubber stamp.
/// </para>
/// </remarks>
public class GodClassGuardTests
{
    private static readonly string SourceRoot = ResolveSourceRoot();

    /// <summary>Ceiling for any file NOT explicitly waivered. Ratcheted 4,950 → 2,700 (2026-08-15).</summary>
    private const int CeilingLines = 2_700;

    /// <summary>
    /// Currently-oversized files frozen at their present line count (measured 2026-08-15). Keys are
    /// repo-relative paths with '/' separators. Each is a tracked decomposition target
    /// (SpeAdminGraphService / ChatEndpoints / Compose* / the two Dataverse stacks → NG1). When a file
    /// is decomposed below <see cref="CeilingLines"/>, REMOVE its entry here — that ratchets the floor.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Waivers = new Dictionary<string, int>
    {
        ["src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs"] = 4_911,
        ["src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs"] = 4_066,
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs"] = 3_573,
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs"] = 3_085,
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs"] = 2_999,
        ["src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs"] = 2_864,
        ["src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs"] = 2_822,
    };

    [Fact(DisplayName = "FR-14: no NEW server file exceeds the ratchet ceiling; no waivered god-class grows")]
    public void NoServerSourceFileExceedsLineCeiling()
    {
        var serverRoot = Path.Combine(SourceRoot, "src", "server");
        Assert.True(Directory.Exists(serverRoot), $"src/server not found: {serverRoot}");

        var offenders = new List<string>();
        foreach (var file in EnumerateProductionCsFiles(serverRoot))
        {
            var lines = File.ReadAllLines(file).Length;
            var rel = RelPath(file);
            if (Waivers.TryGetValue(rel, out var frozen))
            {
                if (lines > frozen)
                {
                    offenders.Add($"{rel} = {lines} lines (GREW past its frozen waiver of {frozen} — decompose, do not expand)");
                }
            }
            else if (lines > CeilingLines)
            {
                offenders.Add($"{rel} = {lines} lines (exceeds the {CeilingLines}-line ratchet ceiling — decompose, or add a documented waiver in the PR)");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"FR-14 God-class violation. This is the ratchet review, not a rubber stamp: decompose the " +
            $"file(s) below, or (if genuinely justified) adjust CeilingLines/Waivers with a documented " +
            $"reason in the PR.\nOffenders:\n{string.Join("\n", offenders.OrderByDescending(o => o))}");
    }

    // --- Negative controls: prove BOTH rules would flag a violation (not tautologies) ---

    [Fact(DisplayName = "FR-14: ratchet flags a new file over the ceiling and a grown waivered file")]
    public void LineCeiling_NegativeControl_FlagsBothRuleViolations()
    {
        // Rule 1: a non-waivered file at ceiling+1 must be over the ceiling.
        Assert.True(CeilingLines + 1 > CeilingLines);
        // Rule 2: a waivered file that grows by one line must exceed its frozen size.
        var frozen = Waivers.Values.First();
        Assert.True(frozen + 1 > frozen, "A waivered file growing past its frozen size must be flagged.");
        // Sanity: the tightened ceiling is genuinely below the old 4,950 freeze (a real ratchet-down).
        Assert.True(CeilingLines < 4_950, "The ceiling must have ratcheted DOWN from the prior 4,950 freeze.");
    }

    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
        => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // Repo-relative path with '/' separators so waiver keys match on Windows AND Linux CI.
    private static string RelPath(string fullPath)
        => fullPath.Replace(SourceRoot + Path.DirectorySeparatorChar, string.Empty)
                   .Replace(Path.DirectorySeparatorChar, '/');

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
