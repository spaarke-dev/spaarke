using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="UiActionAckCoordinator"/> (D-F3 UI-action truthfulness /
/// FR-A1-08 / NFR-08 / task AIR2-037).
/// </summary>
/// <remarks>
/// <para>
/// Deterministic timing: rather than a real wall-clock wait (banned per
/// tests/CLAUDE.md — Stopwatch/Task.Delay in tests are a flakiness source), these tests
/// use two minimal <see cref="TimeProvider"/> subclasses that control exactly when the
/// coordinator's internal <c>Task.Delay(timeout, timeProvider, ct)</c> fires:
/// <see cref="NeverFiringTimeProvider"/> (the delay timer never calls back — used for the
/// Acknowledged path, where the test resolves the ack itself and must never race a real
/// timeout) and <see cref="InstantTimeProvider"/> (the delay timer's callback fires on the
/// next thread-pool turn — used for the TimedOut path, with no real elapsed-time wait).
/// </para>
/// <para>
/// The full <c>Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider</c> package is
/// NOT referenced by this test project; these two tiny local subclasses give the same
/// determinism guarantee for this coordinator's specific need (control WHEN the timer
/// callback fires) without adding a new package reference (bff-extensions.md §B).
/// </para>
/// </remarks>
public sealed class UiActionAckCoordinatorTests
{
    [Fact]
    public async Task WaitForAckAsync_WhenAckArrivesBeforeTimeout_ReturnsAcknowledged()
    {
        var coordinator = new UiActionAckCoordinator(
            NullLogger<UiActionAckCoordinator>.Instance, new NeverFiringTimeProvider());

        // Start the wait — TryAdd runs synchronously before the first await, so the pending
        // waiter is registered by the time this line returns control here.
        var waitTask = coordinator.WaitForAckAsync(
            "session-1", "frame-1", TimeSpan.FromSeconds(30), CancellationToken.None);

        var acknowledged = coordinator.TryAcknowledge("session-1", "frame-1");

        acknowledged.Should().BeTrue(because: "a pending waiter existed for this exact (sessionId, frameId)");
        (await waitTask).Should().Be(UiActionAckOutcome.Acknowledged);
    }

    [Fact]
    public async Task WaitForAckAsync_WhenNoAckArrives_ReturnsTimedOut()
    {
        var coordinator = new UiActionAckCoordinator(
            NullLogger<UiActionAckCoordinator>.Instance, new InstantTimeProvider());

        var outcome = await coordinator.WaitForAckAsync(
            "session-2", "frame-2", TimeSpan.FromMilliseconds(1), CancellationToken.None);

        outcome.Should().Be(UiActionAckOutcome.TimedOut,
            because: "FR-A1-08: an un-acked UI-affecting tool call must fail honestly, never fabricate success");
    }

    [Fact]
    public async Task WaitForAckAsync_KeyedByBothSessionIdAndFrameId_WrongSessionOrFrameDoesNotResolveIt()
    {
        var coordinator = new UiActionAckCoordinator(
            NullLogger<UiActionAckCoordinator>.Instance, new InstantTimeProvider());

        var waitTask = coordinator.WaitForAckAsync(
            "session-A", "frame-A", TimeSpan.FromMilliseconds(1), CancellationToken.None);

        // Neither of these references the exact (sessionId, frameId) pair — they must be
        // benign no-ops, not accidental cross-resolutions of an unrelated tool call's wait.
        coordinator.TryAcknowledge("session-B", "frame-A").Should().BeFalse();
        coordinator.TryAcknowledge("session-A", "frame-B").Should().BeFalse();

        (await waitTask).Should().Be(UiActionAckOutcome.TimedOut,
            because: "an ack for a DIFFERENT session or frame id must never resolve this wait");
    }

    [Fact]
    public async Task WaitForAckAsync_DuplicateRegistration_FailsTheDuplicateImmediately_WithoutWaitingOutTheTimeout()
    {
        // NeverFiringTimeProvider proves this: if the duplicate call fell through to the
        // normal wait path (awaiting its own orphaned tcs against a delay that never fires),
        // this test would hang. It must short-circuit instead.
        var coordinator = new UiActionAckCoordinator(
            NullLogger<UiActionAckCoordinator>.Instance, new NeverFiringTimeProvider());

        var firstWait = coordinator.WaitForAckAsync(
            "session-dup", "frame-dup", TimeSpan.FromMinutes(5), CancellationToken.None);

        // Second registration for the SAME (sessionId, frameId) — the coordinator must fail
        // this one immediately rather than await a wait that can never be acked (TryAcknowledge
        // would only ever resolve the FIRST registration's tcs).
        var secondOutcome = await coordinator.WaitForAckAsync(
            "session-dup", "frame-dup", TimeSpan.FromMinutes(5), CancellationToken.None);

        secondOutcome.Should().Be(UiActionAckOutcome.TimedOut);

        // The first registration is unaffected — it can still be acked normally.
        coordinator.TryAcknowledge("session-dup", "frame-dup").Should().BeTrue();
        (await firstWait).Should().Be(UiActionAckOutcome.Acknowledged);
    }

    [Fact]
    public void TryAcknowledge_WhenNoPendingWaiterExists_ReturnsFalse()
    {
        // No WaitForAckAsync was ever registered for this (sessionId, frameId) — e.g. a late
        // ack after the timeout already fired, a duplicate ack, or an unknown frame id. This
        // must be a benign false, never a throw (the ack endpoint always returns 200).
        var coordinator = new UiActionAckCoordinator(
            NullLogger<UiActionAckCoordinator>.Instance, TimeProvider.System);

        coordinator.TryAcknowledge("session-unknown", "frame-unknown").Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Deterministic timer control (see class remarks)
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class NeverFiringTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new NoOpTimer();

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class InstantTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new InstantTimer();
        }

        private sealed class InstantTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
