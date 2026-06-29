using System.Collections.Concurrent;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Registry implementation that manages node executor discovery and resolution.
/// Uses DI container for executor instances and indexes by ExecutorType.
/// </summary>
/// <remarks>
/// <para>
/// Executor discovery flow:
/// </para>
/// <list type="number">
/// <item>At startup, all INodeExecutor implementations are registered in DI</item>
/// <item>Registry receives executors via constructor injection (IEnumerable)</item>
/// <item>Executors are indexed by SupportedExecutorTypes for fast lookup</item>
/// </list>
/// <para>
/// Follows ADR-010 DI minimalism by using constructor injection for executors.
/// </para>
/// </remarks>
public sealed class NodeExecutorRegistry : INodeExecutorRegistry
{
    private readonly ConcurrentDictionary<ExecutorType, INodeExecutor> _executorsByType;
    private readonly List<INodeExecutor> _allExecutors;
    private readonly ILogger<NodeExecutorRegistry> _logger;

    public NodeExecutorRegistry(
        IEnumerable<INodeExecutor> executors,
        ILogger<NodeExecutorRegistry> logger)
    {
        _executorsByType = new ConcurrentDictionary<ExecutorType, INodeExecutor>();
        _allExecutors = new List<INodeExecutor>();
        _logger = logger;

        RegisterExecutors(executors);
    }

    /// <inheritdoc />
    public INodeExecutor? GetExecutor(ExecutorType executorType)
    {
        if (_executorsByType.TryGetValue(executorType, out var executor))
            return executor;

        _logger.LogWarning("No executor registered for ExecutorType {ExecutorType}", executorType);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<INodeExecutor> GetAllExecutors() => _allExecutors.AsReadOnly();

    /// <inheritdoc />
    public bool HasExecutor(ExecutorType executorType) =>
        _executorsByType.ContainsKey(executorType);

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> GetSupportedExecutorTypes() =>
        _executorsByType.Keys.ToList();

    /// <inheritdoc />
    public int ExecutorCount => _allExecutors.Count;

    /// <summary>
    /// Registers all provided executors, indexing by supported ExecutorTypes.
    /// </summary>
    private void RegisterExecutors(IEnumerable<INodeExecutor> executors)
    {
        var executorList = executors.ToList();

        _logger.LogInformation("Registering {Count} node executors", executorList.Count);

        foreach (var executor in executorList)
        {
            var typeName = executor.GetType().Name;
            var supportedTypes = executor.SupportedExecutorTypes;

            if (supportedTypes.Count == 0)
            {
                _logger.LogWarning(
                    "Skipping executor {ExecutorType} with no supported ExecutorTypes",
                    typeName);
                continue;
            }

            _allExecutors.Add(executor);

            foreach (var executorType in supportedTypes)
            {
                if (!_executorsByType.TryAdd(executorType, executor))
                {
                    var existingExecutor = _executorsByType[executorType];
                    _logger.LogWarning(
                        "Duplicate executor for ExecutorType {ExecutorType} - {ExistingType} already registered, skipping {NewType}",
                        executorType,
                        existingExecutor.GetType().Name,
                        typeName);
                    continue;
                }

                // Per-executor logging removed — see batch summary below
            }
        }

        _logger.LogInformation(
            "Node executor registration complete: {ExecutorCount} executors, {ExecutorTypeCount} executor types",
            _allExecutors.Count,
            _executorsByType.Count);
    }
}
