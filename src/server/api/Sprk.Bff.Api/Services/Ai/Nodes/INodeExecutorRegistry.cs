namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Registry for discovering and resolving node executors by ActionType.
/// </summary>
/// <remarks>
/// <para>
/// The registry provides:
/// </para>
/// <list type="bullet">
/// <item>Executor registration at startup via DI</item>
/// <item>Executor resolution by ActionType</item>
/// <item>Metadata for executor discovery</item>
/// </list>
/// <para>
/// Follows the same pattern as IToolHandlerRegistry per ADR-010.
/// </para>
/// </remarks>
public interface INodeExecutorRegistry
{
    /// <summary>
    /// Gets a node executor that can handle the specified ActionType.
    /// </summary>
    /// <param name="actionType">The action type to find an executor for.</param>
    /// <returns>The registered executor, or null if not found.</returns>
    INodeExecutor? GetExecutor(ActionType actionType);

    /// <summary>
    /// Gets all registered executors.
    /// </summary>
    /// <returns>All registered executor instances.</returns>
    IReadOnlyList<INodeExecutor> GetAllExecutors();

    /// <summary>
    /// Checks if an executor is registered for the specified ActionType.
    /// </summary>
    /// <param name="actionType">The action type to check.</param>
    /// <returns>True if an executor exists for this action type.</returns>
    bool HasExecutor(ActionType actionType);

    /// <summary>
    /// Gets all ActionTypes that have registered executors.
    /// </summary>
    /// <returns>Collection of supported action types.</returns>
    IReadOnlyList<ActionType> GetSupportedActionTypes();

    /// <summary>
    /// Gets the count of registered executors.
    /// </summary>
    int ExecutorCount { get; }
}
