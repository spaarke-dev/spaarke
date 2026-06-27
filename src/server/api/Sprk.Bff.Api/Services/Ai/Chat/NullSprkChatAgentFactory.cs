using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Null-Object subclass of <see cref="SprkChatAgentFactory"/> registered when the compound
/// AI kill switch is OFF (<c>Analysis:Enabled=false</c> or <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 B2 (task 011 Phase 1b Tier 3, 2026-06-01). Chat without
/// AI is semantically meaningless — silent empty SSE streams would mislead the chat UI into
/// rendering a blank conversation rather than communicating the kill-switch state. Every
/// public entry point throws <see cref="FeatureDisabledException"/>; <c>ChatEndpoints</c>
/// handler-level catches surface the exception as an SSE <c>error</c> chunk (for SSE handlers)
/// or as a 503 ProblemDetails (for non-SSE handlers) per ADR-018 + ADR-019.
/// </para>
/// <para>
/// Construction: uses the protected base ctor that only requires <c>ILogger</c> — none of the
/// production AI dependencies (<c>IChatClient</c>, raw chat client) are resolved, which keeps
/// the DI graph valid when those services are absent.
/// </para>
/// <para>
/// Registered in <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c> via the same
/// concrete type <see cref="SprkChatAgentFactory"/> that the production singleton uses; ADR-010
/// keeps the registration concrete-class (no interface introduced).
/// </para>
/// </remarks>
public sealed class NullSprkChatAgentFactory : SprkChatAgentFactory
{
    private const string ErrorCode = "ai.chat.disabled";
    private const string DetailMessage =
        "AI chat requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<SprkChatAgentFactory> _logger;

    public NullSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task<ISprkChatAgent> CreateAgentAsync(
        string sessionId,
        string documentId,
        Guid? playbookId,
        string tenantId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<string>? additionalDocumentIds = null,
        HttpContext? httpContext = null,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null,
        string? latestUserMessage = null,
        IReadOnlyList<string>? previousTurnToolNames = null,
        IReadOnlyList<ChatSessionFile>? uploadedFiles = null,
        CancellationToken cancellationToken = default,
        string? intentHint = null)
    {
        LogDisabled(nameof(CreateAgentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<PlaybookDispatcher> CreatePlaybookDispatcherAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(CreatePlaybookDispatcherAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override DynamicCommandResolver CreateCommandResolver()
    {
        LogDisabled(nameof(CreateCommandResolver));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override PlaybookOutputHandler CreatePlaybookOutputHandler()
    {
        LogDisabled(nameof(CreatePlaybookOutputHandler));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullSprkChatAgentFactory.{Method} invoked while AI chat feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
