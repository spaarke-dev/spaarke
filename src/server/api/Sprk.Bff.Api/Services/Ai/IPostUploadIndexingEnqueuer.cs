using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Centralized helper that enqueues post-upload RAG indexing for files written to
/// SharePoint Embedded via the BFF upload pipeline. Single seam — every BFF upload
/// endpoint that writes to SPE calls this after success.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by the upload-indexing centralization fix (scope extension to
/// multi-container-multi-index-r1) to close the architectural gap where files
/// uploaded via the Create* wizards (Matter / Project / WorkAssignment / Event)
/// landed in SPE but were never enqueued for tenant RAG indexing — and the
/// SprkChat persist path had the same gap.
/// </para>
/// <para>
/// The implementation:
/// <list type="bullet">
///   <item>Resolves <see cref="RagIndexingJobPayload.SearchIndexName"/> later in
///     <see cref="Jobs.Handlers.RagIndexingJobHandler"/> via
///     <see cref="ISearchIndexNameResolver"/> when the caller passes null —
///     parent record → BU cascade is unchanged.</item>
///   <item>Uses idempotency key <c>rag-index-{driveId}-{itemId}</c> so duplicate
///     uploads (e.g., client retries) don't double-index.</item>
///   <item>Is non-fatal — failure to enqueue is logged at WARN; never propagates
///     to the caller. RAG indexing is best-effort; the SPE upload contract with
///     the user is what matters.</item>
///   <item>Skips enqueue when the feature flag is off, the file is empty, the
///     content type is non-indexable (video/audio/archives), the file exceeds
///     the size cap, or tenant context is missing.</item>
/// </list>
/// </para>
/// <para>
/// Replaces previously duplicated inline enqueue blocks in
/// <see cref="Workers.Office.UploadFinalizationWorker"/>,
/// <see cref="Services.Communication.IncomingCommunicationProcessor"/>, and
/// several internal AI flow sites (see design doc §3.3).
/// </para>
/// </remarks>
public interface IPostUploadIndexingEnqueuer
{
    /// <summary>
    /// Synchronously indexes a USER-OBO-uploaded SPE file in the OBO request scope
    /// via <see cref="IFileIndexingService.IndexFileAsync"/>. Use from BFF endpoints
    /// that handle user-initiated uploads (Create* wizards, SprkChat persist).
    /// </summary>
    /// <param name="request">Upload outcome + context. <see cref="PostUploadIndexingRequest.TenantId"/>,
    /// <see cref="PostUploadIndexingRequest.DriveId"/>, <see cref="PostUploadIndexingRequest.ItemId"/>,
    /// and <see cref="PostUploadIndexingRequest.FileName"/> are required.</param>
    /// <param name="httpContext">HTTP context — required for OBO token extraction. The indexing
    /// runs as the user (same identity that just wrote the file to SPE), which is the only identity
    /// guaranteed read access on the file's SPE ACL (per <c>sdap-auth-patterns.md</c> Pattern 4).</param>
    /// <param name="ct">Cancellation token (request-scope).</param>
    /// <returns>Result indicating whether indexing succeeded, was skipped, or failed.</returns>
    /// <remarks>
    /// **SPE writer-identity rule** (per Pattern 4): user-OBO-uploaded files can ONLY be read by
    /// the same user, unless the reader's app id is explicitly registered on the SPE container type.
    /// Spaarke's MI is intentionally NOT registered. Therefore this method MUST dispatch synchronously
    /// in the OBO request scope. NEVER enqueue a Service Bus job for a user-OBO-uploaded file — the
    /// MI-based handler will 403 on the SPE download.
    /// </remarks>
    Task<PostUploadIndexingResult> EnqueueIfApplicableAsync(
        PostUploadIndexingRequest request,
        HttpContext httpContext,
        CancellationToken ct);

    /// <summary>
    /// Asynchronously enqueues RAG indexing of an MI-WRITTEN SPE file via Service Bus.
    /// Use from background workers (<c>UploadFinalizationWorker</c>,
    /// <c>IncomingCommunicationProcessor</c>, <c>AnalysisResultPersistence</c>) where
    /// the file was written by MI itself — MI can read its own writes via the SPE ACL.
    /// </summary>
    /// <param name="request">Indexing request. Same shape as the OBO method.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating whether the job was submitted, skipped, or failed.</returns>
    /// <remarks>
    /// **SPE writer-identity rule** (per Pattern 4): this method may ONLY be called from contexts
    /// where the file in question was written by the BFF's Managed Identity (Office Add-in
    /// finalization, Email-to-Document processing, internal AI workflow re-index). Calling this
    /// for user-OBO-uploaded files will 403 when the Service Bus job handler attempts the SPE
    /// download under MI auth.
    /// </remarks>
    Task<PostUploadIndexingResult> EnqueueAppOnlyIfApplicableAsync(
        PostUploadIndexingRequest request,
        CancellationToken ct);
}

/// <summary>
/// Input contract for <see cref="IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync"/>.
/// Every BFF upload endpoint builds one of these after a successful SPE write.
/// </summary>
/// <param name="TenantId">Azure AD tenant ID for the upload. Required — empty/missing
/// triggers a skip with ERROR log (indicates misconfigured upload path).</param>
/// <param name="DriveId">SPE drive ID. Required.</param>
/// <param name="ItemId">SPE item ID returned by the upload. Required — used in the
/// idempotency key.</param>
/// <param name="FileName">File name (including extension). Required for skip-list
/// evaluation and downstream display.</param>
/// <param name="FileSizeBytes">File size in bytes. Zero triggers a skip
/// (empty file = nothing to index). Null = unknown — size-based skips bypass.
/// (Some callers, like background-finalization workers, don't have the size
/// handy at the enqueue site.)</param>
/// <param name="ContentType">MIME content type if known (may be null when not
/// reported by the upload pipeline). Skip-list filtering uses this when present.</param>
/// <param name="DocumentId">Optional Dataverse <c>sprk_document</c> ID. When the
/// upload created or linked to a Dataverse record, pass it here so the indexer
/// can correlate.</param>
/// <param name="ParentEntity">Optional parent entity context (Matter/Project/etc.).
/// When provided, <see cref="ISearchIndexNameResolver"/> uses it for the index
/// name cascade.</param>
/// <param name="SearchIndexName">Optional explicit index name. When the caller has
/// already resolved it (e.g., wizard had it pre-resolved), pass it here to avoid
/// a second resolver lookup downstream. When null, the handler runs the resolver
/// chain.</param>
/// <param name="Source">Identifies the caller for telemetry / debugging. Suggested
/// values: <c>SpeContainerUpload</c>, <c>OboUploadSession</c>,
/// <c>DirectContainerUpload</c>, <c>DirectUploadSession</c>, <c>ChatPersist</c>,
/// <c>OfficeAddin</c>, <c>EmailToDocument</c>, <c>EnqueueEndpoint</c>.</param>
/// <param name="CorrelationId">Correlation ID for distributed tracing. Should be
/// the inbound request's correlation ID (typically <c>HttpContext.TraceIdentifier</c>
/// or <c>Activity.Current?.Id</c>).</param>
public sealed record PostUploadIndexingRequest(
    string TenantId,
    string DriveId,
    string ItemId,
    string FileName,
    long? FileSizeBytes,
    string? ContentType,
    string? DocumentId,
    ParentEntityContext? ParentEntity,
    string? SearchIndexName,
    string Source,
    string CorrelationId);

/// <summary>
/// Outcome of an enqueue attempt. Returned for observability + test assertions —
/// callers do not need to inspect this (the helper handles all logging itself).
/// </summary>
public sealed record PostUploadIndexingResult
{
    /// <summary>Whether a job was actually submitted to Service Bus.</summary>
    public required bool JobSubmitted { get; init; }

    /// <summary>Job ID if submitted, null if skipped or failed.</summary>
    public Guid? JobId { get; init; }

    /// <summary>If skipped, the reason. Null when submitted or failed.</summary>
    public string? SkipReason { get; init; }

    /// <summary>If failed, the exception type name. Null when submitted or skipped.</summary>
    public string? FailureReason { get; init; }

    public static PostUploadIndexingResult Submitted(Guid jobId) =>
        new() { JobSubmitted = true, JobId = jobId };

    public static PostUploadIndexingResult Skipped(string reason) =>
        new() { JobSubmitted = false, SkipReason = reason };

    public static PostUploadIndexingResult Failed(string reason) =>
        new() { JobSubmitted = false, FailureReason = reason };
}
