using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

// PlaybookValidationResult is in Models.Ai namespace

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for PlaybookOrchestrationService.
/// Tests mode detection, legacy delegation, and node-based execution.
/// </summary>
public class PlaybookOrchestrationServiceTests
{
    private readonly Mock<INodeService> _nodeServiceMock;
    private readonly Mock<INodeExecutorRegistry> _executorRegistryMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IAnalysisOrchestrationService> _legacyOrchestratorMock;
    private readonly Mock<IInsightsActionRouter> _insightsRouterMock;
    private readonly Mock<ILogger<PlaybookOrchestrationService>> _loggerMock;
    private readonly PlaybookOrchestrationService _service;
    private readonly HttpContext _mockHttpContext;

    public PlaybookOrchestrationServiceTests()
    {
        _nodeServiceMock = new Mock<INodeService>();
        _executorRegistryMock = new Mock<INodeExecutorRegistry>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _legacyOrchestratorMock = new Mock<IAnalysisOrchestrationService>();
        _insightsRouterMock = new Mock<IInsightsActionRouter>();
        // Pass-through routing: return the default action / PassThrough decision so
        // existing tests behave identically to pre-Wave-D4 behavior.
        _insightsRouterMock
            .Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string? _, AnalysisAction action, System.Threading.CancellationToken _) => action);
        _insightsRouterMock
            .Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string? _, string? __, AnalysisAction action, System.Threading.CancellationToken _) => InsightsLayer2RoutingResult.PassThrough(action));
        _loggerMock = new Mock<ILogger<PlaybookOrchestrationService>>();
        _mockHttpContext = new DefaultHttpContext();

        _service = new PlaybookOrchestrationService(
            _nodeServiceMock.Object,
            _executorRegistryMock.Object,
            _scopeResolverMock.Object,
            _legacyOrchestratorMock.Object,
            _insightsRouterMock.Object,
            _loggerMock.Object);
    }

    private static PlaybookRunRequest CreateRequest(Guid? playbookId = null, params Guid[] documentIds) => new()
    {
        PlaybookId = playbookId ?? Guid.NewGuid(),
        DocumentIds = documentIds.Length > 0 ? documentIds : [Guid.NewGuid()]
    };

    private static PlaybookNodeDto CreateNode(
        string name,
        Guid? actionId = null,
        string? outputVariable = null,
        int order = 1,
        params Guid[] dependsOn) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            ActionId = actionId ?? Guid.NewGuid(),
            OutputVariable = outputVariable ?? name.ToLowerInvariant().Replace(" ", "_"),
            ExecutionOrder = order,
            DependsOn = dependsOn,
            IsActive = true,
            NodeType = NodeType.AIAnalysis,
            // R7 task 024 (FR-07) — single-hop dispatch reads SprkExecutortype directly.
            // Existing AI-analysis tests dispatch via AiAnalysis executor.
            SprkExecutortype = ExecutorType.AiAnalysis
        };

    private static AnalysisAction CreateAction(Guid? id = null, string? name = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name ?? "Test Action",
        SystemPrompt = "You are a helpful assistant."
    };

    private static ResolvedScopes CreateEmptyScopes() => new([], [], []);

    #region Mode Detection Tests

    [Fact]
    public async Task ExecuteAsync_NoNodes_UsesLegacyMode()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        // Return empty nodes array (Legacy mode)
        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        // Setup legacy orchestrator
        SetupLegacyOrchestrator(["Test content"]);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCountGreaterOrEqualTo(2); // RunStarted + RunCompleted
        events.First().Type.Should().Be(PlaybookEventType.RunStarted);
        events.Last().Type.Should().Be(PlaybookEventType.RunCompleted);
        events.Last().Done.Should().BeTrue();

        // Verify legacy orchestrator was called
        _legacyOrchestratorMock.Verify(
            x => x.ExecutePlaybookAsync(
                It.Is<PlaybookExecuteRequest>(r => r.PlaybookId == playbookId),
                _mockHttpContext,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNodes_UsesNodeBasedMode()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Extract Entities", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Setup executor
        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NodeOutput.Ok(node.Id, node.OutputVariable, new { result = "test" }));

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCountGreaterOrEqualTo(4); // RunStarted, NodeStarted, NodeCompleted, RunCompleted

        var eventTypes = events.Select(e => e.Type).ToList();
        eventTypes.Should().Contain(PlaybookEventType.RunStarted);
        eventTypes.Should().Contain(PlaybookEventType.NodeStarted);
        eventTypes.Should().Contain(PlaybookEventType.NodeCompleted);
        eventTypes.Should().Contain(PlaybookEventType.RunCompleted);

        // Verify legacy orchestrator was NOT called
        _legacyOrchestratorMock.Verify(
            x => x.ExecutePlaybookAsync(
                It.IsAny<PlaybookExecuteRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Legacy Mode Tests

    [Fact]
    public async Task ExecuteAsync_LegacyMode_StreamsContentAsNodeProgress()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        SetupLegacyOrchestrator(["Chunk 1", "Chunk 2", "Chunk 3"]);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var progressEvents = events.Where(e => e.Type == PlaybookEventType.NodeProgress).ToList();
        progressEvents.Should().HaveCount(3);
        progressEvents[0].Content.Should().Be("Chunk 1");
        progressEvents[1].Content.Should().Be("Chunk 2");
        progressEvents[2].Content.Should().Be("Chunk 3");
    }

    [Fact]
    public async Task ExecuteAsync_LegacyMode_ErrorInLegacy_YieldsRunFailed()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        // Setup legacy orchestrator to return error
        SetupLegacyOrchestratorWithError("Document not found");

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var failedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.RunFailed);
        failedEvent.Should().NotBeNull();
        failedEvent!.Error.Should().Be("Document not found");
        failedEvent.Done.Should().BeTrue();
    }

    #endregion

    #region NodeBased Mode Tests

    [Fact]
    public async Task ExecuteAsync_NodeBased_ExecutesNodesInOrder()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        var node1 = CreateNode("Step 1", actionId, "step1_output", order: 1);
        var node2 = CreateNode("Step 2", actionId, "step2_output", order: 2, node1.Id);
        var node3 = CreateNode("Step 3", actionId, "step3_output", order: 3, node2.Id);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node1, node2, node3]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        var executedNodeIds = new List<Guid>();
        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns((NodeExecutionContext ctx, CancellationToken _) =>
            {
                executedNodeIds.Add(ctx.Node.Id);
                return Task.FromResult(NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { }));
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var nodeCompletedEvents = events.Where(e => e.Type == PlaybookEventType.NodeCompleted).ToList();
        nodeCompletedEvents.Should().HaveCount(3);
        nodeCompletedEvents[0].NodeName.Should().Be("Step 1");
        nodeCompletedEvents[1].NodeName.Should().Be("Step 2");
        nodeCompletedEvents[2].NodeName.Should().Be("Step 3");

        // Verify execution order (topological sort ensures node1 → node2 → node3)
        executedNodeIds.Should().ContainInOrder(node1.Id, node2.Id, node3.Id);
    }

    [Fact]
    public async Task ExecuteAsync_NodeBased_NodeFailure_StopsExecution()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        var node1 = CreateNode("Step 1", actionId, "step1_output", order: 1);
        var node2 = CreateNode("Step 2", actionId, "step2_output", order: 2, node1.Id);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node1, node2]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) =>
                NodeOutput.Error(ctx.Node.Id, ctx.Node.OutputVariable, "Execution failed"));

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var eventTypes = events.Select(e => e.Type).ToList();
        eventTypes.Should().Contain(PlaybookEventType.NodeFailed);
        eventTypes.Should().Contain(PlaybookEventType.RunFailed);

        // Node 2 should never have started
        var nodeStartedEvents = events.Where(e => e.Type == PlaybookEventType.NodeStarted).ToList();
        nodeStartedEvents.Should().HaveCount(1);
        nodeStartedEvents[0].NodeName.Should().Be("Step 1");
    }

    [Fact]
    public async Task ExecuteAsync_NodeBased_ActionNotFound_FailsNode()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Test Node", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        // Action not found
        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisAction?)null);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var failedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.NodeFailed);
        failedEvent.Should().NotBeNull();
        failedEvent!.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_NodeBased_ValidationFailure_FailsNode()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Test Node", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Executor returns validation failure
        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Failure("Missing required field"));

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var failedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.NodeFailed);
        failedEvent.Should().NotBeNull();
        failedEvent!.Error.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task ExecuteAsync_NodeBased_NoExecutor_FailsNode()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Test Node", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // No executor registered
        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns((INodeExecutor?)null);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var failedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.NodeFailed);
        failedEvent.Should().NotBeNull();
        failedEvent!.Error.Should().Contain("No executor registered");
    }

    #endregion

    #region Parallel Execution Tests (Phase 3)

    [Fact]
    public async Task ExecuteAsync_IndependentNodes_ExecuteInParallel()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        // Create 3 independent nodes (no dependencies - they can run in parallel)
        var node1 = CreateNode("Node A", actionId, "node_a_output", order: 1);
        var node2 = CreateNode("Node B", actionId, "node_b_output", order: 2);
        var node3 = CreateNode("Node C", actionId, "node_c_output", order: 3);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node1, node2, node3]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Track concurrent execution count
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    maxConcurrent = Math.Max(maxConcurrent, concurrentCount);
                }

                // Simulate some work
                await Task.Delay(50);

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var nodeCompletedEvents = events.Where(e => e.Type == PlaybookEventType.NodeCompleted).ToList();
        nodeCompletedEvents.Should().HaveCount(3);

        // Verify nodes ran in parallel (max concurrent > 1)
        maxConcurrent.Should().BeGreaterThan(1, "Independent nodes should execute in parallel");
    }

    [Fact]
    public async Task ExecuteAsync_DependentNodes_ExecuteInBatches()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        // Create nodes: A, B (depends on A), C (depends on A)
        // A should run first, then B and C can run in parallel
        var nodeA = CreateNode("Node A", actionId, "node_a_output", order: 1);
        var nodeB = CreateNode("Node B", actionId, "node_b_output", order: 2, nodeA.Id);
        var nodeC = CreateNode("Node C", actionId, "node_c_output", order: 3, nodeA.Id);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([nodeA, nodeB, nodeC]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        var executionOrder = new List<string>();
        var lockObj = new object();

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                lock (lockObj)
                {
                    executionOrder.Add($"START:{ctx.Node.Name}");
                }

                await Task.Delay(30); // Simulate work

                lock (lockObj)
                {
                    executionOrder.Add($"END:{ctx.Node.Name}");
                }

                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        // Node A must complete before B and C start
        var nodeAEndIndex = executionOrder.IndexOf("END:Node A");
        var nodeBStartIndex = executionOrder.IndexOf("START:Node B");
        var nodeCStartIndex = executionOrder.IndexOf("START:Node C");

        nodeAEndIndex.Should().BeLessThan(nodeBStartIndex, "Node A must complete before Node B starts");
        nodeAEndIndex.Should().BeLessThan(nodeCStartIndex, "Node A must complete before Node C starts");
    }

    [Fact]
    public async Task ExecuteAsync_ThrottlesParallelExecution()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        // Create 5 independent nodes (more than default throttle of 3)
        var nodes = Enumerable.Range(1, 5)
            .Select(i => CreateNode($"Node {i}", actionId, $"node_{i}_output", order: i))
            .ToArray();

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    maxConcurrent = Math.Max(maxConcurrent, concurrentCount);
                }

                await Task.Delay(100); // Hold the slot for a bit

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var nodeCompletedEvents = events.Where(e => e.Type == PlaybookEventType.NodeCompleted).ToList();
        nodeCompletedEvents.Should().HaveCount(5);

        // Max concurrent should not exceed DefaultMaxParallelNodes (3)
        maxConcurrent.Should().BeLessOrEqualTo(3, "Throttling should limit concurrent execution to 3");
    }

    [Fact]
    public async Task ExecuteAsync_ParallelBatch_OneNodeFails_StopsExecution()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        // Create 3 independent nodes
        var node1 = CreateNode("Node A", actionId, "node_a_output", order: 1);
        var node2 = CreateNode("Node B", actionId, "node_b_output", order: 2);
        var node3 = CreateNode("Node C", actionId, "node_c_output", order: 3);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node1, node2, node3]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) =>
            {
                // Node B fails
                if (ctx.Node.Name == "Node B")
                {
                    return NodeOutput.Error(ctx.Node.Id, ctx.Node.OutputVariable, "Simulated failure");
                }
                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var runFailedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.RunFailed);
        runFailedEvent.Should().NotBeNull();
        runFailedEvent!.Error.Should().Contain("Node B");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ValidateAsync_NoNodes_ReturnsSuccessWithLegacyWarning()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        // Act
        var result = await _service.ValidateAsync(playbookId, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Contains("Legacy mode"));
    }

    [Fact]
    public async Task ValidateAsync_ValidGraph_ReturnsSuccess()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var node1 = CreateNode("Step 1", actionId, "step1", order: 1);
        var node2 = CreateNode("Step 2", actionId, "step2", order: 2, node1.Id);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node1, node2]);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Act
        var result = await _service.ValidateAsync(playbookId, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_MissingAction_ReturnsFailure()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var node = CreateNode("Test Node");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisAction?)null);

        // Act
        var result = await _service.ValidateAsync(playbookId, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("non-existent action"));
    }

    [Fact]
    public async Task ValidateAsync_DuplicateOutputVariable_ReturnsFailure()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var node1 = CreateNode("Step 1", actionId, "same_output", order: 1);
        var node2 = CreateNode("Step 2", actionId, "same_output", order: 2);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node1, node2]);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Act
        var result = await _service.ValidateAsync(playbookId, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("same_output") && e.Contains("multiple nodes"));
    }

    [Fact]
    public async Task ValidateAsync_InvalidDependency_ReturnsFailure()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var nonExistentNodeId = Guid.NewGuid();
        var node = CreateNode("Step 1", actionId, "output", order: 1, nonExistentNodeId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Act
        var result = await _service.ValidateAsync(playbookId, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("non-existent node"));
    }

    [Fact]
    public async Task ValidateAsync_DisabledNodes_ReturnsWarning()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var activeNode = CreateNode("Active Node", actionId, "active_output", order: 1);
        var disabledNode = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            Name = "Disabled Node",
            ActionId = actionId,
            OutputVariable = "disabled_output",
            ExecutionOrder = 2,
            DependsOn = [],
            IsActive = false
        };

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([activeNode, disabledNode]);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Act
        var result = await _service.ValidateAsync(playbookId, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Contains("disabled"));
    }

    #endregion

    #region Run Status Tests

    [Fact]
    public async Task GetRunStatusAsync_RunNotFound_ReturnsNull()
    {
        // Arrange
        var runId = Guid.NewGuid();

        // Act
        var status = await _service.GetRunStatusAsync(runId, CancellationToken.None);

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetRunStatusAsync_ActiveRun_ReturnsStatus()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        SetupLegacyOrchestrator(["content"]);

        // Execute to create an active run
        Guid capturedRunId = Guid.Empty;
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            if (evt.Type == PlaybookEventType.RunStarted)
            {
                capturedRunId = evt.RunId;
            }
        }

        // Act
        var status = await _service.GetRunStatusAsync(capturedRunId, CancellationToken.None);

        // Assert
        status.Should().NotBeNull();
        status!.RunId.Should().Be(capturedRunId);
        status.PlaybookId.Should().Be(playbookId);
        status.State.Should().Be(PlaybookRunState.Completed);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public async Task CancelAsync_RunNotFound_ReturnsFalse()
    {
        // Arrange
        var runId = Guid.NewGuid();

        // Act
        var result = await _service.CancelAsync(runId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Run History Tests

    [Fact]
    public async Task GetRunHistoryAsync_NoRuns_ReturnsEmptyList()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var response = await _service.GetRunHistoryAsync(playbookId, 1, 20, null, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetRunHistoryAsync_WithRuns_ReturnsMatchingRuns()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        SetupLegacyOrchestrator(["content"]);

        // Execute to create a run
        await foreach (var _ in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            // Consume all events
        }

        // Act
        var response = await _service.GetRunHistoryAsync(playbookId, 1, 20, null, CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(1);
        response.TotalCount.Should().Be(1);
        response.Items[0].PlaybookId.Should().Be(playbookId);
        response.Items[0].State.Should().Be("Completed");
    }

    [Fact]
    public async Task GetRunHistoryAsync_StateFilter_FiltersRuns()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        SetupLegacyOrchestrator(["content"]);

        // Execute to create a completed run
        await foreach (var _ in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            // Consume all events
        }

        // Act - filter for "Failed" state (should find nothing)
        var response = await _service.GetRunHistoryAsync(playbookId, 1, 20, "Failed", CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRunHistoryAsync_Pagination_RespectsPageSize()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        SetupLegacyOrchestrator(["content"]);

        // Execute multiple runs
        for (int i = 0; i < 5; i++)
        {
            var request = CreateRequest(playbookId);
            await foreach (var _ in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
            {
                // Consume all events
            }
        }

        // Act - request page 1 with size 2
        var response = await _service.GetRunHistoryAsync(playbookId, 1, 2, null, CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.TotalCount.Should().Be(5);
        response.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task GetRunHistoryAsync_NormalizesPageSize()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act - request with pageSize > 100
        var response = await _service.GetRunHistoryAsync(playbookId, 1, 500, null, CancellationToken.None);

        // Assert - should clamp to 100
        response.PageSize.Should().Be(100);
    }

    #endregion

    #region Run Detail Tests

    [Fact]
    public async Task GetRunDetailAsync_RunNotFound_ReturnsNull()
    {
        // Arrange
        var runId = Guid.NewGuid();

        // Act
        var detail = await _service.GetRunDetailAsync(runId, CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task GetRunDetailAsync_ExistingRun_ReturnsDetail()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Test Node", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NodeOutput.Ok(node.Id, node.OutputVariable, new { result = "test" }, "Test output", 0.95));

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Execute to create a run
        Guid capturedRunId = Guid.Empty;
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            if (evt.Type == PlaybookEventType.RunStarted)
            {
                capturedRunId = evt.RunId;
            }
        }

        // Act
        var detail = await _service.GetRunDetailAsync(capturedRunId, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.RunId.Should().Be(capturedRunId);
        detail.PlaybookId.Should().Be(playbookId);
        detail.State.Should().Be("Completed");
        detail.DocumentIds.Should().NotBeEmpty();
        detail.NodeDetails.Should().HaveCount(1);
        detail.NodeDetails[0].Success.Should().BeTrue();
        detail.NodeDetails[0].Confidence.Should().Be(0.95);
        detail.NodeDetails[0].OutputPreview.Should().Be("Test output");
    }

    [Fact]
    public async Task GetRunDetailAsync_IncludesTokenMetrics()
    {
        // Arrange - Use node-based execution to test token tracking
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Test Node", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);

        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Create executor that returns output with token metrics
        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeOutput
            {
                NodeId = node.Id,
                OutputVariable = node.OutputVariable,
                Success = true,
                TextContent = "Test result",
                Metrics = new NodeExecutionMetrics
                {
                    StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAt = DateTimeOffset.UtcNow,
                    TokensIn = 100,
                    TokensOut = 50
                }
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Execute to create a run
        Guid capturedRunId = Guid.Empty;
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            if (evt.Type == PlaybookEventType.RunStarted)
            {
                capturedRunId = evt.RunId;
            }
        }

        // Act
        var detail = await _service.GetRunDetailAsync(capturedRunId, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.TotalTokensIn.Should().Be(100);
        detail.TotalTokensOut.Should().Be(50);
        detail.NodeDetails.Should().HaveCount(1);
        detail.NodeDetails[0].TokensIn.Should().Be(100);
        detail.NodeDetails[0].TokensOut.Should().Be(50);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_ExceptionDuringExecution_YieldsRunFailed()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var failedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.RunFailed);
        failedEvent.Should().NotBeNull();
        failedEvent!.Error.Should().Contain("Test exception");
        failedEvent.Done.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var cts = new CancellationTokenSource();

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, CancellationToken ct) =>
            {
                // Cancel during node loading
                await Task.Yield();
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Array.Empty<PlaybookNodeDto>();
            });

        // Act & Assert
        // When the caller cancels, the enumeration throws OperationCanceledException
        // which is the expected behavior for cancelled async enumerations
        var act = async () =>
        {
            await foreach (var _ in _service.ExecuteAsync(request, _mockHttpContext, cts.Token))
            {
                // Just iterate
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Unrendered Template Detection (R3 FR-3H1.4 / AC-H1.2)

    /// <summary>
    /// R3 FR-3H1.4 / AC-H1.2 — when a node's NodeOutput.TextContent contains literal
    /// "{{" (an unrendered Handlebars template), the orchestrator MUST emit a
    /// PlaybookStreamEvent of type UnrenderedTemplateDetected AND log a structured
    /// warning. Output still flows downstream (non-fatal degradation).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NodeOutputContainsUnrenderedTemplate_EmitsWarningStreamEvent()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Notify", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());
        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Output deliberately includes literal "{{x.output}}" (template that was
        // never substituted) — simulates the FR-3H1.4 leak scenario.
        var leakedOutput = NodeOutput.Ok(
            node.Id,
            node.OutputVariable,
            data: null,
            textContent: "Hello, your task '{{x.output}}' is overdue.");

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leakedOutput);

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — stream event
        var unrenderedEvents = events.Where(e => e.Type == PlaybookEventType.UnrenderedTemplateDetected).ToList();
        unrenderedEvents.Should().HaveCount(1, "exactly one warning event per node with leaked templates");
        unrenderedEvents[0].NodeId.Should().Be(node.Id);
        unrenderedEvents[0].NodeName.Should().Be("Notify");
        unrenderedEvents[0].PlaybookId.Should().Be(playbookId);
        unrenderedEvents[0].Content.Should().Contain("{{x.output}}", "sample text MUST carry the leaked template token");
        unrenderedEvents[0].RunId.Should().NotBe(Guid.Empty, "RunId doubles as correlationId per NFR-08");

        // Assert — output STILL flows downstream (non-fatal)
        events.Should().Contain(e => e.Type == PlaybookEventType.NodeCompleted);
        events.Should().Contain(e => e.Type == PlaybookEventType.RunCompleted);
        events.Last().Done.Should().BeTrue();

        // Assert — structured warning logged with NodeId + RunId + sample
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) =>
                    o.ToString()!.Contains("Unrendered template detected") &&
                    o.ToString()!.Contains("Notify")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// R3 FR-3H1.4 / AC-H1.2 — clean NodeOutput (no literal "{{") must NOT trigger
    /// a false-positive UnrenderedTemplateDetected event or warning log.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NodeOutputClean_NoUnrenderedTemplateEventEmitted()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("Notify", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());
        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Realistic substituted output — single curly braces (JSON), no doubles.
        var cleanOutput = NodeOutput.Ok(
            node.Id,
            node.OutputVariable,
            data: new { ok = true, id = 42 },
            textContent: "Hello, your task 'Review Contract' is overdue.");

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cleanOutput);

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — NO false-positive warning event
        events.Should().NotContain(e => e.Type == PlaybookEventType.UnrenderedTemplateDetected,
            "clean output MUST NOT trigger a false-positive warning");

        // And NO warning log carrying the FR-3H1.4 phrase
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Unrendered template detected")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    /// <summary>
    /// R3 FR-3H1.4 / AC-H1.2 — when multiple string fields of NodeOutput contain
    /// "{{" (TextContent + Warnings + StructuredData), the orchestrator emits a
    /// SINGLE warning event per node (one warning per node — design decision noted
    /// in task notes), with the warning log listing all offending field names.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NodeOutputMultipleFieldsUnrendered_EmitsSingleWarningWithAllFields()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateNode("MultiLeak", actionId);

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        _scopeResolverMock
            .Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());
        _scopeResolverMock
            .Setup(x => x.GetActionAsync(actionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAction(actionId));

        // Leak in TextContent + Warnings + StructuredData simultaneously.
        var leakedOutput = NodeOutput.Ok(
            node.Id,
            node.OutputVariable,
            data: new { recipient = "{{user.email}}" },  // StructuredData leak
            textContent: "Hello {{user.name}}",            // TextContent leak
            warnings: new[] { "Could not resolve {{x.fallback}}" }); // Warnings leak

        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leakedOutput);

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.AiAnalysis))
            .Returns(mockExecutor.Object);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — exactly ONE warning event despite 3 distinct leaks
        var unrenderedEvents = events.Where(e => e.Type == PlaybookEventType.UnrenderedTemplateDetected).ToList();
        unrenderedEvents.Should().HaveCount(1, "design decision: single event per node, not per leaked field");
        unrenderedEvents[0].NodeId.Should().Be(node.Id);
        unrenderedEvents[0].NodeName.Should().Be("MultiLeak");

        // Assert — warning log lists ALL offending field names so operators can locate every leak
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) =>
                    o.ToString()!.Contains("TextContent") &&
                    o.ToString()!.Contains("Warnings[0]") &&
                    o.ToString()!.Contains("StructuredData")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private void SetupLegacyOrchestrator(IEnumerable<string> chunks)
    {
        _legacyOrchestratorMock
            .Setup(x => x.ExecutePlaybookAsync(
                It.IsAny<PlaybookExecuteRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateLegacyStream(chunks));
    }

    private void SetupLegacyOrchestratorWithError(string error)
    {
        _legacyOrchestratorMock
            .Setup(x => x.ExecutePlaybookAsync(
                It.IsAny<PlaybookExecuteRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateLegacyErrorStream(error));
    }

    private static async IAsyncEnumerable<AnalysisStreamChunk> CreateLegacyStream(IEnumerable<string> chunks)
    {
        yield return AnalysisStreamChunk.Metadata(Guid.NewGuid(), "test.pdf");

        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return AnalysisStreamChunk.TextChunk(chunk);
        }

        yield return AnalysisStreamChunk.Completed(Guid.NewGuid(), new TokenUsage(100, 50));
    }

    private static async IAsyncEnumerable<AnalysisStreamChunk> CreateLegacyErrorStream(string error)
    {
        yield return AnalysisStreamChunk.Metadata(Guid.NewGuid(), "test.pdf");
        await Task.Yield();
        yield return AnalysisStreamChunk.FromError(error);
    }

    #endregion
}

/// <summary>
/// Tests for PlaybookStreamEvent factory methods.
/// </summary>
public class PlaybookStreamEventTests
{
    [Fact]
    public void RunStarted_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.RunStarted(runId, playbookId, 5);

        // Assert
        evt.Type.Should().Be(PlaybookEventType.RunStarted);
        evt.RunId.Should().Be(runId);
        evt.PlaybookId.Should().Be(playbookId);
        evt.Metrics.Should().NotBeNull();
        evt.Metrics!.TotalNodes.Should().Be(5);
        evt.Done.Should().BeFalse();
    }

    [Fact]
    public void NodeStarted_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.NodeStarted(runId, playbookId, nodeId, "Test Node");

        // Assert
        evt.Type.Should().Be(PlaybookEventType.NodeStarted);
        evt.RunId.Should().Be(runId);
        evt.PlaybookId.Should().Be(playbookId);
        evt.NodeId.Should().Be(nodeId);
        evt.NodeName.Should().Be("Test Node");
    }

    [Fact]
    public void NodeProgress_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.NodeProgress(runId, playbookId, nodeId, "streaming content");

        // Assert
        evt.Type.Should().Be(PlaybookEventType.NodeProgress);
        evt.Content.Should().Be("streaming content");
    }

    [Fact]
    public void NodeCompleted_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var output = NodeOutput.Ok(nodeId, "test_output", new { result = "done" });

        // Act
        var evt = PlaybookStreamEvent.NodeCompleted(runId, playbookId, nodeId, "Test Node", output);

        // Assert
        evt.Type.Should().Be(PlaybookEventType.NodeCompleted);
        evt.NodeOutput.Should().Be(output);
    }

    [Fact]
    public void NodeSkipped_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.NodeSkipped(runId, playbookId, nodeId, "Test Node", "Condition not met");

        // Assert
        evt.Type.Should().Be(PlaybookEventType.NodeSkipped);
        evt.Content.Should().Be("Condition not met");
    }

    [Fact]
    public void NodeFailed_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.NodeFailed(runId, playbookId, nodeId, "Test Node", "Execution error");

        // Assert
        evt.Type.Should().Be(PlaybookEventType.NodeFailed);
        evt.Error.Should().Be("Execution error");
    }

    [Fact]
    public void RunCompleted_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var metrics = new PlaybookRunMetrics
        {
            TotalNodes = 5,
            CompletedNodes = 5,
            Duration = TimeSpan.FromSeconds(30)
        };

        // Act
        var evt = PlaybookStreamEvent.RunCompleted(runId, playbookId, metrics);

        // Assert
        evt.Type.Should().Be(PlaybookEventType.RunCompleted);
        evt.Done.Should().BeTrue();
        evt.Metrics.Should().Be(metrics);
    }

    [Fact]
    public void RunFailed_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.RunFailed(runId, playbookId, "Fatal error");

        // Assert
        evt.Type.Should().Be(PlaybookEventType.RunFailed);
        evt.Done.Should().BeTrue();
        evt.Error.Should().Be("Fatal error");
    }

    [Fact]
    public void RunCancelled_CreatesCorrectEvent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();

        // Act
        var evt = PlaybookStreamEvent.RunCancelled(runId, playbookId);

        // Assert
        evt.Type.Should().Be(PlaybookEventType.RunCancelled);
        evt.Done.Should().BeTrue();
    }
}

/// <summary>
/// Tests for PlaybookValidationResult factory methods.
/// </summary>
public class PlaybookValidationResultTests
{
    [Fact]
    public void Success_CreatesValidResult()
    {
        // Act
        var result = PlaybookValidationResult.Success();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Success_WithWarnings_CreatesValidResultWithWarnings()
    {
        // Act
        var result = PlaybookValidationResult.Success(["Warning 1", "Warning 2"]);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public void Failure_CreatesInvalidResult()
    {
        // Act
        var result = PlaybookValidationResult.Failure("Error 1", "Error 2");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain("Error 1");
        result.Errors.Should().Contain("Error 2");
    }
}

/// <summary>
/// Tests for PlaybookRunMetrics and PlaybookRunStatus models.
/// </summary>
public class PlaybookRunModelsTests
{
    [Fact]
    public void PlaybookRunMetrics_CanBeCreated()
    {
        // Arrange & Act
        var metrics = new PlaybookRunMetrics
        {
            TotalNodes = 10,
            CompletedNodes = 7,
            FailedNodes = 1,
            SkippedNodes = 2,
            TotalTokensIn = 5000,
            TotalTokensOut = 2500,
            Duration = TimeSpan.FromMinutes(2)
        };

        // Assert
        metrics.TotalNodes.Should().Be(10);
        metrics.CompletedNodes.Should().Be(7);
        metrics.FailedNodes.Should().Be(1);
        metrics.SkippedNodes.Should().Be(2);
        metrics.TotalTokensIn.Should().Be(5000);
        metrics.TotalTokensOut.Should().Be(2500);
        metrics.Duration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void PlaybookRunStatus_CanBeCreated()
    {
        // Arrange & Act
        var status = new PlaybookRunStatus
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            State = PlaybookRunState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CurrentNodeId = Guid.NewGuid()
        };

        // Assert
        status.RunId.Should().NotBeEmpty();
        status.PlaybookId.Should().NotBeEmpty();
        status.State.Should().Be(PlaybookRunState.Running);
        status.CurrentNodeId.Should().NotBeNull();
        status.CompletedAt.Should().BeNull();
        status.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void PlaybookRunState_HasExpectedValues()
    {
        // Assert
        ((int)PlaybookRunState.Pending).Should().Be(0);
        ((int)PlaybookRunState.Running).Should().Be(1);
        ((int)PlaybookRunState.Completed).Should().Be(2);
        ((int)PlaybookRunState.Failed).Should().Be(3);
        ((int)PlaybookRunState.Cancelled).Should().Be(4);
    }
}
