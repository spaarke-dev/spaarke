using System.Security.Claims;
using System.Text.RegularExpressions;
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
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Internal system-user shares — spec FR-29, task 063: the endpoints behind the Manage Access "+ User" picker.
///
/// <para><b>What these tests protect.</b> A share ends at EXACTLY the requested level (an existing share is changed
/// with ModifyAccess, never widened by GrantAccess); a failed read is a refusal, never "no share"; every write is
/// confirmed by reading the stored mask back; an unshare of nothing writes nothing; only an existing, enabled,
/// internal person can receive a share, while any existing user's share stays removable; and the affected user's
/// impersonated root-set cache is cleared after every write attempt.</para>
///
/// <para><b>Why the doubles are strict.</b> <see cref="FakeRecordShareTable"/> models GrantAccess on an existing share
/// as ADDITIVE and refuses ModifyAccess or RevokeAccess for a principal with no share — the unsafe readings of the
/// cases Microsoft Learn leaves undocumented — so a handler that relied on either would fail here rather than in
/// production. The systemuser double answers only the <c>$filter</c> clause shape the handler is meant to send and
/// returns ONLY the selected columns, so a <c>$select</c> that lost a column the eligibility check needs reads as a
/// missing value.</para>
///
/// <para>The masks are asserted as LITERALS in Dataverse's own AccessRights values (Read 1, Write 2, Append 4,
/// AppendTo 16, Delete 65536, Share 262144, Assign 524288): a test that derived them from
/// <see cref="RecordShareLevels"/> would pass whatever that table said.</para>
///
/// Placement: <c>tests/integration/auth/**</c> — the ADR-038 security-auth KEEP path. The delegation gate on these
/// routes is pinned in <see cref="DelegationRuleCharacterizationTests"/>; the wire shape in the contract tests.
/// </summary>
public class InternalUserShareTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000cc";
    private const string MatterTable = "sprk_matter";

    private static readonly Guid MatterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherMatterId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InheritedOnlyUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TeamId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CallerOid = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // Dataverse's stored masks, as literals (owner 2026-09-30, task 139: Collaborate and Full Access carry Share).
    private const int ViewOnlyMask = 1;              // Read
    private const int CollaborateMask = 262167;      // Read 1 + Write 2 + Append 4 + AppendTo 16 + Share 262144
    private const int FullAccessMask = 327703;       // Collaborate + Delete 65536
    private const int LegacyCollaborateMask = 23;    // Collaborate before task 139: Read + Write + Append + AppendTo
    private const int LegacyFullAccessMask = 65559;  // Full Access before task 139: legacy Collaborate + Delete
    private const int ShareBit = 262144;
    private const int AssignBit = 524288;

    private const string CollaborateCsv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess";
    private const string LegacyCollaborateCsv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess";

    private readonly FakeRecordShareTable _shares = new();
    private readonly FakeSystemUsers _users = new();

    /// <summary>
    /// Task 149: the secure-child synchronizer the share routes now fan out through, over a world with the Secure Record BU
    /// and team but NO secure roots — the matter here is ordinary, so the fan-out reads its root, finds it ordinary and
    /// writes nothing. The secure fan-out itself is pinned in SecureChildShareMirrorTests.
    /// </summary>
    private readonly Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld _children =
        Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard();

    /// <summary>The record's flags, read by /unshare-user's S5 rule (task 139). Unseeded: Standard, not secure.</summary>
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _flags = new(defaultFlags: RootRecordFlags.None);
    private readonly Mock<ITenantCache> _cache = new();
    private readonly List<(string Tenant, string Resource, string Id, int Version)> _invalidated = new();

    // Task 143 — the No Access check /share-user asks before any share write. The REAL guard over the real deny-list
    // reader (wire seam only) and a row store for the user↔contact link; the record's flags come from _flags.
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyList = new();
    private readonly IdentityBinding.InMemoryContactIdentityStore _identity = new();
    private readonly SecureShareNoAccessGuard _guard;

    /// <summary>Task 142: an inert materializer — its ledger is empty, so the share routes' operator markers are no-ops here
    /// (the markers themselves are pinned by <c>AssignedAccessMarkerTests</c>).</summary>
    private static Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer AssignedAccess =>
        AssignedAccessTestDoubles.InertMaterializer();

    public InternalUserShareTests()
    {
        _guard = new SecureShareNoAccessGuard(_flags, _denyList, _identity, AssignedAccessTestDoubles.NoFilingRows(),
            NullLogger<SecureShareNoAccessGuard>.Instance);

        _users.SeedPerson(UserId, "Ada Lovelace");
        _users.SeedPerson(OtherUserId, "Brook Okafor");

        _cache
            .Setup(c => c.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, string, CancellationToken>(
                (tenant, resource, id, version, _, _) => _invalidated.Add((tenant, resource, id, version)))
            .Returns(Task.CompletedTask);
    }

    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    // ─────────────────────────────────────────────────────────────────────────────
    // Task 143 — the No Access list binds internal users on SECURE records (owner Q4)
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid UserOid = Guid.Parse("14314314-0000-0000-0000-0000000000b2");
    private static readonly Guid LinkedContactId = Guid.Parse("14314314-0000-0000-0000-0000000000c1");
    private static readonly Guid FirmId = Guid.Parse("14314314-0000-0000-0000-0000000000d1");
    private static readonly Guid ReferencedOrgId = Guid.Parse("14314314-0000-0000-0000-0000000000d2");

    /// <summary>The matter is SECURE, and the user's task-141 link is readable (linked to a contact).</summary>
    private void SecureMatterWithLinkedUser()
    {
        _flags.Flags[MatterId] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _identity.AddContact(LinkedContactId);
        _identity.AddSystemUser(UserId, UserOid, "ada@customer.example", primaryContactId: LinkedContactId);
    }

    /// <summary>Criterion 2: a systemuser-subject entry refuses the share — 403, the new code, a message, no write.</summary>
    [Fact]
    public async Task Share_AUserOnTheSecureRecordsNoAccessList_Is403SubjectNoAccess_AndWritesNothing()
    {
        SecureMatterWithLinkedUser();
        _denyList.DenySystemUserOnRecord(UserId, MatterId);

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        ProblemOf(result).Should().Be((403, InternalShareEndpoints.SubjectNoAccessReasonCode));
        result.Should().BeOfType<ProblemHttpResult>().Which.ProblemDetails.Detail
            .Should().Be("This person is on the No Access list for this record, so it was not shared with them.");
        _shares.Writes.Should().BeEmpty("the refusal comes before any Dataverse share write");
        _shares.StrictReads.Should().Be(0, "and before the share table is even read");
    }

    /// <summary>Criterion 3: the same refusal through the linked contact, its organization, and an organization object.</summary>
    [Theory]
    [InlineData("linked contact")]
    [InlineData("organization of the linked contact")]
    [InlineData("organization the record references")]
    public async Task Share_AUserWalledThroughAnyOtherSubjectOrObjectForm_IsRefusedToo(string form)
    {
        SecureMatterWithLinkedUser();
        switch (form)
        {
            case "linked contact":
                _denyList.DenyContactOnRecord(LinkedContactId, MatterId);
                break;
            case "organization of the linked contact":
                _flags.ContactOrganizations[LinkedContactId] = new[] { FirmId };
                _denyList.DenyOrganizationOnRecord(FirmId, MatterId);
                break;
            default:
                _flags.RecordOrganizations[MatterId] = new[] { ReferencedOrgId };
                _denyList.DenySystemUserOnOrganization(UserId, ReferencedOrgId);
                break;
        }

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        ProblemOf(result).Should().Be((403, InternalShareEndpoints.SubjectNoAccessReasonCode), "walled through the {0}", form);
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>Criterion 4: on a NON-secure record the same entry does not block the share (Q4 scope).</summary>
    [Fact]
    public async Task Share_OnANonSecureRecord_TheSameEntryDoesNotBlock()
    {
        _identity.AddSystemUser(UserId, UserOid, "ada@customer.example");
        _denyList.DenySystemUserOnRecord(UserId, MatterId); // _flags answers Standard, not secure, by default

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
        _denyList.Queries.Should().Be(0, "the internal wall is not consulted on a non-secure record");
    }

    /// <summary>Criterion 5: an unreadable deny list, flag set, link or membership refuses with a message and writes nothing.</summary>
    [Theory]
    [InlineData("deny list")]
    [InlineData("flags")]
    [InlineData("link")]
    [InlineData("memberships")]
    public async Task Share_WhenAnyInputOfTheNoAccessCheckCannotBeRead_RefusesWithAMessage_AndWritesNothing(string fault)
    {
        SecureMatterWithLinkedUser();
        switch (fault)
        {
            case "deny list": _denyList.Faults = true; break;
            case "flags": _flags.Flags[MatterId] = RootRecordFlags.Unreadable; break;
            case "link": _identity.SystemUsers.Remove(UserId); break;
            default: _flags.MembershipsUnreadable = true; break;
        }

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.NoAccessUnverifiableReasonCode), "the {0} could not be read", fault);
        result.Should().BeOfType<ProblemHttpResult>().Which.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace();
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>The control for criteria 2–5: a secure record, a readable list that names someone else — shared.</summary>
    [Fact]
    public async Task Share_OnASecureRecordWhoseListNamesSomeoneElse_IsShared()
    {
        SecureMatterWithLinkedUser();
        _denyList.DenySystemUserOnRecord(OtherUserId, MatterId);

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
        _denyList.Queries.Should().BeGreaterThan(0, "the list WAS consulted, and named nobody here");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 1 — share, list, unshare
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Share_ANewUserAtCollaborate_GrantsExactlyTheCollaborateRightsAndConfirmsTheStoredMask()
    {
        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.Collaborate, CollaborateMask, InternalShareEndpoints.OutcomeCreated,
            Narrowed: false));
        _shares.Writes.Should().Equal($"GrantAccess {CollaborateCsv}");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task ShareThenListThenUnshare_ListsTheUserAtTheLevelThenRemovesThem()
    {
        await Share(UserId, ExternalAccessLevel.Collaborate);

        var listed = OkBody<RecordUserSharesResponse>(await List()).Shares;
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SystemUserId = UserId,
            FullName = "Ada Lovelace",
            AccessRightsMask = CollaborateMask,
            AccessLevel = (ExternalAccessLevel?)ExternalAccessLevel.Collaborate,
        });

        OkBody<UnshareRecordWithUserResponse>(await Unshare(UserId))
            .Should().Be(new UnshareRecordWithUserResponse(UserId, Removed: true));

        OkBody<RecordUserSharesResponse>(await List()).Shares.Should().BeEmpty();
        _shares.Writes.Should().Equal($"GrantAccess {CollaborateCsv}", "RevokeAccess");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A share ends at EXACTLY the requested level
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The trap this design exists to avoid: GrantAccess on an existing share is undocumented and reported additive,
    /// so a downgrade sent through it would leave Write and Delete in place while the UI showed View Only.
    /// </summary>
    [Fact]
    public async Task Share_DowngradingFullAccessToViewOnly_ReplacesTheRightsInsteadOfAddingToThem()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), FullAccessMask);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.ViewOnly, ViewOnlyMask, InternalShareEndpoints.OutcomeUpdated,
            Narrowed: false));
        _shares.Writes.Should().Equal("ModifyAccess ReadAccess");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(ViewOnlyMask,
            "the user now holds Read only — not Read plus the Write and Delete a GrantAccess would have kept");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // You may grant only what you hold (owner decision 2026-09-16)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The escalation this closes. The POA write is app-only, so Dataverse cannot apply its own "a sharer may only
    /// pass on rights they hold" rule; the endpoint applies it. A caller holding Collaborate who asks for Full Access
    /// grants Collaborate — and because the rule is keyed on the RIGHTS, not on who the target is, it closes the
    /// self-share path too: the same caller naming themselves also gets no Delete.
    /// </summary>
    [Fact]
    public async Task Share_WhenTheCallerLacksDelete_NarrowsFullAccessToWhatTheyHold()
    {
        var callerHoldsCollaborate =
            AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

        var result = await Share(UserId, ExternalAccessLevel.FullAccess, callerRights: callerHoldsCollaborate);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.Collaborate, CollaborateMask, InternalShareEndpoints.OutcomeCreated,
            Narrowed: true));
        _shares.Writes.Should().Equal($"GrantAccess {CollaborateCsv}");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(CollaborateMask,
            "Delete was never the caller's to give");
    }

    /// <summary>
    /// The caller's rights need not be one of the three levels — a role can grant Read and Write without Append. The
    /// share is still written, at the intersection, and reports <c>accessLevel: null</c> because those rights match no
    /// level. Refusing instead would block an administrator from giving a colleague exactly the access they have.
    /// </summary>
    [Fact]
    public async Task Share_WhenTheCallersRightsMatchNoLevel_GrantsTheIntersectionAndReportsNoLevel()
    {
        var result = await Share(UserId, ExternalAccessLevel.Collaborate,
            callerRights: AccessRights.Read | AccessRights.Write);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, AccessLevel: null, AccessRightsMask: 3, InternalShareEndpoints.OutcomeCreated, Narrowed: true));
        _shares.Writes.Should().Equal("GrantAccess ReadAccess,WriteAccess");
    }

    /// <summary>
    /// The probe answers <see cref="AccessRights.None"/> both for "no rights" and for "could not answer", so this one
    /// case covers both — and both must refuse rather than write something.
    /// </summary>
    [Fact]
    public async Task Share_WhenTheCallersRightsCannotBeEstablished_Is403AndWritesNothing()
    {
        var result = await Share(UserId, ExternalAccessLevel.ViewOnly, callerRights: AccessRights.None);

        ProblemOf(result).Should().Be((403, InternalShareEndpoints.CallerCannotGrantReasonCode));
        _shares.Writes.Should().BeEmpty();
        _shares.StrictReads.Should().Be(0, "the refusal comes before the share read — nothing to read for");
    }

    /// <summary>
    /// The intersection is where the two rights vocabularies meet, so it is pinned right by right. Spaarke's Append is
    /// 16 and Dataverse's is 4; Spaarke's Delete is 4 and Dataverse's is 65536. A mis-paired row would grant a right
    /// nobody asked for — silently, and in the dangerous direction.
    /// </summary>
    [Theory]
    [InlineData(AccessRights.Read, "ReadAccess", 1)]
    [InlineData(AccessRights.Read | AccessRights.Write, "ReadAccess,WriteAccess", 3)]
    [InlineData(AccessRights.Read | AccessRights.Append, "ReadAccess,AppendAccess", 5)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo, "ReadAccess,AppendToAccess", 17)]
    [InlineData(AccessRights.Read | AccessRights.Delete, "ReadAccess,DeleteAccess", 65537)]
    [InlineData(AccessRights.Read | AccessRights.Share, "ReadAccess,ShareAccess", 262145)]   // Spaarke 64 ↔ Dataverse 262144
    public void Intersect_PairsTheTwoVocabulariesRightByRight(
        AccessRights callerRights, string expectedCsv, int expectedMask)
    {
        RecordShareLevels.TryGetRights(ExternalAccessLevel.FullAccess, out var fullAccess).Should().BeTrue();

        var granted = RecordShareLevels.Intersect(fullAccess, callerRights);

        granted.Should().Be(new RecordShareRights(expectedCsv, expectedMask));
    }

    /// <summary>
    /// Task 139, criterion 2: Share joined the pairing table, so the intersection carries <c>ShareAccess</c> exactly when
    /// the caller holds <see cref="AccessRights.Share"/>. Before the row existed, Intersect DROPPED a Share bit whatever
    /// the caller held — which, once Share joined the levels, would have stripped it from every share. The twin
    /// differs only in the caller's Share flag.
    /// </summary>
    [Theory]
    [InlineData(true, "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess,DeleteAccess", 327703)]
    [InlineData(false, "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess", 65559)]
    public void Intersect_FullAccess_CarriesShareOnlyWhenTheCallerHoldsShare(
        bool callerHoldsShare, string expectedCsv, int expectedMask)
    {
        RecordShareLevels.TryGetRights(ExternalAccessLevel.FullAccess, out var fullAccess).Should().BeTrue();
        var callerRights = FullWorkingRights & ~AccessRights.Share;
        if (callerHoldsShare)
            callerRights |= AccessRights.Share;

        RecordShareLevels.Intersect(fullAccess, callerRights)
            .Should().Be(new RecordShareRights(expectedCsv, expectedMask));
    }

    /// <summary>A caller's rights the level does not ask for add nothing: the level is the ceiling, their rights the floor.</summary>
    [Fact]
    public void Intersect_NeverAddsARightTheLevelDoesNotCarry()
    {
        RecordShareLevels.TryGetRights(ExternalAccessLevel.ViewOnly, out var viewOnly).Should().BeTrue();

        var granted = RecordShareLevels.Intersect(viewOnly, FullWorkingRights | AccessRights.Share);

        granted.Should().Be(new RecordShareRights("ReadAccess", ViewOnlyMask));
    }

    [Fact]
    public async Task Share_UpgradingViewOnlyToFullAccess_ModifiesTheExistingShare()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), ViewOnlyMask);

        var result = await Share(UserId, ExternalAccessLevel.FullAccess);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeUpdated);
        _shares.Writes.Should().Equal($"ModifyAccess {CollaborateCsv},DeleteAccess");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(FullAccessMask);
    }

    [Fact]
    public async Task Share_AtTheLevelTheUserAlreadyHolds_WritesNothingAndClearsNothing()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeUnchanged);
        _shares.Writes.Should().BeEmpty("sharing twice at the same level is idempotent");
        _invalidated.Should().BeEmpty("nothing changed, so no cached answer went stale");
    }

    /// <summary>
    /// Task 139, criterion 1: a share written before Share joined the levels (the legacy Collaborate mask, 23) is
    /// re-shared at Collaborate by a caller who holds Share — the write brings it up to the current mask and reports
    /// "updated", not "unchanged". The twin below differs only in the caller's Share right.
    /// </summary>
    [Fact]
    public async Task Share_AtCollaborate_OverALegacyCollaborateShare_ByACallerHoldingShare_UpgradesItToTheCurrentMask()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), LegacyCollaborateMask);
        var callerRights =
            AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

        var result = await Share(UserId, ExternalAccessLevel.Collaborate, callerRights: callerRights);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.Collaborate, CollaborateMask, InternalShareEndpoints.OutcomeUpdated,
            Narrowed: false));
        _shares.Writes.Should().Equal($"ModifyAccess {CollaborateCsv}");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(CollaborateMask);
    }

    /// <summary>
    /// The twin: the same request from a caller WITHOUT Share is narrowed to exactly the legacy mask the user already
    /// holds, so nothing is written and the outcome is "unchanged" — a caller cannot hand on a right they lack.
    /// </summary>
    [Fact]
    public async Task Share_AtCollaborate_OverALegacyCollaborateShare_ByACallerWithoutShare_WritesNothing()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), LegacyCollaborateMask);
        var callerRights = AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;

        var result = await Share(UserId, ExternalAccessLevel.Collaborate, callerRights: callerRights);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.Collaborate, LegacyCollaborateMask, InternalShareEndpoints.OutcomeUnchanged,
            Narrowed: true));
        _shares.Writes.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Task 139, criterion 7 — a narrowed share never silently LOWERS an existing one
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The hazard the cap makes live: ModifyAccess REPLACES rights, so a caller holding Collaborate who asks for Full
    /// Access — narrowed to Collaborate — would overwrite somebody else's Full Access share, LOWERING it while asking for
    /// more. Refused with 409 and nothing written.
    /// </summary>
    [Fact]
    public async Task Share_WhenNarrowedBelowWhatTheUserAlreadyHolds_Is409AndWritesNothing()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), FullAccessMask);
        var callerHoldsCollaborate =
            AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

        var result = await Share(UserId, ExternalAccessLevel.FullAccess, callerRights: callerHoldsCollaborate);

        ProblemOf(result).Should().Be((409, ExternalGrantLifecycle.WouldLowerExistingReasonCode));
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Detail
            .Should().Contain("already have more access than you can grant");
        _shares.Writes.Should().BeEmpty();
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(FullAccessMask);
    }

    /// <summary>
    /// The positive twin: an EXPLICIT request for a lower level (not narrowed — the caller holds everything) is a
    /// deliberate downgrade by a Write-holder and is applied, exactly as before task 139.
    /// </summary>
    [Fact]
    public async Task Share_AnExplicitDowngradeByACallerHoldingEverything_StillDowngrades()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), FullAccessMask);

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.Collaborate, CollaborateMask, InternalShareEndpoints.OutcomeUpdated,
            Narrowed: false));
        _shares.Writes.Should().Equal($"ModifyAccess {CollaborateCsv}");
    }

    /// <summary>
    /// Criterion 8, pinned alongside the grant routes: a THROWING probe is 500 read_failed and a probe answering None
    /// is 403 caller_cannot_grant (the latter is <see cref="Share_WhenTheCallersRightsCannotBeEstablished_Is403AndWritesNothing"/>);
    /// neither writes.
    /// </summary>
    [Fact]
    public async Task Share_WhenTheCallersRightsProbeThrows_Is500ReadFailedAndWritesNothing()
    {
        var result = await InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest("matter", MatterId, UserId, ExternalAccessLevel.ViewOnly),
            _shares, _users.Client, _cache.Object, new ThrowingCallerRightsProbe(), _children.Synchronizer(_shares), _guard,
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), AssignedAccessTestDoubles.InertMaterializer(),
            AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.ReadFailedReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>A row with a zero mask carries only inherited access; a direct share there is created, not modified.</summary>
    [Fact]
    public async Task Share_WhenTheUserHoldsOnlyInheritedAccess_CreatesADirectShareWithGrant()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), mask: 0);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
        _shares.Writes.Should().Equal("GrantAccess ReadAccess");
    }

    /// <summary>Another principal's share with the same id — a TEAM — is not this user's share.</summary>
    [Fact]
    public async Task Share_IgnoresATeamShareThatHappensToCarryTheUsersId()
    {
        _shares.Seed(MatterTable, MatterId, DataversePrincipalRef.Team(UserId), FullAccessMask);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
        _shares.MaskOf(MatterTable, MatterId, DataversePrincipalRef.Team(UserId)).Should().Be(FullAccessMask);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The one level table
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly, "ReadAccess", ViewOnlyMask)]
    [InlineData(ExternalAccessLevel.Collaborate, CollaborateCsv, CollaborateMask)]
    [InlineData(ExternalAccessLevel.FullAccess, CollaborateCsv + ",DeleteAccess", FullAccessMask)]
    public void Levels_MapToExactlyTheOwnerDecidedDataverseRights(ExternalAccessLevel level, string csv, int mask)
    {
        RecordShareLevels.TryGetRights(level, out var rights).Should().BeTrue();

        rights.Should().Be(new RecordShareRights(csv, mask));
        RecordShareLevels.LevelForMask(mask).Should().Be(level);
    }

    /// <summary>
    /// Owner 2026-09-30 (task 139, C4): a Write-holder may pass access on, so Collaborate and Full Access carry Share;
    /// View Only does not; and NO level carries Assign — nobody given access through Manage Access can take the record
    /// over.
    /// </summary>
    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly, false)]
    [InlineData(ExternalAccessLevel.Collaborate, true)]
    [InlineData(ExternalAccessLevel.FullAccess, true)]
    public void Levels_CarryShareFromCollaborateUp_AndNeverAssign(ExternalAccessLevel level, bool carriesShare)
    {
        RecordShareLevels.TryGetRights(level, out var rights).Should().BeTrue();

        (rights.AccessRightsMask & ShareBit).Should().Be(carriesShare ? ShareBit : 0);
        rights.AccessRightsCsv.Contains("ShareAccess", StringComparison.Ordinal).Should().Be(carriesShare);
        (rights.AccessRightsMask & AssignBit).Should().Be(0, "no level lets a person take the record over");
        rights.AccessRightsCsv.Should().NotContain("AssignAccess");
    }

    /// <summary>
    /// Task 139, criterion 1 (data compatibility): the masks Collaborate and Full Access stored before Share joined
    /// them still read as their level — otherwise every pre-existing share would show "no level" until the backfill ran.
    /// </summary>
    [Theory]
    [InlineData(LegacyCollaborateMask, ExternalAccessLevel.Collaborate)]
    [InlineData(LegacyFullAccessMask, ExternalAccessLevel.FullAccess)]
    public void LevelForMask_ReadsTheLegacyMasksAsTheirLevel(int mask, ExternalAccessLevel level)
        => RecordShareLevels.LevelForMask(mask).Should().Be(level);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]                               // Read + Write: no level
    [InlineData(ViewOnlyMask | ShareBit)]         // Read + Share: no level
    [InlineData(CollaborateMask | 32)]            // Collaborate + Create
    [InlineData(CollaborateMask | AssignBit)]     // Collaborate + Assign
    [InlineData(LegacyFullAccessMask | AssignBit)] // a legacy mask with anything more is not a level either
    public void LevelForMask_ForRightsOutsideTheThreeLevels_IsNull(int mask)
        => RecordShareLevels.LevelForMask(mask).Should().BeNull();

    /// <summary>
    /// Owner 2026-09-30 (C4): colleagues named at secure provisioning receive EXACTLY the creator's rights — the
    /// Collaborate level, which now carries Share. Both constants are asserted against the LITERAL.
    /// </summary>
    [Fact]
    public void ProvisioningShares_CreatorAndColleagues_CarryTheIdenticalCollaborateLiteral()
    {
        const string collaborateLiteral = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess";

        ProvisionProjectEndpoint.CollaboratorAccessRights.Should().Be(collaborateLiteral,
            "asserted against the LITERAL, not against RecordShareLevels.CollaborateRights: comparing a constant " +
            "with the constant it is now DEFINED as can only catch re-literalization, and would move with any drift");
        ProvisionProjectEndpoint.CreatorAccessRights.Should().Be(collaborateLiteral,
            "the creator's share is the same Collaborate level — its value (mask 262167) is unchanged by task 139");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Validation — nothing is read or written for a malformed request
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(7777)]
    public async Task Share_WithoutAValidLevel_Is400AndTouchesNothing(int? level)
    {
        var result = await Share(UserId, (ExternalAccessLevel?)level);

        ProblemOf(result).Should().Be((400, InternalShareEndpoints.LevelInvalidReasonCode));
        AssertNothingReadOrWritten();
    }

    [Theory]
    [InlineData("share")]
    [InlineData("unshare")]
    public async Task ShareAndUnshare_WithoutAUser_Is400AndTouchesNothing(string route)
    {
        var result = route == "share"
            ? await Share(Guid.Empty, ExternalAccessLevel.ViewOnly)
            : await Unshare(null);

        ProblemOf(result).Should().Be((400, InternalShareEndpoints.UserRequiredReasonCode));
        AssertNothingReadOrWritten();
    }

    [Theory]
    [InlineData("document")]
    [InlineData(null)]
    public async Task ShareUnshareAndList_WithoutAResolvableRecord_Are400(string? recordType)
    {
        ProblemOf(await Share(UserId, ExternalAccessLevel.ViewOnly, recordType))
            .Should().Be((400, InternalShareEndpoints.RecordUnresolvedReasonCode));
        ProblemOf(await Unshare(UserId, recordType))
            .Should().Be((400, InternalShareEndpoints.RecordUnresolvedReasonCode));
        ProblemOf(await List(recordType))
            .Should().Be((400, InternalShareEndpoints.RecordUnresolvedReasonCode));
        AssertNothingReadOrWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Who can receive a share
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Share_WithAnUnknownUser_Is404AndWritesNothing()
    {
        var result = await Share(Guid.NewGuid(), ExternalAccessLevel.ViewOnly);

        ProblemOf(result).Should().Be((404, InternalShareEndpoints.UserNotFoundReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]    // unreadable → refused (ADR-003)
    public async Task Share_WithADisabledUser_Is422AndWritesNothing(bool? isDisabled)
    {
        _users.SeedPerson(UserId, "Ada Lovelace", isDisabled: isDisabled);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        ProblemOf(result).Should().Be((422, InternalShareEndpoints.UserDisabledReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(3, false)]     // Support User
    [InlineData(4, false)]     // Non-interactive
    [InlineData(5, false)]     // Delegated Admin
    [InlineData(null, false)]  // unreadable
    [InlineData(0, true)]      // an application user, whatever its access mode says
    public async Task Share_WithAnAccountThatIsNotAPerson_Is422AndWritesNothing(int? accessMode, bool isApplication)
    {
        _users.SeedPerson(UserId, "Ada Lovelace", accessMode: accessMode,
            applicationId: isApplication ? Guid.NewGuid() : null);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        ProblemOf(result).Should().Be((422, InternalShareEndpoints.UserNotAPersonReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]    // not confirmed internal → refused, as SystemUserIdentityResolver treats it
    public async Task Share_WithAUserNotConfirmedInternal_Is422AndWritesNothing(bool? isExternal)
    {
        _users.SeedPerson(UserId, "Ada Lovelace", isExternal: isExternal);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        ProblemOf(result).Should().Be((422, InternalShareEndpoints.UserNotInternalReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>The positive twin of the refusals above: every person access mode can receive a share.</summary>
    [Theory]
    [InlineData(0)]    // Read-Write
    [InlineData(1)]    // Administrative
    [InlineData(2)]    // Read
    public async Task Share_WithAnInternalPersonInEachPersonAccessMode_Succeeds(int accessMode)
    {
        _users.SeedPerson(UserId, "Ada Lovelace", accessMode: accessMode);

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        OkBody<ShareRecordWithUserResponse>(result).Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
    }

    [Fact]
    public async Task Share_WhenTheUserCannotBeLookedUp_Is500AndWritesNothing()
    {
        _users.QueryFailure = new HttpRequestException("Dataverse 503");

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.ReadFailedReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fail closed: a failed read is never "no share"; every write is confirmed
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The soft read answers "no shares" on failure. Deciding from it would send a GrantAccess for a user who may
    /// already hold Full Access — additive, so a requested downgrade would change nothing while reporting success.
    /// </summary>
    [Fact]
    public async Task Share_WhenTheSharesCannotBeRead_RefusesAndNeverGrants()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), FullAccessMask);
        _shares.FailReads = true;

        var result = await Share(UserId, ExternalAccessLevel.ViewOnly);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.ReadFailedReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Share_WhenDataverseDoesNotStoreWhatWasAsked_IsNotConfirmedButStillClearsTheCache()
    {
        _shares.IgnoreWrites = true;

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.WriteNotConfirmedReasonCode));
        _invalidated.Should().ContainSingle();
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Extensions
            .Should().Contain(new KeyValuePair<string, object?>("observedAccessRightsMask", 0),
                "the refusal reports the mask actually stored, so the client need not round-trip to find out");
    }

    [Fact]
    public async Task Share_WhenTheWriteThrows_IsNotConfirmedAndStillClearsTheCache()
    {
        _shares.WriteFailure = new HttpRequestException("Dataverse timed out after the write may have committed");

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.WriteNotConfirmedReasonCode));
        _invalidated.Should().ContainSingle("a write that threw may still have applied");
    }

    [Fact]
    public async Task Share_WhenTheReadBackFails_IsNotConfirmedAndReportsNoObservedMask()
    {
        _shares.FailReadBackAfterWrite = true;

        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.WriteNotConfirmedReasonCode));
        _shares.Writes.Should().Equal($"GrantAccess {CollaborateCsv}");
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Extensions
            .Should().NotContainKey("observedAccessRightsMask",
                "the read-back itself failed, so there is no observed mask — and none is invented");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Unshare
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Acceptance criterion 4: removing a share that does not exist succeeds and writes nothing.</summary>
    [Fact]
    public async Task Unshare_AUserWithNoShare_Is200RemovedFalseAndSendsNoRevoke()
    {
        var result = await Unshare(UserId);

        OkBody<UnshareRecordWithUserResponse>(result).Should().Be(new UnshareRecordWithUserResponse(UserId, Removed: false));
        _shares.Writes.Should().BeEmpty(
            "RevokeAccess for a principal with no share is undocumented, so the endpoint must not rely on it");
        _invalidated.Should().BeEmpty();
    }

    [Fact]
    public async Task Unshare_Twice_RevokesOnceAndThenReportsNothingToRemove()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        OkBody<UnshareRecordWithUserResponse>(await Unshare(UserId)).Removed.Should().BeTrue();
        OkBody<UnshareRecordWithUserResponse>(await Unshare(UserId)).Removed.Should().BeFalse();

        _shares.Writes.Should().Equal("RevokeAccess");
    }

    /// <summary>
    /// The eligibility rules gate RECEIVING a share, not losing one: a share held by a user who has since been
    /// disabled or reclassified external is exactly the kind an administrator must be able to remove.
    /// </summary>
    [Fact]
    public async Task Unshare_ADisabledExternalAccountsShare_IsStillRemoved()
    {
        _users.SeedPerson(UserId, "Ada Lovelace", accessMode: 4, isDisabled: true, isExternal: true);
        _shares.Seed(MatterTable, MatterId, User(UserId), FullAccessMask);

        OkBody<UnshareRecordWithUserResponse>(await Unshare(UserId)).Removed.Should().BeTrue();
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().BeNull();
    }

    [Fact]
    public async Task Unshare_WithAnUnknownUser_Is404AndWritesNothing()
    {
        ProblemOf(await Unshare(Guid.NewGuid())).Should().Be((404, InternalShareEndpoints.UserNotFoundReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// A failed read must not become "nothing to remove": that answer would tell the administrator the user has no
    /// access while their share stands.
    /// </summary>
    [Fact]
    public async Task Unshare_WhenTheSharesCannotBeRead_RefusesRatherThanReportingNothingToRemove()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.FailReads = true;

        var result = await Unshare(UserId);

        ProblemOf(result).Should().Be((500, InternalShareEndpoints.ReadFailedReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Unshare_WhenTheShareSurvivesTheRevoke_IsNotConfirmed()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.IgnoreWrites = true;

        ProblemOf(await Unshare(UserId)).Should().Be((500, InternalShareEndpoints.WriteNotConfirmedReasonCode));
        _invalidated.Should().ContainSingle();
    }

    [Fact]
    public async Task Unshare_RemovesOnlyThatUsersShareOnThatRecord()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.Seed(MatterTable, MatterId, User(OtherUserId), CollaborateMask);
        _shares.Seed(MatterTable, OtherMatterId, User(UserId), CollaborateMask);

        await Unshare(UserId);

        _shares.MaskOf(MatterTable, MatterId, User(OtherUserId)).Should().Be(CollaborateMask);
        _shares.MaskOf(MatterTable, OtherMatterId, User(UserId)).Should().Be(CollaborateMask);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // S5 (owner round 3; task 139 amendment R3) — a secure record always keeps someone who can see it
    // ─────────────────────────────────────────────────────────────────────────────

    private void MarkMatterSecure() => _flags.Flags[MatterId] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

    [Fact]
    public async Task Unshare_TheLastPersonOnASecureRecord_Is409AndRemovesNothing()
    {
        MarkMatterSecure();
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        var result = await Unshare(UserId);

        ProblemOf(result).Should().Be((409, InternalShareEndpoints.LastReaderOnSecureRecordReasonCode));
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Detail
            .Should().Contain("last person who can open this secure record");
        _shares.Writes.Should().BeEmpty();
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(CollaborateMask);
    }

    /// <summary>The positive twin: another enabled person keeps a share that can read it, so the removal proceeds.</summary>
    [Fact]
    public async Task Unshare_OnASecureRecord_WhenAnotherEnabledPersonCanStillReadIt_Removes()
    {
        MarkMatterSecure();
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.Seed(MatterTable, MatterId, User(OtherUserId), ViewOnlyMask);

        OkBody<UnshareRecordWithUserResponse>(await Unshare(UserId)).Removed.Should().BeTrue();
        _shares.Writes.Should().Equal("RevokeAccess");
    }

    /// <summary>The other twin: on a record that is NOT secure, the owner's business unit still sees it — no S5 check.</summary>
    [Fact]
    public async Task Unshare_TheOnlyShareOnANonSecureRecord_IsRemoved()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        OkBody<UnshareRecordWithUserResponse>(await Unshare(UserId)).Removed.Should().BeTrue();
    }

    /// <summary>
    /// Who counts: an enabled USER whose share carries Read. A disabled user cannot open the record, and a team's
    /// membership is not read here, so neither keeps the record visible — the removal is refused.
    /// </summary>
    [Theory]
    [InlineData("disabled-user")]
    [InlineData("team-only")]
    [InlineData("no-read")]
    public async Task Unshare_OnASecureRecord_WhenNoOtherEnabledReaderRemains_Is409(string other)
    {
        MarkMatterSecure();
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        switch (other)
        {
            case "disabled-user":
                _users.SeedPerson(OtherUserId, "Brook Okafor", isDisabled: true);
                _shares.Seed(MatterTable, MatterId, User(OtherUserId), FullAccessMask);
                break;
            case "team-only":
                _shares.Seed(MatterTable, MatterId, DataversePrincipalRef.Team(TeamId), FullAccessMask);
                break;
            case "no-read":
                _shares.Seed(MatterTable, MatterId, User(OtherUserId), mask: 0);
                break;
        }

        ProblemOf(await Unshare(UserId)).Should().Be((409, InternalShareEndpoints.LastReaderOnSecureRecordReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>A record whose secure flag cannot be read is treated as secure (fail closed: keep the share).</summary>
    [Fact]
    public async Task Unshare_WhenTheSecureFlagCannotBeRead_AppliesTheLastPersonRule()
    {
        _flags.Flags[MatterId] = RootRecordFlags.Unreadable;
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        ProblemOf(await Unshare(UserId)).Should().Be((409, InternalShareEndpoints.LastReaderOnSecureRecordReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// Task 150, round 17 item 3: a row whose <c>sprk_issecure</c> came back EMPTY (the column is field-secured; empty
    /// means the app identity's Read was lost) is mapped by the ONE flag reader
    /// (<see cref="ExternalParticipationService.FlagsFrom"/>) — and must reach this rule as secure, never "not secure".
    /// </summary>
    [Fact]
    public async Task Unshare_WhenTheSecureFlagReadsEmpty_AppliesTheLastPersonRule()
    {
        _flags.Flags[MatterId] = ExternalParticipationService.FlagsFrom(isSecure: null, accessPermission: null, stateCode: 0);
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        ProblemOf(await Unshare(UserId)).Should().Be((409, InternalShareEndpoints.LastReaderOnSecureRecordReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// The twin of the case above for a flag read that THROWS (not one that answers Unreadable): the catch around the
    /// read must also treat the record as secure. Without this, a regression of that catch to "not secure" would let
    /// the last person go from a secure record whenever the flag read faults (S5 / ADR-003).
    /// </summary>
    [Fact]
    public async Task Unshare_WhenTheSecureFlagReadThrows_AppliesTheLastPersonRule()
    {
        _flags.ThrowOnRead = true;
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);

        ProblemOf(await Unshare(UserId)).Should().Be((409, InternalShareEndpoints.LastReaderOnSecureRecordReasonCode));
        _flags.Reads.Should().Contain((MatterTable, MatterId), "the flag read must actually have been attempted");
        _shares.Writes.Should().BeEmpty();
    }

    /// <summary>If the other sharers cannot be checked, nothing is removed — never a guess that someone remains.</summary>
    [Fact]
    public async Task Unshare_OnASecureRecord_WhenTheOtherSharersCannotBeRead_Is500AndRemovesNothing()
    {
        MarkMatterSecure();
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.Seed(MatterTable, MatterId, User(OtherUserId), CollaborateMask);
        _users.FailQueriesSelecting = InternalShareEndpoints.SystemUserEnabledSelect;

        ProblemOf(await Unshare(UserId)).Should().Be((500, InternalShareEndpoints.ReadFailedReasonCode));
        _shares.Writes.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // List
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsTheDirectSystemUserSharesWithNamesAndExactLevels()
    {
        _shares.Seed(MatterTable, MatterId, User(OtherUserId), CollaborateMask | AssignBit);
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.Seed(MatterTable, MatterId, DataversePrincipalRef.Team(TeamId), ViewOnlyMask);
        _shares.Seed(MatterTable, MatterId, User(InheritedOnlyUserId), mask: 0);

        var shares = OkBody<RecordUserSharesResponse>(await List()).Shares;

        shares.Select(s => (s.SystemUserId, s.FullName, s.AccessRightsMask, s.AccessLevel)).Should().Equal(
            (UserId, "Ada Lovelace", CollaborateMask, ExternalAccessLevel.Collaborate),
            (OtherUserId, "Brook Okafor", CollaborateMask | AssignBit, (ExternalAccessLevel?)null));
    }

    /// <summary>Task 139: a share still at a legacy mask lists at its level, with the stored mask shown as it is.</summary>
    [Fact]
    public async Task List_ShowsALegacyMaskShareAtItsLevel()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), LegacyFullAccessMask);

        var shares = OkBody<RecordUserSharesResponse>(await List()).Shares;

        shares.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SystemUserId = UserId,
            AccessRightsMask = LegacyFullAccessMask,
            AccessLevel = (ExternalAccessLevel?)ExternalAccessLevel.FullAccess,
        });
    }

    [Fact]
    public async Task List_WhenTheNamesCannotBeRead_StillListsEveryShare()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _users.QueryFailure = new HttpRequestException("Dataverse 503");

        var shares = OkBody<RecordUserSharesResponse>(await List()).Shares;

        shares.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SystemUserId = UserId,
            FullName = (string?)null,
            AccessRightsMask = CollaborateMask,
        });
    }

    /// <summary>A failed read is a 500, never an empty 200 that reads as "nobody has access".</summary>
    [Fact]
    public async Task List_WhenTheSharesCannotBeRead_Is500NotAnEmptyList()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.FailReads = true;

        ProblemOf(await List()).Should().Be((500, InternalShareEndpoints.ReadFailedReasonCode));
    }

    [Fact]
    public async Task List_OnlyListsSharesOnThatRecord()
    {
        _shares.Seed(MatterTable, OtherMatterId, User(UserId), CollaborateMask);
        _shares.Seed("sprk_project", MatterId, User(OtherUserId), CollaborateMask);   // same id, other table

        OkBody<RecordUserSharesResponse>(await List()).Shares.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 5 — the evaluator-relevant cache is cleared
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("matter", "sprk_matter")]
    [InlineData("project", "sprk_project")]
    [InlineData("workassignment", "sprk_workassignment")]
    public async Task ShareAndUnshare_ClearTheUsersImpersonatedRootSetForThatRecordType(string recordType, string table)
    {
        await Share(UserId, ExternalAccessLevel.Collaborate, recordType);
        await Unshare(UserId, recordType);

        _invalidated.Should().HaveCount(2).And.OnlyContain(i =>
            i.Tenant == TenantId
            && i.Resource == ImpersonatedRootSetSource.CacheResource
            && i.Id == $"{UserId:D}:{table}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The two level tables share three names — and must keep disagreeing deliberately
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="RecordShareLevels"/> (a POA share) and <see cref="ExternalAccessLevels.ToAccessRights"/> (an
    /// external contact's grant) use the SAME three level names for DIFFERENT rights: internal Collaborate carries
    /// Append and AppendTo and no Create; external Collaborate carries Create and no Append. That divergence is
    /// deliberate, but one picker renders one label for both planes, so this pins what is meant to hold instead of
    /// leaving it to drift — a POA level never carries Create, and both tables nest.
    /// </summary>
    [Fact]
    public void TheTwoLevelTables_DisagreeDeliberately_AndBothNest()
    {
        const int createBit = 32;
        var levels = new[] { ExternalAccessLevel.ViewOnly, ExternalAccessLevel.Collaborate, ExternalAccessLevel.FullAccess };

        var poa = levels.Select(level =>
        {
            RecordShareLevels.TryGetRights(level, out var rights).Should().BeTrue();
            return rights.AccessRightsMask;
        }).ToList();

        poa.Should().OnlyContain(mask => (mask & createBit) == 0,
            "Create means nothing on a share of an existing record — the reason the evaluator's table cannot be reused here");
        (poa[0] & poa[1]).Should().Be(poa[0], "View Only ⊂ Collaborate");
        (poa[1] & poa[2]).Should().Be(poa[1],
            "Collaborate ⊂ Full Access — the nesting the concurrent-create path depends on");

        var evaluator = levels.Select(level => ExternalAccessLevels.ToAccessRights(level)).ToList();

        (evaluator[0] & evaluator[1]).Should().Be(evaluator[0], "the evaluator's table nests too");
        (evaluator[1] & evaluator[2]).Should().Be(evaluator[1]);
        evaluator[1].Should().HaveFlag(AccessRights.Create,
            "the evaluator's Collaborate DOES carry Create; if that ever changes the two tables have converged and " +
            "this test — and the reason RecordShareLevels exists — should be revisited");
    }

    /// <summary>
    /// The list reads names in batches of <see cref="InternalShareEndpoints.NameBatchSize"/>. With one more share
    /// than a batch, the chunking, the per-batch filter and the per-batch <c>$top</c> all have to line up — and if
    /// they do not, a share silently loses its name, which the list is designed to tolerate, so nothing else here
    /// would notice.
    /// </summary>
    [Fact]
    public async Task List_WithMoreSharesThanOneNameBatch_NamesEveryShare()
    {
        var ids = Enumerable.Range(1, InternalShareEndpoints.NameBatchSize + 1)
            .Select(i => Guid.Parse($"{i:D8}-0000-0000-0000-000000000000"))
            .ToList();

        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            _users.SeedPerson(id, $"User {index:D3}");
            _shares.Seed(MatterTable, MatterId, User(id), CollaborateMask);
        }

        var shares = OkBody<RecordUserSharesResponse>(await List()).Shares;

        shares.Should().HaveCount(ids.Count);
        shares.Should().OnlyContain(s => s.FullName != null, "every share's name must survive the batching");
        _users.Queries.Should().Be(2, "{0} users read in batches of {1}", ids.Count, InternalShareEndpoints.NameBatchSize);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What a caller holds unless a test says otherwise: a full working set, so the level asked for is the level
    /// granted and the narrowing path stays visible only in the tests that ask for it. Includes Share since task 139
    /// put Share into Collaborate and Full Access — without it every Collaborate share here would be narrowed.
    /// </summary>
    private const AccessRights FullWorkingRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
        | AccessRights.Share;

    private Task<IResult> Share(
        Guid? systemUserId, ExternalAccessLevel? level, string? recordType = "matter", AccessRights? callerRights = null) =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest(recordType, MatterId, systemUserId, level),
            _shares, _users.Client, _cache.Object, new StubCallerRightsProbe(callerRights ?? FullWorkingRights),
            _children.Synchronizer(_shares), _guard, Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), AssignedAccess, AuthenticatedContext(),
            NullLogger<Program>.Instance,
            CancellationToken.None);

    /// <summary>
    /// Reports fixed rights for the caller, which is what the intersection rule reads. A probe that answered
    /// <see cref="AccessRights.None"/> would make every share refuse, so the default has to be a caller who can
    /// actually grant — and a test that wants the narrowing path states the narrower rights explicitly.
    /// </summary>
    internal sealed class StubCallerRightsProbe : CallerRecordAccessProbe
    {
        private readonly AccessRights _rights;

        public StubCallerRightsProbe(AccessRights rights)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _rights = rights;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(_rights);
    }

    /// <summary>A probe that throws — the 500 path, distinct from the None answer every OBO/transport failure gives.</summary>
    private sealed class ThrowingCallerRightsProbe : CallerRecordAccessProbe
    {
        public ThrowingCallerRightsProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated failure establishing the caller's rights.");
    }

    private Task<IResult> Unshare(Guid? systemUserId, string? recordType = "matter") =>
        InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest(recordType, MatterId, systemUserId),
            _shares, _users.Client, _flags, _cache.Object, AssignedAccess, _children.Synchronizer(_shares),
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(), AuthenticatedContext(),
            NullLogger<Program>.Instance, CancellationToken.None);

    private Task<IResult> List(string? recordType = "matter") =>
        InternalShareEndpoints.ListAsync(
            new RecordUserSharesQuery(recordType, MatterId),
            _shares, _users.Client, AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);

    private void AssertNothingReadOrWritten()
    {
        _shares.StrictReads.Should().Be(0, "a malformed request is refused before any Dataverse call");
        _shares.Writes.Should().BeEmpty();
        _users.Queries.Should().Be(0);
    }

    private static HttpContext AuthenticatedContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", TenantId),
            new Claim("oid", CallerOid.ToString()),
        }, "test")),
        TraceIdentifier = "trace-063",
    };

    private static T OkBody<T>(IResult result) => result.Should().BeOfType<Ok<T>>().Subject.Value!;

    private static (int Status, string? ReasonCode) ProblemOf(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions.Should().ContainKey("traceId");
        return (problem.StatusCode,
            problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string : null);
    }

    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            // Takes the managed-identity branch, whose credential is constructed lazily and never used — every
            // method the handlers call on this client is overridden.
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["TENANT_ID"] = TenantId
        }).Build();

    /// <summary>
    /// The <c>systemusers</c> table behind <see cref="DataverseWebApiClient.QueryAsync{T}"/>, strict the way Dataverse
    /// is: it understands only <c>systemuserid eq {id}</c> clauses joined by <c>or</c>, rejects a <c>$select</c> naming a
    /// column the table does not have, and returns ONLY the selected columns.
    /// </summary>
    internal sealed class FakeSystemUsers
    {
        private static readonly HashSet<string> Columns = new(StringComparer.Ordinal)
        {
            "systemuserid", "fullname", "isdisabled", "accessmode", "applicationid", "sprk_isexternal"
        };

        private static readonly Regex Clause = new(@"^systemuserid eq ([0-9a-fA-F-]{36})$", RegexOptions.CultureInvariant);

        private readonly Dictionary<Guid, InternalShareEndpoints.SystemUserRow> _rows = new();

        public FakeSystemUsers()
        {
            var mock = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance,
                // Moq matches a class-proxy constructor exactly, so the two optional credential slots are passed
                // positionally; this double never authenticates.
                null!, null!);

            mock.Setup(c => c.QueryAsync<InternalShareEndpoints.SystemUserRow>(
                    InternalShareEndpoints.SystemUserEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? select, int? _, int? _, CancellationToken _) =>
                    Answer(filter, select));

            Client = mock.Object;
        }

        public DataverseWebApiClient Client { get; }

        public Exception? QueryFailure { get; set; }

        /// <summary>Fails only the queries whose <c>$select</c> is exactly this (task 139: one read among several).</summary>
        public string? FailQueriesSelecting { get; set; }

        public int Queries { get; private set; }

        public void SeedPerson(
            Guid id, string fullName, int? accessMode = 0, bool? isDisabled = false, bool? isExternal = false,
            Guid? applicationId = null)
            => _rows[id] = new InternalShareEndpoints.SystemUserRow
            {
                Id = id,
                FullName = fullName,
                AccessMode = accessMode,
                IsDisabled = isDisabled,
                IsExternal = isExternal,
                ApplicationId = applicationId,
            };

        private List<InternalShareEndpoints.SystemUserRow> Answer(string? filter, string? select)
        {
            Queries++;
            if (QueryFailure is not null)
                throw QueryFailure;
            if (FailQueriesSelecting is not null && select == FailQueriesSelecting)
                throw new HttpRequestException("Dataverse 503");

            var columns = (select ?? throw new InvalidOperationException("A systemuser read without $select returns every column."))
                .Split(',');
            foreach (var column in columns)
            {
                if (!Columns.Contains(column))
                    throw new InvalidOperationException(
                        $"Could not find a property named '{column}' on type 'Microsoft.Dynamics.CRM.systemuser'.");
            }

            var ids = (filter ?? throw new InvalidOperationException("An unfiltered systemuser read returns every user."))
                .Split(" or ")
                .Select(clause => Clause.Match(clause) is { Success: true } match
                    ? Guid.Parse(match.Groups[1].Value)
                    : throw new InvalidOperationException($"The fake does not understand the clause '{clause}'."))
                .ToHashSet();

            return _rows.Values
                .Where(r => ids.Contains(r.Id))
                .Select(r => new InternalShareEndpoints.SystemUserRow
                {
                    Id = columns.Contains("systemuserid") ? r.Id : Guid.Empty,
                    FullName = columns.Contains("fullname") ? r.FullName : null,
                    IsDisabled = columns.Contains("isdisabled") ? r.IsDisabled : null,
                    AccessMode = columns.Contains("accessmode") ? r.AccessMode : null,
                    ApplicationId = columns.Contains("applicationid") ? r.ApplicationId : null,
                    IsExternal = columns.Contains("sprk_isexternal") ? r.IsExternal : null,
                })
                .ToList();
        }
    }
}
