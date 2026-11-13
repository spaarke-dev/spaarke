using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2735 (the #2709 pattern) — structural fitness function: a reconciliation pass under <c>src/server/**</c> never computes its
/// first window from a watermark FIELD without storing it, i.e. never reads <c>_field ?? start - span</c>.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> A pass that lists "everything changed since my watermark" and has none yet (the first run on an
/// instance) looks back a fixed span from the run's start. Written as <c>since = _watermark ?? startedAt - InitialLookback;</c>,
/// that first window is never stored. When the first run HOLDS the watermark (its listing fails, its carry overflows, it runs
/// report-only, it throws before reaching the pass), the next run computes the look-back again from ITS OWN start: the window
/// slides forward, and a row changed between the two starts is never listed. Seven passes had this shape (#2709: the
/// Direct-message pass; #2735: the External-team pass, four passes of the secure-child job and the To Do assignee job).</para>
/// <para><b>The sanctioned shape.</b> Store the first window's start before anything can fail, then read it:
/// <c>_watermark ??= startedAt - InitialLookback; since = _watermark.Value;</c>. The <c>??=</c> form does not match: the
/// pattern needs <c>??</c> followed by whitespace, an optional <c>(</c>, then <c>operand - operand</c>.</para>
/// <para><b>What the pattern covers.</b> Any field read (<c>_name</c>) falling back to a subtraction, whatever the look-back is
/// called, scanned per STATEMENT (<see cref="SourceScan.Statements"/>), so a statement wrapped at <c>??</c> is still seen.
/// <b>Not covered:</b> a property or local as the stored value (no leading underscore), a fallback through a helper call
/// (<c>?? Initial(start)</c>), and block comments (the scan strips <c>//</c> only).</para>
/// <para><b>Maintenance procedure.</b> If this guard fires on your change: store the first window (the <c>??=</c> form above).
/// If the fallback is not a look-back (a false positive) or the pass must NOT store its first window in some mode, add an
/// <see cref="Allowlist"/> entry keyed on the file and the statement's exact text (whitespace collapsed), with the reason and
/// the ADR or issue that decided it, and say so in the PR. Each entry must match exactly one statement: a stale entry (its
/// statement gone or reworded) and a second identical statement in the same file both fail the guard.</para>
/// <para><b>Enforced by this test.</b> A must-fire control (the unstored forms: plain, parenthesised, wrapped at <c>??</c>,
/// another look-back name) and a must-not-fire control (the <c>??=</c> form, prose, a fallback that is not a subtraction, the
/// real tree with its allowlist).</para>
/// </remarks>
public class UnstoredFirstWindowGuardTests
{
    /// <summary>A field read falling back to "an operand minus an operand": the first window, computed and not stored.</summary>
    private static readonly Regex UnstoredFirstWindow = new(
        @"\b_\w+\s*\?\?\s*\(?\s*[\w.()]+\s*-\s*[\w.()]+",
        RegexOptions.Compiled);

    /// <summary>
    /// (Relative file, statement text with whitespace collapsed) where the unstored form is deliberate, each with its reason and
    /// the decision behind it. Each entry must match exactly one statement.
    /// </summary>
    private static readonly Dictionary<(string File, string Statement), string> Allowlist = new()
    {
        [("src/server/api/Sprk.Bff.Api/Services/Access/SecureChildReconciliationJob.cs",
            "since = _ordinaryMovesWatermark ?? startedAt - InitialRecentChangesLookback;")] =
            "#2735 (site 3; pending owner sign-off, not covered by option A): the ordinary-parent-move pass is report-only by "
            + "default and only a writing run moves its watermark, so a first window stored by a report-only run would never "
            + "move and the window would grow without bound. A writing run stores it (ExecuteAsync, before anything can fail). "
            + "Until a writing run has stored it, a report-only run looks back from its own start; after that it reads the held "
            + "watermark. A switch to writes restarts the catch-up sweep (task 194 F4-a), which walks every ordinary root. "
            + "Per-instance job state: ADR-036 (A1) and ADR-052 §5.",
    };

    [Fact(DisplayName = "#2735: no reconciliation pass under src/server computes its first window from a watermark field without storing it")]
    public void NoPassReadsAnUnstoredFirstWindow()
    {
        var hits = ScanTree();
        var violations = hits.Where(h => !Allowlist.ContainsKey((h.File, h.Statement))).ToList();

        Assert.True(
            violations.Count == 0,
            "A pass computes its first window as `_field ?? start - span` and never stores it. A first run that holds the "
            + "watermark then makes the next run look back from its OWN start, so the window slides past rows never listed "
            + "(#2709, #2735).\n\nREMEDY: `_field ??= start - InitialLookback; since = _field.Value;` (store it before anything "
            + "can fail). If the pass must not store it in some mode, or this is not a look-back, add an Allowlist entry with "
            + "the reason and the decision (see this file's maintenance procedure).\n\nOffending sites:\n  "
            + string.Join("\n  ", violations.Select(v => v.Site)));

        // Each entry must match exactly one statement: a stale entry would exempt the next statement written the same way, and a
        // second identical statement in the file would ride on the first one's exemption.
        var miscounted = Allowlist.Keys
            .Select(k => (Key: k, Count: hits.Count(h => h.File == k.File && h.Statement == k.Statement)))
            .Where(x => x.Count != 1)
            .ToList();
        Assert.True(miscounted.Count == 0,
            "Allowlist entries must each match exactly one statement: "
            + string.Join("; ", miscounted.Select(m => $"{m.Key.File} `{m.Key.Statement}` matched {m.Count}")));
    }

    [Fact(DisplayName = "#2735 must-fire control: the detector fires on an unstored first window, plain, parenthesised, wrapped at ?? or with another look-back name")]
    public void TheDetectorFiresOnTheUnstoredFirstWindow()
    {
        Assert.Single(ScanText("Fake/A.cs", """
            since = _recentChangesWatermark ?? startedAt - InitialRecentChangesLookback;
            """));

        // The External-team pass's form: parenthesised, then converted.
        Assert.Single(ScanText("Fake/B.cs", """
            since = _externalSince ?? (startedAt - ExternalInitialLookback).UtcDateTime;
            """));

        // A local `var`, another start variable, a look-back not named "Lookback".
        Assert.Single(ScanText("Fake/C.cs", """
            var since = _watermark ?? runStart - FirstWindow;
            """));

        // Wrapped at `??`, and wrapped before it.
        Assert.Single(ScanText("Fake/D.cs", """
            since = _watermark
                ?? startedAt - InitialLookback;
            """));
        Assert.Single(ScanText("Fake/E.cs", """
            since = _watermark ??
                startedAt.AddTicks(0) - Options.Value.InitialLookback;
            """));
    }

    [Fact(DisplayName = "#2735 must-not-fire control: the detector does not fire on the stored first window, on prose, on a non-subtraction fallback, or on the real tree")]
    public void TheDetectorDoesNotFireOnTheStoredFirstWindow()
    {
        // (1) The fix: the first window is stored, then read.
        Assert.Empty(ScanText("Fake/F.cs", """
            _directWatermark ??= startedAt - DirectInitialLookback;
            since = _directWatermark.Value;
            _externalSince ??= (startedAt - ExternalInitialLookback).UtcDateTime;
            """));

        // (2) Prose naming the old shape in a line comment.
        Assert.Empty(ScanText("Fake/G.cs", """
            // was: since = _watermark ?? startedAt - InitialLookback;
            var since = _watermark.Value;
            """));

        // (3) A fallback that is not a subtraction (a stored value, a constant) is not this defect.
        Assert.Empty(ScanText("Fake/H.cs", """
            var after = _cursor ?? SweepFromStart;
            var since = _watermark ?? fallback;
            """));

        // (4) THE REAL PRODUCTION TREE, with its allowlist: the control that matters.
        Assert.All(ScanTree(), h => Assert.True(Allowlist.ContainsKey((h.File, h.Statement)), h.Site));
    }

    // =================================================================================================================
    // MACHINERY: crude by design (arch-fitness scanning, not compilation); both controls pin the detector in each direction.
    // =================================================================================================================

    private static List<(string File, string Statement, string Site)> ScanTree()
        => SourceScan.ServerSourceFiles()
            .SelectMany(f => ScanText(SourceScan.Relative(f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();

    private static List<(string File, string Statement, string Site)> ScanText(string relativeFile, string rawText)
    {
        var hits = new List<(string, string, string)>();
        var lines = rawText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        foreach (var (statement, line) in SourceScan.Statements(lines))
        {
            var text = Regex.Replace(statement, @"\s+", " ").Trim();
            if (UnstoredFirstWindow.IsMatch(text))
            {
                hits.Add((relativeFile, text, $"{relativeFile}:{line}: {text}"));
            }
        }

        return hits;
    }
}
