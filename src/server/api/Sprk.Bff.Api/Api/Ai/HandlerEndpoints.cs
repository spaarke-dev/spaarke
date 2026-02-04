using Microsoft.Extensions.Caching.Memory;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Handler discovery endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides metadata for registered tool handlers to enable frontend configuration validation.
/// </summary>
public static class HandlerEndpoints
{
    private const string HandlersCacheKey = "api:ai:handlers:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapHandlerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/handlers")
            .RequireAuthorization()
            .WithTags("AI Handlers");

        // GET /api/ai/handlers - Get all registered handler metadata
        group.MapGet("/", GetHandlers)
            .WithName("GetHandlers")
            .WithSummary("Get all registered tool handlers")
            .WithDescription("Returns metadata for all registered tool handlers including supported types, parameters, and configuration schema. Response is cached for 5 minutes.")
            .Produces<HandlersResponse>()
            .ProducesProblem(401)
            .ProducesProblem(500);

        // GET /api/ai/handlers/{handlerId} - Get specific handler metadata
        group.MapGet("/{handlerId}", GetHandler)
            .WithName("GetHandler")
            .WithSummary("Get specific tool handler metadata")
            .WithDescription("Returns metadata for a specific tool handler by its handler ID.")
            .Produces<HandlerDto>()
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// GET /api/ai/handlers - Returns metadata for all registered tool handlers.
    /// Uses cache-aside pattern with 5-minute TTL per ADR-014.
    /// </summary>
    private static IResult GetHandlers(
        IToolHandlerRegistry registry,
        IMemoryCache cache,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HandlerEndpoints");

        // Try to get from cache first
        if (cache.TryGetValue(HandlersCacheKey, out HandlersResponse? cachedResponse) && cachedResponse != null)
        {
            logger.LogDebug("[HANDLERS] Returning {Count} handlers from cache", cachedResponse.Handlers.Length);
            return Results.Ok(cachedResponse);
        }

        // Get fresh data from registry
        var handlerInfos = registry.GetAllHandlerInfo();

        var handlers = handlerInfos.Select(h => new HandlerDto(
            HandlerId: h.HandlerId,
            Name: h.Metadata.Name,
            Description: h.Metadata.Description,
            Version: h.Metadata.Version,
            SupportedToolTypes: h.SupportedToolTypes.Select(t => t.ToString()).ToArray(),
            SupportedInputTypes: h.Metadata.SupportedInputTypes.ToArray(),
            Parameters: h.Metadata.Parameters.Select(p => new ParameterDto(
                Name: p.Name,
                Description: p.Description,
                Type: p.Type.ToString().ToLowerInvariant(),
                Required: p.Required,
                DefaultValue: p.DefaultValue
            )).ToArray(),
            ConfigurationSchema: h.Metadata.ConfigurationSchema,
            IsEnabled: h.IsEnabled
        )).ToArray();

        var response = new HandlersResponse(handlers);

        // Cache the response
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetPriority(CacheItemPriority.Normal);

        cache.Set(HandlersCacheKey, response, cacheOptions);

        logger.LogInformation("[HANDLERS] Loaded {Count} handlers from registry, cached for {Minutes} minutes",
            handlers.Length, CacheDuration.TotalMinutes);

        return Results.Ok(response);
    }

    /// <summary>
    /// GET /api/ai/handlers/{handlerId} - Returns metadata for a specific handler.
    /// </summary>
    private static IResult GetHandler(
        string handlerId,
        IToolHandlerRegistry registry,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HandlerEndpoints");

        var handler = registry.GetHandler(handlerId);
        if (handler == null)
        {
            logger.LogWarning("[HANDLERS] Handler {HandlerId} not found or disabled", handlerId);
            return Results.NotFound(new { error = $"Handler '{handlerId}' not found or is disabled." });
        }

        var handlerInfo = registry.GetAllHandlerInfo().FirstOrDefault(h => h.HandlerId == handlerId);
        if (handlerInfo == null)
        {
            return Results.NotFound(new { error = $"Handler info for '{handlerId}' not found." });
        }

        var dto = new HandlerDto(
            HandlerId: handlerInfo.HandlerId,
            Name: handlerInfo.Metadata.Name,
            Description: handlerInfo.Metadata.Description,
            Version: handlerInfo.Metadata.Version,
            SupportedToolTypes: handlerInfo.SupportedToolTypes.Select(t => t.ToString()).ToArray(),
            SupportedInputTypes: handlerInfo.Metadata.SupportedInputTypes.ToArray(),
            Parameters: handlerInfo.Metadata.Parameters.Select(p => new ParameterDto(
                Name: p.Name,
                Description: p.Description,
                Type: p.Type.ToString().ToLowerInvariant(),
                Required: p.Required,
                DefaultValue: p.DefaultValue
            )).ToArray(),
            ConfigurationSchema: handlerInfo.Metadata.ConfigurationSchema,
            IsEnabled: handlerInfo.IsEnabled
        );

        logger.LogDebug("[HANDLERS] Retrieved handler {HandlerId}", handlerId);
        return Results.Ok(dto);
    }
}

#region Response DTOs

/// <summary>
/// Response containing all registered handlers.
/// </summary>
/// <param name="Handlers">Array of handler metadata.</param>
public record HandlersResponse(HandlerDto[] Handlers);

/// <summary>
/// Metadata for a single tool handler.
/// </summary>
/// <param name="HandlerId">Unique handler identifier matching HandlerClass in Dataverse.</param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="Description">Description of handler capabilities.</param>
/// <param name="Version">Handler version for compatibility tracking.</param>
/// <param name="SupportedToolTypes">Tool types this handler can process.</param>
/// <param name="SupportedInputTypes">Content types this handler accepts.</param>
/// <param name="Parameters">Configuration parameters accepted by this handler.</param>
/// <param name="ConfigurationSchema">JSON Schema for configuration validation (optional).</param>
/// <param name="IsEnabled">Whether this handler is currently enabled.</param>
public record HandlerDto(
    string HandlerId,
    string Name,
    string Description,
    string Version,
    string[] SupportedToolTypes,
    string[] SupportedInputTypes,
    ParameterDto[] Parameters,
    object? ConfigurationSchema,
    bool IsEnabled);

/// <summary>
/// Configuration parameter definition.
/// </summary>
/// <param name="Name">Parameter name (JSON property name).</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="Type">Parameter type (string, integer, boolean, decimal, array, object).</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="DefaultValue">Default value if not specified.</param>
public record ParameterDto(
    string Name,
    string Description,
    string Type,
    bool Required,
    object? DefaultValue);

#endregion
