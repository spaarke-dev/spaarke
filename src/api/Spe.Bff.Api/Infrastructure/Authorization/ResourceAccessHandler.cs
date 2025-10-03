using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Spaarke.Core.Auth;

namespace Spe.Bff.Api.Infrastructure.Authorization;

/// <summary>
/// Authorization handler that enforces resource-based access control using Spaarke.Core.Auth.AuthorizationService.
/// Extracts user ID from claims and resource ID from route values, then checks permissions via Dataverse.
/// </summary>
public class ResourceAccessHandler : AuthorizationHandler<ResourceAccessRequirement>
{
    private readonly Spaarke.Core.Auth.AuthorizationService _authService;
    private readonly ILogger<ResourceAccessHandler> _logger;

    public ResourceAccessHandler(
        Spaarke.Core.Auth.AuthorizationService authService,
        ILogger<ResourceAccessHandler> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceAccessRequirement requirement)
    {
        using var activity = new Activity("ResourceAccessCheck").Start();
        activity.SetTag("operation", requirement.RequiredOperation);

        // Extract user ID from claims
        var userId = ExtractUserId(context.User);
        if (userId == null)
        {
            _logger.LogWarning("Authorization failed: No user ID found in claims");
            context.Fail(new AuthorizationFailureReason(this, "No user ID in claims"));
            return;
        }

        activity.SetTag("userId", userId);

        // Extract resource ID from HTTP context
        if (context.Resource is not HttpContext httpContext)
        {
            _logger.LogWarning("Authorization failed: Context.Resource is not HttpContext");
            context.Fail(new AuthorizationFailureReason(this, "Invalid resource context"));
            return;
        }

        var resourceId = ExtractResourceId(httpContext);
        if (resourceId == null)
        {
            _logger.LogWarning("Authorization failed: No resource ID found in route for user {UserId}", userId);
            context.Fail(new AuthorizationFailureReason(this, "No resource ID in route"));
            return;
        }

        activity.SetTag("resourceId", resourceId);

        try
        {
            // Create authorization context
            var authContext = new AuthorizationContext
            {
                UserId = userId,
                ResourceId = resourceId,
                Operation = requirement.RequiredOperation,
                CorrelationId = Activity.Current?.Id
            };

            // Check access via AuthorizationService
            var result = await _authService.AuthorizeAsync(authContext, httpContext.RequestAborted);

            if (result.IsAllowed)
            {
                _logger.LogInformation("Authorization succeeded for user {UserId} on resource {ResourceId} operation {Operation}",
                    userId, resourceId, requirement.RequiredOperation);

                activity.SetTag("result", "allow");
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("Authorization denied for user {UserId} on resource {ResourceId} operation {Operation}: {Reason} (rule: {Rule})",
                    userId, resourceId, requirement.RequiredOperation, result.ReasonCode, result.RuleName);

                activity.SetTag("result", "deny");
                activity.SetTag("reason", result.ReasonCode);

                context.Fail(new AuthorizationFailureReason(this, $"{result.ReasonCode} ({result.RuleName})"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authorization error for user {UserId} on resource {ResourceId} operation {Operation}",
                userId, resourceId, requirement.RequiredOperation);

            activity.SetTag("result", "error");
            context.Fail(new AuthorizationFailureReason(this, "Authorization system error"));
        }
    }

    /// <summary>
    /// Extracts user ID from claims. Checks both Azure AD 'oid' claim and standard NameIdentifier.
    /// </summary>
    private string? ExtractUserId(ClaimsPrincipal user)
    {
        // Try Azure AD object identifier (OID) first
        var oid = user.FindFirst("oid")?.Value
               ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (!string.IsNullOrWhiteSpace(oid))
        {
            return oid;
        }

        // Fallback to standard NameIdentifier
        var nameId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(nameId))
        {
            return nameId;
        }

        // Fallback to sub claim (OIDC)
        var sub = user.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            return sub;
        }

        return null;
    }

    /// <summary>
    /// Extracts resource ID from route values.
    /// Checks common route parameter names: containerId, driveId, documentId, resourceId.
    /// </summary>
    private string? ExtractResourceId(HttpContext httpContext)
    {
        // Check common route parameter names
        var resourceId = httpContext.Request.RouteValues["containerId"]?.ToString()
                      ?? httpContext.Request.RouteValues["driveId"]?.ToString()
                      ?? httpContext.Request.RouteValues["documentId"]?.ToString()
                      ?? httpContext.Request.RouteValues["resourceId"]?.ToString()
                      ?? httpContext.Request.RouteValues["id"]?.ToString();

        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            return resourceId;
        }

        // Fallback: Check query string
        resourceId = httpContext.Request.Query["containerId"].FirstOrDefault()
                  ?? httpContext.Request.Query["driveId"].FirstOrDefault()
                  ?? httpContext.Request.Query["documentId"].FirstOrDefault()
                  ?? httpContext.Request.Query["resourceId"].FirstOrDefault();

        return resourceId;
    }
}
