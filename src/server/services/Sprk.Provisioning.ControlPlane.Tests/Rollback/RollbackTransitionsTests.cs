// -----------------------------------------------------------------------------
// RollbackTransitionsTests.cs
//
// L2 CONTROL-PLANE tests for the §4C rollback state-transition table (task 061).
//
// TESTED BEHAVIORS (POML acceptance criteria):
//   AC #1  FailureClass.Resumable            -> RunStatus.Failed        (no auto-retry)
//   AC #2  FailureClass.RetryableWithCleanup -> RunStatus.Failed        (auto-retry ON)
//   AC #3  FailureClass.QuarantineRequired   -> RunStatus.Quarantined   (no auto-retry, guard held)
//   AC #4  FailureClass.SuccessfulButDrifted -> RunStatus.Completed     (no auto-retry, guard released)
//   AC #5  All four classes covered by [Theory] — new-class-added-without-branch
//          fails compile at CS8524 (warning-as-error via TreatWarningsAsErrors).
//
// ADR-038 KEEP category: tests/unit/ — pure-function policy over static enum
// values (no I/O, no mocks). Runs in-process; no external dependency.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Rollback;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Rollback;

/// <summary>
/// Unit tests for <see cref="RollbackTransitions"/> — the pure-function §4C
/// state-transition policy.
/// </summary>
public sealed class RollbackTransitionsTests
{
    // -----------------------------------------------------------------------
    // AC #1..#4 — per-class state transition mapping
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(FailureClass.Resumable,            RunStatus.Failed)]
    [InlineData(FailureClass.RetryableWithCleanup, RunStatus.Failed)]
    [InlineData(FailureClass.QuarantineRequired,   RunStatus.Quarantined)]
    [InlineData(FailureClass.SuccessfulButDrifted, RunStatus.Completed)]
    public void MapToRunStatus_MatchesSection4CTable(FailureClass failureClass, RunStatus expected)
    {
        RollbackTransitions.MapToRunStatus(failureClass).Should().Be(expected,
            "§4C row for {0}", failureClass);
    }

    [Theory]
    [InlineData(FailureClass.Resumable,            false)]  // Operator resumes via POST /api/runs/{id}/resume.
    [InlineData(FailureClass.RetryableWithCleanup, true)]   // Auto-retry — handler idempotency owns cleanup.
    [InlineData(FailureClass.QuarantineRequired,   false)]  // Operator clears via POST /api/runs/{id}/clear-quarantine.
    [InlineData(FailureClass.SuccessfulButDrifted, false)]  // Operator re-runs affected phases via POST /api/runs/{id}/resume.
    public void ShouldReEnqueue_MatchesSection4CRetryPolicy(FailureClass failureClass, bool expected)
    {
        RollbackTransitions.ShouldReEnqueue(failureClass).Should().Be(expected,
            "§4C retry policy for {0}", failureClass);
    }

    [Theory]
    [InlineData(FailureClass.Resumable,            false)]  // Operator may still resume — keep guard.
    [InlineData(FailureClass.RetryableWithCleanup, false)]  // Auto-retry in flight — keep guard.
    [InlineData(FailureClass.QuarantineRequired,   false)]  // spec FR-24 SCOPE: BLOCK new runs until cleared.
    [InlineData(FailureClass.SuccessfulButDrifted, true)]   // Run is Completed — customer may start a new run.
    public void ShouldReleaseCustomerGuard_MatchesSpecFR24Scope(FailureClass failureClass, bool expected)
    {
        RollbackTransitions.ShouldReleaseCustomerGuard(failureClass).Should().Be(expected,
            "spec FR-24 guard policy for {0}", failureClass);
    }

    // -----------------------------------------------------------------------
    // AC #5 — exhaustive-switch coverage: every enum value has an explicit
    //         branch. A new value added to the enum without a branch here
    //         fails CS8524 (warning-as-error) at BUILD time. This test
    //         provides a runtime failsafe if the compile-time guard were ever
    //         relaxed.
    // -----------------------------------------------------------------------

    [Fact]
    public void MapToRunStatus_CoversEveryEnumValue()
    {
        var allClasses = Enum.GetValues<FailureClass>();
        allClasses.Should().HaveCount(4,
            "§4C defines exactly 4 classes; a new class requires paired updates in " +
            "design.md §4C + RollbackTransitions.cs + this test.");

        foreach (var cls in allClasses)
        {
            var act = () => RollbackTransitions.MapToRunStatus(cls);
            act.Should().NotThrow<UnreachableException>(
                "class {0} must have an explicit branch in the switch expression", cls);
        }
    }

    [Fact]
    public void ShouldReEnqueue_CoversEveryEnumValue()
    {
        foreach (var cls in Enum.GetValues<FailureClass>())
        {
            var act = () => RollbackTransitions.ShouldReEnqueue(cls);
            act.Should().NotThrow<UnreachableException>(
                "class {0} must have an explicit branch in the switch expression", cls);
        }
    }

    [Fact]
    public void ShouldReleaseCustomerGuard_CoversEveryEnumValue()
    {
        foreach (var cls in Enum.GetValues<FailureClass>())
        {
            var act = () => RollbackTransitions.ShouldReleaseCustomerGuard(cls);
            act.Should().NotThrow<UnreachableException>(
                "class {0} must have an explicit branch in the switch expression", cls);
        }
    }

    // -----------------------------------------------------------------------
    // Runtime guard — an undefined enum cast MUST throw UnreachableException
    // rather than silently mis-classifying. Verifies the `_ => throw` branch
    // is wired correctly.
    // -----------------------------------------------------------------------

    [Fact]
    public void MapToRunStatus_UndefinedEnumValue_ThrowsUnreachableException()
    {
        var undefined = (FailureClass)999;
        var act = () => RollbackTransitions.MapToRunStatus(undefined);
        act.Should().Throw<UnreachableException>()
            .WithMessage("*not handled in RollbackTransitions.MapToRunStatus*");
    }

    [Fact]
    public void ShouldReEnqueue_UndefinedEnumValue_ThrowsUnreachableException()
    {
        var undefined = (FailureClass)999;
        var act = () => RollbackTransitions.ShouldReEnqueue(undefined);
        act.Should().Throw<UnreachableException>()
            .WithMessage("*not handled in RollbackTransitions.ShouldReEnqueue*");
    }

    [Fact]
    public void ShouldReleaseCustomerGuard_UndefinedEnumValue_ThrowsUnreachableException()
    {
        var undefined = (FailureClass)999;
        var act = () => RollbackTransitions.ShouldReleaseCustomerGuard(undefined);
        act.Should().Throw<UnreachableException>()
            .WithMessage("*not handled in RollbackTransitions.ShouldReleaseCustomerGuard*");
    }
}
