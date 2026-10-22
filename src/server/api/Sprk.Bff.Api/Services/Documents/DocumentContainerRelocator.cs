using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
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
/// one), re-point through the SAME pointer-attach path, then settle what the move leaves behind (below). One mechanism,
/// three purposes (<see cref="RelocationPurpose"/>; round 26 item 3, #2159): task 166's legacy migration
/// (<see cref="DocumentContainerMigrationJob"/>), the Make Secure transition (task 150's provisioning lane, and the
/// <c>SecureChildReconciliationJob</c> backstop for the moves it left unmade), and the RE-FILE (#2159): a document whose
/// filing changed — through the BFF or outside it (a model-driven form, an import) — has its file moved into its new
/// derived container by the <c>SecureChildReconciliationJob</c>'s recent-changes cadence (every 2 minutes, writes on
/// unless its emergency stop is set). A re-file moves only the row's BFF-bound item, and an unfiled document whose file
/// is in no business unit's container and no declared home (#2151) is left where it is
/// (<see cref="RelocationState.UnfiledHomeUnknown"/>). Until the pass reaches it, a re-filed document is refused under
/// the strict rule (up to about two minutes plus the run). <b>The widening guard holds for every purpose</b> (#2159): a
/// row owned by the Secure Record owner team whose derivation is not a secure record's own container never has its file
/// moved — not by a re-file, not by the Make Secure backstop, not by the legacy migration, and not moved along by another
/// row's settle (<see cref="RelocationState.HeldIsolated"/>) — nor, when its file is in ANOTHER secure record's own
/// container, into this one's (S1 to S2 needs F3: only the release a BFF re-file with F3 records, <see cref="RecordReleaseAsync"/>,
/// lets that one move through, #2159 slice 2). Whether the row is isolated (owned in the Secure Record business unit)
/// is read by the relocator itself, under the document's lock and again just before the re-point, and an unreadable
/// answer moves nothing. A checked-out document is never moved (<see cref="RelocationState.CheckedOut"/>).</item>
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

    /// <summary>
    /// #2159 slice 2c: the AGE bound of an F3 release (<see cref="RelocationRelease"/>) — a backstop only. A release is valid
    /// only while the row's file is still the item it names AND the row still derives to its target; any locked pass that
    /// finds either changed drops it (<see cref="DropInvalidReleaseAsync"/>), so an edit that moves the document away and
    /// back never reuses it. Long enough that a long checkout does not strand the file; a document still held when its
    /// release expires is reported (<see cref="DocumentRelocationOutcome.ReleaseExpired"/>).
    /// </summary>
    internal static readonly TimeSpan ReleaseLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// #2159 slice 2: how long recording a release waits for the document's relocation lock (another relocation of it is
    /// running) before it gives up — the re-file's own response must not wait on a move.
    /// </summary>
    internal static readonly TimeSpan ReleaseLockWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// #2159 slice 4: the TOTAL time recording the releases of one re-file's descendant documents
    /// (<see cref="RecordReleasesAsync(IReadOnlyList{Guid}, SecureExitPermit, CancellationToken)"/>) waits for their locks.
    /// Each still gets one lock attempt once it is spent, so only a document whose lock is held at that moment goes without.
    /// </summary>
    internal static readonly TimeSpan DescendantReleaseLockBudget = TimeSpan.FromSeconds(5);

    /// <summary>#2159 B5: the file (or one of its versions) is larger than a single-request copy — permanent until #2176 (an upload-session copy).</summary>
    internal const string TerminalTooLarge = "too-large";

    /// <summary>#2159 B5: the row's ledger already owes <see cref="MaxLedgerEntries"/> moves — permanent until they settle.</summary>
    internal const string TerminalLedgerFull = "ledger-full";

    /// <summary>
    /// #2159 B5: the row's ledger has been unreadable for the job's limit of consecutive runs — permanent until an
    /// administrator repairs it. Set by the job (<see cref="SecureChildReconciliationJob"/>), never by a single call.
    /// </summary>
    internal const string TerminalLedgerUnreadable = "ledger-unreadable";

    /// <summary>The prefix of the pending line an unreadable ledger reports (<see cref="DocumentRelocationOutcome.Pending"/>).</summary>
    internal const string LedgerUnreadablePendingPrefix = "ledger-unreadable:";

    /// <summary>How often <see cref="RecordReleaseAsync"/> asks for the lock again within <see cref="ReleaseLockWait"/>.</summary>
    internal static readonly TimeSpan ReleaseLockRetryInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// #2159 F2 fix round: how long ONE delivery of a queued release retry (<see cref="RelocationReleaseRetryJobHandler"/>)
    /// waits for the document's relocation lock — a relocation replaying a long history holds it for minutes. Within the
    /// queue's message lock (5 minutes, auto-renewed to 10); with the broker's redeliveries it covers about ten minutes.
    /// </summary>
    internal static readonly TimeSpan ReleaseRetryLockWait = TimeSpan.FromMinutes(2);

    /// <summary>How often a queued release retry asks for the lock again within <see cref="ReleaseRetryLockWait"/>.</summary>
    internal static readonly TimeSpan ReleaseRetryLockRetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The one recovery for a document whose F3 release could not be recorded, nor its retry completed: a re-file to the
    /// SAME parent changes nothing (<see cref="RecordOwnerResolution.ParentsUnchanged"/>) and carries no F3 permission, so
    /// only a new re-file that leaves the secure record holding the file, with F3, can release it.
    /// </summary>
    internal const string UnreleasedRecovery =
        "file the document back under the secure record whose container holds its file, then re-file it with F3";

    /// <summary>The most referencing rows one source check reads; more is undecidable (pending), never "none".</summary>
    internal const int MaxReferencesPerSource = 50;

    private const string DocumentEntity = "sprk_document";
    private const string OwningTeamColumn = "owningteam";
    private const string OwningBusinessUnitColumn = "owningbusinessunit";

    /// <summary>#2159 B4: who holds the document checked out (<c>DocumentCheckoutService</c>; cleared at check-in).</summary>
    internal const string CheckedOutByColumn = "sprk_checkedoutby";

    /// <summary>#2159 B4: when it was checked out (cleared at check-in, with <see cref="CheckedOutByColumn"/>).</summary>
    internal const string CheckedOutDateColumn = "sprk_checkedoutdate";
    private const string DocumentIdColumn = "sprk_documentid";
    internal const string DriveColumn = "sprk_graphdriveid";
    internal const string ItemColumn = "sprk_graphitemid";
    private const string FilePathColumn = "sprk_filepath";
    private const string HasFileColumn = "sprk_hasfile";
    private const string FileNameColumn = "sprk_filename";
    private const string CreatedByColumn = "createdby";
    private const string SearchIndexNameColumn = "sprk_searchindexname";
    private const string ParentDocumentColumn = "sprk_parentdocument";
    private const string ParentItemColumn = "sprk_parentgraphitemid";
    internal const string AttachmentEntity = "sprk_communicationattachment";
    internal const string AttachmentDocumentColumn = "sprk_document";
    internal const string AttachmentCommunicationColumn = "sprk_communication";

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
    private readonly IConfiguration _configuration;

    /// <summary>Who an APP-ONLY upload was made for (task 171 attach fix); without it an app-uploaded file is refused.</summary>
    private readonly UploadAttribution? _uploadAttribution;

    /// <summary>
    /// Where the ADR-004 job queue is resolved from, on first use (<see cref="QueueReleaseRetryAsync"/>) — never at
    /// construction: the attach route constructs this type on every client upload and must not fail where Service Bus is
    /// not configured (the <c>CoreAncestorRestampQueue</c> idiom). <see langword="null"/> = no retry can be queued.
    /// </summary>
    private readonly IServiceProvider? _services;

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
    /// <param name="configuration">
    /// The host configuration: which business unit and owner team are the Secure Record ones
    /// (<see cref="SecureRecordOwnerTeam"/>) — the isolation the widening guard reads (#2159).
    /// </param>
    /// <param name="timeProvider">The clock of the ledger and of the lock's heartbeat.</param>
    /// <param name="uploadAttribution">Who an app-only upload was made for (the pointer attach).</param>
    /// <param name="services">
    /// Resolves the job queue a release that could not be recorded is retried through
    /// (<see cref="RelocationReleaseRetryJobHandler"/>), lazily; <see langword="null"/> = none is queued.
    /// </param>
    public DocumentContainerRelocator(
        RecordContainerResolver resolver,
        IGenericEntityService dataverse,
        SpeFileStore spe,
        IRelocatedFileIndexing indexing,
        IIdempotencyService locks,
        IAccessDataSource access,
        ILogger<DocumentContainerRelocator> logger,
        IConfiguration configuration,
        TimeProvider? timeProvider = null,
        UploadAttribution? uploadAttribution = null,
        IServiceProvider? services = null)
    {
        _uploadAttribution = uploadAttribution;
        _services = services;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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
        Guid documentId, string? callerObjectId, string? driveId, string? itemId, CancellationToken ct = default,
        string? callerTenantId = null)
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
        var consumeBinding = false;
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
            // The ONLY admission for a BFF-uploaded item is an ITEM binding naming the caller, in the caller's tenant
            // (verifier F1, 2026-10-07: a path / name binding could be matched by another user's file at that path).
            if (_uploadAttribution is null || string.IsNullOrWhiteSpace(callerTenantId))
            {
                return NotTheUploader(documentId, drive, item, caller);
            }

            UploadAttribution.MatchOutcome match;
            try
            {
                match = await _uploadAttribution.MatchAsync(callerTenantId, caller, drive, item, ct).ConfigureAwait(false);
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

            consumeBinding = true;
        }
        else
        {
            // Absent, or uploaded by another application.
            return NotTheUploader(documentId, drive, item, caller);
        }

        await WritePointerAsync(documentId, drive, item, facts.WebUrl, alsoWrite: null, ct).ConfigureAwait(false);
        if (consumeBinding)
        {
            // After the pointer is written: the binding has served its one purpose. Consuming it opens no other way of
            // attaching — a BFF-uploaded item has no admission but its item binding.
            await _uploadAttribution!.ConsumeAsync(callerTenantId!, item, ct).ConfigureAwait(false);
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
    /// <see cref="RelocationPurpose.LegacyMigration"/> moves only a file that is verifiably the row's own — the
    /// row's BFF-bound item that the BFF identity uploaded (#2159 B3), or
    /// <see cref="RecordContainerResolver.IsRelocationSourceVerifiedAsync"/>: a forged pointer is reported, never copied
    /// into the document's container. <see cref="RelocationPurpose.MakeSecure"/> — a record just made secure — moves the
    /// file the document shows into the record's OWN container whatever its uploader: that move only NARROWS who can
    /// reach the bytes (SPE permissions are additive-only, so leaving them in the shared container is the exposure),
    /// and it applies only when the derived container is that secure one.
    /// <see cref="RelocationPurpose.Refile"/> (#2159) — the document's filing changed — moves the file into its NEW
    /// derived container. Its source is verified ONLY as the row's BFF-bound item (<c>sprk_graphitemidbound</c>) that the
    /// BFF identity uploaded (B9; never the legacy check — a legacy misplaced row is the operator-gated migration's); and an
    /// unfiled document whose file is in no business unit's container (a record's own, or another owner's that is not a
    /// declared home, #2151) is reported <see cref="RelocationState.UnfiledHomeUnknown"/> and never pulled into a business
    /// unit's container. For EVERY purpose (#2159): a checked-out row is left alone (<see cref="RelocationState.CheckedOut"/>,
    /// B4); and the widening guard holds an isolated row (owned in the Secure Record business unit) whose move would leave
    /// its secure record — its derivation is not a secure record's own container, or its file is in ANOTHER secure record's
    /// own container (A1: S1 to S2 needs F3, honoured only through the exact, unexpired release a BFF re-file with F3
    /// recorded — <see cref="RecordReleaseAsync"/>, consumed by the re-point) — as <see cref="RelocationState.HeldIsolated"/>, nothing moved (isolation
    /// holds the row, owner round 24 item 2). The guard runs again under the confirmed lock just before the re-point (B8).
    /// What it cannot read moves nothing (<see cref="RelocationState.Failed"/>). A ledger entry an earlier move owes is
    /// settled first all the same: deleting a stale source and re-keying only narrow.
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
            var releaseCheck = new ReleaseCheck();
            var outcome = await RelocateCoreAsync(documentId, apply, purpose, expectedTargetContainer, ct, releaseCheck)
                .ConfigureAwait(false);
            return releaseCheck.Expired ? outcome with { ReleaseExpired = true } : outcome;
        }
        finally
        {
            await UnlockAsync(documentId).ConfigureAwait(false);
        }
    }

    /// <summary>What the locked release check of one call found (#2159 slice 2c).</summary>
    private sealed class ReleaseCheck
    {
        /// <summary>The row's F3 release had expired and was dropped.</summary>
        public bool Expired { get; set; }
    }

    private async Task<DocumentRelocationOutcome> RelocateCoreAsync(
        Guid documentId, bool apply, RelocationPurpose purpose, string? expectedTargetContainer, CancellationToken ct,
        ReleaseCheck? releaseCheck = null)
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

        // #2159 slice 2c: an F3 release is valid only while the row's file is its item AND the row derives to its target. A
        // locked pass that finds either changed (or the release expired) drops it — BEFORE the checkout check, so a document
        // moved away and back while checked out never reuses an old release for a move that had no F3.
        if (apply && RelocationLedger.Parse(row!.GetAttributeValue<string>(RelocationLedgerColumn))?.Release is { } release
            && await DropInvalidReleaseAsync(documentId, row!, release, sourceDrive, sourceItem, ct).ConfigureAwait(false)
                is ReleaseDrop.Expired
            && releaseCheck is not null)
        {
            releaseCheck.Expired = true;
        }

        // #2159 B4: a CHECKED-OUT document is left alone, whatever the purpose: its holder is editing THIS item, so a copy,
        // a re-point or a source delete (or a ledger re-copy, which re-points) would strand or lose those edits. Check-in
        // clears the checkout and modifies the row, so a later pass (the re-file window, the backstop's listing, the
        // migration) reaches it again.
        if (IsCheckedOut(row!))
        {
            return DocumentRelocationOutcome.Of(
                documentId, RelocationState.CheckedOut, sourceDrive, sourceItem, null, null,
                "it is checked out; nothing is moved, settled or re-pointed until it is checked in");
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
            if (recopy.CheckedOut is { } checkedOut)
            {
                // Checked out while the re-copy ran: nothing more is copied or re-pointed until it is checked in (B4).
                return checkedOut.With(prior);
            }

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

        // #2159: no move widens — never out of isolation (every purpose), never from an unknown home into a business
        // unit's container (a re-file). After the ledger settle above: a settle only narrows, and it is not a move.
        if (await WideningHoldAsync(documentId, derivation, purpose, sourceDrive, sourceItem, target, ct).ConfigureAwait(false)
            is { } held)
        {
            return held.With(prior);
        }

        var verified = await IsSourceVerifiedAsync(documentId, row!, sourceDrive, sourceItem, facts, derivation, purpose, ct)
            .ConfigureAwait(false);
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
                $"the file is {facts.Size} bytes, larger than a single-request copy ({MaxRelocatableBytes} bytes)").With(prior) with
            {
                TerminalCause = TerminalTooLarge,
            };
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
                $"its version history could not be copied: {history.Failure}").With(prior) with
            {
                TerminalCause = history.TooLarge ? TerminalTooLarge : null,
            };
        }

        var moved = await MoveAsync(documentId, row, sourceDrive, sourceItem, target,
            new ReplayPlan(history.Steps, facts, history.Witness, facts.Name ?? row.GetAttributeValue<string>(FileNameColumn)),
            prior.Ledger, LedgerSourceState.Pending, ct,
            recheck: token => RecheckBeforeRepointAsync(documentId, sourceDrive, sourceItem, target, token)).ConfigureAwait(false);
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

    /// <summary>Is <paramref name="item"/> the item the BFF bound to <paramref name="row"/> (its field-secured copy names it)?</summary>
    private static bool IsBoundItem(Entity row, string item)
        => Spaarke.Dataverse.DocumentPointerBinding.Compare(
               row.GetAttributeValue<string>(Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn), item)
           == Spaarke.Dataverse.DocumentPointerBinding.BindingState.Bound;

    /// <summary>#2159 B4: the row is checked out (either checkout column is set; check-in clears both).</summary>
    private static bool IsCheckedOut(Entity row)
        => row.GetAttributeValue<EntityReference>(CheckedOutByColumn) is { Id: var holder } && holder != Guid.Empty
           || row.Contains(CheckedOutDateColumn) && row[CheckedOutDateColumn] is not null;

    /// <summary>
    /// Is the source the row's OWN file, so that moving it cannot launder another item into this document's container?
    /// <list type="bullet">
    /// <item><see cref="RelocationPurpose.MakeSecure"/> into the record's secure container FROM A CONTAINER THAT IS NO SECURE
    /// RECORD'S OWN: always (a move that only narrows). From ANOTHER secure record's own container (S1 to S2, released by
    /// F3 — it does not narrow; #2159 fix round): only the row's BFF-bound item, or the legacy check. Otherwise the legacy
    /// check (<see cref="RecordContainerResolver.IsRelocationSourceVerifiedAsync"/>).</item>
    /// <item><see cref="RelocationPurpose.Refile"/> (#2159 B9): ONLY the row's BFF-bound item that the BFF identity uploaded
    /// — the binding alone is not proof (the bound copy records the item, not who placed its bytes). Never the legacy check:
    /// a legacy misplaced row is the operator-gated migration's (<c>DocumentContainerMigration:WritesEnabled</c>), never
    /// the two-minute pass's.</item>
    /// <item><see cref="RelocationPurpose.LegacyMigration"/> (#2159 B3): the same bound-and-BFF-placed item, or the legacy
    /// check — so the migration repairs a re-filed row whose file is a relocation copy (made by the BFF, never by the row's
    /// creator) when the re-file pass lost its carry on a restart.</item>
    /// </list>
    /// <paramref name="row"/> has passed <see cref="RecordContainerResolver.ItemBindingRefusal"/> (a DIFFERING copy never
    /// reaches here).
    /// </summary>
    private async Task<bool> IsSourceVerifiedAsync(
        Guid documentId, Entity row, string sourceDrive, string sourceItem, SpeItemCreator facts,
        DocumentContainerDerivation derivation, RelocationPurpose purpose, CancellationToken ct)
    {
        var bound = IsBoundItem(row, sourceItem);
        var placedByTheBff = bound && _resolver.IsUploadedByTheBffIdentity(facts);
        switch (purpose)
        {
            case RelocationPurpose.Refile:
                return placedByTheBff;
            case RelocationPurpose.MakeSecure when derivation.IsSecure:
                // Narrows only from a container that is no secure record's own. From another secure record's (S1 to S2,
                // which only an F3 release lets through) it does not narrow, so the source must be proven the row's own.
                if (await _resolver.ResolveOwningRecordAsync(sourceDrive, ct).ConfigureAwait(false) is null || bound)
                {
                    return true;
                }

                break;
            case RelocationPurpose.LegacyMigration when placedByTheBff:
                return true;
        }

        return await _resolver.IsRelocationSourceVerifiedAsync(documentId, sourceDrive, sourceItem, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// #2159 B8 — asked under the CONFIRMED lock immediately before a move re-points the row: the row's file must be
    /// unchanged and not checked out (<see cref="RecheckFileUnchangedAsync"/>), the row's derivation, read again, must
    /// still allow <paramref name="target"/>, and the widening guard must still let the move through (the filing or the
    /// owner may have changed while the bytes were copied). Otherwise the move stops there: the caller removes its copy
    /// and writes nothing (<see cref="RelocationState.CheckedOut"/> or <see cref="RelocationState.Failed"/>; a later call
    /// decides again). A read that faults stops it the same way.
    /// </summary>
    private async Task<DocumentRelocationOutcome?> RecheckBeforeRepointAsync(
        Guid documentId, string sourceDrive, string sourceItem, string target, CancellationToken ct)
    {
        if (await RecheckFileUnchangedAsync(documentId, sourceDrive, sourceItem, target, ct).ConfigureAwait(false) is { } changed)
        {
            return changed;
        }

        var now = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (!now.Decided || !now.Allows(target))
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"its derived container changed while the file was copied (now: {now.Reason}); the copy was removed and "
                + "nothing was written");
        }

        if (await IsolationHoldAsync(documentId, now, sourceDrive, sourceItem, target, ct).ConfigureAwait(false) is { } held)
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"the widening guard stopped it while the file was copied ({held.Detail}); the copy was removed and nothing "
                + "was written");
        }

        return null;
    }

    /// <summary>
    /// #2159 fix round (B4 during the copy) — asked under the confirmed lock immediately before ANY re-point (a move, a
    /// move-along, and the re-copy of an edited source): the row, read again, must still name
    /// <paramref name="sourceDrive"/>/<paramref name="sourceItem"/> and must not be checked out. A document checked out
    /// while its versions were copied is <see cref="RelocationState.CheckedOut"/> — its holder's Office session edits the
    /// item the move would re-point away from and the settle would delete; a row whose file changed is
    /// <see cref="RelocationState.Failed"/>. Either way the caller removes the copy and writes nothing.
    /// </summary>
    private async Task<DocumentRelocationOutcome?> RecheckFileUnchangedAsync(
        Guid documentId, string sourceDrive, string sourceItem, string target, CancellationToken ct)
    {
        var row = await ReadRowAsync(documentId, ct).ConfigureAwait(false);
        if (row is null)
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                "the row could not be read again before the re-point; the copy was removed and nothing was written");
        }

        if (IsCheckedOut(row))
        {
            return DocumentRelocationOutcome.Of(documentId, RelocationState.CheckedOut, sourceDrive, sourceItem, target, null,
                "it was checked out while the file was copied; the copy was removed and nothing was written — it is moved "
                + "once it is checked in");
        }

        var drive = row.GetAttributeValue<string>(DriveColumn);
        var item = row.GetAttributeValue<string>(ItemColumn);
        return SameItem(drive, item, sourceDrive, sourceItem)
            ? null
            : DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                $"its file changed while the file was copied (now {drive?.Trim()}/{item?.Trim()}); the copy was removed and "
                + "nothing was written");
    }

    /// <summary>
    /// #2159 — why a move must NOT run, or <see langword="null"/> when it may. Fail closed: anything that cannot be read
    /// moves nothing.
    /// <list type="number">
    /// <item><b>The widening guard</b> — every purpose (<see cref="IsolationHoldAsync"/>).</item>
    /// <item><b>An unfiled document's unknown home</b> (#2151) — <see cref="RelocationPurpose.Refile"/>. Unfiled, and its
    /// file is in no business unit's container (a declared home would have derived in place) →
    /// <see cref="RelocationState.UnfiledHomeUnknown"/>: where it belongs (a record's own container after a re-scope,
    /// another owner's) cannot be told, and pulling it into a business unit's container could widen a narrower audience.
    /// (The legacy migration exists to pull such files into their business unit's container; it keeps doing so.)</item>
    /// </list>
    /// </summary>
    private async Task<DocumentRelocationOutcome?> WideningHoldAsync(
        Guid documentId, DocumentContainerDerivation derivation, RelocationPurpose purpose, string sourceDrive, string sourceItem,
        string target, CancellationToken ct)
    {
        if (await IsolationHoldAsync(documentId, derivation, sourceDrive, sourceItem, target, ct).ConfigureAwait(false)
            is { } held)
        {
            return held;
        }

        if (purpose == RelocationPurpose.Refile && derivation.Unfiled)
        {
            bool inABusinessUnitContainer;
            try
            {
                inABusinessUnitContainer = await _resolver.IsBusinessUnitContainerAsync(sourceDrive, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT re-filed: whether {SourceDrive} is a business unit's "
                    + "container could not be read. Nothing is moved.", documentId, sourceDrive);
                return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                    "whether its file is in a business unit's container could not be read; nothing is moved and a later pass retries");
            }

            if (!inABusinessUnitContainer)
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT re-filed: it is unfiled and its file is in {SourceDrive}, "
                    + "which is no business unit's container and no declared home; it is not pulled into {Target}.",
                    documentId, sourceDrive, target);
                return DocumentRelocationOutcome.Of(documentId, RelocationState.UnfiledHomeUnknown, sourceDrive, sourceItem, target,
                    null, "unfiled, and its file is in no business unit's container and no declared home: where it belongs cannot "
                          + "be told, so it is left where it is");
            }
        }

        return null;
    }

    /// <summary>
    /// #2159 — THE widening guard, for every purpose and for a row moved along by another's settle. An ISOLATED row
    /// (<see cref="IsIsolatedAsync"/>) is <see cref="RelocationState.HeldIsolated"/>, nothing moved, when the move would
    /// leave its secure record:
    /// <list type="bullet">
    /// <item>its derivation is not a secure record's own container — a filing edited outside the BFF (a model-driven
    /// form) leaves the row isolated (owner round 24 item 2) while it derives to a business unit's container;</item>
    /// <item>(A1) its derivation IS a secure record's container but its file is in ANOTHER secure record's own container:
    /// leaving secure root S1 for S2 is an un-secure of S1's content, which needs F3 (owner round 10 item 7).</item>
    /// </list>
    /// Its bytes stay where they are until an F3 act releases the row. Never held: into a secure container from one that
    /// is no secure record's (a business unit's, the archive) — that narrows — and any row that is not isolated. Asked
    /// under the row's lock, after its derivation, and again just before the re-point (<see cref="RecheckBeforeRepointAsync"/>).
    /// What cannot be read (the isolation, the source container's secure owner) is <see cref="RelocationState.Failed"/>:
    /// nothing moves and a later call retries.
    /// </summary>
    private async Task<DocumentRelocationOutcome?> IsolationHoldAsync(
        Guid documentId, DocumentContainerDerivation derivation, string sourceDrive, string sourceItem, string target,
        CancellationToken ct)
    {
        bool isolated;
        try
        {
            if (derivation.IsSecure
                && await _resolver.ResolveOwningRecordAsync(sourceDrive, ct).ConfigureAwait(false) is null)
            {
                return null; // into the secure record's container from no secure record's: it narrows
            }

            isolated = await IsIsolatedAsync(documentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT moved: whether it is isolated, or whose secure container its "
                + "file is in, could not be read. Nothing is moved.", documentId);
            return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                "whether it is isolated (owned in the Secure Record business unit), or whose secure container its file is in, "
                + "could not be read; nothing is moved and a later call retries");
        }

        if (!isolated)
        {
            return null;
        }

        if (derivation.IsSecure)
        {
            // #2159 slice 2: the ONE way through — the F3 release a BFF re-file recorded for exactly this move (this item,
            // into this container, unexpired). Consumed by the re-point's own ledger write (MoveAsync), never here: a move
            // that stops later keeps it for the retry. Missing, unreadable, stale or for another move: held.
            RelocationRelease? release;
            bool releaseUnreadable;
            try
            {
                (release, releaseUnreadable) = await ReadReleaseAsync(documentId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT moved: its F3 release could not be read.", documentId);
                return DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, target, null,
                    "whether an F3 re-file released its move into this secure record's container could not be read; nothing is "
                    + "moved and a later call retries");
            }

            if (release is { } r && r.Releases(sourceDrive, sourceItem, target, _time.GetUtcNow()))
            {
                _logger.LogInformation(
                    "[DOCUMENT-RELOCATE] Document {DocumentId}: its move from {SourceDrive} into {Target} was released by the F3 "
                    + "re-file of {Caller} ({Basis}) at {At}.", documentId, sourceDrive, target, r.AuthorizedBy, r.Basis, r.At);
                return null;
            }

            var why = releaseUnreadable
                ? "its relocation ledger (where an F3 release is recorded) is unreadable"
                : release is null
                    ? "no F3 re-file released this move"
                    : "its F3 release is for another move, or has expired";
            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT moved: it is isolated and its file is in another secure record's "
                + "container {SourceDrive}; leaving it for {Target} ({Reason}) needs F3 ({Why}).", documentId, sourceDrive, target,
                derivation.Reason, why);
            return DocumentRelocationOutcome.Of(documentId, RelocationState.HeldIsolated, sourceDrive, sourceItem, target, null,
                "isolated, and its file is in another secure record's own container: leaving that secure record for this one is "
                + $"an un-secure and needs F3 (owner round 10 item 7) — {why}; its file is not moved");
        }

        _logger.LogWarning(
            "[DOCUMENT-RELOCATE] Document {DocumentId} is NOT moved: it is owned in the Secure Record business unit but derives "
            + "to {Target} ({Reason}); its file stays in {SourceDrive} until an F3 act releases it.",
            documentId, target, derivation.Reason, sourceDrive);
        return DocumentRelocationOutcome.Of(documentId, RelocationState.HeldIsolated, sourceDrive, sourceItem, target, null,
            "still owned in the Secure Record business unit while its filing points outside every secure record (an edit "
            + "outside the product); its file is not moved out of isolation until an F3 act releases it");
    }

    /// <summary>
    /// #2159 A2 — is the document ISOLATED NOW: owned in the Secure Record business unit — its owning team is ANY team in
    /// that business unit, or the row's own business unit is it? The same predicate as
    /// <c>SecureChildReconciler.ReownOrdinaryRowAsync</c> and <c>TodoAssigneeOwnerReconciliationJob</c>, never the one
    /// named owner team, so it holds once each secure family has its own owner team (task 178; #2162). No Secure Record
    /// business unit in this environment = no row is isolated. Two business units with the name (ambiguous), a row that
    /// cannot be read, or an owning team whose business unit cannot be read throw: the caller then moves nothing.
    /// </summary>
    private async Task<bool> IsIsolatedAsync(Guid documentId, CancellationToken ct)
    {
        var secure = await SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct)
            .ConfigureAwait(false);
        if (secure.Ambiguous)
        {
            throw new InvalidOperationException("more than one business unit carries the Secure Record name");
        }

        if (secure.Id is not { } secureBu)
        {
            return false;
        }

        var row = await _dataverse.RetrieveAsync(DocumentEntity, documentId, [OwningTeamColumn, OwningBusinessUnitColumn], ct)
                      .ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"sprk_document {documentId:D} could not be read");
        if (row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id == secureBu)
        {
            return true;
        }

        if (row.GetAttributeValue<EntityReference>(OwningTeamColumn) is not { Id: var teamId } || teamId == Guid.Empty)
        {
            return false; // user-owned outside the Secure Record business unit
        }

        var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
        var team = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        var teamBu = team?.GetAttributeValue<EntityReference>("businessunitid")?.Id;
        return teamBu is { } bu && bu != Guid.Empty
            ? bu == secureBu
            : throw new InvalidOperationException($"the business unit of sprk_document {documentId:D}'s owning team {teamId:D} could not be read");
    }

    /// <summary>
    /// #2159 slice 2 — records the F3 release of a re-filed document's file: the BFF re-file took the document out of
    /// secure record S1 with the caller's F3 permission (<paramref name="permit"/>) and filed it under secure record S2, so
    /// its file may follow it from S1's own container into S2's — the one move the widening guard otherwise holds (A1).
    /// </summary>
    /// <remarks>
    /// <para>The relocator stays the ONE writer of the ledger: the release is read-modify-written under the document's ADR-004
    /// relocation lock (asked for up to <see cref="ReleaseLockWait"/>; still held → nothing is written,
    /// <see cref="RelocationReleaseState.LockHeld"/>), and never outside it.</para>
    /// <para>What it names is read HERE, under the lock, never taken from the caller: the row's current file (the source), the
    /// container it derives to now (the target — a secure record's own), and the secure record whose own container holds
    /// the file, which must be one the re-file left (<see cref="SecureExitPermit.RootsLeft"/>); and the target's secure record
    /// must be one the re-file filed the row under (<see cref="SecureExitPermit.RootsAfter"/>, PR #2161 round 4). Anything
    /// else records nothing. An unreadable ledger is left as it is. Never throws for a read or write fault (<see cref="RelocationReleaseState.Failed"/>).</para>
    /// </remarks>
    public Task<RelocationReleaseOutcome> RecordReleaseAsync(Guid documentId, SecureExitPermit permit, CancellationToken ct = default)
        => RecordReleaseAsync(documentId, permit, ReleaseLockWait, ct);

    /// <summary>
    /// #2159 slice 4 — <see cref="RecordReleaseAsync(Guid, SecureExitPermit, CancellationToken)"/> for each of a re-file's
    /// DESCENDANT documents, in order, their lock waits sharing one <see cref="DescendantReleaseLockBudget"/>: each waits at
    /// most <see cref="ReleaseLockWait"/> and at most what is left; once it is spent, each still gets one lock attempt (no
    /// wait), so only a document whose lock is held at that moment is <see cref="RelocationReleaseState.LockHeld"/> — and,
    /// like every release not recorded inline, retried through the job queue (<see cref="RelocationReleaseOutcome.RetryQueued"/>).
    /// One outcome per id, in order. Never throws (cancellation aside).
    /// </summary>
    public Task<IReadOnlyList<(Guid DocumentId, RelocationReleaseOutcome Outcome)>> RecordReleasesAsync(
        IReadOnlyList<Guid> documentIds, SecureExitPermit permit, CancellationToken ct = default)
        => RecordReleasesAsync(documentIds, permit, DescendantReleaseLockBudget, ct);

    internal async Task<IReadOnlyList<(Guid DocumentId, RelocationReleaseOutcome Outcome)>> RecordReleasesAsync(
        IReadOnlyList<Guid> documentIds, SecureExitPermit permit, TimeSpan budget, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(permit);
        var deadline = _time.GetUtcNow() + budget;
        var outcomes = new List<(Guid, RelocationReleaseOutcome)>(documentIds.Count);
        foreach (var documentId in documentIds)
        {
            var left = deadline - _time.GetUtcNow();
            var wait = left <= TimeSpan.Zero ? TimeSpan.Zero : left < ReleaseLockWait ? left : ReleaseLockWait;
            outcomes.Add((documentId, await RecordReleaseAsync(documentId, permit, wait, ct).ConfigureAwait(false)));
        }

        return outcomes;
    }

    /// <summary>
    /// The inline release of a re-file: recorded under the lock now, or — when it could not be (the lock stayed held, a
    /// read or write failed) — retried DURABLY through the job queue (<see cref="QueueReleaseRetryAsync"/>), bound to the
    /// move this re-file permitted. A re-file to the same parent again changes nothing and carries no F3, so it is no retry.
    /// </summary>
    private async Task<RelocationReleaseOutcome> RecordReleaseAsync(
        Guid documentId, SecureExitPermit permit, TimeSpan lockWait, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(permit);
        var permittedAt = _time.GetUtcNow();
        var outcome = await RecordReleaseUnderLockAsync(documentId, permit, lockWait, ReleaseLockRetryInterval, binding: null, ct)
            .ConfigureAwait(false);
        return outcome.State is RelocationReleaseState.LockHeld or RelocationReleaseState.Failed
            ? await QueueReleaseRetryAsync(documentId, permit, permittedAt, outcome, ct).ConfigureAwait(false)
            : outcome;
    }

    /// <summary>
    /// #2159 fix round — the queued retry of a release the re-file could not record (<see cref="RelocationReleaseRetryJobHandler"/>):
    /// recorded under the document's lock exactly as the inline path records it, and only while the move it was bound to
    /// at the re-file still holds — the row's file is still in the source container, the row derives to the target, and the re-file's F3
    /// permission is at most <see cref="ReleaseLifetime"/> old (the release keeps the re-file's time, so it never outlives
    /// it). It never records a release the inline path would refuse. Never throws (cancellation aside).
    /// </summary>
    /// <param name="payload">What the queued retry carries.</param>
    /// <param name="lockWait">How long it waits for the document's lock (<see cref="ReleaseRetryLockWait"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    internal Task<RelocationReleaseOutcome> RecordQueuedReleaseAsync(
        RelocationReleaseRetryPayload payload, TimeSpan lockWait, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var permit = new SecureExitPermit(
            payload.RootsLeft, SecureRemovalDecision.Permitted(payload.Basis, payload.AuthorizedBy), payload.RootsAfter ?? []);
        return RecordReleaseUnderLockAsync(payload.DocumentId, permit, lockWait, ReleaseRetryLockRetryInterval,
            new ReleaseBinding(payload.SourceDrive, payload.TargetContainer, payload.PermittedAt), ct);
    }

    /// <summary>The move a queued release retry is bound to: what the re-file permitted, read right after it.</summary>
    private sealed record ReleaseBinding(string SourceDrive, string TargetContainer, DateTimeOffset PermittedAt);

    /// <summary>
    /// Queues the durable retry of a release the inline path did not record: bound to the move this re-file permitted —
    /// the container holding the row's file and the container it derives to NOW (read without the lock; the retry records nothing unless both
    /// still hold under it) — and carrying the permission's facts the release records (the roots left, who, on what basis,
    /// when). Not queued (no queue, the row or its derivation unreadable, the send failed): an Error naming the document
    /// only, and the file stays held. Never throws (cancellation aside).
    /// </summary>
    private async Task<RelocationReleaseOutcome> QueueReleaseRetryAsync(
        Guid documentId, SecureExitPermit permit, DateTimeOffset permittedAt, RelocationReleaseOutcome notRecorded,
        CancellationToken ct)
    {
        try
        {
            var jobs = _services?.GetService<JobSubmissionService>()
                       ?? throw new InvalidOperationException("no job queue is available");
            var row = await ReadRowAsync(documentId, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("the row could not be read");
            var drive = row.GetAttributeValue<string>(DriveColumn)?.Trim();
            var item = row.GetAttributeValue<string>(ItemColumn)?.Trim();
            if (string.IsNullOrWhiteSpace(drive) || string.IsNullOrWhiteSpace(item))
            {
                throw new InvalidOperationException("the row names no file");
            }

            var derivation = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
            if (!derivation.Decided || string.IsNullOrWhiteSpace(derivation.PrimaryContainer))
            {
                throw new InvalidOperationException("its container could not be derived");
            }

            await jobs.SubmitJobAsync(RelocationReleaseRetryJobHandler.NewJob(new RelocationReleaseRetryPayload(
                documentId, drive, derivation.PrimaryContainer.Trim(), permit.RootsLeft.ToList(),
                permit.Decision.CallerSystemUserId, permit.Decision.Basis, permittedAt, permit.RootsAfter.ToList())), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[DOCUMENT-RELOCATE] The F3 release of document {DocumentId}'s file was NOT recorded and its retry could not be "
                + "queued; its file stays held where it is.", documentId);
            return notRecorded with { Detail = $"{notRecorded.Detail}; its retry could not be queued — {UnreleasedRecovery}" };
        }

        _logger.LogWarning(
            "[DOCUMENT-RELOCATE] The F3 release of document {DocumentId}'s file was not recorded now ({State}); a retry is queued.",
            documentId, notRecorded.State);
        return notRecorded with { RetryQueued = true, Detail = $"{notRecorded.Detail}; a retry is queued" };
    }

    private async Task<RelocationReleaseOutcome> RecordReleaseUnderLockAsync(
        Guid documentId, SecureExitPermit permit, TimeSpan lockWait, TimeSpan retryInterval, ReleaseBinding? binding,
        CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + lockWait;
        while (!await TryLockAsync(documentId, ct).ConfigureAwait(false))
        {
            if (_time.GetUtcNow() >= deadline)
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] The F3 release of document {DocumentId}'s file was NOT recorded: another relocation of it "
                    + "held its lock for {Wait}. Its file stays held where it is; the re-file stands.", documentId, lockWait);
                return new RelocationReleaseOutcome(RelocationReleaseState.LockHeld,
                    $"another relocation of this document held its lock for {lockWait.TotalSeconds:0.#} s; no release was recorded");
            }

            await Task.Delay(retryInterval, _time, ct).ConfigureAwait(false);
        }

        try
        {
            return await RecordReleaseCoreAsync(documentId, permit, binding, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[DOCUMENT-RELOCATE] The F3 release of document {DocumentId}'s file could not be recorded.", documentId);
            return new RelocationReleaseOutcome(RelocationReleaseState.Failed,
                $"it could not be read or written ({ex.GetType().Name}); no release was recorded");
        }
        finally
        {
            await UnlockAsync(documentId).ConfigureAwait(false);
        }
    }

    /// <param name="documentId">The document.</param>
    /// <param name="permit">The re-file's F3 permission.</param>
    /// <param name="binding">
    /// A queued retry's binding (<see cref="RecordQueuedReleaseAsync"/>): the release is recorded only while the row's file
    /// is still in its source container and the row still derives to its target, with the re-file's own time, at most
    /// <see cref="ReleaseLifetime"/> ago. <see langword="null"/> for the inline path, which records what it reads now.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<RelocationReleaseOutcome> RecordReleaseCoreAsync(
        Guid documentId, SecureExitPermit permit, ReleaseBinding? binding, CancellationToken ct)
    {
        var row = await ReadRowAsync(documentId, ct).ConfigureAwait(false);
        var sourceDrive = row?.GetAttributeValue<string>(DriveColumn)?.Trim();
        var sourceItem = row?.GetAttributeValue<string>(ItemColumn)?.Trim();
        if (string.IsNullOrWhiteSpace(sourceDrive) || string.IsNullOrWhiteSpace(sourceItem))
        {
            return new RelocationReleaseOutcome(RelocationReleaseState.NotNeeded, "the row carries no file");
        }

        var derivation = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (!derivation.Decided)
        {
            return new RelocationReleaseOutcome(RelocationReleaseState.Failed,
                $"its container could not be derived ({derivation.Reason}); no release was recorded");
        }

        if (!derivation.IsSecure || derivation.Allows(sourceDrive) || string.IsNullOrWhiteSpace(derivation.PrimaryContainer))
        {
            return new RelocationReleaseOutcome(RelocationReleaseState.NotNeeded,
                derivation.Allows(sourceDrive)
                    ? "its file is already in its derived container"
                    : "it derives to no secure record's own container, so the widening guard asks no release");
        }

        var sourceRecord = await _resolver.ResolveOwningRecordAsync(sourceDrive, ct).ConfigureAwait(false);
        if (sourceRecord is null)
        {
            return new RelocationReleaseOutcome(RelocationReleaseState.NotNeeded,
                "its file is in no secure record's own container: the move into the secure record's container only narrows");
        }

        // A queued retry releases only the move the re-file permitted: out of the same SOURCE CONTAINER (whose secure record
        // the re-file left — checked below) into the same TARGET. A row moved or re-filed since is another move, which that
        // F3 act did not approve. It is NOT bound to the item (PR #2161 re-check): the relocator itself re-points the row
        // inside that container (an edited source closed in place) and keeps a recorded release across it (MoveCoreAsync
        // re-binds it), so an item binding lost the release for good to that same re-copy. The release names the item read
        // now, under the lock, so the widening guard's exact match still holds.
        if (binding is not null && !RecordContainerResolver.IsSameContainerId(binding.SourceDrive, sourceDrive))
        {
            return BindingMismatch(documentId,
                $"its file is no longer in {binding.SourceDrive}, the container the re-file released it from");
        }

        if (binding is not null && !RecordContainerResolver.IsSameContainerId(binding.TargetContainer, derivation.PrimaryContainer))
        {
            return BindingMismatch(documentId,
                $"it no longer derives to {binding.TargetContainer}, the container the re-file filed it under");
        }

        if (!permit.RootsLeft.Any(root =>
                string.Equals(root.EntityLogicalName, sourceRecord.EntityLogicalName, StringComparison.OrdinalIgnoreCase)
                && root.RecordId == sourceRecord.RecordId))
        {
            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] The F3 re-file of document {DocumentId} did not leave {Table} {RecordId}, whose own container "
                + "{SourceDrive} holds its file: no release is recorded; the file stays held.", documentId,
                sourceRecord.EntityLogicalName, sourceRecord.RecordId, sourceDrive);
            return new RelocationReleaseOutcome(RelocationReleaseState.NotCovered,
                $"its file is in the own container of {sourceRecord.EntityLogicalName} {sourceRecord.RecordId:D}, which this "
                + "re-file did not leave with F3; no release was recorded");
        }

        // PR #2161 round 4 (F1-b): the TARGET must be the filing the F3 decision approved, too — the own container of one of
        // the secure roots the row was filed under after the re-file (SecureExitPermit.RootsAfter, from the same F3 walk). The
        // target is read now, so another writer's move between the re-file and this release (the row filed under a third
        // secure record S3 directly in Dataverse) would otherwise take S1's file to S3's members on an F3 act that never saw
        // S3. "Belongs" is exact: the record that owns the derived container (ResolveOwningRecordAsync) is one of those roots;
        // a container no secure record owns, or owned by any other, is NotCovered and the file stays held. A target that is
        // NO secure record's own container needs no rule here: it was answered NotNeeded above (nothing is recorded, and the
        // widening guard holds an isolated file bound for an ordinary container whatever the ledger says), so the permit
        // never releases a file toward an ordinary container. The queued retry carries RootsAfter and runs this same check.
        var targetRecord = await _resolver.ResolveOwningRecordAsync(derivation.PrimaryContainer!, ct).ConfigureAwait(false);
        if (targetRecord is null || !permit.RootsAfter.Any(root =>
                string.Equals(root.EntityLogicalName, targetRecord.EntityLogicalName, StringComparison.OrdinalIgnoreCase)
                && root.RecordId == targetRecord.RecordId))
        {
            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] Document {DocumentId} derives to container {Target}, whose secure record is not one the F3 "
                + "re-file filed it under ({Count} root(s)): no release is recorded; the file stays held.", documentId,
                derivation.PrimaryContainer, permit.RootsAfter.Count);
            return new RelocationReleaseOutcome(RelocationReleaseState.NotCovered,
                $"it derives to {derivation.PrimaryContainer!.Trim()}, which is not the own container of a secure record this "
                + "re-file filed it under with F3; no release was recorded");
        }

        var ledger = RelocationLedger.Parse(row!.GetAttributeValue<string>(RelocationLedgerColumn));
        if (ledger is null)
        {
            return new RelocationReleaseOutcome(RelocationReleaseState.Failed,
                $"its {RelocationLedgerColumn} is unreadable; it is not overwritten, and no release was recorded");
        }

        var release = new RelocationRelease
        {
            SourceDrive = sourceDrive,
            SourceItem = sourceItem,
            TargetContainer = derivation.PrimaryContainer!.Trim(),
            SourceRecordTable = sourceRecord.EntityLogicalName,
            SourceRecordId = sourceRecord.RecordId,
            AuthorizedBy = permit.Decision.CallerSystemUserId,
            Basis = permit.Decision.Basis.ToString(),
            At = binding?.PermittedAt ?? _time.GetUtcNow(),
        };

        if (binding is not null)
        {
            var now = _time.GetUtcNow();
            if (!release.IsCurrent(now))
            {
                return new RelocationReleaseOutcome(RelocationReleaseState.NotCovered,
                    $"the re-file's F3 permission ({binding.PermittedAt:o}) is more than {ReleaseLifetime.TotalDays:0} days old; "
                    + "no release was recorded");
            }

            if (ledger.Release is { } existing && existing.Releases(sourceDrive, sourceItem, release.TargetContainer, now))
            {
                // Recorded already (a redelivery, or the inline path won): kept as it is.
                return new RelocationReleaseOutcome(RelocationReleaseState.Recorded,
                    $"its file may follow it from {sourceDrive} into {release.TargetContainer} (already recorded)");
            }
        }

        if (!await StillLockedAsync(documentId, ct).ConfigureAwait(false))
        {
            return new RelocationReleaseOutcome(RelocationReleaseState.Failed,
                LockLost(documentId, "before its F3 release was recorded"));
        }

        await _dataverse.UpdateAsync(DocumentEntity, documentId, new Dictionary<string, object>
        {
            [RelocationLedgerColumn] = (ledger with { Release = release }).Serialize(),
        }, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] Recorded the F3 release of document {DocumentId}'s file {SourceDrive}/{SourceItem} into {Target} "
            + "(left {Table} {RecordId}; by {Caller}, {Basis}).", documentId, sourceDrive, sourceItem, release.TargetContainer,
            release.SourceRecordTable, release.SourceRecordId, release.AuthorizedBy, release.Basis);
        return new RelocationReleaseOutcome(RelocationReleaseState.Recorded,
            $"its file may follow it from {sourceDrive} into {release.TargetContainer}");
    }

    /// <summary>
    /// A queued release retry whose binding no longer holds: nothing is recorded and no retry finds otherwise, so its file
    /// stays held — an Error with the one recovery (<see cref="UnreleasedRecovery"/>).
    /// </summary>
    private RelocationReleaseOutcome BindingMismatch(Guid documentId, string why)
    {
        _logger.LogError(
            "[DOCUMENT-RELOCATE] The queued F3 release of document {DocumentId} was NOT recorded: {Why}. Its file stays held "
            + "unless a later F3 re-file released it. Recovery: {Recovery}.", documentId, why, UnreleasedRecovery);
        return new RelocationReleaseOutcome(RelocationReleaseState.NotCovered,
            $"{why}; no release was recorded — {UnreleasedRecovery}");
    }

    /// <summary>The row's F3 release, read now; <c>Unreadable</c> when its ledger is not a relocation ledger. A fault throws.</summary>
    private async Task<(RelocationRelease? Release, bool Unreadable)> ReadReleaseAsync(Guid documentId, CancellationToken ct)
    {
        var row = await _dataverse.RetrieveAsync(DocumentEntity, documentId, [RelocationLedgerColumn], ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"sprk_document {documentId:D} could not be read");
        var ledger = RelocationLedger.Parse(row.GetAttributeValue<string>(RelocationLedgerColumn));
        return ledger is null ? (null, true) : (ledger.Release, false);
    }

    /// <summary>Why <see cref="DropInvalidReleaseAsync"/> dropped a release, if it did.</summary>
    private enum ReleaseDrop
    {
        Kept,
        Expired,
        Invalid,
    }

    /// <summary>
    /// #2159 slice 2c — drops the row's F3 release, under the confirmed lock, when it can no longer release the move it was
    /// recorded for: it expired (<see cref="ReleaseLifetime"/>), the row's file is no longer its item, or the row derives to
    /// another container now (a derivation that cannot be read decides nothing: it is kept, and the guard holds the move).
    /// The row read in memory is updated with what was written. A failed or unlocked write leaves it; the guard's exact
    /// match holds the move either way.
    /// </summary>
    private async Task<ReleaseDrop> DropInvalidReleaseAsync(
        Guid documentId, Entity row, RelocationRelease release, string currentDrive, string currentItem, CancellationToken ct)
    {
        string? why = null;
        var expired = !release.IsCurrent(_time.GetUtcNow());
        if (expired)
        {
            why = $"it expired (recorded {release.At:o}, lifetime {ReleaseLifetime.TotalDays:0} days)";
        }
        else if (!SameItem(release.SourceDrive, release.SourceItem, currentDrive, currentItem))
        {
            why = "the row's file is no longer the item it names";
        }
        else
        {
            var derivation = await _resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
            if (derivation.Decided && !RecordContainerResolver.IsSameContainerId(release.TargetContainer, derivation.PrimaryContainer))
            {
                why = $"the row derives to another container now ({derivation.PrimaryContainer})";
            }
        }

        if (why is null || RelocationLedger.Parse(row.GetAttributeValue<string>(RelocationLedgerColumn)) is not { } ledger
            || !await StillLockedAsync(documentId, ct).ConfigureAwait(false))
        {
            return ReleaseDrop.Kept;
        }

        var without = ledger with { Release = null };
        try
        {
            await _dataverse.UpdateAsync(DocumentEntity, documentId, new Dictionary<string, object>
            {
                [RelocationLedgerColumn] = without.IsEmpty ? DBNull.Value : without.Serialize(),
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] The invalid F3 release of document {DocumentId} could not be dropped.", documentId);
            return ReleaseDrop.Kept;
        }

        if (without.IsEmpty)
        {
            row.Attributes.Remove(RelocationLedgerColumn);
        }
        else
        {
            row[RelocationLedgerColumn] = without.Serialize();
        }

        _logger.LogWarning(
            "[DOCUMENT-RELOCATE] Dropped the F3 release of document {DocumentId} ({SourceDrive}/{SourceItem} into {Target}, by "
            + "{Caller} at {At}): {Why}. Leaving its secure record now needs a new F3 re-file.", documentId, release.SourceDrive,
            release.SourceItem, release.TargetContainer, release.AuthorizedBy, release.At, why);
        return expired ? ReleaseDrop.Expired : ReleaseDrop.Invalid;
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

        /// <summary>#2159 B5: a version is larger than a single-request copy — permanent, unlike every other failure here.</summary>
        public bool TooLarge { get; init; }

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
            return History.Fail($"a version of {drive}/{item} is larger than a single-request copy ({MaxRelocatableBytes} bytes)") with
            {
                TooLarge = true,
            };
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

    /// <param name="recheck">
    /// #2159 B8: asked under the confirmed lock immediately before the re-point; an outcome stops the move there (the copy
    /// is removed, nothing is written). A relocation passes <see cref="RecheckBeforeRepointAsync"/>; the re-copy of an
    /// edited source (into the row's CURRENT container) passes <see cref="RecheckFileUnchangedAsync"/>.
    /// </param>
    /// <remarks>
    /// #2159 fix round: every path that stops a move after its copy exists removes the copy itself; a move that EXITS by an
    /// exception instead — a cancellation mid-replay (shutdown), which the fault filters let through, or an unexpected
    /// fault — removes it here, with <see cref="CancellationToken.None"/>, best effort and logged
    /// (<see cref="RemoveAbandonedCopyAsync"/>). A copy the row may already point at is never removed blindly.
    /// </remarks>
    private async Task<MoveResult> MoveAsync(
        Guid documentId, Entity row, string sourceDrive, string sourceItem, string targetDrive, ReplayPlan plan,
        RelocationLedger ledger, LedgerSourceState sourceState, CancellationToken ct,
        Func<CancellationToken, Task<DocumentRelocationOutcome?>>? recheck = null)
    {
        var copy = new CopyInFlight();
        var returned = false;
        try
        {
            var result = await MoveCoreAsync(documentId, row, sourceDrive, sourceItem, targetDrive, plan, ledger, sourceState,
                copy, ct, recheck).ConfigureAwait(false);
            returned = true; // every return has committed its copy or removed it itself
            return result;
        }
        finally
        {
            if (!returned && copy.Item is { } abandoned && !copy.Committed)
            {
                await RemoveAbandonedCopyAsync(documentId, targetDrive, abandoned, copy.PointerWriteStarted).ConfigureAwait(false);
            }
        }
    }

    /// <summary>What a move's copy has reached, for <see cref="MoveAsync"/>'s cleanup when the move exits by an exception.</summary>
    private sealed class CopyInFlight
    {
        /// <summary>The copy, once its first version was written.</summary>
        public string? Item { get; set; }

        /// <summary>The re-point that names the copy was sent (it may have landed even if it did not answer).</summary>
        public bool PointerWriteStarted { get; set; }

        /// <summary>The re-point landed: the copy is the row's file.</summary>
        public bool Committed { get; set; }
    }

    /// <summary>
    /// Removes the copy of a move that exited by an exception before it was committed, with
    /// <see cref="CancellationToken.None"/> (the move's own token may be the cancelled one). When the re-point had been
    /// sent, the copy is KEPT (PR #2161 re-check): the move exits this way only when that write did not answer — a
    /// cancellation (Make Secure's request token) — so it may have landed, or may still land after any re-read, and
    /// deleting a file a row may point at would lose the document. The copy is logged for an administrator. Never throws.
    /// </summary>
    private async Task RemoveAbandonedCopyAsync(Guid documentId, string targetDrive, string copyItem, bool pointerWriteStarted)
    {
        try
        {
            if (pointerWriteStarted)
            {
                _logger.LogError(
                    "[DOCUMENT-RELOCATE] the move of document {DocumentId} stopped while re-pointing it and the re-point may "
                    + "still land; the copy {TargetDrive}/{TargetItem} is kept — an administrator must check whether the row "
                    + "names it.", documentId, targetDrive, copyItem);
                return;
            }

            _logger.LogWarning(
                "[DOCUMENT-RELOCATE] the move of document {DocumentId} stopped before its re-point (cancelled or faulted); "
                + "removing its copy {TargetDrive}/{TargetItem}; the row and the source are unchanged.", documentId, targetDrive, copyItem);
            await DeleteQuietlyAsync(targetDrive, copyItem, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DOCUMENT-RELOCATE] the abandoned copy {TargetDrive}/{TargetItem} of document {DocumentId} could not be "
                + "removed; an administrator must delete it.", targetDrive, copyItem, documentId);
        }
    }

    /// <summary>What the row says about a copy whose re-point faulted (refused, or sent and did not answer).</summary>
    private enum CopyNamed
    {
        /// <summary>
        /// The row names something else NOW. For a write whose outcome is unknown this proves nothing: it may still land.
        /// </summary>
        No,

        /// <summary>The row names the copy: the re-point (one atomic update) landed.</summary>
        Yes,

        /// <summary>The row could not be read again: it may name the copy, so the copy must be kept.</summary>
        Unknown,
    }

    /// <summary>
    /// After a re-point that faulted — refused (the client's last retry only), or sent and did not answer — reads the row again
    /// with <see cref="CancellationToken.None"/> — the move's own token may be the cancelled one — to learn whether it
    /// landed. Best effort: a read that faults or finds no row is <see cref="CopyNamed.Unknown"/>. Never throws.
    /// </summary>
    private async Task<CopyNamed> RowNamesCopyAsync(Guid documentId, string targetDrive, string copyItem)
    {
        try
        {
            var row = await ReadRowAsync(documentId, CancellationToken.None).ConfigureAwait(false);
            if (row is null)
            {
                return CopyNamed.Unknown;
            }

            return SameItem(row.GetAttributeValue<string>(DriveColumn), row.GetAttributeValue<string>(ItemColumn), targetDrive, copyItem)
                ? CopyNamed.Yes
                : CopyNamed.No;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] document {DocumentId} could not be read again after its re-point.", documentId);
            return CopyNamed.Unknown;
        }
    }

    /// <summary>
    /// PR #2161 re-check: did Dataverse DEFINITELY refuse a write, so it never commits? Only when it ANSWERED with a refusal
    /// — a service fault or a 4xx (the update's transaction rolled back; the <c>DecisionActionExecutors.IsClearRefusal</c>
    /// rule); a concurrency mismatch arrives as such a fault. Looked for through the whole exception chain: the entity
    /// service wraps every failure, a cancellation included, in an <see cref="InvalidOperationException"/>. A timeout, a
    /// cancellation, a transport fault or a 5xx is not an answer: the write may have committed, or may still. Even a
    /// definite refusal is only the client's LAST attempt (it retries internally), so the caller still reads the row
    /// again before removing anything.
    /// </summary>
    internal static bool IsDefiniteWriteRejection(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault>
                || e is Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseOperationException
                {
                    InnerException: Microsoft.Rest.HttpOperationException
                    {
                        Response.StatusCode: >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError,
                    },
                })
            {
                return true;
            }
        }

        return false;
    }

    private async Task<MoveResult> MoveCoreAsync(
        Guid documentId, Entity row, string sourceDrive, string sourceItem, string targetDrive, ReplayPlan plan,
        RelocationLedger ledger, LedgerSourceState sourceState, CopyInFlight inFlight, CancellationToken ct,
        Func<CancellationToken, Task<DocumentRelocationOutcome?>>? recheck)
    {
        if (ledger.Entries.Count >= MaxLedgerEntries)
        {
            return MoveResult.Failed(DocumentRelocationOutcome.Of(
                documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                $"the row already owes {ledger.Entries.Count} earlier moves; settle them before moving it again") with
            {
                TerminalCause = TerminalLedgerFull,
            });
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
            inFlight.Item = written!.Id; // the check above refused a write with no item
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
        // #2159 slice 2: a release is CONSUMED here, in the re-point's own write, by a move that LEAVES its container — the
        // item it named is no longer the row's. A re-copy WITHIN the container (an edited source closed in place, #2159 fix
        // round) is not the move it releases: the release is kept, re-bound to the copy (same source container and secure
        // record, same target, same age), so the released move still runs.
        var release = RecordContainerResolver.IsSameContainerId(sourceDrive, targetDrive)
                      && ledger.Release is { } kept && SameItem(kept.SourceDrive, kept.SourceItem, sourceDrive, sourceItem)
            ? kept with { SourceItem = copy.Id }
            : null;
        alsoWrite[RelocationLedgerColumn] = (ledger.With(entry) with { Release = release }).Serialize();
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

        if (recheck is not null)
        {
            DocumentRelocationOutcome? stopped;
            try
            {
                stopped = await recheck(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[DOCUMENT-RELOCATE] the re-check before re-pointing document {DocumentId} faulted.", documentId);
                stopped = DocumentRelocationOutcome.Of(documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                    $"the re-check before the re-point faulted ({ex.GetType().Name}); the copy was removed and nothing was written");
            }

            if (stopped is not null)
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] re-point STOPPED for document {DocumentId} ({Detail}); removing the copy "
                    + "{TargetDrive}/{TargetItem}; the row still points at {SourceDrive}/{SourceItem}.",
                    documentId, stopped.Detail, targetDrive, copy.Id, sourceDrive, sourceItem);
                await DeleteQuietlyAsync(targetDrive, copy.Id, ct).ConfigureAwait(false);
                return MoveResult.Failed(stopped);
            }
        }

        try
        {
            inFlight.PointerWriteStarted = true;
            await WritePointerAsync(documentId, targetDrive, copy.Id, copyFacts!.WebUrl, alsoWrite, ct).ConfigureAwait(false);
            inFlight.Committed = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The row is ALWAYS read again first, a definite refusal included: the Dataverse client retries an update
            // internally (502/503, transient SQL codes, throttling), so the refusal it surfaces is only its LAST attempt's
            // answer — an earlier attempt may have committed. A row that names the copy is a commit, whatever the exception.
            var definite = IsDefiniteWriteRejection(ex);
            var names = await RowNamesCopyAsync(documentId, targetDrive, copy.Id).ConfigureAwait(false);

            // PR #2161 re-check: only a DEFINITE refusal (Dataverse answered with a fault or a 4xx) whose row was read and
            // names something else proves the update never committed, so only then is the copy removed.
            if (definite && names == CopyNamed.No)
            {
                _logger.LogError(ex,
                    "[DOCUMENT-RELOCATE] document {DocumentId}: Dataverse refused the re-point and the row, read again, does not "
                    + "name the copy; the copy {TargetDrive}/{TargetItem} is removed.", documentId, targetDrive, copy.Id);
                await DeleteQuietlyAsync(targetDrive, copy.Id, CancellationToken.None).ConfigureAwait(false);
                return MoveResult.Failed(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null, "the row could not be re-pointed"));
            }

            // Otherwise the outcome is unknown: a refusal whose row could not be read again, or a write that was SENT and
            // did not answer (a timeout, a cancellation — the request's own token, wrapped by the entity service — a
            // transport fault or a 5xx after send), which may have landed or may still land after any re-read. A row that
            // does not name the copy (yet) proves nothing, so the copy is KEPT — a stray copy is recoverable, a row pointing
            // at a deleted file is not.
            if (names != CopyNamed.Yes)
            {
                _logger.LogError(ex,
                    "[DOCUMENT-RELOCATE] document {DocumentId}: {Answer} ({Reread}); the copy {TargetDrive}/{TargetItem} is kept "
                    + "— an administrator must check whether the row names it.",
                    documentId,
                    definite ? "Dataverse refused the re-point, but an earlier attempt of the client's retries may have committed it"
                             : "the re-point did not answer and may still land",
                    names == CopyNamed.Unknown ? "the row could not be read again" : "the row does not name it yet",
                    targetDrive, copy.Id);
                return MoveResult.Failed(DocumentRelocationOutcome.Of(
                    documentId, RelocationState.Failed, sourceDrive, sourceItem, targetDrive, null,
                    names == CopyNamed.Unknown
                        ? "the re-point faulted and the row could not be read again; the copy is kept until it is checked"
                        : "the re-point did not answer and may still land; the copy is kept until it is checked"));
            }

            // The one atomic update landed (pointer, ledger, version record, release): the move is committed.
            _logger.LogWarning(ex,
                "[DOCUMENT-RELOCATE] document {DocumentId}: the re-point faulted but landed; the move is committed.", documentId);
            inFlight.Committed = true;
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] re-pointed: document {DocumentId} {SourceDrive}/{SourceItem} -> {TargetDrive}/{TargetItem}; "
            + "re-keyed own columns [{Columns}]; {Recorded} version(s) recorded with their original authorship; ledger entry "
            + "(with the source's witness) for the old item recorded.",
            documentId, sourceDrive, sourceItem, targetDrive, copy.Id,
            string.Join(", ", alsoWrite.Keys.Where(k => k != RelocationLedgerColumn && k != RelocatedVersionHistory.Column)),
            record.Recorded);
        if (release is not null)
        {
            _logger.LogInformation(
                "[DOCUMENT-RELOCATE] document {DocumentId}'s F3 release into {Target} is kept, re-bound from {SourceItem} to its "
                + "re-copy {CopyItem} in the same container.", documentId, release.TargetContainer, sourceItem, copy.Id);
        }
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

    /// <param name="Summary">What the settle and the re-copy found.</param>
    /// <param name="Row">The row after the re-copy re-pointed it; <see langword="null"/> when it did not.</param>
    /// <param name="CheckedOut">The re-copy stopped because the document was checked out while it ran (#2159 fix round).</param>
    private sealed record RecopyResult(SettleSummary Summary, Entity? Row, DocumentRelocationOutcome? CheckedOut = null);

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

        // #2159 fix round: the re-copy re-points too, so it is re-checked the same way just before — a document checked
        // out (or re-pointed) while the histories were replayed is left alone.
        var moved = await MoveAsync(documentId, row, currentDrive, currentItem, currentDrive,
            new ReplayPlan(steps, finalFacts, current.Witness, current.Facts!.Name ?? row.GetAttributeValue<string>(FileNameColumn)),
            ledger, LedgerSourceState.Pending, ct,
            recheck: token => RecheckFileUnchangedAsync(documentId, currentDrive, currentItem, currentDrive, token)).ConfigureAwait(false);
        if (moved.Failure is not null)
        {
            summary.AddPending($"source-changed-after-move: the re-copy failed ({moved.Failure.State}: {moved.Failure.Detail}); "
                               + "a repeat call retries");
            return new RecopyResult(summary, null, moved.Failure.State == RelocationState.CheckedOut ? moved.Failure : null);
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
                $"{LedgerUnreadablePendingPrefix} {RelocationLedgerColumn} of this document is not a relocation ledger; it is not acted on — "
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
        var updated = ledger with { Entries = remaining };
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
                    [RelocationLedgerColumn] = updated.IsEmpty ? DBNull.Value : updated.Serialize(),
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
    /// still matches the witness recorded when its copy was verified (round 45 item 4) AND the row's current file exists
    /// (PR #2161 re-check) — edited since → reported
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

            if (purpose == RelocationPurpose.Refile && !derivation.IsSecure)
            {
                // #2159: a re-file moves ANOTHER row along only into a secure record's own container (that narrows). Whether
                // it may leave for any other container — it may still be isolated — is that row's own re-file to decide, so
                // it keeps the source as its file, settled.
                keptFor.Add($"sprk_document:{other} (a re-file moves another row along only into a secure container; it keeps the source)");
                continue;
            }

            // Inside: it belongs where this document's file now is — move it along, with its OWN copy (unique item key).
            var along = await MoveAlongAsync(other, entry, currentDrive, purpose, ct).ConfigureAwait(false);
            if (along.State == RelocationState.HeldIsolated)
            {
                // #2159: an isolated row is never moved along out of isolation either; the source stays its file, settled.
                keptFor.Add($"sprk_document:{other} (owned by the Secure Record owner team; it keeps the source as its file)");
                continue;
            }

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

        // PR #2161 re-check: the source is deleted only while the row's CURRENT file provably exists. A row can name a file
        // that is gone (a re-point that landed after its copy was removed); deleting the source then loses the document.
        if (!await CurrentFileExistsAsync(documentId, currentDrive, currentItem, ct).ConfigureAwait(false))
        {
            return new SourceSettlement(LedgerSourceState.Pending,
                "the document's current file is missing or could not be read, so its source is kept");
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

    /// <summary>
    /// PR #2161 re-check — does the row's current file exist, read now? Missing or unreadable is <see langword="false"/>,
    /// logged as an Error naming the document only: its source is then kept, and the entry stays owed.
    /// </summary>
    private async Task<bool> CurrentFileExistsAsync(Guid documentId, string currentDrive, string currentItem, CancellationToken ct)
    {
        string why;
        try
        {
            if (await _spe.GetItemCreatorAsync(currentDrive, currentItem, ct).ConfigureAwait(false) is not null)
            {
                return true;
            }

            why = "is missing";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            why = $"could not be read ({ex.GetType().Name})";
        }

        _logger.LogError(
            "[DOCUMENT-RELOCATE] document {DocumentId}: its current file {Why}; the old source its ledger owes is NOT deleted "
            + "and stays pending.", documentId, why);
        return false;
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

            // #2159 B4: a checked-out row is never moved along (its holder is editing the source); the source stays pending.
            if (IsCheckedOut(row))
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.CheckedOut, entry.SourceDrive, entry.SourceItem,
                    targetDrive, null, "it is checked out; it is moved along once it is checked in");
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

            // #2159: the same widening guard as the row's own move, under ITS lock.
            if (await IsolationHoldAsync(other, derivation, entry.SourceDrive, entry.SourceItem, targetDrive, ct).ConfigureAwait(false)
                is { } held)
            {
                return held;
            }

            var verified = await IsSourceVerifiedAsync(other, row, entry.SourceDrive, entry.SourceItem, facts, derivation, purpose, ct)
                .ConfigureAwait(false);
            if (!verified)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.SourceUnverified, entry.SourceDrive, entry.SourceItem,
                    targetDrive, null, "the file is not verifiably this row's own");
            }

            if (facts.Size is > MaxRelocatableBytes)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive,
                    null, "too large for a single-request copy") with
                {
                    TerminalCause = TerminalTooLarge,
                };
            }

            var history = await ReadHistoryAsync(entry.SourceDrive, entry.SourceItem, VersionRecordOf(row), ct).ConfigureAwait(false);
            if (history.Failure is not null)
            {
                return DocumentRelocationOutcome.Of(other, RelocationState.Failed, entry.SourceDrive, entry.SourceItem, targetDrive,
                    null, $"its version history could not be copied: {history.Failure}") with
                {
                    TerminalCause = history.TooLarge ? TerminalTooLarge : null,
                };
            }

            var moved = await MoveAsync(other, row, entry.SourceDrive, entry.SourceItem, targetDrive,
                new ReplayPlan(history.Steps, facts, history.Witness, facts.Name ?? row.GetAttributeValue<string>(FileNameColumn)),
                ledger, LedgerSourceState.Delegated, ct,
                recheck: token => RecheckBeforeRepointAsync(other, entry.SourceDrive, entry.SourceItem, targetDrive, token))
                .ConfigureAwait(false);
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
             RelocationLedgerColumn, RelocatedVersionHistory.Column, CheckedOutByColumn, CheckedOutDateColumn], ct)
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

    /// <summary>
    /// The row's relocation ledger: the old items a move of THIS row still owes something for, and (#2159 slice 2) the F3
    /// release a BFF re-file recorded for the row's next move (<see cref="Release"/>).
    /// </summary>
    internal sealed record RelocationLedger(IReadOnlyList<RelocationLedgerEntry> Entries)
    {
        public static RelocationLedger EmptyLedger { get; } = new([]);

        /// <summary>#2159 slice 2: the F3 release of the row's move from one secure record's container into another's.</summary>
        public RelocationRelease? Release { get; init; }

        /// <summary>Nothing owed and no release: the column is cleared.</summary>
        public bool IsEmpty => Entries.Count == 0 && Release is null;

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
                    || entries.Any(e => string.IsNullOrWhiteSpace(e.SourceDrive) || string.IsNullOrWhiteSpace(e.SourceItem))
                    || stored.Release is { IsWellFormed: false })
                {
                    return null;
                }

                return new RelocationLedger(entries) { Release = stored.Release };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public RelocationLedger With(RelocationLedgerEntry entry)
            => this with
            {
                Entries = Entries.Where(e => !SameItem(e.SourceDrive, e.SourceItem, entry.SourceDrive, entry.SourceItem)).Append(entry).ToList(),
            };

        public string Serialize() => JsonSerializer.Serialize(new StoredLedger(1, Entries.ToList(), Release), LedgerJson);

        public bool SameAs(RelocationLedger other) => Entries.SequenceEqual(other.Entries) && Equals(Release, other.Release);

        private sealed record StoredLedger(int V, List<RelocationLedgerEntry>? Entries, RelocationRelease? Release = null);
    }

    /// <summary>
    /// #2159 slice 2 — the F3 release of ONE move of the row: its file <see cref="SourceItem"/> in secure record
    /// <see cref="SourceRecordTable"/>/<see cref="SourceRecordId"/>'s own container <see cref="SourceDrive"/> may follow it
    /// into <see cref="TargetContainer"/>, because a BFF re-file took the row out of that record with
    /// <see cref="AuthorizedBy"/>'s F3 permission at <see cref="At"/>. Written only by <see cref="RecordReleaseAsync"/>
    /// under the row's lock; consumed by the re-point; honoured only on an exact match (<see cref="Releases"/>).
    /// </summary>
    internal sealed record RelocationRelease
    {
        public required string SourceDrive { get; init; }
        public required string SourceItem { get; init; }
        public required string TargetContainer { get; init; }
        public required string SourceRecordTable { get; init; }
        public Guid SourceRecordId { get; init; }
        public Guid AuthorizedBy { get; init; }
        public string? Basis { get; init; }
        public DateTimeOffset At { get; init; }

        /// <summary>Every field a release is matched on is present (otherwise the whole ledger is unreadable).</summary>
        [JsonIgnore]
        public bool IsWellFormed
            => !string.IsNullOrWhiteSpace(SourceDrive) && !string.IsNullOrWhiteSpace(SourceItem)
               && !string.IsNullOrWhiteSpace(TargetContainer) && !string.IsNullOrWhiteSpace(SourceRecordTable)
               && SourceRecordId != Guid.Empty && At != default;

        /// <summary>Unexpired: recorded no more than <see cref="ReleaseLifetime"/> ago, and not in the future.</summary>
        public bool IsCurrent(DateTimeOffset now) => At <= now + TimeSpan.FromMinutes(5) && now - At <= ReleaseLifetime;

        /// <summary>Does it release exactly this move: this item, into this container, unexpired?</summary>
        public bool Releases(string sourceDrive, string sourceItem, string target, DateTimeOffset now)
            => IsCurrent(now)
               && SameItem(SourceDrive, SourceItem, sourceDrive, sourceItem)
               && RecordContainerResolver.IsSameContainerId(TargetContainer, target);
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

    /// <summary>
    /// #2159: the document's filing changed (by any writer), so its file moves into its NEW derived container. Only the
    /// row's BFF-bound item that the BFF identity uploaded is moved (never the legacy check, B9), and never an unfiled
    /// document's file of unknown home: see
    /// <see cref="DocumentContainerRelocator.RelocateIfMisplacedAsync"/>. (No purpose moves a file out of isolation.)
    /// </summary>
    Refile,
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

    /// <summary>
    /// #2159 (every purpose): the row is still owned by the Secure Record owner team, so its file is not moved into a
    /// container that is not a secure record's own — its filing was changed outside the BFF and isolation holds the row
    /// (owner round 24 item 2). Only an F3 act (<c>/unsecure-project</c>, a re-parent with F3) releases it; the next
    /// re-file pass then moves it. A ledger entry an earlier move owed was still settled; nothing else was written.
    /// </summary>
    HeldIsolated,

    /// <summary>
    /// #2159 / #2151 (<see cref="RelocationPurpose.Refile"/>): the document is UNFILED and its file is in no business
    /// unit's container and in no declared home — where it belongs cannot be told (a record's narrower audience must not
    /// be widened to a whole business unit). Reported and left; nothing was written.
    /// </summary>
    UnfiledHomeUnknown,

    /// <summary>
    /// #2159 B4 (every purpose): the document is checked out (<c>sprk_checkedoutby</c> / <c>sprk_checkedoutdate</c>), so its
    /// file is not copied, re-pointed, settled or deleted while its holder edits it. Not settled: check-in modifies the row
    /// and clears the checkout, and a later pass moves it. Nothing was written.
    /// </summary>
    CheckedOut,
}

/// <summary>#2159 slice 2: what <see cref="DocumentContainerRelocator.RecordReleaseAsync"/> did.</summary>
public enum RelocationReleaseState
{
    /// <summary>The release was written to the row's ledger, under its lock.</summary>
    Recorded,

    /// <summary>No release is needed: the document's file does not leave a secure record's own container for another's.</summary>
    NotNeeded,

    /// <summary>
    /// Not recorded: the move the file would make is not one this F3 permission covers (its file is in a secure record the
    /// re-file did not leave). The widening guard keeps holding it.
    /// </summary>
    NotCovered,

    /// <summary>Not recorded: another relocation of the document held its lock past <see cref="DocumentContainerRelocator.ReleaseLockWait"/>.</summary>
    LockHeld,

    /// <summary>Not recorded: something could not be read or written (the file stays held — fail closed).</summary>
    Failed,
}

/// <summary>#2159 slice 2: the answer of <see cref="DocumentContainerRelocator.RecordReleaseAsync"/>.</summary>
public sealed record RelocationReleaseOutcome(RelocationReleaseState State, string Detail)
{
    /// <summary>
    /// #2159 fix round: not recorded now (<see cref="RelocationReleaseState.LockHeld"/> or
    /// <see cref="RelocationReleaseState.Failed"/>), and its retry was queued (<see cref="RelocationReleaseRetryJobHandler"/>).
    /// </summary>
    public bool RetryQueued { get; init; }
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

    /// <summary>
    /// #2159 slice 2c: the row's F3 release had EXPIRED (<see cref="DocumentContainerRelocator.ReleaseLifetime"/>) and this call
    /// dropped it — the move it released never happened, so the document stays held until a new F3 re-file. Reported once by
    /// the job (<c>releaseExpiredDocuments</c>), for an administrator.
    /// </summary>
    public bool ReleaseExpired { get; init; }

    /// <summary>
    /// #2159 B5: a <see cref="RelocationState.Failed"/> that no repeat call can settle — <see cref="DocumentContainerRelocator.TerminalTooLarge"/>
    /// or <see cref="DocumentContainerRelocator.TerminalLedgerFull"/> (the job adds
    /// <see cref="DocumentContainerRelocator.TerminalLedgerUnreadable"/>). The job reports it once, never carries it, and it
    /// fails no later run; <see langword="null"/> for every other outcome.
    /// </summary>
    public string? TerminalCause { get; init; }

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
