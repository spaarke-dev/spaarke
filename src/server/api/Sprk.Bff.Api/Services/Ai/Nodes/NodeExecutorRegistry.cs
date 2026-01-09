using System.Collections.Concurrent;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Registry implementation that manages node executor discovery and resolution.
/// Uses DI container for executor instances and indexes by ActionType.
/// </summary>
/// <remarks>
/// <para>
/// Executor discovery flow:
/// </para>
/// <list type="number">
/// <item>At startup, all INodeExecutor implementations are registered in DI</item>
/// <item>Registry receives executors via constructor injection (IEnumerable)</item>
/// <item>Executors are indexed by SupportedActionTypes for fast lookup</item>
/// </list>
/// <para>
/// Follows ADR-010 DI minimalism by using constructor injection for executors.
/// </para>
/// </remarks>
public sealed class NodeExecutorRegistry : INodeExecutorRegistry
{
    private readonly ConcurrentDictionary<ActionType, INodeExecutor> _executorsByType;
    private readonly List<INodeExecutor> _allExecutors;
    private readonly ILogger<NodeExecutorRegistry> _logger;

    public NodeExecutorRegistry(
        IEnumerable<INodeExecutor> executors,
        ILogger<NodeExecutorRegistry> logger)
    {
        _executorsByType = new ConcurrentDictionary<ActionType, INodeExecutor>();
        _allExecutors = new List<INodeExecutor>();
        _logger = logger;

        RegisterExecutors(executors);
    }

    /// <inheritdoc />
    public INodeExecutor? GetExecutor(ActionType actionType)
    {
        if (_executorsByType.TryGetValue(actionType, out var executor))
            return executor;

        _logger.LogWarning("No executor registered for ActionType {ActionType}", actionType);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<INodeExecutor> GetAllExecutors() => _allExecutors.AsReadOnly();

    /// <inheritdoc />
    public bool HasExecutor(ActionType actionType) =>
        _executorsByType.ContainsKey(actionType);

    /// <inheritdoc />
    public IReadOnlyList<ActionType> GetSupportedActionTypes() =>
        _executorsByType.Keys.ToList();

    /// <inheritdoc />
    public int ExecutorCount => _allExecutors.Count;

    /// <summary>
    /// Registers all provided executors, indexing by supported ActionTypes.
    /// </summary>
    private void RegisterExecutors(IEnumerable<INodeExecutor> executors)
    {
        var executorList = executors.ToList();

        _logger.LogInformation("Registering {Count} node executors", executorList.Count);

        foreach (var executor in executorList)
        {
            var typeName = executor.GetType().Name;
            var supportedTypes = executor.SupportedActionTypes;

            if (supportedTypes.Count == 0)
            {
                _logger.LogWarning(
                    "Skipping executor {ExecutorType} with no supported ActionTypes",
                    typeName);
                continue;
            }

            _allExecutors.Add(executor);

            foreach (var actionType in supportedTypes)
            {
                if (!_executorsByType.TryAdd(actionType, executor))
                {
                    var existingExecutor = _executorsByType[actionType];
                    _logger.LogWarning(
                        "Duplicate executor for ActionType {ActionType} - {ExistingType} already registered, skipping {NewType}",
                        actionType,
                        existingExecutor.GetType().Name,
                        typeName);
                    continue;
                }

                _logger.LogDebug(
                    "Registered executor {ExecutorType} for ActionType {ActionType}",
                    typeName,
                    actionType);
            }
        }

        _logger.LogInformation(
            "Node executor registration complete: {ExecutorCount} executors, {ActionTypeCount} action types",
            _allExecutors.Count,
            _executorsByType.Count);
    }
}
