using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose.Operations;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Orchestration facade for the Compose drafting workspace. Coordinates load-document
/// (Path A: from <c>sprk_document</c>; Path B continuation: from SPE drive-item id only),
/// save-document (commits new SPE version), and first-Save promotion (creates a
/// <c>sprk_document</c> record idempotently when the first Save fires on an ephemeral
/// document).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this service exists</b>: per spec FR-04 / FR-05 / FR-06, the Compose UI needs a
/// single orchestration seam that the <c>POST /api/compose/load</c>,
/// <c>POST /api/compose/save</c>, and <c>POST /api/compose/promote</c> endpoints (task
/// 024) can call. This service owns workflow logic and the FR-06 first-Save promotion
/// idempotency contract. SPE plumbing (Graph drive-item read/write) is delegated to
/// <see cref="IComposeDocumentService"/> (task 022); ChatSession binding is delegated to
/// <see cref="ComposeSessionService"/> (task 023; concrete per ADR-010 strict — interface collapsed 2026-06-29 cleanup).
/// </para>
/// <para>
/// <b>Upload note (R1)</b>: per spec §10.5 Placement Justification + design.md §14 row 5
/// (R1 default-open = Browse + Search), <i>upload-to-Compose</i> in R1 routes through the
/// existing Assistant upload pipeline (Assistant → SPE → drive-item id → "Open in
/// Compose" hand-off). ComposeService.<see cref="UploadAsync"/> is preserved on the
/// interface for R2 inline-upload (drag-drop into editor) but throws
/// <see cref="NotImplementedException"/> in R1.
/// </para>
/// <para>
/// <b>ADR-013 facade boundary (refined 2026-05-20)</b>: this service is CRUD code, not AI
/// code. Implementations MUST NOT inject <c>IOpenAiClient</c>, <c>IPlaybookService</c>,
/// <c>IPlaybookOrchestrationService</c>, or any other AI-internal type. When AI is needed
/// for the Compose AI-action dispatch endpoint (task 024
/// <c>POST /api/compose/action/{consumerType}</c>), consumers consume
/// <c>IConsumerRoutingService</c> + the since-deleted generic playbook facade from
/// <c>Services/Ai/PublicContracts/</c> only. That dispatch path is part of the endpoint
/// composition in task 024; this orchestration service is intentionally narrower (it owns
/// the document-lifecycle orchestration, not the AI dispatch).
/// </para>
/// <para>
/// <b>ADR-015 multi-tenant isolation (Tier 3)</b>: tenant scoping is enforced transitively
/// through <see cref="IComposeSessionService"/> (which inherits the existing
/// <c>ChatSession</c> three-tier infrastructure) and through
/// <see cref="IComposeDocumentService"/> (which uses the existing OBO Graph pipeline). This
/// orchestrator itself MUST NOT bypass tenant boundaries.
/// </para>
/// <para>
/// <b>First-Save promotion (FR-06)</b>: <see cref="PromoteIfEphemeralAsync"/> is idempotent.
/// Multiple Save clicks before or after promotion MUST NOT create duplicate
/// <c>sprk_document</c> rows. The implementation checks for an existing
/// <c>sprk_documentid</c> bound to the SPE drive-item id (via the
/// <c>sprk_graphitemid_uk</c> alternate key created by task 010 / FW-1 OI-1) before
/// creating; if a row already exists, it returns the existing id unchanged.
/// </para>
/// </remarks>
public interface IComposeService
{
    /// <summary>
    /// Reserved for R2 inline upload (drag-drop into the Compose editor). In R1, upload
    /// routes through the existing Assistant upload pipeline; this method throws
    /// <see cref="NotImplementedException"/>. See class-level remarks.
    /// </summary>
    /// <exception cref="NotImplementedException">Always — R1 routes upload through the
    /// Assistant pipeline.</exception>
    Task<UploadComposeDocumentResult> UploadAsync(
        UploadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-01 (task 010, spaarkeai-compose-fidelity-r4.5, WS-1 "one reader everywhere"): builds the
    /// server DOCX→editor projection from caller-supplied bytes using the SAME single-walk
    /// <see cref="ComposeDocxProjectionBuilder"/> <see cref="LoadAsync"/> uses internally (F-2 — one
    /// reader). Used by the <c>POST /api/compose/upload</c> transient-mount door, which reads its
    /// bytes from <c>ITenantCache</c> rather than SPE, so it cannot call <see cref="LoadAsync"/>
    /// itself; this thin seam lets it reuse the identical builder/shape instead of a divergent
    /// projection path. Pure / synchronous — no I/O, no Graph (ADR-007). Fail-closed like the Load
    /// path: an unreadable or empty source returns <see cref="ComposeProjectionStatus.Failed"/> with
    /// <c>CanEdit=false</c>, never throws.
    /// </summary>
    /// <param name="content">The document's raw bytes (e.g. an Assistant-uploaded <c>.docx</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SAME <see cref="ComposeDocxProjection"/> shape
    /// <see cref="LoadComposeDocumentResult.Projection"/> carries.</returns>
    ComposeDocxProjection ProjectDocument(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Task 012 (spaarkeai-compose-r6, the client cutover): the EDIT-MOUNT projection bundle for the
    /// upload / browse-project doors. Extends <see cref="ProjectDocument"/> (same builder, same walk
    /// idioms — root §11 extension, not a fork) with the two additional facts an editable mount needs
    /// on the render-on-save path: (1) the bytes are paraId-MINTED first (in-memory, fail-open — the
    /// same ingest stamp <see cref="LoadAsync"/> applies) so the HTML projection the editor mounts and
    /// the canonical <see cref="ComposeMountProjection.ContentModel"/> the client retains AGREE on every
    /// paragraph id (two independent walks would otherwise mint different ids for id-less paragraphs);
    /// (2) the canonical content model itself, which the client's imported-save mapper merges editor
    /// state onto and re-posts as <c>SaveComposeDocumentRequest.ContentModel</c> (preserving every
    /// server-set field). Pure / synchronous — no I/O, no Graph (ADR-007). Fail-closed like
    /// <see cref="ProjectDocument"/>: an unreadable source returns the original bytes, a Failed HTML
    /// projection, and a null model — never throws.
    /// </summary>
    ComposeMountProjection ProjectForMount(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load an existing document into the Compose workspace. Used by both Path A (open from
    /// an existing <c>sprk_document</c> record — caller passes both
    /// <see cref="LoadComposeDocumentRequest.DocumentRecordId"/> and the resolved SPE
    /// drive-item id) and Path B continuation (re-open an ephemeral session by SPE
    /// drive-item id only). Ensures a <c>ChatSession</c> exists for the document binding.
    /// </summary>
    /// <param name="request">Load payload: SPE drive-item id (required), SPE drive id
    /// (required), tenant id (required), optional <c>sprk_documentid</c> for Path A.</param>
    /// <param name="httpContext">HTTP context for OBO auth into Graph. Required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="LoadComposeDocumentResult"/> carrying the DOCX bytes, the bound
    /// session id, and (if Path A) the <c>sprk_documentid</c>.</returns>
    Task<LoadComposeDocumentResult> LoadAsync(
        LoadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save the current state of a Compose document. Always commits a new SPE version (via
    /// <see cref="IComposeDocumentService.SaveDocxAsync"/>). For ephemeral documents (Path
    /// B without a <c>sprk_document</c> record yet), the first Save additionally triggers
    /// <see cref="PromoteIfEphemeralAsync"/> to create the Document record idempotently.
    /// </summary>
    /// <remarks>
    /// FR-06 idempotency: a concurrent or repeated Save MUST NOT create duplicate
    /// <c>sprk_document</c> rows. The promotion step is internally a no-op when the
    /// drive-item already has a bound record.
    /// </remarks>
    /// <param name="request">Save payload: SPE drive-item id + drive id + tenant id +
    /// session id + bytes + optional bound <c>sprk_documentid</c>.</param>
    /// <param name="httpContext">HTTP context for OBO auth into Graph + Dataverse. Required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SaveComposeDocumentResult"/> with the new SPE version id and
    /// the resolved <c>sprk_documentid</c> (created on first Save, returned unchanged on
    /// subsequent Saves).</returns>
    Task<SaveComposeDocumentResult> SaveAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent first-Save promotion: ensure a <c>sprk_document</c> record exists for a
    /// SPE drive-item id, creating one if absent. Re-binds the <c>ChatSession</c>
    /// <c>DocumentId</c> from the SPE drive-item id to the new <c>sprk_documentid</c> (per
    /// FR-07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Idempotency contract (FR-06)</b>: this method is safe to call repeatedly. If a
    /// <c>sprk_document</c> row already exists for the SPE drive-item id (looked up by the
    /// SPE id alternate-key index on <c>sprk_document</c> — task 010 / FW-1 OI-1 created
    /// the key), the method returns the existing id without creating a duplicate row.
    /// Concurrent callers (e.g., two Save clicks racing) resolve to the same final state.
    /// </para>
    /// <para>
    /// <b>Concurrency note</b>: the canonical idempotency mechanism is the Dataverse
    /// alternate-key + check-then-create pattern. A narrow race window exists between the
    /// lookup and the create; in that window, a duplicate-create attempt will surface as a
    /// Dataverse 412 / duplicate-key error, which the implementation catches and converts
    /// to a retrieve of the now-existing row.
    /// </para>
    /// </remarks>
    /// <param name="request">Promote payload: SPE drive-item id (required) + the bound
    /// session id (required for rebinding) + tenant id (required) + optional metadata
    /// (display name) used only on initial creation.</param>
    /// <param name="httpContext">HTTP context for OBO auth into Dataverse. Required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PromoteComposeDocumentResult"/> with the resolved
    /// <c>sprk_documentid</c> and a <see cref="PromoteComposeDocumentResult.WasCreated"/>
    /// flag distinguishing fresh-create from existing-lookup outcomes.</returns>
    Task<PromoteComposeDocumentResult> PromoteIfEphemeralAsync(
        PromoteComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// G10 (FR-09, task 040): the manual "Refresh Profile" leg — re-run the Document Profile on demand for
    /// an existing <c>sprk_document</c>, reusing the SAME fire-and-forget pipeline the save-hook +
    /// reload/onload re-trigger use (never a second trigger). User-initiated and UNCONDITIONAL (unlike the
    /// storm-guarded reload leg), but still best-effort/fire-and-forget — returns immediately; the profile
    /// fields populate shortly after under the caller's OBO identity. Returns <c>true</c> when the profile
    /// was dispatched.
    /// </summary>
    /// <param name="request">Refresh payload: the <c>sprk_documentid</c> (required) + optional SPE
    /// drive-item id / eTag used only to stamp the profiled version so an immediate reopen does not
    /// redundantly re-trigger.</param>
    /// <param name="httpContext">HTTP context for OBO auth into the profile pipeline. Required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> RefreshProfileAsync(
        RefreshComposeProfileRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-29 read: projects the CURRENT <see cref="AnchoredAnnotation"/> and
    /// <see cref="DefinedTerm"/> collections stored on a Compose session (design.md §8).
    /// Read-only — used internally by <see cref="LoadAsync"/> and available standalone
    /// (e.g., for a Context-pane refresh). Returns empty collections (never null) for a
    /// session with none stored, or for an unknown session id.
    /// </summary>
    /// <param name="tenantId">Tenant id (ADR-015 Tier 3 isolation). Required.</param>
    /// <param name="sessionId">Bound <c>ChatSession</c> id. Required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ComposeAnnotationsState> GetComposeAnnotationsAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-29 write: persists <see cref="AnchoredAnnotation"/> and/or <see cref="DefinedTerm"/>
    /// collections onto the EXISTING Compose session payload (the three-tier <c>ChatSession</c>
    /// stack — no new entity; design.md §8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Partial-replace semantics</b>: a <c>null</c> collection on the request leaves the
    /// corresponding stored collection UNCHANGED; a non-null (possibly empty) collection
    /// REPLACES it wholesale. Annotations are mutable positional UI state — accept / reject /
    /// edit — not an append-only ledger (contrast with <c>ChatSession.Outputs</c>/<c>ToolChains</c>).
    /// </para>
    /// <para>
    /// <b>Path-A boundary (charter §3.4)</b>: this method MUST NOT be confused with
    /// <c>memory.write</c> — it never touches the Memory Service, never routes through the
    /// Context Binder, and the stored collections are never surfaced in the memory
    /// review/delete view. See the class-level remarks on <see cref="AnchoredAnnotation"/>.
    /// </para>
    /// <para>
    /// <b>ADR-040 provenance</b>: any non-null <c>Provenance.LedgerRef</c> on a supplied entry
    /// MUST be in <c>{bindingId}@t{n}</c> form; a malformed ref throws
    /// <see cref="ArgumentException"/> before anything persists.
    /// </para>
    /// </remarks>
    /// <param name="request">Save payload: tenant id + session id + the collection(s) to replace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The session does not exist.</exception>
    Task<ComposeAnnotationsState> SaveComposeAnnotationsAsync(
        SaveComposeAnnotationsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Task 032 (spaarkeai-compose-r6, FR-05) — apply a RESOLVED firm/matter template to a PERSISTED
    /// Compose document: downloads the document's current SPE bytes (OBO), merges its BODY into the
    /// template's chrome via the single <see cref="ComposeTemplatePartMergeEngine"/> (task 030 —
    /// styles/numbering/theme/headers/footers/sectPr from the TEMPLATE, body from the DOCUMENT; never
    /// re-implemented here), persists the merged result as a NEW SPE version through the existing
    /// replace path, and re-projects the persisted bytes into the canonical content model so the
    /// client can re-mount on the response.
    /// </summary>
    /// <remarks>
    /// The merge applies to the PERSISTED bytes — the client guards apply-template on a saved
    /// (non-dirty, non-transient) document. Template STORAGE/variable resolution is the caller's
    /// concern (the endpoint resolves via <c>IComposeTemplateSource</c>, the ADR-013-sanctioned
    /// PublicContracts facade); this method receives only the resolved <c>.dotx</c> bytes —
    /// <c>Services/Compose</c> stays pure (no AI internals, no Graph types — ADR-007/ADR-013).
    /// </remarks>
    /// <param name="httpContext">HTTP context for OBO auth into Graph. Required.</param>
    /// <param name="driveId">SPE drive (container) id. Required.</param>
    /// <param name="documentSpeId">SPE drive-item id of the persisted document. Required.</param>
    /// <param name="resolvedTemplateBytes">The resolved firm/matter <c>.dotx</c> package bytes
    /// (task 031's <c>IComposeTemplateSource.ResolveAsync</c> output). Required, non-empty.</param>
    /// <param name="templateName">Display name of the applied template (for the result/logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="ApplyComposeTemplateResult"/> with the new SPE version id/etag, the
    /// merge degradation warnings (loud, never silent), and the post-merge canonical content model.</returns>
    Task<ApplyComposeTemplateResult> ApplyTemplateAsync(
        HttpContext httpContext,
        string driveId,
        string documentSpeId,
        byte[] resolvedTemplateBytes,
        string templateName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Task 032 (spaarkeai-compose-r6, FR-05) — outcome of
/// <see cref="IComposeService.ApplyTemplateAsync"/>: the new SPE version the merged document was
/// persisted as, the 030 engine's degradation warnings, and the post-merge canonical model the
/// client adopts on re-mount. Standalone (not a <see cref="ComposeDocumentResult"/> — apply-template
/// is session-less; the client re-mounts via the existing Load path).
/// </summary>
public sealed record ApplyComposeTemplateResult
{
    /// <summary>SPE drive-item id (unchanged — the merge replaces content in place).</summary>
    public required string DocumentSpeId { get; init; }

    /// <summary>SPE drive id (echoed from the replace response when available).</summary>
    public string? DriveId { get; init; }

    /// <summary>New SPE version committed by the merge (mirrors the Save path's
    /// <see cref="SaveComposeDocumentResult.VersionId"/> convention).</summary>
    public required string VersionId { get; init; }

    /// <summary>Updated ETag after the replace (Graph response ETag).</summary>
    public string? ETag { get; init; }

    /// <summary>New file size after the merge.</summary>
    public long? Size { get; init; }

    /// <summary>The applied template's display name (the <c>template</c> record's title).</summary>
    public required string TemplateName { get; init; }

    /// <summary>The 030 engine's <c>template-merge-*</c> degradation warnings (codes + counts) —
    /// loud, never silent (operator principle). Null when the merge was clean.</summary>
    public IReadOnlyList<ComposeProjectionWarning>? MergeWarnings { get; init; }

    /// <summary>The canonical content model re-projected from the PERSISTED merged bytes (mirrors the
    /// post-save re-projection) — the client adopts it/re-mounts on the response. Null when the
    /// post-merge projection failed (best-effort; the merge itself still succeeded).</summary>
    public ComposeContentModel? ContentModel { get; init; }

    /// <summary>The post-merge canonical projection's flatten warnings. Null when clean or failed.</summary>
    public IReadOnlyList<ComposeProjectionWarning>? ContentModelWarnings { get; init; }
}

// ---------------------------------------------------------------------------
// DTOs — Compose orchestration shape (FR-04 / FR-05 / FR-06 / FR-07)
// ---------------------------------------------------------------------------

/// <summary>Base type for Compose orchestration outcomes (carries the SPE drive-item id +
/// drive id + bound session id + optionally the resolved <c>sprk_documentid</c>).</summary>
public abstract record ComposeDocumentResult
{
    /// <summary>SPE drive-item id (stable across the lifetime of the file).</summary>
    public required string DocumentSpeId { get; init; }

    /// <summary>SPE drive (container) id — required for round-trip Graph calls on the
    /// drive-item.</summary>
    public string? DriveId { get; init; }

    /// <summary>Bound <c>ChatSession</c> id (the session has its <c>DocumentId</c> set to
    /// <see cref="DocumentSpeId"/> for ephemeral docs, or to
    /// <see cref="DocumentRecordId"/> after promotion).</summary>
    public required string SessionId { get; init; }

    /// <summary><c>sprk_documentid</c> when the document has been promoted (Path A or
    /// post-first-Save); null otherwise (Path B / ephemeral).</summary>
    public Guid? DocumentRecordId { get; init; }
}

/// <summary>
/// G1 (FR-01, task 020): durable cross-session authored-vs-imported origin marker for a Compose
/// <c>sprk_document</c>, persisted on the Dataverse <c>sprk_composeorigin</c> choice field at
/// create-on-save (<see cref="ComposeService.PromoteIfEphemeralAsync"/>) and returned by
/// <see cref="IComposeService.LoadAsync"/> / <see cref="IComposeService.SaveAsync"/> so the client can
/// route a reopened document onto the clean (Authored) or tracked (Imported) save path WITHOUT
/// inferring origin from SPE-id presence or document content (NFR-02 — no text-search/inference in the
/// write path; that fragile discriminator is exactly what G1 replaces).
/// </summary>
/// <remarks>
/// AS-BUILT integer values (owner-created Dataverse choice field — see
/// <c>projects/spaarkeai-compose-r5/notes/g1-origin-field-asbuilt.md</c>). Do NOT renumber:
/// <list type="bullet">
/// <item><see cref="Authored"/> = 100000000 — born in the editor (AI-drafted / blank / edited
/// browse-local): <see cref="ComposeDocumentRenderer"/> authors the whole document from the client's
/// content model; there is no retained baseline to delta onto (the SAME <c>request.ContentModel is not
/// null</c> discriminant <see cref="ComposeService.SaveAsync"/> already uses to select the born-in-editor
/// render branch).</item>
/// <item><see cref="Imported"/> = 100000001 — the save carries retained SPE bytes (upload / browse /
/// open-from-existing <c>.docx</c>). Also the DEFAULT for the Dataverse field itself.</item>
/// </list>
/// <para>
/// <b>NULL-HANDLING (BINDING)</b>: pre-existing <c>sprk_document</c> rows that predate this field return
/// <c>null</c> (no backfill). Every consumer of a nullable <see cref="ComposeOrigin"/> here MUST treat
/// <c>null</c> as <see cref="Imported"/> — NEVER strict-equal a null/missing value to
/// <see cref="Authored"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum ComposeOrigin
{
    Authored = 100000000,
    Imported = 100000001,
}

/// <summary>Upload request payload (R2-reserved; see <see cref="IComposeService.UploadAsync"/>).</summary>
public sealed record UploadComposeDocumentRequest
{
    /// <summary>Caller-supplied file bytes (DOCX).</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>MIME type.</summary>
    public required string MimeType { get; init; }

    /// <summary>Tenant id (ADR-015 Tier 3).</summary>
    public required string TenantId { get; init; }
}

/// <summary>Upload outcome — SPE drive-item id + bound session id; no <c>sprk_documentid</c>
/// yet. R2-reserved.</summary>
public sealed record UploadComposeDocumentResult : ComposeDocumentResult;

/// <summary>Load request payload (Path A or Path B continuation).</summary>
public sealed record LoadComposeDocumentRequest
{
    /// <summary>SPE drive (container) id. Required for Graph drive-item access.</summary>
    public required string DriveId { get; init; }

    /// <summary>SPE drive-item id. Required.</summary>
    public required string DocumentSpeId { get; init; }

    /// <summary>Tenant id (multi-tenant isolation per ADR-015 Tier 3). Required.</summary>
    public required string TenantId { get; init; }

    /// <summary>Bound <c>sprk_documentid</c> when the load is Path A (open from an existing
    /// Document record). Null for Path B continuation (open ephemeral by SPE id).</summary>
    public Guid? DocumentRecordId { get; init; }

    /// <summary>Optional display name carried through for session-binding initialization.
    /// Used only when a new session is created.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// FR-29 (R2): optional known <c>ChatSession</c> id to RESUME so the load restores prior
    /// <see cref="AnchoredAnnotation"/>/<see cref="DefinedTerm"/> state (design.md §8). When
    /// supplied AND a session with this id exists AND is bound to the SAME document identity
    /// being loaded (the <c>DocumentRecordId</c>-or-<c>DocumentSpeId</c> binding key —
    /// see <see cref="ComposeService.LoadAsync"/> remarks), <see cref="LoadAsync"/> reuses it
    /// instead of minting a new session. When absent, not found, or bound to a DIFFERENT
    /// document, <see cref="LoadAsync"/> falls back to minting a fresh session (R1 behavior,
    /// unchanged) — this parameter is purely additive.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// FR-33 (R2, design.md §8): optional Matter identifier completing the cross-version
    /// session binding key <c>DocumentId + MatterId</c> (ADR-040 — augments the EXISTING
    /// FR-29 <see cref="SessionId"/> resume-match; it is NOT a new lookup index or a parallel
    /// session cache). When supplied alongside <see cref="SessionId"/>, the candidate session
    /// must match BOTH the document-identity binding AND this Matter id (via the session's
    /// <see cref="Models.Ai.Chat.ChatHostContext"/> — canonical <c>EntityType == "matter"</c>,
    /// <c>EntityId == MatterId</c>) before it is reused — see
    /// <see cref="ComposeService.LoadAsync"/> remarks. A DOCX version change (Word save) never
    /// changes this key, so the resumed session — and its restored annotations + action
    /// history — survives Word handoffs. A <c>null</c> value preserves the FR-29
    /// DocumentId-only match (backward compatible with callers that predate FR-33). When a NEW
    /// session is minted, this value seeds the session's <c>ChatHostContext</c> so the next
    /// Load can resume by the same key.
    /// </summary>
    public string? MatterId { get; init; }
}

/// <summary>Load outcome — DOCX bytes + session id + (Path A) <c>sprk_documentid</c>.</summary>
public sealed record LoadComposeDocumentResult : ComposeDocumentResult
{
    /// <summary>DOCX bytes loaded from SPE.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>SPE ETag (used for staleness detection on next Save).</summary>
    public string? ETag { get; init; }

    /// <summary>
    /// FR-06 (E1, task 022/027): the LOAD-TIME SPE version id — the drive-item's CURRENT version at the
    /// moment of Load, resolved via <c>ISpeFileOperations.GetCurrentVersionIdAsUserAsync</c>. The client
    /// carries it back on a later dirty save as <see cref="SaveComposeDocumentRequest.BaselineVersionId"/>
    /// so the server can re-fetch this exact baseline even after the client no longer holds the retained
    /// bytes (e.g. a save after a page refresh) — the load-time version stays addressable after the save
    /// advances the item's current version. Null (best-effort) when the item has no version history or the
    /// version lookup was unavailable; the client then relies on the retained-bytes <c>Content</c> fast-path.
    /// </summary>
    public string? VersionId { get; init; }

    /// <summary>File name from SPE metadata.</summary>
    public string? FileName { get; init; }

    /// <summary>File size in bytes.</summary>
    public long? Size { get; init; }

    /// <summary>
    /// FR-29 rehydrate (design.md §8): anchored annotations restored from the resumed/created
    /// session. Empty (never null) when none exist yet or the session was freshly minted.
    /// </summary>
    public IReadOnlyList<AnchoredAnnotation> AnchoredAnnotations { get; init; } = Array.Empty<AnchoredAnnotation>();

    /// <summary>
    /// FR-29 rehydrate (design.md §8): defined-terms tracking restored from the
    /// resumed/created session. Empty (never null) when none exist yet.
    /// </summary>
    public IReadOnlyList<DefinedTerm> DefinedTermsTracking { get; init; } = Array.Empty<DefinedTerm>();

    /// <summary>
    /// FR-33 rehydrate (design.md §8): prior action-history entries — "prior decisions" —
    /// restored from the resumed session's ledger via
    /// <see cref="ComposeService.GetActionHistory(Models.Ai.Chat.ChatSession, string?)"/> (task
    /// 061's read-only ledger query). Restored alongside <see cref="AnchoredAnnotations"/> /
    /// <see cref="DefinedTermsTracking"/> ("prior annotations") whenever a Word round-trip
    /// resumes an existing session (FR-33 cross-version persistence). Empty (never null) for a
    /// freshly-minted session or a resumed session whose ledger has no outputs yet.
    /// </summary>
    public IReadOnlyList<ComposeActionHistoryEntry> ActionHistory { get; init; } = Array.Empty<ComposeActionHistoryEntry>();

    /// <summary>
    /// FR-08 (task 010, E2): the ordered <c>w14:paraId</c> map for the loaded document — one
    /// <see cref="ParaIdMapEntry"/> per body paragraph (incl. table-cell + nested-table paragraphs) in
    /// document order, produced by <see cref="ParaIdPreParser"/> alongside the mammoth convert (which
    /// discards paraId). Existing ids are collected verbatim; missing ids are minted OOXML-valid. The
    /// client (task 011) carries these as hidden node attributes so paraId — the E1 splice key + E2
    /// anchor — survives the editor round-trip. Empty (never null) when the document has no body or the
    /// pre-parse could not read the source (best-effort; Load still returns the content bytes).
    /// </summary>
    public IReadOnlyList<ParaIdMapEntry> ParaIdMap { get; init; } = Array.Empty<ParaIdMapEntry>();

    /// <summary>
    /// Phase-1 mammoth removal (design <c>notes/design-server-side-docx-html-conversion.md</c>): the
    /// server-side DOCX→editor projection — paraId-tagged HTML + status + fidelity warnings, produced by the
    /// single-walk <see cref="ComposeDocxProjectionBuilder"/>. The client mounts <c>Projection.Html</c> via
    /// <c>setContent</c> (the paraId extension parses <c>data-paraid</c>) instead of running mammoth +
    /// position-stamping ids. Fail-closed: the client keys off <see cref="ComposeDocxProjection.Status"/> /
    /// <see cref="ComposeDocxProjection.CanEdit"/>, NOT <c>Html.Length</c>. Defaulted so existing constructions
    /// remain valid; <see cref="ComposeService.LoadAsync"/> always populates it. Tier-3 HTML — never logged.
    /// </summary>
    public ComposeDocxProjection Projection { get; init; } = new()
    {
        Status = ComposeProjectionStatus.Failed,
        CanEdit = false,
    };

    /// <summary>
    /// FR-24 (task 050, import round-trip): the existing native Word tracked changes (<c>w:ins</c>/
    /// <c>w:del</c>, any authorship) recovered from the load-time <c>.docx</c> by the EXISTING
    /// <see cref="DocxAnnotationReader"/> (reused verbatim), run server-side ALONGSIDE the mammoth
    /// convert — which flattens those marks to prose before the editor sees them (<c>docxBridge.ts</c>).
    /// Each recovered <see cref="RecoveredRevision"/> is projected here with the E2 <c>w14:paraId</c>
    /// of its containing paragraph (resolved from <see cref="ParaIdMap"/> by the reader's document-order
    /// <c>ParagraphHint</c>), so the client renders it as a first-class accept/reject-able insertion/
    /// deletion mark anchored by paraId (design §7 — the reader is not the gap, the editor mount is).
    /// Empty (never null) when the document has no revisions, or when the read could not run (best-effort;
    /// a malformed source degrades to no imported revisions, never fails Load).
    /// </summary>
    public IReadOnlyList<ImportedRevision> ImportedRevisions { get; init; } = Array.Empty<ImportedRevision>();

    /// <summary>
    /// FR-25 (task 051, import round-trip): the existing native Word comment threads (<c>w:comment</c>,
    /// any authorship) recovered from the load-time <c>.docx</c> by the SAME EXISTING
    /// <see cref="DocxAnnotationReader"/> (reused verbatim) that projects <see cref="ImportedRevisions"/>
    /// above, run alongside the mammoth convert — which flattens comment anchors to plain prose before
    /// the editor sees them (<c>docxBridge.ts</c>). Each recovered <see cref="RecoveredComment"/> is
    /// projected here with the E2 <c>w14:paraId</c> of its containing paragraph (resolved from
    /// <see cref="ParaIdMap"/> by the reader's document-order <c>ParagraphHint</c>, the identical
    /// <c>ResolveParaIdForHint</c> resolver the revisions projection uses), so the client
    /// groups same-anchor comments into a thread (first = root, rest = flat replies — the reader carries no
    /// modern-comments 4-part structure, so a deeper reply tree is never representable here by construction)
    /// and renders it via the FR-23 <c>ComposeCommentThread</c> UI (task 044) instead of the mammoth-flattened
    /// prose. Empty (never null) when the document has no comments, or when the read could not run
    /// (best-effort; a malformed source degrades to no imported comments, never fails Load).
    /// </summary>
    public IReadOnlyList<ImportedComment> ImportedComments { get; init; } = Array.Empty<ImportedComment>();

    /// <summary>
    /// Task 012 (spaarkeai-compose-r6, the client cutover): the CANONICAL content model projected from
    /// the SAME minted load-time bytes the HTML <see cref="Projection"/> is built from (one mint, two
    /// walks — paraIds agree by construction). The client RETAINS this as the loaded model and, on an
    /// imported dirty save, merges its editor state onto it (untouched blocks pass through VERBATIM,
    /// preserving every server-set field: numbering identity, table structural facts, page breaks,
    /// comments + anchors, revision/format-change facts) and re-posts it as
    /// <c>SaveComposeDocumentRequest.ContentModel</c> + a baseline source — the render-on-save (a1)
    /// shape that replaces the transitional op-log path. Null when the canonical projection failed
    /// (unreadable / over-cap source) — the client then falls back to the transitional op-log save
    /// shape; never fails Load (best-effort, mirrors <see cref="Projection"/>'s posture).
    /// </summary>
    public ComposeContentModel? ContentModel { get; init; }

    /// <summary>Task 013 (012-review F7): the canonical-model projection's counted flatten warnings
    /// (codes + counts, Tier-1 safe) for THIS load - what the thin model could not carry (text boxes,
    /// drawings, ...). Previously server-log-only, so the save that MATERIALIZES the loss showed the
    /// user nothing; the client folds these into the FIRST model-path save's degradation banner.
    /// Null when the projection was clean or failed.</summary>
    public IReadOnlyList<ComposeProjectionWarning>? ContentModelWarnings { get; init; }

    /// <summary>
    /// G1 (FR-01, task 020): the persisted <see cref="ComposeOrigin"/> marker for Path A loads (an
    /// existing <c>sprk_document</c> record — <see cref="ComposeDocumentResult.DocumentRecordId"/> is
    /// non-null). Read from the <c>sprk_composeorigin</c> Dataverse field; <c>null</c> for Path B
    /// continuation (no record yet — nothing to read) OR a legacy pre-existing record with no value
    /// (no backfill — see <see cref="ComposeOrigin"/> remarks). Callers MUST treat a <c>null</c> value
    /// as <see cref="ComposeOrigin.Imported"/> — NEVER strict-equal it to
    /// <see cref="ComposeOrigin.Authored"/>. Drives the client's clean-vs-tracked save routing on reopen
    /// (NFR-02 — no SPE-id/content inference).
    /// </summary>
    public ComposeOrigin? Origin { get; init; }
}

/// <summary>
/// FR-25 (task 051) — one native Word comment recovered on Load, projected for the editor render. A
/// mirror-first projection of <see cref="RecoveredComment"/> (same field vocabulary, NOT a parallel
/// schema) PLUS the E2 <see cref="ParaId"/> of the containing paragraph — the primary anchor the client
/// uses to group same-span comments into a <c>ComposeCommentThreadModel</c> (first recovered comment on a
/// span = thread root, the rest = flat replies) and render it through the FR-23 thread UI. <see cref="AnchorText"/>
/// is the anchored document range the comment is about (NOT the comment's own body — that is
/// <see cref="CommentText"/>). <see cref="ParaId"/> is <c>null</c> when the reader's paragraph index falls
/// outside the paraId map (best-effort; the client then fuzzy-anchors by <see cref="AnchorText"/> +
/// <see cref="ParagraphHint"/>, mirroring <see cref="ImportedRevision"/>'s fallback). Wire shape is
/// camelCase, matching the client <c>ImportedComment</c> mirror in <c>compose-contracts.ts</c>.
/// </summary>
public sealed record ImportedComment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("commentText")] string CommentText,
    [property: JsonPropertyName("anchorText")] string AnchorText,
    [property: JsonPropertyName("paragraphHint")] int ParagraphHint,
    [property: JsonPropertyName("paraId")] string? ParaId);

/// <summary>
/// FR-24 (task 050) — one native Word revision recovered on Load, projected for the editor render.
/// A mirror-first projection of <see cref="RecoveredRevision"/> (same field vocabulary +
/// <see cref="RecoveredAnnotationKind"/>, NOT a parallel schema) PLUS the E2 <see cref="ParaId"/> of the
/// containing paragraph — the primary anchor the client uses to render the revision as a first-class
/// insertion/deletion mark (with the <see cref="ParaIdPreParser"/> fuzzy fallback retained for the
/// cross-Word-session case where Word regenerated paraIds). <see cref="ParaId"/> is <c>null</c> when the
/// reader's paragraph index falls outside the paraId map (best-effort; the client then fuzzy-anchors).
/// Wire shape is camelCase, matching the client <c>ImportedRevision</c> mirror in
/// <c>compose-contracts.ts</c>.
/// </summary>
public sealed record ImportedRevision(
    [property: JsonPropertyName("kind")] RecoveredAnnotationKind Kind,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("anchorText")] string AnchorText,
    [property: JsonPropertyName("paragraphHint")] int ParagraphHint,
    [property: JsonPropertyName("paraId")] string? ParaId);

/// <summary>
/// Task 012 (spaarkeai-compose-r6) — the edit-mount projection bundle returned by
/// <see cref="IComposeService.ProjectForMount"/> for the upload / browse-project doors. NOT a new body
/// model (root §11): <see cref="Projection"/> is the one existing HTML projection and
/// <see cref="ContentModel"/> the one existing canonical model; this record only bundles them with the
/// paraId-minted bytes both were built from so the three stay id-consistent on the client.
/// </summary>
public sealed record ComposeMountProjection
{
    /// <summary>The (possibly) paraId-minted bytes BOTH projections were built from. Identical to the
    /// input when no paragraph needed a mint (the ingest stamp is idempotent + fail-open). The door
    /// returns these to the client as the retained mount bytes so the editor's node ids, the retained
    /// model's block ids, and the save-time carrier stay one id universe.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>True when minting mutated the bytes (at least one id-less paragraph gained an id) —
    /// lets the stateless browse door omit the (large) content echo when nothing changed.</summary>
    public required bool Minted { get; init; }

    /// <summary>The HTML editor projection — the SAME shape <see cref="IComposeService.ProjectDocument"/>
    /// returns and <see cref="LoadComposeDocumentResult.Projection"/> carries.</summary>
    public required ComposeDocxProjection Projection { get; init; }

    /// <summary>The canonical content model (see <see cref="LoadComposeDocumentResult.ContentModel"/> for
    /// the retention/re-post contract). Null when the canonical projection failed — the client falls back
    /// to the transitional op-log save shape.</summary>
    public ComposeContentModel? ContentModel { get; init; }

    /// <summary>Task 013 (012-review F7): the canonical projection's flatten warnings - see
    /// <see cref="LoadComposeDocumentResult.ContentModelWarnings"/>. Null when clean or failed.</summary>
    public IReadOnlyList<ComposeProjectionWarning>? ContentModelWarnings { get; init; }
}

/// <summary>Save request payload.</summary>
/// <remarks>
/// <b>Create-on-save (FR-05)</b>: <see cref="DocumentSpeId"/> and <see cref="DriveId"/> are
/// OPTIONAL. When <see cref="DocumentSpeId"/> is absent the caller is saving a TRANSIENT draft
/// (Browse / Upload / AI-drafted — task 010/012) that has no SPE drive-item yet; the Save then
/// CREATES the drive-item in the client-supplied <see cref="ContainerId"/> under OBO before the
/// record + indexing steps. When <see cref="DocumentSpeId"/> is present the Save replaces the
/// existing item's content (the original R1 behavior). Both cases are idempotent.
/// </remarks>
public sealed record SaveComposeDocumentRequest
{
    /// <summary>SPE drive (container) id. Required for the replace-content path (when
    /// <see cref="DocumentSpeId"/> is present); ignored for the transient create path, where the
    /// drive is derived from <see cref="ContainerId"/>.</summary>
    public string? DriveId { get; init; }

    /// <summary>SPE drive-item id. Null/absent for a TRANSIENT create-on-save draft (FR-05,
    /// Fork B) — the Save creates the drive-item in <see cref="ContainerId"/>. Present for the
    /// replace-content path (a document already backed by SPE).</summary>
    public string? DocumentSpeId { get; init; }

    /// <summary>
    /// CLIENT-SUPPLIED SPE container (or drive) id for the create-on-save path (FR-05, Fork A).
    /// The client resolves this via the existing wizard cascade
    /// (<c>resolveBusinessUnitContainerId</c> → <c>businessunit.sprk_containerid</c>) and passes
    /// it in. Required when <see cref="DocumentSpeId"/> is absent; the BFF does NOT resolve a
    /// business-unit → container mapping server-side (multi-container INV-7 — the resolver stays
    /// in the wizards). Ignored when <see cref="DocumentSpeId"/> is present.
    /// </summary>
    public string? ContainerId { get; init; }

    /// <summary>
    /// E1 (FR-01/FR-06, task 022): the retained load-time original <c>.docx</c> bytes, supplied as the
    /// same-session FAST-PATH baseline. This is the pristine mount payload the client still holds
    /// (<c>state.docxBytes</c>) — NOT a TipTap reconstruction (<c>docx.js</c> is dropped from the export
    /// path; a dirty save never rebuilds the whole document). <see cref="SaveAsync"/> applies
    /// <see cref="EditedParagraphs"/> as a delta ONTO these bytes.
    /// <para>
    /// OPTIONAL (was <c>required</c> pre-R3): when the client no longer holds the original (e.g. a save
    /// after a page refresh), it omits <see cref="Content"/> and instead supplies
    /// <see cref="BaselineVersionId"/> so the BFF re-fetches the load-time SPE version (FR-06 primary).
    /// A transient create-on-save (Browse/Upload/AI-draft with no <see cref="DocumentSpeId"/>) still
    /// supplies <see cref="Content"/> as the document bytes. When present with no
    /// <see cref="EditedParagraphs"/> and no <see cref="Annotations"/>, the baseline is persisted
    /// byte-identical (a clean Save is unchanged; FR-06a — see <c>ComposeServiceUploadFidelityTests</c>).
    /// </para>
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// E1 (FR-06, task 022): the LOAD-TIME SPE version id captured when the document was loaded
    /// (<see cref="LoadComposeDocumentResult.VersionId"/> via <c>ComposeService.LoadAsync</c>). Used to
    /// re-fetch the retained original baseline via <c>ISpeFileOperations.DownloadFileVersionAsUserAsync</c>
    /// (task 002) when <see cref="Content"/> is absent (the client-bytes fast-path is unavailable, e.g. a
    /// save after a page refresh). The load-time version stays addressable even after later dirty saves
    /// advance the item's CURRENT version, so the delta always applies onto the correct baseline. Null on a
    /// same-session save (the <see cref="Content"/> fast-path is used) or a transient create-on-save.
    /// </summary>
    public string? BaselineVersionId { get; init; }

    /// <summary>
    /// R4 FR-06 (task 032, the write-path cutover — supersedes the retired R3 paragraph-diff decision, Path B /
    /// ADR-049): the ordered, rebased task-003 OPERATION LOG the client captured this dirty session
    /// (<c>(paraId, runIndex, run-local-offset)</c>-anchored insertText/deleteRange/replaceRange/setMark/
    /// clearMark + the four structural paragraph ops). When non-empty, <see cref="SaveAsync"/> applies it via
    /// the single <see cref="ComposeShadowPatchEngine"/> onto the resolved baseline (the "base version" =
    /// <see cref="Content"/> or the <see cref="BaselineVersionId"/> re-fetch) — surgical, ID-anchored, ZERO
    /// write-path text-search (I-7), preserving every untouched paragraph + all structure by construction.
    /// This REPLACES the retired <c>ComposeEditedParagraph</c> paragraph-diff payload + its
    /// <c>ComposeParagraphRedlineSynthesizer</c>. Null/empty on a clean Save or a create-on-save (nothing to
    /// apply; the baseline persists byte-identical). An op whose <c>w14:paraId</c>/anchor does not resolve is a
    /// handled <see cref="ComposePatchException"/> (mapped to ProblemDetails), never a silent wrong write.
    /// </summary>
    public ComposeOperationLog? OperationLog { get; init; }

    /// <summary>
    /// R4 FR-06 (task 032): durable, <c>(paraId, range)</c>-anchored comments to emit as native
    /// <c>w:comment</c> via the <see cref="ComposeShadowPatchEngine"/> (its EDGE-1 comment-before-track-change
    /// ordering) — the text-search-free (I-7) replacement for the save-path <see cref="DocxAnnotation"/>
    /// comment payload the retired <c>DocxAnnotationWriter</c> path baked in. Applied together with
    /// <see cref="OperationLog"/> in one engine pass. Null/empty → no comments baked on save (session comments
    /// still persist as mutable UI state via <c>SaveComposeAnnotationsAsync</c> and bake to native OOXML via the
    /// push-annotations path, both unchanged). The text-anchored push-annotations surface is retired/migrated by
    /// task 036 (§6.5 Path-A boundary).
    /// </summary>
    public IReadOnlyList<ComposeAnchoredComment>? Comments { get; init; }

    /// <summary>
    /// E1 born-in-editor (FR-01a, task 026): the paraId-keyed editor CONTENT MODEL — the authoring source for
    /// a document BORN IN THE EDITOR (AI-drafted from a <c>initialHtml</c> seed, blank-new, or browse-local)
    /// that has NO retained load-time original to delta against. When present, <see cref="SaveAsync"/> renders
    /// a high-fidelity <c>.docx</c> server-side via <see cref="ComposeDocumentRenderer"/> (real styles +
    /// style-linked multi-level numbering + native tables + minted <c>w14:paraId</c>) — the deterministic
    /// replacement for the removed client <c>docx.js</c> exporter (task 027). Mutually exclusive with
    /// <see cref="Content"/> / <see cref="BaselineVersionId"/> / <see cref="EditedParagraphs"/> (a born-in-editor
    /// first Save authors the whole document; there is no baseline to delta onto). Null on every loaded-document
    /// save (the E1 edit path). Server-side render-from-a-content-model is deterministic OOXML authoring, NOT an
    /// AI dispatch (ADR-039 complied — design §11).
    /// </summary>
    public ComposeContentModel? ContentModel { get; init; }

    /// <summary>Bound session id (required to keep the session's <c>DocumentId</c> in sync
    /// across the ephemeral → promoted transition).</summary>
    public required string SessionId { get; init; }

    /// <summary>Tenant id (multi-tenant isolation per ADR-015 Tier 3). Required.</summary>
    public required string TenantId { get; init; }

    /// <summary>Existing <c>sprk_documentid</c> when the caller already knows the document
    /// is promoted (Path A). Null for ephemeral-first-Save (Path B); the orchestration will
    /// run <see cref="IComposeService.PromoteIfEphemeralAsync"/> idempotently.</summary>
    public Guid? DocumentRecordId { get; init; }

    /// <summary>Optional display name used only on first-Save promotion (Path B initial
    /// row creation) and as the created drive-item's file name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// C2 fix (UAT 2026-07-20): the client's ordered load-time paraId map — one entry per editor
    /// paragraph in document order carrying its <c>w14:paraId</c> + reject-state text. When present,
    /// <see cref="SaveAsync"/> runs <see cref="ComposeBaselineParaIdStamper"/> to stamp any MINTED ids
    /// (assigned on Load by <see cref="ParaIdPreParser"/> but never written into the source bytes, or
    /// client-minted on an uploaded doc) physically onto the resolved baseline's id-LESS paragraphs
    /// BEFORE the E1 synthesizer/E2 anchoring resolve against it — so an edit/accept on such a paragraph
    /// no longer fails with "w14:paraId matches no paragraph in the retained original". Text-verified +
    /// fill-gaps-only + count-gated (never a wrong write). Null/empty on an older client → the stamper is
    /// skipped and behavior is unchanged.
    /// </summary>
    public IReadOnlyList<ComposeBaselineParaId>? ParaIdMap { get; init; }

    /// <summary>
    /// Task 041 (Phase 4, NDA-REVIEW Summary Page): optional Summary Page content, deterministically derived
    /// from the ONE ledgered NDA-REVIEW result (<c>{overallRisk, flaggedSections[]}</c>, task 023) — NO
    /// second LLM call (see <see cref="ComposeSummaryPageGenerator"/>). When present, <see cref="SaveAsync"/>
    /// appends it as a page-broken, non-tracked section at the END of <c>contentToPersist</c> via
    /// <see cref="ComposeDocumentRenderer.AppendSection"/> (ADR-049; the retired <c>DocxAnnotationWriter</c>
    /// stays retired). Null/absent (every non-NDA-REVIEW Compose save) → no Summary Page appended, unchanged
    /// behavior.
    /// </summary>
    public NdaReviewSummaryPageInput? SummaryPage { get; init; }

    /// <summary>
    /// G7 (FR-06, task 022): a CLIENT-MINTED stable key for a TRANSIENT (not-yet-promoted) Compose draft,
    /// minted once (<c>crypto.randomUUID()</c>) when the draft is mounted and sent on every create-on-save.
    /// It is the durable dedup identity that fixes the 8-duplicate defect: a transient draft has no SPE
    /// drive-item id until its first save mints one, and the transient create-on-save branch minted a NEW
    /// SPE item on EVERY call, so lost/raced round-trips (concurrent saves, a re-created mount, a new tab)
    /// each produced another SPE item → another <c>sprk_document</c> row. Persisted onto the
    /// <c>sprk_composetransientkey</c> column (single-column alt-key <c>sprk_composetransientkey_uk</c>) at
    /// create-on-save; on a subsequent create-on-save with the SAME key, <see cref="SaveAsync"/> resolves the
    /// existing record BY THIS KEY (never by content — I-7/NFR-02) and REPLACES its SPE item in place instead
    /// of minting a duplicate. Null on the replace path (a promoted doc already has its SPE id) and for older
    /// clients that predate G7 (behavior unchanged — no dedup, R1 mint-each-call).
    /// </summary>
    public string? TransientKey { get; init; }

    /// <summary>
    /// G7 (FR-06, task 022): the deliberate <b>Save New Document</b> fork. When <c>true</c>, a create-on-save
    /// SKIPS the <see cref="TransientKey"/> dedup lookup and always mints a fresh SPE item + a fresh
    /// <c>sprk_document</c> row — the user explicitly asked for a NEW document, not a new version of the
    /// existing one. The client pairs this with a freshly-minted <see cref="TransientKey"/> so the forked
    /// document gets its own dedup identity going forward. Default <c>false</c> = <b>Save Version</b> (replace
    /// in place / dedup — the primary action).
    /// </summary>
    public bool ForkNew { get; init; }
}

/// <summary>Save outcome — new SPE version id + resolved <c>sprk_documentid</c>.</summary>
public sealed record SaveComposeDocumentResult : ComposeDocumentResult
{
    /// <summary>New SPE version id committed by this Save. Empty when the operation failed
    /// before a version was committed (e.g. the create-on-save container step failed).</summary>
    public required string VersionId { get; init; }

    /// <summary>Updated ETag after the save (matches Graph's response ETag).</summary>
    public string? ETag { get; init; }

    /// <summary>New file size after save.</summary>
    public long? Size { get; init; }

    /// <summary>True when this Save promoted an ephemeral document (i.e., a new
    /// <c>sprk_document</c> row was created during this call). False for subsequent saves
    /// or for Path A Saves where the row pre-existed. Useful for telemetry + UI signalling
    /// (e.g., enabling "View in record" UX after promotion).</summary>
    public bool WasPromotedThisSave { get; init; }

    /// <summary>
    /// FR-05 per-step create-on-save projection over the existing job pipeline
    /// (<see cref="JobAwareCompletionState"/>): the ordered steps
    /// <c>container → record → profile-analysis → indexing</c> with each step's state. The
    /// <c>profile-analysis</c> step runs in-process fire-and-forget under the caller's OBO identity
    /// (best-effort enrichment via the canonical <c>IDocumentProfileAi</c> facade — #615 resolved),
    /// so the synchronous aggregate is <see cref="JobAwareState.Partial"/> on the
    /// happy path (record exists + indexed, profile dispatched off-thread). A record with no SPE
    /// file OR no index is never a success — see
    /// <see cref="ComposeService.IsInterimCreateOnSaveSuccess"/> for the interim R5-E bar. Null
    /// only for legacy callers that predate FR-05 (always populated by the current Save path).
    /// </summary>
    public JobAwareCompletionState? CompletionState { get; init; }

    /// <summary>
    /// FR-08 (task 050): when this save detected a STALE base (the version stamp this service persisted
    /// after its own last write no longer matched the live SPE eTag), the <see cref="AnnotationReanchorService"/>
    /// band summary for the re-anchored operation log + comments — AUTO (exact paraId match) applied
    /// through the Patch Engine; REVIEW/ORPHAN surfaced here for the client to present, never silently
    /// applied and never silently dropped. Null on every non-stale save (the common case).
    /// </summary>
    public ReanchorSummary? ReanchorSummary { get; init; }

    /// <summary>
    /// Task 026 (FR-04 graceful degradation — success-with-warnings): the render-side DEGRADATION
    /// warnings this save produced (codes + counts only, no document content) — content the authoring
    /// engine had to simplify or drop (filtered comment anchors, failed tracked-format-change parse
    /// gates, unresolvable hyperlink targets). Populated on the born-in-editor render path
    /// (ContentModel present); null/empty = nothing degraded. The save still SUCCEEDS — a hard-tier
    /// construct is never a 422 (the graceful-degradation guarantee); the prior version remains
    /// retrievable via SPE version history (FR-07 safety net).
    /// </summary>
    public IReadOnlyList<ComposeProjectionWarning>? DegradationWarnings { get; init; }

    /// <summary>Task 012 (the client cutover): the canonical model projected from the FINAL persisted
    /// bytes — populated ONLY on render-path saves (the request carried a ContentModel). The client
    /// adopts it as its new retained loaded model and re-baselines its edit snapshot so the next dirty
    /// save merges against the just-persisted state. Null on op-log/clean saves or when the post-save
    /// projection failed (the client then keeps the model it posted as its merge base).</summary>
    public ComposeContentModel? ContentModel { get; init; }

    /// <summary>
    /// Prong 1 (task 055 — keep-edits graceful degradation). Populated ONLY when the loaded-doc apply
    /// hit an op-level anchoring refusal and the service fell back to best-effort per-paragraph recovery:
    /// the resolvable paragraph-units were applied and the unresolvable ops are surfaced here (never
    /// silently applied — the paragraph is the atomic unit under the engine's intra-paragraph sequential
    /// rebasing — and never silently dropped). Null on the common path (the whole batch applied cleanly,
    /// or the refusal was batch-level — malformed docx / schema skew — which still fails hard). The client
    /// shows a banner prompting the user to redo just the unresolved edits; their content is safe (still
    /// in the editor's op-log until they retry).
    /// </summary>
    public PartialApplySummary? PartialApply { get; init; }

    /// <summary>
    /// G1 (FR-01, task 020): the <see cref="ComposeOrigin"/> this save resolved (server-side, from
    /// <c>request.ContentModel is not null</c> — never SPE-id/content inference). Populated on EVERY
    /// save (not only create-on-save) so a caller does not need a subsequent Load to learn the origin
    /// of the document it just saved — e.g. task 021's clean-apply engine mode selection consumes this
    /// directly. On a create-on-save this is also the value persisted onto the new <c>sprk_document</c>
    /// row (see <see cref="ComposeService.PromoteIfEphemeralAsync"/>); on a replace-path save of an
    /// already-promoted document the persisted field is UNCHANGED (origin is set once, at create) even
    /// though this property still reports the save's own resolved discriminant.
    /// </summary>
    public ComposeOrigin? Origin { get; init; }
}

/// <summary>
/// Prong 1 (task 055) — the best-effort partial-apply outcome surfaced on a
/// <see cref="SaveComposeDocumentResult.PartialApply"/>. When a loaded-doc save hit an op-level anchoring
/// refusal, the service applied the resolvable paragraph-units and lists the ops it could NOT anchor here so
/// the client can prompt the user to redo just those edits — never silently applying a wrong edit (the
/// paragraph is the atomic unit under the engine's intra-paragraph sequential rebasing) and never silently
/// dropping one. Serialized camelCase to mirror the client type.
/// </summary>
public sealed record PartialApplySummary(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("appliedCount")] int AppliedCount,
    [property: JsonPropertyName("unresolvedCount")] int UnresolvedCount,
    [property: JsonPropertyName("unresolved")] IReadOnlyList<UnresolvedComposeOp> Unresolved,
    [property: JsonPropertyName("computedAtUtc")] DateTimeOffset ComputedAtUtc);

/// <summary>
/// Prong 1 (task 055) — one op the best-effort recovery could not anchor. Carries the durable
/// <c>w14:paraId</c>, the op discriminator (<c>InsertTextOperation</c>, …), the
/// <c>ComposePatchErrorKind</c> (as a string — the enum lives in the engine namespace, kept off this
/// contract), and the engine's human-readable refusal reason so telemetry + the client banner self-explain.
/// No document content — only anchor metadata (NFR-02 / I-7: nothing content-derived leaves the engine).
/// </summary>
public sealed record UnresolvedComposeOp(
    [property: JsonPropertyName("paraId")] string ParaId,
    [property: JsonPropertyName("opType")] string OpType,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// G10 (FR-09, task 040): manual "Refresh Profile" payload — re-run the Document Profile on demand for an
/// existing <c>sprk_document</c>. See <see cref="IComposeService.RefreshProfileAsync"/>.
/// </summary>
public sealed record RefreshComposeProfileRequest
{
    /// <summary>The <c>sprk_documentid</c> to re-profile. Required.</summary>
    public required Guid DocumentRecordId { get; init; }

    /// <summary>Tenant id (ADR-015 Tier 3 isolation). Required.</summary>
    public required string TenantId { get; init; }

    /// <summary>Optional SPE drive-item id — used only to stamp the profiled version (with
    /// <see cref="ETag"/>) so an immediate reopen does not redundantly re-trigger the storm-guarded
    /// reload leg.</summary>
    public string? DocumentSpeId { get; init; }

    /// <summary>Optional current SPE eTag — stamped as the profiled version when supplied alongside
    /// <see cref="DocumentSpeId"/>.</summary>
    public string? ETag { get; init; }
}

/// <summary>Promote request payload.</summary>
public sealed record PromoteComposeDocumentRequest
{
    /// <summary>SPE drive-item id. Required.</summary>
    public required string DocumentSpeId { get; init; }

    /// <summary>Bound session id (for the FR-07 ephemeral→promoted DocumentId rebind).
    /// Required.</summary>
    public required string SessionId { get; init; }

    /// <summary>Tenant id (multi-tenant isolation per ADR-015 Tier 3). Required.</summary>
    public required string TenantId { get; init; }

    /// <summary>Optional display name used only when a new row is created.</summary>
    public string? DisplayName { get; init; }

    // ── SPE pointer + file metadata (threaded from SaveAsync's already-computed upload result) ──
    // Written onto the new sprk_document so the record is COMPLETE — carrying the SPE drive
    // pointer + has-file flag + size/mime/filepath, mirroring the canonical
    // OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync write. Without these the
    // record has only the item-id, and every downstream reader (open-links, preview) 409s
    // "No file is attached to this document yet." All optional: a standalone /promote caller
    // that supplies none still creates a row (sprk_hasfile is set true by precondition — the
    // drive-item id itself proves a file exists), just without the drive pointer.

    /// <summary>SPE drive (container) id → <c>sprk_graphdriveid</c>. The field whose absence is
    /// the root cause of the "no file attached" 409s.</summary>
    public string? GraphDriveId { get; init; }

    /// <summary>Resolved file name (carries the <c>.docx</c> extension) → <c>sprk_filename</c>.
    /// Preferred over <see cref="DisplayName"/> for the file-name column when supplied.</summary>
    public string? FileName { get; init; }

    /// <summary>File size in bytes → <c>sprk_filesize</c> (cast to the Whole Number column).</summary>
    public long? FileSize { get; init; }

    /// <summary>MIME type → <c>sprk_mimetype</c> (DOCX for Compose).</summary>
    public string? MimeType { get; init; }

    /// <summary>SPE web URL → <c>sprk_filepath</c> (enables "Open in SharePoint" links).</summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// G1 (FR-01, task 020): the <see cref="ComposeOrigin"/> to persist onto <c>sprk_composeorigin</c>
    /// WHEN a new <c>sprk_document</c> row is created by this call (threaded from
    /// <see cref="ComposeService.SaveAsync"/>'s <c>request.ContentModel is not null</c> discriminant —
    /// never SPE-id/content inference). Ignored on the idempotent existing-row path (origin is set
    /// ONLY at create-on-save; a later replace-path save never mutates it). Defaults to
    /// <see cref="ComposeOrigin.Imported"/> — the Dataverse field's own default — when a caller (e.g. a
    /// standalone <c>/promote</c> call that predates G1) supplies none.
    /// </summary>
    public ComposeOrigin? Origin { get; init; }

    /// <summary>
    /// G7 (FR-06, task 022): the client-minted transient dedup key to STAMP onto <c>sprk_composetransientkey</c>
    /// WHEN a new <c>sprk_document</c> row is created by this call (threaded from
    /// <see cref="ComposeService.SaveAsync"/>). Ignored on the idempotent existing-row path (the key is set
    /// ONCE, at create-on-save). Null for a replace-path save or an older client that predates G7. Enables the
    /// next create-on-save with the same key to resolve THIS record via the <c>sprk_composetransientkey_uk</c>
    /// alt-key and replace in place instead of minting a duplicate. See <see cref="SaveComposeDocumentRequest.TransientKey"/>.
    /// </summary>
    public string? TransientKey { get; init; }
}

/// <summary>Promote outcome — resolved <c>sprk_documentid</c> + a flag distinguishing
/// "row created this call" from "row already existed".</summary>
public sealed record PromoteComposeDocumentResult : ComposeDocumentResult
{
    /// <summary>True when the <c>sprk_document</c> row was created in this call. False
    /// when an existing row was returned (idempotent behavior on repeated Save).</summary>
    public required bool WasCreated { get; init; }
}

/// <summary>
/// FR-29 (design.md §8) — the CURRENT state of the two Compose-domain session collections.
/// Returned by <see cref="IComposeService.GetComposeAnnotationsAsync"/> and
/// <see cref="IComposeService.SaveComposeAnnotationsAsync"/>.
/// </summary>
public sealed record ComposeAnnotationsState
{
    /// <summary>Current anchored annotations (empty, never null).</summary>
    public required IReadOnlyList<AnchoredAnnotation> AnchoredAnnotations { get; init; }

    /// <summary>Current defined-terms tracking (empty, never null).</summary>
    public required IReadOnlyList<DefinedTerm> DefinedTermsTracking { get; init; }
}

/// <summary>FR-29 save request — partial-replace payload for a session's Compose-domain collections. See <see cref="IComposeService.SaveComposeAnnotationsAsync"/> remarks for semantics.</summary>
public sealed record SaveComposeAnnotationsRequest
{
    /// <summary>Tenant id (ADR-015 Tier 3 isolation). Required.</summary>
    public required string TenantId { get; init; }

    /// <summary>Bound <c>ChatSession</c> id. Required — the session must already exist.</summary>
    public required string SessionId { get; init; }

    /// <summary>New anchored-annotations collection. <c>null</c> leaves the stored collection unchanged; a non-null (possibly empty) value replaces it wholesale.</summary>
    public IReadOnlyList<AnchoredAnnotation>? AnchoredAnnotations { get; init; }

    /// <summary>New defined-terms-tracking collection. <c>null</c> leaves the stored collection unchanged; a non-null (possibly empty) value replaces it wholesale.</summary>
    public IReadOnlyList<DefinedTerm>? DefinedTermsTracking { get; init; }
}
