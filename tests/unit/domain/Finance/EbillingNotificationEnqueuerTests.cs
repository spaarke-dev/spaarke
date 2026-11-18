using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Notifications;

/// <summary>
/// ebilling-r1 task 132 (FR-39, FR-35, EB-D-258, ADR-004): the job message and the one enqueuer, and the sink that maps the submission pipeline's
/// report onto it. Pure mapping: a recording double stands in for the queue.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038 domain): the key and the payload are the contract between the three callers (103, 104, the sink) and the job, and the
/// notice caps keep a request body from reaching the broker's message limit.
/// </remarks>
public sealed class EbillingNotificationEnqueuerTests
{
    private static readonly Guid Invoice = Guid.Parse("32000000-0000-4000-8000-000000000001");
    private static readonly Guid Submission = Guid.Parse("32000000-0000-4000-8000-000000000002");

    private readonly List<JobContract> _jobs = [];
    private Exception? _fault;

    private EbillingNotificationEnqueuer Enqueuer(ILogger<EbillingNotificationEnqueuer>? logger = null)
    {
        var queue = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { QueueName = "sdap-jobs" }),
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, CancellationToken _) =>
            {
                if (_fault is { } fault)
                {
                    return Task.FromException(fault);
                }

                _jobs.Add(job);
                return Task.CompletedTask;
            });
        return new EbillingNotificationEnqueuer(queue.Object, logger ?? NullLogger<EbillingNotificationEnqueuer>.Instance);
    }

    [Fact(DisplayName = "132: one job per (event, subject): the key, the subject id and the payload are fixed by the pair")]
    public async Task Enqueue_KeysTheJobOnTheEventAndTheSubject()
    {
        await Enqueuer().EnqueueAsync(EbillingNotificationEvent.InvoiceRejected, EbillingNotificationSubject.Invoice, Invoice, "Your invoice", "We cannot approve it.", "corr-1");

        var job = _jobs.Should().ContainSingle().Subject;
        job.JobType.Should().Be(EbillingNotificationEnqueuer.JobTypeName);
        job.SubjectId.Should().Be(Invoice.ToString("D"));
        job.IdempotencyKey.Should().Be($"ebilling-notify:InvoiceRejected:{Invoice:N}");
        job.CorrelationId.Should().Be("corr-1");
        var payload = EbillingNotificationEnqueuer.ParsePayload(job.Payload)!;
        payload.Event.Should().Be(EbillingNotificationEvent.InvoiceRejected);
        payload.Subject.Should().Be(EbillingNotificationSubject.Invoice);
        payload.NoticeSubject.Should().Be("Your invoice");
        payload.NoticeProse.Should().Be("We cannot approve it.");
        payload.HasConfirmedNotice.Should().BeTrue();
        EbillingNotificationEnqueuer.MarkerFor(EbillingNotificationEvent.InvoiceRejected, Invoice).Should().Be($"ebilling-notified:InvoiceRejected:{Invoice:N}");
    }

    [Fact(DisplayName = "132: two events of one invoice have two keys, and two subjects of one event have two keys (a Return and a later Reject never collide)")]
    public void Keys_DifferByEventAndBySubject()
    {
        EbillingNotificationEnqueuer.IdempotencyKeyFor(EbillingNotificationEvent.ReturnedToFirm, Invoice)
            .Should().NotBe(EbillingNotificationEnqueuer.IdempotencyKeyFor(EbillingNotificationEvent.InvoiceRejected, Invoice));
        EbillingNotificationEnqueuer.IdempotencyKeyFor(EbillingNotificationEvent.InvoiceAccepted, Invoice)
            .Should().NotBe(EbillingNotificationEnqueuer.IdempotencyKeyFor(EbillingNotificationEvent.InvoiceAccepted, Submission));
        EbillingNotificationEnqueuer.MarkerFor(EbillingNotificationEvent.ReturnedToFirm, Invoice)
            .Should().NotBe(EbillingNotificationEnqueuer.IdempotencyKeyFor(EbillingNotificationEvent.ReturnedToFirm, Invoice), "the marker and the message id are different keys");
    }

    [Fact(DisplayName = "132: a failing queue never fails the decision: Enqueue returns, and the Warning carries ids and the exception TYPE only")]
    public async Task Enqueue_SwallowsAQueueFault_AndLogsIdsOnly()
    {
        _fault = new InvalidOperationException("secret detail: mailbox pat@firm.test");
        var log = new List<string>();
        var logger = new Mock<ILogger<EbillingNotificationEnqueuer>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback((IInvocation i) => log.Add(i.Arguments[2]!.ToString()!));

        var act = async () => await Enqueuer(logger.Object).EnqueueAsync(EbillingNotificationEvent.ApprovedWithAdjustments, EbillingNotificationSubject.Invoice, Invoice);

        await act.Should().NotThrowAsync();
        log.Should().ContainSingle().Which.Should().Contain(Invoice.ToString()).And.Contain(nameof(InvalidOperationException)).And.NotContain("pat@firm.test").And.NotContain("secret detail");
    }

    [Theory(DisplayName = "132: a confirmed notice rides only on a Return or a Reject; for any other event it is dropped")]
    [InlineData(EbillingNotificationEvent.ReturnedToFirm, true)]
    [InlineData(EbillingNotificationEvent.InvoiceRejected, true)]
    [InlineData(EbillingNotificationEvent.ApprovedWithAdjustments, false)]
    [InlineData(EbillingNotificationEvent.SubmissionRejected, false)]
    [InlineData(EbillingNotificationEvent.InvoiceAccepted, false)]
    public async Task ConfirmedNotice_IsKeptForReturnAndRejectOnly(EbillingNotificationEvent evt, bool kept)
    {
        await Enqueuer().EnqueueAsync(evt, EbillingNotificationSubject.Invoice, Invoice, "Subject", "Prose");

        var payload = EbillingNotificationEnqueuer.ParsePayload(_jobs.Single().Payload)!;
        payload.HasConfirmedNotice.Should().Be(kept);
    }

    [Theory(DisplayName = "132: an incomplete or over-long notice is never queued (no firm email then), and the job is still enqueued")]
    [InlineData("Subject only", null)]
    [InlineData(null, "Prose only")]
    [InlineData("   ", "Prose")]
    public async Task IncompleteNotice_IsDropped(string? subject, string? prose)
    {
        await Enqueuer().EnqueueAsync(EbillingNotificationEvent.ReturnedToFirm, EbillingNotificationSubject.Invoice, Invoice, subject, prose);

        var payload = EbillingNotificationEnqueuer.ParsePayload(_jobs.Single().Payload)!;
        payload.HasConfirmedNotice.Should().BeFalse();
        payload.NoticeSubject.Should().BeNull();
        payload.NoticeProse.Should().BeNull();
    }

    [Fact(DisplayName = "132: a notice over the screen's own length limits is not queued (a request body cannot reach the broker's message limit)")]
    public async Task OverLongNotice_IsDropped()
    {
        await Enqueuer().EnqueueAsync(
            EbillingNotificationEvent.InvoiceRejected, EbillingNotificationSubject.Invoice, Invoice,
            new string('s', EbillingNotificationEnqueuer.MaxQueuedSubjectLength + 1), "Prose");
        await Enqueuer().EnqueueAsync(
            EbillingNotificationEvent.ReturnedToFirm, EbillingNotificationSubject.Invoice, Invoice,
            "Subject", new string('p', FirmNoticeProse.MaxProseLength + 1));

        _jobs.Should().HaveCount(2);
        _jobs.Select(j => EbillingNotificationEnqueuer.ParsePayload(j.Payload)!.HasConfirmedNotice).Should().AllBeEquivalentTo(false);
    }

    [Fact(DisplayName = "132: a payload that is missing, malformed, of an unknown event or half a notice is not a payload")]
    public void ParsePayload_RefusesWhatItCannotTrust()
    {
        EbillingNotificationEnqueuer.ParsePayload(null).Should().BeNull();
        EbillingNotificationEnqueuer.ParsePayload(JsonDocument.Parse("""{"event":"Nonsense","subject":"Invoice"}""")).Should().BeNull();
        EbillingNotificationEnqueuer.ParsePayload(JsonDocument.Parse("""[1,2]""")).Should().BeNull();
        EbillingNotificationEnqueuer.ParsePayload(JsonDocument.Parse("""{"event":"ReturnedToFirm","subject":"Invoice","noticeSubject":"only a subject"}""")).Should().BeNull();
        EbillingNotificationEnqueuer.ParsePayload(JsonDocument.Parse("""{"event":"ReturnedToFirm","subject":"Invoice"}""")).Should().NotBeNull();
    }

    // ── The sink: the submission pipeline's report, mapped onto the job ──

    private (EbillingNotificationSink Sink, Func<(EbillingNotificationEvent Event, EbillingNotificationSubject Subject, Guid Id)[]> Sent) SinkOverEnqueuer()
    {
        var sink = new EbillingNotificationSink(Enqueuer(), NullLogger<EbillingNotificationSink>.Instance);
        return (sink, () => _jobs.Select(j =>
        {
            var p = EbillingNotificationEnqueuer.ParsePayload(j.Payload)!;
            return (p.Event, p.Subject, Guid.Parse(j.SubjectId));
        }).ToArray());
    }

    [Fact(DisplayName = "132 sink: Accepted is the approvers' event on the invoice; Rejected is the firm's on the submission; Returned is the firm's on the invoice with no notice")]
    public async Task Sink_MapsEachOutcome()
    {
        var (sink, sent) = SinkOverEnqueuer();

        await sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(Submission, SubmissionDecisionOutcome.Accepted, InvoiceSubmissionChannel.Spa, Invoice, null));
        await sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(Submission, SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Spa, null, "ebilling.submission.rate_above_approved"));
        await sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(Submission, SubmissionDecisionOutcome.Returned, InvoiceSubmissionChannel.Internal, Invoice, null));

        sent().Should().Equal(
            (EbillingNotificationEvent.InvoiceAccepted, EbillingNotificationSubject.Invoice, Invoice),
            (EbillingNotificationEvent.SubmissionRejected, EbillingNotificationSubject.Submission, Submission),
            (EbillingNotificationEvent.ReturnedToFirm, EbillingNotificationSubject.Invoice, Invoice));
        _jobs.Select(j => EbillingNotificationEnqueuer.ParsePayload(j.Payload)!.HasConfirmedNotice).Should().AllBeEquivalentTo(false, "105's Return carries no reviewer-confirmed notice");
    }

    [Fact(DisplayName = "132 sink: a blocked submission (held for review) is reported as Rejected by the pipeline but is NOT an outcome the firm is told")]
    public async Task Sink_DoesNotTellTheFirmAboutAHeldSubmission()
    {
        var (sink, sent) = SinkOverEnqueuer();

        await sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(Submission, SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Internal, null, SubmissionReasonCodes.Blocked));

        sent().Should().BeEmpty();
    }

    [Theory(DisplayName = "132 sink: an Accepted or Returned report that names no invoice enqueues nothing (there is nobody to route it to)")]
    [InlineData(SubmissionDecisionOutcome.Accepted)]
    [InlineData(SubmissionDecisionOutcome.Returned)]
    public async Task Sink_NeedsAnInvoiceForAcceptedAndReturned(SubmissionDecisionOutcome outcome)
    {
        var (sink, sent) = SinkOverEnqueuer();

        await sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(Submission, outcome, InvoiceSubmissionChannel.Spa, null, null));

        sent().Should().BeEmpty();
    }
}
