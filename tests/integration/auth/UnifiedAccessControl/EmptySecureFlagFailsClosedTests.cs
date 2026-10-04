using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 150, round 17 item 3 (2026-10-03, BINDING): every app-only reader of
/// <c>sprk_issecure</c> treats an EMPTY value exactly as <c>RecordContainerResolver</c> does — fail closed, never "not
/// secure".
/// </summary>
/// <remarks>
/// <para><b>Why empty means "unknown".</b> Since task 150 every row holds true or false (the one-time backfill
/// <c>scripts/Repair-SecureFlagNulls.ps1</c>; the column defaults to No) and the column is field-secured, so Dataverse
/// returns it EMPTY to an identity without field-level Read instead of refusing the read. An empty value is the BFF
/// having lost that Read — the true value was masked.</para>
///
/// <para><b>The readers.</b> The external-access readers share ONE flag reader,
/// <see cref="ExternalParticipationService.GetRootRecordFlagsAsync"/>, whose row mapping
/// <see cref="ExternalParticipationService.FlagsFrom"/> now answers <see cref="RootRecordFlags.Unreadable"/> for an empty
/// flag. Its consumers inherit it: the read-time evaluator (FR-22 Secure suppression of the standing-grant / derived /
/// org-expansion terms — <c>AccessibleRecordSetServiceTests</c>), the write-time grant policy (below), and the internal
/// user-share last-reader rule (<c>InternalUserShareTests.Unshare_WhenTheSecureFlagReadsEmpty_AppliesTheLastPersonRule</c>).
/// <c>ExternalDataService</c> only LABELS a project for the external SPA, so its fail-closed answer is to label it secure
/// (below). <c>SubjectStandingGrantReader</c> reads no <c>sprk_issecure</c>: its contribution is suppressed through the
/// shared reader. <c>RecordOwnershipResolver</c>'s flag read lives on task 146's branch, not this one — handed over at
/// integration (task 150 note §19).</para>
/// </remarks>
[Trait("Category", "Security")]
public class EmptySecureFlagFailsClosedTests
{
    private static readonly Guid Root = Guid.Parse("15000000-0000-0000-0000-000000000150");

    [Theory]
    [InlineData(null)]
    [InlineData(100000000)]   // Standard
    [InlineData(100000001)]   // Limited
    [InlineData(100000002)]   // Restricted
    public void TheSharedFlagReader_MapsAnEmptySecureFlagToUnreadable_WhateverTheAccessPermission(int? accessPermission)
    {
        var flags = ExternalParticipationService.FlagsFrom(isSecure: null, accessPermission, stateCode: 0);

        flags.Should().Be(RootRecordFlags.Unreadable, "an empty flag is unknown, and unknown is the fail-closed answer");
        flags.IsDirectOnly.Should().BeTrue("an unknown flag never opens the record to derived or org-expansion terms");
    }

    /// <summary>The control: a flag that holds a value is mapped as before, and is never unreadable.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheSharedFlagReader_MapsAStoredSecureFlagAsStored(bool stored, bool secure)
    {
        var flags = ExternalParticipationService.FlagsFrom(stored, accessPermission: null, stateCode: 0);

        flags.IsSecure.Should().Be(secure);
        flags.IsUnreadable.Should().BeFalse();
    }

    /// <summary>
    /// The write-time grant policy on a row whose flag came back EMPTY: refused as "could not be read" (503), for a
    /// contact and for an organization — never allowed as if the record were ordinary.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGrantPolicy_RefusesARowWhoseSecureFlagReadsEmpty_AsUnreadable(bool organizationGrant)
    {
        var flags = new Dictionary<Guid, RootRecordFlags>
        {
            [Root] = ExternalParticipationService.FlagsFrom(isSecure: null, accessPermission: null, stateCode: 0),
        };

        var decision = ExternalGrantLifecycle.DecideGrantPolicy(
            flags, Root, organizationGrant ? GrantGranteeKind.Organization : GrantGranteeKind.Contact);

        decision.IsAllowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(ExternalGrantLifecycle.PolicyUnreadableReasonCode);
        decision.StatusCode.Should().Be(503);
    }

    /// <summary>
    /// The external SPA's project label: an EMPTY flag is shown as secure, never as an ordinary project; a stored value
    /// is shown as stored.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheExternalProjectLabel_ShowsAnEmptySecureFlagAsSecure(bool? stored, bool shown)
    {
        var dto = ExternalDataService.MapProject(new ExternalDataService.ProjectRow
        {
            SprkProjectid = Root.ToString(),
            SprkName = "Acme",
            SprkIssecure = stored,
        });

        dto.SprkIssecure.Should().Be(shown);
    }
}
