using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Builder;

/// <summary>
/// Executes builder tools and generates canvas operations.
/// Handles the conversion from tool calls to CanvasPatch operations for the PCF control.
/// </summary>
public class BuilderToolExecutor
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<BuilderToolExecutor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BuilderToolExecutor(
        IScopeResolverService scopeResolver,
        ILogger<BuilderToolExecutor> logger)
    {
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <summary>
    /// Execute a tool call and return the result.
    /// </summary>
    /// <param name="toolCall">The tool call to execute.</param>
    /// <param name="canvasState">Current canvas state for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool execution result with canvas operations.</returns>
    public async Task<BuilderToolResult> ExecuteAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing tool: {ToolName} (id: {ToolCallId})",
            toolCall.ToolName, toolCall.Id);

        try
        {
            return toolCall.ToolName switch
            {
                BuilderToolDefinitions.ToolNames.AddNode =>
                    await ExecuteAddNodeAsync(toolCall, canvasState, cancellationToken),
                BuilderToolDefinitions.ToolNames.RemoveNode =>
                    await ExecuteRemoveNodeAsync(toolCall, canvasState, cancellationToken),
                BuilderToolDefinitions.ToolNames.CreateEdge =>
                    await ExecuteCreateEdgeAsync(toolCall, canvasState, cancellationToken),
                BuilderToolDefinitions.ToolNames.UpdateNodeConfig =>
                    await ExecuteUpdateNodeConfigAsync(toolCall, canvasState, cancellationToken),
                BuilderToolDefinitions.ToolNames.LinkScope =>
                    await ExecuteLinkScopeAsync(toolCall, canvasState, cancellationToken),
                BuilderToolDefinitions.ToolNames.SearchScopes =>
                    await ExecuteSearchScopesAsync(toolCall, cancellationToken),
                BuilderToolDefinitions.ToolNames.CreateScope =>
                    await ExecuteCreateScopeAsync(toolCall, cancellationToken),
                BuilderToolDefinitions.ToolNames.AutoLayout =>
                    await ExecuteAutoLayoutAsync(toolCall, canvasState, cancellationToken),
                BuilderToolDefinitions.ToolNames.ValidateCanvas =>
                    await ExecuteValidateCanvasAsync(toolCall, canvasState, cancellationToken),
                _ => CreateErrorResult(toolCall, $"Unknown tool: {toolCall.ToolName}")
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse arguments for tool {ToolName}", toolCall.ToolName);
            return CreateErrorResult(toolCall, $"Invalid arguments: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolCall.ToolName);
            return CreateErrorResult(toolCall, $"Tool execution failed: {ex.Message}");
        }
    }

    #region Canvas Operation Tools

    private Task<BuilderToolResult> ExecuteAddNodeAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<AddNodeArguments>(JsonOptions)
            ?? throw new JsonException("Failed to parse AddNode arguments");

        // Generate unique node ID
        var nodeId = $"node_{Guid.NewGuid():N}"[..20];

        // Calculate position if not provided
        var position = args.Position ?? CalculateNextNodePosition(canvasState);

        // Create the new node
        var newNode = new CanvasNode
        {
            Id = nodeId,
            Type = args.NodeType,
            Label = args.Label,
            Position = new NodePosition(position.X, position.Y),
            Config = args.Config != null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(args.Config.RootElement.GetRawText(), JsonOptions)
                : null
        };

        var result = new AddNodeResult { NodeId = nodeId, Success = true };

        // Create canvas patch operation
        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddNode,
            Node = newNode
        };

        _logger.LogInformation("AddNode: Created node {NodeId} of type {NodeType}", nodeId, args.NodeType);

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = new[] { CreateCanvasOperation(CanvasOperationType.AddNode, patch) }
        });
    }

    private Task<BuilderToolResult> ExecuteRemoveNodeAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<RemoveNodeArguments>(JsonOptions)
            ?? throw new JsonException("Failed to parse RemoveNode arguments");

        // Find node by ID or label
        var nodeId = args.NodeId ?? FindNodeIdByLabel(canvasState, args.NodeLabel);
        if (nodeId == null)
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Node not found"));
        }

        // Find edges to remove (connected to this node)
        var edgesToRemove = canvasState.Edges
            .Where(e => e.SourceId == nodeId || e.TargetId == nodeId)
            .Select(e => e.Id)
            .ToArray();

        var result = new RemoveNodeResult
        {
            RemovedNodeId = nodeId,
            RemovedEdgeIds = edgesToRemove,
            Success = true
        };

        // Create canvas operations (remove node + connected edges)
        var operations = new List<CanvasOperation>();

        foreach (var edgeId in edgesToRemove)
        {
            operations.Add(CreateCanvasOperation(CanvasOperationType.RemoveEdge,
                new CanvasPatch { Operation = CanvasPatchOperation.RemoveEdge, EdgeId = edgeId }));
        }

        operations.Add(CreateCanvasOperation(CanvasOperationType.RemoveNode,
            new CanvasPatch { Operation = CanvasPatchOperation.RemoveNode, NodeId = nodeId }));

        _logger.LogInformation("RemoveNode: Removed node {NodeId} and {EdgeCount} edges",
            nodeId, edgesToRemove.Length);

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = operations
        });
    }

    private Task<BuilderToolResult> ExecuteCreateEdgeAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<CreateEdgeArguments>(JsonOptions)
            ?? throw new JsonException("Failed to parse CreateEdge arguments");

        // Find source and target nodes
        var sourceId = args.SourceId ?? FindNodeIdByLabel(canvasState, args.SourceLabel);
        var targetId = args.TargetId ?? FindNodeIdByLabel(canvasState, args.TargetLabel);

        if (sourceId == null)
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Source node not found"));
        }
        if (targetId == null)
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Target node not found"));
        }

        // Check for duplicate edge
        if (canvasState.Edges.Any(e => e.SourceId == sourceId && e.TargetId == targetId))
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Edge already exists between these nodes"));
        }

        // Generate unique edge ID
        var edgeId = $"edge_{Guid.NewGuid():N}"[..20];

        var newEdge = new CanvasEdge
        {
            Id = edgeId,
            SourceId = sourceId,
            TargetId = targetId,
            EdgeType = args.EdgeType ?? "default",
            Animated = false
        };

        var result = new CreateEdgeResult { EdgeId = edgeId, Success = true };

        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddEdge,
            Edge = newEdge
        };

        _logger.LogInformation("CreateEdge: Created edge {EdgeId} from {SourceId} to {TargetId}",
            edgeId, sourceId, targetId);

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = new[] { CreateCanvasOperation(CanvasOperationType.AddEdge, patch) }
        });
    }

    private Task<BuilderToolResult> ExecuteUpdateNodeConfigAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<UpdateNodeConfigArguments>(JsonOptions)
            ?? throw new JsonException("Failed to parse UpdateNodeConfig arguments");

        // Find node
        var nodeId = args.NodeId ?? FindNodeIdByLabel(canvasState, args.NodeLabel);
        if (nodeId == null)
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Node not found"));
        }

        var existingNode = canvasState.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (existingNode == null)
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Node not found"));
        }

        // Build update data
        var updatedFields = new List<string>();
        var updateData = new Dictionary<string, object?>();

        if (args.Updates.Label != null)
        {
            updateData["label"] = args.Updates.Label;
            updatedFields.Add("label");
        }

        if (args.Updates.Position != null)
        {
            updateData["position"] = new { x = args.Updates.Position.X, y = args.Updates.Position.Y };
            updatedFields.Add("position");
        }

        if (args.Updates.Config != null)
        {
            var configUpdates = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                args.Updates.Config.RootElement.GetRawText(), JsonOptions);
            if (configUpdates != null)
            {
                updateData["config"] = configUpdates;
                updatedFields.Add("config");
            }
        }

        var result = new
        {
            nodeId,
            updatedFields = updatedFields.ToArray(),
            success = true
        };

        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.UpdateNode,
            NodeId = nodeId,
            Config = updateData
        };

        _logger.LogInformation("UpdateNodeConfig: Updated node {NodeId}, fields: {Fields}",
            nodeId, string.Join(", ", updatedFields));

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = new[] { CreateCanvasOperation(CanvasOperationType.UpdateNode, patch) }
        });
    }

    private Task<BuilderToolResult> ExecuteAutoLayoutAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<AutoLayoutArguments>(JsonOptions)
            ?? new AutoLayoutArguments();

        // Calculate new positions using simple grid layout
        // (In production, this would use dagre or similar algorithm)
        var direction = args.Direction ?? "LR";
        var hSpacing = args.NodeSpacing?.Horizontal ?? 250;
        var vSpacing = args.NodeSpacing?.Vertical ?? 150;

        var updatedPositions = new List<object>();
        var nodes = canvasState.Nodes.ToList();

        for (int i = 0; i < nodes.Count; i++)
        {
            var (x, y) = direction switch
            {
                "TB" => ((i % 3) * hSpacing + 50, (i / 3) * vSpacing + 50),
                "LR" => ((i / 3) * hSpacing + 50, (i % 3) * vSpacing + 50),
                "BT" => ((i % 3) * hSpacing + 50, (nodes.Count / 3 - i / 3) * vSpacing + 50),
                "RL" => ((nodes.Count / 3 - i / 3) * hSpacing + 50, (i % 3) * vSpacing + 50),
                _ => ((i / 3) * hSpacing + 50, (i % 3) * vSpacing + 50)
            };

            updatedPositions.Add(new { nodeId = nodes[i].Id, position = new { x, y } });
        }

        var result = new
        {
            updatedNodePositions = updatedPositions,
            nodesAffected = nodes.Count,
            success = true
        };

        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AutoLayout,
            Config = new Dictionary<string, object?>
            {
                ["positions"] = updatedPositions
            }
        };

        _logger.LogInformation("AutoLayout: Arranged {Count} nodes with direction {Direction}",
            nodes.Count, direction);

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = new[] { CreateCanvasOperation(CanvasOperationType.UpdateLayout, patch) }
        });
    }

    private Task<BuilderToolResult> ExecuteValidateCanvasAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<ValidateCanvasArguments>(JsonOptions)
            ?? new ValidateCanvasArguments();

        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        // Check for start node (nodes without incoming edges)
        var nodesWithIncoming = canvasState.Edges.Select(e => e.TargetId).ToHashSet();
        var startNodes = canvasState.Nodes.Where(n => !nodesWithIncoming.Contains(n.Id)).ToList();
        if (startNodes.Count == 0 && canvasState.Nodes.Length > 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = "NO_START_NODE",
                Message = "Canvas has no entry point (no nodes without incoming edges)",
                Severity = "error"
            });
        }

        // Check for end node (deliver node)
        var hasDeliverNode = canvasState.Nodes.Any(n =>
            n.Type.Equals("deliver", StringComparison.OrdinalIgnoreCase) ||
            n.Type.Equals("deliverOutput", StringComparison.OrdinalIgnoreCase));
        if (!hasDeliverNode && canvasState.Nodes.Length > 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = "NO_END_NODE",
                Message = "Canvas has no deliver node",
                Severity = "error"
            });
        }

        // Check for orphan nodes
        var nodesWithOutgoing = canvasState.Edges.Select(e => e.SourceId).ToHashSet();
        foreach (var node in canvasState.Nodes)
        {
            var hasIncoming = nodesWithIncoming.Contains(node.Id);
            var hasOutgoing = nodesWithOutgoing.Contains(node.Id);
            if (!hasIncoming && !hasOutgoing && canvasState.Nodes.Length > 1)
            {
                warnings.Add(new ValidationIssue
                {
                    Code = "ORPHAN_NODE",
                    Message = $"Node '{node.Label ?? node.Id}' has no connections",
                    NodeId = node.Id,
                    Severity = "warning"
                });
            }
        }

        // Check for missing action scope on aiAnalysis nodes
        foreach (var node in canvasState.Nodes.Where(n =>
            n.Type.Equals("aiAnalysis", StringComparison.OrdinalIgnoreCase)))
        {
            if (node.ScopeId == null && string.IsNullOrEmpty(node.ActionId))
            {
                errors.Add(new ValidationIssue
                {
                    Code = "MISSING_ACTION",
                    Message = $"AI Analysis node '{node.Label ?? node.Id}' has no Action scope linked",
                    NodeId = node.Id,
                    Severity = "error"
                });
            }
        }

        // Check condition nodes have both branches
        foreach (var node in canvasState.Nodes.Where(n =>
            n.Type.Equals("condition", StringComparison.OrdinalIgnoreCase)))
        {
            var outgoingEdges = canvasState.Edges.Where(e => e.SourceId == node.Id).ToList();
            var hasTrueBranch = outgoingEdges.Any(e =>
                e.EdgeType?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                e.SourceHandle?.Contains("true", StringComparison.OrdinalIgnoreCase) == true);
            var hasFalseBranch = outgoingEdges.Any(e =>
                e.EdgeType?.Equals("false", StringComparison.OrdinalIgnoreCase) == true ||
                e.SourceHandle?.Contains("false", StringComparison.OrdinalIgnoreCase) == true);

            if (!hasTrueBranch || !hasFalseBranch)
            {
                errors.Add(new ValidationIssue
                {
                    Code = "CONDITION_INCOMPLETE",
                    Message = $"Condition node '{node.Label ?? node.Id}' is missing {(!hasTrueBranch ? "true" : "false")} branch",
                    NodeId = node.Id,
                    Severity = "error"
                });
            }
        }

        var isValid = errors.Count == 0 || (args.StrictMode != true && errors.All(e => e.Severity != "error"));

        var result = new ValidateCanvasResult
        {
            IsValid = isValid,
            Errors = errors.Count > 0 ? errors : null,
            Warnings = warnings.Count > 0 ? warnings : null,
            Summary = new ValidationSummary
            {
                TotalNodes = canvasState.Nodes.Length,
                TotalEdges = canvasState.Edges.Length,
                ErrorCount = errors.Count,
                WarningCount = warnings.Count
            }
        };

        _logger.LogInformation("ValidateCanvas: {Errors} errors, {Warnings} warnings",
            errors.Count, warnings.Count);

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = new[] { CreateCanvasOperation(CanvasOperationType.ValidateResult,
                new CanvasPatch { Config = new Dictionary<string, object?> { ["validation"] = result } }) }
        });
    }

    #endregion

    #region Scope Operation Tools

    private Task<BuilderToolResult> ExecuteLinkScopeAsync(
        BuilderToolCall toolCall,
        CanvasState canvasState,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<LinkScopeArguments>(JsonOptions)
            ?? throw new JsonException("Failed to parse LinkScope arguments");

        // Find node
        var nodeId = args.NodeId ?? FindNodeIdByLabel(canvasState, args.NodeLabel);
        if (nodeId == null)
        {
            return Task.FromResult(CreateErrorResult(toolCall, "Node not found"));
        }

        // Resolve scope by ID or name
        Guid? scopeId = null;
        if (!string.IsNullOrEmpty(args.ScopeId))
        {
            scopeId = Guid.TryParse(args.ScopeId, out var parsed) ? parsed : null;
        }
        else if (!string.IsNullOrEmpty(args.ScopeName))
        {
            // Try to find scope by name using scope resolver
            // For now, just use the name as a reference
            _logger.LogInformation("LinkScope: Looking up scope by name: {ScopeName}", args.ScopeName);
        }

        var result = new
        {
            nodeId,
            linkedScopeId = args.ScopeId ?? args.ScopeName,
            scopeType = args.ScopeType,
            success = true
        };

        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.LinkScope,
            NodeId = nodeId,
            Config = new Dictionary<string, object?>
            {
                ["scopeType"] = args.ScopeType,
                ["scopeId"] = args.ScopeId,
                ["scopeName"] = args.ScopeName,
                ["replaceExisting"] = args.ReplaceExisting
            }
        };

        _logger.LogInformation("LinkScope: Linked {ScopeType} scope to node {NodeId}",
            args.ScopeType, nodeId);

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = new[] { CreateCanvasOperation(CanvasOperationType.UpdateNode, patch) }
        });
    }

    private Task<BuilderToolResult> ExecuteSearchScopesAsync(
        BuilderToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<SearchScopesArguments>(JsonOptions)
            ?? new SearchScopesArguments();

        _logger.LogInformation("SearchScopes: query='{Query}', type={Type}",
            args.Query ?? args.SemanticQuery, args.ScopeType);

        // TODO: Implement actual scope search using IScopeResolverService
        // For now, return a placeholder result with common scopes
        var results = new List<ScopeSearchResult>();

        // Add placeholder results based on query
        if (args.ScopeType == "action" || args.ScopeType == "all" || args.ScopeType == null)
        {
            results.Add(new ScopeSearchResult
            {
                ScopeId = "sys-act-001",
                Name = "SYS-ACT-001",
                DisplayName = "Entity Extraction",
                ScopeType = "Action",
                Description = "Extract named entities (parties, dates, amounts) from document text",
                OwnerType = "system",
                Tags = new[] { "extraction", "entities" }
            });
            results.Add(new ScopeSearchResult
            {
                ScopeId = "sys-act-002",
                Name = "SYS-ACT-002",
                DisplayName = "Document Summary",
                ScopeType = "Action",
                Description = "Generate TL;DR summary of document content",
                OwnerType = "system",
                Tags = new[] { "summary", "tldr" }
            });
        }

        if (args.ScopeType == "skill" || args.ScopeType == "all" || args.ScopeType == null)
        {
            results.Add(new ScopeSearchResult
            {
                ScopeId = "sys-skl-001",
                Name = "SYS-SKL-001",
                DisplayName = "Real Estate Domain",
                ScopeType = "Skill",
                Description = "Domain expertise for real estate documents (leases, deeds, easements)",
                OwnerType = "system",
                Tags = new[] { "real-estate", "lease" }
            });
        }

        var searchResult = new SearchScopesResult
        {
            Results = results.Take(args.Limit ?? 10).ToList(),
            TotalCount = results.Count,
            Success = true
        };

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(searchResult, JsonOptions)),
            CanvasOperations = null // Search doesn't modify canvas
        });
    }

    private Task<BuilderToolResult> ExecuteCreateScopeAsync(
        BuilderToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var args = toolCall.Arguments.Deserialize<CreateScopeArguments>(JsonOptions)
            ?? throw new JsonException("Failed to parse CreateScope arguments");

        _logger.LogInformation("CreateScope: Creating {ScopeType} scope '{Name}'",
            args.ScopeType, args.Name);

        // TODO: Implement actual scope creation in Dataverse using IScopeManagementService
        // For now, return a placeholder result
        var scopeId = Guid.NewGuid().ToString();
        var fullName = $"CUST-{args.ScopeType.ToUpper()[..3]}-{args.Name}";

        var result = new CreateScopeResult
        {
            ScopeId = scopeId,
            FullName = fullName,
            Success = true
        };

        return Task.FromResult(new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = true,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions)),
            CanvasOperations = null // Scope creation doesn't directly modify canvas
        });
    }

    #endregion

    #region Helper Methods

    private static string? FindNodeIdByLabel(CanvasState canvasState, string? label)
    {
        if (string.IsNullOrEmpty(label)) return null;
        return canvasState.Nodes
            .FirstOrDefault(n => n.Label?.Equals(label, StringComparison.OrdinalIgnoreCase) == true)
            ?.Id;
    }

    private static NodePosition CalculateNextNodePosition(CanvasState canvasState)
    {
        if (canvasState.Nodes.Length == 0)
        {
            return new NodePosition(100, 100);
        }

        // Find rightmost node and place new node to its right
        var maxX = canvasState.Nodes
            .Where(n => n.Position != null)
            .Select(n => n.Position!.X)
            .DefaultIfEmpty(0)
            .Max();

        var avgY = canvasState.Nodes
            .Where(n => n.Position != null)
            .Select(n => n.Position!.Y)
            .DefaultIfEmpty(100)
            .Average();

        return new NodePosition(maxX + 250, avgY);
    }

    private static CanvasOperation CreateCanvasOperation(CanvasOperationType type, CanvasPatch patch)
    {
        return new CanvasOperation
        {
            Type = type,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(patch, JsonOptions))
        };
    }

    private static BuilderToolResult CreateErrorResult(BuilderToolCall toolCall, string error)
    {
        return new BuilderToolResult
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.ToolName,
            Success = false,
            Error = error,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(new { success = false, error }, JsonOptions))
        };
    }

    #endregion
}
