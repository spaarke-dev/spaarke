using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing playbook nodes in Dataverse.
/// Provides CRUD operations for nodes within a playbook, including reordering and scope management.
/// </summary>
public interface INodeService
{
    /// <summary>
    /// Get all nodes for a playbook, ordered by execution order.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of nodes ordered by execution order.</returns>
    Task<PlaybookNodeDto[]> GetNodesAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single node by ID.
    /// </summary>
    /// <param name="nodeId">Node ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Node or null if not found.</returns>
    Task<PlaybookNodeDto?> GetNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new node in a playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID to add node to.</param>
    /// <param name="request">Node creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created node.</returns>
    Task<PlaybookNodeDto> CreateNodeAsync(
        Guid playbookId,
        CreateNodeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing node.
    /// </summary>
    /// <param name="nodeId">Node ID to update.</param>
    /// <param name="request">Node update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated node.</returns>
    Task<PlaybookNodeDto> UpdateNodeAsync(
        Guid nodeId,
        UpdateNodeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a node from a playbook.
    /// </summary>
    /// <param name="nodeId">Node ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reorder nodes within a playbook.
    /// Updates execution order based on the provided node ID sequence.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="nodeIds">Ordered array of node IDs representing new order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReorderNodesAsync(
        Guid playbookId,
        Guid[] nodeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update node scopes (skills and knowledge sources).
    /// </summary>
    /// <param name="nodeId">Node ID.</param>
    /// <param name="scopes">New scope configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated node.</returns>
    Task<PlaybookNodeDto> UpdateNodeScopesAsync(
        Guid nodeId,
        NodeScopesRequest scopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate node configuration.
    /// </summary>
    /// <param name="request">Node request to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    Task<NodeValidationResult> ValidateAsync(
        CreateNodeRequest request,
        CancellationToken cancellationToken = default);
}
