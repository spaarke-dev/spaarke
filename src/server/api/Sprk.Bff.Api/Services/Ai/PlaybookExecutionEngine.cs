using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Unified playbook execution engine supporting both batch and conversational modes.
/// Implements ADR-013 AI Architecture with dual execution paths.
/// </summary>
/// <remarks>
/// This engine coordinates:
/// <list type="bullet">
/// <item><b>Batch mode</b>: Delegates to IPlaybookOrchestrationService for document analysis</item>
/// <item><b>Conversational mode</b>: Uses IAiPlaybookBuilderService for multi-turn interactions</item>
/// </list>
/// </remarks>
public class PlaybookExecutionEngine : IPlaybookExecutionEngine
{
    private readonly IAiPlaybookBuilderService _builderService;
    private readonly IPlaybookOrchestrationService _orchestrationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PlaybookExecutionEngine> _logger;

    public PlaybookExecutionEngine(
        IAiPlaybookBuilderService builderService,
        IPlaybookOrchestrationService orchestrationService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PlaybookExecutionEngine> logger)
    {
        _builderService = builderService;
        _orchestrationService = orchestrationService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BuilderResult> ExecuteConversationalAsync(
        ConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.CurrentMessage);
        ArgumentNullException.ThrowIfNull(context.SessionState);

        _logger.LogInformation(
            "Starting conversational execution. SessionId: {SessionId}, MessageLength: {Length}",
            context.SessionState.SessionId,
            context.CurrentMessage.Length);

        // Emit thinking indicator
        yield return BuilderResult.Thinking("Processing your request...");

        // Convert conversation context to builder request
        var builderRequest = ConvertToBuilderRequest(context);

        // Process through builder service and convert results
        await foreach (var chunk in _builderService.ProcessMessageAsync(builderRequest, cancellationToken))
        {
            var result = ConvertToBuilderResult(chunk);
            if (result != null)
            {
                yield return result;
            }
        }

        // Update session state if needed
        var updatedState = context.SessionState with
        {
            LastActiveAt = DateTimeOffset.UtcNow
        };
        yield return BuilderResult.StateUpdate(updatedState);

        _logger.LogInformation(
            "Conversational execution completed. SessionId: {SessionId}",
            context.SessionState.SessionId);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PlaybookStreamEvent> ExecuteBatchAsync(
        PlaybookRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Starting batch execution. PlaybookId: {PlaybookId}, DocumentCount: {Count}",
            request.PlaybookId,
            request.DocumentIds.Length);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for batch execution.");

        // Delegate to orchestration service
        await foreach (var streamEvent in _orchestrationService.ExecuteAsync(
            request, httpContext, cancellationToken))
        {
            yield return streamEvent;
        }

        _logger.LogInformation(
            "Batch execution completed. PlaybookId: {PlaybookId}",
            request.PlaybookId);
    }

    /// <inheritdoc />
    public ExecutionMode DetermineExecutionMode(
        Guid playbookId,
        bool hasCanvasState,
        bool hasDocuments)
    {
        // Conversational mode: When builder is providing canvas state
        // Batch mode: When documents are provided for analysis
        if (hasCanvasState && !hasDocuments)
        {
            _logger.LogDebug(
                "Determined execution mode: Conversational for playbook {PlaybookId}",
                playbookId);
            return ExecutionMode.Conversational;
        }

        if (hasDocuments)
        {
            _logger.LogDebug(
                "Determined execution mode: Batch for playbook {PlaybookId}",
                playbookId);
            return ExecutionMode.Batch;
        }

        // Default to conversational if neither is provided (interactive mode)
        _logger.LogDebug(
            "Determined execution mode: Conversational (default) for playbook {PlaybookId}",
            playbookId);
        return ExecutionMode.Conversational;
    }

    /// <summary>
    /// Convert ConversationContext to BuilderRequest for the builder service.
    /// </summary>
    private static BuilderRequest ConvertToBuilderRequest(ConversationContext context)
    {
        // Convert conversation history to chat message format
        var chatHistory = context.History
            .Select(h => new BuilderChatMessage
            {
                Role = h.Role.ToString().ToLowerInvariant(),
                Content = h.Content,
                Timestamp = h.Timestamp.DateTime
            })
            .ToArray();

        return new BuilderRequest
        {
            Message = context.CurrentMessage,
            CanvasState = context.SessionState.CanvasState,
            PlaybookId = context.PlaybookId,
            SessionId = context.SessionState.SessionId,
            ChatHistory = chatHistory.Length > 0 ? chatHistory : null
        };
    }

    /// <summary>
    /// Convert BuilderStreamChunk to BuilderResult.
    /// </summary>
    private static BuilderResult? ConvertToBuilderResult(BuilderStreamChunk chunk)
    {
        return chunk.Type switch
        {
            BuilderChunkType.Message => BuilderResult.Message(chunk.Text ?? string.Empty),

            BuilderChunkType.CanvasOperation when chunk.Patch != null =>
                BuilderResult.Operation(chunk.Patch),

            BuilderChunkType.Clarification =>
                BuilderResult.Clarification(chunk.Text ?? "Could you please clarify?"),

            BuilderChunkType.PlanPreview => null, // Plan preview handled separately

            BuilderChunkType.Complete => BuilderResult.Complete(),

            BuilderChunkType.Error =>
                BuilderResult.ErrorResult(chunk.Error ?? "An error occurred"),

            _ => null
        };
    }
}

/// <summary>
/// Extension methods for PlaybookExecutionEngine registration.
/// </summary>
public static class PlaybookExecutionEngineExtensions
{
    /// <summary>
    /// Add PlaybookExecutionEngine and related services to the DI container.
    /// </summary>
    public static IServiceCollection AddPlaybookExecutionEngine(this IServiceCollection services)
    {
        services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>();
        return services;
    }
}
