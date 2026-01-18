using System.Runtime.CompilerServices;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Testing;

/// <summary>
/// Executes playbook tests in Production mode using real SPE documents.
/// Provides highest fidelity validation by using the same pipeline as production.
/// </summary>
/// <remarks>
/// Production test flow:
/// 1. Validate playbook exists (saved playbook required)
/// 2. Validate document exists in SPE
/// 3. Download and extract text using Document Intelligence
/// 4. Execute playbook nodes against real extraction
/// 5. Stream progress events to client
/// 6. Save results to Dataverse with test flag
///
/// Expected execution time: ~30-60 seconds for typical documents.
/// </remarks>
public class ProductionTestExecutor : IProductionTestExecutor
{
    private readonly ISpeFileOperations _speFileOperations;
    private readonly ITextExtractor _textExtractor;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<ProductionTestExecutor> _logger;

    // In-memory store for test results (aligns with Phase 1 AnalysisOrchestrationService pattern)
    // TODO: Replace with Dataverse persistence in future task
    private static readonly Dictionary<Guid, ProductionTestResult> _testResultStore = new();

    public ProductionTestExecutor(
        ISpeFileOperations speFileOperations,
        ITextExtractor textExtractor,
        IOpenAiClient openAiClient,
        ILogger<ProductionTestExecutor> logger)
    {
        _speFileOperations = speFileOperations;
        _textExtractor = textExtractor;
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TestExecutionEvent> ExecuteAsync(
        ProductionTestRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Starting production test execution: SessionId={SessionId}, AnalysisId={AnalysisId}, PlaybookId={PlaybookId}, DriveId={DriveId}, ItemId={ItemId}",
            sessionId, analysisId, request.PlaybookId, request.DriveId, request.ItemId);

        // Step 1: Validate document exists in SPE
        yield return CreateProgressEvent("Validating document in SPE storage...", 0, 5);

        // Validate document (collect error events without yield in catch)
        var validateEvents = new List<TestExecutionEvent>();
        Models.FileHandleDto? fileMetadata = null;
        var validateFailed = false;

        try
        {
            // Resolve drive ID if needed (handles container IDs)
            var resolvedDriveId = await _speFileOperations.ResolveDriveIdAsync(request.DriveId, cancellationToken);

            fileMetadata = await _speFileOperations.GetFileMetadataAsync(
                resolvedDriveId,
                request.ItemId,
                cancellationToken);

            if (fileMetadata == null)
            {
                validateFailed = true;
                validateEvents.Add(new TestExecutionEvent
                {
                    Type = TestEventTypes.Error,
                    Data = new { message = "Document not found in SPE storage. Verify the DriveId and ItemId are correct." },
                    Done = true
                });
            }
            else
            {
                _logger.LogInformation(
                    "Document validated in SPE: Name={Name}, Size={Size} bytes",
                    fileMetadata.Name, fileMetadata.Size);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate document in SPE storage");
            validateFailed = true;
            validateEvents.Add(new TestExecutionEvent
            {
                Type = TestEventTypes.Error,
                Data = new { message = $"Failed to access document in SPE: {ex.Message}" },
                Done = true
            });
        }

        // Emit validation events outside try/catch
        foreach (var evt in validateEvents)
        {
            yield return evt;
        }
        if (validateFailed)
        {
            yield break;
        }

        // Step 2: Download document from SPE
        yield return CreateProgressEvent("Downloading document from SPE storage...", 1, 5);

        var downloadEvents = new List<TestExecutionEvent>();
        Stream? documentStream = null;
        var downloadFailed = false;

        try
        {
            var resolvedDriveId = await _speFileOperations.ResolveDriveIdAsync(request.DriveId, cancellationToken);
            documentStream = await _speFileOperations.DownloadFileAsync(
                resolvedDriveId,
                request.ItemId,
                cancellationToken);

            if (documentStream == null)
            {
                downloadFailed = true;
                downloadEvents.Add(new TestExecutionEvent
                {
                    Type = TestEventTypes.Error,
                    Data = new { message = "Failed to download document from SPE storage." },
                    Done = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document from SPE");
            downloadFailed = true;
            downloadEvents.Add(new TestExecutionEvent
            {
                Type = TestEventTypes.Error,
                Data = new { message = $"Failed to download document: {ex.Message}" },
                Done = true
            });
        }

        // Emit download events outside try/catch
        foreach (var evt in downloadEvents)
        {
            yield return evt;
        }
        if (downloadFailed)
        {
            yield break;
        }

        // Step 3: Extract text from document
        yield return CreateProgressEvent("Extracting document content via Document Intelligence...", 2, 5);

        var extractEvents = new List<TestExecutionEvent>();
        string? extractedText = null;
        var extractFailed = false;

        try
        {
            var fileName = fileMetadata!.Name ?? "document";
            var extractionResult = await _textExtractor.ExtractAsync(documentStream!, fileName, cancellationToken);

            if (!extractionResult.Success)
            {
                _logger.LogWarning(
                    "Text extraction failed: {ErrorMessage}, Method={Method}",
                    extractionResult.ErrorMessage, extractionResult.Method);

                extractFailed = true;
                extractEvents.Add(new TestExecutionEvent
                {
                    Type = TestEventTypes.Error,
                    Data = new
                    {
                        message = $"Text extraction failed: {extractionResult.ErrorMessage}",
                        method = extractionResult.Method.ToString()
                    },
                    Done = true
                });
            }
            else
            {
                extractedText = extractionResult.Text;
                _logger.LogInformation(
                    "Document extracted: {CharCount} characters using {Method}",
                    extractedText?.Length ?? 0, extractionResult.Method);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from document");
            extractFailed = true;
            extractEvents.Add(new TestExecutionEvent
            {
                Type = TestEventTypes.Error,
                Data = new { message = $"Failed to extract text: {ex.Message}" },
                Done = true
            });
        }
        finally
        {
            documentStream?.Dispose();
        }

        // Emit extract events outside try/catch
        foreach (var evt in extractEvents)
        {
            yield return evt;
        }
        if (extractFailed)
        {
            yield break;
        }

        // Step 4: Build execution context
        yield return CreateProgressEvent("Preparing execution context...", 3, 5);

        var nodes = request.Canvas.Nodes ?? Array.Empty<CanvasNode>();
        var maxNodes = request.Options?.MaxNodes ?? nodes.Length;
        var totalSteps = Math.Min(maxNodes, nodes.Length);

        var executionContext = new Dictionary<string, object>
        {
            ["document"] = new ProductionTestDocumentContext
            {
                FileName = fileMetadata!.Name ?? "document",
                DriveId = request.DriveId,
                ItemId = request.ItemId,
                ExtractedText = extractedText ?? string.Empty,
                SizeBytes = fileMetadata.Size ?? 0
            }
        };

        var nodesExecuted = 0;
        var nodesSkipped = 0;
        var nodesFailed = 0;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var nodeResults = new List<NodeExecutionResult>();

        // Step 5: Execute nodes
        yield return CreateProgressEvent("Executing playbook nodes...", 4, 5);

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
            var nodeStartTime = DateTime.UtcNow;
            var shouldContinue = false;
            object? nodeOutput = null;

            try
            {
                // Check if node should be skipped
                if (ShouldSkipNode(node, executionContext))
                {
                    nodesSkipped++;
                    nodeEvents.Add(new TestExecutionEvent
                    {
                        Type = TestEventTypes.NodeSkipped,
                        Data = new
                        {
                            nodeId = node.Id,
                            reason = "Condition not met"
                        }
                    });
                    shouldContinue = true;
                }
                else
                {
                    // Execute node with real document context
                    var (output, tokens) = await ExecuteNodeAsync(
                        node, extractedText!, executionContext, cancellationToken);

                    nodeOutput = output;

                    // Store output in context for downstream nodes
                    if (!string.IsNullOrEmpty(node.OutputVariable))
                    {
                        executionContext[node.OutputVariable] = output;
                    }

                    // Track condition results
                    if (node.Type == "condition" && output is IDictionary<string, object> condOutput)
                    {
                        if (condOutput.TryGetValue("result", out var result) && result is bool condResult)
                        {
                            executionContext[$"condition_{node.Id}"] = condResult;
                        }
                    }

                    nodesExecuted++;
                    totalInputTokens += tokens.InputTokens;
                    totalOutputTokens += tokens.OutputTokens;

                    var nodeDuration = (int)(DateTime.UtcNow - nodeStartTime).TotalMilliseconds;

                    // Store node result for Dataverse persistence
                    nodeResults.Add(new NodeExecutionResult
                    {
                        NodeId = node.Id,
                        NodeType = node.Type,
                        Success = true,
                        Output = output,
                        DurationMs = nodeDuration,
                        TokenUsage = tokens
                    });

                    // Add node_output event
                    nodeEvents.Add(new TestExecutionEvent
                    {
                        Type = TestEventTypes.NodeOutput,
                        Data = new NodeOutputData
                        {
                            NodeId = node.Id,
                            Output = output,
                            DurationMs = nodeDuration,
                            TokenUsage = tokens
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
                            OutputVariable = node.OutputVariable
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                nodesFailed++;
                _logger.LogWarning(ex, "Production test node execution failed for {NodeId}", node.Id);

                nodeResults.Add(new NodeExecutionResult
                {
                    NodeId = node.Id,
                    NodeType = node.Type,
                    Success = false,
                    Error = ex.Message,
                    DurationMs = (int)(DateTime.UtcNow - nodeStartTime).TotalMilliseconds
                });

                nodeEvents.Add(new TestExecutionEvent
                {
                    Type = TestEventTypes.NodeError,
                    Data = new
                    {
                        nodeId = node.Id,
                        error = ex.Message
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

        // Step 6: Save results to Dataverse (with test flag)
        var persistEvents = new List<TestExecutionEvent>();

        try
        {
            var testResult = new ProductionTestResult
            {
                Id = analysisId,
                PlaybookId = request.PlaybookId,
                DocumentDriveId = request.DriveId,
                DocumentItemId = request.ItemId,
                DocumentName = fileMetadata!.Name,
                IsTestExecution = true, // Test flag
                Status = nodesFailed == 0 ? "Completed" : "CompletedWithErrors",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                TotalDurationMs = totalDuration,
                NodesExecuted = nodesExecuted,
                NodesSkipped = nodesSkipped,
                NodesFailed = nodesFailed,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens,
                NodeResults = nodeResults
            };

            // Store in-memory (Phase 1 pattern - will be replaced with Dataverse)
            _testResultStore[analysisId] = testResult;

            _logger.LogInformation(
                "Production test result saved: AnalysisId={AnalysisId}, IsTest=true, Status={Status}",
                analysisId, testResult.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist test results");
            // Don't fail the test if persistence fails - just log it
            persistEvents.Add(new TestExecutionEvent
            {
                Type = TestEventTypes.Progress,
                Data = new { message = "Warning: Failed to persist test results to Dataverse", error = ex.Message }
            });
        }

        // Emit persistence events
        foreach (var evt in persistEvents)
        {
            yield return evt;
        }

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
                    InputTokens = totalInputTokens,
                    OutputTokens = totalOutputTokens,
                    Model = "gpt-4o-mini"
                },
                // Include analysis ID for result retrieval
                AnalysisId = analysisId
            },
            Done = true
        };

        _logger.LogInformation(
            "Production test execution completed: AnalysisId={AnalysisId}, Executed={Executed}, Skipped={Skipped}, Failed={Failed}, Duration={Duration}ms",
            analysisId, nodesExecuted, nodesSkipped, nodesFailed, totalDuration);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateDocumentExistsAsync(
        string driveId,
        string itemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolvedDriveId = await _speFileOperations.ResolveDriveIdAsync(driveId, cancellationToken);
            var metadata = await _speFileOperations.GetFileMetadataAsync(
                resolvedDriveId, itemId, cancellationToken);
            return metadata != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate document existence: DriveId={DriveId}, ItemId={ItemId}",
                driveId, itemId);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<ProductionTestResult?> GetTestResultAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        _testResultStore.TryGetValue(analysisId, out var result);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Execute a single node with real document context.
    /// </summary>
    private async Task<(object Output, TokenUsageData Tokens)> ExecuteNodeAsync(
        CanvasNode node,
        string documentText,
        IDictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        return node.Type switch
        {
            "aiAnalysis" => await ExecuteAiAnalysisNodeAsync(node, documentText, context, cancellationToken),
            "aiCompletion" => await ExecuteAiCompletionNodeAsync(node, documentText, context, cancellationToken),
            "condition" => ExecuteConditionNode(node, context),
            "deliverOutput" => ExecuteDeliverOutputNode(node, context),
            _ => (new { type = node.Type, status = "completed" }, new TokenUsageData())
        };
    }

    /// <summary>
    /// Execute an AI analysis node against real document content.
    /// </summary>
    private async Task<(object Output, TokenUsageData Tokens)> ExecuteAiAnalysisNodeAsync(
        CanvasNode node,
        string documentText,
        IDictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        var nodeLabel = node.Label ?? "analysis";
        var prompt = $"""
            Analyze the following document and provide a {nodeLabel}.

            Document Content:
            {TruncateForContext(documentText, 8000)}

            Provide a structured analysis response in JSON format.
            """;

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

        var inputTokens = prompt.Length / 4;
        var outputTokens = response?.Length / 4 ?? 0;

        return (
            new { analysis = response, nodeType = "aiAnalysis", label = nodeLabel },
            new TokenUsageData { InputTokens = inputTokens, OutputTokens = outputTokens, Model = "gpt-4o-mini" }
        );
    }

    /// <summary>
    /// Execute an AI completion node against real document content.
    /// </summary>
    private async Task<(object Output, TokenUsageData Tokens)> ExecuteAiCompletionNodeAsync(
        CanvasNode node,
        string documentText,
        IDictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        var nodeLabel = node.Label ?? "completion";
        var prompt = $"""
            Based on the following document, generate a {nodeLabel}.

            Document Content:
            {TruncateForContext(documentText, 8000)}

            Generate the requested output.
            """;

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

        var inputTokens = prompt.Length / 4;
        var outputTokens = response?.Length / 4 ?? 0;

        return (
            new { text = response, nodeType = "aiCompletion", label = nodeLabel },
            new TokenUsageData { InputTokens = inputTokens, OutputTokens = outputTokens, Model = "gpt-4o-mini" }
        );
    }

    /// <summary>
    /// Execute a condition node (evaluates against context).
    /// </summary>
    private static (object Output, TokenUsageData Tokens) ExecuteConditionNode(
        CanvasNode node,
        IDictionary<string, object> context)
    {
        var result = true; // Default to true for testing

        if (!string.IsNullOrEmpty(node.ConditionJson))
        {
            result = context.Count > 0;
        }

        return (
            new { result, branch = result ? "true" : "false", nodeType = "condition" },
            new TokenUsageData()
        );
    }

    /// <summary>
    /// Execute a deliver output node.
    /// </summary>
    private static (object Output, TokenUsageData Tokens) ExecuteDeliverOutputNode(
        CanvasNode node,
        IDictionary<string, object> context)
    {
        var outputs = context
            .Where(kv => kv.Key != "document" && !kv.Key.StartsWith("condition_"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return (
            new { delivered = true, format = "json", outputs, nodeType = "deliverOutput" },
            new TokenUsageData()
        );
    }

    /// <summary>
    /// Check if a node should be skipped based on condition dependencies.
    /// </summary>
    private static bool ShouldSkipNode(CanvasNode node, IDictionary<string, object> context)
    {
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

    /// <summary>
    /// Truncate text to fit within context window.
    /// </summary>
    private static string TruncateForContext(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..maxChars] + "\n\n[Content truncated for context limits]";
    }

    /// <summary>
    /// Create a progress event for the SSE stream.
    /// </summary>
    private static TestExecutionEvent CreateProgressEvent(string message, int current, int total)
    {
        return new TestExecutionEvent
        {
            Type = TestEventTypes.Progress,
            Data = new
            {
                message,
                current,
                total,
                percent = total > 0 ? (int)(current * 100.0 / total) : 0
            }
        };
    }
}

/// <summary>
/// Interface for production test execution.
/// </summary>
public interface IProductionTestExecutor
{
    /// <summary>
    /// Execute a playbook in production test mode with a real SPE document.
    /// </summary>
    IAsyncEnumerable<TestExecutionEvent> ExecuteAsync(
        ProductionTestRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validate that a document exists in SPE storage.
    /// </summary>
    Task<bool> ValidateDocumentExistsAsync(
        string driveId,
        string itemId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get a test result by analysis ID.
    /// </summary>
    Task<ProductionTestResult?> GetTestResultAsync(
        Guid analysisId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request for production test execution.
/// </summary>
public record ProductionTestRequest
{
    /// <summary>ID of the saved playbook to test.</summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>Canvas state to execute (loaded from playbook).</summary>
    public required CanvasState Canvas { get; init; }

    /// <summary>SPE drive ID containing the document.</summary>
    public required string DriveId { get; init; }

    /// <summary>SPE item ID of the document.</summary>
    public required string ItemId { get; init; }

    /// <summary>Optional test execution options.</summary>
    public TestOptions? Options { get; init; }
}

/// <summary>
/// Document context for production test execution.
/// </summary>
public record ProductionTestDocumentContext
{
    /// <summary>Document filename.</summary>
    public required string FileName { get; init; }

    /// <summary>SPE drive ID.</summary>
    public required string DriveId { get; init; }

    /// <summary>SPE item ID.</summary>
    public required string ItemId { get; init; }

    /// <summary>Extracted text from document.</summary>
    public required string ExtractedText { get; init; }

    /// <summary>Document size in bytes.</summary>
    public long SizeBytes { get; init; }
}

/// <summary>
/// Result of a production test execution (stored in Dataverse).
/// </summary>
public record ProductionTestResult
{
    /// <summary>Unique analysis ID.</summary>
    public Guid Id { get; init; }

    /// <summary>Playbook ID that was tested.</summary>
    public Guid PlaybookId { get; init; }

    /// <summary>SPE drive ID of the test document.</summary>
    public string? DocumentDriveId { get; init; }

    /// <summary>SPE item ID of the test document.</summary>
    public string? DocumentItemId { get; init; }

    /// <summary>Document name.</summary>
    public string? DocumentName { get; init; }

    /// <summary>Flag indicating this is a test execution (not production).</summary>
    public bool IsTestExecution { get; init; } = true;

    /// <summary>Execution status.</summary>
    public string? Status { get; init; }

    /// <summary>When execution started.</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>When execution completed.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Total execution duration in milliseconds.</summary>
    public int TotalDurationMs { get; init; }

    /// <summary>Number of nodes executed.</summary>
    public int NodesExecuted { get; init; }

    /// <summary>Number of nodes skipped.</summary>
    public int NodesSkipped { get; init; }

    /// <summary>Number of nodes that failed.</summary>
    public int NodesFailed { get; init; }

    /// <summary>Total input tokens consumed.</summary>
    public int TotalInputTokens { get; init; }

    /// <summary>Total output tokens generated.</summary>
    public int TotalOutputTokens { get; init; }

    /// <summary>Individual node execution results.</summary>
    public List<NodeExecutionResult>? NodeResults { get; init; }
}

/// <summary>
/// Result of executing a single node.
/// </summary>
public record NodeExecutionResult
{
    /// <summary>Node ID.</summary>
    public required string NodeId { get; init; }

    /// <summary>Node type.</summary>
    public required string NodeType { get; init; }

    /// <summary>Whether execution succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Node output.</summary>
    public object? Output { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Execution duration in milliseconds.</summary>
    public int DurationMs { get; init; }

    /// <summary>Token usage for this node.</summary>
    public TokenUsageData? TokenUsage { get; init; }
}
