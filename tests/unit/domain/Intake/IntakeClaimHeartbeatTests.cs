using FluentAssertions;
using Sprk.Bff.Api.Services.Intake;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Intake;

/// <summary>
/// The intake run's claim heartbeat interval (spaarke-intake-pipeline-r1 task 013, review F1): how
/// <c>IntakePipeline:ClaimHeartbeatSeconds</c> is bounded so a live holder's claim never reaches the same-job takeover age.
/// </summary>
/// <remarks>Maintain-class (ADR-038): a heartbeat slower than a third of the takeover age lets a redelivery take a live run
/// over mid AI call (two refreshes may fail in a row); one faster than 5 s is a fenced Dataverse write in a tight loop.</remarks>
public sealed class IntakeClaimHeartbeatTests
{
    [Theory]
    [InlineData(0, 20)]   // absent from configuration binds as the default 20; a zero is treated the same
    [InlineData(-5, 20)]
    [InlineData(1, 5)]    // below the floor
    [InlineData(5, 5)]
    [InlineData(12, 12)]
    [InlineData(20, 20)]
    [InlineData(60, 20)]  // the takeover age itself: clamped to a third of it
    [InlineData(int.MaxValue, 20)]
    public void ClaimHeartbeat_ConfiguredSeconds_IsBoundedToFiveSecondsThroughAThirdOfTheTakeoverAge(int configured, int expectedSeconds)
    {
        var interval = IntakeRunStore.ClaimHeartbeat(configured);

        interval.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
        (interval * 3).Should().BeLessThanOrEqualTo(IntakeRunStore.OwnerTakeoverAge, "two missed beats still leave the claim live");
    }
}
