using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// E-2 engine-output→ledger adapter (ADR-040 / FR-P1-05, spaarke-ai-architecture-redesign-r1
/// task 024): converts the FROZEN Insights engine's composite outputs (surfaced at the chat
/// boundary as an aggregated <see cref="PlaybookInvocationResult"/>) into addressable
/// <see cref="SessionOutput"/> ledger entries via the task-021 universal write path
/// (<see cref="IOutputRouter.RouteAsync"/>). Without this shim, engine runs would be the ONE
/// output class invisible to the ledger, breaking session composition ("draft a letter based
/// on that matter summary") for exactly the highest-value composite outputs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Attach point (freeze-line + P3-survival analysis, task 024)</b>: the only
/// chat-session-attached path into the frozen engine today is
/// <c>SprkChatAgent → invoke_playbook → InvokePlaybookHandler.ExecuteChatAsync →
/// IInvokePlaybookAi → IPlaybookOrchestrationService.ExecuteAsync</c> (both NL and the
/// /summarize soft-slash flow through it since R6 B-G9c3). The adapter therefore attaches in
/// <see cref="Handlers.InvokePlaybookHandler"/> — a component the OVERLAY-MATRIX S4 verdict
/// marks 🔧 fills (tool framework + handlers), NOT inside <c>PlaybookExecutionEngine</c> or
/// the <c>AnalysisOrchestrationService</c> legacy path (both FR-P3-05 DELETE targets), and
/// NOT inside <c>PlaybookOrchestrationService</c> or its nodes (the frozen surface — ADR-039:
/// no new capability lands on the frozen engine; this adapter is a boundary shim on OUR side
/// of the freeze line).
/// </para>
/// <para>
/// <b>Interim Binding identity (documented decision, task 024)</b>: the Insights ask/search
/// Binding rows do not exist until P3 (FR-P3-01), so no <c>sprk_playbookconsumer</c> row can
/// be resolved for an engine run today. The adapter constructs an in-memory interim
/// <see cref="Binding"/> whose <see cref="Binding.BindingId"/> is the invoked PLAYBOOK id —
/// the deterministic identity of the frozen flow — so the ledger key is
/// <c>{playbookId}@t{n}</c> and sequential runs of the same composite increment <c>t{n}</c>.
/// <see cref="Binding.ConsumerType"/> is the interim marker
/// <see cref="InterimConsumerType"/> (<c>engine-playbook</c>), which lands on
/// <see cref="SessionOutput.UcId"/> via the router's legacy-row fallback and makes
/// engine-origin entries discoverable. P3 task 040 (FR-P3-01) re-points this adapter at the
/// real resolved insights Binding rows; nothing else changes because the write path and the
/// entry shape are already the universal ones.
/// </para>
/// <para>
/// <b>ADR-039 (no second routing surface)</b>: the interim Binding makes NO routing decision.
/// Which playbook runs was already decided upstream by the tenant-visible
/// <c>invoke_playbook(playbookId)</c> dispatch; the disposition is
/// <see cref="BindingDisposition.Informational"/> — the platform default AND the engine
/// flow's actual behavior today (results render in the Assistant pane). No config key, no
/// code list, no intent mechanism is introduced.
/// </para>
/// <para>
/// <b>Session scope (documented decision, task 024)</b>: ledger entries are session-scoped,
/// so the adapter covers session-attached (chat-invoked) engine runs only. Record/widget
/// context engine runs (<c>PlaybookRunEndpoints</c>, insights ask/assistant endpoints,
/// scheduler, app-only ingest) have no <see cref="ChatSession"/> and join the ledger at P3
/// FR-P3-08 (work-product persistence). When the chat session cannot be found (expired /
/// GDPR-erased mid-turn), the adapter degrades gracefully — identifiers-only Warning, null
/// return — rather than inventing a session store.
/// </para>
/// <para>
/// <b>ADR-040 store-precedes-render</b>: <see cref="Handlers.InvokePlaybookHandler"/> calls
/// <see cref="RecordAsync"/> AFTER the engine run completes and BEFORE the
/// <see cref="ToolResult"/> is returned to the LLM (the LLM's assistant turn is the render).
/// A ledger-write failure propagates and fails the tool call — unstored output is never
/// rendered.
/// </para>
/// <para>
/// <b>Interface rationale (ADR-010 tension, documented)</b>: mirrors
/// <see cref="IOutputRouter"/> — this is a genuine module boundary: the chat handler mocks it
/// per ADR-038, and P3 FR-P3-08 adds the record-context leg as a second caller while task 040
/// swaps the Binding source. Not interface-for-testability-alone.
/// </para>
/// <para>
/// <b>NFR-07 / ADR-015</b>: this type logs identifiers only (tenant, session, playbook, run,
/// ledger key, citation COUNT) — never <see cref="PlaybookInvocationResult.TextContent"/> or
/// structured-data content. The ledger entry itself is ADR-015 Tier 3 session data (erased
/// with the session).
/// </para>
/// </remarks>
public interface IEngineOutputLedgerAdapter
{
    /// <summary>
    /// Writes a successful frozen-engine composite run's output to the session ledger as an
    /// addressable <see cref="SessionOutput"/> (keyed <c>{playbookId}@t{n}</c> under the
    /// interim E-2 Binding identity) via <see cref="IOutputRouter.RouteAsync"/>.
    /// </summary>
    /// <param name="tenantId">Tenant the chat session belongs to.</param>
    /// <param name="chatSessionId">
    /// Chat session id (as carried on <c>ChatInvocationContext.ChatSessionId</c>).
    /// </param>
    /// <param name="playbookId">The invoked playbook — the frozen flow's identity.</param>
    /// <param name="result">
    /// The aggregated engine output. MUST be a successful result — failed runs produce no
    /// ledger output (throws <see cref="ArgumentException"/>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The stored, addressable ledger entry; <c>null</c> when the session is not
    /// session-store-resolvable (record-context runs join the ledger at P3 FR-P3-08).
    /// </returns>
    Task<SessionOutput?> RecordAsync(
        string tenantId,
        Guid chatSessionId,
        Guid playbookId,
        PlaybookInvocationResult result,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IEngineOutputLedgerAdapter"/>. See interface remarks for the full E-2
/// contract (attach point, interim Binding identity, session scope, ADR-039/040 posture).
/// </summary>
public sealed class EngineOutputLedgerAdapter : IEngineOutputLedgerAdapter
{
    /// <summary>
    /// Interim consumer-type marker for engine-origin ledger entries (lands on
    /// <see cref="SessionOutput.UcId"/> via the router's legacy-row fallback). Replaced by
    /// the real insights Binding rows at P3 FR-P3-01 / task 040. Deliberately NOT added to
    /// <see cref="ConsumerTypes"/>: no <c>sprk_playbookconsumer</c> row exists for it yet and
    /// the FR-P0-04 boot reconciliation requires constants ↔ rows parity.
    /// </summary>
    internal const string InterimConsumerType = "engine-playbook";

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ChatSessionManager _sessionManager;
    private readonly IOutputRouter _outputRouter;
    private readonly ILogger<EngineOutputLedgerAdapter> _logger;

    public EngineOutputLedgerAdapter(
        ChatSessionManager sessionManager,
        IOutputRouter outputRouter,
        ILogger<EngineOutputLedgerAdapter> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _outputRouter = outputRouter ?? throw new ArgumentNullException(nameof(outputRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SessionOutput?> RecordAsync(
        string tenantId,
        Guid chatSessionId,
        Guid playbookId,
        PlaybookInvocationResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(result);
        if (playbookId == Guid.Empty)
        {
            throw new ArgumentException("playbookId must be a non-empty GUID.", nameof(playbookId));
        }
        if (!result.Success)
        {
            // Contract anchor: failed engine runs never enter the ledger — the ledger carries
            // capability OUTPUTS (ADR-040), not failure diagnostics.
            throw new ArgumentException(
                "Only successful engine runs are recorded to the session ledger. " +
                "Callers must check PlaybookInvocationResult.Success before calling RecordAsync.",
                nameof(result));
        }

        var session = await ResolveSessionAsync(tenantId, chatSessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            // Session-scope boundary (task-024 decision): no ChatSession → no session ledger.
            // Record-context engine runs join the ledger at P3 FR-P3-08. Identifiers only (NFR-07).
            _logger.LogWarning(
                "EngineOutputLedgerAdapter: chat session not found — engine output NOT recorded to the ledger. " +
                "tenant={TenantId} session={ChatSessionId} playbookId={PlaybookId} runId={RunId}. " +
                "Record-context runs join the ledger at P3 FR-P3-08.",
                tenantId, chatSessionId, playbookId, result.RunId);
            return null;
        }

        // Composite output shape → ledger payload (the aggregated boundary shape — the adapter
        // consumes the facade's projection of the ADR-037 section-keyed stream; it does not
        // touch the streaming contract).
        var payload = JsonSerializer.SerializeToElement(
            new EngineOutputLedgerPayload
            {
                RunId = result.RunId,
                PlaybookId = playbookId,
                TextContent = result.TextContent,
                StructuredData = result.StructuredData,
                CitationCount = result.Citations.Count,
                Confidence = result.Confidence,
            },
            PayloadSerializerOptions);

        // sourceRefs: identifiers ONLY (NFR-07) — citation chunk ids, never excerpts.
        var sourceRefs = result.Citations
            .Select(c => c.ChunkId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Interim E-2 Binding identity (see class remarks): playbookId is the key identity,
        // Informational is the engine flow's actual rendering behavior. Ucid stays null so the
        // router's legacy-row fallback stamps UcId = InterimConsumerType.
        var binding = new Binding
        {
            BindingId = playbookId,
            ConsumerType = InterimConsumerType,
            Disposition = BindingDisposition.Informational,
        };

        // STORE through the task-021 universal write path (write-before-render; append-only).
        var routed = await _outputRouter
            .RouteAsync(session, binding, payload, sourceRefs.Count > 0 ? sourceRefs : null, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "EngineOutputLedgerAdapter: engine output recorded key={Key} playbookId={PlaybookId} runId={RunId} " +
            "citationCount={CitationCount} tenant={TenantId} session={SessionId}",
            routed.Entry.Key, playbookId, result.RunId, sourceRefs.Count, tenantId, session.SessionId);

        return routed.Entry;
    }

    /// <summary>
    /// Resolves the <see cref="ChatSession"/> for the Guid-typed
    /// <c>ChatInvocationContext.ChatSessionId</c>. <see cref="ChatSessionManager"/> creates
    /// session ids in "N" format (<c>Guid.NewGuid().ToString("N")</c>), so "N" is tried
    /// first; "D" covers sessions minted by other paths. <see cref="Guid.Empty"/> (the
    /// factory's unparseable-session-id sentinel) short-circuits to null.
    /// </summary>
    private async Task<ChatSession?> ResolveSessionAsync(
        string tenantId,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        if (chatSessionId == Guid.Empty)
        {
            return null;
        }

        return await _sessionManager.GetSessionAsync(tenantId, chatSessionId.ToString("N"), cancellationToken).ConfigureAwait(false)
            ?? await _sessionManager.GetSessionAsync(tenantId, chatSessionId.ToString("D"), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Ledger payload shape for engine-origin <see cref="SessionOutput"/> entries — the frozen
/// engine's aggregated composite output plus the run/playbook identity needed for later-turn
/// referencing. ADR-015 Tier 3 (rides the session ledger; erased with the session).
/// </summary>
internal sealed record EngineOutputLedgerPayload
{
    /// <summary>Deterministic run id assigned by the orchestration layer.</summary>
    [JsonPropertyName("runId")]
    public required Guid RunId { get; init; }

    /// <summary>The invoked playbook — the frozen flow's identity (also the interim ledger key prefix).</summary>
    [JsonPropertyName("playbookId")]
    public required Guid PlaybookId { get; init; }

    /// <summary>Aggregated text from terminal output nodes. Omitted when null.</summary>
    [JsonPropertyName("textContent")]
    public string? TextContent { get; init; }

    /// <summary>Aggregated structured data from terminal output nodes. Omitted when null.</summary>
    [JsonPropertyName("structuredData")]
    public JsonElement? StructuredData { get; init; }

    /// <summary>Count of citation envelopes across the run (ids live in <see cref="SessionOutput.SourceRefs"/>).</summary>
    [JsonPropertyName("citationCount")]
    public int CitationCount { get; init; }

    /// <summary>Run-level confidence when the playbook reports one. Omitted when null.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}
