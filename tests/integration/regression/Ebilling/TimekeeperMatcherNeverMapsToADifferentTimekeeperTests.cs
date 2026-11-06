using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Regression for the ebilling-r1 task 046 review finding F1-1 (FR-23, "never silently maps to a different timekeeper").
/// </summary>
/// <remarks>
/// Defect: stage 2 (normalized) ran over the eligible roster rows only, and the "known but not approved" check ran after it. A billed
/// "David Lee" whose roster row was Inactive, Pending or out of date was therefore matched to an Approved "David K. Lee" at confidence 1
/// (a middle name present on one side only is treated as missing). Fix: before stage 2, an ineligible roster row with exactly the billed
/// key ends the match as <see cref="TimekeeperUnmatchedReason.NotApprovedOnLineDate"/>.
/// <para>The same file pins the 078 prerequisite: when several roster rows share a name with non-overlapping periods (a renewed or
/// promoted timekeeper), the matcher picks the row eligible on the line date, and the stage-2 guard does not block a differently
/// spelled billed name that a still-eligible row matches.</para>
/// </remarks>
public sealed class TimekeeperMatcherNeverMapsToADifferentTimekeeperTests
{
    private static readonly Guid Firm = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Day = new(2026, 6, 15);

    private readonly Mock<IInvoiceAi> _ai = new();
    private int _embeddingCalls;

    public TimekeeperMatcherNeverMapsToADifferentTimekeeperTests()
    {
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, int?, CancellationToken>((_, _, _, _) =>
            {
                _embeddingCalls++;
                return Task.FromResult(new ReadOnlyMemory<float>([0f, 0f, 0f, 1f]));
            });
    }

    private static RosterEntry Person(
        string name,
        bool active = true,
        int status = (int)TimekeeperStatus.Approved,
        DateOnly? from = null,
        DateOnly? to = null) =>
        new(Guid.NewGuid(), Firm, name, active, status, from, to, 1);

    private async Task<TimekeeperResolution> MatchOne(string billed, IReadOnlyList<RosterEntry> roster, DateOnly? date = null)
    {
        var matcher = new TimekeeperMatcher(_ai.Object, Mock.Of<ILogger<TimekeeperMatcher>>());
        var results = await matcher.MatchAsync(
            Firm, roster, [], [new TimekeeperMatchRequest(billed, date ?? Day)], 0.90m);
        return results.Should().ContainSingle().Subject;
    }

    public enum Ineligible
    {
        InactiveStatus,
        Pending,
        RecordDeactivated,
        EndedBeforeTheLineDate,
        StartsAfterTheLineDate,
    }

    private static RosterEntry IneligibleDavidLee(Ineligible kind) => kind switch
    {
        Ineligible.InactiveStatus => Person("David Lee", status: (int)TimekeeperStatus.Inactive),
        Ineligible.Pending => Person("David Lee", status: (int)TimekeeperStatus.Pending),
        Ineligible.RecordDeactivated => Person("David Lee", active: false),
        Ineligible.EndedBeforeTheLineDate => Person("David Lee", to: new DateOnly(2026, 3, 31)),
        _ => Person("David Lee", from: new DateOnly(2026, 9, 1)),
    };

    [Theory]
    [InlineData(Ineligible.InactiveStatus)]
    [InlineData(Ineligible.Pending)]
    [InlineData(Ineligible.RecordDeactivated)]
    [InlineData(Ineligible.EndedBeforeTheLineDate)]
    [InlineData(Ineligible.StartsAfterTheLineDate)]
    public async Task Stage2_ABilledNameThatIsOnTheRosterButNotEligible_IsNotMappedToAnApprovedLongerName(Ineligible kind)
    {
        var approvedDavidKLee = Person("David K. Lee");

        var result = await MatchOne("David Lee", [IneligibleDavidLee(kind), approvedDavidKLee]);

        result.IsMatched.Should().BeFalse("the billed person is on the roster and not approved on the line date");
        result.TimekeeperId.Should().BeNull();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
        _embeddingCalls.Should().Be(0, "the AI stage must not hop from a known unapproved person to a look-alike");
    }

    [Fact]
    public async Task Stage2_WithNoRosterRowForTheBilledName_StillMatchesTheLongerName()
    {
        var approvedDavidKLee = Person("David K. Lee");

        var result = await MatchOne("David Lee", [approvedDavidKLee, Person("Someone Else")]);

        result.TimekeeperId.Should().Be(approvedDavidKLee.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.Normalized);
    }

    // ── 078 prerequisite: several rows share a name, with non-overlapping periods ────────────────────────────────

    [Fact]
    public async Task RowsSharingANameWithNonOverlappingPeriods_ResolveToTheRowEligibleOnTheLineDate()
    {
        var renewed2026 = Person("Jane Doe", from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 12, 31));
        var renewed2027 = Person("Jane Doe", from: new DateOnly(2027, 1, 1), to: new DateOnly(2027, 12, 31));
        var roster = new[] { renewed2026, renewed2027 };

        var in2026 = await MatchOne("Jane Doe", roster, new DateOnly(2026, 6, 15));
        var in2027 = await MatchOne("Jane Doe", roster, new DateOnly(2027, 3, 1));
        var in2028 = await MatchOne("Jane Doe", roster, new DateOnly(2028, 1, 1));

        in2026.TimekeeperId.Should().Be(renewed2026.Id);
        in2027.TimekeeperId.Should().Be(renewed2027.Id);
        in2028.IsMatched.Should().BeFalse();
        in2028.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task ARenewedRowThatIsStillPending_LeavesTheLineUnmatchedUntilPublish()
    {
        var ended2026 = Person("Jane Doe", from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 12, 31));
        var pending2027 = Person("Jane Doe", status: (int)TimekeeperStatus.Pending, from: new DateOnly(2027, 1, 1));

        var result = await MatchOne("Jane Doe", [ended2026, pending2027], new DateOnly(2027, 3, 1));

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task Stage2_ADifferentlySpelledBilledName_IsNotBlockedByAnEndedRowOfTheSamePerson()
    {
        var ended2026 = Person("Jane Doe", from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 12, 31));
        var renewed2027 = Person("Jane Doe", from: new DateOnly(2027, 1, 1));

        var result = await MatchOne("Doe, Jane", [ended2026, renewed2027], new DateOnly(2027, 3, 1));

        result.TimekeeperId.Should().Be(renewed2027.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.Normalized);
    }
}
