using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Admin.Models;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Api.Admin;

/// <summary>
/// Admin endpoints for inspecting and managing the user-record membership
/// discovery cache (R3 Part 1). Maps:
/// <list type="bullet">
///   <item><c>GET  /api/admin/membership/discovered/{entityType}</c> — operator audit (FR-1A.10)</item>
///   <item><c>POST /api/admin/membership/refresh-metadata</c> — cache invalidation (FR-1A.11)</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Spec coverage</b>: R3 spec.md FR-1A.10 (discovery audit endpoint), FR-1A.11
/// (metadata refresh endpoint), AC-1A.2 (descriptor list with source <c>"auto"</c> /
/// <c>"override"</c>), AC-1A.7 (refresh invalidates so the next DiscoverAsync re-fetches
/// from Dataverse).</para>
///
/// <para><b>Auth model</b>: Both endpoints behind <c>RequireAuthorization("SystemAdmin")</c>
/// per Q6 owner clarification (existing policy at
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AuthorizationModule"/> line 241 — NOT a new
/// <c>PlatformAdmin</c> policy). Precedent: <see cref="JobsEndpoints"/> uses the same
/// policy for <c>/api/admin/jobs/*</c>; <c>RagEndpoints</c> uses it for
/// <c>/api/ai/rag/admin/*</c>.</para>
///
/// <para><b>ADR compliance</b>: ADR-001 (Minimal API), ADR-008 (endpoint-filter authorization
/// via <c>RequireAuthorization</c> on the group — NOT global middleware), ADR-010 (consumes
/// <see cref="IMembershipFieldDiscoveryService"/> as a concrete injected via the DI seam
/// established by <see cref="Sprk.Bff.Api.Infrastructure.DI.MembershipModule"/>).</para>
///
/// <para><b>BFF placement justification</b> (bff-extensions.md §A): membership discovery
/// is a Dataverse-metadata operation consumed by AI playbook nodes + endpoints in the
/// same request lifecycle. Admin tooling belongs alongside the service it operates on
/// — extracting it to a sidecar would force operators to authenticate to two surfaces.
/// Both endpoints add minimal LOC (handlers are thin pass-throughs to the service);
/// no new packages, no new transitive dependencies, publish-size impact ≈ zero.</para>
/// </remarks>
public static class MembershipAdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/admin/membership")
            .RequireAuthorization("SystemAdmin")
            .WithTags("Admin Membership");

        // ============================================================================
        // ===== Task 036 — GET /api/admin/membership/discovered/{entityType} =========
        // ============================================================================
        // FR-1A.10 / AC-1A.2: Returns the full DiscoveryResult shape (entityType,
        // discoveredAt, discoveredFields[], excludedFields[], ignoredFields[]) so
        // operators can audit which fields the discovery algorithm picked up and
        // whether the source was "auto" or "override". DiscoveryResult already
        // matches the design.md Part 1 § "Discovery Report endpoint" shape verbatim,
        // so the handler is a thin pass-through. Empty/unknown entityType returns 400.
        group.MapGet("/discovered/{entityType}", DiscoverEntityAsync)
            .WithName("AdminMembershipDiscovered")
            .WithSummary("Audit the membership discovery result for an entity")
            .WithDescription("Returns the full discovery result for the requested entity logical name: discovered fields (with source 'auto' or 'override'), excluded fields (with reason), and ignored fields (with target table). Cache hit returns immediately; cache miss issues a Dataverse RetrieveEntityRequest. Used by operators to verify the auto-discovery picked the right fields before deploying a new entity.")
            .Produces<DiscoveryResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // ===== Task 036 — POST /api/admin/membership/refresh-metadata ==============
        // ============================================================================
        // FR-1A.11 / AC-1A.7: Invalidates the per-entity discovery cache. Body is
        // optional: { "entityType": "sprk_matter" } targets a single entity; missing
        // body (or null/empty entityType) invalidates every entry the service has
        // populated since process start. Returns 200 with the list of invalidated
        // entity types + the UTC timestamp.
        group.MapPost("/refresh-metadata", RefreshMetadataAsync)
            .WithName("AdminMembershipRefreshMetadata")
            .WithSummary("Invalidate the membership discovery cache")
            .WithDescription("Invalidates the discovery cache so the next DiscoverAsync call for the affected entity re-fetches from Dataverse metadata. Optional body { entityType: 'sprk_matter' } targets a single entity; omitted/empty body invalidates every entry populated since process start. AC-1A.7 verified by a second discover call returning fresh DiscoveredAt.")
            .Produces<RefreshMetadataResponse>(StatusCodes.Status200OK)
            .Accepts<RefreshMetadataRequest>("application/json")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // ============================================================================
    // ===== Task 036 handlers ===================================================
    // ============================================================================

    /// <summary>
    /// Handler for <c>GET /api/admin/membership/discovered/{entityType}</c>.
    /// Pass-through to <see cref="IMembershipFieldDiscoveryService.DiscoverAsync"/>
    /// — the returned <see cref="DiscoveryResult"/> already matches the design.md
    /// response shape. Translates the service's exceptions to HTTP status codes:
    /// <see cref="ArgumentException"/> → 400 (empty route parameter), and
    /// <see cref="InvalidOperationException"/> with a "not found" message → 404.
    /// </summary>
    private static async Task<IResult> DiscoverEntityAsync(
        string entityType,
        IMembershipFieldDiscoveryService discoveryService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "entityType route parameter is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var result = await discoveryService.DiscoverAsync(entityType, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            // Service guards against null/empty/whitespace; the route already does
            // the same check, so this is defensive only.
            logger.LogWarning(ex,
                "MembershipAdminEndpoints: invalid entity type '{EntityType}' for discovered audit",
                entityType);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Service surfaces unknown entities as InvalidOperationException
            // "Entity 'X' not found in Dataverse metadata."
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
    }

    /// <summary>
    /// Handler for <c>POST /api/admin/membership/refresh-metadata</c>. Delegates
    /// the invalidation policy (single entity vs all) to the service per
    /// <see cref="IMembershipFieldDiscoveryService.InvalidateCacheAsync"/>. The
    /// request body is optional — Minimal API binds a null
    /// <see cref="RefreshMetadataRequest"/> when no body is sent, in which case
    /// we pass <c>null</c> as the entity-type (the service treats null/empty as
    /// "refresh all").
    /// </summary>
    private static async Task<IResult> RefreshMetadataAsync(
        [FromBody] RefreshMetadataRequest? request,
        IMembershipFieldDiscoveryService discoveryService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var requestedEntityType = request?.EntityType;
        var invalidated = await discoveryService.InvalidateCacheAsync(requestedEntityType, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "MembershipAdminEndpoints refresh-metadata invoked (requested='{Requested}', invalidatedCount={Count})",
            string.IsNullOrWhiteSpace(requestedEntityType) ? "<all>" : requestedEntityType,
            invalidated.Count);

        return Results.Ok(new RefreshMetadataResponse(
            Refreshed: invalidated,
            At: DateTimeOffset.UtcNow));
    }
}
