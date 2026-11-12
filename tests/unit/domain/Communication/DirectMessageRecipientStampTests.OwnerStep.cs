using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a, through the real entry points: every join path (the send with no thread
/// id, the respond into a thread, the inbound capture) runs the one owner step, and a 1:1 message send is refused before anything
/// is sent when the Direct message owner team is configured but unusable (409) or cannot be read (503). No Direct setting at all
/// sends as before.
/// </summary>
public partial class DirectMessageRecipientStampTests
{
    private readonly DirectMessageOwnerStepTests.RecordingLogger _stepLog = new();

    private DirectMessageOwnerStep OwnerStep(DirectMessageOwnerTeam lookup) =>
        new(_directAccess.Object, _entities.Object, lookup, _stepLog, recordShares: _shares.Object);

    /// <summary>The team the owner step moved the message to, through its conditional owner write; <c>null</c> when it did not.</summary>
    private Guid? _messageOwnerTeam;

    // ── The pre-send refusal (1:1 only) ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task OneToOneSend_WithOnlyOneDirectSettingSet_Is409_AndSendsNothing(string onlySet)
    {
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(onlySet));

        var act = () => Service(Join(DirectThread), ownerStep: step).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.StatusCode.Should().Be(409);
        refusal.Code.Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        refusal.Extensions!["reasonCode"].Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        refusal.Detail.Should().Contain("not sent");
        _sender.Sends.Should().Be(0);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communication");
    }

    [Fact]
    public async Task OneToOneSend_WhenTheDirectTeamCannotBeRead_Is503_AndSendsNothing()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(Guid.NewGuid());
        world.Faults = true;
        var step = OwnerStep(world.Lookup());

        var act = () => Service(Join(DirectThread), ownerStep: step).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.StatusCode.Should().Be(503);
        refusal.Detail.Should().Contain("Try again");
        _sender.Sends.Should().Be(0);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communication");
    }

    [Fact]
    public async Task OneToOneSend_WithNoDirectSetting_Sends()
    {
        var step = OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured());

        await Service(Join(DirectThread), ownerStep: step).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1);
        SentMessageCreate().Should().NotBeNull();
    }

    [Fact]
    public async Task SendToTwoPeople_WithOnlyOneDirectSettingSet_IsNotRefused()
    {
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey));

        await Service(Join(DirectThread), ownerStep: step).SendAsync(
            MessageRequest() with { To = new[] { "someone@contoso.com", "another@contoso.com" } }, Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1);
    }

    [Fact]
    public async Task SelfSend_WithOnlyOneDirectSettingSet_IsNotRefused()
    {
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey));

        await Service(Join(DirectThread), ownerStep: step).SendAsync(
            MessageRequest() with { To = new[] { "Sender@Contoso.com" } }, Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1, "the sender's own address is not another person");
    }

    // ── The pre-send refusal of a reply: the thread's shape decides ─────────────────────────────────────────────────────

    /// <summary>The Console's reply: a Message into a thread, with no addressee (<c>{ communicationType, threadId, body }</c>).</summary>
    private static SendCommunicationRequest ReplyRequest() => MessageRequest() with { ThreadId = DirectThread, To = Array.Empty<string>() };

    [Fact]
    public async Task ReplyIntoAStrictPair_WithNoTo_AndOnlyOneDirectSettingSet_Is409_AndSendsNothing()
    {
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey));

        var act = () => Service(resolver: null, ownerStep: step).SendAsync(ReplyRequest(), Caller(), CancellationToken.None);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.StatusCode.Should().Be(409);
        refusal.Code.Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        _sender.Sends.Should().Be(0);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communication");
    }

    [Fact]
    public async Task ReplyWhereTheThreadShapeCannotBeRead_Is503_AndSendsNothing()
    {
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated 429 from Dataverse"));
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey));

        var act = () => Service(resolver: null, ownerStep: step).SendAsync(ReplyRequest(), Caller(), CancellationToken.None);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.StatusCode.Should().Be(503);
        _sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task ReplyIntoAStrictPair_WhenTheDirectTeamCannotBeRead_Is503_AndSendsNothing()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(Guid.NewGuid());
        world.Faults = true;

        var act = () => Service(resolver: null, ownerStep: OwnerStep(world.Lookup())).SendAsync(ReplyRequest(), Caller(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
        _sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task ReplyIntoAGroupThread_WithOneToAddress_IsNotRefused()
    {
        _threadShares = new() { UserShare(Other, ReadMask), UserShare(Third, ReadMask) };
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey));

        await Service(resolver: null, ownerStep: step).SendAsync(
            MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1, "a reply into a thread of three is not 1:1, whatever its To names");
    }

    [Fact]
    public async Task ReplyWithNoDirectSetting_Sends_AndTheThreadShapeIsNeverReadBeforeTheSend()
    {
        var sendsAtShareRead = new List<int>();
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .Callback(() => sendsAtShareRead.Add(_sender.Sends))
            .ReturnsAsync(() => _threadShares.ToList());

        await Service(resolver: null, ownerStep: OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .SendAsync(ReplyRequest(), Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1);
        sendsAtShareRead.Should().OnlyContain(sends => sends >= 1, "piece 3 is off, so the pre-send check reads no thread shape");
    }

    // ── Every join path, with a RESOLVED lookup and a valid pair, moves the message to the Direct team ─────────────────

    [Fact]
    public async Task SendJoin_WithAResolvedLookup_MovesTheMessageToTheDirectTeam()
    {
        var team = ArrangeTheOwnerStepWorld();

        await Service(Join(DirectThread), ownerStep: OwnerStep(new DirectMessageOwnerTeamDouble().WithDirectTeam(team).Lookup()))
            .SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageOwnerTeam.Should().Be(team);
    }

    [Fact]
    public async Task RespondIntoADirectThread_WithAResolvedLookup_MovesTheMessageToTheDirectTeam()
    {
        var team = ArrangeTheOwnerStepWorld();

        await Service(resolver: null, ownerStep: OwnerStep(new DirectMessageOwnerTeamDouble().WithDirectTeam(team).Lookup()))
            .SendAsync(ReplyRequest(), Caller(), CancellationToken.None);

        _messageOwnerTeam.Should().Be(team);
    }

    [Fact]
    public async Task InboundJoin_WithAResolvedLookup_MovesTheMessageToTheDirectTeam()
    {
        var team = ArrangeTheOwnerStepWorld();
        _messageRow = MessageRow(sentBy: null); // an inbound row carries no sender until the join stamps the mapped one

        await InboundHandler(Join(DirectThread), OwnerStep(new DirectMessageOwnerTeamDouble().WithDirectTeam(team).Lookup()))
            .ProcessAsync(InboundJob(SenderChatId), CancellationToken.None);

        _messageOwnerTeam.Should().Be(team);
    }

    /// <summary>
    /// The rows the owner step reads, as stored: the message is its create plus every filing update that landed (so its pair is
    /// the one the join stamped), owned by the application user until the step's conditional owner write moves it; the thread is
    /// the stored thread. Returns the Direct team's id.
    /// </summary>
    private Guid ArrangeTheOwnerStepWorld()
    {
        var team = Guid.NewGuid();
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communication" && ById(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EntityCollection(new List<Entity> { StoredMessage() }));
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread" && ById(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EntityCollection(new List<Entity> { _threadRow }));
        _entities
            .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                "sprk_communication", MessageId, "5", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, Dictionary<string, object>, CancellationToken>((_, _, _, fields, _) =>
                _messageOwnerTeam = fields["ownerid"] is EntityReference { LogicalName: "team" } owner ? owner.Id : null)
            .Returns(Task.CompletedTask);
        return team;
    }

    /// <summary>A read of one row by its id (the owner step's reads), not a search by another column.</summary>
    private static bool ById(QueryExpression query) =>
        query.Criteria.Conditions.Count == 1 && query.Criteria.Conditions[0].AttributeName == query.EntityName + "id";

    private Entity StoredMessage()
    {
        var row = new Entity("sprk_communication", MessageId) { ["ownerid"] = new EntityReference("systemuser", AppUser) };
        foreach (var attribute in _created.Single(e => e.LogicalName == "sprk_communication").Attributes)
            row[attribute.Key] = attribute.Value;
        foreach (var update in _messageUpdates)
        {
            foreach (var (column, value) in update)
                row[column] = value;
        }

        row["versionnumber"] = 5L;
        if (_messageOwnerTeam is { } team)
        {
            row["ownerid"] = new EntityReference("team", team);
            row["owningteam"] = new EntityReference("team", team);
        }

        return row;
    }

    // ── Every join path runs the owner step ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendJoin_RunsTheOwnerStep()
    {
        await Service(Join(DirectThread), ownerStep: OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        StepRanFor(DirectThread);
    }

    [Fact]
    public async Task RespondIntoADirectThread_RunsTheOwnerStep()
    {
        await Service(resolver: null, ownerStep: OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .SendAsync(MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        StepRanFor(DirectThread);
    }

    [Fact]
    public async Task InboundJoin_RunsTheOwnerStep()
    {
        await InboundHandler(Join(DirectThread), OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .ProcessAsync(InboundJob(SenderChatId), CancellationToken.None);

        StepRanFor(DirectThread);
    }

    /// <summary>The step's Debug "grant attempted" entry names the join's audience and thread.</summary>
    private void StepRanFor(Guid threadId)
    {
        _stepLog.Entries.Should().Contain(e => e.Message.Contains("DirectParticipants") && e.Message.Contains(threadId.ToString()),
            "the join's grant goes through the owner step");
        _directAccess.Verify(a => a.GrantMessageAccessAsync(MessageId, It.Is<ThreadAssignment>(x => x.ThreadId == threadId),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
