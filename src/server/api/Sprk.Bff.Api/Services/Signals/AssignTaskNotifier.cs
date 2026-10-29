using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// Task 168 (D-194, owner UAT round 2 item 2): Assign Task's "Send email notification". After the decision is recorded (D-17: the
// Decision Record stays the last WRITE of the commit; a notification is not one of its writes), the Assigned To contact is emailed
// the task and the contact's internal user gets a bell notification. Nothing here can fail or change the decision.
// Fix round (D-199): only an assignee who can see the record is emailed (AssignTaskRecipientGate), in the request AND in the
// system-mailbox job; an external assignee gets the portal link and no bell.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: CommunicationService.SendAsync (delegated as the user, SendMode.User, the InquiryEmailSender path; app-only from the
//     approved system mailbox, the RegistrationEmailService path), RegistrationEmailService.FillTemplate (one-pass placeholders),
//     NotificationService (appnotification, the GrantAccessNotifier precedent), InquiryRecordFacts (the record's name only when it
//     is Standard and the recipient is confirmed internal; AppLink), GrantAccessNotifier.BuildPortalLink and ExternalGranterName,
//     JobSubmissionService + IJobHandler (the GateDecisionRecordJob shape).
//   Extension: every one of them is reused unchanged; this class only composes them for one action. It is not added to an existing
//     class because none of them owns "tell the assignee of a decision's task": the executor runs before the record (it may not
//     notify), and DecisionCommitFollowUps is the commit route's seam, which calls this.
//   Cost of doing nothing: the assignee is not told a task was assigned (owner, UAT round 2 item 2).
// =====================================================================================================================

/// <summary>Stable <see cref="EventId"/>s for the Assign Task notification (task 168). Beside the notifier, like
/// <see cref="GateDecisionRecordEvents"/>, so a parallel task adding ids elsewhere cannot collide. Ids only in every line.</summary>
public static class AssignTaskNotifierEvents
{
    /// <summary>Information: the email went from the assigner's own mailbox.</summary>
    public static readonly EventId EmailSentAsUser = new(50390, nameof(EmailSentAsUser));

    /// <summary>Warning: the assigner's send failed or timed out; the system-mailbox job was queued instead (or could not be).</summary>
    public static readonly EventId EmailFellBack = new(50391, nameof(EmailFellBack));

    /// <summary>Information or Warning: no email was attempted (the gate refused the assignee, or the contact has no usable address).</summary>
    public static readonly EventId EmailSkipped = new(50392, nameof(EmailSkipped));

    /// <summary>Information (no internal user) or Warning (a write fault): no bell notification was created.</summary>
    public static readonly EventId BellNotCreated = new(50393, nameof(BellNotCreated));

    /// <summary>Information or Warning: what the system-mailbox job did.</summary>
    public static readonly EventId SystemEmailJob = new(50394, nameof(SystemEmailJob));
}

/// <summary>
/// One Assign Task the decision took with Send email notification on, whose task was created: the step's own values and the ids the
/// notice needs. Built only from the commit's server-side state (<see cref="From"/>), never sent anywhere as is.
/// </summary>
/// <param name="AssigneeContactId">The Assigned To contact the step bound (the caller was allowed to append it, FR-51 / preflight).</param>
/// <param name="TaskId">The created <c>sprk_event</c>; null only when the executor did not name it (then the gate cannot check Read).</param>
/// <param name="Description">"What to do", as the step carried it.</param>
/// <param name="Due">"Due", or null for no due date.</param>
/// <param name="Core">The record the decision is about (the matter), or null.</param>
public sealed record AssignTaskNotice(Guid AssigneeContactId, Guid? TaskId, string Description, DateOnly? Due, DecisionCoreRecord? Core)
{
    /// <summary>
    /// The notice for one executed Assign Task step, or null when the step asks for none: Send email notification is off, or the
    /// assignee or "What to do" cannot be read (the executor refused those, so a Done step always has them). An omitted
    /// <see cref="AssignTaskNotifier.NotifyParameter"/> is ON (D-194: the box is ticked by default, and server and wizard agree).
    /// </summary>
    internal static AssignTaskNotice? From(
        IReadOnlyDictionary<string, string?> values, IReadOnlyList<DecisionCommitRef> written, DecisionCoreRecord? core)
    {
        string? Param(string code) => values.TryGetValue(code, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        if (!AssignTaskNotifier.IsOn(Param(AssignTaskNotifier.NotifyParameter)))
            return null;
        if (!Guid.TryParse(Param("assignee"), out var contact) || contact == Guid.Empty || Param("description") is not { } description)
            return null;

        DateOnly? due = DateOnly.TryParseExact(Param("dueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
        var task = written.FirstOrDefault(w => w.Entity == AssignTaskNotifier.TaskTable)?.Id;
        return new AssignTaskNotice(contact, task, description, due, core);
    }
}

/// <summary>What the system-mailbox job carries (task 168): IDS only, never the task text, a name or an address. The job reads the
/// task, the contact, the record and the assigner's display name by id when it runs.</summary>
/// <param name="AssignedByObjectId">The assigner's Entra oid (fix round, review finding 4): the system-mailbox email names them and
/// its <c>sprk_communication</c> carries them as <c>sprk_sentby</c>. Null in a payload queued before the fix round.</param>
public sealed record AssignTaskEmailPayload(Guid TaskId, Guid AssigneeContactId, string? CoreEntity, Guid? CoreId, Guid? AssignedByObjectId = null);

/// <summary>What one system-mailbox send did.</summary>
public enum AssignTaskSystemEmailOutcome
{
    /// <summary>The email was sent from the system mailbox.</summary>
    Sent,

    /// <summary>An email for this task was already sent (a redelivery, or the assigner's own send did record one).</summary>
    AlreadySent,

    /// <summary>Nothing to send: the task or the contact no longer exists, the task was closed or reassigned, or the contact has no
    /// usable address.</summary>
    NothingToSend,

    /// <summary>The recipient gate refused the assignee at send time (D-199): no email, ever, for this job.</summary>
    NotAllowed,

    /// <summary>Whether the assignee may be emailed could not be checked at send time: no email now (fail closed); the job retries.</summary>
    Unverified,
}

/// <summary>
/// Assign Task's email and bell for the assignee (task 168, D-194; fix round D-199). <see cref="NotifyAsync"/> runs in the commit
/// request after the record: first the recipient gate (<see cref="AssignTaskRecipientGate"/>); for an assignee it lets through, the
/// email from the ASSIGNER's own mailbox (delegated, bounded by <see cref="UserSendTimeout"/>), and on any failure an ADR-004 job sends
/// it from the system mailbox (<c>Communication:DefaultMailbox</c>, approved-sender checked by the send path) after asking the SAME gate
/// again, so the request never waits twice and the fallback never reaches a refused recipient; then one bell notification, for an
/// internal assignee only. Never throws.
/// </summary>
/// <remarks>
/// <para><b>What the email says.</b> The record's name (only when it is Standard and the recipient is confirmed internal,
/// <see cref="InquiryRecordFacts"/>) and its link: the model-driven link for an internal assignee, the portal link for an external one
/// (the portal has no task deep link, DI-135 #2445). What to do, Due, and "Assigned by" the assigner's display name only. One embedded
/// template with every value HTML-encoded (<see cref="BuildEmail"/>). Nothing else from the decision or its Signal.</para>
/// <para><b>Who it goes to.</b> Only the contact the step bound (<c>emailaddress1</c>), and only when the gate lets them through; a
/// refusal or an empty address skips the email with a commit warning saying why. Log lines carry record ids only, never an address.</para>
/// <para><b>Tracked communication.</b> The send path (<see cref="CommunicationService.SendAsync"/>) writes a <c>sprk_communication</c>
/// by design, filed under the decision's record when there is one (so a Secure matter's row stays a secure child), marked
/// internal-only (review finding 2: partners on the record never see it in their message thread), with the correlation id
/// <see cref="CorrelationIdFor"/>. The system-mailbox job reads that id back before it sends, so a redelivered job sends once.</para>
/// <para><b>Test seams.</b> <see cref="SendAsUserAsync"/> and <see cref="SendAsSystemAsync"/> are virtual (the Graph boundary, the
/// <see cref="InquiryEmailSender"/> precedent); the communication service is used only through them.</para>
/// </remarks>
public class AssignTaskNotifier
{
    /// <summary>The catalog code this notifies for.</summary>
    internal const string ActionCode = "assign-task";

    /// <summary>The Boolean parameter of <see cref="ActionCode"/> (D-194).</summary>
    internal const string NotifyParameter = "notify";

    /// <summary>The table the Assign Task executor creates.</summary>
    internal const string TaskTable = "sprk_event";

    /// <summary>The <c>appnotification</c> category (icon "assignment").</summary>
    internal const string Category = "assignment";

    /// <summary>Commit warning: the email could not be sent and no fallback was queued.</summary>
    internal const string EmailNotSentWarning = "assignee_email_not_sent";

    /// <summary>Commit warning: the contact has no email address, so none was sent.</summary>
    internal const string EmailMissingWarning = "assignee_email_missing";

    /// <summary>The ADR-004 job type of the system-mailbox fallback.</summary>
    public const string JobTypeName = "AssignTaskEmail";

    /// <summary>The bound on the assigner's own send (D-194: at most 10 s).</summary>
    internal static readonly TimeSpan DefaultUserSendTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The bound on queuing the fallback job (the <see cref="GateDecisionRecordEnqueuer"/> value).</summary>
    internal static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(5);

    private const string TemplateResource = "Sprk.Bff.Api.Services.Signals.EmailTemplates.AssignTaskNotificationTemplate.html";

    /// <summary>The noun and portal token of each core type the email can name. A type not listed gets "Record" and no portal link.</summary>
    private static readonly IReadOnlyDictionary<string, (string Noun, string Token)> CoreTypes =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = ("Matter", "matter"),
            ["sprk_project"] = ("Project", "project"),
            ["sprk_workassignment"] = ("Work assignment", "workassignment"),
        };

    private static readonly Lazy<string> Template = new(LoadTemplate);

    private readonly CommunicationService _communication;
    private readonly NotificationService _notifications;
    private readonly IGenericEntityService _entities;
    private readonly AssignTaskRecipientGate _gate;
    private readonly InquiryRecordFacts _facts;
    private readonly JobSubmissionService _jobs;
    private readonly IOptions<CommunicationOptions> _communicationOptions;
    private readonly CustomerIdentity _customer;
    private readonly IConfiguration _configuration;
    private readonly string? _environmentUrl;
    private readonly ILogger<AssignTaskNotifier> _logger;

    /// <param name="communication">Used only by the virtual send seams (null in tests that replace them).</param>
    public AssignTaskNotifier(
        CommunicationService communication,
        NotificationService notifications,
        IGenericEntityService entities,
        AssignTaskRecipientGate gate,
        InquiryRecordFacts facts,
        JobSubmissionService jobs,
        IOptions<CommunicationOptions> communicationOptions,
        CustomerIdentity customer,
        IConfiguration configuration,
        IOptions<DataverseOptions> dataverse,
        ILogger<AssignTaskNotifier> logger)
    {
        _communication = communication;
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _communicationOptions = communicationOptions ?? throw new ArgumentNullException(nameof(communicationOptions));
        _customer = customer ?? throw new ArgumentNullException(nameof(customer));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environmentUrl = dataverse?.Value.EnvironmentUrl;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The bound on the assigner's own send; <see cref="DefaultUserSendTimeout"/> unless a test shortens it.</summary>
    internal TimeSpan UserSendTimeout { get; set; } = DefaultUserSendTimeout;

    /// <summary>Send email notification's value: on unless it is exactly <c>false</c> (an omitted value is on, D-194).</summary>
    internal static bool IsOn(string? value) => !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>The correlation id the send path stores on the tracked communication: one per task, the same on every attempt.</summary>
    internal static string CorrelationIdFor(Guid taskId) => $"assign-task-{taskId:N}";

    /// <summary>
    /// In the commit request, after the record: the gate, then the email (as the assigner, else the fallback job) and, for an internal
    /// assignee, the bell. Returns the commit warnings for an email that did not go (<see cref="EmailNotSentWarning"/>,
    /// <see cref="EmailMissingWarning"/>, or the gate's <see cref="AssignTaskRecipient.Warning"/>). Never throws.
    /// </summary>
    public virtual async Task<IReadOnlyList<string>> NotifyAsync(HttpContext http, AssignTaskNotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(notice);

        var warnings = new List<string>();
        try
        {
            var assignerOid = CallerResolution.ResolveObjectIdGuid(http.User);
            var recipient = await _gate.DecideAsync(notice.AssigneeContactId, notice.Core, notice.TaskId, assignerOid, ct).ConfigureAwait(false);
            if (recipient.Warning is { } refused)
            {
                // D-199: nobody the gate refuses is emailed or belled; the task stands and the assigner is told why.
                warnings.Add(refused);
                return warnings;
            }

            var (warning, record) = await EmailAsync(http, notice, recipient, assignerOid, ct).ConfigureAwait(false);
            if (warning is not null)
                warnings.Add(warning);
            if (recipient is { Kind: AssignTaskRecipientKind.Internal, UserId: { } user })
                await BellAsync(notice, user, record, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Every step below catches its own faults; this is the backstop that keeps the decision whole.
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] notifying contact {ContactId} of task {TaskId} stopped ({ExceptionType}); the decision stands.",
                notice.AssigneeContactId, notice.TaskId, ex.GetType().Name);
            warnings.Add(EmailNotSentWarning);
        }

        return warnings;
    }

    /// <summary>
    /// The system-mailbox send the fallback job runs: re-reads the task (still active, still assigned to the payload's contact), asks
    /// the recipient gate again (D-199 Q2: never someone it refuses), and sends from <c>Communication:DefaultMailbox</c> unless an
    /// email for the task was already recorded. Throws on a send or read fault (the job decides retry or poison).
    /// </summary>
    internal async Task<AssignTaskSystemEmailOutcome> SendFromSystemMailboxAsync(AssignTaskEmailPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var correlationId = CorrelationIdFor(payload.TaskId);
        if (await AlreadySentAsync(correlationId, ct).ConfigureAwait(false))
            return AssignTaskSystemEmailOutcome.AlreadySent;

        Entity task;
        try
        {
            task = await _entities.RetrieveAsync(
                TaskTable, payload.TaskId, ["sprk_description", "sprk_duedate", "sprk_assignedto", "statecode"], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex))
        {
            return AssignTaskSystemEmailOutcome.NothingToSend;
        }

        // The task as it is NOW: a closed task, or one reassigned since the decision, is not emailed about.
        if (task.GetAttributeValue<OptionSetValue>("statecode") is not { Value: 0 }
            || task.GetAttributeValue<EntityReference>("sprk_assignedto")?.Id != payload.AssigneeContactId)
        {
            _logger.LogInformation(AssignTaskNotifierEvents.SystemEmailJob,
                "[ASSIGN-TASK-NOTIFY] task {TaskId} is no longer active or no longer assigned to contact {ContactId}; no email.",
                payload.TaskId, payload.AssigneeContactId);
            return AssignTaskSystemEmailOutcome.NothingToSend;
        }

        var core = payload.CoreEntity is { } entity && payload.CoreId is { } id && id != Guid.Empty ? new DecisionCoreRecord(entity, id) : null;
        var recipient = await _gate.DecideAsync(payload.AssigneeContactId, core, payload.TaskId, payload.AssignedByObjectId, ct).ConfigureAwait(false);
        switch (recipient.Refusal)
        {
            case AssignTaskRefusal.None:
                break;
            case AssignTaskRefusal.Unverified:
                return AssignTaskSystemEmailOutcome.Unverified;
            default:
                return AssignTaskSystemEmailOutcome.NotAllowed;
        }

        if (task.GetAttributeValue<string>("sprk_description") is not { Length: > 0 } description
            || await ReadAddressAsync(payload.AssigneeContactId, ct).ConfigureAwait(false) is not { } address)
            return AssignTaskSystemEmailOutcome.NothingToSend;

        DateOnly? due = task.GetAttributeValue<DateTime?>("sprk_duedate") is { } date ? DateOnly.FromDateTime(date) : null;
        var record = await RecordFactsAsync(recipient, core, address, ct).ConfigureAwait(false);
        if (record is null)
            return AssignTaskSystemEmailOutcome.NothingToSend;

        var assignedBy = await AssignerNameAsync(payload.AssignedByObjectId, ct).ConfigureAwait(false);
        var content = BuildEmail(Template.Value, record, description, due, assignedBy, DateTime.UtcNow.Year);
        var request = Request(content, address, core, record, correlationId) with
        {
            SendMode = SendMode.SharedMailbox,
            FromMailbox = RegistrationEmailService.ResolveFromMailbox(_communicationOptions.Value),
        };

        await SendAsSystemAsync(request, SenderContext(payload.AssignedByObjectId), ct).ConfigureAwait(false);
        return AssignTaskSystemEmailOutcome.Sent;
    }

    /// <summary>The Graph boundary for the assigner's send (OBO, <c>/me/sendMail</c>). Throws on failure.</summary>
    internal virtual Task SendAsUserAsync(SendCommunicationRequest request, HttpContext http, CancellationToken ct) =>
        _communication.SendAsync(request, http, ct);

    /// <summary>The Graph boundary for the system-mailbox send (app-only). <paramref name="sender"/> carries only the assigner's oid,
    /// which the send path stamps as <c>sprk_sentby</c> and the record's requester; null when the payload has none. Throws on failure.</summary>
    internal virtual Task SendAsSystemAsync(SendCommunicationRequest request, HttpContext? sender, CancellationToken ct) =>
        _communication.SendAsync(request, sender, ct);

    /// <summary>The record as the email and the bell show it.</summary>
    /// <param name="Noun">"Matter", "Project", "Work assignment" or "Record".</param>
    /// <param name="Label">The record's name, or null when it may not be shown (not Standard, not confirmed internal, external).</param>
    /// <param name="Link">The model-driven link (internal) or the portal link (external), or null.</param>
    /// <param name="LinkText">What the link says when there is no label.</param>
    /// <param name="RelativeLink">The bell's same-origin record path (internal only).</param>
    internal sealed record RecordFacts(string Noun, string? Label, string? Link, string LinkText, string? RelativeLink);

    /// <summary>
    /// The subject and HTML body (task 168): the record (its label, linked when there is a link), What to do (line breaks kept), Due
    /// (or "No due date") and who assigned it (a display name only, or the line is left out). Every value is HTML-encoded and the
    /// template is filled in ONE pass (<see cref="RegistrationEmailService.FillTemplate"/>), so a value that contains a placeholder is text.
    /// </summary>
    internal static (string Subject, string Body) BuildEmail(
        string template, RecordFacts? record, string description, DateOnly? due, string? assignedBy, int year)
    {
        var label = record?.Label;
        var link = record?.Link;
        var recordHtml = (label, link) switch
        {
            (not null, not null) => $"<a href=\"{Encode(link)}\" style=\"color: #0f6cbd;\">{Encode(label)}</a>",
            (not null, null) => Encode(label),
            (null, not null) => $"<a href=\"{Encode(link)}\" style=\"color: #0f6cbd;\">{Encode(record!.LinkText)}</a>",
            _ => "Not filed under a record",
        };
        var whatToDo = Encode(description.Trim()).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br />", StringComparison.Ordinal);
        var dueText = due is { } d ? d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) : "No due date";
        var assignedByHtml = string.IsNullOrWhiteSpace(assignedBy)
            ? string.Empty
            : $"<p style=\"margin: 20px 0 0; color: #616161; font-size: 13px; line-height: 1.4;\">Assigned by {Encode(assignedBy.Trim())}</p>";

        var body = RegistrationEmailService.FillTemplate(template, new Dictionary<string, string>
        {
            ["RecordNoun"] = Encode(record?.Noun ?? "Record"),
            ["Record"] = recordHtml,
            ["WhatToDo"] = whatToDo,
            ["Due"] = Encode(dueText),
            ["AssignedBy"] = assignedByHtml,
            ["Year"] = year.ToString(CultureInfo.InvariantCulture),
        });
        var subject = label is null ? "A task was assigned to you" : $"A task was assigned to you: {label}";
        return (subject, body);
    }

    // ── The email ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The email to an assignee the gate let through: the warning when it did not go, and the record facts the bell reuses.</summary>
    private async Task<(string? Warning, RecordFacts? Record)> EmailAsync(
        HttpContext http, AssignTaskNotice notice, AssignTaskRecipient recipient, Guid? assignerOid, CancellationToken ct)
    {
        string? address;
        try
        {
            address = await ReadAddressAsync(notice.AssigneeContactId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Not "no address": the job reads it again, behind the same gate.
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId}'s address could not be read for task {TaskId} ({ExceptionType}); falling back to the system mailbox.",
                notice.AssigneeContactId, notice.TaskId, ex.GetType().Name);
            return (await FallBackAsync(notice, assignerOid, ct).ConfigureAwait(false) ? null : EmailNotSentWarning, null);
        }

        if (address is null)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId} has no usable email address; no email was sent for task {TaskId}.",
                notice.AssigneeContactId, notice.TaskId);
            return (EmailMissingWarning, recipient.Kind == AssignTaskRecipientKind.Internal
                ? await RecordFactsAsync(recipient, notice.Core, null, ct).ConfigureAwait(false)
                : null);
        }

        var record = await RecordFactsAsync(recipient, notice.Core, address, ct).ConfigureAwait(false);
        if (record is null)
            return (EmailNotSentWarning, null);

        var content = BuildEmail(Template.Value, record, notice.Description, notice.Due, GrantAccessNotifier.ExternalGranterName(http.User), DateTime.UtcNow.Year);
        var request = Request(content, address, notice.Core, record, notice.TaskId is { } task ? CorrelationIdFor(task) : null) with
        { SendMode = SendMode.User };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(UserSendTimeout);
        try
        {
            await SendAsUserAsync(request, http, timeout.Token).ConfigureAwait(false);
            _logger.LogInformation(AssignTaskNotifierEvents.EmailSentAsUser,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: emailed contact {ContactId} from the assigner's mailbox.", notice.TaskId, notice.AssigneeContactId);
            return (null, record);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // D-194: no mailbox, no consent, a Graph error or the timeout. The system mailbox sends it from a job (the request does not
            // wait twice), behind the same gate (D-199 Q2). Known limit: a send that reached Graph and timed out on our side may also arrive.
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the assigner's send to contact {ContactId} failed ({ExceptionType}, {ErrorCode}{Timeout}); falling back to the system mailbox.",
                notice.TaskId, notice.AssigneeContactId, ex.GetType().Name, ErrorCode(ex), timeout.IsCancellationRequested ? ", timed out" : string.Empty);
            return (await FallBackAsync(notice, assignerOid, ct).ConfigureAwait(false) ? null : EmailNotSentWarning, record);
        }
    }

    /// <summary>Queues the system-mailbox job. False when it could not be queued (logged).</summary>
    private async Task<bool> FallBackAsync(AssignTaskNotice notice, Guid? assignerOid, CancellationToken ct)
    {
        if (notice.TaskId is not { } taskId)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId}: the task id is unknown, so no system-mailbox email can be queued.", notice.AssigneeContactId);
            return false;
        }

        var payload = new AssignTaskEmailPayload(taskId, notice.AssigneeContactId, notice.Core?.Entity, notice.Core?.Id, assignerOid);
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = JobTypeName,
            SubjectId = taskId.ToString("D", CultureInfo.InvariantCulture),
            CorrelationId = CorrelationIdFor(taskId),
            IdempotencyKey = CorrelationIdFor(taskId),
            Payload = JsonSerializer.SerializeToDocument(payload),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(EnqueueTimeout);
        try
        {
            await _jobs.SubmitJobAsync(job, timeout.Token).ConfigureAwait(false);
            _logger.LogInformation(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the system-mailbox email to contact {ContactId} was queued (job {JobId}).",
                taskId, notice.AssigneeContactId, job.JobId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the system-mailbox email to contact {ContactId} could not be queued ({ExceptionType}); no email was sent.",
                taskId, notice.AssigneeContactId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>The send request both paths share: the one recipient, the HTML body, filed under the record when there is one, and
    /// internal-only (review finding 2: the row is never shown in a partner's message thread on the record).</summary>
    private static SendCommunicationRequest Request(
        (string Subject, string Body) content, string address, DecisionCoreRecord? core, RecordFacts record, string? correlationId) =>
        new()
        {
            To = [address],
            Subject = content.Subject,
            Body = content.Body,
            BodyFormat = BodyFormat.HTML,
            CorrelationId = correlationId,
            IsInternalOnly = true,
            Associations = core is not null && core.Id != Guid.Empty
                ? [new CommunicationAssociation { EntityType = core.Entity, EntityId = core.Id, EntityName = record.Label }]
                : null,
        };

    /// <summary>
    /// The record facts for the recipient the gate let through: an internal assignee gets the model-driven link, and the name only as
    /// <see cref="InquiryRecordFacts.ResolveAsync"/> allows it (Standard, the address confirmed internal); an external assignee gets the
    /// portal link and never the name. Null when an external assignee's portal link cannot be built (no email: the portal link is what
    /// D-199 gives them), logged.
    /// </summary>
    private async Task<RecordFacts?> RecordFactsAsync(AssignTaskRecipient recipient, DecisionCoreRecord? core, string? address, CancellationToken ct)
    {
        if (core is null)
            return new RecordFacts("Record", null, null, "Open in Spaarke", null);

        var known = CoreTypes.TryGetValue(core.Entity, out var type);
        var noun = known ? type.Noun : "Record";

        if (recipient.Kind == AssignTaskRecipientKind.External)
        {
            var portal = known
                ? GrantAccessNotifier.BuildPortalLink(_configuration["ExternalAccess:PortalUrl"], _customer.IsResolved ? _customer.Id : null, type.Token, core.Id)
                : null;
            if (portal is null)
            {
                _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                    "[ASSIGN-TASK-NOTIFY] no portal link for {Entity} {RecordId}: the portal must parse the record type, ExternalAccess:PortalUrl " +
                    "must be an https URL and Customer:Id must be resolved. No email to the external assignee.", core.Entity, core.Id);
                return null;
            }

            return new RecordFacts(noun, null, portal, "Open in the Spaarke portal", null);
        }

        string? label = null;
        if (address is not null)
        {
            // Never throws; a fault or a non-Standard record leaves the name out.
            label = (await _facts.ResolveAsync(core.Entity, core.Id, [address], ct).ConfigureAwait(false)).RecordLabel;
        }

        var appLink = InquiryRecordFacts.AppLink(_environmentUrl, core.Entity, core.Id);
        var relative = appLink is null ? null : $"/main.aspx?etn={core.Entity}&id={core.Id:D}&pagetype=entityrecord";
        return new RecordFacts(noun, label, appLink, "Open in Spaarke", relative);
    }

    /// <summary>The contact's <c>emailaddress1</c> when it is a usable address; null when it is empty or the contact no longer exists.
    /// A read fault throws.</summary>
    private async Task<string?> ReadAddressAsync(Guid contactId, CancellationToken ct)
    {
        Entity contact;
        try
        {
            contact = await _entities.RetrieveAsync("contact", contactId, ["emailaddress1"], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex))
        {
            return null;
        }

        return contact.GetAttributeValue<string>("emailaddress1")?.Trim() is { Length: > 0 } text && OutsideFirmContactResolver.Address.IsMatch(text)
            ? text
            : null;
    }

    /// <summary>An email for the task is already recorded (the correlation id the send path stores).</summary>
    private async Task<bool> AlreadySentAsync(string correlationId, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_communication") { ColumnSet = new ColumnSet("sprk_communicationid"), TopCount = 1 };
        query.Criteria.AddCondition("sprk_correlationid", ConditionOperator.Equal, correlationId);
        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return found.Entities.Count > 0;
    }

    /// <summary>
    /// The assigner's display name for the system-mailbox email (review finding 4): the one enabled systemuser with that oid, its
    /// <c>fullname</c> only (never a UPN or an address; a name shaped like an address is left out, the
    /// <see cref="GrantAccessNotifier.ExternalGranterName"/> rule). Null when there is no oid, no single user, or the read fails.
    /// </summary>
    private async Task<string?> AssignerNameAsync(Guid? oid, CancellationToken ct)
    {
        if (oid is not { } id || id == Guid.Empty)
            return null;

        try
        {
            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("fullname"), TopCount = 2 };
            query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, id);
            query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            var users = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var name = users.Entities.Count == 1 ? users.Entities[0].GetAttributeValue<string>("fullname")?.Trim() : null;
            return string.IsNullOrEmpty(name) || name.Contains('@') ? null : name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.SystemEmailJob,
                "[ASSIGN-TASK-NOTIFY] the assigner's name could not be read ({ExceptionType}); the email leaves it out.", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The send path's view of the assigner for a job (no request): a principal carrying their oid only, so the tracked
    /// row's <c>sprk_sentby</c> is the assigner (review finding 4). Null when the payload has no oid.</summary>
    private static HttpContext? SenderContext(Guid? assignerOid) =>
        assignerOid is { } oid && oid != Guid.Empty
            ? new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(CallerResolution.ObjectIdClaim, oid.ToString("D", CultureInfo.InvariantCulture))], "assign-task-email-job")),
            }
            : null;

    /// <summary>The send path's error code of a fault, with Graph's own code when the channel sender recorded one
    /// (<c>GRAPH_SEND_FAILED/ErrorSendAsDenied</c>), for the log: never the exception or its message, which can name an address.</summary>
    internal static string ErrorCode(Exception ex) => ex is Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException problem
        ? problem.Extensions?.TryGetValue("graphErrorCode", out var graph) == true && graph is string { Length: > 0 } code
            ? $"{problem.Code}/{code}"
            : problem.Code
        : "none";

    // ── The bell ───────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One bell for the internal user the gate let through (an external contact has no model-driven app, D-199).</summary>
    private async Task BellAsync(AssignTaskNotice notice, Guid userId, RecordFacts? record, CancellationToken ct)
    {
        var noun = record?.Noun ?? "Record";
        try
        {
            await _notifications.CreateNotificationAsync(
                userId: userId,
                title: "A task was assigned to you",
                body: record?.Label is { } label ? $"On {noun.ToLowerInvariant()} {label}." : null,
                category: Category,
                actionUrl: record?.RelativeLink,
                regardingId: notice.Core?.Id ?? notice.TaskId,
                actionTitle: record?.RelativeLink is null ? null : $"Open {noun.ToLowerInvariant()}",
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.BellNotCreated,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the bell notification for user {UserId} could not be created ({ExceptionType}); the email is unaffected.",
                notice.TaskId, userId, ex.GetType().Name);
        }
    }

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string LoadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException($"Email template '{TemplateResource}' is not an embedded resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
