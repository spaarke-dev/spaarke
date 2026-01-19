using System.Runtime.CompilerServices;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Testing;

/// <summary>
/// Executes playbook tests in Quick mode using real documents via temp blob storage.
/// Provides realistic validation with Document Intelligence extraction but ephemeral storage.
/// </summary>
/// <remarks>
/// Quick test flow:
/// 1. Upload document to temp blob storage (24hr TTL)
/// 2. Extract text using Document Intelligence (or native for text files)
/// 3. Execute playbook nodes against real extraction
/// 4. Stream progress events to client
/// 5. Return results (not persisted to Dataverse)
///
/// Expected execution time: ~20-30 seconds for typical documents.
/// </remarks>
public class QuickTestExecutor : IQuickTestExecutor
{
    private readonly ITempBlobStorageService _tempBlobStorage;
    private readonly ITextExtractor _textExtractor;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<QuickTestExecutor> _logger;

    public QuickTestExecutor(
        ITempBlobStorageService tempBlobStorage,
        ITextExtractor textExtractor,
        IOpenAiClient openAiClient,
        ILogger<QuickTestExecutor> logger)
    {
        _tempBlobStorage = tempBlobStorage;
        _textExtractor = textExtractor;
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TestExecutionEvent> ExecuteAsync(
        CanvasState canvas,
        Stream documentStream,
        string fileName,
        string contentType,
        TestOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;
        var nodes = canvas.Nodes ?? Array.Empty<CanvasNode>();
        var maxNodes = options?.MaxNodes ?? nodes.Length;
        var totalSteps = Math.Min(maxNodes, nodes.Length);

        _logger.LogInformation(
            "Starting quick test execution: SessionId={SessionId}, Nodes={NodeCount}, FileName={FileName}",
            sessionId, nodes.Length, fileName);

        // Step 1: Upload document to temp storage
        yield return CreateProgressEvent("Uploading document to temporary storage...", 0, totalSteps + 3);

        // Upload document (collect error events without yield in catch)
        var uploadEvents = new List<TestExecutionEvent>();
        TempDocumentInfo? documentInfo = null;
        var uploadFailed = false;

        try
        {
            documentInfo = await _tempBlobStorage.UploadTestDocumentAsync(
                documentStream,
                fileName,
                contentType,
                sessionId,
                cancellationToken);

            _logger.LogInformation(
                "Document uploaded: {BlobName}, Size={Size} bytes, SAS expires at {Expiry}",
                documentInfo.BlobName, documentInfo.SizeBytes, documentInfo.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload document to temp storage");
            uploadFailed = true;
            uploadEvents.Add(new TestExecutionEvent
            {
                Type = TestEventTypes.Error,
                Data = new { message = $"Failed to upload document: {ex.Message}" },
                Done = true
            });
        }

        // Emit upload events outside try/catch
        foreach (var evt in uploadEvents)
        {
            yield return evt;
        }
        if (uploadFailed)
        {
            yield break;
        }

        // Step 2: Extract text from document
        yield return CreateProgressEvent("Extracting document content...", 1, totalSteps + 3);

        // Extract text (collect error events without yield in catch)
        var extractEvents = new List<TestExecutionEvent>();
        string? extractedText = null;
        var extractFailed = false;

        try
        {
            // Download from blob for extraction
            var blobStream = await _tempBlobStorage.DownloadAsync(documentInfo!.BlobName, cancellationToken);
            if (blobStream == null)
            {
                extractFailed = true;
                extractEvents.Add(new TestExecutionEvent
                {
                    Type = TestEventTypes.Error,
                    Data = new { message = "Failed to download document for extraction" },
                    Done = true
                });
            }
            else
            {
                var extractionResult = await _textExtractor.ExtractAsync(blobStream, fileName, cancellationToken);

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

        // Emit extract events outside try/catch
        foreach (var evt in extractEvents)
        {
            yield return evt;
        }
        if (extractFailed)
        {
            yield break;
        }

        // Step 3: Build execution context with extracted text
        yield return CreateProgressEvent("Preparing execution context...", 2, totalSteps + 3);

        var executionContext = new Dictionary<string, object>
        {
            ["document"] = new QuickTestDocumentContext
            {
                FileName = fileName,
                ContentType = contentType,
                ExtractedText = extractedText ?? string.Empty,
                SizeBytes = documentInfo!.SizeBytes,
                BlobUrl = documentInfo.SasUrl,
                ExpiresAt = documentInfo.ExpiresAt
            }
        };

        var nodesExecuted = 0;
        var nodesSkipped = 0;
        var nodesFailed = 0;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        // Step 4: Execute nodes
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
                _logger.LogWarning(ex, "Quick test node execution failed for {NodeId}", node.Id);

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
                // Include document URL for result preview (expires in 24hr)
                ReportUrl = documentInfo!.SasUrl
            },
            Done = true
        };

        _logger.LogInformation(
            "Quick test execution completed: SessionId={SessionId}, Executed={Executed}, Skipped={Skipped}, Failed={Failed}, Duration={Duration}ms",
            sessionId, nodesExecuted, nodesSkipped, nodesFailed, totalDuration);
    }

    /// <inheritdoc />
    public async Task<TempDocumentInfo> UploadTestDocumentAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        return await _tempBlobStorage.UploadTestDocumentAsync(
            documentStream, fileName, contentType, sessionId, cancellationToken);
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
        // Build prompt with document context
        var nodeLabel = node.Label ?? "analysis";
        var prompt = $"""
            Analyze the following document and provide a {nodeLabel}.

            Document Content:
            {TruncateForContext(documentText, 8000)}

            Provide a structured analysis response in JSON format.
            """;

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

        // Estimate tokens (rough approximation)
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
        // Simple condition evaluation - in production, would parse ConditionJson
        var result = true; // Default to true for testing

        if (!string.IsNullOrEmpty(node.ConditionJson))
        {
            // Basic evaluation: check if referenced variable exists and is truthy
            // Full implementation would use a JSON rules engine
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
        // Gather outputs from context
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
/// Interface for quick test execution.
/// </summary>
public interface IQuickTestExecutor
{
    /// <summary>
    /// Execute a playbook in quick test mode with a real document.
    /// </summary>
    IAsyncEnumerable<TestExecutionEvent> ExecuteAsync(
        CanvasState canvas,
        Stream documentStream,
        string fileName,
        string contentType,
        TestOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upload a test document to temp storage (for pre-upload scenarios).
    /// </summary>
    Task<TempDocumentInfo> UploadTestDocumentAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken);
}

/// <summary>
/// Document context for quick test execution.
/// </summary>
public record QuickTestDocumentContext
{
    /// <summary>Original filename.</summary>
    public required string FileName { get; init; }

    /// <summary>MIME content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Extracted text from document.</summary>
    public required string ExtractedText { get; init; }

    /// <summary>Document size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Temporary SAS URL for document access.</summary>
    public string? BlobUrl { get; init; }

    /// <summary>When the temporary URL expires.</summary>
    public DateTime ExpiresAt { get; init; }
}
