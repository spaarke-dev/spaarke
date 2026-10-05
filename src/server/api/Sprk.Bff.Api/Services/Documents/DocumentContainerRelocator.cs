using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// The ONE server-side owner of WHERE a <c>sprk_document</c>'s file lives (unified-access-control-r2 task 166 f1; owner
/// round 21 item 1 (i)–(ii), round 26 item 3, round 37): the only writer of a document's SharePoint Embedded pointer
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
/// the SAME pointer-attach path, then settle what the move leaves behind (below). Two callers, one mechanism (round 26
/// item 3): task 166's legacy migration (<see cref="DocumentContainerMigrationJob"/>) and the Make Secure transition
/// (task 150's provisioning lane).</item>
/// </list>
/// <para><b>What a move leaves behind, and the relocation ledger</b> (owner round 37, task 166 f1-v1). Re-pointing the row
/// is one atomic Dataverse update that ALSO re-keys the row's own columns holding the old drive / item
/// (<see cref="ReKeyedOwnColumns"/>) and writes the row's relocation ledger (<see cref="RelocationLedgerColumn"/>): one
/// entry per old item, naming what is still owed for it —</para>
/// <list type="bullet">
/// <item><b>re-key</b>: the other rows that hold the old item id for THIS document — a child attachment's
/// <c>sprk_parentgraphitemid</c>, and the <c>sprk_communicationattachment</c> row linked to this document;</item>
/// <item><b>source</b>: the old item is deleted only when no row references it any more. A referencing
/// <c>sprk_document</c> whose own derived container is the one this document now lives in (inside the secure record's
/// subtree, for Make Secure) is moved along — with its OWN copy, because <c>sprk_graphitemid_uk</c> is a unique key on
/// the item id and two rows can never share one. A row OUTSIDE keeps the source, which is then that record's file: the
/// transition is complete and the report lists it (<see cref="DocumentRelocationOutcome.KeptForOtherRecords"/>); the
/// entry stays so a later pass deletes the source once nothing references it;</item>
/// <item><b>index</b>: the new item indexed and the old item's chunks removed, through ONE <c>Services/Ai/PublicContracts</c>
/// facade (<see cref="IRelocatedFileIndexing"/>, ADR-013).</item>
/// </list>
/// <para>Whatever cannot be finished is reported pending (<see cref="RelocationState.RelocationPending"/>, incomplete) and
/// stays in the ledger, so a REPEAT call — the next Make Secure call, or the migration's next pass — recognises the row
/// as already re-pointed and settles it. A source is deleted on a repeat call only when it is byte-identical to the
/// document's current file (size and <c>quickXorHash</c>), so a ledger entry can never be used to delete an unrelated file.
/// The ledger column is BFF-written only (field-level security, <c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>).</para>
/// <para><b>Every step is logged with before / after ids</b> (<c>[DOCUMENT-RELOCATE]</c>).</para>
/// <para><b>Placement</b> (CLAUDE.md §10; <c>.claude/constraints/bff-extensions.md</c>): in the BFF — it composes the
/// BFF's own container decisions (<see cref="RecordContainerResolver"/>), its app-only SPE facade and its Dataverse
/// application identity, and the pointer columns are written by that identity alone. Scoped, registered unconditionally
/// in <c>DocumentsModule</c>: the route that calls it is mapped unconditionally (§F.1).</para>
/// </remarks>
public sealed class DocumentContainerRelocator
{
    /// <summary>Graph's simple-upload boundary for SPE (250 MB). A larger file is reported, never half-copied.</summary>
    internal const long MaxRelocatableBytes = 250L * 1024 * 1024;

    /// <summary>
    /// <c>sprk_document.sprk_relocationpending</c> — the row's relocation ledger (JSON; owner round 37, task 166 f1-v1).
    /// Created by <c>scripts/Set-DocumentRelocationSchema.ps1</c>; until it exists every relocation fails closed (the row
    /// read names it), so nothing is moved without a place to record what the move still owes.
    /// </summary>
    public const string RelocationLedgerColumn = "sprk_relocationpending";

    /// <summary>The most old items a row's ledger holds; a row that would need more is not moved (reported failed).</summary>
    internal const int MaxLedgerEntries = 8;

    /// <summary>The most referencing rows one source check reads; more is undecidable (pending), never "none".</summary>
    internal const int MaxReferencesPerSource = 50;

    private const string DocumentEntity = "sprk_document";
    private const string DocumentIdColumn = "sprk_documentid";
    private const string DriveColumn = "sprk_graphdriveid";
    private const string ItemColumn = "sprk_graphitemid";
    private const string FilePathColumn = "sprk_filepath";
    private const string HasFileColumn = "sprk_hasfile";
    private const string FileNameColumn = "sprk_filename";
    private const string CreatedByColumn = "createdby";
    private const string SearchIndexNameColumn = "sprk_searchindexname";
    private const string ParentDocumentColumn = "sprk_parentdocument";
    private const string ParentItemColumn = "sprk_parentgraphitemid";
    private const string AttachmentEntity = "sprk_communicationattachment";
    private const string AttachmentDocumentColumn = "sprk_document";

    /// <summary>
    /// The row's OWN legacy columns that hold its file's drive or item (live <c>sprk_document</c> metadata, spaarkedev1
    /// 2026-10-05): re-keyed in the SAME update as the pointer. <c>sprk_driveitemid</c> / <c>spk_fileviewerid</c> hold the
    /// item id, <c>sprk_containerid</c> the drive id (legacy rows; the design keeps it null), <c>sprk_parentfolderid</c>
    /// the folder the item sat in, <c>sprk_etag</c> the item's eTag.
    /// </summary>
    internal static readonly string[] ReKeyedOwnColumns =
        ["sprk_driveitemid", "spk_fileviewerid", "sprk_containerid", "sprk_parentfolderid", "sprk_etag"];

    private static readonly JsonSerializerOptions LedgerJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>How long one relocation of a document may hold its processing lock (a crashed holder's lock expires).</summary>
    internal static readonly TimeSpan RelocationLockDuration = TimeSpan.FromMinutes(10);

    private readonly RecordContainerResolver _resolver;
    private readonly IGenericEntityService _dataverse;
    private readonly SpeFileStore _spe;
    private readonly IRelocatedFileIndexing _indexing;
    private readonly IIdempotencyService _locks;
    private readonly TimeProvider _time;
    private readonly ILogger<DocumentContainerRelocator> _logger;

    public DocumentContainerRelocator(
        RecordContainerResolver resolver,
        IGenericEntityService dataverse,
        SpeFileStore spe,
        IRelocatedFileIndexing indexing,
        IIdempotencyService locks,
        ILogger<DocumentContainerRelocator> logger,
        TimeProvider? timeProvider = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The processing-lock key of one document's relocation.</summary>
    internal static string RelocationLockKey(Guid documentId) => $"document-relocate-{documentId:N}";

    private async Task<bool> TryLockAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            return await _locks.TryAcquireProcessingLockAsync(RelocationLockKey(documentId), RelocationLockDuration, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Unknown whether another relocation runs: do not run (fail closed); the repeat call retries.
            _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the relocation lock of document {DocumentId} could not be taken.", documentId);
            return false;
        }
    }

    private async Task UnlockAsync(Guid documentId)
    {
        try
        {
            await _locks.ReleaseProcessingLockAsync(RelocationLockKey(documentId), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] the relocation lock of document {DocumentId} could not be released; it expires in {Duration}.",
                documentId, RelocationLockDuration);
        }
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

        await WritePointerAsync(documentId, drive, item, facts.WebUrl, alsoWrite: null, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[DOCUMENT-ATTACH] document {DocumentId} -> {Drive}/{Item} (uploaded by its creator, in its derived container).",
            documentId, drive, item);
        return PointerAttachResult.Attached(drive, item, alreadyAttached: false);
    }

    /// <summary>
    /// THE pointer-attach path: the only write of a document's pointer by this type. App-only — the identity the pointer
    /// columns' field-level security admits. <c>sprk_hasfile</c> is set with the pointer, so "has a file" and "points at
    /// a file" never disagree. <paramref name="alsoWrite"/> carries what a RELOCATION writes in the same atomic update:
    /// the re-keyed own columns and the relocation ledger (<see cref="DBNull.Value"/> clears a column).
    /// </summary>
    private Task WritePointerAsync(
        Guid documentId, string drive, string item, string? webUrl, IReadOnlyDictionary<string, object>? alsoWrite,
        CancellationToken ct)
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

        foreach (var (column, value) in alsoWrite ?? new Dictionary<string, object>())
        {
            fields[column] = value;
        }

        return _dataverse.UpdateAsync(DocumentEntity, documentId, fields, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (ii) Relocation — the legacy migration and Make Secure
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Moves the document's file into its derived container when it is not already there, and settles what an earlier
    /// move of this row still owes (its relocation ledger).
    /// </summary>
    /// <param name="documentId">The document.</param>
    /// <param name="apply"><see langword="false"/> = report only: the outcome says what WOULD happen, and nothing is
    /// read beyond the checks or written (a ledger entry still owed is reported, not settled).</param>
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
        if (!apply)
        {
            return await RelocateCoreAsync(documentId, apply, purpose, expectedTargetContainer, ct).ConfigureAwait(false);
        }

        // ONE writer per document at a time: two concurrent relocations (a double-clicked Make Secure, or Make Secure while
        // the migration pass reaches the same row) would each copy and re-point, and the loser's copy and ledger entry
        // would be lost. The existing ADR-004 processing lock; a held lock is reported (incomplete), never waited on.
        if (!await TryLockAsync(documentId, ct).ConfigureAwait(false))
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, null, null, expectedTargetContainer, null,
                "another relocation of this document is running; a repeat call settles it");
        }

        try
        {
            return await RelocateCoreAsync(documentId, apply, purpose, expectedTargetContainer, ct).ConfigureAwait(false);
        }
        finally
        {
            await UnlockAsync(documentId).ConfigureAwait(false);
        }
    }

    private async Task<DocumentRelocationOutcome> RelocateCoreAsync(
        Guid documentId, bool apply, RelocationPurpose purpose, string? expectedTargetContainer, CancellationToken ct)
    {
        var row = await ReadRowAsync(documentId, ct).ConfigureAwait(false);
        var sourceDrive = row?.GetAttributeValue<string>(DriveColumn)?.Trim();
        var sourceItem = row?.GetAttributeValue<string>(ItemColumn)?.Trim();
        if (string.IsNullOrWhiteSpace(sourceDrive) || string.IsNullOrWhiteSpace(sourceItem))
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.NoFile, null, null, null, null, "the row carries no file");
        }

        // An earlier move of this row may still owe something (round 37 / F2): settle it BEFORE anything else, so a
        // repeat call completes it whatever the file's placement is now.
        var ledger = RelocationLedger.Parse(row!.GetAttributeValue<string>(RelocationLedgerColumn));
        var prior = await SettleOrReportAsync(documentId, row, sourceDrive, sourceItem, ledger, purpose, apply, justCopiedFrom: null, ct)
            .ConfigureAwait(false);

        var derivation = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (!derivation.Decided)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Undecidable, sourceDrive, sourceItem, null, null, derivation.Reason).With(prior);
        }

        if (!string.IsNullOrWhiteSpace(expectedTargetContainer) && !derivation.Allows(expectedTargetContainer))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Undecidable, sourceDrive, sourceItem, null, null,
                $"the requested target {expectedTargetContainer.Trim()} is not the document's derived container ({derivation.Reason})")
                .With(prior);
        }

        var facts = await _spe.GetItemCreatorAsync(sourceDrive, sourceItem, ct).ConfigureAwait(false);
        if (facts is null)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.FileMissing, sourceDrive, sourceItem, derivation.PrimaryContainer, null,
                "the item is not in the drive the row names").With(prior);
        }

        if (derivation.Allows(sourceDrive))
        {
            // Placed. A ledger entry still owed makes this a pending relocation (the row WAS re-pointed); otherwise the
            // file is simply in place (any kept-for-other-records sources are reported, and are settled).
            return prior.Pending.Count > 0
                ? DocumentRelocationOutcome.Of(
                        documentId, RelocationState.RelocationPending, prior.PendingSourceDrive, prior.PendingSourceItem,
                        sourceDrive, sourceItem, "an earlier move of this document still owes: " + string.Join("; ", prior.Pending))
                    .With(prior)
                : DocumentRelocationOutcome.Of(
                    documentId, RelocationState.InPlace, sourceDrive, sourceItem, sourceDrive, sourceItem, derivation.Reason)
                    .With(prior);
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
                + "unit) — an administrator must repair it; it is not copied").With(prior);
        }

        if (facts.Size is > MaxRelocatableBytes)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"the file is {facts.Size} bytes, larger than a single-request copy ({MaxRelocatableBytes} bytes)").With(prior);
        }

        if (!apply)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.WouldRelocate, sourceDrive, sourceItem, target, null, derivation.Reason).With(prior);
        }

        if (prior.LedgerUnreadable)
        {
            // Moving would overwrite a ledger nobody can read — and with it whatever an earlier move still owes.
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"its {RelocationLedgerColumn} is unreadable; an administrator must repair it before the file is moved").With(prior);
        }

        var moved = await MoveAsync(documentId, row, sourceDrive, sourceItem, target, facts, prior.Ledger,
            LedgerSourceState.Pending, ct).ConfigureAwait(false);
        if (moved.Failure is not null)
        {
            return moved.Failure.With(prior);
        }

        // Settle what the move owes now — the copy was verified against the source moments ago in this process, so the
        // source may be deleted without the repeat call's byte-identity check.
        var rowAfter = await ReadRowAsync(documentId, ct).ConfigureAwait(false) ?? row;
        var settled = await SettleOrReportAsync(documentId, rowAfter, target, moved.CopyId!,
            RelocationLedger.Parse(rowAfter.GetAttributeValue<string>(RelocationLedgerColumn)), purpose, apply: true,
            justCopiedFrom: (sourceDrive, sourceItem), ct).ConfigureAwait(false);

        var state = settled.Pending.Count > 0
            ? RelocationState.RelocationPending
            : settled.Kept.Any(k => SameItem(k.SourceDrive, k.SourceItem, sourceDrive, sourceItem))
                ? RelocationState.RelocatedSourceKeptForOtherRecords
                : RelocationState.Relocated;
        var detail = state switch
        {
            RelocationState.RelocationPending => "re-pointed; still owed: " + string.Join("; ", settled.Pending),
            RelocationState.RelocatedSourceKeptForOtherRecords =>
                "relocated; the source is kept because rows outside this document's container still use it (that record's file)",
            _ => "relocated",
        };
        var outcome = DocumentRelocationOutcome.Of(documentId, state, sourceDrive, sourceItem, target, moved.CopyId, detail).With(settled);
        return outcome with { MovedAlong = prior.MovedAlong.Concat(outcome.MovedAlong).ToList() };
    }

    /// <summary>
    /// The batch entry point for a caller that holds the document ids and the target container — task 150's Make Secure
    /// transition (round 26 item 3: the record's existing files move into its own container). Each document goes through
    /// <see cref="RelocateIfMisplacedAsync"/> with <paramref name="targetContainerId"/> as the expected target; a fault in
    /// one document is that document's <see cref="RelocationState.Failed"/>, never the batch's. The result counts every
    /// outcome, lists the INCOMPLETE ones and every source kept for another record; a repeat call settles what is owed.
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

        return DocumentRelocationBatchResult.From(outcomes);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // The move: copy → verify → re-point (with the own-column re-key and the ledger entry, one update)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<MoveResult> MoveAsync(
        Guid documentId, Entity row, string sourceDrive, string sourceItem, string targetDrive, SpeItemCreator sourceFacts,
        RelocationLedger ledger, LedgerSourceState sourceState, CancellationToken ct)
    {
        if (ledger.Entries.Count >= MaxLedgerEntries)
        {
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                $"the row already owes {ledger.Entries.Count} earlier moves; settle them before moving it again"));
        }

        // Read the row's own reference columns BEFORE any byte moves: if they cannot be read, nothing has happened yet.
        var ownReferences = await ReadOwnReferenceColumnsAsync(documentId, ct).ConfigureAwait(false);
        var uploadPath = SpeUploadPath.SanitizeFileName(sourceFacts.Name ?? row.GetAttributeValue<string>(FileNameColumn));

        // 1. COPY through the BFF identity. Rename on a name collision: a flat container must never overwrite another
        //    document's file.
        FileHandleDto? copy;
        await using (var bytes = await _spe.DownloadFileAsync(sourceDrive, sourceItem, ct).ConfigureAwait(false))
        {
            if (bytes is null)
            {
                return MoveResult.Failed(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.FileMissing, sourceDrive, sourceItem, targetDrive, null,
                    "the source file could not be downloaded"));
            }

            copy = await _spe.UploadSmallAsync(targetDrive, uploadPath, bytes, ConflictBehavior.Rename, ct).ConfigureAwait(false);
        }

        if (copy is null || string.IsNullOrWhiteSpace(copy.Id))
        {
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, "the copy was not created"));
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
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                $"the copy did not verify ({mismatch})"));
        }

        // 3. RE-POINT through the pointer-attach path — with the row's own re-keyed columns and the ledger entry for the
        //    old item, in ONE update, so no crash can leave a re-pointed row that forgot what it still owes.
        var alsoWrite = ReKeyOwnColumns(ownReferences, sourceDrive, sourceItem, targetDrive, copy);
        var entry = new RelocationLedgerEntry
        {
            SourceDrive = sourceDrive,
            SourceItem = sourceItem,
            Source = sourceState,
            RekeyPending = true,
            Indexed = LedgerIndexScope.None,
            At = _time.GetUtcNow(),
        };
        alsoWrite[RelocationLedgerColumn] = ledger.With(entry).Serialize();
        try
        {
            await WritePointerAsync(documentId, targetDrive, copy.Id, copyFacts!.WebUrl, alsoWrite, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[DOCUMENT-RELOCATE] re-point FAILED for document {DocumentId}; removing the copy {TargetDrive}/{TargetItem}; "
                + "the row still points at {SourceDrive}/{SourceItem}.", documentId, targetDrive, copy.Id, sourceDrive, sourceItem);
            await DeleteQuietlyAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, "the row could not be re-pointed"));
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] re-pointed: document {DocumentId} {SourceDrive}/{SourceItem} -> {TargetDrive}/{TargetItem}; "
            + "re-keyed own columns [{Columns}]; ledger entry for the old item recorded.",
            documentId, sourceDrive, sourceItem, targetDrive, copy.Id,
            string.Join(", ", alsoWrite.Keys.Where(k => k != RelocationLedgerColumn)));
        return new MoveResult(copy.Id, copyFacts, null);
    }

    private sealed record MoveResult(string? CopyId, SpeItemCreator? CopyFacts, DocumentRelocationOutcome? Failure)
    {
        public static MoveResult Failed(DocumentRelocationOutcome failure) => new(null, null, failure);
    }

    /// <summary>
    /// The row's own columns that held the OLD drive / item, re-keyed to the copy (round 37 item 1): an item id becomes
    /// the copy's, the drive id the target's; the folder and eTag become the copy's (cleared when Graph returned none).
    /// Columns that held something else are left alone.
    /// </summary>
    internal static Dictionary<string, object> ReKeyOwnColumns(
        IReadOnlyDictionary<string, string> own, string sourceDrive, string sourceItem, string targetDrive, FileHandleDto copy)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (column, value) in own)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (column)
            {
                case "sprk_driveitemid" or "spk_fileviewerid" when string.Equals(value.Trim(), sourceItem, StringComparison.Ordinal):
                    fields[column] = copy.Id;
                    break;
                case "sprk_containerid" when RecordContainerResolver.IsSameContainerId(value, sourceDrive):
                    fields[column] = targetDrive;
                    break;
                case "sprk_parentfolderid":
                    fields[column] = string.IsNullOrWhiteSpace(copy.ParentId) ? DBNull.Value : copy.ParentId;
                    break;
                case "sprk_etag":
                    fields[column] = string.IsNullOrWhiteSpace(copy.ETag) ? DBNull.Value : copy.ETag;
                    break;
            }
        }

        return fields;
    }

    /// <summary>
    /// The row's <see cref="ReKeyedOwnColumns"/> values. Read together; when the environment lacks one of these legacy
    /// columns ("doesn't contain attribute"), each is read on its own and an ABSENT column is simply not a reference. Any
    /// other fault propagates — the move has not started, so nothing is left half-done.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ReadOwnReferenceColumnsAsync(Guid documentId, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var row = await _dataverse.RetrieveAsync(DocumentEntity, documentId, ReKeyedOwnColumns, ct).ConfigureAwait(false);
            foreach (var column in ReKeyedOwnColumns)
            {
                if (row?.GetAttributeValue<string>(column) is { } value)
                {
                    values[column] = value;
                }
            }

            return values;
        }
        catch (Exception ex) when (IsMissingAttribute(ex))
        {
            foreach (var column in ReKeyedOwnColumns)
            {
                try
                {
                    var row = await _dataverse.RetrieveAsync(DocumentEntity, documentId, [column], ct).ConfigureAwait(false);
                    if (row?.GetAttributeValue<string>(column) is { } value)
                    {
                        values[column] = value;
                    }
                }
                catch (Exception single) when (IsMissingAttribute(single))
                {
                    // This environment does not have the column: it holds no reference.
                }
            }

            return values;
        }
    }

    private static bool IsMissingAttribute(Exception ex)
        => ex.Message.Contains("doesn't contain attribute", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("does not contain attribute", StringComparison.OrdinalIgnoreCase);

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Settling the ledger (round 37 items 1-2; F2)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Settles (apply) or reports (report-only) every entry of the row's ledger against its CURRENT file
    /// (<paramref name="currentDrive"/> / <paramref name="currentItem"/>), then writes the ledger back.
    /// </summary>
    private async Task<SettleSummary> SettleOrReportAsync(
        Guid documentId, Entity row, string currentDrive, string currentItem, RelocationLedger? ledger, RelocationPurpose purpose,
        bool apply, (string Drive, string Item)? justCopiedFrom, CancellationToken ct)
    {
        if (ledger is null)
        {
            // An unreadable ledger is never acted on (it could name anything); an administrator must repair it.
            return SettleSummary.Unreadable(
                $"ledger-unreadable: {RelocationLedgerColumn} of this document is not a relocation ledger; it is not acted on — "
                + "an administrator must clear or repair it");
        }

        if (ledger.Entries.Count == 0)
        {
            return SettleSummary.Empty;
        }

        var summary = new SettleSummary(ledger);
        if (!apply)
        {
            foreach (var entry in ledger.Entries)
            {
                summary.Report(entry, Owed(entry));
            }

            return summary;
        }

        string? stampedIndex = row.GetAttributeValue<string>(SearchIndexNameColumn);
        SpeItemCreator? currentFacts = null;
        var remaining = new List<RelocationLedgerEntry>();
        foreach (var original in ledger.Entries)
        {
            var entry = original;

            // (a) RE-KEY the other rows that hold the old item id for THIS document.
            if (entry.RekeyPending)
            {
                if (await ReKeyReferencesAsync(documentId, entry, currentDrive, currentItem, ct).ConfigureAwait(false))
                {
                    entry = entry with { RekeyPending = false };
                }
            }

            // (b) The SOURCE.
            var sourceDetail = (string?)null;
            if (entry.Source is LedgerSourceState.Pending or LedgerSourceState.KeptForOtherRecords)
            {
                var justCopied = justCopiedFrom is { } j && SameItem(j.Drive, j.Item, entry.SourceDrive, entry.SourceItem);
                currentFacts ??= await _spe.GetItemCreatorAsync(currentDrive, currentItem, ct).ConfigureAwait(false);
                var source = await SettleSourceAsync(documentId, entry, currentDrive, currentFacts, purpose, justCopied, summary, ct)
                    .ConfigureAwait(false);
                entry = entry with { Source = source.State };
                sourceDetail = source.Detail;
            }

            // (c) The INDEX: the new item indexed, the old item's chunks removed — all of them once the old item is gone.
            var required = entry.Source == LedgerSourceState.Removed ? LedgerIndexScope.All : LedgerIndexScope.Own;
            var indexDetail = (string?)null;
            if (entry.Indexed < required)
            {
                currentFacts ??= await _spe.GetItemCreatorAsync(currentDrive, currentItem, ct).ConfigureAwait(false);
                var indexed = await _indexing.ReindexRelocatedFileAsync(
                    new RelocatedFileIndexRequest(
                        documentId, currentDrive, currentItem, currentFacts?.Name ?? row.GetAttributeValue<string>(FileNameColumn),
                        currentFacts?.Size, entry.SourceItem, OldItemRemoved: required == LedgerIndexScope.All, stampedIndex),
                    ct).ConfigureAwait(false);
                if (indexed.Settled)
                {
                    entry = entry with { Indexed = required };
                }
                else
                {
                    indexDetail = indexed.Detail;
                }
            }

            var owed = Owed(entry, sourceDetail, indexDetail);
            summary.Report(entry, owed);
            if (!entry.IsComplete)
            {
                remaining.Add(entry);
            }
        }

        // Write the ledger back. A failed write leaves the stored ledger as it was; every step above is idempotent, so the
        // next call redoes only what it finds owed — but this call must not report the row settled.
        var updated = new RelocationLedger(remaining);
        if (!updated.SameAs(ledger))
        {
            try
            {
                await _dataverse.UpdateAsync(DocumentEntity, documentId, new Dictionary<string, object>
                {
                    [RelocationLedgerColumn] = remaining.Count == 0 ? DBNull.Value : updated.Serialize(),
                }, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "[DOCUMENT-RELOCATE] ledger of document {DocumentId}: {Before} -> {After} entr(ies) owed or kept.",
                    documentId, ledger.Entries.Count, remaining.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[DOCUMENT-RELOCATE] the ledger of document {DocumentId} could not be written back.", documentId);
                summary.AddPending("ledger-write: the settled state could not be recorded; the next call redoes it");
            }
        }

        summary.Ledger = updated;
        return summary;
    }

    /// <summary>What an entry still owes, as report lines (empty = settled).</summary>
    private static List<string> Owed(RelocationLedgerEntry entry, string? sourceDetail = null, string? indexDetail = null)
    {
        var owed = new List<string>();
        if (entry.RekeyPending)
        {
            owed.Add($"rekey-pending: rows holding the old item {entry.SourceItem} for this document were not all re-keyed");
        }

        if (entry.Source == LedgerSourceState.Pending)
        {
            owed.Add($"source-pending: {entry.SourceDrive}/{entry.SourceItem}" + (sourceDetail is null ? string.Empty : $" ({sourceDetail})"));
        }

        var required = entry.Source == LedgerSourceState.Removed ? LedgerIndexScope.All : LedgerIndexScope.Own;
        if (entry.Indexed < required)
        {
            owed.Add($"index-pending: the old item {entry.SourceItem}" + (indexDetail is null ? string.Empty : $" ({indexDetail})"));
        }

        return owed;
    }

    /// <summary>
    /// Children and communication attachments of THIS document that still name the old item → the current file.
    /// <see langword="true"/> when none remains.
    /// </summary>
    private async Task<bool> ReKeyReferencesAsync(
        Guid documentId, RelocationLedgerEntry entry, string currentDrive, string currentItem, CancellationToken ct)
    {
        try
        {
            // A child attachment document records its parent's item (sprk_parentgraphitemid; UploadFinalizationWorker).
            var children = await _dataverse.RetrieveMultipleAsync(new QueryExpression(DocumentEntity)
            {
                ColumnSet = new ColumnSet(ParentItemColumn),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(ParentDocumentColumn, ConditionOperator.Equal, documentId),
                        new ConditionExpression(ParentItemColumn, ConditionOperator.Equal, entry.SourceItem),
                    },
                },
            }, ct).ConfigureAwait(false);
            foreach (var child in (IEnumerable<Entity>?)children?.Entities ?? [])
            {
                await _dataverse.UpdateAsync(DocumentEntity, child.Id,
                    new Dictionary<string, object> { [ParentItemColumn] = currentItem }, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "[DOCUMENT-RELOCATE] re-keyed: child document {Child} {Column} {Old} -> {New} (parent {DocumentId}).",
                    child.Id, ParentItemColumn, entry.SourceItem, currentItem, documentId);
            }

            // The communication's attachment row mirrors its document's pointer (the .eml embed follows it app-only).
            var attachments = await _dataverse.RetrieveMultipleAsync(new QueryExpression(AttachmentEntity)
            {
                ColumnSet = new ColumnSet(DriveColumn, ItemColumn),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(AttachmentDocumentColumn, ConditionOperator.Equal, documentId),
                        new ConditionExpression(ItemColumn, ConditionOperator.Equal, entry.SourceItem),
                    },
                },
            }, ct).ConfigureAwait(false);
            foreach (var attachment in (IEnumerable<Entity>?)attachments?.Entities ?? [])
            {
                await _dataverse.UpdateAsync(AttachmentEntity, attachment.Id,
                    new Dictionary<string, object> { [DriveColumn] = currentDrive, [ItemColumn] = currentItem }, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "[DOCUMENT-RELOCATE] re-keyed: communication attachment {Attachment} {OldDrive}/{OldItem} -> {NewDrive}/{NewItem} "
                    + "(document {DocumentId}).", attachment.Id, entry.SourceDrive, entry.SourceItem, currentDrive, currentItem, documentId);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] the rows naming the old item {OldItem} of document {DocumentId} could not all be re-keyed; "
                + "a repeat call retries.", entry.SourceItem, documentId);
            return false;
        }
    }

    private sealed record SourceSettlement(LedgerSourceState State, string? Detail);

    /// <summary>
    /// The old item: gone → removed; referenced only by rows OUTSIDE → kept for those records (settled); referenced by
    /// rows that belong where this document's file now is → moved along (each with its own copy); unreferenced → deleted
    /// (on a repeat call only when byte-identical to the current file). Anything unknown keeps it pending.
    /// </summary>
    private async Task<SourceSettlement> SettleSourceAsync(
        Guid documentId, RelocationLedgerEntry entry, string currentDrive, SpeItemCreator? currentFacts, RelocationPurpose purpose,
        bool justCopied, SettleSummary summary, CancellationToken ct)
    {
        var sourceFacts = await _spe.GetItemCreatorAsync(entry.SourceDrive, entry.SourceItem, ct).ConfigureAwait(false);
        if (sourceFacts is null)
        {
            _logger.LogInformation(
                "[DOCUMENT-RELOCATE] source gone: {SourceDrive}/{SourceItem} (document {DocumentId}).",
                entry.SourceDrive, entry.SourceItem, documentId);
            return new SourceSettlement(LedgerSourceState.Removed, null);
        }

        var references = await ReadSourceReferencesAsync(documentId, entry, ct).ConfigureAwait(false);
        if (references is null)
        {
            return new SourceSettlement(LedgerSourceState.Pending, "whether another row still uses it could not be read");
        }

        var keptFor = new List<string>();
        var unknown = new List<string>();
        foreach (var other in references.Documents)
        {
            var derivation = await _resolver.DeriveDocumentContainersAsync(other, ct).ConfigureAwait(false);
            if (!derivation.Decided)
            {
                unknown.Add($"sprk_document {other} (its container cannot be derived: {derivation.Reason})");
                continue;
            }

            if (!derivation.Allows(currentDrive))
            {
                keptFor.Add($"sprk_document:{other}");
                continue;
            }

            // Inside: it belongs where this document's file now is — move it along, with its OWN copy (unique item key).
            var along = await MoveAlongAsync(other, entry, currentDrive, purpose, ct).ConfigureAwait(false);
            summary.MovedAlong.Add(along);
            switch (along.State)
            {
                case RelocationState.Relocated or RelocationState.RelocationPending or RelocationState.InPlace:
                    break; // it no longer names the source; its own ledger owes its own re-key / index
                case RelocationState.SourceUnverified:
                    keptFor.Add($"sprk_document:{other} (its file is not verifiably its own — reported for an administrator)");
                    break;
                default:
                    unknown.Add($"sprk_document {other} belongs with this file but could not be moved ({along.State}: {along.Detail})");
                    break;
            }
        }

        keptFor.AddRange(references.Attachments.Select(a => $"sprk_communicationattachment:{a}"));

        if (unknown.Count > 0)
        {
            return new SourceSettlement(LedgerSourceState.Pending, string.Join("; ", unknown));
        }

        if (keptFor.Count > 0)
        {
            summary.Kept.Add(new KeptSource(documentId, entry.SourceDrive, entry.SourceItem, keptFor));
            _logger.LogInformation(
                "[DOCUMENT-RELOCATE] source KEPT for other records: {SourceDrive}/{SourceItem} (document {DocumentId}) is used by {Others}.",
                entry.SourceDrive, entry.SourceItem, documentId, string.Join(", ", keptFor));
            return new SourceSettlement(LedgerSourceState.KeptForOtherRecords, null);
        }

        // No row references it. On a repeat call the ledger is the only witness, so the source must be provably a copy of
        // THIS document's file before it is deleted (a ledger entry can never be used to delete an unrelated file).
        if (!justCopied && !IsByteIdentical(sourceFacts, currentFacts))
        {
            return new SourceSettlement(LedgerSourceState.Pending,
                "it cannot be verified as a byte-identical copy of this document's file (size and quickXorHash), so it is not "
                + "deleted automatically — an administrator must check and remove it");
        }

        if (!await DeleteQuietlyAsync(entry.SourceDrive, entry.SourceItem, ct).ConfigureAwait(false))
        {
            return new SourceSettlement(LedgerSourceState.Pending, "its delete failed (logged); a repeat call retries");
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] source deleted: {SourceDrive}/{SourceItem} (document {DocumentId} now {CurrentDrive}).",
            entry.SourceDrive, entry.SourceItem, documentId, currentDrive);
        return new SourceSettlement(LedgerSourceState.Removed, null);
    }

    private sealed record SourceReferences(IReadOnlyList<Guid> Documents, IReadOnlyList<Guid> Attachments);

    /// <summary>
    /// The OTHER rows that still name the old item: <c>sprk_document</c> rows (in a degraded environment — the unique key
    /// <c>sprk_graphitemid_uk</c> forbids two while it is Active), and <c>sprk_communicationattachment</c> rows not linked
    /// to this document (a communication's own attachment record). App-only (a row the operator cannot see still depends
    /// on the file). <see langword="null"/> = could not be read, or more than <see cref="MaxReferencesPerSource"/>.
    /// </summary>
    private async Task<SourceReferences?> ReadSourceReferencesAsync(Guid documentId, RelocationLedgerEntry entry, CancellationToken ct)
    {
        try
        {
            var documents = await _dataverse.RetrieveMultipleAsync(new QueryExpression(DocumentEntity)
            {
                ColumnSet = new ColumnSet(DriveColumn),
                TopCount = MaxReferencesPerSource,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(ItemColumn, ConditionOperator.Equal, entry.SourceItem),
                        new ConditionExpression(DocumentIdColumn, ConditionOperator.NotEqual, documentId),
                    },
                },
            }, ct).ConfigureAwait(false);
            var attachments = await _dataverse.RetrieveMultipleAsync(new QueryExpression(AttachmentEntity)
            {
                ColumnSet = new ColumnSet(DriveColumn, AttachmentDocumentColumn),
                TopCount = MaxReferencesPerSource,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions = { new ConditionExpression(ItemColumn, ConditionOperator.Equal, entry.SourceItem) },
                },
            }, ct).ConfigureAwait(false);
            if (documents?.Entities is not { } docs || attachments?.Entities is not { } atts
                || docs.Count >= MaxReferencesPerSource || atts.Count >= MaxReferencesPerSource)
            {
                return null;
            }

            return new SourceReferences(
                docs.Where(d => d.Id != Guid.Empty && d.Id != documentId
                                && RecordContainerResolver.IsSameContainerId(d.GetAttributeValue<string>(DriveColumn), entry.SourceDrive))
                    .Select(d => d.Id).ToList(),
                atts.Where(a => a.GetAttributeValue<EntityReference>(AttachmentDocumentColumn)?.Id != documentId
                                && (string.IsNullOrWhiteSpace(a.GetAttributeValue<string>(DriveColumn))
                                    || RecordContainerResolver.IsSameContainerId(a.GetAttributeValue<string>(DriveColumn), entry.SourceDrive)))
                    .Select(a => a.Id).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] Whether another row uses {Drive}/{Item} could not be read.", entry.SourceDrive, entry.SourceItem);
            return null;
        }
    }

    /// <summary>
    /// Moves ANOTHER row that still names <paramref name="entry"/>'s source and belongs in <paramref name="targetDrive"/>
    /// (round 37 item 2), under the same legitimacy rules as its own relocation. Its ledger entry delegates the source to
    /// this document's entry (one owner of the delete); its own re-key and index are settled now.
    /// </summary>
    private async Task<DocumentRelocationOutcome> MoveAlongAsync(
        Guid other, RelocationLedgerEntry entry, string targetDrive, RelocationPurpose purpose, CancellationToken ct)
    {
        if (!await TryLockAsync(other, ct).ConfigureAwait(false))
        {
            return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive, null,
                "another relocation of it is running");
        }

        try
        {
            return await MoveAlongCoreAsync(other, entry, targetDrive, purpose, ct).ConfigureAwait(false);
        }
        finally
        {
            await UnlockAsync(other).ConfigureAwait(false);
        }
    }

    private async Task<DocumentRelocationOutcome> MoveAlongCoreAsync(
        Guid other, RelocationLedgerEntry entry, string targetDrive, RelocationPurpose purpose, CancellationToken ct)
    {
        try
        {
            var row = await ReadRowAsync(other, ct).ConfigureAwait(false);
            if (row is null || !SameItem(row.GetAttributeValue<string>(DriveColumn), row.GetAttributeValue<string>(ItemColumn),
                    entry.SourceDrive, entry.SourceItem))
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.InPlace, entry.SourceDrive, entry.SourceItem, null, null,
                    "it no longer names the source");
            }

            var ledger = RelocationLedger.Parse(row.GetAttributeValue<string>(RelocationLedgerColumn));
            if (ledger is null)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive, null,
                    $"its {RelocationLedgerColumn} is not a relocation ledger");
            }

            var derivation = await _resolver.DeriveDocumentContainersAsync(other, ct).ConfigureAwait(false);
            var facts = await _spe.GetItemCreatorAsync(entry.SourceDrive, entry.SourceItem, ct).ConfigureAwait(false);
            if (facts is null)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.FileMissing, entry.SourceDrive, entry.SourceItem, targetDrive,
                    null, "the source is not in its drive");
            }

            var verified = purpose == RelocationPurpose.MakeSecure && derivation.IsSecure
                || await _resolver.IsRelocationSourceVerifiedAsync(other, entry.SourceDrive, entry.SourceItem, ct).ConfigureAwait(false);
            if (!verified)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.SourceUnverified, entry.SourceDrive, entry.SourceItem,
                    targetDrive, null, "the file is not verifiably this row's own");
            }

            if (facts.Size is > MaxRelocatableBytes)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive,
                    null, "too large for a single-request copy");
            }

            var moved = await MoveAsync(other, row, entry.SourceDrive, entry.SourceItem, targetDrive, facts, ledger,
                LedgerSourceState.Delegated, ct).ConfigureAwait(false);
            if (moved.Failure is not null)
            {
                return moved.Failure;
            }

            var rowAfter = await ReadRowAsync(other, ct).ConfigureAwait(false) ?? row;
            var settled = await SettleOrReportAsync(other, rowAfter, targetDrive, moved.CopyId!,
                RelocationLedger.Parse(rowAfter.GetAttributeValue<string>(RelocationLedgerColumn)), purpose, apply: true,
                justCopiedFrom: null, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "[DOCUMENT-RELOCATE] moved along: document {Other} {SourceDrive}/{SourceItem} -> {TargetDrive}/{TargetItem} "
                + "(it named the same source).", other, entry.SourceDrive, entry.SourceItem, targetDrive, moved.CopyId);
            return DocumentRelocationOutcome.Of(other,
                    settled.Pending.Count > 0 ? RelocationState.RelocationPending : RelocationState.Relocated,
                    entry.SourceDrive, entry.SourceItem, targetDrive, moved.CopyId,
                    settled.Pending.Count > 0 ? "moved along; still owed: " + string.Join("; ", settled.Pending) : "moved along")
                .With(settled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[DOCUMENT-RELOCATE] moving document {Other} along faulted.", other);
            return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive, null,
                $"faulted ({ex.GetType().Name})");
        }
    }

    private async Task<Entity?> ReadRowAsync(Guid documentId, CancellationToken ct)
        => await _dataverse.RetrieveAsync(
            DocumentEntity, documentId, [DriveColumn, ItemColumn, FileNameColumn, SearchIndexNameColumn, RelocationLedgerColumn], ct)
            .ConfigureAwait(false);

    private static bool SameItem(string? driveA, string? itemA, string? driveB, string? itemB)
        => RecordContainerResolver.IsSameContainerId(driveA, driveB)
           && string.Equals(itemA?.Trim(), itemB?.Trim(), StringComparison.Ordinal);

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
    /// The repeat call's deletion test: the same size AND the same <c>quickXorHash</c>, both present. Stricter than
    /// <see cref="CopyMismatch"/> (which accepts a hash Graph has not computed yet), because here the ledger — not a copy
    /// made moments ago — is the only witness that the two are the same file.
    /// </summary>
    internal static bool IsByteIdentical(SpeItemCreator? a, SpeItemCreator? b)
        => a is not null && b is not null
           && a.Size is { } size && b.Size == size
           && !string.IsNullOrWhiteSpace(a.QuickXorHash) && !string.IsNullOrWhiteSpace(b.QuickXorHash)
           && string.Equals(a.QuickXorHash, b.QuickXorHash, StringComparison.Ordinal);

    private async Task<bool> DeleteQuietlyAsync(string drive, string item, CancellationToken ct)
    {
        try
        {
            return await _spe.DeleteFileAsync(drive, item, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[DOCUMENT-RELOCATE] could not delete {Drive}/{Item}.", drive, item);
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // The ledger
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The row's relocation ledger: the old items a move of THIS row still owes something for.</summary>
    internal sealed record RelocationLedger(IReadOnlyList<RelocationLedgerEntry> Entries)
    {
        public static RelocationLedger EmptyLedger { get; } = new([]);

        /// <summary>The ledger in <paramref name="json"/>; empty for no value; <see langword="null"/> when unreadable.</summary>
        public static RelocationLedger? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return EmptyLedger;
            }

            try
            {
                var stored = JsonSerializer.Deserialize<StoredLedger>(json, LedgerJson);
                if (stored is not { V: 1, Entries: { } entries }
                    || entries.Any(e => string.IsNullOrWhiteSpace(e.SourceDrive) || string.IsNullOrWhiteSpace(e.SourceItem)))
                {
                    return null;
                }

                return new RelocationLedger(entries);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public RelocationLedger With(RelocationLedgerEntry entry)
            => new(Entries.Where(e => !SameItem(e.SourceDrive, e.SourceItem, entry.SourceDrive, entry.SourceItem)).Append(entry).ToList());

        public string Serialize() => JsonSerializer.Serialize(new StoredLedger(1, Entries.ToList()), LedgerJson);

        public bool SameAs(RelocationLedger other) => Entries.SequenceEqual(other.Entries);

        private sealed record StoredLedger(int V, List<RelocationLedgerEntry>? Entries);
    }

    /// <summary>
    /// What a move of the row still owes for ONE old item. <see cref="IsComplete"/> entries are dropped; an entry whose
    /// source is kept for other records stays (settled — a later pass deletes the source once nothing references it).
    /// </summary>
    internal sealed record RelocationLedgerEntry
    {
        public required string SourceDrive { get; init; }
        public required string SourceItem { get; init; }
        public LedgerSourceState Source { get; init; }
        public bool RekeyPending { get; init; }
        public LedgerIndexScope Indexed { get; init; }
        public DateTimeOffset At { get; init; }

        [JsonIgnore]
        public bool IsComplete
            => !RekeyPending
               && Source is LedgerSourceState.Removed or LedgerSourceState.Delegated
               && Indexed >= (Source == LedgerSourceState.Removed ? LedgerIndexScope.All : LedgerIndexScope.Own);

        [JsonIgnore]
        public bool IsSettled
            => IsComplete || (!RekeyPending && Source == LedgerSourceState.KeptForOtherRecords && Indexed >= LedgerIndexScope.Own);
    }

    /// <summary>Where the old item stands.</summary>
    internal enum LedgerSourceState
    {
        /// <summary>Still to delete (or to decide).</summary>
        Pending,

        /// <summary>Rows of other records still use it: it is theirs; deleted by a later pass once nothing references it.</summary>
        KeptForOtherRecords,

        /// <summary>Deleted, or already gone.</summary>
        Removed,

        /// <summary>Moved along with another row whose ledger owns the source (one owner of the delete).</summary>
        Delegated,
    }

    /// <summary>How much of the old item's index has been re-keyed.</summary>
    internal enum LedgerIndexScope
    {
        /// <summary>Nothing yet.</summary>
        None,

        /// <summary>New item enqueued; the old item's chunks attributed to THIS document removed.</summary>
        Own,

        /// <summary>New item enqueued; ALL the old item's chunks removed (it is gone).</summary>
        All,
    }

    /// <summary>What a settle (or report) found.</summary>
    internal sealed class SettleSummary
    {
        public SettleSummary(RelocationLedger ledger) => Ledger = ledger;

        public static SettleSummary Empty => new(RelocationLedger.EmptyLedger);

        public static SettleSummary Unreadable(string reason)
        {
            var summary = new SettleSummary(RelocationLedger.EmptyLedger) { LedgerUnreadable = true };
            summary.AddPending(reason);
            return summary;
        }

        /// <summary>The row's ledger column holds something that is not a relocation ledger (never acted on).</summary>
        public bool LedgerUnreadable { get; private init; }

        public RelocationLedger Ledger { get; set; }
        public List<string> Pending { get; } = [];
        public List<KeptSource> Kept { get; } = [];
        public List<DocumentRelocationOutcome> MovedAlong { get; } = [];
        public string? PendingSourceDrive { get; private set; }
        public string? PendingSourceItem { get; private set; }

        public void AddPending(string reason) => Pending.Add(reason);

        public void Report(RelocationLedgerEntry entry, IReadOnlyList<string> owed)
        {
            if (owed.Count == 0)
            {
                if (entry.Source == LedgerSourceState.KeptForOtherRecords
                    && !Kept.Any(k => SameItem(k.SourceDrive, k.SourceItem, entry.SourceDrive, entry.SourceItem)))
                {
                    Kept.Add(new KeptSource(Guid.Empty, entry.SourceDrive, entry.SourceItem, ["(recorded by an earlier pass)"]));
                }

                return;
            }

            Pending.AddRange(owed);
            PendingSourceDrive ??= entry.SourceDrive;
            PendingSourceItem ??= entry.SourceItem;
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

    /// <summary>The file is already in its derived container and nothing is owed for it.</summary>
    InPlace,

    /// <summary>Report-only: the file is misplaced and WOULD be moved.</summary>
    WouldRelocate,

    /// <summary>Moved: copied, verified, re-pointed; the source deleted, the references re-keyed, the index re-keyed.</summary>
    Relocated,

    /// <summary>
    /// Moved and settled; the source is KEPT because rows of OTHER records (outside this document's container) still use
    /// it — it is their file now (owner round 37 item 2). Complete; listed in the report.
    /// </summary>
    RelocatedSourceKeptForOtherRecords,

    /// <summary>
    /// Re-pointed (now or by an earlier call) but something is still owed — the source's delete, a re-key or the index
    /// (<see cref="DocumentRelocationOutcome.Pending"/>). Incomplete; a repeat call settles it.
    /// </summary>
    RelocationPending,

    /// <summary>The document's container cannot be derived — an administrator must repair its filing.</summary>
    Undecidable,

    /// <summary>The item the row names does not exist.</summary>
    FileMissing,

    /// <summary>The file is not verifiably the row's own; never copied.</summary>
    SourceUnverified,

    /// <summary>A copy, verification or re-point step failed; the row is unchanged.</summary>
    Failed,
}

/// <summary>A source kept because rows of other records still use it (round 37 item 2: <c>SourceKeptForOtherRecords</c>).</summary>
/// <param name="DocumentId">The document that moved away from it.</param>
/// <param name="SourceDrive">The kept item's drive.</param>
/// <param name="SourceItem">The kept item.</param>
/// <param name="KeptFor">The rows that still use it (<c>sprk_document:{id}</c> / <c>sprk_communicationattachment:{id}</c>).</param>
public sealed record KeptSource(Guid DocumentId, string SourceDrive, string SourceItem, IReadOnlyList<string> KeptFor);

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
    /// <summary>What is still owed (<c>source-pending</c>, <c>index-pending</c>, <c>rekey-pending</c>, …); empty when settled.</summary>
    public IReadOnlyList<string> Pending { get; init; } = [];

    /// <summary>Sources kept because rows of other records still use them.</summary>
    public IReadOnlyList<KeptSource> KeptForOtherRecords { get; init; } = [];

    /// <summary>Rows that named the same source and belonged with this file, moved along (each with its own copy).</summary>
    public IReadOnlyList<DocumentRelocationOutcome> MovedAlong { get; init; } = [];

    /// <summary>The pointer the row ends with: the new one after a move, the current one otherwise.</summary>
    public (string? Drive, string? Item) FinalPointer
        => State is RelocationState.Relocated or RelocationState.RelocatedSourceKeptForOtherRecords or RelocationState.RelocationPending
            ? (TargetDrive, TargetItem)
            : (SourceDrive, SourceItem);

    internal static DocumentRelocationOutcome Of(
        Guid documentId, RelocationState state, string? sourceDrive, string? sourceItem, string? targetDrive,
        string? targetItem, string detail)
        => new(documentId, state, sourceDrive, sourceItem, targetDrive, targetItem, detail);

    /// <summary>
    /// This outcome with what a ledger settle found. An outcome that is otherwise settled but owes something becomes
    /// <see cref="RelocationState.RelocationPending"/> only through its caller; here the owed lines and lists are carried.
    /// </summary>
    internal DocumentRelocationOutcome With(DocumentContainerRelocator.SettleSummary summary)
        => this with
        {
            Pending = Pending.Concat(summary.Pending).ToList(),
            KeptForOtherRecords = KeptForOtherRecords
                .Concat(summary.Kept.Select(k => k.DocumentId == Guid.Empty ? k with { DocumentId = DocumentId } : k))
                .ToList(),
            MovedAlong = MovedAlong.Concat(summary.MovedAlong).ToList(),
        };
}

/// <summary>
/// A batch's per-file outcomes, counts by state, the INCOMPLETE ones (anything not settled: a planned, failed or pending
/// move, an undecidable container, a missing or unverified file) and every source kept for another record.
/// </summary>
public sealed record DocumentRelocationBatchResult(
    IReadOnlyList<DocumentRelocationOutcome> Outcomes,
    IReadOnlyDictionary<RelocationState, int> Counts,
    IReadOnlyList<DocumentRelocationOutcome> Incomplete,
    IReadOnlyList<KeptSource> SourceKeptForOtherRecords)
{
    /// <summary>Every file is settled.</summary>
    public bool Complete => Incomplete.Count == 0;

    internal static DocumentRelocationBatchResult From(IReadOnlyList<DocumentRelocationOutcome> outcomes)
    {
        var counts = outcomes.GroupBy(o => o.State).ToDictionary(g => g.Key, g => g.Count());
        // A row moved along with a document (it named the same file) that still owes a step is incomplete too: its own
        // ledger records the debt, and a repeat call with the incomplete ids settles it.
        var incomplete = outcomes.Where(o => !IsSettled(o))
            .Concat(outcomes.SelectMany(o => o.MovedAlong).Where(m => !IsSettled(m)))
            .ToList();
        var kept = outcomes.SelectMany(o => o.KeptForOtherRecords.Concat(o.MovedAlong.SelectMany(m => m.KeptForOtherRecords)))
            .ToList();
        return new DocumentRelocationBatchResult(outcomes, counts, incomplete, kept);
    }

    /// <summary>
    /// Settled: nothing to move or owe (<see cref="RelocationState.NoFile"/>, <see cref="RelocationState.InPlace"/>), or
    /// moved with nothing owed (<see cref="RelocationState.Relocated"/>, and
    /// <see cref="RelocationState.RelocatedSourceKeptForOtherRecords"/> — the kept source is another record's file, owner
    /// round 37 item 2 — for BOTH purposes). A row that still owes anything is never settled.
    /// </summary>
    internal static bool IsSettled(DocumentRelocationOutcome outcome)
        => outcome.Pending.Count == 0 && IsSettledState(outcome.State);

    internal static bool IsSettledState(RelocationState state) => state is
        RelocationState.NoFile or RelocationState.InPlace or RelocationState.Relocated
        or RelocationState.RelocatedSourceKeptForOtherRecords;
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
