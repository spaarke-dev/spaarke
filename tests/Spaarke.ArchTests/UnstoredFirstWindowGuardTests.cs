using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2735 (the #2709 pattern) — structural fitness function: a reconciliation pass under <c>src/server/**</c> never computes its
/// first window from a watermark FIELD without storing it, i.e. never reads <c>_field ?? start - SomeLookback</c>.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> A pass that lists "everything changed since my watermark" and has none yet (the first run on an
/// instance) looks back a fixed span from the run's start. Written as <c>since = _watermark ?? startedAt - InitialLookback;</c>,
/// that first window is never stored. When the first run HOLDS the watermark (its listing fails, its carry overflows, it runs
/// report-only), the next run computes the look-back again from ITS OWN start: the window slides forward, and a row changed
/// between the two starts is never listed. Seven passes had this shape (#2709: the Direct-message pass; #2735: the External-team
/// pass, four passes of the secure-child job and the To Do assignee job).</para>
/// <para><b>The sanctioned shape.</b> Store the first window's start before anything can fail, then read it:
/// <c>_watermark ??= startedAt - InitialLookback; since = _watermark.Value;</c>. The <c>??=</c> form does not match: the
/// pattern needs <c>??</c> followed by whitespace, an optional <c>(</c>, then <c>identifier - ...Lookback</c>.</para>
/// <para><b>Maintenance procedure.</b> If this guard fires on your change: store the first window (the <c>??=</c> form above)
/// unless the pass must NOT keep its first window in some mode. Then add an <see cref="Allowlist"/> entry for that one file and
/// field, with the reason the window must not be stored and the ADR or issue that decided it, and say so in the PR. Never widen
/// the pattern's exemptions by renaming the constant (a look-back named without "Lookback" evades the scan; it is still the
/// defect). A stale allowlist entry (its line gone) fails the guard, so remove the entry with the line.</para>
/// <para><b>Enforced by this test.</b> A must-fire control (the unstored forms, parenthesised or not, any field name) and a
/// must-not-fire control (the <c>??=</c> form, prose, the real tree with its allowlist).</para>
/// </remarks>
public class UnstoredFirstWindowGuardTests
{
    /// <summary>A field read falling back to "a time minus a look-back": the first window, computed and not stored.</summary>
    private static readonly Regex UnstoredFirstWindow = new(
        @"\b_\w+\s*\?\?\s*\(?\s*\w+\s*-\s*\w*Look[Bb]ack",
        RegexOptions.Compiled);

    /// <summary>
    /// (Relative file, field) pairs where the unstored form is deliberate. Each carries its reason and the decision behind it.
    /// </summary>
    private static readonly Dictionary<(string File, string Field), string> Allowlist = new()
    {
        [("src/server/api/Sprk.Bff.Api/Services/Access/SecureChildReconciliationJob.cs", "_ordinaryMovesWatermark")] =
            "#2735: the ordinary-parent-move pass is report-only by default, and only a WRITING run moves its watermark, so a "
            + "first window stored by a report-only run would grow without bound. A writing run stores it (`??=` in the line "
            + "above); a report-only run looks back from its own start, and a switch to writes restarts the catch-up sweep "
            + "(task 194 F4-a), which walks every ordinary root. Per-instance job state: ADR-036 (A1) and ADR-052 §5.",
    };

    [Fact(DisplayName = "#2735: no reconciliation pass under src/server computes its first window from a watermark field without storing it")]
    public void NoPassReadsAnUnstoredFirstWindow()
    {
        var hits = ScanTree();
        var violations = hits.Where(h => !Allowlist.ContainsKey((h.File, h.Field))).ToList();

        Assert.True(
            violations.Count == 0,
            "A pass computes its first window as `_field ?? start - ...Lookback` and never stores it. A first run that holds the "
            + "watermark then makes the next run look back from its OWN start, so the window slides past rows never listed "
            + "(#2709, #2735).\n\nREMEDY: `_field ??= start - InitialLookback; since = _field.Value;` (store it before anything "
            + "can fail). If the pass must not store it in some mode, add an Allowlist entry with the reason and the decision "
            + "(see this file's maintenance procedure).\n\nOffending sites:\n  "
            + string.Join("\n  ", violations.Select(v => v.Site)));

        // A stale entry would silently exempt the next site to reuse the field name: every entry must still match a line.
        var stale = Allowlist.Keys.Where(k => !hits.Any(h => h.File == k.File && h.Field == k.Field)).ToList();
        Assert.True(stale.Count == 0,
            "Allowlist entries no longer match any line; remove them with the code they exempted: "
            + string.Join(", ", stale.Select(s => $"{s.File} {s.Field}")));
    }

    [Fact(DisplayName = "#2735 must-fire control: the detector fires on the unstored first window, with or without parentheses, for any field")]
    public void TheDetectorFiresOnTheUnstoredFirstWindow()
    {
        Assert.Single(ScanText("Fake/A.cs", """
            since = _recentChangesWatermark ?? startedAt - InitialRecentChangesLookback;
            """));

        // The External-team pass's form: parenthesised, then converted.
        Assert.Single(ScanText("Fake/B.cs", """
            since = _externalSince ?? (startedAt - ExternalInitialLookback).UtcDateTime;
            """));

        // A local `var`, another start variable, the `LookBack` spelling.
        Assert.Single(ScanText("Fake/C.cs", """
            var since = _watermark ?? runStart - InitialLookBack;
            """));
    }

    [Fact(DisplayName = "#2735 must-not-fire control: the detector does not fire on the stored first window, on prose, or on the real tree")]
    public void TheDetectorDoesNotFireOnTheStoredFirstWindow()
    {
        // (1) The fix: the first window is stored, then read.
        Assert.Empty(ScanText("Fake/D.cs", """
            _directWatermark ??= startedAt - DirectInitialLookback;
            since = _directWatermark.Value;
            _externalSince ??= (startedAt - ExternalInitialLookback).UtcDateTime;
            """));

        // (2) Prose naming the old shape in a line comment.
        Assert.Empty(ScanText("Fake/E.cs", """
            // was: since = _watermark ?? startedAt - InitialLookback;
            var since = _watermark.Value;
            """));

        // (3) A fallback that is not a look-back (a stored value, a constant) is not this defect.
        Assert.Empty(ScanText("Fake/F.cs", """
            var after = _cursor ?? SweepFromStart;
            var since = _watermark ?? fallback;
            """));

        // (4) THE REAL PRODUCTION TREE, with its allowlist: the control that matters.
        Assert.All(ScanTree(), h => Assert.True(Allowlist.ContainsKey((h.File, h.Field)), h.Site));
    }

    // =================================================================================================================
    // MACHINERY: crude by design (arch-fitness scanning, not compilation); both controls pin the detector in each direction.
    // =================================================================================================================

    private static List<(string File, string Field, string Site)> ScanTree()
        => SourceScan.ServerSourceFiles()
            .SelectMany(f => ScanText(SourceScan.Relative(f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();

    private static List<(string File, string Field, string Site)> ScanText(string relativeFile, string rawText)
    {
        var hits = new List<(string, string, string)>();
        var lines = rawText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            foreach (Match match in UnstoredFirstWindow.Matches(code))
            {
                var field = match.Value[..match.Value.IndexOf("??", StringComparison.Ordinal)].Trim();
                hits.Add((relativeFile, field, $"{relativeFile}:{i + 1}: {code.Trim()}"));
            }
        }

        return hits;
    }
}
