namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Registry for discovering and resolving tool handlers.
/// Supports dynamic loading based on HandlerId from Dataverse configuration.
/// </summary>
/// <remarks>
/// <para>
/// The registry provides:
/// </para>
/// <list type="bullet">
/// <item>Handler registration at startup via reflection</item>
/// <item>Handler resolution by HandlerId or ToolType</item>
/// <item>Configuration-based tool enabling/disabling</item>
/// <item>Metadata for tool discovery and documentation</item>
/// </list>
/// <para>
/// See ADR-013 for AI architecture patterns and ADR-010 for DI minimalism.
/// </para>
/// </remarks>
public interface IToolHandlerRegistry
{
    /// <summary>
    /// Gets a tool handler by its unique handler ID.
    /// </summary>
    /// <param name="handlerId">The handler identifier (e.g., "EntityExtractorHandler").</param>
    /// <returns>The registered handler, or null if not found.</returns>
    IAnalysisToolHandler? GetHandler(string handlerId);

    /// <summary>
    /// Gets all handlers that support a specific tool type.
    /// </summary>
    /// <param name="toolType">The tool type to filter by.</param>
    /// <returns>All handlers supporting the specified type.</returns>
    IReadOnlyList<IAnalysisToolHandler> GetHandlersByType(ToolType toolType);

    /// <summary>
    /// Gets all registered handler IDs.
    /// </summary>
    /// <returns>Collection of all registered handler IDs.</returns>
    IReadOnlyList<string> GetRegisteredHandlerIds();

    /// <summary>
    /// Gets metadata for all registered handlers.
    /// Useful for tool discovery and documentation.
    /// </summary>
    /// <returns>Metadata for all registered handlers.</returns>
    IReadOnlyList<ToolHandlerInfo> GetAllHandlerInfo();

    /// <summary>
    /// Checks if a handler is registered and enabled.
    /// </summary>
    /// <param name="handlerId">The handler identifier to check.</param>
    /// <returns>True if the handler exists and is enabled.</returns>
    bool IsHandlerAvailable(string handlerId);

    /// <summary>
    /// Gets the count of registered handlers.
    /// </summary>
    int HandlerCount { get; }
}

/// <summary>
/// Information about a registered tool handler.
/// Combines handler ID with metadata for discovery.
/// </summary>
public record ToolHandlerInfo(
    string HandlerId,
    ToolHandlerMetadata Metadata,
    IReadOnlyList<ToolType> SupportedToolTypes,
    bool IsEnabled);
