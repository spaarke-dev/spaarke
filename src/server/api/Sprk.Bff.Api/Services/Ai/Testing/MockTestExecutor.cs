using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Testing;

/// <summary>
/// Executes playbook tests in Mock mode using generated sample data.
/// Provides fast validation of playbook logic without AI calls or real documents.
/// </summary>
/// <remarks>
/// Mock test execution:
/// - Generates sample input data based on scope definitions
/// - Simulates node execution with realistic timing
/// - Returns mock outputs matching expected schemas
/// - Completes in under 5 seconds for typical playbooks
/// </remarks>
public class MockTestExecutor : IMockTestExecutor
{
    private readonly IMockDataGenerator _mockDataGenerator;
    private readonly ILogger<MockTestExecutor> _logger;

    public MockTestExecutor(
        IMockDataGenerator mockDataGenerator,
        ILogger<MockTestExecutor> logger)
    {
        _mockDataGenerator = mockDataGenerator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TestExecutionEvent> ExecuteAsync(
        CanvasState canvas,
        TestOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nodes = canvas.Nodes ?? Array.Empty<CanvasNode>();
        var maxNodes = options?.MaxNodes ?? nodes.Length;
        var totalSteps = Math.Min(maxNodes, nodes.Length);

        _logger.LogInformation(
            "Starting mock test execution with {NodeCount} nodes (max: {MaxNodes})",
            nodes.Length, maxNodes);

        var startTime = DateTime.UtcNow;
        var executionContext = new Dictionary<string, object>();
        var nodesExecuted = 0;
        var nodesSkipped = 0;
        var nodesFailed = 0;

        // Generate mock document context
        var mockDocument = _mockDataGenerator.GenerateMockDocument(options?.SampleDocumentType);
        executionContext["document"] = mockDocument;

        // Execute nodes in order
        for (var i = 0; i < totalSteps && !cancellationToken.IsCancellationRequested; i++)
        {
            var node = nodes[i];
            var stepNumber = i + 1;

            // Emit node_start event
            yield return new TestExecutionEvent
            {
                Type = TestEventTypes.NodeStart,
                Data = new NodeStartData
                {
                    NodeId = node.Id,
                    Label = node.Label ?? $"Node {stepNumber}",
                    NodeType = node.Type,
                    StepNumber = stepNumber,
                    TotalSteps = totalSteps
                }
            };

            // Process node and collect events (avoid yield in try/catch)
            var nodeEvents = new List<TestExecutionEvent>();
            var shouldContinue = false;

            try
            {
                // Check if node should be skipped (condition node returned false)
                if (ShouldSkipNode(node, executionContext))
                {
                    nodesSkipped++;
                    nodeEvents.Add(new TestExecutionEvent
                    {
                        Type = TestEventTypes.NodeSkipped,
                        Data = new
                        {
                            nodeId = node.Id,
                            reason = "Condition not met",
                            isMock = true
                        }
                    });
                    shouldContinue = true;
                }
                else
                {
                    // Generate mock output
                    var mockOutput = await _mockDataGenerator.GenerateNodeOutputAsync(
                        node,
                        executionContext,
                        cancellationToken);

                    // Store output in execution context for downstream nodes
                    if (!string.IsNullOrEmpty(mockOutput.OutputVariable))
                    {
                        executionContext[mockOutput.OutputVariable] = mockOutput.Output;
                    }

                    // Update condition tracking for downstream nodes
                    if (node.Type == "condition" && mockOutput.Output is IDictionary<string, object> condOutput)
                    {
                        if (condOutput.TryGetValue("result", out var result) && result is bool condResult)
                        {
                            executionContext[$"condition_{node.Id}"] = condResult;
                        }
                    }

                    nodesExecuted++;

                    // Add node_output event
                    nodeEvents.Add(new TestExecutionEvent
                    {
                        Type = TestEventTypes.NodeOutput,
                        Data = new NodeOutputData
                        {
                            NodeId = node.Id,
                            Output = mockOutput.Output,
                            DurationMs = mockOutput.DurationMs,
                            TokenUsage = new TokenUsageData
                            {
                                InputTokens = 0,
                                OutputTokens = 0,
                                Model = "mock"
                            }
                        }
                    });

                    // Add node_complete event
                    nodeEvents.Add(new TestExecutionEvent
                    {
                        Type = TestEventTypes.NodeComplete,
                        Data = new NodeCompleteData
                        {
                            NodeId = node.Id,
                            Success = true,
                            OutputVariable = mockOutput.OutputVariable
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                nodesFailed++;
                _logger.LogWarning(ex, "Mock node execution failed for {NodeId}", node.Id);

                nodeEvents.Add(new TestExecutionEvent
                {
                    Type = TestEventTypes.NodeError,
                    Data = new
                    {
                        nodeId = node.Id,
                        error = ex.Message,
                        isMock = true
                    }
                });
            }

            // Emit all collected events for this node
            foreach (var evt in nodeEvents)
            {
                yield return evt;
            }

            if (shouldContinue)
            {
                continue;
            }
        }

        var totalDuration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        // Emit test_complete event
        yield return new TestExecutionEvent
        {
            Type = TestEventTypes.Complete,
            Data = new TestCompleteData
            {
                Success = nodesFailed == 0,
                NodesExecuted = nodesExecuted,
                NodesSkipped = nodesSkipped,
                NodesFailed = nodesFailed,
                TotalDurationMs = totalDuration,
                TotalTokenUsage = new TokenUsageData
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                    Model = "mock"
                }
            },
            Done = true
        };

        _logger.LogInformation(
            "Mock test execution completed: {Executed} executed, {Skipped} skipped, {Failed} failed in {Duration}ms",
            nodesExecuted, nodesSkipped, nodesFailed, totalDuration);
    }

    /// <inheritdoc />
    public MockDocumentContext GenerateTestDocument(string? documentType = null)
    {
        return _mockDataGenerator.GenerateMockDocument(documentType);
    }

    /// <inheritdoc />
    public Dictionary<string, object> GetExecutionSummary(
        int nodesExecuted,
        int nodesSkipped,
        int nodesFailed,
        int durationMs)
    {
        return new Dictionary<string, object>
        {
            ["mode"] = "mock",
            ["nodesExecuted"] = nodesExecuted,
            ["nodesSkipped"] = nodesSkipped,
            ["nodesFailed"] = nodesFailed,
            ["totalDurationMs"] = durationMs,
            ["aiTokensUsed"] = 0,
            ["isMock"] = true,
            ["completedAt"] = DateTime.UtcNow
        };
    }

    private static bool ShouldSkipNode(CanvasNode node, IDictionary<string, object> context)
    {
        // Check if this node depends on a condition that was false
        // This is a simplified check - full implementation would follow edges
        if (node.Config != null &&
            node.Config.TryGetValue("DependsOnCondition", out var depValue) &&
            depValue is string conditionNodeId)
        {
            if (context.TryGetValue($"condition_{conditionNodeId}", out var condResult))
            {
                return condResult is false;
            }
        }

        return false;
    }
}

/// <summary>
/// Interface for mock test execution.
/// </summary>
public interface IMockTestExecutor
{
    /// <summary>
    /// Execute a playbook in mock mode with generated sample data.
    /// </summary>
    IAsyncEnumerable<TestExecutionEvent> ExecuteAsync(
        CanvasState canvas,
        TestOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generate a mock document for testing.
    /// </summary>
    MockDocumentContext GenerateTestDocument(string? documentType = null);

    /// <summary>
    /// Get execution summary for display.
    /// </summary>
    Dictionary<string, object> GetExecutionSummary(
        int nodesExecuted,
        int nodesSkipped,
        int nodesFailed,
        int durationMs);
}
