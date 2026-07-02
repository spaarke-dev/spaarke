using System.Diagnostics;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IInvokePlaybookAi"/>: consumes the SSE event
/// stream produced by <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> and
/// aggregates the terminal node outputs + citation envelopes into a single domain-shape
/// <see cref="PlaybookInvocationResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-013 facade boundary</b>: this is the ONLY allowed translation point between
/// the orchestration-internal types (<see cref="PlaybookStreamEvent"/>,
/// <see cref="NodeOutput"/>, <see cref="PlaybookRunMetrics"/>) and the domain-shape
/// <see cref="PlaybookInvocationResult"/> consumed by CRUD-side callers. The facade
/// does NOT layer additional retry / cache / governance on top of the orchestration
/// service — those concerns live one level down (<see cref="PlaybookOrchestrationService"/>
/// + the node executors) so all callers share the same semantics.
/// </para>
/// <para>
/// <b>ADR-015 telemetry hygiene</b>: this class logs <see cref="ILogger"/> structured
/// properties using <c>playbookId</c> + <c>runId</c> + <c>decision</c> + (optionally)
/// <c>tenantId</c> only. No user content, no parameter values, no node prompt text.
/// </para>
/// </remarks>
public sealed class InvokePlaybookAi : IInvokePlaybookAi
{
    private readonly IPlaybookOrchestrationService _orchestrator;
    private readonly ILogger<InvokePlaybookAi> _logger;

    public InvokePlaybookAi(
        IPlaybookOrchestrationService orchestrator,
        ILogger<InvokePlaybookAi> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PlaybookInvocationResult> InvokePlaybookAsync(
        Guid playbookId,
        IReadOnlyDictionary<string, string>? parameters,
        PlaybookInvocationContext context,
        CancellationToken cancellationToken = default,
        string? userContext = null,
        Sprk.Bff.Api.Services.Ai.DocumentContext? document = null)
    {
        if (playbookId == Guid.Empty)
        {
            throw new ArgumentException("playbookId must be a non-empty GUID.", nameof(playbookId));
        }

        ArgumentNullException.ThrowIfNull(context);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // ADR-015 telemetry hygiene: log presence + lengths only, never the
        // userContext / document text bodies. `hasDocument` + `documentTextLength`
        // let operators size prompt budgets from telemetry without leaking content.
        _logger.LogInformation(
            "InvokePlaybookAi.InvokePlaybookAsync started (playbookId={PlaybookId}, tenantId={TenantId}, parameterCount={ParameterCount}, hasUserContext={HasUserContext}, userContextLength={UserContextLength}, hasDocument={HasDocument}, documentTextLength={DocumentTextLength}).",
            playbookId,
            context.TenantId,
            parameters?.Count ?? 0,
            userContext is not null,
            userContext?.Length ?? 0,
            document is not null,
            document?.TextLength ?? 0);

        // Construct the orchestration request.
        //
        // spaarkeai-compose-r1 task 095 widening (Path B ADR-013 amendment, per
        // CLAUDE.md §6.5 — filed formally in task 102):
        //   `UserContext` + `Document` are forwarded from the new facade parameters.
        //   When both are null (existing Phase 1 chat-tool + M365 Copilot callers),
        //   the request shape is byte-identical to the pre-widening path — the
        //   orchestration service treats null Document + empty DocumentIds as
        //   "no document context" (consistent with the pre-Compose behaviour).
        //
        //   `DocumentIds` remains empty in the widened path because Compose supplies
        //   the pre-loaded document directly via `Document` — no re-fetch from
        //   Dataverse is required (per PlaybookRunRequest.Document XML doc).
        var request = new PlaybookRunRequest
        {
            PlaybookId = playbookId,
            DocumentIds = Array.Empty<Guid>(),
            Parameters = parameters,
            UserContext = userContext,
            Document = document,
        };

        Guid runId = Guid.Empty;
        bool success = false;
        string? terminalText = null;
        System.Text.Json.JsonElement? structuredData = null;
        double? confidence = null;
        string? errorMessage = null;
        string? errorCode = null;
        List<ToolResultCitation> citations = new();

        try
        {
            await foreach (var ev in _orchestrator.ExecuteAsync(request, context.HttpContext, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                // Capture run id from the first event (every event carries it).
                if (runId == Guid.Empty)
                {
                    runId = ev.RunId;
                }

                switch (ev.Type)
                {
                    case PlaybookEventType.NodeCompleted when ev.NodeOutput is { Success: true } output:
                        // Aggregate node outputs. The terminal DeliverOutput node is preferred
                        // when present; otherwise the last successful node wins. Citations are
                        // accumulated from every node's tool results across the run.
                        if (output.IsDeliverOutput || terminalText is null)
                        {
                            terminalText = output.TextContent ?? terminalText;
                            structuredData = output.StructuredData ?? structuredData;
                            if (output.Confidence.HasValue)
                            {
                                confidence = output.Confidence;
                            }
                        }

                        AccumulateCitationsFromToolResults(output.ToolResults, citations);
                        break;

                    case PlaybookEventType.NodeFailed when ev.Error is not null:
                        // Per-node failure does NOT terminate the run yet (orchestrator may
                        // continue past required failures). Surface only run-level outcome.
                        break;

                    case PlaybookEventType.RunCompleted:
                        success = true;
                        break;

                    case PlaybookEventType.RunFailed:
                        success = false;
                        errorMessage = ev.Error;
                        errorCode = "PLAYBOOK_INVOCATION_FAILED";
                        break;

                    case PlaybookEventType.RunCancelled:
                        success = false;
                        errorMessage = "Playbook run was cancelled.";
                        errorCode = NodeErrorCodes.Cancelled;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "InvokePlaybookAi.InvokePlaybookAsync cancelled (playbookId={PlaybookId}, runId={RunId}, durationMs={DurationMs}).",
                playbookId,
                runId,
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "InvokePlaybookAi.InvokePlaybookAsync completed (playbookId={PlaybookId}, runId={RunId}, success={Success}, citationCount={CitationCount}, durationMs={DurationMs}).",
            playbookId,
            runId,
            success,
            citations.Count,
            stopwatch.ElapsedMilliseconds);

        return new PlaybookInvocationResult
        {
            RunId = runId,
            Success = success,
            TextContent = terminalText,
            StructuredData = structuredData,
            Citations = citations,
            Confidence = confidence,
            Duration = stopwatch.Elapsed,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
        };
    }

    /// <summary>
    /// Accumulates citation envelopes from a node's tool results into the run-level
    /// citation list. Reads <see cref="ToolResult.Metadata"/> under the Wave 7b
    /// <see cref="ToolResultMetadataKeys.Citations"/> key.
    /// </summary>
    private static void AccumulateCitationsFromToolResults(
        IReadOnlyList<ToolResult> toolResults,
        List<ToolResultCitation> sink)
    {
        if (toolResults.Count == 0)
        {
            return;
        }

        foreach (var tr in toolResults)
        {
            if (tr.Metadata is null ||
                !tr.Metadata.TryGetValue(ToolResultMetadataKeys.Citations, out var value) ||
                value is null)
            {
                continue;
            }

            // Accept either a typed enumerable (preferred) or the JSON-equivalent the
            // adapter contract documents on ToolResultMetadataKeys.Citations.
            if (value is IEnumerable<ToolResultCitation> typed)
            {
                sink.AddRange(typed);
            }
            // JSON-shape pass-through is intentionally NOT handled here: the only producer
            // upstream of this facade today is the chat-tool adapter (Wave 7b), which uses
            // the typed envelope. If a future producer emits JSON-shape citations, extend
            // here with explicit deserialization rather than silent best-effort.
        }
    }
}
