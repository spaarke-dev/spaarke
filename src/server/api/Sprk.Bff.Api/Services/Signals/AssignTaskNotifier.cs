using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
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
// the task and the contact's linked user gets a bell notification. Nothing here can fail or change the decision.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: CommunicationService.SendAsync (delegated as the user, SendMode.User, the InquiryEmailSender path; app-only from the
//     approved system mailbox, the RegistrationEmailService path), RegistrationEmailService.FillTemplate (one-pass placeholders),
//     NotificationService (appnotification, the GrantAccessNotifier precedent), PersonalToDoAssignee.SingleEnabledUserAsync (the D-129
//     contact -> linked user rule), InquiryRecordFacts.AppLink / RecordLabel, JobSubmissionService + IJobHandler (the
//     GateDecisionRecordJob shape).
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

    /// <summary>Warning: no email was attempted (the contact has no usable address, or no longer exists).</summary>
    public static readonly EventId EmailSkipped = new(50392, nameof(EmailSkipped));

    /// <summary>Information (no single linked user) or Warning (a read or write fault): no bell notification was created.</summary>
    public static readonly EventId BellNotCreated = new(50393, nameof(BellNotCreated));

    /// <summary>Information or Warning: what the system-mailbox job did.</summary>
    public static readonly EventId SystemEmailJob = new(50394, nameof(SystemEmailJob));
}

/// <summary>
/// One Assign Task the decision took with Send email notification on, whose task was created: the step's own values and the ids the
/// notice needs. Built only from the commit's server-side state (<see cref="From"/>), never sent anywhere as is.
/// </summary>
/// <param name="AssigneeContactId">The Assigned To contact the step bound (the caller was allowed to append it, FR-51 / preflight).</param>
/// <param name="TaskId">The created <c>sprk_event</c>; null only when the executor did not name it (then no fallback job can be queued).</param>
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

/// <summary>What the system-mailbox job carries (task 168): IDS only, never the task text or an address. The job reads the task, the
/// contact and the record by id when it runs.</summary>
public sealed record AssignTaskEmailPayload(Guid TaskId, Guid AssigneeContactId, string? CoreEntity, Guid? CoreId);

/// <summary>What one system-mailbox send did.</summary>
public enum AssignTaskSystemEmailOutcome
{
    /// <summary>The email was sent from the system mailbox.</summary>
    Sent,

    /// <summary>An email for this task was already sent (a redelivery, or the assigner's own send did record one).</summary>
    AlreadySent,

    /// <summary>Nothing to send: the task or the contact no longer exists, or the contact has no usable address.</summary>
    NothingToSend,
}

/// <summary>
/// Assign Task's email and bell for the assignee (task 168, D-194). <see cref="NotifyAsync"/> runs in the commit request after the
/// record: the email first from the ASSIGNER's own mailbox (delegated, bounded by <see cref="UserSendTimeout"/>), and on any failure an
/// ADR-004 job sends it from the system mailbox (<c>Communication:DefaultMailbox</c>, approved-sender checked by the send path) so the
/// request never waits twice; then one bell notification for the contact's single enabled linked user. Never throws.
/// </summary>
/// <remarks>
/// <para><b>What the email says.</b> The record's name and its model-driven link, What to do and Due, from one embedded template with
/// every value HTML-encoded (<see cref="BuildEmail"/>). Nothing else from the decision or its Signal.</para>
/// <para><b>Who it goes to.</b> Only the contact the step bound (<c>emailaddress1</c>); an empty one skips the email (logged). Log lines
/// carry record ids only, never an address.</para>
/// <para><b>Tracked communication.</b> The send path (<see cref="CommunicationService.SendAsync"/>) writes a <c>sprk_communication</c>
/// by design, filed under the decision's record when there is one (so a Secure matter's row stays a secure child), with the correlation
/// id <see cref="CorrelationIdFor"/>. The system-mailbox job reads that id back before it sends, so a redelivered job sends once.</para>
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

    /// <summary>The name and number columns of the core types whose label the email and the bell show.</summary>
    private static readonly IReadOnlyDictionary<string, (string Name, string Number, string Noun)> LabelColumns =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = ("sprk_mattername", "sprk_matternumber", "Matter"),
            ["sprk_project"] = ("sprk_projectname", "sprk_projectnumber", "Project"),
        };

    private static readonly Lazy<string> Template = new(LoadTemplate);

    private readonly CommunicationService _communication;
    private readonly NotificationService _notifications;
    private readonly IGenericEntityService _entities;
    private readonly AssignedAccessStore _links;
    private readonly JobSubmissionService _jobs;
    private readonly IOptions<CommunicationOptions> _communicationOptions;
    private readonly string? _environmentUrl;
    private readonly ILogger<AssignTaskNotifier> _logger;

    /// <param name="communication">Used only by the virtual send seams (null in tests that replace them).</param>
    public AssignTaskNotifier(
        CommunicationService communication,
        NotificationService notifications,
        IGenericEntityService entities,
        AssignedAccessStore links,
        JobSubmissionService jobs,
        IOptions<CommunicationOptions> communicationOptions,
        IOptions<DataverseOptions> dataverse,
        ILogger<AssignTaskNotifier> logger)
    {
        _communication = communication;
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _communicationOptions = communicationOptions ?? throw new ArgumentNullException(nameof(communicationOptions));
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
    /// In the commit request, after the record: the email (as the assigner, else the fallback job) and the bell. Returns the commit
    /// warnings for an email that did not go (<see cref="EmailNotSentWarning"/>, <see cref="EmailMissingWarning"/>). Never throws.
    /// </summary>
    public virtual async Task<IReadOnlyList<string>> NotifyAsync(HttpContext http, AssignTaskNotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(notice);

        var warnings = new List<string>();
        try
        {
            var record = await ReadRecordAsync(notice.Core?.Entity, notice.Core?.Id, ct).ConfigureAwait(false);
            if (await EmailAsync(http, notice, record, ct).ConfigureAwait(false) is { } warning)
                warnings.Add(warning);
            await BellAsync(notice, record, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Every step below catches its own faults; this is the backstop that keeps the decision whole.
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack, ex,
                "[ASSIGN-TASK-NOTIFY] notifying contact {ContactId} of task {TaskId} stopped ({ExceptionType}); the decision stands.",
                notice.AssigneeContactId, notice.TaskId, ex.GetType().Name);
            warnings.Add(EmailNotSentWarning);
        }

        return warnings;
    }

    /// <summary>
    /// The system-mailbox send the fallback job runs: reads the task, the contact and the record by id, and sends from
    /// <c>Communication:DefaultMailbox</c> unless an email for the task was already recorded. Throws on a fault (the job retries).
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
            task = await _entities.RetrieveAsync(TaskTable, payload.TaskId, ["sprk_description", "sprk_duedate"], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex))
        {
            return AssignTaskSystemEmailOutcome.NothingToSend;
        }

        if (task.GetAttributeValue<string>("sprk_description") is not { Length: > 0 } description
            || await ReadAddressAsync(payload.AssigneeContactId, ct).ConfigureAwait(false) is not { } address)
            return AssignTaskSystemEmailOutcome.NothingToSend;

        DateOnly? due = task.GetAttributeValue<DateTime?>("sprk_duedate") is { } date ? DateOnly.FromDateTime(date) : null;
        var record = await ReadRecordAsync(payload.CoreEntity, payload.CoreId, ct).ConfigureAwait(false);
        var content = BuildEmail(Template.Value, record, description, due, DateTime.UtcNow.Year);
        var request = Request(content, address, payload.CoreEntity, payload.CoreId, record, correlationId) with
        {
            SendMode = SendMode.SharedMailbox,
            FromMailbox = RegistrationEmailService.ResolveFromMailbox(_communicationOptions.Value),
        };

        await SendAsSystemAsync(request, ct).ConfigureAwait(false);
        return AssignTaskSystemEmailOutcome.Sent;
    }

    /// <summary>The Graph boundary for the assigner's send (OBO, <c>/me/sendMail</c>). Throws on failure.</summary>
    internal virtual Task SendAsUserAsync(SendCommunicationRequest request, HttpContext http, CancellationToken ct) =>
        _communication.SendAsync(request, http, ct);

    /// <summary>The Graph boundary for the system-mailbox send (app-only). Throws on failure.</summary>
    internal virtual Task SendAsSystemAsync(SendCommunicationRequest request, CancellationToken ct) =>
        _communication.SendAsync(request, cancellationToken: ct);

    /// <summary>The record's label, link and noun as the email and the bell show them.</summary>
    internal sealed record RecordFacts(string Noun, string? Label, string? AppLink, string? RelativeLink);

    /// <summary>
    /// The subject and HTML body (task 168): the record (its label, linked when there is a link), What to do (line breaks kept) and Due
    /// (or "No due date"). Every value is HTML-encoded and the template is filled in ONE pass
    /// (<see cref="RegistrationEmailService.FillTemplate"/>), so a value that contains a placeholder is text.
    /// </summary>
    internal static (string Subject, string Body) BuildEmail(string template, RecordFacts? record, string description, DateOnly? due, int year)
    {
        var label = record?.Label;
        var link = record?.AppLink;
        var recordHtml = (label, link) switch
        {
            (not null, not null) => $"<a href=\"{Encode(link)}\" style=\"color: #0f6cbd;\">{Encode(label)}</a>",
            (not null, null) => Encode(label),
            (null, not null) => $"<a href=\"{Encode(link)}\" style=\"color: #0f6cbd;\">Open in Spaarke</a>",
            _ => "Not filed under a record",
        };
        var whatToDo = Encode(description.Trim()).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br />", StringComparison.Ordinal);
        var dueText = due is { } d ? d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) : "No due date";

        var body = RegistrationEmailService.FillTemplate(template, new Dictionary<string, string>
        {
            ["RecordNoun"] = Encode(record?.Noun ?? "Record"),
            ["Record"] = recordHtml,
            ["WhatToDo"] = whatToDo,
            ["Due"] = Encode(dueText),
            ["Year"] = year.ToString(CultureInfo.InvariantCulture),
        });
        var subject = label is null ? "A task was assigned to you" : $"A task was assigned to you: {label}";
        return (subject, body);
    }

    // ── The email ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<string?> EmailAsync(HttpContext http, AssignTaskNotice notice, RecordFacts? record, CancellationToken ct)
    {
        string? address;
        try
        {
            address = await ReadAddressAsync(notice.AssigneeContactId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Not "no address": the job reads it again.
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack, ex,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId}'s address could not be read for task {TaskId}; falling back to the system mailbox.",
                notice.AssigneeContactId, notice.TaskId);
            return await FallBackAsync(notice, ct).ConfigureAwait(false) ? null : EmailNotSentWarning;
        }

        if (address is null)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId} has no usable email address; no email was sent for task {TaskId}.",
                notice.AssigneeContactId, notice.TaskId);
            return EmailMissingWarning;
        }

        var content = BuildEmail(Template.Value, record, notice.Description, notice.Due, DateTime.UtcNow.Year);
        var request = Request(content, address, notice.Core?.Entity, notice.Core?.Id, record,
            notice.TaskId is { } task ? CorrelationIdFor(task) : null) with
        { SendMode = SendMode.User };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(UserSendTimeout);
        try
        {
            await SendAsUserAsync(request, http, timeout.Token).ConfigureAwait(false);
            _logger.LogInformation(AssignTaskNotifierEvents.EmailSentAsUser,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: emailed contact {ContactId} from the assigner's mailbox.", notice.TaskId, notice.AssigneeContactId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // D-194: no mailbox, no consent, a Graph error or the timeout. The system mailbox sends it from a job (the request does not
            // wait twice). Known limit: a send that reached Graph and timed out on our side may also arrive.
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack, ex,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the assigner's send to contact {ContactId} failed ({ExceptionType}{Timeout}); falling back to the system mailbox.",
                notice.TaskId, notice.AssigneeContactId, ex.GetType().Name, timeout.IsCancellationRequested ? ", timed out" : string.Empty);
            return await FallBackAsync(notice, ct).ConfigureAwait(false) ? null : EmailNotSentWarning;
        }
    }

    /// <summary>Queues the system-mailbox job. False when it could not be queued (logged).</summary>
    private async Task<bool> FallBackAsync(AssignTaskNotice notice, CancellationToken ct)
    {
        if (notice.TaskId is not { } taskId)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailFellBack,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId}: the task id is unknown, so no system-mailbox email can be queued.", notice.AssigneeContactId);
            return false;
        }

        var payload = new AssignTaskEmailPayload(taskId, notice.AssigneeContactId, notice.Core?.Entity, notice.Core?.Id);
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
            _logger.LogError(AssignTaskNotifierEvents.EmailFellBack, ex,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the system-mailbox email to contact {ContactId} could not be queued ({ExceptionType}); no email was sent.",
                taskId, notice.AssigneeContactId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>The send request both paths share: the one recipient, the HTML body, filed under the record when there is one.</summary>
    private static SendCommunicationRequest Request(
        (string Subject, string Body) content, string address, string? coreEntity, Guid? coreId, RecordFacts? record, string? correlationId) =>
        new()
        {
            To = [address],
            Subject = content.Subject,
            Body = content.Body,
            BodyFormat = BodyFormat.HTML,
            CorrelationId = correlationId,
            Associations = coreEntity is not null && coreId is { } id && id != Guid.Empty
                ? [new CommunicationAssociation { EntityType = coreEntity, EntityId = id, EntityName = record?.Label }]
                : null,
        };

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

    // ── The bell ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task BellAsync(AssignTaskNotice notice, RecordFacts? record, CancellationToken ct)
    {
        Guid? user;
        try
        {
            user = await PersonalToDoAssignee.SingleEnabledUserAsync(_links, notice.AssigneeContactId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.BellNotCreated, ex,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the users linked to contact {ContactId} could not be read; no bell notification.",
                notice.TaskId, notice.AssigneeContactId);
            return;
        }

        if (user is not { } userId)
        {
            // D-194: zero or several enabled linked users, or only ineligible ones: the email alone goes.
            _logger.LogInformation(AssignTaskNotifierEvents.BellNotCreated,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: contact {ContactId} has no single enabled linked user; no bell notification.",
                notice.TaskId, notice.AssigneeContactId);
            return;
        }

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
            _logger.LogWarning(AssignTaskNotifierEvents.BellNotCreated, ex,
                "[ASSIGN-TASK-NOTIFY] task {TaskId}: the bell notification for user {UserId} could not be created; the email is unaffected.",
                notice.TaskId, userId);
        }
    }

    // ── The record ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The record's label (read app-only; best effort: a fault leaves it out), its absolute app link for the email and its
    /// relative link for the bell. Null when there is no record.</summary>
    private async Task<RecordFacts?> ReadRecordAsync(string? entity, Guid? id, CancellationToken ct)
    {
        if (entity is null || id is not { } recordId || recordId == Guid.Empty)
            return null;

        var known = LabelColumns.TryGetValue(entity, out var columns);
        string? label = null;
        if (known)
        {
            try
            {
                var row = await _entities.RetrieveAsync(entity, recordId, [columns.Name, columns.Number], ct).ConfigureAwait(false);
                label = InquiryRecordFacts.RecordLabel(row.GetAttributeValue<string>(columns.Name), row.GetAttributeValue<string>(columns.Number));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped, ex,
                    "[ASSIGN-TASK-NOTIFY] the name of {Entity} {RecordId} could not be read; the notice goes without it.", entity, recordId);
            }
        }

        var appLink = InquiryRecordFacts.AppLink(_environmentUrl, entity, recordId);
        var relative = appLink is null ? null : $"/main.aspx?etn={entity}&id={recordId:D}&pagetype=entityrecord";
        return new RecordFacts(known ? columns.Noun : "Record", label, appLink, relative);
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
