using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Service interface for Office add-in operations.
/// Provides save, share, and search functionality for Outlook and Word add-ins.
/// </summary>
/// <remarks>
/// <para>
/// This service orchestrates the Office add-in workflows:
/// - Saving emails, attachments, and documents to SPE containers
/// - Creating Dataverse records for tracking
/// - Triggering AI processing jobs
/// - Managing job status and streaming updates
/// </para>
/// <para>
/// Implementation follows ADR-010 DI minimalism - this is the single service
/// interface for all Office operations, with specialized handlers injected.
/// </para>
/// </remarks>
public interface IOfficeService
{
    /// <summary>
    /// Saves content (email, attachment, or document) from an Office add-in.
    /// </summary>
    /// <param name="request">Save request with content metadata.</param>
    /// <param name="userId">Authenticated user ID.</param>
    /// <param name="httpContext">HTTP context for OBO authentication (used to fetch email body via Graph API).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Save response with job tracking information.</returns>
    Task<SaveResponse> SaveAsync(
        SaveRequest request,
        string userId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a processing job.
    /// Returns null if job not found or if user doesn't own the job.
    /// </summary>
    /// <param name="jobId">Processing job ID.</param>
    /// <param name="userId">User ID for authorization check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Job status response, or null if not found or unauthorized.</returns>
    Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a processing job without ownership validation.
    /// Used by authorization filters for ownership checks.
    /// </summary>
    /// <param name="jobId">Processing job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Job status response, or null if not found.</returns>
    Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the service is healthy and ready to accept requests.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if healthy.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for association target entities (Matters, Projects, Invoices, Accounts, Contacts).
    /// </summary>
    /// <param name="request">Search request with query, entity types, and pagination.</param>
    /// <param name="userId">Authenticated user ID for permission filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with matched entities.</returns>
    /// <remarks>
    /// <para>
    /// Searches across multiple Dataverse tables based on the requested entity types.
    /// Results are filtered to only include entities the user has access to.
    /// </para>
    /// <para>
    /// Search is performed against primary name fields and optionally email fields:
    /// - Matter: sprk_name
    /// - Project: sprk_name
    /// - Invoice: sprk_invoicenumber, sprk_name
    /// - Account: name
    /// - Contact: fullname, emailaddress1
    /// </para>
    /// </remarks>
    Task<EntitySearchResponse> SearchEntitiesAsync(
        EntitySearchRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for documents to share from the Office add-in.
    /// Returns documents the user has permission to share.
    /// </summary>
    /// <param name="request">Search request with query, filters, and pagination.</param>
    /// <param name="userId">Authenticated user ID for permission filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with matched documents and metadata for preview.</returns>
    /// <remarks>
    /// <para>
    /// Searches the sprk_document entity and filters results based on:
    /// - User's share permissions (only returns shareable documents)
    /// - Association type/ID if specified
    /// - Container/folder if specified
    /// - Content type if specified
    /// - Date range if specified
    /// </para>
    /// <para>
    /// Results include thumbnail URLs and association info for UI preview.
    /// </para>
    /// </remarks>
    Task<DocumentSearchResponse> SearchDocumentsAsync(
        DocumentSearchRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates shareable links for the specified documents.
    /// </summary>
    /// <param name="request">Share links request containing document IDs and options.</param>
    /// <param name="userId">Authenticated user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Share links response with URLs and any invitations created.</returns>
    /// <remarks>
    /// <para>
    /// Generated links resolve through Spaarke access controls. The link format
    /// is configurable via ShareLinkBaseUrl setting.
    /// </para>
    /// <para>
    /// Supports partial success - documents the user cannot share will be returned
    /// in the Errors array, while accessible documents will have links generated.
    /// </para>
    /// </remarks>
    Task<ShareLinksResponse> CreateShareLinksAsync(
        ShareLinksRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new entity (Matter, Project, Invoice, Account, Contact) with minimal fields.
    /// </summary>
    /// <param name="entityType">Type of entity to create.</param>
    /// <param name="request">Quick create request with entity fields.</param>
    /// <param name="userId">Authenticated user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quick create response with created entity details, or null if creation is unavailable for the type.</returns>
    /// <exception cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException">
    /// Matter only (spaarkeai-word-add-in-r1 task 030): the server-side creation service refused — the caller has
    /// no Dataverse user (403) or the request is invalid (400). No row was written; the exception carries the stable
    /// code and HTTP status.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This supports inline entity creation from the Office add-in when the user
    /// needs to create a new association target that doesn't exist yet.
    /// </para>
    /// <para>
    /// Field requirements vary by entity type:
    /// - Matter/Project/Invoice/Account: Name required
    /// - Contact: FirstName and LastName required
    /// </para>
    /// </remarks>
    Task<QuickCreateResponse?> QuickCreateAsync(
        QuickCreateEntityType entityType,
        QuickCreateRequest request,
        string userId,
        string? ownerSystemUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a first-class <c>sprk_todo</c> from the add-in inline "Create To Do"
    /// (email-communication-intelligence-r2, #3), regarding the record the email was filed to.
    /// </summary>
    /// <param name="request">Create-To-Do request (name, description, contact assignee, due date, priority/effort scores, regarding).</param>
    /// <param name="userId">Authenticated user id (OBO oid).</param>
    /// <param name="ownerSystemUserId">Caller's resolved <c>systemuserid</c> for <c>ownerid</c> attribution (ADR-024); null → app-owned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created To Do id + name, or null when creation is unavailable (no generic-create dep injected).</returns>
    /// <remarks>
    /// <para>
    /// Targets <c>sprk_todo</c> (NOT <c>sprk_event</c>) — mirroring the <c>CreateTodoWizard</c> field set. The
    /// regarding is written via the entity-specific lookup (<c>sprk_regardingmatter</c>/<c>project</c>/<c>invoice</c>)
    /// plus the ADR-024 denormalized resolver fields (id/name, and a best-effort record-type ref). App-only create
    /// via <see cref="IGenericEntityService"/> with <c>ownerid</c> = caller (same posture as
    /// <see cref="QuickCreateAsync"/> — no impersonated-create helper exists in the BFF).
    /// </para>
    /// </remarks>
    Task<CreateTodoResponse?> CreateTodoAsync(
        CreateTodoRequest request,
        string userId,
        string? ownerSystemUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-08 (spaarkeai-word-add-in-r1 task 022): re-runs document profiling for the given
    /// <c>sprk_document</c> on user request from the pane's "Generate Profile" control. Fire-and-forget,
    /// best-effort — mirrors Compose's shipped <c>refresh-profile</c> semantics exactly: dispatches the
    /// SAME OBO direct-Action profile pipeline (<c>IDocumentProfileAi</c>) and returns as soon as the
    /// DISPATCH DECISION is made (fast, synchronous), never awaiting the profile itself. Unconditionally
    /// OVERWRITES any existing profile — no confirmation, no idempotency gate (deliberately NOT the
    /// <c>AppOnlyDocumentAnalysis</c> Service-Bus job path, whose idempotency key would silently skip an
    /// already-profiled or Failed document).
    /// </summary>
    /// <param name="documentId">The target <c>sprk_document</c> id. Caller (the endpoint filter) has
    /// already authorized <c>write</c> on this record.</param>
    /// <param name="httpContext">The current request context — the OBO bearer token and claims are
    /// captured from it before the background dispatch detaches.</param>
    /// <param name="cancellationToken">Request-scope cancellation token (not used by the detached
    /// background profile itself, which runs under the app-shutdown token instead).</param>
    /// <returns>
    /// The dispatch outcome (<see cref="GenerateProfileDispatchOutcome"/>). The caller MUST branch on
    /// this — coordinator-review fix: an earlier draft ignored a bare <see langword="bool"/> return and
    /// always answered 202, which let the endpoint claim success for a profile that would never run
    /// (compound AI gate off). Only <see cref="GenerateProfileDispatchOutcome.Dispatched"/> may produce
    /// a 202; <see cref="GenerateProfileDispatchOutcome.FacadeUnavailable"/> and
    /// <see cref="GenerateProfileDispatchOutcome.NoBearer"/> are honest non-success outcomes the endpoint
    /// maps to 503 and 401 respectively.
    /// </returns>
    Task<GenerateProfileDispatchOutcome> GenerateProfileAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recently used association targets and documents for the user.
    /// </summary>
    /// <param name="userId">Authenticated user ID.</param>
    /// <param name="top">Maximum number of items to return per category (default: 10, max: 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing recent associations, documents, and favorites.</returns>
    /// <remarks>
    /// <para>
    /// Recent items are tracked when users save documents via the Office add-in.
    /// Items are sorted by most recently used and filtered to only include
    /// entities the user still has access to.
    /// </para>
    /// <para>
    /// Storage mechanism: Recent items are stored in Redis sorted sets per user
    /// for efficient retrieval. Keys expire after 30 days of inactivity.
    /// </para>
    /// <para>
    /// Categories returned:
    /// - RecentAssociations: Entities used as save targets (Matter, Project, etc.)
    /// - RecentDocuments: Documents the user has accessed/modified
    /// - Favorites: User-pinned entities (persisted in Dataverse)
    /// </para>
    /// </remarks>
    Task<RecentDocumentsResponse> GetRecentDocumentsAsync(
        string userId,
        int top = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves documents and packages them for attachment to Outlook compose emails.
    /// </summary>
    /// <param name="request">Request containing document IDs and delivery mode.</param>
    /// <param name="userId">Authenticated user ID for permission verification.</param>
    /// <param name="correlationId">Correlation ID for request tracing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing packaged attachments and any errors.</returns>
    /// <remarks>
    /// <para>
    /// Each document is validated for:
    /// - Existence in Dataverse
    /// - User share permission via UAC
    /// - Size limits (25MB per file, 100MB total per spec NFR-03)
    /// </para>
    /// <para>
    /// Partial success is allowed - some documents may succeed while others fail.
    /// Failed documents are reported in the Errors array.
    /// </para>
    /// </remarks>
    Task<ShareAttachResponse> GetAttachmentsAsync(
        ShareAttachRequest request,
        string userId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams job status updates via Server-Sent Events (SSE).
    /// </summary>
    /// <param name="jobId">Processing job ID.</param>
    /// <param name="lastEventId">Last-Event-ID from reconnection header (for resume support).</param>
    /// <param name="cancellationToken">Cancellation token for connection termination.</param>
    /// <returns>AsyncEnumerable of SSE event data (already formatted as bytes).</returns>
    /// <remarks>
    /// <para>
    /// Per spec.md, this method MUST:
    /// - Send initial status event on connection
    /// - Send heartbeat events every 15 seconds
    /// - Stream phase transition and progress updates
    /// - Send job-complete or job-failed terminal event
    /// - Support reconnection via Last-Event-ID header
    /// </para>
    /// <para>
    /// Event types:
    /// - connected: Initial connection established
    /// - stage-update: Job phase changed
    /// - progress: Progress percentage updated
    /// - heartbeat: Keep-alive signal
    /// - job-complete: Job finished successfully
    /// - job-failed: Job encountered an error
    /// - error: Terminal error (per ADR-019)
    /// </para>
    /// </remarks>
    IAsyncEnumerable<byte[]> StreamJobStatusAsync(
        Guid jobId,
        string? lastEventId,
        CancellationToken cancellationToken = default);
}
