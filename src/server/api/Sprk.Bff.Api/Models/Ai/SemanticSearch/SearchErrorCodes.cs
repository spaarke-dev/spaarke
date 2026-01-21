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
    /// User does not have access to the requested entity.
    /// </summary>
    public const string EntityAccessDenied = "ENTITY_ACCESS_DENIED";

    /// <summary>
    /// User does not have access to one or more requested documents.
    /// </summary>
    public const string DocumentAccessDenied = "DOCUMENT_ACCESS_DENIED";
}
