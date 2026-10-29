using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Unit tests for <see cref="RecordOwnershipResolver"/> — write-path invariant I-6 (spaarkeai-word-add-in-r1 task 080):
/// every record the BFF creates is owned by a business unit's DEFAULT OWNER TEAM, resolved RECORD-FIRST.
/// </summary>
/// <remarks>
/// <para>
/// These guard an ACCESS-CONTROL invariant. A wrong answer moves a record into the wrong business unit, and a
/// fallback that answers when it should refuse is worse than no answer: a secure record's child owned by the
/// caller's GENERAL business unit is readable by everyone in that unit. So the two refusals are pinned as hard as the
/// happy paths — (1) a named target that cannot be read, and (2) nothing to resolve from at all.
/// </para>
/// <para>
/// The Dataverse boundary is a small in-memory directory that answers the three queries by evaluating their
/// CONDITIONS, not by recognising their shape. That is what lets a test catch a dropped predicate: the directory holds
/// a non-default Owner team and a default Access team beside each unit's default Owner team, exactly as dev does, so a
/// team query missing <c>isdefault</c> or <c>teamtype</c> matches two rows and the resolver refuses.
/// </para>
/// </remarks>
public class RecordOwnershipResolverTests
{
    private static readonly Guid GeneralBu = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid ChildBu = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");
    private static readonly Guid SecureBu = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000001");

    private static readonly Guid GeneralTeam = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid ChildTeam = Guid.Parse("cf15f587-baa0-f111-aaac-000d3a99d1d7");

    /// <summary>The Secure Record BU's DEFAULT team — what this resolver answered before task 144, and must never again.</summary>
    private static readonly Guid SecureDefaultTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000002");

    /// <summary>The Secure Record BU's NAMED owner team (task 144) — the owner of every secure record and its children.</summary>
    private static readonly Guid SecureNamedTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000003");

    private const string SecureBuName = "Secure Record";
    private const string SecureOwnerTeamName = "Secure Record Owners";

    private static readonly Guid CallerUserId = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");
    private static readonly Guid CallerOid = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe");

    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecureProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // =====================================================================================
    // Record-first: a filed record follows what it is filed against
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenFiledToARecord_ReturnsThatRecordsBusinessUnitTeam_NotTheCallers()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                CallerSystemUserId = CallerUserId, // sits in GeneralBu — must NOT decide
            },
            CancellationToken.None);

        team.Should().Be(ChildTeam, "the document belongs with its matter, whoever uploaded it");
        directory.QueriedEntities.Should().NotContain("systemuser", "a resolvable target never consults the caller");
    }

    // Task 035 (owner decision 2026-10-11): a Legal Front Door intake request is owned by the default team of the business
    // unit that owns the requester's CONTACT. The contact is not an ownership PARENT (a relationship, not a filing), but it is
    // a valid TARGET: the target path reads the record's owningbusinessunit whatever the table, so no resolver change is needed.
    [Fact]
    public async Task ResolveOwner_ForARequesterContactTarget_IsTheDefaultOwnerTeamOfTheBusinessUnitThatOwnsTheContact()
    {
        var contactId = Guid.Parse("33333333-3333-3333-3333-333333333035");
        var directory = Directory().WithRecord("contact", contactId, ChildBu);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "contact", TargetRecordId = contactId },
            CancellationToken.None);

        resolution.IsOwned.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(ChildTeam, "the customer BU's default owner team, not the root's and not an Access team");
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwner_ForARequesterContactThatDoesNotExistOrHasNoBusinessUnit_Refuses()
    {
        var absent = Guid.Parse("33333333-3333-3333-3333-333333333036");
        var unowned = Guid.Parse("33333333-3333-3333-3333-333333333037");
        var directory = Directory().WithRecord("contact", unowned, owningBusinessUnit: null);

        var resolver = Build(directory);
        var noRow = await resolver.ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "contact", TargetRecordId = absent }, CancellationToken.None);
        var noBu = await resolver.ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "contact", TargetRecordId = unowned }, CancellationToken.None);

        noRow.IsRefused.Should().BeTrue();
        noRow.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        noBu.IsRefused.Should().BeTrue();
        noBu.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenFiledToASecureRecord_OwnsItByTheNamedSecureTeam_NeverTheDefaultOrTheCallers()
    {
        // The cross-business-unit case that matters: a caller in the general unit files to a SECURE project. The
        // child must land with the team that owns the secure record itself — the Secure Record BU's NAMED owner team
        // (task 144). Owned by the general team it would be readable by everyone there; owned by the Secure Record
        // BU's DEFAULT team it would follow that team's uncurated membership, the hole #967 closes.
        var directory = Directory().WithRecord("sprk_project", SecureProjectId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_project",
                TargetRecordId = SecureProjectId,
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        team.Should().Be(SecureNamedTeam).And.NotBe(SecureDefaultTeam).And.NotBe(GeneralTeam);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheSecureRecordsNamedTeamIsMissing_Refuses_NeverFallsBackToTheDefaultTeam()
    {
        var directory = Directory(withNamedSecureTeam: false).WithRecord("sprk_project", SecureProjectId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = SecureProjectId },
            CancellationToken.None);

        team.Should().BeNull("the Secure Record BU's default team is present in the directory and must not be chosen");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTwoNamedSecureTeamsMatch_Refuses()
    {
        var directory = Directory()
            .WithTeam(Guid.NewGuid(), SecureBu, isDefault: false, teamType: 0, SecureOwnerTeamName)
            .WithRecord("sprk_matter", MatterId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_matter", TargetRecordId = MatterId },
            CancellationToken.None);

        team.Should().BeNull("two teams answering one name is ambiguous; the resolver never picks one");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenOnlyTheDefaultSecureTeamCarriesTheOwnerTeamName_Refuses()
    {
        // The case only `isdefault = false` defends: the default team bears the configured owner-team name and no
        // named team exists. Dropping that predicate would hand every secure child to the default team.
        var directory = new FakeDirectory()
            .WithBusinessUnit(SecureBu, SecureBuName)
            .WithTeam(SecureDefaultTeam, SecureBu, isDefault: true, teamType: 0, SecureOwnerTeamName)
            .WithRecord("sprk_project", SecureProjectId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = SecureProjectId },
            CancellationToken.None);

        team.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheSecureBusinessUnitNameIsAmbiguous_Refuses()
    {
        var directory = Directory()
            .WithBusinessUnit(Guid.NewGuid(), SecureBuName) // a second BU with the configured name
            .WithRecord("sprk_matter", MatterId, ChildBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_matter", TargetRecordId = MatterId },
            CancellationToken.None);

        team.Should().BeNull("whether this business unit is the Secure Record BU cannot be decided");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheActingUserSitsInTheSecureBusinessUnit_StillNeverAnswersTheDefaultTeam()
    {
        // Should never happen — the Secure Record BU holds no users, and provisioning plus the census job report
        // otherwise — but if it does, an unfiled record is owned by the named team, never the retired default team.
        var userInSecureBuOid = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000005ec");
        var directory = Directory().WithUser(Guid.NewGuid(), userInSecureBuOid, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerObjectId = userInSecureBuOid },
            CancellationToken.None);

        team.Should().Be(SecureNamedTeam).And.NotBe(SecureDefaultTeam);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheTargetIsNamedByItsFriendlyAlias_ReadsTheLogicalEntity()
    {
        // A queued job's AssociationType may carry "matter"; reading an entity called "matter" would fail and
        // REFUSE a legitimate save.
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "Matter", TargetRecordId = MatterId },
            CancellationToken.None);

        team.Should().Be(ChildTeam);
        directory.QueriedEntities.Should().Contain("sprk_matter").And.NotContain("matter");
    }

    // =====================================================================================
    // Refuse branch 1 — a NAMED target that cannot be resolved never falls back to the caller
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenTheNamedTargetDoesNotExist_RefusesWithoutFallingBackToTheCaller()
    {
        var directory = Directory(); // no such project row

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_project",
                TargetRecordId = SecureProjectId,
                CallerSystemUserId = CallerUserId, // resolvable — and must not be used
            },
            CancellationToken.None);

        team.Should().BeNull("an unresolvable target might be secure; answering with the caller's unit could expose it");
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenReadingTheNamedTargetFaults_PropagatesTheFault_AndNeverUsesTheCaller()
    {
        // A throttled or failed read is not an answer. Refusing would tell the user to check a record that is fine
        // (a permanent 403 for a transient fault); falling back would risk a secure record's isolation. It surfaces.
        var directory = Directory().WithUnreadable("sprk_project");

        var act = () => Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_project",
                TargetRecordId = SecureProjectId,
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheNamedTargetHasNoOwningBusinessUnit_Refuses()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, owningBusinessUnit: null);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        team.Should().BeNull();
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    // =====================================================================================
    // Nothing named: the acting user's business unit
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenFiledToNothing_ReturnsTheCallersBusinessUnitTeam()
    {
        var team = await Build(Directory()).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        team.Should().Be(GeneralTeam);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenOnlyTheCallersObjectIdIsKnown_LooksTheUserUpByObjectId()
    {
        // The background path: a queued save carries the Entra oid, never a systemuserid.
        var directory = Directory();

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerObjectId = CallerOid },
            CancellationToken.None);

        team.Should().Be(GeneralTeam);
        directory.UserKeyColumns.Should().ContainSingle().Which.Should().Be("azureactivedirectoryobjectid");
    }

    [Fact]
    public async Task ResolveOwner_WhenTheActingUserIsDisabled_RefusesRatherThanFilingUnderTheirBusinessUnit()
    {
        // Issue #1576: a disabled systemuser keeps its oid. Its business unit must not own a new record.
        var disabledOid = Guid.Parse("15760000-0000-0000-0000-0000000001d1");
        var directory = Directory().WithDisabledUser(Guid.Parse("15760000-0000-0000-0000-0000000001d2"), disabledOid, GeneralBu);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { CallerObjectId = disabledOid },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.OwningTeamId.Should().BeNull();
    }

    // =====================================================================================
    // Refuse branch 2 — nothing to resolve from, or an ambiguous answer
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenNeitherTargetNorCallerIsKnown_Refuses()
    {
        var directory = Directory();

        var team = await Build(directory).ResolveOwningTeamAsync(new RecordOwnershipContext(), CancellationToken.None);

        team.Should().BeNull("an app-owned record in ROOT is the defect this resolver exists to remove");
        directory.QueriedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheCallerMapsToTwoUsers_RefusesRatherThanChoosingABusinessUnit()
    {
        var directory = Directory().WithUser(Guid.NewGuid(), CallerOid, ChildBu); // a second user, same oid

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerObjectId = CallerOid },
            CancellationToken.None);

        team.Should().BeNull("the same fact must have one answer — RecordContainerResolver refuses this case too");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheBusinessUnitHasNoDefaultOwnerTeam_Refuses()
    {
        var directory = Directory(withDefaultTeams: false);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        team.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheUnitAlsoHasNonDefaultAndAccessTeams_ReturnsOnlyTheDefaultOwnerTeam()
    {
        // Both team predicates are load-bearing. The directory holds, in the SAME unit as the default Owner team, a
        // non-default Owner team and a default Access team (dev has both). Drop `isdefault` and two Owner teams
        // match; drop `teamtype` and two default teams match — either way the answer becomes ambiguous and wrong.
        // Decoys are inserted BEFORE the default Owner team, so a dropped predicate is caught even if TOP 2 also
        // regressed to TOP 1 (the first matching row would then be a decoy, not the answer).
        var directory = new FakeDirectory()
            .WithUser(CallerUserId, CallerOid, GeneralBu)
            .WithTeam(Guid.NewGuid(), GeneralBu, isDefault: false, teamType: 0)
            .WithTeam(Guid.NewGuid(), GeneralBu, isDefault: true, teamType: 1)
            .WithTeam(GeneralTeam, GeneralBu, isDefault: true, teamType: 0);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        team.Should().Be(GeneralTeam);
    }

    // =====================================================================================
    // Task 146 — every parent, secure-if-any, the flagged-not-isolated refusal
    // =====================================================================================

    private static readonly Guid OrdinaryProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FlaggedProjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task ResolveOwner_WhenOnlyTheRelatedProjectIsSecure_OwnsTheDocumentByTheNamedSecureTeam()
    {
        // The both-project-lookups case (design.md §5.1d): sprk_document carries sprk_project AND
        // sprk_relatedproject. The ORDINARY project is the primary parent; the SECURE one is named only by the second
        // lookup. A writer passing its first lookup alone would hand the document the ordinary team.
        var directory = Directory()
            .WithRecord("sprk_project", OrdinaryProjectId, ChildBu)
            .WithRecord("sprk_project", SecureProjectId, SecureBu);

        var document = new Entity("sprk_document")
        {
            ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProjectId),
            ["sprk_relatedproject"] = new EntityReference("sprk_project", SecureProjectId),
        };

        var resolution = await Build(directory).ResolveOwnerAsync(RecordOwnershipContext.ForChild(document), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Owned);
        resolution.OwningTeamId.Should().Be(SecureNamedTeam).And.NotBe(SecureDefaultTeam).And.NotBe(ChildTeam);
    }

    [Fact]
    public async Task ResolveOwner_WhenEveryParentIsOrdinary_UsesThePrimaryParentsBusinessUnitTeam()
    {
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_project", OrdinaryProjectId, GeneralBu);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                Parents = new[] { new RecordOwnershipParent("sprk_project", OrdinaryProjectId) },
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam, "the primary parent decides an ordinary child's business unit");
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwner_WhenAParentIsFlaggedSecureButNotIsolated_RefusesWithTheStableCode_NeverAnOrdinaryTeam()
    {
        // A failed or interrupted provisioning (C11): sprk_issecure = true, still owned in an ordinary BU.
        // Record-first would read "ordinary" from its ownership; the flag says secure. Fail closed.
        var directory = Directory().WithRecord("sprk_project", FlaggedProjectId, ChildBu, isSecure: true);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = FlaggedProjectId },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        resolution.Reason.Should().Contain(FlaggedProjectId.ToString("D"));
        resolution.OwningTeamId.Should().BeNull();
        directory.QueriedEntities.Should().NotContain("team", "a refusal is decided before any team is looked up");
    }

    /// <summary>
    /// Task 150, round 17 item 3: a parent root whose <c>sprk_issecure</c> comes back EMPTY (field-level security masked it
    /// from the BFF) fails CLOSED — refused exactly as a flagged-not-isolated parent, never resolved to an ordinary team.
    /// The explicit-<c>false</c> twin below is the control.
    /// </summary>
    [Fact]
    public async Task ResolveOwner_WhenAParentRootsSecureFlagReadsEmpty_RefusesLikeAFlaggedParent_NeverAnOrdinaryTeam()
    {
        var directory = Directory().WithMaskedSecureFlag("sprk_project", FlaggedProjectId, ChildBu);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = FlaggedProjectId },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        resolution.OwningTeamId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwner_WhenAParentRootIsExplicitlyFlaggedNo_ResolvesToItsBusinessUnitsTeam()
    {
        var directory = Directory().WithRecord("sprk_project", FlaggedProjectId, ChildBu, isSecure: false);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = FlaggedProjectId },
            CancellationToken.None);

        resolution.RefusalCode.Should().BeNull();
        resolution.OwningTeamId.Should().Be(ChildTeam, "an ordinary root's children belong with its business unit");
    }

    /// <summary>
    /// Task 148: the ONE root an unsecure transition names (<see cref="RecordOwnershipContext.UnsecuringRoot"/>) is
    /// mid-transition — moved off the Secure team, its flag cleared last — so its children resolve from its ownership like an
    /// ordinary record's, to its business unit's team. Any OTHER flagged-not-isolated parent still refuses.
    /// </summary>
    [Fact]
    public async Task ResolveOwner_ForTheRootBeingUnsecured_ResolvesFromItsOwnership_ButAnotherFlaggedParentStillRefuses()
    {
        var directory = Directory()
            .WithRecord("sprk_project", FlaggedProjectId, ChildBu, isSecure: true)
            .WithRecord("sprk_matter", MatterId, ChildBu, isSecure: true);
        var unsecuring = new RecordOwnershipParent("sprk_project", FlaggedProjectId);

        var alone = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { Parents = new[] { unsecuring }, UnsecuringRoot = unsecuring },
            CancellationToken.None);
        var besideAnother = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                Parents = new[] { unsecuring, new RecordOwnershipParent("sprk_matter", MatterId) },
                UnsecuringRoot = unsecuring,
            },
            CancellationToken.None);

        alone.OwningTeamId.Should().Be(ChildTeam, "the unsecured record's own business unit decides its children");
        besideAnother.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated,
            "the exemption names one record; every other flagged-not-isolated parent keeps failing closed");
    }

    /// <summary>
    /// Task 148 r2: a report-only pass's planned owner (<see cref="RecordOwnershipContext.PlannedOwningTeams"/>) is read as
    /// that parent's owner — INTO isolation (an ordinary document planned onto the named team makes its child secure) and OUT
    /// of it (an isolated document planned onto an ordinary team no longer makes its child secure) — while a parent not
    /// named keeps its stored owner. The rule is the same; only the facts of a planned row are the planned ones.
    /// </summary>
    [Fact]
    public async Task ResolveOwner_WithAPlannedOwningTeam_ReadsThatParentAsOwnedByIt()
    {
        var ordinaryDocument = Guid.NewGuid();
        var isolatedDocument = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_document", ordinaryDocument, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_document", isolatedDocument, SecureBu, owningTeam: SecureNamedTeam);
        var ordinaryParent = new RecordOwnershipParent("sprk_document", ordinaryDocument);
        var isolatedParent = new RecordOwnershipParent("sprk_document", isolatedDocument);

        var stored = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { Parents = new[] { ordinaryParent } }, CancellationToken.None);
        var plannedIn = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                Parents = new[] { ordinaryParent },
                PlannedOwningTeams = new Dictionary<RecordOwnershipParent, Guid> { [ordinaryParent] = SecureNamedTeam },
            },
            CancellationToken.None);
        var plannedOut = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                Parents = new[] { isolatedParent },
                PlannedOwningTeams = new Dictionary<RecordOwnershipParent, Guid> { [isolatedParent] = ChildTeam },
            },
            CancellationToken.None);

        stored.OwningTeamId.Should().Be(ChildTeam, "without a plan the stored owner decides");
        plannedIn.OwningTeamId.Should().Be(SecureNamedTeam, "planned onto the named team, the parent is isolated");
        plannedOut.OwningTeamId.Should().Be(ChildTeam, "planned onto an ordinary team, the parent no longer isolates its child");
    }

    /// <summary>
    /// Task 148 r2, fail closed: a planned owner whose team cannot be found is an unreadable parent — the resolver refuses,
    /// it never falls back to the parent's stored owner (which could be the wrong side of the isolation boundary).
    /// </summary>
    [Fact]
    public async Task ResolveOwner_WhenAPlannedTeamCannotBeFound_Refuses_NeverTheStoredOwner()
    {
        var document = Guid.NewGuid();
        var directory = Directory().WithRecord("sprk_document", document, SecureBu, owningTeam: SecureNamedTeam);
        var parent = new RecordOwnershipParent("sprk_document", document);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                Parents = new[] { parent },
                PlannedOwningTeams = new Dictionary<RecordOwnershipParent, Guid> { [parent] = Guid.NewGuid() },
            },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact]
    public async Task ResolveOwner_WhenAFlaggedRootIsIsolated_OwnsTheChildByTheNamedTeam()
    {
        var directory = Directory().WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = SecureProjectId },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task ResolveOwner_WhenTheEnvironmentHasNoSecureBusinessUnit_RefusesAFlaggedRoot()
    {
        var directory = new FakeDirectory()
            .WithBusinessUnit(ChildBu, "Spaarke Business Unit 1")
            .WithTeam(ChildTeam, ChildBu, isDefault: true, teamType: 0)
            .WithRecord("sprk_matter", MatterId, ChildBu, isSecure: true);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_matter", TargetRecordId = MatterId },
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
    }

    [Fact]
    public async Task ResolveOwner_WhenASecondParentIsMissing_RefusesWithoutConsultingTheCaller()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu); // SecureProjectId absent

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                Parents = new[] { new RecordOwnershipParent("sprk_project", SecureProjectId) },
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved,
            "the unreadable parent might be the secure one");
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwner_WhenReadingASecondParentFaults_PropagatesTheFault()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu).WithUnreadable("sprk_project");

        var act = () => Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                Parents = new[] { new RecordOwnershipParent("sprk_project", SecureProjectId) },
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("a fault is not a refusal");
    }

    [Fact]
    public async Task ResolveOwner_WhenAParentEntryIsNotAnOwnershipParent_IgnoresIt()
    {
        // A contact (or organization) on a child is a relationship, not a parent: it is never read for ownership.
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                Parents = new[] { new RecordOwnershipParent("contact", ContactId) },
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.QueriedEntities.Should().NotContain("contact");
    }

    [Fact]
    public void ParentsOf_TakesEveryOwnershipParentReference_AndNoOther()
    {
        var fields = new Dictionary<string, object>
        {
            ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProjectId),
            ["sprk_relatedproject"] = new EntityReference("sprk_project", SecureProjectId),
            ["sprk_relatedmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_relatedcontact"] = new EntityReference("contact", ContactId),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid()),
            ["sprk_documentname"] = "x.docx",
        };

        RecordOwnershipContext.ParentsOf(fields, "sprk_document").Should().BeEquivalentTo(new[]
        {
            new RecordOwnershipParent("sprk_project", OrdinaryProjectId),
            new RecordOwnershipParent("sprk_project", SecureProjectId),
            new RecordOwnershipParent("sprk_matter", MatterId),
        });
    }

    [Fact]
    public async Task ResolveOwner_WhenUnfiledAndTheWriterKeepsItsCreator_AnswersUnchanged_WithoutAnyRead()
    {
        var directory = Directory();

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { WhenUnfiled = UnfiledOwnership.KeepCreator, CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        directory.QueriedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveOwner_WhenAFiledRowKeepsCreatorWhenUnfiled_StillResolvesFromItsParent()
    {
        // KeepCreator governs ONLY the unfiled case: a filed communication is owned like any child.
        var directory = Directory().WithRecord("sprk_project", SecureProjectId, SecureBu);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                Parents = new[] { new RecordOwnershipParent("sprk_project", SecureProjectId) },
                WhenUnfiled = UnfiledOwnership.KeepCreator,
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task ResolveOwner_ForAContentRowOfAParentThatIsNotTeamOwned_AnswersUnchanged()
    {
        var communicationId = Guid.NewGuid();
        var directory = Directory().WithRecord("sprk_communication", communicationId, GeneralBu, owningTeam: null);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_communication",
                TargetRecordId = communicationId,
                KeepCreatorUnlessTargetIsTeamOwned = true,
            },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
    }

    [Fact]
    public async Task ResolveOwner_ForAContentRowOfATeamOwnedSecureParent_OwnsItByTheNamedTeam()
    {
        var communicationId = Guid.NewGuid();
        var directory = Directory().WithRecord("sprk_communication", communicationId, SecureBu, owningTeam: SecureNamedTeam);

        var resolution = await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_communication",
                TargetRecordId = communicationId,
                KeepCreatorUnlessTargetIsTeamOwned = true,
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    // ---- Content rows of a parent that is NOT team-owned but IS filed (task 146 r1, verifier item 3) ----

    [Fact]
    public async Task ResolveOwner_ForAContentRowOfAUserOwnedCommunicationFiledToASecureMatter_OwnsItByTheNamedTeam()
    {
        // A run-as-user / client-created / pre-146 communication: owned by its creator in an ordinary BU, but FILED to a
        // secure matter. Its content (review logs, participants, attachments, archived documents) must not go to the
        // creator — the parent's own filing decides.
        var communicationId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_communication", communicationId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", communicationId), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Owned);
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task ResolveOwner_ForAContentRowOfAUserOwnedCommunicationFiledToAnOrdinaryMatter_OwnsItByTheMattersTeam()
    {
        var communicationId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, isSecure: false, owningTeam: ChildTeam)
            .WithRecord("sprk_communication", communicationId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", communicationId), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam, "the filing decides, never the parent's creator");
    }

    [Fact]
    public async Task ResolveOwner_ForAContentRowOfAUserOwnedCommunicationFiledToAFlaggedButNotIsolatedProject_Refuses()
    {
        var communicationId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", FlaggedProjectId, ChildBu, isSecure: true, owningTeam: ChildTeam)
            .WithRecord("sprk_communication", communicationId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", FlaggedProjectId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", communicationId), CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
    }

    // ---- Any child of a parent that is NOT team-owned but IS filed (task 146 r2, verifier item 10) ----

    [Fact]
    public async Task ResolveOwner_ForAChildOfAUserOwnedDocumentFiledToASecureMatter_OwnsItByTheNamedTeam()
    {
        // The verifier's case: a document owned by its creator in an ordinary business unit (pre-146 data, or a client
        // create — task 147) but FILED to a secure matter. An analysis of it (ForChild / ForParents, not ContentOf) used
        // to take the document's own unit's team. The document's filing decides secrecy.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        resolution.OwningTeamId.Should().NotBe(SecureDefaultTeam);
    }

    [Fact]
    public async Task ResolveOwner_ForAChildTwoLevelsBelowASecureMatter_ThroughUnownedChildren_OwnsItByTheNamedTeam()
    {
        // A file version of a user-owned document attached to a user-owned communication filed to a secure matter.
        var communicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_communication", communicationId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) })
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_communication"] = new EntityReference("sprk_communication", communicationId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task ResolveOwner_ForAChildOfAUserOwnedDocumentFiledToAnOrdinaryMatter_FollowsTheDocumentsFiling()
    {
        // UAC task 194b (I-6, "a content row of a user-owned but FILED parent follows the parent's filing, not its creator"):
        // before 194b the ordinary answer was the user-owned document's own unit (its creator's, GeneralBu), so an analysis of
        // a document filed to a BU1 matter landed in the creator's unit. The document's filing decides now.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, isSecure: false, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
    }

    [Fact]
    public async Task ResolveOwner_ForAChildOfAUserOwnedDocumentFiledToAFlaggedButNotIsolatedProject_Refuses()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", FlaggedProjectId, ChildBu, isSecure: true, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_relatedproject"] = new EntityReference("sprk_project", FlaggedProjectId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
    }

    [Fact]
    public async Task ResolveOwner_WhenAnAncestorOfAnUnownedChildIsMissing_Refuses()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) }); // the matter is absent

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact]
    public async Task ResolveOwner_ForAChildOfATeamOwnedParent_DoesNotReadTheParentsFiling()
    {
        // A team-owned parent's owner already IS the resolver's answer for its own filing — it is not followed.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) }); // never read

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.QueriedEntities.Should().NotContain("sprk_matter");
    }

    // ---- Reparent ----

    [Fact]
    public async Task Reparent_WhenTheOnlySecureLookupIsClearedWithNull_ReassignsOutOfTheSecureTeam_ReadBack()
    {
        // Task 146 r1 (verifier item 8): clearing a lookup moves the child OUT of that parent exactly as setting another does.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new()
                {
                    ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
                    ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                });

        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = documentId,
            ParentChanges = RecordReparent.ParentChangesWithClearsIn("sprk_document",
                new Dictionary<string, object?> { ["sprk_project"] = null, ["sprk_description"] = null }),
            // Owner round 10 item 7 (task 146 c1): leaving the secure project is an un-secure — a Full Access holder.
            SecureExitCaller = CallerHolding(FullAccess),
        };
        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(ChildTeam, "read back");
    }

    // ── #2091 round 3: the payload reading every generic writer shares (RecordReparent.ParentChangesWithClearsIn) ──────

    [Fact]
    public void ParentChangesWithClearsIn_ReadsAFilingBind_AsAParentChange_OnTheColumnItBinds()
    {
        var matter = Guid.NewGuid();

        var changes = RecordReparent.ParentChangesWithClearsIn("sprk_communication", new Dictionary<string, object?>
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter})",
            ["sprk_subject"] = "Re: invoice",
        });

        changes.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, EntityReference?>("sprk_regardingmatter", new EntityReference("sprk_matter", matter)));
    }

    [Fact]
    public void ParentChangesWithClearsIn_ReadsABindAsJson_AndARootsOwnEntitySet()
    {
        var project = Guid.NewGuid();
        var json = System.Text.Json.JsonDocument.Parse($"\"sprk_projects({project})\"").RootElement;

        var changes = RecordReparent.ParentChangesWithClearsIn("sprk_todo",
            new Dictionary<string, object?> { ["sprk_regardingproject@odata.bind"] = json });

        changes["sprk_regardingproject"].Should().Be(new EntityReference("sprk_project", project));
    }

    [Fact]
    public void ParentChangesWithClearsIn_EntersABindClearOrAValueClear_UnderTheColumnItClears()
    {
        var changes = RecordReparent.ParentChangesWithClearsIn("sprk_communication", new Dictionary<string, object?>
        {
            ["sprk_RegardingMatter@odata.bind"] = null,
            ["_sprk_regardingproject_value"] = System.Text.Json.JsonDocument.Parse("null").RootElement,
        });

        changes.Should().BeEquivalentTo(new Dictionary<string, EntityReference?>
        {
            ["sprk_regardingmatter"] = null,
            ["sprk_regardingproject"] = null,
        });
    }

    [Theory]
    [InlineData("sprk_communication", "sprk_Contact@odata.bind", "/contacts(6a4b2f0e-0000-4000-8000-000000000001)")] // a relationship
    [InlineData("sprk_communication", "sprk_communicationthread@odata.bind", "garbage")] // filing lineage, not an ownership parent
    [InlineData("sprk_matter", "sprk_Project@odata.bind", "garbage")] // a root's own lookups are no child's re-file
    public void ParentChangesWithClearsIn_IgnoresABindThatIsNoOwnershipParentOfAChild(string table, string key, string bind)
    {
        RecordReparent.ParentChangesWithClearsIn(table, new Dictionary<string, object?> { [key] = bind })
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("/sprk_projects(6a4b2f0e-0000-4000-8000-000000000001)")] // another table's entity set
    [InlineData("/sprk_matter(6a4b2f0e-0000-4000-8000-000000000001)")] // the logical name, not the set
    [InlineData("/sprk_matters(nope)")]
    [InlineData("/sprk_matters(00000000-0000-0000-0000-000000000000)")]
    [InlineData("https://org.crm.dynamics.com/api/data/v9.2/sprk_matters(6a4b2f0e-0000-4000-8000-000000000001)")]
    [InlineData("")]
    public void ParentChangesWithClearsIn_RefusesAFilingBindThatNamesNoRowOfItsTarget(string bind)
    {
        var read = () => RecordReparent.ParentChangesWithClearsIn("sprk_communication",
            new Dictionary<string, object?> { ["sprk_RegardingMatter@odata.bind"] = bind });

        read.Should().Throw<RecordOwnerUnresolvedException>().Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact]
    public void ParentChangesWithClearsIn_RefusesAFilingBindThatIsNotText()
    {
        var read = () => RecordReparent.ParentChangesWithClearsIn("sprk_communication",
            new Dictionary<string, object?> { ["sprk_RegardingMatter@odata.bind"] = Guid.NewGuid() });

        read.Should().Throw<RecordOwnerUnresolvedException>();
    }

    [Theory]
    [InlineData("sprk_communication", "sprk_matter", null, true)] // sets an ownership parent
    [InlineData("sprk_communication", "contact", "sprk_regardingmatter", true)] // clears a filing lookup
    [InlineData("sprk_communication", "contact", "_sprk_regardingmatter_value", true)]
    [InlineData("sprk_communication", "contact", "sprk_RegardingMatter@odata.bind", true)]
    [InlineData("sprk_todo", "contact", "sprk_regardingservicerequest", true)] // a service request files a to-do too
    [InlineData("sprk_communication", "contact", "sprk_subject", false)] // round 4: a cleared TEXT column is no re-file
    [InlineData("sprk_communication", "contact", "sprk_communicationthread", true)] // #2143 r7c: a message's thread re-files it
    [InlineData("sprk_communication", "sprk_communicationthread", null, true)] // #2143 r7c: a thread set re-files it
    [InlineData("sprk_document", "sprk_fileversion", null, false)] // a document's current version: lineage only, no re-file
    [InlineData("sprk_document", "contact", "sprk_currentversionid", false)]
    [InlineData("sprk_communication", "contact", null, false)]
    [InlineData("sprk_matter", "sprk_matter", "sprk_project", false)] // a secure root is never re-filed by its lookups
    [InlineData("sprk_servicerequest", "sprk_matter", "sprk_regardingmatter", false)]
    public void MayRefile_CountsOwnershipParentsSet_AndFilingLookupsCleared_Only(
        string table, string lookupTarget, string? clearedColumn, bool expected)
    {
        RecordReparent.MayRefile(table, [lookupTarget], clearedColumn is null ? [] : [clearedColumn]).Should().Be(expected);
    }

    [Theory]
    [InlineData("sprk_communication", "sprk_CommunicationThread@odata.bind", "/sprk_communicationthreads(21430000-0000-4000-8000-0000000000f1)", true)]
    [InlineData("sprk_communication", "sprk_communicationthread", "ref", true)]
    [InlineData("sprk_document", "sprk_CurrentVersionId@odata.bind", "/sprk_fileversions(21430000-0000-4000-8000-0000000000f2)", false)]
    [InlineData("sprk_document", "sprk_currentversionid", "ref", false)]
    public void WithLineageLookupsIn_OnItsOwn_OnlyAMessagesThreadIsAReFile(string table, string key, string value, bool isARefile)
    {
        // Must-fire / must-NOT-fire (#2143 round 7c, F2): a payload whose ONLY change is a lineage-only lookup. A message's thread
        // re-files it (RecordReparent.IsThreadFilingColumn), so it is returned as a change and every writer routes it through
        // the reparent; a document's current version still makes no change a re-file on its own.
        var target = table == "sprk_communication" ? "sprk_communicationthread" : "sprk_fileversion";
        object raw = value == "ref" ? new EntityReference(target, Guid.NewGuid()) : value;
        var fields = new Dictionary<string, object?> { [key] = raw };

        var changes = RecordReparent.WithLineageLookupsIn(table, RecordReparent.ParentChangesWithClearsIn(table, fields), fields);

        changes.Count.Should().Be(isARefile ? 1 : 0);
        RecordReparent.MayRefile(table, changes).Should().Be(isARefile);
        if (isARefile)
            changes.Should().ContainKey("sprk_communicationthread").WhoseValue!.LogicalName.Should().Be("sprk_communicationthread");
    }

    [Fact]
    public void ParentChangesWithClearsIn_ReadsAServiceRequestBind_FromTheSolutionsRelationshipList()
    {
        var request = Guid.NewGuid();

        var changes = RecordReparent.ParentChangesWithClearsIn("sprk_todo",
            new Dictionary<string, object?> { ["sprk_RegardingServiceRequest@odata.bind"] = $"/sprk_servicerequests({request})" });
        var documentChanges = RecordReparent.ParentChangesWithClearsIn("sprk_document",
            new Dictionary<string, object?> { ["sprk_RelatedServiceRequest@odata.bind"] = $"/sprk_servicerequests({request})" });

        changes["sprk_regardingservicerequest"].Should().Be(new EntityReference("sprk_servicerequest", request));
        documentChanges["sprk_relatedservicerequest"].Should().Be(new EntityReference("sprk_servicerequest", request));
        var mismatched = () => RecordReparent.ParentChangesWithClearsIn("sprk_todo",
            new Dictionary<string, object?> { ["sprk_RegardingServiceRequest@odata.bind"] = $"/sprk_matters({request})" });
        mismatched.Should().Throw<RecordOwnerUnresolvedException>().Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact]
    public async Task Reparent_ThatRebindsTheParentTheColumnAlreadyHolds_WritesIt_AndSaysNothingMoved()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) });
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = RecordReparent.ParentChangesWithClearsIn("sprk_document",
                    new Dictionary<string, object?> { ["sprk_Project@odata.bind"] = $"/sprk_projects({SecureProjectId})" }),
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.ParentsUnchanged.Should().BeTrue("the row is already filed under that project");
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_ThatRebindsOneParentItHolds_AndSetsAnother_IsAParentChange()
    {
        // #2091 round 5: one unchanged column does not make the whole change a no-op.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) });

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = RecordReparent.ParentChangesWithClearsIn("sprk_document",
                    new Dictionary<string, object?>
                    {
                        ["sprk_Project@odata.bind"] = $"/sprk_projects({SecureProjectId})",
                        ["sprk_Matter@odata.bind"] = $"/sprk_matters({MatterId})",
                    }),
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        resolution.ParentsUnchanged.Should().BeFalse("the matter column moved, so the step after a re-file runs");
        resolution.IsRefused.Should().BeFalse();
    }

    [Fact]
    public async Task Reparent_ThatClearsAFilingLookupAlreadyNull_WritesIt_AndSaysNothingMoved()
    {
        // #2091 round 5: a clear of a filing lookup the row does not hold moves nothing.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) });
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = RecordReparent.ParentChangesWithClearsIn("sprk_document",
                    new Dictionary<string, object?> { ["sprk_Matter@odata.bind"] = null }),
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.ParentsUnchanged.Should().BeTrue("the matter column was already empty");
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_WhenTheChangeOnlyClearsColumnsThatHoldNoParent_WritesIt_AndDecidesNoOwner()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: GeneralTeam,
                extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) });
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = RecordReparent.ParentChangesWithClearsIn("sprk_document",
                    new Dictionary<string, object?> { ["sprk_description"] = null }),
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        directory.Assignments.Should().BeEmpty("no parent changed, so the owner is not re-derived");
    }

    [Fact]
    public async Task Reparent_WhenAChildMovesUnderASecureParent_AppliesTheChangeThenAssignsTheNamedTeam_ReadBack()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });
        var applied = false;
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = documentId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_relatedproject"] = new EntityReference("sprk_project", SecureProjectId),
            },
        };

        var resolution = await Build(directory).ReparentAsync(
            request, Applies(directory, request, () => applied = true), CancellationToken.None);

        applied.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        directory.Assignments.Should().ContainSingle().Which.Should().Be(("sprk_document", documentId, SecureNamedTeam));
    }

    [Fact]
    public async Task Reparent_WhenAChildMovesOutOfEverySecureParent_AssignsTheNewParentsBusinessUnitTeam()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) });

        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = documentId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_project"] = null, // cleared
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            },
            SecureExitCaller = CallerHolding(FullAccess), // owner round 10 item 7: an un-secure needs F3 rights
        };
        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().ContainSingle().Which.Item3.Should().Be(ChildTeam);
    }

    [Theory(DisplayName = "194b R (A5) must fire: a Signal's or a Decision Record's filing is its writer's — a generic re-file (into isolation, or between ordinary records) is refused before anything is read or written")]
    [InlineData("sprk_decisionrecord", "sprk_project", true)]
    [InlineData("sprk_decisionrecord", "sprk_matter", false)]
    [InlineData("sprk_signal", "sprk_matter", true)]
    [InlineData("sprk_signal", "sprk_matter", false)]
    public async Task Reparent_OfASignalOrADecisionRecord_IsRefused_BeforeAnyReadOrWrite(string table, string column, bool toSecure)
    {
        var (recordId, target) = (Guid.NewGuid(), toSecure ? SecureProjectId : MatterId);
        var targetTable = toSecure ? "sprk_project" : "sprk_matter";
        if (toSecure && column == "sprk_matter")
            (target, targetTable) = (MatterId, "sprk_matter");
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, toSecure ? SecureBu : ChildBu, isSecure: toSecure ? true : null,
                owningTeam: toSecure ? SecureNamedTeam : ChildTeam)
            .WithRecord(table, recordId, GeneralBu, owningTeam: GeneralTeam);
        var applied = false;
        var request = new RecordReparent
        {
            EntityLogicalName = table,
            RecordId = recordId,
            ParentChanges = new Dictionary<string, EntityReference?> { [column] = new EntityReference(targetTable, target) },
        };

        var resolution = await Build(directory).ReparentAsync(
            request, Applies(directory, request, () => applied = true), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        resolution.Reason.Should().Be(RecordOwnershipContext.WriterOwnedFilingRefusal);
        applied.Should().BeFalse("refused before the change is written");
        directory.QueriedEntities.Should().BeEmpty("refused before anything is read");
        directory.RecordQueries.Should().BeEmpty();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact(DisplayName = "194b R (A5) must not fire: a generic re-file of another child table (an event) still moves and settles its owner")]
    public async Task Reparent_OfAnEvent_StillMoves()
    {
        var (eventId, otherMatter) = (Guid.NewGuid(), Guid.NewGuid());
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_matter", otherMatter, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_event", eventId, GeneralBu, owningTeam: GeneralTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", otherMatter) });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_event",
            RecordId = eventId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            },
        };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().ContainSingle().Which.Should().Be(("sprk_event", eventId, ChildTeam));
    }

    [Fact]
    public async Task Reparent_WhenTheNewParentIsFlaggedButNotIsolated_RefusesAndDoesNotApplyTheChange()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", FlaggedProjectId, ChildBu, isSecure: true)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam);
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_project"] = new EntityReference("sprk_project", FlaggedProjectId),
                },
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        applied.Should().BeFalse("a refused re-file writes nothing");
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_WhenTheOwnerDoesNotReadBack_Throws()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam)
            .IgnoringAssignments();

        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = documentId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
            },
        };
        var act = () => Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        // #2143 round 2: the write succeeded and another owner read back — a conflict (409 at the HTTP writers), not a plain
        // InvalidOperationException (the global 500).
        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>().WithMessage("*did not read back*"))
            .Which.RecordId.Should().Be(documentId);
    }

    [Fact]
    public async Task Reparent_WhenTheRowIsAlreadyOwnedByTheResolvedTeam_AssignsNothing()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam);

        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = documentId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            },
        };
        await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        directory.Assignments.Should().BeEmpty();
        directory.ConditionalWrites.Should().BeEmpty("#2143: a row already owned by the resolved team gets no owner write at all");
    }

    // ---- Reparent: a failed owner assignment AFTER the change (task 146 b2, verifier RESIDUAL FAIL-OPEN) ----

    /// <summary>A document filed under an ordinary matter, about to gain a secure related project.</summary>
    private static (FakeDirectory Directory, Guid DocumentId) MoveIntoSecureWorld()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });
        return (directory, documentId);
    }

    /// <summary>The change itself, written onto the directory's row as Dataverse would.</summary>
    private static Func<CancellationToken, Task> Writes(FakeDirectory directory, Guid documentId, string column, EntityReference? value) =>
        _ =>
        {
            var row = directory.Row("sprk_document", documentId);
            if (value is null)
                row.Attributes.Remove(column);
            else
                row[column] = value;
            return Task.CompletedTask;
        };

    /// <summary>
    /// #2143: the change <paramref name="request"/> describes, written onto the row as Dataverse would (its parent changes; a
    /// null clears) — the resolver reads the row AFTER the change and settles its owner from the parents it holds then, so an
    /// applyChange that wrote nothing would read as a concurrent writer having undone the change.
    /// </summary>
    private static Func<CancellationToken, Task> Applies(FakeDirectory directory, RecordReparent request, Action? alsoDone = null) =>
        _ =>
        {
            directory.Write(request.EntityLogicalName, request.RecordId,
                request.ParentChanges.ToDictionary(change => change.Key, change => (object?)change.Value));
            alsoDone?.Invoke();
            return Task.CompletedTask;
        };

    private static RecordReparent MoveUnder(Guid documentId, string column, EntityReference? parent) => new()
    {
        EntityLogicalName = "sprk_document",
        RecordId = documentId,
        ParentChanges = new Dictionary<string, EntityReference?> { [column] = parent },
    };

    [Fact]
    public async Task Reparent_WhenTheOwnerAssignmentFailsAfterAMoveUnderASecureParent_PutsTheFilingBack_AndRethrows()
    {
        var (directory, documentId) = MoveIntoSecureWorld();
        var refusal = new InvalidOperationException("Read Privilege Check For Owner failed ... missing prvReadsprk_Document");
        directory.AssignmentFault = refusal;
        var secureProject = new EntityReference("sprk_project", SecureProjectId);

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(refusal);
        var row = directory.Row("sprk_document", documentId);
        row.Contains("sprk_relatedproject").Should().BeFalse(
            "the move under the secure project is put back: the row stays filed where its ordinary owner belongs");
        row.GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(ChildTeam);
        directory.FieldUpdates.Should().ContainSingle()
            .Which.Fields.Should().ContainKey("sprk_relatedproject").WhoseValue.Should().Be(DBNull.Value);
    }

    [Fact]
    public async Task Reparent_WhenTheOwnerAssignmentFailsAfterAMoveOutOfASecureParent_RestoresTheSecureLookup()
    {
        var documentId = Guid.NewGuid();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new()
                {
                    ["sprk_project"] = secureProject,
                    ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                });
        directory.AssignmentFault = new TimeoutException("throttled");

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_project", null) with { SecureExitCaller = CallerHolding(FullAccess) },
            Writes(directory, documentId, "sprk_project", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        var row = directory.Row("sprk_document", documentId);
        row.GetAttributeValue<EntityReference>("sprk_project").Should().Be(secureProject,
            "the cleared secure lookup comes back, matching the Secure team the row still has");
        row.GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenTheAssignmentReportsAFailureButLanded_TheRefileStands_NothingIsPutBack()
    {
        var (directory, documentId) = MoveIntoSecureWorld();
        directory.AssignmentFault = new TimeoutException("response lost");
        directory.AssignmentLandsBeforeFault = true;
        var secureProject = new EntityReference("sprk_project", SecureProjectId);

        var resolution = await Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("sprk_relatedproject")
            .Should().Be(secureProject);
        directory.FieldUpdates.Should().BeEmpty("filing and owner agree — nothing to undo");
    }

    [Fact]
    public async Task Reparent_WhenTheOwnerCannotBeReadAfterAFailedMoveIntoASecureParent_PutsTheFilingBack()
    {
        // Which way the row is wrong is unknown, so the safe direction is taken: never filed under the secure project
        // while possibly owned elsewhere (at worst over-restricted).
        var (directory, documentId) = MoveIntoSecureWorld();
        directory.AssignmentFault = new TimeoutException("throttled");
        directory.OwnerReadFault = new TimeoutException("throttled read");
        var secureProject = new EntityReference("sprk_project", SecureProjectId);

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        directory.Row("sprk_document", documentId).Contains("sprk_relatedproject").Should().BeFalse();
    }

    [Fact]
    public async Task Reparent_WhenTheOwnerCannotBeReadAfterAFailedMoveToAnOrdinaryParent_KeepsTheNewFiling()
    {
        // The new filing names no secure record, so keeping it can never expose one; putting back the secure lookup while
        // the assignment may have landed on the ordinary team could.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) });
        directory.OwnerReadFault = new TimeoutException("throttled read");
        var ordinaryMatter = new EntityReference("sprk_matter", MatterId);

        var act = () => Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_project"] = null, ["sprk_matter"] = ordinaryMatter },
                SecureExitCaller = CallerHolding(FullAccess), // owner round 10 item 7: an un-secure needs F3 rights
            },
            async ct =>
            {
                await Writes(directory, documentId, "sprk_project", null)(ct);
                await Writes(directory, documentId, "sprk_matter", ordinaryMatter)(ct);
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        var row = directory.Row("sprk_document", documentId);
        row.Contains("sprk_project").Should().BeFalse("the secure lookup is NOT put back");
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().Be(ordinaryMatter);
        directory.FieldUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_WhenTheRestoreAlsoFails_RethrowsTheAssignmentFailure_NotTheRestoreFailure()
    {
        var (directory, documentId) = MoveIntoSecureWorld();
        var assignFailure = new InvalidOperationException("missing prvReadsprk_Document");
        directory.AssignmentFault = assignFailure;
        directory.RestoreFault = new TimeoutException("restore throttled");
        var secureProject = new EntityReference("sprk_project", SecureProjectId);

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(assignFailure,
            "the writer reports the original failure in its own contract; the restore failure is logged for repair");
    }

    [Fact]
    public async Task Reparent_ThreadJoinAttachColumn_IsPutBackWhenTheAssignmentFails()
    {
        // A message joining a SECURE record's thread: the thread lookup is the change (AttachColumns + ParentChanges), and the
        // thread's record is what the message is filed under (round 6: read from the thread, RecordThreadFiling). A failed
        // assignment takes the message back out of the thread. It sat in a Direct thread (no regarding) before.
        var communicationId = Guid.NewGuid();
        var previousThread = new EntityReference("sprk_communicationthread", Guid.NewGuid());
        var secureThread = new EntityReference("sprk_communicationthread", Guid.NewGuid());
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_communicationthread", previousThread.Id, ChildBu, owningTeam: ChildTeam, extra: new() { ["sprk_threadtype"] = new OptionSetValue(100000001) }) // #2143 r7d: Direct
            .WithRecord("sprk_communicationthread", secureThread.Id, SecureBu, owningTeam: SecureNamedTeam, extra: new()
            {
                ["sprk_threadtype"] = new OptionSetValue(100000000), // #2143 r7d: a record thread
                ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProjectId),
            })
            .WithRecord("sprk_communication", communicationId, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_communicationthread"] = previousThread });
        var assignFailure = new InvalidOperationException("missing prvReadsprk_Communication");
        directory.AssignmentFault = assignFailure;

        var act = () => Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = communicationId,
                // #2143 round 5: the join shape names the thread it writes (ThreadResolver.JoinCoreAsync).
                ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_communicationthread"] = secureThread },
                AttachColumns = new[] { "sprk_communicationthread" },
                WhenUnfiled = UnfiledOwnership.KeepCreator,
            },
            _ =>
            {
                directory.Row("sprk_communication", communicationId)["sprk_communicationthread"] = secureThread;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(assignFailure,
            "the owner assignment failed (not the thread read)");
        directory.Row("sprk_communication", communicationId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Should().Be(previousThread, "the message is not left in the secure record's thread while owned elsewhere");
    }

    [Fact]
    public async Task Reparent_WhenARegardingChangeAndAThreadMoveAreWrittenAndTheAssignmentFails_BothArePutBack()
    {
        // Must-fire (#2143 round 7, F4): one change re-files the message's regarding into a secure matter AND moves it to that
        // matter's thread, both named in ParentChanges (no AttachColumns). The owner assignment fails, so the change is put
        // back — the thread move with the regarding, never only the regarding.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: OrdinaryThreadId);
        var assignFailure = new InvalidOperationException("missing prvReadsprk_Communication");
        directory.AssignmentFault = assignFailure;
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
            },
        };

        var act = () => Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(assignFailure);
        var row = directory.Row("sprk_communication", messageId);
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(MatterId, "the regarding is put back");
        row.GetAttributeValue<EntityReference>("sprk_communicationthread")?.Id
            .Should().Be(OrdinaryThreadId, "the thread move is put back with it");
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam);
    }

    [Theory]
    [InlineData(false)] // the move lands before the owner write: the put-back reads it again
    [InlineData(true)] // the move lands right after the change, before the first read: the put-back's own observation
    public async Task Reparent_IntoASecureRecord_WhenAnotherWriterMovesTheMessageToAnotherThreadWithTheSameParents_ItIsStillPutBack(
        bool beforeTheFirstRead)
    {
        // Must-fire (#2143 round 9, F1): as in the F4 test the change re-files the message into the secure matter and its
        // thread, and the owner assignment fails, so the change is put back. Before the put-back ANOTHER writer moved the
        // message to a second thread regarding the same secure matter. Round 8 left the row to that writer — filed under the
        // secure matter while still owned by its ORDINARY owner from before the change: a secure filing on an ordinary owner.
        // Toward a secure target the lineage test does not apply: the filing is put back whole, as before round 8.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: OrdinaryThreadId);
        var otherSecureThread = Guid.NewGuid();
        directory.WithRecord("sprk_communicationthread", otherSecureThread, SecureBu, owningTeam: SecureNamedTeam, extra: new()
        {
            ["sprk_threadtype"] = new OptionSetValue(100000000),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
        });
        var assignFailure = new InvalidOperationException("missing prvReadsprk_Communication");
        directory.AssignmentFault = assignFailure;
        var moved = false;
        void MoveIt(FakeDirectory world)
        {
            moved = true;
            world.Write("sprk_communication", messageId, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", otherSecureThread),
            });
        }

        if (!beforeTheFirstRead)
            directory.BeforeWrite = (world, fields) => { if (!moved && fields.ContainsKey("ownerid")) MoveIt(world); };
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
            },
        };

        var act = () => Build(directory).ReparentAsync(
            request, Applies(directory, request, beforeTheFirstRead ? () => MoveIt(directory) : null), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        moved.Should().BeTrue("the other writer's move landed before the put-back");
        var row = directory.Row("sprk_communication", messageId);
        row.GetAttributeValue<EntityReference>("sprk_communicationthread")?.Id
            .Should().Be(OrdinaryThreadId, "no secure filing is left on the ordinary owner");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id
            .Should().Be(MatterId, "the filing is put back whole");
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam, "the owner the put-back filing belongs to");
    }

    [Theory]
    [InlineData(false)] // the move lands before the owner write: the put-back reads it again
    [InlineData(true)] // the move lands right after the change, before the first read: the put-back's own observation
    public async Task Reparent_OffASecureRecord_WhenAnotherWriterMovesTheMessageToAnotherThreadWithTheSameParents_ItIsNotReverted(
        bool beforeTheFirstRead)
    {
        // Must-not-fire (#2143 round 8, kept by round 9's F1): the change re-files a HELD message off the secure matter onto the
        // ordinary matter and its thread, and the assignment to the ordinary owner fails, so the change is put back. Before
        // the put-back ANOTHER writer moved the message to a second thread regarding the same ORDINARY matter: the filing is
        // that writer's, and toward an ordinary target it is never reverted. The owner stays the Secure team from before the
        // change: an ordinary filing on a secure owner, an under-share the caller logs for repair.
        var (directory, messageId) = MessageWorld(SecureThreadMatterId, SecureBu, SecureNamedTeam, threadId: SecureThreadId);
        var otherOrdinaryThread = Guid.NewGuid();
        directory.WithRecord("sprk_communicationthread", otherOrdinaryThread, ChildBu, owningTeam: ChildTeam, extra: new()
        {
            ["sprk_threadtype"] = new OptionSetValue(100000000),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });
        var assignFailure = new InvalidOperationException("missing prvReadsprk_Communication");
        directory.AssignmentFault = assignFailure;
        var moved = false;
        void MoveIt(FakeDirectory world)
        {
            moved = true;
            world.Write("sprk_communication", messageId, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", otherOrdinaryThread),
            });
        }

        if (!beforeTheFirstRead)
            directory.BeforeWrite = (world, fields) => { if (!moved && fields.ContainsKey("ownerid")) MoveIt(world); };
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", OrdinaryThreadId),
            },
            SecureExitCaller = CallerHolding(FullAccess), // an exit from a secure record needs F3 rights
        };

        var act = () => Build(directory).ReparentAsync(
            request, Applies(directory, request, beforeTheFirstRead ? () => MoveIt(directory) : null), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        moved.Should().BeTrue("the other writer's move landed before the put-back");
        var row = directory.Row("sprk_communication", messageId);
        row.GetAttributeValue<EntityReference>("sprk_communicationthread")?.Id
            .Should().Be(otherOrdinaryThread, "the other writer's thread move is never reverted");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id
            .Should().Be(MatterId, "the filing is left to the newer writer whole");
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam, "held: the failed assignment changed no owner");
    }

    [Fact]
    public async Task Reparent_WhenAnAttachColumnIsALineageOnlyLookupNotNamedInParentChanges_ThrowsArgumentException_BeforeAnyReadOrWrite()
    {
        // Must-fire (#2143 round 5): the lineage overlay reads this call's own lineage-only changes from ParentChanges only, so
        // a thread lookup named only in AttachColumns (the pre-round-5 join shape) would read as another writer's move on every
        // call. It is a programming error: refused, naming the column, before anything is read, changed or written.
        var communicationId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_communication", communicationId, ChildBu, owningTeam: ChildTeam);
        var changed = false;

        var act = () => Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = communicationId,
                ParentChanges = new Dictionary<string, EntityReference?>(),
                InheritedParents = new[] { new RecordOwnershipParent("sprk_project", SecureProjectId) },
                AttachColumns = new[] { "sprk_communicationthread" },
                WhenUnfiled = UnfiledOwnership.KeepCreator,
            },
            _ =>
            {
                changed = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).WithMessage("*'sprk_communicationthread'*");
        changed.Should().BeFalse("the change is never applied");
        directory.QueriedEntities.Should().BeEmpty("nothing is read");
        directory.Assignments.Should().BeEmpty();
        directory.FieldUpdates.Should().BeEmpty();
        directory.ConditionalWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_WhenAnAttachColumnIsNotALineageLookup_ItNeedsNoParentChangesEntry()
    {
        // Must-NOT-fire control for the round-5 argument check: an AttachColumns entry that is no lineage-only lookup (no
        // production writer names one today; the contract allows it) is not refused — the reparent runs and is put back as
        // before when the assignment fails.
        var communicationId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_communication", communicationId, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_name"] = "before" });
        directory.AssignmentFault = new InvalidOperationException("missing prvReadsprk_Communication");

        var act = () => Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = communicationId,
                ParentChanges = new Dictionary<string, EntityReference?>(),
                InheritedParents = new[] { new RecordOwnershipParent("sprk_project", SecureProjectId) },
                AttachColumns = new[] { "sprk_name" },
                WhenUnfiled = UnfiledOwnership.KeepCreator,
            },
            _ =>
            {
                directory.Row("sprk_communication", communicationId)["sprk_name"] = "after";
                return Task.CompletedTask;
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<ArgumentException>();
        directory.Row("sprk_communication", communicationId).GetAttributeValue<string>("sprk_name")
            .Should().Be("before", "the non-lineage attach column is put back with the failed assignment");
    }

    [Fact]
    public async Task Reparent_WhenAnotherWriterReownedTheRowBetweenTheReadAndTheChange_AssignsTheResolvedTeam()
    {
        // The row is owned by the resolved team when it is read, then another writer (FR-E7 category routing) re-owns it
        // before the change lands. The "already owned" decision reads the owner AFTER the change, so it is not skipped.
        var documentId = Guid.NewGuid();
        var routedTeam = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam);
        var matter = new EntityReference("sprk_matter", MatterId);

        var resolution = await Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_matter", matter),
            _ =>
            {
                // The concurrent writer, then the change — a NEW row version, so the row the resolver read before the
                // change is a genuinely stale snapshot (the harness otherwise hands back the live row).
                directory.WithRecord("sprk_document", documentId, ChildBu, owningTeam: routedTeam,
                    extra: new() { ["sprk_matter"] = matter });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(ChildTeam);
    }

    // ---- #2143: concurrent re-files — the owner is settled from the parents the row holds AFTER the change ----
    //
    // The owner is written conditionally (versionnumber), from the parents the row holds at that version; a refused or
    // unsettled answer writes no owner; the put-back never overwrites a newer filing. The directory honours the version,
    // and BeforeWrite lands another writer's change between the resolver's read and its write.

    private static readonly Guid GeneralMatterId = Guid.Parse("21430000-0000-4000-8000-000000000001");

    /// <summary>
    /// A document filed under an ordinary matter in the general unit (owned by that unit's team), about to move to the child
    /// unit's matter — an ordinary re-file whose resolved owner is <see cref="ChildTeam"/>. A secure project is seeded for a
    /// concurrent writer to file it under.
    /// </summary>
    private static (FakeDirectory Directory, Guid DocumentId, RecordReparent Request) OrdinaryRefileWorld()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", GeneralMatterId, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: GeneralTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", GeneralMatterId) });
        return (directory, documentId, MoveUnder(documentId, "sprk_matter", new EntityReference("sprk_matter", MatterId)));
    }

    private static Guid? OwnerOf(FakeDirectory directory, Guid documentId) =>
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam")?.Id;

    private static Dictionary<string, object?> FiledUnderSecureProject() =>
        new() { ["sprk_relatedproject"] = new EntityReference("sprk_project", SecureProjectId) };

    [Fact]
    public async Task Reparent_WhenAConcurrentFilingUnderASecureRecordLandsBeforeTheOwnerWrite_TheOwnerIsTheSecureTeam()
    {
        // Must-fire: another re-file moves the row under a secure project after this change is written and before its owner
        // is (that writer's own owner write still in flight). The owner comes from the parents the row holds NOW — the Secure
        // team — never the ordinary team resolved from the parents read before the change.
        var (directory, documentId, request) = OrdinaryRefileWorld();

        var resolution = await Build(directory).ReparentAsync(
            request,
            Applies(directory, request, () => directory.Write("sprk_document", documentId, FiledUnderSecureProject())),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        resolution.IsSecureOwner.Should().BeTrue();
        directory.Assignments.Should().Equal(("sprk_document", documentId, SecureNamedTeam));
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam, "filed under the secure project, owned by its team");
    }

    [Fact]
    public async Task Reparent_WhenTheRowChangesBetweenItsReadAndTheOwnerWrite_TheWriteIsRefused_AndTheRowIsReadAgain()
    {
        // Must-fire: the concurrent filing lands between the resolver's read after the change and its owner write. The write
        // is conditioned on the version read, so Dataverse refuses it; the row is read again and the owner its CURRENT parents
        // resolve to is written.
        var (directory, documentId, request) = OrdinaryRefileWorld();
        var landed = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (landed || !fields.ContainsKey("ownerid"))
                return;
            landed = true;
            world.Write("sprk_document", documentId, FiledUnderSecureProject());
        };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
        directory.ConditionalWrites.Select(w => (((EntityReference)w.Fields["ownerid"]).Id, w.Applied)).Should().Equal(
            new[] { (ChildTeam, false), (SecureNamedTeam, true) },
            "the stale owner write is refused; the second is from the parents read again");
        directory.Assignments.Should().Equal(("sprk_document", documentId, SecureNamedTeam));
    }

    [Fact]
    public async Task Reparent_WhenTheRowKeepsChangingUnderEveryOwnerWrite_ThrowsAfterTheBoundedAttempts_WritingNoOwner()
    {
        // Must-fire: another writer moves the row in or out of the secure project before each of this re-file's owner writes
        // lands, the last one toward the ORDINARY team. The attempts are bounded; when they run out the call throws the typed
        // unsettled failure and none of its owner writes was applied. The recovery then leaves the newer filing alone and
        // settles the owner against it (the Secure team — the row ended under the secure project).
        var (directory, documentId, request) = OrdinaryRefileWorld();
        var contended = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (!fields.ContainsKey("ownerid") || ++contended > RecordOwnershipResolver.MaxOwnerWriteAttempts)
                return;
            var inSecureProject = world.Row("sprk_document", documentId).Contains("sprk_relatedproject");
            world.Write("sprk_document", documentId, inSecureProject
                ? new Dictionary<string, object?> { ["sprk_relatedproject"] = null }
                : FiledUnderSecureProject());
        };

        var act = () => Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>()).WithMessage("*could not be settled in 3 attempts*")
            .Which.RecordId.Should().Be(documentId);
        directory.ConditionalWrites.Where(w => w.Fields.ContainsKey("ownerid")).Take(3).Select(w => w.Applied)
            .Should().Equal(new[] { false, false, false }, "every owner write of the re-file met a changed row and was refused");
        directory.FieldUpdates.Should().BeEmpty("the put-back is never written over the newer filing");
        directory.Assignments.Should().Equal(new[] { ("sprk_document", documentId, SecureNamedTeam) },
            "only the recovery's re-check wrote an owner — the one the parents the row holds give");
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenTheOwnerAssignmentFails_AndANewerFilingLandsBeforeThePutBack_TheNewerFilingIsNotOverwritten()
    {
        // Must-fire: the put-back is conditioned on the version read after this change. Another writer re-files the row
        // (under a second secure record, and owns it) after this owner write failed and before the put-back: the put-back is
        // refused, the row is left to that writer, and its owner is re-checked against the parents it holds.
        var (directory, documentId) = MoveIntoSecureWorld();
        directory.WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
        var refusal = new InvalidOperationException("Read Privilege Check For Owner failed ... missing prvReadsprk_Document");
        directory.AssignmentFault = refusal;
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var otherSecureMatter = new EntityReference("sprk_matter", OtherSecureMatterId);
        var landed = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (landed || fields.ContainsKey("ownerid"))
                return; // only before the put-back
            landed = true;
            world.Write("sprk_document", documentId, new()
            {
                ["sprk_matter"] = otherSecureMatter,
                ["ownerid"] = new EntityReference("team", SecureNamedTeam),
            });
        };

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(refusal);
        landed.Should().BeTrue("the newer filing landed before the put-back");
        directory.FieldUpdates.Should().BeEmpty("the put-back is never written over a newer filing");
        var row = directory.Row("sprk_document", documentId);
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().Be(otherSecureMatter, "the newer filing stands");
        row.GetAttributeValue<EntityReference>("sprk_relatedproject").Should().Be(secureProject);
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam, "its owner matches the parents it holds");
    }

    [Fact]
    public async Task Reparent_WhenAnEditOutsideThisRefileTakesASecureRowOutOfItsSecureRecord_NoOrdinaryOwnerIsWritten()
    {
        // Must-fire, fail closed: this re-file ADDS an ordinary matter to a document filed under a secure project (it stays the
        // Secure team's; no F3 question). Before its owner is settled, an edit this re-file did not make (a form edit — no F3
        // check) clears the secure project. Re-deriving would answer the ordinary team, an un-secure no F3 check covered, so no
        // owner is written: the row stays the Secure team's (over-restricted, never exposed) and the call throws.
        var (directory, documentId) = SecureDocumentWorld();
        var request = MoveUnder(documentId, "sprk_matter", new EntityReference("sprk_matter", MatterId));

        var act = () => Build(directory).ReparentAsync(
            request,
            Applies(directory, request,
                () => directory.Write("sprk_document", documentId, new() { ["sprk_project"] = null })),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>())
            .WithMessage("*would take it out of the Secure Record business unit on a filing this re-file did not make*");
        directory.ConditionalWrites.Should().BeEmpty("no ordinary owner is ever sent");
        directory.Assignments.Should().BeEmpty();
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_OnAnUncontendedRefile_WritesTheOwnerOnce_AtTheVersionReadAfterTheChange_AndLogsAsBefore()
    {
        // Must-not-fire: with no concurrent writer the re-file is what it was — one owner write (now conditional, at the version
        // the change left), the same Information log, and nothing at Warning or above.
        var (directory, documentId, request) = OrdinaryRefileWorld();
        var log = new ResolverLog();
        long versionAfterChange = 0;

        var resolution = await directory.Resolver(logger: log).ReparentAsync(
            request,
            Applies(directory, request, () => versionAfterChange = directory.Version("sprk_document", documentId)),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
        directory.ConditionalWrites.Should().ContainSingle().Which.Should().Match<(string Entity, Guid Id, string Version, Dictionary<string, object> Fields, bool Applied)>(
            w => w.Applied && w.Version == versionAfterChange.ToString(System.Globalization.CultureInfo.InvariantCulture));
        log.Entries.Select(e => e.Message).Should().ContainSingle(m => m.Contains("owner reassigned"))
            .Which.Should().Be($"Re-filed sprk_document {documentId}: owner reassigned to team {ChildTeam}.");
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    // ---- #2143 fix round 1: re-decide every attempt (F1-a), fail closed into Secure (F1-b), the guard's must-not-fire
    //      control, a refused re-derivation, put-back retry and owner check (K2), the landed-but-re-filed recovery, and
    //      case-insensitive parents (F3) ----

    private static readonly Guid OtherOrdinaryMatterId = Guid.Parse("21430000-0000-4000-8000-000000000002");
    private static readonly Guid NotIsolatedProjectId = Guid.Parse("21430000-0000-4000-8000-000000000003");

    [Fact]
    public async Task Reparent_WhenItsParentIsMadeSecureAndTheReconcilerOwnsTheRowFirst_TheOwnerIsTheSecureTeam_NeverTheStaleOrdinaryOne()
    {
        // Must-fire (F1-a): the document moves under an ordinary matter; before its owner write lands, the matter is made
        // Secure and SecureChildReconciler (unconditional) gives the document the Secure team. The conditional write meets a
        // changed row; the parents read again are the same, but the owner is RE-DERIVED from them — now the Secure team, which
        // the row already has — never the ordinary team resolved before the change.
        var (directory, documentId, request) = OrdinaryRefileWorld();
        var landed = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (landed || !fields.ContainsKey("ownerid"))
                return;
            landed = true;
            world.WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
            world.Write("sprk_document", documentId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
        };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        resolution.IsSecureOwner.Should().BeTrue();
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam, "filed under a Secure matter, owned by its team");
        directory.ConditionalWrites.Select(w => (((EntityReference)w.Fields["ownerid"]).Id, w.Applied)).Should().Equal(
            new[] { (ChildTeam, false) }, "the stale ordinary write is refused, and none is sent again");
    }

    [Fact]
    public async Task Reparent_WhenAConcurrentWriterMovesTheRowBetweenTwoOrdinaryRecords_TheOrdinaryOwnerItsParentsGiveIsWritten()
    {
        // Must-NOT-fire control for the Secure-unit guard: a concurrent re-file between two ORDINARY records, outside the
        // Secure Record business unit, still gets the ordinary owner its parents give. A guard that always answered "would
        // leave the Secure unit" would refuse it.
        var (directory, documentId, request) = OrdinaryRefileWorld();
        directory.WithRecord("sprk_matter", OtherOrdinaryMatterId, ChildBu, owningTeam: ChildTeam);

        var resolution = await Build(directory).ReparentAsync(
            request,
            Applies(directory, request, () => directory.Write("sprk_document", documentId, new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", OtherOrdinaryMatterId),
            })),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
        OwnerOf(directory, documentId).Should().Be(ChildTeam);
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningbusinessunit").Id
            .Should().Be(ChildBu, "the fake moves the owning business unit with the owner team");
    }

    [Fact]
    public async Task Reparent_WhenAConcurrentFilingResolvesToNoOwner_ThrowsUnsettled_WritingNoOwnerAndNoPutBack()
    {
        // Must-fire: another writer files the row under a project flagged secure but not isolated (a failed provisioning)
        // after this change. The re-derivation is REFUSED: no owner is written, the put-back is not sent over that newer
        // filing, and the call throws the typed unsettled failure.
        var (directory, documentId, request) = OrdinaryRefileWorld();
        directory.WithRecord("sprk_project", NotIsolatedProjectId, ChildBu, isSecure: true, owningTeam: ChildTeam);

        var act = () => Build(directory).ReparentAsync(
            request,
            Applies(directory, request, () => directory.Write("sprk_document", documentId, new()
            {
                ["sprk_relatedproject"] = new EntityReference("sprk_project", NotIsolatedProjectId),
            })),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>())
            .WithMessage($"*resolve to no owner ({RecordOwnerRefusal.SecureParentNotIsolated})*");
        directory.ConditionalWrites.Should().BeEmpty("no owner write is sent for a refused re-derivation");
        directory.FieldUpdates.Should().BeEmpty("the put-back is never written over the newer filing");
        OwnerOf(directory, documentId).Should().Be(GeneralTeam);
    }

    [Fact]
    public async Task Reparent_WhenAMoveIntoASecureRecordKeepsMeetingAChangedRow_TheSecureTeamIsWrittenUnconditionally_AndTheReFileStands()
    {
        // Must-fire (F1-b): the document moves under a secure project; every conditional owner write meets a row another
        // writer just touched (an unrelated column). When the attempts run out toward the SECURE team, one unconditional
        // write of the Secure team lands — the row is never left under the secure project with its ordinary owner.
        var (directory, documentId) = MoveIntoSecureWorld();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var contended = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (fields.ContainsKey("ownerid") && ++contended <= RecordOwnershipResolver.MaxOwnerWriteAttempts)
                world.Write("sprk_document", documentId, new() { ["sprk_name"] = $"edit {contended}" });
        };

        var resolution = await Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        directory.ConditionalWrites.Should().HaveCount(RecordOwnershipResolver.MaxOwnerWriteAttempts)
            .And.OnlyContain(w => !w.Applied);
        directory.Assignments.Should().Equal(("sprk_document", documentId, SecureNamedTeam));
        directory.FieldUpdates.Should().BeEmpty("the move into the secure project stands");
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("sprk_relatedproject")
            .Should().Be(secureProject);
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenThePutBackMeetsAnUnrelatedEdit_ItIsReadAgainAndApplied()
    {
        // Must-fire: the owner assignment fails; before the put-back lands, an edit that moves no filing and no owner bumps the
        // row's version. The conditional put-back is refused, the row is read again (still filed and owned as this re-file
        // left it) and the put-back is applied at the new version.
        var (directory, documentId) = MoveIntoSecureWorld();
        var refusal = new InvalidOperationException("Read Privilege Check For Owner failed ... missing prvReadsprk_Document");
        directory.AssignmentFault = refusal;
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var edited = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (edited || fields.ContainsKey("ownerid"))
                return;
            edited = true;
            world.Write("sprk_document", documentId, new() { ["sprk_name"] = "renamed" });
        };

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(refusal);
        directory.ConditionalWrites.Where(w => !w.Fields.ContainsKey("ownerid")).Select(w => w.Applied)
            .Should().Equal(new[] { false, true }, "refused at the stale version, applied at the version read again");
        directory.Row("sprk_document", documentId).Contains("sprk_relatedproject").Should().BeFalse("the filing is put back");
        OwnerOf(directory, documentId).Should().Be(ChildTeam);
    }

    [Fact]
    public async Task Reparent_WhenAnotherWriterReOwnsTheRowBeforeThePutBack_ThePutBackIsNotWritten()
    {
        // Must-fire (K2): the owner assignment fails; before the put-back lands, another writer (the reconcile) gives the row the
        // Secure team without moving its filing. The filing now matches an owner that is NOT the one the put-back restores it
        // for, so the put-back is left to that writer and the owner is re-checked: the row stays under the secure project,
        // owned by the Secure team.
        var (directory, documentId) = MoveIntoSecureWorld();
        directory.AssignmentFault = new InvalidOperationException("Read Privilege Check For Owner failed ... missing prvReadsprk_Document");
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var reOwned = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (reOwned || fields.ContainsKey("ownerid"))
                return;
            reOwned = true;
            world.Write("sprk_document", documentId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
        };

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        reOwned.Should().BeTrue();
        directory.FieldUpdates.Should().BeEmpty("a row another writer re-owned is never put back");
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("sprk_relatedproject")
            .Should().Be(secureProject);
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenTheAssignmentLandsButTheRowWasReFiledMeanwhile_TheFailureSurfaces_AndTheNewerFilingStands()
    {
        // Must-fire (RecoverFromFailedAssignmentAsync, "write landed but row re-filed"): the Secure team's assignment lands
        // but reports a failure, and another writer moved the row under a second secure matter while it was in flight. The
        // owner reads back as the resolved team, yet the row is no longer filed as it was resolved — not "the re-file
        // stands": the failure surfaces, and the newer filing is not put back.
        var (directory, documentId) = MoveIntoSecureWorld();
        directory.WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
        var lostResponse = new InvalidOperationException("The operation timed out");
        directory.AssignmentFault = lostResponse;
        directory.AssignmentLandsBeforeFault = true;
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var otherSecureMatter = new EntityReference("sprk_matter", OtherSecureMatterId);
        var refiled = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (refiled || !fields.ContainsKey("ownerid"))
                return;
            refiled = true;
            // The re-file lands while the assignment is in flight (after its version check: no version bump here).
            world.Row("sprk_document", documentId)["sprk_matter"] = otherSecureMatter;
        };

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(lostResponse);
        directory.FieldUpdates.Should().BeEmpty("the newer filing is never put back");
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("sprk_matter")
            .Should().Be(otherSecureMatter);
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenTheChangeNamesTheParentTableInAnotherCase_TheRowReadBackIsStillFiledAsResolved()
    {
        // Must-fire (F3): a writer passes the lookup's table as it was given ("sprk_Project"); Dataverse reads it back
        // lower-case. The two are the same parent: after a failed Secure assignment the filing is PUT BACK (the row is still
        // filed as this re-file left it) — never mistaken for another writer's newer filing and left under the secure project
        // with its ordinary owner.
        var (directory, documentId) = MoveIntoSecureWorld();
        directory.AssignmentFault = new InvalidOperationException("Read Privilege Check For Owner failed ... missing prvReadsprk_Document");

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", new EntityReference("sprk_Project", SecureProjectId)),
            Writes(directory, documentId, "sprk_relatedproject", new EntityReference("sprk_project", SecureProjectId)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        directory.FieldUpdates.Should().ContainSingle()
            .Which.Fields.Should().ContainKey("sprk_relatedproject").WhoseValue.Should().Be(DBNull.Value);
        directory.Row("sprk_document", documentId).Contains("sprk_relatedproject").Should().BeFalse();
        OwnerOf(directory, documentId).Should().Be(ChildTeam);
    }

    // ---- #2143 fix round 2: the guard keys on this call's F3 decision (F1), the unconditional Secure write is checked against
    //      a fresh read (F2), committed reads (NoLock), and no blind put-back toward an ordinary owner (K1) ----

    [Fact]
    public async Task Reparent_WhenAnotherWritersSecureWriteLandsAfterThisCallsPermittedMoveOut_TheOrdinaryOwnerIsWritten()
    {
        // Must-fire (F1): R1 moved the document under the secure project (its filing is written, its Secure owner write still in
        // flight). This call (Full Access on that project) reads the row AFTER R1's filing, so its F3 check is asked about leaving
        // the project and permits it. Then R1's Secure write lands, then this call's change moves the row to the ordinary matter.
        // The row is filed exactly as this call resolved it and held in the Secure Record business unit by R1's write: the move
        // out this call's own F3 check permitted — the ordinary owner its parents give is written, never a 409 that strands it.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: GeneralTeam, extra: new()
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
                ["createdby"] = new EntityReference("systemuser", SomeoneElse),
            });
        var asked = new List<Guid>();
        var request = MoveToOrdinaryMatter(documentId, CallerHolding(FullAccess, asked: asked));

        var resolution = await Build(directory).ReparentAsync(
            request,
            async ct =>
            {
                directory.Write("sprk_document", documentId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
                await Applies(directory, request)(ct);
            },
            CancellationToken.None);

        asked.Should().Equal(new[] { SecureProjectId }, "this call's F3 check was asked about the project it leaves");
        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
        OwnerOf(directory, documentId).Should().Be(ChildTeam, "filed under the ordinary matter, owned by its team");
    }

    [Fact]
    public async Task Reparent_WhenTheRowIsHeldInTheSecureUnitByAnotherWriterAndThisCallAskedNoF3_NoOrdinaryOwnerIsWritten()
    {
        // Must-fire (the guard's before-unit arm): an ordinary re-file between two ordinary matters asks no F3 question. Before its
        // owner is settled another writer places the row in the Secure Record business unit without changing its filing. The row
        // is filed as this call resolved it, but it was NOT held there before the change and no F3 check of this call permitted
        // it to leave: no ordinary owner is written, it stays held (over-restricted), and the call throws.
        var (directory, documentId, request) = OrdinaryRefileWorld();

        var act = () => Build(directory).ReparentAsync(
            request,
            Applies(directory, request, () => directory.Write("sprk_document", documentId, new()
            {
                ["ownerid"] = new EntityReference("team", SecureNamedTeam),
            })),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>())
            .WithMessage("*would take it out of the Secure Record business unit on a filing this re-file did not make*");
        directory.ConditionalWrites.Should().BeEmpty("no ordinary owner is ever sent");
        directory.Assignments.Should().BeEmpty();
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenItsSecureParentIsUnsecuredBeforeTheOwnerWrite_ARowHeldThereBeforeTheChangeTakesTheOrdinaryOwner()
    {
        // Must-NOT-fire control for the guard's before-unit arm: this re-file adds an ordinary matter to a document under a
        // secure project (it stays the Secure team's; no F3 question is asked). Before its owner write, the project is
        // un-secured (the F3-checked unsecure flow). The row is filed as this call resolved it and was held in the Secure Record
        // business unit BEFORE the change, so the ordinary owner its parents now give is written.
        var (directory, documentId) = SecureDocumentWorld();
        var request = MoveUnder(documentId, "sprk_matter", new EntityReference("sprk_matter", MatterId));

        var resolution = await Build(directory).ReparentAsync(
            request,
            Applies(directory, request,
                () => directory.WithRecord("sprk_project", SecureProjectId, ChildBu, isSecure: false, owningTeam: ChildTeam)),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        OwnerOf(directory, documentId).Should().Be(ChildTeam);
    }

    /// <summary>
    /// #2143 round 2 (F2): every conditional owner write of a move INTO the secure project meets a changed row; before the
    /// LAST one, a mover takes the row back out from under the project (<paramref name="moverOwner"/>: the owner its own
    /// settle writes, or <c>null</c> when it writes none), so the attempts run out toward the Secure team and its
    /// unconditional write lands AFTER the mover's.
    /// </summary>
    private static void MovedOutBeforeTheLastOwnerWrite(FakeDirectory directory, Guid documentId, Guid? moverOwner)
    {
        var ownerWrites = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (!fields.ContainsKey("ownerid") || ++ownerWrites > RecordOwnershipResolver.MaxOwnerWriteAttempts)
                return;
            if (ownerWrites < RecordOwnershipResolver.MaxOwnerWriteAttempts)
            {
                world.Write("sprk_document", documentId, new() { ["sprk_name"] = $"edit {ownerWrites}" });
                return;
            }

            var mover = new Dictionary<string, object?> { ["sprk_relatedproject"] = null };
            if (moverOwner is { } team)
                mover["ownerid"] = new EntityReference("team", team);
            world.Write("sprk_document", documentId, mover);
        };
    }

    [Fact]
    public async Task Reparent_WhenTheRowIsMovedOutBetweenTheLastReadAndTheUnconditionalSecureWrite_ItsOrdinaryOwnerIsPutBack_AndTheCallThrows()
    {
        // Must-fire (F2): the mover settles first (its ordinary owner, a 200 to it); then this call's unconditional Secure write
        // lands on a row filed under no secure record. The fresh read shows it: the Secure owner is this call's own, so the owner
        // the row's parents give is written back conditionally, and the call throws (no step after the re-file runs) instead of
        // answering success over a row isolated under ordinary parents.
        var (directory, documentId) = MoveIntoSecureWorld();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        MovedOutBeforeTheLastOwnerWrite(directory, documentId, moverOwner: ChildTeam);

        var act = () => Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>())
            .WithMessage("*moved out from under its secure record by another writer*")
            .Which.RecordId.Should().Be(documentId);
        directory.Assignments.Should().Equal(
            new[] { ("sprk_document", documentId, SecureNamedTeam), ("sprk_document", documentId, ChildTeam) },
            "the unconditional Secure write, then the owner the fresh parents give, written back");
        directory.FieldUpdates.Should().BeEmpty("the mover's filing is never put back over");
        directory.Row("sprk_document", documentId).Contains("sprk_relatedproject").Should().BeFalse("the mover's filing stands");
        OwnerOf(directory, documentId).Should().Be(ChildTeam, "filed under the ordinary matter only, owned by its team");
    }

    [Fact]
    public async Task Reparent_WhenTheRowMovedOutWasAlreadyHeldInTheSecureUnit_TheUnconditionalSecureOwnerIsKept_AndLoggedForRepair()
    {
        // Must-NOT-fire control for the F2 put-back: the row was ALREADY held in the Secure Record business unit at this call's
        // last read (another Secure team owned it — a hold another writer's guard or the reconcile placed), so its isolation is
        // not this call's own write and only an F3-checked move may lift it. The mover writes no owner. Nothing ordinary is
        // written: it stays held (over-restricted, never exposed), the call throws, and the row is logged for repair.
        var documentId = Guid.NewGuid();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureDefaultTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });
        MovedOutBeforeTheLastOwnerWrite(directory, documentId, moverOwner: null);
        var log = new ResolverLog();

        var act = () => directory.Resolver(logger: log).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        await act.Should().ThrowAsync<RecordOwnerUnsettledException>();
        directory.Assignments.Should().Equal(new[] { ("sprk_document", documentId, SecureNamedTeam) },
            "only the unconditional Secure write — no ordinary owner");
        directory.FieldUpdates.Should().BeEmpty();
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("No sweep releases it"));
    }

    // ---- #2143 round 3 ----

    private static readonly Guid SecureThreadMatterId = Guid.Parse("21430000-0000-4000-8000-000000000031");
    private static readonly Guid SecureThreadId = Guid.Parse("21430000-0000-4000-8000-000000000032");
    private static readonly Guid OrdinaryThreadId = Guid.Parse("21430000-0000-4000-8000-000000000033");

    /// <summary>
    /// #2143 round 3 (F1): a message (<c>sprk_communication</c>) owned in <paramref name="owningBu"/> by
    /// <paramref name="owningTeam"/>, regarding <paramref name="regardingMatterId"/>, in <paramref name="threadId"/> when given.
    /// Seeded beside it: the ordinary matter (<see cref="MatterId"/>), the secure matter (<see cref="OtherSecureMatterId"/>),
    /// and two threads — one regarding a SECURE matter of its own (<see cref="SecureThreadId"/>, the thread a join makes the
    /// message inherit), one ordinary (<see cref="OrdinaryThreadId"/>).
    /// </summary>
    private static (FakeDirectory Directory, Guid MessageId) MessageWorld(
        Guid regardingMatterId, Guid owningBu, Guid owningTeam, Guid? threadId = null)
    {
        var messageId = Guid.NewGuid();
        var columns = new Dictionary<string, object>
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", regardingMatterId),
            ["createdby"] = new EntityReference("systemuser", SomeoneElse),
        };
        if (threadId is { } thread)
            columns["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", thread);

        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", SecureThreadMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_communicationthread", SecureThreadId, SecureBu, owningTeam: SecureNamedTeam, extra: new()
            {
                ["sprk_threadtype"] = new OptionSetValue(100000000), // #2143 r7d: a record thread
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
            })
            .WithRecord("sprk_communicationthread", OrdinaryThreadId, ChildBu, owningTeam: ChildTeam, extra: new()
            {
                ["sprk_threadtype"] = new OptionSetValue(100000000), // #2143 r7d: a record thread
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            })
            .WithRecord("sprk_communication", messageId, owningBu, owningTeam: owningTeam, extra: columns);
        return (directory, messageId);
    }

    private static Guid? MessageOwnerOf(FakeDirectory directory, Guid messageId) =>
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("owningteam")?.Id;

    /// <summary>
    /// #2143 round 3 (F1): A re-files the message into the secure matter; every conditional owner write meets a changed row,
    /// and before the LAST one M moves the message back to the ordinary matter and settles its ordinary owner. A's attempts
    /// run out toward the Secure team, so its unconditional Secure write lands after M's.
    /// </summary>
    private static void MessageMovedOutBeforeTheLastOwnerWrite(FakeDirectory directory, Guid messageId)
    {
        var ownerWrites = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (!fields.ContainsKey("ownerid") || ++ownerWrites > RecordOwnershipResolver.MaxOwnerWriteAttempts)
                return;
            world.Write("sprk_communication", messageId, ownerWrites < RecordOwnershipResolver.MaxOwnerWriteAttempts
                ? new() { ["sprk_name"] = $"edit {ownerWrites}" }
                : new()
                {
                    ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                    ["ownerid"] = new EntityReference("team", ChildTeam),
                });
        };
    }

    /// <summary>
    /// #2143 round 6: no owner write this call sent — applied or met by a 412 — carried an ORDINARY team: every assignment and
    /// every conditional owner write is the Secure team.
    /// </summary>
    private static void NoOrdinaryOwnerEverSent(FakeDirectory directory)
    {
        directory.Assignments.Should().NotContain(a => a.TeamId != SecureNamedTeam, "no ordinary owner is ever written");
        directory.ConditionalWrites.Should().NotContain(
            w => w.Fields.ContainsKey("ownerid") && ((EntityReference)w.Fields["ownerid"]).Id != SecureNamedTeam,
            "no ordinary owner is ever sent");
    }

    private static RecordReparent MoveMessageIntoTheSecureMatter(Guid messageId) => new()
    {
        EntityLogicalName = "sprk_communication",
        RecordId = messageId,
        ParentChanges = new Dictionary<string, EntityReference?>
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherSecureMatterId),
        },
    };

    [Fact]
    public async Task Reparent_WhenAThreadJoinLandsOnAMessageAfterItsUnconditionalSecureWrite_ItSettlesSecure_AndNoOrdinaryOwnerIsEverWritten()
    {
        // Must-fire (round 3 F1, re-pinned round 6): A re-files a message into the secure matter and exhausts toward Secure; M
        // moves it back to the ordinary matter and settles ordinary; A writes the Secure team unconditionally; then W joins the
        // message to a thread regarding ANOTHER secure matter (W's settle finds the Secure team already there). A's fresh read
        // resolves the message's thread too (RecordThreadFiling): its parents still give the Secure team, so A's Secure owner
        // is confirmed and the call succeeds — never put back to the ordinary team.
        // Round 6: the throw this test used to expect came from the unresolved thread (the fresh parents could not see it).
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam);
        MessageMovedOutBeforeTheLastOwnerWrite(directory, messageId);
        var joined = false;
        directory.AfterWrite = (world, fields) =>
        {
            if (joined || fields.GetValueOrDefault("ownerid") is not EntityReference { Id: var team } || team != SecureNamedTeam)
                return;
            joined = true;
            world.Write("sprk_communication", messageId, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
            });
        };
        var request = MoveMessageIntoTheSecureMatter(messageId);

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        joined.Should().BeTrue("the join landed after the unconditional Secure write");
        resolution.IsOwned.Should().BeTrue();
        resolution.IsSecureOwner.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        NoOrdinaryOwnerEverSent(directory);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam, "in a secure record's thread");
    }

    [Fact]
    public async Task Reparent_WhenAMessagesThreadIsUnchangedAcrossItsUnconditionalSecureWrite_TheOrdinaryOwnerIsPutBack()
    {
        // Must-NOT-fire control for F1 (the release): the same interleaving, but the message sits in an ordinary thread at
        // every read and nobody joins it anywhere. Its lineage-only lookup is unchanged, so the release proceeds as in round 2:
        // the owner the fresh parents give is written back and the call throws.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: OrdinaryThreadId);
        MessageMovedOutBeforeTheLastOwnerWrite(directory, messageId);
        var request = MoveMessageIntoTheSecureMatter(messageId);

        var act = () => Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>())
            .WithMessage("*moved out from under its secure record by another writer*");
        directory.Assignments.Should().Equal(
            new[] { ("sprk_communication", messageId, SecureNamedTeam), ("sprk_communication", messageId, ChildTeam) });
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam);
    }

    [Fact]
    public async Task Reparent_WhenAThreadJoinMakesAPermittedMoveOutsRowSecure_ItSettlesSecure_AndNoOrdinaryOwnerIsEverWritten()
    {
        // Must-fire (round 3 F1 guard, re-pinned round 6): this call (Full Access on the secure matter) moves a message out of
        // it to the ordinary matter; its F3 check permits that. Before this call's owner write, W joins it to a thread regarding
        // another secure matter and W's settle writes the Secure team. The settle resolves the thread (RecordThreadFiling): the
        // parents the message holds now give the Secure team, which it already has — settled Secure, success, no ordinary owner.
        // Round 6: the throw this test used to expect came from the unresolved thread (the guard saw an ordinary target).
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, ChildBu, ChildTeam);
        var asked = new List<Guid>();
        var request = MoveMessageTo(messageId, MatterId, CallerHolding(FullAccess, asked: asked));

        var resolution = await Build(directory).ReparentAsync(
            request,
            async ct =>
            {
                directory.Write("sprk_communication", messageId, new()
                {
                    ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
                    ["ownerid"] = new EntityReference("team", SecureNamedTeam),
                });
                await Applies(directory, request)(ct);
            },
            CancellationToken.None);

        asked.Should().Equal(new[] { OtherSecureMatterId }, "the F3 check was asked and permitted the move out");
        resolution.IsSecureOwner.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        NoOrdinaryOwnerEverSent(directory);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenThisCallsOwnChangeMovesTheThreadOfAPermittedMoveOut_TheF3ExemptionStillApplies()
    {
        // Must-NOT-fire control for F1 (the guard): the thread lookup this call's OWN change sets is not another writer's move.
        // A permitted move out of the secure matter that also files the message into an ordinary thread, while another writer's
        // Secure write lands, still takes the ordinary owner its parents give (round 2 behaviour).
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, ChildBu, ChildTeam);
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", OrdinaryThreadId),
            },
            SecureExitCaller = CallerHolding(FullAccess),
        };

        var resolution = await Build(directory).ReparentAsync(
            request,
            async ct =>
            {
                directory.Write("sprk_communication", messageId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
                await Applies(directory, request)(ct);
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam);
    }

    // ---- #2143 round 4 ----

    /// <summary>
    /// #2143 round 4: a message regarding the secure matter, held in the Secure Record business unit by the Secure team before
    /// the change (in <paramref name="threadId"/> when given), re-filed by this call onto an ordinary project too — it stays
    /// under the secure matter, so it resolves to the Secure team and no F3 question is asked.
    /// </summary>
    private static (FakeDirectory Directory, Guid MessageId, RecordReparent Request) HeldMessageAlsoFiledOnAnOrdinaryProject(
        Guid? threadId = null)
    {
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, SecureBu, SecureNamedTeam, threadId);
        directory.WithRecord("sprk_project", OrdinaryProjectId, ChildBu, owningTeam: ChildTeam);
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingproject"] = new EntityReference("sprk_project", OrdinaryProjectId),
            },
        };
        return (directory, messageId, request);
    }

    /// <summary>The F3-checked unsecure flow: the secure matter the message regards becomes an ordinary one.</summary>
    private static void UnsecureTheMessagesMatter(FakeDirectory directory) =>
        directory.WithRecord("sprk_matter", OtherSecureMatterId, ChildBu, isSecure: false, owningTeam: ChildTeam);

    [Fact]
    public async Task Reparent_WhenAThreadJoinLandsOnARowHeldSecureBeforeTheChangeWhoseSecureParentIsUnsecured_ItSettlesSecure_AndNoOrdinaryOwnerIsEverWritten()
    {
        // Must-fire (round 4, re-pinned round 6): the message was held in the Secure Record business unit before the change.
        // Before this call's owner write, its secure matter is un-secured AND another writer W joins it to a thread regarding
        // another secure matter. The settle resolves the thread (RecordThreadFiling): the parents the message holds now give the
        // Secure team, which it already has — settled Secure, success, no ordinary owner.
        // Round 6: the throw this test used to expect came from the unresolved thread (the guard saw an ordinary target).
        var (directory, messageId, request) = HeldMessageAlsoFiledOnAnOrdinaryProject();

        var resolution = await Build(directory).ReparentAsync(
            request,
            Applies(directory, request, () =>
            {
                UnsecureTheMessagesMatter(directory);
                directory.Write("sprk_communication", messageId, new()
                {
                    ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
                });
            }),
            CancellationToken.None);

        resolution.IsSecureOwner.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        NoOrdinaryOwnerEverSent(directory);
        directory.Assignments.Should().BeEmpty("it already holds the Secure team");
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam, "in a secure record's thread");
    }

    [Fact]
    public async Task Reparent_WhenARowHeldSecureBeforeTheChangeHasItsSecureParentUnsecured_AndItsThreadIsUnchanged_TheOrdinaryOwnerIsWritten()
    {
        // Must-NOT-fire control (round 4): the same re-file and un-secure, but the message sits in an ordinary thread at every
        // read and nobody joins it anywhere. Its lineage-only lookup is unchanged, so the before-unit exemption still applies:
        // the ordinary owner its parents now give is written.
        var (directory, messageId, request) = HeldMessageAlsoFiledOnAnOrdinaryProject(threadId: OrdinaryThreadId);

        var resolution = await Build(directory).ReparentAsync(
            request,
            Applies(directory, request, () => UnsecureTheMessagesMatter(directory)),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam);
    }

    // ---- #2143 round 5 ----

    /// <summary>
    /// #2143 round 5: the thread row <see cref="ThreadResolver"/> reads for a join — a record thread anchored to the ordinary
    /// matter (<see cref="OrdinaryThreadId"/> regarding <see cref="MatterId"/>).
    /// </summary>
    private static Mock<IGenericEntityService> OrdinaryRecordThreadRow()
    {
        var threads = new Mock<IGenericEntityService>();
        threads
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", OrdinaryThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communicationthread", OrdinaryThreadId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            });
        return threads;
    }

    [Fact]
    public async Task Reparent_WhenAThreadJoinOfARowHeldSecureMeetsItsSecureParentUnsecured_ItsOwnThreadChangeIsNotAnotherWriters_AndTheOrdinaryOwnerIsWritten()
    {
        // Must-NOT-fire (round 5, F1): the message regards the secure matter and is held in the Secure Record business unit. It
        // joins a record thread anchored to the ordinary matter through the REAL join (ThreadResolver builds the request), and
        // the secure matter is un-secured while the join is written. The thread lookup the join writes is this call's OWN
        // change, never another writer's: the before-unit exemption applies, the ordinary owner the parents now give is
        // written, and the join neither throws nor is put back (which would leave a sent message unthreaded).
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, SecureBu, SecureNamedTeam);
        var joinedThread = new EntityReference("sprk_communicationthread", OrdinaryThreadId);

        await ThreadResolver.AssignToThreadReconcilingOwnerAsync(
            OrdinaryRecordThreadRow().Object, Build(directory), messageId, OrdinaryThreadId,
            _ =>
            {
                directory.Write("sprk_communication", messageId, new()
                {
                    [ThreadResolver.ThreadLookupOnCommunication] = joinedThread,
                });
                UnsecureTheMessagesMatter(directory);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        directory.Assignments.Should().Equal(new[] { ("sprk_communication", messageId, ChildTeam) },
            "the ordinary owner the un-secured filing gives, and nothing else");
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam);
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Should().Be(joinedThread, "the join stands: the message is not left unthreaded");
    }

    [Fact]
    public async Task Reparent_WhenAThreadJoinLandsBeforeTheLastReadAndItsSecureParentIsUnsecuredBeforeTheUnconditionalSecureWrite_ItSettlesSecure_AndNoOrdinaryOwnerIsEverWritten()
    {
        // Must-fire (round 5 F2, re-pinned round 6): A re-files a message into the secure matter S; every conditional owner write
        // meets a changed row. Before A's LAST read, W joins the message to a thread regarding another secure matter; after that
        // read, W's settle writes the Secure team (A's third 412). Then S is un-secured and A writes the Secure team
        // unconditionally. A's fresh read resolves the thread (RecordThreadFiling): the parents still give the Secure team, so the
        // Secure owner is confirmed and the call succeeds — never released to the ordinary team.
        // Round 6: the throw this test used to expect came from the unresolved thread (the fresh parents gave ordinary).
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam);
        var ownerWrites = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (!fields.ContainsKey("ownerid"))
                return;
            switch (++ownerWrites)
            {
                case 1:
                    world.Write("sprk_communication", messageId, new() { ["sprk_name"] = "edit 1" });
                    break;
                case 2: // W's join, before A's last read
                    world.Write("sprk_communication", messageId, new()
                    {
                        ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
                    });
                    break;
                case 3: // W's settle, after A's last read
                    world.Write("sprk_communication", messageId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
                    break;
                case 4: // before A's unconditional Secure write
                    UnsecureTheMessagesMatter(world);
                    break;
            }
        };
        var request = MoveMessageIntoTheSecureMatter(messageId);

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        ownerWrites.Should().BeGreaterThanOrEqualTo(4, "the interleaving ran to the unconditional Secure write");
        resolution.IsSecureOwner.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        NoOrdinaryOwnerEverSent(directory);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam, "held, never exposed");
    }

    [Fact]
    public async Task Reparent_WhenTheAssignmentLandsButAnotherWriterMovedTheThreadFirst_ItIsNotALostResponse_AndTheFailureIsThrown()
    {
        // Must-fire (round 5, the recovery's landed check): A re-files a message (in an ordinary thread) into the secure matter.
        // Just before A's owner write, W joins the message to a thread regarding another secure matter. A's re-read write
        // lands as the Secure team and then reports a failure (a lost response). The owner is the Secure team and the row is
        // still filed as A resolved it — but its thread is W's, not the one A's change left: the Secure owner is not proof
        // that A's assignment landed for A's filing. No success is answered, nothing is put back over W's join, and A's
        // original failure is thrown.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: OrdinaryThreadId);
        var failure = new TimeoutException("response lost");
        directory.AssignmentFault = failure;
        directory.AssignmentLandsBeforeFault = true;
        var joined = false;
        directory.BeforeWrite = (world, fields) =>
        {
            if (joined || !fields.ContainsKey("ownerid"))
                return;
            joined = true;
            world.Write("sprk_communication", messageId, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SecureThreadId),
            });
        };
        var request = MoveMessageIntoTheSecureMatter(messageId);

        var act = () => Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        (await act.Should().ThrowAsync<TimeoutException>("no success over W's thread join")).Which.Should().BeSameAs(failure);
        joined.Should().BeTrue();
        directory.FieldUpdates.Should().BeEmpty("nothing is put back over the other writer's join");
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Id.Should().Be(SecureThreadId, "W's join stands");
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam, "held, never exposed");
    }

    [Fact]
    public async Task Reparent_WhenAThreadJoinsAssignmentLandsButReportsAFailure_TheJoinStands_AndNothingIsPutBack()
    {
        // Must-NOT-fire control for the recovery's landed check: a lost-response thread join through the REAL join
        // (ThreadResolver). The message joins a record thread regarding a secure matter; its Secure owner write lands and then
        // reports a failure. The thread it holds is the one this join wrote (its own lineage change, named in ParentChanges),
        // so the assignment landed: the join returns, the thread stands, and nothing is put back.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam);
        directory.AssignmentFault = new TimeoutException("response lost");
        directory.AssignmentLandsBeforeFault = true;
        var joinedThread = new EntityReference("sprk_communicationthread", SecureThreadId);
        var threads = new Mock<IGenericEntityService>();
        threads
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", SecureThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communicationthread", SecureThreadId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
            });

        var assignment = await ThreadResolver.AssignToThreadReconcilingOwnerAsync(
            threads.Object, Build(directory), messageId, SecureThreadId,
            _ =>
            {
                directory.Write("sprk_communication", messageId, new()
                {
                    [ThreadResolver.ThreadLookupOnCommunication] = joinedThread,
                });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        assignment.Should().NotBeNull("the join returns: the re-file stands");
        directory.Assignments.Should().Equal(new[] { ("sprk_communication", messageId, SecureNamedTeam) });
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Should().Be(joinedThread, "the join stands");
        directory.FieldUpdates.Should().BeEmpty("filing and owner agree — nothing to undo");
    }

    // ---- #2143 round 6: a message's record thread files it (RecordThreadFiling) ----

    private static readonly Guid DirectThreadId = Guid.Parse("21430000-0000-4000-8000-000000000061");

    [Fact]
    public async Task Reparent_WhenASecureThreadJoinIsOnTheMessageBeforeTheReFile_AndItsSecureParentIsUnsecured_ItSettlesSecure_AndOnlyTheSecureTeamIsAssigned()
    {
        // Must-fire (round 6, (a)): the round-5 F2 interleaving with W's thread write moved BEFORE the re-file — the join is on
        // the message at A's first read, so no lineage snapshot can tell it apart. A re-files the message into secure matter S;
        // every conditional owner write meets a changed row (W's settle writes the Secure team on the third); S is un-secured
        // before A's unconditional Secure write. The thread's secure matter is one of the message's parents at every read, so
        // the parents still give the Secure team: settled Secure, success, and only the Secure team is ever assigned.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: SecureThreadId);
        var ownerWrites = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (!fields.ContainsKey("ownerid"))
                return;
            switch (++ownerWrites)
            {
                case 1:
                case 2:
                    world.Write("sprk_communication", messageId, new() { ["sprk_name"] = $"edit {ownerWrites}" });
                    break;
                case 3: // W's settle
                    world.Write("sprk_communication", messageId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
                    break;
                case 4: // before A's unconditional Secure write
                    UnsecureTheMessagesMatter(world);
                    break;
            }
        };
        // Round 7b: the re-file leaves the thread's secure matter for another (the thread reconcile moves the message to the new
        // matter's thread), so F3 is asked about it - A holds Full Access.
        var request = MoveMessageIntoTheSecureMatter(messageId) with { SecureExitCaller = CallerHolding(FullAccess) };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        ownerWrites.Should().BeGreaterThanOrEqualTo(4, "the interleaving ran to the unconditional Secure write");
        resolution.IsOwned.Should().BeTrue();
        resolution.IsSecureOwner.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        directory.Assignments.Should().NotBeEmpty().And.OnlyContain(a => a.TeamId == SecureNamedTeam);
        NoOrdinaryOwnerEverSent(directory);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenAMessageInASecureRecordsThreadIsMovedOffItsOtherSecureParent_ByAnS1OnlyHolder_IsRefused_F3AskedAboutBoth()
    {
        // Must-fire (round 6 (b), re-pinned round 7b): the message regards secure matter S1 and sits in a thread regarding secure
        // matter S2. A caller holding F3 on S1 only re-files its regarding to the ordinary matter O. The thread reconcile that
        // follows moves it off S2's thread (it is no longer filed to S2), so the re-file leaves S2 too: F3 is asked about both
        // and the caller is refused before anything is written.
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, SecureBu, SecureNamedTeam, threadId: SecureThreadId);
        var asked = new List<Guid>();
        var caller = CallerHolding(Collaborate,
            perRecord: new Dictionary<Guid, AccessRights> { [OtherSecureMatterId] = FullAccess }, asked: asked);
        var request = MoveMessageTo(messageId, MatterId, caller);
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            request,
            _ =>
            {
                applied = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        asked.Should().BeEquivalentTo(new[] { OtherSecureMatterId, SecureThreadMatterId });
        applied.Should().BeFalse("refused before anything is written");
        directory.Assignments.Should().BeEmpty();
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenAMessageInASecureRecordsThreadIsMovedOffItsOtherSecureParent_ByAHolderOfBoth_ItStaysSecure_UntilTheThreadFollows()
    {
        // Must-NOT-fire control (round 6 (b), round 7b): the same re-file by a caller holding F3 on S1 AND S2 is permitted and
        // written; the message stays in S2's thread until the thread reconcile follows the filing, so its owner stays the
        // Secure team (secure-if-any) and nothing is assigned here.
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, SecureBu, SecureNamedTeam, threadId: SecureThreadId);
        var asked = new List<Guid>();
        var caller = CallerHolding(FullAccess, asked: asked);
        var request = MoveMessageTo(messageId, MatterId, caller);

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsOwned.Should().BeTrue(resolution.Reason);
        resolution.IsSecureOwner.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam, "unchanged: still in S2's thread");
        directory.Assignments.Should().BeEmpty();
        NoOrdinaryOwnerEverSent(directory);
        asked.Should().BeEquivalentTo(new[] { OtherSecureMatterId, SecureThreadMatterId }, "S1, and S2's thread the reconcile leaves");
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_regardingmatter").Id
            .Should().Be(MatterId, "the re-file itself stands");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reparent_WhenAMessageLeavesASecureRecordsThreadAsItsRegardingMovesOffItsOtherSecureParent_F3IsAskedAboutBoth_AndAnS1OnlyHolderIsRefused(
        bool intoAnOrdinaryThread)
    {
        // Must-fire (round 6, (b2)): the same message (regarding S1, in S2's thread) is moved off S1 AND out of S2's thread in the
        // same change — the thread cleared, or set to an ordinary record thread. It leaves S2 too, so F3 is asked about S2 as
        // well, and a caller holding F3 on S1 only is refused before anything is written.
        var (directory, messageId) = MessageWorld(OtherSecureMatterId, SecureBu, SecureNamedTeam, threadId: SecureThreadId);
        var asked = new List<Guid>();
        var caller = CallerHolding(Collaborate,
            perRecord: new Dictionary<Guid, AccessRights> { [OtherSecureMatterId] = FullAccess }, asked: asked);
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_communicationthread"] = intoAnOrdinaryThread
                    ? new EntityReference("sprk_communicationthread", OrdinaryThreadId)
                    : null,
            },
            SecureExitCaller = caller,
        };
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            request,
            _ =>
            {
                applied = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        asked.Should().Contain(SecureThreadMatterId, "leaving S2's thread leaves S2");
        applied.Should().BeFalse("refused before anything is written");
        directory.Assignments.Should().BeEmpty();
        directory.ConditionalWrites.Should().BeEmpty();
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenTheMessagesThreadCannotBeRead_ItFailsClosed_BeforeAnyWrite()
    {
        // Must-fire (round 6, (c)): the thread the message sits in does not come back. "No parents" would release a message
        // from a secure thread, so the call fails before the change is applied or anything is written.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: Guid.NewGuid());
        var request = MoveMessageIntoTheSecureMatter(messageId);
        var applied = false;

        var act = () => Build(directory).ReparentAsync(
            request,
            _ =>
            {
                applied = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*sprk_communicationthread*could not be read*");
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
        directory.ConditionalWrites.Should().BeEmpty();
        directory.FieldUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OfAMessageInADirectThread_TheThreadAddsNoParent()
    {
        // Must-NOT-fire (round 6): a per-user master or Direct thread carries no regarding, so it files the message under
        // nothing (the thread is never an ownership parent). A message in one, filed onto the ordinary matter, takes that
        // matter's team — exactly as it would in no thread.
        var (directory, messageId) = MessageWorld(OtherOrdinaryMatterId, GeneralBu, GeneralTeam, threadId: DirectThreadId);
        directory
            .WithRecord("sprk_matter", OtherOrdinaryMatterId, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_communicationthread", DirectThreadId, SecureBu, owningTeam: SecureNamedTeam, extra: new() { ["sprk_threadtype"] = new OptionSetValue(100000001) }); // #2143 r7d: Direct
        var request = MoveMessageTo(messageId, MatterId, CallerHolding(Collaborate));

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsSecureOwner.Should().BeFalse();
        resolution.OwningTeamId.Should().Be(ChildTeam, "the ordinary matter's team; the Direct thread adds nothing");
        MessageOwnerOf(directory, messageId).Should().Be(ChildTeam);
    }

    [Fact]
    public async Task Reparent_WhenAMessageMovesIntoAnOrdinaryRecordThread_ItTakesThatThreadsOrdinaryParentOnly()
    {
        // Must-NOT-fire (round 6): a message with no regarding, in a Direct thread, is moved into an ordinary record thread (the
        // thread lookup is its only change). The thread's record is an ordinary matter, so the message takes that matter's
        // team — an ordinary owner, never the Secure team — and the move is a parent change.
        var messageId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_communicationthread", DirectThreadId, GeneralBu, owningTeam: GeneralTeam, extra: new() { ["sprk_threadtype"] = new OptionSetValue(100000001) }) // #2143 r7d: Direct
            .WithRecord("sprk_communicationthread", OrdinaryThreadId, ChildBu, owningTeam: ChildTeam, extra: new()
            {
                ["sprk_threadtype"] = new OptionSetValue(100000000), // #2143 r7d: a record thread
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            })
            .WithRecord("sprk_communication", messageId, GeneralBu, owningTeam: GeneralTeam, extra: new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", DirectThreadId),
            });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = messageId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", OrdinaryThreadId),
            },
            WhenUnfiled = UnfiledOwnership.KeepCreator,
        };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsOwned.Should().BeTrue(resolution.Reason);
        resolution.IsSecureOwner.Should().BeFalse();
        resolution.OwningTeamId.Should().Be(ChildTeam);
        resolution.ParentsUnchanged.Should().BeFalse();
        directory.Assignments.Should().Equal(new[] { ("sprk_communication", messageId, ChildTeam) });
    }

    [Fact]
    public async Task Reparent_WhenAMessageJoinsASecureRecordThreadUncontended_ThroughTheRealJoin_ItSettlesSecure()
    {
        // Must-NOT-fire (round 6): a normal join through ThreadResolver — no InheritedParents any more; the resolver reads the
        // thread the join names and files the message under its secure matter. Settled Secure, the thread stands.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam);
        var joinedThread = new EntityReference("sprk_communicationthread", SecureThreadId);
        var threads = new Mock<IGenericEntityService>();
        threads
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", SecureThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communicationthread", SecureThreadId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
            });

        await ThreadResolver.AssignToThreadReconcilingOwnerAsync(
            threads.Object, Build(directory), messageId, SecureThreadId,
            _ =>
            {
                directory.Write("sprk_communication", messageId, new()
                {
                    [ThreadResolver.ThreadLookupOnCommunication] = joinedThread,
                });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        directory.Assignments.Should().Equal(new[] { ("sprk_communication", messageId, SecureNamedTeam) });
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Should().Be(joinedThread);
    }

    [Fact]
    public async Task Reparent_WhenAPermittedMoveOutsParentsAreChangedByAnotherWriter_ARowHeldInTheSecureUnitIsNotGivenAnOrdinaryOwner()
    {
        // Must-fire (the guard's conjunction): this call's F3 check permitted the move out of the secure project, and another
        // writer's Secure write holds the row in the Secure Record business unit — but ANOTHER writer then re-files it to a
        // different ordinary matter, so the filing is not this call's. The permission does not travel to that filing: no ordinary
        // owner is written (a precedence slip `!(parentsAsResolved && beforeHeld || permitted)` would write one).
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_matter", OtherOrdinaryMatterId, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: GeneralTeam, extra: new()
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
                ["createdby"] = new EntityReference("systemuser", SomeoneElse),
            });
        var request = MoveToOrdinaryMatter(documentId, CallerHolding(FullAccess));

        var act = () => Build(directory).ReparentAsync(
            request,
            async ct =>
            {
                directory.Write("sprk_document", documentId, new() { ["ownerid"] = new EntityReference("team", SecureNamedTeam) });
                await Applies(directory, request)(ct);
                directory.Write("sprk_document", documentId, new()
                {
                    ["sprk_matter"] = new EntityReference("sprk_matter", OtherOrdinaryMatterId),
                });
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnsettledException>())
            .WithMessage("*would take it out of the Secure Record business unit on a filing this re-file did not make*");
        directory.ConditionalWrites.Should().BeEmpty();
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_WhenAnotherWriterReOwnsTheRowAfterTheUnconditionalSecureWrite_TheReleaseWritesNothing()
    {
        // Must-fire (the release's "the Secure owner is this call's own" conjunct): after this call's unconditional Secure write,
        // another writer gives the row an owner of its own. The fresh parents give the child unit's team; the row holds neither
        // that nor this call's Secure team: the release writes nothing and the row is logged as held. (The recovery's re-check
        // then settles the owner the row's parents give, as for any re-file whose filing a newer writer superseded.)
        var (directory, documentId) = MoveIntoSecureWorld();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        MovedOutBeforeTheLastOwnerWrite(directory, documentId, moverOwner: null);
        directory.AfterWrite = (world, fields) =>
        {
            if (fields.GetValueOrDefault("ownerid") is EntityReference { Id: var team } && team == SecureNamedTeam)
                world.Write("sprk_document", documentId, new() { ["ownerid"] = new EntityReference("team", GeneralTeam) });
        };
        var log = new ResolverLog();

        var act = () => directory.Resolver(logger: log).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        await act.Should().ThrowAsync<RecordOwnerUnsettledException>();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("No sweep releases it"),
            "the release held: it never wrote over another writer's owner");
        log.Entries.Should().NotContain(e => e.Message.Contains("This re-file did not stand"), "no release is reported");
        OwnerOf(directory, documentId).Should().Be(ChildTeam, "the recovery's re-check settles the owner its parents give");
    }

    [Fact]
    public async Task Reparent_WhenTheReleaseMeetsAChangedRow_ItIsHeld_NeverRetriedOrReportedReleased()
    {
        // Must-fire (the release's 412 arm): the mover settles its ordinary owner, this call's unconditional Secure write lands,
        // and the release's conditional write meets a row changed again. Nothing is written: the row is held (logged for
        // repair), never reported as released.
        var (directory, documentId) = MoveIntoSecureWorld();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        MovedOutBeforeTheLastOwnerWrite(directory, documentId, moverOwner: ChildTeam);
        var mover = directory.BeforeWrite!;
        var ownerWrites = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            mover(world, fields);
            if (fields.ContainsKey("ownerid") && ++ownerWrites == RecordOwnershipResolver.MaxOwnerWriteAttempts + 2)
                world.Write("sprk_document", documentId, new() { ["sprk_name"] = "edit during the release" });
        };
        var log = new ResolverLog();

        var act = () => directory.Resolver(logger: log).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        await act.Should().ThrowAsync<RecordOwnerUnsettledException>();
        directory.ConditionalWrites.Should().HaveCount(RecordOwnershipResolver.MaxOwnerWriteAttempts + 2)
            .And.OnlyContain(w => !w.Applied, "the attempts, the release, and the recovery's put-back all met a changed row");
        directory.Assignments.Should().Equal(new[] { ("sprk_document", documentId, SecureNamedTeam) });
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam, "held, over-restricted");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("No sweep releases it"));
    }

    [Fact]
    public async Task Reparent_WhenItsSecureParentIsUnsecuredAfterTheUnconditionalSecureWrite_ItsOwnFilingSettles_AndTheCallSucceeds()
    {
        // Must-fire (K4): a move into the secure project exhausts toward Secure (every conditional write meets an unrelated
        // edit); after the unconditional Secure write, the project is un-secured (the F3-checked unsecure flow). The row is still
        // filed exactly as this call left it, so the release writes the owner those parents now give — and the re-file is
        // settled: it answers that owner, never "moved out by another writer".
        var (directory, documentId) = MoveIntoSecureWorld();
        var secureProject = new EntityReference("sprk_project", SecureProjectId);
        var contended = 0;
        directory.BeforeWrite = (world, fields) =>
        {
            if (fields.ContainsKey("ownerid") && ++contended <= RecordOwnershipResolver.MaxOwnerWriteAttempts)
                world.Write("sprk_document", documentId, new() { ["sprk_name"] = $"edit {contended}" });
        };
        var unsecured = false;
        directory.AfterWrite = (world, fields) =>
        {
            if (unsecured || fields.GetValueOrDefault("ownerid") is not EntityReference { Id: var team } || team != SecureNamedTeam)
                return;
            unsecured = true;
            world.WithRecord("sprk_project", SecureProjectId, ChildBu, isSecure: false, owningTeam: ChildTeam);
        };

        var resolution = await Build(directory).ReparentAsync(
            MoveUnder(documentId, "sprk_relatedproject", secureProject),
            Writes(directory, documentId, "sprk_relatedproject", secureProject),
            CancellationToken.None);

        unsecured.Should().BeTrue();
        resolution.IsOwned.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(
            new[] { ("sprk_document", documentId, SecureNamedTeam), ("sprk_document", documentId, ChildTeam) });
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("sprk_relatedproject")
            .Should().Be(secureProject, "the filing is this call's own and stands");
        OwnerOf(directory, documentId).Should().Be(ChildTeam);
    }

    [Fact]
    public async Task Reparent_ReadsEveryParentFactCommitted_TheF3WalkAndTheSettleAlike()
    {
        // K1: the settle and put-back decisions are taken from the parents' facts (owning business unit, sprk_issecure, a parent's
        // own filing), so on the re-file path they are committed reads. An analysis moves from one un-team-owned document to
        // another — the F3 walk reads the document it leaves and the matter above it; the settle reads the new document, its
        // filing and its matter.
        var analysisId = Guid.NewGuid();
        var oldDocumentId = Guid.NewGuid();
        var newDocumentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_matter", OtherOrdinaryMatterId, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_document", oldDocumentId, GeneralBu,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OtherOrdinaryMatterId) })
            .WithRecord("sprk_document", newDocumentId, GeneralBu,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) })
            .WithRecord("sprk_analysis", analysisId, GeneralBu, owningTeam: GeneralTeam,
                extra: new() { ["sprk_documentid"] = new EntityReference("sprk_document", oldDocumentId) });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_analysis",
            RecordId = analysisId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_documentid"] = new EntityReference("sprk_document", newDocumentId),
            },
        };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam,
            "an ordinary analysis takes its primary parent's unit, and a user-owned FILED document answers with its filing (194b, I-6)");
        var parentReads = directory.RecordQueries.Where(q => q.EntityName is "sprk_document" or "sprk_matter").ToList();
        parentReads.Where(q => q.EntityName == "sprk_document" && q.ColumnSet.AllColumns)
            .Select(q => (Guid)q.Criteria.Conditions.Single().Values.Single())
            .Should().Contain(new[] { oldDocumentId, newDocumentId }, "the F3 walk's and the settle's filing reads");
        parentReads.Where(q => q.EntityName == "sprk_matter").Should().NotBeEmpty();
        parentReads.Should().OnlyContain(q => !q.NoLock, "every parent fact on the re-file path is a committed read");
    }

    [Fact]
    public async Task ResolveOwner_OnACreatePath_KeepsItsNoLockParentReads()
    {
        // Must-NOT-fire control for K1: the create paths (hot, shared) are unchanged — their parent reads stay NoLock.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, GeneralBu,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });

        await Build(directory).ResolveOwnerAsync(
            new RecordOwnershipContext { Parents = new[] { new RecordOwnershipParent("sprk_document", documentId) } },
            CancellationToken.None);

        var parentReads = directory.RecordQueries.Where(q => q.EntityName is "sprk_document" or "sprk_matter").ToList();
        parentReads.Should().NotBeEmpty().And.OnlyContain(q => q.NoLock);
    }

    [Fact]
    public async Task Reparent_ReadsTheRowCommitted_NeverWithNoLock()
    {
        // #2143 round 2: "already owned, write nothing", the version a conditional write is conditioned on and the parents the
        // owner is derived from all come from the whole-row read — a committed read, never a dirty one.
        var (directory, _, request) = OrdinaryRefileWorld();

        await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        directory.RecordQueries.Where(q => q.EntityName == "sprk_document" && q.ColumnSet.AllColumns)
            .Should().HaveCountGreaterThan(1, "the read before the change and the settle read")
            .And.OnlyContain(q => !q.NoLock);
    }

    [Fact]
    public async Task Reparent_WhenNoReadSucceedsAfterAMoveToAnOrdinaryParent_ThePutBackIsNeverSentBlind()
    {
        // Must-fire (K1): the document moves out of the secure project to an ordinary matter. After the change no whole-row read
        // succeeds (the settle's, the put-back's); the owner read says the assignment did not land. A blind put-back would restore
        // the secure project over whatever owner a newer writer gave the row — so none is sent: the row stays filed under the
        // ordinary matter with its stricter old owner, logged for repair, and the failure surfaces.
        var (directory, documentId) = SecureDocumentWorld();
        var request = MoveToOrdinaryMatter(documentId, CallerHolding(FullAccess));
        var log = new ResolverLog();

        var act = () => directory.Resolver(logger: log).ReparentAsync(
            request,
            Applies(directory, request, () => directory.WithUnreadable("sprk_document")),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not readable*");
        directory.FieldUpdates.Should().BeEmpty("no put-back is sent blind toward an ordinary owner");
        var row = directory.Row("sprk_document", documentId);
        row.Contains("sprk_project").Should().BeFalse("the secure project is not restored");
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        OwnerOf(directory, documentId).Should().Be(SecureNamedTeam, "over-restricted, never exposed");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("could not be sent safely"),
            "K3: the re-check names why the filing was not put back, and does not claim a newer writer re-filed it");
    }

    [Fact]
    public void RecordOwnershipParent_NormalisesItsTable_SoTwoSpellingsOfOneRowAreEqual()
    {
        var id = Guid.NewGuid();

        new RecordOwnershipParent(" sprk_Matter ", id).Should().Be(new RecordOwnershipParent("sprk_matter", id));
        (new RecordOwnershipParent("sprk_matter", id) with { EntityLogicalName = "SPRK_MATTER" }).EntityLogicalName
            .Should().Be("sprk_matter");
    }

    /// <summary>Captures the resolver's log lines (level and formatted message).</summary>
    private sealed class ResolverLog : ILogger<RecordOwnershipResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    // ---- Reparent OUT of a secure root: F3 (owner round 10 item 7, task 146 c1) ----
    //
    // "Moving a CHILD out of a secure root is an un-secure, so F3's limit applies": Full Access on the secure root the
    // child leaves, or being the child's own creator. Refused before any write otherwise.

    private static readonly Guid OtherSecureMatterId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SomeoneElse = Guid.Parse("7e7e7e7e-0000-4000-8000-000000000001");
    private static readonly Guid ApplicationUser = Guid.Parse("7e7e7e7e-0000-4000-8000-0000000000a1");

    /// <summary>Full Access: Collaborate plus Delete (RecordShareLevels).</summary>
    private const AccessRights FullAccess =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share
        | AccessRights.Delete;

    /// <summary>Collaborate: Write without Delete — the Write holder F3 does not admit.</summary>
    private const AccessRights Collaborate =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    /// <summary>
    /// The caller (<see cref="CallerUserId"/>, as WhoAmI answers) with <paramref name="rights"/> on every record, or the
    /// <paramref name="perRecord"/> rights where stated; each record whose rights were asked is added to
    /// <paramref name="asked"/>.
    /// </summary>
    private static Sprk.Bff.Api.Services.Access.SecureRemovalCaller CallerHolding(
        AccessRights rights,
        IReadOnlyDictionary<Guid, AccessRights>? perRecord = null,
        List<Guid>? asked = null) =>
        new(_ => Task.FromResult<Guid?>(CallerUserId),
            (record, _) =>
            {
                asked?.Add(record.RecordId);
                return Task.FromResult(perRecord is not null && perRecord.TryGetValue(record.RecordId, out var stated)
                    ? stated
                    : rights);
            });

    /// <summary>A document filed under the secure project, owned by the named team, created by <paramref name="extra"/>'s person.</summary>
    private static (FakeDirectory Directory, Guid DocumentId) SecureDocumentWorld(Dictionary<string, object>? extra = null)
    {
        var documentId = Guid.NewGuid();
        var columns = new Dictionary<string, object>
        {
            ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
            ["createdby"] = new EntityReference("systemuser", SomeoneElse),
        };
        foreach (var (column, value) in extra ?? new())
            columns[column] = value;

        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam, extra: columns);
        return (directory, documentId);
    }

    /// <summary>Out of the secure project, onto the ordinary matter.</summary>
    private static RecordReparent MoveToOrdinaryMatter(
        Guid documentId, Sprk.Bff.Api.Services.Access.SecureRemovalCaller? caller) => new()
    {
        EntityLogicalName = "sprk_document",
        RecordId = documentId,
        ParentChanges = new Dictionary<string, EntityReference?>
        {
            ["sprk_project"] = null,
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
        },
        SecureExitCaller = caller,
    };

    [Fact]
    public async Task Reparent_OutOfASecureRoot_ByAFullAccessHolderOnThatRoot_IsApplied_AndReassigned()
    {
        var (directory, documentId) = SecureDocumentWorld();
        var asked = new List<Guid>();
        var applied = false;

        var request = MoveToOrdinaryMatter(documentId, CallerHolding(FullAccess, asked: asked));
        var resolution = await Build(directory).ReparentAsync(
            request, Applies(directory, request, () => applied = true), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue();
        asked.Should().Equal(new[] { SecureProjectId }, "Full Access is asked on the secure root the document leaves");
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_ByTheDocumentsCreator_IsApplied_WithoutFullAccess()
    {
        var (directory, documentId) = SecureDocumentWorld(new() { ["createdby"] = new EntityReference("systemuser", CallerUserId) });
        var asked = new List<Guid>();

        var request = MoveToOrdinaryMatter(documentId, CallerHolding(Collaborate, asked: asked));
        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        asked.Should().BeEmpty("the creator is admitted without a rights probe");
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_ByThePersonRecordedAsCreatingAnAppCreatedRow_IsApplied()
    {
        // sprk_createdbyperson, else a human createdby: an app-created row names its person in sprk_createdbyperson.
        var (directory, documentId) = SecureDocumentWorld(new()
        {
            ["createdby"] = new EntityReference("systemuser", ApplicationUser),
            ["sprk_createdbyperson"] = new EntityReference("systemuser", CallerUserId),
        });

        var request = MoveToOrdinaryMatter(documentId, CallerHolding(Collaborate));
        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
    }

    /// <summary>
    /// #2273: a child of a table that carries <c>sprk_createdbyperson</c> but that the BFF does not stamp (ebilling task 080
    /// created the column UNSECURED on its procedure tables), filed under the secure project.
    /// </summary>
    private const string UnstampedChildTable = "sprk_procedure";

    private static (FakeDirectory Directory, Guid RowId) SecureUnstampedChildWorld(Dictionary<string, object> columns)
    {
        RecordCreatorPerson.IsStamped(UnstampedChildTable).Should().BeFalse("this case needs a table the BFF does not stamp");
        var rowId = Guid.NewGuid();
        var all = new Dictionary<string, object> { ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId) };
        foreach (var (column, value) in columns)
            all[column] = value;

        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord(UnstampedChildTable, rowId, SecureBu, owningTeam: SecureNamedTeam, extra: all);
        return (directory, rowId);
    }

    private static RecordReparent MoveUnstampedChildToOrdinaryMatter(Guid rowId) => MoveToOrdinaryMatter(rowId, CallerHolding(Collaborate))
        with { EntityLogicalName = UnstampedChildTable };

    [Fact]
    public async Task Reparent_OutOfASecureRoot_OnATableTheBffDoesNotStamp_AForgedCreatorPerson_IsNotAdmitted()
    {
        // #2273 must-fire: the caller, a Write holder, wrote themselves into the unsecured sprk_createdbyperson. It is not
        // evidence there, so they are neither the creator nor a Full Access holder: refused before anything is written.
        var (directory, rowId) = SecureUnstampedChildWorld(new()
        {
            ["createdby"] = new EntityReference("systemuser", SomeoneElse),
            ["sprk_createdbyperson"] = new EntityReference("systemuser", CallerUserId),
        });
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            MoveUnstampedChildToOrdinaryMatter(rowId), _ => { applied = true; return Task.CompletedTask; }, CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("a forged creator person on an unstamped table admits nobody");
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        resolution.SecureRemovalRefusal!.Basis.Should().Be(Sprk.Bff.Api.Services.Access.SecureRemovalBasis.NotFullAccessOrCreator);
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_OnATableTheBffDoesNotStamp_ItsCreatedByCreator_IsStillAdmitted()
    {
        // #2273 control: "not recorded" falls back to createdby, which Dataverse itself sets — that creator is admitted.
        var (directory, rowId) = SecureUnstampedChildWorld(new()
        {
            ["createdby"] = new EntityReference("systemuser", CallerUserId),
            ["sprk_createdbyperson"] = new EntityReference("systemuser", SomeoneElse),
        });
        var request = MoveUnstampedChildToOrdinaryMatter(rowId);

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        directory.Assignments.Should().Equal((UnstampedChildTable, rowId, ChildTeam));
    }

    [Fact]
    public async Task Reparent_OutOfIsolation_OnATableTheBffDoesNotStamp_WithNoNamedSecureRoot_AForgedCreatorPerson_IsNotAdmitted()
    {
        // #2273 must-fire, the "unidentified" branch: held in the Secure Record unit with no secure root among its own
        // parents, only the creator may take it out — and a forged creator person is not the creator.
        var rowId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord(UnstampedChildTable, rowId, SecureBu, owningTeam: SecureNamedTeam, extra: new Dictionary<string, object>
            {
                ["createdby"] = new EntityReference("systemuser", SomeoneElse),
                ["sprk_createdbyperson"] = new EntityReference("systemuser", CallerUserId),
            });
        var request = new RecordReparent
        {
            EntityLogicalName = UnstampedChildTable,
            RecordId = rowId,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) },
            SecureExitCaller = CallerHolding(FullAccess),
        };
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            request, _ => { applied = true; return Task.CompletedTask; }, CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("only the creator may take an unidentified secure row out, and the caller is not it");
        // Exactly the unidentified refusal: a forged value read as the creator would have admitted (no refusal at all), and a
        // value read as "column absent" would name a different basis.
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        resolution.SecureRemovalRefusal!.Basis.Should().Be(Sprk.Bff.Api.Services.Access.SecureRemovalBasis.SecureRecordUnidentified);
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_ByAWriteOnlyHolder_IsRefusedNotPermitted_BeforeAnyWrite()
    {
        var (directory, documentId) = SecureDocumentWorld();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            MoveToOrdinaryMatter(documentId, CallerHolding(Collaborate)),
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.IsForbidden.Should().BeTrue("an authorization answer, not an owner refusal");
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        resolution.SecureRemovalRefusal!.StatusCode.Should().Be(403);
        resolution.Reason.Should().Contain("Full Access").And.Contain("document");
        applied.Should().BeFalse("refused BEFORE the change is written");
        directory.Assignments.Should().BeEmpty();
        directory.FieldUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_ByAWriterActingForNoPerson_IsRefusedUnverifiable()
    {
        var (directory, documentId) = SecureDocumentWorld();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            MoveToOrdinaryMatter(documentId, caller: null),
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        resolution.IsForbidden.Should().BeTrue();
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_FromOneSecureRootToAnother_AsksF3OnTheRootItLeaves_NotTheOneItJoins()
    {
        // "or to a different root": Full Access on the NEW secure matter does not license leaving the secure project.
        var (directory, documentId) = SecureDocumentWorld();
        var asked = new List<Guid>();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_project"] = null,
                    ["sprk_matter"] = new EntityReference("sprk_matter", OtherSecureMatterId),
                },
                SecureExitCaller = CallerHolding(
                    Collaborate, new Dictionary<Guid, AccessRights> { [OtherSecureMatterId] = FullAccess }, asked),
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        asked.Should().Equal(new[] { SecureProjectId });
        applied.Should().BeFalse();
    }

    // #2159 slice 2: a written re-file carries the F3 permission it was given — the roots it left and the decision — so the
    // step after it can let the document's file follow it (S1 to S2). Generic: the resolver knows nothing of files.

    /// <summary>Out of the secure project, onto the other secure matter (S1 to S2), by a Full Access holder.</summary>
    private static RecordReparent MoveToOtherSecureMatter(Guid documentId) => new()
    {
        EntityLogicalName = "sprk_document",
        RecordId = documentId,
        ParentChanges = new Dictionary<string, EntityReference?>
        {
            ["sprk_project"] = null,
            ["sprk_matter"] = new EntityReference("sprk_matter", OtherSecureMatterId),
        },
        SecureExitCaller = CallerHolding(FullAccess),
    };

    [Fact]
    public async Task Reparent_FromOneSecureRootToAnother_WithF3_CarriesThePermit_TheRootLeftAndTheDecision()
    {
        // PR #2161 round 4 (F3): the change is WRITTEN (Applies), so the settle reads this call's own filing.
        var (directory, documentId) = SecureDocumentWorld();
        var request = MoveToOtherSecureMatter(documentId);

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.SecureExitPermitted.Should().NotBeNull();
        resolution.SecureExitPermitted!.RootsLeft.Should().Equal(new RecordOwnershipParent("sprk_project", SecureProjectId));
        resolution.SecureExitPermitted.RootsAfter.Should().Equal(
            new[] { new RecordOwnershipParent("sprk_matter", OtherSecureMatterId) }, "the filing the F3 decision approved");
        resolution.SecureExitPermitted.Decision.IsPermitted.Should().BeTrue();
        resolution.SecureExitPermitted.Decision.CallerSystemUserId.Should().Be(CallerUserId);
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_WithF3_CarriesThePermit()
    {
        var (directory, documentId) = SecureDocumentWorld();
        var request = MoveToOrdinaryMatter(documentId, CallerHolding(FullAccess));

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.SecureExitPermitted!.RootsLeft.Should().Equal(new RecordOwnershipParent("sprk_project", SecureProjectId));
        resolution.SecureExitPermitted.RootsAfter.Should().BeEmpty("the ordinary matter is no secure root");
    }

    [Fact]
    public async Task Reparent_WithF3_WhenAnotherWriterReFilesTheRowBeforeTheSettle_CarriesNoPermit()
    {
        // PR #2161 round 4 (F1-a): A re-files S1 to S2 with F3; before A's settle reads the row, B files it under a THIRD
        // secure matter S3 directly in Dataverse. S3 maps to the same Secure team, so the settle answers "already owned" —
        // from B's filing, which A's F3 check never saw. That answer must not carry A's permission.
        var (directory, documentId) = SecureDocumentWorld();
        var thirdSecureMatter = Guid.Parse("66666666-6666-6666-6666-666666666663");
        directory.WithRecord("sprk_matter", thirdSecureMatter, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
        var request = MoveToOtherSecureMatter(documentId);

        var resolution = await Build(directory).ReparentAsync(
            request,
            Applies(directory, request, alsoDone: () => directory.Write("sprk_document", documentId,
                new Dictionary<string, object?> { ["sprk_matter"] = new EntityReference("sprk_matter", thirdSecureMatter) })),
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.OwningTeamId.Should().Be(SecureNamedTeam, "the settle answered from the row's current filing");
        resolution.SecureExitPermitted.Should().BeNull("the answer came from another writer's filing, never F3-checked");
        directory.Assignments.Should().BeEmpty("already owned by the Secure team: nothing written");
    }

    /// <summary>
    /// PR #2161 round 5 (K1): a permitted move out of the secure project whose ordinary owner write lands and then reports a
    /// failure (a lost response). The owner reads back as the resolved team; <paramref name="filingReadFaults"/> makes the
    /// filing read-back that follows it fault.
    /// </summary>
    private static async Task<(RecordOwnerResolution Resolution, FakeDirectory Directory, ResolverLog Log, Guid DocumentId)>
        LostResponseMoveOutAsync(bool filingReadFaults)
    {
        var (directory, documentId) = SecureDocumentWorld();
        directory.AssignmentFault = new TimeoutException("response lost");
        directory.AssignmentLandsBeforeFault = true;
        if (filingReadFaults)
        {
            directory.BeforeWrite = (world, fields) =>
            {
                if (fields.ContainsKey("ownerid"))
                    world.QueryFault = new TimeoutException("throttled filing read");
            };
        }

        var request = MoveToOrdinaryMatter(documentId, CallerHolding(FullAccess));
        var log = new ResolverLog();

        var resolution = await directory.Resolver(logger: log).ReparentAsync(
            request, Applies(directory, request), CancellationToken.None);
        return (resolution, directory, log, documentId);
    }

    [Fact]
    public async Task Reparent_WithF3_WhenALostResponseAssignmentsFilingCannotBeReadBack_TheRefileStands_ButCarriesNoPermit()
    {
        // Must-fire (PR #2161 round 5, K1): the recovery keeps its earlier answer when the filing cannot be read back (#2150:
        // the re-file stands), but nobody confirmed that filing is this call's own — this call's F3 permission rides only on a
        // filing confirmed as its own (round 4's FilingIsOwn rule), so no permit: a file held in the root it left stays held.
        var (resolution, directory, log, documentId) = await LostResponseMoveOutAsync(filingReadFaults: true);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.OwningTeamId.Should().Be(ChildTeam, "#2150: an unreadable filing keeps the earlier answer, the re-file stands");
        resolution.SecureExitPermitted.Should().BeNull("the filing the answer stands on was never confirmed as this call's");
        directory.FieldUpdates.Should().BeEmpty("nothing is put back");
        OwnerOf(directory, documentId).Should().Be(ChildTeam);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("could not be confirmed as this re-file's own")
            && e.Message.Contains(documentId.ToString()));
    }

    [Fact]
    public async Task Reparent_WithF3_WhenALostResponseAssignmentsFilingReadsBackAsItsOwn_CarriesThePermit()
    {
        // Must-NOT-fire control (round 5, K1): the same lost response, and the filing read-back confirms the row is filed as
        // this call left it — the answer is this call's own filing, so the F3 permission rides on it.
        var (resolution, directory, log, _) = await LostResponseMoveOutAsync(filingReadFaults: false);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.OwningTeamId.Should().Be(ChildTeam);
        resolution.SecureExitPermitted.Should().NotBeNull();
        resolution.SecureExitPermitted!.RootsLeft.Should().Equal(new RecordOwnershipParent("sprk_project", SecureProjectId));
        directory.FieldUpdates.Should().BeEmpty("filing and owner agree — nothing to undo");
        log.Entries.Should().NotContain(e => e.Message.Contains("could not be confirmed as this re-file's own"));
    }

    [Fact]
    public async Task Reparent_RefusedByF3_CarriesNoPermit()
    {
        var (directory, documentId) = SecureDocumentWorld();

        var resolution = await Build(directory).ReparentAsync(
            MoveToOrdinaryMatter(documentId, CallerHolding(Collaborate)), _ => Task.CompletedTask, CancellationToken.None);

        resolution.IsForbidden.Should().BeTrue();
        resolution.SecureExitPermitted.Should().BeNull();
    }

    [Fact]
    public async Task Reparent_LeavingNoSecureRoot_CarriesNoPermit()
    {
        var (directory, documentId) = SecureDocumentWorld();

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) },
                SecureExitCaller = CallerHolding(FullAccess),
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.SecureExitPermitted.Should().BeNull("it left no secure root");
    }

    [Fact]
    public async Task Reparent_GainingAParentWhileStillUnderItsSecureRoot_AsksNothing()
    {
        // Not a move OUT: the document stays filed under the secure project, so no F3 question (and no caller) is needed.
        var (directory, documentId) = SecureDocumentWorld();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) },
                SecureExitCaller = null,
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue();
        resolution.OwningTeamId.Should().Be(SecureNamedTeam, "secure-if-any: still the named team's");
    }

    [Fact]
    public async Task Reparent_OfAChildOfASecureMessage_OutOfTheSecureRootAboveIt_AsksF3OnThatRoot()
    {
        // The document is filed under a communication, which is filed under the secure matter: leaving the communication
        // leaves the secure matter above it (the walk follows team-owned children too).
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_communication", communicationId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherSecureMatterId) })
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_communication"] = new EntityReference("sprk_communication", communicationId) });
        var asked = new List<Guid>();

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_communication"] = null,
                    ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                },
                SecureExitCaller = CallerHolding(Collaborate, asked: asked),
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        asked.Should().Equal(new[] { OtherSecureMatterId });
        directory.Assignments.Should().BeEmpty();
    }

    // ---- c1-r1: the gate's own fail-closed rules (verifier c1 items 1-3) ----

    /// <summary>
    /// A message held in the Secure Record business unit (owned by the named team) whose own columns name NO secure root:
    /// it was secured by the record thread it joined, and its regarding names an ordinary matter. Moving it to another
    /// ordinary matter takes it out of isolation, yet no secure root can be named whose Full Access could count.
    /// </summary>
    private static (FakeDirectory Directory, Guid MessageId, Guid OtherOrdinaryMatterId) UnidentifiedSecureMessageWorld(Guid createdBy)
    {
        var messageId = Guid.NewGuid();
        var otherOrdinaryMatterId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_matter", otherOrdinaryMatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_communication", messageId, SecureBu, owningTeam: SecureNamedTeam, extra: new()
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["createdby"] = new EntityReference("systemuser", createdBy),
            });
        return (directory, messageId, otherOrdinaryMatterId);
    }

    // ---- #2143 round 7 (F1) ----

    /// <summary>
    /// #2143 round 7 (F1): a message regarding the secure matter S (<see cref="SecureThreadMatterId"/>), sitting in S's own record
    /// thread T (<see cref="SecureThreadId"/>), owned by the Secure team: what a re-file of its regarding off S starts from.
    /// </summary>
    private static (FakeDirectory Directory, Guid MessageId) MessageInItsSecureRecordsThread() =>
        MessageWorld(SecureThreadMatterId, SecureBu, SecureNamedTeam, threadId: SecureThreadId);

    [Fact]
    public async Task Reparent_OfAMessageOffTheSecureRecordItsThreadAlsoHolds_ByACallerWithoutFullAccess_IsRefused_AndNothingIsWritten()
    {
        // Must-fire (#2143 round 7, F1): the re-file takes S out of the message's own regarding; its thread T still files it under
        // S, but the thread follows the filing after the write, so the move out of S is THIS re-file's. A caller without Full
        // Access on S is refused here — before round 7 the thread hid S and no F3 was asked.
        var (directory, messageId) = MessageInItsSecureRecordsThread();
        var asked = new List<Guid>();
        var request = MoveMessageTo(messageId, MatterId, CallerHolding(Collaborate, asked: asked));
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            request,
            _ =>
            {
                applied = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        asked.Should().Contain(SecureThreadMatterId, "leaving the regarding leaves S");
        applied.Should().BeFalse("refused before anything is written");
        directory.Assignments.Should().BeEmpty();
        directory.ConditionalWrites.Should().BeEmpty();
        directory.FieldUpdates.Should().BeEmpty();
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id
            .Should().Be(SecureThreadMatterId);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task Reparent_OfAMessageOffTheSecureRecordItsThreadAlsoHolds_ByAFullAccessHolder_AsksF3OnThatRecord_AndProceeds()
    {
        // Must-NOT-fire control (#2143 round 7, F1): the same re-file by a Full Access holder on S is permitted (F3 asked about
        // S) and written; the message stays Secure-owned until its thread follows the filing (the step after the re-file).
        var (directory, messageId) = MessageInItsSecureRecordsThread();
        var asked = new List<Guid>();
        var request = MoveMessageTo(messageId, MatterId, CallerHolding(FullAccess, asked: asked));

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse();
        asked.Should().Equal(new[] { SecureThreadMatterId }, "F3 is asked about S, the record the regarding leaves");
        resolution.IsSecureOwner.Should().BeTrue("its thread still files it under S until the thread follows the filing");
        directory.Row("sprk_communication", messageId).GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id
            .Should().Be(MatterId);
        NoOrdinaryOwnerEverSent(directory);
    }

    [Fact]
    public async Task Reparent_OfAMessageBetweenOrdinaryRecords_InAnOrdinaryThread_AsksNoF3()
    {
        // Must-NOT-fire (#2143 round 7, F1): a message regarding an ordinary matter, in that matter's thread, re-filed onto
        // another ordinary matter leaves no secure record: nothing is asked, and it takes the new matter's team.
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: OrdinaryThreadId);
        directory.WithRecord("sprk_matter", OtherOrdinaryMatterId, GeneralBu, owningTeam: GeneralTeam);
        var asked = new List<Guid>();
        var request = MoveMessageTo(messageId, OtherOrdinaryMatterId, CallerHolding(Collaborate, asked: asked));

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse();
        asked.Should().BeEmpty("no secure record is left");
    }

    // ---- #2143 round 7 (R7d): a thread's parents are decided by its type ----

    /// <summary>
    /// #2143 round 7 (R7d): a message regarding the ordinary matter (<see cref="MatterId"/>), in a thread of its own whose
    /// regarding names the secure matter S (<see cref="SecureThreadMatterId"/>). <paramref name="threadType"/> null leaves the
    /// type unset; <paramref name="isDefaultThread"/> marks the thread the way a master thread is marked.
    /// </summary>
    private static (FakeDirectory Directory, Guid MessageId) MessageInAThreadRegardingS(int? threadType, bool isDefaultThread = false)
    {
        var threadId = Guid.NewGuid();
        var (directory, messageId) = MessageWorld(MatterId, ChildBu, ChildTeam, threadId: threadId);
        var thread = new Dictionary<string, object>
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureThreadMatterId),
            ["sprk_isdefaultthread"] = isDefaultThread,
        };
        if (threadType is { } type)
            thread["sprk_threadtype"] = new OptionSetValue(type);
        directory
            .WithRecord("sprk_communicationthread", threadId, SecureBu, owningTeam: SecureNamedTeam, extra: thread)
            .WithRecord("sprk_matter", OtherOrdinaryMatterId, GeneralBu, owningTeam: GeneralTeam);
        return (directory, messageId);
    }

    [Theory]
    [InlineData(false)] // a Direct thread that carries a regarding
    [InlineData(true)] // a master thread: Direct, marked as the default thread
    public async Task Reparent_OfAMessageInADirectThreadRegardingASecureRecord_TheThreadContributesNothing(bool isDefaultThread)
    {
        // Must-NOT-fire (#2143 round 7, R7d): a Direct thread (100000001) is not filed under any record, whatever its regarding
        // holds, so it contributes no parent. The message re-filed between ordinary matters asks no F3 and takes the new
        // matter's team. Before R7d the thread's regarding made S a parent: F3 was asked and this caller refused.
        var (directory, messageId) = MessageInAThreadRegardingS(threadType: 100000001, isDefaultThread);
        var asked = new List<Guid>();
        var request = MoveMessageTo(messageId, OtherOrdinaryMatterId, CallerHolding(Collaborate, asked: asked));

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse("a Direct thread files the message under nothing");
        asked.Should().BeEmpty("no secure record is left");
        resolution.IsSecureOwner.Should().BeFalse();
        resolution.OwningTeamId.Should().Be(GeneralTeam);
        MessageOwnerOf(directory, messageId).Should().Be(GeneralTeam);
    }

    [Fact]
    public async Task Reparent_OfAMessageInAnUntypedThreadRegardingASecureRecord_CountsItAsARecordThread_AndTheMessageStaysSecure()
    {
        // Must-fire (#2143 round 7, R7d): a thread with no type is read as a Record thread, so its secure regarding still
        // files the message under S — fail closed by isolating. A Full Access holder on S is asked about S and permitted;
        // the message stays Secure-owned until its thread follows the filing.
        var (directory, messageId) = MessageInAThreadRegardingS(threadType: null);
        var asked = new List<Guid>();
        var request = MoveMessageTo(messageId, OtherOrdinaryMatterId, CallerHolding(FullAccess, asked: asked));

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse();
        asked.Should().Contain(SecureThreadMatterId, "an untyped thread's regarding is a parent");
        resolution.IsSecureOwner.Should().BeTrue("an untyped thread regarding S keeps the message Secure");
        NoOrdinaryOwnerEverSent(directory);
        MessageOwnerOf(directory, messageId).Should().Be(SecureNamedTeam);
    }

    private static RecordReparent MoveMessageTo(
        Guid messageId, Guid matterId, Sprk.Bff.Api.Services.Access.SecureRemovalCaller caller) => new()
    {
        EntityLogicalName = "sprk_communication",
        RecordId = messageId,
        ParentChanges = new Dictionary<string, EntityReference?>
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
        },
        SecureExitCaller = caller,
    };

    [Fact]
    public async Task Reparent_OfASecureBuRowWhoseSecureRootCannotBeNamed_ByAFullAccessNonCreator_IsRefusedUnverifiable()
    {
        // Item 1 (note §16b): "a row held in the Secure BU whose root cannot be named admits only its creator". Full Access
        // on everything does not help: there is no secure record to ask it about.
        var (directory, messageId, otherMatterId) = UnidentifiedSecureMessageWorld(createdBy: SomeoneElse);
        var asked = new List<Guid>();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            MoveMessageTo(messageId, otherMatterId, CallerHolding(FullAccess, asked: asked)),
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.IsForbidden.Should().BeTrue("leaving secure isolation is an un-secure (owner round 10 item 7)");
        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        resolution.SecureRemovalRefusal!.Basis.Should().Be(Sprk.Bff.Api.Services.Access.SecureRemovalBasis.SecureRecordUnidentified);
        asked.Should().BeEmpty("no secure root can be named, so nobody's Full Access is asked");
        applied.Should().BeFalse("refused BEFORE the change is written");
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OfASecureBuRowWhoseSecureRootCannotBeNamed_ByItsCreator_IsApplied()
    {
        var (directory, messageId, otherMatterId) = UnidentifiedSecureMessageWorld(createdBy: CallerUserId);
        var applied = false;

        var request = MoveMessageTo(messageId, otherMatterId, CallerHolding(Collaborate));
        var resolution = await Build(directory).ReparentAsync(
            request, Applies(directory, request, () => applied = true), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        applied.Should().BeTrue();
        directory.Assignments.Should().Equal(("sprk_communication", messageId, ChildTeam));
        resolution.SecureExitPermitted.Should().BeNull("#2159 slice 2: no secure root can be named, so no root was left to permit");
    }

    [Fact]
    public async Task Reparent_WhenTheFilingLeftIsDeeperThanTheLineageLimit_RefusesUnresolved_NeverSkipsF3()
    {
        // Item 2: the document stays under the secure project (so the answer keeps it secure), but it also leaves a
        // parent document whose filing runs past MaxLineageDepth to ANOTHER secure matter. Read as "no root there", the
        // move would leave that matter with no F3 question at all (fail-open). The gate refuses instead.
        var documentId = Guid.NewGuid();
        var chain = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_document", documentId, SecureBu, owningTeam: SecureNamedTeam, extra: new()
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
                ["sprk_parentdocument"] = new EntityReference("sprk_document", chain[0]),
                ["createdby"] = new EntityReference("systemuser", SomeoneElse),
            });
        for (var i = 0; i < chain.Length; i++)
        {
            var filing = i + 1 < chain.Length
                ? new EntityReference("sprk_document", chain[i + 1])
                : new EntityReference("sprk_matter", OtherSecureMatterId);
            directory.WithRecord("sprk_document", chain[i], SecureBu, owningTeam: SecureNamedTeam,
                extra: new() { ["sprk_parentdocument"] = filing });
        }

        var asked = new List<Guid>();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_parentdocument"] = null },
                SecureExitCaller = CallerHolding(Collaborate, asked: asked),
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("a filing left unread at the depth limit might hold a secure root");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.Reason.Should().Contain("levels").And.Contain("leaves a secure record");
        asked.Should().BeEmpty();
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OutOfASecureRoot_WhenTheSecureBusinessUnitIsAmbiguous_RefusesBeforeF3_EvenForAKeepCreatorRow()
    {
        // Item 3: the gate's own ambiguity refusal is REACHABLE. A communication cleared of its only parent keeps its
        // creator (E1), so the owner decision answers Unchanged without ever reading the business units; the gate is
        // then the first to find two units carrying the Secure Record name. Without the refusal it would ask F3 on the
        // flagged matter and a Full Access holder would move the message out of a unit nobody can identify.
        var messageId = Guid.NewGuid();
        var directory = Directory()
            .WithBusinessUnit(Guid.NewGuid(), SecureBuName) // a second BU with the configured name
            .WithRecord("sprk_matter", OtherSecureMatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord("sprk_communication", messageId, SecureBu, owningTeam: SecureNamedTeam, extra: new()
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherSecureMatterId),
            });
        var asked = new List<Guid>();
        var applied = false;

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = messageId,
                ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = null },
                WhenUnfiled = UnfiledOwnership.KeepCreator,
                SecureExitCaller = CallerHolding(FullAccess, asked: asked),
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.SecureBusinessUnitAmbiguous);
        asked.Should().BeEmpty("refused before F3 is asked");
        applied.Should().BeFalse();
    }

    // ---- c1-r1, owner round 13 item 9: children the BFF creates as the application record the person who asked ----

    [Fact]
    public async Task ResolveOwner_RecordsTheRequesterNamedBySystemUserId_WithoutReadingAnyUser()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_matter", MatterId) })
                with { RequestedBy = RecordRequester.Of(SomeoneElse) },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam, "who asked never changes the owner");
        resolution.CreatedByPerson.Should().Be(SomeoneElse);
        directory.QueriedEntities.Should().NotContain("systemuser", "a systemuserid needs no lookup");
    }

    [Fact]
    public async Task ResolveOwner_LooksAnObjectIdRequesterUpAsTheirOneSystemUser()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_matter", MatterId) })
                with { RequestedBy = RecordRequester.Of(objectId: CallerOid) },
            CancellationToken.None);

        resolution.CreatedByPerson.Should().Be(CallerUserId);
        directory.UserKeyColumns.Should().Equal("azureactivedirectoryobjectid");
    }

    [Fact]
    public async Task ResolveOwner_ADisabledObjectIdRequesterIsNobodysCreatorPerson()
    {
        // Issue #1576: a disabled systemuser keeps its oid; it must not be recorded as who asked for the row.
        var disabledOid = Guid.Parse("15760000-0000-0000-0000-0000000002d1");
        var directory = Directory()
            .WithDisabledUser(Guid.Parse("15760000-0000-0000-0000-0000000002d2"), disabledOid, GeneralBu)
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_matter", MatterId) })
                with { RequestedBy = RecordRequester.Of(objectId: disabledOid) },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam, "who asked never changes the owner");
        resolution.CreatedByPerson.Should().BeNull();
    }

    [Theory]
    [InlineData(false)] // no Dataverse user carries the object id
    [InlineData(true)]  // two do — ambiguous
    public async Task ResolveOwner_ARequesterWhoIsNotExactlyOneUser_RecordsNobody_AndRefusesNothing(bool ambiguous)
    {
        var unknownOid = Guid.NewGuid();
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam);
        if (ambiguous)
        {
            directory.WithUser(Guid.NewGuid(), unknownOid, GeneralBu).WithUser(Guid.NewGuid(), unknownOid, GeneralBu);
        }

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_matter", MatterId) })
                with { RequestedBy = RecordRequester.Of(objectId: unknownOid) },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam, "an unknown person is not a reason to refuse the row");
        resolution.CreatedByPerson.Should().BeNull("a row records nobody rather than a guess");
    }

    [Fact]
    public async Task ResolveOwner_ARefusedOwner_NeverReadsTheRequester()
    {
        var directory = Directory(); // the named matter does not exist

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_matter", MatterId) })
                with { RequestedBy = RecordRequester.Of(objectId: CallerOid) },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.CreatedByPerson.Should().BeNull();
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public void ApplyTo_StampsThePersonOnATableThatCarriesTheColumn_EvenWhenTheRowKeepsItsCreator_AndNowhereElse()
    {
        var owned = RecordOwnerResolution.Owned(ChildTeam) with { CreatedByPerson = CallerUserId };
        var kept = RecordOwnerResolution.Unchanged("E1") with { CreatedByPerson = CallerUserId };
        var document = new Entity("sprk_document");
        var unfiledMessage = new Entity("sprk_communication");
        // A child table the BFF never creates app-only, so it carries no column. (Task 147 r1 moved sprk_memo — this test's
        // example until then — onto the app-only G5 create, with the column: RecordCreatorPerson.StampedChildTables.)
        var agreement = new Entity("sprk_agreement");

        owned.ApplyTo(document);
        kept.ApplyTo(unfiledMessage);
        owned.ApplyTo(agreement);

        RecordCreatorPerson.StampedChildTables.Should().NotContain("sprk_agreement", "this case needs a table without the column");
        document.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(CallerUserId);
        unfiledMessage.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(CallerUserId,
            "who asked is a fact about the create, whoever owns the row");
        agreement.Attributes.Should().NotContainKey(RecordCreatorPerson.Column, "Dataverse refuses a create naming a missing column");
    }

    [Fact]
    public async Task Reparent_OfAChildWhoseTableLacksTheCreatorColumn_ANonCreatorIsUnverifiable_NeverNotPermitted()
    {
        // Main-session condition 2, now for children: a table without the column (its schema step has not run) is "could
        // not tell who created it", never "nobody did".
        var (directory, documentId) = SecureDocumentWorld();
        directory.WithoutColumn("sprk_document", RecordCreatorPerson.Column);

        var resolution = await Build(directory).ReparentAsync(
            MoveToOrdinaryMatter(documentId, CallerHolding(Collaborate)), _ => Task.CompletedTask, CancellationToken.None);

        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        resolution.SecureRemovalRefusal!.Basis.Should().Be(Sprk.Bff.Api.Services.Access.SecureRemovalBasis.CreatorColumnAbsent);
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reparent_OfAChildWithTheCreatorColumnButNobodyRecorded_ANonCreatorIsNotPermitted()
    {
        // Control for the test above: the column exists and is empty (a row created before the stamp) — a definite answer.
        var (directory, documentId) = SecureDocumentWorld();

        var resolution = await Build(directory).ReparentAsync(
            MoveToOrdinaryMatter(documentId, CallerHolding(Collaborate)), _ => Task.CompletedTask, CancellationToken.None);

        resolution.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
    }

    // ---- Lineage depth limit (task 146 b2, verifier LOW) ----

    [Fact]
    public async Task ResolveOwner_WhenTheUnownedFilingChainIsDeeperThanTheLookThroughLimit_AndEndsOnASecureMatter_AnswersSecure()
    {
        // Six user-owned documents, each filed to the next; the last is filed to a SECURE matter beyond the look-through
        // limit. Fix round R (A3): at the bound the look-through stops and the lineage walk (its own bound) reads on to the
        // secure matter — secure-if-any answers the Secure team. Before A3 this refused, so the chain never moved in.
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var directory = Directory().WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
        DocumentChain(directory, ids);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", ids[0]) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task ResolveOwner_WhenTheUnownedFilingChainIsDeeperThanTheLookThroughLimit_AndEndsOnAnOrdinaryMatter_Refuses_NeverAnswersOrdinary()
    {
        // The same chain on an ORDINARY matter: the answer would be ordinary, which needs the primary's business unit the
        // look-through did not reach — it refuses, never the general team.
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam);
        DocumentChain(directory, ids);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", ids[0]) }),
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("the primary's business unit is past the look-through limit");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.Reason.Should().Contain("levels");
    }

    [Fact]
    public async Task ResolveOwner_WhenTheUnownedFilingChainIsDeeperThanBothLimits_Refuses_NeverAnswersOrdinary()
    {
        // Twelve user-owned documents ending on a SECURE matter past the lineage walk's bound as well: the secure ancestor
        // is never read, so the chain refuses rather than answer ordinary.
        var ids = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        var directory = Directory().WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
        DocumentChain(directory, ids);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", ids[0]) }),
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("an unread filing above both limits might be secure");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.Reason.Should().Contain("levels");
    }

    private static void DocumentChain(FakeDirectory directory, Guid[] ids)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            var filing = i + 1 < ids.Length
                ? new EntityReference("sprk_document", ids[i + 1])
                : new EntityReference("sprk_matter", MatterId);
            directory.WithRecord("sprk_document", ids[i], GeneralBu, owningTeam: null,
                extra: new() { ["sprk_parentdocument"] = filing });
        }
    }

    [Fact]
    public async Task ResolveOwner_WhenTheUnownedFilingChainEndsWithinTheLineageLimit_Resolves()
    {
        // Control for the test above: the same shape within the limit reaches the secure matter.
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var directory = Directory().WithRecord("sprk_matter", MatterId, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);
        for (var i = 0; i < ids.Length; i++)
        {
            var filing = i + 1 < ids.Length
                ? new EntityReference("sprk_document", ids[i + 1])
                : new EntityReference("sprk_matter", MatterId);
            directory.WithRecord("sprk_document", ids[i], GeneralBu, owningTeam: null,
                extra: new() { ["sprk_parentdocument"] = filing });
        }

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", ids[0]) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    // ── UAC task 194b: one deterministic primary; a user-owned FILED primary child is looked through ──────────────

    [Theory]
    [InlineData(true)]  // the project lookup comes first in the row's attributes: before 194b it was the primary
    [InlineData(false)]
    public async Task Reparent_TakesTheTablesPrimary_WhateverTheAttributeOrder(bool projectFirst)
    {
        // An event filed under an ordinary project (GeneralBu) and re-filed to a matter in ChildBu. An event's primary order
        // (SecureChildLineage) names sprk_regardingmatter before sprk_regardingproject, so the matter decides, both ways round.
        var eventId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var project = new KeyValuePair<string, object>("sprk_regardingproject", new EntityReference("sprk_project", projectId));
        var matter = new KeyValuePair<string, object>("sprk_regardingmatter", new EntityReference("sprk_matter", OtherOrdinaryMatterId));
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_matter", OtherOrdinaryMatterId, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_project", projectId, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_event", eventId, GeneralBu, owningTeam: GeneralTeam,
                extra: new Dictionary<string, object>(projectFirst ? new[] { project, matter } : new[] { matter, project }));
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_event",
            RecordId = eventId,
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            },
        };

        var resolution = await Build(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam, "the event's primary is its matter, whatever order its lookups come back in");
    }

    [Fact]
    public async Task ResolveOwner_APrimaryChildChainThatLoops_Refuses_NeverOrdinary()
    {
        // Two user-owned documents filed under each other, filed under nothing else: the look-through of the primary is bounded
        // (MaxLineageDepth) and REFUSES past it. Before 194b the answer was the first document's creator's unit.
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var directory = Directory()
            .WithRecord("sprk_document", a, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_parentdocument"] = new EntityReference("sprk_document", b) })
            .WithRecord("sprk_document", b, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_parentdocument"] = new EntityReference("sprk_document", a) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", a) }), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("a look-through past its bound never answers ordinary");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Theory(DisplayName = "194b R (A3) must fire: a primary that loops (event ↔ communication, document ↔ document) on a SECURE matter stops looking through at the revisit and answers the Secure team — never a refusal")]
    [InlineData("sprk_event", "sprk_regardingcommunication", "sprk_communication", "sprk_regardingevent", "sprk_regardingmatter", "sprk_regardingmatter")]
    [InlineData("sprk_document", "sprk_parentdocument", "sprk_document", "sprk_parentdocument", "sprk_matter", "sprk_matter")]
    public async Task ResolveOwner_APrimaryLoopOnASecureMatter_AnswersTheSecureTeam(
        string tableA, string aToB, string tableB, string bToA, string aMatter, string bMatter)
    {
        var (a, b, secureMatter) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var directory = Directory()
            .WithRecord("sprk_matter", secureMatter, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
            .WithRecord(tableA, a, GeneralBu, owningTeam: null, extra: new()
            {
                [aToB] = new EntityReference(tableB, b), [aMatter] = new EntityReference("sprk_matter", secureMatter),
            })
            .WithRecord(tableB, b, GeneralBu, owningTeam: null, extra: new()
            {
                [bToA] = new EntityReference(tableA, a), [bMatter] = new EntityReference("sprk_matter", secureMatter),
            });

        foreach (var primary in new[] { new RecordOwnershipParent(tableA, a), new RecordOwnershipParent(tableB, b) })
        {
            var resolution = await Build(directory).ResolveOwnerAsync(RecordOwnershipContext.ForParents(new[] { primary }), CancellationToken.None);

            resolution.OwningTeamId.Should().Be(SecureNamedTeam, "secure-if-any over what the loop is filed under: the secure matter");
        }
    }

    [Fact(DisplayName = "194b R (A3) must not fire: the same event ↔ communication loop on an ORDINARY matter refuses — its ordinary unit needs the primary the loop never settles")]
    public async Task ResolveOwner_APrimaryLoopOnAnOrdinaryMatter_Refuses()
    {
        var (e, c) = (Guid.NewGuid(), Guid.NewGuid());
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_event", e, GeneralBu, owningTeam: null, extra: new()
            {
                ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", c),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            })
            .WithRecord("sprk_communication", c, GeneralBu, owningTeam: null, extra: new()
            {
                ["sprk_regardingevent"] = new EntityReference("sprk_event", e),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_event", e) }), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("an ordinary answer would need the looping primary's business unit");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.Reason.Should().Contain("loops back");
    }

    [Fact]
    public async Task ResolveOwner_AnUnfiledUserOwnedPrimaryChild_KeepsItsOwnUnit()
    {
        // Must-not-fire: a user-owned document filed under nothing keeps today's answer (its own business unit).
        var documentId = Guid.NewGuid();
        var directory = Directory().WithRecord("sprk_document", documentId, ChildBu, owningTeam: null);

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
    }

    [Fact]
    public async Task ResolveOwner_AUserOwnedCoreRecordPrimary_IsNeverLookedThrough()
    {
        // Must-not-fire (ontology review, 194b addition 1): a user-owned work assignment is a ROOT; it keeps its own unit even
        // when it names a matter in another unit. 22 of 27 work assignments on dev are user-owned, and 21 Signals have one as core.
        var workAssignmentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, owningTeam: ChildTeam)
            .WithRecord("sprk_workassignment", workAssignmentId, GeneralBu, isSecure: false, owningTeam: null,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[]
            {
                new RecordOwnershipParent("sprk_workassignment", workAssignmentId), new RecordOwnershipParent("sprk_matter", MatterId),
            }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(GeneralTeam, "a core record is a root: its own business unit decides");
        directory.RecordQueries.Should().NotContain(q => q.EntityName == "sprk_workassignment" && q.ColumnSet.AllColumns,
            "a root's filing is never read for its primary");
    }

    private static RecordOwnershipResolver Build(FakeDirectory directory) => directory.Resolver();

    /// <summary>The standard world (<see cref="FakeDirectory.Standard"/>) — shared with SecureChildOwnershipTests.</summary>
    private static FakeDirectory Directory(bool withDefaultTeams = true, bool withNamedSecureTeam = true) =>
        FakeDirectory.Standard(withDefaultTeams, withNamedSecureTeam);
}
