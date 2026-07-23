using System.Globalization;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Deterministic <see cref="IConstrainedFieldResolver"/> (spec FR-B1). Sources a closed field's valid set from
/// Dataverse metadata and applies the pure <see cref="ConstrainedFieldMatcher"/> ladder — never the LLM.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuse-first (CLAUDE.md §11).</b> The valid-set sourcing reuses existing infrastructure — the only
/// genuinely-new code is the match ladder (<see cref="ConstrainedFieldMatcher"/>) and this thin orchestration:
/// </para>
/// <list type="bullet">
/// <item><b>Option-set fields</b> → <see cref="MetadataService.GetMetadataAsync"/> already returns
/// <c>OptionSetDto.Options</c> as <c>{Value, Label}</c> pairs (Redis-cached 6h). No new metadata query.</item>
/// <item><b>Lookup fields</b> → the target reference entity is discovered from
/// <c>LookupAttributeMetadata.Targets</c>; its primary id/name come from <see cref="MetadataService"/>; the
/// <c>(id, name)</c> candidates come from a single OData query over the shared authenticated Dataverse client
/// provided by <see cref="DataverseHttpServiceBase"/>.</item>
/// </list>
/// <para>
/// Candidate lists are cached per field (<c>sdap:cfr:{entity}.{attribute}</c>, 10-minute TTL) via the shared
/// <see cref="IDistributedCache"/>; cache failures are non-fatal. A field with no closed set, an unknown
/// attribute, or a sourcing error returns <see cref="ResolutionConfidence.None"/> without throwing
/// (ADR-032 quiet no-op).
/// </para>
/// </remarks>
public class ConstrainedFieldResolver : DataverseHttpServiceBase, IConstrainedFieldResolver
{
    private readonly MetadataService _metadata;
    private readonly IDistributedCache _cache;
    private readonly ConstrainedFieldMatchOptions _matchOptions;

    private const string CacheKeyPrefix = "sdap:cfr:";
    private static readonly TimeSpan CandidateCacheTtl = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConstrainedFieldResolver(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<ConstrainedFieldResolver> logger,
        MetadataService metadata,
        IDistributedCache cache)
        : base(httpClient, configuration, credential, logger)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _matchOptions = new ConstrainedFieldMatchOptions();
    }

    /// <inheritdoc />
    public async Task<ConstrainedFieldResolution> ResolveAsync(
        string entityLogicalName,
        string attributeLogicalName,
        string proposedValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(proposedValue) ||
            string.IsNullOrWhiteSpace(entityLogicalName) ||
            string.IsNullOrWhiteSpace(attributeLogicalName))
        {
            return ConstrainedFieldResolution.NoneResult([]);
        }

        IReadOnlyList<FieldCandidate> candidates;
        try
        {
            candidates = await GetCandidatesAsync(entityLogicalName, attributeLogicalName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ADR-032 quiet no-op: a sourcing failure is not a closed set we can resolve against.
            Logger.LogWarning(ex,
                "[CFR] Candidate sourcing failed for {Entity}.{Attribute}; returning None",
                entityLogicalName, attributeLogicalName);
            return ConstrainedFieldResolution.NoneResult([]);
        }

        if (candidates.Count == 0)
        {
            // No closed set (free text/number/date, unknown attribute) — quiet no-op.
            return ConstrainedFieldResolution.NoneResult([]);
        }

        var match = ConstrainedFieldMatcher.Match(proposedValue, candidates, _matchOptions);

        return match.Confidence switch
        {
            ResolutionConfidence.High => new ConstrainedFieldResolution
            {
                Resolved = match.Best!.Value,
                Confidence = ResolutionConfidence.High,
                Candidates = candidates,
            },
            ResolutionConfidence.Low => new ConstrainedFieldResolution
            {
                Resolved = match.Best!.Value,
                Confidence = ResolutionConfidence.Low,
                Candidates = TopFirst(candidates, match.Best),
            },
            _ => ConstrainedFieldResolution.NoneResult(candidates),
        };
    }

    /// <summary>
    /// Sources the closed valid set for a field. Virtual so unit tests can substitute canned candidates
    /// without touching Dataverse (ADR-038 — no <c>Mock&lt;HttpMessageHandler&gt;</c>). Returns an empty list
    /// for a field with no closed set or an unknown attribute.
    /// </summary>
    protected virtual async Task<IReadOnlyList<FieldCandidate>> GetCandidatesAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyPrefix + entityLogicalName.ToLowerInvariant() + "." + attributeLogicalName.ToLowerInvariant();

        var cached = await TryGetCandidatesFromCacheAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var entityMeta = await _metadata.GetMetadataAsync(entityLogicalName, cancellationToken);
        var attribute = entityMeta.Attributes.FirstOrDefault(a =>
            string.Equals(a.LogicalName, attributeLogicalName, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<FieldCandidate> candidates;
        if (attribute is null)
        {
            candidates = [];
        }
        else if (attribute.OptionSet is { Options.Count: > 0 } optionSet)
        {
            candidates = optionSet.Options
                .Where(o => !string.IsNullOrWhiteSpace(o.Label))
                .Select(o => new FieldCandidate(o.Value.ToString(CultureInfo.InvariantCulture), o.Label))
                .ToList();
        }
        else if (string.Equals(attribute.AttributeType, "Lookup", StringComparison.OrdinalIgnoreCase))
        {
            candidates = await GetLookupCandidatesAsync(entityLogicalName, attributeLogicalName, cancellationToken);
        }
        else
        {
            candidates = [];
        }

        await TrySetCandidatesInCacheAsync(cacheKey, candidates, cancellationToken);
        return candidates;
    }

    /// <summary>
    /// Resolves a lookup field's candidate <c>(recordId, name)</c> set: discover the target reference entity,
    /// read its primary id/name from metadata, then query active records via the shared Dataverse client.
    /// </summary>
    private async Task<IReadOnlyList<FieldCandidate>> GetLookupCandidatesAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var targetEntity = await GetLookupTargetAsync(entityLogicalName, attributeLogicalName, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return [];
        }

        var targetMeta = await _metadata.GetMetadataAsync(targetEntity, cancellationToken);
        var idAttribute = targetMeta.PrimaryIdAttribute;
        var nameAttribute = targetMeta.PrimaryNameAttribute;
        if (string.IsNullOrWhiteSpace(idAttribute) || string.IsNullOrWhiteSpace(nameAttribute))
        {
            return [];
        }

        // Reference entities pluralize as logicalName + "s" (matches the LookupChoicesResolver convention).
        var entitySet = targetEntity + "s";
        var safeId = Sanitize(idAttribute);
        var safeName = Sanitize(nameAttribute);
        var url = $"{Sanitize(entitySet)}?$select={safeId},{safeName}&$filter=statecode eq 0&$orderby={safeName} asc&$top=500";

        var response = await Http.GetAsync(url, cancellationToken);
        await EnsureSuccessWithDiagnosticsAsync(response, $"CFR.GetLookupCandidates({entitySet})", cancellationToken);

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("value", out var valueArray) ||
            valueArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<FieldCandidate>();
        foreach (var item in valueArray.EnumerateArray())
        {
            if (!item.TryGetProperty(idAttribute, out var idProp) ||
                !item.TryGetProperty(nameAttribute, out var nameProp) ||
                nameProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : idProp.ToString();
            var name = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                candidates.Add(new FieldCandidate(id!, name!));
            }
        }

        return candidates;
    }

    /// <summary>Reads the lookup attribute's first target entity via <c>LookupAttributeMetadata.Targets</c>.</summary>
    private async Task<string?> GetLookupTargetAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken cancellationToken)
    {
        var url = $"EntityDefinitions(LogicalName='{Sanitize(entityLogicalName)}')" +
                  $"/Attributes(LogicalName='{Sanitize(attributeLogicalName)}')" +
                  "/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=Targets";

        var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning(
                "[CFR] Could not read lookup Targets for {Entity}.{Attribute} ({Status})",
                entityLogicalName, attributeLogicalName, response.StatusCode);
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (doc.RootElement.TryGetProperty("Targets", out var targets) &&
            targets.ValueKind == JsonValueKind.Array &&
            targets.GetArrayLength() > 0)
        {
            return targets[0].GetString();
        }

        return null;
    }

    private static IReadOnlyList<FieldCandidate> TopFirst(IReadOnlyList<FieldCandidate> candidates, FieldCandidate top)
    {
        if (candidates.Count == 0 || ReferenceEquals(candidates[0], top))
        {
            return candidates;
        }

        var ordered = new List<FieldCandidate>(candidates.Count) { top };
        ordered.AddRange(candidates.Where(c => !ReferenceEquals(c, top)));
        return ordered;
    }

    private static string Sanitize(string value) => value.Replace("'", string.Empty).Replace("/", string.Empty);

    private async Task<IReadOnlyList<FieldCandidate>?> TryGetCandidatesFromCacheAsync(string cacheKey, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, ct);
            if (cached is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<List<FieldCandidate>>(cached, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[CFR] Candidate cache read failed for {CacheKey}; sourcing fresh", cacheKey);
            return null;
        }
    }

    private async Task TrySetCandidatesInCacheAsync(string cacheKey, IReadOnlyList<FieldCandidate> candidates, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(candidates, JsonOptions);
            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CandidateCacheTtl },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[CFR] Candidate cache write failed for {CacheKey}; non-fatal", cacheKey);
        }
    }
}
