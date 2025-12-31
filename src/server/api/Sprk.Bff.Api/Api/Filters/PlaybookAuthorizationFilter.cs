using System.Security.Claims;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding playbook authorization to endpoints.
/// </summary>
public static class PlaybookAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds authorization for playbook owner operations (update, delete, share).
    /// User must own the playbook to perform the operation.
    /// </summary>
    public static TBuilder AddPlaybookOwnerAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var playbookService = context.HttpContext.RequestServices.GetRequiredService<IPlaybookService>();
            var sharingService = context.HttpContext.RequestServices.GetService<IPlaybookSharingService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<PlaybookAuthorizationFilter>>();
            var filter = new PlaybookAuthorizationFilter(playbookService, sharingService, logger, PlaybookAuthorizationMode.OwnerOnly);
            return await filter.InvokeAsync(context, next);
        });
    }

    /// <summary>
    /// Adds authorization for playbook access operations (read).
    /// User must own the playbook, have shared access via team/organization, or it must be public.
    /// </summary>
    public static TBuilder AddPlaybookAccessAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var playbookService = context.HttpContext.RequestServices.GetRequiredService<IPlaybookService>();
            var sharingService = context.HttpContext.RequestServices.GetService<IPlaybookSharingService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<PlaybookAuthorizationFilter>>();
            var filter = new PlaybookAuthorizationFilter(playbookService, sharingService, logger, PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization mode for playbook endpoints.
/// </summary>
public enum PlaybookAuthorizationMode
{
    /// <summary>User must own the playbook.</summary>
    OwnerOnly,

    /// <summary>User must own the playbook, have shared access, or it must be public.</summary>
    OwnerOrSharedOrPublic
}

/// <summary>
/// Authorization filter for Playbook endpoints.
/// Validates user has appropriate access to playbook records.
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
///
/// Authorization strategy:
/// - OwnerOnly: User must be the owner of the playbook
/// - OwnerOrSharedOrPublic: User owns the playbook OR has shared access (team/org) OR playbook.IsPublic == true
/// </remarks>
public class PlaybookAuthorizationFilter : IEndpointFilter
{
    private readonly IPlaybookService _playbookService;
    private readonly IPlaybookSharingService? _sharingService;
    private readonly ILogger<PlaybookAuthorizationFilter>? _logger;
    private readonly PlaybookAuthorizationMode _mode;

    public PlaybookAuthorizationFilter(
        IPlaybookService playbookService,
        IPlaybookSharingService? sharingService,
        ILogger<PlaybookAuthorizationFilter>? logger,
        PlaybookAuthorizationMode mode)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _sharingService = sharingService;
        _logger = logger;
        _mode = mode;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        // Extract playbook ID from route
        if (!httpContext.Request.RouteValues.TryGetValue("id", out var playbookIdValue) ||
            !Guid.TryParse(playbookIdValue?.ToString(), out var playbookId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Playbook identifier not found in request");
        }

        // Get playbook to check ownership and public status
        var playbook = await _playbookService.GetPlaybookAsync(playbookId);
        if (playbook == null)
        {
            _logger?.LogWarning("Playbook not found: {PlaybookId}", playbookId);
            return Results.NotFound();
        }

        // Check authorization based on mode
        var isOwner = playbook.OwnerId == userId;
        var isPublic = playbook.IsPublic;
        var authorized = false;

        switch (_mode)
        {
            case PlaybookAuthorizationMode.OwnerOnly:
                authorized = isOwner;
                break;

            case PlaybookAuthorizationMode.OwnerOrSharedOrPublic:
                if (isOwner || isPublic)
                {
                    authorized = true;
                }
                else if (_sharingService != null)
                {
                    // Check for team-based or organization-wide access
                    authorized = await _sharingService.UserHasSharedAccessAsync(
                        playbookId, userId, PlaybookAccessRights.Read);
                }
                break;
        }

        if (!authorized)
        {
            _logger?.LogWarning(
                "Playbook authorization denied: User {UserId} lacks {Mode} access to playbook {PlaybookId}",
                userId, _mode, playbookId);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: _mode == PlaybookAuthorizationMode.OwnerOnly
                    ? "You do not have permission to modify this playbook"
                    : "You do not have permission to access this playbook");
        }

        _logger?.LogDebug(
            "Playbook authorization granted: User {UserId} authorized for playbook {PlaybookId} (Mode: {Mode})",
            userId, playbookId, _mode);

        return await next(context);
    }
}
