using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Email;

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
    private readonly ILogger<EmailAttachmentAddPlanner> _logger;

    public EmailAttachmentAddPlanner(
        IDocumentDataverseService documents,
        IGenericEntityService entities,
        SpeFileStore speFileStore,
        IEmailToEmlConverter emlConverter,
        AttachmentFilterService attachmentFilter,
        ILogger<EmailAttachmentAddPlanner> logger)
    {
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
    /// <param name="AttachmentNames">The attachments to add, by file name; never empty.</param>
    public sealed record Plan(
        Guid EmlDocumentId,
        string DriveId,
        string ItemId,
        string EmlFileName,
        long EmlFileSize,
        IReadOnlyList<string> AttachmentNames);

    /// <summary>
    /// The plan for a re-save of an email whose earlier save created <paramref name="emlDocumentId"/> on
    /// <paramref name="emlItemId"/>, or <see langword="null"/> when there is nothing to add: the selection names no attachment
    /// that is not already saved, or the <c>.eml</c> no longer exists or no longer points at that item.
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
    {
        var eml = await _documents.GetDocumentAsync(emlDocumentId.ToString("D"), cancellationToken);
        if (eml is null
            || string.IsNullOrEmpty(eml.GraphDriveId)
            || !string.Equals(eml.GraphItemId, emlItemId, StringComparison.Ordinal))
        {
            // A deleted .eml, or one that no longer points at the item its save uploaded: no child is created under it.
            _logger.LogInformation(
                "Email re-save: the saved .eml document {DocumentId} is gone or no longer points at its item; nothing to add",
                emlDocumentId);
            return null;
        }

        var filing = await _entities.RetrieveAsync(
            "sprk_document", emlDocumentId, DocumentLinkFields.LogicalNames.ToArray(), cancellationToken);
        if (!IsFiledTo(filing, targetLogicalName, targetRecordId))
        {
            // The key matched a save that filed this .eml somewhere else (or it was re-filed since): adding under it would
            // make children of another record's email, and expose that record's content to this record's readers.
            _logger.LogWarning(
                "Email re-save: the saved .eml document {DocumentId} is not filed to this request's record; nothing is added",
                emlDocumentId);
            return null;
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
            return null;
        }

        var stream = await _speFileStore.DownloadFileAsync(eml.GraphDriveId, emlItemId, cancellationToken);
        if (stream is null)
        {
            _logger.LogWarning("Email re-save: the saved .eml of document {DocumentId} could not be read; nothing to add", emlDocumentId);
            return null;
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

            _logger.LogInformation(
                "Email re-save: {Missing} of {Wanted} selected attachment(s) are not yet saved under .eml document {DocumentId}",
                missing.Count, chosen.Wanted.Count, emlDocumentId);

            return missing.Count == 0
                ? null
                : new Plan(emlDocumentId, eml.GraphDriveId, emlItemId, eml.FileName ?? eml.Name, eml.FileSize ?? 0, missing);
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
        string.Equals(child.Name, attachmentFileName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(child.FileName, attachmentFileName, StringComparison.OrdinalIgnoreCase);
}
