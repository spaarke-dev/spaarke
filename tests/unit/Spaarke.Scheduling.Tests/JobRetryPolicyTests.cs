using FluentAssertions;
using Spaarke.Scheduling;
using Xunit;

namespace Spaarke.Scheduling.Tests;

public class JobRetryPolicyTests
{
    [Fact]
    public void Defaults_Match3Attempts_5sBase_2minCap()
    {
        var policy = new JobRetryPolicy();
        policy.MaxAttempts.Should().Be(3);
        policy.BaseDelay.Should().Be(TimeSpan.FromSeconds(5));
        policy.MaxDelay.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ComputeDelay_Attempt1_IsZero()
    {
        // Attempt 1 is the initial call; no prior sleep.
        new JobRetryPolicy().ComputeDelay(1).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ComputeDelay_Attempt2_EqualsBaseDelay()
    {
        new JobRetryPolicy { BaseDelay = TimeSpan.FromSeconds(5) }
            .ComputeDelay(2).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ComputeDelay_Attempt3_EqualsBaseDelayTimes2()
    {
        new JobRetryPolicy { BaseDelay = TimeSpan.FromSeconds(5) }
            .ComputeDelay(3).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ComputeDelay_Attempt5_EqualsBaseDelayTimes8()
    {
        // attempt 5 -> exponent 3 -> 2^3 = 8
        new JobRetryPolicy { BaseDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromHours(1) }
            .ComputeDelay(5).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void ComputeDelay_CapsAtMaxDelay()
    {
        var policy = new JobRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(30)
        };
        // attempt 2 = 10s; attempt 3 = 20s; attempt 4 = 40s -> capped to 30s; attempt 5 = 80s -> capped to 30s
        policy.ComputeDelay(2).Should().Be(TimeSpan.FromSeconds(10));
        policy.ComputeDelay(3).Should().Be(TimeSpan.FromSeconds(20));
        policy.ComputeDelay(4).Should().Be(TimeSpan.FromSeconds(30));
        policy.ComputeDelay(5).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ComputeDelay_ExtremeAttemptNumber_DoesNotOverflow_CapsAtMaxDelay()
    {
        // 2^60 would overflow TimeSpan.Ticks if computed naively. Verify cap takes effect.
        var policy = new JobRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromMinutes(2)
        };
        policy.ComputeDelay(100).Should().Be(TimeSpan.FromMinutes(2));
        policy.ComputeDelay(1000).Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ComputeDelay_AttemptZero_Throws()
    {
        Action act = () => new JobRetryPolicy().ComputeDelay(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
