using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Projects Dataverse entity metadata into the slim <see cref="EntityMetadataDto"/> shape consumed by the
/// Spaarke DataGrid Framework R1 (FR-BFF-03). Caches the projected DTO for 6 hours via the shared Redis
/// <see cref="IDistributedCache"/> (ADR-029 — single Redis instance per BFF; per task 010 Q3 decision).
/// </summary>
/// <remarks>
/// <para>
/// <b>Cache key</b>: <c>sdap:dv:entitymetadata:{entityLogicalName}</c>. Task 010 §6 specifies a
/// version-pinned key (<c>:v{globalMetadataVersion}</c>) for instant invalidation on solution import.
/// For R1 we use the unversioned key with a 6h absolute TTL — solution-import staleness is acceptable
/// within that window (see <c>012-deviations.md</c>). Version pinning is a deferred follow-up.
/// </para>
/// <para>
/// <b>Cache failure mode</b>: graceful — if Redis is unreachable, we fall back to a direct Dataverse
/// fetch and log a warning (matches the <c>GraphMetadataCache</c> precedent at lines 187-193). Cache
/// failures NEVER block a successful response.
/// </para>
/// <para>
/// <b>Payload budget</b>: &lt;50KB per entity per FR-BFF-03. The DTO projection drops localized label
/// arrays, privilege catalogs, and audit/security descriptors — the fields that make raw
/// <c>EntityMetadata</c> hundreds of KB.
/// </para>
/// <para>
/// <b>Auth path</b>: app-only via <see cref="DataverseServiceClientImpl.OrganizationService"/>. Metadata
/// is tenant-wide and does not vary per user; the per-user Read-privilege check happens in
/// <c>DataverseAuthorizationFilter</c> (task 011) BEFORE this service is invoked.
/// </para>
/// </remarks>
public sealed class MetadataService
{
    private readonly IDataverseService _dataverseService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MetadataService> _logger;

    // 6h absolute TTL per FR-BFF-03. Solution-import staleness within the window is acceptable for R1.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private const string CacheKeyPrefix = "sdap:dv:entitymetadata:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MetadataService(
        IDataverseService dataverseService,
        IDistributedCache cache,
        ILogger<MetadataService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the projected metadata DTO for the requested entity. Cache hit returns immediately;
    /// cache miss issues a single <see cref="RetrieveEntityRequest"/> with
    /// <see cref="EntityFilters.Attributes"/> and projects to the slim DTO.
    /// </summary>
    /// <param name="entityLogicalName">Dataverse entity logical name (lowercase, e.g., <c>systemuser</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The projected metadata DTO.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="entityLogicalName"/> is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the entity is not found in Dataverse metadata.</exception>
    public async Task<EntityMetadataDto> GetMetadataAsync(string entityLogicalName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            throw new ArgumentException("Entity logical name cannot be null or empty.", nameof(entityLogicalName));
        }

        // Lowercase normalize for cache key consistency (Dataverse logical names are case-insensitive).
        var normalizedName = entityLogicalName.Trim().ToLowerInvariant();
        var cacheKey = CacheKeyPrefix + normalizedName;

        // 1. Cache lookup (graceful on failure)
        var cached = await TryGetFromCacheAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Metadata cache HIT for {EntityLogicalName}", normalizedName);
            return cached;
        }

        // 2. Cache miss — fetch from Dataverse and project
        _logger.LogDebug("Metadata cache MISS for {EntityLogicalName}; fetching from Dataverse", normalizedName);

        var entityMetadata = await FetchEntityMetadataAsync(normalizedName, ct);
        var dto = ProjectToDto(entityMetadata);

        // 3. Cache write (graceful on failure)
        await TrySetInCacheAsync(cacheKey, dto, ct);

        return dto;
    }

    private async Task<EntityMetadata> FetchEntityMetadataAsync(string entityLogicalName, CancellationToken ct)
    {
        var serviceClient = GetServiceClient();

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true
        };

        try
        {
            // ServiceClient.Execute is synchronous; wrap in Task.Run with the cancellation token.
            var response = (RetrieveEntityResponse)await Task.Run(
                () => serviceClient.Execute(request), ct).ConfigureAwait(false);

            return response.EntityMetadata;
        }
        catch (Exception ex) when (
            ex.Message.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Entity '{EntityLogicalName}' not found in Dataverse metadata", entityLogicalName);
            throw new InvalidOperationException(
                $"Entity '{entityLogicalName}' not found in Dataverse metadata.", ex);
        }
    }

    /// <summary>
    /// Projects a full <see cref="EntityMetadata"/> to the slim <see cref="EntityMetadataDto"/>.
    /// Drops localized labels, privilege catalogs, security descriptors, and audit metadata to stay
    /// under the &lt;50KB per-entity budget (FR-BFF-03).
    /// </summary>
    private static EntityMetadataDto ProjectToDto(EntityMetadata entity)
    {
        var attributes = entity.Attributes ?? Array.Empty<AttributeMetadata>();

        var projectedAttributes = new List<AttributeDto>(attributes.Length);

        foreach (var attr in attributes)
        {
            if (attr is null || string.IsNullOrEmpty(attr.LogicalName))
            {
                continue;
            }

            projectedAttributes.Add(ProjectAttribute(attr));
        }

        return new EntityMetadataDto(
            LogicalName: entity.LogicalName ?? string.Empty,
            PrimaryIdAttribute: entity.PrimaryIdAttribute ?? string.Empty,
            PrimaryNameAttribute: entity.PrimaryNameAttribute ?? string.Empty,
            Attributes: projectedAttributes);
    }

    private static AttributeDto ProjectAttribute(AttributeMetadata attr)
    {
        var attributeType = attr.AttributeType?.ToString() ?? "Unknown";
        var format = ExtractFormat(attr);
        var optionSet = ExtractOptionSet(attr);

        var isPrimaryName = attr.IsPrimaryName == true;
        var isPrimaryId = attr.IsPrimaryId == true;

        return new AttributeDto(
            LogicalName: attr.LogicalName!,
            AttributeType: attributeType,
            Format: format,
            IsPrimaryName: isPrimaryName,
            IsPrimaryId: isPrimaryId,
            OptionSet: optionSet);
    }

    /// <summary>
    /// Extract the attribute's format (e.g., <c>Email</c>, <c>Url</c>, <c>DateOnly</c>) when the SDK
    /// exposes a <c>Format</c> property for the attribute type. Uses reflection-free typed access via
    /// pattern matching on the concrete metadata subclass.
    /// </summary>
    private static string? ExtractFormat(AttributeMetadata attr) => attr switch
    {
        StringAttributeMetadata s => s.Format?.ToString(),
        MemoAttributeMetadata m => m.Format?.ToString(),
        DateTimeAttributeMetadata d => d.Format?.ToString(),
        IntegerAttributeMetadata i => i.Format?.ToString(),
        _ => null
    };

    /// <summary>
    /// Extract option-set options for picklist / state / status attributes. Returns <c>null</c> for
    /// non-option-set attributes. Only <c>Value</c>, <c>Label</c>, and <c>Color</c> are projected — the
    /// localized label arrays are dropped to stay under the per-entity payload budget.
    /// </summary>
    private static OptionSetDto? ExtractOptionSet(AttributeMetadata attr)
    {
        OptionSetMetadata? optionSetMetadata = attr switch
        {
            PicklistAttributeMetadata p => p.OptionSet,
            StateAttributeMetadata s => s.OptionSet,
            StatusAttributeMetadata st => st.OptionSet,
            MultiSelectPicklistAttributeMetadata ms => ms.OptionSet,
            _ => null
        };

        if (optionSetMetadata?.Options is null || optionSetMetadata.Options.Count == 0)
        {
            return null;
        }

        var options = new List<OptionDto>(optionSetMetadata.Options.Count);
        foreach (var opt in optionSetMetadata.Options)
        {
            if (opt is null || !opt.Value.HasValue)
            {
                continue;
            }

            var label = opt.Label?.UserLocalizedLabel?.Label ?? string.Empty;

            options.Add(new OptionDto(
                Value: opt.Value.Value,
                Label: label,
                Color: opt.Color));
        }

        return new OptionSetDto(options);
    }

    private async Task<EntityMetadataDto?> TryGetFromCacheAsync(string cacheKey, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // SYSTEM-LEVEL EXCEPTION (NFR-08): Dataverse entity schema metadata is org-wide (one BFF / one Redis instance per org per ADR-029); tenant-scoping would defeat the purpose of the schema cache.
            var cached = await _cache.GetStringAsync(cacheKey, ct).ConfigureAwait(false);
            sw.Stop();

            if (cached is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<EntityMetadataDto>(cached, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "Error reading entity metadata from cache for key {CacheKey}; falling back to Dataverse",
                cacheKey);
            return null;
        }
    }

    private async Task TrySetInCacheAsync(string cacheKey, EntityMetadataDto dto, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(dto, JsonOptions);

            // SYSTEM-LEVEL EXCEPTION (NFR-08): Dataverse entity schema metadata is org-wide (one BFF / one Redis instance per org per ADR-029); tenant-scoping would defeat the purpose of the schema cache.
            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error caching entity metadata for key {CacheKey}; caller will still receive the response",
                cacheKey);
            // Cache failures are non-fatal — caller already has the DTO.
        }
    }

    /// <summary>
    /// Unwrap the underlying <see cref="ServiceClient"/> from <see cref="IDataverseService"/>.
    /// Follows the same pattern used in <see cref="Services.Workspace.TodoGenerationService"/> +
    /// <see cref="Services.Finance.SpendSnapshotService"/> for generic SDK operations not exposed on
    /// the <see cref="IDataverseService"/> interface.
    /// </summary>
    private ServiceClient GetServiceClient()
    {
        if (_dataverseService is DataverseServiceClientImpl impl)
        {
            return impl.OrganizationService;
        }

        throw new InvalidOperationException(
            $"MetadataService requires IDataverseService to be backed by DataverseServiceClientImpl. " +
            $"Actual type: {_dataverseService?.GetType().Name ?? "null"}.");
    }
}
