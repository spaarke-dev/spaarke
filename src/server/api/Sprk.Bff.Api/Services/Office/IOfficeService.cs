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
    /// <param name="userId">The caller's Entra object id (<c>oid</c>), for logging and correlation.</param>
    /// <param name="callerSystemUserId">
    /// The caller's Dataverse <c>systemuserid</c>, resolved by the endpoint via
    /// <c>ICallerSystemUserResolver</c>. REQUIRED and non-empty: the search query is issued
    /// IMPERSONATED as this user (<c>MSCRMCallerID</c>) so Dataverse applies row-level security
    /// natively. Deliberately a required positional parameter with no default — omitting it must be a
    /// compile error, not a silent reversion to the tenant-wide app-only enumeration that finding F1
    /// closed (task 062). <see cref="System.Guid.Empty"/> is refused by the implementation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with matched entities.</returns>
    /// <remarks>
    /// <para>
    /// Searches across multiple Dataverse tables based on the requested entity types.
    /// Results are filtered to only include entities the user has access to — by Dataverse itself,
    /// inside the query, for every entity type and every page.
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
        Guid callerSystemUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the active rows of one of the create form's reference lists — matter types (task 038), practice
    /// areas and project types (task 100). See <see cref="OfficeSearchService.ReferenceLists"/>.
    /// </summary>
    /// <param name="list">The list, from <see cref="OfficeSearchService.TryGetReferenceList"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active rows, ordered by name.</returns>
    /// <remarks>
    /// <para>
    /// Small, load-once reference lists — siblings of <see cref="SearchEntitiesAsync"/> under the same
    /// <c>/api/office/search</c> group, not filters on it. They are reference/lookup tables, not association-target
    /// entities, and the caller loads each once rather than per keystroke, so they do not fit the
    /// 2-character-minimum typeahead contract.
    /// </para>
    /// </remarks>
    Task<ReferenceListResponse> GetReferenceListAsync(
        OfficeReferenceList list,
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
    /// <param name="ownerSystemUserId">Caller's resolved <c>systemuserid</c> — an INPUT to the owner-team resolution, not the owner (task 080: every record created here is owned by a business-unit default owner team, and the create is refused with OFFICE_022 when none resolves — never app-owned).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created To Do id + name, or null when the request carries no name (the endpoint validates the name first, so this is a defensive guard).</returns>
    /// <remarks>
    /// <para>
    /// Targets <c>sprk_todo</c> (NOT <c>sprk_event</c>) — mirroring the <c>CreateTodoWizard</c> field set. The
    /// regarding is written via the entity-specific lookup (<c>sprk_regardingmatter</c>/<c>project</c>/<c>invoice</c>)
    /// plus the ADR-024 denormalized resolver fields (id/name, and a best-effort record-type ref). App-only create
    /// via <see cref="IGenericEntityService"/>, owned by a business-unit default owner team resolved record-first
    /// (regarding record → document → communication → caller; task 080), refused with OFFICE_022 when none resolves.
    /// </para>
    /// </remarks>
    Task<CreateTodoResponse?> CreateTodoAsync(
        CreateTodoRequest request,
        string userId,
        string? ownerSystemUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-08 (task 022; durable since task 068, #1086): requests a fresh document profile for the given
    /// <c>sprk_document</c> from the pane's "Generate Profile" control. Queues ONE <c>AppOnlyDocumentAnalysis</c>
    /// job whose key carries this request's id (task 029's discriminator), so every click runs, including on an
    /// already-profiled or Failed document, with no confirmation; and returns once the job is on the queue, never
    /// awaiting the profile. See <see cref="OfficeProfileQueue"/>.
    /// </summary>
    /// <param name="documentId">The target <c>sprk_document</c> id. Caller (the endpoint filter) has
    /// already authorized <c>write</c> on this record.</param>
    /// <param name="httpContext">The current request: its trace id is the job's correlation id, and its caller is
    /// recorded as the requester.</param>
    /// <param name="cancellationToken">Unused: once started, the submit is not abandoned (see
    /// <see cref="OfficeProfileQueue.QueueAsync"/>).</param>
    /// <returns>
    /// The outcome and, when queued, the job's id (<see cref="GenerateProfileResult"/>). The caller MUST branch on it:
    /// only <see cref="GenerateProfileDispatchOutcome.Dispatched"/> may produce a 202;
    /// <see cref="GenerateProfileDispatchOutcome.FacadeUnavailable"/> (profiling off) and
    /// <see cref="GenerateProfileDispatchOutcome.QueueUnavailable"/> (Service Bus refused) are 503s.
    /// </returns>
    Task<GenerateProfileResult> GenerateProfileAsync(
        Guid documentId,
        HttpContext httpContext,
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
