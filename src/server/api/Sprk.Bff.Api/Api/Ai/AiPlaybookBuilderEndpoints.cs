using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Streaming;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Builder;
using Sprk.Bff.Api.Services.Ai.Testing;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// AI Playbook Builder endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides conversational AI assistance for building playbooks on the canvas.
/// Uses Server-Sent Events (SSE) for real-time streaming responses.
/// </summary>
public static class AiPlaybookBuilderEndpoints
{

    public static IEndpointRouteBuilder MapAiPlaybookBuilderEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Re-enable authorization once MSAL auth is implemented in PlaybookBuilderHost PCF
        // For development/testing, endpoints are temporarily accessible without authentication.
        // Production deployment MUST restore .RequireAuthorization() and implement proper auth.
        var group = app.MapGroup("/api/ai/playbook-builder")
            .AllowAnonymous()  // TEMPORARY: Allow anonymous for development (was: .RequireAuthorization())
            .WithTags("AI Playbook Builder");

        // POST /api/ai/playbook-builder/process - Process a builder message with SSE streaming
        group.MapPost("/process", ProcessBuilderMessage)
            .RequireRateLimiting("ai-stream")
            .WithName("ProcessBuilderMessage")
            .WithSummary("Process a playbook builder message with SSE streaming")
            .WithDescription("""
                Processes a user message in the context of playbook building.
                Classifies intent, generates canvas operations, and streams results.
                Uses proper SSE format with event: and data: lines.

                Event Types:
                - thinking: AI processing status/progress
                - dataverse_operation: Dataverse record changes (scope creation, etc.)
                - canvas_patch: Canvas node/edge changes to apply
                - message: AI response text to display
                - clarification: Request for user clarification
                - plan_preview: Build plan preview for user approval
                - done: Processing complete
                - error: Error occurred
                """)
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/generate-plan - Generate a build plan
        group.MapPost("/generate-plan", GenerateBuildPlan)
            .RequireRateLimiting("ai-batch")
            .WithName("GenerateBuildPlan")
            .WithSummary("Generate a build plan for a playbook goal")
            .WithDescription("Generates a structured build plan with steps to create a playbook for the specified goal.")
            .Produces<BuildPlan>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/classify-intent - Classify user intent
        group.MapPost("/classify-intent", ClassifyIntent)
            .RequireRateLimiting("ai-batch")
            .WithName("ClassifyBuilderIntent")
            .WithSummary("Classify the intent of a user message")
            .WithDescription("Classifies a user message into one of the builder intent categories and extracts entities.")
            .Produces<IntentClassification>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/clarification-response - Handle user response to clarification
        group.MapPost("/clarification-response", HandleClarificationResponse)
            .RequireRateLimiting("ai-stream")
            .WithName("HandleClarificationResponse")
            .WithSummary("Process a user's response to a clarification question")
            .WithDescription("""
                Handles a user's response to a clarification question and re-classifies the intent
                with the additional context. Supports option selection, free-text responses,
                and cancellation.

                Response Types:
                - OPTION_SELECTED: User selected one of the provided options
                - FREE_TEXT: User provided a free-form text clarification
                - CANCELLED: User wants to cancel the current operation
                - CONFIRMED: User confirmed the suggested action
                - REJECTED: User rejected the suggested action

                Uses proper SSE format with event: and data: lines.
                """)
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/generate-clarification - Generate AI-powered clarification
        group.MapPost("/generate-clarification", GenerateClarification)
            .RequireRateLimiting("ai-batch")
            .WithName("GenerateClarification")
            .WithSummary("Generate AI-powered clarification questions")
            .WithDescription("""
                Generates contextually relevant clarification questions when the user's intent
                is ambiguous. Uses AI to understand what the user meant and what additional
                information is needed.

                Returns structured clarification questions with options, suggestions, and context
                about what was understood vs. what needs clarification.
                """)
            .Produces<ClarificationQuestion>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/test-execution - Execute playbook test with SSE streaming
        group.MapPost("/test-execution", TestPlaybookExecution)
            .RequireRateLimiting("ai-stream")
            .WithName("TestPlaybookExecution")
            .WithSummary("Execute a playbook test with SSE streaming")
            .WithDescription("""
                Executes a playbook in test mode to validate its configuration.
                Supports three test modes:
                - Mock: Quick logic validation with synthesized sample data (~5s)
                - Quick: Real document with ephemeral storage (~20-30s)
                - Production: Full end-to-end validation (~30-60s)

                Returns progress via Server-Sent Events.

                Event Types:
                - test_started: Test execution beginning
                - node_start: Beginning execution of a node
                - node_output: Node produced output
                - node_complete: Node execution finished
                - node_skipped: Node was skipped (condition not met)
                - node_error: Node execution failed
                - progress: General progress update
                - test_complete: Test execution finished
                - error: Error occurred
                """)
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/test-execution/quick - Quick test with document upload
        group.MapPost("/test-execution/quick", QuickTestPlaybookExecution)
            .RequireRateLimiting("ai-stream")
            .WithName("QuickTestPlaybookExecution")
            .WithSummary("Execute a quick test with document upload")
            .WithDescription("""
                Executes a playbook in Quick test mode with a real document.
                Document is uploaded to temp blob storage (24hr TTL), extracted via Document Intelligence,
                and used as context for playbook execution.

                Request must be multipart/form-data with:
                - file: The document file (PDF, DOCX, TXT, etc.)
                - canvasJson: JSON string of the canvas state
                - options: Optional JSON string of test options

                Returns progress via Server-Sent Events with same event types as regular test execution.
                Results are NOT persisted to Dataverse.
                """)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .DisableAntiforgery();

        // POST /api/ai/playbook-builder/test-execution/production - Production test with SPE document
        group.MapPost("/test-execution/production", ProductionTestPlaybookExecution)
            .RequireRateLimiting("ai-stream")
            .WithName("ProductionTestPlaybookExecution")
            .WithSummary("Execute a production test with SPE document")
            .WithDescription("""
                Executes a playbook in Production test mode with a real SPE document.
                Uses the same pipeline as production execution:
                - Document must exist in SPE storage
                - Full Document Intelligence extraction
                - Results persisted to Dataverse with test flag

                Returns progress via Server-Sent Events with same event types as regular test execution.
                Results ARE persisted to Dataverse (marked as test execution).
                """)
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/agentic - Agentic builder with function calling (Phase 2)
        group.MapPost("/agentic", ProcessAgenticBuilder)
            .RequireRateLimiting("ai-stream")
            .WithName("ProcessAgenticBuilder")
            .WithSummary("Process a builder message using agentic function calling")
            .WithDescription("""
                Processes a user message using an agentic loop with OpenAI function calling.
                The AI agent can execute multiple tool calls (add_node, create_edge, link_scope, etc.)
                in a multi-turn conversation until the task is complete.

                This endpoint implements the "Claude Code for Playbooks" pattern where the AI:
                1. Receives the user request and current canvas state
                2. Has full awareness of available scopes (actions, skills, knowledge)
                3. Calls tools to manipulate the canvas
                4. Continues the loop until the task is complete
                5. Returns all canvas operations and a summary message

                Returns JSON response (not SSE streaming) with:
                - message: AI's response summarizing what was done
                - canvasOperations: Array of operations to apply to the canvas
                - toolCallCount: Number of tool calls made
                - success: Whether the operation completed successfully
                """)
            .Produces<AgenticBuilderResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Process a builder message with SSE streaming.
    /// POST /api/ai/playbook-builder/process
    /// </summary>
    /// <remarks>
    /// Streams events in SSE format with proper event: and data: lines.
    /// Event types: thinking, dataverse_operation, canvas_patch, message, done, error, clarification, plan_preview
    /// </remarks>
    private static async Task ProcessBuilderMessage(
        BuilderRequest request,
        IAiPlaybookBuilderService builderService,
        HttpContext context,
        ILogger<AiPlaybookBuilderService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Message is required" }, cancellationToken);
            return;
        }

        // Set SSE headers using utility
        ServerSentEventWriter.SetSseHeaders(response);

        logger.LogInformation(
            "Processing builder message, PlaybookId={PlaybookId}, SessionId={SessionId}, TraceId={TraceId}",
            request.PlaybookId, request.SessionId, context.TraceIdentifier);

        var operationCount = 0;

        try
        {
            // Stream initial thinking event - friendly message
            await ServerSentEventWriter.WriteThinkingAsync(
                response, "Let me understand what you need...", "Thinking", cancellationToken);

            await foreach (var chunk in builderService.ProcessMessageAsync(request, cancellationToken))
            {
                if (await WriteChunkAsSseEventAsync(response, chunk, cancellationToken))
                {
                    operationCount++;
                }
            }

            // Stream done event
            await ServerSentEventWriter.WriteDoneAsync(
                response, operationCount, "Processing complete", cancellationToken: cancellationToken);

            logger.LogInformation(
                "Builder processing completed, OperationCount={OperationCount}, TraceId={TraceId}",
                operationCount, context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during builder processing, TraceId={TraceId}",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during builder processing, TraceId={TraceId}", context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Task 050: Include correlation ID in error response for tracing
                await ServerSentEventWriter.WriteErrorAsync(
                    response,
                    "An error occurred while processing your request",
                    code: "PROCESSING_ERROR",
                    isRecoverable: true,
                    suggestedAction: "Please try again",
                    correlationId: context.TraceIdentifier,
                    cancellationToken: CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Map a BuilderStreamChunk to the appropriate SSE event type and write it.
    /// </summary>
    /// <returns>True if a canvas operation was written, false otherwise.</returns>
    private static async Task<bool> WriteChunkAsSseEventAsync(
        HttpResponse response,
        BuilderStreamChunk chunk,
        CancellationToken cancellationToken)
    {
        var isCanvasOperation = false;

        switch (chunk.Type)
        {
            case BuilderChunkType.Message:
                await ServerSentEventWriter.WriteMessageAsync(
                    response, chunk.Text ?? string.Empty, isPartial: false, cancellationToken);
                break;

            case BuilderChunkType.CanvasOperation:
                if (chunk.Patch is not null)
                {
                    await ServerSentEventWriter.WriteCanvasPatchAsync(
                        response, chunk.Patch, description: null, cancellationToken);
                    isCanvasOperation = true;
                }
                break;

            case BuilderChunkType.Clarification:
                await ServerSentEventWriter.WriteClarificationAsync(
                    response, chunk.Text ?? "Please clarify your request", options: null, cancellationToken);
                break;

            case BuilderChunkType.PlanPreview:
                // Plan preview requires a BuildPlan object - extract from metadata if available
                if (chunk.Metadata?.TryGetValue("plan", out var planObj) == true &&
                    planObj is BuildPlan plan)
                {
                    await ServerSentEventWriter.WritePlanPreviewAsync(response, plan, cancellationToken);
                }
                break;

            case BuilderChunkType.Complete:
                // Done is handled after the loop; skip here
                break;

            case BuilderChunkType.Error:
                await ServerSentEventWriter.WriteErrorAsync(
                    response, chunk.Error ?? "Unknown error", cancellationToken: cancellationToken);
                break;

            default:
                // Unknown chunk type - log and skip
                break;
        }

        return isCanvasOperation;
    }

    /// <summary>
    /// Generate a build plan for a playbook goal.
    /// POST /api/ai/playbook-builder/generate-plan
    /// </summary>
    private static async Task<IResult> GenerateBuildPlan(
        BuildPlanRequest request,
        IAiPlaybookBuilderService builderService,
        ILogger<AiPlaybookBuilderService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            return Results.BadRequest(new { error = "Goal is required" });
        }

        logger.LogInformation("Generating build plan for goal: {Goal}", request.Goal);

        try
        {
            var plan = await builderService.GenerateBuildPlanAsync(request, cancellationToken);
            return Results.Ok(plan);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating build plan");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to generate build plan");
        }
    }

    /// <summary>
    /// Classify the intent of a user message.
    /// POST /api/ai/playbook-builder/classify-intent
    /// </summary>
    private static async Task<IResult> ClassifyIntent(
        ClassifyIntentRequest request,
        IAiPlaybookBuilderService builderService,
        ILogger<AiPlaybookBuilderService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required" });
        }

        logger.LogDebug("Classifying intent for message: {Message}", request.Message);

        try
        {
            var classification = await builderService.ClassifyIntentAsync(
                request.Message, request.CanvasContext, cancellationToken);
            return Results.Ok(classification);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error classifying intent");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to classify intent");
        }
    }

    /// <summary>
    /// Handle user response to a clarification question.
    /// POST /api/ai/playbook-builder/clarification-response
    /// </summary>
    /// <remarks>
    /// Re-classifies the user's intent with the additional context from their clarification response.
    /// Streams the result similar to the regular /process endpoint.
    /// </remarks>
    private static async Task HandleClarificationResponse(
        ClarificationResponseRequest request,
        IAiPlaybookBuilderService builderService,
        HttpContext context,
        ILogger<AiPlaybookBuilderService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Validate request
        if (request.ClarificationResponse == null)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "ClarificationResponse is required" }, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ClarificationResponse.SessionId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "SessionId is required in ClarificationResponse" }, cancellationToken);
            return;
        }

        // Build the BuilderRequest with clarification response
        var builderRequest = new BuilderRequest
        {
            Message = request.ClarificationResponse.FreeTextResponse ?? request.OriginalMessage ?? "",
            CanvasState = request.CanvasState ?? new CanvasState(),
            SessionId = request.ClarificationResponse.SessionId,
            ClarificationResponse = request.ClarificationResponse,
            PlaybookId = request.PlaybookId
        };

        // Set SSE headers
        ServerSentEventWriter.SetSseHeaders(response);

        logger.LogInformation(
            "Handling clarification response, SessionId={SessionId}, ResponseType={ResponseType}, TraceId={TraceId}",
            request.ClarificationResponse.SessionId,
            request.ClarificationResponse.ResponseType,
            context.TraceIdentifier);

        var operationCount = 0;

        try
        {
            // Stream initial thinking event - friendly message
            await ServerSentEventWriter.WriteThinkingAsync(
                response, "Got it, let me work on that...", "Thinking", cancellationToken);

            await foreach (var chunk in builderService.ProcessMessageAsync(builderRequest, cancellationToken))
            {
                if (await WriteChunkAsSseEventAsync(response, chunk, cancellationToken))
                {
                    operationCount++;
                }
            }

            // Stream done event
            await ServerSentEventWriter.WriteDoneAsync(
                response, operationCount, "Processing complete", cancellationToken: cancellationToken);

            logger.LogInformation(
                "Clarification response processing completed, OperationCount={OperationCount}, TraceId={TraceId}",
                operationCount, context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during clarification processing, TraceId={TraceId}",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during clarification processing, TraceId={TraceId}", context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Task 050: Include correlation ID in error response for tracing
                await ServerSentEventWriter.WriteErrorAsync(
                    response,
                    "An error occurred while processing your clarification",
                    code: "CLARIFICATION_ERROR",
                    isRecoverable: true,
                    suggestedAction: "Please try again",
                    correlationId: context.TraceIdentifier,
                    cancellationToken: CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Generate AI-powered clarification questions.
    /// POST /api/ai/playbook-builder/generate-clarification
    /// </summary>
    /// <remarks>
    /// Generates contextually relevant clarification questions using AI.
    /// Used when intent classification has low confidence.
    /// </remarks>
    private static async Task<IResult> GenerateClarification(
        GenerateClarificationRequest request,
        AiPlaybookBuilderService builderService,
        ILogger<AiPlaybookBuilderService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required" });
        }

        logger.LogDebug(
            "Generating clarification for message: {Message}, InitialConfidence={Confidence}",
            request.Message, request.InitialClassification?.Confidence);

        try
        {
            // Build canvas context if provided
            var canvasContext = request.CanvasContext != null
                ? new CanvasContext
                {
                    NodeCount = request.CanvasContext.NodeCount,
                    NodeTypes = request.CanvasContext.NodeTypes ?? [],
                    IsSaved = request.CanvasContext.IsSaved,
                    SelectedNodeId = request.CanvasContext.SelectedNodeId
                }
                : null;

            // Generate clarification using the service
            var clarification = await builderService.GenerateClarificationAsync(
                request.Message,
                canvasContext,
                request.InitialClassification,
                cancellationToken);

            return Results.Ok(clarification);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating clarification");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to generate clarification");
        }
    }

    /// <summary>
    /// Execute a playbook test with SSE streaming.
    /// POST /api/ai/playbook-builder/test-execution
    /// </summary>
    /// <remarks>
    /// Streams test execution progress via SSE format.
    /// Supports Mock, Quick, and Production test modes.
    /// </remarks>
    private static async Task TestPlaybookExecution(
        TestPlaybookRequest request,
        IAiPlaybookBuilderService builderService,
        HttpContext context,
        ILogger<AiPlaybookBuilderService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Validate request
        var validationError = ValidateTestRequest(request);
        if (validationError != null)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = validationError }, cancellationToken);
            return;
        }

        // Set SSE headers using utility
        ServerSentEventWriter.SetSseHeaders(response);

        logger.LogInformation(
            "Starting playbook test execution, Mode={Mode}, PlaybookId={PlaybookId}, SessionId={SessionId}, TraceId={TraceId}",
            request.Mode, request.PlaybookId, request.SessionId, context.TraceIdentifier);

        try
        {
            // Write initial test_started event
            await WriteTestEventAsync(response, TestEventTypes.Started, new
            {
                mode = request.Mode.ToString().ToLowerInvariant(),
                playbookId = request.PlaybookId,
                timestamp = DateTime.UtcNow
            }, cancellationToken);

            // Execute the test and stream results
            await foreach (var evt in builderService.ExecuteTestAsync(request, cancellationToken))
            {
                await WriteTestEventAsync(response, evt.Type, evt.Data, cancellationToken, evt.Done);
            }

            logger.LogInformation(
                "Playbook test execution completed, Mode={Mode}, TraceId={TraceId}",
                request.Mode, context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during test execution, TraceId={TraceId}",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during test execution, TraceId={TraceId}", context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Task 050: Include correlation ID in error response for tracing
                await WriteTestEventAsync(response, TestEventTypes.Error, new
                {
                    message = "An error occurred during test execution",
                    code = "TEST_EXECUTION_ERROR",
                    isRecoverable = true,
                    correlationId = context.TraceIdentifier
                }, CancellationToken.None, done: true);
            }
        }
    }

    /// <summary>
    /// Validate test execution request.
    /// </summary>
    private static string? ValidateTestRequest(TestPlaybookRequest request)
    {
        // Production mode requires a saved playbook
        if (request.Mode == TestMode.Production && request.PlaybookId == null)
        {
            return "PlaybookId is required for Production test mode. Save the playbook first.";
        }

        // Must provide either PlaybookId or CanvasJson
        if (request.PlaybookId == null && request.CanvasJson == null)
        {
            return "Either PlaybookId or CanvasJson must be provided.";
        }

        // Validate canvas has content if provided
        if (request.CanvasJson != null &&
            (request.CanvasJson.Nodes == null || request.CanvasJson.Nodes.Length == 0))
        {
            return "CanvasJson must contain at least one node.";
        }

        // Validate timeout if provided
        if (request.Options?.TimeoutSeconds is < 1 or > 600)
        {
            return "TimeoutSeconds must be between 1 and 600 seconds.";
        }

        // Validate MaxNodes if provided
        if (request.Options?.MaxNodes is < 1)
        {
            return "MaxNodes must be at least 1.";
        }

        return null;
    }

    /// <summary>
    /// Write a test execution event in SSE format.
    /// </summary>
    private static async Task WriteTestEventAsync(
        HttpResponse response,
        string eventType,
        object? data,
        CancellationToken cancellationToken,
        bool done = false)
    {
        var eventData = new TestExecutionEvent
        {
            Type = eventType,
            Data = data,
            Done = done
        };

        await ServerSentEventWriter.WriteEventAsync(response, eventType, eventData, cancellationToken);
    }

    /// <summary>
    /// Execute a quick test with document upload.
    /// POST /api/ai/playbook-builder/test-execution/quick
    /// </summary>
    /// <remarks>
    /// Accepts multipart/form-data with:
    /// - file: The document to test with
    /// - canvasJson: JSON string of canvas state
    /// - options: Optional JSON string of test options
    /// </remarks>
    private static async Task QuickTestPlaybookExecution(
        HttpContext context,
        IQuickTestExecutor quickTestExecutor,
        ILogger<AiPlaybookBuilderService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;
        var request = context.Request;

        // Validate content type
        if (!request.HasFormContentType)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "Request must be multipart/form-data" },
                cancellationToken);
            return;
        }

        var form = await request.ReadFormAsync(cancellationToken);

        // Get the uploaded file
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "Document file is required. Include a 'file' field in the form data." },
                cancellationToken);
            return;
        }

        // Validate file size (50MB max)
        const long maxFileSize = 50 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = $"File size ({file.Length / (1024 * 1024):F2}MB) exceeds maximum allowed (50MB)" },
                cancellationToken);
            return;
        }

        // Parse canvas JSON
        var canvasJsonString = form["canvasJson"].ToString();
        if (string.IsNullOrWhiteSpace(canvasJsonString))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "canvasJson field is required. Include the canvas state as a JSON string." },
                cancellationToken);
            return;
        }

        CanvasState? canvasState;
        try
        {
            canvasState = System.Text.Json.JsonSerializer.Deserialize<CanvasState>(
                canvasJsonString,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = $"Invalid canvasJson: {ex.Message}" },
                cancellationToken);
            return;
        }

        if (canvasState?.Nodes == null || canvasState.Nodes.Length == 0)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "Canvas must contain at least one node" },
                cancellationToken);
            return;
        }

        // Parse optional test options
        TestOptions? options = null;
        var optionsString = form["options"].ToString();
        if (!string.IsNullOrWhiteSpace(optionsString))
        {
            try
            {
                options = System.Text.Json.JsonSerializer.Deserialize<TestOptions>(
                    optionsString,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException ex)
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsJsonAsync(
                    new { error = $"Invalid options JSON: {ex.Message}" },
                    cancellationToken);
                return;
            }
        }

        // Set SSE headers
        ServerSentEventWriter.SetSseHeaders(response);

        logger.LogInformation(
            "Starting quick test execution with document: FileName={FileName}, Size={Size} bytes, Nodes={NodeCount}",
            file.FileName, file.Length, canvasState.Nodes.Length);

        try
        {
            // Write initial test_started event
            await WriteTestEventAsync(response, TestEventTypes.Started, new
            {
                mode = "quick",
                fileName = file.FileName,
                fileSize = file.Length,
                nodeCount = canvasState.Nodes.Length,
                timestamp = DateTime.UtcNow
            }, cancellationToken);

            // Execute quick test with document
            await using var fileStream = file.OpenReadStream();

            await foreach (var evt in quickTestExecutor.ExecuteAsync(
                canvasState,
                fileStream,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                options,
                cancellationToken))
            {
                await WriteTestEventAsync(response, evt.Type, evt.Data, cancellationToken, evt.Done);
            }

            logger.LogInformation(
                "Quick test execution completed for {FileName}",
                file.FileName);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during quick test execution, FileName={FileName}",
                file.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during quick test execution, FileName={FileName}", file.FileName);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Task 050: Include correlation ID in error response for tracing
                await WriteTestEventAsync(response, TestEventTypes.Error, new
                {
                    message = "An error occurred during quick test execution",
                    code = "QUICK_TEST_ERROR",
                    isRecoverable = true,
                    details = ex.Message,
                    correlationId = context.TraceIdentifier
                }, CancellationToken.None, done: true);
            }
        }
    }

    /// <summary>
    /// Execute a production test with SPE document.
    /// POST /api/ai/playbook-builder/test-execution/production
    /// </summary>
    /// <remarks>
    /// Accepts JSON body with:
    /// - playbookId: ID of the saved playbook
    /// - driveId: SPE drive ID containing the document
    /// - itemId: SPE item ID of the document
    /// - options: Optional test options
    /// </remarks>
    private static async Task ProductionTestPlaybookExecution(
        ProductionTestApiRequest request,
        IProductionTestExecutor productionTestExecutor,
        IPlaybookService playbookService,
        HttpContext context,
        ILogger<AiPlaybookBuilderService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Validate request
        if (request.PlaybookId == Guid.Empty)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "PlaybookId is required for production test mode." },
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.DriveId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "DriveId is required. Provide the SPE drive ID containing the document." },
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ItemId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "ItemId is required. Provide the SPE item ID of the document." },
                cancellationToken);
            return;
        }

        // Load playbook metadata
        var playbook = await playbookService.GetPlaybookAsync(request.PlaybookId, cancellationToken);
        if (playbook == null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(
                new { error = $"Playbook {request.PlaybookId} not found. Save the playbook before running production tests." },
                cancellationToken);
            return;
        }

        // Load canvas layout separately
        var canvasLayout = await playbookService.GetCanvasLayoutAsync(request.PlaybookId, cancellationToken);
        if (canvasLayout?.Layout?.Nodes == null || canvasLayout.Layout.Nodes.Length == 0)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(
                new { error = "Playbook has no nodes. Add nodes to the playbook before testing." },
                cancellationToken);
            return;
        }

        // Convert CanvasLayoutDto to CanvasState for test execution
        var canvasState = ConvertToCanvasState(canvasLayout.Layout);

        // Set SSE headers
        ServerSentEventWriter.SetSseHeaders(response);

        logger.LogInformation(
            "Starting production test execution: PlaybookId={PlaybookId}, DriveId={DriveId}, ItemId={ItemId}",
            request.PlaybookId, request.DriveId, request.ItemId);

        try
        {
            // Write initial test_started event
            await WriteTestEventAsync(response, TestEventTypes.Started, new
            {
                mode = "production",
                playbookId = request.PlaybookId,
                playbookName = playbook.Name,
                driveId = request.DriveId,
                itemId = request.ItemId,
                nodeCount = canvasState.Nodes.Length,
                timestamp = DateTime.UtcNow
            }, cancellationToken);

            // Build production test request
            var testRequest = new ProductionTestRequest
            {
                PlaybookId = request.PlaybookId,
                Canvas = canvasState,
                DriveId = request.DriveId,
                ItemId = request.ItemId,
                Options = request.Options
            };

            // Execute production test
            await foreach (var evt in productionTestExecutor.ExecuteAsync(testRequest, cancellationToken))
            {
                await WriteTestEventAsync(response, evt.Type, evt.Data, cancellationToken, evt.Done);
            }

            logger.LogInformation(
                "Production test execution completed for PlaybookId={PlaybookId}",
                request.PlaybookId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during production test execution, PlaybookId={PlaybookId}",
                request.PlaybookId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during production test execution, PlaybookId={PlaybookId}", request.PlaybookId);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Task 050: Include correlation ID in error response for tracing
                await WriteTestEventAsync(response, TestEventTypes.Error, new
                {
                    message = "An error occurred during production test execution",
                    code = "PRODUCTION_TEST_ERROR",
                    isRecoverable = true,
                    details = ex.Message,
                    correlationId = context.TraceIdentifier
                }, CancellationToken.None, done: true);
            }
        }
    }
    /// <summary>
    /// Convert CanvasLayoutDto to CanvasState for test execution.
    /// </summary>
    private static CanvasState ConvertToCanvasState(CanvasLayoutDto layout)
    {
        return new CanvasState
        {
            Nodes = layout.Nodes.Select(n => new CanvasNode
            {
                Id = n.Id,
                Type = n.Type,
                Position = new NodePosition(n.X, n.Y),
                Label = n.Data?.TryGetValue("label", out var label) == true ? label?.ToString() : null,
                Config = n.Data,
                OutputVariable = n.Data?.TryGetValue("outputVariable", out var outputVar) == true ? outputVar?.ToString() : null,
                ConditionJson = n.Data?.TryGetValue("conditionJson", out var condJson) == true ? condJson?.ToString() : null
            }).ToArray(),
            Edges = layout.Edges.Select(e => new CanvasEdge
            {
                Id = e.Id,
                SourceId = e.Source,
                TargetId = e.Target,
                SourceHandle = e.SourceHandle,
                TargetHandle = e.TargetHandle
            }).ToArray()
        };
    }

    /// <summary>
    /// Process a builder message using agentic function calling.
    /// POST /api/ai/playbook-builder/agentic
    /// </summary>
    /// <remarks>
    /// Uses an agentic loop with OpenAI function calling to process the user's request.
    /// The AI can make multiple tool calls (add_node, create_edge, link_scope, etc.)
    /// until the task is complete.
    /// </remarks>
    private static async Task<IResult> ProcessAgenticBuilder(
        AgenticBuilderRequest request,
        IBuilderAgentService agentService,
        HttpContext context,
        ILogger<BuilderAgentService> logger)
    {
        var cancellationToken = context.RequestAborted;

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required" });
        }

        logger.LogInformation(
            "Processing agentic builder request, NodeCount={NodeCount}, TraceId={TraceId}",
            request.CanvasState.Nodes.Length, context.TraceIdentifier);

        try
        {
            var result = await agentService.ExecuteAsync(
                request.Message,
                request.CanvasState,
                cancellationToken);

            logger.LogInformation(
                "Agentic builder completed, ToolCalls={ToolCalls}, Operations={Operations}, Success={Success}, TraceId={TraceId}",
                result.ToolCallCount, result.CanvasOperations.Count, result.Success, context.TraceIdentifier);

            return Results.Ok(new AgenticBuilderResponse
            {
                Message = result.Message,
                CanvasOperations = result.CanvasOperations.Select(op => new AgenticCanvasOperation
                {
                    Type = op.Type.ToString(),
                    Payload = op.Payload
                }).ToArray(),
                ToolCallCount = result.ToolCallCount,
                Success = result.Success,
                Error = result.Error
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during agentic builder processing, TraceId={TraceId}",
                context.TraceIdentifier);
            return Results.StatusCode(499); // Client Closed Request
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during agentic builder processing, TraceId={TraceId}", context.TraceIdentifier);
            return Results.Problem(
                detail: "An error occurred while processing your request",
                statusCode: 500,
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier }
            );
        }
    }
}

/// <summary>
/// API request for production test execution.
/// </summary>
public record ProductionTestApiRequest
{
    /// <summary>ID of the saved playbook to test.</summary>
    public Guid PlaybookId { get; init; }

    /// <summary>SPE drive ID containing the document.</summary>
    public string DriveId { get; init; } = string.Empty;

    /// <summary>SPE item ID of the document.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Optional test execution options.</summary>
    public TestOptions? Options { get; init; }
}

/// <summary>
/// Request for classifying user intent.
/// </summary>
public record ClassifyIntentRequest
{
    /// <summary>The user message to classify.</summary>
    public required string Message { get; init; }

    /// <summary>Optional canvas context for disambiguation.</summary>
    public CanvasContext? CanvasContext { get; init; }
}

/// <summary>
/// Request for handling a clarification response.
/// </summary>
public record ClarificationResponseRequest
{
    /// <summary>The user's response to the clarification question.</summary>
    public required ClarificationResponse ClarificationResponse { get; init; }

    /// <summary>Current canvas state.</summary>
    public CanvasState? CanvasState { get; init; }

    /// <summary>The original message that triggered the clarification.</summary>
    public string? OriginalMessage { get; init; }

    /// <summary>Optional playbook ID if editing existing playbook.</summary>
    public Guid? PlaybookId { get; init; }
}

/// <summary>
/// Request for generating AI-powered clarification questions.
/// </summary>
public record GenerateClarificationRequest
{
    /// <summary>The ambiguous user message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional canvas context for generating relevant options.</summary>
    public GenerateClarificationCanvasContext? CanvasContext { get; init; }

    /// <summary>The initial low-confidence classification result, if any.</summary>
    public AiIntentResult? InitialClassification { get; init; }
}

/// <summary>
/// Canvas context for clarification generation requests.
/// </summary>
public record GenerateClarificationCanvasContext
{
    /// <summary>Number of nodes on canvas.</summary>
    public int NodeCount { get; init; }

    /// <summary>Types of nodes present.</summary>
    public string[]? NodeTypes { get; init; }

    /// <summary>Whether playbook has been saved.</summary>
    public bool IsSaved { get; init; }

    /// <summary>Currently selected node ID.</summary>
    public string? SelectedNodeId { get; init; }
}

/// <summary>
/// Request for agentic builder processing with function calling.
/// </summary>
public record AgenticBuilderRequest
{
    /// <summary>The user's natural language request.</summary>
    public required string Message { get; init; }

    /// <summary>Current canvas state (nodes and edges).</summary>
    public required CanvasState CanvasState { get; init; }

    /// <summary>Optional playbook ID if editing existing playbook.</summary>
    public Guid? PlaybookId { get; init; }

    /// <summary>Session ID for conversation continuity.</summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// Response from agentic builder processing.
/// </summary>
public record AgenticBuilderResponse
{
    /// <summary>The AI's response message to the user.</summary>
    public required string Message { get; init; }

    /// <summary>Canvas operations generated by tool execution.</summary>
    public AgenticCanvasOperation[] CanvasOperations { get; init; } = [];

    /// <summary>Number of tool calls made during the conversation.</summary>
    public int ToolCallCount { get; init; }

    /// <summary>Whether the agent completed successfully.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Error message if the agent failed.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// A canvas operation in the agentic response.
/// </summary>
public record AgenticCanvasOperation
{
    /// <summary>Operation type: AddNode, RemoveNode, AddEdge, RemoveEdge, UpdateNode, UpdateLayout.</summary>
    public required string Type { get; init; }

    /// <summary>Operation payload (node data, edge data, etc.).</summary>
    public required System.Text.Json.JsonDocument Payload { get; init; }
}
