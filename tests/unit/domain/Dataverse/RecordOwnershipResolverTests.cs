using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
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

        RecordOwnershipContext.ParentsOf(fields).Should().BeEquivalentTo(new[]
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
    public async Task ResolveOwner_ForAChildOfAUserOwnedDocumentFiledToAnOrdinaryMatter_KeepsThePrimaryParentsUnit()
    {
        // Nothing changes for a child of ordinary records: the ancestors only take part in the SECURE decision, and the
        // ordinary answer is still the primary parent's own unit.
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu, isSecure: false, owningTeam: ChildTeam)
            .WithRecord("sprk_document", documentId, GeneralBu, owningTeam: null,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });

        var resolution = await Build(directory).ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", documentId) }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(GeneralTeam);
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

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = RecordReparent.ParentChangesWithClearsIn(
                    new Dictionary<string, object?> { ["sprk_project"] = null, ["sprk_description"] = null }),
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().Equal(("sprk_document", documentId, ChildTeam));
        directory.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(ChildTeam, "read back");
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
                ParentChanges = RecordReparent.ParentChangesWithClearsIn(
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

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_relatedproject"] = new EntityReference("sprk_project", SecureProjectId),
                },
            },
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

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

        var resolution = await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_project"] = null, // cleared
                    ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                },
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ChildTeam);
        directory.Assignments.Should().ContainSingle().Which.Item3.Should().Be(ChildTeam);
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

        var act = () => Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_project"] = new EntityReference("sprk_project", SecureProjectId),
                },
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*did not read back*");
    }

    [Fact]
    public async Task Reparent_WhenTheRowIsAlreadyOwnedByTheResolvedTeam_AssignsNothing()
    {
        var documentId = Guid.NewGuid();
        var directory = Directory()
            .WithRecord("sprk_matter", MatterId, ChildBu)
            .WithRecord("sprk_document", documentId, ChildBu, owningTeam: ChildTeam);

        await Build(directory).ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = documentId,
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                },
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        directory.Assignments.Should().BeEmpty();
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
            MoveUnder(documentId, "sprk_project", null), Writes(directory, documentId, "sprk_project", null),
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
        // A message joining a SECURE record's thread: the thread lookup is the change (AttachColumns), the record is
        // inherited. A failed assignment takes the message back out of the thread.
        var communicationId = Guid.NewGuid();
        var previousThread = new EntityReference("sprk_communicationthread", Guid.NewGuid());
        var directory = Directory()
            .WithRecord("sprk_project", SecureProjectId, SecureBu)
            .WithRecord("sprk_communication", communicationId, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_communicationthread"] = previousThread });
        directory.AssignmentFault = new InvalidOperationException("missing prvReadsprk_Communication");
        var secureThread = new EntityReference("sprk_communicationthread", Guid.NewGuid());

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
                directory.Row("sprk_communication", communicationId)["sprk_communicationthread"] = secureThread;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        directory.Row("sprk_communication", communicationId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Should().Be(previousThread, "the message is not left in the secure record's thread while owned elsewhere");
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

    // ---- Lineage depth limit (task 146 b2, verifier LOW) ----

    [Fact]
    public async Task ResolveOwner_WhenTheUnownedFilingChainIsDeeperThanTheLineageLimit_Refuses_NeverAnswersOrdinary()
    {
        // Six user-owned documents, each filed to the next; the last is filed to a SECURE matter beyond the read limit.
        // Read as ordinary, the first would be handed the general team — the chain refuses instead.
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
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

        resolution.IsRefused.Should().BeTrue("an unread filing above the limit might be secure");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.Reason.Should().Contain("levels");
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

    private static RecordOwnershipResolver Build(FakeDirectory directory) => directory.Resolver();

    /// <summary>The standard world (<see cref="FakeDirectory.Standard"/>) — shared with SecureChildOwnershipTests.</summary>
    private static FakeDirectory Directory(bool withDefaultTeams = true, bool withNamedSecureTeam = true) =>
        FakeDirectory.Standard(withDefaultTeams, withNamedSecureTeam);
}
