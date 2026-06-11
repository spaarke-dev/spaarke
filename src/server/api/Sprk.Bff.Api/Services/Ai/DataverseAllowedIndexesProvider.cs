using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Dataverse-backed implementation of <see cref="IAllowedIndexesProvider"/>. Reads the
/// active rows of <c>sprk_aisearchindex</c> (statecode = 0), projects
/// <c>sprk_searchindexname</c>, and caches the resulting <c>HashSet&lt;string&gt;</c> in
/// <see cref="IMemoryCache"/> for 5 minutes.
/// </summary>
/// <remarks>
/// <para>
/// multi-container-multi-index-r1 Phase G (task 102). Replaces the static
/// <see cref="AiSearchOptions.AllowedIndexes"/> validator with a Dataverse query so the
/// <c>sprk_aisearchindex</c> table becomes the single source of truth — adding a new
/// index requires only creating a new catalog row (no App Service config update).
/// </para>
/// <para>
/// <b>Cache strategy</b>: single key (<c>sprk_aisearchindex:active</c>), 5-minute absolute
/// TTL. The cache stores the full active-names <see cref="HashSet{T}"/> (case-insensitive),
/// not per-index booleans, so a single Dataverse round-trip serves every
/// <see cref="IsAllowedAsync"/> call within a TTL window.
/// </para>
/// <para>
/// <b>Failure / empty handling</b> (per Phase G spec §13 Q4): when the Dataverse fetch
/// throws OR returns zero rows, the provider falls back to
/// <see cref="AiSearchOptions.AllowedIndexes"/> and emits a single WARNING log entry per
/// TTL cycle (the fallback set is cached too, so the warning isn't spammed). This keeps
/// the BFF operational when the catalog table is empty (e.g., before task 101 seeds it)
/// or briefly unreachable.
/// </para>
/// <para>
/// <b>Lifetime</b>: Singleton (matches <see cref="IMemoryCache"/>; provider holds no
/// per-request state). Dependencies: <see cref="IGenericEntityService"/> (Scoped — see
/// note below), <see cref="IMemoryCache"/> (Singleton),
/// <see cref="IOptions{TOptions}"/> of <see cref="AiSearchOptions"/> (Singleton),
/// <see cref="ILogger{T}"/> (Singleton).
/// </para>
/// <para>
/// <b>Scoped-in-Singleton handling</b>: <see cref="IGenericEntityService"/> is registered
/// scoped, so this Singleton provider obtains a fresh instance per cache-miss load via
/// <see cref="IServiceProvider.CreateScope"/>. This avoids the captive-dependency
/// anti-pattern while keeping the cache itself process-wide.
/// </para>
/// </remarks>
public sealed class DataverseAllowedIndexesProvider : IAllowedIndexesProvider
{
    /// <summary>Cache key for the active-names <see cref="HashSet{T}"/>.</summary>
    internal const string CacheKey = "sprk_aisearchindex:active";

    /// <summary>Absolute TTL for the cached set (5 minutes).</summary>
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // FetchXml constants — kept close to the impl to make the wire contract obvious.
    private const string CatalogEntity = "sprk_aisearchindex";
    private const string CatalogNameColumn = "sprk_searchindexname";
    private const string FetchXml = """
        <fetch>
          <entity name="sprk_aisearchindex">
            <attribute name="sprk_searchindexname" />
            <filter>
              <condition attribute="statecode" operator="eq" value="0" />
            </filter>
          </entity>
        </fetch>
        """;

    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly AiSearchOptions _options;
    private readonly ILogger<DataverseAllowedIndexesProvider> _logger;

    public DataverseAllowedIndexesProvider(
        IServiceProvider serviceProvider,
        IMemoryCache cache,
        IOptions<AiSearchOptions> options,
        ILogger<DataverseAllowedIndexesProvider> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsAllowedAsync(string indexName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return false;
        }

        var allowed = await GetOrLoadAllowedSetAsync(ct);
        return allowed.Contains(indexName);
    }

    /// <summary>
    /// Returns the cached active-names set, loading from Dataverse on cache miss.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="IMemoryCache.GetOrCreateAsync{TItem}(object, Func{ICacheEntry, Task{TItem}})"/>
    /// so concurrent cache-miss callers within a single TTL window all share the same load.
    /// </remarks>
    private async Task<HashSet<string>> GetOrLoadAllowedSetAsync(CancellationToken ct)
    {
        var cached = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await LoadAsync(ct);
        });

        // GetOrCreateAsync returns Task<TItem>? in theory; in practice with our factory it is
        // always non-null. Guard defensively.
        return cached ?? new HashSet<string>(_options.AllowedIndexes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads the active-names set from Dataverse. On exception OR empty result, falls back
    /// to the appsettings <see cref="AiSearchOptions.AllowedIndexes"/> array and emits a
    /// single WARNING log entry (the result is cached for the TTL window so the warning
    /// isn't spammed on every request).
    /// </summary>
    private async Task<HashSet<string>> LoadAsync(CancellationToken ct)
    {
        try
        {
            // Resolve a fresh scope so we can consume the scoped IGenericEntityService from
            // this Singleton without taking a captive dependency.
            using var scope = _serviceProvider.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();

            var fetch = new FetchExpression(FetchXml);
            var results = await entityService.RetrieveMultipleAsync(fetch, ct);

            var names = results.Entities
                .Select(e => e.GetAttributeValue<string>(CatalogNameColumn))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (names.Count == 0)
            {
                _logger.LogWarning(
                    "PhaseG.AllowedIndexes Dataverse empty/failed — falling back to appsettings (0 active rows in sprk_aisearchindex)");
                return BuildAppsettingsFallback();
            }

            _logger.LogDebug(
                "PhaseG.AllowedIndexes loaded {Count} active index names from Dataverse",
                names.Count);
            return names;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PhaseG.AllowedIndexes Dataverse empty/failed — falling back to appsettings");
            return BuildAppsettingsFallback();
        }
    }

    /// <summary>
    /// Builds the final-fallback set from <see cref="AiSearchOptions.AllowedIndexes"/>. The
    /// returned set is the same instance type/comparer as the Dataverse-loaded set so
    /// downstream <c>Contains</c> checks behave uniformly.
    /// </summary>
    private HashSet<string> BuildAppsettingsFallback()
    {
        return new HashSet<string>(
            _options.AllowedIndexes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }
}
