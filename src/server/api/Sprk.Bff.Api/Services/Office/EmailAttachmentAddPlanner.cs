using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Workers.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209): which attachments of an email ALREADY saved as a <c>.eml</c> document a
/// re-save names that are not yet saved under it. Read-only: it creates nothing.
/// </summary>
/// <remarks>
/// <para>The answer is taken from the stored state, never from the client: the attachments of the STORED <c>.eml</c>, through
/// the ONE selection the upload-finalization worker also calls (<see cref="SelectAttachments"/>: converter, noise filter,
/// selection, "can be created"), minus the attachments the <c>.eml</c> already has a child document for
/// (<see cref="IsAlreadySaved"/>). The worker applies <see cref="ExcludeAlreadySaved"/> too when it creates the children, so
/// the save's answer and the worker's work cannot drift apart. The children are read first: a selection whose every name
/// already has one reads no SPE bytes.</para>
/// <para>Notes: <c>projects/spaarkeai-word-add-in-r1/notes/138-email-resave-add-attachments.md</c>.</para>
/// </remarks>
public class EmailAttachmentAddPlanner
{
    private readonly IDocumentDataverseService _documents;
    private readonly IGenericEntityService _entities;
    private readonly SpeFileStore _speFileStore;
    private readonly IEmailToEmlConverter _emlConverter;
    private readonly AttachmentFilterService _attachmentFilter;
    private readonly ITenantCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<EmailAttachmentAddPlanner> _logger;

    /// <summary>
    /// The phase a save that queued an attachments-only finalization records on its job (see
    /// <see cref="IsFinalizationPendingAsync"/>, which tells such a job from a first save's).
    /// </summary>
    internal const string AttachmentsQueuedPhase = "AttachmentsQueued";

    /// <summary>
    /// The message key of the attachments-only finalization a re-save queues for job <paramref name="jobId"/>: its own, so the
    /// first save's "already processed" mark does not skip it. The worker sets its processed mark under this key.
    /// </summary>
    internal static string AddJobMessageKey(string idempotencyKey, Guid jobId) =>
        $"{idempotencyKey}|attachments-add|{jobId:N}";

    public EmailAttachmentAddPlanner(
        IDocumentDataverseService documents,
        IGenericEntityService entities,
        SpeFileStore speFileStore,
        IEmailToEmlConverter emlConverter,
        AttachmentFilterService attachmentFilter,
        ITenantCache cache,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<EmailAttachmentAddPlanner> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _emlConverter = emlConverter ?? throw new ArgumentNullException(nameof(emlConverter));
        _attachmentFilter = attachmentFilter ?? throw new ArgumentNullException(nameof(attachmentFilter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The <c>.eml</c> document a re-save would add attachments under, and the names of the attachments to add.
    /// </summary>
    /// <param name="EmlDocumentId">The existing <c>.eml</c> document.</param>
    /// <param name="DriveId">Its SPE drive.</param>
    /// <param name="ItemId">Its SPE item.</param>
    /// <param name="EmlFileName">Its stored file name (the children's "attachment from" name).</param>
    /// <param name="EmlFileSize">Its size.</param>
    /// <param name="AttachmentNames">The attachments to add, by file name; empty when the plan only reports
    /// <see cref="NotStored"/> or <see cref="NotSaveable"/>.</param>
    public sealed record Plan(
        Guid EmlDocumentId,
        string DriveId,
        string ItemId,
        string EmlFileName,
        long EmlFileSize,
        IReadOnlyList<string> AttachmentNames)
    {
        /// <summary>
        /// The names the selection asked for that the stored <c>.eml</c> does not hold (the first save left them out: unreadable
        /// or too big then). They cannot be added: the app creates children only from the stored <c>.eml</c>, never from the
        /// request's own bytes. Reported, so the user is told instead of seeing "Saved".
        /// </summary>
        public IReadOnlyList<string> NotStored { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Task 141 review: the names the selection asked for that the stored <c>.eml</c> DOES hold but an email save never
        /// creates a document for: the noise filter drops them (<see cref="AttachmentFilterService"/>: a calendar file, a
        /// blocked extension, an inline or signature image, a tiny image, an oversized file) or they have no content
        /// (<see cref="CanBeCreated"/>), and no child saves them. They can never be added, by this save or a new one, so they
        /// are reported (never answered as "nothing to add", which the pane would show as saved). Kept apart from
        /// <see cref="NotStored"/> because the advice differs: saving the email again as a new document does not help here.
        /// </summary>
        public IReadOnlyList<string> NotSaveable { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Task 141 review: the <c>.eml</c> row's own <c>sprk_relatedcommunication</c>, read with its filing (named add only;
        /// <see langword="null"/> when it has none, and on task 138's key-matched add). The children of a named add are linked to
        /// this communication, never to one found from the request.
        /// </summary>
        public Guid? CommunicationId { get; init; }
    }

    /// <summary>
    /// The plan for a re-save of an email whose earlier save created <paramref name="emlDocumentId"/> on
    /// <paramref name="emlItemId"/>, or <see langword="null"/> when there is nothing to add or report: the selection names no
    /// attachment that is not already saved, or the <c>.eml</c> no longer exists or no longer points at that item.
    /// </summary>
    /// <param name="selection"><c>null</c> = every attachment; a list = those names. The caller never passes <c>[]</c>
    /// (none): there is no add operation for it.</param>
    /// <param name="targetLogicalName">The logical name of the record the request files to (<see cref="DocumentAssociationMap.ToLogicalName"/>).</param>
    /// <param name="targetRecordId">That record's id.</param>
    /// <remarks>
    /// <para>A read that fails throws: the caller answers the save as it would without this task (a duplicate).</para>
    /// <para>The <c>.eml</c> must still be filed to the request's record (<see cref="IsFiledTo"/>): the job that named it was
    /// found by an idempotency key, which says nothing about WHERE the email was filed (the client may send its own key, and
    /// the <c>.eml</c> may have been re-filed since). Otherwise nothing is added.</para>
    /// </remarks>
    public async Task<Plan?> PlanAsync(
        Guid emlDocumentId,
        string emlItemId,
        IReadOnlyCollection<string>? selection,
        string? targetLogicalName,
        Guid targetRecordId,
        CancellationToken cancellationToken)
        => (await PlanCoreAsync(emlDocumentId, emlItemId, selection, targetLogicalName, targetRecordId, cancellationToken)).Plan;

    /// <summary>
    /// Task 141: why <see cref="PlanForSavedEmailAsync"/> has no plan to act on.
    /// </summary>
    public enum SavedEmailOutcome
    {
        /// <summary>There is a plan: names to add, names the <c>.eml</c> does not hold, or both.</summary>
        Planned,

        /// <summary>Every name asked for already has a child document: nothing to add or report.</summary>
        NothingToAdd,

        /// <summary>The document is gone, is not a saved email (<c>.eml</c>), or has no file to read the attachments from.</summary>
        NotASavedEmail,

        /// <summary>The <c>.eml</c> is not filed to the request's record (<see cref="IsFiledTo"/>).</summary>
        NotFiledToTheRecord,

        /// <summary>The stored <c>.eml</c> could not be read.</summary>
        Unreadable,

        /// <summary>
        /// Task 141 review: the request's email is not the email the named <c>.eml</c> stores (<see cref="IsTheNamedEmailAsync"/>).
        /// </summary>
        NotThisEmail,
    }

    /// <summary>
    /// Task 141 review: the keys of the email a named add's request describes: its RFC Message-ID
    /// (<see cref="OfficeEmailEnricher.ResolveRfcMessageId"/>) and its Exchange item id
    /// (<see cref="OfficeEmailEnricher.ResolveExchangeItemId"/>).
    /// </summary>
    public sealed record RequestEmailKeys(string? RfcMessageId, string? ExchangeItemId);

    /// <summary><c>sprk_document</c>'s stored message id (the Office save writes the RFC Message-ID, or an item id before task 121).</summary>
    internal const string EmailMessageIdColumn = "sprk_emailmessageid";

    private const string RelatedCommunicationColumn = "sprk_relatedcommunication";
    private const string CommunicationEntityName = "sprk_communication";
    private const string CommunicationInternetMessageIdColumn = "sprk_internetmessageid";

    /// <summary>
    /// Task 141: the plan for an add-attachments save that NAMES the saved email (<c>SaveRequest.Email.SavedDocumentId</c>), so it
    /// needs no earlier job under the same key. The same plan as <see cref="PlanAsync"/> (filing check, children first, the ONE
    /// selection over the STORED <c>.eml</c>, <see cref="ExcludeAlreadySaved"/>), read from the document's own SPE pointer instead
    /// of a job's, plus a check that the document is a saved email at all. It says why there is no plan, so the save can refuse
    /// instead of answering "already saved". Read-only. A read that faults throws.
    /// </summary>
    /// <param name="selection">The names to add. The caller never passes <c>null</c> or <c>[]</c>: the request validator
    /// refuses an add that names none.</param>
    /// <param name="requestEmail">The keys of the email the request describes. The named <c>.eml</c> must store that email
    /// (<see cref="IsTheNamedEmailAsync"/>), else <see cref="SavedEmailOutcome.NotThisEmail"/>: the request's metadata never
    /// decides anything about another email's <c>.eml</c>.</param>
    public async Task<(SavedEmailOutcome Outcome, Plan? Plan)> PlanForSavedEmailAsync(
        Guid emlDocumentId,
        IReadOnlyCollection<string> selection,
        RequestEmailKeys requestEmail,
        string? targetLogicalName,
        Guid targetRecordId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestEmail);
        return await PlanCoreAsync(
            emlDocumentId, emlItemId: null, selection, targetLogicalName, targetRecordId, cancellationToken, requestEmail);
    }

    /// <summary>
    /// The plan shared by both triggers. <paramref name="emlItemId"/> is the SPE item an earlier job uploaded (task 138: the
    /// <c>.eml</c> must still point at it), or <see langword="null"/> when the request named the document (task 141: its own
    /// pointer is used, and it must be a saved email).
    /// </summary>
    private async Task<(SavedEmailOutcome Outcome, Plan? Plan)> PlanCoreAsync(
        Guid emlDocumentId,
        string? emlItemId,
        IReadOnlyCollection<string>? selection,
        string? targetLogicalName,
        Guid targetRecordId,
        CancellationToken cancellationToken,
        RequestEmailKeys? requestEmail = null)
    {
        var eml = await _documents.GetDocumentAsync(emlDocumentId.ToString("D"), cancellationToken);
        var itemId = emlItemId ?? eml?.GraphItemId;
        if (eml is null
            || string.IsNullOrEmpty(eml.GraphDriveId)
            || string.IsNullOrEmpty(itemId)
            || !string.Equals(eml.GraphItemId, itemId, StringComparison.Ordinal)
            || (emlItemId is null && !IsSavedEmail(eml)))
        {
            // A deleted .eml, one that no longer points at the item its save uploaded, or (task 141) a named document that is
            // not a saved email: no child is created under it.
            _logger.LogInformation(
                "Email re-save: the saved .eml document {DocumentId} is gone, is not a saved email, or no longer points at its "
                + "item; nothing to add",
                emlDocumentId);
            return (SavedEmailOutcome.NotASavedEmail, null);
        }

        // A named add (task 141) also reads the row's own message id, in the same read; the communication link is one of the
        // filing columns.
        var columns = requestEmail is null
            ? DocumentLinkFields.LogicalNames.ToArray()
            : DocumentLinkFields.LogicalNames.Append(EmailMessageIdColumn).ToArray();
        var filing = await _entities.RetrieveAsync("sprk_document", emlDocumentId, columns, cancellationToken);
        if (requestEmail is not null && !await IsTheNamedEmailAsync(filing, requestEmail, cancellationToken))
        {
            // Task 141 review: the request names this .eml but describes another email. Nothing is added, captured or filed
            // for either email.
            _logger.LogWarning(
                "Email attachments add: the request's email is not the email .eml document {DocumentId} stores; nothing is added",
                emlDocumentId);
            return (SavedEmailOutcome.NotThisEmail, null);
        }

        if (!IsFiledTo(filing, targetLogicalName, targetRecordId))
        {
            // The key matched a save that filed this .eml somewhere else (or it was re-filed since): adding under it would
            // make children of another record's email, and expose that record's content to this record's readers.
            _logger.LogWarning(
                "Email re-save: the saved .eml document {DocumentId} is not filed to this request's record; nothing is added",
                emlDocumentId);
            return (SavedEmailOutcome.NotFiledToTheRecord, null);
        }

        // The children first: a selection whose every name already has a child has nothing missing, whatever the .eml holds,
        // and then no SPE byte is read (as before this task). Only a selection of every attachment (null), or one naming a
        // file with no child yet, needs the .eml to say what is there.
        var children = (await _documents.GetDocumentsByParentAsync(emlDocumentId, cancellationToken)).ToList();
        if (selection is not null
            && selection.All(name => children.Any(child => IsAlreadySaved(child, name))))
        {
            _logger.LogInformation(
                "Email re-save: every selected attachment already has a child under .eml document {DocumentId}; nothing to add",
                emlDocumentId);
            return (SavedEmailOutcome.NothingToAdd, null);
        }

        var stream = await _speFileStore.DownloadFileAsync(eml.GraphDriveId, itemId, cancellationToken);
        if (stream is null)
        {
            _logger.LogWarning("Email re-save: the saved .eml of document {DocumentId} could not be read; nothing to add", emlDocumentId);
            return (SavedEmailOutcome.Unreadable, null);
        }

        EmailAttachmentSelection chosen;
        using (stream)
        {
            chosen = SelectAttachments(stream, _emlConverter, _attachmentFilter, selection);
        }

        try
        {
            var missing = ExcludeAlreadySaved(chosen.Wanted, children)
                .Select(a => a.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The names the selection asked for that the stored .eml does not hold at all. Nothing is created for them from the
            // request's bytes; the answer names them.
            var stored = new HashSet<string>(chosen.All.Select(a => a.FileName), StringComparer.OrdinalIgnoreCase);
            var notStored = selection is null
                ? new List<string>()
                : selection.Where(name => !stored.Contains(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Task 141 review: the names the .eml holds that the ONE selection drops (the noise filter, or no content) and no
            // child saves: no save can ever create them, so they are reported too, never answered as "nothing to add".
            var creatable = new HashSet<string>(chosen.Wanted.Select(a => a.FileName), StringComparer.OrdinalIgnoreCase);
            var notSaveable = selection is null
                ? new List<string>()
                : selection
                    .Where(name => stored.Contains(name)
                        && !creatable.Contains(name)
                        && !children.Any(child => IsAlreadySaved(child, name)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _logger.LogInformation(
                "Email re-save: {Missing} of {Wanted} selected attachment(s) are not yet saved under .eml document {DocumentId}; "
                + "{NotStored} selected name(s) are not in the stored .eml; {NotSaveable} can never be saved",
                missing.Count, chosen.Wanted.Count, emlDocumentId, notStored.Count, notSaveable.Count);

            return missing.Count == 0 && notStored.Count == 0 && notSaveable.Count == 0
                ? (SavedEmailOutcome.NothingToAdd, null)
                : (SavedEmailOutcome.Planned,
                    new Plan(emlDocumentId, eml.GraphDriveId, itemId, eml.FileName ?? eml.Name, eml.FileSize ?? 0, missing)
                    {
                        NotStored = notStored,
                        NotSaveable = notSaveable,
                        CommunicationId = requestEmail is null ? null : CommunicationOf(filing),
                    });
        }
        finally
        {
            foreach (var attachment in chosen.All)
            {
                attachment.Content?.Dispose();
            }
        }
    }

    /// <summary>
    /// Whether the finalization of the job a re-save matched has NOT finished yet: its worker has not set the processed mark
    /// AND the job is younger than <see cref="OfficeJobStatusService.MaxSaveDuration"/> (the age past which a save that never
    /// completed reads as abandoned). While it has not finished the attachments are still being created, so a re-save would
    /// count them as missing and add them again: it adds nothing yet. Once the mark is set, or the job is older than that
    /// age, planning proceeds (the children are re-read and the add job's own message key keeps a second add from repeating
    /// the first).
    /// </summary>
    /// <remarks>
    /// The mark is the worker's own (<see cref="UploadFinalizationWorker.ProcessedMarkResource"/>), set under the message key
    /// its job was queued with: the save's idempotency key for a first save, <see cref="AddJobMessageKey"/> for a job that
    /// queued an attachments-only finalization (<see cref="AttachmentsQueuedPhase"/>). A read that faults throws: the caller
    /// answers the save as a duplicate.
    /// </remarks>
    public async Task<bool> IsFinalizationPendingAsync(
        JobStatusResponse job,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var messageKey = string.Equals(job.CurrentPhase, AttachmentsQueuedPhase, StringComparison.Ordinal)
            ? AddJobMessageKey(idempotencyKey, job.JobId)
            : idempotencyKey;

        var mark = await _cache.GetAsync<string>(
            UploadFinalizationWorker.TenantIdForCache(_configuration),
            resource: UploadFinalizationWorker.ProcessedMarkResource,
            id: messageKey,
            version: UploadFinalizationWorker.ProcessedMarkVersion,
            ct: cancellationToken);
        if (!string.IsNullOrEmpty(mark))
        {
            return false;
        }

        var young = _time.GetUtcNow() - job.CreatedAt < OfficeJobStatusService.MaxSaveDuration;
        if (young)
        {
            _logger.LogInformation(
                "Email re-save: the matched job {JobId} has not finished creating its documents; nothing is added yet", job.JobId);
        }

        return young;
    }

    /// <summary>
    /// What a stored <c>.eml</c> holds (<see cref="All"/>, the caller disposes every <c>Content</c>) and which of it an email
    /// save would create documents for (<see cref="Wanted"/>).
    /// </summary>
    internal sealed record EmailAttachmentSelection(
        IReadOnlyList<EmailAttachmentInfo> All,
        IReadOnlyList<EmailAttachmentInfo> Wanted);

    /// <summary>
    /// THE selection of attachments an email save creates documents for, from the stored <c>.eml</c>: extract (the converter),
    /// drop the noise (the filter), apply the user's selection (<see cref="ApplySelection"/>), and keep only what can become a
    /// document (<see cref="CanBeCreated"/>). The save's "not yet saved" answer (this class) and the upload-finalization worker's
    /// create loop both call it, so what the save says it adds is what the worker can create.
    /// </summary>
    internal static EmailAttachmentSelection SelectAttachments(
        Stream emlStream,
        IEmailToEmlConverter converter,
        AttachmentFilterService filter,
        IReadOnlyCollection<string>? selection)
    {
        var all = converter.ExtractAttachments(emlStream);
        var wanted = ApplySelection(filter.FilterAttachments(all), selection)
            .Where(CanBeCreated)
            .ToList();
        return new EmailAttachmentSelection(all, wanted);
    }

    /// <summary>
    /// Whether an attachment can become a document: it has content. The worker's create (<c>ProcessSingleAttachmentAsync</c>)
    /// skips one with none, so it would otherwise be counted missing on every re-save while nothing is ever created.
    /// </summary>
    internal static bool CanBeCreated(EmailAttachmentInfo attachment) =>
        attachment.Content is { Length: > 0 };

    /// <summary>
    /// Whether the <c>.eml</c> row is filed to the record <paramref name="targetLogicalName"/> / <paramref name="targetRecordId"/>:
    /// it carries at least one link of that record's type (both column families, <c>sprk_*</c> and <c>sprk_related*</c>, from
    /// <see cref="DocumentLinkFields.All"/>), and EVERY link of that type points at that record. A link to a different record of
    /// the type (a re-filing, or a save to another record) makes it not filed here. No target, an unknown type or an unreadable
    /// row is not filed here: fail closed.
    /// </summary>
    internal static bool IsFiledTo(Microsoft.Xrm.Sdk.Entity? emlRow, string? targetLogicalName, Guid targetRecordId)
    {
        if (emlRow is null || string.IsNullOrEmpty(targetLogicalName) || targetRecordId == Guid.Empty)
        {
            return false;
        }

        var links = DocumentLinkFields.All
            .Where(f => string.Equals(f.TargetEntityLogicalName, targetLogicalName, StringComparison.Ordinal))
            .Select(f => emlRow.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(f.LogicalName))
            .Where(r => r is { Id: var id } && id != Guid.Empty)
            .ToList();
        return links.Count > 0 && links.All(r => r!.Id == targetRecordId);
    }

    /// <summary>
    /// The attachment selection rule of an email save: <c>null</c> = every attachment, a list = the attachments with those
    /// file names (case-insensitive). The caller handles <c>[]</c> (none) before asking.
    /// </summary>
    internal static IReadOnlyList<EmailAttachmentInfo> ApplySelection(
        IReadOnlyList<EmailAttachmentInfo> attachments,
        IReadOnlyCollection<string>? selection)
    {
        if (selection is null)
        {
            return attachments;
        }

        var selectedNames = new HashSet<string>(selection, StringComparer.OrdinalIgnoreCase);
        return attachments.Where(a => selectedNames.Contains(a.FileName)).ToList();
    }

    /// <summary>
    /// <paramref name="attachments"/> without those the <c>.eml</c> already has a child document for
    /// (<see cref="IsAlreadySaved"/>).
    /// </summary>
    internal static IReadOnlyList<EmailAttachmentInfo> ExcludeAlreadySaved(
        IReadOnlyList<EmailAttachmentInfo> attachments,
        IEnumerable<DocumentEntity>? existingChildren)
    {
        var children = existingChildren?.ToList() ?? new List<DocumentEntity>();
        return attachments.Where(a => !children.Any(c => IsAlreadySaved(c, a.FileName))).ToList();
    }

    /// <summary>
    /// Whether <paramref name="child"/> is the saved document of the attachment named <paramref name="attachmentFileName"/>:
    /// the worker creates each child named (<c>sprk_documentname</c>) and filed (<c>sprk_filename</c>) by the attachment's
    /// file name, so either one equal to it, case-insensitive as the selection is, is that attachment.
    /// </summary>
    internal static bool IsAlreadySaved(DocumentEntity child, string attachmentFileName) =>
        SavedNamesOf(child).Any(name => string.Equals(name, attachmentFileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The attachment names <paramref name="child"/> counts as saving: its <c>sprk_documentname</c> and its
    /// <c>sprk_filename</c> (the non-empty ones). THE name rule: <see cref="IsAlreadySaved"/> is "the attachment's file name is
    /// one of these, case-insensitive", and task 141's <c>savedAttachmentNames</c> on the email identity response is these names
    /// for every child the caller may read, so the pane marks an attachment saved by the same rule (a case-insensitive match).
    /// </summary>
    internal static IEnumerable<string> SavedNamesOf(DocumentEntity child)
    {
        if (!string.IsNullOrEmpty(child.Name))
        {
            yield return child.Name;
        }

        if (!string.IsNullOrEmpty(child.FileName))
        {
            yield return child.FileName;
        }
    }

    /// <summary>
    /// Task 141 review: whether the <c>.eml</c> row <paramref name="emlRow"/> stores the email <paramref name="requestEmail"/>
    /// describes, by the rule <c>resolve-email-identity</c> found it with (<c>DocumentUrlIdentityResolution.ResolveByEmailMessageIdAsync</c>):
    /// the row's own <c>sprk_emailmessageid</c> equals either key, OR its <c>sprk_relatedcommunication</c>'s
    /// <c>sprk_internetmessageid</c> equals the RFC Message-ID (a mailbox-captured or Spaarke-sent archive stores no key of its
    /// own). Compared case-insensitively, as Dataverse string equality is. No key, an unreadable row or a communication that
    /// cannot be found: not this email (fail closed). The communication is read only when the row's own key does not match.
    /// </summary>
    internal async Task<bool> IsTheNamedEmailAsync(
        Microsoft.Xrm.Sdk.Entity? emlRow, RequestEmailKeys requestEmail, CancellationToken cancellationToken)
    {
        if (emlRow is null)
        {
            return false;
        }

        var stored = emlRow.GetAttributeValue<string>(EmailMessageIdColumn);
        if (SameKey(stored, requestEmail.RfcMessageId) || SameKey(stored, requestEmail.ExchangeItemId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(requestEmail.RfcMessageId) || CommunicationOf(emlRow) is not { } communicationId)
        {
            return false;
        }

        var communication = await _entities.RetrieveAsync(
            CommunicationEntityName, communicationId, new[] { CommunicationInternetMessageIdColumn }, cancellationToken);
        return SameKey(communication?.GetAttributeValue<string>(CommunicationInternetMessageIdColumn), requestEmail.RfcMessageId);
    }

    private static bool SameKey(string? stored, string? requested) =>
        !string.IsNullOrWhiteSpace(stored)
        && !string.IsNullOrWhiteSpace(requested)
        && string.Equals(stored, requested, StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>.eml</c> row's own <c>sprk_relatedcommunication</c>, or <see langword="null"/>.</summary>
    private static Guid? CommunicationOf(Microsoft.Xrm.Sdk.Entity? emlRow) =>
        emlRow?.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(RelatedCommunicationColumn) is { Id: var id } && id != Guid.Empty
            ? id
            : null;

    /// <summary>
    /// Task 141: whether a document the request named is a saved email: flagged an email archive, or stored as a <c>.eml</c>
    /// (the Office save writes both). Anything else (a Word document, an attachment child) has no stored email to read
    /// attachments from.
    /// </summary>
    internal static bool IsSavedEmail(DocumentEntity document) =>
        document.IsEmailArchive == true
        || (document.FileName?.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) ?? false);
}
