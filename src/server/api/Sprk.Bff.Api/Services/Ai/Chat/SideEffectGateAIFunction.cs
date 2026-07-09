using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// The ONE Confirmation Gate at the agent-turn loop's tool-invocation boundary
/// (FR-P2-02 / D12; landed by FR-P2-08 task 037 after the eval suite's
/// prompt-injection family exposed the gap). Wraps a typed-handler tool
/// (<see cref="ToolHandlerToAIFunctionAdapter"/>) whose <c>sprk_analysistool</c>
/// row DECLARES a side-effecting class (<c>write</c> / <c>communicate</c> per
/// <see cref="PendingPlanManager.RequiresConfirmation(ToolSideEffectClass?, Sprk.Bff.Api.Services.Ai.PublicContracts.BindingRisk, bool)"/>)
/// and, when the LLM invokes it, SUSPENDS the invocation into the unified pending
/// store instead of executing — the inner tool NEVER runs without explicit user
/// confirmation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this decorator exists (§11 Component Justification)</b>:
/// (1) <i>Existing</i> — <see cref="BudgetedAIFunction"/> is the loop's budget/audit
/// decorator and knows nothing of catalog rows; <see cref="BindingCapabilityTool"/>
/// suspends only Binding-shaped elicitations. Neither gates typed-handler side
/// effects: after task 034 deleted the interim pre-pass gate, no production code
/// called <see cref="PendingPlanManager.RequiresConfirmation(ToolSideEffectClass?, Sprk.Bff.Api.Services.Ai.PublicContracts.BindingRisk, bool)"/>.
/// (2) <i>Extension</i> — fusing gating into <see cref="BudgetedAIFunction"/> would
/// entangle two orthogonal loop-contract clauses and force row metadata into
/// <see cref="AgentToolProjection.Finalize"/>, which deliberately sees only
/// <see cref="AIFunction"/>s; putting it inside the adapter would give the
/// schema/context bridge a store dependency + root-provider lifetime it does not have.
/// (3) <i>Cost of doing nothing</i> — <c>dataverse.create_record</c> /
/// <c>update_record</c> / <c>delete_record</c> (and <c>analysis.rerun</c>, until the
/// 2026-07-06 operator ruling re-declared it read) execute
/// UNGATED from the loop, so adversarial instructions embedded in uploaded-document
/// text or tool results can trigger real Dataverse writes without user confirmation
/// (NFR-03 violation; eval cases GU-051/GU-052 fail without this class).
/// </para>
/// <para>
/// <b>ADR-039</b>: the gate decision keys EXCLUSIVELY on the row's declared
/// <c>sprk_sideeffectclass</c> — never tool-name lists. Wrap-site selection in
/// <see cref="SprkChatAgentFactory"/> applies
/// <see cref="PendingPlanManager.RequiresConfirmation(ToolSideEffectClass?, Sprk.Bff.Api.Services.Ai.PublicContracts.BindingRisk, bool)"/>
/// over catalog metadata only.
/// </para>
/// <para>
/// <b>ADR-040</b>: suspension goes through
/// <see cref="PendingPlanManager.SuspendInvocationAsync"/> — the pending
/// <c>SessionGate</c> ledger marker lands BEFORE the <c>action_confirmation</c>
/// presentation event renders and before the grounded suspension message returns to
/// the model. The resumable args payload lives ONLY in the Tier-3 store
/// (ADR-015; never logged — NFR-07).
/// </para>
/// <para>
/// <b>Fail-closed (NFR-03 — the gate is the last line)</b>: if the unified store
/// cannot be resolved, or suspension itself fails, the inner tool is NOT executed.
/// A degraded environment yields an honest "cannot execute" message; a suspension
/// failure propagates loudly (same posture as task 032's W4 ruling — a gate whose
/// marker cannot land aborts the invocation rather than silently proceeding).
/// </para>
/// <para>
/// <b>Resume surface</b>: <c>POST /sessions/{sessionId}/gates/{gateId}/resolve</c>
/// (task 032). Reject works end-to-end; Confirm on a non-Binding invocation executes
/// through <see cref="TypedHandlerResumeExecutor"/> (FR-P3-03, task 042 — the seam this
/// class's P2 landing deferred): the suspended tool resolves back to its catalog row +
/// handler and runs under the confirming user's OBO scope, ledger-writing SessionOutput
/// + ToolChain before the result renders. Invocations with no resolvable execution
/// target still close honestly (<c>confirmed-unexecutable</c> + 422
/// <c>gate.no-binding-target</c>).
/// </para>
/// <para>
/// <b>NFR-04</b>: the wrapper preserves the inner function's name / description /
/// schema verbatim, so the projected tool block the model sees is byte-identical to
/// the unwrapped projection (same rule as <see cref="BudgetedAIFunction"/>).
/// </para>
/// <para>
/// <b>Confirmation Policy v2 (FR-A1-03, task 032)</b>: the DELIBERATION of WHETHER (and how)
/// this suspension becomes a dialog / auto-execute-with-Undo / elicit is owned by the
/// deterministic <see cref="Gate.ConfirmationPolicyEngine"/> — it produces a
/// <see cref="Services.Ai.PublicContracts.GateDecisionV2"/> from (catalog-declared risk tier ×
/// deterministically-classified origin × arg completeness) with the precedence-ordered overlays.
/// That engine is NOT a second gate: it reads THIS gate's ADR-040 ledger state and projects the
/// outcome the surface renders. This class remains the fail-closed suspension MECHANISM — its
/// current declared-class suspension is the safe floor while the origin/proposal plumbing the
/// engine consumes is wired at the call sites (tasks 034 / 042); the engine can only ever weaken
/// a gate for a structurally-<c>Explicit</c> origin (Click path today), never for an inferred or
/// injection-suspect one.
/// </para>
/// </remarks>
public sealed class SideEffectGateAIFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly ToolSideEffectClass _declaredClass;
    private readonly IServiceProvider _rootServices;
    private readonly string _tenantId;
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? _sseWriter;

    /// <param name="inner">The wrapped side-effecting tool (typed-handler adapter).</param>
    /// <param name="declaredClass">The row's declared <c>sprk_sideeffectclass</c> that fired the gate.</param>
    /// <param name="rootServices">ROOT service provider — fresh scope per invocation (the
    /// agent-creation scope is disposed long before the LLM invokes tools; same discipline
    /// as <see cref="BindingCapabilityTool"/>).</param>
    /// <param name="tenantId">Tenant id (ADR-014).</param>
    /// <param name="sessionId">Chat session id.</param>
    /// <param name="logger">Logger (identifiers + counts only — NFR-07).</param>
    /// <param name="sseWriter">Optional chat-stream SSE writer: carries the
    /// <c>action_confirmation</c> presentation event (client ActionConfirmationDialog,
    /// task 032 rewire). Null on surfaces without a chat SSE stream — the grounded
    /// suspension message still reaches the user via the model's turn text.</param>
    public SideEffectGateAIFunction(
        AIFunction inner,
        ToolSideEffectClass declaredClass,
        IServiceProvider rootServices,
        string tenantId,
        string sessionId,
        ILogger logger,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _declaredClass = declaredClass;
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new ArgumentException("tenantId required", nameof(tenantId));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : throw new ArgumentException("sessionId required", nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sseWriter = sseWriter;
    }

    /// <summary>The wrapped inner function (exposed for projection fingerprinting + tests).</summary>
    public AIFunction Inner => _inner;

    /// <summary>The declared side-effect class that fired the gate (exposed for tests).</summary>
    public ToolSideEffectClass DeclaredSideEffectClass => _declaredClass;

    /// <inheritdoc />
    public override string Name => _inner.Name;

    /// <inheritdoc />
    public override string Description => _inner.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _inner.JsonSchema;

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Fresh scope per invocation — PendingPlanManager is Scoped and the
        // agent-creation scope is long gone by the time the LLM calls tools.
        await using var scope = _rootServices.CreateAsyncScope();
        var pendingManager = scope.ServiceProvider.GetService<PendingPlanManager>();
        if (pendingManager is null)
        {
            // FAIL CLOSED: no unified store in this DI graph — the side effect must
            // NOT execute without a gate. Honest capability state, never a bypass.
            _logger.LogError(
                "[FR-P2-02][gate] unified pending store unavailable — side-effecting tool " +
                "REFUSED (fail-closed). tool={ToolName} declaredClass={DeclaredClass} session={SessionId}",
                Name, _declaredClass, _sessionId);
            return $"The '{Name}' action cannot run: it performs a side effect (declared class: " +
                   $"{PendingPlanManager.ToLedgerSideEffectClass(_declaredClass)}) and the confirmation " +
                   "gate is unavailable in this environment. Tell the user honestly; do not retry and " +
                   "do not fabricate a result.";
        }

        var gateId = $"confirmation-{Guid.NewGuid():N}";
        var argsJson = arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(arguments.ToDictionary(a => a.Key, a => a.Value))
            : "{}";

        // ADR-040: pending marker + resumable payload land FIRST (SuspendInvocationAsync
        // writes the ledger marker before storing the payload; if the marker cannot land
        // it throws and this invocation aborts loudly — never executes).
        var suspended = await pendingManager.SuspendInvocationAsync(
            new PendingInvocation
            {
                GateId = gateId,
                Kind = PendingPlanManager.GateKindConfirmation,
                SessionId = _sessionId,
                TenantId = _tenantId,
                ToolId = Name,
                SideEffectClass = PendingPlanManager.ToLedgerSideEffectClass(_declaredClass),
                Risk = "none",
                ArgsJson = argsJson,
                Title = Name,
            },
            cancellationToken).ConfigureAwait(false);

        // NFR-07: identifiers + counts only — never argument values.
        _logger.LogInformation(
            "[FR-P2-02][gate] side-effecting invocation suspended — gateId={GateId} tool={ToolName} " +
            "declaredClass={DeclaredClass} argCount={ArgCount} session={SessionId}",
            suspended.GateId, Name, _declaredClass, arguments?.Count ?? 0, _sessionId);

        // Presentation AFTER storage (ADR-040). actionId carries the gate id — the client
        // ActionConfirmationDialog resolves it via POST /gates/{gateId}/resolve (task 032).
        if (_sseWriter is not null)
        {
            await _sseWriter(
                new Api.Ai.ChatSseEvent(
                    "action_confirmation",
                    null,
                    new Api.Ai.ChatSseActionConfirmationData(
                        ActionId: suspended.GateId,
                        ActionName: Name,
                        Summary: BuildUserSummary(arguments),
                        Parameters: new Dictionary<string, string>())),
                cancellationToken).ConfigureAwait(false);
        }

        // Grounded turn instruction: the model must relay the pending state honestly.
        return $"ACTION SUSPENDED FOR USER CONFIRMATION: '{Name}' performs a side effect " +
               $"(declared class: {PendingPlanManager.ToLedgerSideEffectClass(_declaredClass)}) and was " +
               "NOT executed. A confirmation request has been presented to the user. Do NOT call this " +
               "tool again this turn, do NOT assume it succeeded, and do NOT fabricate its result. " +
               "Tell the user the action is awaiting their explicit confirmation.";
    }

    /// <summary>
    /// User-facing one-liner for the confirmation card. Carries the tool name and the
    /// NFR-07-safe argument summary (identifier-shaped values kept, free text redacted) —
    /// the full args payload stays in the Tier-3 store for the resume path.
    /// </summary>
    private string BuildUserSummary(AIFunctionArguments? arguments)
    {
        var argsSummary = AgentTurnContract.SummarizeArguments(arguments);
        return argsSummary is null
            ? $"The assistant requested the side-effecting action '{Name}'. Confirm to proceed or reject to cancel."
            : $"The assistant requested the side-effecting action '{Name}' ({argsSummary}). Confirm to proceed or reject to cancel.";
    }
}
