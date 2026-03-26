using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Structured telemetry for the M365 Copilot agent gateway.
///
/// Logs interaction metrics, playbook invocations, handoff events, and errors
/// using both ILogger (structured logs) and Application Insights (custom events/metrics).
///
/// ADR-015: MUST NOT log document content, prompts, or model output.
/// Logs ONLY: identifiers, sizes, timings, outcome codes.
/// ADR-010: Concrete type, registered via DI — no unnecessary interface.
/// </summary>
public sealed class AgentTelemetry
{
    private readonly ILogger<AgentTelemetry> _logger;
    private readonly TelemetryClient? _telemetryClient;

    /// <summary>
    /// Known interaction types for the agent gateway.
    /// </summary>
    public static class InteractionTypes
    {
        public const string Message = "message";
        public const string Invoke = "invoke";
        public const string PlaybookRun = "playbook_run";
        public const string Search = "search";
        public const string EmailDraft = "email_draft";
        public const string Handoff = "handoff";
    }

    public AgentTelemetry(ILogger<AgentTelemetry> logger, TelemetryClient? telemetryClient = null)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    /// <summary>
    /// Tracks an agent interaction (message, invoke, search, etc.).
    /// </summary>
    /// <param name="interactionType">One of <see cref="InteractionTypes"/> constants.</param>
    /// <param name="durationMs">Elapsed time in milliseconds.</param>
    /// <param name="success">Whether the interaction completed successfully.</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    public void TrackAgentInteraction(string interactionType, double durationMs, bool success, string? correlationId = null)
    {
        _logger.LogInformation(
            "AgentInteraction: Type={InteractionType} DurationMs={DurationMs} Success={Success} CorrelationId={CorrelationId}",
            interactionType, durationMs, success, correlationId);

        _telemetryClient?.TrackEvent("AgentInteraction", new Dictionary<string, string>
        {
            ["InteractionType"] = interactionType,
            ["Success"] = success.ToString(),
            ["CorrelationId"] = correlationId ?? string.Empty
        }, new Dictionary<string, double>
        {
            ["DurationMs"] = durationMs
        });

        _telemetryClient?.GetMetric("Agent.Interaction.Duration", "InteractionType", "Success")
            .TrackValue(durationMs, interactionType, success.ToString());
    }

    /// <summary>
    /// Tracks a playbook invocation with execution details.
    /// </summary>
    /// <param name="playbookId">The playbook identifier (GUID or slug — never content).</param>
    /// <param name="strategy">Execution strategy: "inline" or "async".</param>
    /// <param name="durationMs">Elapsed time in milliseconds.</param>
    /// <param name="success">Whether the playbook completed successfully.</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    public void TrackPlaybookInvocation(string playbookId, string strategy, double durationMs, bool success, string? correlationId = null)
    {
        _logger.LogInformation(
            "PlaybookInvocation: PlaybookId={PlaybookId} Strategy={Strategy} DurationMs={DurationMs} Success={Success} CorrelationId={CorrelationId}",
            playbookId, strategy, durationMs, success, correlationId);

        _telemetryClient?.TrackEvent("AgentPlaybookInvocation", new Dictionary<string, string>
        {
            ["PlaybookId"] = playbookId,
            ["Strategy"] = strategy,
            ["Success"] = success.ToString(),
            ["CorrelationId"] = correlationId ?? string.Empty
        }, new Dictionary<string, double>
        {
            ["DurationMs"] = durationMs
        });

        _telemetryClient?.GetMetric("Agent.Playbook.Duration", "Strategy", "Success")
            .TrackValue(durationMs, strategy, success.ToString());

        _telemetryClient?.GetMetric("Agent.Playbook.Count", "PlaybookId")
            .TrackValue(1, playbookId);
    }

    /// <summary>
    /// Tracks a handoff event from the agent to a Dataverse deep-link.
    /// </summary>
    /// <param name="destination">Handoff target: "analysis_workspace", "wizard", "document_viewer".</param>
    /// <param name="documentId">Optional document ID (identifier only — no content).</param>
    /// <param name="analysisId">Optional analysis ID (identifier only — no content).</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    public void TrackHandoff(string destination, Guid? documentId = null, Guid? analysisId = null, string? correlationId = null)
    {
        _logger.LogInformation(
            "AgentHandoff: Destination={Destination} DocumentId={DocumentId} AnalysisId={AnalysisId} CorrelationId={CorrelationId}",
            destination, documentId, analysisId, correlationId);

        _telemetryClient?.TrackEvent("AgentHandoff", new Dictionary<string, string>
        {
            ["Destination"] = destination,
            ["HasDocumentId"] = (documentId.HasValue).ToString(),
            ["HasAnalysisId"] = (analysisId.HasValue).ToString(),
            ["CorrelationId"] = correlationId ?? string.Empty
        });

        _telemetryClient?.GetMetric("Agent.Handoff.Count", "Destination")
            .TrackValue(1, destination);
    }

    /// <summary>
    /// Tracks an error that occurred during agent processing.
    /// </summary>
    /// <param name="errorType">Categorized error type (e.g. "auth_failure", "timeout", "playbook_error").</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    /// <param name="interactionType">Optional interaction type where the error occurred.</param>
    public void TrackError(string errorType, string? correlationId = null, string? interactionType = null)
    {
        _logger.LogWarning(
            "AgentError: ErrorType={ErrorType} InteractionType={InteractionType} CorrelationId={CorrelationId}",
            errorType, interactionType, correlationId);

        _telemetryClient?.TrackEvent("AgentError", new Dictionary<string, string>
        {
            ["ErrorType"] = errorType,
            ["InteractionType"] = interactionType ?? string.Empty,
            ["CorrelationId"] = correlationId ?? string.Empty
        });

        _telemetryClient?.GetMetric("Agent.Error.Count", "ErrorType")
            .TrackValue(1, errorType);
    }

    /// <summary>
    /// Tracks agent session duration when a conversation ends or times out.
    /// </summary>
    /// <param name="sessionDurationMs">Total session duration in milliseconds.</param>
    /// <param name="interactionCount">Number of interactions within the session.</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    public void TrackSessionDuration(double sessionDurationMs, int interactionCount, string? correlationId = null)
    {
        _logger.LogInformation(
            "AgentSession: DurationMs={SessionDurationMs} InteractionCount={InteractionCount} CorrelationId={CorrelationId}",
            sessionDurationMs, interactionCount, correlationId);

        _telemetryClient?.TrackEvent("AgentSessionEnd", new Dictionary<string, string>
        {
            ["CorrelationId"] = correlationId ?? string.Empty
        }, new Dictionary<string, double>
        {
            ["DurationMs"] = sessionDurationMs,
            ["InteractionCount"] = interactionCount
        });

        _telemetryClient?.GetMetric("Agent.Session.Duration")
            .TrackValue(sessionDurationMs);
    }

    /// <summary>
    /// Creates a <see cref="Stopwatch"/> for timing operations.
    /// Convenience helper so callers don't need to import System.Diagnostics.
    /// </summary>
    public static Stopwatch StartTimer() => Stopwatch.StartNew();
}
