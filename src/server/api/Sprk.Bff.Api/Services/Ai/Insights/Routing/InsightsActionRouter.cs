using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Insights.Routing;

/// <summary>
/// Default implementation of <see cref="IInsightsActionRouter"/>. Resolves
/// per-(practice-area) Layer 1 and per-(practice-area, document-type) Layer 2
/// action rows from Dataverse, with an in-memory cache that's safe to share
/// across requests because the underlying reference data changes rarely.
/// </summary>
/// <remarks>
/// <para>
/// <b>Caching strategy</b>: a per-instance <see cref="IMemoryCache"/> entry for
/// (i) per-area Layer 1 action lookups keyed by area code, and (ii) the
/// <c>sprk_practicearea_documenttype</c> matrix row keyed by
/// <c>(area, type)</c>. Both use a 15-minute sliding window: long enough to
/// absorb hot-path traffic during a single soak, short enough that an SME
/// editing the matrix sees the change within a quarter-hour.
/// </para>
/// <para>
/// <b>Cache-miss semantics</b>: on a cache miss, the router queries Dataverse via
/// <see cref="IGenericEntityService"/>. The matrix lookup uses
/// <see cref="QueryExpression"/> (the matrix's composite alternate key requires
/// the row Guid be inferred from <c>(sprk_practicearea, sprk_documenttype)</c>
/// lookups which are themselves lookups by code — a single query is cheaper).
/// The Layer 1 action lookup uses <see cref="IGenericEntityService.RetrieveByAlternateKeyAsync"/>
/// since <c>sprk_actioncode</c> is an alternate key on <c>sprk_analysisaction</c>.
/// </para>
/// <para>
/// <b>Negative caching</b> — when a per-area action does NOT exist (e.g., a
/// playbook running for the MA practice area when no <c>INS-L1C-MA@v1</c> row
/// has been authored), we cache a <c>null</c> sentinel so subsequent calls
/// for the same area in the cache window don't re-query Dataverse. Same for
/// missing matrix rows.
/// </para>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Routing/</c>;
/// imports <see cref="IScopeResolverService"/> (Zone A) to load the resolved
/// per-pair action's <c>SystemPrompt</c> when the matrix carries an action code.
/// </para>
/// <para>
/// <b>ADR-032 §F.1 inspection</b>: this service is registered UNCONDITIONALLY
/// in <c>AnalysisServicesModule</c> alongside <c>AiAnalysisNodeExecutor</c> (its
/// consumer via <c>PlaybookOrchestrationService</c>). No <c>if (flag)</c> block;
/// the asymmetric-registration anti-pattern does NOT apply. Static-scan recipe
/// per ADR-032 §10: verified compliant — no new conditional registration.
/// </para>
/// </remarks>
public sealed class InsightsActionRouter : IInsightsActionRouter
{
    /// <summary>Logical name of the per-area Layer 1 action prefix. Combines as <c>INS-L1C-&lt;AREA&gt;@v1</c>.</summary>
    internal const string Layer1ActionCodePrefix = "INS-L1C-";

    /// <summary>Generic Layer 1 action code used for fallback (matches universal-ingest.playbook.json node 2).</summary>
    internal const string Layer1GenericActionCode = "INS-L1C@v1";

    /// <summary>Generic Layer 2 action code used for fallback (matches universal-ingest.playbook.json node 4).</summary>
    internal const string Layer2GenericActionCode = "INS-L2X@v1";

    /// <summary>Action code version suffix.</summary>
    internal const string ActionVersionSuffix = "@v1";

    /// <summary>Dataverse entity logical name for the practice-area × document-type matrix.</summary>
    internal const string MatrixEntityName = "sprk_practicearea_documenttype";

    /// <summary>Lookup-target entity for <c>sprk_practicearea</c> field on the matrix.</summary>
    internal const string PracticeAreaRefEntityName = "sprk_practicearea_ref";

    /// <summary>Lookup-target entity for <c>sprk_documenttype</c> field on the matrix.</summary>
    internal const string DocumentTypeRefEntityName = "sprk_documenttype_ref";

    /// <summary>15-minute sliding window — balances Dataverse load against SME edit latency.</summary>
    internal static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(15);

    /// <summary>Reasonable cap so the router doesn't grow unbounded on a busy host.</summary>
    private const int CacheSizeLimit = 1024;

    private readonly IGenericEntityService _entityService;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InsightsActionRouter> _logger;

    /// <summary>
    /// Concurrency-safe ledger of cache keys we've populated, so the router can
    /// log warnings only the first time it sees a (area, type) miss in a soak —
    /// repeated misses (cache hits) stay silent to avoid log flooding.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _knownMissKeys = new(StringComparer.OrdinalIgnoreCase);

    public InsightsActionRouter(
        IGenericEntityService entityService,
        IScopeResolverService scopeResolver,
        IMemoryCache cache,
        ILogger<InsightsActionRouter> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AnalysisAction> ResolveLayer1ActionAsync(
        string? practiceAreaCode,
        AnalysisAction defaultAction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defaultAction);

        if (string.IsNullOrWhiteSpace(practiceAreaCode))
        {
            return defaultAction;
        }

        var normalizedArea = NormalizeCode(practiceAreaCode);
        var perAreaActionCode = Layer1ActionCodePrefix + normalizedArea + ActionVersionSuffix;
        var cacheKey = "insights.router.l1:" + normalizedArea;

        var perAreaAction = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SetSlidingExpiration(CacheSlidingExpiration);
            entry.SetSize(1);

            return await LoadActionByCodeAsync(perAreaActionCode, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (perAreaAction is null)
        {
            if (_knownMissKeys.TryAdd(cacheKey, 0))
            {
                _logger.LogInformation(
                    "InsightsActionRouter.Layer1: no per-area action row found for {ActionCode}; falling back to generic {GenericActionCode}",
                    perAreaActionCode, Layer1GenericActionCode);
            }
            return defaultAction;
        }

        _logger.LogDebug(
            "InsightsActionRouter.Layer1: routed practiceArea={PracticeArea} to action {ActionCode} (id={ActionId})",
            normalizedArea, perAreaActionCode, perAreaAction.Id);

        return perAreaAction;
    }

    /// <inheritdoc />
    public async Task<InsightsLayer2RoutingResult> ResolveLayer2ActionAsync(
        string? practiceAreaCode,
        string? documentTypeCode,
        AnalysisAction defaultAction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defaultAction);

        if (string.IsNullOrWhiteSpace(practiceAreaCode) || string.IsNullOrWhiteSpace(documentTypeCode))
        {
            return InsightsLayer2RoutingResult.PassThrough(defaultAction);
        }

        var normalizedArea = NormalizeCode(practiceAreaCode);
        var normalizedType = NormalizeCode(documentTypeCode);
        var cacheKey = "insights.router.l2:" + normalizedArea + ":" + normalizedType;

        var matrixHit = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SetSlidingExpiration(CacheSlidingExpiration);
            entry.SetSize(1);

            return await LoadMatrixRowAsync(normalizedArea, normalizedType, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (matrixHit is null)
        {
            // No matrix row for (area, type) — fall back to generic. Universal-ingest's
            // "every matter classifies" promise: unmapped pairs still emit Observations
            // via the generic Layer 2 prompt rather than crashing the run.
            if (_knownMissKeys.TryAdd(cacheKey, 0))
            {
                _logger.LogInformation(
                    "InsightsActionRouter.Layer2: no matrix row for ({PracticeArea}, {DocumentType}); falling back to generic {GenericActionCode}",
                    normalizedArea, normalizedType, Layer2GenericActionCode);
            }
            return new InsightsLayer2RoutingResult(InsightsLayer2RoutingDecision.FallbackToGeneric, defaultAction);
        }

        if (string.IsNullOrWhiteSpace(matrixHit.Layer2ActionCode))
        {
            // NULL action code = structured gate-fail (e.g., CTRNS × NDA by design).
            // Caller skips the Layer 2 LLM call and emits Layer-1-only Observation.
            _logger.LogInformation(
                "InsightsActionRouter.Layer2: matrix row for ({PracticeArea}, {DocumentType}) has NULL sprk_layer2actioncode — gate-failing Layer 2 (matrixRowId={MatrixRowId})",
                normalizedArea, normalizedType, matrixHit.MatrixRowId);
            return new InsightsLayer2RoutingResult(
                InsightsLayer2RoutingDecision.GateFailNullActionCode,
                defaultAction,
                MatrixRowId: matrixHit.MatrixRowId);
        }

        // Resolve the per-pair action row via the same alternate-key lookup we use
        // for Layer 1 fall-through. Cached on its own key — same TTL.
        var perPairCacheKey = "insights.router.l2.action:" + matrixHit.Layer2ActionCode!;
        var perPairAction = await _cache.GetOrCreateAsync(perPairCacheKey, async entry =>
        {
            entry.SetSlidingExpiration(CacheSlidingExpiration);
            entry.SetSize(1);
            return await LoadActionByCodeAsync(matrixHit.Layer2ActionCode!, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (perPairAction is null)
        {
            // Matrix referenced an action code that doesn't exist in sprk_analysisaction —
            // SME authoring error or stale matrix. Log Warning and fall back so the run
            // still emits an Observation. Defense-in-depth: this is a SME data-quality
            // issue (ref-integrity), not a hard failure of the routing path.
            _logger.LogWarning(
                "InsightsActionRouter.Layer2: matrix row for ({PracticeArea}, {DocumentType}) references action {ActionCode} but no such row exists in sprk_analysisaction; falling back to generic {GenericActionCode}",
                normalizedArea, normalizedType, matrixHit.Layer2ActionCode, Layer2GenericActionCode);
            return new InsightsLayer2RoutingResult(
                InsightsLayer2RoutingDecision.FallbackToGeneric,
                defaultAction,
                MatrixRowId: matrixHit.MatrixRowId);
        }

        _logger.LogDebug(
            "InsightsActionRouter.Layer2: routed ({PracticeArea}, {DocumentType}) to action {ActionCode} (id={ActionId}, matrixRowId={MatrixRowId})",
            normalizedArea, normalizedType, matrixHit.Layer2ActionCode, perPairAction.Id, matrixHit.MatrixRowId);

        return new InsightsLayer2RoutingResult(
            InsightsLayer2RoutingDecision.UsePerPairAction,
            perPairAction,
            MatrixRowId: matrixHit.MatrixRowId,
            ResolvedActionCode: matrixHit.Layer2ActionCode);
    }

    /// <summary>
    /// Load an action row by its <c>sprk_actioncode</c> alternate key. Returns null
    /// when the row does not exist (caller handles fallback). Other Dataverse errors
    /// surface as a logged Warning + null return to preserve the "every matter
    /// classifies" invariant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Action-code reform (FR-06, task 023)</b>: applies
    /// <see cref="ActionCodeNormalizer.Normalize"/> to the input so callers passing
    /// either <c>"foo-bar"</c> or <c>"foo-bar@v1"</c> resolve to the same row. A
    /// tier-1-safe telemetry tag <c>actionCodeFormat</c> (<c>"clean"</c> or
    /// <c>"v1Suffix"</c>) reflects the INPUT form before normalization, so the
    /// stabilization-window decay rate is measurable from logs.
    /// </para>
    /// </remarks>
    private async Task<AnalysisAction?> LoadActionByCodeAsync(string actionCode, CancellationToken cancellationToken)
    {
        // FR-06 (task 023): normalize at the lookup boundary so callers passing the
        // legacy "@v1" suffix and callers passing the new clean form resolve identically.
        // The tag value reflects the INPUT form so the deprecation window can be measured.
        var actionCodeFormat = ActionCodeNormalizer.Format(actionCode);
        var normalizedCode = ActionCodeNormalizer.Normalize(actionCode) ?? actionCode;

        var keys = new KeyAttributeCollection { { "sprk_actioncode", normalizedCode } };
        var columns = new[]
        {
            "sprk_analysisactionid",
            "sprk_name",
            "sprk_description",
            "sprk_actioncode",
            "sprk_systemprompt"
        };

        try
        {
            var entity = await _entityService.RetrieveByAlternateKeyAsync(
                MatrixActionEntityName,
                keys,
                columns,
                cancellationToken).ConfigureAwait(false);

            if (entity is null)
            {
                _logger.LogInformation(
                    "InsightsActionRouter: action {ActionCode} not found (format: {ActionCodeFormat})",
                    normalizedCode, actionCodeFormat);
                return null;
            }

            // Resolve via IScopeResolverService so we get the canonical AnalysisAction shape
            // (including ExecutorType from the expanded sprk_ActionTypeId lookup). This is the
            // same path PlaybookOrchestrationService uses for default action resolution,
            // ensuring routed actions and default actions look identical to downstream code.
            var actionId = entity.GetAttributeValue<Guid>("sprk_analysisactionid");
            var resolved = await _scopeResolver.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "InsightsActionRouter: resolved action {ActionCode} (format: {ActionCodeFormat}, actionId: {ActionId})",
                normalizedCode, actionCodeFormat, actionId);

            return resolved;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            // Expected on the "no per-area row authored yet" path.
            _logger.LogInformation(
                "InsightsActionRouter: action {ActionCode} not found (format: {ActionCodeFormat})",
                normalizedCode, actionCodeFormat);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Defense-in-depth: never let a Dataverse hiccup break universal-ingest's
            // "every matter classifies" invariant. Caller falls back to the default.
            _logger.LogWarning(ex,
                "InsightsActionRouter: error loading action {ActionCode} (format: {ActionCodeFormat}) from Dataverse — caller will fall back to default action",
                normalizedCode, actionCodeFormat);
            return null;
        }
    }

    /// <summary>
    /// Load the <c>sprk_practicearea_documenttype</c> matrix row for the given
    /// area + type codes. Resolves the two lookup ids by code internally, then
    /// queries the matrix by composite filter. Returns null if either ref row
    /// or the matrix row does not exist.
    /// </summary>
    private async Task<InsightsMatrixRow?> LoadMatrixRowAsync(
        string practiceAreaCode,
        string documentTypeCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var areaId = await LoadRefRowIdAsync(
                PracticeAreaRefEntityName, "sprk_practiceareacode", "sprk_practicearea_refid",
                practiceAreaCode, cancellationToken).ConfigureAwait(false);
            if (areaId is null)
            {
                _logger.LogDebug(
                    "InsightsActionRouter.LoadMatrixRow: no {RefEntity} row found for code {Code}",
                    PracticeAreaRefEntityName, practiceAreaCode);
                return null;
            }

            var typeId = await LoadRefRowIdAsync(
                DocumentTypeRefEntityName, "sprk_documenttypecode", "sprk_documenttype_refid",
                documentTypeCode, cancellationToken).ConfigureAwait(false);
            if (typeId is null)
            {
                _logger.LogDebug(
                    "InsightsActionRouter.LoadMatrixRow: no {RefEntity} row found for code {Code}",
                    DocumentTypeRefEntityName, documentTypeCode);
                return null;
            }

            var query = new QueryExpression(MatrixEntityName)
            {
                ColumnSet = new ColumnSet(
                    "sprk_practicearea_documenttypeid",
                    "sprk_layer2actioncode",
                    "sprk_layer2required",
                    "sprk_gatesignal"),
                TopCount = 1,
                NoLock = true
            };
            query.Criteria.AddCondition("sprk_practicearea", ConditionOperator.Equal, areaId.Value);
            query.Criteria.AddCondition("sprk_documenttype", ConditionOperator.Equal, typeId.Value);
            // Active rows only — deactivated matrix entries are SME signal that the pair
            // is intentionally retired (per design-a3 §2.4 ownership semantics).
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var results = await _entityService.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
            if (results.Entities.Count == 0)
            {
                return null;
            }

            var matrix = results.Entities[0];
            return new InsightsMatrixRow(
                MatrixRowId: matrix.GetAttributeValue<Guid>("sprk_practicearea_documenttypeid"),
                Layer2ActionCode: matrix.GetAttributeValue<string?>("sprk_layer2actioncode"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same defense-in-depth posture as LoadActionByCodeAsync.
            _logger.LogWarning(ex,
                "InsightsActionRouter: error loading matrix row for ({PracticeArea}, {DocumentType}) — caller will fall back to generic",
                practiceAreaCode, documentTypeCode);
            return null;
        }
    }

    /// <summary>
    /// Look up a ref-entity row Id by its code alternate key. Both
    /// <c>sprk_practicearea_ref.sprk_practiceareacode</c> and
    /// <c>sprk_documenttype_ref.sprk_documenttypecode</c> are alternate keys; either
    /// could in principle use <see cref="IGenericEntityService.RetrieveByAlternateKeyAsync"/>.
    /// We use a QueryExpression instead so the same code path handles both ref tables
    /// uniformly and survives if either alternate key is renamed in a future schema rev.
    /// </summary>
    private async Task<Guid?> LoadRefRowIdAsync(
        string refEntityName,
        string codeColumnName,
        string idColumnName,
        string code,
        CancellationToken cancellationToken)
    {
        var query = new QueryExpression(refEntityName)
        {
            ColumnSet = new ColumnSet(idColumnName),
            TopCount = 1,
            NoLock = true
        };
        query.Criteria.AddCondition(codeColumnName, ConditionOperator.Equal, code);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

        var results = await _entityService.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
        if (results.Entities.Count == 0)
        {
            return null;
        }

        return results.Entities[0].GetAttributeValue<Guid>(idColumnName);
    }

    /// <summary>
    /// Normalize a code value for cache keys + Dataverse equality: trim + upper.
    /// </summary>
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    /// <summary>Dataverse entity logical name for analysis actions (matrix's referenced action).</summary>
    private const string MatrixActionEntityName = "sprk_analysisaction";

    /// <summary>Internal projection of a matrix row's routing-relevant fields.</summary>
    private sealed record InsightsMatrixRow(Guid MatrixRowId, string? Layer2ActionCode);
}
