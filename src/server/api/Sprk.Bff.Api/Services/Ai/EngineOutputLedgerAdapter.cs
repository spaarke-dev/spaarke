using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// E-2 engine-output→ledger adapter (ADR-040 / FR-P1-05, spaarke-ai-architecture-redesign-r1
/// task 024): converts the FROZEN engine's composite outputs (surfaced at the chat boundary
/// as an aggregated <see cref="EngineRunOutput"/>) into addressable
/// <see cref="SessionOutput"/> ledger entries via the task-021 universal write path
/// (<see cref="IOutputRouter.RouteAsync"/>). Without this shim, engine runs would be the ONE
/// output class invisible to the ledger, breaking session composition ("draft a letter based
/// on that matter summary") for exactly the highest-value composite outputs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Attach point (freeze-line + P3-survival analysis, task 024; re-homed by FR-P3-05
/// task 044)</b>: the only chat-session-attached path into the frozen engine is
/// <c>SprkChatAgent → analysis.rerun → AnalysisExecutionHandler.ExecuteChatAsync →
/// IAnalysisOrchestrationService.ExecutePlaybookAsync</c> (the task-044 deletions closed
/// the former generic playbook-dispatch leg). The adapter therefore attaches in
/// <see cref="Handlers.AnalysisExecutionHandler"/> — a component the OVERLAY-MATRIX S4
/// verdict marks 🔧 fills (tool framework + handlers), NOT inside
/// <c>PlaybookOrchestrationService</c> or its nodes (the frozen surface — ADR-039:
/// no new capability lands on the frozen engine; this adapter is a boundary shim on OUR side
/// of the freeze line). P3 FR-P3-08 adds the record-context leg as a second caller.
/// </para>
/// <para>
/// <b>Binding identity (FR-P3-01, task 040 — re-pointed)</b>: the adapter reverse-resolves
/// the REAL <c>sprk_playbookconsumer</c> row for the invoked playbook via
/// <see cref="IConsumerRoutingService.GetBindingByPlaybookIdAsync"/> (the P3 insights
/// Binding rows exist now), so engine-origin ledger entries key on the resolved row's
/// <c>{bindingId}@t{n}</c> and carry its catalog identity (consumer type, ucid,
/// disposition). The task-024 INTERIM identity (<see cref="Binding.BindingId"/> = the
/// invoked PLAYBOOK id, <see cref="Binding.ConsumerType"/> =
/// <see cref="InterimConsumerType"/> stamped onto <see cref="SessionOutput.UcId"/> via the
/// router's legacy-row fallback) remains ONLY as the documented degrade path for playbooks
/// no Binding row targets — a session's analysis playbook may be ANY tenant playbook,
/// and an unregistered composite's output must still land in the ledger
/// (ADR-040: store-precedes-render has no exception for catalog gaps). This is not a compat
/// shim: no config key, no code list, no routing decision — the reverse lookup only recovers
/// the catalog identity of a dispatch decided upstream.
/// </para>
/// <para>
/// <b>ADR-039 (no second routing surface)</b>: the interim Binding makes NO routing decision.
/// Which playbook runs was already decided upstream (the session's playbook context on the
/// <c>analysis.rerun</c> leg); the disposition is
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
/// <b>ADR-040 store-precedes-render</b>: <see cref="Handlers.AnalysisExecutionHandler"/>
/// calls <see cref="RecordAsync"/> AFTER the engine run completes and BEFORE the
/// <c>document_replace</c> SSE render / the <see cref="ToolResult"/> returned to the LLM
/// (the LLM's assistant turn is the render). A ledger-write failure propagates and fails
/// the tool call — unstored output is never rendered.
/// </para>
/// <para>
/// <b>Interface rationale (ADR-010 tension, documented)</b>: mirrors
/// <see cref="IOutputRouter"/> — this is a genuine module boundary: the chat handler mocks it
/// per ADR-038, and P3 FR-P3-08 adds the record-context leg as a second caller while task 040
/// swaps the Binding source. Not interface-for-testability-alone.
/// </para>
/// <para>
/// <b>NFR-07 / ADR-015</b>: this type logs identifiers only (tenant, session, playbook, run,
/// ledger key, citation COUNT) — never <see cref="EngineRunOutput.TextContent"/> or
/// structured-data content. The ledger entry itself is ADR-015 Tier 3 session data (erased
/// with the session).
/// </para>
/// </remarks>
public interface IEngineOutputLedgerAdapter
{
    /// <summary>
    /// Writes a successful frozen-engine composite run's output to the session ledger as an
    /// addressable <see cref="SessionOutput"/> via <see cref="IOutputRouter.RouteAsync"/> —
    /// keyed <c>{bindingId}@t{n}</c> under the reverse-resolved Binding row (FR-P3-01), or
    /// <c>{playbookId}@t{n}</c> under the interim E-2 identity when no row targets the playbook.
    /// </summary>
    /// <param name="tenantId">Tenant the chat session belongs to.</param>
    /// <param name="chatSessionId">
    /// Chat session id (as carried on <c>ChatInvocationContext.ChatSessionId</c>).
    /// </param>
    /// <param name="playbookId">The invoked playbook — the frozen flow's identity.</param>
    /// <param name="output">
    /// The aggregated SUCCESSFUL engine output (callers record successful runs only —
    /// failed runs never enter the ledger per ADR-040; the ledger carries capability
    /// OUTPUTS, not failure diagnostics).
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
        EngineRunOutput output,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated successful frozen-engine run output consumed by
/// <see cref="IEngineOutputLedgerAdapter.RecordAsync"/>. Decoupled from any facade DTO
/// (FR-P3-05 task 044 — the former facade result type died with the deleted generic
/// playbook-dispatch leg).
/// </summary>
public sealed record EngineRunOutput
{
    /// <summary>Deterministic run identifier assigned by the orchestration layer (the
    /// persisted analysis id on the <c>analysis.rerun</c> leg).</summary>
    public required Guid RunId { get; init; }

    /// <summary>Aggregated text from terminal output nodes. Null when no text emitted.</summary>
    public string? TextContent { get; init; }

    /// <summary>Aggregated structured data from terminal output nodes. Null when none emitted.</summary>
    public JsonElement? StructuredData { get; init; }

    /// <summary>Citation chunk ids accumulated across the run (identifiers only, NFR-07).</summary>
    public IReadOnlyList<string> CitationChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>Run-level confidence when the playbook reports one.</summary>
    public double? Confidence { get; init; }
}

/// <summary>
/// Default <see cref="IEngineOutputLedgerAdapter"/>. See interface remarks for the full E-2
/// contract (attach point, interim Binding identity, session scope, ADR-039/040 posture).
/// </summary>
public sealed class EngineOutputLedgerAdapter : IEngineOutputLedgerAdapter
{
    /// <summary>
    /// Interim consumer-type marker for engine-origin ledger entries whose playbook has NO
    /// <c>sprk_playbookconsumer</c> row (lands on <see cref="SessionOutput.UcId"/> via the
    /// router's legacy-row fallback). Since FR-P3-01 (task 040) this is the DEGRADE path
    /// only — registered playbooks (e.g. the insights-ask rows) resolve their real Binding
    /// identity via <see cref="IConsumerRoutingService.GetBindingByPlaybookIdAsync"/>.
    /// Deliberately NOT added to <see cref="ConsumerTypes"/>: no row exists for it by
    /// definition and the FR-P0-04 boot reconciliation requires constants ↔ rows parity.
    /// </summary>
    internal const string InterimConsumerType = "engine-playbook";

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ChatSessionManager _sessionManager;
    private readonly IOutputRouter _outputRouter;
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly ILogger<EngineOutputLedgerAdapter> _logger;

    public EngineOutputLedgerAdapter(
        ChatSessionManager sessionManager,
        IOutputRouter outputRouter,
        IConsumerRoutingService consumerRouting,
        ILogger<EngineOutputLedgerAdapter> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _outputRouter = outputRouter ?? throw new ArgumentNullException(nameof(outputRouter));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SessionOutput?> RecordAsync(
        string tenantId,
        Guid chatSessionId,
        Guid playbookId,
        EngineRunOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(output);
        if (playbookId == Guid.Empty)
        {
            throw new ArgumentException("playbookId must be a non-empty GUID.", nameof(playbookId));
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
                tenantId, chatSessionId, playbookId, output.RunId);
            return null;
        }

        // Composite output shape → ledger payload (the aggregated boundary shape — the adapter
        // consumes the caller's aggregation of the engine stream; it does not touch the
        // streaming contract).
        var payload = JsonSerializer.SerializeToElement(
            new EngineOutputLedgerPayload
            {
                RunId = output.RunId,
                PlaybookId = playbookId,
                TextContent = output.TextContent,
                StructuredData = output.StructuredData,
                CitationCount = output.CitationChunkIds.Count,
                Confidence = output.Confidence,
            },
            PayloadSerializerOptions);

        // sourceRefs: identifiers ONLY (NFR-07) — citation chunk ids, never excerpts.
        var sourceRefs = output.CitationChunkIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // FR-P3-01 (task 040) re-point: reverse-resolve the REAL Binding row for the invoked
        // playbook. Registered playbooks (the P3 insights-ask rows et al.) key their ledger
        // entries on the resolved row's BindingId and carry its catalog identity (consumer
        // type, ucid, disposition). Cached 5 min; a routing failure resolves null (the
        // routing service never throws to consumers).
        var binding = await _consumerRouting
            .GetBindingByPlaybookIdAsync(playbookId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (binding is null)
        {
            // Degrade path (see class remarks): a playbook no Binding row targets — keep the
            // task-024 interim identity so the output still lands in the ledger (ADR-040).
            // playbookId is the key identity, Informational is the engine flow's actual
            // rendering behavior. Ucid stays null so the router's legacy-row fallback stamps
            // UcId = InterimConsumerType.
            _logger.LogDebug(
                "EngineOutputLedgerAdapter: no Binding row targets playbook {PlaybookId} — " +
                "recording under the interim E-2 identity.",
                playbookId);
            binding = new Binding
            {
                BindingId = playbookId,
                ConsumerType = InterimConsumerType,
                Disposition = BindingDisposition.Informational,
            };
        }

        // STORE through the task-021 universal write path (write-before-render; append-only).
        var routed = await _outputRouter
            .RouteAsync(session, binding, payload, sourceRefs.Count > 0 ? sourceRefs : null, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "EngineOutputLedgerAdapter: engine output recorded key={Key} playbookId={PlaybookId} " +
            "bindingConsumerType={BindingConsumerType} runId={RunId} " +
            "citationCount={CitationCount} tenant={TenantId} session={SessionId}",
            routed.Entry.Key, playbookId, binding.ConsumerType, output.RunId, sourceRefs.Count,
            tenantId, session.SessionId);

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
