using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages node dependencies and determines execution order for playbook nodes.
/// Implements topological sort using Kahn's algorithm with cycle detection.
/// </summary>
public class ExecutionGraph
{
    private readonly Dictionary<Guid, PlaybookNodeDto> _nodes;
    private readonly Dictionary<Guid, HashSet<Guid>> _adjacencyList;
    private readonly Dictionary<Guid, int> _inDegree;

    /// <summary>
    /// Creates a new execution graph from a list of playbook nodes.
    /// Disabled nodes (IsActive = false) are excluded from the graph.
    /// </summary>
    /// <param name="nodes">Playbook nodes to build graph from.</param>
    /// <exception cref="ArgumentNullException">If nodes is null.</exception>
    public ExecutionGraph(IEnumerable<PlaybookNodeDto> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        // Filter to active nodes only
        var activeNodes = nodes.Where(n => n.IsActive).ToList();
        var activeNodeIds = activeNodes.Select(n => n.Id).ToHashSet();

        _nodes = activeNodes.ToDictionary(n => n.Id);
        _adjacencyList = new Dictionary<Guid, HashSet<Guid>>();
        _inDegree = new Dictionary<Guid, int>();

        // Initialize adjacency list and in-degree for each node
        foreach (var node in activeNodes)
        {
            _adjacencyList[node.Id] = new HashSet<Guid>();
            _inDegree[node.Id] = 0;
        }

        // Build adjacency list from dependencies
        // If A depends on B, then B -> A (B must complete before A)
        foreach (var node in activeNodes)
        {
            foreach (var dependencyId in node.DependsOn)
            {
                // Only include dependencies that are active nodes
                if (activeNodeIds.Contains(dependencyId))
                {
                    _adjacencyList[dependencyId].Add(node.Id);
                    _inDegree[node.Id]++;
                }
            }
        }
    }

    /// <summary>
    /// Gets the number of active nodes in the graph.
    /// </summary>
    public int NodeCount => _nodes.Count;

    /// <summary>
    /// Gets all active nodes in the graph.
    /// </summary>
    public IReadOnlyCollection<PlaybookNodeDto> Nodes => _nodes.Values;

    /// <summary>
    /// Gets a node by ID.
    /// </summary>
    /// <param name="nodeId">Node ID to look up.</param>
    /// <returns>The node, or null if not found.</returns>
    public PlaybookNodeDto? GetNode(Guid nodeId) =>
        _nodes.TryGetValue(nodeId, out var node) ? node : null;

    /// <summary>
    /// Performs topological sort on the graph using Kahn's algorithm.
    /// </summary>
    /// <returns>Nodes in valid execution order.</returns>
    /// <exception cref="InvalidOperationException">If the graph contains a cycle.</exception>
    public IReadOnlyList<PlaybookNodeDto> GetTopologicalOrder()
    {
        if (_nodes.Count == 0)
        {
            return Array.Empty<PlaybookNodeDto>();
        }

        var result = new List<PlaybookNodeDto>();
        var inDegreeCopy = new Dictionary<Guid, int>(_inDegree);

        // Start with nodes that have no dependencies (in-degree = 0)
        var queue = new Queue<Guid>();
        foreach (var (nodeId, degree) in inDegreeCopy)
        {
            if (degree == 0)
            {
                queue.Enqueue(nodeId);
            }
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            result.Add(_nodes[nodeId]);

            // Reduce in-degree for all nodes that depend on this one
            foreach (var dependentId in _adjacencyList[nodeId])
            {
                inDegreeCopy[dependentId]--;
                if (inDegreeCopy[dependentId] == 0)
                {
                    queue.Enqueue(dependentId);
                }
            }
        }

        // If we didn't process all nodes, there's a cycle
        if (result.Count != _nodes.Count)
        {
            var cycleNodes = _nodes.Values
                .Where(n => inDegreeCopy[n.Id] > 0)
                .Select(n => n.Name)
                .ToArray();

            throw new InvalidOperationException(
                $"Playbook contains a circular dependency involving nodes: {string.Join(", ", cycleNodes)}");
        }

        return result;
    }

    /// <summary>
    /// Gets execution batches for parallel processing.
    /// Nodes in the same batch have no dependencies on each other and can run in parallel.
    /// </summary>
    /// <returns>List of batches, where each batch contains nodes that can run in parallel.</returns>
    /// <exception cref="InvalidOperationException">If the graph contains a cycle.</exception>
    public IReadOnlyList<IReadOnlyList<PlaybookNodeDto>> GetExecutionBatches()
    {
        if (_nodes.Count == 0)
        {
            return Array.Empty<IReadOnlyList<PlaybookNodeDto>>();
        }

        var batches = new List<IReadOnlyList<PlaybookNodeDto>>();
        var inDegreeCopy = new Dictionary<Guid, int>(_inDegree);
        var processedCount = 0;

        while (processedCount < _nodes.Count)
        {
            // Find all nodes with in-degree = 0 (ready to execute)
            var currentBatch = new List<PlaybookNodeDto>();
            foreach (var (nodeId, degree) in inDegreeCopy)
            {
                if (degree == 0)
                {
                    currentBatch.Add(_nodes[nodeId]);
                }
            }

            // If no nodes are ready but we haven't processed all, there's a cycle
            if (currentBatch.Count == 0 && processedCount < _nodes.Count)
            {
                var cycleNodes = _nodes.Values
                    .Where(n => inDegreeCopy[n.Id] > 0)
                    .Select(n => n.Name)
                    .ToArray();

                throw new InvalidOperationException(
                    $"Playbook contains a circular dependency involving nodes: {string.Join(", ", cycleNodes)}");
            }

            // Order nodes within batch by ExecutionOrder for deterministic output
            batches.Add(currentBatch.OrderBy(n => n.ExecutionOrder).ToList());

            // Mark batch nodes as processed
            foreach (var node in currentBatch)
            {
                inDegreeCopy[node.Id] = -1; // Mark as processed

                // Reduce in-degree for dependent nodes
                foreach (var dependentId in _adjacencyList[node.Id])
                {
                    if (inDegreeCopy[dependentId] > 0)
                    {
                        inDegreeCopy[dependentId]--;
                    }
                }
            }

            processedCount += currentBatch.Count;
        }

        return batches;
    }

    /// <summary>
    /// Validates the graph for cycles without throwing.
    /// </summary>
    /// <returns>True if graph is valid (no cycles), false otherwise.</returns>
    public bool IsValid()
    {
        if (_nodes.Count == 0)
        {
            return true;
        }

        var inDegreeCopy = new Dictionary<Guid, int>(_inDegree);
        var processedCount = 0;

        var queue = new Queue<Guid>();
        foreach (var (nodeId, degree) in inDegreeCopy)
        {
            if (degree == 0)
            {
                queue.Enqueue(nodeId);
            }
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            processedCount++;

            foreach (var dependentId in _adjacencyList[nodeId])
            {
                inDegreeCopy[dependentId]--;
                if (inDegreeCopy[dependentId] == 0)
                {
                    queue.Enqueue(dependentId);
                }
            }
        }

        return processedCount == _nodes.Count;
    }

    /// <summary>
    /// Gets the direct dependencies of a node.
    /// </summary>
    /// <param name="nodeId">Node ID to get dependencies for.</param>
    /// <returns>Node IDs that this node depends on.</returns>
    public IReadOnlyCollection<Guid> GetDependencies(Guid nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            return Array.Empty<Guid>();
        }

        // Return only dependencies that are in the active graph
        return node.DependsOn.Where(id => _nodes.ContainsKey(id)).ToList();
    }

    /// <summary>
    /// Gets nodes that directly depend on the specified node.
    /// </summary>
    /// <param name="nodeId">Node ID to get dependents for.</param>
    /// <returns>Node IDs that depend on this node.</returns>
    public IReadOnlyCollection<Guid> GetDependents(Guid nodeId)
    {
        if (!_adjacencyList.TryGetValue(nodeId, out var dependents))
        {
            return Array.Empty<Guid>();
        }

        return dependents;
    }
}
