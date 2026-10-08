using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 139 (#1062, defect C4; owner Q1 round 2, confirmed round 3b): every MANUAL grant is
/// capped at the grantor's own level, on <c>/grant</c> and <c>/invite-and-grant</c> alike — narrowed, never refused;
/// a narrowed grant never LOWERS an existing higher one; and a grantee on the record's No Access list is refused at
/// write time.
/// </summary>
/// <remarks>
/// <para><b>What the doubles are, and why.</b> The handlers run for real, with the real grant core, the real
/// write-time checks and — for the No Access cases — the real <see cref="AccessibleRecordSetService"/> and the real
/// <see cref="NoAccessListReader"/> behind its substituted wire seam, so the deny decision is the read path's own code.
/// Substituted: the caller-rights probe (its OBO exchange), the root-flag and organization reads, the Dataverse
/// client — an in-memory grant table that INTERPRETS the production <c>$filter</c> (no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, ADR-038 ban B1) — and, for <c>/invite-and-grant</c>, the identity row store
/// behind the REAL <see cref="ContactIdentityBinder"/> (task 141), so the invitee is resolved by the binder's own
/// invite decision: the one email→contact answer onboarding also uses.</para>
/// <para><b>Every negative has a positive twin</b> that differs only in the caller's rights or the grantee, as
/// <c>DelegationRuleCharacterizationTests</c> requires — otherwise a refusal would pass against a handler that refused
/// everything.</para>
/// <para>KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2, security-auth).</para>
/// </remarks>
public class GrantorCeilingTests
{
    private static readonly Guid ProjectId = Guid.Parse("13913913-9139-1391-3913-913913913913");
    private static readonly Guid ContactId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000001");
    private static readonly Guid OtherContactId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000002");
    private static readonly Guid FirmId = Guid.Parse("f1f1f1f1-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("0a0a0a0a-0000-0000-0000-000000000001");
    private static readonly Guid OtherOrganizationId = Guid.Parse("0a0a0a0a-0000-0000-0000-000000000002");
    private static readonly DateOnly Today = new(2026, 10, 2);
    private const string InviteeEmail = "counsel@firm.example";

    private const int ViewOnly = (int)ExternalAccessLevel.ViewOnly;
    private const int Collaborate = (int)ExternalAccessLevel.Collaborate;
    private const int FullAccess = (int)ExternalAccessLevel.FullAccess;

    /// <summary>A caller holding the new Collaborate level on the record: everything but Delete.</summary>
    private const AccessRights CollaborateCaller =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    /// <summary>The positive twin of <see cref="CollaborateCaller"/>: the same, plus Delete.</summary>
    private const AccessRights FullAccessCaller = CollaborateCaller | AccessRights.Delete;

    private readonly GrantTableClient _dataverse = new();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations = new(RootRecordFlags.None);
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyReader = new();
    private readonly InMemoryContactIdentityStore _identities = new();

    /// <summary>The invitee already holds a CIAM account: bound on the External plane (onboarding is idempotent).</summary>
    private void SeedProvisionedInvitee() => _identities.AddContact(
        ContactId, email: InviteeEmail, oid: "1e1e1e1e-0000-0000-0000-000000000139", plane: IdentityPlaneMarker.External);

    /// <summary>The invitee has an active, UNBOUND contact (onboarding would create its CIAM account).</summary>
    private void SeedUnboundInvitee() => _identities.AddContact(ContactId, email: InviteeEmail);

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 4 — the ONE ceiling table
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.Delete, ExternalAccessLevel.FullAccess)]
    [InlineData(AccessRights.Read | AccessRights.Write, ExternalAccessLevel.Collaborate)]
    [InlineData(AccessRights.Read, ExternalAccessLevel.ViewOnly)]
    [InlineData(AccessRights.Read | AccessRights.Delete, ExternalAccessLevel.ViewOnly)]   // Delete without Write is not Full
    public void GrantCeilingFor_FollowsTheOwnersTable(AccessRights rights, ExternalAccessLevel expected)
        => ExternalAccessLevels.GrantCeilingFor(rights).Should().Be(expected);

    [Theory]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Write | AccessRights.Delete)]   // no Read: nothing readable to grant
    public void GrantCeilingFor_WithoutRead_IsNone(AccessRights rights)
        => ExternalAccessLevels.GrantCeilingFor(rights).Should().BeNull();

    /// <summary>
    /// Create, Append, AppendTo and Share are NOT consulted: RetrievePrincipalAccess on an existing record need not
    /// report CreateAccess, so a table that read Create would under-state a Deep-role user. Adding or removing them
    /// changes nothing.
    /// </summary>
    [Theory]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Read)]
    [InlineData(AccessRights.Read | AccessRights.Write)]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.Delete)]
    public void GrantCeilingFor_IgnoresCreateAppendAppendToAndShare(AccessRights baseRights)
    {
        const AccessRights ignored = AccessRights.Create | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;
        var expected = ExternalAccessLevels.GrantCeilingFor(baseRights);

        ExternalAccessLevels.GrantCeilingFor(baseRights | ignored).Should().Be(expected);
        ExternalAccessLevels.GrantCeilingFor(baseRights | AccessRights.Create).Should().Be(expected);
        ExternalAccessLevels.GrantCeilingFor(baseRights | AccessRights.Share).Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 5 — /grant and /invite-and-grant narrow to the grantor's level
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_FullAccessByACallerWithoutDelete_IsWrittenAtCollaborate_AndSaysSo()
    {
        var result = await Grant(ContactGrant(ExternalAccessLevel.FullAccess), CollaborateCaller);

        var body = OkBody<GrantAccessResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Narrowed.Should().BeTrue();
        _dataverse.ActiveRows.Should().ContainSingle().Which.AccessLevel.Should().Be(Collaborate,
            "Delete was never the caller's to give");
    }

    [Fact]
    public async Task Grant_FullAccessByACallerWithDelete_IsWrittenAtFullAccess_NotNarrowed()
    {
        var result = await Grant(ContactGrant(ExternalAccessLevel.FullAccess), FullAccessCaller);

        var body = OkBody<GrantAccessResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.FullAccess);
        body.Narrowed.Should().BeFalse();
        _dataverse.ActiveRows.Should().ContainSingle().Which.AccessLevel.Should().Be(FullAccess);
    }

    [Fact]
    public async Task InviteAndGrant_FullAccessByACallerWithoutDelete_IsWrittenAtCollaborate_AndSaysSo()
    {
        SeedProvisionedInvitee();

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.FullAccess), CollaborateCaller);

        var body = OkBody<InviteAndGrantResponse>(result);
        body.ContactId.Should().Be(ContactId);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Narrowed.Should().BeTrue();
        _dataverse.ActiveRows.Should().ContainSingle().Which.AccessLevel.Should().Be(Collaborate);
    }

    [Fact]
    public async Task InviteAndGrant_FullAccessByACallerWithDelete_IsWrittenAtFullAccess_NotNarrowed()
    {
        SeedProvisionedInvitee();

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.FullAccess), FullAccessCaller);

        var body = OkBody<InviteAndGrantResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.FullAccess);
        body.Narrowed.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 6 — the same cap applies to an organization-wide grant (on a Standard root)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_OrganizationWide_FullAccessByACallerWithoutDelete_IsWrittenAtCollaborate()
    {
        var result = await Grant(OrganizationGrant(ExternalAccessLevel.FullAccess), CollaborateCaller);

        var body = OkBody<GrantAccessResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Narrowed.Should().BeTrue();
        var row = _dataverse.ActiveRows.Should().ContainSingle().Subject;
        row.ContactId.Should().BeNull("an organization-wide grant names no contact");
        row.OrganizationId.Should().Be(OrganizationId);
        row.AccessLevel.Should().Be(Collaborate);
    }

    [Fact]
    public async Task Grant_OrganizationWide_FullAccessByACallerWithDelete_IsWrittenAtFullAccess()
    {
        var result = await Grant(OrganizationGrant(ExternalAccessLevel.FullAccess), FullAccessCaller);

        OkBody<GrantAccessResponse>(result).Narrowed.Should().BeFalse();
        _dataverse.ActiveRows.Should().ContainSingle().Which.AccessLevel.Should().Be(FullAccess);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 7 — a narrowed grant never silently LOWERS an existing higher one
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The latent hazard the cap would make live: the upsert's match path updates the survivor's level in place, so a
    /// "Full Access please" narrowed to Collaborate would overwrite somebody else's Full Access grant.
    /// </summary>
    [Fact]
    public async Task Grant_NarrowedBelowAnExistingHigherGrant_Is409_AndWritesNothing()
    {
        var existing = _dataverse.Seed(ContactId, null, FullAccess);

        var result = await Grant(ContactGrant(ExternalAccessLevel.FullAccess), CollaborateCaller);

        Problem(result).Should().Be((409, ExternalGrantLifecycle.WouldLowerExistingReasonCode));
        Detail(result).Should().Contain("already have more access than you can grant");
        _dataverse.Updates.Should().BeEmpty("no UpdateAsync — the existing grant keeps its level");
        _dataverse.Creates.Should().BeEmpty();
        _dataverse.ActiveRows.Should().ContainSingle().Which.Should().BeSameAs(existing);
        existing.AccessLevel.Should().Be(FullAccess);
    }

    /// <summary>The org-wide grant gets the same protection.</summary>
    [Fact]
    public async Task Grant_OrganizationWide_NarrowedBelowAnExistingHigherGrant_Is409()
    {
        _dataverse.Seed(null, OrganizationId, FullAccess);

        var result = await Grant(OrganizationGrant(ExternalAccessLevel.FullAccess), CollaborateCaller);

        Problem(result).Should().Be((409, ExternalGrantLifecycle.WouldLowerExistingReasonCode));
        _dataverse.Updates.Should().BeEmpty();
    }

    /// <summary>
    /// The positive twin: an EXPLICIT request for a lower level (not narrowed — the caller holds everything) is a
    /// deliberate downgrade by a Write-holder and is applied, exactly as before task 139.
    /// </summary>
    [Fact]
    public async Task Grant_AnExplicitDowngradeByACallerHoldingEverything_StillDowngrades()
    {
        var existing = _dataverse.Seed(ContactId, null, FullAccess);

        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        OkBody<GrantAccessResponse>(result).Narrowed.Should().BeFalse();
        existing.AccessLevel.Should().Be(ViewOnly);
    }

    /// <summary>
    /// Task 113 (ISS-028 / #1008): <c>/grant</c> over an EXPIRED grant with no new expiry and a different level answers
    /// 409 "Grant did not take effect" — and that is now true: nothing was written. Before task 113 the level was written
    /// first and the 409 returned over the changed row. Pins the whole 409 (status, title, detail, reasonCode, traceId,
    /// accessRecordId), which no handler-level test pinned before, so the reorder is shown not to have moved it.
    /// </summary>
    [Fact]
    public async Task Grant_OverAnExpiredGrantAtADifferentLevelWithNoNewExpiry_Is409_AndWritesNothing()
    {
        var existing = _dataverse.Seed(ContactId, null, ViewOnly);
        existing.ExpiresDate = Today.AddDays(-1);

        var result = await Grant(ContactGrant(ExternalAccessLevel.FullAccess), FullAccessCaller);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(409);
        problem.ProblemDetails.Title.Should().Be("Grant did not take effect");
        problem.ProblemDetails.Detail.Should().Be(
            "The existing grant expired on 2026-10-01 and this request supplied no new expiry date, so it still confers " +
            "no access. Re-send with an expiryDate to restore it.");
        problem.ProblemDetails.Extensions.Should().Contain("reasonCode", "sdap.grant.expired_not_restored");
        problem.ProblemDetails.Extensions.Should().Contain("traceId", "trace-139");
        problem.ProblemDetails.Extensions.Should().Contain("accessRecordId", existing.Id);
        _dataverse.Updates.Should().BeEmpty("the request did not take effect, so it changed nothing");
        _dataverse.Creates.Should().BeEmpty();
        existing.AccessLevel.Should().Be(ViewOnly, "the refused request must not have written the level");
    }

    /// <summary>A narrowed request that does NOT lower anything (the grantee holds less) is written — the upgrade case.</summary>
    [Fact]
    public async Task Grant_NarrowedAboveAnExistingLowerGrant_RaisesIt()
    {
        var existing = _dataverse.Seed(ContactId, null, ViewOnly);

        var result = await Grant(ContactGrant(ExternalAccessLevel.FullAccess), CollaborateCaller);

        OkBody<GrantAccessResponse>(result).Narrowed.Should().BeTrue();
        existing.AccessLevel.Should().Be(Collaborate);
    }

    [Fact]
    public async Task InviteAndGrant_ForAnExistingContactHoldingMore_Is409BeforeOnboarding()
    {
        SeedUnboundInvitee();
        _dataverse.Seed(ContactId, null, FullAccess);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.FullAccess), CollaborateCaller);

        Problem(result).Should().Be((409, ExternalGrantLifecycle.WouldLowerExistingReasonCode));
        AssertNothingOnboardedOrWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 8 — the handler's own probe: a throw is 500, an answer of None is 403
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_WhenTheCallersRightsProbeThrows_Is500_AndWritesNothing()
    {
        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), callerRights: null);

        Problem(result).Should().Be((500, ExternalGrantLifecycle.CallerRightsUnreadableReasonCode));
        Detail(result).Should().Contain("could not be established");
        _dataverse.Creates.Should().BeEmpty();
        _dataverse.QueriedSets.Should().NotContain(GrantTableClient.GrantSet);
    }

    [Fact]
    public async Task Grant_WhenTheCallersRightsAreNone_Is403CallerCannotGrant_AndWritesNothing()
    {
        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), AccessRights.None);

        Problem(result).Should().Be((403, ExternalGrantLifecycle.CallerCannotGrantReasonCode));
        Detail(result).Should().NotBeNullOrWhiteSpace();
        _dataverse.Creates.Should().BeEmpty();
        _dataverse.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task InviteAndGrant_WhenTheCallersRightsProbeThrows_Is500_AndOnboardsNothing()
    {
        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly), callerRights: null);

        Problem(result).Should().Be((500, ExternalGrantLifecycle.CallerRightsUnreadableReasonCode));
        _identities.Reads.Should().BeEmpty("the invitee lookup — the onboarding seam's first step — is never reached");
        AssertNothingOnboardedOrWritten();
    }

    [Fact]
    public async Task InviteAndGrant_WhenTheCallersRightsAreNone_Is403_AndOnboardsNothing()
    {
        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly), AccessRights.None);

        Problem(result).Should().Be((403, ExternalGrantLifecycle.CallerCannotGrantReasonCode));
        AssertNothingOnboardedOrWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 9 — the No Access list at write time, through the read path's own veto code
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToAContactOnTheRecordsNoAccessList_Is422_AndWritesNothing()
    {
        _denyReader.DenyContactOnRecord(ContactId, ProjectId);

        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        Problem(result).Should().Be((422, ExternalGrantLifecycle.GranteeDeniedReasonCode));
        Detail(result).Should().Contain("No Access list")
            .And.NotContain("could not be checked", "an entry is the record's policy, never a fault (task 142 r4)");
        _dataverse.Creates.Should().BeEmpty();
        _denyReader.Queries.Should().BePositive("the decision came from the shared deny-list reader");
    }

    /// <summary>The positive twin: the same request for a contact who is NOT on the list is written.</summary>
    [Fact]
    public async Task Grant_ToAContactNotOnTheList_IsWritten()
    {
        _denyReader.DenyContactOnRecord(OtherContactId, ProjectId);

        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        OkBody<GrantAccessResponse>(result).GrantedAccessLevel.Should().Be(ExternalAccessLevel.ViewOnly);
        _denyReader.Queries.Should().BePositive();
    }

    [Fact]
    public async Task Grant_ToAContactWhoseActiveOrganizationIsOnTheList_Is422()
    {
        _participations.ContactOrganizations[ContactId] = new[] { OrganizationId };
        _denyReader.DenyOrganizationOnRecord(OrganizationId, ProjectId);

        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        Problem(result).Should().Be((422, ExternalGrantLifecycle.GranteeDeniedReasonCode));
        _dataverse.Creates.Should().BeEmpty();
    }

    /// <summary>Twin: the contact's organization is a DIFFERENT one from the denied organization.</summary>
    [Fact]
    public async Task Grant_ToAContactWhoseOrganizationIsNotOnTheList_IsWritten()
    {
        _participations.ContactOrganizations[ContactId] = new[] { OtherOrganizationId };
        _denyReader.DenyOrganizationOnRecord(OrganizationId, ProjectId);

        OkBody<GrantAccessResponse>(await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller))
            .Narrowed.Should().BeFalse();
    }

    [Fact]
    public async Task Grant_OrganizationWide_ToAnOrganizationOnTheList_Is422()
    {
        _denyReader.DenyOrganizationOnRecord(OrganizationId, ProjectId);

        var result = await Grant(OrganizationGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        Problem(result).Should().Be((422, ExternalGrantLifecycle.GranteeDeniedReasonCode));
        _dataverse.Creates.Should().BeEmpty();
    }

    /// <summary>Twin: an organization-wide grant to a different organization is written.</summary>
    [Fact]
    public async Task Grant_OrganizationWide_ToAnOrganizationNotOnTheList_IsWritten()
    {
        _denyReader.DenyOrganizationOnRecord(OtherOrganizationId, ProjectId);

        OkBody<GrantAccessResponse>(await Grant(OrganizationGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller))
            .AccessRecordId.Should().NotBeEmpty();
    }

    /// <summary>
    /// Task 142 r4 (owner round 13 item 4): a No Access check that could not be completed is a FAULT — 503
    /// <c>no_access_unverifiable</c>, retryable, counted as a server-side failure — never absorbed into the 422
    /// <c>grantee_denied</c> an ENTRY gets. Every read fault the deny-veto code meets: the ones it used to absorb into
    /// "denied" (an unreadable membership read, an unreadable referenced-organization read, a fail-closed deny-list read, a
    /// 5xx-shaped throw) and the timeouts it rethrows alike. Fail closed: nothing is written. The twin is
    /// <see cref="Grant_ToAContactOnTheRecordsNoAccessList_Is422_AndWritesNothing"/> (an entry: 422, never "could not be
    /// checked"); the positive twin is <see cref="Grant_ToAContactNotOnTheList_IsWritten"/> (the reads answer).
    /// </summary>
    [Theory]
    [InlineData("reader-fault")]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-unreadable")]
    [InlineData("referenced-organizations-throw")]
    [InlineData("referenced-organizations-timeout")]
    [InlineData("no-access-list-timeout")]
    public async Task Grant_WhenTheNoAccessListCannotBeChecked_Is503NoAccessUnverifiable_AFaultNeverAnEntry_AndWritesNothing(
        string fault)
    {
        InjectNoAccessFault(fault);

        var result = await Grant(ContactGrant(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        Problem(result).Should().Be((503, ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode),
            "a check that could not be completed is reported as the fault it is, not as an entry on the list");
        Detail(result).Should().Contain("could not be checked").And.Contain("Try again");
        _dataverse.Creates.Should().BeEmpty("a No Access check that could not finish must never grant");
        _dataverse.Updates.Should().BeEmpty();
        if (fault is "reader-fault" or "no-access-list-timeout")
            _denyReader.Queries.Should().BePositive("the fault came from the shared deny-list reader itself");
    }

    /// <summary>The same faults on <c>/invite-and-grant</c>: 503 before onboarding, nothing created or bound.</summary>
    [Theory]
    [InlineData("reader-fault")]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-timeout")]
    public async Task InviteAndGrant_WhenTheNoAccessListCannotBeChecked_Is503BeforeOnboarding(string fault)
    {
        SeedUnboundInvitee();
        InjectNoAccessFault(fault);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        Problem(result).Should().Be((503, ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode));
        AssertNothingOnboardedOrWritten();
    }

    /// <summary>
    /// Called DIRECTLY (the path the Assigned-To materializer and task 140's route take): the core reports an
    /// unverifiable check as a fault — <see cref="GrantPolicyDecision.IsDenyListReadFault"/>, its own code — and an
    /// entry as an entry. Neither writes.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_CalledDirectly_ReportsAnUnverifiableNoAccessCheckAsAFault_AndAnEntryAsAnEntry()
    {
        var ceiling = GrantCeiling.FromGrantorRights(FullAccessCaller);
        _participations.MembershipsUnreadable = true;

        var faulted = await Core(ContactGrant(ExternalAccessLevel.ViewOnly), ceiling);

        faulted.Refusal!.IsDenyListReadFault.Should().BeTrue();
        faulted.Refusal.ReasonCode.Should().Be(ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode);
        faulted.Refusal.StatusCode.Should().Be(503);

        _participations.MembershipsUnreadable = false;
        _denyReader.DenyContactOnRecord(ContactId, ProjectId);

        var entry = await Core(ContactGrant(ExternalAccessLevel.ViewOnly), ceiling);

        entry.Refusal!.IsDenyListReadFault.Should().BeFalse();
        entry.Refusal.ReasonCode.Should().Be(ExternalGrantLifecycle.GranteeDeniedReasonCode);
        entry.Refusal.StatusCode.Should().Be(422);
        _dataverse.Creates.Should().BeEmpty();
    }

    /// <summary>
    /// Task 142 r5 (r4 verifier finding 6): the core's switch over the check's answer, one row per refusing answer —
    /// including a value outside the enum, which no production check returns: it is never "allowed", it refuses as a
    /// FAULT (fail closed). An entry refuses as an entry. Nothing is written. The positive twin is
    /// <see cref="CreateGrantAsync_CalledDirectly_GrantsWhenTheCheckAnswersAllowed"/> (same double, Allowed).
    /// </summary>
    [Theory]
    [InlineData((int)NoAccessCheckAnswer.Denied, ExternalGrantLifecycle.GranteeDeniedReasonCode, 422, false)]
    [InlineData((int)NoAccessCheckAnswer.Unverifiable, ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode, 503, true)]
    [InlineData(99, ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode, 503, true)]
    public async Task CreateGrantAsync_CalledDirectly_RefusesEveryAnswerButAllowed_AnUnknownAnswerAsAFault(
        int answer, string reasonCode, int status, bool isFault)
    {
        var outcome = await Core(
            ContactGrant(ExternalAccessLevel.ViewOnly), GrantCeiling.FromGrantorRights(FullAccessCaller),
            GrantPolicyTestDoubles.DenyListAnswering((NoAccessCheckAnswer)answer));

        outcome.Refusal.Should().NotBeNull("only an Allowed answer grants");
        outcome.Refusal!.ReasonCode.Should().Be(reasonCode);
        outcome.Refusal.StatusCode.Should().Be(status);
        outcome.Refusal.IsDenyListReadFault.Should().Be(isFault);
        _dataverse.Creates.Should().BeEmpty("a refused grant writes nothing");
        _dataverse.Updates.Should().BeEmpty();
    }

    /// <summary>The positive twin: the same double answering Allowed — the grant is written.</summary>
    [Fact]
    public async Task CreateGrantAsync_CalledDirectly_GrantsWhenTheCheckAnswersAllowed()
    {
        var outcome = await Core(
            ContactGrant(ExternalAccessLevel.ViewOnly), GrantCeiling.FromGrantorRights(FullAccessCaller),
            GrantPolicyTestDoubles.DenyListAnswering(NoAccessCheckAnswer.Allowed));

        outcome.Refusal.Should().BeNull();
        _dataverse.ActiveRows.Should().ContainSingle().Which.ContactId.Should().Be(ContactId);
    }

    /// <summary>Task 142 r4: one switch per No Access read fault the write-time check can meet.</summary>
    private void InjectNoAccessFault(string fault)
    {
        // The shape of an HttpClient timeout: a TaskCanceledException while the CALLER has not cancelled.
        var timeout = new TaskCanceledException("Simulated HttpClient timeout (the caller did not cancel).");
        switch (fault)
        {
            case "reader-fault": _denyReader.Faults = true; break;
            case "memberships-unreadable": _participations.MembershipsUnreadable = true; break;
            case "referenced-organizations-unreadable": _participations.UnreadableReferencedOrganizations[ProjectId] = true; break;
            case "referenced-organizations-throw":
                _participations.ReferencedOrganizationsThrow = new InvalidOperationException("Simulated Dataverse 5xx.");
                break;
            case "referenced-organizations-timeout": _participations.ReferencedOrganizationsThrow = timeout; break;
            case "no-access-list-timeout": _denyReader.Throws = timeout; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "unknown fault");
        }
    }

    [Fact]
    public async Task InviteAndGrant_ForAnExistingContactOnTheList_Is422BeforeOnboarding()
    {
        SeedUnboundInvitee();
        _denyReader.DenyContactOnRecord(ContactId, ProjectId);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        Problem(result).Should().Be((422, ExternalGrantLifecycle.GranteeDeniedReasonCode));
        AssertNothingOnboardedOrWritten();
    }

    /// <summary>
    /// A person with no contact yet: the check runs on the request's firm. Nothing is created to obtain an id — a
    /// refused request leaves no Contact and no CIAM account behind.
    /// </summary>
    [Fact]
    public async Task InviteAndGrant_ForANewPersonWhoseFirmIsOnTheList_Is422_AndCreatesNoContact()
    {
        _denyReader.DenyOrganizationOnRecord(FirmId, ProjectId);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly, firmId: FirmId), FullAccessCaller);

        Problem(result).Should().Be((422, ExternalGrantLifecycle.GranteeDeniedReasonCode));
        AssertNothingOnboardedOrWritten();
    }

    /// <summary>Twin: the existing contact is not on the list, so onboarding (already bound — idempotent) and the grant proceed.</summary>
    [Fact]
    public async Task InviteAndGrant_ForAnExistingContactNotOnTheList_IsGranted()
    {
        SeedProvisionedInvitee();
        _denyReader.DenyContactOnRecord(OtherContactId, ProjectId);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        OkBody<InviteAndGrantResponse>(result).OnboardStatus.Should().Be("AlreadyProvisioned");
        _dataverse.ActiveRows.Should().ContainSingle().Which.ContactId.Should().Be(ContactId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tasks 139 + 141 — the invitee the checks judge is the binder's ONE email→contact answer
    // ─────────────────────────────────────────────────────────────────────────────
    // /invite-and-grant judges the invitee BEFORE onboarding (task 139). Since task 141 the email→contact answer is the
    // binder's invite decision (active contacts, two rows, the systemuser-reference check), and the SAME resolution is
    // handed to onboarding. Each negative below has its "no contact yet" twin in
    // InviteAndGrant_ForANewPersonWhoseFirmIsOnTheList_Is422_AndCreatesNoContact: had an ambiguous or unreadable
    // lookup been read as "no contact", the firm on the No Access list would have answered 422 — and a request that
    // passed the checks would have onboarded a NEW contact for a person who already has one.

    [Fact]
    public async Task InviteAndGrant_WhenTwoActiveContactsCarryTheEmail_Is409EmailAmbiguous_NeverJudgedAsANewPerson()
    {
        _identities.AddContact(ContactId, email: InviteeEmail);
        _identities.AddContact(OtherContactId, email: InviteeEmail);
        _denyReader.DenyOrganizationOnRecord(FirmId, ProjectId);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly, firmId: FirmId), FullAccessCaller);

        Problem(result).Should().Be((409, ContactBindingDecision.InviteEmailAmbiguous));
        AssertNothingOnboardedOrWritten();
        _denyReader.Queries.Should().Be(0, "an invitee that cannot be told apart is refused before any grant check");
    }

    [Fact]
    public async Task InviteAndGrant_WhenTheContactLookupCannotBeRead_Is503LookupFailed_NeverJudgedAsANewPerson()
    {
        _identities.EmailLookupStatus = LookupStatus.Failed;
        _denyReader.DenyOrganizationOnRecord(FirmId, ProjectId);

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly, firmId: FirmId), FullAccessCaller);

        Problem(result).Should().Be((503, ContactBindingDecision.InviteContactLookupFailed));
        Detail(result).Should().Be(ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteContactLookupFailed));
        AssertNothingOnboardedOrWritten();
        _identities.Writes.Should().BeEmpty("nothing is bound and nothing is flagged on an unreadable lookup");
        _denyReader.Queries.Should().Be(0, "not knowing who the invitee is, nothing is judged");
    }

    /// <summary>
    /// The checks and onboarding share ONE resolution: the email is looked up exactly once, so the contact the
    /// never-lower and No Access checks judged is, by construction, the contact onboarding provisions.
    /// </summary>
    [Fact]
    public async Task InviteAndGrant_LooksTheInviteeUpOnce_TheChecksAndOnboardingJudgeTheSameContact()
    {
        SeedProvisionedInvitee();

        var result = await InviteAndGrant(Invite(ExternalAccessLevel.ViewOnly), FullAccessCaller);

        OkBody<InviteAndGrantResponse>(result).ContactId.Should().Be(ContactId);
        _identities.Reads.Count(r => r == "email").Should().Be(1,
            "onboarding reuses the resolution the grant checks judged — it does not look the email up a second time");
        _dataverse.ActiveRows.Should().ContainSingle().Which.ContactId.Should().Be(ContactId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // WP-1 — the checks live in the CORE, not only in the handlers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called DIRECTLY — the path a future writer (tasks 140, 142) takes — the core caps, refuses to lower and consults
    /// the No Access list by itself. No handler is involved.
    /// </summary>
    [Fact]
    public async Task CreateGrantAsync_CalledDirectly_AppliesTheCeiling_NeverLower_AndTheNoAccessList()
    {
        var collaborateCeiling = GrantCeiling.FromGrantorRights(CollaborateCaller);

        var narrowed = await Core(ContactGrant(ExternalAccessLevel.FullAccess), collaborateCeiling);
        narrowed.Refusal.Should().BeNull();
        narrowed.GrantedLevel.Should().Be(ExternalAccessLevel.Collaborate);
        narrowed.Narrowed.Should().BeTrue();

        _dataverse.Seed(OtherContactId, null, FullAccess);
        var lowering = await Core(ContactGrant(ExternalAccessLevel.FullAccess, OtherContactId), collaborateCeiling);
        lowering.Refusal!.ReasonCode.Should().Be(ExternalGrantLifecycle.WouldLowerExistingReasonCode);

        _denyReader.DenyOrganizationOnRecord(OrganizationId, ProjectId);
        var denied = await Core(OrganizationGrant(ExternalAccessLevel.ViewOnly), collaborateCeiling);
        denied.Refusal!.ReasonCode.Should().Be(ExternalGrantLifecycle.GranteeDeniedReasonCode);

        var none = await Core(ContactGrant(ExternalAccessLevel.ViewOnly), GrantCeiling.FromGrantorRights(AccessRights.None));
        none.Refusal!.ReasonCode.Should().Be(ExternalGrantLifecycle.CallerCannotGrantReasonCode);
        none.Refusal.StatusCode.Should().Be(403);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────────────

    private static GrantAccessRequest ContactGrant(ExternalAccessLevel level, Guid? contactId = null) => new(
        ContactId: contactId ?? ContactId,
        ProjectId: Guid.Empty,
        AccessLevel: level,
        ExpiryDate: null,
        OrganizationId: null,
        RecordType: "project",
        RecordId: ProjectId);

    private static GrantAccessRequest OrganizationGrant(ExternalAccessLevel level) => new(
        ContactId: Guid.Empty,
        ProjectId: Guid.Empty,
        AccessLevel: level,
        ExpiryDate: null,
        OrganizationId: OrganizationId,
        RecordType: "project",
        RecordId: ProjectId);

    private static InviteExternalUserRequest Invite(ExternalAccessLevel level, Guid? firmId = null) => new(
        Email: InviteeEmail,
        ProjectId: Guid.Empty,
        AccessLevel: (int)level,
        FirstName: "Casey",
        LastName: "Counsel",
        ExpiryDate: null,
        OrganizationId: firmId,
        RecordType: "project",
        RecordId: ProjectId);

    private AccessibleRecordSetService DenyList() => GrantPolicyTestDoubles.RealDenyList(_participations, _denyReader);

    /// <param name="callerRights">The caller's rights the probe reports; <c>null</c> makes the probe THROW.</param>
    private Task<IResult> Grant(GrantAccessRequest request, AccessRights? callerRights) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            request, _dataverse, _participations, DenyList(), new FixedRightsProbe(callerRights),
            AssignedAccessTestDoubles.InertMaterializer(),
            Context(), NullLogger<Program>.Instance, new FixedClock(Today),
            CancellationToken.None);

    /// <remarks>
    /// The CIAM provisioner and the email service are passed as null: every positive case here onboards an EXISTING
    /// contact that already has an oid (idempotent — neither is touched), and every refusal must stop before
    /// onboarding. A call into either would fault and show up as a 500, not as the asserted outcome.
    /// </remarks>
    private Task<IResult> InviteAndGrant(InviteExternalUserRequest request, AccessRights? callerRights) =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            request, _dataverse, _participations, DenyList(), new FixedRightsProbe(callerRights),
            ciamProvisioner: null!, emailService: null!, IdentityBindingTestKit.Binder(_identities),
            PortalConfig(), AssignedAccessTestDoubles.InertMaterializer(), Context(),
            NullLogger<Program>.Instance, new FixedClock(Today), CancellationToken.None);

    /// <param name="noAccessCheck">The write-time No Access check; the real one over the seam reader when null.</param>
    private Task<GrantExternalAccessEndpoint.GrantUpsertOutcome> Core(
        GrantAccessRequest request, GrantCeiling ceiling, IAccessibleRecordSetService? noAccessCheck = null) =>
        GrantExternalAccessEndpoint.CreateGrantAsync(
            request, ExternalGrantRootType.Project, ProjectId, Today, ceiling, callerOid: null,
            _dataverse, _participations, noAccessCheck ?? DenyList(), NullLogger.Instance,
            CancellationToken.None);

    private void AssertNothingOnboardedOrWritten()
    {
        _dataverse.Creates.Should().BeEmpty("no Contact and no grant row is created");
        _dataverse.Updates.Should().BeEmpty("no grant level moves");
        _identities.Writes.Should().NotContain(w => w.Op == "bind", "no CIAM oid is bound");
    }

    private static IConfiguration PortalConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = "https://portal.test" })
        .Build();

    private static HttpContext Context() => new DefaultHttpContext { TraceIdentifier = "trace-139" };

    private static T OkBody<T>(IResult result) => result.Should().BeOfType<Ok<T>>().Subject.Value!;

    private static (int Status, string? ReasonCode) Problem(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions.Should().ContainKey("traceId");
        return (problem.StatusCode,
            problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string : null);
    }

    private static string? Detail(IResult result) => result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Detail;

    /// <summary>Reports fixed rights for the caller — or throws, when constructed with <c>null</c>.</summary>
    private sealed class FixedRightsProbe : CallerRecordAccessProbe
    {
        private readonly AccessRights? _rights;

        public FixedRightsProbe(AccessRights? rights)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _rights = rights;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => _rights is { } rights
                ? Task.FromResult(rights)
                : throw new InvalidOperationException("Simulated failure establishing the caller's rights.");
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateOnly today) => _now = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// An in-memory <c>sprk_externalrecordaccess</c> table behind the real client's <c>virtual</c> seams. Grant queries
    /// are answered by INTERPRETING the production <c>$filter</c>, so a wrong predicate fails here rather than passing
    /// against canned rows. Contacts are not here: since task 141 the invitee is resolved through the identity store
    /// (<see cref="InMemoryContactIdentityStore"/>), and a <c>contacts</c> query through this client THROWS, so a
    /// second, divergent email match could not pass silently.
    /// </summary>
    private sealed class GrantTableClient : DataverseWebApiClient
    {
        public const string GrantSet = "sprk_externalrecordaccesses";

        private readonly List<ExternalGrantRow> _rows = new();
        private int _seq;

        public GrantTableClient()
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
        {
        }

        public List<(string Set, object Payload)> Creates { get; } = new();
        public List<(string Set, Guid Id, string Payload)> Updates { get; } = new();
        public List<string> QueriedSets { get; } = new();

        public IReadOnlyList<ExternalGrantRow> ActiveRows => _rows.Where(r => r.IsActive).ToList();

        public ExternalGrantRow Seed(Guid? contactId, Guid? organizationId, int level)
        {
            var row = new ExternalGrantRow
            {
                Id = NextId(),
                ContactId = contactId,
                OrganizationId = organizationId,
                ProjectId = ProjectId,
                AccessLevel = level,
                ExpiresDate = Today.AddDays(60),
                StateCode = 0,
            };
            _rows.Add(row);
            return row;
        }

        private Guid NextId() => Guid.Parse($"bbbbbbbb-0000-0000-0000-{++_seq:D12}");

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
            int? top = null, int? skip = null, CancellationToken cancellationToken = default)
        {
            QueriedSets.Add(entitySetName);
            object rows = entitySetName switch
            {
                GrantSet => MatchGrants(filter),
                "contacts" => throw new InvalidOperationException(
                    "A contact was looked up through the Dataverse client. The invitee is resolved ONLY by the identity " +
                    "binder's invite decision (task 141)."),
                _ => Array.Empty<object>(),
            };

            // Round-trip through JSON so the handler receives its OWN row types, exactly as from the wire.
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
        {
            Creates.Add((entitySetName, entity));
            if (entitySetName != GrantSet)
                return Task.FromResult(Guid.NewGuid());

            var payload = (IDictionary<string, object?>)entity;
            var row = new ExternalGrantRow
            {
                Id = NextId(),
                ContactId = BoundId(payload, "sprk_Contact@odata.bind"),
                OrganizationId = BoundId(payload, "sprk_Organization@odata.bind"),
                ProjectId = BoundId(payload, "sprk_Project@odata.bind"),
                AccessLevel = (int?)payload["sprk_accesslevel"],
                StateCode = 0,
            };
            _rows.Add(row);
            return Task.FromResult(row.Id);
        }

        public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(entity);
            Updates.Add((entitySetName, id, json));

            var row = _rows.FirstOrDefault(r => r.Id == id);
            if (row is not null)
            {
                if (json.Contains("\"statecode\":1", StringComparison.Ordinal))
                    row.StateCode = 1;
                var level = Regex.Match(json, @"""sprk_accesslevel"":(\d+)");
                if (level.Success)
                    row.AccessLevel = int.Parse(level.Groups[1].Value);
            }

            return Task.CompletedTask;
        }

        private IEnumerable<ExternalGrantRow> MatchGrants(string? filter)
        {
            if (filter is null)
                return Enumerable.Empty<ExternalGrantRow>();

            var rootId = Guid.Parse(Regex.Match(filter, @"_sprk_project_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
            if (filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal))
            {
                var orgId = Guid.Parse(Regex.Match(filter, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
                return _rows.Where(r => r.IsActive && r.ProjectId == rootId && r.OrganizationId == orgId && r.ContactId is null).ToList();
            }

            var contactId = Guid.Parse(Regex.Match(filter, @"_sprk_contact_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
            return _rows.Where(r => r.IsActive && r.ProjectId == rootId && r.ContactId == contactId).ToList();
        }

        private static Guid? BoundId(IDictionary<string, object?> payload, string key) =>
            payload.TryGetValue(key, out var value) && value is string s
                ? Guid.Parse(Regex.Match(s, @"\(([0-9a-fA-F-]{36})\)").Groups[1].Value)
                : null;

        private sealed class NoCredential : TokenCredential
        {
            public static readonly NoCredential Instance = new();

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => throw new NotSupportedException("This double issues no HTTP.");

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => throw new NotSupportedException("This double issues no HTTP.");
        }
    }
}
