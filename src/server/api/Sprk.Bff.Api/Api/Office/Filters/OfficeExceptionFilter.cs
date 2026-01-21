using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Api.Office.Filters;

/// <summary>
/// Endpoint filter that handles exceptions and converts them to ProblemDetails responses.
/// Per ADR-019, all errors must return RFC 7807 format with correlation IDs.
/// </summary>
/// <remarks>
/// <para>
/// This filter catches exceptions thrown by Office endpoints and returns appropriate
/// ProblemDetails responses. It handles:
/// - OfficeProblemException: Office-specific errors with OFFICE_001-015 codes
/// - SdapProblemException: General SDAP errors
/// - ODataError: Microsoft Graph API errors
/// - Other exceptions: Returns 500 Internal Server Error
/// </para>
/// <para>
/// IMPORTANT: This filter ensures no stack traces or sensitive information
/// are leaked in production error responses.
/// </para>
/// </remarks>
public class OfficeExceptionFilter : IEndpointFilter
{
    private readonly ILogger<OfficeExceptionFilter> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Creates a new instance of the OfficeExceptionFilter.
    /// </summary>
    public OfficeExceptionFilter(ILogger<OfficeExceptionFilter> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Invokes the filter, wrapping the endpoint in exception handling.
    /// </summary>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // Ensure correlation ID is available
        var correlationId = CorrelationIdHelper.EnsureCorrelationId(context.HttpContext);

        try
        {
            return await next(context);
        }
        catch (OfficeProblemException ex)
        {
            return HandleOfficeProblemException(ex, correlationId, context.HttpContext);
        }
        catch (SdapProblemException ex)
        {
            return HandleSdapProblemException(ex, correlationId, context.HttpContext);
        }
        catch (ODataError ex)
        {
            return HandleGraphException(ex, correlationId, context.HttpContext);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or request was cancelled
            _logger.LogInformation("Request cancelled. CorrelationId: {CorrelationId}", correlationId);

            return Results.Problem(
                type: $"{OfficeErrorCodes.TypeBaseUri}cancelled",
                title: "Request Cancelled",
                detail: "The request was cancelled.",
                statusCode: 499, // Client Closed Request (nginx convention)
                instance: context.HttpContext.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "REQUEST_CANCELLED",
                    ["correlationId"] = correlationId
                });
        }
        catch (Exception ex)
        {
            return HandleUnexpectedException(ex, correlationId, context.HttpContext);
        }
    }

    /// <summary>
    /// Handles OfficeProblemException by returning the appropriate ProblemDetails.
    /// </summary>
    private IResult HandleOfficeProblemException(
        OfficeProblemException ex,
        string correlationId,
        HttpContext httpContext)
    {
        _logger.LogWarning(
            "Office error {ErrorCode}: {Detail}. CorrelationId: {CorrelationId}",
            ex.ErrorCode,
            ex.Detail,
            correlationId);

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = ex.ErrorCode,
            ["correlationId"] = correlationId
        };

        // Add any additional extensions from the exception
        if (ex.Extensions != null)
        {
            foreach (var kvp in ex.Extensions.Where(kvp => kvp.Value != null))
            {
                extensions[kvp.Key] = kvp.Value;
            }
        }

        return Results.Problem(
            type: ex.Type,
            title: ex.Title,
            detail: ex.Detail,
            statusCode: ex.StatusCode,
            instance: ex.Instance,
            extensions: extensions);
    }

    /// <summary>
    /// Handles SdapProblemException by returning the appropriate ProblemDetails.
    /// </summary>
    private IResult HandleSdapProblemException(
        SdapProblemException ex,
        string correlationId,
        HttpContext httpContext)
    {
        _logger.LogWarning(
            "SDAP error {ErrorCode}: {Title}. CorrelationId: {CorrelationId}",
            ex.Code,
            ex.Title,
            correlationId);

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = ex.Code,
            ["correlationId"] = correlationId
        };

        // Add any additional extensions from the exception
        if (ex.Extensions != null)
        {
            foreach (var kvp in ex.Extensions)
            {
                extensions[kvp.Key] = kvp.Value;
            }
        }

        return Results.Problem(
            type: $"https://spaarke.com/errors/{ex.Code}",
            title: ex.Title,
            detail: ex.Detail ?? ex.Message,
            statusCode: ex.StatusCode,
            instance: httpContext.Request.Path,
            extensions: extensions);
    }

    /// <summary>
    /// Handles ODataError (Microsoft Graph errors) by returning the appropriate ProblemDetails.
    /// </summary>
    private IResult HandleGraphException(
        ODataError ex,
        string correlationId,
        HttpContext httpContext)
    {
        string? graphRequestId = null;
        try
        {
            graphRequestId = ex.ResponseHeaders?
                .Where(h => h.Key.Equals("request-id", StringComparison.OrdinalIgnoreCase) ||
                            h.Key.Equals("client-request-id", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .FirstOrDefault();
        }
        catch
        {
            // Ignore header access errors
        }

        var errorCode = ex.Error?.Code ?? "unknown";
        var status = ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 502;

        _logger.LogError(
            ex,
            "Graph API error {GraphErrorCode}: {Message}. GraphRequestId: {GraphRequestId}, CorrelationId: {CorrelationId}",
            errorCode,
            ex.Error?.Message,
            graphRequestId,
            correlationId);

        // Map Graph auth errors to OFFICE_009
        if (status is 401 or 403)
        {
            return Results.Problem(
                type: OfficeErrorCodes.GetTypeUri(OfficeErrorCodes.AccessDenied),
                title: OfficeErrorCodes.GetTitle(OfficeErrorCodes.AccessDenied),
                detail: "Access was denied by Microsoft Graph API.",
                statusCode: 403,
                instance: httpContext.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = OfficeErrorCodes.AccessDenied,
                    ["correlationId"] = correlationId,
                    ["graphRequestId"] = graphRequestId,
                    ["graphErrorCode"] = errorCode
                });
        }

        // Other Graph errors map to OFFICE_013
        return Results.Problem(
            type: OfficeErrorCodes.GetTypeUri(OfficeErrorCodes.GraphApiError),
            title: OfficeErrorCodes.GetTitle(OfficeErrorCodes.GraphApiError),
            detail: "A Microsoft Graph API error occurred. Please try again later.",
            statusCode: 502,
            instance: httpContext.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = OfficeErrorCodes.GraphApiError,
                ["correlationId"] = correlationId,
                ["graphRequestId"] = graphRequestId,
                ["graphErrorCode"] = errorCode
            });
    }

    /// <summary>
    /// Handles unexpected exceptions with a generic 500 response.
    /// IMPORTANT: Does not leak stack traces or sensitive information in production.
    /// </summary>
    private IResult HandleUnexpectedException(
        Exception ex,
        string correlationId,
        HttpContext httpContext)
    {
        // Always log the full exception for debugging
        _logger.LogError(
            ex,
            "Unexpected error in Office endpoint. CorrelationId: {CorrelationId}",
            correlationId);

        // In development, include more detail
        var detail = _environment.IsDevelopment()
            ? $"An unexpected error occurred: {ex.Message}"
            : "An unexpected error occurred. Please try again later.";

        return Results.Problem(
            type: $"{OfficeErrorCodes.TypeBaseUri}internal-error",
            title: "Internal Server Error",
            detail: detail,
            statusCode: 500,
            instance: httpContext.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "INTERNAL_ERROR",
                ["correlationId"] = correlationId
                // NOTE: Stack trace is intentionally NOT included per ADR-019
            });
    }
}
