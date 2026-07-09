using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
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
/// <b>P1 execution envelope</b>: prompted Actions with Informational disposition only.
/// Non-prompted kinds and non-informational dispositions reject PRE-RUN with stable
/// error codes (cheaper and more honest than letting <see cref="OutputRouter"/>'s
/// loud P3 stubs throw after the LLM spend). The P2/P3 phases widen this envelope
/// where the coded-workflow executor and the remaining disposition legs land.
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
    private readonly ISessionFileTextSource _sessionFileTextSource;
    private readonly IOutputRouter _outputRouter;
    private readonly PendingPlanManager _pendingPlanManager;
    private readonly EventRulesOptions _manifestProbeOptions;
    private readonly Sprk.Bff.Api.Telemetry.AiTelemetry _aiTelemetry;
    private readonly ILogger<SessionDispatchOrchestrator> _logger;

    public SessionDispatchOrchestrator(
        ChatSessionManager sessionManager,
        IConsumerRoutingService consumerRouting,
        IScopeResolverService scopeResolver,
        IActionRunner actionRunner,
        ISessionFileTextSource sessionFileTextSource,
        IOutputRouter outputRouter,
        PendingPlanManager pendingPlanManager,
        IOptions<EventRulesOptions> manifestProbeOptions,
        Sprk.Bff.Api.Telemetry.AiTelemetry aiTelemetry,
        ILogger<SessionDispatchOrchestrator> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
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
        _sessionFileTextSource = null!;
        _outputRouter = null!;
        _pendingPlanManager = null!;
        _manifestProbeOptions = null!;
        _aiTelemetry = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        if (binding.ActionKind != ActionKind.Prompted)
        {
            throw new DispatchRejectedException(
                DispatchRejectedException.ActionKindUnsupported,
                StatusCodes.Status422UnprocessableEntity,
                $"The requested binding targets an Action of kind '{binding.ActionKind}'; " +
                "the P1 dispatch path executes prompted Actions only.");
        }

        // Disposition envelope: the dispatch seam executes the dispositions whose
        // OutputRouter legs are IMPLEMENTED — Informational (P1 task 021) and WorkProduct
        // (FR-P3-08 task 047: host-record persistence). Rejecting the still-stubbed legs
        // PRE-RUN is cheaper and more honest than letting OutputRouter's loud stubs throw
        // after the LLM spend. (Email is deliberately NOT dispatchable here — its only
        // consumer is the coded briefing composite, which routes directly; see task 043.)
        if (binding.Disposition is not (BindingDisposition.Informational or BindingDisposition.WorkProduct))
        {
            throw new DispatchRejectedException(
                DispatchRejectedException.DispositionUnsupported,
                StatusCodes.Status422UnprocessableEntity,
                $"The requested binding declares disposition '{binding.Disposition}'; " +
                "only informational and work_product outputs execute on the dispatch path.");
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
        // non-empty (default-all), then degrade to whatever resolved. Task.Delay matches
        // the Event-path precedent; the TimeProvider refactor is on the /defer list.
        if (IsResolutionIncomplete(requestedFileIds, targetFiles))
        {
            for (var attempt = 1; attempt <= _manifestProbeOptions.ReadinessProbeAttempts; attempt++)
            {
                await Task.Delay(Math.Max(0, _manifestProbeOptions.ReadinessProbeDelayMs), cancellationToken)
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
            yield return AnalysisChunk.FromError(
                "No session files were available for this action. Upload a file first, or pass a valid fileId subset.");
            yield break;
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
            yield return AnalysisChunk.FromError(fetchError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(textResult.ExtractedText))
        {
            yield return AnalysisChunk.FromError(
                "Session files contained no text to analyze. The RAG index may still be catching up — try again in a few seconds.");
            yield break;
        }

        // ── Prompted executor: ActionRunner renders the Action's JPS SystemPrompt via
        // PromptSchemaRenderer and calls the structured completion against the Action's
        // output schema — identical execution stack to tasks 020/022.
        var documentText = new DocumentText
        {
            DocumentId = null,
            FileName = textResult.DisplayName,
            ExtractedText = textResult.ExtractedText,
        };
        var runContext = new LinearRunContext
        {
            ConsumerType = binding.ConsumerType,
            CorrelationId = request.CorrelationId,
            TenantId = request.TenantId,
        };

        JsonElement output = default;
        string? llmError = null;
        try
        {
            output = await _actionRunner
                .RunAsync(action, documentText, runContext, cancellationToken)
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

        // FR-P4-05 per-tenant metering (task 054): one capability-invocation increment at
        // THE dispatch seam — covers chip clicks, the loop's BindingCapabilityTool, gate
        // resolution, and /summarize (they all converge here). User + entry-path come from
        // the ambient AiMeteringContext scope set at the entry endpoints (text turns set
        // "text"; the HTTP dispatch endpoints set "click"); the capability identifier is
        // the bounded catalog ucid/consumer-type — identifiers/counts only (NFR-07).
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

        // ── ADR-040 SEAM (FR-P1-02): the universal ledger write BEFORE render. The
        // OutputRouter writes the addressable SessionOutput ({bindingId}@t{n}) through the
        // session store FIRST, then routes by disposition; the terminal chunk below renders
        // FROM the stored entry's payload (render follows store). SourceRefs carry the
        // grounding file ids (identifiers only, NFR-07).
        var routed = await _outputRouter
            .RouteAsync(
                session,
                binding,
                output,
                sourceRefs: targetFiles.Select(f => f.FileId).ToList(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // ── FR-P2-03: a successful dispatch of this Binding resolves any pending
        // elicitation gate awaiting it — whether the completed args arrived via the loop
        // re-invoking the capability tool, the wizard surface (capture_mode: modal →
        // dispatchConsumer), or a chip click. ONE resolution point at the ONE dispatch
        // seam (ADR-039); marker written BEFORE the terminal chunk renders (ADR-040).
        await _pendingPlanManager
            .ResolveElicitationOnDispatchAsync(
                request.TenantId, request.SessionId, binding.BindingId, cancellationToken)
            .ConfigureAwait(false);

        // ── FR-A1-10 / D-F5 (task 039): explicit render-boundary assertion — the terminal
        // chunk is built EXCLUSIVELY from the entry ProgressiveRenderGuard confirms was
        // actually written to the ledger (ADR-040 storage-precedes-rendering), never from
        // the pre-store `output` local above.
        var storedEntry = ProgressiveRenderGuard.EnsureStored(routed.Entry);
        yield return DeserializeResultChunk(storedEntry.Payload.GetRawText());

        // ── Next-step chips (G-P1 UAT round-1 Defect 1 fix, 2026-07-05): the dispatched
        // Binding's curated sprk_chiptransitions follow the terminal complete chunk so the
        // conversation surface always shows the CURRENT next steps after a Click dispatch
        // (e.g. summarize → "Summarize again"). Previously only the Event path emitted
        // chips — every chip click permanently emptied the strip.
        var transitionChips = BuildTransitionChips(binding);
        if (transitionChips.Count > 0)
        {
            yield return AnalysisChunk.FromChips(transitionChips);
        }
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
    /// Deserialize the STORED ledger payload into the terminal chunk. Payloads matching
    /// the <see cref="DocumentAnalysisResult"/> shape surface as the structured
    /// <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> (the client
    /// synthesizes per-field workspace deltas from <c>result</c>); anything else
    /// degrades to a text completion — the client always sees a terminal
    /// <c>complete</c> chunk, never a silent EOF. Same wire semantics as
    /// the pre-044 summarize shell so there is ONE AnalysisChunk vocabulary.
    /// </summary>
    private static AnalysisChunk DeserializeResultChunk(string jsonContent)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<DocumentAnalysisResult>(jsonContent);
            if (doc is not null)
            {
                doc.ParsedSuccessfully = true;
                return AnalysisChunk.Completed(doc);
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
public sealed record SessionDispatchRequest(
    string TenantId,
    string SessionId,
    Guid BindingId,
    JsonElement? Args,
    string? CorrelationId = null);

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
