namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Standard error codes for semantic search validation failures.
/// Used with ProblemDetails responses per ADR-019.
/// </summary>
public static class SearchErrorCodes
{
    /// <summary>
    /// Query exceeds maximum length (1000 characters).
    /// </summary>
    public const string QueryTooLong = "QUERY_TOO_LONG";

    /// <summary>
    /// Query is required for this hybrid mode.
    /// </summary>
    public const string QueryRequired = "QUERY_REQUIRED";

    /// <summary>
    /// Invalid scope value provided.
    /// </summary>
    public const string InvalidScope = "INVALID_SCOPE";

    /// <summary>
    /// scope=all is not supported in R1.
    /// </summary>
    public const string ScopeNotSupported = "SCOPE_NOT_SUPPORTED";

    /// <summary>
    /// entityType is required when scope=entity.
    /// </summary>
    public const string EntityTypeRequired = "ENTITY_TYPE_REQUIRED";

    /// <summary>
    /// entityId is required when scope=entity.
    /// </summary>
    public const string EntityIdRequired = "ENTITY_ID_REQUIRED";

    /// <summary>
    /// Invalid entityType value.
    /// </summary>
    public const string InvalidEntityType = "INVALID_ENTITY_TYPE";

    /// <summary>
    /// documentIds is required when scope=documentIds.
    /// </summary>
    public const string DocumentIdsRequired = "DOCUMENT_IDS_REQUIRED";

    /// <summary>
    /// documentIds exceeds maximum count (100).
    /// </summary>
    public const string TooManyDocumentIds = "TOO_MANY_DOCUMENT_IDS";

    /// <summary>
    /// Invalid limit value (must be 1-50).
    /// </summary>
    public const string InvalidLimit = "INVALID_LIMIT";

    /// <summary>
    /// Invalid offset value (must be 0-1000).
    /// </summary>
    public const string InvalidOffset = "INVALID_OFFSET";

    /// <summary>
    /// Invalid hybridMode value.
    /// </summary>
    public const string InvalidHybridMode = "INVALID_HYBRID_MODE";

    /// <summary>
    /// Invalid dateRange.field value.
    /// </summary>
    public const string InvalidDateRangeField = "INVALID_DATE_RANGE_FIELD";

    /// <summary>
    /// Invalid entityTypes filter value.
    /// </summary>
    public const string InvalidEntityTypes = "INVALID_ENTITY_TYPES";

    /// <summary>
    /// User does not have access to the requested entity.
    /// </summary>
    public const string EntityAccessDenied = "ENTITY_ACCESS_DENIED";

    /// <summary>
    /// User does not have access to one or more requested documents.
    /// </summary>
    public const string DocumentAccessDenied = "DOCUMENT_ACCESS_DENIED";

    // ========================================================================
    // Authorization error codes (SemanticSearchAuthorizationFilter, POST /api/ai/search)
    //
    // Added by unified-access-control-r2 task 070. Every denial from that filter previously
    // carried a bare ProblemDetails — same title, same wording, no code, no correlation id — so
    // a 403 was indistinguishable from any other 403 to the client and left no support handle.
    // ADR-019 requires both, and names AI endpoints specifically.
    //
    // Codes may distinguish cases the human-readable `detail` deliberately does NOT. The uniform
    // 403 wording is a security property: telling a caller "that record exists but you cannot
    // read it" versus "no such record" confirms the existence of records they cannot see. These
    // codes are for the legitimate client's control flow, not for making the denial chattier.
    // ========================================================================

    /// <summary>
    /// No tenant claim in the authentication token, so tenant membership cannot be established.
    /// </summary>
    public const string MissingTenantIdentity = "MISSING_TENANT_IDENTITY";

    /// <summary>
    /// No parseable search request body was present on the invocation.
    /// </summary>
    public const string RequestBodyRequired = "REQUEST_BODY_REQUIRED";

    /// <summary>
    /// No caller object id in the authentication token, so access cannot be evaluated for anyone.
    /// </summary>
    public const string MissingCallerIdentity = "MISSING_CALLER_IDENTITY";

    /// <summary>
    /// No caller bearer token available. Access must be evaluated AS THE CALLER; without the token
    /// the only alternative is an app-only evaluation, which is refused rather than substituted.
    /// </summary>
    public const string MissingCallerToken = "MISSING_CALLER_TOKEN";

    /// <summary>
    /// <c>scope=all</c> was refused. Distinct from <see cref="ScopeNotSupported"/>, which frames the
    /// same value as a capability gap ("not supported in R1"): this is a permanent authorization
    /// refusal, not a feature awaiting release, and a client should never retry or feature-flag on it.
    /// </summary>
    public const string ScopeAllNotPermitted = "SCOPE_ALL_NOT_PERMITTED";

    /// <summary>
    /// The requested <c>entityType</c> is not a parent type <c>scope=entity</c> can be authorized
    /// against. Distinct from <see cref="InvalidEntityType"/>, which is a request-shape complaint:
    /// this one is reached only after the shape is valid.
    /// </summary>
    public const string EntityTypeNotAuthorizable = "ENTITY_TYPE_NOT_AUTHORIZABLE";

    /// <summary>
    /// <c>entityId</c> was present but is not a non-empty GUID, so no record can be authorized.
    /// </summary>
    public const string InvalidEntityId = "INVALID_ENTITY_ID";

    /// <summary>
    /// One or more <c>documentIds</c> entries is not a GUID. Deliberately a 400 rather than a 403:
    /// unparseable ids are a malformed payload, and reporting them as an access denial would send the
    /// caller looking at permissions instead of at their request.
    /// </summary>
    public const string InvalidDocumentIds = "INVALID_DOCUMENT_IDS";

    /// <summary>
    /// The caller may read NONE of the requested documents. Distinct from
    /// <see cref="DocumentAccessDenied"/> ("one or more"): a partially-readable list is not denied —
    /// it proceeds with the readable subset — so only the empty case reaches this code.
    /// </summary>
    public const string NoReadableDocuments = "NO_READABLE_DOCUMENTS";

    // ========================================================================
    // Record Search error codes (POST /api/ai/search/records)
    // ========================================================================

    /// <summary>
    /// Invalid or unrecognized record type(s) in recordTypes.
    /// </summary>
    public const string InvalidRecordTypes = "INVALID_RECORD_TYPES";

    /// <summary>
    /// Record search operation failed due to a service error.
    /// </summary>
    public const string RecordSearchFailed = "RECORD_SEARCH_FAILED";
}
