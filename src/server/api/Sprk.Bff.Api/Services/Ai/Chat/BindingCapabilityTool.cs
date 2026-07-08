using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Projects one catalog Binding row (<c>sprk_playbookconsumer</c>) into the
/// agent-turn loop's tool list as a capability tool (FR-P2-01 /
/// ADR-039 loop-as-dispatcher). The function's name is derived deterministically
/// from the Binding's consumer type; its description is the maker-authored
/// <see cref="Binding.ToolDescription"/> (the §6.2 intent surface); its parameter
/// schema is the target Action's <see cref="Binding.InputSchemaJson"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE dispatch protocol (ADR-039)</b>: invocation delegates to the SAME
/// executor stack as the Click path — <see cref="SessionDispatchOrchestrator"/>
/// resolves the Binding BY ID and executes via ActionRunner with the universal
/// ledger write (<see cref="IOutputRouter"/>) BEFORE the terminal chunk
/// (ADR-040 storage-precedes-rendering). The loop choosing this tool IS the
/// text-path dispatch decision; no second intent mechanism exists here — the
/// model picks from the closed, projected catalog and function-calling
/// validation rejects anything unlisted.
/// </para>
/// <para>
/// <b>Catalog-column gating (ADR-039)</b>: which Bindings project is decided by
/// catalog columns only — <c>sprk_tooldescription</c> non-empty (the maker's
/// explicit opt-in to text-path invocation) and <c>sprk_surfaces</c> ∋ the
/// session's surface (applied by <see cref="AgentToolProjection"/>). No
/// tool-name lists anywhere.
/// </para>
/// <para>
/// <b>Scope discipline (ADR-010)</b>: the tool captures the ROOT
/// <see cref="IServiceProvider"/> and opens a fresh scope per invocation —
/// the factory's creation scope is disposed long before the LLM invokes tools.
/// </para>
/// <para>
/// <b>Gate seam note (W-P2-B integration)</b>: side-effecting capabilities are
/// not executed here ungated — the P1/P2 dispatch envelope rejects non-prompted
/// kinds / non-informational dispositions pre-run, and FR-P2-02 (task 031)
/// generalizes the pending store so write-shaped dispatch suspends through the
/// ONE confirmation gate keyed on <c>side_effect_class</c>. This tool codes
/// against the orchestrator seam; the gate slots in behind it without changing
/// this projection.
/// </para>
/// </remarks>
public sealed class BindingCapabilityTool : AIFunction
{
    /// <summary>Deterministic function-name prefix for projected capability tools.</summary>
    public const string NamePrefix = "capability_";

    /// <summary>Cap on the capability output text handed back to the model as the tool result.</summary>
    internal const int MaxResultChars = 8_000;

    private static readonly JsonElement DefaultSchema = JsonDocument.Parse(
        """
        {"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"},"description":"Optional subset of session file ids the capability should operate on. Omit to use all session files."}},"additionalProperties":true}
        """).RootElement.Clone();

    private readonly Binding _binding;
    private readonly IServiceProvider _rootServices;
    private readonly string _tenantId;
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? _sseWriter;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _schema;

    /// <param name="binding">The projected catalog Binding row.</param>
    /// <param name="rootServices">ROOT service provider (fresh scope per invocation — ADR-010).</param>
    /// <param name="tenantId">Tenant id (ADR-014).</param>
    /// <param name="sessionId">Chat session id.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="sseWriter">
    /// Optional chat-stream SSE writer (FR-P2-03): carries the <c>elicitation_modal</c>
    /// wizard-routing event when the Binding declares <c>capture_mode: modal</c> and
    /// required args are missing. Null on surfaces without a chat SSE stream — modal
    /// capture then degrades to loop elicitation (logged). Does not perturb the
    /// projected name/description/schema (NFR-04 cache stability).
    /// </param>
    public BindingCapabilityTool(
        Binding binding,
        IServiceProvider rootServices,
        string tenantId,
        string sessionId,
        ILogger logger,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null)
    {
        _sseWriter = sseWriter;
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new ArgumentException("tenantId required", nameof(tenantId));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : throw new ArgumentException("sessionId required", nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(binding.ToolDescription))
        {
            throw new ArgumentException(
                $"Binding {binding.BindingId} has no sprk_tooldescription — not text-projectable (ADR-039 catalog opt-in).",
                nameof(binding));
        }

        _name = BuildFunctionName(binding.ConsumerType);
        _description = binding.ToolDescription!;
        _schema = ParseSchema(binding.InputSchemaJson);
    }

    /// <summary>The projected Binding (exposed for pre-filter + tests).</summary>
    public Binding Binding => _binding;

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// Deterministic capability-tool name for a consumer type:
    /// <c>capability_{consumerType}</c> with non-identifier characters mapped to
    /// <c>_</c> (LLM function-name charset: <c>[a-zA-Z0-9_-]</c>). Pure — same
    /// input always yields the same name (NFR-04 cache stability).
    /// </summary>
    public static string BuildFunctionName(string consumerType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerType);
        var sanitized = new string(consumerType
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray());
        return NamePrefix + sanitized;
    }

    private static JsonElement ParseSchema(string? inputSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            return DefaultSchema;
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return DefaultSchema;
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Maker-authored schema is malformed — degrade to the permissive default
            // (routing/projection never throws on maker data; same tolerance as
            // ConsumerRoutingService JSON parsing).
            return DefaultSchema;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Fresh scope per invocation: the orchestrator + its dependencies are Scoped,
        // and the agent-creation scope is long gone by the time the LLM calls tools.
        await using var scope = _rootServices.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetService<SessionDispatchOrchestrator>();
        if (orchestrator is null)
        {
            return "This capability is not available in the current environment (dispatch service disabled).";
        }

        // ── FR-P2-03 loop-native elicitation ─────────────────────────────────────
        // Validate the model's arguments against the Binding's DECLARED input schema
        // BEFORE dispatch. Missing required args suspend the invocation into the ONE
        // pending store (ledger Gate marker first — ADR-040) and yield a clarifying
        // turn (or the wizard escape when capture_mode: modal) instead of executing
        // with absent/guessed values. This elicitation-triggering call still consumed
        // one budget unit and is recorded on the ToolChain (NFR-09: elicitation turns
        // count within the per-turn tool budget).
        var missing = BindingInputSchemaValidator.FindMissingRequired(_binding.InputSchemaJson, arguments);
        if (missing.Count > 0)
        {
            return await SuspendForElicitationAsync(
                scope.ServiceProvider, missing, arguments, cancellationToken).ConfigureAwait(false);
        }
        // ── End FR-P2-03 validation ──────────────────────────────────────────────

        JsonElement? args = null;
        if (arguments is { Count: > 0 })
        {
            args = JsonSerializer.SerializeToElement(
                arguments.ToDictionary(a => a.Key, a => a.Value));
        }

        var request = new SessionDispatchRequest(_tenantId, _sessionId, _binding.BindingId, args);

        try
        {
            string? summary = null;
            string? error = null;
            await foreach (var chunk in orchestrator.DispatchAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (!chunk.Done)
                {
                    continue;
                }

                error = chunk.Error;
                summary = chunk.Summary ?? chunk.Content;
            }

            if (error is not null)
            {
                _logger.LogWarning(
                    "[agent-turn.capability] dispatch error — binding={BindingId} consumerType={ConsumerType} session={SessionId}",
                    _binding.BindingId, _binding.ConsumerType, _sessionId);
                return $"The '{_binding.ConsumerType}' capability failed: {error}. Tell the user honestly; do not fabricate its output.";
            }

            var text = summary ?? string.Empty;
            if (text.Length > MaxResultChars)
            {
                text = text[..MaxResultChars] + "…[truncated]";
            }

            // The capability output was ledger-written by the dispatch stack BEFORE this
            // return (ADR-040) — the model composes over an already-grounded output.
            //
            // G-P3 UAT round-2 R2-B (2026-07-07): the previous framing ("Capability
            // 'create-task' completed.") read as an EXECUTED side effect to the model —
            // every fabricated "has now been created" turn in the round-2 transcript
            // correlated with a capability_create-task DRAFTING call (App Insights
            // 14:50/15:02/15:05/15:07Z, session 5329…). Capability tools GENERATE
            // content only; the result text now states that explicitly so the model
            // cannot conflate generation success with record creation.
            //
            // G-P3 UAT round-3 R3-1 (2026-07-07): after the round-2 reframe the model
            // RE-INVOKED this drafting capability on every user confirmation (4× in
            // App Insights 16:40:34–16:41:36Z) and never invoked the write tool —
            // the result text now closes that loop explicitly: user already confirmed
            // → invoke the write tool NOW; never re-draft; never re-ask in chat.
            return $"Capability '{_binding.ConsumerType}' finished GENERATING its output " +
                   "(a draft stored to the session ledger — shown below). This tool call did NOT create, " +
                   "save, send, or modify any record, task, email, or tab. If the user asked to create/save/" +
                   "send this content, you must still invoke the corresponding write tool (it will ask the " +
                   "user to confirm before executing). If the user has ALREADY confirmed, invoke the write " +
                   "tool NOW — do NOT invoke this capability again for the same request, and do NOT ask the " +
                   $"user to confirm again in chat: the write tool's confirmation dialog IS the approval step.\nOutput:\n{text}";
        }
        catch (DispatchRejectedException ex)
        {
            // Clean catalog-boundary refusal (unsupported kind/disposition at this phase,
            // disabled row) — relay honestly, never fabricate (ADR-039 grounded outputs).
            _logger.LogInformation(
                "[agent-turn.capability] dispatch rejected — binding={BindingId} errorCode={ErrorCode}",
                _binding.BindingId, ex.ErrorCode);
            return $"The '{_binding.ConsumerType}' capability cannot run right now ({ex.ErrorCode}). Tell the user honestly and suggest an alternative.";
        }
    }

    /// <summary>
    /// FR-P2-03: suspends a missing-required-args invocation — pending Gate marker to the
    /// ledger FIRST (ADR-040 storage-precedes-rendering; via the unified store), resumable
    /// partial-args payload second, THEN the grounded turn instruction (loop clarify or
    /// modal escape). A re-suspension mid-elicitation (partial answer) reuses the pending
    /// gate id: the fresh pending marker carries the REMAINING fields (append-only
    /// correlation) and the payload overwrite merges what the model re-supplied.
    /// </summary>
    private async Task<object?> SuspendForElicitationAsync(
        IServiceProvider services,
        IReadOnlyList<ElicitationField> missing,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var pendingManager = services.GetService<PendingPlanManager>();
        var sessionManager = services.GetService<ChatSessionManager>();
        if (pendingManager is null || sessionManager is null)
        {
            // Degraded environment (no unified store in this DI graph). Still refuse to
            // execute with missing args — the clarifying turn stays grounded; only the
            // cross-turn marker is unavailable. Loud log so the gap is visible.
            _logger.LogWarning(
                "[agent-turn.elicitation] pending store unavailable — clarifying without ledger marker. " +
                "binding={BindingId} session={SessionId}",
                _binding.BindingId, _sessionId);
            return ElicitationTurnRouter.BuildClarifyInstruction(_binding.ConsumerType, Name, missing);
        }

        // Reuse the gate id of an already-pending elicitation for this Binding (the
        // partial-answer walk stays ONE logical invocation across turns).
        var session = await sessionManager.GetSessionAsync(_tenantId, _sessionId, cancellationToken).ConfigureAwait(false);
        var existing = PendingPlanManager.FindPendingGate(
            session?.Gates, PendingPlanManager.GateKindElicitation, _binding.BindingId.ToString());
        var gateId = existing?.GateId ?? $"elicitation-{Guid.NewGuid():N}";

        var providedArgsJson = arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(arguments.ToDictionary(a => a.Key, a => a.Value))
            : "{}";

        var suspended = await pendingManager.SuspendInvocationAsync(
            new PendingInvocation
            {
                GateId = gateId,
                Kind = PendingPlanManager.GateKindElicitation,
                SessionId = _sessionId,
                TenantId = _tenantId,
                ToolId = Name,
                BindingId = _binding.BindingId.ToString(),
                MissingFields = missing.Select(f => f.Name).ToList(),
                Risk = MapRisk(_binding.Risk),
                ArgsJson = providedArgsJson,
                Title = _binding.ConsumerType,
                Turn = existing?.Turn ?? 0,
            },
            cancellationToken).ConfigureAwait(false);

        var wantsModal = _binding.CaptureMode == BindingCaptureMode.Modal;
        if (wantsModal && _sseWriter is not null)
        {
            // Marker already written above — the wizard-routing event renders AFTER storage
            // (ADR-040). The client dispatch helper (task 023 dispatchConsumer) closes the
            // loop: wizard completion POSTs the completed args by Binding id; the dispatch
            // seam resolves this gate (PendingPlanManager.ResolveElicitationOnDispatchAsync).
            await _sseWriter(
                new Api.Ai.ChatSseEvent(
                    "elicitation_modal",
                    null,
                    new Api.Ai.ChatSseElicitationModalData(
                        GateId: suspended.GateId,
                        BindingId: _binding.BindingId.ToString(),
                        ConsumerType: _binding.ConsumerType,
                        Title: _binding.ToolDescription,
                        MissingFields: missing
                            .Select(f => new Api.Ai.ChatSseElicitationFieldData(f.Name, f.Prompt, f.Type))
                            .ToArray(),
                        ProvidedArgs: arguments is { Count: > 0 }
                            ? JsonSerializer.SerializeToElement(arguments.ToDictionary(a => a.Key, a => a.Value))
                            : null)),
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[agent-turn.elicitation] modal escape — gateId={GateId} binding={BindingId} " +
                "missingFieldCount={MissingFieldCount} session={SessionId}",
                suspended.GateId, _binding.BindingId, missing.Count, _sessionId);

            return ElicitationTurnRouter.BuildModalNotice(_binding.ConsumerType, missing);
        }

        if (wantsModal)
        {
            _logger.LogWarning(
                "[agent-turn.elicitation] capture_mode=modal but no SSE surface on this invocation — " +
                "degrading to loop elicitation. binding={BindingId} session={SessionId}",
                _binding.BindingId, _sessionId);
        }

        _logger.LogInformation(
            "[agent-turn.elicitation] suspended — gateId={GateId} binding={BindingId} " +
            "missingFieldCount={MissingFieldCount} captureMode={CaptureMode} session={SessionId}",
            suspended.GateId, _binding.BindingId, missing.Count, _binding.CaptureMode, _sessionId);

        return ElicitationTurnRouter.BuildClarifyInstruction(_binding.ConsumerType, Name, missing);
    }

    /// <summary>Ledger/audit vocabulary for the Binding risk posture (mirrors sprk_risk labels).</summary>
    private static string MapRisk(BindingRisk risk) => risk switch
    {
        BindingRisk.AlwaysConfirm => "always-confirm",
        BindingRisk.ConfirmWhenUncertain => "confirm-when-uncertain",
        _ => "none",
    };
}
