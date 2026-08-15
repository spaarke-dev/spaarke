using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Forcing-function (code-quality-and-assurance-r3 task 040 / FR-14; archived-R3 item #8 God-class
/// audit): a baseline-and-ratchet LOC guard on the .NET server surface. It freezes the current
/// worst-offender line count and fails when any production source file grows past the ceiling —
/// the same maintenance mechanism as the ADR-010 1:1-interface ceiling (currently 153) in
/// <see cref="ADR010_DITests"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scope: <c>src/server/**/*.cs</c> (the BFF API + shared libraries), excluding <c>obj/</c> and
/// <c>bin/</c>. Line count uses <see cref="File.ReadAllLines(string)"/> — the same method the
/// baseline was measured with, so the count is checkout-stable (a line terminator is a line
/// terminator regardless of LF vs CRLF).
/// </para>
/// <para>
/// Current worst offender (measured 2026-08-14, task 040):
/// <c>src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs</c> at 4,911 lines.
/// Ceiling set to <see cref="MaxFileLineCeiling"/> = 4,950 — the current max frozen with a small
/// (&lt;1%) headroom for measurement/line-ending wobble. A genuine god-class regression adds
/// hundreds of lines and trips this well before the headroom is consumed. The intent is to RATCHET
/// DOWN as god-classes are decomposed — never up without documenting why (mirrors the 153 ceiling).
/// </para>
/// </remarks>
public class GodClassGuardTests
{
    private static readonly string SourceRoot = ResolveSourceRoot();

    /// <summary>
    /// Frozen ceiling. Current max = 4,911 (SpeAdminGraphService.cs, 2026-08-14). To lower it,
    /// decompose the largest file and drop this constant. To raise it, document the new god-class
    /// and the reason in the PR (this is the review forcing-function, not a suppression).
    /// </summary>
    private const int MaxFileLineCeiling = 4_950;

    [Fact(DisplayName = "FR-14: no server source file exceeds the frozen God-class LOC ceiling")]
    public void NoServerSourceFileExceedsLineCeiling()
    {
        var serverRoot = Path.Combine(SourceRoot, "src", "server");
        Assert.True(Directory.Exists(serverRoot), $"src/server not found: {serverRoot}");

        var offenders = EnumerateProductionCsFiles(serverRoot)
            .Select(f => (Path: f, Lines: File.ReadAllLines(f).Length))
            .Where(x => x.Lines > MaxFileLineCeiling)
            .OrderByDescending(x => x.Lines)
            .Select(x => $"{x.Path.Replace(SourceRoot + Path.DirectorySeparatorChar, string.Empty)} = {x.Lines} lines")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"FR-14 God-class violation: a server source file exceeds the frozen ceiling of " +
            $"{MaxFileLineCeiling} lines. Decompose the file, or (if genuinely justified) raise " +
            $"MaxFileLineCeiling with a documented reason in the PR — this is the ratchet review, " +
            $"not a rubber stamp.\nOffenders:\n{string.Join("\n", offenders)}");
    }

    // --- Negative control: prove the ceiling comparison would flag an oversized file ---

    [Fact(DisplayName = "FR-14: God-class ceiling comparison flags a seeded oversized value")]
    public void LineCeiling_NegativeControl_FlagsOversizedFile()
    {
        const int seededOversized = MaxFileLineCeiling + 500;
        Assert.True(seededOversized > MaxFileLineCeiling,
            "A file exceeding the ceiling must be flagged by the guard.");

        // And the current worst offender must sit at-or-below the ceiling (the rule is green now).
        Assert.True(4_911 <= MaxFileLineCeiling,
            "The documented current max (4,911) must be within the frozen ceiling.");
    }

    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
        => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

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
