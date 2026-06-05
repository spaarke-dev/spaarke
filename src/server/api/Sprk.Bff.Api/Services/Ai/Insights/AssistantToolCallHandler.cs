using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Zone A handler that implements the Wave E3 Spaarke Assistant unified tool-call contract
/// (task 042 / FR-05). Consumes the intent classifier + the playbook + RAG facade methods
/// internally; returns a uniform <see cref="AssistantQueryFacadeResult"/> to the BFF facade
/// (<see cref="IInsightsAi.AssistantQueryAsync"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/</c> and freely consumes
/// <see cref="IInsightsIntentClassifier"/>, <see cref="IInsightsAi"/> (via the calling
/// orchestrator's already-resolved reference to <c>AnswerQuestionAsync</c> / <c>SearchAsync</c>
/// — actually consumed via internal helper delegates to avoid a circular DI dependency on
/// the facade interface). External Zone B consumers see ONLY <see cref="IInsightsAi.AssistantQueryAsync"/>
/// per SPEC §3.5.4.
/// </para>
/// <para>
/// <b>Contract anchor</b>: <c>projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md</c>
/// — the canonical Spaarke Assistant ↔ Insights tool-call contract. This handler is the
/// binding implementation of contract §3 (request), §4 (response), §7 (kill-switch matrix).
/// </para>
/// <para>
/// <b>Routing decision tree</b> (per contract §3.2 + §7):
/// <list type="number">
///   <item>If <c>ForceMode == "playbook"</c> → resolve playbook id + invoke playbook path; intentSource = "forceMode".</item>
///   <item>If <c>ForceMode == "rag"</c> → invoke RAG path directly; intentSource = "forceMode".</item>
///   <item>Else invoke <see cref="IInsightsIntentClassifier.ClassifyAsync"/>. If <c>BelowThreshold</c> →
///   fall back to RAG; intentSource = "classifier-fallback". Else dispatch per <c>Path</c>;
///   intentSource = "classifier".</item>
/// </list>
/// </para>
/// <para>
/// <b>Kill-switch handling</b>: this handler does NOT catch <see cref="FeatureDisabledException"/>.
/// It propagates unchanged from the underlying classifier / playbook / RAG service. The
/// endpoint converts to 503 ProblemDetails via <c>AsFeatureDisabled503()</c> with the correct
/// <c>errorCode</c> (<c>ai.insights.disabled</c>, <c>ai.rag.disabled</c>, or
/// <c>ai.intent-classification.disabled</c>).
/// </para>
/// <para>
/// <b>ADR-013 / §3.5 compliance</b>: Zone A. Consumed via the Zone B-safe facade method
/// <see cref="IInsightsAi.AssistantQueryAsync"/> only.
/// </para>
/// <para>
/// <b>ADR-032 §F.1 inspection</b>: the handler is registered alongside the real classifier
/// in the compound-AI-ON path. It has no Null-Object of its own because the only consumer
/// (<see cref="InsightsOrchestrator"/>) is itself only constructed on the compound-AI-ON
/// branch — if AI is disabled the orchestrator is replaced with the null facade path, so
/// the handler is never instantiated. Forward-compat note: when r2 wrap-up adds a Null
/// orchestrator the handler will need a mirror Null-Object.
/// </para>
/// </remarks>
public sealed class AssistantToolCallHandler
{
    /// <summary>
    /// Configuration key for the default playbook canonical name used when
    /// <c>ForceMode == "playbook"</c> and the classifier did NOT supply a hint
    /// (because the classifier was bypassed). Per contract §3.3.
    /// </summary>
    internal const string DefaultPlaybookConfigKey = "Insights:Playbooks:DefaultName";

    /// <summary>Default playbook canonical name when config key is unset.</summary>
    internal const string FallbackDefaultPlaybookName = "predict-matter-cost@v1";

    /// <summary>Intent-source telemetry value when classifier returned a routable hint.</summary>
    internal const string IntentSourceClassifier = "classifier";

    /// <summary>Intent-source telemetry value when caller supplied <c>ForceMode</c>.</summary>
    internal const string IntentSourceForceMode = "forceMode";

    /// <summary>Intent-source telemetry value when classifier returned below-threshold and the handler fell back to RAG.</summary>
    internal const string IntentSourceClassifierFallback = "classifier-fallback";

    /// <summary>Structured-envelope discriminator for the playbook artifact path.</summary>
    internal const string StructuredKindInference = "inference";

    /// <summary>Structured-envelope discriminator for the playbook decline path.</summary>
    internal const string StructuredKindDecline = "decline";

    /// <summary>Structured-envelope discriminator for the RAG path.</summary>
    internal const string StructuredKindObservation = "observation";

    /// <summary>JSON options used to serialize the <see cref="AssistantQueryFacadeResult.StructuredEnvelopeJson"/>
    /// payload. Matches the Insights envelope serialization convention (camelCase property names).</summary>
    internal static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Preserve the polymorphic InsightArtifact type discriminator already declared via attributes.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IInsightsIntentClassifier _classifier;
    private readonly IOptionsMonitor<InsightsPlaybookNameMapOptions> _playbookNameMap;
    private readonly IOptionsMonitor<AssistantCitationHrefOptions> _citationHrefOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssistantToolCallHandler> _logger;

    public AssistantToolCallHandler(
        IInsightsIntentClassifier classifier,
        IOptionsMonitor<InsightsPlaybookNameMapOptions> playbookNameMap,
        IOptionsMonitor<AssistantCitationHrefOptions> citationHrefOptions,
        IConfiguration configuration,
        ILogger<AssistantToolCallHandler> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _playbookNameMap = playbookNameMap ?? throw new ArgumentNullException(nameof(playbookNameMap));
        _citationHrefOptions = citationHrefOptions ?? throw new ArgumentNullException(nameof(citationHrefOptions));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute the Assistant tool-call request. Routes to playbook OR RAG path via classifier
    /// or <see cref="AssistantQueryFacadeRequest.ForceMode"/> override, then shapes the
    /// underlying response into the uniform <see cref="AssistantQueryFacadeResult"/>.
    /// </summary>
    /// <param name="request">The Assistant tool-call request.</param>
    /// <param name="playbookInvoker">Delegate to invoke the playbook path. Supplied by the
    /// caller (<see cref="InsightsOrchestrator"/>) so the handler does NOT take a direct
    /// dependency on the orchestrator (avoids constructor cycle).</param>
    /// <param name="ragInvoker">Delegate to invoke the RAG path. Same rationale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The uniform Assistant tool-call response.</returns>
    public async Task<AssistantQueryFacadeResult> ExecuteAsync(
        AssistantQueryFacadeRequest request,
        Func<InsightsAgentRequest, CancellationToken, Task<InsightsAgentResult>> playbookInvoker,
        Func<InsightsSearchFacadeRequest, CancellationToken, Task<InsightsSearchFacadeResult>> ragInvoker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(playbookInvoker);
        ArgumentNullException.ThrowIfNull(ragInvoker);

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ParentEntityType, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ParentEntityId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallerOid, nameof(request));

        var sw = Stopwatch.StartNew();

        // ─── Step 1: Routing decision ──────────────────────────────────────────────────
        // Per contract §3.2 + §7: forceMode dominates; null forceMode triggers classifier.
        // Factored to DecideRouteAsync so streaming + single-shot share the same routing
        // logic (Wave F task 051 / FR-05 v1.1 — avoid divergence between modes).
        var routing = await DecideRouteAsync(request, cancellationToken).ConfigureAwait(false);

        // ─── Step 2: Execute the chosen path ───────────────────────────────────────────
        // Task 051 refactored routing into RoutingDecision (PascalCase members) for the
        // streaming-path code share — adapt the dispatch to the new shape here.
        AssistantQueryFacadeResult result;
        if (routing.Path == "playbook")
        {
            result = await ExecutePlaybookPathAsync(
                request, routing.ClassifierPlaybookHint, routing.IntentSource, routing.BelowThreshold,
                playbookInvoker, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await ExecuteRagPathAsync(
                request, routing.IntentSource, routing.BelowThreshold,
                ragInvoker, cancellationToken).ConfigureAwait(false);
        }

        sw.Stop();
        // Note: DurationMs is final wall time INCLUDING classifier + path execution.
        result = result with { DurationMs = sw.ElapsedMilliseconds };

        _logger.LogInformation(
            "AssistantToolCallHandler: completed path={Path} intentSource={IntentSource} hits={HitCount} cacheHit={CacheHit} elapsedMs={ElapsedMs} tenant={TenantId} subject={Scheme}:{Id}",
            result.Path, result.IntentSource, result.HitCount, result.CacheHit, result.DurationMs,
            request.TenantId, request.ParentEntityType, request.ParentEntityId);

        return result;
    }

    // ───────────────────────────────────────────────────────────────────────────────────
    // PLAYBOOK PATH
    // ───────────────────────────────────────────────────────────────────────────────────

    private async Task<AssistantQueryFacadeResult> ExecutePlaybookPathAsync(
        AssistantQueryFacadeRequest request,
        string? classifierPlaybookHint,
        string intentSource,
        bool belowThreshold,
        Func<InsightsAgentRequest, CancellationToken, Task<InsightsAgentResult>> playbookInvoker,
        CancellationToken cancellationToken)
    {
        // Step P1: resolve playbook canonical name → Guid.
        var canonicalName = ResolvePlaybookCanonicalName(classifierPlaybookHint);
        var playbookId = _playbookNameMap.CurrentValue.ResolveOrDefault(canonicalName);

        if (playbookId == Guid.Empty)
        {
            // Per contract §5.1: 503 ai.assistant-default-playbook.unconfigured (handled at endpoint).
            throw new InvalidOperationException(
                $"Default playbook '{canonicalName}' is not configured in '{InsightsPlaybookNameMapOptions.SectionName}:Map'. " +
                "Configure the playbook Guid per-environment OR omit forceMode to let the classifier route the query.");
        }

        // Step P2: build playbook request. AccessibleScopeHash mirrors the InsightEndpoints.Ask
        // contract — sha256(tid + oid) — for cache-key stability vs the standalone /ask endpoint.
        var playbookRequest = new InsightsAgentRequest(
            Question: playbookId,
            Subject: request.Subject,
            Parameters: null,
            TenantId: request.TenantId,
            AccessibleScopeHash: ComputeAccessibleScopeHash(request.TenantId, request.CallerOid));

        // Step P3: invoke playbook via the supplied delegate (avoids handler→orchestrator cycle).
        var agentResult = await playbookInvoker(playbookRequest, cancellationToken).ConfigureAwait(false);

        // Step P4: shape the result into the uniform Assistant response.
        if (agentResult.Artifact is not null)
        {
            return BuildArtifactResponse(agentResult, canonicalName, intentSource, belowThreshold);
        }
        if (agentResult.Decline is not null)
        {
            return BuildDeclineResponse(agentResult, canonicalName, intentSource, belowThreshold);
        }

        // Defensive: per IInsightsAi.AnswerQuestionAsync contract this is unreachable. Log + surface
        // a generic decline so the Assistant has a safe envelope to render.
        _logger.LogError(
            "AssistantToolCallHandler: playbook {PlaybookId} returned neither artifact nor decline — contract violation. tenant={TenantId} subject={Subject}",
            canonicalName, request.TenantId, request.Subject);

        return new AssistantQueryFacadeResult
        {
            Path = "playbook",
            Answer = "The Insights service returned an empty response. Please retry or contact support.",
            Citations = [],
            Confidence = 0.0,
            PlaybookId = canonicalName,
            StructuredKind = StructuredKindDecline,
            StructuredEnvelopeJson = "{}",
            IntentSource = intentSource,
            ClassifierBelowThreshold = belowThreshold,
            CacheHit = false,
            HitCount = 0
        };
    }

    private AssistantQueryFacadeResult BuildArtifactResponse(
        InsightsAgentResult agentResult,
        string canonicalName,
        string intentSource,
        bool belowThreshold)
    {
        var artifact = agentResult.Artifact!;

        // Citations: project evidence refs that look document-like into the uniform shape.
        // Evidence with empty Quote becomes a citation without an excerpt (Source-only).
        // Wave F task 052 / contract v1.1: project Href when EvidenceRef carries a
        // sprk_document Guid (bare-Guid emission form). EvidenceRef.Ref in spe://drive/X/item/Y
        // form is the dominant production emission (per FilesIndexIngestDocumentSource +
        // ObservationEmitter empirical grep 2026-06-03) but requires async sprk_document
        // lookup — deferred to v1.2 to keep this synchronous projection path. Such citations
        // get Href = null and the client falls back to display-name-only rendering per §3.5.
        var bffBaseUrl = GetBffBaseUrl();
        var citations = artifact.Evidence
            .Select((e, idx) => new AssistantQueryCitation(
                N: idx + 1,
                Source: e.Ref,
                Excerpt: e.Quote ?? string.Empty,
                ObservationId: null,
                ChunkId: null,
                Href: BuildHrefForEvidence(e, bffBaseUrl)))
            .ToList();

        // Answer: plain-text rendering. For Inference artifacts we surface the Reasoning when
        // present (richest signal); else fall back to a generic claim summary.
        string answer = artifact switch
        {
            InferenceArtifact inf when !string.IsNullOrWhiteSpace(inf.Reasoning) =>
                inf.Reasoning!,
            _ =>
                $"{artifact.Predicate}: {artifact.Value.Raw.GetRawText()}"
        };

        // Confidence: artifact-specific. Facts = 1.0; Observations/Inferences carry it.
        double confidence = artifact switch
        {
            FactArtifact f => f.Confidence,
            ObservationArtifact o => o.Confidence,
            InferenceArtifact i => i.Confidence,
            _ => 1.0
        };

        // StructuredEnvelopeJson: serialise the full artifact preserving the polymorphic discriminator.
        var envelopeJson = JsonSerializer.Serialize<InsightArtifact>(artifact, EnvelopeJsonOptions);

        return new AssistantQueryFacadeResult
        {
            Path = "playbook",
            Answer = answer,
            Citations = citations,
            Confidence = confidence,
            PlaybookId = canonicalName,
            StructuredKind = StructuredKindInference,
            StructuredEnvelopeJson = envelopeJson,
            IntentSource = intentSource,
            ClassifierBelowThreshold = belowThreshold,
            CacheHit = agentResult.CacheHit,
            HitCount = citations.Count
        };
    }

    private AssistantQueryFacadeResult BuildDeclineResponse(
        InsightsAgentResult agentResult,
        string canonicalName,
        string intentSource,
        bool belowThreshold)
    {
        var decline = agentResult.Decline!;
        var envelopeJson = JsonSerializer.Serialize(decline, EnvelopeJsonOptions);

        return new AssistantQueryFacadeResult
        {
            Path = "playbook",
            Answer = decline.Explanation,
            Citations = [],
            // Per contract §4.4: confidence = 1 - ConfidenceInDecline.
            Confidence = Math.Clamp(1.0 - decline.ConfidenceInDecline, 0.0, 1.0),
            PlaybookId = canonicalName,
            StructuredKind = StructuredKindDecline,
            StructuredEnvelopeJson = envelopeJson,
            IntentSource = intentSource,
            ClassifierBelowThreshold = belowThreshold,
            CacheHit = false, // Declines are never cached per InsightsOrchestrator semantics.
            HitCount = 0
        };
    }

    // ───────────────────────────────────────────────────────────────────────────────────
    // RAG PATH
    // ───────────────────────────────────────────────────────────────────────────────────

    private async Task<AssistantQueryFacadeResult> ExecuteRagPathAsync(
        AssistantQueryFacadeRequest request,
        string intentSource,
        bool belowThreshold,
        Func<InsightsSearchFacadeRequest, CancellationToken, Task<InsightsSearchFacadeResult>> ragInvoker,
        CancellationToken cancellationToken)
    {
        // Step R1: build the RAG facade request. Default TopK = 10 per FR-04 / contract §4.
        var ragRequest = new InsightsSearchFacadeRequest(
            Query: request.Query,
            ParentEntityType: request.ParentEntityType,
            ParentEntityId: request.ParentEntityId,
            ArtifactType: null,
            Predicate: null,
            TopK: 10,
            TenantId: request.TenantId,
            CallerPrincipal: request.CallerPrincipal,
            ForceMode: request.ForceMode);

        // Step R2: invoke RAG via the supplied delegate.
        var ragResult = await ragInvoker(ragRequest, cancellationToken).ConfigureAwait(false);

        // Step R3: shape into uniform response. Each hit projects to AssistantQueryCitation
        // with the 1-based n matching the [n] tokens in ragResult.Summary.
        // Wave F task 052 / contract v1.1: RAG-path Href uses ObservationId (which IS the
        // sprk_document Guid per IRagService.RagSearchResult.DocumentId XML doc) routed
        // through the existing GET /api/documents/{id}/preview endpoint. AIPU2-027 privilege
        // filtering is enforced naturally — the preview endpoint runs OBO so Graph + Dataverse
        // ACL gate access. When ObservationId is null (orphan chunk without sprk_document
        // parent), Href is null and consumer falls back to display-name-only per §3.5.
        var bffBaseUrl = GetBffBaseUrl();
        var citations = ragResult.Results
            .Select((h, idx) => new AssistantQueryCitation(
                N: idx + 1,
                Source: h.DocumentName,
                Excerpt: h.Snippet,
                ObservationId: h.ObservationId,
                ChunkId: h.ChunkId,
                Href: BuildHrefForObservationId(h.ObservationId, bffBaseUrl)))
            .ToList();

        // Confidence per contract §4.1: top hit's relevance score, or 0.0 when no hits.
        double confidence = ragResult.Results.Count > 0
            ? ragResult.Results[0].Confidence
            : 0.0;

        // StructuredEnvelopeJson: a minimal RAG-side envelope so the Assistant can render
        // a "view all sources" power-user panel. Includes the summary + raw hits.
        var envelopeJson = JsonSerializer.Serialize(new
        {
            results = ragResult.Results,
            summary = ragResult.Summary
        }, EnvelopeJsonOptions);

        return new AssistantQueryFacadeResult
        {
            Path = "rag",
            Answer = ragResult.Summary,
            Citations = citations,
            Confidence = confidence,
            PlaybookId = null,
            StructuredKind = StructuredKindObservation,
            StructuredEnvelopeJson = envelopeJson,
            IntentSource = intentSource,
            ClassifierBelowThreshold = belowThreshold,
            CacheHit = false, // RAG path does not surface playbook D-P13 cache.
            HitCount = citations.Count
        };
    }

    // ───────────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Routing decision tuple returned from <see cref="DecideRouteAsync"/>. Captures the
    /// dispatch decision so streaming + single-shot share identical routing logic.
    /// </summary>
    /// <param name="Path">Dispatch target: <c>"playbook"</c> | <c>"rag"</c>.</param>
    /// <param name="ClassifierPlaybookHint">Playbook canonical name suggested by the
    /// classifier; null on forceMode or fallback paths.</param>
    /// <param name="IntentSource">Telemetry value: <c>classifier</c> | <c>forceMode</c>
    /// | <c>classifier-fallback</c>.</param>
    /// <param name="BelowThreshold">True when classifier was invoked and returned
    /// below-threshold (handler fell back to RAG per FR-05 safety).</param>
    public sealed record RoutingDecision(
        string Path,
        string? ClassifierPlaybookHint,
        string IntentSource,
        bool BelowThreshold);

    /// <summary>
    /// Make the routing decision per contract §3.2 + §7. Extracted as a public method so
    /// the streaming path (<see cref="HandleStreamingAsync"/>) and the single-shot path
    /// (<see cref="ExecuteAsync"/>) share identical routing logic without duplication
    /// (Wave F task 051 / FR-05 v1.1).
    /// </summary>
    /// <remarks>
    /// <b>FeatureDisabledException</b>: when <c>ForceMode == null</c>, the classifier is
    /// invoked and may throw <see cref="FeatureDisabledException"/> with
    /// <c>ErrorCode = "ai.intent-classification.disabled"</c>. This propagates unchanged —
    /// the streaming caller MUST catch it BEFORE yielding the first chunk so the endpoint
    /// can return 503 ProblemDetails with no SSE body (ADR-032 kill-switch ordering).
    /// </remarks>
    public async Task<RoutingDecision> DecideRouteAsync(
        AssistantQueryFacadeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.ForceMode, "playbook", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "AssistantToolCallHandler: forceMode=playbook bypassing classifier. tenant={TenantId} subject={Scheme}:{Id}",
                request.TenantId, request.ParentEntityType, request.ParentEntityId);
            return new RoutingDecision("playbook", ClassifierPlaybookHint: null, IntentSourceForceMode, BelowThreshold: false);
        }

        if (string.Equals(request.ForceMode, "rag", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "AssistantToolCallHandler: forceMode=rag bypassing classifier. tenant={TenantId} subject={Scheme}:{Id}",
                request.TenantId, request.ParentEntityType, request.ParentEntityId);
            return new RoutingDecision("rag", ClassifierPlaybookHint: null, IntentSourceForceMode, BelowThreshold: false);
        }

        // No forceMode → invoke classifier. May throw FeatureDisabledException; propagates.
        var classification = await _classifier.ClassifyAsync(
            request.Query,
            new IntentClassificationContext(
                SubjectScheme: request.ParentEntityType,
                TenantId: request.TenantId),
            cancellationToken).ConfigureAwait(false);

        // FR-05 safety: BelowThreshold MUST fall back to RAG regardless of classifier hint.
        if (classification.BelowThreshold)
        {
            _logger.LogInformation(
                "AssistantToolCallHandler: classifier below-threshold (confidence={Confidence:0.00}) — falling back to RAG. tenant={TenantId} subject={Scheme}:{Id}",
                classification.Confidence, request.TenantId, request.ParentEntityType, request.ParentEntityId);
            return new RoutingDecision("rag", ClassifierPlaybookHint: null, IntentSourceClassifierFallback, BelowThreshold: true);
        }

        var pathStr = classification.Path == IntentPath.Playbook ? "playbook" : "rag";
        _logger.LogDebug(
            "AssistantToolCallHandler: classifier dispatched path={Path} playbookHint={PlaybookHint} confidence={Confidence:0.00}. tenant={TenantId} subject={Scheme}:{Id}",
            pathStr, classification.PlaybookId, classification.Confidence, request.TenantId, request.ParentEntityType, request.ParentEntityId);
        return new RoutingDecision(pathStr, classification.PlaybookId, IntentSourceClassifier, BelowThreshold: false);
    }

    /// <summary>
    /// Build the artifact-path <see cref="AssistantQueryFacadeResult"/> from an
    /// <see cref="InsightsAgentResult"/>. Public-internal so <see cref="InsightsOrchestrator"/>
    /// streaming code can reuse projection logic without duplication.
    /// </summary>
    internal AssistantQueryFacadeResult BuildPlaybookResult(
        InsightsAgentResult agentResult,
        string canonicalName,
        string intentSource,
        bool belowThreshold)
    {
        if (agentResult.Artifact is not null)
        {
            return BuildArtifactResponse(agentResult, canonicalName, intentSource, belowThreshold);
        }
        if (agentResult.Decline is not null)
        {
            return BuildDeclineResponse(agentResult, canonicalName, intentSource, belowThreshold);
        }

        return new AssistantQueryFacadeResult
        {
            Path = "playbook",
            Answer = "The Insights service returned an empty response. Please retry or contact support.",
            Citations = [],
            Confidence = 0.0,
            PlaybookId = canonicalName,
            StructuredKind = StructuredKindDecline,
            StructuredEnvelopeJson = "{}",
            IntentSource = intentSource,
            ClassifierBelowThreshold = belowThreshold,
            CacheHit = false,
            HitCount = 0
        };
    }

    /// <summary>
    /// Build the RAG-path <see cref="AssistantQueryFacadeResult"/> from an
    /// <see cref="InsightsSearchFacadeResult"/>. Public-internal so
    /// <see cref="InsightsOrchestrator"/> streaming code can assemble the terminal
    /// <c>result</c> chunk without duplicating projection logic.
    /// </summary>
    /// <param name="ragResult">The completed RAG search facade result.</param>
    /// <param name="streamedAnswer">The accumulated synthesis text from streamed tokens.
    /// When non-null, REPLACES <see cref="InsightsSearchFacadeResult.Summary"/> in the
    /// terminal chunk so the streaming path's final result matches what the client saw via
    /// delta chunks. When null (e.g., zero-hit path with no synthesis), the result uses the
    /// RAG facade's empty Summary.</param>
    /// <param name="intentSource">Routing telemetry per <see cref="DecideRouteAsync"/>.</param>
    /// <param name="belowThreshold">Classifier below-threshold flag per
    /// <see cref="DecideRouteAsync"/>.</param>
    internal AssistantQueryFacadeResult BuildRagResult(
        InsightsSearchFacadeResult ragResult,
        string? streamedAnswer,
        string intentSource,
        bool belowThreshold)
    {
        // Wave F task 052 / contract v1.1: same Href projection as the single-shot RAG
        // path — streaming and single-shot must produce identical terminal `result` chunks.
        var bffBaseUrl = GetBffBaseUrl();
        var citations = ragResult.Results
            .Select((h, idx) => new AssistantQueryCitation(
                N: idx + 1,
                Source: h.DocumentName,
                Excerpt: h.Snippet,
                ObservationId: h.ObservationId,
                ChunkId: h.ChunkId,
                Href: BuildHrefForObservationId(h.ObservationId, bffBaseUrl)))
            .ToList();

        double confidence = ragResult.Results.Count > 0
            ? ragResult.Results[0].Confidence
            : 0.0;

        var envelopeJson = JsonSerializer.Serialize(new
        {
            results = ragResult.Results,
            summary = streamedAnswer ?? ragResult.Summary
        }, EnvelopeJsonOptions);

        return new AssistantQueryFacadeResult
        {
            Path = "rag",
            Answer = streamedAnswer ?? ragResult.Summary,
            Citations = citations,
            Confidence = confidence,
            PlaybookId = null,
            StructuredKind = StructuredKindObservation,
            StructuredEnvelopeJson = envelopeJson,
            IntentSource = intentSource,
            ClassifierBelowThreshold = belowThreshold,
            CacheHit = false,
            HitCount = citations.Count
        };
    }

    /// <summary>
    /// Resolve the canonical playbook name to use for the playbook path. Priority order
    /// per contract §3.3:
    /// 1. Classifier-supplied hint (when classifier was invoked).
    /// 2. <c>Insights:Playbooks:DefaultName</c> configuration value.
    /// 3. <see cref="FallbackDefaultPlaybookName"/> hard-coded final fallback.
    /// </summary>
    internal string ResolvePlaybookCanonicalName(string? classifierHint)
    {
        if (!string.IsNullOrWhiteSpace(classifierHint))
        {
            return classifierHint.Trim();
        }

        var configValue = _configuration[DefaultPlaybookConfigKey];
        if (!string.IsNullOrWhiteSpace(configValue))
        {
            return configValue.Trim();
        }

        return FallbackDefaultPlaybookName;
    }

    /// <summary>
    /// Compute the Phase 1 AccessibleScopeHash from the tenant + caller oid. Mirrors
    /// <c>InsightEndpoints.ComputeAccessibleScopeHash</c> so the playbook D-P13 cache key
    /// is shared between the standalone <c>/ask</c> endpoint and the unified Assistant
    /// endpoint — same query from the same user reuses the cache regardless of entry point.
    /// </summary>
    internal static string ComputeAccessibleScopeHash(string tenantId, string callerOid)
    {
        var input = $"tid:{tenantId}|oid:{callerOid}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ───────────────────────────────────────────────────────────────────────────────────
    // CITATION HREF PROJECTION (Wave F task 052 / contract v1.1)
    // ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read the configured BFF base URL used to construct citation <c>href</c> values.
    /// Returns null when unconfigured — projection emits <c>Href = null</c> for all
    /// citations in that case (consumer falls back to display-name-only per §3.5).
    /// </summary>
    private string? GetBffBaseUrl()
    {
        var raw = _citationHrefOptions.CurrentValue.BffBaseUrl;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Strip trailing slash to guarantee canonical {origin}/api/documents/{id}/preview shape.
        return raw.TrimEnd('/');
    }

    /// <summary>
    /// Build the citation <c>href</c> URL from a RAG path's <c>ObservationId</c> (which
    /// IS the <c>sprk_document</c> Guid per <c>RagSearchResult.DocumentId</c> XML doc —
    /// see <c>IRagService.cs</c>). Returns null when <paramref name="observationId"/> is
    /// null (orphan chunk), unparseable as a Guid (defense), or <paramref name="bffBaseUrl"/>
    /// is null (unconfigured environment). AIPU2-027 privilege filtering is enforced
    /// by the downstream <c>/api/documents/{id}/preview</c> endpoint via OBO — no URL
    /// signing or token embedding is required.
    /// </summary>
    /// <param name="observationId">The sprk_document Guid as a string, or null.</param>
    /// <param name="bffBaseUrl">Pre-normalized BFF origin (no trailing slash), or null.</param>
    /// <returns>The absolute preview URL, or null if the citation cannot be addressed.</returns>
    internal static string? BuildHrefForObservationId(string? observationId, string? bffBaseUrl)
    {
        if (bffBaseUrl is null) return null;
        if (string.IsNullOrWhiteSpace(observationId)) return null;
        // Defensive Guid validation — the preview endpoint will 400 on non-Guid input.
        // Surface null here rather than emitting a URL that's guaranteed to 400.
        if (!Guid.TryParse(observationId, out _)) return null;
        return $"{bffBaseUrl}/api/documents/{observationId}/preview";
    }

    /// <summary>
    /// Build the citation <c>href</c> URL from a playbook-path <see cref="EvidenceRef"/>.
    /// Only <c>document</c>-type evidence in bare-Guid form is addressable in v1.1; the
    /// dominant production emission (<c>spe://drive/{driveId}/item/{itemId}</c>) requires
    /// an async <c>sprk_document</c> lookup via <c>sprk_driveitemid</c> (see
    /// <see cref="Services.Insights.Observations.DataverseObservationMirror"/>) — deferred
    /// to v1.2 to keep this synchronous projection path. All non-document evidence
    /// (fact-source, comparable-matter, supporting-matter, playbook-run) returns null
    /// per spike F1 §B.
    /// </summary>
    internal static string? BuildHrefForEvidence(EvidenceRef evidence, string? bffBaseUrl)
    {
        if (bffBaseUrl is null) return null;
        var documentId = TryExtractDocumentIdFromEvidenceRef(evidence);
        if (documentId is null) return null;
        return $"{bffBaseUrl}/api/documents/{documentId.Value}/preview";
    }

    /// <summary>
    /// Extract a <c>sprk_document</c> Guid from an <see cref="EvidenceRef"/> when possible.
    /// Returns null for non-document evidence types, empty <c>Ref</c>, or evidence in
    /// <c>spe://drive/{driveId}/item/{itemId}</c> form (which requires async lookup,
    /// deferred to v1.2). Empirical grep (2026-06-03) confirmed bare-Guid form is NOT
    /// the dominant production emission for the predict-matter-cost@v1 playbook, but
    /// remains a valid emission shape (e.g., when callers pre-resolve the sprk_document
    /// Guid). This helper supports the bare-Guid form so future upstream changes can
    /// shift emission without requiring an additional contract revision.
    /// </summary>
    /// <param name="evidence">The evidence ref from a playbook artifact.</param>
    /// <returns>The sprk_document Guid, or null if not extractable synchronously.</returns>
    internal static Guid? TryExtractDocumentIdFromEvidenceRef(EvidenceRef evidence)
    {
        if (evidence is null) return null;
        if (!string.Equals(evidence.RefType, "document", StringComparison.Ordinal)) return null;
        if (string.IsNullOrWhiteSpace(evidence.Ref)) return null;

        // Form 1: bare Guid (cleanest emission shape).
        if (Guid.TryParse(evidence.Ref, out var bareGuid)) return bareGuid;

        // Form 2: spe://drive/{driveId}/item/{itemId} URI — DOMINANT production shape
        // per FilesIndexIngestDocumentSource.cs:166 + ObservationEmitter.cs:107 +
        // Layer1ClassificationEmitter.cs:118 empirical grep (2026-06-03).
        // Resolving driveItemId → sprk_document.sprk_documentid requires an async
        // Dataverse query (DataverseObservationMirror.ResolveDocumentIdAsync at :214
        // is the existing pattern). Deferred to v1.2 — this synchronous helper returns
        // null so the citation gets Href = null per §3.5 back-compat. Roadmap option
        // is either (a) upstream emitter change to pre-resolve sprk_document Guid OR
        // (b) make citation projection async with bounded fan-out batch lookup.
        return null;
    }
}
