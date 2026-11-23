using System.Net;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Tests.Api.Communication;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056b: what a private message writes, and that a refused one writes nothing. Driven
/// through the real <c>POST /api/communications/send</c> and <c>CommunicationService</c> (<see cref="PrivateMessageSendHost"/>);
/// the rows are captured at the Dataverse boundary and the send at the Message channel sender.
/// <list type="bullet">
///   <item>AC1: a pair send is sent once and its row carries exactly one reference column (for its table) and
///   <c>sprk_isinternalonly = true</c>, no typed regarding, and the Direct filing stamps the pair.</item>
///   <item>AC3: every refusal before the send sends nothing and creates or updates no row.</item>
///   <item>Order: a sender who fails gets the sender text and no recipient question is asked.</item>
///   <item>AC4: an ordinary record-thread send is unchanged: no reference column, no gate question, the same row.</item>
/// </list>
/// </summary>
public class PrivateMessageSendTests : IClassFixture<PrivateMessageSendHost>
{
    private static readonly Guid MessageId = Guid.Parse("05605605-0560-4560-8560-0000000000c1");

    private readonly PrivateMessageSendHost _fixture;

    public PrivateMessageSendTests(PrivateMessageSendHost fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    /// <summary>
    /// The created message keeps a fixed id and reads back as created (the Direct filing re-reads it), and both participants read
    /// enabled people (the Direct participant rule the filing stamp applies).
    /// </summary>
    private void MessageRowsReadBack()
    {
        _fixture.Host.Entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback((DataverseEntity e, CancellationToken _) => _fixture.Created.Add(e))
            .ReturnsAsync(MessageId);
        _fixture.Host.Entities
            .Setup(e => e.RetrieveAsync("sprk_communication", MessageId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var row = new DataverseEntity("sprk_communication", MessageId);
                foreach (var attribute in _fixture.Created.Single(c => c.LogicalName == "sprk_communication").Attributes)
                    row[attribute.Key] = attribute.Value;
                return row;
            });
        _fixture.Host.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var collection = new EntityCollection();
                foreach (var id in q.Criteria.Conditions.Single(c => c.AttributeName == "systemuserid").Values.Cast<Guid>())
                    collection.Entities.Add(new DataverseEntity("systemuser", id) { ["isdisabled"] = false });
                return collection;
            });
    }

    private DataverseEntity CreatedMessage() =>
        _fixture.Created.Should().ContainSingle(c => c.LogicalName == "sprk_communication").Subject;

    private void AssertNothingWritten()
    {
        _fixture.Host.MessageSender.Sent.Should().BeEmpty("the channel is never called");
        _fixture.Host.Entities.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _fixture.Updates.Should().BeEmpty("no row is updated");
    }

    // ── AC1: the pair send ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task PrivateSend_IntoTheCallersPair_IsSentOnce_AndItsRowCarriesExactlyOneReference_AndIsInternalOnly(string table)
    {
        MessageRowsReadBack();
        var thread = _fixture.PairThread();
        var record = Guid.NewGuid();

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread, entityType: table, recordId: record));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Host.MessageSender.Sent.Should().ContainSingle();

        var row = CreatedMessage();
        var expectedColumn = PrivateContextColumns.ColumnByTable[table];
        PrivateContextColumns.All.Where(row.Attributes.ContainsKey).Should().Equal(new[] { expectedColumn });
        row.GetAttributeValue<EntityReference>(expectedColumn).Should().BeEquivalentTo(new EntityReference(table, record));
        row.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeTrue();
        DirectMessageRecipient.CarriesATypedRegarding(row).Should().BeFalse("the reference is not a filing: the message stays a Direct message");
        RegardingFieldMap.AllRegardingFields.Where(row.Attributes.ContainsKey).Should().BeEmpty();
        row.GetAttributeValue<EntityReference>("sprk_sentby")?.Id.Should().Be(PrivateMessageSendHost.Caller);
    }

    [Fact]
    public async Task PrivateSend_IntoTheCallersPair_IsFiledIntoThatThread_WithBothParticipantsStamped()
    {
        // The explicit-thread send (the respond path) files a Direct message with sprk_sentby + sprk_directrecipient in the
        // filing write; a private message takes that path unchanged.
        MessageRowsReadBack();
        var thread = _fixture.PairThread();

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var filing = _fixture.Updates.Should()
            .ContainSingle(u => u.Table == "sprk_communication" && u.Id == MessageId && u.Fields.ContainsKey("sprk_communicationthread"))
            .Subject.Fields;
        ((EntityReference)filing["sprk_communicationthread"]).Id.Should().Be(thread);
        ((EntityReference)filing[DirectMessageRecipient.RecipientColumn]).Id.Should().Be(PrivateMessageSendHost.Recipient);
        CreatedMessage().GetAttributeValue<EntityReference>(DirectMessageRecipient.SenderColumn).Id.Should().Be(PrivateMessageSendHost.Caller);
        PrivateContextColumns.All.Where(filing.ContainsKey).Should().BeEmpty("the reference is written once, at create");
    }

    [Fact]
    public async Task PrivateSend_WhenTheCallerIsTheThreadsOtherMember_AsksAboutTheOwnerAsRecipient()
    {
        MessageRowsReadBack();
        var owner = Guid.NewGuid();
        var thread = _fixture.PairThread(owner: owner, other: PrivateMessageSendHost.Caller);

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Script.Calls.Should().Contain(c => c.StartsWith($"recipient_rights {owner} ", StringComparison.Ordinal));
    }

    // ── AC3: a refusal before the send writes nothing ───────────────────────────────────────────────────────────────────

    public static TheoryData<string> Refusals() => new()
    {
        "sender-no-read", "sender-walled", "sender-external-on-restricted", "recipient-no-read", "recipient-walled",
        "recipient-external-on-restricted", "recipient-rights-unanswered", "wall-unverifiable", "columns-missing", "not-a-pair",
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task PrivateSend_RefusedBeforeTheSend_SendsNothing_AndWritesNoRow(string refusal)
    {
        MessageRowsReadBack();
        var thread = refusal == "not-a-pair" ? _fixture.PairThread(owner: Guid.NewGuid(), other: Guid.NewGuid()) : _fixture.PairThread();
        var script = _fixture.Script;
        switch (refusal)
        {
            case "sender-no-read": script.SenderRights = AccessRights.None; break;
            case "sender-walled": script.Wall[PrivateMessageSendHost.Caller] = SecureShareWallOutcome.Walled; break;
            case "sender-external-on-restricted":
                script.Restricted = true;
                script.External[PrivateMessageSendHost.Caller] = true;
                break;
            case "recipient-no-read": script.RecipientRights = AccessRights.None; break;
            case "recipient-walled": script.Wall[PrivateMessageSendHost.Recipient] = SecureShareWallOutcome.Walled; break;
            case "recipient-external-on-restricted":
                script.Restricted = true;
                script.External[PrivateMessageSendHost.Recipient] = true;
                break;
            case "recipient-rights-unanswered": script.RecipientRights = null; break;
            case "wall-unverifiable": script.Wall[PrivateMessageSendHost.Recipient] = SecureShareWallOutcome.Unverifiable; break;
            case "columns-missing": script.ColumnsPresent = false; break;
        }

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        ((int)response.StatusCode).Should().BeOneOf(400, 403, 409, 503);
        AssertNothingWritten();
    }

    // ── Order: the sender's checks in full, before any recipient question ──────────────────────────────────────────────

    public static TheoryData<string> SenderFailures() => new() { "no-read", "unknown-record", "walled", "external-on-restricted" };

    [Theory]
    [MemberData(nameof(SenderFailures))]
    public async Task PrivateSend_WhenTheSenderFails_GetsTheSenderText_AndNoRecipientQuestionIsAsked(string cause)
    {
        var thread = _fixture.PairThread();
        var script = _fixture.Script;
        // The recipient would fail every check too: only the order decides which text is returned.
        script.RecipientRights = AccessRights.None;
        script.Wall[PrivateMessageSendHost.Recipient] = SecureShareWallOutcome.Walled;
        script.External[PrivateMessageSendHost.Recipient] = true;
        switch (cause)
        {
            case "no-read":
            case "unknown-record": // the equalised gate question answers an unknown id as None, the same as a denied one
                script.SenderRights = AccessRights.None;
                break;
            case "walled":
                script.Wall[PrivateMessageSendHost.Caller] = SecureShareWallOutcome.Walled;
                break;
            default:
                script.Restricted = true;
                script.External[PrivateMessageSendHost.Caller] = true;
                break;
        }

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(PrivateMessageSendGate.SenderAccessDetail);
        script.AskedAboutRecipient.Should().BeFalse("a refused sender learns nothing about the recipient's access");
        script.Calls.Should().NotContain("columns");
        AssertNothingWritten();
    }

    [Fact]
    public async Task PrivateSend_AsksEverySenderQuestion_BeforeTheFirstRecipientQuestion()
    {
        MessageRowsReadBack();
        var thread = _fixture.PairThread();
        _fixture.Script.Restricted = true; // every check reads, so the whole order is visible

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var calls = _fixture.Script.Calls.Select(c => c.Split(' ')[0] + (c.Contains(PrivateMessageSendHost.Caller.ToString()) ? ":sender"
            : c.Contains(PrivateMessageSendHost.Recipient.ToString()) ? ":recipient" : string.Empty)).ToList();
        calls.Should().Equal(
            "sender_rights", "wall:sender", "restricted", "external:sender",
            "recipient_rights:recipient", "wall:recipient", "external:recipient",
            "columns");
    }

    // ── AC4: an ordinary send is unchanged ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The columns an ordinary Message into a record thread is created with, as before task 056b (verified against the code before
    /// the change). Any extra column, a reference column included, changes the row.
    /// </summary>
    private static readonly string[] OrdinaryRecordThreadMessageColumns =
    {
        "sprk_name", "sprk_communicationtype", "statuscode", "statecode", "sprk_direction", "sprk_bodyformat", "sprk_to", "sprk_from",
        "sprk_sentby", "sprk_subject", "sprk_body", "sprk_isinternalonly", "sprk_sentat", "sprk_correlationid",
        "sprk_regardingmatter", "sprk_regardingrecordid", "sprk_associationstatus", "ownerid", "sprk_createdbyperson",
        "sprk_acsmessageid",
    };

    [Fact]
    public async Task OrdinarySend_IntoARecordThread_IsUnchanged_NoReferenceColumn_AndNoGateQuestion()
    {
        MessageRowsReadBack();
        var matter = Guid.NewGuid();
        _fixture.Host.Probe.Grant("sprk_matters", matter, AccessRights.Read | AccessRights.AppendTo);
        var thread = _fixture.RecordThread(matter);

        var response = await _fixture.SendAsync(new
        {
            to = new[] { "colleague@contoso.com" },
            subject = "About the matter",
            body = "For the team.",
            communicationType = "Message",
            threadId = thread,
            isInternalOnly = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var row = CreatedMessage();
        PrivateContextColumns.All.Where(row.Attributes.ContainsKey).Should().BeEmpty();
        row.Attributes.Keys.Should().BeEquivalentTo(OrdinaryRecordThreadMessageColumns);
        row.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeFalse("an Open record thread keeps the requested value");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(matter);
        _fixture.Script.Calls.Should().BeEmpty("the gate is asked only for a private message");
        _fixture.Host.MessageSender.Sent.Should().ContainSingle().Which.Communication.PrivateContext.Should().BeNull();
    }
}
