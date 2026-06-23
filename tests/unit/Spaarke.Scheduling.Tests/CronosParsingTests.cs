using Cronos;
using FluentAssertions;
using Xunit;

namespace Spaarke.Scheduling.Tests;

/// <summary>
/// Per FR-2.2: cron parsing is delegated to the Cronos NuGet. These tests pin the contract
/// the host relies on (5-field UTC, exclusive-of-input next-occurrence semantics) so a future
/// Cronos upgrade can't silently change scheduling behavior.
/// </summary>
public class CronosParsingTests
{
    [Fact]
    public void DailyAt2AM_ParsesAndComputesNextFire()
    {
        var cron = CronExpression.Parse("0 2 * * *");
        var from = new DateTime(2026, 6, 21, 1, 0, 0, DateTimeKind.Utc);

        var next = cron.GetNextOccurrence(from, TimeZoneInfo.Utc);

        next.Should().Be(new DateTime(2026, 6, 21, 2, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EveryFiveMinutes_ParsesAndComputesNextFire()
    {
        var cron = CronExpression.Parse("*/5 * * * *");
        var from = new DateTime(2026, 6, 21, 1, 7, 0, DateTimeKind.Utc);

        var next = cron.GetNextOccurrence(from, TimeZoneInfo.Utc);

        next.Should().Be(new DateTime(2026, 6, 21, 1, 10, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void InvalidCronExpression_ThrowsCronFormatException()
    {
        var act = () => CronExpression.Parse("not-a-cron");
        act.Should().Throw<CronFormatException>();
    }

    [Fact]
    public void DailyAt2AM_AfterFiringTime_RollsToNextDay()
    {
        var cron = CronExpression.Parse("0 2 * * *");
        var firing = new DateTime(2026, 6, 21, 2, 0, 0, DateTimeKind.Utc);

        // GetNextOccurrence(from, ...) is exclusive of the input — passing the firing time
        // returns the NEXT future occurrence. This matches ScheduledJobHost.AdvanceNextFire.
        var next = cron.GetNextOccurrence(firing, TimeZoneInfo.Utc);

        next.Should().Be(new DateTime(2026, 6, 22, 2, 0, 0, DateTimeKind.Utc));
    }
}
