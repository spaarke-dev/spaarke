using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Live half of the task 165b-4 source-access guards (ontology-r1 O3 and O4; offline half: <c>SpendReaderSourceAccessGuardTests</c> in
/// the ArchTests). The spend readers decide only from server-written sources: the approve Decision Record (counted spend) and the
/// submission's recorded findings (the pending hold). Both are read by the identity the readers run as; if that identity cannot read
/// them, an approved invoice would silently drop out of counted spend, or a held one would be counted as pending.
/// </summary>
/// <remarks>
/// Opt-in like <see cref="SignalPredicateTests"/> (same fixture and environment variables): the identity checked is whatever WhoAmI
/// answers for the configured connection (the impersonated caller when <see cref="SignalPredicateTests.CallerIdEnvVar"/> is set). It
/// names no identity and no role, and it adds no runtime query: the readers themselves only fail loud (the O5 canary) when the
/// findings read back empty.
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class SpendReaderSourceAccessLiveTests : IClassFixture<SignalPredicateTests.LiveDataverse>
{
    private readonly SignalPredicateTests.LiveDataverse _dv;
    private readonly ITestOutputHelper _out;

    public SpendReaderSourceAccessLiveTests(SignalPredicateTests.LiveDataverse dv, ITestOutputHelper output)
    {
        _dv = dv;
        _out = output;
    }

    [SkippableFact]
    public async Task O3_TheReadersIdentity_ReadsDecisionRecords_AtGlobalDepth()
    {
        Skip.IfNot(_dv.IsLive, "Live Dataverse not configured (SIGNALS_LIVE_DATAVERSE_URL unset); a vacuous pass would hide that nothing was checked.");

        var principal = await _dv.WhoAmIAsync();
        (await _dv.GetReadDepthAsync(principal, InvoiceReviewReadService.DecisionRecordEntity)).Should().Be(PrivilegeDepth.Global,
            "counted spend needs the approve Decision Record of every invoice in scope; a narrower depth would drop approved invoices silently");
    }

    [SkippableFact]
    public async Task O4_TheReadersIdentity_IsCoveredByTheProfileThatReadsTheHoldSources()
    {
        Skip.IfNot(_dv.IsLive, "Live Dataverse not configured (SIGNALS_LIVE_DATAVERSE_URL unset); a vacuous pass would hide that nothing was checked.");

        var principal = await _dv.WhoAmIAsync();
        var direct = await _dv.RunFetchRowsAsync($"""
            <fetch top="1"><entity name="fieldsecurityprofile"><attribute name="fieldsecurityprofileid" />
              <filter><condition attribute="name" operator="eq" value="{ProfileName}" /></filter>
              <link-entity name="systemuserprofiles" from="fieldsecurityprofileid" to="fieldsecurityprofileid" intersect="true">
                <filter><condition attribute="systemuserid" operator="eq" value="{principal}" /></filter>
              </link-entity></entity></fetch>
            """);
        var throughTeam = await _dv.RunFetchRowsAsync($"""
            <fetch top="1"><entity name="fieldsecurityprofile"><attribute name="fieldsecurityprofileid" />
              <filter><condition attribute="name" operator="eq" value="{ProfileName}" /></filter>
              <link-entity name="teamprofiles" from="fieldsecurityprofileid" to="fieldsecurityprofileid" intersect="true">
                <link-entity name="teammembership" from="teamid" to="teamid" intersect="true">
                  <filter><condition attribute="systemuserid" operator="eq" value="{principal}" /></filter>
                </link-entity>
              </link-entity></entity></fetch>
            """);
        _out.WriteLine($"direct: {direct.Count}, through a team: {throughTeam.Count}");

        (direct.Count + throughTeam.Count).Should().BePositive(
            "the hold sources (InvoiceBlockedHold.HoldSourceColumns) are field-secured and read through this profile; without it a held invoice would read as not held");
        InvoiceBlockedHold.HoldSourceColumns.Should().NotBeEmpty();
    }

    /// <summary>The profile the offline guard checks (<c>Other/FieldSecurityProfiles.xml</c>) for read on every hold source.</summary>
    private const string ProfileName = "Spaarke BFF-Managed Field Writers";
}
