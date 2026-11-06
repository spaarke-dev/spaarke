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

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Task 151c-2: the plain reference from the email to its AP export batch
    // ---------------------------------------------------------------------------------------------------------------------------------

    private const string BatchLink = "sprk_billingexport";

    private static bool CarriesAnyFiling(Entity communication) =>
        communication.Attributes.Keys.Any(k => k.StartsWith("sprk_regarding", StringComparison.OrdinalIgnoreCase));

    private static SendCommunicationRequest NormalRequest() => new()
    {
        To = ["someone@example.com"], Subject = "s", Body = "b", CommunicationType = CommunicationType.Email, SendMode = SendMode.User,
    };

    /// <summary>
    /// MUTATION: not writing <c>BillingExportId</c> in <c>BuildDataverseRecordForUserAsync</c> leaves the column off the row. The reference is in the
    /// SAME create as the row (one create, no follow-up update), the owner is still the batch's team, and nothing files the email.
    /// </summary>
    [Fact(DisplayName = "151c-2 must-fire: the send sets sprk_billingexport in the create; the owner is unchanged and the email is not filed")]
    public async Task BillingExport_IsSetInTheCreate_AndChangesNeitherOwnerNorFiling()
    {
        var rig = Rig();

        var response = await rig.Service.SendAsync(Request(Options() with { BillingExportId = BatchId }), RecordingUserEmailRig.UserContext());

        response.CommunicationId.Should().NotBeNull();
        var communication = rig.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        communication.GetAttributeValue<EntityReference>(BatchLink).Should().BeEquivalentTo(new EntityReference("sprk_billingexport", BatchId));
        rig.Created.Should().ContainSingle("one create, carrying the reference");
        rig.Entities.Verify(
            e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(d => d.ContainsKey(BatchLink)), It.IsAny<CancellationToken>()),
            Times.Never, "not a later update");
        communication.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", FinanceTeam));
        CarriesAnyFiling(communication).Should().BeFalse("a plain reference, not a filing: no sprk_regarding* column");
        communication.Contains("sprk_associationcount").Should().BeFalse();
        rig.Owners.Requests.Should().ContainSingle("the batch reference is not an ownership parent: the only owner question is the one about the batch")
            .Which.TargetRecordId.Should().Be(BatchId);
    }

    /// <summary>MUTATION: writing the column for every server send (not only when <c>BillingExportId</c> is set) puts it on this row.</summary>
    [Fact(DisplayName = "151c-2 must-not-fire: a server send without a batch id, and a normal send, create the row without the column")]
    public async Task WithoutABatchId_NoColumnIsWritten()
    {
        var rig = Rig();

        await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());
        await rig.Service.SendAsync(Request(Options() with { BillingExportId = Guid.Empty }), RecordingUserEmailRig.UserContext());
        await rig.Service.SendAsync(NormalRequest(), RecordingUserEmailRig.UserContext());

        rig.Created.Should().HaveCount(3).And.OnlyContain(e => e.LogicalName == "sprk_communication");
        rig.Created.Should().OnlyContain(e => !e.Contains(BatchLink));
    }

    /// <summary>
    /// MUTATION: making <see cref="SendCommunicationRequest.ServerSend"/> public AND removing its <c>[JsonIgnore]</c> binds the body's reference and fails
    /// this test (either change alone does not: System.Text.Json binds neither an internal property nor an ignored one).
    /// </summary>
    [Fact(DisplayName = "151c-2 must-not-fire: a JSON body cannot set the batch reference, under any spelling")]
    public void BillingExportId_IsNotBoundFromJson()
    {
        const string json = """
            {"to":["a@example.com"],"subject":"s","body":"b",
             "serverSend":{"billingExportId":"15100000-0000-4000-8000-0000000000b1"},
             "ServerSend":{"BillingExportId":"15100000-0000-4000-8000-0000000000b1"},
             "billingExportId":"15100000-0000-4000-8000-0000000000b1",
             "sprk_billingexport":"15100000-0000-4000-8000-0000000000b1",
             "sprk_billingexport@odata.bind":"/sprk_billingexports(15100000-0000-4000-8000-0000000000b1)"}
            """;

        var bound = JsonSerializer.Deserialize<SendCommunicationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        bound.ServerSend.Should().BeNull();
        JsonSerializer.Serialize(Request(Options() with { BillingExportId = BatchId }), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContain("serverSend").And.NotContain("15100000-0000-4000-8000-0000000000b1");
    }

    /// <summary>
    /// The chosen fallback (BFF deployed before the column): the create faults naming the column, the row is created once more WITHOUT it and the email
    /// is recorded. MUTATION: removing the retry loses the row (the best-effort step logs and moves on, the response has no communication id).
    /// </summary>
    [Fact(DisplayName = "151c-2 fallback: a create that faults because the column does not exist yet is retried without it, and the email is recorded")]
    public async Task MissingColumn_RecordsTheEmailWithoutTheLink()
    {
        var rig = Rig();
        var attempts = new List<bool>();
        rig.Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((entity, _) =>
            {
                attempts.Add(entity.Contains(BatchLink));
                rig.Created.Add(entity);
                if (entity.Contains(BatchLink))
                {
                    throw new InvalidOperationException(
                        "Failed to create sprk_communication record: 'sprk_communication' entity doesn't contain attribute with Name = 'sprk_billingexport' and NameMapping = 'Logical'.",
                        new Exception("'sprk_communication' entity doesn't contain attribute with Name = 'sprk_billingexport'"));
                }

                return Task.FromResult(Guid.NewGuid());
            });

        var response = await rig.Service.SendAsync(Request(Options() with { BillingExportId = BatchId }), RecordingUserEmailRig.UserContext());

        attempts.Should().Equal(true, false);
        response.CommunicationId.Should().NotBeNull("the email is recorded");
        rig.Channel.Sent.Should().ContainSingle("the email went once");
        var recorded = rig.Created[^1];
        recorded.Contains(BatchLink).Should().BeFalse();
        recorded.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", FinanceTeam), "the owner is untouched by the retry");
        recorded.GetAttributeValue<string>("sprk_subject").Should().Be("AP export b1");
    }

    /// <summary>MUTATION: retrying on any fault (or on a fault that does not name the column) would double-create on a fault the retry cannot fix.</summary>
    [Theory(DisplayName = "151c-2 must-not-fire: any other create fault is not retried (one attempt, the existing best-effort behaviour)")]
    [InlineData("The operation timed out")]
    [InlineData("Principal user is missing prvAppendToBillingExport privilege on sprk_billingexport")]
    [InlineData("'sprk_communication' entity doesn't contain attribute with Name = 'sprk_somethingelse'")]
    public async Task OtherCreateFaults_AreNotRetried(string fault)
    {
        var rig = Rig();
        var attempts = 0;
        rig.Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((_, _) =>
            {
                attempts++;
                throw new InvalidOperationException(fault);
            });

        var response = await rig.Service.SendAsync(Request(Options() with { BillingExportId = BatchId }), RecordingUserEmailRig.UserContext());

        attempts.Should().Be(1);
        rig.Channel.Sent.Should().ContainSingle("the email was sent; a record fault does not unsend or resend it");
        response.CommunicationId.Should().BeNull();
    }

    /// <summary>MUTATION: a fault on a row that does not carry the link (every other send) must never enter the retry, even when its text names the column.</summary>
    [Fact(DisplayName = "151c-2 must-not-fire: a normal send whose create faults is not retried")]
    public async Task NormalSend_CreateFault_IsNotRetried()
    {
        var rig = Rig();
        var attempts = 0;
        rig.Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((_, _) =>
            {
                attempts++;
                throw new InvalidOperationException("'sprk_communication' entity doesn't contain attribute with Name = 'sprk_billingexport'");
            });

        await rig.Service.SendAsync(NormalRequest(), RecordingUserEmailRig.UserContext());

        attempts.Should().Be(1);
    }

    [Theory(DisplayName = "151c-2: the missing-column classifier names the column and a missing-attribute shape, nothing else")]
    [InlineData("'sprk_communication' entity doesn't contain attribute with Name = 'sprk_billingexport' and NameMapping = 'Logical'.", true)]
    [InlineData("The entity 'sprk_communication' does not contain an attribute with the name sprk_billingexport", true)]
    [InlineData("Could not find a property named 'sprk_billingexport' on type 'Microsoft.Dynamics.CRM.sprk_communication'", true)]
    [InlineData("'sprk_communication' entity doesn't contain attribute with Name = 'sprk_other'", false)]
    [InlineData("sprk_billingexport: access denied", false)]
    [InlineData("", false)]
    public void MissingColumnClassifier(string message, bool expected)
    {
        CommunicationService.IsMissingColumnFault(new InvalidOperationException("wrapper", new Exception(message)), BatchLink).Should().Be(expected);
        CommunicationService.IsMissingColumnFault(null, BatchLink).Should().BeFalse();
    }

    [Fact(DisplayName = "151c-2 K3: the typed Dataverse fault 0x80041103 naming the column is a missing column, in any language; the same code for another column, or another code, is not")]
    public void MissingColumnClassifier_TypedFault()
    {
        static Exception Fault(int code, string reason) =>
            new InvalidOperationException("wrapper", new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = code, Message = reason }, new System.ServiceModel.FaultReason(reason)));

        const int noAttribute = unchecked((int)0x80041103);

        // A localized message: no English phrase, only the code and the column name.
        CommunicationService.IsMissingColumnFault(Fault(noAttribute, "Das Attribut sprk_billingexport ist nicht vorhanden."), BatchLink).Should().BeTrue();
        // Same code, a different column: a retry without THIS column would not fix it.
        CommunicationService.IsMissingColumnFault(Fault(noAttribute, "Das Attribut sprk_other ist nicht vorhanden."), BatchLink).Should().BeFalse();
        // Another code (object does not exist), column named, no missing-attribute phrase.
        CommunicationService.IsMissingColumnFault(Fault(unchecked((int)0x80040217), "sprk_billingexport ist nicht vorhanden."), BatchLink).Should().BeFalse();
    }

    /// <summary>MUTATION: removing the agreement check lets a send be owned like one batch and linked to another.</summary>
    [Theory(DisplayName = "151c-2 K5: a server send whose batch reference and followed owner record differ is refused (409) BEFORE anything is sent or written")]
    [InlineData("other-batch")]
    [InlineData("other-table")]
    public async Task BatchAndOwnerRecordDisagree_RefusesBeforeTheSend(string kind)
    {
        var rig = Rig();
        var options = kind == "other-batch"
            ? Options() with { BillingExportId = Guid.Parse("15100000-0000-4000-8000-0000000000b2") }
            : Options() with { BillingExportId = BatchId, OwnerFollows = new ServerOwnerRecord("sprk_invoice", BatchId, FinanceTeam) };

        var act = () => rig.Service.SendAsync(Request(options), RecordingUserEmailRig.UserContext());

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.StatusCode.Should().Be(409);
        failure.Code.Should().Be(ServerSendOptions.BatchMismatchCode);
        rig.Channel.Sent.Should().BeEmpty("nothing is sent");
        rig.Created.Should().BeEmpty("nothing is written");
        rig.Owners.Requests.Should().BeEmpty("refused before the owner is asked");
    }

    [Fact(DisplayName = "151c-2 K5 must-not-fire: the same batch in both, the link alone, and the owner alone are accepted")]
    public async Task BatchAndOwnerRecordAgree_AreAccepted()
    {
        var rig = Rig();

        await rig.Service.SendAsync(Request(Options() with { BillingExportId = BatchId }), RecordingUserEmailRig.UserContext());
        await rig.Service.SendAsync(Request(Options() with { BillingExportId = BatchId, OwnerFollows = null }), RecordingUserEmailRig.UserContext());
        await rig.Service.SendAsync(Request(), RecordingUserEmailRig.UserContext());

        rig.Channel.Sent.Should().HaveCount(3);
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
