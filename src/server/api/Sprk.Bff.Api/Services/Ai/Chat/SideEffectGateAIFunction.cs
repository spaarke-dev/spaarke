using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.Gate;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

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
/// <b>Pre-suspend validation (FR-A1-05, task 034)</b>: before suspending, the gate runs the
/// wrapped handler's <c>ValidateChat</c> via
/// <see cref="ToolHandlerToAIFunctionAdapter.ValidateForGate"/>. A call the handler can PROVE
/// is doomed at validation time (e.g. the R5-E <c>sprk_document</c> hard block) renders an
/// honest ❌ + D-F0(d) affordance and is REJECTED to a terminal
/// <see cref="PendingPlanManager.GateStatusValidationFailed"/> ledger marker (store-before-render)
/// WITHOUT ever presenting a confirmation dialog — instead of asking the user to confirm a call
/// that cannot succeed (the R5-E Confirm→❌ dead-end). This only weakens the gate toward HONESTY:
/// a validation PASS (or a faulting/uncertain validator) still suspends and confirms exactly as
/// before, so the gate is NEVER weakened toward execution.
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
    private readonly Func<bool>? _dispatchUncertaintyProbe;

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
    /// <param name="dispatchUncertaintyProbe">
    /// OPTIONAL gate-side ambiguity/low-confidence backstop (spaarke-ai-architecture-redesign-r2
    /// task 044). When supplied and it returns <c>true</c>, the Policy v2 evaluation treats the
    /// invocation as a NON-confident capability selection (origin ⇒ Inferred) AND fires the
    /// dispatch-uncertain overlay, forcing a confirmation dialog even on an otherwise
    /// execute-eligible low-risk request. It is the OPTIONAL backstop ON TOP of the primary
    /// ambiguity decider — the agent turn asking a clarifying question BEFORE any tool call
    /// (layer 1, ADR-039's sanctioned decider). Production wires <c>null</c> today (no live
    /// low-confidence producer reaches this gate; genuine capability ambiguity is diverted upstream
    /// by layer 1), so the overlay honestly stays unfired — the gate HONORS a real signal when one
    /// exists rather than hardcoding it. Threading a real producer (routing candidate-match count /
    /// content-safety fail-open) is the documented follow-on.
    /// </param>
    public SideEffectGateAIFunction(
        AIFunction inner,
        ToolSideEffectClass declaredClass,
        IServiceProvider rootServices,
        string tenantId,
        string sessionId,
        ILogger logger,
        Func<Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null,
        Func<bool>? dispatchUncertaintyProbe = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _declaredClass = declaredClass;
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new ArgumentException("tenantId required", nameof(tenantId));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : throw new ArgumentException("sessionId required", nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sseWriter = sseWriter;
        _dispatchUncertaintyProbe = dispatchUncertaintyProbe;
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

        // === FR-A1-05 (task 034) — PRE-SUSPEND VALIDATION ==========================
        // Run the handler's ValidateChat BEFORE suspending. A call the handler can PROVE
        // is doomed at validation time (e.g. the R5-E sprk_document hard block, a missing
        // tenant, malformed args) renders an honest ❌ + D-F0(d) affordance NOW and is NEVER
        // suspended into a confirmation dialog — the user is never asked to confirm a call
        // that cannot succeed (the Confirm→❌ dead-end; spec FR-A1-05 / R16, policy §5).
        //
        // Invariant (project constraint + D-F0): this weakens the gate ONLY toward honesty —
        // a doomed call becomes an honest ❌ instead of a doomed confirm. It NEVER weakens the
        // gate toward execution: a call that PASSES validation still suspends and confirms
        // exactly as before. Validation that cannot PROVE doom (returns valid, OR faults) falls
        // through to the safe suspend floor, so a call whose failure is not certain at
        // validation time keeps its legitimate confirm dialog (the escalation-trigger boundary).
        var adapter = _inner as ToolHandlerToAIFunctionAdapter;
        if (adapter is not null)
        {
            ToolValidationResult preValidation;
            try
            {
                preValidation = adapter.ValidateForGate(arguments);
            }
            catch (Exception ex)
            {
                // A faulting validator is NOT proof the call is doomed — fall through to the
                // suspend floor rather than fabricating an honest-❌ shortcut on uncertainty.
                _logger.LogWarning(ex,
                    "[FR-A1-05][gate] pre-suspend validation threw — tool={ToolName} session={SessionId}; " +
                    "falling through to the confirmation gate (no honest-❌ shortcut on uncertain validation).",
                    Name, _sessionId);
                preValidation = ToolValidationResult.Success();
            }

            if (!preValidation.IsValid)
            {
                return await RenderPreSuspendRefusalAsync(pendingManager, arguments, preValidation, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // === FR-A1-03 (task 044) — DETERMINISTIC CONFIRMATION POLICY v2 =====================
        // The pre-suspend validation above proved the call is not DOOMED (R5-E honest-❌ path stays
        // ahead of everything). Now the deterministic ConfirmationPolicyEngine decides HOW the
        // invocation resolves — execute (no dialog) / execute-with-Undo / draft-and-handoff /
        // elicit / confirm / honest-block — from REAL signals: catalog-declared risk tier
        // (RiskTierResolver over the row's sprk_configuration.riskProfile DATA), argument
        // completeness (declared required[] vs supplied args), and the ambiguity/confidence slot
        // (operator reframe 2026-07-09 — NOT the E-1..E-6 authorization-provenance machinery). The
        // engine REPLACES the old declared-class-only "always suspend" decision: it is now the
        // single top-level decider on this live path (there is no parallel decider left to disagree).
        var decision = EvaluatePolicy(adapter, arguments);

        _logger.LogInformation(
            "[FR-A1-03][gate] policy decision — tool={ToolName} tier={Tier} origin={Origin} " +
            "completeness={Completeness} outcome={Outcome} session={SessionId}",
            Name, decision.Tier, decision.Origin, decision.Completeness, decision.Outcome, _sessionId);

        return decision.Outcome switch
        {
            GateOutcome.Execute or GateOutcome.ExecuteWithUndo =>
                await ExecuteInlineAsync(scope, adapter, decision, arguments, cancellationToken).ConfigureAwait(false),

            GateOutcome.Elicit => RenderElicit(adapter, arguments),

            GateOutcome.HonestBlock => RenderHonestBlock(),

            // ConfirmDialog (and any fail-closed default) → the existing suspend MECHANISM.
            _ => await SuspendForConfirmationAsync(pendingManager, arguments, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// FR-A1-05: a PROVABLY-doomed side-effecting call renders an honest ❌ BEFORE it suspends.
    /// ADR-040 store-before-render: a terminal <c>validation-failed</c> Gate marker lands in the
    /// session ledger FIRST (the ❌ is a persisted terminal entry, not an ephemeral render), then
    /// the grounded ❌ instruction returns to the model carrying the handler's real reason + its
    /// D-F0(d) affordance (e.g. the Document Upload deep link on the R5-E sprk_document block).
    /// NO resumable payload is stored and NO <c>action_confirmation</c> event is presented — the
    /// call was never suspended, so there is nothing to confirm and no dialog fires. A ledger
    /// write failure (e.g. the session vanished) degrades to a loud log but STILL returns the
    /// honest ❌ — never a fabricated success and never a silent suspend.
    /// </summary>
    private async Task<object?> RenderPreSuspendRefusalAsync(
        PendingPlanManager pendingManager,
        AIFunctionArguments? arguments,
        ToolValidationResult validation,
        CancellationToken cancellationToken)
    {
        var gateId = $"confirmation-{Guid.NewGuid():N}";
        var reason = validation.Errors is { Count: > 0 }
            ? string.Join(" ", validation.Errors)
            : "The action failed validation and was not performed.";

        // ADR-040: the terminal marker is stored BEFORE the render. Marker-only (no resumable
        // payload) via WriteGateMarkerAsync — the invocation never suspended.
        try
        {
            await pendingManager.WriteGateMarkerAsync(
                _tenantId,
                _sessionId,
                gateId,
                kind: PendingPlanManager.GateKindConfirmation,
                status: PendingPlanManager.GateStatusValidationFailed,
                sideEffectClass: PendingPlanManager.ToLedgerSideEffectClass(_declaredClass),
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The refusal still stands; a marker-write failure must not turn an honest ❌ into a
            // 5xx or (worse) a fall-through to suspend. NFR-07: identifiers only, never the reason.
            _logger.LogError(ex,
                "[FR-A1-05][gate] terminal validation-failed marker write failed — gateId={GateId} " +
                "tool={ToolName} session={SessionId}; returning the honest ❌ anyway (never suspend a doomed call).",
                gateId, Name, _sessionId);
        }

        // NFR-07: identifiers + counts only — never argument values or the reason/affordance text.
        _logger.LogInformation(
            "[FR-A1-05][gate] side-effecting invocation REJECTED pre-suspend (doomed at validation) — " +
            "gateId={GateId} tool={ToolName} declaredClass={DeclaredClass} argCount={ArgCount} session={SessionId}",
            gateId, Name, _declaredClass, arguments?.Count ?? 0, _sessionId);

        // Grounded ❌ turn instruction: the model relays the honest failure (reason + affordance
        // link verbatim) and does NOT retry or fabricate a result. This is deliberately DISTINCT
        // from the "ACTION SUSPENDED" text so the model cannot conflate a rejection with a
        // pending confirmation.
        return $"ACTION NOT PERFORMED — '{Name}' cannot succeed and was REJECTED before any " +
               $"confirmation was requested: {reason} Relay this to the user honestly, include any " +
               "link in the reason exactly as written, do NOT call this tool again this turn, and do " +
               "NOT fabricate a result.";
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

    // =====================================================================================
    // FR-A1-03 (task 044) — Confirmation Policy v2 wiring (execute / elicit / confirm / block)
    // =====================================================================================

    /// <summary>
    /// Assembles the <see cref="PolicyEvaluationContext"/> from REAL signals and asks the
    /// deterministic <see cref="ConfirmationPolicyEngine"/> for the outcome. Every input is
    /// structural DATA — there is no model judgment expressible here (ADR-039).
    /// <list type="bullet">
    ///   <item><b>Tier</b> — the catalog row's declared <c>sprk_configuration.riskProfile</c>
    ///   (task-020 seed DATA) via <see cref="GateRiskProfile.FromConfiguration"/> →
    ///   <see cref="RiskTierResolver"/>. Absent/unparseable ⇒ FAIL-CLOSED: a conservative Tier 2b
    ///   profile + Inferred origin, which the engine projects to a confirmation dialog (today's safe
    ///   floor for an un-migrated write/communicate row) — never an auto-execute.</item>
    ///   <item><b>Completeness</b> — the declared JSON-schema <c>required[]</c> vs the LLM-supplied
    ///   argument keys.</item>
    ///   <item><b>Origin / confidence</b> — the operator reframe (2026-07-09): a gate-reaching
    ///   typed-handler call is a COMMITTED capability selection by the agent turn (ADR-039's
    ///   sanctioned decider); genuine capability ambiguity is diverted to a clarifying question
    ///   BEFORE any tool call (layer 1). So the request is a confident (Explicit) selection IFF the
    ///   row declares real execute-eligible risk DATA AND no low-confidence backstop fired —
    ///   otherwise fail-closed Inferred. The origin slot is fed by this real
    ///   (declared-risk × ambiguity) confidence, NOT by parsed authorization provenance.</item>
    /// </list>
    /// </summary>
    private GateDecisionV2 EvaluatePolicy(ToolHandlerToAIFunctionAdapter? adapter, AIFunctionArguments arguments)
    {
        var declaredProfile = adapter is null ? null : TryParseRiskProfile(adapter.Tool.Configuration);
        var profile = declaredProfile ?? new GateRiskProfile { DeclaredTier = GateRiskTier.Tier2b };

        var argsComplete = adapter is null || ArgsComplete(adapter.Tool.JsonSchema, arguments);

        // OPTIONAL ambiguity/low-confidence backstop — HONORED from the injected probe when a
        // producer supplies one; honestly false in production today (layer 1 covers ambiguity).
        // NOT hardcoded — the value flows from the probe, so a real signal flips the outcome and
        // the integration proof fails if this were faked to a constant.
        var dispatchUncertain = _dispatchUncertaintyProbe?.Invoke() ?? false;

        var confident = declaredProfile is not null && !dispatchUncertain;
        var origin = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            UtteranceEnumeratesCapability = confident,
        };

        return ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = profile,
            Origin = origin,
            ArgsComplete = argsComplete,
            DispatchUncertain = dispatchUncertain,
            // No live content-safety / safety-perimeter producer is threaded to THIS gate today: a
            // Prompt-Shields BLOCKED turn yield-breaks upstream and never reaches the loop, and a
            // fail-OPEN degradation is not yet propagated here. These stay false honestly; the
            // overlay plumbing already honors a real signal (documented follow-on to thread one).
            ContentSafetyFlagged = false,
            SafetyPerimeterDegraded = false,
            // First evaluation of a fresh invocation — no gate id yet ⇒ ConfirmationState = None
            // (the ADR-040 "no second ask" applies on the resume path, not this first pass).
            Ledger = null,
            GateId = null,
        });
    }

    /// <summary>
    /// The EXECUTE leg (Policy v2 <see cref="GateOutcome.Execute"/> / <see cref="GateOutcome.ExecuteWithUndo"/>):
    /// runs the wrapped tool NOW — no confirmation dialog — under the loop's live OBO turn (exactly
    /// as a non-gated read executes). ADR-040 store-before-render: the executed outcome is persisted
    /// as a <c>loop@t{n}</c> <see cref="SessionOutput"/> BEFORE the task-035 <see cref="OutcomeCard"/>
    /// is composed + emitted. Carries the declared affordance — an Undo chip for a reversible
    /// Tier 2a/2b create, or the review-and-send record link for a Tier-1 email DRAFT (which is
    /// DRAFTED, never sent; the human's review-and-send at the linked record is the gate, r2).
    /// </summary>
    private async Task<object?> ExecuteInlineAsync(
        AsyncServiceScope scope,
        ToolHandlerToAIFunctionAdapter? adapter,
        GateDecisionV2 decision,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        object? raw;
        try
        {
            raw = await _inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        // The wrapped adapter returns a ToolResult on success (or an error envelope on a failed
        // handler). An unsuccessful/unexpected result is relayed AS-IS to the model — no card, no
        // fabricated success (D-F0 / no_fabrication).
        if (raw is not ToolResult result || !result.Success)
        {
            _logger.LogWarning(
                "[FR-A1-03][gate] auto-execute did not complete successfully — tool={ToolName} session={SessionId}; " +
                "relaying the tool's own outcome (no OutcomeCard, no fabricated success).",
                Name, _sessionId);
            return raw ?? $"ACTION NOT COMPLETED — '{Name}' executed but reported no result. Relay the outcome honestly.";
        }

        // Store-before-render (ADR-040): persist the loop@t{n} ledger output first.
        var sessionManager = scope.ServiceProvider.GetService<ChatSessionManager>();
        var ledgerKey = sessionManager is null
            ? null
            : await AppendSessionOutputAsync(sessionManager, result, cancellationToken).ConfigureAwait(false);

        var isEmail = _declaredClass == ToolSideEffectClass.Communicate;
        var userFacing = TypedHandlerResumeExecutor.ExtractUserSummary(result) ?? result.Summary ?? "Done.";
        var record = TypedHandlerResumeExecutor.ExtractCreatedRecord(result);
        var recordUrl = record is null ? null : BuildRecordUrl(scope, record);

        OutcomeCardLink? link = recordUrl is null
            ? null
            : OutcomeCardLink.ServerComposed(recordUrl, isEmail ? "Review and send" : "Open record", "record");

        var chips = new List<NextStepChip>();
        if (decision.Outcome == GateOutcome.ExecuteWithUndo)
        {
            // Declared Undo affordance for a reversible Tier 2a/2b create (the OutcomeCard NextStep
            // chip is the platform's declared-affordance vocabulary; the compensating delete is the
            // existing dataverse.delete_record capability the surface wires to this chip).
            chips.Add(new NextStepChip("Undo", "invoke_capability"));
        }

        // Compose + emit the OutcomeCard only when the ledger write succeeded (store-before-render).
        if (!string.IsNullOrWhiteSpace(ledgerKey))
        {
            var card = CompletionEngine.ComposeForGateAutoExecute(
                ledgerOutputKey: ledgerKey!,
                userFacing: userFacing,
                internalDetail: result.Summary,
                link: link,
                nextSteps: chips);

            if (_sseWriter is not null)
            {
                var view = card.Render();
                await _sseWriter(
                    new Api.Ai.ChatSseEvent(
                        "action_outcome",
                        null,
                        new Api.Ai.ChatSseActionOutcomeData(
                            ActionName: Name,
                            Status: view.Status.ToString().ToLowerInvariant(),
                            UserSummary: view.UserSummary,
                            LinkUrl: view.LinkUrl,
                            LinkLabel: view.LinkLabel,
                            NextSteps: view.NextStepLabels,
                            LedgerOutputKey: card.LedgerOutputKey)),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "[FR-A1-03][gate] side-effecting invocation AUTO-EXECUTED (no dialog) — tool={ToolName} " +
            "outcome={Outcome} declaredClass={DeclaredClass} ledgerKey={LedgerKey} session={SessionId}",
            Name, decision.Outcome, _declaredClass, ledgerKey, _sessionId);

        // Grounded turn text — the model relays the honest outcome. For email the invariant is
        // DRAFT + HANDOFF: the system NEVER sent it; the link (relayed verbatim) opens the record
        // for the human to review, edit, and send themselves.
        if (isEmail)
        {
            var linkText = recordUrl is null ? string.Empty : $" Review and send it here: [Review and send]({recordUrl}).";
            return $"EMAIL DRAFTED (NOT SENT): '{Name}' prepared a draft email for the user to review, edit, " +
                   $"and send themselves — the system did NOT send anything.{linkText} Tell the user the draft " +
                   "is ready for their review and send; do NOT say it was sent, and do NOT fabricate a send.";
        }

        var undoText = decision.Outcome == GateOutcome.ExecuteWithUndo
            ? " An Undo affordance is available on the outcome if they want to reverse it."
            : string.Empty;
        var openText = recordUrl is null ? string.Empty : $" [Open record]({recordUrl}).";
        return $"ACTION EXECUTED: '{Name}' completed. {userFacing}{openText}{undoText} Relay this outcome to the " +
               "user honestly; only relay links exactly as written, and do NOT fabricate anything further.";
    }

    /// <summary>
    /// The ELICIT leg (Policy v2 <see cref="GateOutcome.Elicit"/>): the required args are incomplete
    /// for a Tier ≥ 2 side effect. The tool is NEITHER executed NOR suspended — the model asks the
    /// user ONE natural question for the missing detail(s), then re-invokes with complete args (which
    /// re-evaluates from the top). No suspend marker is written (this is not a scary confirm; it is a
    /// natural elicitation — the operator's "incomplete args ⇒ elicit" ruling).
    /// </summary>
    private object? RenderElicit(ToolHandlerToAIFunctionAdapter? adapter, AIFunctionArguments arguments)
    {
        var missing = adapter is null ? new List<string>() : MissingRequired(adapter.Tool.JsonSchema, arguments);
        var missingText = missing.Count > 0 ? $" The following required detail(s) are missing: {string.Join(", ", missing)}." : string.Empty;

        _logger.LogInformation(
            "[FR-A1-03][gate] invocation ELICITS (incomplete args) — tool={ToolName} missingCount={Count} session={SessionId}",
            Name, missing.Count, _sessionId);

        return $"ACTION NEEDS MORE INFORMATION — '{Name}' was NOT executed and NOT suspended because required " +
               $"details are missing.{missingText} Ask the user ONE natural question to supply the missing detail(s), " +
               "then re-invoke the tool with complete arguments. Do NOT fabricate values and do NOT claim the action happened.";
    }

    /// <summary>
    /// The fail-closed HONEST-BLOCK leg (Policy v2 <see cref="GateOutcome.HonestBlock"/>): the engine
    /// could not resolve a safe outcome. The tool does NOT execute; an honest block is relayed.
    /// </summary>
    private object? RenderHonestBlock()
    {
        _logger.LogWarning(
            "[FR-A1-03][gate] invocation HONEST-BLOCKED (fail-closed) — tool={ToolName} session={SessionId}",
            Name, _sessionId);

        return $"ACTION CANNOT RUN — '{Name}' could not be evaluated to a safe outcome and was NOT executed. " +
               "Tell the user honestly; do not retry and do not fabricate a result.";
    }

    /// <summary>
    /// The CONFIRM leg (Policy v2 <see cref="GateOutcome.ConfirmDialog"/>): the existing suspension
    /// MECHANISM (unchanged from task 037). ADR-040: the pending Gate marker + resumable payload land
    /// BEFORE the <c>action_confirmation</c> presentation renders; a marker that cannot land aborts
    /// the invocation loudly (never executes).
    /// </summary>
    private async Task<object?> SuspendForConfirmationAsync(
        PendingPlanManager pendingManager,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var gateId = $"confirmation-{Guid.NewGuid():N}";
        var argsJson = arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(arguments.ToDictionary(a => a.Key, a => a.Value))
            : "{}";

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

        _logger.LogInformation(
            "[FR-P2-02][gate] side-effecting invocation suspended — gateId={GateId} tool={ToolName} " +
            "declaredClass={DeclaredClass} argCount={ArgCount} session={SessionId}",
            suspended.GateId, Name, _declaredClass, arguments?.Count ?? 0, _sessionId);

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

        return $"ACTION SUSPENDED FOR USER CONFIRMATION: '{Name}' performs a side effect " +
               $"(declared class: {PendingPlanManager.ToLedgerSideEffectClass(_declaredClass)}) and was " +
               "NOT executed. A confirmation request has been presented to the user. Do NOT call this " +
               "tool again this turn, do NOT assume it succeeded, and do NOT fabricate its result. " +
               "Tell the user the action is awaiting their explicit confirmation.";
    }

    /// <summary>
    /// Appends the executed result as an addressable <c>loop@t{n}</c> ledger output (ADR-040
    /// store-before-render) — the SAME reserved <c>loop</c> binding + monotonic turn scheme
    /// <see cref="TypedHandlerResumeExecutor"/> uses on the confirm-resume path. Best-effort: a
    /// write failure after a real side effect degrades to a loud log (never masks the executed
    /// effect behind a 5xx).
    /// </summary>
    private async Task<string?> AppendSessionOutputAsync(
        ChatSessionManager sessionManager,
        ToolResult result,
        CancellationToken ct)
    {
        try
        {
            var session = await sessionManager.GetSessionAsync(_tenantId, _sessionId, ct).ConfigureAwait(false);
            if (session is null)
            {
                _logger.LogWarning(
                    "[FR-A1-03][gate] session vanished before ledger output write — tool={ToolName} session={SessionId}",
                    Name, _sessionId);
                return null;
            }

            var turn = (session.Outputs is { Count: > 0 } ? session.Outputs.Max(o => o.Turn) : 0) + 1;
            var payload = result.Data ?? JsonSerializer.SerializeToElement(new { summary = result.Summary ?? string.Empty });
            var capped = SessionLedger.CapInlinePayload(payload.Clone());

            var entry = new SessionOutput
            {
                Key = SessionLedger.BuildOutputKey("loop", turn),
                BindingId = "loop",
                UcId = Name,
                Turn = turn,
                Disposition = _declaredClass == ToolSideEffectClass.Communicate ? "email" : "record",
                Payload = capped.Payload,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var appended = new List<SessionOutput>(session.Outputs ?? Array.Empty<SessionOutput>()) { entry };
            await sessionManager.UpdateSessionCacheAsync(session with { Outputs = appended }, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[FR-A1-03][gate] ledger output stored — key={Key} ucid={UcId} disposition={Disposition} session={SessionId}",
                entry.Key, entry.UcId, entry.Disposition, _sessionId);

            return entry.Key;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FR-A1-03][gate] ledger output write failed AFTER execution — tool={ToolName} session={SessionId}",
                Name, _sessionId);
            return null;
        }
    }

    /// <summary>
    /// Composes the server-side MDA entityrecord deep link for a handler-reported created/updated
    /// record (the review-and-send record for an email draft; the Undo target for a create). Null
    /// when the Dataverse environment URL is unavailable — link enrichment degrades, the outcome
    /// still renders. Server-composed only (never a model-invented URL — NFR-07 / ADR-040).
    /// </summary>
    private string? BuildRecordUrl(AsyncServiceScope scope, ToolCreatedRecord record)
    {
        var envUrl = scope.ServiceProvider.GetService<IOptions<DataverseOptions>>()?.Value?.EnvironmentUrl;
        if (string.IsNullOrWhiteSpace(envUrl) ||
            string.IsNullOrWhiteSpace(record.EntityLogicalName) ||
            record.RecordId == Guid.Empty)
        {
            return null;
        }

        return $"{envUrl.TrimEnd('/')}/main.aspx?pagetype=entityrecord&etn={record.EntityLogicalName}&id={record.RecordId:D}";
    }

    /// <summary>
    /// Parses the catalog row's declared risk profile from its <c>sprk_configuration</c> JSON string
    /// (the raw column value). Null (⇒ fail-closed confirm) when the config is absent/blank, is not a
    /// JSON object, declares no <c>riskProfile</c> block, or declares an invalid tier token — an
    /// authoring slip can never let a write auto-execute.
    /// </summary>
    private static GateRiskProfile? TryParseRiskProfile(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            return GateRiskProfile.FromConfiguration(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            // Present-but-invalid tier token ⇒ fail-closed (treated as no declared profile ⇒ confirm).
            return null;
        }
    }

    /// <summary>True when every declared required arg is present + non-empty (Policy v2 overlay 3 input).</summary>
    private static bool ArgsComplete(string? jsonSchema, AIFunctionArguments arguments) =>
        MissingRequired(jsonSchema, arguments).Count == 0;

    /// <summary>
    /// The declared <c>required[]</c> arg names that are absent or empty in the LLM-supplied
    /// arguments. An unparseable/absent schema declares nothing required ⇒ empty (complete).
    /// </summary>
    private static List<string> MissingRequired(string? jsonSchema, AIFunctionArguments arguments)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(jsonSchema))
        {
            return missing;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonSchema);
            if (!doc.RootElement.TryGetProperty("required", out var required) ||
                required.ValueKind != JsonValueKind.Array)
            {
                return missing;
            }

            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = item.GetString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (arguments is null || !arguments.TryGetValue(name, out var value) || IsEmptyArg(value))
                {
                    missing.Add(name);
                }
            }
        }
        catch (JsonException)
        {
            // Malformed schema ⇒ declare nothing missing (completeness is not the doomed-call gate;
            // a truly-broken schema fails the handler's own ValidateChat pre-suspend).
            return new List<string>();
        }

        return missing;
    }

    /// <summary>A supplied arg counts as ABSENT when it is null, an empty/whitespace string, or a JSON null/empty string.</summary>
    private static bool IsEmptyArg(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        JsonElement { ValueKind: JsonValueKind.Null } => true,
        JsonElement { ValueKind: JsonValueKind.String } el => string.IsNullOrWhiteSpace(el.GetString()),
        _ => false,
    };
}
