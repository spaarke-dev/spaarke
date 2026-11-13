using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b: the generic update paths (the playbook output mapping's
/// <see cref="DataverseUpdateHandler"/> and the Update Record node's <see cref="UpdateRecordActionCore"/>) take the one unfile rule
/// (<see cref="DirectMessageOwnerStep.UnfiledOwnershipFor"/>) when they act for a person: an eligible Direct message returns to the
/// Direct team after the change, its participants granted first; one whose Direct team cannot be confirmed is refused before
/// anything is written; any other communication left linked to nothing goes to its recorded creator's business-unit team
/// (uac-r2 197a, #2722) or keeps its owner — never the acting user's team. A write that acts for nobody keeps its refusal.
/// </summary>
/// <remarks>The REAL <see cref="RecordOwnershipResolver"/> over <see cref="FakeDirectory"/>, the REAL
/// <see cref="DirectMessageOwnerStep"/> over the same directory, and the REAL <see cref="DirectMessageOwnerTeam"/> over
/// <see cref="DirectMessageOwnerTeamDouble"/>.</remarks>
public class DirectUnfileGenericPathTests
{
    private static readonly Guid DirectUnit = DirectMessageOwnerTeamDouble.DirectUnitId;
    private static readonly Guid DirectTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c1");
    private static readonly Guid DirectUnitDefaultTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c2");
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid MatterId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c5");
    private static readonly Guid Person = FakeDirectory.CallerUserId;

    private readonly List<string> _calls = new();

    // ── DataverseUpdateHandler ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHandler_UnfilingACommunicationThatIsNotADirectMessage_WithNoRecordedCreator_WritesNoOwner()
    {
        // UAC-r2: the generic path never hands an unfiled communication to the acting user's business-unit team; with no
        // recorded creator (sprk_createdbyperson) it keeps its owner (197a, #2722).
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email);
        var (handler, writes) = UpdateHandler(world, Resolved());

        await handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);

        writes.Should().ContainSingle("the unfile itself is written");
        world.Assignments.Should().BeEmpty("no recorded creator: the former owner is kept (#2722)");
        world.ConditionalWrites.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam).And.NotBe(FakeDirectory.GeneralTeam);
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateHandler_UnfilingACommunicationThatIsNotADirectMessage_WithARecordedCreator_MovesItToTheCreatorsTeam()
    {
        // uac-r2 197a (#2722), end to end through a writer: the creator's business-unit team, written by the settle.
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        var (handler, writes) = UpdateHandler(world, Resolved());

        await handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);

        writes.Should().ContainSingle("the unfile itself is written");
        OwningTeam(world).Should().Be(FakeDirectory.CustomerChildTeam).And.NotBe(FakeDirectory.GeneralTeam);
        _calls.Should().BeEmpty("not a Direct message: no participant grant");
    }

    [Fact]
    public async Task UpdateHandler_UnfilingADirectMessage_AsAPerson_GrantsItsParticipants_ThenReturnsItToTheDirectTeam()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, Resolved());

        await handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);

        writes.Should().ContainSingle();
        OwningTeam(world).Should().Be(DirectTeam);
        world.Assignments.Should().Equal(("sprk_communication", MessageId, DirectTeam));
        _calls.Should().Equal("grant DirectParticipants", "owner sprk_communication", "grant DirectParticipants");
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task UpdateHandler_UnfilingADirectMessage_WhoseDirectTeamIsNotUsable_IsRefused_AndWritesNothing(string onlySet)
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, DirectMessageOwnerStepTests.PartiallyConfigured(onlySet));

        var act = () => handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        writes.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateHandler_UnfilingADirectMessage_WhenTheLookupFaults_IsRefusedAsUnavailable_AndWritesNothing()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;
        var (handler, writes) = UpdateHandler(world, lookup.Lookup());

        var act = () => handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnavailable);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateHandler_UnfilingACommunication_ForNobody_KeepsItsRefusal()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, Resolved());

        var act = () => handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        writes.Should().BeEmpty();
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateHandler_WithNoStepComposed_KeepsTheCreator_AndNeverAsksTheDirectRule()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, DirectMessageOwnerStepTests.PartiallyConfigured(
            DirectMessageOwnerTeam.BusinessUnitNameConfigKey), withStep: false);

        await handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);

        writes.Should().ContainSingle();
        world.Assignments.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateHandler_MovingADirectMessageFromAMatterToAContact_WritesIt_RefusesNothing_GrantsNothing_MovesNoOwner(
        bool lookupResolves)
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, lookupResolves
            ? Resolved()
            : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey));

        await handler.UpdateAsync("sprk_communication", MessageId, ToAContact(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Person);

        writes.Should().ContainSingle("a message re-filed to a contact is still linked to a record: no unfile");
        world.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("sprk_regardingperson")?.Id.Should().Be(ContactId);
        world.Assignments.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
        _calls.Should().BeEmpty("no participant is granted for a message that does not move");
    }

    [Fact]
    public async Task UpdateRecordNode_MovingADirectMessageFromAMatterToAContact_IsNotRefused_AndMovesNoOwner()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);

        await UpdateRecordCore(world, DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey))
            .UpdateAsync(new UpdateRecordActionInput(
                "sprk_communication", MessageId, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_regardingmatter"] = null },
                Lookups: [new RenderedLookup("sprk_RegardingPerson", "contact", ContactId.ToString("D"))],
                ImpersonateSystemUserId: Person), CancellationToken.None);

        world.Assignments.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
        _calls.Should().BeEmpty();
    }

    // ── Pass 2 (F1): a re-file to a contact, then the contact cleared ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHandler_MovedToAContact_ThenTheContactCleared_ReturnsTheMessageAndItsContentRowsToTheDirectTeam()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam)
            .WithRecord(AttachmentTable, AttachmentId, FakeDirectory.ChildBu, owningTeam: FakeDirectory.ChildTeam, extra: AttachmentOf());
        var threads = ThreadReconciles();
        var (handler, writes) = UpdateHandler(world, Resolved(), threads: threads.Object);

        await handler.UpdateAsync("sprk_communication", MessageId, ToAContact(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam, "a message linked to a contact is not unfiled");
        _calls.Clear();

        await handler.UpdateAsync("sprk_communication", MessageId, ClearTheContact(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Person);

        writes.Should().HaveCount(2);
        OwningTeam(world).Should().Be(DirectTeam);
        world.Row(AttachmentTable, AttachmentId).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(DirectTeam, "an ordinary unfile moves the content rows with the message");
        _calls.Should().Equal(
            "grant DirectParticipants", "owner sprk_communication", "owner " + AttachmentTable, "grant DirectParticipants",
            "reconcile after the re-file");
    }

    // ── Pass 3 (F1): the move off the Secure owner is asked F3 ───────────────────────────────────────────────────────────

    /// <summary>
    /// One person with Full Access on the secure record moves the message to a contact (the owner stays the Secure team); then a
    /// second person clears the contact. The unfile takes the message off the Secure owner with no secure root that can be named,
    /// so F3 admits only its creator: the second person is refused and nothing is written. The first is refused the same way, as on
    /// the non-Direct path for a row held in the Secure Record business unit that leaves it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateHandler_MovedFromASecureRecordToAContact_ThenTheContactClearedByAPersonWhoDidNotCreateIt_IsForbidden_AndWritesNothing(
        bool clearedByTheFirstPerson)
    {
        var world = World().WithSecureRoot("sprk_matter", SecureMatterId)
            .WithMessageFiledUnder(SecureMatterId, FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam)
            .CreatedBy(Creator);
        var (handler, writes) = UpdateHandler(world, Resolved(), threads: ThreadReconciles().Object, shares: FullRightsFor(Person));

        await handler.UpdateAsync("sprk_communication", MessageId, ToAContact(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);
        OwningTeam(world).Should().Be(FakeDirectory.SecureNamedTeam);
        _calls.Clear();
        writes.Clear();

        var act = () => handler.UpdateAsync("sprk_communication", MessageId, ClearTheContact(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: clearedByTheFirstPerson ? Person : SecondPerson);

        var refused = (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.IsReasonCode(refused.RefusalCode).Should().BeTrue("the F3 refusal");
        writes.Should().BeEmpty("nothing is written");
        world.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("sprk_regardingperson")?.Id.Should().Be(ContactId);
        OwningTeam(world).Should().Be(FakeDirectory.SecureNamedTeam);
        world.Assignments.Should().BeEmpty();
        _calls.Should().BeEmpty("no grant, no move, no reconcile");
    }

    [Fact]
    public async Task UpdateHandler_UnfilingFromASecureRecord_ByAPersonWithoutF3_IsForbidden_BeforeTheDirectTeamIsAsked()
    {
        var world = World().WithSecureRoot("sprk_matter", SecureMatterId)
            .WithMessageFiledUnder(SecureMatterId, FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam)
            .CreatedBy(Creator);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        var (handler, writes) = UpdateHandler(world, lookup.Lookup(), shares: FullRightsFor(Person));

        var act = () => handler.UpdateAsync("sprk_communication", MessageId, Unfile(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: SecondPerson);

        var refused = (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.IsReasonCode(refused.RefusalCode).Should().BeTrue();
        lookup.Reads.Should().Be(0, "the Direct team is not asked for a change the caller may not make");
        writes.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.SecureNamedTeam);
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateHandler_MovedFromASecureRecordToAContact_ThenTheContactCleared_MovesTheMessage_AndTheSecureChildReconcileTakesItsMirrorOff()
    {
        var world = World().WithSecureRoot("sprk_matter", SecureMatterId)
            .WithMessageFiledUnder(SecureMatterId, FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam)
            .CreatedBy(Creator)
            .WithRecord(AttachmentTable, AttachmentId, FakeDirectory.SecureBu, owningTeam: FakeDirectory.SecureNamedTeam, extra: AttachmentOf());
        var threads = ThreadReconciles();
        // The reconcile reads the message from its own store, which the Direct step's owner write reaches too.
        var shareWorld = SecureChildShareWorld.Standard();
        var shareTable = new FakeRecordShareTable();
        var children = SecureChildShareWorld.ReconcilerOver(() => shareWorld, shareTable, null!, threads: threads.Object);
        var (handler, _) = UpdateHandler(world, Resolved(), shares: FullRightsFor(Person), children: children,
            afterOwnerWrite: (table, id, team) => shareWorld.Set(table, id, "owningteam", new EntityReference("team", team)));

        await handler.UpdateAsync("sprk_communication", MessageId, ToAContact(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None,
            impersonateSystemUserId: Person);
        OwningTeam(world).Should().Be(FakeDirectory.SecureNamedTeam);
        _calls.Clear();

        // As the first write left it: owned by the Secure team, linked to the contact, carrying its secure record's sharee.
        shareWorld.SecureChild("sprk_communication", MessageId, ("sprk_regardingperson", "contact", ContactId));
        shareTable.Seed("sprk_communication", MessageId, DataversePrincipalRef.User(Sharee), CollaborateMask);

        // The contact is cleared by the message's creator, whom F3 admits.
        await handler.UpdateAsync("sprk_communication", MessageId, ClearTheContact(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Creator);

        OwningTeam(world).Should().Be(DirectTeam);
        world.Row(AttachmentTable, AttachmentId).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(FakeDirectory.SecureNamedTeam, "from the Secure team the content rows are the secure-child reconcile's");
        _calls.Should().Equal(
            "grant DirectParticipants", "owner sprk_communication", "grant DirectParticipants", "reconcile after the re-file");
        shareTable.MaskOf("sprk_communication", MessageId, DataversePrincipalRef.User(Sharee))
            .Should().BeNull("the message left isolation, so the reconcile takes its mirror off");
    }

    private static readonly Guid Sharee = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003cc");
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task UpdateHandler_ClearingTheLastContact_OfADirectMessage_WithAPartialConfig_IsRefused409_AndWritesNothing(string onlySet)
    {
        var world = World().WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, DirectMessageOwnerStepTests.PartiallyConfigured(onlySet));

        var act = () => handler.UpdateAsync("sprk_communication", MessageId, ClearTheContact(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        writes.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CommunicationType.Email, true)]
    [InlineData(CommunicationType.Message, false)]
    public async Task UpdateHandler_ClearingAContact_OfAMessageThatIsNotAnEligibleDirectMessage_WritesNoOwner_AndAsksNoLookup(
        CommunicationType type, bool withRecipient)
    {
        var world = World().WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type, withRecipient);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;
        var (handler, writes) = UpdateHandler(world, lookup.Lookup());

        await handler.UpdateAsync("sprk_communication", MessageId, ClearTheContact(), ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: Person);

        writes.Should().ContainSingle();
        lookup.Reads.Should().Be(0);
        world.Assignments.Should().BeEmpty();
        world.ConditionalWrites.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
        _calls.Should().BeEmpty();
    }

    // ── Pass 3 (F1): only a clear of a link the row holds is an unfile ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHandler_ClearingALinkTheRowDoesNotHold_OfAnUnfiledDirectMessage_MovesNothing_GrantsNothing_WritesNoOwner()
    {
        var world = World().WithUnfiledDirectMessage(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        var (handler, writes) = UpdateHandler(world, lookup.Lookup());

        await handler.UpdateAsync("sprk_communication", MessageId, new Dictionary<string, object?> { ["sprk_regardingaccount"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, impersonateSystemUserId: Person);

        writes.Should().ContainSingle();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
        world.Assignments.Should().BeEmpty();
        world.ConditionalWrites.Should().BeEmpty();
        lookup.Reads.Should().Be(0);
        _calls.Should().BeEmpty("no participant is granted and no owner is written");
    }

    // ── Pass 2 (K2): the writer passes every column the rule reads ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHandler_SettingTheTypeToMessage_AndClearingTheMatter_ReturnsTheMessageToTheDirectTeam()
    {
        // The pair columns are BFF-owned (054b: a generic writer refuses them), so the type is the rule's column a writer can write.
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email);
        var (handler, _) = UpdateHandler(world, Resolved());

        await handler.UpdateAsync("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["sprk_regardingmatter"] = null,
            ["sprk_communicationtype"] = (int)CommunicationType.Message,
        },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, impersonateSystemUserId: Person);

        OwningTeam(world).Should().Be(DirectTeam);
        _calls.Should().Equal("grant DirectParticipants", "owner sprk_communication", "grant DirectParticipants");
    }

    [Fact]
    public async Task UpdateHandler_SettingTheTypeToEmail_AndClearingTheMatter_WithAPartialConfig_IsNotRefused_AndMovesNoOwner()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var (handler, writes) = UpdateHandler(world, DirectMessageOwnerStepTests.PartiallyConfigured(
            DirectMessageOwnerTeam.OwnerTeamNameConfigKey));

        await handler.UpdateAsync("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["sprk_regardingmatter"] = null,
            ["sprk_communicationtype"] = (int)CommunicationType.Email,
        },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, impersonateSystemUserId: Person);

        writes.Should().ContainSingle();
        world.Assignments.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
        _calls.Should().BeEmpty();
    }

    // ── Pass 2 (F3a): the field-mapping push ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Push_ReFilingADirectMessageToAnotherRecord_FollowsTheRecord_IsNeverRefusedForTheDirectTeam_AndGrantsNothing(
        bool lookupResolves)
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam)
            .WithRecord("sprk_matter", OtherMatterId, FakeDirectory.CustomerChildBu, isSecure: false, owningTeam: FakeDirectory.CustomerChildTeam);
        var lookup = lookupResolves
            ? Resolved()
            : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey);

        var (updated, failed, _, errors, _) = await PushAsync(world, lookup, $"/sprk_matters({OtherMatterId:D})");

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        OwningTeam(world).Should().Be(FakeDirectory.CustomerChildTeam);
        _calls.Should().BeEmpty("a message re-filed to a record is not unfiled: no participant is granted");
    }

    // ── The payload reader every unfile writer passes (RecordReparent.RegardingLinksSetIn) ───────────────────────────────

    [Fact]
    public void RegardingLinksSetIn_ReadsTheTypedRegardingsThatFileNothing_InEverySpelling()
    {
        var account = Guid.NewGuid();
        var organization = Guid.NewGuid();

        var links = RecordReparent.RegardingLinksSetIn("sprk_communication", new Dictionary<string, object?>
        {
            ["sprk_RegardingPerson@odata.bind"] = $"/contacts({ContactId:D})",
            ["sprk_regardingaccount"] = new EntityReference("account", account),
            ["sprk_RegardingOrganization@odata.bind"] = System.Text.Json.JsonSerializer.SerializeToElement($"sprk_organizations({organization:D})"),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_regardingproject"] = null,
            ["sprk_name"] = "x",
        });

        links.Keys.Should().BeEquivalentTo("sprk_regardingperson", "sprk_regardingaccount", "sprk_regardingorganization");
        links["sprk_regardingperson"].Should().Be(new EntityReference("contact", ContactId));
        links["sprk_regardingaccount"].Id.Should().Be(account);
        links["sprk_regardingorganization"].Id.Should().Be(organization);
    }

    [Fact]
    public void RegardingLinksSetIn_ReadsNothingForAnotherTable_OrABindThatNamesNoRow()
    {
        RecordReparent.RegardingLinksSetIn("sprk_event", new Dictionary<string, object?>
        {
            ["sprk_regardingperson"] = new EntityReference("contact", ContactId),
        }).Should().BeEmpty();
        RecordReparent.RegardingLinksSetIn("sprk_communication", new Dictionary<string, object?>
        {
            ["sprk_RegardingPerson@odata.bind"] = $"/accounts({ContactId:D})",
            ["sprk_regardingaccount"] = new EntityReference("account", Guid.Empty),
        }).Should().BeEmpty();
    }

    // ── UpdateRecordActionCore ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecordNode_UnfilingADirectMessage_AsAPerson_ReturnsItToTheDirectTeam()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);

        await UpdateRecordCore(world, Resolved()).UpdateAsync(UnfileInput(Person), CancellationToken.None);

        OwningTeam(world).Should().Be(DirectTeam);
        _calls.First().Should().Be("grant DirectParticipants");
    }

    [Fact]
    public async Task UpdateRecordNode_UnfilingACommunicationThatIsNotADirectMessage_AsAPerson_WritesNoOwner()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, withRecipient: false);

        await UpdateRecordCore(world, Resolved()).UpdateAsync(UnfileInput(Person), CancellationToken.None);

        world.Assignments.Should().BeEmpty();
        OwningTeam(world).Should().Be(FakeDirectory.ChildTeam);
    }

    [Fact]
    public async Task UpdateRecordNode_UnfilingADirectMessage_WhoseDirectTeamIsNotUsable_IsRefused()
    {
        var world = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);

        var act = () => UpdateRecordCore(world, DirectMessageOwnerStepTests.PartiallyConfigured(
            DirectMessageOwnerTeam.OwnerTeamNameConfigKey)).UpdateAsync(UnfileInput(Person), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        world.Assignments.Should().BeEmpty();
    }

    // ── The HTTP refusal shape ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RecordOwnerRefusal.DirectTeamUnavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(RecordOwnerRefusal.DirectTeamUnusable, StatusCodes.Status409Conflict)]
    [InlineData(RecordOwnerRefusal.NoOwnerSource, StatusCodes.Status409Conflict)]
    public void HttpRefusal_AnUnreadableDirectTeamIs503_EveryOtherOwnerRefusalStays409(string code, int status)
    {
        var result = ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution.Refused(code, "reason"), "communication", "trace");

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(code);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithBusinessUnit(DirectUnit, DirectMessageOwnerTeamDouble.BusinessUnitName, parent: FakeDirectory.GeneralBu)
        .WithTeam(DirectUnitDefaultTeam, DirectUnit, isDefault: true, teamType: 0, DirectMessageOwnerTeamDouble.BusinessUnitName)
        .WithTeam(DirectTeam, DirectUnit, isDefault: false, teamType: 0, DirectMessageOwnerTeamDouble.OwnerTeamName)
        .WithOrdinaryRoot("sprk_matter", MatterId);

    private static Dictionary<string, object?> Unfile() => new() { ["sprk_regardingmatter"] = null };

    private static readonly Guid SecureMatterId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c7");
    private static readonly Guid OtherMatterId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c8");
    private static readonly Guid AttachmentId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c9");
    private const string AttachmentTable = "sprk_communicationattachment";

    private static Dictionary<string, object> AttachmentOf() => new()
    {
        ["sprk_communication"] = new EntityReference("sprk_communication", MessageId),
    };

    /// <summary>The second step: the contact cleared, nothing else.</summary>
    private static Dictionary<string, object?> ClearTheContact() => new() { ["sprk_regardingperson"] = null };

    /// <summary>The thread reconcile the step after a re-file runs for a communication: seen as one call in order.</summary>
    private Mock<IThreadResolver> ThreadReconciles()
    {
        var threads = new Mock<IThreadResolver>();
        threads
            .Setup(t => t.ReconcileThreadForFilingAsync(
                It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(),
                It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("reconcile after the re-file"))
            .Returns(Task.CompletedTask);
        return threads;
    }

    /// <summary>The message's recorded creator: neither the acting <see cref="Person"/> nor <see cref="SecondPerson"/>.</summary>
    private static readonly Guid Creator = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003ca");

    /// <summary>A second person, who holds only Read and did not create the message.</summary>
    private static readonly Guid SecondPerson = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003cb");

    /// <summary><paramref name="holder"/> holds every right on every record (F3 permits leaving a secure record it names); anyone
    /// else holds Read only.</summary>
    private static Sprk.Bff.Api.Services.Access.IDataverseRecordShareService FullRightsFor(Guid holder)
    {
        var shares = new Mock<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>();
        shares
            .Setup(s => s.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid principal, string _, Guid _, CancellationToken _) => principal == holder
                ? AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share
                  | AccessRights.Delete
                : AccessRights.Read);
        return shares.Object;
    }

    /// <summary>The field-mapping push, re-filing the message by a lookup rule bound to <paramref name="bind"/>.</summary>
    private Task<(int Updated, int Failed, int Skipped, Sprk.Bff.Api.Api.FieldMappings.Dtos.PushFieldMappingsError[] Errors,
        Sprk.Bff.Api.Api.FieldMappings.Dtos.FieldMappingResultDto[] FieldResults)> PushAsync(
        FakeDirectory world, DirectMessageOwnerTeam lookup, string bind)
    {
        var rule = new Sprk.Bff.Api.Models.FieldMapping.FieldMappingRuleDto
        {
            SourceField = "sprk_mattersource",
            TargetField = "sprk_regardingmatter",
            SourceFieldType = "Lookup",
            TargetFieldType = "Lookup",
            Priority = 1,
        };
        var plans = new Dictionary<Sprk.Bff.Api.Models.FieldMapping.FieldMappingRuleDto, Sprk.Bff.Api.Api.FieldMappings.LookupBindPlan>(
            ReferenceEqualityComparer.Instance)
        {
            [rule] = new("sprk_RegardingMatter@odata.bind", bind, null),
        };
        var scopes = new ServiceCollection()
            .AddSingleton<IRecordOwnershipResolver>(world.Resolver(directMessageOwnerTeam: lookup))
            .AddSingleton(SecureChildReconcilerFixtures.OverEmptyWorld())
            .AddSingleton(Step(world, lookup))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            FieldWrites(world, new List<Dictionary<string, object?>>()),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper,
            [rule], new Dictionary<string, object?> { ["sprk_mattersource"] = Guid.NewGuid() },
            "sprk_communication", [MessageId], Person, NullLogger.Instance, CancellationToken.None,
            scopes, rootFiling: null, lookupBinds: plans, appOnlyEntities: Mock.Of<IGenericEntityService>());
    }

    private static readonly Guid ContactId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003c6");

    /// <summary>The re-file from the matter to a contact: the matter cleared and the contact bound in one update.</summary>
    private static Dictionary<string, object?> ToAContact() => new()
    {
        ["sprk_regardingmatter"] = null,
        ["sprk_RegardingPerson@odata.bind"] = $"/contacts({ContactId:D})",
    };

    private static UpdateRecordActionInput UnfileInput(Guid person) => new(
        "sprk_communication", MessageId, FieldMappings: null,
        LegacyFields: new Dictionary<string, string?> { ["sprk_regardingmatter"] = null }, Lookups: null,
        ImpersonateSystemUserId: person);

    private static Guid? OwningTeam(FakeDirectory world) =>
        world.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("owningteam")?.Id;

    private static DirectMessageOwnerTeam Resolved() => new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).Lookup();

    private (DataverseUpdateHandler Handler, List<Dictionary<string, object?>> Writes) UpdateHandler(
        FakeDirectory world, DirectMessageOwnerTeam lookup, bool withStep = true, IThreadResolver? threads = null,
        Sprk.Bff.Api.Services.Access.IDataverseRecordShareService? shares = null,
        Sprk.Bff.Api.Services.Access.SecureChildReconciler? children = null, Action<string, Guid, Guid>? afterOwnerWrite = null)
    {
        var writes = new List<Dictionary<string, object?>>();
        var handler = new DataverseUpdateHandler(
            FieldWrites(world, writes), Mock.Of<IGenericEntityService>(),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper,
            world.Resolver(directMessageOwnerTeam: lookup),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            children ?? SecureChildReconcilerFixtures.OverEmptyWorld(threads),
            shares: shares,
            directOwner: withStep ? Step(world, lookup, afterOwnerWrite) : null);
        return (handler, writes);
    }

    private UpdateRecordActionCore UpdateRecordCore(FakeDirectory world, DirectMessageOwnerTeam lookup) =>
        new(
            FieldWrites(world, new List<Dictionary<string, object?>>()),
            new ServiceCollection()
                .AddSingleton(new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper)
                .AddSingleton<IRecordOwnershipResolver>(world.Resolver(directMessageOwnerTeam: lookup))
                .AddSingleton(Step(world, lookup))
                .AddSingleton(EntitySetNameFixtures.Service())
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);

    /// <summary>The path's own PATCH, landed on the directory row (the resolver reads the row after it).</summary>
    private static IFieldMappingDataverseService FieldWrites(FakeDirectory world, List<Dictionary<string, object?>> writes)
    {
        var fields = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        fields
            .Setup(f => f.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, Guid?, CancellationToken>((t, id, f, _, _) =>
            {
                writes.Add(f);
                world.ApplyPatch(t, id, f);
            })
            .Returns(Task.CompletedTask);
        return fields.Object;
    }

    /// <summary>The real step over the directory: rows read by id, the message's attachment (when seeded) its one content row,
    /// owner writes conditional (each also handed to <paramref name="afterOwnerWrite"/> with the team it names).</summary>
    private DirectMessageOwnerStep Step(FakeDirectory world, DirectMessageOwnerTeam lookup, Action<string, Guid, Guid>? afterOwnerWrite = null)
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                q.Criteria.Conditions.Any(c => c.AttributeName == q.EntityName + "id")
                    ? world.Answer(q)
                    : q.EntityName == AttachmentTable
                      && world.Row(AttachmentTable, AttachmentId).GetAttributeValue<EntityReference>("sprk_communication")?.Id == MessageId
                        ? new EntityCollection(new List<Entity> { world.Row(AttachmentTable, AttachmentId) })
                        : new EntityCollection());
        dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, Dictionary<string, object>, CancellationToken>((table, id, version, fields, _) =>
            {
                _calls.Add("owner " + table);
                world.UpdateIfVersion(table, id, version, fields);
                if (afterOwnerWrite is not null && fields.TryGetValue("ownerid", out var owner) && owner is EntityReference team)
                    afterOwnerWrite(table, id, team.Id);
            })
            .Returns(Task.CompletedTask);
        var access = new Mock<IDirectThreadAccessService>();
        access
            .Setup(a => a.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ThreadAssignment, CancellationToken>((_, assignment, _) => _calls.Add("grant " + assignment.Audience))
            .Returns(Task.CompletedTask);
        return new DirectMessageOwnerStep(
            access.Object, dataverse.Object, lookup, NullLogger<DirectMessageOwnerStep>.Instance,
            configuration: new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
    }
}
