namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Registry for discovering and resolving node executors by ExecutorType.
/// </summary>
/// <remarks>
/// <para>
/// The registry provides:
/// </para>
/// <list type="bullet">
/// <item>Executor registration at startup via DI</item>
/// <item>Executor resolution by ExecutorType</item>
/// <item>Metadata for executor discovery</item>
/// </list>
/// <para>
/// Follows the same pattern as IToolHandlerRegistry per ADR-010.
/// </para>
/// </remarks>
public interface INodeExecutorRegistry
{
    /// <summary>
    /// Gets a node executor that can handle the specified ExecutorType.
    /// </summary>
    /// <param name="executorType">The executor type to find an executor for.</param>
    /// <returns>The registered executor, or null if not found.</returns>
    INodeExecutor? GetExecutor(ExecutorType executorType);

    /// <summary>
    /// Gets all registered executors.
    /// </summary>
    /// <returns>All registered executor instances.</returns>
    IReadOnlyList<INodeExecutor> GetAllExecutors();

    /// <summary>
    /// Checks if an executor is registered for the specified ExecutorType.
    /// </summary>
    /// <param name="executorType">The executor type to check.</param>
    /// <returns>True if an executor exists for this executor type.</returns>
    bool HasExecutor(ExecutorType executorType);

    /// <summary>
    /// Gets all ExecutorTypes that have registered executors.
    /// </summary>
    /// <returns>Collection of supported executor types.</returns>
    IReadOnlyList<ExecutorType> GetSupportedExecutorTypes();

    /// <summary>
    /// Gets the count of registered executors.
    /// </summary>
    int ExecutorCount { get; }
}
