using System.Text;
using System.Text.Json;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// Production <see cref="IObservationIndexUpserter"/>. Embeds the Observation's
/// <c>content</c> field (predicate + value + quote concatenation) via
/// <see cref="IOpenAiClient.GenerateEmbeddingAsync"/> and merges/uploads the
/// document to <c>spaarke-insights-index</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Content composition</b>: <c>"{predicate} = {value} ({quote})"</c> where value is
/// the JSON-serialized raw value and quote is the first non-empty evidence quote (or
/// empty when no quote is available, e.g., Layer 1 Classification Observations). This
/// composition is what gets embedded; it's a SHORT searchable text that drives the
/// cohort retrieval queries in D-P14 synthesis.
/// </para>
/// <para>
/// <b>Embedding model</b>: text-embedding-3-large at 3072 dims (configured default;
/// matches the <c>spaarke-insights-index</c> schema's <c>contentVector</c> field).
/// </para>
/// <para>
/// <b>Singleton lifetime</b>: stateless wrapper over thread-safe dependencies.
/// </para>
/// </remarks>
internal sealed class ObservationIndexUpserter : IObservationIndexUpserter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly EventId UpsertedEvent = new(8045, "ObservationIndexUpserted");
    private static readonly EventId FailedEvent = new(8046, "ObservationIndexUpsertFailed");

    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly AiSearchOptions _options;
    private readonly ILogger<ObservationIndexUpserter> _logger;

    public ObservationIndexUpserter(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IOptions<AiSearchOptions> options,
        ILogger<ObservationIndexUpserter> logger)
    {
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ObservationArtifact observation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ct.ThrowIfCancellationRequested();

        try
        {
            // Compose the embedded content. Short + dense + matches D-P14 retrieval needs.
            var content = ComposeContent(observation);

            // Embed it. Configured defaults (text-embedding-3-large @ 3072 dims) match
            // the spaarke-insights-index schema; we don't override model or dimensions.
            var embedding = await _openAiClient.GenerateEmbeddingAsync(
                content,
                model: null,
                dimensions: null,
                cancellationToken: ct);

            // Project to the index document shape.
            var doc = new ObservationIndexDocument
            {
                Id = observation.Id,
                TenantId = observation.TenantId,
                ArtifactType = "observation",
                Subject = observation.Subject,
                Predicate = observation.Predicate,
                ValueJson = JsonSerializer.Serialize(observation.Value, JsonOptions),
                Confidence = observation.Confidence,
                Evidence = observation.Evidence
                    .Select(e => new EvidenceIndexEntry
                    {
                        RefType = e.RefType,
                        Ref = e.Ref,
                        Quote = e.Quote
                    })
                    .ToArray(),
                AsOf = observation.AsOf,
                ProducedBy = observation.ProducedBy.Id,
                Content = content,
                ContentVector = embedding.ToArray(),
                Status = "produced",
                Scope = BuildScopeEntry(observation, _options.DualWriteScopeMatterId)
            };

            var searchClient = _searchIndexClient.GetSearchClient(_options.InsightsIndexName);
            var response = await searchClient.MergeOrUploadDocumentsAsync(
                new[] { doc },
                cancellationToken: ct);

            var succeeded = response.Value.Results.All(r => r.Succeeded);
            if (!succeeded)
            {
                var firstFailure = response.Value.Results.FirstOrDefault(r => !r.Succeeded);
                throw new InvalidOperationException(
                    $"ObservationIndexUpserter upsert reported partial failure: " +
                    $"observationId={observation.Id} status={firstFailure?.Status} " +
                    $"errorMessage={firstFailure?.ErrorMessage}");
            }

            _logger.Log(
                LogLevel.Information,
                UpsertedEvent,
                "ObservationIndexUpserter upserted: observationId={ObservationId} predicate={Predicate} subject={Subject} indexName={IndexName}",
                observation.Id, observation.Predicate, observation.Subject, _options.InsightsIndexName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(
                LogLevel.Error,
                FailedEvent,
                ex,
                "ObservationIndexUpserter upsert failed: observationId={ObservationId} predicate={Predicate} subject={Subject}",
                observation.Id, observation.Predicate, observation.Subject);
            throw;
        }
    }

    /// <summary>
    /// Wave D6 (task 035) — projects <see cref="ObservationArtifact.Scope"/> + <see cref="ObservationArtifact.Subject"/>
    /// onto the index's top-level <c>scope</c> ComplexType per design-a6 §4.4 writer-behavior table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subject parsing is intentionally permissive (matches r1's <c>DataverseLiveFactResolver</c>
    /// shape). When the subject does NOT match <c>&lt;scheme&gt;:&lt;id&gt;</c>, the entity fields are
    /// left null and the writer falls back to whatever the Observation's <c>Scope.MatterId</c> already
    /// carries — preserving Phase 1 behavior for legacy producers.
    /// </para>
    /// <para>
    /// Dual-write rule per design-a6 §4.4: when scheme is "matter" AND
    /// <see cref="AiSearchOptions.DualWriteScopeMatterId"/> is true, <c>scope.matterId</c> is
    /// populated alongside the canonical <c>scope.entityType</c>/<c>scope.entityId</c>.
    /// This preserves NFR-08 for any consumer (Wave E1 RAG retriever, future Phase 1 callers)
    /// that filters by <c>scope/matterId eq '…'</c>.
    /// </para>
    /// </remarks>
    internal static ScopeIndexEntry BuildScopeEntry(ObservationArtifact observation, bool dualWriteMatterId)
    {
        var (scheme, entityId) = ParseSubject(observation.Subject);

        // matterId: dual-write when scheme="matter" + flag on, OR pass-through from Observation.Scope.MatterId
        string? matterId = null;
        if (string.Equals(scheme, "matter", StringComparison.OrdinalIgnoreCase) && dualWriteMatterId)
        {
            matterId = entityId;
        }
        else if (!string.IsNullOrWhiteSpace(observation.Scope.MatterId))
        {
            // Honor any matterId the Observation already carries (Phase 1 legacy producers).
            matterId = observation.Scope.MatterId;
        }

        return new ScopeIndexEntry
        {
            MatterId = matterId,
            EntityType = scheme,
            EntityId = entityId,
            TenantId = observation.Scope.TenantId,
            PracticeArea = observation.Scope.PracticeArea
        };
    }

    /// <summary>
    /// Parses an Observation subject of the shape <c>&lt;scheme&gt;:&lt;entityId&gt;</c>. Returns
    /// (null, null) when the subject does not match. Permissive — the entityId portion is not
    /// validated as a GUID (Wave D5 <c>ISubjectParser</c> handles strict validation at the
    /// dispatch layer; this writer is downstream and trusts upstream validation).
    /// </summary>
    internal static (string? Scheme, string? EntityId) ParseSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return (null, null);
        }

        var colonIdx = subject.IndexOf(':');
        if (colonIdx <= 0 || colonIdx >= subject.Length - 1)
        {
            return (null, null);
        }

        var scheme = subject[..colonIdx].Trim().ToLowerInvariant();
        var entityId = subject[(colonIdx + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(entityId))
        {
            return (null, null);
        }

        return (scheme, entityId);
    }

    /// <summary>
    /// Build the searchable content string that gets embedded + stored as the index's
    /// <c>content</c> field. Format: <c>"{predicate} = {valueRawAsText} ({firstQuote})"</c>.
    /// Quote portion omitted when no evidence quote is available.
    /// </summary>
    private static string ComposeContent(ObservationArtifact observation)
    {
        var sb = new StringBuilder();
        sb.Append(observation.Predicate);
        sb.Append(" = ");
        sb.Append(observation.Value.Raw.ToString());

        var firstQuote = observation.Evidence
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Quote))?.Quote;
        if (!string.IsNullOrWhiteSpace(firstQuote))
        {
            sb.Append(" (");
            sb.Append(firstQuote);
            sb.Append(')');
        }

        return sb.ToString();
    }
}
