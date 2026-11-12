using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 084e, ontology-r1 O-7: the ONE episode filter of a record rule (<see cref="ProcedureResolution.CurrentEpisodeVersions"/>), which the
/// resolver's <c>RecordRuleVersionOn</c> and the record read's <see cref="RecordProcedureReadService.CurrentEpisodeVersion"/> both use.
/// </summary>
public class RecordRuleEpisodeTests
{
    private static readonly Guid RecordRule = Guid.Parse("084e0000-0000-4000-8000-000000000001");
    private static readonly DateTime Boundary = new(2026, 2, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Date = new(2026, 3, 15);

    private static ResolvedPolicyVersion Version(int number, DateTime? created, DateOnly from) => new()
    {
        PolicyId = RecordRule,
        PolicyCode = "BRR-1",
        PolicyVersionId = Guid.Parse($"084e0000-0000-4000-8000-0000000001{number:x2}"),
        RuleSetId = Guid.Parse("084e0000-0000-4000-8000-000000000002"),
        VersionNumber = number,
        InForceFrom = from,
        CreatedOn = created,
    };

    private static ProcedureOverrideRow Exclude(DateTime? created) => new(
        Guid.Parse("084e0000-0000-4000-8000-000000000003"), ProcedureOverrideMode.Excluded,
        new CoreRecordLink("sprk_matter", Guid.Parse("084e0000-0000-4000-8000-000000000004"), 0), null, null,
        Guid.Parse("084e0000-0000-4000-8000-000000000005"), null, null, created);

    [Fact(DisplayName = "084e O-7 boundary: a version created at the Exclude row's own instant is in the episode ('on or after'), and the helper and the read service agree on it")]
    public void VersionCreatedAtTheBoundary_IsInTheEpisode_InBothCallers()
    {
        var older = Version(1, Boundary.AddTicks(-1), new DateOnly(2026, 2, 15));
        var atBoundary = Version(2, Boundary, new DateOnly(2026, 1, 15));
        var unstamped = Version(3, null, new DateOnly(2026, 1, 20));
        var all = new[] { older, atBoundary, unstamped };

        var episode = ProcedureResolution.CurrentEpisodeVersions(RecordRule, all, Exclude(Boundary));
        var shown = RecordProcedureReadService.CurrentEpisodeVersion(all, Date, RecordRule, Exclude(Boundary));

        episode.Should().Equal(atBoundary);
        shown.Should().BeSameAs(atBoundary, "the read service names the version the resolver counts, although an older episode's version is newer in force");
    }

    [Fact(DisplayName = "084e O-7: an Exclude row with no createdon has no boundary, so every version counts (both callers)")]
    public void NoBoundary_EveryVersionCounts()
    {
        var a = Version(1, null, new DateOnly(2026, 1, 15));
        var b = Version(2, Boundary, new DateOnly(2026, 2, 15));

        ProcedureResolution.CurrentEpisodeVersions(RecordRule, [a, b], Exclude(null)).Should().Equal(a, b);
        RecordProcedureReadService.CurrentEpisodeVersion([a, b], Date, RecordRule, Exclude(null)).Should().BeSameAs(b);
    }

    [Fact(DisplayName = "084e O-7 must-fire: a known boundary that no version reaches is the resolver's fault, in both callers")]
    public void KnownBoundary_NoVersionReachesIt_IsAFault()
    {
        var older = Version(1, Boundary.AddTicks(-1), new DateOnly(2026, 2, 15));

        var helper = () => ProcedureResolution.CurrentEpisodeVersions(RecordRule, [older], Exclude(Boundary));
        var read = () => RecordProcedureReadService.CurrentEpisodeVersion([older], Date, RecordRule, Exclude(Boundary));

        helper.Should().Throw<ProcedureResolutionFaultException>();
        read.Should().Throw<ProcedureResolutionFaultException>();
        ProcedureResolution.CurrentEpisodeVersions(RecordRule, [], Exclude(Boundary)).Should().BeEmpty("no versions at all is not a fault");
    }
}
