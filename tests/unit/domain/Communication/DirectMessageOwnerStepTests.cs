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
        Store(Content("sprk_communicationattachment", Attachment, "sprk_communication", Owner(AppUser)));
        Store(Content("sprk_communicationparticipant", Participant, "sprk_communication", Owner(AppUser)));
        Store(Content("sprk_emailreviewlog", ReviewLog, "sprk_communication", Owner(AppUser)));
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
        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

        OwningTeam("sprk_communicationattachment", Attachment).Should().Be(DirectTeam);
        OwningTeam("sprk_communicationparticipant", Participant).Should().Be(DirectTeam);
        OwningTeam("sprk_emailreviewlog", ReviewLog).Should().Be(DirectTeam);
        OwningTeam("sprk_document", OwnDocument).Should().Be(DirectTeam);
        OwningTeam("sprk_document", FiledDocument).Should().Be(OtherTeam);
        _calls.Should().NotContain(c => c.Contains(FiledDocument.ToString()));
    }

    [Fact]
    public async Task Resolved_AContentRowRefused_StaysWithAnError_TheOthersAndTheMessageStillMove()
    {
        _refusedWrites.Add(Participant);

        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, Direct(), CancellationToken.None);

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

    [Theory]
    [InlineData(ThreadGrantAudience.AnchorRecordMembers)] // a Direct thread filed to a (secure) record joins as Anchored
    [InlineData(ThreadGrantAudience.None)] // the per-user master thread grants nobody
    public async Task NonDirectJoin_GrantsOnly_AndNeverAsksTheLookup(ThreadGrantAudience audience)
    {
        await Step(Resolved()).GrantThenOwnDirectAsync(MessageId, new ThreadAssignment(ThreadId, audience), CancellationToken.None);

        _calls.Should().Equal("grant " + audience);
        _dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
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

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

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

    private static Entity Content(string table, Guid id, string lookup, EntityReference owner) => new(table, id)
    {
        [lookup] = new EntityReference("sprk_communication", MessageId),
        ["ownerid"] = owner,
        ["owningteam"] = owner.LogicalName == "team" ? owner : null,
        ["versionnumber"] = 7L,
    };

    private void Store(Entity row) => _rows[(row.LogicalName, row.Id)] = row;

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
