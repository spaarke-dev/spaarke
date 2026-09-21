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
    // The null case — INVERTED task 107 (ISS-009 / #974, owner decision D-1, 2026-09-19)
    // ─────────────────────────────────────────────────────────────────────────────
    //
    // Task 007 added an explicit `eq null` branch so a grant with no expiry kept conferring access
    // ("never expires"). D-1 retired that: "we do not use Dataverse plugins" ruled out every
    // Dataverse-side default for rows created outside the BFF, so ISS-009 closes the gap from the READ
    // side instead — a null sprk_expiresdate now confers NOTHING. Undated moved from fail-OPEN to
    // fail-CLOSED (ADR-003). This test's ORIGINAL NAME asserted the retired rule; renamed + inverted
    // rather than deleted (ADR-038 §7 — characterization tests pin behaviour, they are not scaffolding).

    /// <summary>
    /// Since task 097 the BFF never writes an undated grant, but the column is optional in Dataverse and
    /// users hold Create on <c>sprk_externalrecordaccess</c>, so a row created outside the BFF (a form,
    /// the Web API, a flow or an import) can still omit it. Without the explicit exclusion, that row
    /// would confer access forever — the exact defect D-1 closed.
    /// </summary>
    [Fact]
    public void ExpiryPredicate_TreatsAGrantWithNoExpiryAsConferringNothing()
    {
        ExternalParticipationService.ExpiryPredicate(Today)
            .Should().NotContain("eq null",
                "task 107 / D-1: a grant with no expiry date must confer NOTHING — `ge` alone already " +
                "excludes nulls in OData, so the retired `eq null` disjunct must be gone entirely");
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

    // ─────────────────────────────────────────────────────────────────────────────
    // The IN-MEMORY mirror (task 106) — the write path must answer the SAME question
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>ConfersAccessOn</c> is the in-memory mirror of <see cref="ExternalParticipationService.ExpiryPredicate"/>.
    /// </summary>
    /// <remarks>
    /// <para>Task 106 needed it because the grant upsert must decide "does this row confer access" over
    /// MATERIALIZED rows, where an OData <c>$filter</c> string cannot be reused. Two independent
    /// definitions of "expired" is the drift that would let <c>/grant</c> report an outcome the reader
    /// contradicts — A-5's shape — so there is exactly one in-memory copy, it lives next to the predicate
    /// it mirrors, and this pins it to the same semantics the predicate itself is pinned to above:
    /// <c>null</c> confers NOTHING (task 107 / ISS-009 / D-1, inverted 2026-09-21 from task 007's
    /// original "null never expires"), and the expiry date ITSELF still confers (<c>ge</c>, never
    /// <c>gt</c>).</para>
    /// <para>Asserting the mirror separately matters because the OData tests assert the STRING. A mirror
    /// that disagreed with the predicate on the null case would leave every one of them green while
    /// <c>/grant</c> reported a null-expiry row as live — the exact drift this method's own doc comment
    /// warns against.</para>
    /// </remarks>
    [Fact]
    public void ConfersAccessOn_MirrorsTheReadFiltersExpirySemantics()
    {
        ExternalParticipationService.ConfersAccessOn(null, Today).Should().BeFalse(
            "task 107 / D-1: a null sprk_expiresdate confers NOTHING — inverted from task 007's " +
            "original `eq null` branch, which the OData predicate no longer carries either");
        ExternalParticipationService.ConfersAccessOn(Today, Today).Should().BeTrue(
            "`ge`, not `gt`: an expiry of 30 June means access still works ON 30 June (task 007, Date Only)");
        ExternalParticipationService.ConfersAccessOn(Today.AddDays(1), Today).Should().BeTrue();
        ExternalParticipationService.ConfersAccessOn(Today.AddDays(-1), Today).Should().BeFalse(
            "a date in the past confers nothing — FR-06's acceptance case");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Agreement pin (task 107 acceptance criterion 2) — the two definitions cannot drift apart again
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>ExpiryPredicate</c> (the OData <c>$filter</c> string, the READ path) and
    /// <c>ConfersAccessOn</c> (the in-memory mirror, the WRITE path) must answer the SAME question for
    /// the null case — a row with no <c>sprk_expiresdate</c> — or <c>/grant</c> can report a grant as
    /// live while the reader denies it (this method's own doc comment, and finding A-5's shape). This
    /// test pins that agreement directly rather than relying on each definition's own test staying in
    /// sync by coincidence.
    /// </summary>
    [Fact]
    public void ExpiryPredicateAndConfersAccessOn_AgreeOnTheNullCase()
    {
        var predicateExcludesNull = !ExternalParticipationService.ExpiryPredicate(Today).Contains("eq null");
        var mirrorExcludesNull = !ExternalParticipationService.ConfersAccessOn(null, Today);

        predicateExcludesNull.Should().Be(mirrorExcludesNull,
            "the OData filter and its in-memory mirror must treat a null expiry identically — task 107 " +
            "made both exclude it; if either one drifts back to including it, this test must fail");
        ExternalParticipationService.ConfersAccessOn(null, Today).Should().BeFalse(
            "task 107 / D-1: the agreed answer is 'confers nothing'");
    }
}
