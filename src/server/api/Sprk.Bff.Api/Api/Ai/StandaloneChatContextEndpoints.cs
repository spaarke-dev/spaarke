using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Endpoint for resolving standalone (non-analysis) SprkChat context.
///
/// Provides <c>GET /api/ai/chat/context-mappings/standalone</c> which returns the
/// full <see cref="StandaloneChatContextResponse"/> for any supported Dataverse entity type
/// and record ID. This endpoint is used by the Spaarke AI Code Page (<c>sprk_spaarkeai</c>)
/// when SprkChat is opened outside the context of an analysis record.
///
/// Supported entity types (validated against <see cref="StandaloneChatContextProvider.SupportedEntityTypes"/>):
///   - <c>contact</c>
///   - <c>account</c>
///   - <c>opportunity</c>
///   - <c>incident</c>
///   - <c>sprk_matter</c>
///
/// Follows ADR-001 (Minimal API), ADR-008 (endpoint filters for auth), ADR-009 (Redis caching).
/// tenantId is extracted from JWT claims (ADR-014 — never from query string per ADR-008).
/// Caching is handled by <see cref="StandaloneChatContextProvider"/> (ADR-009 — Redis-first,
/// 30-minute absolute TTL; cache key: <c>chat-context:{tenantId}:standalone:{entityType}:{entityId}</c>).
/// </summary>
public static class StandaloneChatContextEndpoints
{
    /// <summary>
    /// Registers the standalone chat context endpoint on the provided route builder.
    /// Called from <see cref="Sprk.Bff.Api.Infrastructure.DI.EndpointMappingExtensions"/>:
    /// <c>app.MapStandaloneChatContextEndpoints();</c>
    /// </summary>
    public static IEndpointRouteBuilder MapStandaloneChatContextEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .WithTags("AI", "Chat");

        // GET /api/ai/chat/context-mappings/standalone
        group.MapGet("/context-mappings/standalone", GetStandaloneChatContextAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-context")
            .WithName("GetStandaloneChatContext")
            .WithSummary("Resolve standalone SprkChat context for a Dataverse entity")
            .WithDescription(
                "Returns the standalone chat context for any supported Dataverse entity type and record ID. " +
                "Provides context field descriptors (attribute logical name, display label, field type) " +
                "for the entity type, allowing the SprkChat UI to render entity metadata in the context panel. " +
                "Supported entity types: contact, account, opportunity, incident, sprk_matter. " +
                "Results are Redis-cached for 30 minutes (cache key: chat-context:{tenantId}:standalone:{entityType}:{entityId}). " +
                "Returns 400 for unsupported entity types or invalid entityId (non-GUID). " +
                "tenantId is extracted from JWT claims (tid) — never from query string (ADR-008).")
            .Produces<StandaloneChatContextResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429);

        return app;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>
    /// Resolve the standalone chat context for the given <paramref name="entityType"/> and <paramref name="entityId"/>.
    ///
    /// GET /api/ai/chat/context-mappings/standalone
    ///
    /// Validates <paramref name="entityType"/> against the supported allowlist (returns 400 on mismatch).
    /// Validates <paramref name="entityId"/> as a parseable GUID (returns 400 on parse failure).
    /// Extracts tenantId from JWT claims or X-Tenant-Id header — never from query string (ADR-008, ADR-014).
    ///
    /// Resolution is delegated to <see cref="StandaloneChatContextProvider"/> which applies
    /// Redis caching (ADR-009) and builds from the static field catalog on cache miss.
    /// Returns 404 when the entity record cannot be found (currently reserved; provider returns null only for
    /// unsupported types, which map to 400).
    /// </summary>
    private static async Task<Results<Ok<StandaloneChatContextResponse>, NotFound, ProblemHttpResult>> GetStandaloneChatContextAsync(
        HttpContext httpContext,
        StandaloneChatContextProvider provider,
        ILogger<StandaloneChatContextProvider> logger,
        string? entityType = null,
        string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        // Validate entityType (required)
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "The 'entityType' query parameter is required.");
        }

        // Validate entityId (required, must be a parseable GUID)
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "The 'entityId' query parameter is required.");
        }

        if (!Guid.TryParse(entityId, out _))
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: $"The 'entityId' value '{entityId}' is not a valid GUID.");
        }

        // Validate entityType against the supported allowlist (ADR-008, project constraint)
        if (!StandaloneChatContextProvider.SupportedEntityTypes.Contains(entityType))
        {
            return TypedResults.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: $"The entity type '{entityType}' is not supported. " +
                        $"Supported types: {string.Join(", ", StandaloneChatContextProvider.SupportedEntityTypes)}.");
        }

        // Extract tenantId from JWT claims or X-Tenant-Id header (ADR-014 — tenant-scoped cache key)
        // tenantId MUST NOT come from query string (ADR-008)
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            logger.LogWarning(
                "Tenant ID not found in claims or headers for standalone context request (entityType={EntityType}, entityId={EntityId})",
                entityType, entityId);

            return TypedResults.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        logger.LogDebug(
            "Resolving standalone chat context for {EntityType}/{EntityId} (tenant={TenantId})",
            entityType, entityId, tenantId);

        var result = await provider.ResolveAsync(entityType, entityId, tenantId, cancellationToken);

        if (result is null)
        {
            // Provider returns null only for unsupported entity types — this path is
            // normally blocked by the allowlist check above, but guarded here defensively.
            logger.LogWarning(
                "Standalone chat context not found for {EntityType}/{EntityId} (tenant={TenantId})",
                entityType, entityId, tenantId);

            return TypedResults.NotFound();
        }

        logger.LogDebug(
            "Standalone chat context resolved for {EntityType}/{EntityId}: {FieldCount} context fields",
            entityType, entityId, result.ContextFields.Count);

        return TypedResults.Ok(result);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Extracts the tenant ID from Azure AD JWT claims (<c>tid</c>) or the
    /// <c>X-Tenant-Id</c> request header for service-to-service calls.
    ///
    /// Mirrors the pattern in <see cref="AnalysisChatContextEndpoints"/>.
    /// tenantId is NEVER read from the query string per ADR-008.
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        // Primary: 'tid' claim from Azure AD JWT token
        // Microsoft.Identity.Web may map 'tid' to the long-form URI claim
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        // Fallback: X-Tenant-Id request header (service-to-service calls)
        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }

        return tenantId;
    }
}
