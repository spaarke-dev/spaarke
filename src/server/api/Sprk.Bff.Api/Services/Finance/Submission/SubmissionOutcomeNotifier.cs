using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>How an invoice submission ended, as the person who submitted it is told (ebilling-r1 task 095b).</summary>
public enum SubmissionOutcomeKind
{
    /// <summary>Processing accepted the submission: its invoice exists and is In Review.</summary>
    Accepted,

    /// <summary>The submission Failed (no invoice could be created, or its commit was undone): the person may submit again.</summary>
    Failed,

    /// <summary>The submission was Rejected: the document could not be read, the rejection rule refused it, or an internal one is held for review.</summary>
    Refused,
}

/// <summary>
/// Stable <see cref="EventId"/>s of the submission outcome notification (ebilling-r1 task 095b), beside the notifier like
/// <see cref="Sprk.Bff.Api.Services.Signals.AssignTaskNotifierEvents"/>. They continue that block (50390-50395) inside the billing range
/// that <c>OntologyEventIdUniquenessGuardTests</c> keeps unique. Ids only in every line (ADR-015).
/// </summary>
public static class SubmissionOutcomeNotifierEvents
{
    /// <summary>Warning: the bell notification could not be created (a read or write fault, or the time bound). The submission's outcome is unaffected.</summary>
    public static readonly EventId BellNotCreated = new(50396, nameof(BellNotCreated));

    /// <summary>Information: nobody is told, because the submission records no internal person (an external caller with no linked user).</summary>
    public static readonly EventId NoRecipient = new(50397, nameof(NoRecipient));

    /// <summary>Information: the bell notification was created.</summary>
    public static readonly EventId BellCreated = new(50398, nameof(BellCreated));
}

// =====================================================================================================================
// ebilling-r1 task 095b (owner 2026-11-03: "notification completion now"): when an invoice submission reaches a final status, the person who
// submitted it gets ONE notification in the model-driven app's bell, so a user who closed the Outlook pane still hears the outcome.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: NotificationService (the appnotification writer; GrantAccessNotifier and AssignTaskNotifier are its precedents) and
//     IEbillingNotificationSink (task 049's seam, no-op until task 132).
//   Extension: the sink cannot carry this. The after-steps call it BEFORE they write the terminal status (so it would announce an outcome
//     that is not yet committed), it never reports Failed, and it is task 132's seam for the firm, the SPA feed and the approvers, which
//     replaces its registration. NotificationService is reused unchanged; this class is only the composition (who, what text, which link)
//     and the "never fails the submission" shell.
//   Cost of doing nothing: a submitter who closes the Outlook pane (or the SPA tab) while the submission is read never learns that it was
//     accepted, refused or failed, and a failed one is not resubmitted.
//
// WHAT IT NAMES. The firm invoice number, the tracking number and one fixed outcome sentence. Never an amount, a finding or a rule's text,
// and never another person's name (the privacy constraint of the task).
//
// EXACTLY ONCE is the CALLER's, not this class's: InvoiceSubmissionProcessingJobHandler calls it only from the delivery whose own write moved
// the submission into its final status (the after-steps' row-version-conditional terminal write; the handler's own Failed / Rejected write;
// the commit gate's Failed write). A redelivery, a replay of a dead-lettered message and a dead-letter after Failed all find the submission
// already final at step 0 and return before any of those writes, so they never reach here. Notes: projects/ebilling-r1/notes/095b-*.md.
// =====================================================================================================================

/// <summary>
/// Tells the person who submitted an invoice how its submission ended, in the Dataverse in-app notification bell (ebilling-r1 task 095b).
/// Best effort: <see cref="NotifyAsync"/> never throws and never waits longer than <see cref="DefaultTimeout"/>, because the submission is
/// already final when it is called and the job's outcome, retries and status must not depend on it.
/// </summary>
public sealed class SubmissionOutcomeNotifier
{
    /// <summary>The <c>appnotification</c> category (the Daily Briefing groups by it).</summary>
    internal const string Category = "invoice";

    /// <summary>Warning priority (<c>NotificationService</c>: 200000000 informational, 200000001 warning), for a submission that did not succeed.</summary>
    private const int WarningPriority = 200000001;

    /// <summary>A number longer than this is cut, so the title stays inside <c>appnotification.title</c>'s 256.</summary>
    internal const int MaxNumberLength = 100;

    /// <summary>How long the whole notification (the read and the write) may take before it is given up; the job must not wait on a slow call.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    internal const string AcceptedSentence = "The invoice was accepted and is now in review.";
    internal const string RefusedSentence = "The submission was refused. Open it for details.";
    internal const string HeldSentence = "The submission did not pass the billing rules and is held for review.";
    internal const string FailedSentence = "The submission could not be processed. Submit it again; if it fails again, contact your administrator.";

    private static readonly string[] Columns =
    [
        RecordCreatorPerson.Column,
        TrackingNumberAllocator.TrackingNumberColumn,
        TrackingNumberAllocator.FirmInvoiceNumberColumn,
        InvoiceSubmissionPersister.SubmissionReasonCode,
    ];

    private readonly IGenericEntityService _entities;
    private readonly NotificationService _notifications;
    private readonly ILogger<SubmissionOutcomeNotifier> _logger;

    /// <summary>The bound on one notification; <see cref="DefaultTimeout"/> unless a test shortens it.</summary>
    internal TimeSpan Timeout { get; init; } = DefaultTimeout;

    public SubmissionOutcomeNotifier(
        IGenericEntityService entities,
        NotificationService notifications,
        ILogger<SubmissionOutcomeNotifier> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the bell notification for the submission's person. Call it ONLY after the final status is committed and only from the delivery
    /// that wrote it (see the file comment). Nothing escapes: a fault is a Warning with <see cref="SubmissionOutcomeNotifierEvents.BellNotCreated"/>.
    /// The calls are not cancelled with the job: the status is already written, so the person is told even if the job's token has gone.
    /// </summary>
    /// <param name="submissionId">The submission that ended.</param>
    /// <param name="outcome">How it ended.</param>
    /// <param name="invoiceId">The invoice it created, when there is one (Accepted, a rule refusal, a held one); the link opens it. Otherwise the link opens the submission.</param>
    public async Task NotifyAsync(Guid submissionId, SubmissionOutcomeKind outcome, Guid? invoiceId)
    {
        try
        {
            using var bound = new CancellationTokenSource(Timeout);
            var row = await _entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, submissionId, Columns, bound.Token).ConfigureAwait(false);

            if (row?.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id is not { } person || person == Guid.Empty)
            {
                // An external caller with no linked user: the portal has its own status view, so nobody is told here.
                _logger.LogInformation(SubmissionOutcomeNotifierEvents.NoRecipient,
                    "Invoice submission {SubmissionId} ended {Outcome}; it records no internal person, so no notification is created",
                    submissionId, outcome);
                return;
            }

            var firmNumber = Clean(row.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn));
            var tracking = Clean(row.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn));
            var held = outcome == SubmissionOutcomeKind.Refused
                && row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode) == SubmissionReasonCodes.Blocked;

            var (title, body) = Compose(outcome, held, firmNumber, tracking);
            var (entity, id, actionTitle) = invoiceId is { } invoice && invoice != Guid.Empty
                ? (FinanceBaseValues.InvoiceEntity, invoice, "Open invoice")
                : (FinanceBaseValues.SubmissionEntity, submissionId, "Open submission");

            await _notifications.CreateNotificationAsync(
                userId: person,
                title: title,
                body: body,
                category: Category,
                priority: outcome == SubmissionOutcomeKind.Accepted ? 200000000 : WarningPriority,
                actionUrl: $"/main.aspx?etn={entity}&id={id:D}&pagetype=entityrecord",
                regardingId: id,
                actionTitle: actionTitle,
                cancellationToken: bound.Token).ConfigureAwait(false);

            _logger.LogInformation(SubmissionOutcomeNotifierEvents.BellCreated,
                "Invoice submission {SubmissionId} ended {Outcome}; the person {UserId} was notified", submissionId, outcome, person);
        }
        catch (Exception ex)
        {
            // Includes the time bound and a cancellation: the submission is final and the job's outcome must not depend on this.
            _logger.LogWarning(SubmissionOutcomeNotifierEvents.BellNotCreated,
                "Invoice submission {SubmissionId} ended {Outcome}; its notification could not be created ({ExceptionType}); the outcome stands",
                submissionId, outcome, ex.GetType().Name);
        }
    }

    /// <summary>The title and body: the firm invoice number, the tracking number and one fixed sentence, nothing else.</summary>
    internal static (string Title, string Body) Compose(SubmissionOutcomeKind outcome, bool held, string? firmNumber, string? tracking)
    {
        var label = firmNumber ?? tracking;
        var subject = label is null ? "Invoice submission" : $"Invoice {label}";
        var (verb, sentence) = outcome switch
        {
            SubmissionOutcomeKind.Accepted => ("accepted", AcceptedSentence),
            SubmissionOutcomeKind.Failed => ("could not be processed", FailedSentence),
            _ => held ? ("held for review", HeldSentence) : ("refused", RefusedSentence),
        };

        var body = tracking is null ? sentence : $"Tracking number {tracking}. {sentence}";
        return ($"{subject} {verb}", body);
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxNumberLength ? trimmed : trimmed[..MaxNumberLength];
    }
}
