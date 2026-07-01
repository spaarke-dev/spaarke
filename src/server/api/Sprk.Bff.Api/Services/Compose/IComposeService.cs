using Microsoft.AspNetCore.Http;

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
/// <c>IConsumerRoutingService</c> + <c>IInvokePlaybookAi</c> from
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
}

/// <summary>Load outcome — DOCX bytes + session id + (Path A) <c>sprk_documentid</c>.</summary>
public sealed record LoadComposeDocumentResult : ComposeDocumentResult
{
    /// <summary>DOCX bytes loaded from SPE.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>SPE ETag (used for staleness detection on next Save).</summary>
    public string? ETag { get; init; }

    /// <summary>File name from SPE metadata.</summary>
    public string? FileName { get; init; }

    /// <summary>File size in bytes.</summary>
    public long? Size { get; init; }
}

/// <summary>Save request payload.</summary>
public sealed record SaveComposeDocumentRequest
{
    /// <summary>SPE drive (container) id. Required.</summary>
    public required string DriveId { get; init; }

    /// <summary>SPE drive-item id. Required.</summary>
    public required string DocumentSpeId { get; init; }

    /// <summary>DOCX bytes to save. Required.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

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
    /// row creation).</summary>
    public string? DisplayName { get; init; }
}

/// <summary>Save outcome — new SPE version id + resolved <c>sprk_documentid</c>.</summary>
public sealed record SaveComposeDocumentResult : ComposeDocumentResult
{
    /// <summary>New SPE version id committed by this Save.</summary>
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
}

/// <summary>Promote outcome — resolved <c>sprk_documentid</c> + a flag distinguishing
/// "row created this call" from "row already existed".</summary>
public sealed record PromoteComposeDocumentResult : ComposeDocumentResult
{
    /// <summary>True when the <c>sprk_document</c> row was created in this call. False
    /// when an existing row was returned (idempotent behavior on repeated Save).</summary>
    public required bool WasCreated { get; init; }
}
