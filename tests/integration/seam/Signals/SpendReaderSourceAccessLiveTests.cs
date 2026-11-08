using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Live half of the task 165b-4 source-access guards (ontology-r1 O3 and O4; offline half: <c>SpendReaderSourceAccessGuardTests</c> in
/// the ArchTests). The spend readers decide only from server-written sources: the approve Decision Record and the approval portions
/// (counted spend) and the submission's recorded findings (the pending hold). The identity the readers run as must be able to read them;
/// if it cannot, an approved invoice would silently drop out of counted spend, or a held one would be counted as pending.
/// </summary>
/// <remarks>
/// <para>Opt-in like <see cref="SignalPredicateTests"/> (same fixture). The principal checked is the one named by
/// <see cref="PrincipalEnvVar"/> (the readers' application user's systemuserid); unset, every test skips with that reason. The repo names no
/// identity.</para>
/// <para>Every read goes through the fixture's connection UNIMPERSONATED (the operator), so it is never limited by the checked principal's
/// own privileges (reading another user's privileges or profile membership under impersonation can come back empty and pass or fail for
/// the wrong reason). With <see cref="SignalPredicateTests.CallerIdEnvVar"/> set the fixture impersonates, so these tests skip.</para>
/// <para>Read-only: the tests send only <c>RetrieveEntity</c>, <c>RetrieveUserPrivileges</c> and fetches.</para>
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class SpendReaderSourceAccessLiveTests : IClassFixture<SignalPredicateTests.LiveDataverse>
{
    /// <summary>The systemuserid of the application user the spend readers run as (set per run; never committed).</summary>
    public const string PrincipalEnvVar = "SPEND_READER_PRINCIPAL_ID";

    /// <summary>The profile the offline guard checks (<c>Other/FieldSecurityProfiles.xml</c>) for read on every hold source.</summary>
    private const string ProfileName = "Spaarke BFF-Managed Field Writers";

    private readonly SignalPredicateTests.LiveDataverse _dv;
    private readonly ITestOutputHelper _out;

    public SpendReaderSourceAccessLiveTests(SignalPredicateTests.LiveDataverse dv, ITestOutputHelper output)
    {
        _dv = dv;
        _out = output;
    }

    [SkippableFact]
    public async Task O3_TheReadersPrincipal_ReadsDecisionRecords_AtGlobalDepth()
    {
        var principal = RequirePrincipal();
        (await _dv.GetReadDepthAsync(principal, InvoiceReviewReadService.DecisionRecordEntity)).Should().Be(PrivilegeDepth.Global,
            "counted spend needs the approve Decision Record of every invoice in scope; a narrower depth would drop approved invoices silently");
    }

    [SkippableFact]
    public async Task O3_TheReadersPrincipal_ReadsApprovalPortions_AtGlobalDepth()
    {
        var principal = RequirePrincipal();
        (await _dv.GetReadDepthAsync(principal, EligibleApproverResolver.ApprovalEntity)).Should().Be(PrivilegeDepth.Global,
            "the approve-record key is built from every approval portion of the invoices in scope; a narrower depth would miss a portion, so "
            + "its approve record would not match and an approved invoice would drop out of counted spend silently");
    }

    [SkippableFact]
    public async Task O4_TheReadersPrincipal_IsAMemberOfTheProfileThatReadsTheHoldSources()
    {
        var principal = RequirePrincipal();
        InvoiceBlockedHold.HoldSourceColumns.Should().NotBeEmpty();
        (await IsProfileMemberAsync(principal)).Should().BeTrue(
            "the hold sources (InvoiceBlockedHold.HoldSourceColumns) are field-secured and read through this profile; without it a held invoice would read as not held");
    }

    /// <summary>Must-fire control for the O4 helper: a principal that exists nowhere is not a member, so the helper can answer "no".</summary>
    [SkippableFact]
    public async Task O4_MembershipHelper_AnswersNotAMember_ForAPrincipalOutsideTheProfile()
    {
        RequirePrincipal();
        (await IsProfileMemberAsync(Guid.NewGuid())).Should().BeFalse("a random principal holds no membership, direct or through a team");
    }

    /// <summary>Whether <paramref name="principal"/> is in <see cref="ProfileName"/>, directly or through a team it belongs to.</summary>
    private async Task<bool> IsProfileMemberAsync(Guid principal)
    {
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
        return direct.Count + throughTeam.Count > 0;
    }

    /// <summary>The principal to check, or a skip with the reason: not live, impersonating, or the principal unset.</summary>
    private Guid RequirePrincipal()
    {
        Skip.IfNot(_dv.IsLive, $"Live Dataverse not configured ({SignalPredicateTests.UrlEnvVar} unset); a vacuous pass would hide that nothing was checked.");
        Skip.If(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SignalPredicateTests.CallerIdEnvVar)),
            $"{SignalPredicateTests.CallerIdEnvVar} is set, so the fixture impersonates; these reads must run unimpersonated. Unset it and name "
            + $"the principal in {PrincipalEnvVar}.");
        var raw = Environment.GetEnvironmentVariable(PrincipalEnvVar);
        Skip.If(string.IsNullOrWhiteSpace(raw),
            $"{PrincipalEnvVar} is unset: set it to the systemuserid of the application user the spend readers run as.");
        return Guid.Parse(raw!);
    }
}
