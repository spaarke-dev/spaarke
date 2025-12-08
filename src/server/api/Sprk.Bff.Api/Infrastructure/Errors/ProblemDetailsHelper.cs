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
}
