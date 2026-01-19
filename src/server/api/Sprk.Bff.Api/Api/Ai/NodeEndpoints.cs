using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Node management endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides CRUD operations for playbook nodes within a playbook context.
/// </summary>
public static class NodeEndpoints
{
    public static IEndpointRouteBuilder MapNodeEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Re-enable authorization once MSAL auth is implemented in PlaybookBuilderHost PCF
        // For development/testing, endpoints are temporarily accessible without authentication.
        // Production deployment MUST restore .RequireAuthorization() and implement proper auth.
        var group = app.MapGroup("/api/ai/playbooks/{id:guid}/nodes")
            .AllowAnonymous()  // TEMPORARY: Allow anonymous for development (was: .RequireAuthorization())
            .WithTags("AI Playbook Nodes");

        // GET /api/ai/playbooks/{id}/nodes - List all nodes for a playbook
        group.MapGet("/", ListNodes)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("ListPlaybookNodes")
            .WithSummary("List all nodes for a playbook")
            .WithDescription("Returns all nodes in the playbook, ordered by execution order.")
            .Produces<PlaybookNodeDto[]>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // POST /api/ai/playbooks/{id}/nodes - Create a new node
        group.MapPost("/", CreateNode)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("CreatePlaybookNode")
            .WithSummary("Create a new node in the playbook")
            .WithDescription("Creates a new node with specified action, scopes, and configuration.")
            .Produces<PlaybookNodeDto>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesValidationProblem();

        // GET /api/ai/playbooks/{id}/nodes/{nodeId} - Get a single node
        group.MapGet("/{nodeId:guid}", GetNode)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("GetPlaybookNode")
            .WithSummary("Get a single node by ID")
            .WithDescription("Retrieves a specific node from the playbook.")
            .Produces<PlaybookNodeDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // PUT /api/ai/playbooks/{id}/nodes/{nodeId} - Update a node
        group.MapPut("/{nodeId:guid}", UpdateNode)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("UpdatePlaybookNode")
            .WithSummary("Update an existing node")
            .WithDescription("Updates node configuration. Only specified fields are updated.")
            .Produces<PlaybookNodeDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesValidationProblem();

        // DELETE /api/ai/playbooks/{id}/nodes/{nodeId} - Delete a node
        group.MapDelete("/{nodeId:guid}", DeleteNode)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("DeletePlaybookNode")
            .WithSummary("Delete a node from the playbook")
            .WithDescription("Removes a node from the playbook. Downstream nodes may need dependency updates.")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // PUT /api/ai/playbooks/{id}/nodes/reorder - Reorder nodes
        group.MapPut("/reorder", ReorderNodes)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("ReorderPlaybookNodes")
            .WithSummary("Reorder nodes in the playbook")
            .WithDescription("Updates the execution order of nodes based on the provided sequence.")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // PUT /api/ai/playbooks/{id}/nodes/{nodeId}/scopes - Update node scopes
        group.MapPut("/{nodeId:guid}/scopes", UpdateNodeScopes)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("UpdatePlaybookNodeScopes")
            .WithSummary("Update node scopes (skills and knowledge)")
            .WithDescription("Updates the skill and knowledge associations for a node.")
            .Produces<PlaybookNodeDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    /// <summary>
    /// List all nodes for a playbook.
    /// GET /api/ai/playbooks/{id}/nodes
    /// </summary>
    private static async Task<IResult> ListNodes(
        Guid id,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        try
        {
            var nodes = await nodeService.GetNodesAsync(id, cancellationToken);
            logger.LogDebug("Listed {Count} nodes for playbook {PlaybookId}", nodes.Length, id);
            return Results.Ok(nodes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list nodes for playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list nodes");
        }
    }

    /// <summary>
    /// Create a new node in the playbook.
    /// POST /api/ai/playbooks/{id}/nodes
    /// </summary>
    private static async Task<IResult> CreateNode(
        Guid id,
        CreateNodeRequest request,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        // Validate request
        var validationResult = await nodeService.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Node"] = validationResult.Errors
                });
        }

        try
        {
            var node = await nodeService.CreateNodeAsync(id, request, cancellationToken);
            logger.LogInformation("Created node {NodeId}: {Name} in playbook {PlaybookId}",
                node.Id, node.Name, id);

            return Results.Created($"/api/ai/playbooks/{id}/nodes/{node.Id}", node);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create node in playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to create node");
        }
    }

    /// <summary>
    /// Get a single node by ID.
    /// GET /api/ai/playbooks/{id}/nodes/{nodeId}
    /// </summary>
    private static async Task<IResult> GetNode(
        Guid id,
        Guid nodeId,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        try
        {
            var node = await nodeService.GetNodeAsync(nodeId, cancellationToken);
            if (node == null)
            {
                return Results.NotFound();
            }

            // Verify node belongs to the playbook
            if (node.PlaybookId != id)
            {
                logger.LogWarning("Node {NodeId} does not belong to playbook {PlaybookId}", nodeId, id);
                return Results.NotFound();
            }

            return Results.Ok(node);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get node {NodeId}", nodeId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get node");
        }
    }

    /// <summary>
    /// Update an existing node.
    /// PUT /api/ai/playbooks/{id}/nodes/{nodeId}
    /// </summary>
    private static async Task<IResult> UpdateNode(
        Guid id,
        Guid nodeId,
        UpdateNodeRequest request,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        // Verify node exists and belongs to playbook
        var existingNode = await nodeService.GetNodeAsync(nodeId, cancellationToken);
        if (existingNode == null)
        {
            return Results.NotFound();
        }
        if (existingNode.PlaybookId != id)
        {
            logger.LogWarning("Node {NodeId} does not belong to playbook {PlaybookId}", nodeId, id);
            return Results.NotFound();
        }

        try
        {
            var node = await nodeService.UpdateNodeAsync(nodeId, request, cancellationToken);
            logger.LogInformation("Updated node {NodeId}: {Name}", node.Id, node.Name);
            return Results.Ok(node);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update node {NodeId}", nodeId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to update node");
        }
    }

    /// <summary>
    /// Delete a node from the playbook.
    /// DELETE /api/ai/playbooks/{id}/nodes/{nodeId}
    /// </summary>
    private static async Task<IResult> DeleteNode(
        Guid id,
        Guid nodeId,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        // Verify node exists and belongs to playbook
        var existingNode = await nodeService.GetNodeAsync(nodeId, cancellationToken);
        if (existingNode == null)
        {
            return Results.NotFound();
        }
        if (existingNode.PlaybookId != id)
        {
            logger.LogWarning("Node {NodeId} does not belong to playbook {PlaybookId}", nodeId, id);
            return Results.NotFound();
        }

        try
        {
            var deleted = await nodeService.DeleteNodeAsync(nodeId, cancellationToken);
            if (!deleted)
            {
                return Results.NotFound();
            }

            logger.LogInformation("Deleted node {NodeId} from playbook {PlaybookId}", nodeId, id);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete node {NodeId}", nodeId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to delete node");
        }
    }

    /// <summary>
    /// Reorder nodes in the playbook.
    /// PUT /api/ai/playbooks/{id}/nodes/reorder
    /// </summary>
    private static async Task<IResult> ReorderNodes(
        Guid id,
        ReorderNodesRequest request,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        if (request.NodeIds == null || request.NodeIds.Length == 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["NodeIds"] = ["NodeIds array is required and must not be empty"]
                });
        }

        try
        {
            await nodeService.ReorderNodesAsync(id, request.NodeIds, cancellationToken);
            logger.LogInformation("Reordered {Count} nodes in playbook {PlaybookId}",
                request.NodeIds.Length, id);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reorder nodes in playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to reorder nodes");
        }
    }

    /// <summary>
    /// Update node scopes (skills and knowledge).
    /// PUT /api/ai/playbooks/{id}/nodes/{nodeId}/scopes
    /// </summary>
    private static async Task<IResult> UpdateNodeScopes(
        Guid id,
        Guid nodeId,
        NodeScopesRequest request,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("NodeEndpoints");

        // Verify node exists and belongs to playbook
        var existingNode = await nodeService.GetNodeAsync(nodeId, cancellationToken);
        if (existingNode == null)
        {
            return Results.NotFound();
        }
        if (existingNode.PlaybookId != id)
        {
            logger.LogWarning("Node {NodeId} does not belong to playbook {PlaybookId}", nodeId, id);
            return Results.NotFound();
        }

        try
        {
            var node = await nodeService.UpdateNodeScopesAsync(nodeId, request, cancellationToken);
            logger.LogInformation("Updated scopes for node {NodeId}", nodeId);
            return Results.Ok(node);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update scopes for node {NodeId}", nodeId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to update node scopes");
        }
    }
}

/// <summary>
/// Request model for reordering nodes.
/// </summary>
public record ReorderNodesRequest
{
    /// <summary>
    /// Ordered array of node IDs representing the new execution order.
    /// </summary>
    public Guid[]? NodeIds { get; init; }
}
