using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// External grant expiry — finding A-5 (spec FR-06), closed by task 007.
///
/// <para><b>What was wrong.</b> <c>sprk_expiresdate</c> was written at grant time and read
/// <i>nowhere</i>: absent from every <c>$filter</c> and every <c>$select</c> on every path, with no
/// sweep job anywhere. A grant whose expiry had passed conferred full access forever — while the
/// Manage Access UI presented expiry as a working control. The operator who set "access until 30 June"
/// believed they had bounded the grant; they had not.</para>
///
/// <para><b>Why task 001 could not pin this.</b> The queries were inline string interpolations
/// immediately before <c>_httpClient.SendAsync</c>, so observing the emitted <c>$filter</c> required
/// intercepting the transport — and <c>Mock&lt;HttpMessageHandler&gt;</c> is banned (ADR-038 §7 ban
/// B1). Task 007 extracted the builders as pure members, which is what makes these assertions
/// possible at all. No reflection into privates (ban B8) — <c>internal</c> +
/// <c>InternalsVisibleTo</c>.</para>
///
/// <para><b>What these tests are asserting.</b> That the predicate is IN THE QUERY, server-side. A
/// test that filtered materialized rows in memory would pass against a build that fetched every
/// expired grant over the wire and dropped them later — which is not the fix, because any later path
/// that forgot to re-filter would see them.</para>
/// </summary>
public class GrantExpiryCharacterizationTests
{
    private static readonly Guid ContactId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OrgId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateOnly Today = new(2026, 6, 30);

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-06 acceptance — the predicate exists, server-side, on every conferring path
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED BY TASK 007 (FR-06) — the contact grant query carried no expiry term at all.
    /// </summary>
    [Fact]
    public void BuildContactGrantFilter_ExcludesGrantsThatHaveExpired()
    {
        var filter = ExternalParticipationService.BuildContactGrantFilter(ContactId, Today);

        filter.Should().Contain("sprk_expiresdate",
            "A-5: the expiry column was written at grant time and read nowhere — a grant whose expiry " +
            "had passed conferred access forever");
        filter.Should().Contain("2026-06-30",
            "the comparison must be against a concrete date, evaluated by Dataverse");
    }

    /// <summary>
    /// The organization-grant path expires identically. Leaving the predicate off this second query
    /// would let any contact keep expired access simply by holding it through their firm — the same
    /// finding wearing a different lookup, and invisible because the two queries union silently.
    /// </summary>
    [Fact]
    public void BuildOrganizationGrantFilter_ExcludesGrantsThatHaveExpired()
    {
        var filter = ExternalParticipationService.BuildOrganizationGrantFilter(new[] { OrgId }, Today);

        filter.Should().Contain("sprk_expiresdate");
        filter.Should().Contain("2026-06-30");
    }

    /// <summary>
    /// Both conferring paths must use the SAME predicate. If they drift, expiry means one thing for a
    /// person grant and another for an org grant on the same record — and nothing would report it.
    /// </summary>
    [Fact]
    public void BothGrantFilters_UseTheSameExpiryPredicate()
    {
        var expected = ExternalParticipationService.ExpiryPredicate(Today);

        ExternalParticipationService.BuildContactGrantFilter(ContactId, Today).Should().Contain(expected);
        ExternalParticipationService.BuildOrganizationGrantFilter(new[] { OrgId }, Today).Should().Contain(expected);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The null branch — the one that would take the whole feature down
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Most grants have NO expiry, and in OData <c>field ge X</c> excludes nulls. Without the explicit
    /// null branch this predicate would revoke every open-ended grant in the system — an outage, not an
    /// expiry bug, and one that would look nothing like the change that caused it.
    /// </summary>
    [Fact]
    public void ExpiryPredicate_TreatsAGrantWithNoExpiryAsNeverExpiring()
    {
        ExternalParticipationService.ExpiryPredicate(Today)
            .Should().Contain("sprk_expiresdate eq null",
                "a grant with no expiry date must keep conferring access — `ge` alone excludes nulls " +
                "and would silently revoke every open-ended grant");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The boundary: sprk_expiresdate is DATE ONLY
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>sprk_expiresdate</c> is <b>Date Only</b> (verified against live Dataverse metadata
    /// 2026-08-23). A grant dated "expires 30 June" must still work ON 30 June — that is what setting
    /// that date means to the person who set it.
    ///
    /// <para>This is the test that pins <c>ge</c> over <c>gt</c>. Switching the operator would silently
    /// shorten every dated grant in the system by one day: not a security hole, but a fleet-wide
    /// behaviour change nobody asked for, showing up as access disappearing "a day early".</para>
    /// </summary>
    [Fact]
    public void ExpiryPredicate_OnTheExpiryDateItself_StillConfersAccess()
    {
        var filter = ExternalParticipationService.ExpiryPredicate(Today);

        filter.Should().Contain("ge",
            "an expiry of 30 June means access works through 30 June; `gt` would cut it at 00:00 that day");
        filter.Should().NotContain(" gt ",
            "`gt` would shorten every dated grant by one day");
    }

    /// <summary>
    /// The comparison must be a bare date, not a timestamp. A datetime literal against a Date Only
    /// column is the kind of mismatch Dataverse answers with a 400 — and a 400 on this query means the
    /// caller's whole grant set comes back empty, i.e. a total access outage rather than a visible error.
    /// </summary>
    [Fact]
    public void ExpiryPredicate_ComparesAgainstADateNotATimestamp()
    {
        var filter = ExternalParticipationService.ExpiryPredicate(Today);

        filter.Should().Contain("2026-06-30");
        filter.Should().NotContain("T00:00",
            "sprk_expiresdate is Date Only; a datetime literal risks a 400, which fails as an empty grant set");
        filter.Should().NotContain("Z",
            "no UTC timestamp suffix belongs in a Date Only comparison");
    }

    /// <summary>
    /// The reference date moves with the clock — a predicate frozen at build time would stop expiring
    /// anything the day after it shipped.
    /// </summary>
    [Fact]
    public void ExpiryPredicate_UsesTheDatePassedIn_NotAFixedDate()
    {
        var todayPredicate = ExternalParticipationService.ExpiryPredicate(new DateOnly(2026, 6, 30));
        var tomorrowPredicate = ExternalParticipationService.ExpiryPredicate(new DateOnly(2026, 7, 1));

        todayPredicate.Should().NotBe(tomorrowPredicate);
        tomorrowPredicate.Should().Contain("2026-07-01");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // No over-narrowing — the fix must not disturb what the queries already selected
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Expiry is an ADDITIONAL term. The pre-existing grantee and state terms must survive it: dropping
    /// <c>statecode eq 0</c> would resurrect revoked grants, and dropping the contact term would hand
    /// one caller everyone's grants — either would be a far worse defect than the one being fixed.
    /// </summary>
    [Fact]
    public void BuildContactGrantFilter_KeepsTheGranteeAndActiveStateTerms()
    {
        var filter = ExternalParticipationService.BuildContactGrantFilter(ContactId, Today);

        filter.Should().Contain($"_sprk_contact_value eq {ContactId}");
        filter.Should().Contain("statecode eq 0", "expired and revoked are different exclusions; both apply");
    }

    /// <summary>
    /// Same for the org path — including <c>_sprk_contact_value eq null</c>, which is what makes an org
    /// grant an org grant. Task 010 established that clause as load-bearing on the write side; it is
    /// equally load-bearing here, and adding a term to the filter is exactly when it could get lost.
    /// </summary>
    [Fact]
    public void BuildOrganizationGrantFilter_KeepsTheOrgGrantMarkerAndActiveStateTerms()
    {
        var filter = ExternalParticipationService.BuildOrganizationGrantFilter(new[] { OrgId }, Today);

        filter.Should().Contain($"_sprk_organization_value eq {OrgId}");
        filter.Should().Contain("_sprk_contact_value eq null",
            "the absence of a contact is what distinguishes an ORG grant from a person grant (task 010)");
        filter.Should().Contain("statecode eq 0");
    }

    /// <summary>
    /// A contact in several organizations must still match a grant held by any one of them. The
    /// multi-org disjunction has to stay parenthesised — without the brackets, ANDing the expiry and
    /// state terms binds only to the last org, and every other org's grants leak through unfiltered.
    /// </summary>
    [Fact]
    public void BuildOrganizationGrantFilter_ForMultipleOrganizations_KeepsTheDisjunctionGrouped()
    {
        var otherOrg = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

        var filter = ExternalParticipationService.BuildOrganizationGrantFilter(new[] { OrgId, otherOrg }, Today);

        filter.Should().StartWith("(", "the org disjunction must be grouped before the AND terms");
        filter.Should().Contain($"_sprk_organization_value eq {OrgId} or _sprk_organization_value eq {otherOrg})");
        filter.Should().Contain("statecode eq 0");
        filter.Should().Contain("sprk_expiresdate");
    }

    /// <summary>
    /// The columns the caller partitions rows by must all still be selected. A missing root column here
    /// makes every grant of that type silently vanish — the same user-visible symptom as expiry
    /// enforcement, from a completely different cause.
    /// </summary>
    [Fact]
    public void GrantRowSelect_ProjectsEveryRootLookupAndTheAccessLevel()
    {
        ExternalParticipationService.GrantRowSelect.Should().Contain("_sprk_project_value");
        ExternalParticipationService.GrantRowSelect.Should().Contain("_sprk_matter_value");
        ExternalParticipationService.GrantRowSelect.Should().Contain("_sprk_workassignment_value");
        ExternalParticipationService.GrantRowSelect.Should().Contain("sprk_accesslevel");
    }
}
