using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration;

/// <summary>
/// Integration tests for Phase 3 playbook execution features.
/// Tests parallel execution, throttling, and all delivery executors.
/// </summary>
/// <remarks>
/// Task 028: Phase 3 Integration Tests
///
/// Tests verify:
/// - Parallel execution batching works correctly
/// - Throttling limits concurrent node execution
/// - All delivery executors are properly registered
/// - Full playbook execution end-to-end flow
/// </remarks>
public class PlaybookExecutionTests
{
    #region Node Executor Registry Tests

    [Fact]
    public void NodeExecutorRegistry_RegistersAllDeliveryExecutors()
    {
        // Arrange
        var mockTemplateEngine = new Mock<ITemplateEngine>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockToolHandlerRegistry = new Mock<IToolHandlerRegistry>();
        var mockGraphClientFactory = new Mock<IGraphClientFactory>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();

        var executors = new List<INodeExecutor>
        {
            new AiAnalysisNodeExecutor(
                mockToolHandlerRegistry.Object,
                Mock.Of<ILogger<AiAnalysisNodeExecutor>>()),
            new CreateTaskNodeExecutor(
                mockTemplateEngine.Object,
                mockHttpClientFactory.Object,
                Mock.Of<ILogger<CreateTaskNodeExecutor>>()),
            new SendEmailNodeExecutor(
                mockTemplateEngine.Object,
                mockGraphClientFactory.Object,
                mockHttpContextAccessor.Object,
                Mock.Of<ILogger<SendEmailNodeExecutor>>()),
            new UpdateRecordNodeExecutor(
                mockTemplateEngine.Object,
                mockHttpClientFactory.Object,
                Mock.Of<ILogger<UpdateRecordNodeExecutor>>()),
            new DeliverOutputNodeExecutor(
                mockTemplateEngine.Object,
                Mock.Of<ILogger<DeliverOutputNodeExecutor>>())
        };

        var registry = new NodeExecutorRegistry(executors, Mock.Of<ILogger<NodeExecutorRegistry>>());

        // Act & Assert
        registry.HasExecutor(ActionType.AiAnalysis).Should().BeTrue("AiAnalysis executor should be registered");
        registry.HasExecutor(ActionType.CreateTask).Should().BeTrue("CreateTask executor should be registered");
        registry.HasExecutor(ActionType.SendEmail).Should().BeTrue("SendEmail executor should be registered");
        registry.HasExecutor(ActionType.UpdateRecord).Should().BeTrue("UpdateRecord executor should be registered");
        registry.HasExecutor(ActionType.DeliverOutput).Should().BeTrue("DeliverOutput executor should be registered");

        registry.ExecutorCount.Should().BeGreaterOrEqualTo(5, "All Phase 3 executors should be registered");
    }

    [Fact]
    public void NodeExecutorRegistry_ReturnsCorrectExecutorByType()
    {
        // Arrange
        var mockTemplateEngine = new Mock<ITemplateEngine>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();

        var createTaskExecutor = new CreateTaskNodeExecutor(
            mockTemplateEngine.Object,
            mockHttpClientFactory.Object,
            Mock.Of<ILogger<CreateTaskNodeExecutor>>());

        var executors = new List<INodeExecutor> { createTaskExecutor };
        var registry = new NodeExecutorRegistry(executors, Mock.Of<ILogger<NodeExecutorRegistry>>());

        // Act
        var retrieved = registry.GetExecutor(ActionType.CreateTask);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().BeSameAs(createTaskExecutor);
    }

    [Fact]
    public void NodeExecutorRegistry_GetSupportedActionTypes_ReturnsAllTypes()
    {
        // Arrange
        var mockTemplateEngine = new Mock<ITemplateEngine>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockToolHandlerRegistry = new Mock<IToolHandlerRegistry>();
        var mockGraphClientFactory = new Mock<IGraphClientFactory>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();

        var executors = new List<INodeExecutor>
        {
            new AiAnalysisNodeExecutor(
                mockToolHandlerRegistry.Object,
                Mock.Of<ILogger<AiAnalysisNodeExecutor>>()),
            new CreateTaskNodeExecutor(
                mockTemplateEngine.Object,
                mockHttpClientFactory.Object,
                Mock.Of<ILogger<CreateTaskNodeExecutor>>()),
            new SendEmailNodeExecutor(
                mockTemplateEngine.Object,
                mockGraphClientFactory.Object,
                mockHttpContextAccessor.Object,
                Mock.Of<ILogger<SendEmailNodeExecutor>>()),
            new UpdateRecordNodeExecutor(
                mockTemplateEngine.Object,
                mockHttpClientFactory.Object,
                Mock.Of<ILogger<UpdateRecordNodeExecutor>>()),
            new DeliverOutputNodeExecutor(
                mockTemplateEngine.Object,
                Mock.Of<ILogger<DeliverOutputNodeExecutor>>())
        };

        var registry = new NodeExecutorRegistry(executors, Mock.Of<ILogger<NodeExecutorRegistry>>());

        // Act
        var supportedTypes = registry.GetSupportedActionTypes();

        // Assert
        supportedTypes.Should().Contain(ActionType.AiAnalysis);
        supportedTypes.Should().Contain(ActionType.CreateTask);
        supportedTypes.Should().Contain(ActionType.SendEmail);
        supportedTypes.Should().Contain(ActionType.UpdateRecord);
        supportedTypes.Should().Contain(ActionType.DeliverOutput);
    }

    #endregion

    #region Parallel Execution Integration Tests

    [Fact]
    public async Task ParallelExecution_AllNodesComplete_ReturnsCorrectMetrics()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var node1 = CreateNode("Step 1", actionId, "step1_output", 1);
        var node2 = CreateNode("Step 2", actionId, "step2_output", 2, node1.Id);
        var node3 = CreateNode("Step 3", actionId, "step3_output", 3, node2.Id);

        var nodes = new[] { node1, node2, node3 };
        var (service, mocks) = CreateTestService();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        SetupSuccessfulExecution(mocks, actionId);

        var request = CreateRequest(playbookId);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in service.ExecuteAsync(request, new DefaultHttpContext(), CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var completedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.RunCompleted);
        completedEvent.Should().NotBeNull("Playbook should complete successfully");
        completedEvent!.Metrics.Should().NotBeNull();
        completedEvent.Metrics!.TotalNodes.Should().Be(3);
        completedEvent.Metrics.CompletedNodes.Should().Be(3);
        completedEvent.Metrics.FailedNodes.Should().Be(0);
    }

    [Fact]
    public async Task ParallelExecution_IndependentNodes_ExecuteConcurrently()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        // Three independent nodes (no dependencies)
        var node1 = CreateNode("Node A", actionId, "node_a", 1);
        var node2 = CreateNode("Node B", actionId, "node_b", 2);
        var node3 = CreateNode("Node C", actionId, "node_c", 3);

        var nodes = new[] { node1, node2, node3 };
        var (service, mocks) = CreateTestService();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        // Track concurrent execution
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        mocks.Executor
            .Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());

        mocks.Executor
            .Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    maxConcurrent = Math.Max(maxConcurrent, concurrentCount);
                }

                await Task.Delay(50); // Simulate work

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(mocks.Executor.Object);

        SetupScopeResolver(mocks, actionId);

        var request = CreateRequest(playbookId);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in service.ExecuteAsync(request, new DefaultHttpContext(), CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        maxConcurrent.Should().BeGreaterThan(1, "Independent nodes should execute in parallel");

        var nodeCompletedCount = events.Count(e => e.Type == PlaybookEventType.NodeCompleted);
        nodeCompletedCount.Should().Be(3, "All three nodes should complete");
    }

    [Fact]
    public async Task ParallelExecution_WithThrottling_LimitsConcurrency()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        // Create 5 independent nodes (more than default throttle of 3)
        var nodes = Enumerable.Range(1, 5)
            .Select(i => CreateNode($"Node {i}", actionId, $"node_{i}", i))
            .ToArray();

        var (service, mocks) = CreateTestService();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        mocks.Executor
            .Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());

        mocks.Executor
            .Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    maxConcurrent = Math.Max(maxConcurrent, concurrentCount);
                }

                await Task.Delay(100); // Hold for long enough to verify throttling

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(mocks.Executor.Object);

        SetupScopeResolver(mocks, actionId);

        var request = CreateRequest(playbookId);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in service.ExecuteAsync(request, new DefaultHttpContext(), CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        maxConcurrent.Should().BeLessOrEqualTo(3, "Throttling should limit to DefaultMaxParallelNodes (3)");

        var completedCount = events.Count(e => e.Type == PlaybookEventType.NodeCompleted);
        completedCount.Should().Be(5, "All 5 nodes should eventually complete");
    }

    [Fact]
    public async Task ParallelExecution_DependentNodes_RespectsBatching()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        // Create dependency chain: A -> B, C (B and C depend on A)
        var nodeA = CreateNode("Node A", actionId, "node_a", 1);
        var nodeB = CreateNode("Node B", actionId, "node_b", 2, nodeA.Id);
        var nodeC = CreateNode("Node C", actionId, "node_c", 3, nodeA.Id);

        var nodes = new[] { nodeA, nodeB, nodeC };
        var (service, mocks) = CreateTestService();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        var executionOrder = new List<string>();
        var lockObj = new object();

        mocks.Executor
            .Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());

        mocks.Executor
            .Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                lock (lockObj)
                {
                    executionOrder.Add($"START:{ctx.Node.Name}");
                }

                await Task.Delay(30);

                lock (lockObj)
                {
                    executionOrder.Add($"END:{ctx.Node.Name}");
                }

                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(mocks.Executor.Object);

        SetupScopeResolver(mocks, actionId);

        var request = CreateRequest(playbookId);

        // Act
        await foreach (var _ in service.ExecuteAsync(request, new DefaultHttpContext(), CancellationToken.None))
        {
            // Consume events
        }

        // Assert
        var nodeAEndIndex = executionOrder.IndexOf("END:Node A");
        var nodeBStartIndex = executionOrder.IndexOf("START:Node B");
        var nodeCStartIndex = executionOrder.IndexOf("START:Node C");

        nodeAEndIndex.Should().BeLessThan(nodeBStartIndex, "Node A must complete before Node B starts");
        nodeAEndIndex.Should().BeLessThan(nodeCStartIndex, "Node A must complete before Node C starts");
    }

    #endregion

    #region CreateTaskNodeExecutor Integration Tests

    [Fact]
    public async Task CreateTaskNodeExecutor_WithValidConfig_CreatesTask()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var loggerMock = new Mock<ILogger<CreateTaskNodeExecutor>>();

        templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);

        var executor = new CreateTaskNodeExecutor(
            templateEngineMock.Object,
            httpClientFactoryMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.CreateTask, @"{""subject"":""Review document"",""description"":""Please review""}");

        // Act
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue("Task creation should succeed with valid config");
        result.TextContent.Should().Contain("Task created");
    }

    [Fact]
    public void CreateTaskNodeExecutor_WithMissingSubject_FailsValidation()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var loggerMock = new Mock<ILogger<CreateTaskNodeExecutor>>();

        var executor = new CreateTaskNodeExecutor(
            templateEngineMock.Object,
            httpClientFactoryMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.CreateTask, @"{""description"":""No subject""}");

        // Act
        var validation = executor.Validate(context);

        // Assert
        validation.IsValid.Should().BeFalse("Validation should fail without subject");
        validation.Errors.Should().Contain(e => e.Contains("subject", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region SendEmailNodeExecutor Integration Tests

    [Fact]
    public void SendEmailNodeExecutor_WithValidConfig_PassesValidation()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var graphClientFactoryMock = new Mock<IGraphClientFactory>();
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var loggerMock = new Mock<ILogger<SendEmailNodeExecutor>>();

        var executor = new SendEmailNodeExecutor(
            templateEngineMock.Object,
            graphClientFactoryMock.Object,
            httpContextAccessorMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.SendEmail, @"{
            ""to"":[""user@example.com""],
            ""subject"":""Test Email"",
            ""body"":""Email body content""
        }");

        // Act
        var validation = executor.Validate(context);

        // Assert
        validation.IsValid.Should().BeTrue("Email config should be valid");
    }

    [Fact]
    public void SendEmailNodeExecutor_WithMissingRecipient_FailsValidation()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var graphClientFactoryMock = new Mock<IGraphClientFactory>();
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var loggerMock = new Mock<ILogger<SendEmailNodeExecutor>>();

        var executor = new SendEmailNodeExecutor(
            templateEngineMock.Object,
            graphClientFactoryMock.Object,
            httpContextAccessorMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.SendEmail, @"{""subject"":""No recipient"",""body"":""test""}");

        // Act
        var validation = executor.Validate(context);

        // Assert
        validation.IsValid.Should().BeFalse("Validation should fail without recipient");
    }

    #endregion

    #region UpdateRecordNodeExecutor Integration Tests

    [Fact]
    public void UpdateRecordNodeExecutor_WithValidConfig_PassesValidation()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var loggerMock = new Mock<ILogger<UpdateRecordNodeExecutor>>();

        var executor = new UpdateRecordNodeExecutor(
            templateEngineMock.Object,
            httpClientFactoryMock.Object,
            loggerMock.Object);

        var recordId = Guid.NewGuid();
        // Use camelCase (JSON-style) property names - should work with PropertyNameCaseInsensitive = true
        var configJson = $@"{{""entityLogicalName"":""sprk_document"",""recordId"":""{recordId}"",""fields"":{{""sprk_status"":""Completed""}}}}";
        var context = CreateNodeContext(ActionType.UpdateRecord, configJson);

        // Act
        var validation = executor.Validate(context);

        // Assert - validation should pass with complete config
        validation.IsValid.Should().BeTrue($"Validation should pass with valid config. Errors: {string.Join(", ", validation.Errors)}");
    }

    [Fact]
    public void UpdateRecordNodeExecutor_WithMissingEntityName_FailsValidation()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var loggerMock = new Mock<ILogger<UpdateRecordNodeExecutor>>();

        var executor = new UpdateRecordNodeExecutor(
            templateEngineMock.Object,
            httpClientFactoryMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.UpdateRecord, @"{""recordId"":""abc"",""fields"":{}}");

        // Act
        var validation = executor.Validate(context);

        // Assert
        validation.IsValid.Should().BeFalse("Validation should fail without entityLogicalName");
    }

    #endregion

    #region DeliverOutputNodeExecutor Integration Tests

    [Fact]
    public async Task DeliverOutputNodeExecutor_WithValidJsonConfig_DeliversOutput()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var loggerMock = new Mock<ILogger<DeliverOutputNodeExecutor>>();

        templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);

        var executor = new DeliverOutputNodeExecutor(
            templateEngineMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.DeliverOutput, @"{
            ""deliveryType"":""json""
        }");

        // Act
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue("JSON output delivery should succeed");
    }

    [Fact]
    public void DeliverOutputNodeExecutor_WithMissingDeliveryType_FailsValidation()
    {
        // Arrange
        var templateEngineMock = new Mock<ITemplateEngine>();
        var loggerMock = new Mock<ILogger<DeliverOutputNodeExecutor>>();

        var executor = new DeliverOutputNodeExecutor(
            templateEngineMock.Object,
            loggerMock.Object);

        var context = CreateNodeContext(ActionType.DeliverOutput, @"{""template"":""Some content""}");

        // Act
        var validation = executor.Validate(context);

        // Assert
        validation.IsValid.Should().BeFalse("Validation should fail without deliveryType");
    }

    #endregion

    #region Full End-to-End Playbook Execution Tests

    [Fact]
    public async Task EndToEnd_PlaybookWithMixedNodes_ExecutesCorrectly()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var analysisActionId = Guid.NewGuid();
        var deliveryActionId = Guid.NewGuid();

        // Create realistic playbook: Analysis -> Delivery
        var analysisNode = CreateNode("AI Analysis", analysisActionId, "analysis_result", 1);
        var deliveryNode = CreateNode("Deliver Output", deliveryActionId, "delivery_result", 2, analysisNode.Id);

        var nodes = new[] { analysisNode, deliveryNode };
        var (service, mocks) = CreateTestService();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        // Setup executors for both action types
        var analysisExecutor = new Mock<INodeExecutor>();
        analysisExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        analysisExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) =>
                NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { result = "AI analysis result" }));

        var deliveryExecutor = new Mock<INodeExecutor>();
        deliveryExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        deliveryExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) =>
                NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { delivered = true }));

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(analysisExecutor.Object);
        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.DeliverOutput))
            .Returns(deliveryExecutor.Object);

        // Setup scope resolver for both actions
        mocks.ScopeResolver
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes([], [], []));
        mocks.ScopeResolver
            .Setup(x => x.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = Guid.NewGuid(), Name = "Test Action" });

        var request = CreateRequest(playbookId);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in service.ExecuteAsync(request, new DefaultHttpContext(), CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var eventTypes = events.Select(e => e.Type).ToList();

        eventTypes.Should().Contain(PlaybookEventType.RunStarted);
        eventTypes.Should().Contain(PlaybookEventType.NodeStarted);
        eventTypes.Should().Contain(PlaybookEventType.NodeCompleted);
        eventTypes.Should().Contain(PlaybookEventType.RunCompleted);

        var completedNodes = events.Where(e => e.Type == PlaybookEventType.NodeCompleted).ToList();
        completedNodes.Should().HaveCount(2, "Both analysis and delivery nodes should complete");
    }

    [Fact]
    public async Task EndToEnd_NodeFailure_PropagatesToRunFailed()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var node = CreateNode("Failing Node", actionId, "output", 1);
        var (service, mocks) = CreateTestService();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        mocks.Executor
            .Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mocks.Executor
            .Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) =>
                NodeOutput.Error(ctx.Node.Id, ctx.Node.OutputVariable, "Simulated failure"));

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(mocks.Executor.Object);

        SetupScopeResolver(mocks, actionId);

        var request = CreateRequest(playbookId);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in service.ExecuteAsync(request, new DefaultHttpContext(), CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var failedEvent = events.FirstOrDefault(e => e.Type == PlaybookEventType.RunFailed);
        failedEvent.Should().NotBeNull("Playbook should fail when node fails");
        failedEvent!.Error.Should().Contain("Failing Node");
    }

    [Fact]
    public async Task EndToEnd_Cancellation_StopsExecution()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        var node1 = CreateNode("Node 1", actionId, "output1", 1);
        var node2 = CreateNode("Node 2", actionId, "output2", 2, node1.Id);

        var (service, mocks) = CreateTestService();
        var cts = new CancellationTokenSource();

        mocks.NodeService
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node1, node2 });

        mocks.Executor
            .Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mocks.Executor
            .Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken ct) =>
            {
                // Cancel during first node execution
                if (ctx.Node.Name == "Node 1")
                {
                    cts.Cancel();
                }
                ct.ThrowIfCancellationRequested();
                await Task.Delay(10, ct);
                return NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { });
            });

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(mocks.Executor.Object);

        SetupScopeResolver(mocks, actionId);

        var request = CreateRequest(playbookId);

        // Act & Assert
        // Use ThrowsAnyAsync to accept both OperationCanceledException and TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.ExecuteAsync(request, new DefaultHttpContext(), cts.Token))
            {
                // Consume events
            }
        });
    }

    #endregion

    #region Helper Methods

    private static PlaybookNodeDto CreateNode(
        string name,
        Guid actionId,
        string outputVariable,
        int order,
        params Guid[] dependsOn)
    {
        return new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            ActionId = actionId,
            OutputVariable = outputVariable,
            ExecutionOrder = order,
            DependsOn = dependsOn,
            IsActive = true
        };
    }

    private static PlaybookRunRequest CreateRequest(Guid playbookId)
    {
        return new PlaybookRunRequest
        {
            PlaybookId = playbookId,
            DocumentIds = [Guid.NewGuid()]
        };
    }

    private static NodeExecutionContext CreateNodeContext(ActionType actionType, string configJson)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Test Node",
                ExecutionOrder = 1,
                OutputVariable = "testOutput",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Test Action"
            },
            ActionType = actionType,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private record TestMocks(
        Mock<INodeService> NodeService,
        Mock<INodeExecutorRegistry> ExecutorRegistry,
        Mock<IScopeResolverService> ScopeResolver,
        Mock<IAnalysisOrchestrationService> LegacyOrchestrator,
        Mock<INodeExecutor> Executor);

    private static (PlaybookOrchestrationService Service, TestMocks Mocks) CreateTestService()
    {
        var nodeServiceMock = new Mock<INodeService>();
        var executorRegistryMock = new Mock<INodeExecutorRegistry>();
        var scopeResolverMock = new Mock<IScopeResolverService>();
        var legacyOrchestratorMock = new Mock<IAnalysisOrchestrationService>();
        var executorMock = new Mock<INodeExecutor>();
        var loggerMock = new Mock<ILogger<PlaybookOrchestrationService>>();

        var service = new PlaybookOrchestrationService(
            nodeServiceMock.Object,
            executorRegistryMock.Object,
            scopeResolverMock.Object,
            legacyOrchestratorMock.Object,
            loggerMock.Object);

        return (service, new TestMocks(
            nodeServiceMock,
            executorRegistryMock,
            scopeResolverMock,
            legacyOrchestratorMock,
            executorMock));
    }

    private static void SetupSuccessfulExecution(TestMocks mocks, Guid actionId)
    {
        mocks.Executor
            .Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mocks.Executor
            .Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) =>
                NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { }));

        mocks.ExecutorRegistry
            .Setup(x => x.GetExecutor(ActionType.AiAnalysis))
            .Returns(mocks.Executor.Object);

        SetupScopeResolver(mocks, actionId);
    }

    private static void SetupScopeResolver(TestMocks mocks, Guid actionId)
    {
        mocks.ScopeResolver
            .Setup(x => x.ResolveNodeScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes([], [], []));

        mocks.ScopeResolver
            .Setup(x => x.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = actionId, Name = "Test Action" });
    }

    #endregion
}
