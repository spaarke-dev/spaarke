using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance.Notifications;

/// <summary>
/// ebilling-r1 task 132 (FR-39, FR-35, FR-50, EB-D-254, EB-D-258, ADR-004, ADR-047): the notification job through the REAL
/// <see cref="EbillingNotificationJobHandler"/>, the REAL <see cref="FirmNoticeComposer"/> (its fixed template, so no model is involved), the REAL
/// <see cref="EbillingNotificationRecipients"/>, the REAL <see cref="EligibleApproverResolver"/> and the REAL <see cref="CommunicationService"/> down to
/// the channel sender, over the in-memory Dataverse of <see cref="ApprovalWorld"/>. Doubled only at the seams: the Graph channel, the appnotification
/// writer, the idempotency cache and the firm outcome read (the projection's own suites cover what it says).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038 seam): every negative the task names is here: the confirmed notice gates Return and Reject, a screen refusal sends nothing
/// and never substitutes the template, a redelivery sends nothing again, a missing billing contact means the submitter only, and only an eligible
/// approver with a linked user is notified. Every send is asserted at the channel, after the marker.
/// </remarks>
public sealed class EbillingNotificationJobHandlerSeamTests
{
    private const string Mailbox = "mailbox-central@spaarke.com";
    private const string SubmitterEmail = "submitter@firm.test";
    private const string BillingEmail = "billing@firm.test";

    private static readonly FirmOutcomeItem Reduced = new(
        FirmOutcomeDecision.Reduced, Guid.NewGuid(),
        new FirmOutcomeLine(new DateOnly(2026, 3, 4), "J. Smith", "Research on the motion", 1234.50m, 800.00m),
        null, "Too many hours for this task", ["Hours exceed the daily cap"], []);

    private sealed class Rig
    {
        public readonly ApprovalWorld World = new();
        public readonly Guid SubmitterId = Guid.NewGuid();
        public readonly Guid BillingId = Guid.NewGuid();
        public readonly FakeIdempotency Cache = new();
        public readonly Memberships Members = new();
        public readonly RecordingChannel Channel = new();
        public readonly Mock<IActionSeam> Actions = new();
        public readonly List<CreateNotificationRequest> Notified = [];
        public readonly LogCapture Log = new();
        public readonly EbillingOptions Options = new() { Enabled = true };
        public readonly List<FirmOutcome?> OutcomeBySubject = [];
        public FirmOutcome? InvoiceOutcome;
        public FirmOutcome? SubmissionOutcome;
        public readonly List<Guid> OutcomeReads = [];
        public readonly List<TimeSpan> Waits = [];
        public Action? OnWait;
        public Exception? SendFault;

        public Rig()
        {
            // The firm: an organisation with a billing contact, a submitter, and the invoice and submission that name them.
            World.Put(new Entity("sprk_organization", FirmId) { ["sprk_organizationname"] = "Acme Law", ["sprk_billingcontact"] = new EntityReference("contact", BillingId) });
            World.Put(Mailable(SubmitterId, "Sam Submitter", SubmitterEmail));
            World.Put(Mailable(BillingId, "Bea Billing", BillingEmail));
            // Both contacts are active members of the firm; a test takes a membership away to prove who is NOT told (ebilling-r1 132 review F1).
            Members.Firms[SubmitterId] = [FirmId];
            Members.Firms[BillingId] = [FirmId];
            World.Invoice["sprk_submittedby"] = new EntityReference("contact", SubmitterId);
            World.Invoice["sprk_trackingnumber"] = "TRK-1.2";
            var submission = World.Row("sprk_invoicesubmission", World.OwnSubmissionId);
            submission["sprk_submitter"] = new EntityReference("contact", SubmitterId);
            submission["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId);

            Actions.Setup(a => a.CreateNotificationAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback((CreateNotificationRequest r, CancellationToken _) => Notified.Add(r))
                .ReturnsAsync(new CreateNotificationResult(true, Guid.NewGuid(), false, null));
            Channel.Fault = () => SendFault;
            Channel.Marker = () => Cache.Marked.ToList();
        }

        public static Entity Mailable(Guid id, string name, string? email, bool active = true)
        {
            var contact = Contact(id, name, null, active);
            if (email is not null)
            {
                contact["emailaddress1"] = email;
            }

            return contact;
        }

        public FirmOutcome Outcome(FirmOutcomeKind kind, params FirmOutcomeItem[] items) =>
            new(kind, "TRK-1.2", kind.ToString(), 9876.54m, kind is FirmOutcomeKind.ApprovedWithAdjustments ? 9000.00m : null, items, false);

        public EbillingNotificationJobHandler Handler()
        {
            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { FirmNoticeAiEnabled = false });
            var composer = new FirmNoticeComposer(
                (id, _) =>
                {
                    OutcomeReads.Add(id);
                    return Task.FromResult(InvoiceOutcome);
                },
                (id, _, _) =>
                {
                    OutcomeReads.Add(id);
                    return Task.FromResult(SubmissionOutcome);
                },
                settings.Object, Mock.Of<IInvoiceAi>(MockBehavior.Strict), Log.For<FirmNoticeComposer>());

            var entities = World.Entities.Object;
            var recipients = new EbillingNotificationRecipients(entities, Members, Log.For<EbillingNotificationRecipients>());
            return new EbillingNotificationJobHandler(
                composer, recipients, Communication(), World.Resolver(), Actions.Object, Cache, entities,
                Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == Options), Log.For<EbillingNotificationJobHandler>())
            {
                ReadyPollAttempts = 3,
                ReadyPollInterval = TimeSpan.FromMilliseconds(1),
                Delay = (interval, _) =>
                {
                    Waits.Add(interval);
                    OnWait?.Invoke();
                    return Task.CompletedTask;
                },
            };
        }

        private CommunicationService Communication()
        {
            var options = new CommunicationOptions
            {
                ApprovedSenders = [new ApprovedSenderConfig { Email = Mailbox, DisplayName = "Spaarke", IsDefault = true }],
                DefaultMailbox = Mailbox,
            };
            var senderValidator = new ApprovedSenderValidator(
                Microsoft.Extensions.Options.Options.Create(options),
                new CommunicationAccountService(
                    Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                    NullLogger<CommunicationAccountService>.Instance),
                Mock.Of<IDistributedCache>(),
                NullLogger<ApprovedSenderValidator>.Instance);
            var dispatcher = new CommunicationChannelDispatcher(
                new ICommunicationChannelSender[] { Channel },
                new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(NullLogger<EmlGenerationService>.Instance)) });
            var communicationEntities = new Mock<IGenericEntityService>();
            communicationEntities.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
            return new CommunicationService(
                dispatcher, senderValidator, Mock.Of<ICommunicationDataverseService>(), communicationEntities.Object,
                Mock.Of<IDocumentDataverseService>(), null!, null!, Mock.Of<ICommunicationEnrichmentService>(),
                Microsoft.Extensions.Options.Options.Create(options),
                Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
                Log.For<CommunicationService>());
        }

        public JobContract Job(EbillingNotificationEvent evt, EbillingNotificationSubject subject, Guid id, string? noticeSubject = null, string? noticeProse = null,
            int attempt = 1, int deliveryCount = 1)
        {
            var payload = new EbillingNotificationPayload(evt, subject, noticeSubject, noticeProse);
            return new JobContract
            {
                JobType = EbillingNotificationEnqueuer.JobTypeName,
                SubjectId = id.ToString("D"),
                CorrelationId = "corr-132",
                IdempotencyKey = EbillingNotificationEnqueuer.IdempotencyKeyFor(evt, id),
                Attempt = attempt,
                DeliveryCount = deliveryCount,
                Payload = JsonSerializer.SerializeToDocument(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            };
        }

        public JobContract InvoiceJob(EbillingNotificationEvent evt, string? noticeSubject = null, string? noticeProse = null) =>
            Job(evt, EbillingNotificationSubject.Invoice, World.InvoiceId, noticeSubject, noticeProse);

        public JobContract RejectedSubmissionJob() => Job(EbillingNotificationEvent.SubmissionRejected, EbillingNotificationSubject.Submission, World.OwnSubmissionId);

        public void SubmissionIs(InvoiceSubmissionStatus status) =>
            World.Row("sprk_invoicesubmission", World.OwnSubmissionId)["sprk_processingstatus"] = new OptionSetValue((int)status);
    }

    private static string[] Recipients(ChannelSendRequest sent) => sent.Communication.To;

    // ── A rejected submission (the system's decision): the composed draft, unchanged ──

    [Fact(DisplayName = "132: a rejected submission sends the composed notice to the submitter AND the firm's billing contact; the marker is written BEFORE the send; the sent text is kept on the submission")]
    public async Task SubmissionRejected_SendsToSubmitterAndBillingContact_MarkerFirst_AndKeepsTheSentText()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected, Reduced);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        Recipients(sent).Should().BeEquivalentTo(SubmitterEmail, BillingEmail);
        sent.Communication.Subject.Should().EndWith("(TRK-1.2)", "the tracking number is appended by code");
        sent.Communication.BodyFormat.Should().Be(BodyFormat.HTML);
        sent.Communication.SendMode.Should().Be(SendMode.SharedMailbox);
        sent.Communication.Body.Should().Contain("Research on the motion").And.Contain("Too many hours for this task", "the findings table is inserted by code");
        rig.Channel.MarkersAtSend.Should().ContainSingle().Which.Should().Contain(EbillingNotificationEnqueuer.MarkerFor(EbillingNotificationEvent.SubmissionRejected, rig.World.OwnSubmissionId),
            "the at-most-once marker is written before anything is sent");
        var stored = rig.World.Row("sprk_invoicesubmission", rig.World.OwnSubmissionId).GetAttributeValue<string>("sprk_firmnoticetext");
        // EB-D-259: one JSON object, the prose recoverable on its own, apart from the html.
        var json = System.Text.Json.JsonDocument.Parse(stored!).RootElement;
        json.GetProperty("v").GetInt32().Should().Be(1);
        json.GetProperty("source").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("subject").GetString().Should().Be(sent.Communication.Subject);
        json.GetProperty("html").GetString().Should().Contain("Research on the motion");
        json.GetProperty("htmlOmitted").GetBoolean().Should().BeFalse();
        json.GetProperty("prose").GetString().Should().NotContain("<", "the prose is stored as plain text, not html");
    }

    [Fact(DisplayName = "132 EB-D-259: the stored notice keeps the reviewer's confirmed prose on its own; an html too big for the column is omitted, never the prose or subject")]
    public async Task StoredNotice_KeepsTheConfirmedProse_AndDropsOnlyAnOversizedHtml()
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(FirmOutcomeKind.ReturnedToFirm, Reduced);
        const string prose = "Hello,\n\nWe could not accept the hours on the research entry.\n\nThank you.";

        await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.ReturnedToFirm, "Please correct your invoice", prose), CancellationToken.None);

        var stored = rig.World.Row("sprk_invoicesubmission", rig.World.OwnSubmissionId).GetAttributeValue<string>("sprk_firmnoticetext");
        var json = System.Text.Json.JsonDocument.Parse(stored!).RootElement;
        json.GetProperty("source").GetString().Should().Be("Reviewer");
        json.GetProperty("prose").GetString().Should().Be(prose);
        json.GetProperty("subject").GetString().Should().StartWith("Please correct your invoice");

        var huge = new FirmNoticeDraft(FirmNoticeKind.ReturnedToFirm, "Subject", "Plain prose", new string('x', EbillingNotificationJobHandler.FirmNoticeTextMaxLength), FirmNoticeSource.Reviewer, null, null);
        var big = System.Text.Json.JsonDocument.Parse(EbillingNotificationJobHandler.SentNoticeJson(huge)).RootElement;
        big.GetProperty("htmlOmitted").GetBoolean().Should().BeTrue();
        big.GetProperty("html").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        big.GetProperty("prose").GetString().Should().Be("Plain prose");
        EbillingNotificationJobHandler.SentNoticeJson(huge).Length.Should().BeLessThanOrEqualTo(EbillingNotificationJobHandler.FirmNoticeTextMaxLength);
    }

    [Fact(DisplayName = "132: a redelivery sends no second email (the marker is found), and the marker outlives the process")]
    public async Task Redelivery_SendsNothingTheSecondTime()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        var job = rig.RejectedSubmissionJob();

        await rig.Handler().ProcessAsync(job, CancellationToken.None);
        var second = await rig.Handler().ProcessAsync(job, CancellationToken.None);

        second.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().HaveCount(1);
    }

    [Fact(DisplayName = "132: a firm with no billing contact means the SUBMITTER only, with a warning that names ids and no address")]
    public async Task MissingBillingContact_MeansTheSubmitterOnly_WithAnIdsOnlyWarning()
    {
        var rig = new Rig();
        rig.World.Row("sprk_organization", FirmId).Attributes.Remove("sprk_billingcontact");
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(SubmitterEmail);
        var warning = rig.Log.Lines.Should().Contain(l => l.Contains("no billing contact")).Subject;
        warning.Should().Contain(rig.World.OwnSubmissionId.ToString()).And.Contain(FirmId.ToString());
        rig.Log.Lines.Should().NotContain(l => l.Contains(SubmitterEmail) || l.Contains(BillingEmail), "ADR-015: ids only");
    }

    [Fact(DisplayName = "132: a billing contact with no address (or an inactive one) is the same as none: the submitter only")]
    public async Task BillingContactWithoutAnAddress_MeansTheSubmitterOnly()
    {
        var rig = new Rig();
        rig.World.Put(Rig.Mailable(rig.BillingId, "Bea Billing", email: null));
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(SubmitterEmail);
    }

    [Fact(DisplayName = "132: nobody with a usable address means nothing is sent, nothing is marked and the job completes with an ids-only warning")]
    public async Task NobodyToTell_SendsNothing_AndWritesNoMarker()
    {
        var rig = new Rig();
        rig.World.Put(Rig.Mailable(rig.BillingId, "Bea Billing", email: null));
        rig.World.Put(Rig.Mailable(rig.SubmitterId, "Sam Submitter", "not an address"));
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
        rig.Cache.Marked.Should().BeEmpty();
    }

    [Fact(DisplayName = "132: a submitter who is also the billing contact is written to once")]
    public async Task OnePersonInBothRoles_IsWrittenToOnce()
    {
        var rig = new Rig();
        rig.World.Row("sprk_organization", FirmId)["sprk_billingcontact"] = new EntityReference("contact", rig.SubmitterId);
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(SubmitterEmail);
    }

    // ── Review F1: only ACTIVE members of the notice's own firm are told ──

    [Fact(DisplayName = "132 review F1 (must-fire): a submitter whose membership in the firm is no longer active is NOT emailed; the billing contact still is")]
    public async Task ASubmitterWithoutAnActiveMembership_IsNotTold()
    {
        var rig = new Rig();
        rig.Members.Firms.Remove(rig.SubmitterId);
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(BillingEmail);
        rig.Log.Lines.Should().NotContain(l => l.Contains(SubmitterEmail) || l.Contains(BillingEmail), "ADR-015: ids only");
    }

    [Fact(DisplayName = "132 review F1 (must-fire): a billing contact who belongs to ANOTHER firm is NOT emailed")]
    public async Task ABillingContactFromAnotherFirm_IsNotTold()
    {
        var rig = new Rig();
        rig.Members.Firms[rig.BillingId] = [Guid.NewGuid()];
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(SubmitterEmail);
    }

    [Fact(DisplayName = "132 review F1 (must-fire): a deactivated contact record confers no membership, so it is not emailed even with a membership row")]
    public async Task ADeactivatedContact_IsNotTold()
    {
        var rig = new Rig();
        rig.Members.InactiveContacts.Add(rig.BillingId);
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(SubmitterEmail);
    }

    [Fact(DisplayName = "132 review F1 (must-fire): a contact whose statecode was not read is NOT active and is not emailed")]
    public async Task AContactWithNoStatecode_IsNotActive_AndIsNotTold()
    {
        var rig = new Rig();
        rig.World.Row("contact", rig.BillingId).Attributes.Remove("statecode");
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        Recipients(rig.Channel.Sent.Should().ContainSingle().Subject).Should().Equal(SubmitterEmail);
    }

    [Fact(DisplayName = "132 review F1: when nobody holds an active membership nothing is sent, nothing is marked, and the job completes with an ids-only warning")]
    public async Task NobodyWithAMembership_SendsNothing_AndWritesNoMarker()
    {
        var rig = new Rig();
        rig.Members.Firms.Clear();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
        rig.Cache.Marked.Should().BeEmpty();
        rig.Log.Lines.Should().Contain(l => l.Contains(rig.World.OwnSubmissionId.ToString()) && l.Contains(FirmId.ToString()) && l.Contains("no "));
        rig.Log.Lines.Should().NotContain(l => l.Contains(SubmitterEmail) || l.Contains(BillingEmail), "ADR-015: ids only");
    }

    [Fact(DisplayName = "132: the pipeline reports a rejection just BEFORE it writes the final status; a submission still Processing is waited for, then the notice goes")]
    public async Task SubmissionStillProcessing_IsWaitedFor_ThenSent()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Processing);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        rig.OnWait = () => rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Waits.Should().HaveCount(1);
        rig.Channel.Sent.Should().HaveCount(1);
    }

    [Fact(DisplayName = "132: a submission that never reaches a final status is retried (Failure), not skipped: nothing is sent and nothing is marked")]
    public async Task SubmissionNeverFinal_IsRetried_NotSkipped()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Processing);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Waits.Should().HaveCount(2);
        rig.Channel.Sent.Should().BeEmpty();
        rig.Cache.Marked.Should().BeEmpty();
        rig.OutcomeReads.Should().BeEmpty("the notice is not composed from a status that is still moving");
    }

    [Fact(DisplayName = "132: a submission accepted after all (overridden) is not a rejection the firm is told")]
    public async Task SubmissionNoLongerRejected_SendsNothing()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Accepted);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
    }

    // ── Approved: with adjustments sends the draft; without sends nothing ──

    [Fact(DisplayName = "132: an approval WITH an adjustment sends the composed notice unchanged")]
    public async Task ApprovedWithAdjustments_SendsTheComposedNotice()
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(FirmOutcomeKind.ApprovedWithAdjustments, Reduced);

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.ApprovedWithAdjustments), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        Recipients(sent).Should().BeEquivalentTo(SubmitterEmail, BillingEmail);
        sent.Communication.Body.Should().Contain("Research on the motion");
        rig.World.Row("sprk_invoicesubmission", rig.World.OwnSubmissionId).GetAttributeValue<string>("sprk_firmnoticetext").Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "132: an approval with NO adjustment sends no email (the composer's one definition of 'with adjustments')")]
    public async Task ApprovedWithoutAdjustment_SendsNothing()
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(FirmOutcomeKind.Approved);

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.ApprovedWithAdjustments), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
        rig.Cache.Marked.Should().BeEmpty();
    }

    // ── Return and Reject: only the reviewer-confirmed notice (EB-D-258) ──

    [Theory(DisplayName = "132 EB-D-258: a Return or Reject WITHOUT the reviewer-confirmed notice sends no firm email and reads nothing")]
    [InlineData(EbillingNotificationEvent.ReturnedToFirm)]
    [InlineData(EbillingNotificationEvent.InvoiceRejected)]
    public async Task ReturnOrReject_WithoutAConfirmedNotice_SendsNothing(EbillingNotificationEvent evt)
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(evt == EbillingNotificationEvent.ReturnedToFirm ? FirmOutcomeKind.ReturnedToFirm : FirmOutcomeKind.Rejected, Reduced);

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(evt), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
        rig.OutcomeReads.Should().BeEmpty();
        rig.Cache.Marked.Should().BeEmpty();
        rig.Log.Lines.Should().Contain(l => l.Contains("no reviewer-confirmed notice") && l.Contains(rig.World.InvoiceId.ToString()));
    }

    [Theory(DisplayName = "132 EB-D-258: a Return or Reject WITH the confirmed notice sends the reviewer's prose, the data inserted by code, the tracking number appended by code")]
    [InlineData(EbillingNotificationEvent.ReturnedToFirm, FirmOutcomeKind.ReturnedToFirm)]
    [InlineData(EbillingNotificationEvent.InvoiceRejected, FirmOutcomeKind.Rejected)]
    public async Task ReturnOrReject_WithAConfirmedNotice_SendsTheReviewersProse(EbillingNotificationEvent evt, FirmOutcomeKind kind)
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(kind, Reduced);

        var outcome = await rig.Handler().ProcessAsync(
            rig.InvoiceJob(evt, "Please correct your invoice (TRK-1.2)", "Hello,\n\nWe could not accept the hours on the research entry.\n\nThank you."),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        Recipients(sent).Should().BeEquivalentTo(SubmitterEmail, BillingEmail);
        sent.Communication.Subject.Should().Be("Please correct your invoice (TRK-1.2)", "the code-written tracking suffix is neither doubled nor changeable");
        sent.Communication.Body.Should().Contain("We could not accept the hours on the research entry.").And.Contain("Research on the motion");
    }

    [Theory(DisplayName = "132 (130f): a degraded or unavailable outcome sends NO email (never an empty or partial table), marks nothing sent, stays retryable, and a later delivery sends exactly once")]
    [InlineData(EbillingNotificationEvent.ApprovedWithAdjustments, false)]
    [InlineData(EbillingNotificationEvent.ReturnedToFirm, true)]
    [InlineData(EbillingNotificationEvent.InvoiceRejected, true)]
    public async Task ADegradedOutcome_SendsNothing_StaysRetryable_AndIsSentOnceWhenItRecovers(EbillingNotificationEvent evt, bool withNotice)
    {
        var rig = new Rig();
        var kind = evt == EbillingNotificationEvent.ApprovedWithAdjustments ? FirmOutcomeKind.ApprovedWithAdjustments
            : evt == EbillingNotificationEvent.ReturnedToFirm ? FirmOutcomeKind.ReturnedToFirm : FirmOutcomeKind.Rejected;
        var healthy = rig.Outcome(kind, Reduced);
        string? subject = withNotice ? "Please review your invoice" : null;
        string? prose = withNotice ? "Hello,\n\nWe could not accept the hours on the research entry.\n\nThank you." : null;

        // 1. The projection could not read the lines: a Degraded, status-only outcome. Then the projection's own Unavailable kind.
        foreach (var broken in new[] { healthy with { Degraded = true, Items = [] }, healthy with { Kind = FirmOutcomeKind.Unavailable, Items = [] } })
        {
            rig.InvoiceOutcome = broken;
            var failed = await rig.Handler().ProcessAsync(rig.InvoiceJob(evt, subject, prose), CancellationToken.None);

            failed.Status.Should().Be(JobStatus.Failed, "not final: the job is retried");
            rig.Channel.Sent.Should().BeEmpty("no email with an empty or partial findings table");
            rig.Cache.Marked.Should().BeEmpty("the notice is not marked sent");
        }

        rig.Log.Lines.Should().NotContain(l => l.Contains("@") && l.Contains("eBilling notification"), "ADR-015: ids only, never an address");

        // 2. The last delivery of a still-broken read is poisoned, not silently dropped, and still sends nothing.
        rig.InvoiceOutcome = healthy with { Degraded = true, Items = [] };
        var final = await rig.Handler().ProcessAsync(
            rig.Job(evt, EbillingNotificationSubject.Invoice, rig.World.InvoiceId, subject, prose, attempt: 5, deliveryCount: JobContract.MaxDeliveryCount), CancellationToken.None);
        final.Status.Should().Be(JobStatus.Poisoned);
        rig.Channel.Sent.Should().BeEmpty();

        // 3. The read recovers: the redelivery sends once, with the table, and a further redelivery sends nothing.
        rig.InvoiceOutcome = healthy;
        var retried = await rig.Handler().ProcessAsync(rig.InvoiceJob(evt, subject, prose), CancellationToken.None);
        var again = await rig.Handler().ProcessAsync(rig.InvoiceJob(evt, subject, prose), CancellationToken.None);

        retried.Status.Should().Be(JobStatus.Completed);
        again.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().ContainSingle().Which.Communication.Body.Should().Contain("Research on the motion");
    }

    [Fact(DisplayName = "132 uac-r2 N2: the reviewer's prose is encoded by RenderHtml (no markup survives) and a subject with line breaks is made header-safe")]
    public async Task ReviewerProseIsEncoded_AndTheSubjectIsHeaderSafe()
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(FirmOutcomeKind.ReturnedToFirm, Reduced);

        await rig.Handler().ProcessAsync(
            rig.InvoiceJob(EbillingNotificationEvent.ReturnedToFirm, "Correct this\r\nBcc: someone", "<script>alert</script> Please review <b>this</b>."),
            CancellationToken.None);

        var sent = rig.Channel.Sent.Should().ContainSingle().Subject.Communication;
        sent.Subject.Should().NotContainAny("\r", "\n");
        sent.Body.Should().NotContain("<script>").And.NotContain("<b>this</b>").And.Contain("&lt;script&gt;");
    }

    [Theory(DisplayName = "132 EB-D-258: edited prose the screen refuses sends NO email, is logged with the stable reason, and the template is NOT substituted")]
    [InlineData("We will pay 5000 by Friday.", "prose.digit")]
    [InlineData("Please see https://evil.test/pay now.", "prose.link")]
    public async Task RefusedProse_SendsNothing_AndNeverSubstitutesTheTemplate(string prose, string reason)
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(FirmOutcomeKind.ReturnedToFirm, Reduced);

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.ReturnedToFirm, "Please correct", prose), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, "the decision stands; the job does not fail it");
        rig.Channel.Sent.Should().BeEmpty();
        rig.Cache.Marked.Should().BeEmpty();
        rig.Log.Lines.Should().Contain(l => l.Contains(reason) && l.Contains(rig.World.InvoiceId.ToString()));
        rig.Log.Lines.Should().NotContain(l => l.Contains(prose), "ADR-015: never the text");
    }

    [Fact(DisplayName = "132 EB-D-258: a Return whose invoice is no longer Returned (it moved on) sends nothing")]
    public async Task ConfirmedNotice_ForAnInvoiceInAnotherState_SendsNothing()
    {
        var rig = new Rig();
        rig.InvoiceOutcome = rig.Outcome(FirmOutcomeKind.Paid);

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.ReturnedToFirm, "Please correct", "Please correct it."), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
    }

    // ── The marker and the send ──

    [Fact(DisplayName = "132: a cache that cannot hold the marker (fails open) sends NOTHING and retries: a send never starts without a visible marker")]
    public async Task MarkerThatCannotBeReadBack_SendsNothing_AndRetries()
    {
        var rig = new Rig();
        rig.Cache.DropsWrites = true;
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Channel.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "132: a send that fails after the marker is Poisoned (an Error with ids and the exception type) and is NOT sent again on redelivery")]
    public async Task SendFault_IsPoisoned_AndNeverRetried()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        rig.SendFault = new InvalidOperationException("graph down: submitter@firm.test");
        var job = rig.RejectedSubmissionJob();

        var first = await rig.Handler().ProcessAsync(job, CancellationToken.None);
        rig.SendFault = null;
        var second = await rig.Handler().ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Poisoned);
        second.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty("at-most-once: the marker was written first, so the redelivery does not send");
        rig.Log.Lines.Should().Contain(l => l.Contains(rig.World.OwnSubmissionId.ToString()));
        rig.Log.Lines.Where(l => l.Contains("eBilling notification")).Should().NotContain(l => l.Contains("submitter@firm.test"), "ADR-015: the job's own lines carry no address");
    }

    private static SdapProblemException GraphSendFailed(string outcome) => new(
        "GRAPH_SEND_FAILED", "Failed to send email", "graph", 502, new Dictionary<string, object> { [EmailChannelSender.SendOutcomeExtension] = outcome });

    [Fact(DisplayName = "132 review F1 (must-fire): a send Graph PROVABLY refused (NotSent) forgets the marker and is retried; the later delivery sends exactly once")]
    public async Task SendThatProvablyDidNotGoOut_ClearsTheMarker_AndIsRetried()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        rig.SendFault = GraphSendFailed(EmailChannelSender.SendOutcomeNotSent);
        var job = rig.RejectedSubmissionJob();

        var first = await rig.Handler().ProcessAsync(job, CancellationToken.None);
        rig.Cache.Marked.Should().BeEmpty("the marker is forgotten, or the retry would drop the notice");
        rig.SendFault = null;
        var second = await rig.Handler().ProcessAsync(job, CancellationToken.None);
        var third = await rig.Handler().ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed, "retryable, not Poisoned");
        second.Status.Should().Be(JobStatus.Completed);
        third.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().ContainSingle("sent once on the retry, not again after it");
    }

    [Fact(DisplayName = "132 review F1 (must-fire): a send refusal that comes BEFORE the channel (sender, validation, daily limit) is also retried with the marker forgotten")]
    public async Task APreSendRefusal_ClearsTheMarker_AndIsRetried()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        rig.SendFault = new SdapProblemException("DAILY_SEND_LIMIT", "Daily send limit reached", null, 429);

        var first = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        rig.Cache.Marked.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 review F1 (must-fire): an AMBIGUOUS send outcome (Graph 'Unknown') stays Poisoned with the marker kept, and is not sent again")]
    public async Task AnAmbiguousSend_StaysPoisoned_AndKeepsTheMarker()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        rig.SendFault = GraphSendFailed(EmailChannelSender.SendOutcomeUnknown);
        var job = rig.RejectedSubmissionJob();

        var first = await rig.Handler().ProcessAsync(job, CancellationToken.None);
        rig.SendFault = null;
        var second = await rig.Handler().ProcessAsync(job, CancellationToken.None);

        first.Status.Should().Be(JobStatus.Poisoned);
        rig.Cache.Marked.Should().ContainSingle("the message may have been accepted: the marker stays");
        second.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 review F1 (must-fire): a NotSent send whose marker cannot be shown gone is Poisoned (a silent drop is worse than an alert)")]
    public async Task ANotSentSend_WhoseMarkerCannotBeCleared_IsPoisoned()
    {
        var rig = new Rig();
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);
        rig.SendFault = GraphSendFailed(EmailChannelSender.SendOutcomeNotSent);
        rig.Cache.CannotClear = true;

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact(DisplayName = "132 ADR-018: with the eBilling kill switch off the job sends and notifies nothing")]
    public async Task KillSwitchOff_SendsNothing()
    {
        var rig = new Rig { Options = { Enabled = false } };
        rig.SubmissionIs(InvoiceSubmissionStatus.Rejected);
        rig.SubmissionOutcome = rig.Outcome(FirmOutcomeKind.SubmissionRejected);

        var outcome = await rig.Handler().ProcessAsync(rig.RejectedSubmissionJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Channel.Sent.Should().BeEmpty();
        rig.Notified.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 ADR-004: a payload that is not ours, or whose subject does not fit its event, is Poisoned (never retried)")]
    public async Task BadPayload_IsPoisoned()
    {
        var rig = new Rig();
        var wrongSubject = rig.Job(EbillingNotificationEvent.SubmissionRejected, EbillingNotificationSubject.Invoice, rig.World.InvoiceId);
        var noPayload = rig.RejectedSubmissionJob();
        noPayload.Payload = null;

        (await rig.Handler().ProcessAsync(wrongSubject, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);
        (await rig.Handler().ProcessAsync(noPayload, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact(DisplayName = "132: an unreadable subject is retried until the final delivery, then Poisoned")]
    public async Task SubjectNotFound_IsRetried_ThenPoisoned()
    {
        var rig = new Rig();
        var missing = rig.Job(EbillingNotificationEvent.ApprovedWithAdjustments, EbillingNotificationSubject.Invoice, Guid.NewGuid());
        var finalDelivery = rig.Job(
            EbillingNotificationEvent.ApprovedWithAdjustments, EbillingNotificationSubject.Invoice, Guid.NewGuid(), deliveryCount: JobContract.MaxDeliveryCount);

        (await rig.Handler().ProcessAsync(missing, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);
        (await rig.Handler().ProcessAsync(finalDelivery, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);
    }

    // ── Approvers (FR-35, ADR-047) ──

    [Fact(DisplayName = "132 FR-35: an accepted invoice notifies each ELIGIBLE approver who has a user, once, through the seam; nobody else")]
    public async Task InvoiceAccepted_NotifiesTheEligibleApproversWithUsers_AndNobodyElse()
    {
        var rig = new Rig();

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.InvoiceAccepted), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Notified.Select(n => n.RecipientId).Should().BeEquivalentTo([AliceUser, BobUser]);
        rig.Notified.Select(n => n.RecipientId).Should().NotContain(CarolUser, "Carol is assigned nowhere on this record");
        rig.Notified.Should().OnlyContain(n =>
            n.Category == EbillingNotificationJobHandler.ApproverCategory
            && n.RegardingId == rig.World.InvoiceId
            && n.RegardingType == "sprk_invoice"
            && n.ActionUrl == EbillingNotificationJobHandler.ReviewUrlFor(rig.World.InvoiceId));
        rig.Notified.Select(n => n.Body).Should().OnlyContain(b => b.Contains("TRK-1.2") && b.Contains("Acme Law"));
        rig.Channel.Sent.Should().BeEmpty("approvers are told in-app, not by email");
    }

    [Fact(DisplayName = "132 FR-35: a redelivery creates no second appnotification")]
    public async Task InvoiceAccepted_Redelivery_NotifiesNobodyAgain()
    {
        var rig = new Rig();
        var job = rig.InvoiceJob(EbillingNotificationEvent.InvoiceAccepted);

        await rig.Handler().ProcessAsync(job, CancellationToken.None);
        rig.Notified.Clear();
        var second = await rig.Handler().ProcessAsync(job, CancellationToken.None);

        second.Status.Should().Be(JobStatus.Completed);
        rig.Notified.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 FR-35: an approver without a linked user, and a record with no eligible approver, notify nobody (a warning with ids)")]
    public async Task InvoiceAccepted_NobodyEligible_NotifiesNobody()
    {
        var rig = new Rig();
        rig.World.Core["sprk_assignedattorney1"] = new EntityReference("contact", Dan);
        rig.World.Core["sprk_assignedtointernal"] = new EntityReference("contact", Dan);

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.InvoiceAccepted), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Notified.Should().BeEmpty();
        rig.Log.Lines.Should().Contain(l => l.Contains("no eligible approver") && l.Contains(rig.World.InvoiceId.ToString()));
    }

    [Fact(DisplayName = "132 FR-35: an invoice that is no longer In Review notifies nobody (nothing is waiting)")]
    public async Task InvoiceAccepted_ForAnInvoiceAlreadyDecided_NotifiesNobody()
    {
        var rig = new Rig();
        rig.World.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);

        await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.InvoiceAccepted), CancellationToken.None);

        rig.Notified.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 FR-35: one approver's failed notification does not stop the others, and the job still completes")]
    public async Task InvoiceAccepted_OneFailure_DoesNotStopTheRest()
    {
        var rig = new Rig();
        rig.Actions.Setup(a => a.CreateNotificationAsync(It.Is<CreateNotificationRequest>(r => r.RecipientId == AliceUser), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse hiccup"));

        var outcome = await rig.Handler().ProcessAsync(rig.InvoiceJob(EbillingNotificationEvent.InvoiceAccepted), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Notified.Select(n => n.RecipientId).Should().Equal(BobUser);
    }

    // ── Doubles ──

    /// <summary>An in-memory idempotency cache. <see cref="DropsWrites"/> models the cache that fails open (a write is lost, a read says "never seen").</summary>
    private sealed class FakeIdempotency : IIdempotencyService
    {
        private readonly HashSet<string> _marked = [];

        public bool DropsWrites { get; set; }

        public IReadOnlyCollection<string> Marked => _marked;

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(_marked.Contains(eventId));

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            if (!DropsWrites)
            {
                _marked.Add(eventId);
            }

            return Task.CompletedTask;
        }

        /// <summary>When true the marker cannot be removed (the cache refuses), so the job cannot show it gone.</summary>
        public bool CannotClear { get; set; }

        public Task<bool> ClearEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            if (CannotClear)
            {
                return Task.FromResult(false);
            }

            _marked.Remove(eventId);
            return Task.FromResult(true);
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>The firm memberships of the contacts (the real <c>ReadContactFirmsAsync</c> rule runs over this double of the two Dataverse reads).</summary>
    private sealed class Memberships : ExternalParticipationService
    {
        public Memberships()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: Mock.Of<TokenCredential>(),
                httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance, filing: null)
        {
        }

        /// <summary>The firms each contact actively belongs to; a contact not listed belongs to none.</summary>
        public Dictionary<Guid, Guid[]> Firms { get; } = [];

        /// <summary>Contacts whose own record is not Active (a deactivated contact confers no firm).</summary>
        public HashSet<Guid> InactiveContacts { get; } = [];

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct) =>
            Task.FromResult(InactiveContacts.Contains(contactId) ? ContactRecordState.Inactive : ContactRecordState.Active);

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            var firms = Firms.TryGetValue(contactId, out var list) ? list : [];
            return Task.FromResult(new ActiveOrgMemberships(firms, firms, false));
        }
    }

    private sealed class RecordingChannel : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;

        public List<ChannelSendRequest> Sent { get; } = [];

        /// <summary>The markers present at each send: proves the marker was written first.</summary>
        public List<List<string>> MarkersAtSend { get; } = [];

        public Func<Exception?>? Fault { get; set; }

        public Func<List<string>>? Marker { get; set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            if (Fault?.Invoke() is { } fault)
            {
                return Task.FromException<ChannelSendResult>(fault);
            }

            MarkersAtSend.Add(Marker?.Invoke() ?? []);
            Sent.Add(request);
            return Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress });
        }
    }

    private sealed class LogCapture
    {
        public List<string> Lines { get; } = [];

        public ILogger<T> For<T>() => new Logger<T>(this);

        private sealed class Logger<T>(LogCapture owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines)
                {
                    owner.Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
                }
            }
        }
    }
}
