// -----------------------------------------------------------------------------
// ProvisioningRunPhaseSucceededTests.cs
//
// T186 L37 — live, H3 parked run b6941254 in WaitingOnGate (admin consent); H5,
// dispatched earlier, succeeded next and wrote Running/H5 over it. The run then
// stalled: no gate to advance, and the reconciler's re-dispatch of H3 reused its
// MessageId. ProvisioningRun.MarkPhaseSucceeded keeps another handler's stop; the
// source scan keeps every handler on it (enforcement ladder: a guard, not prose).
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class ProvisioningRunPhaseSucceededTests
{
    private static ProvisioningRun Run(RunStatus status, string? currentPhase) =>
        new() { RunId = "run-1", CustomerId = "acme", Status = status, CurrentPhase = currentPhase };

    [Fact]
    public void ARunningRun_MovesToTheSucceededHandler()
    {
        var run = Run(RunStatus.Running, "H2a");

        run.MarkPhaseSucceeded("H5");

        run.Status.Should().Be(RunStatus.Running);
        run.CurrentPhase.Should().Be("H5");
    }

    [Theory]
    [InlineData(RunStatus.WaitingOnGate)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Quarantined)]
    public void ASiblingsSuccess_KeepsAnotherHandlersStop(RunStatus stop)
    {
        var run = Run(stop, "H3");

        run.MarkPhaseSucceeded("H5");

        run.Status.Should().Be(stop, "H5 finishing must not erase H3's gate or failure (T186 L37)");
        run.CurrentPhase.Should().Be("H3");
    }

    [Fact]
    public void TheHandlerThatOwnsTheGate_MovesTheRunOn()
    {
        var run = Run(RunStatus.WaitingOnGate, "H3");

        run.MarkPhaseSucceeded("H3");

        run.Status.Should().Be(RunStatus.Running);
        run.CurrentPhase.Should().Be("H3");
    }

    [Fact]
    public void ACancelledRun_IsNeverRevived()
    {
        var run = Run(RunStatus.Cancelled, "H5");

        run.MarkPhaseSucceeded("H5");

        run.Status.Should().Be(RunStatus.Cancelled);
    }

    // ---------- T186 L41: FailedHandlers ----------

    [Fact]
    public void ASecondFailure_TakesTheRun_ButKeepsTheFirstOnRecord()
    {
        var run = Run(RunStatus.Running, "H5");

        run.MarkPhaseFailed("H8", RunStatus.Failed, "[h8] first");
        run.MarkPhaseFailed("H6", RunStatus.Failed, "[h6] second");

        run.Status.Should().Be(RunStatus.Failed);
        run.CurrentPhase.Should().Be("H6");
        run.ErrorDetail.Should().Be("[h6] second");
        run.FailedHandlers.Should().Equal("H8", "H6");
    }

    [Fact]
    public void ARepeatedFailure_IsRecordedOnce()
    {
        var run = Run(RunStatus.Running, "H8");

        run.MarkPhaseFailed("H8", RunStatus.Failed, "a");
        run.MarkPhaseFailed("H8", RunStatus.Failed, "b");

        run.FailedHandlers.Should().Equal("H8");
    }

    [Fact]
    public void AnotherHandlersFailure_NeverDowngradesAQuarantine()
    {
        var run = Run(RunStatus.Quarantined, "H4");
        run.FailedHandlers.Add("H4");

        run.MarkPhaseFailed("H8", RunStatus.Failed, "[h8] resumable");

        run.Status.Should().Be(RunStatus.Quarantined, "quarantine is cleared explicitly, never by a sibling's failure");
        run.CurrentPhase.Should().Be("H4");
        run.FailedHandlers.Should().Equal("H4", "H8");
    }

    [Fact]
    public void ACancelledRun_StaysCancelled_WhenAHandlerFails()
    {
        var run = Run(RunStatus.Cancelled, "H5");

        run.MarkPhaseFailed("H8", RunStatus.Failed, "late");

        run.Status.Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public void Success_RemovesTheHandlerFromTheFailures()
    {
        var run = Run(RunStatus.Running, "H6");
        run.FailedHandlers.Add("H8");
        run.FailedHandlers.Add("H6");

        run.MarkPhaseSucceeded("H8");

        run.FailedHandlers.Should().Equal("H6");
    }

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.WaitingOnGate)]
    public void MarkPhaseFailed_RefusesANonFailureStatus(RunStatus status)
    {
        var run = Run(RunStatus.Running, "H5");

        var act = () => run.MarkPhaseFailed("H8", status, "x");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NoHandler_WritesFailedDirectly()
    {
        var handlers = Path.Combine(RepoRoot(), "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers");
        var direct = new Regex(@"\brun\.Status\s*=\s*RunStatus\.(Failed|Quarantined)\b");

        var offenders = Directory.EnumerateFiles(handlers, "*.cs", SearchOption.AllDirectories)
            .Where(f => direct.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(handlers, f))
            .ToList();

        offenders.Should().BeEmpty("a handler's failure goes through ProvisioningRun.MarkPhaseFailed (T186 L41)");
        direct.IsMatch("        run.Status = RunStatus.Failed;").Should().BeTrue("must-fire control");
        direct.IsMatch("        run.MarkPhaseFailed(HandlerIdentifier, RunStatus.Failed, detail);").Should().BeFalse("must-not-fire control");
    }

    [Fact]
    public void NoHandler_WritesRunningDirectly()
    {
        var handlers = Path.Combine(RepoRoot(), "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers");
        var direct = new Regex(@"\brun\.Status\s*=\s*RunStatus\.Running\b");

        var offenders = Directory.EnumerateFiles(handlers, "*.cs", SearchOption.AllDirectories)
            .Where(f => direct.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(handlers, f))
            .ToList();

        offenders.Should().BeEmpty("a handler's success goes through ProvisioningRun.MarkPhaseSucceeded (T186 L37)");
        direct.IsMatch("        run.Status = RunStatus.Running;").Should().BeTrue("must-fire control");
        direct.IsMatch("        run.MarkPhaseSucceeded(HandlerIdentifier);").Should().BeFalse("must-not-fire control");
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
