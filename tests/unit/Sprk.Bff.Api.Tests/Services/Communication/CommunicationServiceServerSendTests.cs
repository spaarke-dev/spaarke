using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// ebilling-r1 task 151c-1a (EB-D-182 (1)): the server-only send options (<see cref="ServerSendOptions"/>) on a send-as-user email, through the REAL
/// <see cref="CommunicationService.SendAsync"/>: attachments from memory reach the channel, the owner follows a record through the ownership resolver
/// (never the default), no enrichment runs, nothing is copied into a document or attachment row, and a request body can set none of it.
/// </summary>
public class CommunicationServiceServerSendTests
{
    private static readonly Guid BatchId = Guid.Parse("15100000-0000-4000-8000-0000000000b1");
    private static readonly Guid FinanceTeam = Guid.Parse("15100000-0000-4000-8000-0000000000f1");
    private static readonly Guid UnitTeam = Guid.Parse("15100000-0000-4000-8000-0000000000a1");

    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("BatchId,InvoiceNumber\r\nb1,INV-A\r\n");
    private static readonly byte[] Json = Encoding.UTF8.GetBytes("""{"invoices":[{"invoiceNumber":"INV-A"}]}""");

    private static SendCommunicationRequest Request(ServerSendOptions? options = null) => new()
    {
        To = ["ap@example.com"],
        Subject = "AP export b1",
        Body = "Batch b1",
        BodyFormat = BodyFormat.PlainText,
        CommunicationType = CommunicationType.Email,
        SendMode = SendMode.User,
        IsInternalOnly = true,
        ServerSend = options ?? Options(),
    };

    private static ServerSendOptions Options(IReadOnlyList<ServerAttachment>? attachments = null) => new()
    {
        Attachments = attachments ?? [new ServerAttachment("ap-export-b1.csv", "text/csv", Csv), new ServerAttachment("ap-export-b1.json", "application/json", Json)],
        OwnerFollows = new ServerOwnerRecord("sprk_billingexport", BatchId, FinanceTeam),
        SkipEnrichment = true,
    };

    /// <summary>The batch's team for a question about the batch; the unit's default team for any other.</summary>
    private static RecordingUserEmailRig Rig(Sprk.Bff.Api.Services.Communication.Tracking.ITrackingTokenSigner? signer = null)
    {
        var rig = new RecordingUserEmailRig(signer);
        rig.Owners.AnswerFor = context => context.TargetEntityLogicalName == "sprk_billingexport" && context.TargetRecordId == BatchId
            ? RecordOwnerResolution.Owned(FinanceTeam)
            : RecordOwnerResolution.Owned(UnitTeam);
        return rig;
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // MUST FIRE
    // ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>MUTATION: not merging <c>ServerSend.Attachments</c> into the channel request fails the count and the byte comparison.</summary>
    [Fact(DisplayName = "151c-1a must-fire: the bytes reach the channel with the right name and content type")]
    public async Task ServerAttachments_ReachTheChannel_WithNameContentTypeAndBytes()
    {
        var rig = Rig();

        var response = await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());

        response.CommunicationId.Should().NotBeNull();
        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        sent.UserMode.Should().BeTrue();
        sent.Attachments.Should().NotBeNull();
        sent.Attachments!.Select(a => (a.Name, a.ContentType)).Should().Equal(
            ("ap-export-b1.csv", "text/csv"), ("ap-export-b1.json", "application/json"));
        sent.Attachments![0].Content.Should().Equal(Csv);
        sent.Attachments![1].Content.Should().Equal(Json);
    }

    /// <summary>
    /// MUTATION: dropping the <c>OwnerFollows</c> branch (the default <c>ForChild</c> + keep-creator question) leaves the record unowned
    /// (<c>ownerid</c> absent) and the resolver is never asked about the batch; asking <c>ForChild</c> with a parent lookup would not name the batch.
    /// </summary>
    [Fact(DisplayName = "151c-1a must-fire: the communication is owned by the team the resolver names for the BATCH, and the default owner is never asked for")]
    public async Task Owner_FollowsTheBatch_ThroughTheResolver_NeverTheDefault()
    {
        var rig = Rig();

        await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());

        var communication = rig.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        communication.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", FinanceTeam));
        communication.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(UnitTeam);
        var asked = rig.Owners.Requests.Where(c => c.TargetEntityLogicalName == "sprk_billingexport").ToList();
        asked.Should().ContainSingle();
        asked[0].TargetRecordId.Should().Be(BatchId);
        asked[0].KeepCreatorUnlessTargetIsTeamOwned.Should().BeTrue("a content row of the batch: its team, or a refusal, never the creator");
        asked[0].RequestedBy.Should().NotBeNull("the person who sent it is recorded on the row, like any app-created record");
        communication.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeTrue();
    }

    /// <summary>MUTATION: letting a user-owned (unchanged) answer through leaves the default owner on the record.</summary>
    [Theory(DisplayName = "151c-1a must-fire: an owner answer that is not a team (refused, or the creator kept) refuses BEFORE anything is sent or written")]
    [InlineData("refused")]
    [InlineData("unchanged")]
    public async Task OwnerNotATeam_RefusesBeforeTheSend(string answer)
    {
        var rig = Rig();
        rig.Owners.AnswerFor = _ => answer == "refused"
            ? RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "the batch could not be read")
            : RecordOwnerResolution.Unchanged("the batch is user-owned and unfiled");

        var act = () => rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.StatusCode.Should().Be(409);
        rig.Channel.Sent.Should().BeEmpty("nothing is sent");
        rig.Created.Should().BeEmpty("nothing is written");
    }

    /// <summary>MUTATION (uac-r2 F4): removing the expected-owner check files the email under the unit's default team instead of the batch's own team.</summary>
    [Fact(DisplayName = "151c-1a F4 SEEDED: a resolver answer that is not the followed record's owning team refuses BEFORE anything is sent or written")]
    public async Task OwnerAnswerNotTheRecordsTeam_RefusesBeforeTheSend()
    {
        var rig = Rig();
        rig.Owners.AnswerFor = _ => RecordOwnerResolution.Owned(UnitTeam); // the unit's default team, while FinanceTeam owns the batch

        var act = () => rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.StatusCode.Should().Be(409);
        failure.Code.Should().Be(ServerOwnerRecord.OwnerMismatchCode);
        rig.Channel.Sent.Should().BeEmpty("nothing is sent");
        rig.Created.Should().BeEmpty("nothing is written");
    }

    /// <summary>MUTATION (A8): gating enrichment on <c>SkipEnrichment</c> alone runs enrichment (and its category routing) for an owner-following send.</summary>
    [Fact(DisplayName = "151c-1a A8: a send whose owner follows a record is never enriched, even without SkipEnrichment")]
    public async Task OwnerFollows_IsNeverEnriched_EvenWithoutSkipEnrichment()
    {
        var rig = Rig();

        await rig.Service.SendAsync(Request(Options() with { SkipEnrichment = false }), RecordingUserEmailRig.UserContext());

        rig.Channel.Sent.Should().ContainSingle();
        rig.Enrichment.Verify(
            e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never, "category routing would re-own the communication");
    }

    /// <summary>MUTATION (D2 b): not passing the marker to the channel sends the server-only email without its header.</summary>
    [Fact(DisplayName = "151c-1a D2 b: a server-only send carries a marker signed for exactly its attachments; a normal send, or no signing key, carries none")]
    public async Task ServerSend_CarriesASignedMarkerBoundToItsAttachments()
    {
        var signer = new FixedKeyTrackingTokenSigner(FixedKeyTrackingTokenSigner.KeyA);
        var rig = Rig(signer);

        await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());
        await rig.Service.SendAsync(new SendCommunicationRequest
        {
            To = ["someone@example.com"], Subject = "s", Body = "b", CommunicationType = CommunicationType.Email, SendMode = SendMode.User,
        }, RecordingUserEmailRig.UserContext());

        rig.Channel.Sent.Should().HaveCount(2);
        var marker = rig.Channel.Sent[0].ServerSendMarker;
        marker.Should().NotBeNullOrEmpty();
        var files = rig.Channel.Sent[0].Attachments!.Select(a => ((string?)a.Name, (byte[]?)a.Content)).ToList();
        (await ServerSendMarker.IsVerifiedAsync(signer, [marker!], files, CancellationToken.None)).Should().BeTrue();
        rig.Channel.Sent[1].ServerSendMarker.Should().BeNull("a normal send carries no marker");

        var noKey = Rig(new FixedKeyTrackingTokenSigner(null));
        await noKey.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());
        noKey.Channel.Sent.Should().ContainSingle().Which.ServerSendMarker.Should().BeNull("no signing key, no marker; the send is not failed for it");
    }

    /// <summary>MUTATION: running enrichment for a server-only send calls the (strict) enrichment service, which also routes by category.</summary>
    [Fact(DisplayName = "151c-1a: a server-only send is not enriched; a normal send still is (regression pin)")]
    public async Task SkipEnrichment_IsHonoured_AndTheDefaultIsUnchanged()
    {
        var rig = Rig();

        await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());
        rig.Enrichment.Verify(
            e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never, "no AI indexing, triage, proposals or category routing for the AP export email");

        var normal = new SendCommunicationRequest
        {
            To = ["someone@example.com"], Subject = "s", Body = "b", CommunicationType = CommunicationType.Email, SendMode = SendMode.User,
        };
        await rig.Service.SendAsync(normal, RecordingUserEmailRig.UserContext());
        rig.Enrichment.Verify(
            e => e.EnrichAsync(It.IsAny<Guid>(), CommunicationDirection.Outgoing, It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The access invariant (uac-r2): the files go to the mail channel only. No <c>sprk_document</c>, <c>sprk_communicationattachment</c> or any other row
    /// carries them, and nothing is archived to SharePoint Embedded.
    /// </summary>
    [Fact(DisplayName = "151c-1a access: the bytes are never persisted as a document, an attachment row or an archive - the only rows written are the communication and its metadata")]
    public async Task Bytes_AreNeverPersisted()
    {
        var rig = Rig();

        await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());

        rig.Created.Select(e => e.LogicalName).Should().NotContain("sprk_document").And.NotContain("sprk_communicationattachment");
        rig.Created.Should().NotContain(e => e.Attributes.Values.Any(v => v is byte[]), "no row holds the file bytes");
        var communication = rig.Created.Single(e => e.LogicalName == "sprk_communication");
        communication.Contains("sprk_hasattachments").Should().BeFalse("no attachment rows exist for it");
        communication.Contains("sprk_attachmentcount").Should().BeFalse();
        communication.GetAttributeValue<string>("sprk_body").Should().Be("Batch b1");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // The path's limits, applied to the combined attachments
    // ---------------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "151c-1a: attachments over the path's 35 MB total are refused ATTACHMENT_LIMIT_EXCEEDED before anything is sent or written")]
    public async Task TotalOverTheLimit_IsRefused()
    {
        var rig = Rig();
        var big = new byte[(int)(CommunicationService.MaxTotalAttachmentSizeBytes / 2) + 1];
        var options = Options([new ServerAttachment("a.csv", "text/csv", big), new ServerAttachment("b.json", "application/json", big)]);

        var act = () => rig.Service.SendAsync(Request(options), RecordingUserEmailRig.UserContext());

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be("ATTACHMENT_LIMIT_EXCEEDED");
        rig.Channel.Sent.Should().BeEmpty();
        rig.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "151c-1a: a total exactly at the limit is sent")]
    public async Task TotalAtTheLimit_IsSent()
    {
        var rig = Rig();
        var half = new byte[(int)(CommunicationService.MaxTotalAttachmentSizeBytes / 2)];
        var options = Options([new ServerAttachment("a.csv", "text/csv", half), new ServerAttachment("b.json", "application/json", half)]);

        await rig.Service.SendAsync(Request(options), RecordingUserEmailRig.UserContext());

        rig.Channel.Sent.Should().ContainSingle();
    }

    [Fact(DisplayName = "151c-1a: more than the path's 150 attachments are refused")]
    public async Task CountOverTheLimit_IsRefused()
    {
        var rig = Rig();
        var many = Enumerable.Range(0, CommunicationService.MaxAttachmentCount + 1)
            .Select(i => new ServerAttachment($"f{i}.csv", "text/csv", [1])).ToList();

        var act = () => rig.Service.SendAsync(Request(Options(many)), RecordingUserEmailRig.UserContext());

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be("ATTACHMENT_LIMIT_EXCEEDED");
        rig.Channel.Sent.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Only the send-as-user email path accepts the options
    // ---------------------------------------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "151c-1a: the options on any other path (shared mailbox, message, archive, system mail) are refused before anything is sent or written")]
    [InlineData("shared-mailbox")]
    [InlineData("message")]
    [InlineData("archive")]
    [InlineData("system-mail")]
    public async Task OnAnyOtherPath_IsRefused(string path)
    {
        var rig = Rig();
        var request = path switch
        {
            "shared-mailbox" => Request() with { SendMode = SendMode.SharedMailbox },
            "message" => Request() with { CommunicationType = CommunicationType.Message },
            "archive" => Request() with { ArchiveToSpe = true },
            _ => Request() with { SystemMail = new SystemMailOptions { StoredBody = "x" } },
        };

        var act = () => rig.Service.SendAsync(request, RecordingUserEmailRig.UserContext());

        await act.Should().ThrowAsync<InvalidOperationException>();
        rig.Channel.Sent.Should().BeEmpty();
        rig.Created.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // MUST NOT FIRE: never bound from a request body
    // ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// MUTATION: making <see cref="SendCommunicationRequest.ServerSend"/> public AND removing its <c>[JsonIgnore]</c> binds the body's value and fails
    /// this test. Either change alone does not: System.Text.Json binds neither an internal property nor an ignored one, so the two guards back each other up.
    /// </summary>
    [Fact(DisplayName = "151c-1a must-not-fire: a JSON body that carries the field is deserialized with the field ignored")]
    public void ServerSend_IsNotBoundFromJson()
    {
        const string json = """
            {"to":["a@example.com"],"subject":"s","body":"b",
             "serverSend":{"attachments":[{"name":"x.csv","contentType":"text/csv","content":"AQID"}],"ownerFollows":{"entityLogicalName":"sprk_billingexport","recordId":"15100000-0000-4000-8000-0000000000b1"},"skipEnrichment":true},
             "ServerSend":{"Attachments":[{"Name":"x.csv","ContentType":"text/csv","Content":"AQID"}],"SkipEnrichment":true},
             "attachments":[{"name":"x.csv","contentType":"text/csv","content":"AQID"}]}
            """;

        var bound = JsonSerializer.Deserialize<SendCommunicationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        bound.ServerSend.Should().BeNull();
        JsonSerializer.Serialize(Request(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContain("serverSend", "the options are never written back out").And.NotContain("ap-export-b1");
    }

    /// <summary>A send that carries no options is exactly the send it was: nothing merged, the default owner question, enrichment on.</summary>
    [Fact(DisplayName = "151c-1a regression pin: a send without the options has no server attachments and asks the unchanged owner question")]
    public async Task WithoutTheOptions_TheSendIsUnchanged()
    {
        var rig = Rig();
        var request = new SendCommunicationRequest
        {
            To = ["someone@example.com"], Subject = "s", Body = "b", CommunicationType = CommunicationType.Email, SendMode = SendMode.User,
        };
        request.ServerSend.Should().BeNull();

        await rig.Service.SendAsync(request, RecordingUserEmailRig.UserContext());

        rig.Channel.Sent.Single().Attachments.Should().BeNull();
        rig.Owners.Requests.Should().NotContain(c => c.TargetEntityLogicalName == "sprk_billingexport");
        rig.Owners.Requests.Single().WhenUnfiled.Should().Be(UnfiledOwnership.KeepCreator);
    }
}
