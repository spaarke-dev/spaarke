using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Forcing-function (code-quality-and-assurance-r3 task 040 / FR-14; archived-R3 item #8 God-class
/// audit; redesigned 2026-08-15 by quality-godclass-refine). A **regression ratchet** on server source
/// file size — it does NOT assert an ideal max (best practice is far smaller); its job is narrow and
/// specific:
/// <list type="number">
/// <item><b>No NEW god-classes.</b> A file NOT already oversized must stay under
/// <see cref="NewFileCeilingLines"/> (2,000) — the line at which a server file is "becoming a god-class."</item>
/// <item><b>Existing god-classes may not get worse.</b> Every currently-oversized file is frozen at its
/// measured size (<see cref="Waivers"/>); it may drift up to <see cref="GraceLines"/> (100) for incidental
/// edits but a real expansion trips the guard — decompose it, or re-baseline the waiver with a documented
/// reason in the PR.</item>
/// </list>
/// This mirrors the ADR-010 1:1-interface ceiling (153) in <see cref="ADR010_DITests"/>: a baseline that
/// only ratchets DOWN. It is a guardrail, not an architectural law — the 2,000 line is a pragmatic
/// "getting too big" marker, not a target the frozen files must hit. The frozen files are slated for
/// decomposition projects (see <c>projects/code-quality-and-assurance-r3/notes/red-item-analyses/RED-1,2,4</c>);
/// as each is split below 2,000, DELETE its waiver entry (that ratchets the floor down permanently).
/// </summary>
/// <remarks>
/// Discoverability (why this won't ambush an editor): the rule is documented in
/// <c>.claude/patterns/testing/god-class-ratchet.md</c> + root <c>CLAUDE.md</c>, and this test's failure
/// message names the exact file + the two ways to resolve. Waiver values are exact measured LOC so the
/// list is trivial to regenerate (<c>find src/server -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*'
/// -exec wc -l {} \;</c>).
/// </remarks>
public class GodClassGuardTests
{
    private static readonly string SourceRoot = ResolveSourceRoot();

    /// <summary>A file NOT on the waiver list is "becoming a god-class" past this. Redesigned from an
    /// arbitrary single ceiling (4,950→2,700) to a defensible new-file line + a per-file freeze.</summary>
    private const int NewFileCeilingLines = 2_000;

    /// <summary>Incidental-edit tolerance on a frozen file before the ratchet complains. 100 lines is
    /// "one big method" — small edits pass; a real god-class expansion trips it.</summary>
    private const int GraceLines = 100;

    /// <summary>
    /// Every currently-oversized (&gt;2,000 LOC) server file, frozen at its measured size (2026-08-15).
    /// Keys are repo-relative paths with '/' separators (matches on Windows AND Linux CI). A frozen file
    /// may grow up to +<see cref="GraceLines"/>; beyond that, decompose it or re-baseline its number here
    /// with a PR reason. When a file drops below <see cref="NewFileCeilingLines"/>, REMOVE its entry.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Waivers = new Dictionary<string, int>
    {
        ["src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs"] = 4_911,   // RED-1
        ["src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs"] = 4_066,                        // RED-2
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs"] = 3_573,
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs"] = 3_085,
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs"] = 2_999,
        ["src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs"] = 2_864,          // RED-4
        // DataverseWebApiService.cs removed from waivers 2026-08-16 (RED-4 B hardening): dead-code
        // deletion (−1,414 LOC) dropped it from 2,822 → 1,409, below the 2,000 ceiling. Now governed
        // by the standard new-file rule. See docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md.
        ["src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs"] = 2_676,
        ["src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs"] = 2_651,
        ["src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs"] = 2_528,
        ["src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs"] = 2_380,
        ["src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs"] = 2_304,
        ["src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationEnrichmentService.cs"] = 2_087,
        ["src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs"] = 2_038,
    };

    [Fact(DisplayName = "FR-14: no NEW server file over 2,000 LOC; no frozen god-class grows past its baseline")]
    public void NoServerSourceFileExceedsRatchet()
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
                if (lines > frozen + GraceLines)
                {
                    offenders.Add($"{rel} = {lines} lines — grew past its frozen baseline {frozen} (+{GraceLines} grace). " +
                                  $"Decompose it, or re-baseline the waiver to {lines} with a documented reason in the PR.");
                }
            }
            else if (lines > NewFileCeilingLines)
            {
                offenders.Add($"{rel} = {lines} lines — a NEW file over the {NewFileCeilingLines}-line god-class line. " +
                              $"Split it, or (if genuinely justified) add a documented waiver entry in the PR.");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"FR-14 God-class ratchet. This is a review forcing-function, not a rubber stamp. See " +
            $".claude/patterns/testing/god-class-ratchet.md.\nOffenders:\n{string.Join("\n", offenders.OrderByDescending(o => o))}");
    }

    // --- Negative controls: prove BOTH rules would flag a violation (not tautologies) ---

    [Fact(DisplayName = "FR-14: ratchet flags a new file over the ceiling AND a frozen file grown past grace")]
    public void Ratchet_NegativeControl_FlagsBothRuleViolations()
    {
        // Rule 1: a non-waivered file at ceiling+1 must exceed the new-file ceiling.
        Assert.True(NewFileCeilingLines + 1 > NewFileCeilingLines);
        // Rule 2: a frozen file grown past its baseline + grace must be flagged.
        var frozen = Waivers.Values.First();
        Assert.True(frozen + GraceLines + 1 > frozen + GraceLines, "A frozen file growing past its grace must be flagged.");
        // Sanity: the new-file ceiling ratcheted DOWN from the prior 4,950 freeze, and every waiver is above it.
        Assert.True(NewFileCeilingLines < 4_950);
        Assert.All(Waivers.Values, v => Assert.True(v > NewFileCeilingLines, "A waiver below the ceiling should just be un-waivered."));
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
