using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// User-context Dataverse Web API boundary for the <c>dataverse.*</c> tool namespace
/// (spaarke-ai-architecture-redesign-r1 tasks 008/009, FR-P0-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule; audited by task 012)</b>: every request issued through
/// this interface executes under the CALLING USER's Dataverse security context. The
/// implementation (<see cref="DataverseUserClient"/>) resolves the caller's bearer token from
/// the ambient <c>HttpContext</c> and performs an MSAL On-Behalf-Of exchange for the Dataverse
/// resource. There is <b>no app-only fallback</b> — when no authenticated user context is
/// available, calls fail closed with <see cref="DataverseUserClientErrorCodes.UserContextRequired"/>.
/// AI tool handlers MUST NOT reach Dataverse through any other client
/// (<c>IGenericEntityService</c>, <c>DataverseWebApiService</c>, <c>AnalysisToolService</c>, …
/// all authenticate app-only and are off-limits for the dataverse.* tool namespace).
/// </para>
/// <para>
/// This is a module boundary by design: handler unit tests mock this interface (allowed per
/// <c>docs/standards/TEST-ARCHITECTURE.md</c> mock-boundary rules — it is NOT an
/// <c>HttpMessageHandler</c> mock) and exercise handler behavior under simulated user security
/// contexts (403 → access-denied result, 404 → not-found, …).
/// </para>
/// <para>
/// Task 009's write handlers reuse this same surface (<see cref="PostAsync"/>,
/// <see cref="PatchAsync"/>, <see cref="DeleteAsync"/>) so the read and write halves of the
/// namespace share one OBO acquisition + error-mapping path.
/// </para>
/// <para>
/// ADR-013: AI-internal plumbing under <c>Services/Ai/Handlers/</c>; NOT exposed through the
/// PublicContracts facade. ADR-015/NFR-07: implementations log identifiers, status codes,
/// durations and counts only — never row content and never the user's token.
/// </para>
/// </remarks>
public interface IDataverseUserClient
{
    /// <summary>
    /// Issues a GET against the Dataverse OData Web API under the calling user's security
    /// context. <paramref name="relativePath"/> is relative to <c>{env}/api/data/v9.2/</c>
    /// (e.g. <c>"accounts?$select=name&amp;$top=10"</c> or
    /// <c>"EntityDefinitions(LogicalName='account')"</c>).
    /// </summary>
    Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a POST with a JSON body. Used by <c>dataverse.search_data</c> (Dataverse Search
    /// API when <paramref name="absoluteApiPath"/> starts with <c>/api/search/</c>) and by the
    /// task-009 <c>dataverse.create_record</c> handler.
    /// </summary>
    /// <param name="absoluteApiPath">
    /// Environment-relative API path starting with <c>/api/</c> (e.g.
    /// <c>"/api/search/v2.0/query"</c> or <c>"/api/data/v9.2/accounts"</c>).
    /// </param>
    /// <param name="jsonBody">Serialized JSON request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a POST with a JSON body, optionally requesting
    /// <c>Prefer: return=representation</c> so Dataverse echoes the created row (201 + body)
    /// instead of a bare 204. Used by the task-009 <c>dataverse.create_record</c> handler to
    /// read the created record's primary id for citation referencing without a second GET.
    /// Additive overload per task-008 review note — the 3-argument overload is unchanged.
    /// </summary>
    /// <param name="absoluteApiPath">Environment-relative API path starting with <c>/api/</c>.</param>
    /// <param name="jsonBody">Serialized JSON request body.</param>
    /// <param name="preferRepresentation">When true, sends <c>Prefer: return=representation</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a PATCH with a JSON body (task-009 <c>dataverse.update_record</c>).
    /// <b>Update-only semantics</b>: the implementation always sends <c>If-Match: *</c> so a
    /// PATCH against a missing/invisible record fails (404) instead of silently upsert-creating
    /// a new row — a Dataverse Web API default that would otherwise let <c>update_record</c>
    /// masquerade as <c>create_record</c> and bypass side-effect intent.
    /// </summary>
    Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken);

    /// <summary>Issues a DELETE (task-009 <c>dataverse.delete_record</c>).</summary>
    Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

/// <summary>
/// Result envelope for a user-context Dataverse Web API call. Carries the mapped error code
/// (never the raw response body on failure paths beyond the Dataverse error message) so
/// handlers surface consistent, LLM-actionable failures.
/// </summary>
public sealed record DataverseUserResponse
{
    /// <summary>HTTP status code returned by Dataverse (0 when the request never left the BFF).</summary>
    public required int StatusCode { get; init; }

    /// <summary>True when the call succeeded (2xx).</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Parsed JSON body on success (null for 204-style responses).</summary>
    public JsonElement? Body { get; init; }

    /// <summary>Mapped error code from <see cref="DataverseUserClientErrorCodes"/> when <see cref="IsSuccess"/> is false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Human/LLM-readable error description. Carries the Dataverse <c>error.message</c> when
    /// available (identifier/validation text — not row content).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a success response.</summary>
    public static DataverseUserResponse Ok(int statusCode, JsonElement? body) => new()
    {
        StatusCode = statusCode,
        IsSuccess = true,
        Body = body
    };

    /// <summary>Creates a failure response with a mapped error code.</summary>
    public static DataverseUserResponse Fail(int statusCode, string errorCode, string errorMessage) => new()
    {
        StatusCode = statusCode,
        IsSuccess = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Stable error codes surfaced by <see cref="IDataverseUserClient"/> and reused by every
/// <c>dataverse.*</c> handler (read AND write halves) so the agent loop sees one vocabulary.
/// </summary>
public static class DataverseUserClientErrorCodes
{
    /// <summary>No authenticated user context (missing HttpContext / bearer token). Fail-closed — never falls back to app-only.</summary>
    public const string UserContextRequired = "DATAVERSE_USER_CONTEXT_REQUIRED";

    /// <summary>OBO confidential-client configuration missing — operator setup issue, not a caller issue.</summary>
    public const string OboNotConfigured = "DATAVERSE_OBO_NOT_CONFIGURED";

    /// <summary>OBO token exchange failed (consent, expiry, MSAL service error).</summary>
    public const string OboExchangeFailed = "DATAVERSE_OBO_EXCHANGE_FAILED";

    /// <summary>Dataverse returned 401/403 — the CALLING USER lacks permission (row-level security respected).</summary>
    public const string AccessDenied = "DATAVERSE_ACCESS_DENIED";

    /// <summary>Dataverse returned 404 — table/record not found or not visible to the calling user.</summary>
    public const string NotFound = "DATAVERSE_NOT_FOUND";

    /// <summary>Dataverse returned 400 — malformed query/payload.</summary>
    public const string BadRequest = "DATAVERSE_BAD_REQUEST";

    /// <summary>Dataverse returned 429 — service protection limits (ADR-016).</summary>
    public const string RateLimited = "DATAVERSE_RATE_LIMITED";

    /// <summary>Dataverse returned 5xx or the response could not be parsed.</summary>
    public const string ServiceError = "DATAVERSE_SERVICE_ERROR";
}
