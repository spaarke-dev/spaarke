using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Registry implementation that manages tool handler discovery and resolution.
/// Uses DI container for handler instances and supports configuration-based filtering.
/// </summary>
/// <remarks>
/// <para>
/// Handler discovery flow:
/// </para>
/// <list type="number">
/// <item>At startup, all IAnalysisToolHandler implementations are registered in DI</item>
/// <item>Registry receives handlers via constructor injection (IEnumerable)</item>
/// <item>Handlers are indexed by HandlerId for fast lookup</item>
/// <item>Configuration can disable specific handlers</item>
/// </list>
/// <para>
/// Follows ADR-010 DI minimalism by using constructor injection for handlers.
/// </para>
/// </remarks>
public sealed class ToolHandlerRegistry : IToolHandlerRegistry
{
    private readonly ConcurrentDictionary<string, IAnalysisToolHandler> _handlers;
    private readonly ConcurrentDictionary<ToolType, List<IAnalysisToolHandler>> _handlersByType;
    private readonly HashSet<string> _disabledHandlers;
    private readonly ILogger<ToolHandlerRegistry> _logger;

    public ToolHandlerRegistry(
        IEnumerable<IAnalysisToolHandler> handlers,
        IOptions<ToolFrameworkOptions> options,
        ILogger<ToolHandlerRegistry> logger)
    {
        _handlers = new ConcurrentDictionary<string, IAnalysisToolHandler>(StringComparer.OrdinalIgnoreCase);
        _handlersByType = new ConcurrentDictionary<ToolType, List<IAnalysisToolHandler>>();
        _disabledHandlers = new HashSet<string>(
            options.Value.DisabledHandlers ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        _logger = logger;

        RegisterHandlers(handlers);
    }

    /// <inheritdoc />
    public IAnalysisToolHandler? GetHandler(string handlerId)
    {
        if (string.IsNullOrWhiteSpace(handlerId))
            return null;

        if (_disabledHandlers.Contains(handlerId))
        {
            _logger.LogDebug("Handler {HandlerId} is disabled by configuration", handlerId);
            return null;
        }

        if (_handlers.TryGetValue(handlerId, out var handler))
            return handler;

        _logger.LogWarning("Handler {HandlerId} not found in registry", handlerId);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<IAnalysisToolHandler> GetHandlersByType(ToolType toolType)
    {
        if (!_handlersByType.TryGetValue(toolType, out var handlers))
            return Array.Empty<IAnalysisToolHandler>();

        // Filter out disabled handlers
        return handlers
            .Where(h => !_disabledHandlers.Contains(h.HandlerId))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredHandlerIds()
    {
        return _handlers.Keys
            .Where(id => !_disabledHandlers.Contains(id))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolHandlerInfo> GetAllHandlerInfo()
    {
        return _handlers.Values
            .Select(h => new ToolHandlerInfo(
                h.HandlerId,
                h.Metadata,
                h.SupportedToolTypes,
                !_disabledHandlers.Contains(h.HandlerId)))
            .OrderBy(h => h.HandlerId)
            .ToList();
    }

    /// <inheritdoc />
    public bool IsHandlerAvailable(string handlerId)
    {
        if (string.IsNullOrWhiteSpace(handlerId))
            return false;

        return _handlers.ContainsKey(handlerId) && !_disabledHandlers.Contains(handlerId);
    }

    /// <inheritdoc />
    public int HandlerCount => _handlers.Count(h => !_disabledHandlers.Contains(h.Key));

    /// <summary>
    /// Registers all provided handlers, indexing by HandlerId and ToolType.
    /// </summary>
    private void RegisterHandlers(IEnumerable<IAnalysisToolHandler> handlers)
    {
        var handlerList = handlers.ToList();

        _logger.LogInformation("Registering {Count} tool handlers", handlerList.Count);

        foreach (var handler in handlerList)
        {
            var handlerId = handler.HandlerId;

            if (string.IsNullOrWhiteSpace(handlerId))
            {
                _logger.LogWarning(
                    "Skipping handler {Type} with empty HandlerId",
                    handler.GetType().Name);
                continue;
            }

            // Register by HandlerId
            if (!_handlers.TryAdd(handlerId, handler))
            {
                _logger.LogWarning(
                    "Duplicate HandlerId {HandlerId} - handler {ExistingType} already registered, skipping {NewType}",
                    handlerId,
                    _handlers[handlerId].GetType().Name,
                    handler.GetType().Name);
                continue;
            }

            // Index by supported tool types
            foreach (var toolType in handler.SupportedToolTypes)
            {
                var typeHandlers = _handlersByType.GetOrAdd(toolType, _ => new List<IAnalysisToolHandler>());
                lock (typeHandlers)
                {
                    typeHandlers.Add(handler);
                }
            }

            var status = _disabledHandlers.Contains(handlerId) ? "disabled" : "enabled";
            _logger.LogDebug(
                "Registered handler {HandlerId} ({Type}) - {Status}, supports: [{ToolTypes}]",
                handlerId,
                handler.GetType().Name,
                status,
                string.Join(", ", handler.SupportedToolTypes));
        }

        _logger.LogInformation(
            "Tool handler registration complete: {Enabled} enabled, {Disabled} disabled, {Total} total",
            HandlerCount,
            _handlers.Count - HandlerCount,
            _handlers.Count);
    }
}
