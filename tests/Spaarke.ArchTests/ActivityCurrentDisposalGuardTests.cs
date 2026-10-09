using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Task 093 (spaarkeai-word-add-in-r1, GitHub #1084 follow-on) — structural fitness function: no
/// method under <c>src/server/**</c> may dispose <see cref="System.Diagnostics.Activity.Current"/>.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> Twenty call sites across <c>Infrastructure/Graph</c> read the AMBIENT
/// request Activity to attach tags — <c>using var activity = Activity.Current; activity?.SetTag(...);</c>
/// — and the <c>using</c> disposed it the moment the method returned. <c>Activity.Current</c> is the
/// CALLER's request span (set by ASP.NET Core's request-tracing middleware, or by the OBO/app-only
/// pipeline above it), not a span this method started. Disposing someone else's span ends their request
/// early: it is exported with <c>resultCode 0</c>, <c>success false</c>, and a duration truncated to
/// whatever had elapsed when the Graph call returned — <c>UploadSessionManager.UploadSmallAsync</c>
/// alone turned a real 3-second, successful Word save into a 1.4-second "failure" in App Insights
/// (notes/093-job-row-and-telemetry.md §2). Every Graph-touching route's reported status code and
/// duration was unreliable for exactly this reason.</para>
///
/// <para><b>The fix, and why this shape over the alternative.</b> The task's own constraint named two
/// legitimate fixes: drop <c>using</c> (tags land on the current span, as before, minus the early stop),
/// or start a real child span from an <c>ActivitySource</c>. This guard enforces the FIRST — the twenty
/// sites only ever wanted to tag the ambient span, never to create a new one, and a child span would
/// change what each site's telemetry looks like (new span ids, a different parent/duration shape) for no
/// behavioural need. The rule this guard enforces is therefore simply: <b>never dispose
/// <c>Activity.Current</c></b>. A future site that genuinely wants its OWN child span should use
/// <c>ActivitySource.StartActivity(...)</c>, which this guard does not touch (the pattern it matches is
/// anchored on <c>= Activity.Current</c> specifically, not on <c>using var activity =</c> in general).</para>
///
/// <para><b>Why a source scan and not (only) a behavioural test.</b> The defect is invisible to a test
/// that calls one Graph method in isolation — the method's OWN return value and OWN logging are
/// unaffected; only the CALLER's ambient Activity, which the method under test does not own and the test
/// does not usually assert on, is damaged. Twenty sites, each one line, each a silent regression if
/// `using` creeps back in during a refactor. One structural rule covers all twenty and every site added
/// later. The companion behavioural test
/// (<c>UploadSessionManagerActivityLifetimeTests.UploadSmallAsync_DoesNotStopTheCallersRequestActivity</c>,
/// <c>tests/unit/domain/Office/</c>) is the one that CAN observe the caller-visible symptom directly, with
/// an <c>ActivityListener</c>; the two are a deliberate division of labour, not duplication.</para>
/// </remarks>
public class ActivityCurrentDisposalGuardTests
{
    /// <summary>
    /// A <c>using</c> declaration or statement whose initializer is <c>Activity.Current</c>, in either
    /// form (<c>using var x = Activity.Current;</c> or <c>using (var x = Activity.Current) { ... }</c>).
    /// Anchored on <c>Activity.Current</c> specifically — a <c>using</c> around a REAL owned span
    /// (<c>ActivitySource.StartActivity(...)</c>) is legitimate and must not trip this rule.
    /// </summary>
    private static readonly Regex DisposesAmbientActivity = new(
        @"using\s*\(?\s*var\s+\w+\s*=\s*Activity\.Current\b",
        RegexOptions.Compiled);

    [Fact(DisplayName = "No method under src/server disposes the ambient Activity.Current")]
    public void NoMethodDisposesActivityCurrent()
    {
        var violations = ScanTree();

        Assert.True(
            violations.Count == 0,
            "Activity.Current (the CALLER's request span) must never be disposed by a method that did not "
            + "start it. `using var activity = Activity.Current;` ends the request's own trace span the "
            + "moment this method returns — the request is then exported with resultCode 0, success=false, "
            + "and a truncated duration, while actually succeeding. This is the #1084 follow-on defect "
            + "(notes/093-job-row-and-telemetry.md §2): 20 sites in Infrastructure/Graph did this, and every "
            + "Graph-touching route's reported status/duration was unreliable as a result.\n\n"
            + "REMEDY — drop the `using` (keep `var activity = Activity.Current;` with no disposal; tags "
            + "still land on the current span exactly as before). If the call site genuinely needs its OWN "
            + "span, start one from an ActivitySource instead of aliasing the ambient one — do not `using` "
            + "an Activity this code did not create.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));

        // Non-vacuity: the fixed shape (`var activity = Activity.Current;`, no `using`) is still at >= 14
        // sites (20 after the 2026-10-03 fix; 15 since uac-r2 task 171 gave each SPE byte operation ONE shared core and
        // deleted four uncalled OBO byte methods; 14 since customer-provisioning-orchestration-r1 task 227d removed the
        // app-only, type-wide ContainerOperations.ListContainersAsync). A scanner that found 0 total usages of the pattern
        // would make the assertion above vacuously true rather than a real check that it is reading the tree.
        var sanctionedShapeCount = CountSanctionedShape();
        Assert.True(
            sanctionedShapeCount >= 14,
            $"Only found {sanctionedShapeCount} site(s) reading Activity.Current into a local without "
            + "`using`. There were 14 after uac-r2 task 171 and provisioning task 227d (9 in DriveItemOperations.cs, 3 in "
            + "ContainerOperations.cs, 1 in UploadSessionManager.cs, 1 elsewhere). A count well below that means this "
            + "scanner's own pattern has drifted from the tree — check before trusting a pass.");
    }

    [Fact(DisplayName = "Negative control: the detector fires on both using-var and using-statement forms over Activity.Current")]
    public void Detector_NegativeControl_FiresOnBothUsingForms()
    {
        Assert.NotEmpty(ScanText("Infrastructure/Graph/Fake/A.cs", """
            public async Task DoThingAsync()
            {
                using var activity = Activity.Current;
                activity?.SetTag("operation", "DoThing");
            }
            """));

        Assert.NotEmpty(ScanText("Infrastructure/Graph/Fake/B.cs", """
            public async Task DoThingAsync()
            {
                using (var activity = Activity.Current)
                {
                    activity?.SetTag("operation", "DoThing");
                }
            }
            """));

        // A different local name must not hide it.
        Assert.NotEmpty(ScanText("Infrastructure/Graph/Fake/C.cs", """
            using var span = Activity.Current;
            span?.SetTag("x", "y");
            """));
    }

    [Fact(DisplayName = "Positive control: the detector does not fire on the fixed shape, on a real owned child span, on prose, or on the real tree")]
    public void Detector_PositiveControl_DoesNotFireOnTheSanctionedShapes()
    {
        // (1) The fix: no `using`, tags still go on the ambient span.
        Assert.Empty(ScanText("Infrastructure/Graph/Fake/D.cs", """
            public async Task DoThingAsync()
            {
                var activity = Activity.Current;
                activity?.SetTag("operation", "DoThing");
            }
            """));

        // (2) A genuinely OWNED child span from an ActivitySource — disposing THIS is correct, because
        //     this method started it. Must not be confused with disposing the ambient one.
        Assert.Empty(ScanText("Infrastructure/Graph/Fake/E.cs", """
            public async Task DoThingAsync()
            {
                using var activity = MySource.StartActivity("DoThing");
                activity?.SetTag("operation", "DoThing");
            }
            """));

        // (3) Prose must not count — a `//` comment explaining the old defect names the exact banned
        //     shape. (SourceScan.StripLineComment strips `//` only, matching every other guard in this
        //     file set, so the control uses that comment style — not block comments.)
        Assert.Empty(ScanText("Infrastructure/Graph/Fake/F.cs", """
            // This used to be: using var activity = Activity.Current;
            // Historically: using (var activity = Activity.Current) { ... }
            var activity = Activity.Current;
            activity?.SetTag("operation", "DoThing");
            """));

        // (4) THE REAL PRODUCTION TREE — the control that actually matters.
        Assert.Empty(ScanTree());
    }

    // =================================================================================================
    // MACHINERY — crude by design (arch-fitness scanning, not compilation); adequate because both
    // controls above pin the detector's behaviour in each direction, including against the real tree.
    // =================================================================================================

    private static List<string> ScanTree()
        => SourceScan.ServerSourceFiles()
            .SelectMany(f => ScanText(SourceScan.Relative(f), File.ReadAllText(f)))
            .ToList();

    private static List<string> ScanText(string relativeFile, string rawText)
    {
        var violations = new List<string>();
        var lines = rawText.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            if (DisposesAmbientActivity.IsMatch(code))
            {
                violations.Add($"{relativeFile}:{i + 1}: {code.Trim()}");
            }
        }

        return violations;
    }

    /// <summary>A local assigned from <c>Activity.Current</c> — checked for the ABSENCE of <c>using</c>
    /// separately (<see cref="CountSanctionedShape"/>), rather than via a lookbehind, to keep the pattern
    /// simple and obviously correct.</summary>
    private static readonly Regex AssignsAmbientActivity = new(
        @"\bvar\s+\w+\s*=\s*Activity\.Current\b",
        RegexOptions.Compiled);

    /// <summary>Sites assigning <c>Activity.Current</c> to a local with NO <c>using</c> anywhere on the
    /// same (comment-stripped) line — the fixed, sanctioned shape.</summary>
    private static int CountSanctionedShape()
    {
        var count = 0;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            foreach (var rawLine in File.ReadAllLines(file))
            {
                var code = SourceScan.StripLineComment(rawLine);
                if (AssignsAmbientActivity.IsMatch(code)
                    && !code.Contains("using", StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
