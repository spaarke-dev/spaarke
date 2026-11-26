using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// ISS-010 part E (#3184): <see cref="SendCommunicationRequest.DeferPostSend"/> moves a decision email's post-send work (thread, .eml
/// archive, enrichment, participant index, communication-arrived) out of the request into the <see cref="CommunicationPostSendJobHandler"/>
/// job. The send, the <c>sprk_communication</c> create and the response's "sent" stay in the request; a send without the flag is unchanged.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4), as <see cref="OutgoingFiledEmailArchiveTests"/>: the channel sender seam, the
/// <see cref="SpeFileStore"/> facade's upload, the Dataverse writer (<see cref="IGenericEntityService"/>, stateful: a created row answers
/// its reads), the thread resolver, the enrichment service, the job queue and the idempotency cache. The participant indexer is the real one.
/// </remarks>
public class CommunicationPostSendTests
{
    private const string ArchiveContainerId = "drive-archive";
    private const string SharedMailbox = "noreply@contoso.com";
    private const string UserMailbox = "author@contoso.com";
    private const int EmailArchiveSourceType = 659490003;

    private static readonly Guid MatterId = Guid.Parse("a1a1a1a1-0000-0000-0000-0000000000e1");

    private readonly ITestOutputHelper _output;

    public CommunicationPostSendTests(ITestOutputHelper output) => _output = output;

    // ── The profile: what an Assign Task email costs the commit request ────────────────────────────────────────

    [Fact]
    public async Task AssignTaskEmail_Deferred_TheRequestMakesFewerDataverseCalls_AndNoAiCall_AndQueuesOneJob()
    {
        var before = Build();
        await before.Sut.SendAsync(AssignTaskShape(defer: false), UserContext());
        var after = Build();
        await after.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());

        _output.WriteLine($"in-request Dataverse calls: before {before.DataverseCalls}, after {after.DataverseCalls}");
        _output.WriteLine($"in-request enrichment (AI) calls: before {before.EnrichCalls}, after {after.EnrichCalls}");
        _output.WriteLine($"in-request SPE uploads: before {before.UploadPaths.Count}, after {after.UploadPaths.Count}");
        _output.WriteLine($"in-request thread resolves: before {before.ThreadCalls.Count}, after {after.ThreadCalls.Count}");

        before.EnrichCalls.Should().Be(1, "today the email is enriched (triage and propose: the AI) inside the request");
        after.EnrichCalls.Should().Be(0, "the deferred send leaves enrichment to the job");
        before.UploadPaths.Should().ContainSingle();
        after.UploadPaths.Should().BeEmpty("the .eml archive moved to the job");
        before.ThreadCalls.Should().ContainSingle();
        after.ThreadCalls.Should().BeEmpty("the thread moved to the job");
        after.DataverseCalls.Should().BeLessThan(before.DataverseCalls);
        after.PostSendJobs.Should().ContainSingle();
        before.PostSendJobs.Should().BeEmpty();

        // What stays: the row, filed and carrying its correlation id (AlreadySentAsync's key).
        var row = after.Communications.Should().ContainSingle().Subject.Entity;
        row.GetAttributeValue<string>("sprk_correlationid").Should().Be("assign-task-corr");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        row.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeTrue();
    }

    // ── Must not fire: the flag off is today's behaviour ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task FlagOff_EverythingRunsInline_AsBefore_AndNoPostSendJobIsQueued(SendMode mode)
    {
        var h = Build();

        var response = await h.Sut.SendAsync(AssignTaskShape(defer: false) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null);

        h.PostSendJobs.Should().BeEmpty();
        h.ThreadCalls.Should().ContainSingle();
        h.EnrichCalls.Should().Be(1);
        h.ParticipantRows.Should().NotBeEmpty();
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        response.ArchivedDocumentId.Should().Be(eml.Id);
        h.Jobs.Select(j => j.SubjectId).Should().Contain(eml.Id.ToString(), "the archived .eml is profiled, as before");
    }

    // ── The response says "sent", never "archived", before the job runs ────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task Deferred_TheResponseSaysSent_NeverArchived_AndOneJobCarriesIdsOnly(SendMode mode)
    {
        var h = Build();
        var request = AssignTaskShape(defer: true) with { SendMode = mode, Bcc = ["hidden@example.com"] };

        var response = await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        response.Status.Should().Be(CommunicationStatus.Send);
        response.CommunicationId.Should().Be(h.Communications.Single().Id);
        response.ArchivedDocumentId.Should().BeNull("nothing is archived before the job runs");
        response.ArchivalWarning.Should().BeNull();
        h.ArchivedEmls.Should().BeEmpty();
        h.ParticipantRows.Should().BeEmpty("the participant index moved to the job");

        var job = h.PostSendJobs.Should().ContainSingle().Subject;
        job.IdempotencyKey.Should().Be($"comm-postsend:{response.CommunicationId:N}");
        job.CorrelationId.Should().Be("assign-task-corr");
        job.MaxAttempts.Should().Be(JobContract.MaxDeliveryCount);
        var payload = job.Payload!.RootElement.GetRawText();
        payload.Should().NotContain("recipient@example.com").And.NotContain("hidden@example.com").And.NotContain("Status update");
        var read = job.Payload.Deserialize<CommunicationPostSendPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        read.Archive.Should().BeTrue("the row is filed to a record (D-217)");
        read.Enrich.Should().BeTrue();
        read.SenderSystemUserId.Should().Be(
            h.Communications.Single().Entity.GetAttributeValue<EntityReference>("sprk_sentby")?.Id,
            "ontology-r1 condition (b): the systemuser the send resolved (none for a shared-mailbox send without a caller)");
    }

    // ── A failed send still fails, with nothing queued ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task Deferred_AFailedSend_StillThrowsTheSendError_AndQueuesNothing(SendMode mode)
    {
        var h = Build(sendFault: new SdapProblemException("GRAPH_SEND_FAILED", "Email Send Failed", "graph 500", 502));

        var act = () => h.Sut.SendAsync(AssignTaskShape(defer: true) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be("GRAPH_SEND_FAILED");
        h.Jobs.Should().BeEmpty();
        h.Communications.Should().BeEmpty();
    }

    // ── The guard ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deferred_OnAMessage_OrIntoAThread_IsRefusedBeforeAnythingIsSent()
    {
        var h = Build();

        await FluentActions.Invoking(() => h.Sut.SendAsync(AssignTaskShape(defer: true) with { CommunicationType = CommunicationType.Message }, UserContext()))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => h.Sut.SendAsync(AssignTaskShape(defer: true) with { ThreadId = Guid.NewGuid() }, UserContext()))
            .Should().ThrowAsync<InvalidOperationException>();

        h.SendCalls.Should().Be(0);
        h.Created.Should().BeEmpty();
    }

    // ── The job: once per communication ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task TheJob_RunTwice_MakesOneDocument_OneThread_AndEnrichesOnce(SendMode mode)
    {
        var h = Build();
        var response = await h.Sut.SendAsync(AssignTaskShape(defer: true) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null);
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        var first = await handler.ProcessAsync(job, CancellationToken.None);
        h.Idempotency.Forget(); // the cache lost every entry (it fails open, #984): only the durable guards remain
        var second = await handler.ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Completed);
        second.Status.Should().Be(JobStatus.Completed);
        var eml = h.ArchivedEmls.Should().ContainSingle("ArchiveExistingAsync finds the first run's Document").Subject;
        eml.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(response.CommunicationId!.Value);
        h.UploadPaths.Should().ContainSingle();
        h.ThreadCalls.Should().ContainSingle("the second run sees the row's thread");
        h.EnrichCalls.Should().Be(1, "the second run sees the first run's review row");
        h.EnrichArchiveIds.Should().Equal(eml.Id);
        h.Jobs.Count(j => j.SubjectId == eml.Id.ToString()).Should().Be(1, "the .eml is profiled once");
    }

    [Fact]
    public async Task TheJob_RedeliveredWithItsMarkers_DoesNothingAgain_AndEmitsArrivedOnce()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        await handler.ProcessAsync(job, CancellationToken.None);
        var participantRows = h.ParticipantRows.Count;
        var calls = h.DataverseCalls;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        handler.Arrived.Should().Be(1);
        h.ParticipantRows.Count.Should().Be(participantRows).And.BeGreaterThan(0);
        h.DataverseCalls.Should().Be(calls, "a done job reads nothing");
    }

    [Fact]
    public async Task TheJob_CreatesTheThreadPrivate_WhenTheSendAskedForIt()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true) with { CreateThreadPrivate = true }, UserContext());

        await h.Handler().ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None);

        h.ThreadCalls.Should().ContainSingle().Which.CreatePrivate.Should().BeTrue();
    }

    [Fact]
    public async Task TheJob_PlainTextWithBcc_ArchivesTheSentFormatAndBcc()
    {
        // Q2: the job's .eml of a PlainText + Bcc inquiry carries both, as the send-time archive did.
        var h = Build();
        await h.Sut.SendAsync(
            AssignTaskShape(defer: true) with { BodyFormat = BodyFormat.PlainText, Body = "Plain body.", Bcc = ["hidden@example.com"] },
            UserContext());

        await h.Handler().ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None);

        var eml = Encoding.UTF8.GetString(h.UploadedBytes.Should().ContainSingle().Subject);
        eml.Should().Contain("hidden@example.com");
        eml.Should().Contain("text/plain");
    }

    [Fact]
    public async Task TheJob_ARowThatNoLongerExists_IsDone()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        h.Rows.Clear();

        (await h.Handler().ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        h.ArchivedEmls.Should().BeEmpty();
        h.EnrichCalls.Should().Be(0);
    }

    [Fact]
    public async Task TheJob_AnArchiveFaultBeforeTheFinalDelivery_IsRetried_WithoutEnrichment_ThenCompletes()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        h.UploadFails = true;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);
        h.EnrichCalls.Should().Be(0, "RAG indexing needs the document; enrichment waits for the retry");
        handler.Arrived.Should().Be(1, "arrived does not wait for the archive");

        h.UploadFails = false;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        h.ArchivedEmls.Should().ContainSingle();
        h.EnrichCalls.Should().Be(1);
        handler.Arrived.Should().Be(1);
    }

    // ── The final archive failure bells the sender, once ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheJob_ArchiveFailsOnItsFinalDelivery_BellsTheSenderOnce_WithALinkToTheEmail()
    {
        var h = Build();
        var response = await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var id = response.CommunicationId!.Value;
        var sentBy = h.Rows[id].GetAttributeValue<EntityReference>("sprk_sentby").Id;
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        var handler = h.Handler();
        h.UploadFails = true;

        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed, "a re-drive of the dead job");

        var bell = h.Bells.Should().ContainSingle("the :archive-bell marker keeps a re-drive from a second bell").Subject.Entity;
        bell.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("systemuser", sentBy), "the sender, never the owning team");
        bell.GetAttributeValue<string>("title").Should().Be("An email you sent was not saved to SharePoint");
        var data = JsonDocument.Parse(bell.GetAttributeValue<string>("data")).RootElement;
        data.GetProperty("actionUrl").GetString().Should().Be($"/main.aspx?etn=sprk_communication&id={id:D}&pagetype=entityrecord");
        data.GetProperty("regardingId").GetString().Should().Be(id.ToString());
        h.Probe.Asked.Should().ContainSingle().Which.Should().Be((sentBy, "sprk_communications", id));
        h.ArchivedEmls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheJob_ArchiveFailsBeforeItsFinalDelivery_DoesNotBell()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount - 1;
        h.UploadFails = true;

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        h.Bells.Should().BeEmpty("the archive is retried");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheJob_TheSenderCannotOpenTheEmail_TheBellNamesNoRecord(bool dataverseDidNotAnswer)
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        h.Probe.Rights = dataverseDidNotAnswer ? null : AccessRights.Read; // Read without AppendTo is not enough to save it
        h.UploadFails = true;

        await h.Handler().ProcessAsync(job, CancellationToken.None);

        var bell = h.Bells.Should().ContainSingle().Subject.Entity;
        bell.Contains("data").Should().BeFalse("no link and no regarding record when the recipient cannot open it (D-123)");
        bell.GetAttributeValue<string>("body").Should().Be("Ask your administrator to save it.");
    }

    [Fact]
    public async Task TheJob_AnArchiveThatCanNeverSucceed_BellsAtOnce()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single(); // first delivery
        h.UploadFault = new InvalidOperationException("ArchiveContainerId not configured for SPE archival");

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);

        h.Bells.Should().ContainSingle();
    }

    [Fact]
    public async Task TheJob_ASharedMailboxSendWithNoSender_RaisesNoBell()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true) with { SendMode = SendMode.SharedMailbox }, null);
        h.Communications.Single().Entity.Contains("sprk_sentby").Should().BeFalse("the precondition: no sender to bell");
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        h.UploadFails = true;

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        h.Bells.Should().BeEmpty("never the owning team");
        // ontology-r1 condition (a): one stable, queryable Error naming the communication and the correlation id.
        var id = h.Communications.Single().Id;
        h.HandlerLog.At(LogLevel.Error).Should().ContainSingle(m => m.StartsWith("[COMM-POSTSEND] Archive bell not raised: no sender for communication"))
            .Which.Should().Contain(id.ToString()).And.Contain("assign-task-corr");
    }

    [Fact]
    public async Task TheJob_TheRowNamesNoSender_BellsTheSenderTheRequestResolved()
    {
        // ontology-r1 condition (b): the payload carries the systemuser the send resolved; the row's empty sprk_sentby falls back to it.
        var h = Build();
        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext())).CommunicationId!.Value;
        var sender = h.Rows[id].GetAttributeValue<EntityReference>("sprk_sentby").Id;
        h.Rows[id].Attributes.Remove("sprk_sentby");
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        h.UploadFails = true;
        var handler = h.Handler();

        await handler.ProcessAsync(job, CancellationToken.None);
        await handler.ProcessAsync(job, CancellationToken.None);

        h.Bells.Should().ContainSingle().Which.Entity.GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", sender));
        h.Probe.Asked.Should().ContainSingle().Which.Should().Be((sender, "sprk_communications", id), "the same D-123 rights check on the link");
        h.HandlerLog.At(LogLevel.Error).Should().NotContain(m => m.Contains("Archive bell not raised"));
    }

    // ── Run 3 (review F1): a fault outside the archive on the final delivery keeps the one bell ──────────────

    public static TheoryData<string> FaultsOutsideTheArchive => new() { "review-row query", "arrived emit", "row read" };

    [Theory]
    [MemberData(nameof(FaultsOutsideTheArchive))]
    public async Task TheJob_FinalDelivery_TheArchiveFailed_ThenAnotherStepFaults_StillBellsOnce_AndLogsOneError(string fault)
    {
        var h = Build();
        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext())).CommunicationId!.Value;
        var sender = h.Rows[id].GetAttributeValue<EntityReference>("sprk_sentby").Id;
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        h.UploadFails = true;
        var handler = h.Handler();
        var boom = new TimeoutException("dataverse");
        switch (fault)
        {
            case "review-row query": h.ReviewQueryFault = boom; break;
            case "arrived emit": handler.ArrivedFault = boom; break;
            default: h.RowReadFault = boom; break; // the row is never read: the bell goes to the payload's sender
        }

        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);
        var errors = h.HandlerLog.At(LogLevel.Error);
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed, "a re-drive of the dead job");

        h.Bells.Should().ContainSingle("the final delivery's bell is not lost, and a re-drive adds none")
            .Which.Entity.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("systemuser", sender));
        // The review-row and row-read faults: one Error (the archive's, or the final delivery's). The arrived fault: the archive's final
        // Error, then the final delivery's for the step that faulted after it.
        errors.Should().HaveCount(fault == "arrived emit" ? 2 : 1).And.OnlyContain(m => m.Contains(id.ToString()));
        h.HandlerLog.At(LogLevel.Warning).Should().NotContain(m => m.Contains("it will be redelivered") && m.Contains("post-send work"),
            "the final delivery is never redelivered");
    }

    [Fact]
    public async Task TheJob_FinalDelivery_AFaultAfterASuccessfulArchive_LogsAnError_AndDoesNotBell()
    {
        var h = Build();
        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        var handler = h.Handler();
        handler.ArrivedFault = new TimeoutException("outbox");

        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        h.ArchivedEmls.Should().ContainSingle();
        h.Bells.Should().BeEmpty("the email was saved");
        h.HandlerLog.At(LogLevel.Error).Should().ContainSingle(m => m.Contains("failed on its final delivery") && m.Contains("assign-task-corr"));
    }

    // ── Run 3 (K2): a 409 archive_in_progress on the final delivery ───────────────────────────────────────────────

    [Fact]
    public async Task TheJob_FinalDelivery_409ArchiveInProgress_ButTheOtherArchiveFinished_IsArchived_AndDoesNotBell()
    {
        var h = Build();
        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext())).CommunicationId!.Value;
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        var lockKey = CommunicationService.ArchiveLockKeyFor(id);
        h.Idempotency.Hold(lockKey).Should().BeTrue("another archive (the on-demand route) holds the lock");
        var otherDocument = Guid.NewGuid();
        h.Idempotency.OnLockRefused = key =>
        {
            // The other archive creates its Document just after this job's lock is refused.
            if (key == lockKey)
                lock (h.Created)
                    h.Created.Add((new Entity("sprk_document")
                    {
                        ["sprk_isemailarchive"] = true,
                        ["sprk_sourcetype"] = new OptionSetValue(EmailArchiveSourceType),
                        ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", id),
                    }, otherDocument));
        };

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.Bells.Should().BeEmpty("the .eml is saved: no false \"not saved\"");
        h.EnrichArchiveIds.Should().Equal(otherDocument);
        h.ArchivedEmls.Should().ContainSingle();
    }

    [Fact]
    public async Task TheJob_FinalDelivery_409ArchiveInProgress_AndNoDocument_Bells()
    {
        var h = Build();
        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext())).CommunicationId!.Value;
        var job = h.PostSendJobs.Single();
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        h.Idempotency.Hold(CommunicationService.ArchiveLockKeyFor(id)).Should().BeTrue();

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        h.Bells.Should().ContainSingle();
        h.ArchivedEmls.Should().BeEmpty();
    }

    // ── Q3: one .eml archive at a time ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_WhileAnotherArchiveHoldsTheLock_TheRouteAnswers409_TheJobRetries_ThenOneDocument()
    {
        var h = Build();
        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext())).CommunicationId!.Value;
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();
        var lockKey = CommunicationService.ArchiveLockKeyFor(id);
        h.Idempotency.Hold(lockKey).Should().BeTrue();

        var refused = await FluentActions.Invoking(() => h.Sut.ArchiveExistingAsync(id, CancellationToken.None))
            .Should().ThrowAsync<SdapProblemException>();
        refused.Which.StatusCode.Should().Be(409);
        refused.Which.Code.Should().Be(CommunicationService.ArchiveInProgressCode);
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed, "archive_in_progress is retried, not poisoned");

        await h.Idempotency.ReleaseProcessingLockAsync(lockKey);
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        (await h.Sut.ArchiveExistingAsync(id, CancellationToken.None)).AlreadyArchived.Should().BeTrue();

        h.ArchivedEmls.Should().ContainSingle();
        h.Idempotency.IsLocked(lockKey).Should().BeFalse("each archive releases its lock");
    }

    [Fact]
    public async Task Archive_TheRouteAndTheJobAtOnce_WithTheLockFailingOpen_LeaveOneDocument_AndLogTheDuplicateAtError()
    {
        var h = Build();
        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext())).CommunicationId!.Value;
        var job = h.PostSendJobs.Single();
        h.Idempotency.LocksFailOpen = true; // the cache is down (#984): both archives get past the lock

        // Both archives reach the upload; the first then creates its Document, and the second goes on only once it exists.
        var arrivals = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.UploadGate = async () =>
        {
            var n = Interlocked.Increment(ref arrivals);
            if (n == 2) both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (n == 2) await WaitUntilAsync(() => h.ArchivedEmls.Count > 0);
        };

        var route = Task.Run(() => h.Sut.ArchiveExistingAsync(id, CancellationToken.None));
        var run = Task.Run(() => h.Handler().ProcessAsync(job, CancellationToken.None));
        await Task.WhenAll(route, run);

        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        (await route).ArchiveDocumentId.Should().Be(eml.Id);
        (await run).Status.Should().Be(JobStatus.Completed);
        h.Log.At(LogLevel.Error).Should().ContainSingle(m => m.Contains("Duplicate .eml archive detected") && m.Contains(id.ToString()));
    }

    // ── Q4: the queue fails or is slow, so the tail runs inline ───────────────────────────────────────────────

    public static TheoryData<string> QueueFaults => new() { "throws", "hangs" };

    [Theory]
    [MemberData(nameof(QueueFaults))]
    public async Task Deferred_TheQueueFails_TheTailRunsInline_OneWarningNamesTheEmail_AndTheResponseIsUnchanged(string fault)
    {
        Func<CancellationToken, Task> submit = fault == "throws"
            ? _ => throw new ServiceBusException("queue down", ServiceBusFailureReason.ServiceCommunicationProblem)
            : ct => Task.Delay(Timeout.Infinite, ct);
        var inline = Build();
        var expected = await inline.Sut.SendAsync(AssignTaskShape(defer: false), UserContext());
        var h = Build(postSendSubmit: submit);

        var response = await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());

        var id = response.CommunicationId!.Value;
        h.PostSendJobs.Should().BeEmpty();
        h.ThreadCalls.Should().ContainSingle();
        h.EnrichCalls.Should().Be(1);
        h.ParticipantRows.Should().NotBeEmpty();
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        response.ArchivedDocumentId.Should().Be(eml.Id);
        response.ArchivalWarning.Should().Be(expected.ArchivalWarning).And.BeNull();
        response.Status.Should().Be(expected.Status);
        expected.ArchivedDocumentId.Should().NotBeNull();
        h.Warnings.Should().HaveCount(inline.Warnings.Count + 1);
        h.Warnings.Should().ContainSingle(w => w.Contains("could not be queued")).Which.Should().Contain(id.ToString());
    }

    [Fact]
    public async Task Deferred_TheInlineFallbackRan_ThenTheLateJobLanded_ItIsANoOp()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = Build(postSendSubmit: _ => release.Task); // slower than 2 s, then it lands anyway

        await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext());
        h.EnrichCalls.Should().Be(1, "the precondition: the fallback ran the tail");
        release.SetResult();
        await WaitUntilAsync(() => h.PostSendJobs.Count == 1);
        var handler = h.Handler();

        (await handler.ProcessAsync(h.PostSendJobs.Single(), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        handler.Arrived.Should().Be(0, "the fallback already emitted arrived");
        h.EnrichCalls.Should().Be(1);
        h.ArchivedEmls.Should().ContainSingle();
        h.ThreadCalls.Should().ContainSingle();
    }

    // ── Run 3 (review F1): the caller's token fires after the send, during the enqueue ────────────────────────────

    public static TheoryData<string, SendMode> CancelDuringEnqueue => new()
    {
        { "queued", SendMode.User }, { "queued", SendMode.SharedMailbox },
        { "slow queue", SendMode.User }, { "slow queue", SendMode.SharedMailbox },
    };

    [Theory]
    [MemberData(nameof(CancelDuringEnqueue))]
    public async Task Deferred_TheCallersTokenFiresDuringTheEnqueue_TheSentEmailIsNotAFailure_AndItsTailRuns(string queue, SendMode mode)
    {
        // AssignTaskNotifier sends under a 10 s linked token and falls back to a second (system-mailbox) email when SendAsync throws.
        // The token firing AFTER Graph sent must not turn the delivered email into GRAPH_SEND_FAILED.
        using var caller = new CancellationTokenSource();
        var h = Build(postSendSubmit: async submitToken =>
        {
            caller.Cancel();
            if (queue == "queued")
                await Task.Delay(50, submitToken); // the queue answers: the submit is not bound to the caller's token
            else
                await Task.Delay(Timeout.Infinite, submitToken); // the queue does not answer within the 2 s bound
        });

        var response = await h.Sut.SendAsync(
            AssignTaskShape(defer: true) with { SendMode = mode }, mode == SendMode.User ? UserContext() : null, caller.Token);

        caller.IsCancellationRequested.Should().BeTrue("the precondition: the caller's token fired during the enqueue");
        response.Status.Should().Be(CommunicationStatus.Send);
        h.SendCalls.Should().Be(1);
        var id = response.CommunicationId!.Value;
        if (queue == "queued")
        {
            h.PostSendJobs.Should().ContainSingle().Which.SubjectId.Should().Be(id.ToString());
        }
        else
        {
            h.PostSendJobs.Should().BeEmpty();
            h.EnrichCalls.Should().Be(1, "the inline fallback ran the tail");
            (await h.Idempotency.IsEventProcessedAsync(CommunicationPostSendJobHandler.KeyFor(id))).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Deferred_TheQueueFails_AndTheCallersTokenFires_TheInlineFallbackStillLeavesTheJobsMarkers()
    {
        using var caller = new CancellationTokenSource();
        var h = Build(postSendSubmit: _ =>
        {
            caller.Cancel();
            throw new ServiceBusException("queue down", ServiceBusFailureReason.ServiceCommunicationProblem);
        });

        var id = (await h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext(), caller.Token)).CommunicationId!.Value;

        foreach (var marker in new[]
                 {
                     CommunicationPostSendJobHandler.KeyFor(id), CommunicationPostSendJobHandler.EnrichedKeyFor(id),
                     CommunicationPostSendJobHandler.ArrivedKeyFor(id),
                 })
            (await h.Idempotency.IsEventProcessedAsync(marker)).Should().BeTrue(marker);
        h.Idempotency.IsLocked(CommunicationPostSendJobHandler.KeyFor(id)).Should().BeFalse("the fallback released the job's lock");
        h.SendCalls.Should().Be(1);
    }

    // ── K1: the REAL owner-based lock, with the job running while the inline fallback runs ────────────────────────

    [Fact]
    public async Task RealCache_TheLateJobRunsWhileTheInlineFallbackRuns_IsRefused_ThenANoOp_OneDocument()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = Build(postSendSubmit: _ => release.Task, realCache: true); // slower than 2 s, then it lands anyway
        var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.UploadGate = async () =>
        {
            uploading.TrySetResult();
            await finishUpload.Task.WaitAsync(TimeSpan.FromSeconds(30));
        };

        // The fallback claims the job's lock and stops in the .eml upload.
        var send = Task.Run(() => h.Sut.SendAsync(AssignTaskShape(defer: true), UserContext()));
        await uploading.Task.WaitAsync(TimeSpan.FromSeconds(30));
        release.SetResult();
        await WaitUntilAsync(() => h.PostSendJobs.Count == 1);
        var job = h.PostSendJobs.Single();
        var handler = h.Handler();

        var during = await handler.ProcessAsync(job, CancellationToken.None);
        finishUpload.SetResult();
        var response = await send;
        var after = await handler.ProcessAsync(job, CancellationToken.None);

        during.Status.Should().Be(JobStatus.Failed, "the fallback holds the lock (another owner)");
        during.ErrorMessage.Should().Be(CommunicationPostSendJobHandler.LockRefusedError);
        after.Status.Should().Be(JobStatus.Completed, "the fallback left the done marker and released its lock");
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        response.ArchivedDocumentId.Should().Be(eml.Id);
        h.EnrichCalls.Should().Be(1);
        h.ThreadCalls.Should().ContainSingle();
        handler.Arrived.Should().Be(0, "the fallback emitted arrived");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in 10 s");
            await Task.Delay(10);
        }
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static SendCommunicationRequest AssignTaskShape(bool defer) => new()
    {
        To = ["recipient@example.com"],
        Subject = "Status update",
        Body = "<p>Hello.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        SendMode = SendMode.User,
        CorrelationId = "assign-task-corr",
        IsInternalOnly = true,
        Associations = [new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId }],
        DeferPostSend = defer,
    };

    private static HttpContext UserContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", UserMailbox),
            new Claim("oid", Guid.NewGuid().ToString()),
        }, "Test")),
    };

    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required Mock<IGenericEntityService> Entities { get; init; }
        public required Mock<ICommunicationEnrichmentService> Enrichment { get; init; }
        public required List<(Entity Entity, Guid Id)> Created { get; init; }
        public required Dictionary<Guid, Entity> Rows { get; init; }
        public required List<string> UploadPaths { get; init; }
        public required List<byte[]> UploadedBytes { get; init; }
        public required List<JobContract> Jobs { get; init; }
        public required List<ThreadResolutionRequest> ThreadCalls { get; init; }
        public required InMemoryIdempotency Idempotency { get; init; }

        /// <summary>The cache the send path and the job use: <see cref="Idempotency"/>, or the REAL <see cref="IdempotencyService"/>.</summary>
        public required IIdempotencyService Cache { get; init; }
        public required Func<int> SendCount { get; init; }
        public required ListLogger<CommunicationService> Log { get; init; }
        public required ScriptedProbe Probe { get; init; }
        public bool UploadFails { get; set; }

        /// <summary>Thrown by every SPE upload when set (and <see cref="UploadFails"/> is false).</summary>
        public Exception? UploadFault { get; set; }

        /// <summary>Awaited at the start of every SPE upload (null: none); the race tests hold uploads here.</summary>
        public Func<Task>? UploadGate { get; set; }

        /// <summary>Thrown by the <c>sprk_emailreviewlog</c> query (enrichment's re-run guard) when set.</summary>
        public Exception? ReviewQueryFault { get; set; }

        /// <summary>Thrown by every <c>sprk_communication</c> read when set.</summary>
        public Exception? RowReadFault { get; set; }

        /// <summary>The job handler's log lines.</summary>
        public ListLogger<CommunicationPostSendJobHandler> HandlerLog { get; } = new();

        public int SendCalls => SendCount();
        public List<(Entity Entity, Guid Id)> Bells { get { lock (Created) return Created.Where(c => c.Entity.LogicalName == "appnotification").ToList(); } }
        public int DataverseCalls => Entities.Invocations.Count;
        public int EnrichCalls => Enrichment.Invocations.Count(i => i.Method.Name == nameof(ICommunicationEnrichmentService.EnrichAsync));
        public List<Guid?> EnrichArchiveIds => Enrichment.Invocations
            .Where(i => i.Method.Name == nameof(ICommunicationEnrichmentService.EnrichAsync)).Select(i => (Guid?)i.Arguments[3]).ToList();

        public List<(Entity Entity, Guid Id)> Communications { get { lock (Created) return Created.Where(c => c.Entity.LogicalName == "sprk_communication").ToList(); } }
        public List<(Entity Entity, Guid Id)> ParticipantRows { get { lock (Created) return Created.Where(c => c.Entity.LogicalName == "sprk_communicationparticipant").ToList(); } }
        public List<JobContract> PostSendJobs { get { lock (Jobs) return Jobs.Where(j => j.JobType == CommunicationPostSendJobHandler.JobTypeName).ToList(); } }

        public List<(Entity Entity, Guid Id)> ArchivedEmls
        {
            get
            {
                lock (Created)
                    return Created
                        .Where(c => c.Entity.LogicalName == "sprk_document"
                                    && c.Entity.GetAttributeValue<OptionSetValue>("sprk_sourcetype")?.Value == EmailArchiveSourceType)
                        .ToList();
            }
        }

        public List<string> Warnings => Log.At(LogLevel.Warning);

        public CountingHandler Handler() => new(
            Sut, Entities.Object, Enrichment.Object, Cache,
            new NotificationService(Entities.Object, NullLogger<NotificationService>.Instance), Probe, HandlerLog);
    }

    /// <summary>The access probe, answering the rights a test scripts (null: Dataverse did not answer).</summary>
    private sealed class ScriptedProbe() : CallerRecordAccessProbe(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public AccessRights? Rights { get; set; } = AccessRights.Read | AccessRights.AppendTo;
        public List<(Guid User, string EntitySet, Guid Record)> Asked { get; } = [];

        public override Task<AccessRights?> GetPrincipalRightsAppOnlyOrUnknownAsync(
            Guid principalSystemUserId, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Asked.Add((principalSystemUserId, entitySet, recordId));
            return Task.FromResult(Rights);
        }
    }

    /// <summary>A logger that keeps every line (level and rendered message).</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate) Lines.Add((logLevel, formatter(state, exception)));
        }

        public List<string> At(LogLevel level)
        {
            lock (_gate) return Lines.Where(l => l.Level == level).Select(l => l.Message).ToList();
        }
    }

    /// <summary>The handler with its arrived emit counted.</summary>
    private sealed class CountingHandler(
        CommunicationService communication, IGenericEntityService entities, ICommunicationEnrichmentService enrichment, IIdempotencyService idempotency,
        NotificationService notifications, CallerRecordAccessProbe probe, ILogger<CommunicationPostSendJobHandler> logger)
        : CommunicationPostSendJobHandler(communication, entities, enrichment, idempotency, notifications, probe, logger)
    {
        public int Arrived { get; private set; }

        /// <summary>Thrown by the arrived emit when set (a fault outside the archive).</summary>
        public Exception? ArrivedFault { get; set; }

        internal override Task EmitArrivedAsync(Guid communicationId, CancellationToken ct)
        {
            if (ArrivedFault is { } fault) return Task.FromException(fault);
            Arrived++;
            return Task.CompletedTask;
        }
    }

    /// <summary>The ADR-004 idempotency cache, in memory; <see cref="Forget"/> empties it (the cache fails open, #984). Ownerless: a
    /// lock is held or not (the real cache's owner checks are not modelled).</summary>
    internal sealed class InMemoryIdempotency : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _locks = new(StringComparer.Ordinal);

        /// <summary>True: every lock is granted (the cache failing open, #984).</summary>
        public bool LocksFailOpen { get; set; }

        /// <summary>Called with the key of every refused lock (a test makes "the other archive" finish here).</summary>
        public Action<string>? OnLockRefused { get; set; }

        public void Forget()
        {
            lock (_processed) _processed.Clear();
        }

        public bool IsLocked(string key)
        {
            lock (_locks) return _locks.Contains(key);
        }

        public bool Hold(string key)
        {
            lock (_locks) return _locks.Add(key);
        }

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            lock (_processed) return Task.FromResult(_processed.Contains(eventId));
        }

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            lock (_processed) _processed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            if (LocksFailOpen) return Task.FromResult(true);
            bool acquired;
            lock (_locks) acquired = _locks.Add(eventId);
            if (!acquired) OnLockRefused?.Invoke(eventId);
            return Task.FromResult(acquired);
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
        {
            lock (_locks) _locks.Remove(eventId);
            return Task.CompletedTask;
        }
    }

    /// <summary>The stub scope (SPE store, container resolvers) plus the idempotency cache, as the BFF's scope serves them.</summary>
    private static IServiceScopeFactory ScopeFactory(SpeFileStore spe, IIdempotencyService idempotency)
    {
        var inner = SpeScopeFactoryStub.Create(spe, SpeScopeFactoryStub.NonSecureContainerResolver());
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(It.IsAny<Type>()))
            .Returns((Type t) => t == typeof(IIdempotencyService) ? idempotency : inner.CreateScope().ServiceProvider.GetService(t));
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    /// <param name="postSendSubmit">How the queue answers a <c>CommunicationPostSend</c> submit (null: it accepts); the Q4 tests throw or hang.</param>
    /// <param name="realCache">True: the REAL owner-based <see cref="IdempotencyService"/> over an in-memory distributed cache.</param>
    private static Harness Build(Exception? sendFault = null, Func<CancellationToken, Task>? postSendSubmit = null, bool realCache = false)
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = SharedMailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = SharedMailbox,
            ArchiveContainerId = ArchiveContainerId,
        };

        // The Dataverse writer, stateful: a created row answers its reads and its updates; an archive Document and a review row answer
        // the queries that look for them.
        Harness? harness = null;
        var created = new List<(Entity Entity, Guid Id)>();
        var rows = new Dictionary<Guid, Entity>();
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
            {
                if (table == "sprk_communication" && harness?.RowReadFault is { } fault) throw fault;
                lock (created) return rows.TryGetValue(id, out var row) ? row : null!;
            });
        entities
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                if (rows.TryGetValue(id, out var row))
                    foreach (var (k, v) in fields) row[k] = v;
                return Task.CompletedTask;
            });
        entities
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                if (q.EntityName == "sprk_emailreviewlog" && harness?.ReviewQueryFault is { } fault) throw fault;
                lock (created)
                {
                    var match = q.EntityName switch
                    {
                        "sprk_document" when q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_isemailarchive") =>
                            created.Where(c => c.Entity.LogicalName == "sprk_document"
                                && c.Entity.GetAttributeValue<bool?>("sprk_isemailarchive") == true
                                && c.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication")?.Id
                                    == (Guid)q.Criteria.Conditions.First(x => x.AttributeName == "sprk_relatedcommunication").Values[0]),
                        "sprk_emailreviewlog" => created.Where(c => c.Entity.LogicalName == "sprk_emailreviewlog"
                            && c.Entity.GetAttributeValue<EntityReference>("sprk_communication")?.Id
                                == (Guid)q.Criteria.Conditions.First(x => x.AttributeName == "sprk_communication").Values[0]),
                        _ => [],
                    };
                    return new EntityCollection(match.Select(c => new Entity(c.Entity.LogicalName, c.Id)).ToList());
                }
            });
        entities
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                lock (created)
                {
                    var id = Guid.NewGuid();
                    created.Add((e, id));
                    if (e.LogicalName == "sprk_communication")
                    {
                        var row = new Entity("sprk_communication", id);
                        foreach (var attribute in e.Attributes) row[attribute.Key] = attribute.Value;
                        rows[id] = row;
                    }
                    return id;
                }
            });

        var sendCount = 0;
        var sender = new Mock<ICommunicationChannelSender>();
        sender.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        sender
            .Setup(s => s.SendAsync(It.IsAny<ChannelSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSendRequest r, CancellationToken _) =>
            {
                sendCount++;
                if (sendFault is not null) throw sendFault;
                return new ChannelSendResult { FromAddress = r.FromAddress };
            });
        var dispatcher = new CommunicationChannelDispatcher(
            new[] { sender.Object },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        var accounts = new[] { SharedMailbox, UserMailbox }.Select(address => new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = address,
            EmailAddress = address,
            SendEnabled = true,
            ArchiveOutgoingOptIn = true,
        }).ToArray();
        var accountCache = new Mock<IDistributedCache>();
        accountCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts)));
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), accountCache.Object, Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), accountService, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<ApprovedSenderValidator>>());

        var uploadPaths = new List<string>();
        var uploadedBytes = new List<byte[]>();
        var spe = BuildSpeFileStore(
            uploadPaths, uploadedBytes,
            () => harness is null ? null : harness.UploadFails ? new HttpRequestException("SharePoint Embedded upload failed") : harness.UploadFault,
            () => harness?.UploadGate);
        var idempotency = new InMemoryIdempotency();
        IIdempotencyService cache = realCache
            ? new IdempotencyService(
                new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<IdempotencyService>.Instance)
            : idempotency;
        var log = new ListLogger<CommunicationService>();

        var jobs = new List<JobContract>();
        var threadCalls = new List<ThreadResolutionRequest>();
        var threads = new Mock<IThreadResolver>();
        threads.Setup(t => t.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThreadResolutionRequest r, CancellationToken _) =>
            {
                threadCalls.Add(r);
                var threadId = Guid.NewGuid();
                if (rows.TryGetValue(r.CommunicationId, out var row))
                    row["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId);
                return threadId;
            });

        // Enrichment: its propose step writes a review row naming the communication (the durable evidence the job reads).
        var enrichment = new Mock<ICommunicationEnrichmentService>();
        enrichment.Setup(e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CommunicationDirection _, NormalizedMessage _, Guid? _, CancellationToken _) =>
            {
                lock (created) created.Add((new Entity("sprk_emailreviewlog") { ["sprk_communication"] = new EntityReference("sprk_communication", id) }, Guid.NewGuid()));
                return Task.CompletedTask;
            });

        var communicationLookup = Mock.Of<ICommunicationDataverseService>(c =>
            c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<Guid?>(Guid.NewGuid()));
        var indexer = new CommunicationParticipantIndexer(
            communicationLookup, entities.Object, new RecordOwnershipResolverDouble(), NullLogger<CommunicationParticipantIndexer>.Instance);

        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            communicationLookup,
            entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            accountService,
            BuildJobQueue(jobs, postSendSubmit),
            enrichment.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            log,
            threadResolver: threads.Object,
            scopeFactory: ScopeFactory(spe.Object, cache),
            participantIndexer: indexer);

        harness = new Harness
        {
            Sut = sut,
            Entities = entities,
            Enrichment = enrichment,
            Created = created,
            Rows = rows,
            UploadPaths = uploadPaths,
            UploadedBytes = uploadedBytes,
            Jobs = jobs,
            ThreadCalls = threadCalls,
            Idempotency = idempotency,
            Cache = cache,
            Log = log,
            Probe = new ScriptedProbe(),
            SendCount = () => sendCount,
        };
        return harness;
    }

    private static Mock<SpeFileStore> BuildSpeFileStore(
        List<string> uploadPaths, List<byte[]> uploadedBytes, Func<Exception?> uploadFault, Func<Func<Task>?> uploadGate)
    {
        var metadataCache = new GraphMetadataCache(
            new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
            Mock.Of<ILogger<GraphMetadataCache>>());
        var gcf = Mock.Of<IGraphClientFactory>();
        var speMock = new Mock<SpeFileStore>(
            MockBehavior.Loose,
            new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>(), metadataCache),
            new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);

        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (string driveId, string path, Stream content, CancellationToken _) =>
            {
                if (uploadGate() is { } gate)
                    await gate();
                if (uploadFault() is { } fault)
                    throw fault;
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                int count;
                lock (uploadPaths)
                {
                    uploadPaths.Add(path);
                    uploadedBytes.Add(copy.ToArray());
                    count = uploadPaths.Count;
                }
                return new FileHandleDto(
                    Id: $"item-{count}", Name: path, ParentId: null, Size: 1,
                    CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                    ETag: null, IsFolder: false, WebUrl: null, DriveId: driveId);
            });

        return speMock;
    }

    internal static JobSubmissionService BuildJobQueue(List<JobContract> submitted, Func<CancellationToken, Task>? postSendSubmit)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose, sbOptions.Object, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContract job, CancellationToken ct) =>
            {
                if (postSendSubmit is not null && job.JobType == CommunicationPostSendJobHandler.JobTypeName)
                    await postSendSubmit(ct); // throws or hangs; the job is not on the queue
                lock (submitted) submitted.Add(job);
            });
        return jobs.Object;
    }
}
