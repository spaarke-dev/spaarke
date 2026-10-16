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
