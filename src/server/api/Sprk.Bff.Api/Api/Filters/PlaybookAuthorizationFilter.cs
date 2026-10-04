using System.Security.Claims;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Infrastructure.Authentication;

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

    /// <summary>The <see cref="Spaarke.Core.Auth.OperationAccessPolicy"/> key the playbook-use decision asks of a playbook row.</summary>
    public const string PlaybookUseOperation = "read";

    /// <summary>
    /// The playbook-use decision — "may this caller run this playbook" — shared by every route that runs or binds a
    /// caller-chosen playbook (unified-access-control-r2 task 162: /api/ai/analysis/execute and /promote; task 164
    /// switches this filter's own routes and the chat/agent playbook routes to it).
    /// </summary>
    /// <returns>
    /// <c>null</c> when no check is needed: the playbook exists and is PUBLIC (<c>sprk_ispublic</c>, an application
    /// flag, not a Dataverse share). Otherwise a <see cref="FinanceCheckPath.Record"/> check, operation
    /// <see cref="PlaybookUseOperation"/>, on that <c>sprk_analysisplaybook</c> row — so Dataverse's OWN answer decides
    /// (ownership, team POA shares, role depth; owner round 9). A playbook <see cref="IPlaybookService.GetPlaybookAsync"/>
    /// does not find gets the same check, which Dataverse answers None for, so unknown and denied are one answer.
    /// </returns>
    /// <remarks>
    /// Deliberately does NOT compare <c>playbook.OwnerId</c> (a Dataverse systemuserid) with the caller's Entra oid:
    /// the two GUID spaces differ, which is the defect in <see cref="InvokeAsync"/>'s owner branch (owner round 12
    /// item 6, task 164). A fault in the lookup propagates; every caller denies on it (ADR-003).
    /// </remarks>
    public static async Task<FinanceAuthorizationCheck?> BuildPlaybookUseCheckAsync(
        IPlaybookService playbookService, Guid playbookId, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playbookService);

        if (playbookId != Guid.Empty)
        {
            var playbook = await playbookService.GetPlaybookAsync(playbookId, cancellationToken);
            if (playbook is { IsPublic: true })
            {
                return null;
            }
        }

        // An empty id reaches the evaluator as a check with no record, which it denies (no_target).
        return new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = PlaybookService.EntitySetName,
            RecordId = playbookId,
            Operation = PlaybookUseOperation,
            Source = source,
        };
    }

    /// <summary>
    /// The same decision as <see cref="BuildPlaybookUseCheckAsync(IPlaybookService, Guid, string, CancellationToken)"/>,
    /// resolving <see cref="IPlaybookService"/> from <paramref name="services"/>. For callers OUTSIDE the AI API surface
    /// (e.g. AnalysisAuthorizationFilter): ADR-013 / FR-C6 keeps <see cref="IPlaybookService"/> out of their type
    /// dependencies (ADR013_AiBoundaryTests), and this filter is the grandfathered owner of that lookup. A missing
    /// registration throws, which every caller denies on (ADR-003).
    /// </summary>
    public static Task<FinanceAuthorizationCheck?> BuildPlaybookUseCheckAsync(
        IServiceProvider services, Guid playbookId, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return BuildPlaybookUseCheckAsync(
            services.GetRequiredService<IPlaybookService>(), playbookId, source, cancellationToken);
    }

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

        // Order matters: NameIdentifier carries `sub` (non-GUID) under inbound claim mapping and is
        // ALWAYS present, so the former `?? oid` tail never ran and the Guid.TryParse below always
        // failed. See CallerResolution.
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
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
