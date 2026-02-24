using Microsoft.Extensions.Caching.Memory;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Handler discovery endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides metadata for registered tool handlers to enable frontend configuration validation.
/// Includes a simple IAiToolHandler class-name discovery endpoint at /api/ai/tools/handlers
/// for ScopeConfigEditorPCF handler dropdown population (AIPL-036).
/// </summary>
public static class HandlerEndpoints
{
    private const string HandlersCacheKey = "api:ai:handlers:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapHandlerEndpoints(this IEndpointRouteBuilder app)
    {
        // -----------------------------------------------------------------------
        // GET /api/ai/tools/handlers
        // Simple handler class-name discovery for ScopeConfigEditorPCF (AIPL-036).
        // Returns ClassName + Description for every registered IAiToolHandler.
        // Protected via RequireAuthorization() — no document-level resource check
        // needed for this metadata-only listing endpoint.
        // -----------------------------------------------------------------------
        var toolsGroup = app.MapGroup("/api/ai/tools")
            .RequireAuthorization()
            .WithTags("AI Tools");

        toolsGroup.MapGet("/handlers", GetToolHandlers)
            .WithName("GetToolHandlers")
            .WithSummary("Get all registered IAiToolHandler class names and descriptions")
            .WithDescription(
                "Returns the class name and description for every IAiToolHandler registered in DI. " +
                "Used by ScopeConfigEditorPCF to populate the handler class dropdown on Tool records.")
            .Produces<ToolHandlerListResponse>()
            .ProducesProblem(401)
            .ProducesProblem(500);

        // -----------------------------------------------------------------------
        // Existing rich-metadata handler registry endpoints at /api/ai/handlers
        // -----------------------------------------------------------------------
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
    /// GET /api/ai/tools/handlers - Returns class name and description for every
    /// registered IAiToolHandler (playbook tool handlers).
    /// Used by ScopeConfigEditorPCF to populate the handler class dropdown on Tool records.
    /// </summary>
    private static IResult GetToolHandlers(
        IEnumerable<IAiToolHandler> handlers,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HandlerEndpoints");

        try
        {
            var items = handlers
                .Select(h => new ToolHandlerInfoDto(
                    ClassName: h.GetType().Name,
                    ToolName: h.ToolName,
                    Description: GetHandlerDescription(h)))
                .OrderBy(h => h.ClassName)
                .ToArray();

            logger.LogDebug("[HANDLERS] GET /api/ai/tools/handlers returning {Count} IAiToolHandler entries", items.Length);
            return Results.Ok(new ToolHandlerListResponse(items));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[HANDLERS] Failed to enumerate IAiToolHandler registrations");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to enumerate registered tool handlers");
        }
    }

    /// <summary>
    /// Extracts a human-readable description from an IAiToolHandler.
    /// Uses the XML summary comment where available via a naming convention;
    /// falls back to the ToolName if no description attribute is found.
    /// </summary>
    private static string GetHandlerDescription(IAiToolHandler handler)
    {
        // Check for a [Description] attribute first (if handlers opt in).
        var descAttr = handler.GetType()
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
            .OfType<System.ComponentModel.DescriptionAttribute>()
            .FirstOrDefault();

        if (descAttr != null)
            return descAttr.Description;

        // Fall back to ToolName — clear and consistent.
        return handler.ToolName;
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

// ---------------------------------------------------------------------------
// DTOs for GET /api/ai/tools/handlers  (AIPL-036 — ScopeConfigEditorPCF)
// ---------------------------------------------------------------------------

/// <summary>
/// Response for GET /api/ai/tools/handlers.
/// Contains class names and descriptions for all registered IAiToolHandler implementations.
/// </summary>
/// <param name="Handlers">Array of handler class-name entries.</param>
public record ToolHandlerListResponse(ToolHandlerInfoDto[] Handlers);

/// <summary>
/// Lightweight handler descriptor for ScopeConfigEditorPCF dropdown population.
/// </summary>
/// <param name="ClassName">The C# class name of the handler (e.g. "FinancialCalculationToolHandler").</param>
/// <param name="ToolName">The handler's ToolName property used by playbooks to invoke it.</param>
/// <param name="Description">Human-readable description of what the handler does.</param>
public record ToolHandlerInfoDto(string ClassName, string ToolName, string Description);

// ---------------------------------------------------------------------------
// DTOs for GET /api/ai/handlers  (rich registry metadata)
// ---------------------------------------------------------------------------

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
