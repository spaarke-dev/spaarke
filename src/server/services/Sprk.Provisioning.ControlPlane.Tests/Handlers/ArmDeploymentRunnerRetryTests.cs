// -----------------------------------------------------------------------------
// ArmDeploymentRunnerRetryTests.cs
//
// HANDLER-06 (Wave 2 pre-dispatch remediation 2026-08-27) — F11 verbatim
// coverage. Tests the CogSvc RequestConflict retry-with-backoff helper
// on ArmDeploymentRunner. The retry helper is exercised directly with
// hand-crafted RequestFailedException fixtures + a captured no-op delay
// so tests do not wait real wall-clock time. The end-to-end
// ArmDeploymentRunner behavior (retries wrap the actual ARM deploy) is
// covered by ArmDeploymentRunnerTests.cs's SDK-through-fake-transport
// tests — this file is targeted at the retry rule itself.
// -----------------------------------------------------------------------------

using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmDeploymentRunnerRetryTests
{
    // ---------- IsCogSvcRequestConflict boundary tests ----------

    [Theory]
    [InlineData(409, "RequestConflict", true)]
    [InlineData(409, "RequestConflict_CogSvcSoftLock", true)]  // substring match
    [InlineData(409, "requestconflict", true)]                 // case-insensitive
    [InlineData(409, "AlreadyExists", false)]                  // 409 but wrong code
    [InlineData(500, "RequestConflict", false)]                // right code, wrong status
    [InlineData(429, "RequestConflict", false)]                // 429 not covered
    public void IsCogSvcRequestConflict_MatchesOnly409PlusRequestConflict(int status, string errorCode, bool expected)
    {
        var ex = new RequestFailedException(status, "boom", errorCode: errorCode, innerException: null);

        ArmDeploymentRunner.IsCogSvcRequestConflict(ex).Should().Be(expected);
    }

    [Fact]
    public void IsCogSvcRequestConflict_NullErrorCode_ReturnsFalse()
    {
        var ex = new RequestFailedException(409, "boom");

        ArmDeploymentRunner.IsCogSvcRequestConflict(ex).Should().BeFalse();
    }

    // ---------- Retry helper — success paths ----------

    [Fact]
    public async Task RetryHelper_FirstAttemptSucceeds_ReturnsResult_NoDelay()
    {
        var delayCalls = new List<TimeSpan>();

        var result = await ArmDeploymentRunner.RetryOnCogSvcRequestConflictAsync<int>(
            action: (attempt, ct) => Task.FromResult(42 + attempt),
            backoffs: ArmDeploymentRunner.DefaultCogSvcRetryBackoffs,
            delay: (ts, ct) => { delayCalls.Add(ts); return Task.CompletedTask; },
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None);

        result.Should().Be(43, "first attempt = 1 → 42 + 1 = 43");
        delayCalls.Should().BeEmpty("no delay when the first attempt succeeds");
    }

    [Fact]
    public async Task RetryHelper_TwoConflictsThenSuccess_UsesFirstTwoBackoffs()
    {
        var delayCalls = new List<TimeSpan>();
        var invocations = 0;

        var result = await ArmDeploymentRunner.RetryOnCogSvcRequestConflictAsync<string>(
            action: (attempt, ct) =>
            {
                invocations++;
                if (attempt < 3)
                {
                    throw new RequestFailedException(409, $"conflict attempt {attempt}",
                        errorCode: "RequestConflict", innerException: null);
                }
                return Task.FromResult("succeeded-on-attempt-3");
            },
            backoffs: ArmDeploymentRunner.DefaultCogSvcRetryBackoffs,
            delay: (ts, ct) => { delayCalls.Add(ts); return Task.CompletedTask; },
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None);

        result.Should().Be("succeeded-on-attempt-3");
        invocations.Should().Be(3);
        delayCalls.Should().Equal(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90));
    }

    // ---------- Retry helper — exhaustion path ----------

    [Fact]
    public async Task RetryHelper_AllAttemptsConflict_ThrowsCogSvcSoftLockPersistentException_AfterExhaustingBudget()
    {
        var delayCalls = new List<TimeSpan>();
        var invocations = 0;

        var act = async () => await ArmDeploymentRunner.RetryOnCogSvcRequestConflictAsync<int>(
            action: (attempt, ct) =>
            {
                invocations++;
                throw new RequestFailedException(409, $"persistent conflict {attempt}",
                    errorCode: "RequestConflict_CogSvcSoftLock", innerException: null);
            },
            backoffs: ArmDeploymentRunner.DefaultCogSvcRetryBackoffs,
            delay: (ts, ct) => { delayCalls.Add(ts); return Task.CompletedTask; },
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None);

        // F11 verbatim: 4 successive 409 → runner returns Failure(Resumable,
        // 'cogsvc-soft-lock-persistent', ...). Helper attempts 4 times total
        // (initial + 3 retries), waits between attempts (3 backoffs), then
        // throws.
        await act.Should().ThrowAsync<CogSvcSoftLockPersistentException>()
            .Where(ex => ex.AttemptsMade == 4);

        invocations.Should().Be(4, "initial attempt + 3 retries = 4 total attempts");
        delayCalls.Should().Equal(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(180));
    }

    // ---------- Retry helper — non-conflict exception propagates immediately ----------

    [Fact]
    public async Task RetryHelper_NonConflictException_PropagatesImmediately_NoRetry()
    {
        var delayCalls = new List<TimeSpan>();
        var invocations = 0;

        var act = async () => await ArmDeploymentRunner.RetryOnCogSvcRequestConflictAsync<int>(
            action: (attempt, ct) =>
            {
                invocations++;
                throw new RequestFailedException(400, "bad request — invalid template",
                    errorCode: "InvalidTemplate", innerException: null);
            },
            backoffs: ArmDeploymentRunner.DefaultCogSvcRetryBackoffs,
            delay: (ts, ct) => { delayCalls.Add(ts); return Task.CompletedTask; },
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();
        invocations.Should().Be(1, "non-CogSvc-conflict exceptions MUST NOT trigger retry");
        delayCalls.Should().BeEmpty();
    }

    // ---------- Default retry schedule shape ----------

    [Fact]
    public void DefaultCogSvcRetryBackoffs_MatchesF11VerbatimSchedule()
    {
        ArmDeploymentRunner.DefaultCogSvcRetryBackoffs.Should().Equal(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(180));
    }

    // ---------- Cancellation propagates ----------

    [Fact]
    public async Task RetryHelper_CancellationTokenTriggered_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();

        var act = async () => await ArmDeploymentRunner.RetryOnCogSvcRequestConflictAsync<int>(
            action: (attempt, ct) =>
            {
                cts.Cancel();
                throw new RequestFailedException(409, "conflict", errorCode: "RequestConflict", innerException: null);
            },
            backoffs: ArmDeploymentRunner.DefaultCogSvcRetryBackoffs,
            delay: (ts, ct) => Task.CompletedTask,
            logger: NullLogger.Instance,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
