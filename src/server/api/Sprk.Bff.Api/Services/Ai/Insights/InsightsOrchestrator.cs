using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Insights.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Phase 1 Zone A implementation of <see cref="IInsightsAi"/> — orchestrates the
/// synthesis path (<see cref="AnswerQuestionAsync"/> via D-P13 cache +
/// <see cref="IPlaybookExecutionEngine"/>), the ingest path
/// (<see cref="RunIngestAsync"/> via the universal ingest playbook), and embedding
/// generation (<see cref="EmbedTextAsync"/> via <see cref="IOpenAiClient"/>) for
/// Zone B callers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/</c> and freely imports
/// AI internals (<see cref="IPlaybookExecutionEngine"/>, <see cref="IOpenAiClient"/>,
/// <see cref="IInsightsPlaybookExecutionCache"/>). Zone B callers receive
/// <see cref="IInsightsAi"/> via DI and have no visibility into any of these types.
/// </para>
/// <para>
/// <b>Phase 1 implementation status</b>:
/// <list type="bullet">
///   <item><see cref="EmbedTextAsync"/> — <b>complete</b> (thin delegation to
///   <see cref="IOpenAiClient.GenerateEmbeddingAsync"/>); unblocks task 041 Step 3.</item>
///   <item><see cref="AnswerQuestionAsync"/> — <b>scaffold</b>: cache + engine wiring is
///   in place, but the full D-P15 wiring (subject → <c>DocumentIds[]</c> mapping for
///   <see cref="PlaybookRunRequest"/>, <c>NotImplementedException</c> on decline path,
///   D-P14 playbook id resolution) lands with task 061 (D-P15 endpoint) and task 060
///   (D-P14 synthesis playbook). The implementation here invokes the cache directly so
///   that as soon as a D-P14 playbook id is supplied as <see cref="InsightsAgentRequest.Question"/>
///   the wiring is honest.</item>
///   <item><see cref="RunIngestAsync"/> — <b>complete</b> (task 040): delegates to
///   <see cref="IIngestOrchestrator"/> which composes Sanitizer → Layer 1 → conditional
///   Layer 2 → GroundingVerifier → ConfidenceThreshold → ObservationEmitter →
///   IndexUpsert → Mirror. D-P11 mirror is wired as <see cref="Mirror.IObservationMirror"/>
///   seam; Phase 1 ships <see cref="Mirror.NoOpObservationMirror"/>; task 051 swaps in
///   the real Dataverse impl.</item>
/// </list>
/// </para>
/// <para>
/// <b>ADR-013 + ADR-009 + ADR-010 compliance</b>:
/// <list type="bullet">
///   <item>ADR-013: Zone A; single facade implementation behind the IInsightsAi seam.</item>
///   <item>ADR-009: synthesis path goes through the D-P13 Redis cache
///   (<see cref="IInsightsPlaybookExecutionCache"/>); no direct
///   <see cref="IDistributedCache"/> usage here.</item>
///   <item>ADR-010: single concrete impl behind the interface; registered Singleton
///   (all dependencies are themselves Singleton or thread-safe Scoped-resolved per call).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class InsightsOrchestrator : IInsightsAi
{
    /// <summary>
    /// Output variable name on the <c>emitObservations</c> node in
    /// <c>universal-ingest@v1</c> (per <c>universal-ingest.playbook.json</c>). The
    /// adapter reads the node's <see cref="NodeOutput.StructuredData"/> as
    /// <see cref="ObservationEmissionResult"/> and projects to
    /// <see cref="InsightsIngestResult"/>. Mismatch (e.g., playbook renamed the variable)
    /// will cause the adapter to log a Warning and surface an empty result.
    /// </summary>
    private const string EmitObservationsOutputVariable = "emission";

    /// <summary>
    /// Output variable name on the <c>layer1Classify</c> node. Used by the adapter as
    /// the fallback source of the <c>Layer1Classification</c> field when the emission
    /// node did not run (e.g., sanitize-empty short-circuit) — preserves r1 parity where
    /// <c>InsightsIngestResult.Layer1Classification</c> is populated whenever Layer 1
    /// ran successfully.
    /// </summary>
    private const string Layer1OutputVariable = "layer1";

    private static readonly EventId IngestRoutedToPlaybookEvent = new(8060, "InsightsRunIngestRoutedToPlaybook");
    private static readonly EventId IngestRoutedToLegacyEvent = new(8061, "InsightsRunIngestRoutedToLegacy");
    private static readonly EventId IngestPlaybookAdapterMismatchEvent = new(8062, "InsightsRunIngestPlaybookAdapterMismatch");
    private static readonly EventId IngestPlaybookFailedEvent = new(8063, "InsightsRunIngestPlaybookFailed");

    private readonly IPlaybookExecutionEngine _engine;
    private readonly IInsightsPlaybookExecutionCache _cache;
    private readonly IOpenAiClient _openAi;
    private readonly IIngestOrchestrator _ingestOrchestrator;
    private readonly IPlaybookOrchestrationService _playbookOrchestration;
    private readonly IIngestDocumentSource _ingestDocumentSource;
    private readonly IOptions<InsightsIngestOptions> _ingestOptions;
    private readonly ILogger<InsightsOrchestrator> _logger;

    public InsightsOrchestrator(
        IPlaybookExecutionEngine engine,
        IInsightsPlaybookExecutionCache cache,
        IOpenAiClient openAi,
        IIngestOrchestrator ingestOrchestrator,
        IPlaybookOrchestrationService playbookOrchestration,
        IIngestDocumentSource ingestDocumentSource,
        IOptions<InsightsIngestOptions> ingestOptions,
        ILogger<InsightsOrchestrator> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _ingestOrchestrator = ingestOrchestrator ?? throw new ArgumentNullException(nameof(ingestOrchestrator));
        _playbookOrchestration = playbookOrchestration ?? throw new ArgumentNullException(nameof(playbookOrchestration));
        _ingestDocumentSource = ingestDocumentSource ?? throw new ArgumentNullException(nameof(ingestDocumentSource));
        _ingestOptions = ingestOptions ?? throw new ArgumentNullException(nameof(ingestOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InsightsAgentResult> AnswerQuestionAsync(
        InsightsAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccessibleScopeHash);

        var sw = Stopwatch.StartNew();

        // Derive well-known template variables from Subject so playbook node ConfigJson
        // can reference them as {{matterId}} etc. without callers having to supply them
        // redundantly. Per Insights Engine r2 Wave B (2026-06-02 smoke trace): playbook nodes
        // like resolveLiveFacts have configJson `"subject": "matter:{{matterId}}"` — without
        // this enrichment the literal "{{matterId}}" was passed to LiveFactResolver, which
        // rejected the request as InvalidConfiguration. Wave B5 SC-01 unblock.
        IReadOnlyDictionary<string, string>? enrichedParameters = EnrichParametersFromSubject(
            request.Parameters, request.Subject);

        var cacheRequest = new InsightsPlaybookExecutionRequest(
            PlaybookId: request.Question,
            Subject: request.Subject,
            Parameters: enrichedParameters,
            AccessibleScopeHash: request.AccessibleScopeHash,
            TenantId: request.TenantId,
            Ttl: null); // Defer to InsightsPlaybookExecutionCache.DefaultTtl (D-P13: 5 min).

        // Cache wraps the engine. On HIT the engine factory is never invoked.
        // On MISS the cache invokes the factory which drives ExecuteBatchAsync and
        // drains the stream looking for BOTH the ReturnInsightArtifactNode output (D-P12)
        // and the DeclineToFindNode output (task 071). The result carries whichever path
        // the playbook took.
        bool factoryWasCalled = false;

        var runResult = await _cache.GetOrExecuteAsync(
            cacheRequest,
            engineInvocation: ct =>
            {
                factoryWasCalled = true;
                return _engine.ExecuteBatchAsync(
                    new PlaybookRunRequest
                    {
                        PlaybookId = request.Question,
                        // Phase 1 scaffold note: D-P15 endpoint (task 061) is responsible
                        // for resolving subject → relevant document ids before calling the
                        // facade. The synthesis playbook (D-P14) uses LiveFactNode +
                        // IndexRetrieveNode for cohort retrieval; it does NOT process
                        // ad-hoc DocumentIds. We pass an empty array to satisfy the
                        // engine's required[] contract; the playbook ignores it.
                        DocumentIds = Array.Empty<Guid>(),
                        Parameters = enrichedParameters
                    },
                    ct);
            },
            cancellationToken);

        sw.Stop();
        var elapsedMs = sw.ElapsedMilliseconds;
        var cacheHit = !factoryWasCalled;

        // Path 1: artifact produced (sufficient-evidence path). Defensive "both populated"
        // case: prefer Artifact per InsightsEngineRunResult contract ("sufficient path wins").
        if (runResult.HasArtifact)
        {
            _logger.LogDebug(
                "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced artifact (cacheHit={CacheHit}, elapsedMs={ElapsedMs})",
                request.Question, request.Subject, cacheHit, elapsedMs);
            return InsightsAgentResult.Success(runResult.Artifact!, cacheHit, elapsedMs);
        }

        // Path 2: real decline produced (insufficient-evidence path). Per task 071, the
        // cache now extracts DeclineToFindNode output from the engine stream and surfaces
        // it here with structured MinimumEvidenceNeeded gap analysis from upstream
        // EvidenceSufficiencyNode — no more scaffold "no-artifact-produced" for this case.
        // CacheHit is ALWAYS false on the decline path because declines are never cached
        // (evidence sufficiency depends on the current state of the index).
        if (runResult.HasDecline)
        {
            _logger.LogDebug(
                "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced decline (reason={Reason}, gaps={GapCount}, elapsedMs={ElapsedMs})",
                request.Question, request.Subject, runResult.Decline!.Reason,
                runResult.Decline.MinimumEvidenceNeeded.Count, elapsedMs);
            return InsightsAgentResult.Declined(runResult.Decline, cacheHit: false, elapsedMs);
        }

        // Path 3 (defensive): engine produced neither artifact nor decline. This should not
        // happen with a well-formed playbook (EvidenceSufficiencyNode's branch routing
        // guarantees exactly one terminal node fires). If it does — malformed playbook,
        // engine error masked, branch routing bug — log Warning and emit a scaffold decline
        // so the facade contract's "exactly one of artifact/decline" invariant holds for
        // Zone B callers. The scaffold's ConfidenceInDecline = 0.0 signals "this is not a
        // real decline verdict" to observability tooling.
        _logger.LogWarning(
            "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced NO artifact AND NO decline; engine may be misconfigured. Returning scaffold decline. (cacheHit={CacheHit}, elapsedMs={ElapsedMs})",
            request.Question, request.Subject, cacheHit, elapsedMs);

        return InsightsAgentResult.Declined(
            new DeclineResponse
            {
                Reason = "no-artifact-produced",
                Explanation = "The playbook completed without emitting an InsightArtifact or DeclineResponse. This indicates a malformed playbook (missing terminal node), an engine error masked by node validation, or a branch-routing bug. Operations: inspect the playbook's terminal nodes (ReturnInsightArtifactNode + DeclineToFindNode) and EvidenceSufficiencyNode branch wiring.",
                MinimumEvidenceNeeded = new Dictionary<string, object>(),
                SuggestedActions = Array.Empty<string>(),
                ConfidenceInDecline = 0.0 // Sentinel: not a real decline verdict
            },
            cacheHit,
            elapsedMs);
    }

    /// <inheritdoc />
    public async Task<InsightsIngestResult> RunIngestAsync(
        InsightsIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MatterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        // Phase 1.5 r2 Wave C5 (task 024): validate optional parameter overrides at the
        // facade boundary so that bad inputs fail fast with a clear ArgumentException
        // before any AI cost is incurred. The validation enforces well-formedness only;
        // domain validation (e.g., practice-area code exists in sprk_practicearea_ref)
        // happens downstream in the playbook node executor where Dataverse is reachable.
        // See design-a5-universal-ingest-jps.md §6 for the parameter contract.
        ValidateIngestParameters(request);

        // Phase 1.5 r2 Wave C4 (task 023): route between the universal-ingest@v1 JPS
        // playbook (new) and the legacy code-defined IIngestOrchestrator (Phase 1) based
        // on InsightsIngestOptions. The flag IS a runtime choice between two real
        // implementations — both are unconditionally registered in DI per ADR-030 +
        // bff-extensions §F.1 (NOT an asymmetric-registration pattern). Until Wave C-G4
        // (task 022) retires the legacy path, the orchestrator keeps both wired so
        // operators have a safe rollback during the C1+C2+C4 multi-deploy window.
        //
        // Fall-back conditions:
        //   1) UseUniversalIngestPlaybook = false  (operator override / kill-switch)
        //   2) UniversalIngestPlaybookId = Guid.Empty  (env not yet configured)
        // Either triggers the legacy path with a single Information log so observability
        // tooling can attribute the chosen path unambiguously. NO silent fall-through.
        var options = _ingestOptions.Value;
        var routeToPlaybook = options.UseUniversalIngestPlaybook
            && options.UniversalIngestPlaybookId != Guid.Empty;

        if (!routeToPlaybook)
        {
            _logger.Log(
                LogLevel.Information,
                IngestRoutedToLegacyEvent,
                "InsightsOrchestrator.RunIngestAsync routed to legacy IngestOrchestrator (documentId={DocumentId} matterId={MatterId} tenantId={TenantId} useUniversalIngestPlaybook={Flag} playbookIdConfigured={IdConfigured})",
                request.DocumentId, request.MatterId, request.TenantId,
                options.UseUniversalIngestPlaybook,
                options.UniversalIngestPlaybookId != Guid.Empty);
            return await _ingestOrchestrator.RunAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        return await RunIngestViaPlaybookAsync(request, options.UniversalIngestPlaybookId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Wave C4 (task 023) — invoke <c>universal-ingest@v1</c> via
    /// <see cref="IPlaybookOrchestrationService.ExecuteAppOnlyAsync"/>, drain the stream,
    /// and adapt the final <c>emitObservations</c> node output to
    /// <see cref="InsightsIngestResult"/>. The adapter preserves the r1 facade contract
    /// — Zone B callers see no surface change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Path</b>: IIngestDocumentSource.FetchAsync → assemble parameters →
    /// IPlaybookOrchestrationService.ExecuteAppOnlyAsync(universal-ingest@v1, parameters)
    /// → drain stream → find emission node output → project to <see cref="InsightsIngestResult"/>.
    /// </para>
    /// <para>
    /// <b>Why ExecuteAppOnlyAsync, not IPlaybookExecutionEngine.ExecuteBatchAsync</b>: the
    /// ingest path is invoked from a background <c>BackgroundService</c> (D-P8 SPE-upload
    /// consumer / Service Bus). <see cref="IPlaybookExecutionEngine.ExecuteBatchAsync"/>
    /// requires an <see cref="Microsoft.AspNetCore.Http.HttpContext"/> (per
    /// <c>PlaybookExecutionEngine</c> line 91); app-only invocation explicitly avoids it.
    /// </para>
    /// <para>
    /// <b>Failure handling</b>: on playbook failure (engine throws OR emits
    /// <c>RunFailed</c>) we log Error and fall through to the legacy path. This is
    /// deliberate — the legacy code path is proven and the playbook is new; a transient
    /// playbook-engine bug should not crash ingest. Observability tooling can alert on
    /// the failure event. When Wave C-G4 (task 022) retires the legacy path, this
    /// fallback drops and playbook failures propagate as exceptions per the dead-letter
    /// pattern (D-P8 consumer policy).
    /// </para>
    /// </remarks>
    private async Task<InsightsIngestResult> RunIngestViaPlaybookAsync(
        InsightsIngestRequest request,
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Step 1: Fetch document content (Wave C4 owns the fetch per design-a5 §4
        // Node 1 — the executor receives pre-fetched text + chunks via parameters).
        // Null result = non-indexable document; the playbook will short-circuit at
        // sanitize. Match the r1 early-return semantics: produce an empty result so
        // Zone B callers see the same "0 observations, no Layer 1" outcome.
        var content = await _ingestDocumentSource.FetchAsync(
            request.DocumentId, request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (content is null)
        {
            _logger.LogInformation(
                "InsightsOrchestrator.RunIngestAsync (playbook path): document not indexable, returning empty result (documentId={DocumentId} tenantId={TenantId})",
                request.DocumentId, request.TenantId);
            return new InsightsIngestResult(
                ObservationsEmitted: 0,
                Layer1Classification: null,
                Layer2Triggered: false);
        }

        // Step 2: Assemble playbook parameters per design-a5 §6 parameterSchema.
        // Required: documentId, matterId, tenantId, documentText, chunksJson, documentRef.
        // Optional: practiceAreaHint, costCapOverride, layer2Threshold (null → omit,
        // playbook schema defaults apply per §6).
        var parameters = AssemblePlaybookParameters(request, content);

        // Step 3: Invoke the playbook via app-only orchestration. ExecuteAppOnlyAsync is
        // the correct entry — it does NOT require HttpContext (D-P8 dispatch is a
        // BackgroundService scope, not a request scope).
        var runRequest = new PlaybookRunRequest
        {
            PlaybookId = playbookId,
            // Universal-ingest does NOT process ad-hoc DocumentIds (each playbook node
            // reads from parameters.documentText / parameters.chunks). Pass an empty array
            // to satisfy the required[] contract; nodes ignore it.
            DocumentIds = Array.Empty<Guid>(),
            Parameters = parameters
        };

        _logger.Log(
            LogLevel.Information,
            IngestRoutedToPlaybookEvent,
            "InsightsOrchestrator.RunIngestAsync routed to universal-ingest@v1 playbook (playbookId={PlaybookId} documentId={DocumentId} matterId={MatterId} tenantId={TenantId})",
            playbookId, request.DocumentId, request.MatterId, request.TenantId);

        ObservationEmissionResult? emission = null;
        string? layer1Classification = null;
        string? runFailedError = null;

        try
        {
            await foreach (var evt in _playbookOrchestration.ExecuteAppOnlyAsync(
                runRequest, request.TenantId, cancellationToken).ConfigureAwait(false))
            {
                switch (evt.Type)
                {
                    case PlaybookEventType.NodeCompleted when evt.NodeOutput is { Success: true }:
                        // Capture the emission node output (project to InsightsIngestResult below).
                        if (string.Equals(evt.NodeOutput.OutputVariable, EmitObservationsOutputVariable, StringComparison.Ordinal))
                        {
                            emission = evt.NodeOutput.GetData<ObservationEmissionResult>();
                        }
                        // Capture the layer1 classification as a fallback (used if the emission node
                        // did not run — e.g., sanitize short-circuit short of the L1+gate path).
                        else if (string.Equals(evt.NodeOutput.OutputVariable, Layer1OutputVariable, StringComparison.Ordinal))
                        {
                            layer1Classification = ExtractLayer1Classification(evt.NodeOutput);
                        }
                        break;

                    case PlaybookEventType.RunFailed:
                        runFailedError = evt.Error ?? "Playbook run failed (no error message provided)";
                        break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Log(
                LogLevel.Error,
                IngestPlaybookFailedEvent,
                ex,
                "InsightsOrchestrator.RunIngestAsync (playbook path) threw: playbookId={PlaybookId} documentId={DocumentId} matterId={MatterId} tenantId={TenantId} elapsedMs={ElapsedMs} — falling back to legacy IngestOrchestrator.",
                playbookId, request.DocumentId, request.MatterId, request.TenantId, sw.ElapsedMilliseconds);
            return await _ingestOrchestrator.RunAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        sw.Stop();

        if (runFailedError is not null)
        {
            _logger.Log(
                LogLevel.Error,
                IngestPlaybookFailedEvent,
                "InsightsOrchestrator.RunIngestAsync (playbook path) emitted RunFailed: playbookId={PlaybookId} documentId={DocumentId} matterId={MatterId} tenantId={TenantId} error={Error} elapsedMs={ElapsedMs} — falling back to legacy IngestOrchestrator.",
                playbookId, request.DocumentId, request.MatterId, request.TenantId, runFailedError, sw.ElapsedMilliseconds);
            return await _ingestOrchestrator.RunAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        return AdaptPlaybookResult(emission, layer1Classification, request, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Build the playbook parameters dictionary per design-a5 §6 parameterSchema. All
    /// values are stringified — the playbook engine's template substitution operates on
    /// the string form. Optional overrides (PracticeAreaHint, CostCapOverride,
    /// Layer2Threshold) are omitted when null so the parameterSchema defaults apply.
    /// </summary>
    /// <remarks>
    /// <b>Invariant culture for numerics</b> — currency caps and thresholds must
    /// serialize as <c>1.50</c>, <c>0.7</c> (not <c>1,50</c> / <c>0,7</c>) regardless of
    /// the App Service host culture. ALL invariant-culture formatting throughout.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> AssemblePlaybookParameters(
        InsightsIngestRequest request,
        IngestDocumentContent content)
    {
        // Serialize chunks as a JSON array per the SanitizerNodeExecutor contract
        // (parameters.chunksJson is parsed back into a JsonElement[] by the executor).
        var chunksJson = JsonSerializer.Serialize(content.Chunks);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Required (per design-a5 §6 parameterSchema "required" array).
            ["documentId"] = request.DocumentId,
            ["matterId"] = request.MatterId,
            ["tenantId"] = request.TenantId,
            // Required (consumed by SanitizerNodeExecutor + emitObservations executor —
            // these are NOT in the schema "required" array because the design assumes
            // they're injected by the facade, not supplied by upstream callers).
            ["documentText"] = content.FullText,
            ["chunksJson"] = chunksJson,
            ["documentRef"] = content.DocumentRef
        };

        // Optional overrides — omit (rather than send null/empty) so the playbook
        // parameterSchema's default values apply per design-a5 §6:
        //   layer2Threshold default = 0.7 (Phase 1 D-59)
        //   practiceAreaHint default = null (litigation-default prompts)
        //   costCapOverride default = null (tenant monthly cap from D-P9)
        if (!string.IsNullOrWhiteSpace(request.PracticeAreaHint))
        {
            parameters["practiceAreaHint"] = request.PracticeAreaHint;
        }
        if (request.CostCapOverride is { } cap)
        {
            parameters["costCapOverride"] = cap.ToString(CultureInfo.InvariantCulture);
        }
        if (request.Layer2Threshold is { } threshold)
        {
            parameters["layer2Threshold"] = threshold.ToString(CultureInfo.InvariantCulture);
        }

        return parameters;
    }

    /// <summary>
    /// Project the playbook's <see cref="ObservationEmissionResult"/> (from the
    /// <c>emitObservations</c> node — per Wave C1 task 020 shape-matched to
    /// <see cref="InsightsIngestResult"/>) to the facade's <see cref="InsightsIngestResult"/>.
    /// Handles the defensive case where the emission node did not run (e.g., the playbook
    /// short-circuited at sanitize-empty) by surfacing the Layer 1 classification when
    /// available + zero observations.
    /// </summary>
    private InsightsIngestResult AdaptPlaybookResult(
        ObservationEmissionResult? emission,
        string? layer1ClassificationFallback,
        InsightsIngestRequest request,
        long elapsedMs)
    {
        if (emission is not null)
        {
            _logger.LogInformation(
                "InsightsOrchestrator.RunIngestAsync (playbook path) completed: documentId={DocumentId} layer1Classification={Layer1Classification} layer2Triggered={Layer2Triggered} observationsEmitted={ObservationsEmitted} elapsedMs={ElapsedMs}",
                request.DocumentId, emission.Layer1Classification, emission.Layer2Triggered,
                emission.ObservationsEmitted, elapsedMs);
            return new InsightsIngestResult(
                ObservationsEmitted: emission.ObservationsEmitted,
                Layer1Classification: emission.Layer1Classification,
                Layer2Triggered: emission.Layer2Triggered);
        }

        // Defensive path — playbook ran to completion (no RunFailed) but no emission
        // output captured. Most likely cause: sanitize short-circuited (SANITIZE_EMPTY)
        // and downstream nodes skipped — semantically equivalent to r1's
        // "sanitized-empty" early return. Surface the Layer 1 classification if we
        // captured it (we usually won't on this path); otherwise emit empty.
        _logger.Log(
            LogLevel.Warning,
            IngestPlaybookAdapterMismatchEvent,
            "InsightsOrchestrator.RunIngestAsync (playbook path) completed without an emitObservations output (documentId={DocumentId} layer1ClassificationFallback={Layer1Classification} elapsedMs={ElapsedMs}). Most likely cause: sanitize short-circuit (SANITIZE_EMPTY). Surfacing empty result to preserve r1 parity.",
            request.DocumentId, layer1ClassificationFallback, elapsedMs);
        return new InsightsIngestResult(
            ObservationsEmitted: 0,
            Layer1Classification: layer1ClassificationFallback,
            Layer2Triggered: false);
    }

    /// <summary>
    /// Extract the <c>classification</c> string from a Layer 1 node output (case-insensitive).
    /// Mirrors <see cref="ObservationEmitterNodeExecutor"/>'s extraction logic so the
    /// fallback path produces the same field value as the canonical emission path.
    /// </summary>
    private static string? ExtractLayer1Classification(NodeOutput layer1)
    {
        if (layer1.StructuredData is null) return null;
        var data = layer1.StructuredData.Value;
        if (data.ValueKind != JsonValueKind.Object) return null;

        if (data.TryGetProperty("classification", out var camel) && camel.ValueKind == JsonValueKind.String)
            return camel.GetString();
        if (data.TryGetProperty("Classification", out var pascal) && pascal.ValueKind == JsonValueKind.String)
            return pascal.GetString();
        return null;
    }

    /// <summary>
    /// Validate the Wave C5 optional parameter overrides on
    /// <see cref="InsightsIngestRequest"/>. Throws <see cref="ArgumentException"/> on
    /// any malformed override; returns silently when overrides are absent or valid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rules per task 024 POML §step 2 + design-a5 §6 parameterSchema:
    /// <list type="bullet">
    ///   <item><c>PracticeAreaHint</c> — when supplied, must be non-whitespace. Domain
    ///   validation against <c>sprk_practicearea_ref</c> codes happens in the playbook
    ///   node executor (Wave D2) where Dataverse is reachable; the facade only enforces
    ///   well-formedness.</item>
    ///   <item><c>CostCapOverride</c> — when supplied, must be strictly positive
    ///   (&gt; 0). Zero or negative caps are nonsensical; the schema default
    ///   (<c>null</c>) means "use tenant monthly cap from D-P9".</item>
    ///   <item><c>Layer2Threshold</c> — when supplied, must lie in <c>[0.0, 1.0]</c>
    ///   inclusive. NaN and infinity are rejected. The schema default (<c>null</c>)
    ///   means "use playbook default 0.7 per Phase 1 D-59".</item>
    /// </list>
    /// </para>
    /// <para>
    /// Validation runs <em>before</em> any work is dispatched so that bad inputs fail
    /// without incurring LLM cost or Dataverse round-trips. Internal-visible for unit
    /// tests; not part of the public Zone B surface.
    /// </para>
    /// </remarks>
    internal static void ValidateIngestParameters(InsightsIngestRequest request)
    {
        if (request.PracticeAreaHint is not null
            && string.IsNullOrWhiteSpace(request.PracticeAreaHint))
        {
            throw new ArgumentException(
                "PracticeAreaHint, when supplied, must be a non-whitespace practice-area code (e.g., 'CTRNS'). Pass null to omit and use the litigation-default per Phase 1 D-59.",
                nameof(request));
        }

        if (request.CostCapOverride is { } cap && cap <= 0m)
        {
            throw new ArgumentException(
                $"CostCapOverride, when supplied, must be strictly positive (got {cap}). Pass null to omit and use the tenant's monthly cap per D-P9.",
                nameof(request));
        }

        if (request.Layer2Threshold is { } threshold)
        {
            if (double.IsNaN(threshold) || double.IsInfinity(threshold)
                || threshold < 0.0 || threshold > 1.0)
            {
                throw new ArgumentException(
                    $"Layer2Threshold, when supplied, must be a finite value in [0.0, 1.0] (got {threshold}). Pass null to omit and use the playbook default 0.7 per Phase 1 D-59.",
                    nameof(request));
            }
        }
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> EmbedTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Thin delegation to IOpenAiClient. Model + dimensions are left at the
        // client's configured defaults (text-embedding-3-large, 3072 dims) per
        // SPEC §3.4 / D-P2 — the spaarke-insights-index schema is fixed at 3072
        // and the facade is opinionated for substrate consistency. Callers
        // (e.g., D-P4 PrecedentProjectionSync per task 041) do not choose.
        return _openAi.GenerateEmbeddingAsync(
            text,
            model: null,
            dimensions: null,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enrich the caller-supplied Parameters with well-known template variables derived
    /// from the Subject ref, so playbook node ConfigJson can reference them as
    /// <c>{{matterId}}</c>, <c>{{projectId}}</c>, etc. without callers having to supply
    /// them redundantly alongside Subject. Phase 1 supports the <c>matter:</c> scheme;
    /// Phase 1.5 multi-entity work (Wave D5) extends to <c>project:</c> / <c>invoice:</c>.
    /// </summary>
    /// <remarks>
    /// Caller-supplied Parameters take precedence — if the caller explicitly supplied
    /// "matterId" the derived value does NOT overwrite it. This preserves the
    /// power-user override path while making the common case work without ceremony.
    /// Returns a NEW dictionary; never mutates the caller's collection.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string>? EnrichParametersFromSubject(
        IReadOnlyDictionary<string, string>? callerParameters,
        string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return callerParameters;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (callerParameters is not null)
        {
            foreach (var kvp in callerParameters)
                merged[kvp.Key] = kvp.Value;
        }

        // Format: "<scheme>:<id>" — Phase 1 supports "matter:<guid>".
        var colonIdx = subject.IndexOf(':');
        if (colonIdx > 0 && colonIdx < subject.Length - 1)
        {
            var scheme = subject[..colonIdx].Trim().ToLowerInvariant();
            var id = subject[(colonIdx + 1)..].Trim();
            if (id.Length > 0)
            {
                // Well-known key per scheme. Caller-supplied value wins via TryAdd.
                var key = scheme switch
                {
                    "matter" => "matterId",
                    "project" => "projectId",
                    "invoice" => "invoiceId",
                    _ => null
                };
                if (key is not null && !merged.ContainsKey(key))
                    merged[key] = id;
            }
        }

        return merged;
    }
}
