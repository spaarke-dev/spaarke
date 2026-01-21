using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Errors;

/// <summary>
/// Helper for creating RFC 7807 Problem Details responses with Graph error differentiation.
/// Updated for Microsoft.Graph SDK v5.x (Phase 7).
/// </summary>
public static class ProblemDetailsHelper
{
    public static IResult FromGraphException(ODataError ex)
    {
        var status = ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 500;
        var title = status == 403 ? "forbidden" : status == 401 ? "unauthorized" : "error";
        var code = GetErrorCode(ex);
        var detail = (status == 403 && code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
            ? "missing graph app role (filestoragecontainer.selected) for the api identity."
            : status == 403 ? "api identity lacks required container-type permission for this operation."
            : ex.Error?.Message ?? "Graph API error";

        string? graphRequestId = null;
        try
        {
            // Graph SDK v5.x: ResponseHeaders is now Dictionary<string, IEnumerable<string>>
            graphRequestId = ex.ResponseHeaders?
                .Where(h => h.Key == "request-id" || h.Key == "client-request-id")
                .SelectMany(h => h.Value)
                .FirstOrDefault();
        }
        catch
        {
            // Ignore header access errors
        }

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

    public static IResult Forbidden(string reasonCode)
    {
        return Results.Problem(
            title: "Forbidden",
            statusCode: 403,
            detail: "Access denied",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = reasonCode
            }
        );
    }

    private static string GetErrorCode(ODataError ex)
    {
        // Graph SDK v5.x: Error codes are in ex.Error.Code property
        var errorCode = ex.Error?.Code ?? "";
        var status = ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 500;

        return errorCode == "Authorization_RequestDenied" ? "Authorization_RequestDenied" :
               errorCode == "activityLimitReached" ? "TooManyRequests" :
               errorCode == "accessDenied" ? "Forbidden" :
               !string.IsNullOrEmpty(errorCode) ? errorCode :
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
