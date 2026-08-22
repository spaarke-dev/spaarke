using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Errors;

/// <summary>
/// Helper for creating RFC 7807 Problem Details responses with Graph error differentiation.
/// Updated for Microsoft.Graph SDK v5.x (Phase 7).
/// </summary>
/// <remarks>
/// <see cref="FromGraphException"/> takes extracted primitives (status code, error code, message,
/// request id) rather than the raw Graph SDK <c>ODataError</c> so this type stays Graph-free per
/// ADR-007 §1 (only <c>Infrastructure.Graph</c>/<c>SpeFileStore</c> may reference Microsoft.Graph
/// types on their member/signature surface). Callers holding an <c>ODataError</c> (typically in a
/// <c>catch</c> block — a local variable, so the catch site itself stays ADR-007-clean) extract the
/// fields before calling. Renamed extraction responsibility, zero behavior change.
/// </remarks>
public static partial class ProblemDetailsHelper
{
    #region Truthful upstream-error surface (sdap-SPE-admin-app-r2 task 001 / spec FR-A01)

    // Secret-shaped substrings that must never reach a response payload. Secret NAMES are fine and
    // diagnostically useful; VALUES are not. Applied to every upstream message this class surfaces.
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtPattern();

    // The optional quote in <sep> matters: Graph and MSAL report these both as query-string style
    // (client_secret=…) and as JSON ("access_token":"…"), and only the latter closes the key with a quote.
    [GeneratedRegex(@"(?<key>client_secret|password|access_token|id_token|refresh_token|assertion|api[_-]?key)(?<sep>""?\s*[=:]\s*""?)(?<val>[^""&,\s}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignmentPattern();

    /// <summary>
    /// Strips secret-shaped values (bearer tokens, JWTs, <c>client_secret=…</c>-style assignments) from a
    /// message before it is placed in a response payload.
    /// </summary>
    /// <remarks>
    /// Surfacing the real upstream error (spec FR-A01) means putting third-party message text in front of
    /// an admin. This is the guard that keeps that safe: secret NAMES survive (an admin needs to know
    /// <i>which</i> secret is wrong), secret VALUES do not. Task 001 acceptance criterion:
    /// "an error payload contains no secret VALUE".
    /// </remarks>
    public static string? Redact(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var redacted = BearerTokenPattern().Replace(message, "Bearer [redacted]");
        redacted = JwtPattern().Replace(redacted, "[redacted-token]");
        redacted = SecretAssignmentPattern().Replace(redacted, "${key}${sep}[redacted]");
        return redacted;
    }

    /// <summary>
    /// Appends what actually failed to a summary that does not assert a cause, producing the <c>detail</c>
    /// of a ProblemDetails response. Use in a <c>catch</c> whose exception type does NOT establish why the
    /// operation failed.
    /// </summary>
    /// <param name="summary">
    /// What the caller was doing, stated WITHOUT asserting why it failed (e.g. "An unexpected error
    /// occurred while listing containers."). Never name a cause here — that is the defect this exists to
    /// remove.
    /// </param>
    /// <param name="ex">The caught exception. Its type name and redacted message are appended.</param>
    /// <returns>The summary followed by the real exception type and message, secrets redacted.</returns>
    /// <remarks>
    /// <para>
    /// Added 2026-08-21 by <c>sdap-SPE-admin-app-r2</c> task 001 (spec FR-A01). Deliberately shaped as a
    /// <b>string</b> helper rather than an <c>IResult</c> factory: the 33 SpeAdmin generic-catch sites this
    /// replaces use three different argument orderings for <c>Results.Problem</c> plus a mix of inline and
    /// block <c>extensions</c> dictionaries. Rebuilding those calls would churn every line and risk
    /// silently altering a status code or dropping an extension key; wrapping only the <c>detail</c>
    /// argument is order-independent, formatting-independent, and reviewable at a glance.
    /// </para>
    /// <para>
    /// Graph failures do NOT come through here — they arrive as <c>SpaarkeStorageException</c> and route
    /// through <c>GraphErrorTranslator.ToProblemDetails</c>, which additionally carries the Graph error
    /// code, upstream status, and request id.
    /// </para>
    /// </remarks>
    public static string Explain(string summary, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var cause = Redact(ex.Message);
        return string.IsNullOrWhiteSpace(cause)
            ? $"{summary} ({ex.GetType().Name})"
            : $"{summary} {ex.GetType().Name}: {cause}";
    }

    #endregion

    /// <summary>
    /// Builds a ProblemDetails response for a Graph API failure from its extracted fields.
    /// </summary>
    /// <param name="responseStatusCode">The Graph SDK <c>ODataError.ResponseStatusCode</c> (0/negative treated as unknown → 500).</param>
    /// <param name="errorCode">The Graph SDK <c>ODataError.Error?.Code</c>.</param>
    /// <param name="errorMessage">The Graph SDK <c>ODataError.Error?.Message</c>.</param>
    /// <param name="graphRequestId">Optional Graph <c>request-id</c>/<c>client-request-id</c> response header value, if the caller extracted one.</param>
    public static IResult FromGraphException(int responseStatusCode, string? errorCode, string? errorMessage, string? graphRequestId = null)
    {
        var status = responseStatusCode > 0 ? responseStatusCode : 500;
        var title = status == 403 ? "forbidden" : status == 401 ? "unauthorized" : "error";
        var code = GetErrorCode(errorCode, status);
        var detail = (status == 403 && code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
            ? "missing graph app role (filestoragecontainer.selected) for the api identity."
            : status == 403 ? "api identity lacks required container-type permission for this operation."
            : Redact(errorMessage) ?? "Graph API error";

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["graphErrorCode"] = code,
                ["graphRequestId"] = graphRequestId
            });
    }

    public static IResult ValidationProblem(Dictionary<string, string[]> errors)
    {
        return Results.ValidationProblem(errors);
    }

    public static IResult ValidationError(string detail)
    {
        return Results.Problem(
            title: "Validation Error",
            statusCode: 400,
            detail: detail
        );
    }

    /// <summary>
    /// Build a 403 ProblemDetails carrying a deny code and, optionally, an explanation the caller can
    /// act on.
    /// </summary>
    /// <param name="reasonCode">Stable machine-readable deny code, <c>{domain}.{area}.{action}.{reason}</c>.</param>
    /// <param name="detail">
    /// Optional. What was actually checked and what would grant it. Defaults to "Access denied", which
    /// tells the user nothing — supply a real explanation wherever the denying code knows one.
    /// <b>It MUST NOT name a cause the denying code did not establish</b>; an authorization layer that
    /// cannot observe a permission must not claim the caller lacks it (spec FR-B03).
    /// </param>
    /// <param name="traceId">Optional correlation id for support.</param>
    /// <remarks>
    /// The optional parameters were added by sdap-SPE-admin-app-r2 task 012 (spec FR-B03). Existing
    /// call sites keep the previous behaviour unchanged.
    /// </remarks>
    public static IResult Forbidden(string reasonCode, string? detail = null, string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["reasonCode"] = reasonCode
        };

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            extensions["traceId"] = traceId;
        }

        return Results.Problem(
            title: "Forbidden",
            statusCode: 403,
            detail: string.IsNullOrWhiteSpace(detail) ? "Access denied" : detail,
            extensions: extensions
        );
    }

    private static string GetErrorCode(string? errorCode, int status)
    {
        // Graph SDK v5.x: Error codes are in ex.Error.Code property
        var code = errorCode ?? "";

        return code == "Authorization_RequestDenied" ? "Authorization_RequestDenied" :
               code == "activityLimitReached" ? "TooManyRequests" :
               code == "accessDenied" ? "Forbidden" :
               !string.IsNullOrEmpty(code) ? code :
               status.ToString();
    }

    /// <summary>
    /// Create a Problem Details response from a SummarizationException.
    /// </summary>
    public static IResult FromSummarizationException(SummarizationException ex)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = ex.Code
        };

        if (ex.CorrelationId != null)
        {
            extensions["correlationId"] = ex.CorrelationId;
        }

        if (ex.Extensions != null)
        {
            foreach (var kvp in ex.Extensions)
            {
                extensions[kvp.Key] = kvp.Value;
            }
        }

        return Results.Problem(
            title: ex.Title,
            detail: ex.Detail,
            statusCode: ex.StatusCode,
            extensions: extensions);
    }

    /// <summary>
    /// Create an AI service unavailable response.
    /// </summary>
    public static IResult AiUnavailable(string reason, string? correlationId = null)
    {
        return Results.Problem(
            title: "AI Service Unavailable",
            detail: reason,
            statusCode: 503,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "ai_unavailable",
                ["correlationId"] = correlationId
            });
    }

    /// <summary>
    /// Create an AI rate limit exceeded response with optional retry-after.
    /// </summary>
    public static IResult AiRateLimited(int? retryAfterSeconds = null, string? correlationId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = "ai_rate_limited",
            ["correlationId"] = correlationId
        };

        if (retryAfterSeconds.HasValue)
        {
            extensions["retryAfterSeconds"] = retryAfterSeconds.Value;
        }

        return Results.Problem(
            title: "Rate Limit Exceeded",
            detail: "Too many requests to the AI service. Please wait before retrying.",
            statusCode: 429,
            extensions: extensions);
    }

    #region Office Integration Error Helpers

    /// <summary>
    /// Creates an Office integration validation error response.
    /// Per ADR-019, all errors return RFC 7807 ProblemDetails with stable error codes.
    /// </summary>
    /// <param name="errorCode">The OFFICE_XXX error code.</param>
    /// <param name="title">Short error title.</param>
    /// <param name="detail">Detailed error message.</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <returns>A ProblemDetails result.</returns>
    public static IResult OfficeValidationError(
        string errorCode,
        string title,
        string detail,
        string? correlationId = null)
    {
        return Results.Problem(
            type: $"https://spaarke.com/errors/office/{errorCode.ToLowerInvariant()}",
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }

    /// <summary>
    /// Creates an Office integration not found error response.
    /// </summary>
    public static IResult OfficeNotFound(
        string errorCode,
        string title,
        string detail,
        string? correlationId = null)
    {
        return Results.Problem(
            type: $"https://spaarke.com/errors/office/{errorCode.ToLowerInvariant()}",
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }

    /// <summary>
    /// Creates an Office integration forbidden error response.
    /// </summary>
    public static IResult OfficeForbidden(
        string errorCode,
        string title,
        string detail,
        string? correlationId = null)
    {
        return Results.Problem(
            type: $"https://spaarke.com/errors/office/{errorCode.ToLowerInvariant()}",
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }

    /// <summary>
    /// Creates an Office integration service error response.
    /// </summary>
    public static IResult OfficeServiceError(
        string errorCode,
        string title,
        string detail,
        string? correlationId = null)
    {
        return Results.Problem(
            type: $"https://spaarke.com/errors/office/{errorCode.ToLowerInvariant()}",
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }

    /// <summary>
    /// OFFICE_003: Association required - User must select an association target.
    /// </summary>
    public static IResult OfficeAssociationRequired(string? correlationId = null)
    {
        return OfficeValidationError(
            "OFFICE_003",
            "Association Required",
            "A document must be associated with a Matter, Project, Invoice, Account, or Contact. Please select an association target.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_006: Invalid association target - The specified entity does not exist or is invalid.
    /// </summary>
    public static IResult OfficeInvalidAssociationTarget(string entityType, string? correlationId = null)
    {
        return OfficeValidationError(
            "OFFICE_006",
            "Invalid Association Target",
            $"The specified {entityType} does not exist or is not a valid association target.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_001: Invalid source type.
    /// </summary>
    public static IResult OfficeInvalidSourceType(string? correlationId = null)
    {
        return OfficeValidationError(
            "OFFICE_001",
            "Invalid Source Type",
            "The specified source type is not recognized. Valid values are: Email, Attachment, Document.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_002: Invalid association type.
    /// </summary>
    public static IResult OfficeInvalidAssociationType(string? correlationId = null)
    {
        return OfficeValidationError(
            "OFFICE_002",
            "Invalid Association Type",
            "The specified association type is not recognized. Valid values are: Matter, Project, Invoice, Account, Contact.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_004: Attachment too large.
    /// </summary>
    public static IResult OfficeAttachmentTooLarge(long maxSizeBytes, string? correlationId = null)
    {
        var maxSizeMb = maxSizeBytes / (1024 * 1024);
        return OfficeValidationError(
            "OFFICE_004",
            "Attachment Too Large",
            $"The attachment exceeds the maximum allowed size of {maxSizeMb}MB.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_005: Total size exceeded.
    /// </summary>
    public static IResult OfficeTotalSizeExceeded(long maxSizeBytes, string? correlationId = null)
    {
        var maxSizeMb = maxSizeBytes / (1024 * 1024);
        return OfficeValidationError(
            "OFFICE_005",
            "Total Size Exceeded",
            $"The total size of all attachments exceeds the maximum allowed size of {maxSizeMb}MB.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_007: Association target not found.
    /// </summary>
    public static IResult OfficeAssociationTargetNotFound(string entityType, Guid entityId, string? correlationId = null)
    {
        return OfficeNotFound(
            "OFFICE_007",
            "Association Target Not Found",
            $"The specified {entityType} with ID {entityId} was not found.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_008: Job not found.
    /// </summary>
    public static IResult OfficeJobNotFound(Guid jobId, string? correlationId = null)
    {
        return OfficeNotFound(
            "OFFICE_008",
            "Job Not Found",
            $"The processing job with ID {jobId} was not found or has expired.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_009: Access denied.
    /// </summary>
    public static IResult OfficeAccessDenied(string? correlationId = null)
    {
        return OfficeForbidden(
            "OFFICE_009",
            "Access Denied",
            "You do not have permission to perform this operation.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_012: SPE upload failed.
    /// </summary>
    public static IResult OfficeSpeUploadFailed(string? detail = null, string? correlationId = null)
    {
        return OfficeServiceError(
            "OFFICE_012",
            "SPE Upload Failed",
            detail ?? "Failed to upload file to SharePoint Embedded storage.",
            correlationId);
    }

    /// <summary>
    /// OFFICE_015: Rate limit exceeded.
    /// Returns 429 Too Many Requests with Retry-After information.
    /// </summary>
    /// <param name="limit">The rate limit that was exceeded.</param>
    /// <param name="retryAfterSeconds">Seconds until the client can retry.</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <returns>A ProblemDetails result with 429 status.</returns>
    public static IResult OfficeRateLimitExceeded(
        int limit,
        int retryAfterSeconds,
        string? correlationId = null)
    {
        return Results.Problem(
            type: "https://spaarke.com/errors/office/rate-limited",
            title: "Too Many Requests",
            detail: $"Rate limit exceeded. Maximum {limit} requests per minute allowed. Please retry after {retryAfterSeconds} seconds.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "OFFICE_015",
                ["correlationId"] = correlationId,
                ["limit"] = limit,
                ["retryAfterSeconds"] = retryAfterSeconds
            });
    }

    #endregion
}
