using System.Diagnostics;
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssistantToolCallHandler> _logger;

    public AssistantToolCallHandler(
        IInsightsIntentClassifier classifier,
        IOptionsMonitor<InsightsPlaybookNameMapOptions> playbookNameMap,
        IConfiguration configuration,
        ILogger<AssistantToolCallHandler> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _playbookNameMap = playbookNameMap ?? throw new ArgumentNullException(nameof(playbookNameMap));
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
        (string path, string? classifierPlaybookHint, string intentSource, bool belowThreshold) routing;

        if (string.Equals(request.ForceMode, "playbook", StringComparison.OrdinalIgnoreCase))
        {
            routing = ("playbook", classifierPlaybookHint: null, IntentSourceForceMode, belowThreshold: false);
            _logger.LogDebug(
                "AssistantToolCallHandler: forceMode=playbook bypassing classifier. tenant={TenantId} subject={Scheme}:{Id}",
                request.TenantId, request.ParentEntityType, request.ParentEntityId);
        }
        else if (string.Equals(request.ForceMode, "rag", StringComparison.OrdinalIgnoreCase))
        {
            routing = ("rag", classifierPlaybookHint: null, IntentSourceForceMode, belowThreshold: false);
            _logger.LogDebug(
                "AssistantToolCallHandler: forceMode=rag bypassing classifier. tenant={TenantId} subject={Scheme}:{Id}",
                request.TenantId, request.ParentEntityType, request.ParentEntityId);
        }
        else
        {
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
                routing = ("rag", classifierPlaybookHint: null, IntentSourceClassifierFallback, belowThreshold: true);
                _logger.LogInformation(
                    "AssistantToolCallHandler: classifier below-threshold (confidence={Confidence:0.00}) — falling back to RAG. tenant={TenantId} subject={Scheme}:{Id}",
                    classification.Confidence, request.TenantId, request.ParentEntityType, request.ParentEntityId);
            }
            else
            {
                var pathStr = classification.Path == IntentPath.Playbook ? "playbook" : "rag";
                routing = (pathStr, classification.PlaybookId, IntentSourceClassifier, belowThreshold: false);
                _logger.LogDebug(
                    "AssistantToolCallHandler: classifier dispatched path={Path} playbookHint={PlaybookHint} confidence={Confidence:0.00}. tenant={TenantId} subject={Scheme}:{Id}",
                    pathStr, classification.PlaybookId, classification.Confidence, request.TenantId, request.ParentEntityType, request.ParentEntityId);
            }
        }

        // ─── Step 2: Execute the chosen path ───────────────────────────────────────────
        AssistantQueryFacadeResult result;
        if (routing.path == "playbook")
        {
            result = await ExecutePlaybookPathAsync(
                request, routing.classifierPlaybookHint, routing.intentSource, routing.belowThreshold,
                playbookInvoker, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await ExecuteRagPathAsync(
                request, routing.intentSource, routing.belowThreshold,
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
        var citations = artifact.Evidence
            .Select((e, idx) => new AssistantQueryCitation(
                N: idx + 1,
                Source: e.Ref,
                Excerpt: e.Quote ?? string.Empty,
                ObservationId: null,
                ChunkId: null))
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
        var citations = ragResult.Results
            .Select((h, idx) => new AssistantQueryCitation(
                N: idx + 1,
                Source: h.DocumentName,
                Excerpt: h.Snippet,
                ObservationId: h.ObservationId,
                ChunkId: h.ChunkId))
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
}
