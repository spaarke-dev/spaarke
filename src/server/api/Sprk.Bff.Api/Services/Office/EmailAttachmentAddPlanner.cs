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
/// the same converter, noise filter and selection rule the upload-finalization worker uses, minus the attachments the
/// <c>.eml</c> already has a child document for (<see cref="IsAlreadySaved"/>). The worker applies the same two static rules
/// (<see cref="ApplySelection"/>, <see cref="ExcludeAlreadySaved"/>) when it creates the children, so the save's answer and the
/// worker's work cannot drift apart.</para>
/// <para>Notes: <c>projects/spaarkeai-word-add-in-r1/notes/138-email-resave-add-attachments.md</c>.</para>
/// </remarks>
public class EmailAttachmentAddPlanner
{
    private readonly IDocumentDataverseService _documents;
    private readonly SpeFileStore _speFileStore;
    private readonly IEmailToEmlConverter _emlConverter;
    private readonly AttachmentFilterService _attachmentFilter;
    private readonly ILogger<EmailAttachmentAddPlanner> _logger;

    public EmailAttachmentAddPlanner(
        IDocumentDataverseService documents,
        SpeFileStore speFileStore,
        IEmailToEmlConverter emlConverter,
        AttachmentFilterService attachmentFilter,
        ILogger<EmailAttachmentAddPlanner> logger)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
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
    /// <remarks>A read that fails throws: the caller answers the save as it would without this task (a duplicate).</remarks>
    public async Task<Plan?> PlanAsync(
        Guid emlDocumentId,
        string emlItemId,
        IReadOnlyCollection<string>? selection,
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

        var stream = await _speFileStore.DownloadFileAsync(eml.GraphDriveId, emlItemId, cancellationToken);
        if (stream is null)
        {
            _logger.LogWarning("Email re-save: the saved .eml of document {DocumentId} could not be read; nothing to add", emlDocumentId);
            return null;
        }

        IReadOnlyList<EmailAttachmentInfo> attachments;
        using (stream)
        {
            attachments = _emlConverter.ExtractAttachments(stream);
        }

        try
        {
            var wanted = ApplySelection(_attachmentFilter.FilterAttachments(attachments), selection);
            if (wanted.Count == 0)
            {
                return null;
            }

            var children = await _documents.GetDocumentsByParentAsync(emlDocumentId, cancellationToken);
            var missing = ExcludeAlreadySaved(wanted, children)
                .Select(a => a.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation(
                "Email re-save: {Missing} of {Wanted} selected attachment(s) are not yet saved under .eml document {DocumentId}",
                missing.Count, wanted.Count, emlDocumentId);

            return missing.Count == 0
                ? null
                : new Plan(emlDocumentId, eml.GraphDriveId, emlItemId, eml.FileName ?? eml.Name, eml.FileSize ?? 0, missing);
        }
        finally
        {
            foreach (var attachment in attachments)
            {
                attachment.Content?.Dispose();
            }
        }
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
