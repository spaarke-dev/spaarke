using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 140 (#1063) — contact-side Grant Access. Owner C4 (session 27 round 1): a contact with
/// Collaborate or Full Access may grant; Q1 (settled round 3b): capped at the grantor's own level; Q2: colleagues of the
/// grantor's OWN organization only, never organization-wide; round 3 decisions G1 (a) (no new persons, no membership
/// writes), G2 (expiry capped at the grantor's own; no proxy change of a row someone else issued), G3 (a) (the grantor's
/// effective post-veto level counts).
/// </summary>
/// <remarks>
/// <para><b>What runs for real.</b> The route filter (<see cref="ContactGrantorAuthorizationFilter"/>) and the handlers
/// (<see cref="ContactGrantEndpoints"/>), the ONE grant core in its contact-issuer mode, the record-policy decision
/// (task 138), the ceiling table (task 139), the write-time No Access check through the PRODUCTION
/// <see cref="AccessibleRecordSetService"/> and <see cref="NoAccessListReader"/> (only their wire seams substituted), the
/// PRODUCTION membership projection (<see cref="ExternalParticipationService.ProjectOrganizationMemberships"/>) for the
/// inactive and date-ended memberships, and — for the Secure / Limited / Restricted cases — the PRODUCTION CIAM strategy
/// and evaluator composing the grantor's level. Substituted at module boundaries only: the Dataverse client (an in-memory
/// grant table that INTERPRETS the production <c>$filter</c>s), the participation data reads, and the identity row
/// store (ADR-038: no <c>Mock&lt;HttpMessageHandler&gt;</c>).</para>
/// <para><b>Every negative has a positive twin</b> differing in one input (level, organization membership, principal
/// kind, flag, issuer).</para>
/// <para>KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2, security-auth).</para>
/// </remarks>
public class ContactGrantAuthorizationTests
{
    internal static readonly Guid ProjectId = Guid.Parse("14014014-0140-0140-0140-014014014014");
    internal static readonly Guid MatterId = Guid.Parse("14014014-0140-0140-0140-0140140140aa");
    internal static readonly Guid Grantor = Guid.Parse("c1400000-0000-0000-0000-000000000001");
    internal static readonly Guid Colleague = Guid.Parse("c1400000-0000-0000-0000-000000000002");
    internal static readonly Guid Outsider = Guid.Parse("c1400000-0000-0000-0000-000000000003");
    internal static readonly Guid OtherContactGrantor = Guid.Parse("c1400000-0000-0000-0000-000000000004");
    internal static readonly Guid SystemUserId = Guid.Parse("5a140000-0000-0000-0000-000000000001");
    internal static readonly Guid InternalUserOid = Guid.Parse("0e140000-0000-0000-0000-000000000001");
    internal static readonly Guid FirmA = Guid.Parse("0a140000-0000-0000-0000-00000000000a");
    internal static readonly Guid FirmB = Guid.Parse("0a140000-0000-0000-0000-00000000000b");
    internal static readonly DateOnly Today = new(2026, 10, 4);
    internal const string ColleagueEmail = "colleague@firm-a.example";

    private readonly ContactGrantTable _dataverse = new();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations = new(RootRecordFlags.None);
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyReader = new();
    private readonly InMemoryContactIdentityStore _identities = new();

    public ContactGrantAuthorizationTests()
    {
        // The standard world: grantor and colleague are active members of firm A; the outsider of firm B.
        _participations.ContactOrganizations[Grantor] = new[] { FirmA };
        _participations.ContactOrganizations[Colleague] = new[] { FirmA };
        _participations.ContactOrganizations[Outsider] = new[] { FirmB };
        _identities.AddContact(Colleague, email: ColleagueEmail);
        _identities.AddContact(Outsider, email: "outsider@firm-b.example");
        _identities.AddContact(Grantor, email: "grantor@firm-a.example");
        // The colleague-by-email read expands each member's contact from the same identity rows.
        _participations.ContactDirectory = id => _identities.Contacts.TryGetValue(id, out var c) ? (c.Email, c.StateCode) : null;
        // The grantor's own grant on the project — dated well beyond +90, so the default expiry is not capped.
        _dataverse.Seed(Grantor, ProjectId, (int)ExternalAccessLevel.FullAccess, Today.AddDays(200), issuedByContact: null);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 1 — a Collaborate grantor grants a colleague; the row is the contact's, +90 by default
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ByACollaborateContact_ToAnActiveColleague_WritesOneContactIssuedRow_AtPlus90()
    {
        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        var body = Ok<ContactGrantResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Narrowed.Should().BeFalse();
        body.ExpiryDate.Should().Be(Today.AddDays(90));
        body.ExpiryNarrowed.Should().BeFalse();

        var row = _dataverse.RowsFor(Colleague).Should().ContainSingle().Subject;
        row.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        row.GrantedByContactId.Should().Be(Grantor, "the contact issuer is stamped");
        row.GrantedByContactProvenance.Should().Be(Grantor.ToString("D"),
            "its id is recorded as text in the SAME write, so the provenance outlives the contact (round 50 item 2)");
        row.GrantedBySystemUserId.Should().BeNull("sprk_grantedby is a systemuser lookup and stays empty");
        row.ExpiresDate.Should().Be(Today.AddDays(90));
        _dataverse.CreatePayloads.Single().Keys.Should().NotContain("sprk_GrantedBy@odata.bind");
        _dataverse.CreatePayloads.Single()["sprk_grantedbycontactid"].Should().Be(Grantor.ToString("D"));
        _participations.Invalidations.Should().ContainSingle(i => i.Contacts.Contains(Colleague));
        AssertNoMembershipWrites();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 2 — the cap (owner Q1)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_FullAccessByACollaborateGrantor_IsWrittenAtCollaborate_AndSaysSo()
    {
        var result = await Grant(Request(ExternalAccessLevel.FullAccess), Ciam(ExternalAccessLevel.Collaborate));

        var body = Ok<ContactGrantResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.Narrowed.Should().BeTrue();
        _dataverse.RowsFor(Colleague).Single().AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
    }

    [Fact]
    public async Task Grant_FullAccessByAFullAccessGrantor_IsWrittenAtFullAccess()
    {
        var result = await Grant(Request(ExternalAccessLevel.FullAccess), Ciam(ExternalAccessLevel.FullAccess));

        var body = Ok<ContactGrantResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.FullAccess);
        body.Narrowed.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 3 — View Only and "no access at all" are one answer
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ByAViewOnlyContact_Is403LevelInsufficient_IndistinguishableFromNoAccess()
    {
        var viewOnly = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.ViewOnly));
        var none = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), Ciam(level: null));

        Problem(viewOnly).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
        Problem(none).Should().Be(Problem(viewOnly));
        Detail(none).Should().Be(Detail(viewOnly), "the caller cannot tell 'View Only' from 'no access'");
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 4 — the principal KIND decides
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryRoute_ForASystemUserWithALinkedContact_Is403UseManageAccess()
    {
        var systemUser = SystemUser(ExternalAccessLevel.FullAccess);

        var grant = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), systemUser);
        var list = await ThroughFilter(new ContactGrantListQuery("project", ProjectId), systemUser);
        var revoke = await ThroughFilter(new ContactGrantRevokeRequest(Guid.NewGuid()), systemUser);

        foreach (var result in new[] { grant, list, revoke })
            Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.UseManageAccessReasonCode));
        AssertNothingWritten();
    }

    /// <summary>The positive twin: the SAME rights on a workforce CONTACT-ONLY principal grant an existing colleague.</summary>
    [Fact]
    public async Task Grant_ByAWorkforceContactOnlyPrincipal_ToAnExistingColleague_Works()
    {
        var result = await Grant(Request(ExternalAccessLevel.Collaborate), WorkforceContact(ExternalAccessLevel.FullAccess));

        Ok<ContactGrantResponse>(result).GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 5 — organization scope
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ToAContactOfAnotherOrganization_Is422_AndWritesNothing()
    {
        var result = await Grant(Request(ExternalAccessLevel.ViewOnly, grantee: Outsider), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((422, ContactGrantEndpoints.GranteeNotInOrganizationReasonCode));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("ended")]
    [InlineData("not-started")]
    [InlineData("organization-inactive")]
    public async Task Grant_ToAColleagueWhoseOnlyMembershipDoesNotConfer_Is422(string shape)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _participations.MembershipRows[Colleague] = new[] { Membership(FirmA, today, shape) };

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((422, ContactGrantEndpoints.GranteeNotInOrganizationReasonCode));
        AssertNothingWritten();
    }

    /// <summary>The positive twin: the same membership row, current — through the same production projection.</summary>
    [Fact]
    public async Task Grant_ToAColleagueWithACurrentMembershipRow_IsWritten()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _participations.MembershipRows[Colleague] = new[] { Membership(FirmA, today, "current") };

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result);
        _dataverse.RowsFor(Colleague).Should().ContainSingle();
    }

    [Fact]
    public async Task Grant_ByAGrantorInNoActiveOrganization_Is403NoOrganization()
    {
        _participations.ContactOrganizations[Grantor] = Array.Empty<Guid>();

        var result = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.NoOrganizationReasonCode));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("grantor", "reported")]
    [InlineData("grantor", "thrown")]
    [InlineData("grantee", "reported")]
    [InlineData("grantee", "thrown")]
    public async Task Grant_WhenAMembershipReadFaults_Is503MembershipUnreadable_NotNoOrganization(string whose, string how)
    {
        var contact = whose == "grantor" ? Grantor : Colleague;
        if (how == "reported") _participations.UnreadableMembershipContacts[contact] = true;
        else _participations.ThrowingMembershipContacts[contact] = true;

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((503, ContactGrantorAuthorizationFilter.MembershipUnreadableReasonCode));
        Detail(result).Should().Contain("could not be checked");
        AssertNothingWritten();
    }

    [Fact]
    public async Task Grant_ToYourself_Is400()
    {
        var result = await Grant(Request(ExternalAccessLevel.ViewOnly, grantee: Grantor), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((400, ContactGrantEndpoints.SelfGrantReasonCode));
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criteria 7 + 10 — Restricted, Secure and Limited, with the grantor's level composed by the REAL evaluator
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_OnARestrictedRoot_ComposedByTheRealEvaluator_Is403LevelInsufficient()
    {
        _participations.Flags[ProjectId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var principal = await ComposedCiamPrincipalAsync(Grantor, ExternalAccessLevel.FullAccess, direct: true);

        principal.GetEffectiveRights(ProjectId).Should().Be(AccessRights.None, "task 135: nothing contact-sourced survives Restricted");
        var result = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), principal);

        Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
        AssertNothingWritten();
    }

    /// <summary>
    /// The core's own refusal is reachable, not merely shadowed: past the filter (a principal that still claims
    /// Collaborate, as a stale one would), a Restricted flag is refused 422 by task 138's policy inside the core.
    /// </summary>
    [Fact]
    public async Task Grant_OnARestrictedRoot_PastTheFilter_IsRefused422ByTheCore()
    {
        _participations.Flags[ProjectId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((422, ExternalGrantLifecycle.RecordRestrictedReasonCode));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("secure")]
    [InlineData("limited")]
    public async Task Grant_OnADirectOnlyRoot_ByAContactWhoseOnlyAccessIsOrganizationInherited_Is403(string kind)
    {
        _participations.Flags[ProjectId] = DirectOnly(kind);
        var principal = await ComposedCiamPrincipalAsync(Grantor, ExternalAccessLevel.FullAccess, direct: false);

        var result = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), principal);

        Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("secure")]
    [InlineData("limited")]
    public async Task Grant_OnADirectOnlyRoot_ByAContactWithADirectCollaborateGrant_IsWritten(string kind)
    {
        _participations.Flags[ProjectId] = DirectOnly(kind);
        _dataverse.Seed(Grantor, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(200), issuedByContact: null);
        var principal = await ComposedCiamPrincipalAsync(Grantor, ExternalAccessLevel.Collaborate, direct: true);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), principal);

        Ok<ContactGrantResponse>(result).GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        _dataverse.RowsFor(Colleague).Should().ContainSingle().Which.GrantedByContactId.Should().Be(Grantor);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 8 — the No Access list, through task 139's entry point (tri-state since 142 r4)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("contact")]
    [InlineData("organization")]
    public async Task Grant_ToAColleagueOnTheRecordsNoAccessList_Is422GranteeDenied(string how)
    {
        if (how == "contact") _denyReader.DenyContactOnRecord(Colleague, ProjectId);
        else _denyReader.DenyOrganizationOnRecord(FirmA, ProjectId);

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((422, ExternalGrantLifecycle.GranteeDeniedReasonCode));
        _denyReader.Queries.Should().BePositive();
        AssertNothingWritten();
    }

    [Fact]
    public async Task Grant_WhenTheNoAccessListCannotBeRead_Is503AndReportedAsAFault()
    {
        _denyReader.Faults = true;

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((503, ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode));
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 9 — someone else's access is never changed; the caller's own row never goes down
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("systemuser")]
    [InlineData("other-contact")]
    [InlineData("no-issuer")]
    public async Task Grant_ToAColleagueHoldingARowSomebodyElseIssued_Is409ManagedElsewhere_RowUnchanged(string issuer)
    {
        var existing = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(10),
            issuedByContact: issuer == "other-contact" ? OtherContactGrantor : null,
            issuedBySystemUser: issuer == "systemuser" ? SystemUserId : null);
        var before = Snapshot(existing);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.FullAccess));

        Problem(result).Should().Be((409, ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode));
        Detail(result).Should().Contain(ColleagueEmail).And.Contain("granted by someone else");
        Snapshot(existing).Should().Be(before, "level, expiry and both issuer columns are untouched");
        _dataverse.Updates.Should().BeEmpty();
        _dataverse.Creates.Should().BeEmpty();
    }

    /// <summary>
    /// A row another issuer RACES onto the same grant between the check and the create is never deactivated by the
    /// contact's post-create duplicate collapse (no proxy revocation, even by accident) — the contact converges only over
    /// its own rows.
    /// </summary>
    [Fact]
    public async Task Grant_WhenAnotherIssuerRacesOntoTheSameGrant_TheirRowIsNotCollapsedAway()
    {
        ExternalGrantRow? raced = null;
        _dataverse.BeforeCreate = () => raced = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly,
            Today.AddDays(10), issuedByContact: null, issuedBySystemUser: SystemUserId);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result);
        raced!.StateCode.Should().Be(0, "the systemuser's row is somebody else's access");
        _dataverse.RowsFor(Colleague).Should().HaveCount(2);
    }

    [Fact]
    public async Task Grant_OnTheCallersOwnRow_AHigherRequestRaisesIt()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(10), issuedByContact: Grantor);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        own.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        own.GrantedByContactId.Should().Be(Grantor);
    }

    [Fact]
    public async Task Grant_OnTheCallersOwnRow_ALowerRequest_Is409WouldLower_RowUnchanged()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(10), issuedByContact: Grantor);
        var before = Snapshot(own);

        var result = await Grant(Request(ExternalAccessLevel.ViewOnly), Ciam(ExternalAccessLevel.FullAccess));

        Problem(result).Should().Be((409, ExternalGrantLifecycle.WouldLowerExistingReasonCode));
        Snapshot(own).Should().Be(before);
        _dataverse.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_OnTheCallersOwnRow_AnEarlierExpiryNeverShortensIt()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(60), issuedByContact: Grantor);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate, expiry: Today.AddDays(5)), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(60));
        own.ExpiresDate.Should().Be(Today.AddDays(60));
        _dataverse.Updates.Should().BeEmpty("nothing to raise and nothing to lengthen — a no-op");
    }

    /// <summary>
    /// Owner round 80 (task 113): a dateless re-add of the caller's own LAPSED grant restores it at the picked level — and
    /// the restored date is still capped at the grantor's own (round 42 item 2 / owner G2 (i)), here 30 days, not 90.
    /// </summary>
    [Fact]
    public async Task Grant_ReAddOfTheCallersOwnLapsedRow_RestoresItAtThePickedLevel_CappedAtTheGrantorsOwnDate()
    {
        _dataverse.Clear();
        _dataverse.Seed(Grantor, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(30), issuedByContact: null);
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(-2), issuedByContact: Grantor);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        var body = Ok<ContactGrantResponse>(result);
        body.GrantedAccessLevel.Should().Be(ExternalAccessLevel.Collaborate);
        body.ExpiryDate.Should().Be(Today.AddDays(30), "capped at the grantor's own date");
        own.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        own.ExpiresDate.Should().Be(Today.AddDays(30));
    }

    /// <summary>The positive twin: a LATER expiry on the caller's own row lengthens it.</summary>
    [Fact]
    public async Task Grant_OnTheCallersOwnRow_ALaterExpiryLengthensIt()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(5), issuedByContact: Grantor);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate, expiry: Today.AddDays(60)), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(60));
        own.ExpiresDate.Should().Be(Today.AddDays(60));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Owner G2 (i) — the issued expiry never exceeds the grantor's own (dated) access
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_ByAGrantorWhoseOwnGrantEndsSooner_IsCappedAtItsDate_AndSaysSo()
    {
        _dataverse.Clear();
        _dataverse.Seed(Grantor, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(30), issuedByContact: null);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        var body = Ok<ContactGrantResponse>(result);
        body.ExpiryDate.Should().Be(Today.AddDays(30));
        body.ExpiryNarrowed.Should().BeTrue();
        _dataverse.RowsFor(Colleague).Single().ExpiresDate.Should().Be(Today.AddDays(30));
    }

    /// <summary>An organization-wide grant to the grantor's firm is a dated source too (Standard root).</summary>
    [Fact]
    public async Task Grant_ByAGrantorHoldingAccessThroughTheirFirmsGrant_IsCappedAtThatGrantsDate()
    {
        _dataverse.Clear();
        _dataverse.SeedOrganization(FirmA, ProjectId, (int)ExternalAccessLevel.FullAccess, Today.AddDays(45));

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.FullAccess));

        Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(45));
    }

    /// <summary>
    /// ISS-026: a direct grant carrying an INACTIVE firm confers nothing, so it is not a dated source of the grantor's
    /// level — with no other source the level is not held live (403). The twin: the same row with the firm active caps.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task Grant_ByAGrantorWhoseOnlyDatedGrantNamesAFirm_CountsItOnlyWhileTheFirmIsActive(int firmState, bool counts)
    {
        _dataverse.Clear();
        var row = _dataverse.Seed(Grantor, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(20), issuedByContact: null);
        row.OrganizationId = FirmB;
        _dataverse.OrganizationStates[FirmB] = firmState;

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        if (counts)
            Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(20));
        else
            Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
    }

    /// <summary>
    /// On a Secure root only DIRECT grants confer (FR-22), so the firm's organization-wide grant — however late it runs —
    /// is not a source of the grantor's time there: the cap is their own direct grant's date.
    /// </summary>
    [Fact]
    public async Task Grant_OnASecureRoot_IsCappedAtTheGrantorsDirectGrant_NotTheirFirmsLaterOne()
    {
        _participations.Flags[ProjectId] = DirectOnly("secure");
        _dataverse.Clear();
        _dataverse.Seed(Grantor, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(20), issuedByContact: null);
        _dataverse.SeedOrganization(FirmA, ProjectId, (int)ExternalAccessLevel.FullAccess, Today.AddDays(100));

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(20));
    }

    /// <summary>A workforce contact whose level rests on an UNDATED term (standing grant) issues the +90 default.</summary>
    [Fact]
    public async Task Grant_ByAGrantorWhoseLevelRestsOnAnUndatedTerm_IsNotCapped()
    {
        _dataverse.Clear();

        var result = await Grant(Request(ExternalAccessLevel.Collaborate),
            WorkforceContact(ExternalAccessLevel.Collaborate, undatedTermOnProjects: true));

        Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(90));
    }

    /// <summary>The twin: with no dated grant and no undated term, the principal's level is not held live — refused.</summary>
    [Fact]
    public async Task Grant_ByAGrantorWithNoLiveSourceOfTheirLevel_Is403()
    {
        _dataverse.Clear();

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criteria 11, 12, 13 — past expiry, ambiguous email, no new persons
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_WithAPastExpiry_Is400_AndWritesNothing()
    {
        var result = await Grant(Request(ExternalAccessLevel.ViewOnly, expiry: Today.AddDays(-1)), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((400, GrantExternalAccessEndpoint.ExpiryInPastReasonCode));
        AssertNothingWritten();
    }

    [Fact]
    public async Task Grant_ByEmailOfAnActiveColleague_IsWritten()
    {
        var result = await Grant(ByEmail(ColleagueEmail), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).GranteeContactId.Should().Be(Colleague);
    }

    [Fact]
    public async Task Grant_ByAnEmailTwoActiveColleaguesShare_Is409Ambiguous()
    {
        var twin = Guid.Parse("c1400000-0000-0000-0000-000000000099");
        _identities.AddContact(twin, email: ColleagueEmail);
        _participations.ContactOrganizations[twin] = new[] { FirmA };

        var result = await Grant(ByEmail(ColleagueEmail), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((409, ContactGrantEndpoints.GranteeAmbiguousReasonCode));
        Detail(result).Should().Contain("in your organization");
        AssertNothingWritten();
    }

    /// <summary>
    /// Task 140 verifier r1, items 3 and 11 — "by email among active org members", never system-wide. A colleague and an
    /// OUTSIDER (another organization) share the address: the colleague is granted; the outsider is never counted, so
    /// the answer is not "ambiguous" and says nothing about the outsider. The read named only the grantor's organization.
    /// </summary>
    [Fact]
    public async Task Grant_ByEmail_WhenAnOutsiderSharesTheColleaguesAddress_GrantsTheColleague()
    {
        _identities.Contacts[Outsider].Email = ColleagueEmail;

        var result = await Grant(ByEmail(ColleagueEmail), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).GranteeContactId.Should().Be(Colleague);
        _dataverse.RowsFor(Outsider).Should().BeEmpty();
        _participations.ColleagueByEmailReads.Should().OnlyContain(r => r.Organizations.SequenceEqual(new[] { FirmA }),
            "only the grantor's own organizations are read");
    }

    /// <summary>
    /// The twin with no colleague at the address: an email only an outsider uses is the ordinary 422, worded with the
    /// address the caller typed — never "more than one person", which would reveal the outsider.
    /// </summary>
    [Fact]
    public async Task Grant_ByEmailOnlyAnOutsiderUses_Is422_AndRevealsNothingAboutTheOutsider()
    {
        var byOutsiderEmail = await Grant(ByEmail("outsider@firm-b.example"), Ciam(ExternalAccessLevel.Collaborate));
        var byUnknownEmail = await Grant(ByEmail("nobody@firm-b.example"), Ciam(ExternalAccessLevel.Collaborate));

        Problem(byOutsiderEmail).Should().Be((422, ContactGrantEndpoints.GranteeNotInOrganizationReasonCode));
        Detail(byOutsiderEmail)!.Replace("outsider@firm-b.example", "{email}").Should().Be(
            Detail(byUnknownEmail)!.Replace("nobody@firm-b.example", "{email}"),
            "an address an outsider uses and an address nobody uses are one answer");
        AssertNothingWritten();
    }

    /// <summary>
    /// The production projection (not the double) keeps an INACTIVE contact at the colleague's address out of the
    /// match: an active colleague plus an inactive one with the same email is the colleague, not "ambiguous".
    /// </summary>
    [Fact]
    public async Task Grant_ByEmail_AnInactiveContactAtTheSameAddress_IsNotCounted()
    {
        var former = Guid.Parse("c1400000-0000-0000-0000-000000000098");
        _identities.AddContact(former, email: ColleagueEmail);
        _identities.Contacts[former].StateCode = 1;
        _participations.ContactOrganizations[former] = new[] { FirmA };

        var result = await Grant(ByEmail(ColleagueEmail), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).GranteeContactId.Should().Be(Colleague);
    }

    /// <summary>
    /// Task 140 verifier r1, items 2 and 12 — the by-id path is not a contact oracle. Another organization's contact, an
    /// inactive contact and an id that names nobody are ONE refusal: same status, same code, same words — and the
    /// stored email of the contact the GUID names never appears.
    /// </summary>
    [Theory]
    [InlineData("other-organization")]
    [InlineData("inactive")]
    public async Task Grant_ById_OfANonColleague_IsTheSameAnswerAsAnUnknownId_AndNeverShowsTheirEmail(string shape)
    {
        const string secret = "secret.outsider@firm-b.example";
        Guid target;
        if (shape == "other-organization")
        {
            target = Outsider;
            _identities.Contacts[Outsider].Email = secret;
        }
        else
        {
            target = Guid.Parse("c1400000-0000-0000-0000-000000000097");
            _identities.AddContact(target, email: secret);
            _identities.Contacts[target].StateCode = 1;
            _participations.ContactOrganizations[target] = new[] { FirmA };
        }

        var unknownId = Guid.Parse("c1400000-0000-0000-0000-0000000000ff");
        var named = await Grant(Request(ExternalAccessLevel.ViewOnly, grantee: target), Ciam(ExternalAccessLevel.Collaborate));
        var unknown = await Grant(Request(ExternalAccessLevel.ViewOnly, grantee: unknownId), Ciam(ExternalAccessLevel.Collaborate));

        Problem(named).Should().Be((422, ContactGrantEndpoints.GranteeNotInOrganizationReasonCode));
        Problem(unknown).Should().Be(Problem(named));
        Detail(named).Should().Be(Detail(unknown), "the caller cannot tell whether the id names a contact");
        Detail(named).Should().NotContain(secret).And.Contain(ContactGrantEndpoints.UnnamedGranteeLabel);

        // ...nor from the work done: the same reads run for the contact the GUID names and for an id that names nobody.
        _identities.Reads.Count(r => r == "contact").Should().Be(2, "one contact lookup per request");
        _participations.MembershipReads.Count(id => id == target).Should().Be(1);
        _participations.MembershipReads.Count(id => id == unknownId).Should().Be(1,
            "the membership read runs for an id that names no contact too, not only for one that exists");
        AssertNothingWritten();
    }

    /// <summary>
    /// Owner G1 (a): an email matching no active contact is a person who is not yet a colleague in this system — refused;
    /// no contact is created, no membership written, no CIAM identity provisioned (the route has no onboarding path), on
    /// either sign-in plane.
    /// </summary>
    [Theory]
    [InlineData("ciam")]
    [InlineData("workforce")]
    public async Task Grant_ByAnEmailOfAPersonWhoIsNotYetAContact_Is422_AndProvisionsNothing(string plane)
    {
        var principal = plane == "ciam" ? Ciam(ExternalAccessLevel.FullAccess) : WorkforceContact(ExternalAccessLevel.FullAccess);

        var result = await Grant(ByEmail("new.person@firm-a.example"), principal);

        Problem(result).Should().Be((422, ContactGrantEndpoints.GranteeNotInOrganizationReasonCode));
        Detail(result).Should().Contain("new.person@firm-a.example").And.Contain("not yet a member of your organization");
        AssertNothingWritten();
        _identities.Writes.Should().BeEmpty("no contact is created and no oid is bound");
    }

    [Theory]
    [InlineData("email")]
    [InlineData("id")]
    public async Task Grant_WhenTheGranteeLookupCannotBeRead_Is503(string by)
    {
        if (by == "email") _participations.ColleagueByEmailFaults = true;
        else _identities.FailGetContact = true;

        var result = await Grant(by == "email" ? ByEmail(ColleagueEmail) : Request(ExternalAccessLevel.ViewOnly),
            Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((503, ContactGrantorAuthorizationFilter.MembershipUnreadableReasonCode));
        Detail(result).Should().Contain("could not be checked");
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 14 — the list shows only what the caller issued
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsOnlyTheGrantsTheCallerIssued_OnThatRecord()
    {
        _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        _dataverse.Seed(Outsider, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: OtherContactGrantor);
        _dataverse.Seed(Colleague, MatterId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor, onMatter: true);
        _dataverse.Names[Colleague] = ("Casey Colleague", ColleagueEmail);

        var result = await ContactGrantEndpoints.ListAsync(
            new ContactGrantListQuery("project", ProjectId), _dataverse, _participations, Context(Ciam(ExternalAccessLevel.Collaborate)),
            NullLogger<ContactGrantorAuthorizationFilter>.Instance, CancellationToken.None);

        var grant = Ok<ContactIssuedGrantsResponse>(result).Grants.Should().ContainSingle().Subject;
        grant.ContactId.Should().Be(Colleague);
        grant.FullName.Should().Be("Casey Colleague");
        grant.Email.Should().Be(ColleagueEmail);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 15 — revoke
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_OfAGrantTheCallerIssued_DeactivatesIt()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);

        var result = await Revoke(own.Id, Ciam(ExternalAccessLevel.Collaborate));

        var body = Ok<ContactGrantRevokeResponse>(result);
        body.DeactivatedCount.Should().Be(1);
        body.AccessRemainsFromOthers.Should().BeFalse();
        own.StateCode.Should().Be(1);
        _participations.Invalidations.Should().Contain(i => i.Contacts.Contains(Colleague));
    }

    [Fact]
    public async Task Revoke_OfAGrantSomebodyElseIssued_AndOfANonExistentId_AreTheSame404()
    {
        var others = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30),
            issuedByContact: null, issuedBySystemUser: SystemUserId);

        var notTheirs = await Revoke(others.Id, Ciam(ExternalAccessLevel.FullAccess));
        var absent = await Revoke(Guid.NewGuid(), Ciam(ExternalAccessLevel.FullAccess));

        Problem(notTheirs).Should().Be((404, ContactGrantorAuthorizationFilter.NotFoundReasonCode));
        Problem(absent).Should().Be(Problem(notTheirs));
        Detail(absent).Should().Be(Detail(notTheirs), "the route is not an access-record oracle");
        others.StateCode.Should().Be(0);
        _dataverse.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_WhenDataverseFaults_IsAProblemWithAMessage_NeverABare500()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        _dataverse.FailUpdates = true;

        var result = await Revoke(own.Id, Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((500, ContactGrantorAuthorizationFilter.RevokeFailedReasonCode));
        Detail(result).Should().NotBeNullOrWhiteSpace().And.Contain("revoke");
    }

    [Fact]
    public async Task Revoke_WhenTheRowCannotBeRead_Is503WithAMessage()
    {
        _dataverse.FailRetrieve = true;

        var result = await Revoke(Guid.NewGuid(), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((503, ContactGrantorAuthorizationFilter.RevokeFailedReasonCode));
        Detail(result).Should().Contain("Nothing was changed");
    }

    [Fact]
    public async Task Revoke_ByAGrantorWhoseOwnLevelFellToViewOnly_Is403()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);

        var result = await Revoke(own.Id, Ciam(ExternalAccessLevel.ViewOnly));

        Problem(result).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
        own.StateCode.Should().Be(0);
    }

    [Fact]
    public async Task Revoke_LeavesARowAnotherIssuerAddedOnTheSameGrant_AndSaysAccessRemains()
    {
        var own = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        var others = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(30),
            issuedByContact: null, issuedBySystemUser: SystemUserId);

        var result = await Revoke(own.Id, Ciam(ExternalAccessLevel.Collaborate));

        var body = Ok<ContactGrantRevokeResponse>(result);
        body.AccessRemainsFromOthers.Should().BeTrue();
        own.StateCode.Should().Be(1);
        others.StateCode.Should().Be(0, "a contact never revokes access somebody else gave");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 16 — the filter denies an unmapped request type; a mapped one passes
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Filter_DeniesARouteBoundToARequestTypeItDoesNotMap()
    {
        var reached = false;
        var filter = Filter();
        var context = new DefaultEndpointFilterInvocationContext(Context(Ciam(ExternalAccessLevel.FullAccess)), new UnknownGrantDto(ProjectId));

        var result = await filter.InvokeAsync(context, _ => { reached = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        reached.Should().BeFalse();
        Problem((IResult)result!).Should().Be((403, ContactGrantorAuthorizationFilter.UnmappedRequestReasonCode));
    }

    [Fact]
    public async Task Filter_PassesAMappedRequestFromAQualifyingContact()
    {
        var reached = false;
        var context = new DefaultEndpointFilterInvocationContext(
            Context(Ciam(ExternalAccessLevel.Collaborate)), Request(ExternalAccessLevel.ViewOnly));

        await Filter().InvokeAsync(context, _ => { reached = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        reached.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Criterion 17 + no proxy revocation in reverse — an internal user changing a contact-issued row takes it over
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CoreDefaultMode_ChangingAContactIssuedRow_ClearsTheContactIssuer()
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);

        var outcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
            new GrantAccessRequest(Colleague, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "project", ProjectId),
            ExternalGrantRootType.Project, ProjectId, Today, GrantCeiling.FromGrantorRights(AccessRights.Read | AccessRights.Write | AccessRights.Delete),
            callerOid: null, _dataverse, _participations, DenyList(), NullLogger.Instance, CancellationToken.None);

        outcome.Refusal.Should().BeNull();
        row.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        row.GrantedByContactId.Should().BeNull("the contact can no longer revoke a decision an internal user made");
        row.GrantedByContactProvenance.Should().BeNull("the recorded provenance is cleared with the lookup (round 50 item 2)");
    }

    /// <summary>
    /// Session 27 round 50 item 2: a row whose issuing CONTACT WAS DELETED — its lookup emptied by RemoveLink, its id still
    /// recorded in <c>sprk_grantedbycontactid</c> — is contact-issued all the same: an internal change takes it over (the
    /// provenance cleared, the changing systemuser stamped, in the same write). The lookup is not re-sent (it is already
    /// empty). Left recorded, the reconciliation job would later read the internal user's grant as a deleted contact's. The
    /// twin is the take-over test below (the contact still exists): the one input is the lookup.
    /// </summary>
    [Fact]
    public async Task CoreDefaultMode_ChangingARowWhoseIssuingContactWasDeleted_TakesItOver_AndClearsTheProvenance()
    {
        _dataverse.SystemUsersByOid[InternalUserOid] = SystemUserId;
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        row.GrantedByContactId = null; // the contact was deleted: RemoveLink empties the lookup; the provenance stays

        var outcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
            new GrantAccessRequest(Colleague, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "project", ProjectId),
            ExternalGrantRootType.Project, ProjectId, Today, GrantCeiling.FromGrantorRights(AccessRights.Read | AccessRights.Write | AccessRights.Delete),
            callerOid: InternalUserOid.ToString(), _dataverse, _participations, DenyList(), NullLogger.Instance, CancellationToken.None);

        outcome.Refusal.Should().BeNull();
        row.GrantedByContactProvenance.Should().BeNull();
        row.GrantedBySystemUserId.Should().Be(SystemUserId, "the changing internal user now owns the decision");
        var write = _dataverse.Updates.Should().ContainSingle("the take-over travels with the change").Subject;
        write.Payload.Should().Contain("\"sprk_grantedbycontactid\":null")
            .And.Contain($"\"sprk_GrantedBy@odata.bind\":\"/systemusers({SystemUserId})\"")
            .And.NotContain("sprk_GrantedByContact@odata.bind", "an already-empty lookup is not unbound again");
    }

    /// <summary>
    /// Round 50 item 2, the contact side: a row whose issuing contact was deleted is nobody's to change from the SPA — a
    /// contact's grant to that grantee is refused 409 managed_elsewhere and the row is untouched (the issuer check reads the
    /// LOOKUP, which no longer names anyone). The positive twin is <c>Grant_OnTheCallersOwnRow_AHigherRequestRaisesIt</c> (the
    /// lookup names the caller); the other-issuer refusal is <c>Grant_ToAColleagueHoldingARowSomebodyElseIssued…</c>.
    /// </summary>
    [Fact]
    public async Task Grant_OverARowWhoseIssuingContactWasDeleted_Is409ManagedElsewhere_AndTheRowIsUntouched()
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: OtherContactGrantor);
        row.GrantedByContactId = null; // RemoveLink
        var before = Snapshot(row);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((409, ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode));
        Snapshot(row).Should().Be(before);
        row.GrantedByContactProvenance.Should().Be(OtherContactGrantor.ToString("D"));
        _dataverse.Updates.Should().BeEmpty();
    }

    /// <summary>The twin: an unchanged re-grant writes nothing, so the issuer stays the contact.</summary>
    [Fact]
    public async Task CoreDefaultMode_ANoOpRegrantOfAContactIssuedRow_LeavesTheIssuer()
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);

        await GrantExternalAccessEndpoint.CreateGrantAsync(
            new GrantAccessRequest(Colleague, Guid.Empty, ExternalAccessLevel.ViewOnly, null, null, "project", ProjectId),
            ExternalGrantRootType.Project, ProjectId, Today, GrantCeiling.FromGrantorRights(AccessRights.Read | AccessRights.Write),
            callerOid: null, _dataverse, _participations, DenyList(), NullLogger.Instance, CancellationToken.None);

        row.GrantedByContactId.Should().Be(Grantor);
        _dataverse.Updates.Should().BeEmpty();
    }

    /// <summary>
    /// Session 27 round 34 item 3: "takes it over" is BOTH halves in ONE write — the contact issuer cleared AND
    /// <c>sprk_grantedby</c> stamped with the changing systemuser (resolved from the caller's Entra object id).
    /// </summary>
    [Fact]
    public async Task CoreDefaultMode_ChangingAContactIssuedRow_StampsTheChangingSystemUser_InTheSameWrite()
    {
        _dataverse.SystemUsersByOid[InternalUserOid] = SystemUserId;
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);

        var outcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
            new GrantAccessRequest(Colleague, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "project", ProjectId),
            ExternalGrantRootType.Project, ProjectId, Today, GrantCeiling.FromGrantorRights(AccessRights.Read | AccessRights.Write | AccessRights.Delete),
            callerOid: InternalUserOid.ToString(), _dataverse, _participations, DenyList(), NullLogger.Instance, CancellationToken.None);

        outcome.Refusal.Should().BeNull();
        row.GrantedByContactId.Should().BeNull();
        row.GrantedBySystemUserId.Should().Be(SystemUserId, "the changing internal user now owns the decision");
        var write = _dataverse.Updates.Should().ContainSingle("the take-over travels with the change, never as a second write").Subject;
        write.Payload.Should().Contain("\"sprk_GrantedByContact@odata.bind\":null")
            .And.Contain("\"sprk_grantedbycontactid\":null")
            .And.Contain($"\"sprk_GrantedBy@odata.bind\":\"/systemusers({SystemUserId})\"")
            .And.Contain("\"sprk_accesslevel\"");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Session 27 round 34 item 3 — the SAME take-over OUTSIDE the grant core: POST set-record-share-expiry (the Manage
    // Access toolbar's record-wide Expiration), the one other BFF writer that changes a grant row and leaves it ACTIVE
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An internal Write holder sets the record's Expiration (shortening every share, the contact-issued one included).
    /// Afterwards the issuing contact can neither lengthen the colleague's row again nor revoke it: the row is the
    /// internal user's (<c>sprk_grantedbycontact</c> cleared, <c>sprk_grantedby</c> = the changing systemuser) — the
    /// "override an operator's deliberate time bound" harm managed_elsewhere exists to prevent. To isolate the take-over,
    /// the grantor's OWN grant is then renewed alone (as an operator might), so the grantor's expiry cap no longer stops
    /// the lengthening by itself.
    /// The twin differs in ONE input — whether the internal change happened: the contact then lengthens and revokes its
    /// own row as usual.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordShareExpiry_ByAnInternalWriteHolder_TakesTheContactIssuedRowOver_SoTheContactCanNeitherLengthenNorRevokeIt(
        bool internalChangeFirst)
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(30), issuedByContact: Grantor);
        var grantorRow = _dataverse.RowsFor(Grantor).Single();

        if (internalChangeFirst)
        {
            var expiry = await SetRecordShareExpiryAsInternalUser(Today.AddDays(10));

            Ok<SetRecordShareExpiryResponse>(expiry).UpdatedCount.Should().Be(2, "both shares of the project take the date");
            _dataverse.BulkUpdates.Should().ContainSingle("ONE all-or-nothing transaction carries the date and the take-over");
            row.ExpiresDate.Should().Be(Today.AddDays(10));
            row.GrantedByContactId.Should().BeNull("the contact issuer is cleared in the same write");
            row.GrantedByContactProvenance.Should().BeNull("…with its recorded provenance (round 50 item 2)");
            row.GrantedBySystemUserId.Should().Be(SystemUserId, "the changing internal user is stamped");
            grantorRow.GrantedByContactId.Should().BeNull("the grantor's own row was never contact-issued");

            grantorRow.ExpiresDate = Today.AddDays(200); // an operator renews the grantor's own grant alone
        }

        var before = Snapshot(row);
        var lengthen = await Grant(Request(ExternalAccessLevel.Collaborate, expiry: Today.AddDays(60)), Ciam(ExternalAccessLevel.Collaborate));

        if (internalChangeFirst)
        {
            Problem(lengthen).Should().Be((409, ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode));
            Snapshot(row).Should().Be(before, "the internal user's date is not lengthened");

            var revoke = await Revoke(row.Id, Ciam(ExternalAccessLevel.Collaborate));
            Problem(revoke).Should().Be((404, ContactGrantorAuthorizationFilter.NotFoundReasonCode));
            row.IsActive.Should().BeTrue("the contact cannot revoke a decision an internal user made");
        }
        else
        {
            Ok<ContactGrantResponse>(lengthen).ExpiryDate.Should().Be(Today.AddDays(60));
            row.ExpiresDate.Should().Be(Today.AddDays(60));
            row.GrantedByContactId.Should().Be(Grantor);

            Ok<ContactGrantRevokeResponse>(await Revoke(row.Id, Ciam(ExternalAccessLevel.Collaborate))).DeactivatedCount.Should().Be(1);
            row.IsActive.Should().BeFalse();
        }
    }

    /// <summary>
    /// The real <c>set-record-share-expiry</c> handler, as an internal Write holder (its Entra object id resolves to
    /// <see cref="SystemUserId"/>), over this table: the share read through the table's interpreted <c>$filter</c>, the
    /// SDK transaction applied to the same rows (<see cref="ContactGrantTable.ApplyBulkUpdate"/>).
    /// </summary>
    private Task<IResult> SetRecordShareExpiryAsInternalUser(DateOnly expiry)
    {
        _dataverse.SystemUsersByOid[InternalUserOid] = SystemUserId;
        var sdk = new Mock<IDataverseService>(MockBehavior.Strict);
        sdk.Setup(d => d.BulkUpdateAsync(
                It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<(Guid id, Dictionary<string, object> fields)>, CancellationToken>(
                (entity, updates, _) => _dataverse.ApplyBulkUpdate(entity, updates))
            .Returns(Task.CompletedTask);

        var internalUser = new DefaultHttpContext
        {
            TraceIdentifier = "trace-140-share-expiry",
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", InternalUserOid.ToString()) }, "test")),
        };

        return SetRecordShareExpiryEndpoint.Handle(
            new SetRecordShareExpiryRequest("project", ProjectId, expiry),
            _dataverse, sdk.Object, _participations, new FixedClock(Today), internalUser,
            NullLogger<Program>.Instance, CancellationToken.None);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Session 27 round 42 item 1 — the race between a contact's write and an internal take-over is CLOSED: every
    // contact-path write to a row it checked was its own is conditional on the version that check read (If-Match). A take-
    // over that commits in between makes the write fail: 409 managed_elsewhere with the existing copy, the row re-read,
    // and no retry. Each case's twin differs in ONE input — whether the internal change landed in the window.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The contact asks to raise and lengthen its own grant; an internal Write holder sets the record's Expiration (a
    /// take-over, round 34 item 3) AFTER the contact's issuer check read the row and BEFORE its PATCH.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grant_WhenAnInternalTakeOverLandsBetweenTheIssuerCheckAndTheWrite_Is409ManagedElsewhere_AndTheTakeOverStands(
        bool takeOverInTheWindow)
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        if (takeOverInTheWindow)
        {
            _dataverse.BeforeConditionalWrite = async () =>
                Ok<SetRecordShareExpiryResponse>(await SetRecordShareExpiryAsInternalUser(Today.AddDays(10)));
        }

        var result = await Grant(Request(ExternalAccessLevel.Collaborate, expiry: Today.AddDays(60)), Ciam(ExternalAccessLevel.Collaborate));

        var write = _dataverse.ConditionalWrites.Should().ContainSingle("one conditional write; a refused one is never retried").Subject;
        if (takeOverInTheWindow)
        {
            Problem(result).Should().Be((409, ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode));
            Detail(result).Should().Be(
                $"{ColleagueEmail} already has access to this record that was granted by someone else; ask them or the record's " +
                "team to change it. Nothing was changed.", "the existing managed_elsewhere copy");
            write.Applied.Should().BeFalse();
            Snapshot(row).Should().Be(((int?)ExternalAccessLevel.ViewOnly, (DateOnly?)Today.AddDays(10), (Guid?)null, (Guid?)SystemUserId, (int?)0),
                "the internal user's decision stands: their date, their stamp, the level the contact did not raise");
            AssertReReadAndNothingAfter(row.Id);
        }
        else
        {
            Ok<ContactGrantResponse>(result).ExpiryDate.Should().Be(Today.AddDays(60));
            write.Applied.Should().BeTrue();
            Snapshot(row).Should().Be(((int?)ExternalAccessLevel.Collaborate, (DateOnly?)Today.AddDays(60), (Guid?)Grantor, (Guid?)null, (int?)0));
        }
    }

    /// <summary>
    /// The contact revokes its own grant; an internal take-over lands after the revoke's "issued by me" read and before
    /// its deactivation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoke_WhenAnInternalTakeOverLandsBetweenTheIssuerCheckAndTheDeactivation_Is409ManagedElsewhere_AndTheRowStays(
        bool takeOverInTheWindow)
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        if (takeOverInTheWindow)
        {
            _dataverse.BeforeConditionalWrite = async () =>
                Ok<SetRecordShareExpiryResponse>(await SetRecordShareExpiryAsInternalUser(Today.AddDays(10)));
        }

        var result = await Revoke(row.Id, Ciam(ExternalAccessLevel.Collaborate));

        var write = _dataverse.ConditionalWrites.Should().ContainSingle().Subject;
        if (takeOverInTheWindow)
        {
            Problem(result).Should().Be((409, ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode));
            Detail(result).Should().Be(GrantPolicyDecision.ManagedElsewhere.Detail, "the existing managed_elsewhere copy");
            write.Applied.Should().BeFalse();
            row.IsActive.Should().BeTrue("a contact never ends a decision an internal user just made");
            row.GrantedBySystemUserId.Should().Be(SystemUserId);
            row.GrantedByContactId.Should().BeNull();
            AssertReReadAndNothingAfter(row.Id);
        }
        else
        {
            Ok<ContactGrantRevokeResponse>(result).DeactivatedCount.Should().Be(1);
            write.Applied.Should().BeTrue();
            row.IsActive.Should().BeFalse();
        }
    }

    /// <summary>
    /// Two rows of the caller's on one grant (a duplicate pair): the take-over lands on the SECOND before the first is
    /// deactivated. The first — still the caller's — is ended; the second is left; the answer is still 409 managed_elsewhere,
    /// with a detail that does not claim "nothing was changed".
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoke_OfADuplicatePair_WhenOneIsTakenOverInTheWindow_EndsOnlyTheOneStillTheirs_AndSaysSo(bool takeOverInTheWindow)
    {
        var first = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        var second = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(20), issuedByContact: Grantor);
        if (takeOverInTheWindow)
        {
            _dataverse.BeforeConditionalWrite = () =>
            {
                TakeOverAsInternalUser(second);
                return Task.CompletedTask;
            };
        }

        var result = await Revoke(first.Id, Ciam(ExternalAccessLevel.Collaborate));

        first.IsActive.Should().BeFalse("the row still the caller's is ended either way");
        _participations.Invalidations.Should().Contain(i => i.Contacts.Contains(Colleague), "a deactivation committed");
        if (takeOverInTheWindow)
        {
            Problem(result).Should().Be((409, ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode));
            Detail(result).Should().Be(ContactGrantEndpoints.PartlyManagedElsewhereDetail);
            second.IsActive.Should().BeTrue();
            second.GrantedBySystemUserId.Should().Be(SystemUserId);
            AssertReReadAndNothingAfter(second.Id);
        }
        else
        {
            Ok<ContactGrantRevokeResponse>(result).DeactivatedCount.Should().Be(2);
            second.IsActive.Should().BeFalse();
        }
    }

    /// <summary>
    /// The contact-issuer mode's duplicate collapse (match path) deactivates only rows still at the version it read: a
    /// duplicate taken over in the window is left, and the caller's own grant still succeeds on the survivor.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grant_CollapsingTheCallersDuplicate_LeavesOneTakenOverInTheWindow(bool takeOverInTheWindow)
    {
        var survivor = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        var duplicate = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(10), issuedByContact: Grantor);
        if (takeOverInTheWindow)
        {
            _dataverse.BeforeConditionalWrite = () =>
            {
                TakeOverAsInternalUser(duplicate);
                return Task.CompletedTask;
            };
        }

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).AccessRecordId.Should().Be(survivor.Id);
        survivor.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        duplicate.IsActive.Should().Be(takeOverInTheWindow, "only a duplicate still at the version read is collapsed");
        if (takeOverInTheWindow)
            duplicate.GrantedBySystemUserId.Should().Be(SystemUserId);
    }

    /// <summary>
    /// The post-create race collapse: a row of the caller's raced onto the key (a double submit) is collapsed only if it is
    /// still at the version the post-create read returned — one taken over in the window is left.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grant_PostCreateCollapse_LeavesARacedRowTakenOverInTheWindow(bool takeOverInTheWindow)
    {
        ExternalGrantRow? raced = null;
        _dataverse.BeforeCreate = () => raced = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly,
            Today.AddDays(10), issuedByContact: Grantor);
        if (takeOverInTheWindow)
        {
            _dataverse.BeforeConditionalWrite = () =>
            {
                TakeOverAsInternalUser(raced!);
                return Task.CompletedTask;
            };
        }

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result);
        _dataverse.ConditionalWrites.Should().ContainSingle(w => w.Id == raced!.Id, "the collapse of the raced row is conditional");
        raced!.IsActive.Should().Be(takeOverInTheWindow);
    }

    /// <summary>A row read without a version cannot be written conditionally — nothing is sent, and the caller gets a message.</summary>
    [Theory]
    [InlineData("grant")]
    [InlineData("revoke")]
    public async Task AContactWrite_ToARowReadWithoutAVersion_SendsNothing_AndIsAProblemWithAMessage(string route)
    {
        var row = _dataverse.Seed(Colleague, ProjectId, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30), issuedByContact: Grantor);
        row.ETag = null;
        var before = Snapshot(row);

        var result = route == "grant"
            ? await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate))
            : await Revoke(row.Id, Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be(route == "grant"
            ? (500, ContactGrantEndpoints.GrantFailedReasonCode)
            : (500, ContactGrantorAuthorizationFilter.RevokeFailedReasonCode));
        Snapshot(row).Should().Be(before);
        _dataverse.Updates.Should().BeEmpty();
    }

    /// <summary>After a refused conditional write on <paramref name="rowId"/>: the row is re-read, and nothing is written again.</summary>
    private void AssertReReadAndNothingAfter(Guid rowId)
    {
        var after = _dataverse.Trail.SkipWhile(e => e != $"if-match:{rowId}:changed").Skip(1).ToList();
        after.Should().Contain($"read:{rowId}", "the row is re-read for the response");
        after.Should().NotContain(e => e.StartsWith("write:", StringComparison.Ordinal) || e.StartsWith("if-match:", StringComparison.Ordinal),
            "there is no blind retry");
    }

    /// <summary>An internal user's take-over of one row (round 34 item 3), through the real helper set-record-share-expiry uses.</summary>
    private void TakeOverAsInternalUser(ExternalGrantRow row)
    {
        var fields = new Dictionary<string, object>();
        ExternalGrantLifecycle.AddInternalTakeOverFields(fields, SystemUserId);
        _dataverse.ApplyBulkUpdate("sprk_externalrecordaccess", new List<(Guid, Dictionary<string, object>)> { (row.Id, fields) });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A matter root goes through the same rights dispatch
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_OnAMatterTheContactCollaboratesOn_IsWritten_AndOnAProjectTheyDoNot_Is403()
    {
        _dataverse.Seed(Grantor, MatterId, (int)ExternalAccessLevel.Collaborate, Today.AddDays(200), issuedByContact: null, onMatter: true);
        var principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Grantor,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterId] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate) },
        };

        var onMatter = await Grant(Request(ExternalAccessLevel.ViewOnly, recordType: "matter", recordId: MatterId), principal);
        var onProject = await ThroughFilter(Request(ExternalAccessLevel.ViewOnly), principal);

        Ok<ContactGrantResponse>(onMatter);
        _dataverse.RowsFor(Colleague).Should().ContainSingle().Which.MatterId.Should().Be(MatterId);
        Problem(onProject).Should().Be((403, ContactGrantorAuthorizationFilter.LevelInsufficientReasonCode));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The scoped colleague-by-email read (verifier r1 items 3/11): the pure projection and the query text
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The production projection decides who a colleague is, whatever the query returned: an active contact at the
    /// address (case-insensitive, trimmed) with a CONFERRING membership of a named organization. Each excluded row
    /// differs from the included one in exactly one input.
    /// </summary>
    [Fact]
    public void ProjectColleaguesByEmail_KeepsOnlyActiveContactsAtTheAddressWithAConferringMembership()
    {
        var today = new DateOnly(2026, 10, 4);
        var ids = Enumerable.Range(1, 9).Select(i => Guid.Parse($"e1400000-0000-0000-0000-00000000000{i}")).ToArray();
        ExternalParticipationService.ColleagueMembershipRow Row(Guid contact, Guid org, string? email = ColleagueEmail,
            int contactState = 0, bool expanded = true, DateOnly? end = null, int orgState = 0) => new()
        {
            ContactId = contact,
            OrganizationId = org,
            StateCode = 0,
            EndDate = end,
            Organization = new ExternalParticipationService.OrganizationStateRow { StateCode = orgState },
            Contact = expanded ? new ExternalParticipationService.ColleagueContactRow { Email = email, StateCode = contactState } : null,
        };

        var rows = new[]
        {
            Row(ids[0], FirmA),                                        // the colleague
            Row(ids[1], FirmA, email: " COLLEAGUE@firm-a.example "),   // same address, other case + spaces: Dataverse equality
            Row(ids[2], FirmA, contactState: 1),                       // inactive contact
            Row(ids[3], FirmA, email: "someone.else@firm-a.example"),  // another address
            Row(ids[4], FirmA, expanded: false),                       // contact did not come back
            Row(ids[5], FirmB),                                        // an organization not named
            Row(ids[6], FirmA, end: today.AddDays(-1)),                // membership ended
            Row(ids[7], FirmA, orgState: 1),                           // organization inactive
            Row(ids[8], FirmA, end: today.AddDays(-1)), Row(ids[8], FirmA), // one ended + one current row: a member
        };

        ExternalParticipationService.ProjectColleaguesByEmail(rows, new[] { FirmA }, ColleagueEmail, today)
            .Should().Equal(ids[0], ids[1], ids[8]);
    }

    /// <summary>
    /// The query names exactly the organizations given, the wall state clause, the contact's ACTIVE state and the
    /// email as an escaped OData literal — a quote cannot end the literal (session 27 round 16 item 3).
    /// </summary>
    [Fact]
    public void BuildColleagueByEmailFilter_NamesTheOrganizations_AndEscapesTheEmail()
    {
        var filter = ExternalParticipationService.BuildColleagueByEmailFilter(new[] { FirmA, FirmB }, " o'brien@firm-a.example ");

        filter.Should().StartWith($"(_sprk_organization_value eq {FirmA:D} or _sprk_organization_value eq {FirmB:D})");
        filter.Should().Contain(ExternalParticipationService.WallMembershipStateClause);
        filter.Should().Contain("sprk_Contact/statecode eq 0");
        filter.Should().Contain("sprk_Contact/emailaddress1 eq 'o%27%27brien%40firm-a.example'",
            "the quote is doubled, then URL-encoded; the email is trimmed");
        filter.Should().NotContain("o'brien");
    }

    /// <summary>A grantor with more organizations than one query names is read in chunks — every organization is read.</summary>
    [Fact]
    public async Task Grant_ByEmail_ForAGrantorInManyOrganizations_ReadsEveryOrganization()
    {
        var many = Enumerable.Range(1, ExternalParticipationService.ColleagueOrganizationChunkSize + 3)
            .Select(i => Guid.Parse($"0a140000-0000-0000-0001-{i:D12}")).ToArray();
        _participations.ContactOrganizations[Grantor] = many;
        _participations.ContactOrganizations[Colleague] = new[] { many[^1] };

        var result = await Grant(ByEmail(ColleagueEmail), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result).GranteeContactId.Should().Be(Colleague, "the colleague's firm is in the LAST chunk");
        _participations.ColleagueByEmailReads.SelectMany(r => r.Organizations).Should().BeEquivalentTo(many);
        _participations.ColleagueByEmailReads.Should().HaveCount(2);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────────────

    internal static ContactGrantRequest Request(
        ExternalAccessLevel level, Guid? grantee = null, DateOnly? expiry = null, string recordType = "project", Guid? recordId = null)
        => new(recordType, recordId ?? ProjectId, grantee ?? Colleague, null, level, expiry);

    private static ContactGrantRequest ByEmail(string email)
        => new("project", ProjectId, null, email, ExternalAccessLevel.ViewOnly, null);

    internal static CallerPrincipal Ciam(ExternalAccessLevel? level) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Grantor,
        ProjectAccess = level is null
            ? Array.Empty<CallerProjectAccess>()
            : new[] { CallerProjectAccess.FromLevel(ProjectId, level) },
    };

    private static CallerPrincipal WorkforceContact(ExternalAccessLevel level, bool undatedTermOnProjects = false) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Grantor,
        SystemUserId = null,
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(ProjectId, level) },
        UndatedAccessTermEntityTypes = undatedTermOnProjects
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_project" }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    };

    internal static CallerPrincipal SystemUser(ExternalAccessLevel level) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Grantor,            // task 141's user↔contact link — the KIND still decides
        SystemUserId = SystemUserId,
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(ProjectId, level) },
    };

    private static RootRecordFlags DirectOnly(string kind) => kind == "secure"
        ? new RootRecordFlags(IsSecure: true, IsRestricted: false)
        : new RootRecordFlags(IsSecure: false, IsRestricted: false, IsLimited: true);

    private static ExternalParticipationService.ContactOrgRow Membership(Guid organizationId, DateOnly today, string shape) => new()
    {
        OrganizationId = organizationId,
        StateCode = shape == "inactive" ? 1 : 0,
        StartDate = shape == "not-started" ? today.AddDays(5) : today.AddDays(-30),
        EndDate = shape == "ended" ? today.AddDays(-1) : null,
        Organization = new ExternalParticipationService.OrganizationStateRow { StateCode = shape == "organization-inactive" ? 1 : 0 },
    };

    /// <summary>
    /// The grantor's principal composed by the PRODUCTION CIAM strategy over the PRODUCTION evaluator (task 135 parity):
    /// a project grant at <paramref name="level"/>, direct or organization-inherited only.
    /// </summary>
    private async Task<CallerPrincipal> ComposedCiamPrincipalAsync(Guid contactId, ExternalAccessLevel level, bool direct)
    {
        _participations.GrantSets[contactId] = new ExternalGrantSet
        {
            Projects = new[]
            {
                new ExternalParticipation { ProjectId = ProjectId, AccessLevel = level, DirectAccessLevel = direct ? level : null },
            },
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        };

        var identities = new InMemoryContactIdentityStore();
        var oid = Guid.NewGuid().ToString("D");
        identities.AddContact(contactId, oid: oid, plane: IdentityPlaneMarker.External);
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(identities), DenyList(), NullLogger<CiamContactPrincipalStrategy>.Instance);

        var resolution = await strategy.ResolveAsync(
            new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", oid) }, "test")) },
            CancellationToken.None);
        resolution.IsResolved.Should().BeTrue();
        return resolution.Principal!;
    }

    private AccessibleRecordSetService DenyList() => GrantPolicyTestDoubles.RealDenyList(_participations, _denyReader);

    private ContactGrantorAuthorizationFilter Filter() => new(
        _participations, _dataverse, NullLogger<ContactGrantorAuthorizationFilter>.Instance);

    /// <summary>The route as the pipeline runs it: the filter, then (when it passes) the handler.</summary>
    private async Task<IResult> ThroughFilter(object request, CallerPrincipal principal)
    {
        var httpContext = Context(principal);
        var context = new DefaultEndpointFilterInvocationContext(httpContext, request);
        var result = await Filter().InvokeAsync(context, async _ => request switch
        {
            ContactGrantRequest g => await HandlerGrant(g, httpContext),
            ContactGrantListQuery l => await ContactGrantEndpoints.ListAsync(l, _dataverse, _participations, httpContext,
                NullLogger<ContactGrantorAuthorizationFilter>.Instance, CancellationToken.None),
            ContactGrantRevokeRequest r => await ContactGrantEndpoints.RevokeAsync(r, _dataverse, _participations, httpContext,
                NullLogger<ContactGrantorAuthorizationFilter>.Instance, CancellationToken.None),
            _ => Results.StatusCode(599),
        });
        return (IResult)result!;
    }

    private Task<IResult> Grant(ContactGrantRequest request, CallerPrincipal principal)
        => ThroughFilter(request, principal);

    private Task<IResult> Revoke(Guid accessRecordId, CallerPrincipal principal)
        => ThroughFilter(new ContactGrantRevokeRequest(accessRecordId), principal);

    private Task<IResult> HandlerGrant(ContactGrantRequest request, HttpContext httpContext)
        => ContactGrantEndpoints.GrantAsync(
            request, _dataverse, _participations, DenyList(), _identities, httpContext,
            NullLogger<ContactGrantorAuthorizationFilter>.Instance, new FixedClock(Today), CancellationToken.None);

    internal static HttpContext Context(CallerPrincipal principal)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-140" };
        context.Items[CallerPrincipal.HttpContextItemsKey] = principal;
        return context;
    }

    private void AssertNothingWritten()
    {
        _dataverse.Creates.Should().BeEmpty("no grant row (and no contact) is created");
        _dataverse.Updates.Should().BeEmpty("no row is changed");
        AssertNoMembershipWrites();
    }

    /// <summary>Owner G1 (a) / escalation trigger 6: no code path on these routes writes an organization membership.</summary>
    private void AssertNoMembershipWrites()
    {
        _dataverse.Creates.Should().NotContain(c => c.Set == "sprk_contactorganizations");
        _dataverse.Updates.Should().NotContain(u => u.Set == "sprk_contactorganizations");
    }

    private static (int? Level, DateOnly? Expiry, Guid? ByContact, Guid? BySystemUser, int? State) Snapshot(ExternalGrantRow row)
        => (row.AccessLevel, row.ExpiresDate, row.GrantedByContactId, row.GrantedBySystemUserId, row.StateCode);

    internal static T Ok<T>(IResult result) => result.Should().BeOfType<Ok<T>>().Subject.Value!;

    internal static (int Status, string? ReasonCode) Problem(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions.Should().ContainKey("traceId");
        problem.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace("every refusal carries a message");
        return (problem.StatusCode,
            problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string : null);
    }

    internal static string? Detail(IResult result) => result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Detail;

    private sealed record UnknownGrantDto(Guid RecordId);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateOnly today) => _now = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

/// <summary>
/// An in-memory <c>sprk_externalrecordaccess</c> table behind the real client's <c>virtual</c> seams (task 140). Grant
/// queries are answered by INTERPRETING the production <c>$filter</c> — root, contact, organization-wide, issuer and
/// state clauses — so a wrong predicate fails here rather than passing against canned rows. It also answers the two
/// side reads the contact routes make: grantee names (<c>contacts</c>) and a firm's state (<c>sprk_organizations</c>).
/// </summary>
internal sealed class ContactGrantTable : DataverseWebApiClient
{
    public const string GrantSet = "sprk_externalrecordaccesses";

    private readonly List<ExternalGrantRow> _rows = new();
    private int _seq;

    public ContactGrantTable()
        : base(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                .Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            NoCredential.Instance)
    {
    }

    public List<(string Set, object Payload)> Creates { get; } = new();
    public List<IDictionary<string, object?>> CreatePayloads { get; } = new();
    public List<(string Set, Guid Id, string Payload)> Updates { get; } = new();
    public Dictionary<Guid, (string FullName, string Email)> Names { get; } = new();
    public Dictionary<Guid, int> OrganizationStates { get; } = new();
    public bool FailUpdates { get; set; }
    public bool FailRetrieve { get; set; }

    /// <summary>Entra object id → systemuser id: the <c>systemusers</c> read behind the internal take-over stamp.</summary>
    public Dictionary<Guid, Guid> SystemUsersByOid { get; } = new();

    /// <summary>Every <c>IGenericEntityService.BulkUpdateAsync</c> applied through <see cref="ApplyBulkUpdate"/>.</summary>
    public List<(string Entity, List<(Guid Id, Dictionary<string, object> Fields)> Updates)> BulkUpdates { get; } = new();

    /// <summary>
    /// Every conditional write (<see cref="UpdateIfMatchAsync"/>): the row, the version sent as <c>If-Match</c>, and whether
    /// it applied (false = the row had changed, HTTP 412 in production). Also recorded in <see cref="Updates"/> when applied.
    /// </summary>
    public List<(Guid Id, string ETag, bool Applied)> ConditionalWrites { get; } = new();

    /// <summary>Every grant row read by id (<see cref="RetrieveAsync{T}"/>) — the re-read after a refused write shows here.</summary>
    public List<Guid> GrantReadsById { get; } = new();

    /// <summary>
    /// The ordered trail of grant-row I/O by id: <c>read:{id}</c>, <c>write:{id}</c> (unconditional), <c>if-match:{id}:applied</c>
    /// / <c>if-match:{id}:changed</c> — so a test can assert what happened AFTER a refused conditional write.
    /// </summary>
    public List<string> Trail { get; } = new();

    /// <summary>
    /// Runs ONCE, just before the next conditional write is judged — to stage a concurrent writer (an internal take-over)
    /// committing in the window between a contact's "is this row mine?" read and its write (session 27 round 42 item 1).
    /// </summary>
    public Func<Task>? BeforeConditionalWrite { get; set; }

    private long _version;

    /// <summary>A fresh row version, <c>W/"n"</c> — what Dataverse returns as <c>@odata.etag</c> and bumps on every write.</summary>
    private string NextVersion() => $"W/\"{++_version}\"";

    public IReadOnlyList<ExternalGrantRow> RowsFor(Guid contactId)
        => _rows.Where(r => r.ContactId == contactId && r.IsActive).ToList();

    public void Clear() => _rows.Clear();

    public void Reset()
    {
        _rows.Clear();
        Creates.Clear();
        CreatePayloads.Clear();
        Updates.Clear();
        Names.Clear();
        OrganizationStates.Clear();
        SystemUsersByOid.Clear();
        BulkUpdates.Clear();
        ConditionalWrites.Clear();
        GrantReadsById.Clear();
        Trail.Clear();
        FailUpdates = FailRetrieve = false;
        BeforeCreate = null;
        BeforeConditionalWrite = null;
    }

    /// <summary>
    /// Applies an SDK bulk write (<c>IGenericEntityService.BulkUpdateAsync</c> — LOGICAL names and SDK value types, as
    /// <c>DataverseServiceClientImpl.BuildBulkUpdateTransaction</c> hands them on) to this table: a C# <c>null</c> is
    /// skipped, <see cref="DBNull.Value"/> clears, a lookup is an <see cref="Microsoft.Xrm.Sdk.EntityReference"/> of the
    /// lookup's own target. A column or target this fake does not model THROWS — it never ignores a write.
    /// </summary>
    public void ApplyBulkUpdate(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates)
    {
        if (entityLogicalName != "sprk_externalrecordaccess")
            throw new InvalidOperationException($"The fake models bulk writes to sprk_externalrecordaccess only, not '{entityLogicalName}'.");

        BulkUpdates.Add((entityLogicalName, updates.Select(u => (u.id, new Dictionary<string, object>(u.fields))).ToList()));
        foreach (var (id, fields) in updates)
        {
            var row = _rows.Single(r => r.Id == id);
            row.ETag = NextVersion();
            foreach (var (column, value) in fields)
            {
                switch (column)
                {
                    case "sprk_expiresdate": row.ExpiresDate = DateOnly.FromDateTime((DateTime)value); break;
                    case "sprk_grantedbycontact": row.GrantedByContactId = LookupId(value, "contact"); break;
                    case "sprk_grantedbycontactid":
                        row.GrantedByContactProvenance = value switch
                        {
                            DBNull => null,
                            string text => text,
                            _ => throw new InvalidOperationException($"sprk_grantedbycontactid (text) was written as {value.GetType().Name} {value}."),
                        };
                        break;
                    case "sprk_grantedby": row.GrantedBySystemUserId = LookupId(value, "systemuser"); break;
                    default: throw new InvalidOperationException($"The fake does not model a bulk write of '{column}'.");
                }
            }
        }

        static Guid? LookupId(object value, string target) => value switch
        {
            DBNull => null,
            Microsoft.Xrm.Sdk.EntityReference reference when reference.LogicalName == target => reference.Id,
            _ => throw new InvalidOperationException($"A '{target}' lookup was written as {value.GetType().Name} {value}."),
        };
    }

    public ExternalGrantRow Seed(Guid contactId, Guid rootId, int level, DateOnly expiry,
        Guid? issuedByContact, Guid? issuedBySystemUser = null, bool onMatter = false, Guid? id = null)
    {
        var row = new ExternalGrantRow
        {
            Id = id ?? NextId(),
            ContactId = contactId,
            ProjectId = onMatter ? null : rootId,
            MatterId = onMatter ? rootId : null,
            AccessLevel = level,
            ExpiresDate = expiry,
            StateCode = 0,
            GrantedByContactId = issuedByContact,
            // As the BFF writes it (session 27 round 50 item 2): the issuer's id as text beside the lookup, in the same write.
            // A test models the contact's DELETION by emptying the lookup alone (its RemoveLink cascade).
            GrantedByContactProvenance = issuedByContact is { } issuer ? ExternalGrantLifecycle.ContactIssuerProvenance(issuer) : null,
            GrantedBySystemUserId = issuedBySystemUser,
            ETag = NextVersion(),
        };
        _rows.Add(row);
        return row;
    }

    public ExternalGrantRow SeedOrganization(Guid organizationId, Guid projectId, int level, DateOnly expiry)
    {
        var row = new ExternalGrantRow
        {
            Id = NextId(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            AccessLevel = level,
            ExpiresDate = expiry,
            StateCode = 0,
            ETag = NextVersion(),
        };
        _rows.Add(row);
        return row;
    }

    private Guid NextId() => Guid.Parse($"eeeeeeee-0140-0000-0000-{++_seq:D12}");

    public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
        int? top = null, int? skip = null, CancellationToken cancellationToken = default)
    {
        object rows = entitySetName switch
        {
            GrantSet => MatchGrants(filter ?? string.Empty),
            "contacts" => Names
                .Where(kv => (filter ?? string.Empty).Contains(kv.Key.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(kv => new Dictionary<string, object?> { ["contactid"] = kv.Key, ["fullname"] = kv.Value.FullName, ["emailaddress1"] = kv.Value.Email })
                .ToList(),
            "systemusers" => MatchSystemUsers(filter ?? string.Empty),
            _ => Array.Empty<object>(),
        };

        return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
    }

    /// <remarks>
    /// Mirrors the production client: a row that does not exist is a 404 <see cref="HttpRequestException"/>
    /// (<c>EnsureSuccessStatusCode</c>), never a null.
    /// </remarks>
    public override Task<T?> RetrieveAsync<T>(string entitySetName, Guid id, string? select = null, CancellationToken cancellationToken = default)
        where T : default
    {
        if (FailRetrieve)
            throw new HttpRequestException("Simulated Dataverse read failure.", null, HttpStatusCode.ServiceUnavailable);

        if (entitySetName == GrantSet)
        {
            GrantReadsById.Add(id);
            Trail.Add($"read:{id}");
        }

        object? row = entitySetName switch
        {
            GrantSet => _rows.FirstOrDefault(r => r.Id == id),
            "sprk_organizations" => OrganizationStates.TryGetValue(id, out var state)
                ? new Dictionary<string, object?> { ["statecode"] = state }
                : null,
            _ => null,
        };

        if (row is null)
            throw new HttpRequestException("Not found.", null, HttpStatusCode.NotFound);

        return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row)));
    }

    /// <summary>Runs just before a grant row is created — to stage a concurrent writer racing the same key.</summary>
    public Action? BeforeCreate { get; set; }

    public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
    {
        BeforeCreate?.Invoke();
        Creates.Add((entitySetName, entity));
        if (entitySetName != GrantSet)
            return Task.FromResult(Guid.NewGuid());

        var payload = (IDictionary<string, object?>)entity;
        CreatePayloads.Add(payload);
        var row = new ExternalGrantRow
        {
            Id = NextId(),
            ContactId = BoundId(payload, "sprk_Contact@odata.bind"),
            OrganizationId = BoundId(payload, "sprk_Organization@odata.bind"),
            ProjectId = BoundId(payload, "sprk_Project@odata.bind"),
            MatterId = BoundId(payload, "sprk_Matter@odata.bind"),
            WorkAssignmentId = BoundId(payload, "sprk_WorkAssignment@odata.bind"),
            GrantedByContactId = BoundId(payload, "sprk_GrantedByContact@odata.bind"),
            GrantedByContactProvenance = payload.TryGetValue("sprk_grantedbycontactid", out var provenance) ? (string?)provenance : null,
            GrantedBySystemUserId = BoundId(payload, "sprk_GrantedBy@odata.bind"),
            AccessLevel = (int?)payload["sprk_accesslevel"],
            ExpiresDate = payload.TryGetValue("sprk_expiresdate", out var e) && e is string s ? DateOnly.Parse(s) : null,
            StateCode = 0,
            ETag = NextVersion(),
        };
        _rows.Add(row);
        return Task.FromResult(row.Id);
    }

    public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
    {
        if (FailUpdates)
            throw new HttpRequestException("Simulated Dataverse write failure.", null, HttpStatusCode.ServiceUnavailable);

        var json = JsonSerializer.Serialize(entity);
        Updates.Add((entitySetName, id, json));
        Trail.Add($"write:{id}");
        Apply(id, json);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The production contract of <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>: no version → nothing sent
    /// (<see cref="ArgumentException"/>); a missing row → <see cref="KeyNotFoundException"/>; a version that no longer
    /// matches → <see cref="System.Data.DBConcurrencyException"/> and nothing written; otherwise applied, and the row's
    /// version moves on.
    /// </summary>
    public override async Task UpdateIfMatchAsync(string entitySetName, Guid id, object entity, string etag,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(etag))
            throw new ArgumentException("A conditional update needs the row's ETag as it was read; nothing was sent.", nameof(etag));

        if (BeforeConditionalWrite is { } concurrentWriter)
        {
            BeforeConditionalWrite = null;
            await concurrentWriter();
        }

        if (FailUpdates)
            throw new HttpRequestException("Simulated Dataverse write failure.", null, HttpStatusCode.ServiceUnavailable);

        var row = _rows.FirstOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException($"{entitySetName}({id}) was not found; nothing was created.");

        if (row.ETag != etag)
        {
            ConditionalWrites.Add((id, etag, false));
            Trail.Add($"if-match:{id}:changed");
            throw new System.Data.DBConcurrencyException($"{entitySetName}({id}) changed since it was read; the update was not applied.");
        }

        ConditionalWrites.Add((id, etag, true));
        Trail.Add($"if-match:{id}:applied");
        var json = JsonSerializer.Serialize(entity);
        Updates.Add((entitySetName, id, json));
        Apply(id, json);
    }

    private void Apply(Guid id, string json)
    {
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row is not null)
        {
            row.ETag = NextVersion();
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "statecode": row.StateCode = p.Value.GetInt32(); break;
                    case "sprk_accesslevel": row.AccessLevel = p.Value.GetInt32(); break;
                    case "sprk_expiresdate": row.ExpiresDate = DateOnly.Parse(p.Value.GetString()!); break;
                    case "sprk_GrantedByContact@odata.bind":
                        row.GrantedByContactId = p.Value.ValueKind == JsonValueKind.Null ? null : IdIn(p.Value.GetString()!);
                        break;
                    case "sprk_grantedbycontactid":
                        row.GrantedByContactProvenance = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetString();
                        break;
                    case "sprk_GrantedBy@odata.bind":
                        row.GrantedBySystemUserId = p.Value.ValueKind == JsonValueKind.Null ? null : IdIn(p.Value.GetString()!);
                        break;
                }
            }
        }
    }

    private IEnumerable<ExternalGrantRow> MatchGrants(string filter)
    {
        IEnumerable<ExternalGrantRow> rows = _rows;

        var root = Regex.Match(filter, @"_sprk_(project|matter|workassignment)_value eq ([0-9a-fA-F-]{36})");
        if (root.Success)
        {
            var rootId = Guid.Parse(root.Groups[2].Value);
            rows = root.Groups[1].Value switch
            {
                "project" => rows.Where(r => r.ProjectId == rootId),
                "matter" => rows.Where(r => r.MatterId == rootId),
                _ => rows.Where(r => r.WorkAssignmentId == rootId),
            };
        }

        if (filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal))
        {
            var org = Guid.Parse(Regex.Match(filter, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
            rows = rows.Where(r => r.ContactId is null && r.OrganizationId == org);
        }
        else if (Regex.Match(filter, @"_sprk_contact_value eq ([0-9a-fA-F-]{36})") is { Success: true } contact)
        {
            var contactId = Guid.Parse(contact.Groups[1].Value);
            rows = rows.Where(r => r.ContactId == contactId);
        }

        if (Regex.Match(filter, @"_sprk_grantedbycontact_value eq ([0-9a-fA-F-]{36})") is { Success: true } issuer)
        {
            var issuerId = Guid.Parse(issuer.Groups[1].Value);
            rows = rows.Where(r => r.GrantedByContactId == issuerId);
        }

        if (filter.Contains("statecode eq 0", StringComparison.Ordinal))
            rows = rows.Where(r => r.IsActive);

        return rows.ToList();
    }

    /// <summary>The internal caller's systemuser by Entra object id — the one clause the issuer-stamp lookup sends (strict).</summary>
    private List<Dictionary<string, object?>> MatchSystemUsers(string filter)
    {
        var oid = Regex.Match(filter, @"^azureactivedirectoryobjectid eq ([0-9a-fA-F-]{36})$");
        if (!oid.Success)
            throw new InvalidOperationException($"The fake does not understand the systemusers filter '{filter}'.");

        return SystemUsersByOid.TryGetValue(Guid.Parse(oid.Groups[1].Value), out var systemUserId)
            ? new List<Dictionary<string, object?>> { new() { ["systemuserid"] = systemUserId } }
            : new List<Dictionary<string, object?>>();
    }

    private static Guid? BoundId(IDictionary<string, object?> payload, string key)
        => payload.TryGetValue(key, out var value) && value is string s ? IdIn(s) : null;

    private static Guid IdIn(string bind) => Guid.Parse(Regex.Match(bind, @"\(([0-9a-fA-F-]{36})\)").Groups[1].Value);

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }
}

/// <summary>
/// The contact-side routes THROUGH THE HOST (task 140): the ExternalCollaboration group, its caller-principal filter (the
/// resolver substituted at its interface), the route filter attachment, JSON binding of the closed request DTO, and
/// authentication. Module-boundary doubles only — the same table, participation reads, deny reader and identity store
/// as the handler-level tests above.
/// </summary>
public sealed class ContactGrantRouteFixture : ExternalCollaborationTestFixture
{
    internal ContactGrantTable Table { get; } = new();
    internal GrantPolicyTestDoubles.FlagStubParticipationService Participations { get; } = new(RootRecordFlags.None);
    internal GrantPolicyTestDoubles.SeamNoAccessListReader DenyReader { get; } = new();
    internal InMemoryContactIdentityStore Identities { get; } = new();
    internal ExternalTodoScopeTestFixture.StubCallerPrincipalResolver Resolver { get; } = new();

    internal void Reset()
    {
        Table.Reset();
        Identities.Reset();
        Participations.ContactOrganizations.Clear();
        Participations.Invalidations.Clear();
        Resolver.Principal = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ICallerPrincipalResolver>(_ => Resolver);
            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton<DataverseWebApiClient>(Table);
            services.AddScoped<ExternalParticipationService>(_ => Participations);
            services.RemoveAll<IAccessibleRecordSetService>();
            services.AddScoped<IAccessibleRecordSetService>(_ => GrantPolicyTestDoubles.RealDenyList(Participations, DenyReader));
            services.RemoveAll<IContactIdentityStore>();
            services.AddSingleton<IContactIdentityStore>(Identities);
            // Production answers a malformed body with a plain 400 (RouteHandlerOptions.ThrowOnBadRequest is false
            // outside Development); the test host runs as Development, so pin the production value.
            services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
        });
    }
}

/// <summary>Task 140 — the routes through the host (criteria 4 and 6).</summary>
public sealed class ContactGrantRouteTests : IClassFixture<ContactGrantRouteFixture>
{
    private readonly ContactGrantRouteFixture _fixture;

    public ContactGrantRouteTests(ContactGrantRouteFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Participations.ContactOrganizations[ContactGrantAuthorizationTests.Grantor] = new[] { ContactGrantAuthorizationTests.FirmA };
        _fixture.Participations.ContactOrganizations[ContactGrantAuthorizationTests.Colleague] = new[] { ContactGrantAuthorizationTests.FirmA };
        _fixture.Identities.AddContact(ContactGrantAuthorizationTests.Colleague, email: ContactGrantAuthorizationTests.ColleagueEmail);
        _fixture.Table.Seed(ContactGrantAuthorizationTests.Grantor, ContactGrantAuthorizationTests.ProjectId,
            (int)ExternalAccessLevel.FullAccess, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200), issuedByContact: null);
    }

    private static string Body(bool withOrganization) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["recordType"] = "project",
        ["recordId"] = ContactGrantAuthorizationTests.ProjectId,
        ["granteeContactId"] = ContactGrantAuthorizationTests.Colleague,
        ["accessLevel"] = (int)ExternalAccessLevel.ViewOnly,
    }.Concat(withOrganization
        ? new[] { new KeyValuePair<string, object?>("organizationId", ContactGrantAuthorizationTests.FirmA) }
        : Array.Empty<KeyValuePair<string, object?>>()).ToDictionary(kv => kv.Key, kv => kv.Value));

    private Task<HttpResponseMessage> Post(HttpClient client, string body)
        => client.PostAsync("/api/v1/external/contact-grants", new StringContent(body, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Grant_ThroughTheRoute_ByAQualifyingContact_Is200_AndWritesTheRow()
    {
        _fixture.Resolver.Principal = ContactGrantAuthorizationTests.Ciam(ExternalAccessLevel.Collaborate);

        var response = await Post(_fixture.CreateAuthenticatedClient(), Body(withOrganization: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Table.RowsFor(ContactGrantAuthorizationTests.Colleague).Should().ContainSingle()
            .Which.GrantedByContactId.Should().Be(ContactGrantAuthorizationTests.Grantor);
    }

    /// <summary>
    /// Criterion 6, end to end: the DTO is closed, so a body naming an organization is refused — not bound with the field
    /// silently dropped — and nothing is written. The twin above differs only in that member.
    /// </summary>
    [Fact]
    public async Task Grant_ThroughTheRoute_WithAnOrganizationIdMember_Is400_AndWritesNothing()
    {
        _fixture.Resolver.Principal = ContactGrantAuthorizationTests.Ciam(ExternalAccessLevel.Collaborate);

        var response = await Post(_fixture.CreateAuthenticatedClient(), Body(withOrganization: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Table.Creates.Should().BeEmpty();
        _fixture.Table.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryRoute_ThroughTheRoute_ForASystemUser_Is403UseManageAccess_AndUnauthenticatedIs401()
    {
        _fixture.Resolver.Principal = ContactGrantAuthorizationTests.SystemUser(ExternalAccessLevel.FullAccess);
        var client = _fixture.CreateAuthenticatedClient();

        var grant = await Post(client, Body(withOrganization: false));
        var list = await client.GetAsync($"/api/v1/external/contact-grants?recordType=project&recordId={ContactGrantAuthorizationTests.ProjectId}");
        var revoke = await client.PostAsync("/api/v1/external/contact-grants/revoke",
            new StringContent(JsonSerializer.Serialize(new { accessRecordId = Guid.NewGuid() }), Encoding.UTF8, "application/json"));

        foreach (var response in new[] { grant, list, revoke })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain(ContactGrantorAuthorizationFilter.UseManageAccessReasonCode);
        }

        var anonymous = await Post(_fixture.CreateUnauthenticatedClient(), Body(withOrganization: false));
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.Table.Creates.Should().BeEmpty();
    }
}
