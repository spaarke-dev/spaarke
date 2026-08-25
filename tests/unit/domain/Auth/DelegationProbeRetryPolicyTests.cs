using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Auth;

/// <summary>
/// The "record not found" backoff inside <see cref="CallerRecordAccessProbe"/>
/// (unified-access-control-r2, task 008 follow-up, owner-authorised 2026-08-23).
///
/// <para><b>What it protects.</b> The Create Project wizard calls <c>/provision-project</c> within
/// seconds of creating the project row, so the delegation check can ask Dataverse about a record that
/// has not replicated yet. Without a bounded retry, secure-project creation fails intermittently with a
/// 403 that looks like a permissions problem and is not one.</para>
///
/// <para><b>Why the schedule is a separate pure function.</b> It is the only timing logic in the probe,
/// and it sits inside a method that speaks HTTP — which cannot be tested without a transport mock
/// (ADR-038 §7 ban B1). Extracting the decision gives a real seam at the layer ADR-038 calls domain
/// logic: no I/O, no DI, no clock.</para>
/// </summary>
public class DelegationProbeRetryPolicyTests
{
    [Fact]
    public void NotFoundRetryDelay_ForTheFirstNotFound_RetriesQuickly()
    {
        CallerRecordAccessProbe.NotFoundRetryDelay(1)
            .Should().Be(TimeSpan.FromMilliseconds(400),
                "replication lag is usually sub-second, so the first re-ask should be cheap");
    }

    [Fact]
    public void NotFoundRetryDelay_ForTheSecondNotFound_BacksOff()
    {
        CallerRecordAccessProbe.NotFoundRetryDelay(2)
            .Should().BeGreaterThan(CallerRecordAccessProbe.NotFoundRetryDelay(1)!.Value,
                "a second miss suggests a longer lag; re-asking at the same interval adds latency without adding chances");
    }

    /// <summary>
    /// The retry MUST terminate. An unbounded schedule on an authorization path would turn a caller who
    /// genuinely cannot see a record into a hung request — and under OBO that is the common case, since
    /// Dataverse reports "cannot see it" and "does not exist" identically.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(50)]
    public void NotFoundRetryDelay_AfterTheBoundedAttempts_StopsRetrying(int attemptsMade)
    {
        CallerRecordAccessProbe.NotFoundRetryDelay(attemptsMade)
            .Should().BeNull("the schedule must terminate — an authorization check may not hang");
    }

    /// <summary>
    /// Caps the total added latency. This is the number that has to stay defensible: it is paid by every
    /// genuine no-access denial on the six external-access mutations and the Office save gate, so a
    /// well-meaning "just one more retry" is a real regression, not a tuning change.
    /// </summary>
    [Fact]
    public void NotFoundRetryDelay_TotalAddedLatency_StaysUnderTwoSeconds()
    {
        var total = TimeSpan.Zero;
        for (var attempt = 1; CallerRecordAccessProbe.NotFoundRetryDelay(attempt) is { } delay; attempt++)
        {
            total += delay;
            attempt.Should().BeLessThan(10, "guard against an accidentally unbounded schedule");
        }

        total.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "this delay lands on genuine denials too, because Dataverse cannot distinguish " +
            "'not replicated yet' from 'you cannot see this record'");
    }
}
