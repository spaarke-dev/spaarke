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

    // Dataverse's stored masks, as literals.
    private const int ViewOnlyMask = 1;              // Read
    private const int CollaborateMask = 23;          // Read 1 + Write 2 + Append 4 + AppendTo 16
    private const int FullAccessMask = 65559;        // Collaborate + Delete 65536
    private const int CreatorMask = 262167;          // Collaborate + Share 262144 — the provisioning creator's share
    private const int ShareBit = 262144;
    private const int AssignBit = 524288;

    private const string CollaborateCsv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess";

    private readonly FakeRecordShareTable _shares = new();
    private readonly FakeSystemUsers _users = new();
    private readonly Mock<ITenantCache> _cache = new();
    private readonly List<(string Tenant, string Resource, string Id, int Version)> _invalidated = new();

    public InternalUserShareTests()
    {
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
    // Acceptance criterion 1 — share, list, unshare
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Share_ANewUserAtCollaborate_GrantsExactlyTheCollaborateRightsAndConfirmsTheStoredMask()
    {
        var result = await Share(UserId, ExternalAccessLevel.Collaborate);

        OkBody<ShareRecordWithUserResponse>(result).Should().Be(new ShareRecordWithUserResponse(
            UserId, ExternalAccessLevel.Collaborate, CollaborateMask, InternalShareEndpoints.OutcomeCreated));
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
            UserId, ExternalAccessLevel.ViewOnly, ViewOnlyMask, InternalShareEndpoints.OutcomeUpdated));
        _shares.Writes.Should().Equal("ModifyAccess ReadAccess");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(ViewOnlyMask,
            "the user now holds Read only — not Read plus the Write and Delete a GrantAccess would have kept");
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
    /// Owner decision "No re-share at any level": setting a level replaces every right, so a provisioning creator's
    /// Share right does not survive a level change made here.
    /// </summary>
    [Fact]
    public async Task Share_OverACreatorShareCarryingTheShareRight_LeavesExactlyTheLevel()
    {
        _shares.Seed(MatterTable, MatterId, User(UserId), CreatorMask);

        await Share(UserId, ExternalAccessLevel.Collaborate);

        _shares.Writes.Should().Equal($"ModifyAccess {CollaborateCsv}");
        _shares.MaskOf(MatterTable, MatterId, User(UserId)).Should().Be(CollaborateMask);
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

    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly)]
    [InlineData(ExternalAccessLevel.Collaborate)]
    [InlineData(ExternalAccessLevel.FullAccess)]
    public void Levels_NeverCarryShareOrAssign(ExternalAccessLevel level)
    {
        RecordShareLevels.TryGetRights(level, out var rights).Should().BeTrue();

        rights.AccessRightsCsv.Should().NotContainAny("ShareAccess", "AssignAccess");
        (rights.AccessRightsMask & (ShareBit | AssignBit)).Should().Be(0,
            "owner 2026-09-15: no level lets a person pass a record on or take it over");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]                          // Read + Write: no level
    [InlineData(CreatorMask)]                // Collaborate + Share
    [InlineData(CollaborateMask | 32)]       // Collaborate + Create
    public void LevelForMask_ForRightsOutsideTheThreeLevels_IsNull(int mask)
        => RecordShareLevels.LevelForMask(mask).Should().BeNull();

    [Fact]
    public void ProvisioningShares_AreBuiltFromTheCollaborateLevel()
    {
        ProvisionProjectEndpoint.CollaboratorAccessRights.Should().Be(CollaborateCsv,
            "asserted against the LITERAL, not against RecordShareLevels.CollaborateRights: comparing a constant " +
            "with the constant it is now DEFINED as can only catch re-literalization, and would move with any drift");
        ProvisionProjectEndpoint.CreatorAccessRights.Should().Be(CollaborateCsv + ",ShareAccess",
            "the creator's share is unchanged by task 063: Collaborate plus the right to re-share");
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
    // List
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsTheDirectSystemUserSharesWithNamesAndExactLevels()
    {
        _shares.Seed(MatterTable, MatterId, User(OtherUserId), CreatorMask);
        _shares.Seed(MatterTable, MatterId, User(UserId), CollaborateMask);
        _shares.Seed(MatterTable, MatterId, DataversePrincipalRef.Team(TeamId), ViewOnlyMask);
        _shares.Seed(MatterTable, MatterId, User(InheritedOnlyUserId), mask: 0);

        var shares = OkBody<RecordUserSharesResponse>(await List()).Shares;

        shares.Select(s => (s.SystemUserId, s.FullName, s.AccessRightsMask, s.AccessLevel)).Should().Equal(
            (UserId, "Ada Lovelace", CollaborateMask, ExternalAccessLevel.Collaborate),
            (OtherUserId, "Brook Okafor", CreatorMask, (ExternalAccessLevel?)null));
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

    private Task<IResult> Share(Guid? systemUserId, ExternalAccessLevel? level, string? recordType = "matter") =>
        InternalShareEndpoints.ShareAsync(
            new ShareRecordWithUserRequest(recordType, MatterId, systemUserId, level),
            _shares, _users.Client, _cache.Object, AuthenticatedContext(), NullLogger<Program>.Instance,
            CancellationToken.None);

    private Task<IResult> Unshare(Guid? systemUserId, string? recordType = "matter") =>
        InternalShareEndpoints.UnshareAsync(
            new UnshareRecordWithUserRequest(recordType, MatterId, systemUserId),
            _shares, _users.Client, _cache.Object, AuthenticatedContext(), NullLogger<Program>.Instance,
            CancellationToken.None);

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
    private sealed class FakeSystemUsers
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
