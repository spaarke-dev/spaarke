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
        row.GrantedBySystemUserId.Should().BeNull("sprk_grantedby is a systemuser lookup and stays empty");
        row.ExpiresDate.Should().Be(Today.AddDays(90));
        _dataverse.CreatePayloads.Single().Keys.Should().NotContain("sprk_GrantedBy@odata.bind");
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

    [Fact]
    public async Task Grant_WhenTheGranteeLookupCannotBeRead_Is503()
    {
        _identities.EmailLookupStatus = LookupStatus.Failed;

        var result = await Grant(ByEmail(ColleagueEmail), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((503, ContactGrantorAuthorizationFilter.MembershipUnreadableReasonCode));
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
        FailUpdates = FailRetrieve = false;
        BeforeCreate = null;
    }

    public ExternalGrantRow Seed(Guid contactId, Guid rootId, int level, DateOnly expiry,
        Guid? issuedByContact, Guid? issuedBySystemUser = null, bool onMatter = false)
    {
        var row = new ExternalGrantRow
        {
            Id = NextId(),
            ContactId = contactId,
            ProjectId = onMatter ? null : rootId,
            MatterId = onMatter ? rootId : null,
            AccessLevel = level,
            ExpiresDate = expiry,
            StateCode = 0,
            GrantedByContactId = issuedByContact,
            GrantedBySystemUserId = issuedBySystemUser,
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
            GrantedBySystemUserId = BoundId(payload, "sprk_GrantedBy@odata.bind"),
            AccessLevel = (int?)payload["sprk_accesslevel"],
            ExpiresDate = payload.TryGetValue("sprk_expiresdate", out var e) && e is string s ? DateOnly.Parse(s) : null,
            StateCode = 0,
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

        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row is not null)
        {
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
                    case "sprk_GrantedBy@odata.bind":
                        row.GrantedBySystemUserId = p.Value.ValueKind == JsonValueKind.Null ? null : IdIn(p.Value.GetString()!);
                        break;
                }
            }
        }

        return Task.CompletedTask;
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
