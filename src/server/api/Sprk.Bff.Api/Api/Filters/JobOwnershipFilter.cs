using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding JobOwnershipFilter to endpoints.
/// </summary>
public static class JobOwnershipFilterExtensions
{
    /// <summary>
    /// Adds job ownership filter that validates the user owns or has access to the job.
    /// Returns 403 Forbidden if user does not own the job.
    /// Returns 404 Not Found if job does not exist.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This filter should be applied after OfficeAuthFilter to ensure userId is available.
    /// </remarks>
    public static TBuilder AddJobOwnershipFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<JobOwnershipFilter>>();
            var officeService = context.HttpContext.RequestServices.GetRequiredService<IOfficeService>();
            var filter = new JobOwnershipFilter(officeService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that validates user owns or has access to a processing job.
/// Extracts jobId from route parameters and verifies ownership.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// </para>
/// <para>
/// Job ownership is determined by:
/// 1. User created the job (sprk_createdby matches userId)
/// 2. User is in the same tenant (future: team-based access)
/// </para>
/// <para>
/// This filter expects:
/// - Route parameter 'jobId' (GUID)
/// - userId in HttpContext.Items[OfficeAuthFilter.UserIdKey] (set by OfficeAuthFilter)
/// </para>
/// </remarks>
public class JobOwnershipFilter : IEndpointFilter
{
    private readonly IOfficeService _officeService;
    private readonly ILogger<JobOwnershipFilter>? _logger;

    public JobOwnershipFilter(IOfficeService officeService, ILogger<JobOwnershipFilter>? logger = null)
    {
        _officeService = officeService ?? throw new ArgumentNullException(nameof(officeService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Get userId from HttpContext.Items (set by OfficeAuthFilter)
        var userId = httpContext.Items[OfficeAuthFilter.UserIdKey] as string;
        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "Job ownership check failed: No userId in HttpContext.Items. " +
                "Ensure OfficeAuthFilter runs before JobOwnershipFilter. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not established",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_003",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Extract jobId from route parameters
        var jobIdString = ExtractJobId(context, httpContext);
        if (string.IsNullOrEmpty(jobIdString))
        {
            _logger?.LogWarning(
                "Job ownership check failed: No jobId in route. " +
                "Route values: {RouteValues}. CorrelationId: {CorrelationId}",
                string.Join(", ", httpContext.Request.RouteValues.Select(kv => $"{kv.Key}={kv.Value}")),
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Job identifier not found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_008",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Parse jobId as GUID
        if (!Guid.TryParse(jobIdString, out var jobId))
        {
            _logger?.LogWarning(
                "Job ownership check failed: Invalid jobId format '{JobIdString}'. " +
                "CorrelationId: {CorrelationId}",
                jobIdString, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Invalid job identifier format",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_008",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Check if job exists and get owner info
        // Note: Use overload without userId to get raw job data for ownership validation
        var jobStatus = await _officeService.GetJobStatusAsync(jobId, httpContext.RequestAborted);
        if (jobStatus == null)
        {
            _logger?.LogInformation(
                "Job ownership check: Job {JobId} not found for user {UserId}. " +
                "CorrelationId: {CorrelationId}",
                jobId, userId, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: "Job not found or has expired",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_008",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Verify ownership - compare job's creator with current user
        // Note: JobStatusResponse.CreatedBy should contain the userId who created the job
        if (!string.IsNullOrEmpty(jobStatus.CreatedBy) &&
            !string.Equals(jobStatus.CreatedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "Job ownership check failed: User {UserId} attempted to access job {JobId} " +
                "owned by {OwnerId}. CorrelationId: {CorrelationId}",
                userId, jobId, jobStatus.CreatedBy, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "You do not have access to this job",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["reasonCode"] = "sdap.office.job.ownership_mismatch",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        _logger?.LogDebug(
            "Job ownership verified: User {UserId} owns job {JobId}. " +
            "CorrelationId: {CorrelationId}",
            userId, jobId, httpContext.TraceIdentifier);

        // Store jobId for downstream use
        httpContext.Items["Office.JobId"] = jobId;

        return await next(context);
    }

    /// <summary>
    /// Extract job ID from route parameters.
    /// Checks common parameter names: jobId, id.
    /// </summary>
    private static string? ExtractJobId(EndpointFilterInvocationContext context, HttpContext httpContext)
    {
        var routeValues = httpContext.Request.RouteValues;

        // Try 'jobId' first (primary name)
        if (routeValues.TryGetValue("jobId", out var jobId) && jobId != null)
        {
            return jobId.ToString();
        }

        // Try 'id' as fallback
        if (routeValues.TryGetValue("id", out var id) && id != null)
        {
            return id.ToString();
        }

        return null;
    }
}
