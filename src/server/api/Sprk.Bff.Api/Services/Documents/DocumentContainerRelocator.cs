using System.Collections.Concurrent;
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
/// round 21 item 1 (i)–(ii), round 26 item 3, round 37, round 45): the only writer of a document's SharePoint Embedded
/// pointer (<c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c>) outside a server path that uploads the bytes itself.
/// </summary>
/// <remarks>
/// <para><b>Two operations, one pointer-attach path.</b></para>
/// <list type="number">
/// <item><see cref="AttachFileAsync"/> — the CLIENT pointer writers' replacement (round 21 item 1 (i), ADR-002 WP-3):
/// the client creates the row WITHOUT pointers, uploads the bytes through the record-keyed (or record-less) upload
/// route, and asks the BFF to attach them. The BFF verifies, then stamps the pointer as the application — the identity
/// the pointer columns' field-level security admits (<c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>).</item>
/// <item><see cref="RelocateIfMisplacedAsync"/> — moves a file that is NOT in its document's derived container into
/// it: copy through the BFF identity — REPLAYING the source's version history oldest first, the current content last
/// (round 45 item 1) — verify (each version's size; the final content's size, and <c>quickXorHash</c> where Graph returns
/// one), re-point through the SAME pointer-attach path, then settle what the move leaves behind (below). Two callers, one
/// mechanism (round 26 item 3): task 166's legacy migration (<see cref="DocumentContainerMigrationJob"/>) and the Make
/// Secure transition (task 150's provisioning lane).</item>
/// </list>
/// <para><b>What a move leaves behind, and the relocation ledger</b> (owner round 37, task 166 f1-v1; round 45). Re-pointing
/// the row is one atomic Dataverse update that ALSO re-keys the row's own columns holding the old drive / item
/// (<see cref="ReKeyedOwnColumns"/>), records the replayed versions' ORIGINAL authorship
/// (<see cref="RelocatedVersionHistory.Column"/>) and writes the row's relocation ledger (<see cref="RelocationLedgerColumn"/>):
/// one entry per old item, naming what is still owed for it, and the source's WITNESS — its size, <c>quickXorHash</c> and
/// version at the moment its copy was verified (round 45 item 4) —</para>
/// <list type="bullet">
/// <item><b>re-key</b>: the other rows that hold the old item id for THIS document — a child attachment's
/// <c>sprk_parentgraphitemid</c>, and the <c>sprk_communicationattachment</c> row linked to this document;</item>
/// <item><b>source</b>: the old item is deleted only when no row references it any more AND it still matches its witness.
/// A referencing <c>sprk_document</c> whose own derived container is the one this document now lives in (inside the secure
/// record's subtree, for Make Secure) is moved along — with its OWN copy, because <c>sprk_graphitemid_uk</c> is a unique key
/// on the item id and two rows can never share one. A <c>sprk_communicationattachment</c> row not linked to this document
/// is classified by the subtree of the record its communication is regarding (round 45 item 3): inside → re-keyed to the
/// current file; outside → it keeps the source; undecidable → pending. A row OUTSIDE keeps the source, which is then that
/// record's file: the transition is complete and the report lists it (<see cref="DocumentRelocationOutcome.KeptForOtherRecords"/>);
/// the entry stays so a later pass deletes the source once nothing references it. A source EDITED after the move no longer
/// matches its witness: it is reported (<see cref="DocumentRelocationOutcome.SourceChangedAfterMove"/>, never deleted as
/// it stands) and closed by the relocator itself — re-copy (the document's current file and history, and the source's
/// versions written after the move), verify, re-point — never by a manual step. Every such edit is carried (nothing is
/// lost), but it becomes the document's CURRENT content only when its author may write the document now; otherwise it is
/// kept in the history and the document's own content stays current (the old file stayed writable by the old container's
/// audience, and a move must never let someone who lost access with it change the document);</item>
/// <item><b>index</b>: the new item indexed and the old item's chunks removed, through ONE <c>Services/Ai/PublicContracts</c>
/// facade (<see cref="IRelocatedFileIndexing"/>, ADR-013).</item>
/// </list>
/// <para>Whatever cannot be finished is reported pending (<see cref="RelocationState.RelocationPending"/>, incomplete) and
/// stays in the ledger, so a REPEAT call — the next Make Secure call, or the migration's next pass — recognises the row as
/// already re-pointed and settles it. The ledger is the only witness of a source on a repeat call, so a source is deleted
/// only when it still matches the witness recorded at verification (round 45 item 4) — never compared with the
/// document's CURRENT file, which its users may have edited since. The ledger and the version record are BFF-written only
/// (field-level security from their creation, <c>scripts/Set-DocumentRelocationSchema.ps1</c>).</para>
/// <para><b>One relocation per document, for as long as it runs</b> (owner rounds 37 and 54 item 2): the ADR-004 processing
/// lock, taken per call under an owner id of its own, read back before anything runs, renewed by a heartbeat and confirmed
/// again before every destructive step (the re-point, each ledger entry's settle, a source delete, the ledger write). A move
/// that loses it stops there, reports <see cref="LockLostPrefix"/>, and never continues unlocked.</para>
/// <para><b>Every step is logged with before / after ids</b> (<c>[DOCUMENT-RELOCATE]</c>).</para>
/// <para><b>Placement</b> (CLAUDE.md §10; <c>.claude/constraints/bff-extensions.md</c>): in the BFF — it composes the
/// BFF's own container decisions (<see cref="RecordContainerResolver"/>), its app-only SPE facade and its Dataverse
/// application identity, and the pointer columns are written by that identity alone. Scoped, registered unconditionally
/// in <c>DocumentsModule</c>: the route that calls it is mapped unconditionally (§F.1).</para>
/// </remarks>
public sealed class DocumentContainerRelocator
{
    /// <summary>Graph's simple-upload boundary for SPE (250 MB). A larger file (or version) is reported, never half-copied.</summary>
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
    private const string AttachmentCommunicationColumn = "sprk_communication";

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

    /// <summary>
    /// How long one renewal of a document's relocation lock lasts. A crashed holder's lock expires after it; a running
    /// relocation RENEWS it every <see cref="RelocationLockRenewInterval"/> (owner round 54 item 2), so a move that replays
    /// a long history never outlives its lock.
    /// </summary>
    internal static readonly TimeSpan RelocationLockDuration = TimeSpan.FromMinutes(10);

    /// <summary>The heartbeat: how often a running relocation renews its lock (a fifth of <see cref="RelocationLockDuration"/>).</summary>
    internal static readonly TimeSpan RelocationLockRenewInterval = TimeSpan.FromMinutes(2);

    /// <summary>The report code of a relocation that lost its lock and stopped before its next destructive step (round 54 item 2).</summary>
    internal const string LockLostPrefix = "lock-lost";

    /// <summary>
    /// The report code of a source edited after its move whose post-move versions cannot be put in order (round 54 item 3):
    /// the row and the source are left untouched, and a repeat call retries.
    /// </summary>
    internal const string HistoryUndecidablePrefix = "history-undecidable";

    private readonly RecordContainerResolver _resolver;
    private readonly IGenericEntityService _dataverse;
    private readonly SpeFileStore _spe;
    private readonly IRelocatedFileIndexing _indexing;
    private readonly IIdempotencyService _locks;
    private readonly IAccessDataSource _access;
    private readonly TimeProvider _time;
    private readonly ILogger<DocumentContainerRelocator> _logger;

    /// <summary>Who an APP-ONLY upload was made for (task 171 attach fix); without it an app-uploaded file is refused.</summary>
    private readonly UploadAttribution? _uploadAttribution;

    /// <summary>The relocation locks this instance holds, by document (each with its heartbeat).</summary>
    private readonly ConcurrentDictionary<Guid, RelocationLease> _leases = new();

    /// <param name="resolver">The container decisions.</param>
    /// <param name="dataverse">The app-only entity service.</param>
    /// <param name="spe">The app-only SPE facade.</param>
    /// <param name="indexing">The PublicContracts indexing facade (ADR-013).</param>
    /// <param name="locks">The ADR-004 processing lock (taken, confirmed, renewed and released per call).</param>
    /// <param name="access">
    /// Dataverse's own answer "may this person write the document?" — read FRESH (owner round 54 item 1).
    /// <c>DocumentsModule</c> passes the UNCACHED <see cref="DataverseAccessDataSource"/>, never the 60-second
    /// <c>CachedAccessDataSource</c>; and whatever is passed, an answer produced before the question was asked (a cached
    /// one) is never taken as a right (<see cref="MayWriteAsync"/>).
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">The clock of the ledger and of the lock's heartbeat.</param>
    public DocumentContainerRelocator(
        RecordContainerResolver resolver,
        IGenericEntityService dataverse,
        SpeFileStore spe,
        IRelocatedFileIndexing indexing,
        IIdempotencyService locks,
        IAccessDataSource access,
        ILogger<DocumentContainerRelocator> logger,
        TimeProvider? timeProvider = null,
        UploadAttribution? uploadAttribution = null)
    {
        _uploadAttribution = uploadAttribution;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The processing-lock key of one document's relocation.</summary>
    internal static string RelocationLockKey(Guid documentId) => $"document-relocate-{documentId:N}";

    /// <summary>The detail of a row whose <c>sprk_graphitemid</c> holds a GUID — the archive path's document id, not an item.</summary>
    internal const string UnresolvableArchiveItemDetail =
        "the row's item id is a GUID (a document id the communication-archive path wrote), not a drive item; it names no "
        + "file and is reported, never moved or deleted";

    /// <summary>
    /// True when <paramref name="itemId"/> is a HYPHENATED GUID (<c>D</c>, <c>B</c> or <c>P</c> form). SharePoint Embedded
    /// drive-item ids are base-32 strings without hyphens (<c>01…</c>, 34 characters), never this shape, so such a value is
    /// the communication-archive path's <c>sprk_document</c> id (word-add-in-r1's archive bug), not an item. The bare
    /// 32-hex <c>N</c> form is deliberately not matched.
    /// </summary>
    internal static bool NamesADocumentIdNotADriveItem(string itemId)
        => Guid.TryParseExact(itemId, "D", out _) || Guid.TryParseExact(itemId, "B", out _)
           || Guid.TryParseExact(itemId, "P", out _);

    /// <summary>
    /// Takes the document's relocation lock as a lease of THIS call: an owner id of its own, confirmed by reading it back,
    /// then renewed by a heartbeat until <see cref="UnlockAsync"/>. <see langword="false"/> = not held (another relocation
    /// holds it, the lock store faulted, or the lock does not read back as ours) — nothing may run (fail closed).
    /// </summary>
    private async Task<bool> TryLockAsync(Guid documentId, CancellationToken ct)
    {
        var lease = await RelocationLease.TryTakeAsync(_locks, documentId, _time, _logger, ct).ConfigureAwait(false);
        if (lease is null)
        {
            return false;
        }

        if (_leases.TryAdd(documentId, lease))
        {
            return true;
        }

        await lease.DisposeAsync().ConfigureAwait(false);
        return false;
    }

    private async Task UnlockAsync(Guid documentId)
    {
        if (_leases.TryRemove(documentId, out var lease))
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether this call still holds the document's relocation lock: renewed and read back as ours NOW (owner round 54
    /// item 2). Asked before every destructive step — the re-point, a ledger entry's settle (re-key, move-along), a source
    /// delete, the ledger write — so a move that lost its lock stops there and never continues unlocked.
    /// </summary>
    private async Task<bool> StillLockedAsync(Guid documentId, CancellationToken ct)
        => _leases.TryGetValue(documentId, out var lease) && await lease.ConfirmAsync(ct).ConfigureAwait(false);

    /// <summary>The heartbeat already found the lock lost (a check between replay steps, without a round trip).</summary>
    private bool LockKnownLost(Guid documentId) => !_leases.TryGetValue(documentId, out var lease) || lease.Lost;

    private static string LockLost(Guid documentId, string where)
        => $"{LockLostPrefix}: the relocation lock of document {documentId} was lost {where}; nothing more was written; a "
           + "repeat call settles it";

    /// <summary>
    /// One call's hold on a document's relocation lock (owner round 54 item 2): taken under an owner id of its own and
    /// confirmed by reading it back (the lock store's acquire is check-then-set and fails open on a cache fault, #984: a
    /// lock is held only when it reads back as ours); renewed every <see cref="RelocationLockRenewInterval"/> by a
    /// heartbeat; released only while still ours. A renewal that fails marks it <see cref="Lost"/>, for good.
    /// </summary>
    private sealed class RelocationLease : IAsyncDisposable
    {
        private readonly IIdempotencyService _locks;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;
        private readonly Guid _documentId;
        private readonly CancellationTokenSource _stop = new();
        private Task _heartbeat = Task.CompletedTask;
        private int _lost;

        private RelocationLease(IIdempotencyService locks, Guid documentId, TimeProvider time, ILogger logger)
        {
            _locks = locks;
            _documentId = documentId;
            _time = time;
            _logger = logger;
            Key = RelocationLockKey(documentId);
        }

        public string Key { get; }

        /// <summary>This call's owner id — never another call's, so no other relocation can take over or release this lock.</summary>
        public string Owner { get; } = Guid.NewGuid().ToString("N");

        public bool Lost => Volatile.Read(ref _lost) == 1;

        public static async Task<RelocationLease?> TryTakeAsync(
            IIdempotencyService locks, Guid documentId, TimeProvider time, ILogger logger, CancellationToken ct)
        {
            var lease = new RelocationLease(locks, documentId, time, logger);
            bool taken;
            try
            {
                taken = await locks.TryAcquireProcessingLockAsync(lease.Key, lease.Owner, RelocationLockDuration, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Unknown whether another relocation runs: do not run (fail closed); the repeat call retries.
                logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the relocation lock of document {DocumentId} could not be taken.", documentId);
                lease._stop.Dispose();
                return null;
            }

            if (!taken)
            {
                lease._stop.Dispose();
                return null;
            }

            if (!await lease.ConfirmAsync(ct).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "[DOCUMENT-RELOCATE] the relocation lock of document {DocumentId} was answered taken but does not read back "
                    + "as this call's; nothing runs.", documentId);
                await lease.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            lease._heartbeat = lease.HeartbeatAsync(lease._stop.Token);
            return lease;
        }

        /// <summary>Renews the lock now: <see langword="false"/> — and <see cref="Lost"/> from then on — unless it is still ours.</summary>
        public async Task<bool> ConfirmAsync(CancellationToken ct)
        {
            if (Lost)
            {
                return false;
            }

            bool renewed;
            try
            {
                renewed = await _locks.RenewProcessingLockAsync(Key, Owner, RelocationLockDuration, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the relocation lock of document {DocumentId} could not be renewed.", _documentId);
                renewed = false;
            }

            if (!renewed && Interlocked.Exchange(ref _lost, 1) == 0)
            {
                _logger.LogError(
                    "[DOCUMENT-RELOCATE] lock-lost: the relocation lock of document {DocumentId} is no longer this call's; the "
                    + "relocation stops before its next destructive step.", _documentId);
            }

            return renewed;
        }

        /// <summary>Renews the lock every <see cref="RelocationLockRenewInterval"/> until released or lost.</summary>
        private async Task HeartbeatAsync(CancellationToken stop)
        {
            try
            {
                while (!Lost)
                {
                    await Task.Delay(RelocationLockRenewInterval, _time, stop).ConfigureAwait(false);
                    await ConfirmAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Released.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stop.IsCancellationRequested)
            {
                await _stop.CancelAsync().ConfigureAwait(false);
            }

            try
            {
                await _heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The heartbeat stops by cancellation.
            }

            _stop.Dispose();
            try
            {
                // Only while still ours: a lease that lost its lock never removes the lock another relocation now holds.
                await _locks.ReleaseProcessingLockAsync(Key, Owner, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DOCUMENT-RELOCATE] the relocation lock of document {DocumentId} could not be released; it expires in {Duration}.",
                    _documentId, RelocationLockDuration);
            }
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
        string? attributionKey = null;
        if (facts is not null && Guid.TryParse(facts.UserObjectId, out var uploader) && uploader != Guid.Empty)
        {
            // Uploaded BY A PERSON (Graph createdBy.user): it must be the caller.
            if (uploader != caller)
            {
                return NotTheUploader(documentId, drive, item, caller);
            }
        }
        else if (facts is not null && _resolver.IsUploadedByTheBffIdentity(facts))
        {
            // Uploaded APP-ONLY by the BFF (every record-keyed / record-less upload since task 171): Graph cannot say for
            // whom, so the binding the BFF recorded at upload time must name the caller. "Any BFF-uploaded item" would
            // admit everyone's files — that is exactly what this check exists to stop.
            if (_uploadAttribution is null)
            {
                return NotTheUploader(documentId, drive, item, caller);
            }

            UploadAttribution.MatchOutcome match;
            try
            {
                (match, attributionKey) = await _uploadAttribution.MatchAsync(caller, drive, item, facts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "[DOCUMENT-ATTACH] REFUSED: who uploaded item {Item} of {Drive} could not be read (document {DocumentId}); "
                    + "fail closed.", item, drive, documentId);
                return PointerAttachResult.Refused(PointerAttachOutcome.UploaderUnverifiable,
                    "Who uploaded this file could not be confirmed just now. Try attaching it again shortly.");
            }

            if (match != UploadAttribution.MatchOutcome.Caller)
            {
                return NotTheUploader(documentId, drive, item, caller);
            }
        }
        else
        {
            // Absent, or uploaded by another application.
            return NotTheUploader(documentId, drive, item, caller);
        }

        await WritePointerAsync(documentId, drive, item, facts.WebUrl, alsoWrite: null, ct).ConfigureAwait(false);
        if (attributionKey is not null)
        {
            await _uploadAttribution!.ConsumeAsync(attributionKey, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "[DOCUMENT-ATTACH] document {DocumentId} -> {Drive}/{Item} (uploaded by its creator, in its derived container).",
            documentId, drive, item);
        return PointerAttachResult.Attached(drive, item, alreadyAttached: false);
    }

    private PointerAttachResult NotTheUploader(Guid documentId, string drive, string item, Guid caller)
    {
        _logger.LogWarning(
            "[DOCUMENT-ATTACH] REFUSED: item {Item} of {Drive} is absent or was not uploaded by the caller {Caller} "
            + "(document {DocumentId}).", item, drive, caller, documentId);
        return PointerAttachResult.Refused(PointerAttachOutcome.NotTheUploader,
            "The file was not found, or it was not uploaded by you.");
    }

    /// <summary>
    /// THE pointer-attach path: the only write of a document's pointer by this type. App-only — the identity the pointer
    /// columns' field-level security admits. <c>sprk_hasfile</c> is set with the pointer, so "has a file" and "points at
    /// a file" never disagree. <paramref name="alsoWrite"/> carries what a RELOCATION writes in the same atomic update:
    /// the re-keyed own columns, the version record and the relocation ledger (<see cref="DBNull.Value"/> clears a column).
    /// </summary>
    private Task WritePointerAsync(
        Guid documentId, string drive, string item, string? webUrl, IReadOnlyDictionary<string, object>? alsoWrite,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, object>
        {
            [DriveColumn] = drive,
            [ItemColumn] = item,
            [Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn] = item, // Task 171 round 72 (F4): the field-secured copy the pointer check compares — same write, same value.
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
        // would be lost. The existing ADR-004 processing lock, held for as long as the move runs (renewed by a heartbeat,
        // confirmed before every destructive step — round 54 item 2); a held lock is reported (incomplete), never waited on.
        if (!await TryLockAsync(documentId, ct).ConfigureAwait(false))
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, null, null, expectedTargetContainer, null,
                "another relocation of this document is running, or its lock could not be confirmed; a repeat call settles it");
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

        // Task 171 round 74 (V1): the item must be the one the BFF BOUND to this row before anything is copied, re-pointed
        // or deleted. A move writes a MATCHING copy for whatever item it moves, so moving a forged pointer would launder it
        // (and the settle would then delete the item it was forged to). Same rule, same definition as the pointer check.
        if (_resolver.ItemBindingRefusal(row!, sourceItem) is { } bindingRefusal)
        {
            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT moved: {Reason}. Nothing is copied, re-pointed or deleted.",
                documentId, bindingRefusal);
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.SourceUnverified, sourceDrive, sourceItem, null, null,
                bindingRefusal + " — an administrator must repair it; it is not moved");
        }

        // An earlier move of this row may still owe something (round 37 / F2): settle it BEFORE anything else, so a
        // repeat call completes it whatever the file's placement is now.
        var ledger = RelocationLedger.Parse(row!.GetAttributeValue<string>(RelocationLedgerColumn));
        var prior = await SettleOrReportAsync(documentId, row, sourceDrive, sourceItem, ledger, purpose, apply, ct)
            .ConfigureAwait(false);

        // Round 45 item 4: a source EDITED after its move is closed by the relocator's own re-entry — re-copy, verify,
        // re-point (in the document's CURRENT container), then the settle deletes the source against its new witness.
        if (apply && prior.Changed.Count > 0 && !prior.LedgerUnreadable)
        {
            var recopy = await RecopyChangedSourcesAsync(documentId, row, sourceDrive, sourceItem, prior, purpose, ct)
                .ConfigureAwait(false);
            prior = recopy.Summary;
            if (recopy.Row is { } after)
            {
                row = after;
                sourceDrive = after.GetAttributeValue<string>(DriveColumn)?.Trim() ?? sourceDrive;
                sourceItem = after.GetAttributeValue<string>(ItemColumn)?.Trim() ?? sourceItem;
            }
        }

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

        // Batch-4 integration (word-add-in-r1's archive bug, which they fix on master): CommunicationService's
        // ArchiveOutboundAttachmentsAsync wrote each attachment's sprk_document ID into sprk_graphitemid. A GUID is never
        // a drive-item id, so such a row names no file: it is reported (FileMissing) and never treated as an item — no
        // Graph read, no copy, no re-point, no delete of anything.
        if (NamesADocumentIdNotADriveItem(sourceItem))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.FileMissing, sourceDrive, sourceItem, derivation.PrimaryContainer, null,
                UnresolvableArchiveItemDetail).With(prior);
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

        // Round 45 item 1: the copy carries the source's version history — read it (with any originals an earlier move of
        // this row recorded) before a byte moves.
        var history = await ReadHistoryAsync(sourceDrive, sourceItem, VersionRecordOf(row), ct).ConfigureAwait(false);
        if (history.Failure is not null)
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"its version history could not be copied: {history.Failure}").With(prior);
        }

        var moved = await MoveAsync(documentId, row, sourceDrive, sourceItem, target,
            new ReplayPlan(history.Steps, facts, history.Witness, facts.Name ?? row.GetAttributeValue<string>(FileNameColumn)),
            prior.Ledger, LedgerSourceState.Pending, ct).ConfigureAwait(false);
        if (moved.Failure is not null)
        {
            return moved.Failure.With(prior);
        }

        // Settle what the move owes now. The source is deleted only when it still matches the witness this move recorded
        // moments ago (an edit in between is re-copied, below).
        var rowAfter = await ReadRowAsync(documentId, ct).ConfigureAwait(false) ?? row;
        var settled = await SettleOrReportAsync(documentId, rowAfter, target, moved.CopyId!,
            RelocationLedger.Parse(rowAfter.GetAttributeValue<string>(RelocationLedgerColumn)), purpose, apply: true, ct)
            .ConfigureAwait(false);
        settled.Note(moved.Truncated);
        var finalItem = moved.CopyId!;
        if (settled.Changed.Count > 0 && !settled.LedgerUnreadable)
        {
            var recopy = await RecopyChangedSourcesAsync(documentId, rowAfter, target, moved.CopyId!, settled, purpose, ct)
                .ConfigureAwait(false);
            settled = recopy.Summary;
            finalItem = recopy.Row?.GetAttributeValue<string>(ItemColumn)?.Trim() ?? finalItem;
        }

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
        var outcome = DocumentRelocationOutcome.Of(documentId, state, sourceDrive, sourceItem, target, finalItem, detail).With(settled);
        return outcome with
        {
            MovedAlong = prior.MovedAlong.Concat(outcome.MovedAlong).ToList(),
            SourceChangedAfterMove = prior.ChangedAfterMove.Concat(outcome.SourceChangedAfterMove).ToList(),
            VersionsTruncated = prior.Truncated.Concat(outcome.VersionsTruncated).ToList(),
        };
    }

    /// <summary>
    /// The batch entry point for a caller that holds the document ids and the target container — task 150's Make Secure
    /// transition (round 26 item 3: the record's existing files move into its own container). Each document goes through
    /// <see cref="RelocateIfMisplacedAsync"/> with <paramref name="targetContainerId"/> as the expected target; a fault in
    /// one document is that document's <see cref="RelocationState.Failed"/>, never the batch's. The result counts every
    /// outcome, lists the INCOMPLETE ones, every source kept for another record, every source edited after its move and
    /// every truncated history; a repeat call settles what is owed.
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
    // The version history a move carries (owner round 45 item 1)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One version to write into the copy: where its bytes come from, its expected size, and its ORIGINAL authorship.</summary>
    private sealed record ReplayStep(
        string VersionId, Func<CancellationToken, Task<Stream?>> Open, long? ExpectedSize, RelocatedVersionHistory.Entry Origin);

    /// <summary>
    /// What a copy is made of: the <see cref="Steps"/> oldest first (the last is the content the copy must end with),
    /// <see cref="FinalFacts"/> — the item that content is verified against — and the WITNESS of the item the row moves
    /// away from (recorded in its ledger entry).
    /// </summary>
    private sealed record ReplayPlan(
        IReadOnlyList<ReplayStep> Steps, SpeItemCreator FinalFacts, RelocationWitness OldItemWitness, string? FileName);

    /// <summary>An item's history as replay steps, its facts and its witness — or why it cannot be copied.</summary>
    private sealed record History(
        IReadOnlyList<ReplayStep> Steps, SpeItemCreator? Facts, RelocationWitness Witness, string? Failure)
    {
        public static History Fail(string why) => new([], null, new RelocationWitness(null, null, null), why);

        /// <summary>
        /// The steps written AFTER the state <paramref name="witness"/> recorded — a source's edits since its move — oldest
        /// first, ending with its current content (owner round 54 item 3: no intermediate edit is dropped).
        /// <list type="bullet">
        /// <item>By version NUMBER when the witness's version and every listed id are numbers: every version above the
        /// witnessed one. None above it = the witnessed version itself was changed in place, so its current content IS
        /// the only content after the witness.</item>
        /// <item>Otherwise by TIME: every version written after the witness's time (<see cref="RelocationWitness.Modified"/>),
        /// in time order.</item>
        /// </list>
        /// <see langword="null"/> steps, with the reason, when that order cannot be decided — no witness time, a version
        /// without a time, two versions at the same time, none later than the witness, or a current content that is not
        /// the latest by time. Never "the current content alone", which would drop every intermediate edit.
        /// </summary>
        public (IReadOnlyList<ReplayStep>? Steps, string? Undecidable) After(RelocationWitness? witness)
        {
            if (Steps.Count > 0 && witness?.Version is { } version && System.Version.TryParse(version, out var witnessed)
                && Steps.All(s => System.Version.TryParse(s.VersionId, out _)))
            {
                var newer = Steps.Where(s => System.Version.Parse(s.VersionId) > witnessed).ToList();
                return (newer.Count > 0 ? newer : [Steps[^1]], null);
            }

            if (witness?.Modified is not { } since || since == default)
            {
                return (null, "the witness recorded neither a numbered version nor a time to order the later versions by");
            }

            if (Steps.Any(s => s.Origin.At is not { } at || at == default))
            {
                return (null, "a version of the source carries no time");
            }

            var later = Steps.Where(s => s.Origin.At > since).OrderBy(s => s.Origin.At).ToList();
            if (later.Count == 0)
            {
                return (null, "no version of the source is later than the witness, though its content changed");
            }

            if (later.Select(s => s.Origin.At).Distinct().Count() != later.Count)
            {
                return (null, "two versions written after the move carry the same time");
            }

            return ReferenceEquals(later[^1], Steps[^1])
                ? (later, null)
                : (null, "the source's current content is not its latest version by time");
        }
    }

    /// <summary>
    /// The item's versions, oldest first, as replay steps: every PRIOR version through the app-only
    /// <see cref="SpeFileStore.DownloadFileVersionAsync"/>, the current content last through the current download. Each
    /// version's original authorship is the one <paramref name="record"/> holds for it (an earlier relocation replayed it),
    /// else Graph's. The witness is the item's size, hash and current version.
    /// </summary>
    private async Task<History> ReadHistoryAsync(string drive, string item, RelocatedVersionHistory.Map? record, CancellationToken ct)
    {
        var facts = await _spe.GetItemCreatorAsync(drive, item, ct).ConfigureAwait(false);
        if (facts is null)
        {
            return History.Fail($"{drive}/{item} is not in its drive");
        }

        IReadOnlyList<VersionInfoDto>? listed;
        try
        {
            listed = await _spe.ListFileVersionsAsync(drive, item, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the versions of {Drive}/{Item} could not be listed.", drive, item);
            return History.Fail($"the versions of {drive}/{item} could not be listed ({ex.GetType().Name})");
        }

        if (listed is null)
        {
            return History.Fail($"{drive}/{item} is not in its drive");
        }

        var ordered = OldestFirst(listed);
        var steps = new List<ReplayStep>(Math.Max(ordered.Count, 1));
        for (var i = 0; i < ordered.Count; i++)
        {
            var version = ordered[i];
            var origin = record?.Of(item, version.Id) is { } recorded
                ? recorded
                : new RelocatedVersionHistory.Entry(string.Empty, version.LastModifiedBy, version.LastModifiedByUserId,
                    version.LastModifiedByApplicationId, version.LastModifiedDateTime, version.Size, item, version.Id);
            var versionId = version.Id;
            steps.Add(i == ordered.Count - 1
                ? new ReplayStep(versionId, c => _spe.DownloadFileAsync(drive, item, c), facts.Size, origin)
                : new ReplayStep(versionId, c => _spe.DownloadFileVersionAsync(drive, item, versionId, c), version.Size, origin));
        }

        if (steps.Count == 0)
        {
            // Graph listed no version (it lists the current one for every file): the current content, authorship unknown.
            steps.Add(new ReplayStep(string.Empty, c => _spe.DownloadFileAsync(drive, item, c), facts.Size,
                new RelocatedVersionHistory.Entry(string.Empty, null, null, null, null, facts.Size ?? 0, item, null)));
        }

        // The witness: size, hash, the current version id and the time of the current content (that version's time, else
        // the item's) — the time orders the versions a later edit adds when their ids cannot be (round 54 item 3).
        var current = ordered.Count > 0 ? ordered[^1] : null;
        var witness = new RelocationWitness(facts.Size, facts.QuickXorHash, current?.Id,
            current is not null && current.LastModifiedDateTime != default ? current.LastModifiedDateTime : facts.LastModified);
        if (!witness.IsProvable)
        {
            return History.Fail($"{drive}/{item} reports no size, or neither a quickXorHash nor a version, so a later call "
                                + "could never prove it unchanged");
        }

        if (steps.Any(s => s.ExpectedSize is > MaxRelocatableBytes))
        {
            return History.Fail($"a version of {drive}/{item} is larger than a single-request copy ({MaxRelocatableBytes} bytes)");
        }

        return new History(steps, facts, witness, null);
    }

    /// <summary>Versions oldest first: by version number when every id is one ("1.0", "2.0", …), else by date.</summary>
    internal static List<VersionInfoDto> OldestFirst(IEnumerable<VersionInfoDto> versions)
    {
        var list = versions.Where(v => !string.IsNullOrWhiteSpace(v.Id)).ToList();
        return list.All(v => System.Version.TryParse(v.Id, out _))
            ? list.OrderBy(v => System.Version.Parse(v.Id)).ToList()
            : list.OrderBy(v => v.LastModifiedDateTime).ToList();
    }

    private static RelocatedVersionHistory.Map? VersionRecordOf(Entity row)
        => RelocatedVersionHistory.Map.Parse(row.GetAttributeValue<string>(RelocatedVersionHistory.Column));

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // The move: copy (replay) → verify → re-point (with the own-column re-key, the version record and the ledger, one update)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<MoveResult> MoveAsync(
        Guid documentId, Entity row, string sourceDrive, string sourceItem, string targetDrive, ReplayPlan plan,
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

        // 1. COPY through the BFF identity, REPLAYING the history oldest first (round 45 item 1): the first version creates
        //    the copy (Rename on a name collision — a flat container must never overwrite another document's file), every
        //    later one is written to that same item (each becomes a new version of it), the current content last. Each
        //    upload's size is checked; any failure removes the copy and leaves the row and the source untouched.
        FileHandleDto? copy = null;
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            if (LockKnownLost(documentId))
            {
                // The heartbeat lost the lock mid-replay (round 54 item 2): stop now, remove the partial copy.
                await RemoveCopyAsync(documentId, targetDrive, copy, ct).ConfigureAwait(false);
                return MoveResult.Failed(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                    LockLost(documentId, $"during the copy (before version {step.VersionId}); the partial copy was removed")));
            }

            FileHandleDto? written;
            try
            {
                await using var bytes = await step.Open(ct).ConfigureAwait(false);
                if (bytes is null)
                {
                    await RemoveCopyAsync(documentId, targetDrive, copy, ct).ConfigureAwait(false);
                    return MoveResult.Failed(DocumentRelocationOutcome.Of(
                        documentId, RelocationState.FileMissing, sourceDrive, sourceItem, targetDrive, null,
                        i == plan.Steps.Count - 1
                            ? "the source file could not be downloaded"
                            : $"version {step.VersionId} of the source could not be downloaded"));
                }

                // The first upload names the copy (sanitized: a file name IS a path); every later one is written to the name
                // Graph gave the copy — sanitizing a name Graph accepted changes nothing, so it lands on the same item.
                var uploadPath = SpeUploadPath.SanitizeFileName(copy?.Name ?? plan.FileName ?? row.GetAttributeValue<string>(FileNameColumn));
                written = await _spe.UploadSmallAsync(
                    targetDrive, uploadPath, bytes, copy is null ? ConflictBehavior.Rename : ConflictBehavior.Replace, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // A fault mid-replay must not leave a partial copy behind: the row and the source are untouched.
                _logger.LogError(ex,
                    "[DOCUMENT-RELOCATE] replay FAULTED for document {DocumentId} at version {Version}; the row and the source "
                    + "are unchanged.", documentId, step.VersionId);
                await RemoveCopyAsync(documentId, targetDrive, copy, ct).ConfigureAwait(false);
                return MoveResult.Failed(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                    $"the copy faulted at version {step.VersionId} ({ex.GetType().Name})"));
            }

            var failure = written is null || string.IsNullOrWhiteSpace(written.Id)
                ? copy is null ? "the copy was not created" : $"version {step.VersionId} was not written into the copy"
                : copy is not null && !string.Equals(written.Id, copy.Id, StringComparison.Ordinal)
                    ? $"version {step.VersionId} was written to another item ({written.Id}), not the copy"
                    : step.ExpectedSize is { } expected && written.Size != expected
                        ? $"version {step.VersionId} was written with size {written.Size?.ToString() ?? "unknown"}, not {expected}"
                        : null;
            if (failure is not null)
            {
                if (written is not null && !string.IsNullOrWhiteSpace(written.Id)
                    && (copy is null || !string.Equals(written.Id, copy.Id, StringComparison.Ordinal)))
                {
                    await DeleteQuietlyAsync(targetDrive, written.Id, ct).ConfigureAwait(false);
                }

                await RemoveCopyAsync(documentId, targetDrive, copy, ct).ConfigureAwait(false);
                _logger.LogError(
                    "[DOCUMENT-RELOCATE] replay FAILED for document {DocumentId} ({Failure}); the row and the source are unchanged.",
                    documentId, failure);
                return MoveResult.Failed(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, failure));
            }

            copy = written;
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] copied: document {DocumentId} {SourceDrive}/{SourceItem} -> {TargetDrive}/{TargetItem} "
            + "({Versions} version(s) replayed, oldest first)", documentId, sourceDrive, sourceItem, targetDrive, copy!.Id,
            plan.Steps.Count);

        // 2. VERIFY the copy's content against the source before anything points at it or the source is touched.
        var copyFacts = await _spe.GetItemCreatorAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
        var mismatch = CopyMismatch(plan.FinalFacts, copyFacts);
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

        // 3. The ORIGINAL authorship of every version the copy now holds, against its NEW version id (round 45 item 1).
        //    The copy's versions are listed and matched newest-to-newest; fewer than replayed = the target's version limit
        //    dropped the oldest (stated: versions-truncated, never silent).
        var record = await RecordReplayAsync(documentId, targetDrive, copy.Id, plan, ct).ConfigureAwait(false);
        if (record.Failure is not null)
        {
            await DeleteQuietlyAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, record.Failure));
        }

        // 4. RE-POINT through the pointer-attach path — with the row's own re-keyed columns, the version record and the
        //    ledger entry (with the source's witness) for the old item, in ONE update, so no crash can leave a re-pointed
        //    row that forgot what it still owes or who wrote its history.
        var alsoWrite = ReKeyOwnColumns(ownReferences, sourceDrive, sourceItem, targetDrive, copy);
        var entry = new RelocationLedgerEntry
        {
            SourceDrive = sourceDrive,
            SourceItem = sourceItem,
            Source = sourceState,
            RekeyPending = true,
            Indexed = LedgerIndexScope.None,
            At = _time.GetUtcNow(),
            Witness = plan.OldItemWitness,
        };
        alsoWrite[RelocationLedgerColumn] = ledger.With(entry).Serialize();
        alsoWrite[RelocatedVersionHistory.Column] = record.Json!;

        // Round 54 item 2: the re-point is destructive — only under a lock confirmed as this call's NOW. A lost lock
        // stops here: the copy (this call's own, referenced by nothing) is removed; the row and the source are untouched.
        if (!await StillLockedAsync(documentId, ct).ConfigureAwait(false))
        {
            await DeleteQuietlyAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                LockLost(documentId, "before the re-point; the row still names its file and the copy was removed")));
        }

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
            + "re-keyed own columns [{Columns}]; {Recorded} version(s) recorded with their original authorship; ledger entry "
            + "(with the source's witness) for the old item recorded.",
            documentId, sourceDrive, sourceItem, targetDrive, copy.Id,
            string.Join(", ", alsoWrite.Keys.Where(k => k != RelocationLedgerColumn && k != RelocatedVersionHistory.Column)),
            record.Recorded);
        return new MoveResult(copy.Id, copyFacts, null, record.Truncated);
    }

    private sealed record MoveResult(string? CopyId, SpeItemCreator? CopyFacts, DocumentRelocationOutcome? Failure, VersionsTruncated? Truncated = null)
    {
        public static MoveResult Failed(DocumentRelocationOutcome failure) => new(null, null, failure);
    }

    private sealed record ReplayRecord(string? Json, int Recorded, VersionsTruncated? Truncated, string? Failure);

    /// <summary>The copy's version record: each of its versions matched to the step it replayed, newest to newest.</summary>
    private async Task<ReplayRecord> RecordReplayAsync(
        Guid documentId, string targetDrive, string copyId, ReplayPlan plan, CancellationToken ct)
    {
        IReadOnlyList<VersionInfoDto>? copyVersions;
        try
        {
            copyVersions = await _spe.ListFileVersionsAsync(targetDrive, copyId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the copy's versions could not be listed (document {DocumentId}).", documentId);
            return new ReplayRecord(null, 0, null, $"the copy's versions could not be listed ({ex.GetType().Name}), so their "
                                                   + "original authorship could not be recorded");
        }

        if (copyVersions is null || copyVersions.Count == 0)
        {
            return new ReplayRecord(null, 0, null, "the copy's versions could not be listed, so their original authorship could not be recorded");
        }

        var written = OldestFirst(copyVersions);
        var matched = Math.Min(written.Count, plan.Steps.Count);
        var entries = Enumerable.Range(0, matched)
            .Select(k => plan.Steps[plan.Steps.Count - matched + k].Origin with { Id = written[written.Count - matched + k].Id })
            .ToList();
        var (json, dropped) = new RelocatedVersionHistory.Map(copyId, entries).Serialize();
        var truncated = written.Count < plan.Steps.Count || dropped > 0
            ? new VersionsTruncated(documentId, copyId, plan.Steps.Count, written.Count, dropped)
            : null;
        if (truncated is not null)
        {
            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] versions-truncated: document {DocumentId}'s copy {Copy} holds {Kept} of {Replayed} replayed "
                + "versions (the target container's version limit); {Dropped} oldest original author(s) not recorded (column limit).",
                documentId, copyId, written.Count, plan.Steps.Count, dropped);
        }

        return new ReplayRecord(json, entries.Count - dropped, truncated, null);
    }

    private async Task RemoveCopyAsync(Guid documentId, string drive, FileHandleDto? copy, CancellationToken ct)
    {
        if (copy is not null && !string.IsNullOrWhiteSpace(copy.Id))
        {
            _logger.LogWarning("[DOCUMENT-RELOCATE] removing the partial copy {Drive}/{Item} of document {DocumentId}.",
                drive, copy.Id, documentId);
            await DeleteQuietlyAsync(drive, copy.Id, ct).ConfigureAwait(false);
        }
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
    // A source edited after its move (owner round 45 item 4): re-copy, verify, re-point
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record RecopyResult(SettleSummary Summary, Entity? Row);

    /// <summary>
    /// Closes every source in <paramref name="summary"/> that was edited after its move: a NEW copy, in the document's
    /// current container, of the document's current file WITH its history (originals as recorded) and each edited
    /// source's versions written after its witness, oldest first; verified; the row re-pointed to it (the replaced file
    /// becomes an old item of the ledger, with its own witness, and each edited source's witness becomes its current
    /// state); then settled — so the replaced file and each source are deleted on this same call unless they change again.
    /// Nothing the document or the source held is lost: both histories are in the new copy. The last edit becomes the
    /// CURRENT content only when its author may write the document now (<see cref="MayWriteAsync"/>); otherwise it is
    /// history and the document's own content stays current. A failure leaves everything as it was and is reported
    /// pending; the next call retries.
    /// </summary>
    private async Task<RecopyResult> RecopyChangedSourcesAsync(
        Guid documentId, Entity row, string currentDrive, string currentItem, SettleSummary summary, RelocationPurpose purpose,
        CancellationToken ct)
    {
        var changed = summary.Changed.OrderBy(c => c.Entry.At).ToList();
        var current = await ReadHistoryAsync(currentDrive, currentItem, VersionRecordOf(row), ct).ConfigureAwait(false);
        if (current.Failure is not null)
        {
            summary.AddPending($"source-changed-after-move: the re-copy could not start ({current.Failure}); a repeat call retries");
            return new RecopyResult(summary, null);
        }

        var edits = new List<ReplayStep>();
        var ledger = summary.Ledger;
        var reports = new List<SourceChangedAfterMove>();
        SpeItemCreator? lastSourceFacts = null;
        foreach (var source in changed)
        {
            var history = await ReadHistoryAsync(source.Entry.SourceDrive, source.Entry.SourceItem, record: null, ct).ConfigureAwait(false);
            if (history.Failure is not null)
            {
                summary.AddPending($"source-changed-after-move: {source.Entry.SourceDrive}/{source.Entry.SourceItem} could not be "
                                   + $"re-copied ({history.Failure}); a repeat call retries");
                return new RecopyResult(summary, null);
            }

            // Round 54 item 3: every version written after the witness, in order — or, when that order cannot be decided,
            // nothing at all is moved (history-undecidable; the row and the source untouched; a repeat call retries).
            var (after, undecidable) = history.After(source.Entry.Witness);
            if (after is null)
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] history-undecidable: the versions {SourceDrive}/{SourceItem} received after the move of "
                    + "document {DocumentId} cannot be put in order ({Reason}); nothing is moved.",
                    source.Entry.SourceDrive, source.Entry.SourceItem, documentId, undecidable);
                summary.AddPending($"{HistoryUndecidablePrefix}: the versions {source.Entry.SourceDrive}/{source.Entry.SourceItem} "
                                   + $"received after the move cannot be put in order ({undecidable}); the row and the source "
                                   + "are untouched; a repeat call retries");
                return new RecopyResult(summary, null);
            }

            edits.AddRange(after);
            ledger = ledger.With(source.Entry with { Witness = history.Witness, Source = LedgerSourceState.Pending });
            lastSourceFacts = history.Facts;
            reports.Add(new SourceChangedAfterMove(documentId, source.Entry.SourceDrive, source.Entry.SourceItem, after.Count, null));
        }

        // EVERY version written at the old location after the move is carried, so nothing is lost. The last of them becomes
        // the document's CURRENT content only when the person who wrote it may write the document NOW (Dataverse's own
        // answer for that person, RetrievePrincipalAccess) — the old file stayed writable by the old container's audience,
        // and a move must never let someone who lost access with it change the document. Otherwise (no such right, an
        // app-only write, an answer that cannot be had: fail closed) the edits go into the history BEFORE the document's
        // own current content, which stays current; each keeps its original author in the version record.
        var editIsCurrent = await MayWriteAsync(documentId, edits[^1].Origin.ByUser, ct).ConfigureAwait(false);
        var steps = editIsCurrent
            ? current.Steps.Concat(edits).ToList()
            : current.Steps.Take(current.Steps.Count - 1).Concat(edits).Append(current.Steps[^1]).ToList();
        var finalFacts = editIsCurrent ? lastSourceFacts! : current.Facts!;
        reports = reports.Select(r => r with { EditIsCurrent = editIsCurrent }).ToList();

        var moved = await MoveAsync(documentId, row, currentDrive, currentItem, currentDrive,
            new ReplayPlan(steps, finalFacts, current.Witness, current.Facts!.Name ?? row.GetAttributeValue<string>(FileNameColumn)),
            ledger, LedgerSourceState.Pending, ct).ConfigureAwait(false);
        if (moved.Failure is not null)
        {
            summary.AddPending($"source-changed-after-move: the re-copy failed ({moved.Failure.State}: {moved.Failure.Detail}); "
                               + "a repeat call retries");
            return new RecopyResult(summary, null);
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] source-changed-after-move: document {DocumentId} re-copied {CurrentDrive}/{CurrentItem} + "
            + "{Sources} with the edits made after the move -> {CurrentDrive}/{NewItem} (round 45 item 4).",
            documentId, currentDrive, currentItem, string.Join(", ", reports.Select(r => $"{r.SourceDrive}/{r.SourceItem} ({r.CarriedVersions})")),
            currentDrive, moved.CopyId);

        var rowAfter = await ReadRowAsync(documentId, ct).ConfigureAwait(false) ?? row;
        var settled = await SettleOrReportAsync(documentId, rowAfter, currentDrive, moved.CopyId!,
            RelocationLedger.Parse(rowAfter.GetAttributeValue<string>(RelocationLedgerColumn)), purpose, apply: true, ct)
            .ConfigureAwait(false);
        settled.MovedAlong.InsertRange(0, summary.MovedAlong);
        // The sources this re-copy closed are reported once, closed (with the new item); any other report stands.
        settled.ChangedAfterMove.InsertRange(0, summary.ChangedAfterMove.Where(c =>
            !changed.Any(x => SameItem(x.Entry.SourceDrive, x.Entry.SourceItem, c.SourceDrive, c.SourceItem))));
        settled.ChangedAfterMove.AddRange(reports.Select(r => r with { NewItem = moved.CopyId }));
        settled.Truncated.InsertRange(0, summary.Truncated);
        settled.Note(moved.Truncated);

        // Edited AGAIN between this re-copy and its delete: the source no longer matches its new witness — reported
        // pending (settled.Changed is not acted on twice in one call); the next call re-copies.
        foreach (var again in settled.Changed)
        {
            settled.AddPending($"source-changed-after-move: {again.Entry.SourceDrive}/{again.Entry.SourceItem} changed again during "
                               + "the re-copy; a repeat call re-copies it");
        }

        return new RecopyResult(settled, rowAfter);
    }

    /// <summary>
    /// May the person <paramref name="authorObjectId"/> (an Entra object id) write the document NOW? Dataverse's own
    /// answer for that principal (<see cref="IAccessDataSource.GetUserAccessAsync"/>: RetrievePrincipalAccess, which
    /// factors in roles, teams and sharing), read FRESH (owner round 54 item 1). Not a person, no answer, an answer older
    /// than the question, or a fault: <see langword="false"/> (ADR-003).
    /// </summary>
    /// <remarks>
    /// FRESH: a Write answer cached just before Make Secure must never make a non-writer's edit current. The production
    /// wiring passes the uncached source; this method also refuses, whatever source it is given, an answer produced
    /// BEFORE the question was asked — <see cref="AccessSnapshot.CachedAt"/> is when the answer was read
    /// (<see cref="DataverseAccessDataSource"/> stamps the time of its Dataverse read; <c>CachedAccessDataSource</c> returns
    /// the time it CACHED the answer), so an older one is a cached one. Compared on the system clock, the clock both stamp
    /// it with.
    /// </remarks>
    private async Task<bool> MayWriteAsync(Guid documentId, string? authorObjectId, CancellationToken ct)
    {
        if (!Guid.TryParse(authorObjectId, out var author) || author == Guid.Empty)
        {
            return false;
        }

        try
        {
            var asked = DateTimeOffset.UtcNow;
            var snapshot = await _access.GetUserAccessAsync(author.ToString("D"), documentId.ToString("D"), userAccessToken: null, ct)
                .ConfigureAwait(false);
            if (snapshot.CachedAt < asked)
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] whether {Author} may write document {DocumentId} was answered from {AnsweredAt}, before "
                    + "it was asked ({AskedAt}) — a cached answer, not a fresh one; their edit is kept in the history, not made "
                    + "current.", author, documentId, snapshot.CachedAt, asked);
                return false;
            }

            return snapshot.AccessRights.HasFlag(AccessRights.Write);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] whether {Author} may write document {DocumentId} could not be read; their edit is kept in the "
                + "history, not made current.", author, documentId);
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Settling the ledger (round 37 items 1-2; F2; round 45 items 3-4)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Settles (apply) or reports (report-only) every entry of the row's ledger against its CURRENT file
    /// (<paramref name="currentDrive"/> / <paramref name="currentItem"/>), then writes the ledger back.
    /// </summary>
    private async Task<SettleSummary> SettleOrReportAsync(
        Guid documentId, Entity row, string currentDrive, string currentItem, RelocationLedger? ledger, RelocationPurpose purpose,
        bool apply, CancellationToken ct)
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
            // Round 54 item 2: each entry's settle writes (re-keys, a move-along, a delete) — only under the lock, confirmed
            // as this call's now. A lost lock stops the settle: nothing more is written, the stored ledger is left as it was
            // (every step is idempotent, so the repeat call redoes what is still owed).
            if (!await StillLockedAsync(documentId, ct).ConfigureAwait(false))
            {
                summary.AddPending(LockLost(documentId, "while settling what its move owes"));
                summary.Ledger = ledger;
                return summary;
            }

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
                var source = await SettleSourceAsync(documentId, entry, currentDrive, currentItem, purpose, summary, ct)
                    .ConfigureAwait(false);
                entry = entry with { Source = source.State };
                sourceDetail = source.Detail;
            }

            // A lock lost during (a) or (b) — a long move-along, or the source delete's confirmation — stops the settle here.
            if (LockKnownLost(documentId))
            {
                summary.AddPending(LockLost(documentId, "while settling what its move owes"));
                summary.Ledger = ledger;
                return summary;
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
            if (!await StillLockedAsync(documentId, ct).ConfigureAwait(false))
            {
                // Writing the ledger unlocked could overwrite what another relocation of this document now records.
                summary.AddPending(LockLost(documentId, "before its settled ledger was recorded"));
                summary.Ledger = ledger;
                return summary;
            }

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
            owed.Add(sourceDetail is not null && sourceDetail.StartsWith(SourceChangedAfterMovePrefix, StringComparison.Ordinal)
                ? $"{sourceDetail} ({entry.SourceDrive}/{entry.SourceItem})"
                : $"source-pending: {entry.SourceDrive}/{entry.SourceItem}" + (sourceDetail is null ? string.Empty : $" ({sourceDetail})"));
        }

        var required = entry.Source == LedgerSourceState.Removed ? LedgerIndexScope.All : LedgerIndexScope.Own;
        if (entry.Indexed < required)
        {
            owed.Add($"index-pending: the old item {entry.SourceItem}" + (indexDetail is null ? string.Empty : $" ({indexDetail})"));
        }

        return owed;
    }

    /// <summary>The report code of a source edited after its move (round 45 item 4).</summary>
    internal const string SourceChangedAfterMovePrefix = "source-changed-after-move";

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
                await ReKeyAttachmentAsync(attachment.Id, entry, currentDrive, currentItem, $"document {documentId}", ct).ConfigureAwait(false);
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

    private async Task ReKeyAttachmentAsync(
        Guid attachmentId, RelocationLedgerEntry entry, string currentDrive, string currentItem, string why, CancellationToken ct)
    {
        await _dataverse.UpdateAsync(AttachmentEntity, attachmentId,
            new Dictionary<string, object> { [DriveColumn] = currentDrive, [ItemColumn] = currentItem }, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] re-keyed: communication attachment {Attachment} {OldDrive}/{OldItem} -> {NewDrive}/{NewItem} ({Why}).",
            attachmentId, entry.SourceDrive, entry.SourceItem, currentDrive, currentItem, why);
    }

    private sealed record SourceSettlement(LedgerSourceState State, string? Detail);

    /// <summary>
    /// The old item: gone → removed; referenced only by rows OUTSIDE → kept for those records (settled); referenced by
    /// rows that belong where this document's file now is → moved along / re-keyed; unreferenced → deleted only when it
    /// still matches the witness recorded when its copy was verified (round 45 item 4) — edited since → reported
    /// <see cref="SourceChangedAfterMovePrefix"/> and handed to the re-copy. Anything unknown keeps it pending.
    /// </summary>
    private async Task<SourceSettlement> SettleSourceAsync(
        Guid documentId, RelocationLedgerEntry entry, string currentDrive, string currentItem, RelocationPurpose purpose,
        SettleSummary summary, CancellationToken ct)
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

        // Round 45 item 3: a communication's own attachment record of this file is classified by the subtree of the record
        // its communication is regarding — the same split as the document rows above (round 37 item 2).
        foreach (var attachment in references.Attachments)
        {
            if (attachment.Communication is not { } communicationId || communicationId == Guid.Empty)
            {
                unknown.Add($"sprk_communicationattachment {attachment.Id} (it names no communication, so where it belongs cannot be derived)");
                continue;
            }

            var derivation = await _resolver.DeriveCommunicationContainersAsync(communicationId, ct).ConfigureAwait(false);
            if (!derivation.Decided)
            {
                unknown.Add($"sprk_communicationattachment {attachment.Id} (its communication's container cannot be derived: {derivation.Reason})");
                continue;
            }

            if (!derivation.Allows(currentDrive))
            {
                keptFor.Add($"sprk_communicationattachment:{attachment.Id}");
                continue;
            }

            // Inside: its communication belongs where this document's file now is — it names the current file from now on.
            try
            {
                await ReKeyAttachmentAsync(attachment.Id, entry, currentDrive, currentItem,
                    $"communication {communicationId} belongs with document {documentId}'s file", ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] communication attachment {Attachment} could not be re-keyed.", attachment.Id);
                unknown.Add($"sprk_communicationattachment {attachment.Id} belongs with this file but could not be re-keyed ({ex.GetType().Name})");
            }
        }

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

        // No row references it. Round 45 item 4: delete it only when it still matches the witness recorded when its copy
        // was verified — never compared with the document's CURRENT file, which its users may have edited since.
        switch (await CompareWithWitnessAsync(entry, sourceFacts, ct).ConfigureAwait(false))
        {
            case WitnessComparison.NoWitness:
                return new SourceSettlement(LedgerSourceState.Pending,
                    "source-unverifiable: this ledger entry carries no witness recorded at verification, so the source is "
                    + "neither deleted nor copied by it (only a hand-written entry lacks one)");
            case WitnessComparison.Unknown:
                return new SourceSettlement(LedgerSourceState.Pending,
                    "it could not be compared with the witness recorded at verification; a repeat call retries");
            case WitnessComparison.Changed:
                summary.Changed.Add(new ChangedSource(entry, sourceFacts));
                summary.ChangedAfterMove.Add(new SourceChangedAfterMove(documentId, entry.SourceDrive, entry.SourceItem, 0, null));
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] source-changed-after-move: {SourceDrive}/{SourceItem} (document {DocumentId}) no longer "
                    + "matches the witness recorded at verification; it is not deleted.", entry.SourceDrive, entry.SourceItem, documentId);
                return new SourceSettlement(LedgerSourceState.Pending,
                    $"{SourceChangedAfterMovePrefix}: the source was edited after the move (it no longer matches the witness "
                    + "recorded when its copy was verified), so it is not deleted as it stands");
        }

        // Round 54 item 2: the source delete is destructive — only under the lock, confirmed as this call's now (a
        // move-along above may have taken long).
        if (!await StillLockedAsync(documentId, ct).ConfigureAwait(false))
        {
            return new SourceSettlement(LedgerSourceState.Pending, LockLost(documentId, "before the source delete; the source is kept"));
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

    internal enum WitnessComparison
    {
        Matches,
        Changed,
        Unknown,
        NoWitness,
    }

    /// <summary>
    /// The source now against its witness: the same size AND — when both carry one — the same <c>quickXorHash</c>;
    /// otherwise the same current version id (SharePoint mints a new version for every content change). A source whose
    /// versions cannot be listed when they are needed is <see cref="WitnessComparison.Unknown"/> (retried, never deleted).
    /// </summary>
    private async Task<WitnessComparison> CompareWithWitnessAsync(RelocationLedgerEntry entry, SpeItemCreator now, CancellationToken ct)
    {
        if (entry.Witness is not { IsProvable: true })
        {
            return WitnessComparison.NoWitness;
        }

        var witness = entry.Witness;

        string? currentVersion = null;
        if (string.IsNullOrWhiteSpace(witness.QuickXorHash) || string.IsNullOrWhiteSpace(now.QuickXorHash))
        {
            try
            {
                var versions = await _spe.ListFileVersionsAsync(entry.SourceDrive, entry.SourceItem, ct).ConfigureAwait(false);
                if (versions is null)
                {
                    return WitnessComparison.Unknown;
                }

                currentVersion = OldestFirst(versions).LastOrDefault()?.Id;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the versions of {Drive}/{Item} could not be listed.", entry.SourceDrive, entry.SourceItem);
                return WitnessComparison.Unknown;
            }
        }

        return Compare(witness, now, currentVersion);
    }

    /// <summary>The witness rule itself (see <see cref="CompareWithWitnessAsync"/>).</summary>
    internal static WitnessComparison Compare(RelocationWitness? witness, SpeItemCreator? now, string? currentVersion)
    {
        if (witness is not { IsProvable: true })
        {
            return WitnessComparison.NoWitness;
        }

        if (now?.Size is not { } size || witness.Size != size)
        {
            return WitnessComparison.Changed;
        }

        if (!string.IsNullOrWhiteSpace(witness.QuickXorHash) && !string.IsNullOrWhiteSpace(now.QuickXorHash))
        {
            return string.Equals(witness.QuickXorHash, now.QuickXorHash, StringComparison.Ordinal)
                ? WitnessComparison.Matches
                : WitnessComparison.Changed;
        }

        if (string.IsNullOrWhiteSpace(witness.Version) || string.IsNullOrWhiteSpace(currentVersion))
        {
            return string.IsNullOrWhiteSpace(witness.Version) ? WitnessComparison.Changed : WitnessComparison.Unknown;
        }

        return string.Equals(witness.Version, currentVersion, StringComparison.Ordinal)
            ? WitnessComparison.Matches
            : WitnessComparison.Changed;
    }

    private sealed record AttachmentReference(Guid Id, Guid? Communication);

    private sealed record SourceReferences(IReadOnlyList<Guid> Documents, IReadOnlyList<AttachmentReference> Attachments);

    /// <summary>
    /// The OTHER rows that still name the old item: <c>sprk_document</c> rows (in a degraded environment — the unique key
    /// <c>sprk_graphitemid_uk</c> forbids two while it is Active), and <c>sprk_communicationattachment</c> rows not linked
    /// to this document (a communication's own attachment record), each with its communication. App-only (a row the
    /// operator cannot see still depends on the file). <see langword="null"/> = could not be read, or more than
    /// <see cref="MaxReferencesPerSource"/>.
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
                ColumnSet = new ColumnSet(DriveColumn, AttachmentDocumentColumn, AttachmentCommunicationColumn),
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
                    .Select(a => new AttachmentReference(a.Id, a.GetAttributeValue<EntityReference>(AttachmentCommunicationColumn)?.Id))
                    .ToList());
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

            // Task 171 round 74 (K1): the other row's item must be the one the BFF bound to IT — a move-along re-points the
            // row and writes a matching copy, so moving a forged pointer along would launder it. Same rule as the main path.
            if (_resolver.ItemBindingRefusal(row, entry.SourceItem) is { } bindingRefusal)
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] Document {Other} is NOT moved along: {Reason}. Nothing is re-pointed.", other, bindingRefusal);
                return DocumentRelocationOutcome.Of(other, RelocationState.SourceUnverified, entry.SourceDrive, entry.SourceItem,
                    targetDrive, null, bindingRefusal + " — an administrator must repair it; it is not moved");
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

            var history = await ReadHistoryAsync(entry.SourceDrive, entry.SourceItem, VersionRecordOf(row), ct).ConfigureAwait(false);
            if (history.Failure is not null)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive,
                    null, $"its version history could not be copied: {history.Failure}");
            }

            var moved = await MoveAsync(other, row, entry.SourceDrive, entry.SourceItem, targetDrive,
                new ReplayPlan(history.Steps, facts, history.Witness, facts.Name ?? row.GetAttributeValue<string>(FileNameColumn)),
                ledger, LedgerSourceState.Delegated, ct).ConfigureAwait(false);
            if (moved.Failure is not null)
            {
                return moved.Failure;
            }

            var rowAfter = await ReadRowAsync(other, ct).ConfigureAwait(false) ?? row;
            var settled = await SettleOrReportAsync(other, rowAfter, targetDrive, moved.CopyId!,
                RelocationLedger.Parse(rowAfter.GetAttributeValue<string>(RelocationLedgerColumn)), purpose, apply: true, ct)
                .ConfigureAwait(false);
            settled.Note(moved.Truncated);
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
            DocumentEntity, documentId,
            [DriveColumn, ItemColumn, Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn, FileNameColumn, SearchIndexNameColumn,
             RelocationLedgerColumn, RelocatedVersionHistory.Column], ct)
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

        /// <summary>
        /// The source as its copy was verified (round 45 item 4): the ONLY thing a later call deletes it against. An entry
        /// without one (only a hand-written ledger lacks it) never deletes or copies its source.
        /// </summary>
        public RelocationWitness? Witness { get; init; }

        [JsonIgnore]
        public bool IsComplete
            => !RekeyPending
               && Source is LedgerSourceState.Removed or LedgerSourceState.Delegated
               && Indexed >= (Source == LedgerSourceState.Removed ? LedgerIndexScope.All : LedgerIndexScope.Own);

        [JsonIgnore]
        public bool IsSettled
            => IsComplete || (!RekeyPending && Source == LedgerSourceState.KeptForOtherRecords && Indexed >= LedgerIndexScope.Own);
    }

    /// <summary>
    /// The witness of a source (owner round 45 item 4): its size, <c>quickXorHash</c> and current version id when its copy
    /// was verified, and the time of that content (<see cref="Modified"/>, round 54 item 3: what the versions an edit adds
    /// later are ordered against when their ids are not numbers). Provable = a size and at least one of the hash or the version.
    /// </summary>
    internal sealed record RelocationWitness(long? Size, string? QuickXorHash, string? Version, DateTimeOffset? Modified = null)
    {
        [JsonIgnore]
        public bool IsProvable
            => Size is not null && (!string.IsNullOrWhiteSpace(QuickXorHash) || !string.IsNullOrWhiteSpace(Version));
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

    /// <summary>A source a settle found edited after its move — handed to the re-copy.</summary>
    internal sealed record ChangedSource(RelocationLedgerEntry Entry, SpeItemCreator Facts);

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

        /// <summary>Sources edited after their move, found by this settle (the re-copy's input).</summary>
        public List<ChangedSource> Changed { get; } = [];

        /// <summary>The stated report of sources edited after their move (round 45 item 4: reported with the row id).</summary>
        public List<SourceChangedAfterMove> ChangedAfterMove { get; } = [];

        /// <summary>Histories the target's version limit truncated (round 45 item 1: stated, never silent).</summary>
        public List<VersionsTruncated> Truncated { get; } = [];

        public string? PendingSourceDrive { get; private set; }
        public string? PendingSourceItem { get; private set; }

        public void AddPending(string reason) => Pending.Add(reason);

        /// <summary>Records what a move stated (a truncated history).</summary>
        public void Note(VersionsTruncated? truncated)
        {
            if (truncated is not null)
            {
                Truncated.Add(truncated);
            }
        }

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

    /// <summary>Moved: copied with its history, verified, re-pointed; the source deleted, the references re-keyed, the index re-keyed.</summary>
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

/// <summary>
/// A source edited after its move (owner round 45 item 4: <c>source-changed-after-move</c>, reported with the row id). The
/// relocator closes it itself — re-copy, verify, re-point — and <see cref="NewItem"/> names the copy that carries the edits
/// (null while it is still owed: then the outcome's <see cref="DocumentRelocationOutcome.Pending"/> says why).
/// </summary>
/// <param name="DocumentId">The document whose old file was edited.</param>
/// <param name="SourceDrive">The edited source's drive.</param>
/// <param name="SourceItem">The edited source.</param>
/// <param name="CarriedVersions">How many of the source's versions written after the move the re-copy carried.</param>
/// <param name="NewItem">The document's file after the re-copy.</param>
/// <param name="EditIsCurrent">The last carried edit became the document's current content — only when its author may
/// write the document now; otherwise the edits are in the history and the document's own content stays current.</param>
public sealed record SourceChangedAfterMove(
    Guid DocumentId, string SourceDrive, string SourceItem, int CarriedVersions, string? NewItem, bool EditIsCurrent = false);

/// <summary>
/// A moved file whose history the target container could not hold in full (owner round 45 item 1: <c>versions-truncated</c>,
/// stated with counts, never silent): <see cref="KeptVersions"/> of <see cref="ReplayedVersions"/> survive (the oldest
/// dropped by the target's version limit); <see cref="UnrecordedAuthors"/> of the oldest kept versions show Graph's own
/// author and date (the record's column limit).
/// </summary>
public sealed record VersionsTruncated(Guid DocumentId, string Item, int ReplayedVersions, int KeptVersions, int UnrecordedAuthors);

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

    /// <summary>Sources edited after their move (round 45 item 4) — stated; closed by the relocator's re-copy.</summary>
    public IReadOnlyList<SourceChangedAfterMove> SourceChangedAfterMove { get; init; } = [];

    /// <summary>Histories the target's version limit truncated (round 45 item 1) — stated, complete.</summary>
    public IReadOnlyList<VersionsTruncated> VersionsTruncated { get; init; } = [];

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
            SourceChangedAfterMove = SourceChangedAfterMove.Concat(summary.ChangedAfterMove).ToList(),
            VersionsTruncated = VersionsTruncated.Concat(summary.Truncated).ToList(),
        };
}

/// <summary>
/// A batch's per-file outcomes, counts by state, the INCOMPLETE ones (anything not settled: a planned, failed or pending
/// move, an undecidable container, a missing or unverified file), every source kept for another record, every source
/// edited after its move and every truncated history.
/// </summary>
public sealed record DocumentRelocationBatchResult(
    IReadOnlyList<DocumentRelocationOutcome> Outcomes,
    IReadOnlyDictionary<RelocationState, int> Counts,
    IReadOnlyList<DocumentRelocationOutcome> Incomplete,
    IReadOnlyList<KeptSource> SourceKeptForOtherRecords)
{
    /// <summary>Every file is settled.</summary>
    public bool Complete => Incomplete.Count == 0;

    /// <summary>Sources edited after their move (round 45 item 4), each with its document id and, once closed, its new item.</summary>
    public IReadOnlyList<SourceChangedAfterMove> SourceChangedAfterMove { get; init; } = [];

    /// <summary>Moved files whose history the target container truncated (round 45 item 1).</summary>
    public IReadOnlyList<VersionsTruncated> VersionsTruncated { get; init; } = [];

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
        return new DocumentRelocationBatchResult(outcomes, counts, incomplete, kept)
        {
            SourceChangedAfterMove = outcomes
                .SelectMany(o => o.SourceChangedAfterMove.Concat(o.MovedAlong.SelectMany(m => m.SourceChangedAfterMove))).ToList(),
            VersionsTruncated = outcomes
                .SelectMany(o => o.VersionsTruncated.Concat(o.MovedAlong.SelectMany(m => m.VersionsTruncated))).ToList(),
        };
    }

    /// <summary>
    /// Settled: nothing to move or owe (<see cref="RelocationState.NoFile"/>, <see cref="RelocationState.InPlace"/>), or
    /// moved with nothing owed (<see cref="RelocationState.Relocated"/>, and
    /// <see cref="RelocationState.RelocatedSourceKeptForOtherRecords"/> — the kept source is another record's file, owner
    /// round 37 item 2 — for BOTH purposes). A row that still owes anything is never settled. A truncated history and a
    /// source edited after its move that the re-copy closed are STATED outcomes, not owed ones.
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

    /// <summary>The file is an app-only upload and who it was made for could not be read (cache fault) — retryable.</summary>
    UploaderUnverifiable,
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
