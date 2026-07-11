using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Chat-session boundary for the Click entry path (FR-P1-04,
/// spaarke-ai-architecture-redesign-r1 task 023b / ADR-039). The single entry point
/// (<see cref="DispatchAsync"/>) resolves a Binding row BY ID
/// (<see cref="IConsumerRoutingService.GetBindingByIdAsync"/> — chips carry
/// <c>binding_id</c>; the id IS the routing decision, ADR-039 D4), loads the target
/// <c>sprk_analysisaction</c> row, and executes it via the prompted executor
/// (<see cref="IActionRunner"/> — ActionRunner + PromptSchemaRenderer) with the
/// universal ledger write (<see cref="IOutputRouter"/>) BEFORE the terminal render
/// chunk (ADR-040 render-follows-store).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — historically overlapped the summarize-named orchestrator shell
/// (task 020), which executed ONE fixed consumer type (<c>chat-summarize</c>); that shell
/// was deleted by FR-P3-05 (task 044) and /summarize now delegates HERE.
/// (2) <i>Extension</i> — the Click path is capability-agnostic: it executes WHATEVER
/// Binding the chip's id names. Embedding a by-id generic dispatch inside the
/// summarize-named orchestrator would smuggle per-capability branching back into the
/// dispatch seam (the r7 anti-pattern ADR-039 deletes) and force a mid-integration
/// refactor of task 020's convergence contract. Both classes converge on the same
/// executor + ledger seam (ActionRunner → OutputRouter) — no parallel execution stack
/// is introduced. (3) <i>Cost-of-doing-nothing</i> — FR-P1-04 acceptance ("chip click
/// end-to-end") fails concretely: the client <c>dispatchConsumer(bindingId, args)</c>
/// helper (task 023) POSTs to <c>/sessions/{id}/dispatch</c> and gets a 404; every
/// chip emitted by task 022's Event stream is a dead button.
/// </para>
/// <para>
/// <b>Binding-id resolution rule (the Click contract)</b>: <c>bindingId</c> is the
/// <c>sprk_playbookconsumer</c> row GUID. That is what EVERY chip emitter sends —
/// the Event Rules contextual chips (manual-run / summarize-all / M4 confirm) emit
/// <c>Binding.BindingId.ToString()</c>, and the seeded <c>sprk_chiptransitions</c>
/// rows carry the row GUID (task 022 §3). Unknown/disabled ids are rejected with a
/// clean stable error (ADR-039: no fallback, no consumer-type re-detection).
/// </para>
/// <para>
/// <b>Execution envelope</b>: prompted Actions whose disposition <see cref="OutputRouter"/> can
/// route. The disposition gate derives from the ADR-043 §3 <see cref="DispositionRoutability"/>
/// registry (admission = routability — the ONE source; no parallel allow-list), so it admits exactly
/// the routable set and rejects not-yet-routable dispositions PRE-RUN with a stable error code
/// (cheaper and more honest than letting the router's loud post-store stub throw after the LLM spend).
/// The ActionKind gate (non-prompted kinds) is a SEPARATE, orthogonal axis owned by E-30.
/// </para>
/// <para>
/// <b>Args contract</b>: chip <c>args</c> forward verbatim from the client; THIS
/// boundary owns the typed parse (the client never interprets — task 023 contract).
/// P1 vocabulary: <c>fileIds</c> (string array — session-file subset; defaults to
/// the full manifest per FR-08; cap 20 per NFR-02). Unknown members (e.g. the M4
/// confirm chip's <c>confirmedDocType</c>) are accepted and ignored at P1 — the
/// Action's <c>sprk_inputschema</c> owns the future vocabulary.
/// </para>
/// <para>
/// <b>ADR-010</b>: concrete class, no orchestrator-authored interface. Non-sealed to
/// permit the <see cref="NullSessionDispatchOrchestrator"/> kill-switch subclass
/// (ADR-032), registered in <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c>.
/// </para>
/// <para>
/// <b>ADR-013 placement</b>: lives in <c>Services/Ai/Chat/</c> (in-zone AI territory);
/// in-zone code MAY consume executor internals. External CRUD code remains bound to
/// the <c>PublicContracts</c> facade.
/// </para>
/// </remarks>
public class SessionDispatchOrchestrator
{
    private readonly ChatSessionManager _sessionManager;
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IActionRunner _actionRunner;
    private readonly IContextBinder _contextBinder;
    private readonly ICodedWorkflowRegistry _codedWorkflows;
    private readonly ISessionFileTextSource _sessionFileTextSource;
    private readonly IOutputRouter _outputRouter;
    private readonly PendingPlanManager _pendingPlanManager;
    private readonly EventRulesOptions _manifestProbeOptions;
    private readonly Sprk.Bff.Api.Telemetry.AiTelemetry _aiTelemetry;
    private readonly ILogger<SessionDispatchOrchestrator> _logger;
    private readonly TimeProvider _timeProvider;

    public SessionDispatchOrchestrator(
        ChatSessionManager sessionManager,
        IConsumerRoutingService consumerRouting,
        IScopeResolverService scopeResolver,
        IActionRunner actionRunner,
        IContextBinder contextBinder,
        ICodedWorkflowRegistry codedWorkflows,
        ISessionFileTextSource sessionFileTextSource,
        IOutputRouter outputRouter,
        PendingPlanManager pendingPlanManager,
        IOptions<EventRulesOptions> manifestProbeOptions,
        Sprk.Bff.Api.Telemetry.AiTelemetry aiTelemetry,
        ILogger<SessionDispatchOrchestrator> logger,
        TimeProvider? timeProvider = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _contextBinder = contextBinder ?? throw new ArgumentNullException(nameof(contextBinder));
        _codedWorkflows = codedWorkflows ?? throw new ArgumentNullException(nameof(codedWorkflows));
        _sessionFileTextSource = sessionFileTextSource ?? throw new ArgumentNullException(nameof(sessionFileTextSource));
        _outputRouter = outputRouter ?? throw new ArgumentNullException(nameof(outputRouter));
        _pendingPlanManager = pendingPlanManager ?? throw new ArgumentNullException(nameof(pendingPlanManager));
        // G-P2 UAT round-1 finding 4 (§11 reuse): the manifest readiness probe settings
        // are the SAME wait-briefly-or-degrade policy the Event path applies to the SAME
        // upload → manifest-write → cache-propagation race (G-P1 Defect 3) — reusing
        // EventRulesOptions keeps ONE probe policy instead of a second config surface.
        _manifestProbeOptions = manifestProbeOptions?.Value ?? throw new ArgumentNullException(nameof(manifestProbeOptions));
        _aiTelemetry = aiTelemetry ?? throw new ArgumentNullException(nameof(aiTelemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Track-B hygiene sweep (R9, task 073): clock for the manifest readiness probe's
        // Task.Delay — optional, defaults to TimeProvider.System (production/DI); tests
        // inject a FakeTimeProvider for deterministic probe-delay assertions instead of
        // relying on a real (or zeroed) wall-clock wait.
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Protected ctor used only by <see cref="NullSessionDispatchOrchestrator"/> so the
    /// kill-switch subclass can be constructed when the compound AI gate is OFF and the
    /// AI dependencies are absent (ADR-030 P3 / ADR-032 — mirrors
    /// the canonical Null-subclass siblings). The Null override never reads the
    /// nulled fields — it throws <see cref="Configuration.FeatureDisabledException"/>
    /// before they are dereferenced.
    /// </summary>
    protected SessionDispatchOrchestrator(ILogger<SessionDispatchOrchestrator> logger)
    {
        _sessionManager = null!;
        _consumerRouting = null!;
        _scopeResolver = null!;
        _actionRunner = null!;
        _contextBinder = null!;
        _codedWorkflows = null!;
        _sessionFileTextSource = null!;
        _outputRouter = null!;
        _pendingPlanManager = null!;
        _manifestProbeOptions = null!;
        _aiTelemetry = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = null!;
    }

    /// <summary>
    /// THE Click-path convergence method. <c>DispatchSessionEndpoint</c> delegates to
    /// THIS method — no other entry point is exposed.
    /// </summary>
    /// <returns>
    /// SSE-shaped chunks: a terminal <see cref="AnalysisChunk"/> <c>complete</c> chunk
    /// rendered FROM the stored ledger entry (or <see cref="AnalysisChunk.FromError"/>
    /// for runtime failures once the stream has begun). Resolution failures throw
    /// BEFORE the first chunk so the endpoint's pre-stream probe maps them to
    /// ProblemDetails: <see cref="DispatchRejectedException"/> (unknown binding /
    /// unsupported kind / unsupported disposition), <see cref="InvalidOperationException"/>
    /// containing "not found" (session), <see cref="ArgumentException"/> (input caps).
    /// </returns>
    public virtual async IAsyncEnumerable<AnalysisChunk> DispatchAsync(
        SessionDispatchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");
        if (request.BindingId == Guid.Empty)
        {
            throw new ArgumentException("BindingId is required.", nameof(request));
        }

        // ── Args parse (this boundary owns the typed parse — client forwards verbatim).
        var requestedFileIds = TryReadFileIds(request.Args);

        // NFR-02 — hard cap (same defense-in-depth as the summarize boundary).
        if (requestedFileIds is { Count: > ChatSession.MaxUploadedFiles })
        {
            throw new ArgumentException(
                $"Dispatch args exceed the {ChatSession.MaxUploadedFiles}-file per-session cap " +
                $"(spec NFR-02). Received {requestedFileIds.Count} file IDs.",
                nameof(request));
        }

        // Session load — not-found surfaces as InvalidOperationException("... not found ...")
        // which the endpoint maps to 404 (same contract as the sibling orchestrators).
        ChatSession? session = await _sessionManager
            .GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"Chat session '{request.SessionId}' not found for tenant '{request.TenantId}'.");
        }

        // ── ADR-039: resolve the Binding BY ID — the chip's binding_id IS the routing
        // decision. Unknown/disabled ids reject with a clean stable error (no fallback,
        // no consumer-type re-detection, no second intent mechanism).
        var binding = await _consumerRouting
            .GetBindingByIdAsync(request.BindingId, cancellationToken)
            .ConfigureAwait(false);
        if (binding is null)
        {
            throw new DispatchRejectedException(
                DispatchRejectedException.BindingNotFound,
                StatusCodes.Status404NotFound,
                "The requested capability binding does not exist or is disabled.");
        }

        if (binding.ActionId is null || binding.ActionId.Value == Guid.Empty)
        {
            // Pure-engine legacy row (playbook-only target) — the Click path executes
            // catalog Actions only at P1; engine-target chips are a catalog authoring error.
            throw new DispatchRejectedException(
                DispatchRejectedException.ActionKindUnsupported,
                StatusCodes.Status422UnprocessableEntity,
                "The requested binding has no Action target; the dispatch path executes catalog Actions only.");
        }

        // ── ADR-043 §4 (E-30): the ActionKind decision is a deterministic total function over the
        // closed kind vocabulary — Prompted → the prompted executor (ActionRunner), Coded → the
        // registered ICodedWorkflow via ICodedWorkflowRegistry (the reserved Action-Engine front door;
        // engine internals stay behind ICodedWorkflow.ExecuteAsync), and ANY other/undefined kind →
        // the same loud 422 as before (no silent fallback, no second dispatch protocol — ADR-039).
        // The kind decision lives ONLY here; the two executor legs below join the SAME
        // store-before-render tail (OutputRouter.RouteAsync), so there is one ledger write and one
        // render boundary regardless of kind.
        if (binding.ActionKind is not (ActionKind.Prompted or ActionKind.Coded))
        {
            throw new DispatchRejectedException(
                DispatchRejectedException.ActionKindUnsupported,
                StatusCodes.Status422UnprocessableEntity,
                $"The requested binding targets an Action of kind '{binding.ActionKind}'; " +
                "the dispatch path executes prompted or coded Actions only.");
        }

        // ── ADR-043 §3 single-source disposition admission: the dispatch seam admits EXACTLY the
        // dispositions OutputRouter can route (DispositionRoutability — the ONE registry). There is
        // NO separate hardcoded allow-list here: admission DERIVES from routability, so the admit-gate,
        // the OutputRouter switch, and ToLedgerValue can never disagree. This is precisely the drift
        // that half-landed compose's routing promotion (router case added, admit-gate un-widened) →
        // a live 422. Rejecting a not-yet-routable disposition PRE-RUN is cheaper and more honest than
        // letting the router's loud post-store stub throw after the LLM spend. Realizing a disposition
        // is now ONE change in DispositionRoutability, and the admit-gate follows automatically.
        if (!DispositionRoutability.IsAdmissible(binding.Disposition))
        {
            throw new DispatchRejectedException(
                DispatchRejectedException.DispositionUnsupported,
                StatusCodes.Status422UnprocessableEntity,
                $"The requested binding declares disposition '{binding.Disposition}', whose OutputRouter " +
                $"routing leg is not yet implemented ({DispositionRoutability.NotRoutableReason(binding.Disposition)}). " +
                "Only dispositions the router can route are admitted on the dispatch path.");
        }

        var action = await _scopeResolver
            .GetActionAsync(binding.ActionId.Value, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Binding {binding.BindingId} references Action {binding.ActionId.Value}, " +
                "but that sprk_analysisaction row could not be loaded from Dataverse. Fix the Binding's " +
                "sprk_action lookup.");

        _logger.LogDebug(
            "FR-P1-04: Click dispatch resolved via catalog (bindingId={BindingId} ucid={Ucid} " +
            "consumerType={ConsumerType} actionId={ActionId} actionName={ActionName} disposition={Disposition}) " +
            "tenant={TenantId} session={SessionId} argFileCount={ArgFileCount}",
            binding.BindingId, binding.Ucid, binding.ConsumerType, action.Id, action.Name,
            binding.Disposition, request.TenantId, request.SessionId, requestedFileIds?.Count ?? 0);

        // Both executor legs (prompted / coded) produce the output JsonElement + its grounding source
        // refs, then join the SAME store-before-render tail (OutputRouter.RouteAsync) below — one ledger
        // write, one render boundary, regardless of ActionKind (ADR-039 / ADR-040).
        JsonElement output;
        IReadOnlyList<string> sourceRefs = Array.Empty<string>();

        if (binding.ActionKind == ActionKind.Coded)
        {
            // ── Coded leg (E-30 / ADR-043 §4): a registered ICodedWorkflow fulfils the coded Action —
            // the reserved front door the future Action Engine plugs into (engine internals stay behind
            // ICodedWorkflow.ExecuteAsync). Resolution is closed-catalog: the Action's sprk_workflowclass
            // MUST name a registered workflow — unknown/missing refs reject loud (ADR-039, no fallback).
            // Self-contained workflows read their inputs from the operand args (CodedWorkflowContext.
            // ArgumentsJson = the dispatch args verbatim); they do NOT use the prompted ContextBinder
            // grounding. The workflow's result serializes to the output JsonElement the router stores.
            var workflowClassRef = binding.WorkflowClass;
            if (string.IsNullOrWhiteSpace(workflowClassRef))
            {
                throw new DispatchRejectedException(
                    DispatchRejectedException.ActionKindUnsupported,
                    StatusCodes.Status422UnprocessableEntity,
                    $"Binding {binding.BindingId} targets a coded Action but declares no sprk_workflowclass; " +
                    "a coded dispatch requires a registered workflow class ref (ADR-039 closed catalog).");
            }

            var workflow = _codedWorkflows.GetWorkflow(workflowClassRef)
                ?? throw new DispatchRejectedException(
                    DispatchRejectedException.ActionKindUnsupported,
                    StatusCodes.Status422UnprocessableEntity,
                    $"Binding {binding.BindingId} names coded workflow class '{workflowClassRef}', which is not " +
                    $"in the coded-workflow registry (registered: {string.Join(", ", _codedWorkflows.GetRegisteredWorkflowClassRefs())}). " +
                    "Fix the Action row or the registration — there is no fallback (ADR-039).");

            string? codedError = null;
            JsonElement codedOutput = default;
            try
            {
                var result = await workflow
                    .ExecuteAsync(
                        new CodedWorkflowContext
                        {
                            ArgumentsJson = request.Args is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } a
                                ? a.GetRawText()
                                : "{}",
                            UserId = null,
                            ActingUserEmail = request.ActingUserEmail,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                codedOutput = SerializeWorkflowResult(result, workflowClassRef);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SessionDispatchOrchestrator: coded workflow '{WorkflowClass}' failed for binding {BindingId} action {ActionId}. TenantId={TenantId} SessionId={SessionId}",
                    workflowClassRef, binding.BindingId, action.Id, request.TenantId, request.SessionId);
                codedError = "The action failed. Please try again.";
            }

            _aiTelemetry.RecordCapabilityInvocation(
                request.TenantId,
                userId: null,
                entryPath: Sprk.Bff.Api.Telemetry.AiMeteringContext.EntryPathCoded,
                capability: binding.Ucid ?? binding.ConsumerType,
                outcome: codedError is null ? "success" : "failed");

            if (codedError is not null)
            {
                yield return AnalysisChunk.FromError(codedError);
                yield break;
            }

            output = codedOutput;
        }
        else
        {
            // ── ADR-043 (E-10) input resolution: ContextBinder resolves the Action's DECLARED inputs into
            // grounding context (ContextEnvelope) + the typed operand, and writes the ContextEnvelope
            // fingerprint (task-038's dark seam, now live; ADR-040 store-before-render; NFR-07 ids/counts).
            // The operand branch is deterministic over the declared schema (ADR-039): an Action declaring a
            // structured operand (selectionText/changesText/documentText arg, or a ledger_resolution
            // reference — e.g. the compose actions) resolves from the dispatch args WITHOUT session files;
            // otherwise the shipped file/`## Document` path runs unchanged (non-regression).
            var runContext = new LinearRunContext
            {
                ConsumerType = binding.ConsumerType,
                CorrelationId = request.CorrelationId,
                TenantId = request.TenantId,
            };

            // The context fingerprint + the output entry share the turn ordinal (max prior output turn + 1),
            // so the "context selected" leg is anchored to the turn it grounds (OutputRouter allocates the
            // SAME ordinal for the SessionOutput below — no output is appended between).
            var contextTurn = (session.Outputs is { Count: > 0 } ? session.Outputs.Max(o => o.Turn) : 0) + 1;
            var conversationTail = ConversationContextProducer.BuildConversationTail(session);

            BoundInputs boundInputs;

            if (_contextBinder.HasStructuredOperand(binding.InputSchemaJson, request.Args))
            {
                // ── Structured-operand path (args-text / ledger_resolution — the compose-B2 shape). No
                // session files are required; the no-file hard stop is relaxed. ContextBinder resolves the
                // declared operand (and, for ledger_resolution, a prior SessionOutput by reference — ADR-040).
                BoundInputs? bound = null;
                string? bindError = null;
                try
                {
                    bound = await _contextBinder
                        .BindAsync(
                            new ContextBindingRequest
                            {
                                InputSchemaJson = binding.InputSchemaJson,
                                Args = request.Args,
                                LedgerOutputs = session.Outputs,
                                ConversationTail = conversationTail,
                                // Task 053 (FR-B-04): the host record makes the envelope feature-complete
                                // (Business host-identity + Record memory refs). Id-only shape — no lazy
                                // name fetch here; deterministic + pinned. Prompts are UNCHANGED (the
                                // executor renders the operand only; the envelope feeds fingerprint/counts).
                                HostEntityType = session.HostContext?.EntityType,
                                HostEntityId = session.HostContext?.EntityId,
                                HostEntityName = session.HostContext?.EntityName,
                                TenantId = request.TenantId,
                                SessionId = request.SessionId,
                                Turn = contextTurn,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A dangling / truncated ledger_resolution reference (ADR-040) or a malformed operand
                    // surfaces here — loud, never a silent fallback. Identifiers only (NFR-07).
                    _logger.LogError(ex,
                        "SessionDispatchOrchestrator: input resolution failed for binding {BindingId} action {ActionId}. TenantId={TenantId} SessionId={SessionId}",
                        binding.BindingId, action.Id, request.TenantId, request.SessionId);
                    bindError = "The AI action could not resolve its inputs. Please check the request and try again.";
                }

                if (bindError is not null)
                {
                    yield return AnalysisChunk.FromError(bindError);
                    yield break;
                }

                boundInputs = bound!;
            }
            else
            {
                // ── File-operand path: the shipped summarize behavior, unchanged. Resolve session files,
                // probe for manifest readiness, fetch text, then bind the file as a `## Document` operand.
                boundInputs = default!;
                var fileBind = await ResolveFileOperandAsync(
                    request, session, binding, requestedFileIds, conversationTail, contextTurn, cancellationToken)
                    .ConfigureAwait(false);

                if (fileBind.Error is not null)
                {
                    yield return AnalysisChunk.FromError(fileBind.Error);
                    yield break;
                }

                boundInputs = fileBind.Inputs!;
                // The manifest readiness probe may have refreshed the session — carry the refreshed instance
                // to the ledger write (preserves the pre-E-10 behavior where the probe's session flowed to RouteAsync).
                session = fileBind.Session;
                sourceRefs = fileBind.SourceRefs;
            }

            // Route the output write on top of the fingerprint-inclusive session ContextBinder returned, so the
            // output write (read-modify-write) does not clobber the context fingerprint just written (ADR-040
            // append-only; both writes mutate the session). Falls back to the current session when no fingerprint
            // was written (e.g. the session vanished from the store mid-turn).
            session = boundInputs.UpdatedSession ?? session;

            output = default;
            string? llmError = null;
            try
            {
                output = await _actionRunner
                    .RunAsync(action, boundInputs, runContext, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SessionDispatchOrchestrator: prompted executor failed for binding {BindingId} action {ActionId}. TenantId={TenantId} SessionId={SessionId}",
                    binding.BindingId, action.Id, request.TenantId, request.SessionId);
                llmError = "The AI action failed. Please try again.";
            }

            // FR-P4-05 per-tenant metering (task 054): one capability-invocation increment at THE dispatch
            // seam — covers chip clicks, the loop's BindingCapabilityTool, gate resolution, and /summarize.
            _aiTelemetry.RecordCapabilityInvocation(
                request.TenantId,
                userId: null,     // ambient scope
                entryPath: null,  // ambient scope (default "click")
                capability: binding.Ucid ?? binding.ConsumerType,
                outcome: llmError is null ? "success" : "failed");

            if (llmError is not null)
            {
                yield return AnalysisChunk.FromError(llmError);
                yield break;
            }
        }

        // ── ADR-040 SEAM (FR-P1-02): the universal ledger write BEFORE render. The OutputRouter writes
        // the addressable SessionOutput ({bindingId}@t{n}) through the session store FIRST, then routes
        // by disposition; the terminal chunk below renders FROM the stored entry's payload (render
        // follows store). SourceRefs carry the grounding file ids (identifiers only, NFR-07).
        var routed = await _outputRouter
            .RouteAsync(
                session,
                binding,
                output,
                sourceRefs: sourceRefs,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // ── FR-P2-03: a successful dispatch of this Binding resolves any pending elicitation gate
        // awaiting it. ONE resolution point at the ONE dispatch seam (ADR-039); marker written BEFORE
        // the terminal chunk renders (ADR-040).
        await _pendingPlanManager
            .ResolveElicitationOnDispatchAsync(
                request.TenantId, request.SessionId, binding.BindingId, cancellationToken)
            .ConfigureAwait(false);

        // ── FR-A1-10 / D-F5 (task 039): explicit render-boundary assertion — the terminal chunk is built
        // EXCLUSIVELY from the entry ProgressiveRenderGuard confirms was actually written to the ledger
        // (ADR-040 storage-precedes-rendering), never from the pre-store `output` local above.
        var storedEntry = ProgressiveRenderGuard.EnsureStored(routed.Entry);
        yield return DeserializeResultChunk(storedEntry.Payload.GetRawText());

        // ── Next-step chips: the dispatched Binding's curated sprk_chiptransitions follow the terminal
        // complete chunk so the conversation surface always shows the CURRENT next steps.
        var transitionChips = BuildTransitionChips(binding);
        if (transitionChips.Count > 0)
        {
            yield return AnalysisChunk.FromChips(transitionChips);
        }
    }

    private static readonly JsonSerializerOptions WorkflowResultSerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serialize a coded workflow's result into the <see cref="JsonElement"/> the
    /// <see cref="OutputRouter"/> stores as the <c>SessionOutput</c> payload (ADR-040 store-before-render).
    /// A result that is already a <see cref="JsonElement"/> is cloned (detached from any caller-owned
    /// <c>JsonDocument</c> lifetime); anything else is serialized with the shared web options. A null
    /// result is a coded-contract violation — fail loud, never store null.
    /// </summary>
    private static JsonElement SerializeWorkflowResult(object? result, string workflowClassRef)
    {
        if (result is null)
        {
            throw new InvalidOperationException(
                $"Coded workflow '{workflowClassRef}' returned null; a coded dispatch must return a " +
                "serializable result object (the SessionOutput payload — ADR-040 stores it before render).");
        }

        if (result is JsonElement element)
        {
            return element.Clone();
        }

        var json = JsonSerializer.Serialize(result, WorkflowResultSerializerOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Resolve the FILE-operand path (the shipped summarize behavior): the FR-08 file subset, the G-P2
    /// manifest readiness probe, the session-file text fetch, then a <see cref="ContextBinder"/> bind of
    /// the resolved <see cref="DocumentText"/> as a <see cref="OperandChannel.Document"/> operand (writing
    /// the context fingerprint). Extracted from the iterator so it can use try/catch + early returns; the
    /// caller yields <see cref="FileOperandResult.Error"/> as a stream error chunk when present.
    /// </summary>
    private async Task<FileOperandResult> ResolveFileOperandAsync(
        SessionDispatchRequest request,
        ChatSession session,
        Binding binding,
        IReadOnlyList<string>? requestedFileIds,
        IReadOnlyList<LedgerEntryReference> conversationTail,
        int contextTurn,
        CancellationToken cancellationToken)
    {
        // ── FR-08 file resolution: explicit args subset, else the full session manifest.
        var uploadedFiles = session.UploadedFiles ?? Array.Empty<ChatSessionFile>();
        var targetFiles = ResolveTargetFiles(requestedFileIds, uploadedFiles);

        // ── G-P2 UAT round-1 finding 4 (2026-07-06): manifest readiness probe at the
        // ONE dispatch seam (covers the loop's BindingCapabilityTool, chip clicks, and
        // gate-resolve — every caller of DispatchAsync). A fresh upload's manifest write
        // can lag the user's immediate "summarize this document" (upload 202 → manifest
        // write → cache propagation), so requested ids (or the default-all manifest)
        // resolve empty and the capability honestly reports the file as missing.
        // Wait-briefly-or-degrade, IDENTICAL policy + bounds to the Event path's G-P1
        // Defect 3 probe (EventRulesOptions.ReadinessProbe*, ~5s default): re-read the
        // session until requested ids all resolve (explicit subset) or the manifest is
        // non-empty (default-all), then degrade to whatever resolved. Track-B hygiene
        // sweep (R9, task 073): the probe now delays via TimeProvider (per ADR-038 §5 /
        // docs/standards/TEST-ARCHITECTURE.md §4) instead of raw Task.Delay, so tests can
        // deterministically drive the probe with a FakeTimeProvider instead of relying on
        // a zeroed real-world delay.
        if (IsResolutionIncomplete(requestedFileIds, targetFiles))
        {
            for (var attempt = 1; attempt <= _manifestProbeOptions.ReadinessProbeAttempts; attempt++)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Max(0, _manifestProbeOptions.ReadinessProbeDelayMs)),
                        _timeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
                var refreshed = await _sessionManager
                    .GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
                    .ConfigureAwait(false);
                if (refreshed is null)
                {
                    break;
                }
                session = refreshed;
                uploadedFiles = session.UploadedFiles ?? Array.Empty<ChatSessionFile>();
                targetFiles = ResolveTargetFiles(requestedFileIds, uploadedFiles);
                if (!IsResolutionIncomplete(requestedFileIds, targetFiles))
                {
                    break;
                }
            }

            _logger.LogInformation(
                "SessionDispatchOrchestrator: manifest readiness probe finished — resolved {ResolvedCount} file(s) " +
                "(requested {RequestedCount}, manifest {ManifestCount}). tenant={TenantId} session={SessionId} binding={BindingId}",
                targetFiles.Count, requestedFileIds?.Count ?? 0, uploadedFiles.Count,
                request.TenantId, request.SessionId, binding.BindingId);
        }

        if (targetFiles.Count == 0)
        {
            return new FileOperandResult
            {
                Session = session,
                Error = "No session files were available for this action. Upload a file first, or pass a valid fileId subset.",
            };
        }

        // ── Session-scoped text fetch — transport failures surface as a stream error chunk.
        SessionFileText textResult = default!;
        string? fetchError = null;
        try
        {
            textResult = await _sessionFileTextSource
                .FetchAsync(request.TenantId, request.SessionId, targetFiles, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SessionDispatchOrchestrator: session-file text retrieval failed. TenantId={TenantId} SessionId={SessionId} BindingId={BindingId}",
                request.TenantId, request.SessionId, binding.BindingId);
            fetchError = "Failed to retrieve session file content. Please try again.";
        }

        if (fetchError is not null)
        {
            return new FileOperandResult { Session = session, Error = fetchError };
        }

        if (string.IsNullOrWhiteSpace(textResult.ExtractedText))
        {
            return new FileOperandResult
            {
                Session = session,
                Error = "Session files contained no text to analyze. The RAG index may still be catching up — try again in a few seconds.",
            };
        }

        // ── The resolved file is a `## Document`-channel operand (the shipped path). ContextBinder
        // wraps it + assembles the (context-only) envelope + writes the fingerprint (store-before-render).
        var documentText = new DocumentText
        {
            DocumentId = null,
            FileName = textResult.DisplayName,
            ExtractedText = textResult.ExtractedText,
        };

        var bound = await _contextBinder
            .BindAsync(
                new ContextBindingRequest
                {
                    FileDocument = documentText,
                    ConversationTail = conversationTail,
                    // Task 053 (FR-B-04): feature-complete envelope on the file-operand path too — Business
                    // host-identity + Record memory refs from the host record. Prompts UNCHANGED (the
                    // executor renders the `## Document` operand only).
                    HostEntityType = session.HostContext?.EntityType,
                    HostEntityId = session.HostContext?.EntityId,
                    HostEntityName = session.HostContext?.EntityName,
                    TenantId = request.TenantId,
                    SessionId = request.SessionId,
                    Turn = contextTurn,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new FileOperandResult
        {
            Session = session,
            Inputs = bound,
            SourceRefs = targetFiles.Select(f => f.FileId).ToList(),
        };
    }

    // Task 053 (FR-B-04): the conversation-tail reference projection moved to
    // ContextSliceProducers.ConversationContextProducer.BuildConversationTail so tail production is
    // single-home (this orchestrator + the interactive ChatEndpoints path both call it).

    /// <summary>Outcome of <see cref="ResolveFileOperandAsync"/>: either resolved <see cref="Inputs"/> or an <see cref="Error"/> to stream.</summary>
    private sealed record FileOperandResult
    {
        /// <summary>The session as (possibly) refreshed by the manifest readiness probe — used for the ledger write.</summary>
        public required ChatSession Session { get; init; }

        /// <summary>The bound inputs when resolution succeeded; null when <see cref="Error"/> is set.</summary>
        public BoundInputs? Inputs { get; init; }

        /// <summary>A user-facing error to stream as an <c>AnalysisChunk.FromError</c>; null on success.</summary>
        public string? Error { get; init; }

        /// <summary>Grounding file ids for the ledger <c>SourceRefs</c> (identifiers only, NFR-07).</summary>
        public IReadOnlyList<string> SourceRefs { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Map the Binding's valid <c>sprk_chiptransitions</c> to the unified chip wire shape.
    /// Authored <c>prefill_slots</c> forward verbatim; otherwise NO args are attached so a
    /// follow-up click resolves the file set AT DISPATCH TIME (FR-08 default = the full
    /// CURRENT session manifest). G-P1 UAT round-2 fix (2026-07-06): pre-filling the
    /// dispatched batch's fileIds froze "Summarize again" to the ORIGINAL files — a file
    /// uploaded after the first dispatch was silently excluded.
    /// </summary>
    private static IReadOnlyList<AnalysisChunkChip> BuildTransitionChips(Binding binding)
    {
        var chips = new List<AnalysisChunkChip>();
        foreach (var transition in binding.ChipTransitions)
        {
            if (string.IsNullOrWhiteSpace(transition.TargetBindingId) ||
                string.IsNullOrWhiteSpace(transition.ChipLabel))
            {
                continue;
            }
            chips.Add(new AnalysisChunkChip(
                TargetBindingId: transition.TargetBindingId!,
                Label: transition.ChipLabel!,
                Args: transition.PrefillSlots is { } slots ? (object)slots : null,
                RequiresAttachments: transition.RequiresAttachments == true));
        }
        return chips;
    }

    /// <summary>
    /// Deserialize the STORED ledger payload into the terminal chunk. The discriminator is
    /// summarize-preserving AND compose-lossless (spaarkeai-compose-r2 UAT wire-loss fix):
    /// <list type="bullet">
    ///   <item>A JSON object carrying the <see cref="DocumentAnalysisResult"/> SIGNATURE
    ///     (<c>tldr</c> / <c>entities</c> / <c>keywords</c> — fields present in NO compose
    ///     output schema) is the summarize / document-analysis case: it is coerced to the
    ///     typed <see cref="DocumentAnalysisResult"/> via
    ///     <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> so the wire shape is
    ///     BYTE-IDENTICAL to the shipped summarize contract (DAR defaults + top-level
    ///     <c>summary</c>). The section-keyed client bridge (dispatchConsumer.ts) is unchanged.</item>
    ///   <item>ANY OTHER JSON object (compose actions — <c>explanation</c>/<c>keyConcepts</c>,
    ///     <c>matches</c>/<c>overallRisk</c>, <c>changes</c>, ... — or any future non-DAR
    ///     disposition) is passed THROUGH verbatim via <see cref="AnalysisChunk.CompletedRaw"/>,
    ///     so its fields survive on the wire at <c>result</c> for the client's shape-detecting
    ///     formatter (composeResultFormat.ts). BEFORE this fix every such payload was coerced to
    ///     DocumentAnalysisResult, and System.Text.Json silently dropped its unknown properties →
    ///     the client received an EMPTY DAR blob instead of prose.</item>
    ///   <item>A non-object payload (array/scalar) or unparseable content degrades to a text
    ///     completion — the client always sees a terminal <c>complete</c> chunk, never a silent EOF.</item>
    /// </list>
    /// </summary>
    private static AnalysisChunk DeserializeResultChunk(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (IsDocumentAnalysisShape(root))
                {
                    // Summarize / document-analysis consumer: coerce to the typed DAR so the wire
                    // shape (incl. DAR defaults + the top-level `summary`) is unchanged. The payload
                    // already IS the DAR shape, so this round-trips losslessly (non-regression).
                    var dar = JsonSerializer.Deserialize<DocumentAnalysisResult>(jsonContent);
                    if (dar is not null)
                    {
                        dar.ParsedSuccessfully = true;
                        return AnalysisChunk.Completed(dar);
                    }
                }

                // Compose / other capability output: pass the payload THROUGH verbatim (clone so it
                // outlives this JsonDocument's using-scope). The compose fields reach the client at
                // `result` intact — the coercion that stripped them is gone.
                return AnalysisChunk.CompletedRaw(root.Clone());
            }
        }
        catch (JsonException)
        {
            // Graceful degrade — schema-constrained output should parse; drift falls back
            // to a text completion rather than a dead stream.
        }
        return AnalysisChunk.Completed(jsonContent);
    }

    /// <summary>
    /// Whether a terminal ledger payload object carries the <see cref="DocumentAnalysisResult"/>
    /// SIGNATURE — i.e. at least one of <c>tldr</c> / <c>entities</c> / <c>keywords</c>. These three
    /// fields are unique to the summarize / document-analysis output schema and appear in NONE of the
    /// five compose output schemas (compose-explain-clause, compose-compare-to-playbook,
    /// compose-draft-alternative, compose-summarize-word-changes, compose-defined-terms), so this is
    /// an unambiguous discriminator. Deliberately does NOT key on <c>summary</c> alone: the
    /// compose-summarize-word-changes schema also has a <c>summary</c> field, and treating that as DAR
    /// would strip its <c>changes[]</c> — the very wire-loss this fix removes.
    /// </summary>
    private static bool IsDocumentAnalysisShape(JsonElement root) =>
        root.TryGetProperty("tldr", out _)
        || root.TryGetProperty("entities", out _)
        || root.TryGetProperty("keywords", out _);

    /// <summary>
    /// Tolerant extraction of the P1 <c>fileIds</c> arg (string array). Missing /
    /// null / wrong-shape members degrade to "no explicit subset" (FR-08 default-all)
    /// — arg parsing must never throw for shape drift; only the NFR-02 cap throws.
    /// </summary>
    internal static IReadOnlyList<string>? TryReadFileIds(JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }
        if (!obj.TryGetProperty("fileIds", out var fileIdsEl) || fileIdsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = new List<string>();
        foreach (var item in fileIdsEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var id = item.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id!);
                }
            }
        }
        return ids.Count > 0 ? ids : null;
    }

    /// <summary>
    /// Whether file resolution is INCOMPLETE for readiness-probe purposes (finding 4):
    /// an explicit subset that did not fully resolve against the manifest, or a
    /// default-all request against an empty manifest (a just-uploaded file may not be
    /// visible yet). Pure predicate — same completeness rule the Event path applies.
    /// </summary>
    private static bool IsResolutionIncomplete(
        IReadOnlyList<string>? requestedFileIds,
        IReadOnlyList<ChatSessionFile> targetFiles)
        => requestedFileIds is { Count: > 0 }
            ? targetFiles.Count < requestedFileIds.Count
            : targetFiles.Count == 0;

    /// <summary>
    /// Resolve the effective target files: the explicit args subset filtered against the
    /// session manifest (unknown ids ignored), else the full manifest (FR-08).
    /// </summary>
    private static IReadOnlyList<ChatSessionFile> ResolveTargetFiles(
        IReadOnlyList<string>? requestedFileIds,
        IReadOnlyList<ChatSessionFile> uploadedFiles)
    {
        if (requestedFileIds is not { Count: > 0 })
        {
            return uploadedFiles;
        }

        return uploadedFiles
            .Where(f => requestedFileIds.Contains(f.FileId, StringComparer.Ordinal))
            .ToList();
    }
}

/// <summary>
/// Request shape consumed by <see cref="SessionDispatchOrchestrator.DispatchAsync"/>.
/// </summary>
/// <param name="TenantId">Tenant ID (ADR-014). Required.</param>
/// <param name="SessionId">Chat session ID (task 004 manifest key). Required.</param>
/// <param name="BindingId">
/// The <c>sprk_playbookconsumer</c> row GUID carried by the clicked chip — the ONLY
/// routing datum (ADR-039 D4). Required (non-empty).
/// </param>
/// <param name="Args">
/// The chip's <c>args</c> forwarded verbatim from the client. P1 vocabulary:
/// <c>fileIds</c> (string array). Unknown members are ignored (the Action's
/// <c>sprk_inputschema</c> owns the future vocabulary). Null = no args.
/// </param>
/// <param name="CorrelationId">Optional correlation ID propagated to the run context (NFR-17).</param>
/// <param name="ActingUserEmail">
/// The acting user's email, resolved from the caller's auth claims at the dispatch endpoint
/// (E-30). Flows to <c>CodedWorkflowContext.ActingUserEmail</c> so a coded workflow can address a
/// NOTIFICATION email to the user server-side (never a client-supplied recipient). Null for the
/// prompted path and for callers that do not resolve it (backward-compatible default).
/// </param>
public sealed record SessionDispatchRequest(
    string TenantId,
    string SessionId,
    Guid BindingId,
    JsonElement? Args,
    string? CorrelationId = null,
    string? ActingUserEmail = null);

/// <summary>
/// A Click dispatch that was refused at the catalog-resolution boundary (ADR-039:
/// reject unknown ids with a clean error — no fallback). Thrown BEFORE the first
/// stream chunk; <c>DispatchSessionEndpoint</c> maps it to a ProblemDetails with the
/// stable <see cref="ErrorCode"/> and <see cref="StatusCode"/>.
/// </summary>
public sealed class DispatchRejectedException : Exception
{
    /// <summary>No enabled <c>sprk_playbookconsumer</c> row has the requested id (404).</summary>
    public const string BindingNotFound = "dispatch.binding-not-found";

    /// <summary>The Binding targets a kind the P1 dispatch path cannot execute (422).</summary>
    public const string ActionKindUnsupported = "dispatch.action-kind-unsupported";

    /// <summary>The Binding declares a disposition whose routing leg the dispatch path cannot execute yet (422).</summary>
    public const string DispositionUnsupported = "dispatch.disposition-not-supported";

    /// <summary>Stable machine error code (ADR-019 ProblemDetails extension).</summary>
    public string ErrorCode { get; }

    /// <summary>HTTP status the endpoint maps this rejection to.</summary>
    public int StatusCode { get; }

    public DispatchRejectedException(string errorCode, int statusCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
