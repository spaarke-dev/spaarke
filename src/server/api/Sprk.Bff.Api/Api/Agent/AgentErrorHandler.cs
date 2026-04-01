using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Handles all failure modes in the agent gateway and returns user-friendly
/// Adaptive Card error responses. Never exposes raw errors to the agent channel.
/// </summary>
/// <remarks>
/// <para>
/// ADR-010: Concrete type, singleton registration — no interface needed.
/// ADR-015: Never log document content, prompts, or model output — identifiers only.
/// ADR-019: Stable error codes (AGENT_001–005), correlation IDs in all responses.
/// </para>
/// <para>
/// Unlike the Office error handlers that return ProblemDetails HTTP responses,
/// this handler returns Adaptive Card JSON because the M365 Copilot agent channel
/// renders cards as the user-facing format.
/// </para>
/// </remarks>
public sealed class AgentErrorHandler
{
    private readonly ILogger<AgentErrorHandler> _logger;
    private readonly HandoffUrlBuilder _handoffUrlBuilder;

    // ────────────────────────────────────────────────────────────────
    // Stable error codes (AGENT_001–005) per ADR-019 pattern
    // ────────────────────────────────────────────────────────────────

    /// <summary>AGENT_001: BFF API services unreachable.</summary>
    public const string ErrorCodeBffUnavailable = "AGENT_001";

    /// <summary>AGENT_002: OBO token exchange failed.</summary>
    public const string ErrorCodeTokenExchangeFailure = "AGENT_002";

    /// <summary>AGENT_003: Playbook execution timed out.</summary>
    public const string ErrorCodePlaybookTimeout = "AGENT_003";

    /// <summary>AGENT_004: Dataverse operation failed.</summary>
    public const string ErrorCodeDataverseError = "AGENT_004";

    /// <summary>AGENT_005: Unexpected internal error.</summary>
    public const string ErrorCodeUnexpectedError = "AGENT_005";

    public AgentErrorHandler(
        ILogger<AgentErrorHandler> logger,
        HandoffUrlBuilder handoffUrlBuilder)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handoffUrlBuilder = handoffUrlBuilder ?? throw new ArgumentNullException(nameof(handoffUrlBuilder));
    }

    // ────────────────────────────────────────────────────────────────
    // Error handlers — each returns Adaptive Card JSON string
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error card when BFF API services are unreachable.
    /// </summary>
    public string HandleBffUnavailable(string correlationId)
    {
        _logger.LogError(
            "[AGENT-ERROR] BFF services unavailable: CorrelationId={CorrelationId}, ErrorCode={ErrorCode}",
            correlationId, ErrorCodeBffUnavailable);

        return BuildErrorCard(
            "Spaarke services are temporarily unavailable. Please try again in a few moments.",
            correlationId,
            ErrorCodeBffUnavailable);
    }

    /// <summary>
    /// Returns an error card when the OBO token exchange fails.
    /// ADR-015: Logs exception type and correlation only — never logs token content.
    /// </summary>
    public string HandleTokenExchangeFailure(Exception exception, string correlationId)
    {
        // ADR-015: Log exception type and identifier only — no token or credential content.
        _logger.LogError(
            "[AGENT-ERROR] Token exchange failed: CorrelationId={CorrelationId}, ErrorCode={ErrorCode}, ExceptionType={ExceptionType}",
            correlationId, ErrorCodeTokenExchangeFailure, exception.GetType().Name);

        return BuildErrorCard(
            "Authentication failed. Please sign out and sign back in, then try again.",
            correlationId,
            ErrorCodeTokenExchangeFailure);
    }

    /// <summary>
    /// Returns an error card when a playbook execution times out.
    /// Includes a deep-link action to open the Analysis Workspace where the user
    /// can monitor or resume the analysis.
    /// </summary>
    public string HandlePlaybookTimeout(Guid playbookId, Guid jobId, string correlationId)
    {
        _logger.LogWarning(
            "[AGENT-ERROR] Playbook timed out: CorrelationId={CorrelationId}, ErrorCode={ErrorCode}, " +
            "PlaybookId={PlaybookId}, JobId={JobId}",
            correlationId, ErrorCodePlaybookTimeout, playbookId, jobId);

        var workspaceUrl = _handoffUrlBuilder.BuildAnalysisWorkspaceUrl(
            analysisId: jobId,
            sourceFileId: Guid.Empty,
            playbookId: playbookId);

        return BuildPlaybookTimeoutCard(
            "The analysis is taking longer than expected. You can check its progress in the Analysis Workspace.",
            correlationId,
            workspaceUrl);
    }

    /// <summary>
    /// Returns an error card when a Dataverse operation fails.
    /// </summary>
    public string HandleDataverseError(int statusCode, string correlationId)
    {
        _logger.LogError(
            "[AGENT-ERROR] Dataverse error: CorrelationId={CorrelationId}, ErrorCode={ErrorCode}, StatusCode={StatusCode}",
            correlationId, ErrorCodeDataverseError, statusCode);

        var message = statusCode switch
        {
            401 or 403 => "You don't have permission to perform this action. Please contact your administrator.",
            404 => "The requested record was not found. It may have been deleted or you may not have access.",
            429 => "The system is busy. Please wait a moment and try again.",
            >= 500 => "A Dataverse service error occurred. Please try again in a few moments.",
            _ => "A data operation failed. Please try again."
        };

        return BuildErrorCard(message, correlationId, ErrorCodeDataverseError);
    }

    /// <summary>
    /// Returns a generic error card for unexpected failures.
    /// ADR-015: Logs exception type only — never logs message content or model output.
    /// </summary>
    public string HandleUnexpectedError(Exception exception, string correlationId)
    {
        // ADR-015: Log exception type and identifier only — no content, prompts, or model output.
        _logger.LogError(
            "[AGENT-ERROR] Unexpected error: CorrelationId={CorrelationId}, ErrorCode={ErrorCode}, ExceptionType={ExceptionType}",
            correlationId, ErrorCodeUnexpectedError, exception.GetType().Name);

        return BuildErrorCard(
            "Something unexpected went wrong. Please try again. If the problem persists, contact support.",
            correlationId,
            ErrorCodeUnexpectedError);
    }

    // ────────────────────────────────────────────────────────────────
    // Card builders
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a standard error Adaptive Card with a user-friendly message,
    /// correlation ID, and a retry action.
    /// </summary>
    /// <param name="message">User-friendly error message (no internal details).</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="errorCode">Stable error code (AGENT_001–005) per ADR-019.</param>
    /// <param name="originalAction">Optional action verb to include in the retry data.</param>
    public string BuildErrorCard(
        string message,
        string correlationId,
        string errorCode,
        string? originalAction = null)
    {
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["body"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = "Something went wrong",
                    ["weight"] = "Bolder",
                    ["size"] = "Medium",
                    ["color"] = "Attention"
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = message,
                    ["wrap"] = true
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = $"Reference: {correlationId}",
                    ["size"] = "Small",
                    ["isSubtle"] = true
                }
            },
            ["actions"] = new JsonArray
            {
                CreateRetryAction(originalAction)
            }
        };

        return card.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Builds a playbook timeout card with both a retry action and a deep-link
    /// to the Analysis Workspace.
    /// </summary>
    private string BuildPlaybookTimeoutCard(
        string message,
        string correlationId,
        string workspaceUrl)
    {
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["body"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = "Analysis In Progress",
                    ["weight"] = "Bolder",
                    ["size"] = "Medium",
                    ["color"] = "Warning"
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = message,
                    ["wrap"] = true
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = $"Reference: {correlationId}",
                    ["size"] = "Small",
                    ["isSubtle"] = true
                }
            },
            ["actions"] = new JsonArray
            {
                CreateRetryAction(originalAction: null),
                new JsonObject
                {
                    ["type"] = "Action.OpenUrl",
                    ["title"] = "Open in Workspace",
                    ["url"] = workspaceUrl
                }
            }
        };

        return card.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Creates a retry submit action with optional original action context.
    /// </summary>
    private static JsonObject CreateRetryAction(string? originalAction)
    {
        var data = new JsonObject
        {
            ["action"] = "retry"
        };

        if (!string.IsNullOrEmpty(originalAction))
        {
            data["originalAction"] = originalAction;
        }

        return new JsonObject
        {
            ["type"] = "Action.Submit",
            ["title"] = "Try Again",
            ["data"] = data
        };
    }
}
