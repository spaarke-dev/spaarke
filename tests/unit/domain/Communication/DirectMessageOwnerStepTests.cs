using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a: the one step after a message joins a thread
/// (<see cref="DirectMessageOwnerStep"/>). It grants the message first, then assigns a Direct message filed under nothing, and its
/// content rows, to the configured Direct message owner team. A lookup that is not configured writes nothing and refuses nothing;
/// any other unresolved answer keeps today's owner with an Error. No owner is rolled back. It is also the 1:1 pre-send check.
/// </summary>
/// <remarks>The lookup is the real <see cref="DirectMessageOwnerTeam"/> over <see cref="DirectMessageOwnerTeamDouble"/>; Dataverse is
/// an in-memory row store behind <see cref="IGenericEntityService"/>.</remarks>
public class DirectMessageOwnerStepTests
{
    private static readonly Guid MessageId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid ThreadId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002");
    private static readonly Guid Sender = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000003");
    private static readonly Guid Recipient = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000004");
    private static readonly Guid AppUser = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000005");
    private static readonly Guid DirectTeam = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000006");
    private static readonly Guid OtherTeam = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000007");
    private static readonly Guid Attachment = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000011");
    private static readonly Guid Participant = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000012");
    private static readonly Guid ReviewLog = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000013");
    private static readonly Guid OwnDocument = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000014");
    private static readonly Guid FiledDocument = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000015");

    private readonly Mock<IGenericEntityService> _dataverse = new();
    private readonly Mock<IDirectThreadAccessService> _access = new();
    private readonly RecordingLogger _log = new();
    private readonly Dictionary<(string Table, Guid Id), Entity> _rows = new();
    private readonly List<string> _calls = new();
    private readonly HashSet<Guid> _refusedWrites = new();
    private readonly HashSet<Guid> _changedRows = new();

    public DirectMessageOwnerStepTests()
    {
        Store(DirectMessage());
        Store(Thread());
        // Rows that exist before the join (an attachment and its document uploaded with the message). The participant index and
        // the review logs are written by the path after the join: WriteContentAfterJoin.
        Store(Content("sprk_communicationattachment", Attachment, "sprk_communication", Owner(AppUser)));
        Store(Content("sprk_document", OwnDocument, "sprk_relatedcommunication", Owner(AppUser)));
        // A document related to the message but owned by another team (it is filed elsewhere): it is not the message's to move.
        Store(Content("sprk_document", FiledDocument, "sprk_relatedcommunication", new EntityReference("team", OtherTeam)));

        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => Query(q));
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
                Write(table, id, version, fields));
        _access
            .Setup(a => a.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ThreadAssignment, CancellationToken>((_, assignment, _) => _calls.Add("grant " + assignment.Audience))
            .Returns(Task.CompletedTask);
    }

    // ── Resolved: the message and its content rows go to the Direct team, after the grant ────────────────────────────────────

    [Fact]
    public async Task Resolved_GrantsFirst_ThenAssignsTheMessageToTheDirectTeam_ThenGrantsAgain()
    {
        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam);
        _calls.First().Should().Be("grant DirectParticipants", "the message is granted while it is still owned by its creator");
        _calls.IndexOf("owner sprk_communication " + MessageId).Should().BeGreaterThan(0);
        _calls.Last().Should().Be("grant DirectParticipants", "the grant after the move takes the Direct-team branch");
        _log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolved_ContentRowsOwnedLikeTheMessage_FollowIt_AndARowOwnedElsewhereStays()
    {
        var step = Step(Resolved());
        var join = await step.GrantAtJoinAsync(MessageId, Direct(), CancellationToken.None);
        WriteContentAfterJoin();
        await step.OwnDirectAfterContentAsync(join!, CancellationToken.None);

        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(DirectTeam, "a row that existed before the join moves too");
        OwningTeam("sprk_communicationparticipant", Participant).Should().Be(DirectTeam);
        OwningTeam("sprk_emailreviewlog", ReviewLog).Should().Be(DirectTeam);
        OwningTeam("sprk_document", OwnDocument).Should().Be(DirectTeam);
        OwningTeam("sprk_document", FiledDocument).Should().Be(OtherTeam);
        _calls.Should().NotContain(c => c.Contains(FiledDocument.ToString()));
    }

    [Fact]
    public async Task GrantAtJoin_GrantsOnly_AndTheOwnerPartRunsOnlyWhenCalled()
    {
        var step = Step(Resolved());

        var join = await step.GrantAtJoinAsync(MessageId, Direct(), CancellationToken.None);

        join.Should().NotBeNull();
        join!.CommunicationId.Should().Be(MessageId);
        _calls.Should().Equal("grant DirectParticipants");
        OwningTeam("sprk_communication", MessageId).Should().BeNull("the owner part waits for the path's content rows");

        await step.OwnDirectAfterContentAsync(join, CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam);
        _calls.Last().Should().Be("grant DirectParticipants");
    }

    [Fact]
    public async Task GrantAtJoin_AnAnchoredJoin_HasNoOwnerPart()
    {
        var join = await Step(Resolved()).GrantAtJoinAsync(
            MessageId, new ThreadAssignment(ThreadId, ThreadGrantAudience.AnchorRecordMembers), CancellationToken.None);

        join.Should().BeNull();
        _calls.Should().Equal("grant AnchorRecordMembers");
    }

    [Fact]
    public async Task Resolved_AContentRowRefused_StaysWithAnError_TheOthersAndTheMessageStillMove()
    {
        _refusedWrites.Add(Participant);

        var step = Step(Resolved());
        var join = await step.GrantAtJoinAsync(MessageId, Direct(), CancellationToken.None);
        WriteContentAfterJoin();
        await step.OwnDirectAfterContentAsync(join!, CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam, "an owner already set is never rolled back");
        OwningTeam("sprk_communicationparticipant", Participant).Should().BeNull();
        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(DirectTeam);
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5416);
    }

    [Fact]
    public async Task Resolved_TheMessageChangedSinceItWasRead_KeepsItsOwner_WithAnError_AndMovesNoContentRow()
    {
        _changedRows.Add(MessageId);

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        OwningTeam("sprk_communicationattachment", Attachment).Should().BeNull();
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5415);
        _calls.Count(c => c.StartsWith("grant", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Resolved_TheOwnerWriteIsATypedTeamReference_ConditionalOnTheVersionRead()
    {
        Dictionary<string, object>? written = null;
        string? version = null;
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync("sprk_communication", MessageId, It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, Dictionary<string, object>, CancellationToken>((_, _, v, f, _) => (version, written) = (v, f))
            .Returns((string table, Guid id, string v, Dictionary<string, object> fields, CancellationToken _) => Write(table, id, v, fields));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        written.Should().ContainSingle().Which.Key.Should().Be("ownerid");
        written!["ownerid"].Should().BeEquivalentTo(new EntityReference("team", DirectTeam));
        version.Should().Be("101");
    }

    // ── The configuration states ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotConfigured_GrantsOnly_ReadsNoMessage_WritesNothing_AndLogsNoError()
    {
        await Step(DirectMessageOwnerTeamDouble.Unconfigured()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        _calls.Should().Equal("grant DirectParticipants");
        _dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
        _log.Errors.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task NotConfigured_LogsNothingAboveDebug()
    {
        await Step(DirectMessageOwnerTeamDouble.Unconfigured()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        _log.Entries.Should().NotBeEmpty();
        _log.Entries.Should().OnlyContain(e => e.Level <= LogLevel.Debug, "the Direct owner rule is off while both settings are unset");
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey, DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey, DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    public async Task OnlyOneSettingSet_KeepsTodaysOwner_WithAnErrorNamingTheMissingSetting(string onlySet, string missing)
    {
        await Step(PartiallyConfigured(onlySet)).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        Owner("sprk_communication", MessageId).Should().Be(AppUser);
        _calls.Should().Equal("grant DirectParticipants");
        var error = _log.Errors.Should().ContainSingle().Subject;
        error.Event.Id.Should().Be(5414);
        error.Message.Should().Contain(DirectMessageOwnerTeamRefusal.PartiallyConfigured).And.Contain(missing);
    }

    [Fact]
    public async Task ConfiguredButRefused_KeepsTodaysOwner_WithAnError_AndNoDefaultTeam()
    {
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).WithUserInDirectUnit(Sender).Lookup();

        await Step(lookup).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        _dataverse.Verify(d => d.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5414 && e.Message.Contains(DirectMessageOwnerTeamRefusal.BusinessUnitHasUsers));
    }

    [Fact]
    public async Task LookupFaults_KeepsTodaysOwner_WithAnError()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        world.Faults = true;

        await Step(world.Lookup()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5414);
    }

    // ── The pair rule ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyRecipient_KeepsTodaysOwner_AndWritesNothing()
    {
        Store(DirectMessage(recipient: Guid.Empty));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        _calls.Should().Equal("grant DirectParticipants");
        _log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task SenderAndRecipientTheSameUser_KeepsTodaysOwner()
    {
        Store(DirectMessage(recipient: Sender));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
    }

    // ── Must not fire (AC3) ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordFiledMessage_InADirectTypeThread_KeepsTodaysOwner()
    {
        var row = DirectMessage();
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());
        Store(row);

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        _log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AnchoredJoin_GrantsOnly_AndNeverAsksTheLookup()
    {
        // A Direct-typed thread filed to a (secure) record joins as Anchored.
        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, new ThreadAssignment(ThreadId, ThreadGrantAudience.AnchorRecordMembers),
            CancellationToken.None);

        _calls.Should().Equal("grant AnchorRecordMembers");
        _dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyIntoThePerUserMaster_JoinsAsDirectParticipantsOnTheDefaultThread_KeepsTodaysOwner()
    {
        // The real path: the per-user master is typed Direct, so a reply into it joins as DirectParticipants (ThreadResolver's join).
        // The stamp names no recipient there (the master holds no pair), and the thread is the default thread.
        Store(Thread(isDefault: true));
        Store(DirectMessage(recipient: Guid.Empty));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        Owner("sprk_communication", MessageId).Should().Be(AppUser);
        _log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AStrictPairMessage_InTheDefaultThread_KeepsTodaysOwner()
    {
        Store(Thread(isDefault: true));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        Owner("sprk_communication", MessageId).Should().Be(AppUser);
        _calls.Should().Equal("grant DirectParticipants");
    }

    [Fact]
    public async Task AStrictPairMessage_InAThreadNotTypedDirect_KeepsTodaysOwner()
    {
        Store(Thread(threadType: 100000000));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
    }

    [Fact]
    public async Task TheThreadReadFaults_KeepsTodaysOwner_WithAnError()
    {
        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated read fault"));

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5415);
    }

    [Fact]
    public async Task Resolved_AContentRowFiledUnderARecord_WithTheSameOwner_StaysWhereItIs()
    {
        var filed = Content("sprk_document", FiledDocument, "sprk_relatedcommunication", Owner(AppUser));
        filed["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());
        Store(filed);

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam);
        OwningTeam("sprk_document", OwnDocument).Should().Be(DirectTeam);
        Owner("sprk_document", FiledDocument).Should().Be(AppUser, "a document filed under a matter is the matter's, not the message's");
        _calls.Should().NotContain(c => c.Contains(FiledDocument.ToString()));
    }

    [Fact]
    public async Task UnfiledEmail_KeepsTodaysOwner()
    {
        var row = DirectMessage();
        row["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email);
        Store(row);

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
    }

    [Fact]
    public async Task MessageFiledIntoAnotherThread_KeepsTodaysOwner()
    {
        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, new ThreadAssignment(Guid.NewGuid(), ThreadGrantAudience.DirectParticipants),
            CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().BeNull();
    }

    [Fact]
    public async Task MessageAlreadyTeamOwned_IsNotReassigned()
    {
        var row = DirectMessage();
        row["ownerid"] = new EntityReference("team", OtherTeam);
        row["owningteam"] = new EntityReference("team", OtherTeam);
        Store(row);

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(OtherTeam);
    }

    [Fact]
    public async Task TheThreadIsNeverWritten()
    {
        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        _dataverse.Verify(d => d.UpdateIfRowVersionMatchesAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.Verify(d => d.UpdateAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The pre-send check ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckBeforeSend_NotConfiguredOrResolved_Proceeds()
    {
        (await Step(DirectMessageOwnerTeamDouble.Unconfigured()).CheckBeforeSendAsync(CancellationToken.None)).Should().Be(DirectOwnerSendCheck.Proceed);
        (await Step(Resolved()).CheckBeforeSendAsync(CancellationToken.None)).Should().Be(DirectOwnerSendCheck.Proceed);
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task CheckBeforeSend_OnlyOneSettingSet_IsRefused_NotTransient(string onlySet)
    {
        var check = await Step(PartiallyConfigured(onlySet)).CheckBeforeSendAsync(CancellationToken.None);

        check.CanSend.Should().BeFalse();
        check.IsTransient.Should().BeFalse();
        check.RefusalCode.Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
    }

    [Fact]
    public async Task CheckBeforeSend_LookupFaults_IsUnavailable()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        world.Faults = true;

        (await Step(world.Lookup()).CheckBeforeSendAsync(CancellationToken.None)).Should().Be(DirectOwnerSendCheck.Unavailable);
    }

    // ── Piece 3b: the unfile direction, decided before the write ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideUnfile_AnEligibleDirectMessage_WithAResolvedLookup_GoesToTheDirectTeam()
    {
        var decision = await Decide(Resolved(), FiledDirectMessage());

        decision.Verdict.Should().Be(DirectUnfileVerdict.ToDirectTeam);
        _calls.Should().BeEmpty("the decision writes and grants nothing");
    }

    [Fact]
    public async Task DecideUnfile_NotConfigured_KeepsTheOwner_AndReadsNothing()
    {
        var decision = await Decide(DirectMessageOwnerTeamDouble.Unconfigured(), FiledDirectMessage());

        decision.Verdict.Should().Be(DirectUnfileVerdict.KeepOwner);
        _dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
        _log.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task DecideUnfile_OnlyOneSettingSet_IsRefused_WithAStableWarning(string onlySet)
    {
        var decision = await Decide(PartiallyConfigured(onlySet), FiledDirectMessage());

        decision.Verdict.Should().Be(DirectUnfileVerdict.Refused);
        decision.Refusal.Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        _log.Entries.Should().ContainSingle(e => e.Event.Id == 5419 && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task DecideUnfile_ConfiguredButRefused_IsRefused_WithTheLookupsCode()
    {
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).WithUserInDirectUnit(Sender).Lookup();

        var decision = await Decide(lookup, FiledDirectMessage());

        decision.Verdict.Should().Be(DirectUnfileVerdict.Refused);
        decision.Refusal.Should().Be(DirectMessageOwnerTeamRefusal.BusinessUnitHasUsers);
    }

    [Fact]
    public async Task DecideUnfile_LookupFaults_IsUnavailable()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        world.Faults = true;

        var decision = await Decide(world.Lookup(), FiledDirectMessage());

        decision.Verdict.Should().Be(DirectUnfileVerdict.Unavailable);
        decision.Fault.Should().NotBeNull();
        _log.Entries.Should().ContainSingle(e => e.Event.Id == 5419 && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task DecideUnfile_TheThreadReadFaults_IsUnavailable()
    {
        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated read fault"));

        var decision = await Decide(Resolved(), FiledDirectMessage());

        decision.Verdict.Should().Be(DirectUnfileVerdict.Unavailable);
    }

    [Theory]
    [InlineData("empty recipient")]
    [InlineData("same user")]
    [InlineData("email")]
    [InlineData("record thread")]
    [InlineData("default thread")]
    public async Task DecideUnfile_NotADirectMessage_KeepsTheOwner_EvenWhenTheLookupCannotAnswer(string shape)
    {
        var message = FiledDirectMessage(shape switch
        {
            "empty recipient" => Guid.Empty,
            "same user" => Sender,
            _ => null,
        });
        if (shape == "email")
            message["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email);
        if (shape == "record thread")
            Store(Thread(threadType: 100000000));
        if (shape == "default thread")
            Store(Thread(isDefault: true));

        var decision = await Decide(PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey), message);

        decision.Verdict.Should().Be(DirectUnfileVerdict.KeepOwner, "only an eligible Direct message is ever refused");
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task DecideUnfile_NoThread_KeepsTheOwner()
    {
        var decision = await DirectMessageOwnerStep.DecideUnfileAsync(
            _dataverse.Object, Resolved(), Unfiled(FiledDirectMessage(), threadAfter: null), threadId: null, _log, CancellationToken.None);

        decision.Verdict.Should().Be(DirectUnfileVerdict.KeepOwner);
    }

    // ── Piece 3b fix round: the row as the change leaves it ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("the change links a contact")]
    [InlineData("the row already links a contact")]
    [InlineData("the change links an account")]
    public async Task DecideUnfile_ARowStillLinkedToARecordAfterTheChange_KeepsTheOwner_AndIsNeverRefused(string shape)
    {
        var filed = FiledDirectMessage();
        var links = new Dictionary<string, EntityReference>();
        switch (shape)
        {
            case "the change links a contact":
                links["sprk_regardingperson"] = new EntityReference("contact", Guid.NewGuid());
                break;
            case "the row already links a contact":
                filed["sprk_regardingperson"] = new EntityReference("contact", Guid.NewGuid());
                break;
            default:
                links["sprk_regardingaccount"] = new EntityReference("account", Guid.NewGuid());
                break;
        }

        var rowAfter = DirectMessageOwnerStep.RowAfterChange(
            filed, new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = null }, links, ThreadId);
        var decision = await Decide(PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey), rowAfter, unfile: false);

        decision.Verdict.Should().Be(DirectUnfileVerdict.KeepOwner, "a message still linked to a record is not unfiled");
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task DecideUnfile_TheChangeClearsTheContactToo_IsAnUnfile()
    {
        var filed = FiledDirectMessage();
        filed["sprk_regardingperson"] = new EntityReference("contact", Guid.NewGuid());

        var rowAfter = DirectMessageOwnerStep.RowAfterChange(
            filed,
            new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = null, ["sprk_regardingperson"] = null },
            new Dictionary<string, EntityReference>(), ThreadId);

        (await Decide(Resolved(), rowAfter, unfile: false)).Verdict.Should().Be(DirectUnfileVerdict.ToDirectTeam);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("empty recipient")]
    [InlineData("still linked")]
    public async Task DecideUnfile_NotAnEligibleUnfile_ReadsNeitherTheLookupNorTheThread(string shape)
    {
        var message = FiledDirectMessage(shape == "empty recipient" ? Guid.Empty : null);
        if (shape == "email")
            message["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email);
        if (shape == "still linked")
            message["sprk_regardingorganization"] = new EntityReference("sprk_organization", Guid.NewGuid());
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);

        var decision = await Decide(world.Lookup(), message);

        decision.Verdict.Should().Be(DirectUnfileVerdict.KeepOwner);
        world.Reads.Should().Be(0, "a communication that is not an eligible Direct message reads nothing extra");
        _dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecideUnfile_AnEligibleUnfile_DoesReadTheLookup()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);

        (await Decide(world.Lookup(), FiledDirectMessage())).Verdict.Should().Be(DirectUnfileVerdict.ToDirectTeam);
        world.Reads.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AfterReparent_WhenTheRowReadBackIsStillLinkedToARecord_GrantsNothing_AndMovesNothing()
    {
        UnfiledFromRecordTeam();
        _rows[("sprk_communication", MessageId)]["sprk_regardingperson"] = new EntityReference("contact", Guid.NewGuid());

        await Step(Resolved()).AfterReparentAsync(MessageId, Due(leavesSecureOwner: false), CancellationToken.None);

        _calls.Should().BeEmpty("the participants are granted only for a message that moves");
        OwningTeam("sprk_communication", MessageId).Should().Be(RecordTeam);
        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(RecordTeam);
    }

    [Theory]
    [InlineData("a record thread")]
    [InlineData("an unresolved lookup")]
    public async Task AfterReparent_WhenTheThreadOrTheLookupDoesNotPass_GrantsNothing(string shape)
    {
        UnfiledFromRecordTeam();
        var lookup = Resolved();
        if (shape == "a record thread")
            Store(Thread(threadType: 100000000));
        else
            lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).WithUserInDirectUnit(Sender).Lookup();

        await Step(lookup).AfterReparentAsync(MessageId, Due(leavesSecureOwner: false), CancellationToken.None);

        _calls.Should().BeEmpty();
        OwningTeam("sprk_communication", MessageId).Should().Be(RecordTeam);
    }

    [Fact]
    public async Task DecideUnfile_AMessageTheDirectTeamAlreadyOwns_KeepsTheOwner()
    {
        var message = FiledDirectMessage();
        message["owningteam"] = new EntityReference("team", DirectTeam);

        (await Decide(Resolved(), message)).Verdict.Should().Be(DirectUnfileVerdict.KeepOwner);
    }

    // ── Piece 3b: the move after the write ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterReparent_FromAnOrdinaryRecord_GrantsFirst_AssignsTheMessage_MovesItsContentRows_ThenGrantsAgain()
    {
        UnfiledFromRecordTeam();

        await Step(Resolved()).AfterReparentAsync(MessageId, Due(leavesSecureOwner: false), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam);
        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(DirectTeam);
        OwningTeam("sprk_document", OwnDocument).Should().Be(DirectTeam);
        OwningTeam("sprk_document", FiledDocument).Should().Be(OtherTeam, "a row owned elsewhere is not the message's");
        _calls.First().Should().Be("grant DirectParticipants", "the participants are granted before the owner moves");
        _calls.IndexOf("owner sprk_communication " + MessageId).Should().Be(1);
        _calls.Last().Should().Be("grant DirectParticipants");
        _log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AfterReparent_FromASecureRecord_AssignsTheMessage_AndLeavesItsContentRowsToTheSecureChildReconcile()
    {
        UnfiledFromRecordTeam();

        await Step(Resolved()).AfterReparentAsync(MessageId, Due(leavesSecureOwner: true), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(DirectTeam);
        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(RecordTeam);
        OwningTeam("sprk_document", OwnDocument).Should().Be(RecordTeam);
        _calls.Should().Equal("grant DirectParticipants", "owner sprk_communication " + MessageId, "grant DirectParticipants");
    }

    [Fact]
    public async Task AfterReparent_WithNoMoveDue_DoesNothing()
    {
        UnfiledFromRecordTeam();
        var step = Step(Resolved());

        await step.AfterReparentAsync(MessageId, null, CancellationToken.None);
        await step.AfterReparentAsync(MessageId, Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Unchanged("unchanged"),
            CancellationToken.None);

        _calls.Should().BeEmpty();
        OwningTeam("sprk_communication", MessageId).Should().Be(RecordTeam);
    }

    [Fact]
    public async Task AfterReparent_TheOwnerWriteNotConfirmed_KeepsTheOwner_WithAStableError_AndNeverThrows()
    {
        UnfiledFromRecordTeam();
        _changedRows.Add(MessageId);

        await Step(Resolved()).AfterReparentAsync(MessageId, Due(leavesSecureOwner: false), CancellationToken.None);

        OwningTeam("sprk_communication", MessageId).Should().Be(RecordTeam, "the unfile stands; 054c is the recovery");
        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(RecordTeam);
        _log.Errors.Should().ContainSingle(e => e.Event.Id == 5415);
    }

    [Theory]
    [InlineData("sprk_communication", true, true, Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.UnfiledCommunication)]
    [InlineData("sprk_communication", true, false, Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.KeepCreator)]
    [InlineData("sprk_communication", false, true, Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.ActingUserTeam)]
    [InlineData("sprk_event", true, true, Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.ActingUserTeam)]
    public void UnfiledOwnershipFor_IsTheOneRuleEveryWriterTakes(
        string table, bool actsForAPerson, bool withStep, Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership expected) =>
        DirectMessageOwnerStep.UnfiledOwnershipFor(table, actsForAPerson, withStep ? Step(Resolved()) : null).Should().Be(expected);

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid RecordTeam = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000021");

    /// <summary>The decision before an unfile: <paramref name="message"/> as read before the change, unfiled by clearing its matter
    /// (<see cref="Unfiled"/>) unless <paramref name="unfile"/> is false (the row is already the row after the change).</summary>
    private Task<DirectUnfileDecision> Decide(DirectMessageOwnerTeam lookup, Entity message, bool unfile = true) =>
        DirectMessageOwnerStep.DecideUnfileAsync(
            _dataverse.Object, lookup, unfile ? Unfiled(message, ThreadId) : message, ThreadId, _log, CancellationToken.None);

    /// <summary>The row the unfile leaves: <paramref name="filed"/> with its matter cleared, in <paramref name="threadAfter"/>.</summary>
    private static Entity Unfiled(Entity filed, Guid? threadAfter) =>
        DirectMessageOwnerStep.RowAfterChange(
            filed, new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = null },
            new Dictionary<string, EntityReference>(), threadAfter);

    /// <summary>The message as the reparent reads it before an unfile: filed under a matter, owned by that matter's team.</summary>
    private static Entity FiledDirectMessage(Guid? recipient = null)
    {
        var row = DirectMessage(recipient);
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());
        row["ownerid"] = new EntityReference("team", RecordTeam);
        row["owningteam"] = new EntityReference("team", RecordTeam);
        return row;
    }

    /// <summary>The message after its unfile was written: filed under nothing, still owned (with its content rows) by the team of
    /// the record it left.</summary>
    private void UnfiledFromRecordTeam()
    {
        var row = DirectMessage();
        row["ownerid"] = new EntityReference("team", RecordTeam);
        row["owningteam"] = new EntityReference("team", RecordTeam);
        Store(row);
        Store(Content("sprk_communicationattachment", Attachment, "sprk_communication", new EntityReference("team", RecordTeam)));
        Store(Content("sprk_document", OwnDocument, "sprk_relatedcommunication", new EntityReference("team", RecordTeam)));
    }

    private static Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution Due(bool leavesSecureOwner) =>
        Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Unchanged("unfiled") with
        {
            DirectUnfile = new DirectUnfileDue(ThreadId, leavesSecureOwner),
        };

    private DirectMessageOwnerStep Step(DirectMessageOwnerTeam lookup) => new(_access.Object, _dataverse.Object, lookup, _log);

    private static DirectMessageOwnerTeam Resolved() => new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).Lookup();

    /// <summary>The lookup with exactly one Direct setting set. It reads nothing, so its client is never called.</summary>
    internal static DirectMessageOwnerTeam PartiallyConfigured(string onlySetKey)
    {
        var client = new DataverseWebApiClient(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            new NoCredential());
        return new DirectMessageOwnerTeam(
            client,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [onlySetKey] = onlySetKey == DirectMessageOwnerTeam.BusinessUnitNameConfigKey
                    ? DirectMessageOwnerTeamDouble.BusinessUnitName
                    : DirectMessageOwnerTeamDouble.OwnerTeamName,
            }).Build(),
            TimeProvider.System,
            NullLogger<DirectMessageOwnerTeam>.Instance);
    }

    private static ThreadAssignment Direct() => new(ThreadId, ThreadGrantAudience.DirectParticipants);

    private static EntityReference Owner(Guid user) => new("systemuser", user);

    private static Entity DirectMessage(Guid? recipient = null)
    {
        var row = new Entity("sprk_communication", MessageId)
        {
            ["sprk_communicationid"] = MessageId,
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Message),
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", ThreadId),
            ["sprk_sentby"] = new EntityReference("systemuser", Sender),
            ["ownerid"] = Owner(AppUser),
            ["versionnumber"] = 101L,
        };
        if (recipient != Guid.Empty)
            row["sprk_directrecipient"] = new EntityReference("systemuser", recipient ?? Recipient);
        return row;
    }

    /// <summary>The joined thread as task 198 narrows on it: typed Direct and not the per-user master thread by default.</summary>
    private static Entity Thread(int threadType = 100000001, bool isDefault = false) => new("sprk_communicationthread", ThreadId)
    {
        ["sprk_communicationthreadid"] = ThreadId,
        ["sprk_threadtype"] = new OptionSetValue(threadType),
        ["sprk_isdefaultthread"] = isDefault,
    };

    private static Entity Content(string table, Guid id, string lookup, EntityReference owner) => new(table, id)
    {
        [lookup] = new EntityReference("sprk_communication", MessageId),
        ["ownerid"] = owner,
        ["owningteam"] = owner.LogicalName == "team" ? owner : null,
        ["versionnumber"] = 7L,
    };

    private void Store(Entity row) => _rows[(row.LogicalName, row.Id)] = row;

    /// <summary>The rows the path writes after the join and before the owner part (the participant index, the review logs), owned
    /// by their creator as the message is.</summary>
    private void WriteContentAfterJoin()
    {
        Store(Content("sprk_communicationparticipant", Participant, "sprk_communication", Owner(AppUser)));
        Store(Content("sprk_emailreviewlog", ReviewLog, "sprk_communication", Owner(AppUser)));
    }

    private Guid? OwningTeam(string table, Guid id) => _rows[(table, id)].GetAttributeValue<EntityReference>("owningteam")?.Id;

    private Guid? Owner(string table, Guid id) => _rows[(table, id)].GetAttributeValue<EntityReference>("ownerid")?.Id;

    private EntityCollection Query(QueryExpression q)
    {
        var condition = q.Criteria.Conditions.Single();
        var value = (Guid)condition.Values.Single();
        var rows = condition.AttributeName == q.EntityName + "id"
            ? _rows.Values.Where(r => r.LogicalName == q.EntityName && r.Id == value)
            : _rows.Values.Where(r => r.LogicalName == q.EntityName
                                      && r.GetAttributeValue<EntityReference>(condition.AttributeName)?.Id == value);
        return new EntityCollection(rows.ToList());
    }

    private Task Write(string table, Guid id, string version, Dictionary<string, object> fields)
    {
        _calls.Add($"owner {table} {id}");
        if (_changedRows.Contains(id))
            return Task.FromException(new System.Data.DBConcurrencyException("The row changed."));
        if (_refusedWrites.Contains(id))
            return Task.FromException(new InvalidOperationException("Refused."));

        var row = _rows[(table, id)];
        row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be(version);
        var team = (EntityReference)fields["ownerid"];
        row["ownerid"] = team;
        row["owningteam"] = team;
        return Task.CompletedTask;
    }

    private sealed class NoCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This lookup reads nothing.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This lookup reads nothing.");
    }

    /// <summary>Records each log entry's level, event and formatted message.</summary>
    internal sealed class RecordingLogger : ILogger<DirectMessageOwnerStep>
    {
        public List<(LogLevel Level, EventId Event, string Message)> Entries { get; } = new();

        public IEnumerable<(LogLevel Level, EventId Event, string Message)> Errors => Entries.Where(e => e.Level == LogLevel.Error);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
