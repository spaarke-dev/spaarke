using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 4 — semantic record match (FR-14). When the deterministic rungs (0–3) do not auto-file, this
/// rung composes a query from the normalized envelope (subject + body) and runs hybrid (vector + keyword,
/// RRF) search over the <c>spaarke-records-index</c> via the <see cref="IRecordMatchingAi"/> facade to
/// find fuzzy candidates for the searchable target entities (matter / project / invoice). Each hit becomes
/// a <see cref="RungMatch"/> carrying the search <c>confidenceScore</c> + <c>matchReasons</c> as provenance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never auto-files.</b> Rung 4 is an AI/semantic rung (<see cref="RungKind.SemanticMatch"/>). The
/// engine evaluates AI rungs only when the deterministic pass did not auto-file (cost containment,
/// NFR-05), and <see cref="AssociationStatusMapper"/> computes auto-file eligibility from the
/// deterministic-only reinforced confidence — so a semantic match can only raise a Pending Review to
/// Suggested (or, when two strong semantic candidates disagree on one field, Ambiguous). It can never
/// push a target to Resolved. No engine/mapper change is required to add this rung.
/// </para>
/// <para>
/// <b>Facade + lifetime.</b> Consumes AI search via the <see cref="IRecordMatchingAi"/> PublicContracts
/// facade (refined ADR-013) — never the internal <c>IRecordSearchService</c> directly. The facade is
/// scoped and this rung is a singleton (the engine holds rungs as <c>IEnumerable&lt;IAssociationRung&gt;</c>),
/// so the facade is resolved from a per-evaluation scope via <see cref="IServiceScopeFactory"/>.
/// </para>
/// <para>
/// <b>Index name.</b> The rung never names an index — it leaves <see cref="RecordSearchRequest.SearchIndexName"/>
/// null so <c>RecordSearchService</c> resolves the records-index name through its own config/resolver
/// (kept merge-clean with the index-config tokenization work, FR-26 / task 075).
/// </para>
/// <para>
/// <b>Org note (§11 / FR-14).</b> The records index contains matter / project / invoice
/// (<see cref="RecordEntityType.ValidTypes"/>) — organization is not indexed, so organization association
/// remains served by the deterministic domain match (rung 2). Rung 4 does not introduce a new index or
/// search client; it reuses <see cref="IRecordMatchingAi"/> / <c>spaarke-records-index</c>.
/// </para>
/// <para>
/// Best-effort/non-fatal (NFR-06): a throw is treated by the engine as a non-match. Registered as a
/// concrete <see cref="IAssociationRung"/> singleton in <c>CommunicationModule</c> (ADR-010).
/// </para>
/// </remarks>
public sealed class SemanticMatchRung : IAssociationRung
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SemanticMatchOptions> _options;
    private readonly ILogger<SemanticMatchRung> _logger;

    /// <summary>
    /// Record types the <c>spaarke-records-index</c> actually contains (matter / project / invoice).
    /// Organization/other targets are not indexed; see the class remarks.
    /// </summary>
    private static readonly IReadOnlyList<string> SearchableRecordTypes = RecordEntityType.ValidTypes;

    public SemanticMatchRung(
        IServiceScopeFactory scopeFactory,
        IOptions<SemanticMatchOptions> options,
        ILogger<SemanticMatchRung> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public RungKind Kind => RungKind.SemanticMatch;
    public int Order => 4;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var opts = _options.Value;

        // Telemetry hooks (fired/skipped, latency, hit-count) — task 032 consumes these structured events.
        if (!opts.Enabled)
        {
            _logger.LogDebug("Rung 4 (semantic match) skipped — disabled via Communication:SemanticMatch:Enabled.");
            return Array.Empty<RungMatch>();
        }

        var query = BuildQuery(message, opts.MaxQueryChars);
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogDebug("Rung 4 (semantic match) skipped — no query text (empty subject + body).");
            return Array.Empty<RungMatch>();
        }

        var startTs = Stopwatch.GetTimestamp();

        RecordSearchResponse response;
        using (var scope = _scopeFactory.CreateScope())
        {
            var matcher = scope.ServiceProvider.GetRequiredService<IRecordMatchingAi>();
            var request = new RecordSearchRequest
            {
                Query = query,
                RecordTypes = SearchableRecordTypes,
                Options = new RecordSearchOptions
                {
                    Limit = opts.Limit,
                    HybridMode = RecordHybridSearchMode.Rrf,
                    // Rank by keyword relevance (BM25), not the Azure semantic reranker: for matching a
                    // communication to a NAMED record the reranker buries exact-name matches under
                    // conceptually-similar ones (email-r4 UAT 2026-07-17). This is the fuzzy fallback rung;
                    // the deterministic RecordNameMatch rung handles exact appearances.
                    PreferKeywordRanking = true,
                },
                // SearchIndexName intentionally null → RecordSearchService resolves the records-index
                // name via its own config/resolver (no hardcoded index name here).
            };

            response = await matcher.SearchAsync(request, ct);
        }

        var matches = MapResults(response, opts);
        var elapsed = Stopwatch.GetElapsedTime(startTs);

        _logger.LogInformation(
            "Rung 4 (semantic match) fired | QueryChars: {QueryChars}, Hits: {HitCount}, Emitted: {EmitCount}, ElapsedMs: {ElapsedMs}",
            query.Length, response.Results.Count, matches.Count, (long)elapsed.TotalMilliseconds);

        return matches;
    }

    /// <summary>
    /// Composes the search query from the envelope subject + plain-text body, bounded to
    /// <paramref name="maxChars"/> (the record-search query cap). Whitespace-collapsed.
    /// </summary>
    private static string BuildQuery(NormalizedMessage message, int maxChars)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(message.Subject))
            parts.Add(message.Subject.Trim());
        if (!string.IsNullOrWhiteSpace(message.BodyText))
            parts.Add(message.BodyText.Trim());

        if (parts.Count == 0)
            return string.Empty;

        var query = string.Join(" \n ", parts);

        // Collapse runs of whitespace so the body's formatting does not consume the char budget.
        query = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return query.Length > maxChars ? query[..maxChars] : query;
    }

    /// <summary>
    /// Maps search hits to <see cref="RungMatch"/> candidates: filters by <see cref="SemanticMatchOptions.MinScore"/>,
    /// maps record type → regarding field (<see cref="RegardingFieldMap"/>), caps confidence at
    /// <see cref="SemanticMatchOptions.ScoreCeiling"/>, dedups by (field, target) keeping the highest, and
    /// returns the top <see cref="SemanticMatchOptions.MaxCandidates"/> by confidence.
    /// </summary>
    private IReadOnlyList<RungMatch> MapResults(RecordSearchResponse response, SemanticMatchOptions opts)
    {
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        foreach (var result in response.Results)
        {
            if (result.ConfidenceScore < opts.MinScore)
                continue;

            var field = RegardingFieldMap.FieldFor(result.RecordType);
            if (field is null)
                continue; // record type not part of the ADR-024 regarding family

            if (!Guid.TryParse(result.RecordId, out var targetId))
                continue;

            var confidence = Math.Min(result.ConfidenceScore, opts.ScoreCeiling);

            var reasons = result.MatchReasons is { Count: > 0 }
                ? string.Join("; ", result.MatchReasons)
                : "hybrid record-index match";

            var match = new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(result.RecordType, targetId)
                {
                    Name = result.RecordName,
                },
                Confidence = confidence,
                Provenance = $"semantic:{result.RecordType}:score={result.ConfidenceScore:F2}:{reasons}",
                Rung = RungKind.SemanticMatch,
            };

            var key = (field, targetId);
            if (!best.TryGetValue(key, out var existing) || match.Confidence > existing.Confidence)
                best[key] = match;
        }

        return best.Values
            .OrderByDescending(m => m.Confidence)
            .Take(opts.MaxCandidates)
            .ToArray();
    }
}
