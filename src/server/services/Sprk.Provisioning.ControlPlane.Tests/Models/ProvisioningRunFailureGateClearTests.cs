// -----------------------------------------------------------------------------
// ProvisioningRunFailureGateClearTests.cs
//
// ISS-035 (#2986) — a handler's failure writes a Pending marker gate
// ("{handler}-{rejectionCode}"); a later SUCCESS of that handler never cleared it,
// so ACME run 3b6f7b65 ended Completed with five Pending gates and H13 still in
// failedHandlers. ProvisioningRun.MarkPhaseSucceeded now owns the transition: the
// handler's own tagged markers (GateEntry.FailureOfHandler) go Pending -> Cleared and
// the handler leaves FailedHandlers. Real operator gates carry no tag and never move.
// The source scan keeps every handler failure path on ProvisioningRun.RecordFailureGate
// (enforcement ladder: a guard, not prose).
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class ProvisioningRunFailureGateClearTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-23T10:00:00+00:00");

    private static ProvisioningRun Run() =>
        new() { RunId = "run-1", CustomerId = "acme", Status = RunStatus.Running, CurrentPhase = "H2a" };

    private static void Fail(ProvisioningRun run, string handlerId, string gateKey, RunStatus status = RunStatus.Failed)
    {
        run.MarkPhaseFailed(handlerId, status, "[code] boom");
        run.RecordFailureGate(gateKey, handlerId);
    }

    [Fact]
    public void FailureThenSuccess_ClearsTheHandlersMarkers_AndTakesItOutOfFailedHandlers()
    {
        var run = Run();
        Fail(run, "H6", "h6-import-timeout");
        run.RecordFailureGate("h6-solution-import-failed", "H6");
        run.FailedHandlers.Should().Contain("H6");
        run.GateStates["h6-import-timeout"].Status.Should().Be(GateState.Pending);

        run.MarkPhaseSucceeded("H6", At);

        foreach (var key in new[] { "h6-import-timeout", "h6-solution-import-failed" })
        {
            var gate = run.GateStates[key];
            gate.Status.Should().Be(GateState.Cleared, "the marker stays as history but is no longer owed");
            gate.VerifierHandler.Should().Be("system:handler-succeeded");
            gate.VerifiedAt.Should().Be(At);
            gate.FailureOfHandler.Should().Be("H6", "the marker still says whose failure it records");
        }
        run.FailedHandlers.Should().NotContain("H6");
    }

    [Fact]
    public void ASuccessOfHandlerA_DoesNotTouchHandlerBsPendingMarker()
    {
        var run = Run();
        Fail(run, "H2b", "h2b-index-provisioning-failed");
        Fail(run, "H6", "h6-import-timeout");

        run.MarkPhaseSucceeded("H6", At);

        run.GateStates["h6-import-timeout"].Status.Should().Be(GateState.Cleared);
        var other = run.GateStates["h2b-index-provisioning-failed"];
        other.Status.Should().Be(GateState.Pending);
        other.VerifierHandler.Should().Be("H2b");
        other.VerifiedAt.Should().BeNull();
        run.FailedHandlers.Should().Contain("H2b").And.NotContain("H6");
    }

    [Fact]
    public void ARealManualGate_StaysPending_EvenWhenItsKeyAndVerifierLookLikeTheSucceedingHandlers()
    {
        var run = Run();
        // Real gates the handlers write while still succeeding / waiting: same key shape ("h9-..."), same verifier.
        run.GateStates["h9-nfr01-publish-size"] = new GateEntry { Status = GateState.Pending, VerifierHandler = "H9" };
        run.GateStates["admin-consent"] = new GateEntry { Status = GateState.Pending, VerifierHandler = "H3" };
        run.GateStates["h7b-plan-approval"] = new GateEntry { Status = GateState.Pending, VerifierHandler = "H7b" };

        run.MarkPhaseSucceeded("H9", At);
        run.MarkPhaseSucceeded("H3", At);
        run.MarkPhaseSucceeded("H7b", At);

        run.GateStates.Values.Should().OnlyContain(g => g.Status == GateState.Pending && g.VerifiedAt == null);
        run.GateStates["h9-nfr01-publish-size"].VerifierHandler.Should().Be("H9");
    }

    [Fact]
    public void AHandlerThatFailsAgain_StaysFailed_AndItsMarkerIsPendingAgain()
    {
        var run = Run();
        Fail(run, "H9", "h9-slot-swap-infra-fault");
        run.MarkPhaseSucceeded("H9", At);
        run.GateStates["h9-slot-swap-infra-fault"].Status.Should().Be(GateState.Cleared);

        Fail(run, "H9", "h9-slot-swap-infra-fault");

        run.GateStates["h9-slot-swap-infra-fault"].Status.Should().Be(GateState.Pending, "a new failure re-opens the marker");
        run.GateStates["h9-slot-swap-infra-fault"].VerifiedAt.Should().BeNull();
        run.FailedHandlers.Should().Contain("H9");
        run.Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public void AHandlerThatHasNotSucceeded_KeepsItsMarkersPending_WhileASiblingSucceeds()
    {
        var run = Run();
        Fail(run, "H9", "h9-slot-swap-infra-fault");

        run.MarkPhaseSucceeded("H8", At);

        run.GateStates["h9-slot-swap-infra-fault"].Status.Should().Be(GateState.Pending);
        run.FailedHandlers.Should().Contain("H9");
    }

    [Fact]
    public void AnAlreadyVerifiedOrClearedGate_IsNotRewritten()
    {
        var run = Run();
        run.RecordFailureGate("h6-import-timeout", "H6");
        run.MarkPhaseSucceeded("H6", At);
        var first = run.GateStates["h6-import-timeout"].VerifiedAt;

        run.MarkPhaseSucceeded("H6", At.AddHours(1));

        run.GateStates["h6-import-timeout"].VerifiedAt.Should().Be(first, "the first clearing time is the history");
    }

    [Fact]
    public void ThePhaseSucceededDefault_StampsTheCurrentTime()
    {
        var run = Run();
        run.RecordFailureGate("h6-import-timeout", "H6");
        var before = DateTimeOffset.UtcNow;

        run.MarkPhaseSucceeded("H6");

        run.GateStates["h6-import-timeout"].VerifiedAt.Should().BeOnOrAfter(before);
    }

    // ---------- enforcement: every handler failure path uses RecordFailureGate ----------

    private static readonly Regex DirectFailureMarkerWrite = new(@"GateStates\[\s*\$""[^""]*\{rejectionCode\}""\s*\]\s*=", RegexOptions.Compiled);

    [Fact]
    public void NoHandler_WritesAFailureMarkerGateDirectly()
    {
        var handlers = Path.Combine(RepoRoot(), "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers");

        var offenders = Directory.EnumerateFiles(handlers, "*.cs", SearchOption.AllDirectories)
            .Where(f => DirectFailureMarkerWrite.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(handlers, f))
            .ToList();

        offenders.Should().BeEmpty("a handler's failure marker goes through ProvisioningRun.RecordFailureGate so a later success can clear it (ISS-035)");
        DirectFailureMarkerWrite.IsMatch("        run.GateStates[$\"h9-{rejectionCode}\"] = new GateEntry").Should().BeTrue("must-fire control");
        DirectFailureMarkerWrite.IsMatch("        run.RecordFailureGate($\"h9-{rejectionCode}\", HandlerIdentifier);").Should().BeFalse("must-not-fire control");
        DirectFailureMarkerWrite.IsMatch("        run.GateStates[H10Gates.AppUserCreated] = new GateEntry").Should().BeFalse("must-not-fire control: a real gate");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
