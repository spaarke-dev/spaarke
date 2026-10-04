using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// The ONE server-side owner of WHERE a <c>sprk_document</c>'s file lives (unified-access-control-r2 task 166 f1; owner
/// round 21 item 1 (i)–(ii), round 26 item 3): the only writer of a document's SharePoint Embedded pointer
/// (<c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c>) outside a server path that uploads the bytes itself.
/// </summary>
/// <remarks>
/// <para><b>Two operations, one pointer-attach path.</b></para>
/// <list type="number">
/// <item><see cref="AttachFileAsync"/> — the CLIENT pointer writers' replacement (round 21 item 1 (i), ADR-002 WP-3):
/// the client creates the row WITHOUT pointers, uploads the bytes through the record-keyed (or record-less) upload
/// route, and asks the BFF to attach them. The BFF verifies, then stamps the pointer as the application — the identity
/// the pointer columns' field-level security admits (<c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>).</item>
/// <item><see cref="RelocateIfMisplacedAsync"/> — moves a file that is NOT in its document's derived container into
/// it: copy through the BFF identity, verify (size, and <c>quickXorHash</c> where Graph returns one), re-point through
/// the SAME pointer-attach path, then delete the source (never before the copy is verified, and never while another
/// row still points at it). Two callers, one mechanism (round 26 item 3): task 166's legacy migration
/// (<see cref="DocumentContainerMigrationJob"/>) and the Make Secure transition (task 150's provisioning lane).</item>
/// </list>
/// <para><b>Every step is logged with before / after ids</b> (<c>[DOCUMENT-RELOCATE]</c>), so an interrupted relocation
/// is recoverable from the log: a copy that was never re-pointed is an orphan in the TARGET container; a re-pointed
/// row whose source survived names the source it left.</para>
/// <para><b>Placement</b> (CLAUDE.md §10; <c>.claude/constraints/bff-extensions.md</c>): in the BFF — it composes the
/// BFF's own container decisions (<see cref="RecordContainerResolver"/>), its app-only SPE facade and its Dataverse
/// application identity, and the pointer columns are written by that identity alone. Scoped, registered unconditionally
/// in <c>DocumentsModule</c>: the route that calls it is mapped unconditionally (§F.1).</para>
/// </remarks>
public sealed class DocumentContainerRelocator
{
    /// <summary>Graph's simple-upload boundary for SPE (250 MB). A larger file is reported, never half-copied.</summary>
    internal const long MaxRelocatableBytes = 250L * 1024 * 1024;

    private const string DocumentEntity = "sprk_document";
    private const string DriveColumn = "sprk_graphdriveid";
    private const string ItemColumn = "sprk_graphitemid";
    private const string FilePathColumn = "sprk_filepath";
    private const string HasFileColumn = "sprk_hasfile";
    private const string FileNameColumn = "sprk_filename";
    private const string CreatedByColumn = "createdby";

    private readonly RecordContainerResolver _resolver;
    private readonly IGenericEntityService _dataverse;
    private readonly SpeFileStore _spe;
    private readonly ILogger<DocumentContainerRelocator> _logger;

    public DocumentContainerRelocator(
        RecordContainerResolver resolver,
        IGenericEntityService dataverse,
        SpeFileStore spe,
        ILogger<DocumentContainerRelocator> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (i) The pointer attach — the client's first file
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attach the file a client just uploaded to the document it just created. Verified, then stamped app-only.
    /// </summary>
    /// <remarks>
    /// <para>The route has already required WRITE on the row as the caller (<c>DocumentAuthorizationFilter</c>). Then, in
    /// order, each failure leaving the row untouched:</para>
    /// <list type="number">
    /// <item>the row carries no file yet — the same file again is idempotent; ANY other file is refused (re-pointing a
    /// file is the relocator's job, never a client's);</item>
    /// <item>the caller is the row's CREATOR — <c>createdby</c> when a person, else the person the BFF recorded in
    /// <c>sprk_createdbyperson</c>; compared by Entra object id, through the resolver's ONE definition
    /// (<see cref="RecordContainerResolver.ReadDocumentCreatorObjectIdAsync"/>) — so the round-23 item check (item
    /// creator = row creator) holds for every row this route touches;</item>
    /// <item>the file's drive is the container DERIVED for the row (<see cref="RecordContainerResolver.DeriveDocumentContainersAsync"/>)
    /// — the same answer the record-keyed upload route placed the bytes by;</item>
    /// <item>the item exists in that drive and the CALLER uploaded it (Graph <c>createdBy.user.id</c>).</item>
    /// </list>
    /// </remarks>
    public async Task<PointerAttachResult> AttachFileAsync(
        Guid documentId, string? callerObjectId, string? driveId, string? itemId, CancellationToken ct = default)
    {
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId)
            || driveId.Length > 512 || itemId.Length > 512)
        {
            return PointerAttachResult.Refused(PointerAttachOutcome.InvalidRequest,
                "A document id, the file's drive id and its item id are required.");
        }

        if (!Guid.TryParse(callerObjectId, out var caller) || caller == Guid.Empty)
        {
            return PointerAttachResult.Refused(PointerAttachOutcome.NotTheCreator,
                "Your identity could not be determined, so the file cannot be attached.");
        }

        var drive = driveId.Trim();
        var item = itemId.Trim();

        var row = await _dataverse.RetrieveAsync(
            DocumentEntity, documentId, [DriveColumn, ItemColumn, CreatedByColumn], ct).ConfigureAwait(false);
        if (row is null)
        {
            return PointerAttachResult.Refused(PointerAttachOutcome.InvalidRequest, "The document could not be read.");
        }

        var currentDrive = row.GetAttributeValue<string>(DriveColumn);
        var currentItem = row.GetAttributeValue<string>(ItemColumn);
        if (!string.IsNullOrWhiteSpace(currentDrive) || !string.IsNullOrWhiteSpace(currentItem))
        {
            if (RecordContainerResolver.IsSameContainerId(currentDrive, drive)
                && string.Equals(currentItem?.Trim(), item, StringComparison.Ordinal))
            {
                return PointerAttachResult.Attached(drive, item, alreadyAttached: true);
            }

            _logger.LogWarning(
                "[DOCUMENT-ATTACH] REFUSED: document {DocumentId} already carries a file ({Drive}/{Item}); a client may "
                + "attach only its FIRST file.", documentId, currentDrive, currentItem);
            return PointerAttachResult.Refused(PointerAttachOutcome.AlreadyAttached,
                "This document already has a file. Its file can only be changed by uploading a new version.");
        }

        if (await _resolver.ReadDocumentCreatorObjectIdAsync(documentId, row, ct).ConfigureAwait(false) != caller)
        {
            _logger.LogWarning(
                "[DOCUMENT-ATTACH] REFUSED: the caller {Caller} did not create document {DocumentId}.", caller, documentId);
            return PointerAttachResult.Refused(PointerAttachOutcome.NotTheCreator,
                "Only the person who created this document can attach its file.");
        }

        var derivation = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (!derivation.Decided)
        {
            _logger.LogWarning(
                "[DOCUMENT-ATTACH] REFUSED: the container of document {DocumentId} cannot be derived ({Reason}).",
                documentId, derivation.Reason);
            return PointerAttachResult.Refused(PointerAttachOutcome.ContainerUndetermined,
                "Where this document's file belongs could not be determined, so the file was not attached.");
        }

        if (!derivation.Allows(drive))
        {
            _logger.LogWarning(
                "[DOCUMENT-ATTACH] REFUSED: document {DocumentId}'s file is in {Drive}, not in its derived container "
                + "({Reason}).", documentId, drive, derivation.Reason);
            return PointerAttachResult.Refused(PointerAttachOutcome.WrongContainer,
                "The file is not stored where this document's files belong.");
        }

        var facts = await _spe.GetItemCreatorAsync(drive, item, ct).ConfigureAwait(false);
        if (facts is null || !Guid.TryParse(facts.UserObjectId, out var uploader) || uploader != caller)
        {
            _logger.LogWarning(
                "[DOCUMENT-ATTACH] REFUSED: item {Item} of {Drive} is absent or was not uploaded by the caller {Caller} "
                + "(document {DocumentId}).", item, drive, caller, documentId);
            return PointerAttachResult.Refused(PointerAttachOutcome.NotTheUploader,
                "The file was not found, or it was not uploaded by you.");
        }

        await WritePointerAsync(documentId, drive, item, facts.WebUrl, fileName: null, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[DOCUMENT-ATTACH] document {DocumentId} -> {Drive}/{Item} (uploaded by its creator, in its derived container).",
            documentId, drive, item);
        return PointerAttachResult.Attached(drive, item, alreadyAttached: false);
    }

    /// <summary>
    /// THE pointer-attach path: the only write of a document's pointer by this type. App-only — the identity the pointer
    /// columns' field-level security admits. <c>sprk_hasfile</c> is set with the pointer, so "has a file" and "points at
    /// a file" never disagree.
    /// </summary>
    private Task WritePointerAsync(
        Guid documentId, string drive, string item, string? webUrl, string? fileName, CancellationToken ct)
    {
        var fields = new Dictionary<string, object>
        {
            [DriveColumn] = drive,
            [ItemColumn] = item,
            [HasFileColumn] = true,
        };
        if (!string.IsNullOrWhiteSpace(webUrl))
        {
            fields[FilePathColumn] = webUrl;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            fields[FileNameColumn] = fileName;
        }

        return _dataverse.UpdateAsync(DocumentEntity, documentId, fields, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (ii) Relocation — the legacy migration and Make Secure
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Moves the document's file into its derived container when it is not already there.
    /// </summary>
    /// <param name="documentId">The document.</param>
    /// <param name="apply"><see langword="false"/> = report only: the outcome says what WOULD happen, and nothing is
    /// read beyond the checks or written.</param>
    /// <param name="purpose">
    /// <see cref="RelocationPurpose.LegacyMigration"/> moves only a file that is verifiably the row's own
    /// (<see cref="RecordContainerResolver.IsRelocationSourceVerifiedAsync"/>): a forged pointer is reported, never copied
    /// into the document's container. <see cref="RelocationPurpose.MakeSecure"/> — a record just made secure — moves the
    /// file the document shows into the record's OWN container whatever its uploader: that move only NARROWS who can
    /// reach the bytes (SPE permissions are additive-only, so leaving them in the shared container is the exposure),
    /// and it applies only when the derived container is that secure one.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="expectedTargetContainer">
    /// When the caller already knows where the files must go (Make Secure: the record's own container), the container it
    /// expects. It is never trusted on its own: it must BE the document's derived container (else the document is
    /// reported <see cref="RelocationState.Undecidable"/> and nothing moves) — the relocator never puts a file anywhere
    /// the strict rule would refuse.
    /// </param>
    public async Task<DocumentRelocationOutcome> RelocateIfMisplacedAsync(
        Guid documentId, bool apply, RelocationPurpose purpose = RelocationPurpose.LegacyMigration, CancellationToken ct = default,
        string? expectedTargetContainer = null)
    {
        var row = await _dataverse.RetrieveAsync(
            DocumentEntity, documentId, [DriveColumn, ItemColumn, FileNameColumn], ct).ConfigureAwait(false);
        var sourceDrive = row?.GetAttributeValue<string>(DriveColumn)?.Trim();
        var sourceItem = row?.GetAttributeValue<string>(ItemColumn)?.Trim();
        if (string.IsNullOrWhiteSpace(sourceDrive) || string.IsNullOrWhiteSpace(sourceItem))
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.NoFile, null, null, null, null, "the row carries no file");
        }

        var derivation = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (!derivation.Decided)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Undecidable, sourceDrive, sourceItem, null, null, derivation.Reason);
        }

        if (!string.IsNullOrWhiteSpace(expectedTargetContainer) && !derivation.Allows(expectedTargetContainer))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Undecidable, sourceDrive, sourceItem, null, null,
                $"the requested target {expectedTargetContainer.Trim()} is not the document's derived container ({derivation.Reason})");
        }

        var facts = await _spe.GetItemCreatorAsync(sourceDrive, sourceItem, ct).ConfigureAwait(false);
        if (facts is null)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.FileMissing, sourceDrive, sourceItem, derivation.PrimaryContainer, null,
                "the item is not in the drive the row names");
        }

        if (derivation.Allows(sourceDrive))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.InPlace, sourceDrive, sourceItem, sourceDrive, sourceItem, derivation.Reason);
        }

        var target = string.IsNullOrWhiteSpace(expectedTargetContainer)
            ? derivation.PrimaryContainer!
            : expectedTargetContainer.Trim();
        var verified = purpose == RelocationPurpose.MakeSecure && derivation.IsSecure
            || await _resolver.IsRelocationSourceVerifiedAsync(documentId, sourceDrive, sourceItem, ct).ConfigureAwait(false);
        if (!verified)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.SourceUnverified, sourceDrive, sourceItem, target, null,
                "the file is not verifiably this document's own (another person's upload, or a container of no business "
                + "unit) — an administrator must repair it; it is not copied");
        }

        if (facts.Size is > MaxRelocatableBytes)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"the file is {facts.Size} bytes, larger than a single-request copy ({MaxRelocatableBytes} bytes)");
        }

        if (!apply)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.WouldRelocate, sourceDrive, sourceItem, target, null, derivation.Reason);
        }

        return await RelocateAsync(documentId, sourceDrive, sourceItem, target, facts, row!, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The batch entry point for a caller that holds the document ids and the target container — task 150's Make Secure
    /// transition (round 26 item 3: the record's existing files move into its own container). Each document goes through
    /// <see cref="RelocateIfMisplacedAsync"/> with <paramref name="targetContainerId"/> as the expected target; a fault in
    /// one document is that document's <see cref="RelocationState.Failed"/>, never the batch's. The result counts every
    /// outcome and lists the INCOMPLETE ones, so a repeat call (which skips what is already in place) completes the run.
    /// </summary>
    public async Task<DocumentRelocationBatchResult> RelocateDocumentsAsync(
        IReadOnlyCollection<Guid> documentIds, string targetContainerId, RelocationPurpose purpose, bool apply,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetContainerId);

        var outcomes = new List<DocumentRelocationOutcome>(documentIds.Count);
        foreach (var documentId in documentIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                outcomes.Add(await RelocateIfMisplacedAsync(documentId, apply, purpose, ct, targetContainerId).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[DOCUMENT-RELOCATE] document {DocumentId} faulted; it is listed as incomplete.", documentId);
                outcomes.Add(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.Failed, null, null, targetContainerId, null, $"faulted ({ex.GetType().Name})"));
            }
        }

        return DocumentRelocationBatchResult.From(outcomes, purpose);
    }

    private async Task<DocumentRelocationOutcome> RelocateAsync(
        Guid documentId, string sourceDrive, string sourceItem, string targetDrive, SpeItemCreator sourceFacts, Entity row,
        CancellationToken ct)
    {
        var uploadPath = SpeUploadPath.SanitizeFileName(sourceFacts.Name ?? row.GetAttributeValue<string>(FileNameColumn));

        // 1. COPY through the BFF identity. Rename on a name collision: a flat container must never overwrite another
        //    document's file.
        FileHandleDto? copy;
        await using (var bytes = await _spe.DownloadFileAsync(sourceDrive, sourceItem, ct).ConfigureAwait(false))
        {
            if (bytes is null)
            {
                return DocumentRelocationOutcome.Of(
                    documentId, RelocationState.FileMissing, sourceDrive, sourceItem, targetDrive, null,
                    "the source file could not be downloaded");
            }

            copy = await _spe.UploadSmallAsync(targetDrive, uploadPath, bytes, ConflictBehavior.Rename, ct).ConfigureAwait(false);
        }

        if (copy is null || string.IsNullOrWhiteSpace(copy.Id))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, "the copy was not created");
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] copied: document {DocumentId} {SourceDrive}/{SourceItem} -> {TargetDrive}/{TargetItem}",
            documentId, sourceDrive, sourceItem, targetDrive, copy.Id);

        // 2. VERIFY the copy against the source before anything points at it or the source is touched.
        var copyFacts = await _spe.GetItemCreatorAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
        var mismatch = CopyMismatch(sourceFacts, copyFacts);
        if (mismatch is not null)
        {
            _logger.LogError(
                "[DOCUMENT-RELOCATE] verification FAILED for document {DocumentId} ({Mismatch}); removing the copy "
                + "{TargetDrive}/{TargetItem}; the row and the source are unchanged.",
                documentId, mismatch, targetDrive, copy.Id);
            await DeleteQuietlyAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                $"the copy did not verify ({mismatch})");
        }

        // 3. RE-POINT through the pointer-attach path.
        try
        {
            await WritePointerAsync(documentId, targetDrive, copy.Id, copyFacts!.WebUrl, fileName: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[DOCUMENT-RELOCATE] re-point FAILED for document {DocumentId}; removing the copy {TargetDrive}/{TargetItem}; "
                + "the row still points at {SourceDrive}/{SourceItem}.", documentId, targetDrive, copy.Id, sourceDrive, sourceItem);
            await DeleteQuietlyAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, "the row could not be re-pointed");
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] re-pointed: document {DocumentId} {SourceDrive}/{SourceItem} -> {TargetDrive}/{TargetItem}",
            documentId, sourceDrive, sourceItem, targetDrive, copy.Id);

        // 4. DELETE the source — only when no OTHER row still points at it (deleting it would break that row).
        if (await IsReferencedByAnotherDocumentAsync(documentId, sourceDrive, sourceItem, ct).ConfigureAwait(false) is not false)
        {
            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] source KEPT: {SourceDrive}/{SourceItem} (document {DocumentId}) is referenced by another "
                + "document, or that could not be read.", sourceDrive, sourceItem, documentId);
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.RelocatedSourceKept, sourceDrive, sourceItem, targetDrive, copy.Id,
                "another document still points at the source file, so it was kept");
        }

        if (!await DeleteQuietlyAsync(sourceDrive, sourceItem, ct).ConfigureAwait(false))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.RelocatedSourceKept, sourceDrive, sourceItem, targetDrive, copy.Id,
                "the source file could not be deleted (logged with its ids)");
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] source deleted: {SourceDrive}/{SourceItem} (document {DocumentId} now {TargetDrive}/{TargetItem})",
            sourceDrive, sourceItem, documentId, targetDrive, copy.Id);
        return DocumentRelocationOutcome.Of(
            documentId, RelocationState.Relocated, sourceDrive, sourceItem, targetDrive, copy.Id, "relocated");
    }

    /// <summary>Why a copy does not match its source, or <see langword="null"/> when it does.</summary>
    internal static string? CopyMismatch(SpeItemCreator source, SpeItemCreator? copy)
    {
        if (copy is null)
        {
            return "the copy could not be read back";
        }

        if (source.Size is not { } sourceSize || copy.Size != sourceSize)
        {
            return $"size {copy.Size?.ToString() ?? "unknown"} != {source.Size?.ToString() ?? "unknown"}";
        }

        if (!string.IsNullOrWhiteSpace(source.QuickXorHash) && !string.IsNullOrWhiteSpace(copy.QuickXorHash)
            && !string.Equals(source.QuickXorHash, copy.QuickXorHash, StringComparison.Ordinal))
        {
            return "quickXorHash differs";
        }

        return null;
    }

    /// <summary>
    /// Does a document OTHER than <paramref name="documentId"/> point at this file? App-only (a server invariant: a row
    /// the operator cannot see still depends on it). <see langword="null"/> = could not be read (the source is kept).
    /// </summary>
    private async Task<bool?> IsReferencedByAnotherDocumentAsync(
        Guid documentId, string drive, string item, CancellationToken ct)
    {
        var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(DocumentEntity)
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(DriveColumn),
            TopCount = 5,
            Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression(Microsoft.Xrm.Sdk.Query.LogicalOperator.And)
            {
                Conditions =
                {
                    new Microsoft.Xrm.Sdk.Query.ConditionExpression(ItemColumn, Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, item),
                    new Microsoft.Xrm.Sdk.Query.ConditionExpression("sprk_documentid", Microsoft.Xrm.Sdk.Query.ConditionOperator.NotEqual, documentId),
                },
            },
        };

        try
        {
            var others = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            return others?.Entities?.Any(e => RecordContainerResolver.IsSameContainerId(e.GetAttributeValue<string>(DriveColumn), drive));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] Whether another document points at {Drive}/{Item} could not be read.", drive, item);
            return null;
        }
    }

    private async Task<bool> DeleteQuietlyAsync(string drive, string item, CancellationToken ct)
    {
        try
        {
            return await _spe.DeleteFileAsync(drive, item, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[DOCUMENT-RELOCATE] could not delete {Drive}/{Item} — remove it manually.", drive, item);
            return false;
        }
    }
}

/// <summary>Why a relocation is being asked for (see <see cref="DocumentContainerRelocator.RelocateIfMisplacedAsync"/>).</summary>
public enum RelocationPurpose
{
    /// <summary>Task 166's legacy migration: only a verifiably-own file is moved.</summary>
    LegacyMigration,

    /// <summary>Task 150's Make Secure transition: the file the document shows moves into the record's own container.</summary>
    MakeSecure,
}

/// <summary>What a relocation found or did.</summary>
public enum RelocationState
{
    /// <summary>The row carries no file.</summary>
    NoFile,

    /// <summary>The file is already in its derived container.</summary>
    InPlace,

    /// <summary>Report-only: the file is misplaced and WOULD be moved.</summary>
    WouldRelocate,

    /// <summary>Moved: copied, verified, re-pointed, source deleted.</summary>
    Relocated,

    /// <summary>Moved and re-pointed; the source was kept (another row points at it, or its delete failed).</summary>
    RelocatedSourceKept,

    /// <summary>The document's container cannot be derived — an administrator must repair its filing.</summary>
    Undecidable,

    /// <summary>The item the row names does not exist.</summary>
    FileMissing,

    /// <summary>The file is not verifiably the row's own; never copied.</summary>
    SourceUnverified,

    /// <summary>A copy, verification or re-point step failed; the row is unchanged.</summary>
    Failed,
}

/// <summary>One document's relocation result (the migration report's row).</summary>
public sealed record DocumentRelocationOutcome(
    Guid DocumentId,
    RelocationState State,
    string? SourceDrive,
    string? SourceItem,
    string? TargetDrive,
    string? TargetItem,
    string Detail)
{
    internal static DocumentRelocationOutcome Of(
        Guid documentId, RelocationState state, string? sourceDrive, string? sourceItem, string? targetDrive,
        string? targetItem, string detail)
        => new(documentId, state, sourceDrive, sourceItem, targetDrive, targetItem, detail);
}

/// <summary>
/// A batch's per-file outcomes, counts by state, and the INCOMPLETE ones (anything not settled: a planned or failed
/// move, an undecidable container, a missing or unverified file — and, for Make Secure, a source left behind in the
/// shared container).
/// </summary>
public sealed record DocumentRelocationBatchResult(
    IReadOnlyList<DocumentRelocationOutcome> Outcomes,
    IReadOnlyDictionary<RelocationState, int> Counts,
    IReadOnlyList<DocumentRelocationOutcome> Incomplete)
{
    /// <summary>Every file is settled.</summary>
    public bool Complete => Incomplete.Count == 0;

    internal static DocumentRelocationBatchResult From(IReadOnlyList<DocumentRelocationOutcome> outcomes, RelocationPurpose purpose)
    {
        var counts = outcomes.GroupBy(o => o.State).ToDictionary(g => g.Key, g => g.Count());
        var incomplete = outcomes.Where(o => !IsSettled(o.State, purpose)).ToList();
        return new DocumentRelocationBatchResult(outcomes, counts, incomplete);
    }

    /// <summary>
    /// Settled: nothing to move (<see cref="RelocationState.NoFile"/>, <see cref="RelocationState.InPlace"/>) or moved
    /// (<see cref="RelocationState.Relocated"/>; and <see cref="RelocationState.RelocatedSourceKept"/> for the migration,
    /// whose kept source is another row's file — but NOT for Make Secure, where a source left in the shared container is
    /// the exposure the move exists to end).
    /// </summary>
    internal static bool IsSettled(RelocationState state, RelocationPurpose purpose) => state switch
    {
        RelocationState.NoFile or RelocationState.InPlace or RelocationState.Relocated => true,
        RelocationState.RelocatedSourceKept => purpose == RelocationPurpose.LegacyMigration,
        _ => false,
    };
}

/// <summary>Why an attach was refused, or that it succeeded.</summary>
public enum PointerAttachOutcome
{
    Attached,
    InvalidRequest,
    AlreadyAttached,
    NotTheCreator,
    ContainerUndetermined,
    WrongContainer,
    NotTheUploader,
}

/// <summary>The result of <see cref="DocumentContainerRelocator.AttachFileAsync"/>.</summary>
public sealed record PointerAttachResult(
    PointerAttachOutcome Outcome, string? DriveId, string? ItemId, bool AlreadyAttached, string? Detail)
{
    internal static PointerAttachResult Attached(string drive, string item, bool alreadyAttached)
        => new(PointerAttachOutcome.Attached, drive, item, alreadyAttached, null);

    internal static PointerAttachResult Refused(PointerAttachOutcome outcome, string detail)
        => new(outcome, null, null, false, detail);
}
