// Task 070 (Track D) — cluster 2a, extracted from `ComposeService`.
//
// WHY THIS IS ITS OWN COMPONENT. Everything here answers one question: WHEN does an ephemeral draft
// become a durable `sprk_document`, and what do we honestly report about that attempt? It owns the
// promotion itself plus the outcome shaping around it — the container/record failure results, the
// per-step `JobAwareCompletionState` projection, the interim success bar, and file naming.
//
// WHY THE HELPERS CAME TOO, against the seam map's original grouping. `BuildRecordFailedResult`,
// `BuildContainerFailedResult`, `ProjectCreateOnSaveState` and `ResolveFileName` are each called
// from `SaveAsync` as well as from the promotion. The seam map filed them under "create-on-save
// lifecycle" and that is right — `SaveAsync`'s TRANSIENT branch *is* create-on-save. Leaving them
// behind would have made this collaborator call back into `ComposeService`, which is the same
// call-back-into-the-parent shape that made two clusters unextractable in task 072. Moving them
// keeps ONE reason to change on one side of the seam; `ComposeService` now delegates to them.
//
// ADR-010 — NO NEW DI REGISTRATION. `internal sealed class` constructed in the `ComposeService`
// constructor from fields it already holds (including `ComposeRecordResolution`, cluster 2b, which
// the promotion path uses to find an existing row). Verified by an EMPTY `git diff` over
// `Program.cs` + `Infrastructure/DI/`, not asserted.
//
// The bodies below are moved VERBATIM. The only edits are accessibility on the six declarations so
// `ComposeService` can still reach them. `PromoteIfEphemeralAsync` remains an `IComposeService`
// member: the CONTRACT stays on the service, which keeps a thin delegating override — the same
// split cluster 6 (`ComposeAnnotationStore`) established.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 2a — create-on-save: promoting an ephemeral Compose draft into a durable
/// <c>sprk_document</c>, and shaping the honest per-step outcome of that attempt.
/// </summary>
/// <remarks>
/// Constructed by <see cref="ComposeService"/>; never DI-registered (ADR-010). Takes the non-generic
/// <see cref="ILogger"/> so its lines stay attributed to the `ComposeService` category an operator
/// already greps for.
/// </remarks>
internal sealed class ComposeCreateOnSavePromoter
{
    private readonly IGenericEntityService _dataverse;
    private readonly ILogger _logger;
    private readonly ContentDedupDetector? _dedupDetector;
    private readonly ComposeRecordResolution _recordResolution;

    internal ComposeCreateOnSavePromoter(
        IGenericEntityService dataverse,
        ILogger logger,
        ContentDedupDetector? dedupDetector,
        ComposeRecordResolution recordResolution)
    {
        _dataverse = dataverse;
        _logger = logger;
        _dedupDetector = dedupDetector;
        _recordResolution = recordResolution;
    }

    // ORPHANED DOC COMMENT REMOVED (task 070, 2026-09-01). A `<summary>` describing "STEP 5 (FR-30,
    // #629) — best-effort durable memory CAPTURE" sat here, left behind when cluster 7 moved
    // `CaptureDocumentMemoryAsync` to `ComposeMemoryCapturer`. C# attaches every consecutive doc-comment
    // line to the NEXT member, so that summary had silently become the documentation for
    // `PromoteIfEphemeralAsync` — promotion described as memory capture. The `<inheritdoc />` below is
    // this method's real documentation (see `IComposeService.PromoteIfEphemeralAsync`).
    /// <inheritdoc />
    internal async Task<PromoteComposeDocumentResult> PromoteIfEphemeralAsync(
        PromoteComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        // SessionId is OPTIONAL (task 110): the ephemeral→promoted rebind is skipped when no
        // session is bound (transient Browse/local-file first Save). See the conditional rebinds below.
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        // 1) Idempotency check by SPE drive-item id (alt key sprk_graphitemid_uk). The lookup also carries the
        //    FR-C3 dedup columns so graduate-on-divergence needs no extra round-trip.
        var existingRow = await _recordResolution.TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);

        if (existingRow is not null)
        {
            var existingId = existingRow.Id;
            _logger.LogDebug(
                "Compose promote: existing sprk_document {DocumentRecordId} found for driveItem={DocumentSpeId} — idempotent no-op",
                existingId, request.DocumentSpeId);

            // FR-07 rebind is OPTIONAL (task 110): skip entirely when no session is bound
            // (transient Browse/local-file first Save). RebindSessionDocumentIdAsync is already
            // null-tolerant, but skipping avoids an empty-session lookup + a misleading warn.
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                await _recordResolution.RebindSessionDocumentIdAsync(
                        tenantId: request.TenantId,
                        sessionId: request.SessionId,
                        currentDocumentId: request.DocumentSpeId,
                        newDocumentId: existingId.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            // FR-C3 graduate-on-divergence: if this existing row is a hash-linked COPY whose content has now
            // diverged from the canonical it was linked at, sever the link so it becomes its own canonical.
            await _recordResolution.GraduateLinkedCopyIfDivergedAsync(existingRow, request, cancellationToken)
                .ConfigureAwait(false);

            // FR-S09 item 7 (r8 task 016): refresh the file metadata this save just changed.
            //
            // This branch is the REPLACE path — every save after the first lands here. It wrote a new
            // version to SPE (new byte length, and a new web URL whenever the file was renamed or moved)
            // and then returned without touching the row, so `sprk_filesize` and `sprk_filepath` kept
            // describing the FIRST version forever. Downstream readers trust those columns: the
            // Documents grid shows the size, "Open in SharePoint" follows the path. Both quietly drifted.
            //
            // Only these two columns, and only when the caller supplied them: the create branch owns the
            // fields that define IDENTITY (origin, transient key, canonical link) and those must never be
            // mutated by a later save — the existing-row branch's whole contract is idempotence.
            var metadataRefreshFailed = false;
            var refreshFields = new Dictionary<string, object>();
            if (request.FileSize.HasValue)
            {
                // Whole Number (int) column — same cast the create branch uses; the OrganizationService
                // write path is strict about CLR type.
                refreshFields[ComposeService.FileSizeAttribute] = (int)request.FileSize.Value;
            }
            if (!string.IsNullOrWhiteSpace(request.FilePath))
            {
                refreshFields[ComposeService.FilePathAttribute] = request.FilePath!;
            }
            if (refreshFields.Count > 0)
            {
                try
                {
                    await _dataverse.UpdateAsync(ComposeService.DocumentLogicalName, existingId, refreshFields, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Never fails the save — the document IS stored. But it is not silent either: the
                    // flag rides back to SaveAsync, which turns it into a `document-metadata-stale`
                    // degradation warning on a `persisted-with-warnings` outcome.
                    metadataRefreshFailed = true;
                    _logger.LogWarning(ex,
                        "Compose promote: file-metadata refresh failed for sprk_document {DocumentRecordId} " +
                        "(driveItem={DocumentSpeId}). The save itself is unaffected; sprk_filesize/sprk_filepath " +
                        "are now stale for this row.",
                        existingId, request.DocumentSpeId);
                }
            }

            return new PromoteComposeDocumentResult
            {
                DocumentSpeId = request.DocumentSpeId,
                SessionId = request.SessionId,
                DocumentRecordId = existingId,
                WasCreated = false,
                MetadataRefreshFailed = metadataRefreshFailed,
            };
        }

        // 2) Create the sprk_document row.
        //    The record MUST carry the full SPE pointer + file metadata (drive-id + has-file +
        //    size/mime/filepath), NOT just the item-id — otherwise downstream readers (open-links,
        //    preview) validate the pointer, find drive-id empty + sprk_hasfile false, and 409
        //    "No file is attached to this document yet." Field set mirrors the canonical
        //    OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync write.
        var entity = new Entity(ComposeService.DocumentLogicalName);
        entity[ComposeService.GraphItemIdAttribute] = request.DocumentSpeId;
        var effectiveDisplayName = !string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.DisplayName!
            : $"Compose document ({request.DocumentSpeId})";
        entity[ComposeService.DisplayNameAttribute] = effectiveDisplayName;

        // Prefer the resolved file name (carries the .docx extension); fall back to the display
        // name for standalone promote callers that supply neither.
        var effectiveFileName = !string.IsNullOrWhiteSpace(request.FileName)
            ? request.FileName!
            : request.DisplayName;
        if (!string.IsNullOrWhiteSpace(effectiveFileName))
        {
            entity[ComposeService.FileNameAttribute] = effectiveFileName!;
        }

        // SPE drive pointer — the field whose absence is the root cause of the 409s.
        if (!string.IsNullOrWhiteSpace(request.GraphDriveId))
        {
            entity[ComposeService.GraphDriveIdAttribute] = request.GraphDriveId!;
        }

        // A promoted Compose document always has an SPE file behind it (the drive-item id is a
        // hard precondition of this method). Mark it so downstream readers stop rejecting it.
        entity[ComposeService.HasFileAttribute] = true;

        // G1 (FR-01, task 020): persist the durable origin marker ONLY at create-on-save (this branch —
        // the idempotent existing-row branch above never reaches here, so a subsequent replace-path save
        // never mutates an already-persisted origin). Defaults to Imported (the Dataverse field's own
        // default) when the caller supplies none (e.g. a standalone /promote call that predates G1) —
        // never left unset, so a fresh row is never silently null-origin.
        entity[ComposeService.ComposeOriginAttribute] = new OptionSetValue((int)(request.Origin ?? ComposeOrigin.Imported));

        // G7 (FR-06, task 022): stamp the client-minted transient dedup key ONLY at create (this branch;
        // the idempotent existing-row branch above never reaches here). The single-column alt-key
        // sprk_composetransientkey_uk makes this the durable dedup identity for repeated create-on-save
        // calls (see TryFindDocumentByTransientKeyAsync + the SaveAsync transient branch). Omitted for a
        // replace-path save or an older client that predates G7 (nulls are not enforced-unique).
        if (!string.IsNullOrWhiteSpace(request.TransientKey))
        {
            entity[ComposeService.ComposeTransientKeyAttribute] = request.TransientKey!;
        }

        if (request.FileSize.HasValue)
        {
            // sprk_filesize is a Whole Number (int) column; the OrganizationService write path is
            // strict about CLR type, so cast (same as OfficeDocumentPersistence / DataverseServiceClientImpl).
            entity[ComposeService.FileSizeAttribute] = (int)request.FileSize.Value;
        }
        if (!string.IsNullOrWhiteSpace(request.MimeType))
        {
            entity[ComposeService.MimeTypeAttribute] = request.MimeType!;
        }
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            entity[ComposeService.FilePathAttribute] = request.FilePath!;
        }

        // Task 041 B-MED-3 (operator resolution 2026-08-07, option C): a PDF-sourced create-on-save
        // INHERITS the source PDF record's link lookups so the new Word document files ALONGSIDE the
        // PDF (same matter/project/… — containers are BU-level, so placement is already shared; the
        // RECORD association is what was missing). The copied set is the ADR-024 sprk_document link
        // vocabulary (mirrors AttachmentDocumentAssociationRung's map). Best-effort: a failed source
        // read logs LOUDLY and the create proceeds unassociated (mirrors the source having no links —
        // never fails the save); the idempotent existing-row branch above never reaches here, so an
        // existing record's links are never mutated.
        if (request.SourceDocumentRecordId is { } sourceRecordId)
        {
            try
            {
                var sourceEntity = await _dataverse.RetrieveAsync(
                        ComposeService.DocumentLogicalName,
                        sourceRecordId,
                        ComposeService.DocumentAssociationLookupAttributes,
                        cancellationToken)
                    .ConfigureAwait(false);

                var inherited = 0;
                if (sourceEntity is not null)
                {
                    // Column for column, deliberately — including the legacy unprefixed lookups. A subgrid
                    // binds to ONE relationship, so re-filing the copy under a different column than the
                    // source would stop the two appearing together, which is the whole point.
                    var links = Documents.DocumentLinkFieldMap.ProjectForCopy(field =>
                    {
                        var reference = sourceEntity.GetAttributeValue<EntityReference>(field.Attribute);
                        return reference is null || reference.Id == Guid.Empty ? null : reference;
                    });

                    foreach (var (attribute, reference) in links)
                    {
                        entity[attribute] = new EntityReference(reference.LogicalName, reference.Id);
                        inherited++;
                    }
                }

                if (inherited > 0)
                {
                    _logger.LogInformation(
                        "Compose promote: inherited {Count} record link(s) from source document {SourceRecordId} (PDF-sourced create — filed alongside the source).",
                        inherited, sourceRecordId);
                }
                else
                {
                    _logger.LogInformation(
                        "Compose promote: source document {SourceRecordId} carries no record links to inherit — the new document is created unassociated (mirrors the source).",
                        sourceRecordId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose promote: link inheritance from source document {SourceRecordId} failed — creating the new document UNASSOCIATED (the save itself is not affected). Associate manually or re-file from the Documents surface.",
                    sourceRecordId);
            }
        }

        // ── FR-C3 content-dedup, graduate-on-divergence (CREATE branch) ─────────────────────────────
        // (email-communication-intelligence-r2, merged from master 2026-08-07 — runs AFTER the B-MED-3
        // link inheritance above; the two blocks stamp disjoint attribute sets on the same new entity.)
        // Read the just-uploaded item's content identity (quickXorHash) and record it. On a byte-identical
        // hit against an existing CANONICAL, LINK this editable copy (sprk_canonicaldocument) rather than
        // suppressing it: a Compose document is a living document that diverges on first edit — the idempotent
        // branch above graduates it then. NOTIFY (never silent). Best-effort/non-fatal (NFR-04): any failure →
        // create proceeds unstamped. No-op when the detector is absent (bare test ctor) or the drive id is
        // unknown. Suppression is deliberately NOT used here (that is the immutable email-attachment path's
        // behavior; suppressing an editable copy would cross-wire the session onto a foreign drive-item).
        if (_dedupDetector is not null && !string.IsNullOrWhiteSpace(request.GraphDriveId))
        {
            try
            {
                var (contentHash, canonicalId) = await _dedupDetector
                    .ResolveContentIdentityAsync(request.GraphDriveId!, request.DocumentSpeId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(contentHash))
                    entity[ComposeService.CanonicalHashAttribute] = contentHash!;
                if (canonicalId is { } canonical)
                {
                    entity[ComposeService.CanonicalDocumentAttribute] = new EntityReference(ComposeService.DocumentLogicalName, canonical);
                    // Was `FindFirst("oid")` with no schema form: under inbound claim mapping the short
                    // claim does not exist, so this resolved NULL and NotifyLinkedCopyAsync bailed with
                    // "no resolvable uploader oid" — the linked-copy notification was never delivered.
                    var ownerOid = CallerResolution.ResolveObjectId(httpContext.User);
                    await _dedupDetector
                        .NotifyLinkedCopyAsync(ownerOid, canonical, effectiveFileName, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose content-dedup (create) failed (non-fatal) for driveItem={DocumentSpeId}; creating without dedup stamp.",
                    request.DocumentSpeId);
            }
        }

        // FR-07(d) (task 013): atomic UPSERT on the sprk_graphitemid_uk alternate key — replaces the
        // read-then-CreateAsync sequence so two concurrent first-saves of the SAME minted SPE item can
        // never each insert a row (Dataverse resolves the target server-side; the second UPDATES the
        // first's row → exactly one sprk_document, no TOCTOU window). The key uses the RAW DocumentSpeId
        // string, identical to the read above (TryFindDocumentByGraphItemIdAsync): sprk_graphitemid is an
        // opaque SPE drive-item id (a STRING, not a GUID), so the match is exact-string and ADR-044 GUID
        // canonicalization does NOT apply (verified — the alt-key lookup keys on the raw string).
        entity.KeyAttributes[ComposeService.GraphItemIdAttribute] = request.DocumentSpeId;

        Guid newId;
        bool rowCreatedThisCall;
        try
        {
            (newId, rowCreatedThisCall) = await _dataverse.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Compose promote: upserted sprk_document {DocumentRecordId} for driveItem={DocumentSpeId} (created={Created})",
                newId, request.DocumentSpeId, rowCreatedThisCall);
        }
        catch (InvalidOperationException ex)
        {
            // The graphItemId upsert is atomic, so the classic same-SPE-item race is already closed. This
            // catch now handles the SECONDARY race the upsert CANNOT: two truly-concurrent FIRST saves of
            // the same transient draft each mint their OWN SPE item (DIFFERENT graphitemid) but carry the
            // SAME transient key — the loser's upsert-create then fails the sprk_composetransientkey_uk
            // unique constraint. Re-resolve by graphItemId (defensive) then transientKey to land the loser
            // on the winner's record → ONE record (the loser's minted item is orphaned, an acceptable rare
            // edge — never a duplicate ROW).
            _logger.LogWarning(ex,
                "Compose promote: upsert failed for driveItem={DocumentSpeId} — likely a concurrent same-transientKey first-save. Re-resolving via alternate key (graphItemId, then transientKey).",
                request.DocumentSpeId);

            Guid? raceWinnerId = (await _recordResolution.TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false))?.Id;

            if (!raceWinnerId.HasValue && !string.IsNullOrWhiteSpace(request.TransientKey))
            {
                var transientKeyWinner = await _recordResolution.TryFindDocumentByTransientKeyAsync(request.TransientKey!, cancellationToken)
                    .ConfigureAwait(false);
                raceWinnerId = transientKeyWinner?.RecordId;
            }

            if (!raceWinnerId.HasValue)
            {
                throw;
            }

            newId = raceWinnerId.Value;
            rowCreatedThisCall = false; // the winner created the row; this call resolved onto it
        }

        // 3) Rebind the ChatSession DocumentId from SPE id → new sprk_documentid (FR-07).
        //    OPTIONAL (task 110): skip when no session is bound (transient Browse/local-file
        //    first Save). The sprk_document create above already completed without a session.
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            await _recordResolution.RebindSessionDocumentIdAsync(
                    tenantId: request.TenantId,
                    sessionId: request.SessionId,
                    currentDocumentId: request.DocumentSpeId,
                    newDocumentId: newId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new PromoteComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            SessionId = request.SessionId,
            DocumentRecordId = newId,
            // FR-07(d) (task 013): honest create-vs-update signal from the atomic upsert (false when a
            // concurrent winner created the row and this call updated/resolved onto it).
            WasCreated = rowCreatedThisCall,
        };
    }

    // =========================================================================
    // FR-05 create-on-save backbone — helpers (per-step job-aware projection).
    //
    // The four steps container → record → profile-analysis → indexing are projected through the
    // shared JobAwareCompletionStateProjector (store-before-render, ADR-040). profile-analysis is
    // DISPATCHED FIRE-AND-FORGET under OBO via the ADR-013-safe IDocumentProfileAi facade (compose-r2) —
    // captured OBO token + fresh DI scope, because a background MI job 403s on the user-OBO-written file.
    // In the synchronous response the profile step is a non-terminal "dispatched" (Running) signal, so
    // the aggregate reads Partial (record + index exist, profile pending) and never reads Failed on a
    // best-effort profile miss (which happens off-thread and is only logged).
    // =========================================================================

    /// <summary>
    /// The interim R5-E success bar for FR-05 create-on-save (documented exception, 2026-07-09):
    /// a record is interim-successful when the <c>container</c>, <c>record</c>, AND <c>indexing</c>
    /// steps all reached terminal success — a record with no SPE file OR no index is NEVER a success.
    /// <c>profile-analysis</c> is intentionally EXCLUDED from this bar so a best-effort profile miss
    /// never demotes an otherwise-good save. Since the profile now runs FIRE-AND-FORGET in the
    /// background (<see cref="DispatchBackgroundProfile"/>), the synchronous create-on-save response
    /// carries a non-terminal "dispatched" profile step — so the interim bar (container + record +
    /// indexing) is the operative success bar for the returned aggregate; the profile fields land
    /// shortly after, off the response path.
    /// </summary>
    internal static bool IsInterimCreateOnSaveSuccess(JobAwareCompletionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool Completed(string stepName) =>
            state.Steps.Any(s => string.Equals(s.StepName, stepName, StringComparison.Ordinal)
                && s.State == JobAwareState.Completed);
        return Completed(ComposeService.StepContainer) && Completed(ComposeService.StepRecord) && Completed(ComposeService.StepIndexing);
    }

    /// <summary>Resolves the created drive-item's file name from the caller display name,
    /// defaulting to a unique <c>compose-draft-…docx</c> and ensuring a <c>.docx</c> extension.</summary>
    /// <remarks>
    /// SANITIZED 2026-08-29. <paramref name="displayName"/> is CLIENT-SUPPLIED (the compose save request's
    /// DisplayName) and the returned value is handed to <c>UploadSmallAsUserAsync</c> as the whole upload
    /// path — so a '/' in a compose draft's display name made Graph create that folder inside the
    /// container. This is the same defect as the Word add-in's free-text "Document Name" box, on the
    /// Compose surface; it was NOT in the 2026-08-28 site list, which enumerated only the sites that
    /// carried a hardcoded folder PREFIX.
    /// </remarks>
    internal static string ResolveFileName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return $"compose-draft-{Guid.NewGuid():N}.docx";

        var safeName = SpeUploadPath.SanitizeFileName(displayName);
        return safeName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            ? safeName
            : safeName + ".docx";
    }

    /// <summary>A stored terminal-success signal for a step that this request completed inline.</summary>
    internal static StoredStepSignal CompletedSignal(string stepName) => new()
    {
        StepName = stepName,
        StoredStatus = JobStatus.Completed,
        Started = true,
    };

    /// <summary>
    /// FR-S09 item 5 (r8 task 016): the record step ran and resolved no <c>sprk_document</c> id.
    /// Terminal Failed (there is no retry budget on this path), so the aggregate can never read a
    /// success for a save that produced no identity record.
    /// </summary>
    internal static StoredStepSignal RecordNotResolvedSignal() => new()
    {
        StepName = ComposeService.StepRecord,
        StoredStatus = JobStatus.Failed,
        Started = true,
        Attempt = 1,
        MaxAttempts = 1,
        Detail = "record step resolved no sprk_document id",
    };

    /// <summary>
    /// FR-S09 item 5 (r8 task 016): does this <see cref="InvalidOperationException"/> describe one of the
    /// two Dataverse identity-key faults that <c>ComposeEndpoints.ExecuteSaveAsync</c> maps to an honest,
    /// administrator-actionable 409/503?
    /// </summary>
    /// <remarks>
    /// The predicate is duplicated from that catch filter ON PURPOSE, and the duplication is the point:
    /// the promote guard must let exactly those exceptions through so the endpoint handler stays live.
    /// If either side changes, the other must change with it — a single shared helper would be tidier
    /// but would hide that coupling behind an abstraction, and an endpoint handler that quietly stops
    /// being reachable is the defect this whole task exists to remove. Keep them in step.
    /// </remarks>
    internal static bool IsDataverseIdentityKeyFault(InvalidOperationException ex) =>
        ex.Message.Contains("Found multiple records", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not defined as keys", StringComparison.OrdinalIgnoreCase)
        || (ex.Message.Contains("sprk_graphitemid", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("Not Active", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// FR-S09 item 5 (r8 task 016): the terminal result for "the bytes are durable, the record is not".
    /// </summary>
    /// <remarks>
    /// Mirrors <c>BuildContainerFailedResult</c>'s shape — a RETURNED non-success outcome rather than a
    /// throw — because the two are the same kind of event: a save that reached a defined, reportable end
    /// state that is not success. <c>partially-recorded</c> rather than <c>storage-failed</c>: storage
    /// succeeded. Telling the user their document is gone when it is provably stored would be its own
    /// dishonest outcome, and it would invite them to retype work that already exists.
    /// </remarks>
    internal static SaveComposeDocumentResult BuildRecordFailedResult(
        SaveComposeDocumentRequest request,
        string effectiveSpeId,
        string? effectiveDriveId,
        FileHandleDto saved,
        ComposeOrigin origin,
        DateTimeOffset observedAt,
        string detail)
    {
        var completion = ProjectCreateOnSaveState(
            subjectId: effectiveSpeId,
            correlationId: request.SessionId,
            containerSignal: CompletedSignal(ComposeService.StepContainer),
            recordSignal: new StoredStepSignal
            {
                StepName = ComposeService.StepRecord,
                StoredStatus = JobStatus.Failed,
                Started = true,
                Attempt = 1,
                MaxAttempts = 1,
                Detail = detail,
            },
            profileSignal: ComposeProfileDispatcher.ProfileNotAttempted("profile not attempted: record step failed"),
            indexingSignal: new StoredStepSignal { StepName = ComposeService.StepIndexing, StoredStatus = null, Started = false },
            observedAt: observedAt);

        return new SaveComposeDocumentResult
        {
            Outcome = ComposeSaveOutcome.PartiallyRecorded,
            DocumentSpeId = effectiveSpeId,
            DriveId = effectiveDriveId,
            SessionId = request.SessionId,
            DocumentRecordId = null,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            WasPromotedThisSave = false,
            CompletionState = completion,
            Origin = origin,
        };
    }

    /// <summary>Projects the four create-on-save steps (with profile-analysis deferred) through the
    /// shared <see cref="JobAwareCompletionStateProjector"/>.</summary>
    internal static JobAwareCompletionState ProjectCreateOnSaveState(
        string subjectId,
        string correlationId,
        StoredStepSignal containerSignal,
        StoredStepSignal recordSignal,
        StoredStepSignal profileSignal,
        StoredStepSignal indexingSignal,
        DateTimeOffset observedAt)
    {
        var job = new JobContract
        {
            JobType = ComposeService.ComposeCreateOnSaveJobType,
            SubjectId = subjectId,
            CorrelationId = correlationId,
            IdempotencyKey = $"compose-create-on-save-{subjectId}",
        };

        var steps = new List<StoredStepSignal>
        {
            containerSignal,
            recordSignal,
            profileSignal,
            indexingSignal,
        };

        return JobAwareCompletionStateProjector.Project(job, steps, observedAt);
    }

    /// <summary>Builds the create-on-save result for a FAILED container step: no record, no version,
    /// aggregate Failed — never a success. record/indexing project as non-terminal since they never ran.
    /// <para>Post-#858 the two reachable causes are (a) the server could not DERIVE a container — the
    /// acting user's business unit has no <c>sprk_containerid</c> stamped, a legitimate configuration
    /// state — or (b) SPE drive-item creation returned null. A caller-supplied container is NOT one of
    /// them any more; <c>SaveComposeDocumentRequest.ContainerId</c> no longer exists.</para></summary>
    internal SaveComposeDocumentResult BuildContainerFailedResult(
        SaveComposeDocumentRequest request,
        DateTimeOffset observedAt)
    {
        var containerFailed = new StoredStepSignal
        {
            StepName = ComposeService.StepContainer,
            StoredStatus = JobStatus.Failed,
            Started = true,
            Attempt = 1,
            MaxAttempts = 1,
            // #858: this Detail reaches the client and is rendered. It used to say "no client-supplied
            // ContainerId", which post-#858 is both impossible and unactionable — the caller cannot
            // supply one, so telling them one is missing sends them looking for a control that no longer
            // exists. Name the two causes that ARE reachable, and point at the one an admin can fix.
            Detail = "container step failed: no storage container could be resolved for this draft "
                + "(the acting user's business unit has no container configured), or SPE drive-item "
                + "creation failed",
        };

        var completion = ProjectCreateOnSaveState(
            subjectId: request.DocumentSpeId ?? string.Empty,
            correlationId: request.SessionId,
            containerSignal: containerFailed,
            recordSignal: new StoredStepSignal { StepName = ComposeService.StepRecord, StoredStatus = null, Started = false },
            // Container failed → no record → nothing to profile. Non-terminal so the aggregate stays
            // Failed (driven by the container step), not double-counted.
            profileSignal: ComposeProfileDispatcher.ProfileNotAttempted("profile not attempted: container step failed"),
            indexingSignal: new StoredStepSignal { StepName = ComposeService.StepIndexing, StoredStatus = null, Started = false },
            observedAt: observedAt);

        return new SaveComposeDocumentResult
        {
            // FR-S06 (task 013): THE defect this contract exists to remove. This path RETURNS (it does
            // not throw), so the endpoint wraps it in Results.Ok — a save that wrote nothing at all
            // presented as HTTP 200, which the client rendered as "Saved ✓". The status stays 200 (the
            // create-on-save step-projection contract rides on this body), but the body now says plainly
            // that nothing was stored, and the client keys off THIS field rather than the status.
            Outcome = ComposeSaveOutcome.StorageFailed,
            DocumentSpeId = request.DocumentSpeId ?? string.Empty,
            DriveId = request.DriveId,
            SessionId = request.SessionId,
            DocumentRecordId = null,
            VersionId = string.Empty,
            ETag = null,
            Size = null,
            WasPromotedThisSave = false,
            CompletionState = completion,
        };
    }
}
