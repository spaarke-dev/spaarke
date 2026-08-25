using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// auth-v4 task 056 (FR-E7) — structural fitness function: fabricated web-search results must stay
/// reachable from exactly one place, and that place must be behind an explicit opt-in.
/// </summary>
/// <remarks>
/// <para><b>Why this is a guard and not a unit test.</b> Before this task, <c>WebSearchHandler</c>
/// answered a missing Bing API key by returning <c>GenerateMockResults()</c> with
/// <c>degradationNote: null</c> — invented search results carrying real-looking URLs, wrapped in
/// citation envelopes tagged <c>SourceType="web"</c> and rendered to the user as citations, with
/// nothing marking them as invented. That was not an edge case: the dev environment has no
/// <c>BingSearch:ApiKey</c> app setting and no <c>BingSearch-ApiKey</c> secret in the vault, so it
/// was the ONLY path web search ever took there.</para>
/// <para>Fabricated sources fed to an LLM and to the citation UI are a grounding hazard, and the
/// failure mode is silent by construction — a behavioural test would have to already know to look
/// for it. A structural guard fails the build the moment someone re-adds an ungated fallback.</para>
/// <para>Companion to <see cref="CredentialGuardTests"/> / <see cref="CredentialCensusTests"/> —
/// the same idea applied to invented evidence rather than to credentials. Per <c>tests/CLAUDE.md</c>
/// "Structural fitness functions" and task 063, this file is MAINTAIN-class: it is the mechanism,
/// not scaffolding.</para>
/// </remarks>
public class FabricatedResultGuardTests
{
    private const string HandlerRelativePath =
        "src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/WebSearchHandler.cs";

    private static string HandlerPath => Path.Combine(SourceScan.RepoRoot, HandlerRelativePath);

    /// <summary>
    /// Exactly one invocation of the mock generator, and it must sit on the opt-in ternary. Comments
    /// and the declaration are excluded so prose about the fix does not trip the count.
    /// </summary>
    [Fact]
    public void GenerateMockResults_IsInvokedFromExactlyOneGatedCallSite()
    {
        var lines = File.ReadAllLines(HandlerPath);

        var invocations = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);

            // Skip the declaration itself.
            if (Regex.IsMatch(code, @"\bList<WebSearchResult>\s+GenerateMockResults\s*\("))
            {
                continue;
            }

            if (code.Contains("GenerateMockResults(", StringComparison.Ordinal))
            {
                invocations.Add($"line {i + 1}: {code.Trim()}");
            }
        }

        Assert.True(
            invocations.Count == 1,
            "Fabricated search results must be reachable from exactly one place, gated on the " +
            "BingSearch:UseMockResults opt-in. An ungated fallback silently presents invented " +
            $"sources as real web citations. Found {invocations.Count} invocation(s): " +
            string.Join(" | ", invocations));

        Assert.True(
            invocations[0].Contains("useMocks", StringComparison.Ordinal),
            "The single GenerateMockResults call site must be gated on the explicit " +
            $"BingSearch:UseMockResults opt-in. Found: {invocations[0]}");
    }

    /// <summary>
    /// Exactly one <c>BuildToolResult</c> call may pass <c>degradationNote: null</c> — the genuine
    /// success path, which has nothing to disclose. Every other path is a fallback and must say so.
    /// </summary>
    /// <remarks>
    /// The original defect was <c>degradationNote: null</c> paired with <c>GenerateMockResults()</c>:
    /// invented sources presented exactly like real ones. Banning the null outright would be wrong —
    /// real results legitimately carry no note — so the invariant is that the undisclosed path is
    /// unique, and is not the fabricated one.
    /// </remarks>
    [Fact]
    public void OnlyTheSuccessPath_PassesANullDegradationNote()
    {
        var lines = File.ReadAllLines(HandlerPath);

        var nullNoteLines = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            if (code.Contains("degradationNote: null", StringComparison.Ordinal))
            {
                nullNoteLines.Add(i + 1);
            }
        }

        Assert.True(
            nullNoteLines.Count == 1,
            "Exactly one BuildToolResult call may omit a degradation note (the real-results path). " +
            "Any other undisclosed path is indistinguishable from real results — which is how the " +
            $"fabricated-results defect stayed invisible. Found at lines: {string.Join(", ", nullNoteLines)}");

        // ...and that one must not be handing back invented results.
        var window = string.Join(
            Environment.NewLine,
            lines.Skip(Math.Max(0, nullNoteLines[0] - 8)).Take(10).Select(SourceScan.StripLineComment));

        Assert.False(
            window.Contains("GenerateMockResults(", StringComparison.Ordinal),
            $"The BuildToolResult call at line {nullNoteLines[0]} omits a degradation note while " +
            "returning fabricated results. That is the exact defect this guard exists to prevent.");
    }

    /// <summary>
    /// The three Bing config keys must remain declared as constants, so this guard and the
    /// operator-facing log messages cannot drift from the strings actually read.
    /// </summary>
    [Fact]
    public void BingSearchConfigKeys_AreDeclaredAsConstants()
    {
        var code = SourceScan.CodeText(File.ReadAllLines(HandlerPath));

        Assert.Contains("ApiKeySecretNameConfigKey = \"BingSearch:ApiKeySecretName\"", code, StringComparison.Ordinal);
        Assert.Contains("ApiKeyConfigKey = \"BingSearch:ApiKey\"", code, StringComparison.Ordinal);
        Assert.Contains("UseMockResultsConfigKey = \"BingSearch:UseMockResults\"", code, StringComparison.Ordinal);
    }
}
