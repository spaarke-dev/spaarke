using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// ADR-004 job that tells people what happened to an invoice (ebilling-r1 task 132; FR-39, FR-35, FR-50, D-45, EB-D-254, EB-D-258): ONE job per
/// (subject id, <see cref="EbillingNotificationEvent"/>). A firm event sends one email to the submitter and the firm's billing contact; an accepted
/// invoice creates one in-app notification per eligible approver.
/// </summary>
/// <remarks>
/// <para><b>Who is told what.</b> <see cref="EbillingNotificationEvent.SubmissionRejected"/> and
/// <see cref="EbillingNotificationEvent.ApprovedWithAdjustments"/> send the notice composed by 131's <see cref="FirmNoticeComposer"/> unchanged (an approval
/// without an adjustment composes nothing, so no email). <see cref="EbillingNotificationEvent.ReturnedToFirm"/> and
/// <see cref="EbillingNotificationEvent.InvoiceRejected"/> send an email ONLY when the decision carried the reviewer-confirmed subject and prose
/// (EB-D-258): it is re-composed through <see cref="FirmNoticeComposer.ComposeConfirmedForInvoiceAsync"/> (the amounts, lines, clause quotes and table are
/// inserted by code, the prose goes through the same screen as model prose, and only <see cref="FirmNoticeComposer.RenderHtml"/> renders it). Without a
/// confirmed notice, or when the screen refuses it, NO email is sent: the job logs ids and the reason and completes. The template is never substituted.
/// <see cref="EbillingNotificationEvent.InvoiceAccepted"/> notifies the eligible approvers through <see cref="IActionSeam.CreateNotificationAsync"/>
/// (ADR-047: an appnotification, no new notification kind).</para>
/// <para><b>At-most-once (the marker is written BEFORE the send).</b> The marker <see cref="EbillingNotificationEnqueuer.MarkerFor"/> is written, and read
/// back, AFTER the email is composed and the recipients are known and BEFORE anything is sent. A redelivery finds it and sends nothing. The cost of that
/// order is deliberate: a crash between the marker and the send loses the email (logged as an Error on a send fault; never silently sent twice), where
/// the opposite order would send a firm the same notice twice. The idempotency cache is not atomic and fails open on a cache fault, so the read-back
/// decides: when the marker cannot be read back, nothing is sent and the job is retried. Sending never starts before a marker is visible.</para>
/// <para><b>Not yet ready.</b> The submission pipeline reports a decision just before it writes the terminal status. A rejected submission still
/// Received or Processing is waited for briefly (<see cref="ReadyPollAttempts"/> x <see cref="ReadyPollInterval"/>), then the job fails and is retried:
/// it never decides "no notice" from a status that is still moving.</para>
/// <para><b>Failures.</b> A read fault, a record not found yet, a composer that cannot say (Unavailable) and an unreadable marker return
/// <see cref="JobOutcome.Failure"/> until <see cref="JobContract.IsFinalDelivery"/>, then <see cref="JobOutcome.Poisoned"/>. A send fault after the marker is
/// Poisoned at once (not retried). A bad payload is Poisoned. The kill switch (<see cref="EbillingOptions.Enabled"/>, ADR-018) is read here: off, the
/// job completes and sends nothing.</para>
/// <para><b>Logging (ADR-015).</b> Ids, counts and stable codes only: never an address, a subject, prose or the notice text.</para>
/// <para>§11: <i>Existing</i> — <see cref="FirmNoticeComposer"/> (131) and <see cref="CommunicationService"/> (the shared-mailbox send), <see cref="IActionSeam"/>
/// (the appnotification writer), <see cref="EligibleApproverResolver"/> (103). <i>Extension</i> — none of them is a job that decides who is told and when;
/// this is that composition. <i>Cost of doing nothing</i> — a firm hears nothing about a rejected, returned or adjusted invoice and no approver is told one is waiting (FR-39, FR-35).</para>
/// </remarks>
internal class EbillingNotificationJobHandler : IJobHandler
{
    /// <summary>Job type constant: must match <see cref="EbillingNotificationEnqueuer.JobTypeName"/>.</summary>
    public const string JobTypeName = EbillingNotificationEnqueuer.JobTypeName;

    /// <summary>How long a marker is kept: beyond any redelivery the broker or a replay can produce.</summary>
    internal static readonly TimeSpan MarkerLifetime = TimeSpan.FromDays(30);

    /// <summary>The appnotification category of the approver notice: its own, so it never collides with the submitter's bell ("invoice", 095b).</summary>
    internal const string ApproverCategory = "invoice-review";

    /// <summary>The Invoice Review Code Page (110), opened on the invoice.</summary>
    internal static string ReviewUrlFor(Guid invoiceId) =>
        $"/main.aspx?pagetype=webresource&webresourceName=sprk_invoicereviewpage&data=invoiceId%3D{invoiceId:D}";

    /// <summary><c>sprk_invoicesubmission.sprk_firmnoticetext</c> MaxLength.</summary>
    internal const int FirmNoticeTextMaxLength = 100_000;

    internal const string FirmNoticeTextColumn = "sprk_firmnoticetext";

    private readonly FirmNoticeComposer _composer;
    private readonly EbillingNotificationRecipients _recipients;
    private readonly CommunicationService _communications;
    private readonly EligibleApproverResolver _approvers;
    private readonly IActionSeam _actions;
    private readonly IIdempotencyService _idempotency;
    private readonly IGenericEntityService _entities;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly ILogger<EbillingNotificationJobHandler> _logger;

    /// <summary>How many times a submission that is still Received or Processing is looked at before the job is retried. Replaceable by the test assembly.</summary>
    internal int ReadyPollAttempts { get; init; } = 5;

    /// <summary>The wait between those looks. Replaceable by the test assembly.</summary>
    internal TimeSpan ReadyPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The wait itself. Replaceable by the test assembly.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (interval, token) => Task.Delay(interval, token);

    public EbillingNotificationJobHandler(
        FirmNoticeComposer composer,
        EbillingNotificationRecipients recipients,
        CommunicationService communications,
        EligibleApproverResolver approvers,
        IActionSeam actions,
        IIdempotencyService idempotency,
        IGenericEntityService entities,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<EbillingNotificationJobHandler> logger)
    {
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _recipients = recipients ?? throw new ArgumentNullException(nameof(recipients));
        _communications = communications ?? throw new ArgumentNullException(nameof(communications));
        _approvers = approvers ?? throw new ArgumentNullException(nameof(approvers));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = EbillingNotificationEnqueuer.ParsePayload(job.Payload);
        if (payload is null || !Guid.TryParse(job.SubjectId, out var subjectId) || subjectId == Guid.Empty
            || payload.Subject != ExpectedSubject(payload.Event))
        {
            _logger.LogError("eBilling notification job {JobId}: the payload or the subject id is missing or does not fit the event", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
        }

        if (!_ebilling.CurrentValue.Enabled)
        {
            _logger.LogInformation("eBilling notification job {JobId}: eBilling is switched off; nothing is sent | Event {Event}, Subject {SubjectId}", job.JobId, payload.Event, subjectId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        try
        {
            _logger.LogInformation(
                "eBilling notification job {JobId} | Event {Event}, Subject {SubjectId}, attempt {Attempt}, correlation {CorrelationId}",
                job.JobId, payload.Event, subjectId, job.Attempt, job.CorrelationId);

            return payload.Event == EbillingNotificationEvent.InvoiceAccepted
                ? await NotifyApproversAsync(job, subjectId, stopwatch, ct).ConfigureAwait(false)
                : await NotifyFirmAsync(job, payload, subjectId, stopwatch, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A read or marker fault BEFORE anything was sent (the send has its own handling): retry.
            _logger.LogWarning(
                "eBilling notification job {JobId} could not complete | Event {Event}, Subject {SubjectId}, ExceptionType {ExceptionType}",
                job.JobId, payload.Event, subjectId, ex.GetType().Name);
            return Transient(job, ex.GetType().Name, stopwatch);
        }
    }

    private static EbillingNotificationSubject ExpectedSubject(EbillingNotificationEvent evt) =>
        evt == EbillingNotificationEvent.SubmissionRejected ? EbillingNotificationSubject.Submission : EbillingNotificationSubject.Invoice;

    // ── Firm email ──────────────────────────────────────────────────────────────────────────────────

    private async Task<JobOutcome> NotifyFirmAsync(
        JobContract job, EbillingNotificationPayload payload, Guid subjectId, Stopwatch stopwatch, CancellationToken ct)
    {
        var evt = payload.Event;

        // EB-D-258: a Return or Reject tells the firm only what the reviewer confirmed. Nothing confirmed, nothing sent, nothing read.
        if (evt is EbillingNotificationEvent.ReturnedToFirm or EbillingNotificationEvent.InvoiceRejected && !payload.HasConfirmedNotice)
        {
            _logger.LogInformation(
                "eBilling notification job {JobId}: no reviewer-confirmed notice, so no firm email is sent | Event {Event}, Subject {SubjectId}",
                job.JobId, evt, subjectId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var marker = EbillingNotificationEnqueuer.MarkerFor(evt, subjectId);
        if (await _idempotency.IsEventProcessedAsync(marker, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("eBilling notification job {JobId}: already handled; nothing is sent | Event {Event}, Subject {SubjectId}", job.JobId, evt, subjectId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var facts = await ReadFactsWhenReadyAsync(evt, subjectId, ct).ConfigureAwait(false);
        if (facts is null)
        {
            return Transient(job, "the subject is not readable yet", stopwatch);
        }

        if (evt == EbillingNotificationEvent.SubmissionRejected && facts.SubmissionStatus != (int)InvoiceSubmissionStatus.Rejected)
        {
            if (facts.SubmissionStatus is (int)InvoiceSubmissionStatus.Received or (int)InvoiceSubmissionStatus.Processing)
            {
                return Transient(job, "the submission has not reached a final status yet", stopwatch);
            }

            // Accepted (it was overridden) or Failed: not a rejection the firm is told.
            _logger.LogInformation(
                "eBilling notification job {JobId}: the submission is not rejected now, so no firm email is sent | Event {Event}, Subject {SubjectId}, Status {Status}",
                job.JobId, evt, subjectId, facts.SubmissionStatus);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        // Compose. The job decides nothing about the content: the composer's checks and the projection's data are the only source.
        var composed = await ComposeAsync(job, payload, subjectId, ct).ConfigureAwait(false);
        if (composed.Outcome is not null)
        {
            return composed.Outcome;
        }

        var draft = composed.Draft!;
        var recipients = await _recipients.ResolveFirmRecipientsAsync(facts, ExpectedSubject(evt), ct).ConfigureAwait(false);
        if (recipients.BillingContactMissing)
        {
            _logger.LogWarning(
                "eBilling notification job {JobId}: the firm has no billing contact with a usable address; the submitter alone is told | Event {Event}, Subject {SubjectId}, Firm {FirmId}",
                job.JobId, evt, subjectId, facts.FirmId);
        }
        else if (recipients.BillingContactDenied)
        {
            // Ids only (ADR-015): the firm has a billing contact, but the outcome route's row rule refused it, so only the others are told.
            _logger.LogInformation(
                "eBilling notification job {JobId}: the billing contact was not told because the row rule denied it | Event {Event}, Subject {SubjectId}, Firm {FirmId}",
                job.JobId, evt, subjectId, facts.FirmId);
        }

        if (recipients.Addresses.Count == 0)
        {
            _logger.LogWarning(
                "eBilling notification job {JobId}: nobody has a usable address, so no firm email is sent | Event {Event}, Subject {SubjectId}, Firm {FirmId}",
                job.JobId, evt, subjectId, facts.FirmId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        // The marker, BEFORE the send, and read back: the cache is not atomic and fails open, so an unreadable marker means nothing is sent.
        if (!await WriteMarkerAsync(marker, ct).ConfigureAwait(false))
        {
            _logger.LogWarning("eBilling notification job {JobId}: the at-most-once marker could not be read back; nothing is sent, the job is retried | Event {Event}, Subject {SubjectId}", job.JobId, evt, subjectId);
            return Transient(job, "the at-most-once marker could not be written", stopwatch);
        }

        try
        {
            await _communications.SendAsync(
                new SendCommunicationRequest
                {
                    To = recipients.Addresses.ToArray(),
                    Subject = draft.Subject,
                    Body = draft.Html,
                    BodyFormat = BodyFormat.HTML,
                    SendMode = SendMode.SharedMailbox,
                    CommunicationType = CommunicationType.Email,
                    IsInternalOnly = true,
                    CorrelationId = job.CorrelationId,
                    Associations = facts.InvoiceId is { } invoiceId
                        ? [new CommunicationAssociation { EntityType = InvoiceReviewReadService.InvoiceEntity, EntityId = invoiceId }]
                        : null,
                },
                httpContext: null,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdapProblemException ex) when (SendProvablyDidNotGoOut(ex))
        {
            // The email did not go out (a refusal before the send, or Graph's settled refusal): the marker would make the retry drop it, so it is
            // forgotten and the job is retried. If the marker cannot be shown gone, a retry would silently drop the notice: poison it instead.
            var forgotten = await ForgetMarkerAsync(marker, ct).ConfigureAwait(false);
            _logger.LogWarning(
                "eBilling notification job {JobId}: the firm email was not sent and is {Next} | Event {Event}, Subject {SubjectId}, Code {Code}, Status {Status}",
                job.JobId, forgotten ? "retried" : "NOT retried (the marker could not be cleared)", evt, subjectId, ex.Code, ex.StatusCode);
            return forgotten
                ? Transient(job, "the firm email was not sent", stopwatch)
                : JobOutcome.Poisoned(job.JobId, JobType, "The firm email was not sent and its marker could not be cleared", job.Attempt, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            // The marker is written and the outcome is AMBIGUOUS (the message may have been accepted), so this notice is NOT retried (at-most-once): the
            // operator reads the dead-letter queue.
            _logger.LogError(
                "eBilling notification job {JobId}: the firm email was not sent after the marker was written | Event {Event}, Subject {SubjectId}, ExceptionType {ExceptionType}",
                job.JobId, evt, subjectId, ex.GetType().Name);
            return JobOutcome.Poisoned(job.JobId, JobType, "The firm email was not sent", job.Attempt, stopwatch.Elapsed);
        }

        _logger.LogInformation(
            "eBilling notification job {JobId}: firm email sent | Event {Event}, Subject {SubjectId}, Recipients {Count}, Source {Source}",
            job.JobId, evt, subjectId, recipients.Addresses.Count, draft.Source);

        await StoreSentTextAsync(job, facts.SubmissionId, draft, ct).ConfigureAwait(false);
        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
    }

    /// <summary>The facts of the subject. A rejected submission still Received or Processing is waited for briefly: its status is written just after the decision is reported.</summary>
    private async Task<EbillingNotificationFacts?> ReadFactsWhenReadyAsync(EbillingNotificationEvent evt, Guid subjectId, CancellationToken ct)
    {
        var subject = ExpectedSubject(evt);
        var facts = await _recipients.ReadFactsAsync(subject, subjectId, ct).ConfigureAwait(false);
        for (var attempt = 1; evt == EbillingNotificationEvent.SubmissionRejected && attempt < ReadyPollAttempts
            && facts?.SubmissionStatus is (int)InvoiceSubmissionStatus.Received or (int)InvoiceSubmissionStatus.Processing; attempt++)
        {
            await Delay(ReadyPollInterval, ct).ConfigureAwait(false);
            facts = await _recipients.ReadFactsAsync(subject, subjectId, ct).ConfigureAwait(false);
        }

        return facts;
    }

    private sealed record Composition(FirmNoticeDraft? Draft, JobOutcome? Outcome);

    private async Task<Composition> ComposeAsync(JobContract job, EbillingNotificationPayload payload, Guid subjectId, CancellationToken ct)
    {
        var evt = payload.Event;
        var sw = Stopwatch.StartNew();

        if (evt is EbillingNotificationEvent.ReturnedToFirm or EbillingNotificationEvent.InvoiceRejected)
        {
            var kind = evt == EbillingNotificationEvent.ReturnedToFirm ? FirmNoticeKind.ReturnedToFirm : FirmNoticeKind.InvoiceRejected;
            var confirmed = await _composer.ComposeConfirmedForInvoiceAsync(kind, subjectId, payload.NoticeSubject, payload.NoticeProse, ct).ConfigureAwait(false);
            switch (confirmed.Status)
            {
                case ConfirmedFirmNoticeStatus.Composed:
                    return new Composition(confirmed.Draft, null);

                case ConfirmedFirmNoticeStatus.ProseRefused:
                    // The decision stands and no email goes (EB-D-258). The code is the reason; the text is never logged.
                    _logger.LogWarning(
                        "eBilling notification job {JobId}: the reviewer's notice was refused by the screen, so no firm email is sent | Event {Event}, Subject {SubjectId}, Reason {Reason}",
                        job.JobId, evt, subjectId, confirmed.RefusalReason);
                    return new Composition(null, JobOutcome.Success(job.JobId, JobType, sw.Elapsed));

                case ConfirmedFirmNoticeStatus.NotApplicable:
                    _logger.LogInformation("eBilling notification job {JobId}: the invoice is no longer in this state, so no firm email is sent | Event {Event}, Subject {SubjectId}", job.JobId, evt, subjectId);
                    return new Composition(null, JobOutcome.Success(job.JobId, JobType, sw.Elapsed));

                default:
                    return new Composition(null, Transient(job, $"the notice could not be composed ({confirmed.Status})", sw));
            }
        }

        var result = evt == EbillingNotificationEvent.SubmissionRejected
            ? await _composer.ComposeForSubmissionAsync(subjectId, invoiceDecided: false, ct).ConfigureAwait(false)
            : await _composer.ComposeForInvoiceAsync(FirmNoticeKind.ApprovedWithAdjustments, subjectId, ct).ConfigureAwait(false);

        switch (result.Status)
        {
            case FirmNoticeStatus.Composed:
                return new Composition(result.Draft, null);

            case FirmNoticeStatus.NotApplicable:
                // The one place "no adjustment, no email" is decided: the composer's applicability over the projection (an approval without adjustments).
                _logger.LogInformation("eBilling notification job {JobId}: there is nothing to tell the firm, so no email is sent | Event {Event}, Subject {SubjectId}", job.JobId, evt, subjectId);
                return new Composition(null, JobOutcome.Success(job.JobId, JobType, sw.Elapsed));

            default:
                return new Composition(null, Transient(job, $"the notice could not be composed ({result.Status})", sw));
        }
    }

    /// <summary>
    /// The stored shape of the sent notice (EB-D-259: the prose stays recoverable on its own, apart from the HTML, for the SPA's firm-safe reason).
    /// One JSON object in the existing <c>sprk_firmnoticetext</c> column: <c>v</c> (1), <c>source</c> (<see cref="FirmNoticeSource"/> name: Action, Template or
    /// Reviewer), <c>kind</c> (<see cref="FirmNoticeKind"/> name), <c>subject</c> (as sent), <c>prose</c> (the screened prose, plain text, NOT html),
    /// <c>html</c> (the body as sent) and <c>htmlOmitted</c> (true only when the html would not fit the column; subject and prose are always kept).
    /// </summary>
    internal static string SentNoticeJson(FirmNoticeDraft draft)
    {
        string Build(string? html) => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["source"] = draft.Source.ToString(),
            ["kind"] = draft.Kind.ToString(),
            ["subject"] = draft.Subject,
            ["prose"] = draft.Prose,
            ["html"] = html,
            ["htmlOmitted"] = html is null,
        });

        var json = Build(draft.Html);
        if (json.Length <= FirmNoticeTextMaxLength)
        {
            return json;
        }

        // Too big for the column: drop the html (the prose and subject are what a reader needs; both are bounded by the caps and the composer).
        return Build(null);
    }

    /// <summary>The sent notice is kept with the submission (FR-50) as <see cref="SentNoticeJson"/>. A fault is a Warning: the email is out.</summary>
    private async Task StoreSentTextAsync(JobContract job, Guid? submissionId, FirmNoticeDraft draft, CancellationToken ct)
    {
        if (submissionId is not { } id || id == Guid.Empty)
        {
            // The invoice's OWN submission could not be determined (none linked, or none that derives the invoice): nothing is written to any
            // other submission. The email is out; the notice is simply not kept.
            _logger.LogWarning(
                "eBilling notification job {JobId}: the invoice's own submission could not be determined, so the sent notice is not stored | JobType {JobType}",
                job.JobId, job.JobType);
            return;
        }

        try
        {
            var text = SentNoticeJson(draft);

            await _entities.UpdateAsync(
                InvoiceReviewReadService.SubmissionEntity, id,
                new Dictionary<string, object> { [FirmNoticeTextColumn] = text }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "eBilling notification job {JobId}: the sent notice was not stored on the submission | Submission {SubmissionId}, ExceptionType {ExceptionType}",
                job.JobId, id, ex.GetType().Name);
        }
    }

    // ── Approvers ───────────────────────────────────────────────────────────────────────────────────

    private async Task<JobOutcome> NotifyApproversAsync(JobContract job, Guid invoiceId, Stopwatch stopwatch, CancellationToken ct)
    {
        const EbillingNotificationEvent evt = EbillingNotificationEvent.InvoiceAccepted;
        var marker = EbillingNotificationEnqueuer.MarkerFor(evt, invoiceId);
        if (await _idempotency.IsEventProcessedAsync(marker, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("eBilling notification job {JobId}: already handled; nobody is notified again | Event {Event}, Subject {SubjectId}", job.JobId, evt, invoiceId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var facts = await _recipients.ReadFactsAsync(EbillingNotificationSubject.Invoice, invoiceId, ct).ConfigureAwait(false);
        if (facts is null)
        {
            return Transient(job, "the invoice is not readable yet", stopwatch);
        }

        if (facts.InvoiceStatus != InvoiceStatusCodes.InReview)
        {
            // Already decided (or replaced): nobody is waiting on it.
            _logger.LogInformation(
                "eBilling notification job {JobId}: the invoice is not In Review now, so the approvers are not notified | Subject {SubjectId}, Status {Status}",
                job.JobId, invoiceId, facts.InvoiceStatus);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        if (facts.CoreEntity is null || facts.CoreId is not { } coreId)
        {
            _logger.LogWarning("eBilling notification job {JobId}: the invoice has no single core record, so its approvers cannot be resolved | Subject {SubjectId}", job.JobId, invoiceId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var set = await _approvers.ResolveAsync(facts.CoreEntity, coreId, ct).ConfigureAwait(false);
        var people = set.Approvers.Where(a => a.SystemUserId != Guid.Empty).GroupBy(a => a.SystemUserId).Select(g => g.First()).ToList();
        if (people.Count == 0)
        {
            _logger.LogWarning(
                "eBilling notification job {JobId}: no eligible approver has a linked user, so nobody is notified | Subject {SubjectId}, Core {CoreEntity} {CoreId}",
                job.JobId, invoiceId, facts.CoreEntity, coreId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        if (!await WriteMarkerAsync(marker, ct).ConfigureAwait(false))
        {
            _logger.LogWarning("eBilling notification job {JobId}: the at-most-once marker could not be read back; nobody is notified, the job is retried | Subject {SubjectId}", job.JobId, invoiceId);
            return Transient(job, "the at-most-once marker could not be written", stopwatch);
        }

        var firmName = await ReadFirmNameQuietlyAsync(facts.FirmId, ct).ConfigureAwait(false);
        var tracking = FirmNoticeComposer.CleanTrackingNumber(facts.TrackingNumber);
        var what = tracking.Length == 0 ? "An invoice" : $"Invoice {tracking}";
        var from = string.IsNullOrEmpty(firmName) ? string.Empty : $" from {firmName}";

        var created = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var approver in people)
        {
            try
            {
                var result = await _actions.CreateNotificationAsync(
                    new CreateNotificationRequest
                    {
                        Title = "Invoice ready for your review",
                        Body = $"{what}{from} is ready for your review.",
                        RecipientId = approver.SystemUserId,
                        Category = ApproverCategory,
                        ActionUrl = ReviewUrlFor(invoiceId),
                        RegardingId = invoiceId,
                        RegardingType = InvoiceReviewReadService.InvoiceEntity,
                        SourceEntityType = InvoiceReviewReadService.InvoiceEntity,
                        SourceId = invoiceId.ToString("D"),
                        Source = "ebilling",
                        CorrelationId = job.CorrelationId,
                    },
                    ct).ConfigureAwait(false);
                if (result.Success && result.Skipped)
                {
                    skipped++;
                }
                else if (result.Success)
                {
                    created++;
                }
                else
                {
                    failed++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(
                    "eBilling notification job {JobId}: an approver was not notified | Subject {SubjectId}, ExceptionType {ExceptionType}",
                    job.JobId, invoiceId, ex.GetType().Name);
            }
        }

        _logger.LogInformation(
            "eBilling notification job {JobId}: approvers notified | Subject {SubjectId}, Created {Created}, Skipped {Skipped}, Failed {Failed}",
            job.JobId, invoiceId, created, skipped, failed);
        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
    }

    private async Task<string?> ReadFirmNameQuietlyAsync(Guid? firmId, CancellationToken ct)
    {
        try
        {
            var name = await _recipients.ReadFirmNameAsync(firmId, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(name) ? null : FirmNoticeComposer.CleanTrackingNumber(name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null; // the wording is complete without the firm's name
        }
    }

    // ── Shared ──────────────────────────────────────────────────────────────────────────────────────

    private const string GraphSendFailedCode = "GRAPH_SEND_FAILED";

    /// <summary>
    /// The typed codes of the send path's own refusals that are raised BEFORE the channel send (so the message provably did not go out): a request
    /// that fails validation, an unapproved or unset sender, the daily send limit, and a regarding association that could not be derived while the
    /// record is prepared. An ALLOW-LIST (review K4): a typed failure with any other code is not assumed to be pre-send.
    /// </summary>
    internal static readonly IReadOnlySet<string> PreSendRefusalCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "VALIDATION_ERROR", "INVALID_SENDER", "NO_DEFAULT_SENDER", "DAILY_SEND_LIMIT_REACHED", "CORE_ANCESTOR_DERIVATION_FAILED",
    };

    /// <summary>
    /// True when a typed send failure is KNOWN to have happened before Graph accepted the message. Two kinds only: one of the send path's own
    /// refusals before the channel (<see cref="PreSendRefusalCodes"/>, an allow-list), or Graph's own settled refusal (<c>GRAPH_SEND_FAILED</c> whose
    /// <see cref="EmailChannelSender.SendOutcomeExtension"/> is <c>NotSent</c>). <c>Unknown</c> (a timeout, a transport fault, a 5xx, a 408), a failure
    /// that carries no outcome and any code not listed are ambiguous, because the message may have been delivered: the notice is not sent again.
    /// </summary>
    internal static bool SendProvablyDidNotGoOut(SdapProblemException ex)
    {
        if (!string.Equals(ex.Code, GraphSendFailedCode, StringComparison.Ordinal))
        {
            return PreSendRefusalCodes.Contains(ex.Code);
        }

        return ex.Extensions is not null
            && ex.Extensions.TryGetValue(EmailChannelSender.SendOutcomeExtension, out var outcome)
            && string.Equals(outcome as string, EmailChannelSender.SendOutcomeNotSent, StringComparison.Ordinal);
    }

    /// <summary>Removes the marker and reads it back as gone; false when that could not be shown.</summary>
    private async Task<bool> ForgetMarkerAsync(string marker, CancellationToken ct)
    {
        try
        {
            return await _idempotency.ClearEventProcessedAsync(marker, ct).ConfigureAwait(false)
                && !await _idempotency.IsEventProcessedAsync(marker, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Writes the marker and reads it back; false when it is not visible afterwards (a cache that fails open).</summary>
    private async Task<bool> WriteMarkerAsync(string marker, CancellationToken ct)
    {
        await _idempotency.MarkEventAsProcessedAsync(marker, MarkerLifetime, ct).ConfigureAwait(false);
        return await _idempotency.IsEventProcessedAsync(marker, ct).ConfigureAwait(false);
    }

    private static JobOutcome Transient(JobContract job, string reason, Stopwatch stopwatch) =>
        job.IsFinalDelivery
            ? JobOutcome.Poisoned(job.JobId, JobTypeName, reason, job.Attempt, stopwatch.Elapsed)
            : JobOutcome.Failure(job.JobId, JobTypeName, reason, job.Attempt, stopwatch.Elapsed);
}
