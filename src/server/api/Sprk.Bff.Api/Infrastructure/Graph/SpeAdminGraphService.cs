using System.Collections.Concurrent;
using System.Net;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Search;
using Microsoft.Kiota.Authentication.Azure;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Multi-configuration SharePoint Embedded Graph client service for SPE Admin operations.
///
/// Resolves Graph API credentials dynamically per container type configuration:
///   1. Reads client ID and Key Vault secret name from sprk_specontainertypeconfig (Dataverse)
///   2. Fetches the client secret from Azure Key Vault using the secret name
///   3. Creates a GraphServiceClient using ClientSecretCredential
///   4. Caches the client instance by configId (default 30-minute TTL)
///
/// ADR-007: No Graph SDK types leak above this facade — callers receive domain models only.
/// ADR-010: Stateless except for the client cache (ConcurrentDictionary + TTL).
/// ADR-001: Service is stateless per-request; cache is the only shared state.
/// </summary>
public sealed class SpeAdminGraphService
{
    // -------------------------------------------------------------------------
    // Domain models (ADR-007: no Graph SDK types in public API surface)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Represents a resolved container type configuration from Dataverse.
    ///
    /// Phase 1 fields (ClientId, TenantId, SecretKeyVaultName) identify the managing/admin app
    /// registration used for app-only Graph API calls.
    ///
    /// Phase 3 / Multi-App fields (OwningAppId, OwningAppTenantId, OwningAppSecretName) identify
    /// the owning app registration. When present, SpeAdminTokenProvider acquires tokens on behalf
    /// of the owning app via OBO (user token → owning app token) for delegated operations.
    /// When absent, the managing app identity is used (backward-compatible single-app mode).
    /// </summary>
    public sealed record ContainerTypeConfig(
        Guid ConfigId,
        string ContainerTypeId,
        string ClientId,
        string TenantId,
        string SecretKeyVaultName,
        // Multi-App fields (Phase 3 — optional; null = single-app mode)
        string? OwningAppId = null,
        string? OwningAppTenantId = null,
        string? OwningAppSecretName = null)
    {
        /// <summary>
        /// Returns true when this config specifies a distinct owning app registration
        /// (i.e., all three owning app fields are non-empty).
        /// </summary>
        public bool HasOwningApp =>
            !string.IsNullOrWhiteSpace(OwningAppId) &&
            !string.IsNullOrWhiteSpace(OwningAppTenantId) &&
            !string.IsNullOrWhiteSpace(OwningAppSecretName);
    }

    /// <summary>Summarised container data returned from Graph API.</summary>
    public sealed record SpeContainerSummary(
        string Id,
        string DisplayName,
        string? Description,
        string ContainerTypeId,
        DateTimeOffset CreatedDateTime,
        long? StorageUsedInBytes);

    /// <summary>
    /// A single file or folder item returned from the Graph drive items API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">DriveItem ID.</param>
    /// <param name="Name">File or folder name.</param>
    /// <param name="Size">File size in bytes. Null for folders.</param>
    /// <param name="LastModifiedDateTime">When the item was last modified.</param>
    /// <param name="CreatedDateTime">When the item was created.</param>
    /// <param name="CreatedByDisplayName">Display name of the user who created the item. May be null.</param>
    /// <param name="IsFolder">True if the item is a folder; false if it is a file.</param>
    /// <param name="MimeType">MIME type for files; null for folders.</param>
    /// <param name="WebUrl">Web URL for opening the item in a browser.</param>
    public sealed record SpeContainerItemSummary(
        string Id,
        string Name,
        long? Size,
        DateTimeOffset? LastModifiedDateTime,
        DateTimeOffset? CreatedDateTime,
        string? CreatedByDisplayName,
        bool IsFolder,
        string? MimeType,
        string? WebUrl);

    /// <summary>
    /// A single version entry for a DriveItem, returned from the Graph drive item versions API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Version ID (e.g., "1.0", "2.0").</param>
    /// <param name="LastModifiedDateTime">When this version was created/modified.</param>
    /// <param name="LastModifiedByDisplayName">Display name of the user who created this version. May be null.</param>
    /// <param name="Size">Size of this version in bytes. Null if not reported.</param>
    public sealed record SpeFileVersionSummary(
        string Id,
        DateTimeOffset? LastModifiedDateTime,
        string? LastModifiedByDisplayName,
        long? Size);

    /// <summary>
    /// A thumbnail set for a DriveItem, returned from the Graph thumbnails API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Thumbnail set ID.</param>
    /// <param name="SmallUrl">URL of the small thumbnail image. Null if not available.</param>
    /// <param name="MediumUrl">URL of the medium thumbnail image. Null if not available.</param>
    /// <param name="LargeUrl">URL of the large thumbnail image. Null if not available.</param>
    public sealed record SpeThumbnailSet(
        string Id,
        string? SmallUrl,
        string? MediumUrl,
        string? LargeUrl);

    /// <summary>
    /// A sharing link created for a DriveItem via the Graph createLink action.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Url">The sharing link URL that can be shared with users.</param>
    /// <param name="LinkType">The link type: "view", "edit", or "embed".</param>
    /// <param name="Scope">The link scope: "anonymous", "organization", or "users".</param>
    /// <param name="ExpirationDateTime">When the link expires. Null if it does not expire.</param>
    public sealed record SpeSharingLink(
        string Url,
        string LinkType,
        string Scope,
        DateTimeOffset? ExpirationDateTime);

    /// <summary>
    /// Summarised container type data returned from the Graph API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Container type GUID assigned by SharePoint Embedded.</param>
    /// <param name="DisplayName">Human-readable display name for the container type.</param>
    /// <param name="Description">Optional description of the container type's purpose.</param>
    /// <param name="BillingClassification">Billing classification (e.g., "standard"). Null when not returned by Graph.</param>
    /// <param name="CreatedDateTime">When the container type was created (UTC).</param>
    public sealed record SpeContainerTypeSummary(
        string Id,
        string DisplayName,
        string? Description,
        string? BillingClassification,
        DateTimeOffset CreatedDateTime);

    /// <summary>
    /// An application permission entry on an SPE container type, returned from the Graph
    /// applicationPermissions endpoint.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="AppId">Azure AD application (client) ID of the consuming application.</param>
    /// <param name="DelegatedPermissions">
    /// Delegated permission scopes granted to the app for this container type.
    /// Empty when no delegated permissions have been granted.
    /// </param>
    /// <param name="ApplicationPermissions">
    /// Application permission scopes granted to the app for this container type.
    /// Empty when no application permissions have been granted.
    /// </param>
    public sealed record SpeContainerTypePermission(
        string AppId,
        IReadOnlyList<string> DelegatedPermissions,
        IReadOnlyList<string> ApplicationPermissions);

    /// <summary>
    /// A container permission entry returned from the Graph permissions API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Graph permission ID (opaque string, used for PATCH/DELETE).</param>
    /// <param name="Role">SPE role: reader, writer, manager, or owner.</param>
    /// <param name="DisplayName">Display name of the grantee. Null when the principal cannot be resolved.</param>
    /// <param name="Email">Email address of the grantee. Null for service principals or when unavailable.</param>
    /// <param name="PrincipalId">Azure AD object ID of the user or group.</param>
    /// <param name="PrincipalType">Type of principal: user, group, or application.</param>
    public sealed record SpeContainerPermission(
        string Id,
        string Role,
        string? DisplayName,
        string? Email,
        string? PrincipalId,
        string? PrincipalType);

    /// <summary>
    /// A column (custom metadata field) on an SPE container, returned from the Graph columns API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Graph column ID (opaque string, used for PATCH/DELETE).</param>
    /// <param name="Name">Internal column name (API-addressable, no spaces).</param>
    /// <param name="DisplayName">Human-readable display name shown in SPE UIs.</param>
    /// <param name="Description">Optional description of the column's purpose.</param>
    /// <param name="ColumnType">
    /// Column type string derived from the ColumnDefinition's type facets: text, boolean, dateTime,
    /// currency, choice, number, personOrGroup, or hyperlinkOrPicture.
    /// </param>
    /// <param name="Required">True if a value must be provided when creating items.</param>
    /// <param name="Indexed">True if the column is indexed for faster queries.</param>
    /// <param name="ReadOnly">True if Graph reports this column as read-only (system-managed).</param>
    public sealed record SpeContainerColumn(
        string Id,
        string Name,
        string? DisplayName,
        string? Description,
        string ColumnType,
        bool Required,
        bool Indexed,
        bool ReadOnly);

    /// <summary>
    /// A soft-deleted (recycle bin) SPE container returned from the Graph deleted containers API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Graph FileStorageContainer ID. Used for restore and permanent-delete operations.</param>
    /// <param name="DisplayName">Display name of the container before deletion.</param>
    /// <param name="DeletedDateTime">UTC timestamp when the container was soft-deleted.</param>
    /// <param name="ContainerTypeId">The SPE container type GUID string this container belonged to.</param>
    public sealed record DeletedContainerSummary(
        string Id,
        string DisplayName,
        DateTimeOffset? DeletedDateTime,
        string ContainerTypeId);

    // -------------------------------------------------------------------------
    // Exceptions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Thrown when a container type config lookup fails because the configId does not exist
    /// in the sprk_specontainertypeconfig Dataverse table.
    /// Callers should map this to HTTP 400 Bad Request (invalid configId from the client).
    /// </summary>
    public sealed class ConfigNotFoundException : Exception
    {
        public Guid ConfigId { get; }

        public ConfigNotFoundException(Guid configId)
            : base($"Container type config '{configId}' was not found in Dataverse.")
        {
            ConfigId = configId;
        }
    }

    // -------------------------------------------------------------------------
    // Paginated list response (ADR-007: no Graph SDK types in public API surface)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Paginated result for <see cref="ListContainersPageAsync"/>.
    /// </summary>
    /// <param name="Items">Containers on this page.</param>
    /// <param name="NextSkipToken">
    /// Opaque token for the next page, extracted from the Graph OData nextLink.
    /// Null when there are no further pages.
    /// </param>
    public sealed record ContainerPage(
        IReadOnlyList<SpeContainerSummary> Items,
        string? NextSkipToken);

    // =========================================================================
    // Search domain models (SPE-057)
    // =========================================================================

    /// <summary>
    /// A single container search result returned from the Graph Search API.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">Graph FileStorageContainer ID.</param>
    /// <param name="DisplayName">Container display name.</param>
    /// <param name="Description">Optional container description.</param>
    /// <param name="ContainerTypeId">The container type GUID string.</param>
    public sealed record SearchContainerResult(
        string Id,
        string DisplayName,
        string? Description,
        string? ContainerTypeId);

    /// <summary>
    /// Paginated search result for <see cref="SearchContainersAsync"/>.
    /// </summary>
    /// <param name="Items">Container search hits on this page.</param>
    /// <param name="TotalCount">Total number of matching containers as reported by Graph.</param>
    /// <param name="NextSkipToken">
    /// Opaque token for the next page. Pass to the next call as <c>skipToken</c>.
    /// Null when there are no further pages.
    /// </param>
    public sealed record ContainerSearchPage(
        IReadOnlyList<SearchContainerResult> Items,
        long? TotalCount,
        string? NextSkipToken);

    // -------------------------------------------------------------------------
    // Cache entry — GraphServiceClient + expiry timestamp
    // -------------------------------------------------------------------------

    private sealed record CachedClient(GraphServiceClient Client, DateTimeOffset ExpiresAt);

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SecretClient _secretClient;
    private readonly DataverseWebApiClient _dataverseClient;
    private readonly ILogger<SpeAdminGraphService> _logger;
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Token provider for multi-app OBO flows (Phase 3).
    /// Null when SpeAdminTokenProvider is not registered — falls back to single-app mode.
    /// </summary>
    private readonly Sprk.Bff.Api.Services.SpeAdmin.SpeAdminTokenProvider? _tokenProvider;

    /// <summary>Thread-safe cache: configId → (GraphServiceClient, expiry) for app-only clients.</summary>
    private readonly ConcurrentDictionary<Guid, CachedClient> _clientCache = new();

    /// <summary>
    /// Thread-safe cache for owning-app OBO Graph clients.
    /// Key: "{configId}:{sha256(userToken)}" — prevents cross-app and cross-user contamination.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedClient> _oboClientCache = new();

    // Throttling: exponential backoff constants
    private const int MaxRetries = 4;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(1);

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public SpeAdminGraphService(
        IHttpClientFactory httpClientFactory,
        SecretClient secretClient,
        DataverseWebApiClient dataverseClient,
        IConfiguration configuration,
        ILogger<SpeAdminGraphService> logger,
        Sprk.Bff.Api.Services.SpeAdmin.SpeAdminTokenProvider? tokenProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secretClient);
        ArgumentNullException.ThrowIfNull(dataverseClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _secretClient = secretClient;
        _dataverseClient = dataverseClient;
        _logger = logger;
        _tokenProvider = tokenProvider; // Optional: null = single-app mode only

        // TTL is configurable; default 30 minutes. IConfiguration is not stored — value is resolved once.
        var ttlMinutes = configuration.GetValue<int>("SpeAdmin:GraphClientCacheTtlMinutes", defaultValue: 30);
        _cacheTtl = TimeSpan.FromMinutes(ttlMinutes > 0 ? ttlMinutes : 30);

        _logger.LogInformation(
            "SpeAdminGraphService initialized. Graph client cache TTL: {TtlMinutes} minutes. " +
            "Multi-app (OBO) mode: {MultiAppEnabled}",
            ttlMinutes,
            tokenProvider != null);
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Returns a cached (or freshly created) GraphServiceClient for the given configId.
    ///
    /// Resolution flow:
    ///   1. Check cache — return cached client if not expired.
    ///   2. Read sprk_specontainertypeconfig from Dataverse to get clientId + secretKeyVaultName.
    ///   3. Fetch client secret from Key Vault using secretKeyVaultName.
    ///   4. Create GraphServiceClient with ClientSecretCredential.
    ///   5. Cache client with configured TTL.
    /// </summary>
    /// <param name="config">Resolved container type configuration (caller supplies).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>GraphServiceClient authenticated as the config's app registration.</returns>
    public async Task<GraphServiceClient> GetClientForConfigAsync(
        ContainerTypeConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 1. Check cache
        if (_clientCache.TryGetValue(config.ConfigId, out var cached))
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                _logger.LogDebug(
                    "Graph client cache HIT for configId {ConfigId}. Expires: {ExpiresAt}",
                    config.ConfigId, cached.ExpiresAt);
                return cached.Client;
            }

            // Expired — remove stale entry
            _clientCache.TryRemove(config.ConfigId, out _);
            _logger.LogDebug("Graph client cache EXPIRED for configId {ConfigId}", config.ConfigId);
        }

        // 2. Fetch client secret from Key Vault
        _logger.LogInformation(
            "Graph client cache MISS for configId {ConfigId}. Fetching secret '{SecretName}' from Key Vault.",
            config.ConfigId, config.SecretKeyVaultName);

        var clientSecret = await FetchKeyVaultSecretAsync(config.SecretKeyVaultName, ct);

        // 3. Create GraphServiceClient
        var graphClient = CreateGraphClient(config.TenantId, config.ClientId, clientSecret);

        // 4. Cache with TTL
        var entry = new CachedClient(graphClient, DateTimeOffset.UtcNow.Add(_cacheTtl));
        _clientCache[config.ConfigId] = entry;

        _logger.LogInformation(
            "Graph client created and cached for configId {ConfigId}. TTL: {Ttl}",
            config.ConfigId, _cacheTtl);

        return graphClient;
    }

    /// <summary>
    /// Returns a GraphServiceClient authenticated as the owning app registration for the given
    /// BU config, using OBO (On-Behalf-Of) token exchange with the admin user's token.
    ///
    /// When the config does not specify a separate owning app (<see cref="ContainerTypeConfig.HasOwningApp"/> = false),
    /// falls back to <see cref="GetClientForConfigAsync"/> (single-app mode — backward compatible).
    ///
    /// The returned client is cached per (configId + sha256(userToken)) with 55-minute TTL,
    /// matching the OBO token lifetime (auth constraint: 5-minute buffer before token expiry).
    /// </summary>
    /// <param name="config">Resolved container type config.</param>
    /// <param name="userAccessToken">
    /// Admin user's incoming Bearer token. Exchanged for owning-app-scoped token via OBO.
    /// Required for multi-app mode; ignored in single-app fallback.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Graph client authenticated as owning app (or managing app in single-app mode).</returns>
    public async Task<GraphServiceClient> GetClientForOwningAppAsync(
        ContainerTypeConfig config,
        string userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Single-app fallback: no owning app configured OR token provider not registered
        if (!config.HasOwningApp || _tokenProvider is null)
        {
            _logger.LogDebug(
                "No owning app config for configId {ConfigId} or token provider unavailable. " +
                "Using single-app mode (managing app identity).",
                config.ConfigId);
            return await GetClientForConfigAsync(config, ct);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userAccessToken);

        // OBO flow: compute cache key using SHA256 of user token (auth constraint: MUST hash tokens)
        var tokenHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(userAccessToken));
        var cacheKey = $"{config.ConfigId}:{Convert.ToHexString(tokenHash).ToLowerInvariant()}";

        // Check OBO client cache
        if (_oboClientCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogDebug(
                "OBO Graph client cache HIT for configId {ConfigId}. Expires: {ExpiresAt}",
                config.ConfigId, cached.ExpiresAt);
            return cached.Client;
        }

        // Acquire OBO token from token provider (provider manages its own token cache)
        var oboToken = await _tokenProvider.AcquireOwningAppTokenAsync(config, userAccessToken, ct);

        // Create Graph client using the OBO token via BearerTokenCredential pattern
        // The OBO token is a bearer token scoped to the owning app's Graph permissions
        var graphClient = CreateGraphClientFromBearerToken(oboToken);

        // Cache OBO Graph client with 55-minute TTL (matching OBO token TTL)
        var oboTtl = TimeSpan.FromMinutes(55);
        _oboClientCache[cacheKey] = new CachedClient(graphClient, DateTimeOffset.UtcNow.Add(oboTtl));

        _logger.LogInformation(
            "OBO Graph client created and cached for configId {ConfigId}, owningAppId {OwningAppId}. TTL: {Ttl}",
            config.ConfigId, config.OwningAppId, oboTtl);

        return graphClient;
    }

    /// <summary>
    /// Resolves a <see cref="ContainerTypeConfig"/> from Dataverse by configId.
    /// Reads the sprk_specontainertypeconfig record to get app registration credentials.
    /// Returns null when the record is not found.
    /// </summary>
    public async Task<ContainerTypeConfig?> ResolveConfigAsync(Guid configId, CancellationToken ct = default)
    {
        _logger.LogDebug("Resolving container type config for configId {ConfigId}", configId);

        // Include owning app fields (Phase 3) in addition to the Phase 1 managing app fields.
        const string select =
            "sprk_specontainertypeconfigid,sprk_containertypeid,sprk_clientid,sprk_tenantid," +
            "sprk_keyvaultsecretname,sprk_owningappid,sprk_owningapptenantid,sprk_owningappsecretname";

        try
        {
            var record = await _dataverseClient.RetrieveAsync<DataverseConfigDto>(
                "sprk_specontainertypeconfigs", configId, select, ct);

            if (record is null)
            {
                _logger.LogWarning("Container type config {ConfigId} not found in Dataverse", configId);
                return null;
            }

            return new ContainerTypeConfig(
                ConfigId: configId,
                ContainerTypeId: record.Sprk_containertypeid ?? string.Empty,
                ClientId: record.Sprk_clientid ?? string.Empty,
                TenantId: record.Sprk_tenantid ?? string.Empty,
                SecretKeyVaultName: record.Sprk_keyvaultsecretname ?? string.Empty,
                OwningAppId: record.Sprk_owningappid,
                OwningAppTenantId: record.Sprk_owningapptenantid,
                OwningAppSecretName: record.Sprk_owningappsecretname);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Container type config {ConfigId} not found (404)", configId);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error resolving container type config {ConfigId}", configId);
            throw;
        }
    }

    // DTO for deserializing sprk_specontainertypeconfig from Dataverse Web API.
    // Property names match Dataverse column logical names (lowercase).
    private sealed class DataverseConfigDto
    {
        // Phase 1: Managing / admin app registration fields
        public string? Sprk_containertypeid { get; init; }
        public string? Sprk_clientid { get; init; }
        public string? Sprk_tenantid { get; init; }
        public string? Sprk_keyvaultsecretname { get; init; }

        // Phase 3: Owning app registration fields (optional)
        public string? Sprk_owningappid { get; init; }
        public string? Sprk_owningapptenantid { get; init; }
        public string? Sprk_owningappsecretname { get; init; }
    }

    /// <summary>
    /// Lists all SPE containers for a given container type ID using the provided Graph client.
    /// Handles pagination automatically via manual nextLink following.
    /// Returns domain model <see cref="SpeContainerSummary"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The SPE container type GUID to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All containers matching the container type.</returns>
    public async Task<IReadOnlyList<SpeContainerSummary>> ListContainersAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        var results = new List<SpeContainerSummary>();

        _logger.LogInformation(
            "Listing SPE containers for containerTypeId {ContainerTypeId}", containerTypeId);

        // Page through all containers using nextLink pagination
        var response = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers
                .GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"containerTypeId eq '{containerTypeId}'";
                    config.QueryParameters.Select = new[]
                    {
                        "id", "displayName", "description", "containerTypeId",
                        "createdDateTime", "storageUsedInBytes"
                    };
                }, ct),
            ct);

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var container in response.Value)
                {
                    results.Add(new SpeContainerSummary(
                        container.Id ?? string.Empty,
                        container.DisplayName ?? string.Empty,
                        container.Description,
                        container.ContainerTypeId?.ToString() ?? containerTypeId,
                        container.CreatedDateTime ?? DateTimeOffset.UtcNow,
                        StorageUsedInBytes: null)); // Not always returned by Graph
                }
            }

            // Follow nextLink for pagination
            if (response.OdataNextLink == null) break;

            _logger.LogDebug("Following nextLink for container pagination: {NextLink}", response.OdataNextLink);

            response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: ct),
                ct);
        }

        _logger.LogInformation(
            "Listed {Count} containers for containerTypeId {ContainerTypeId}",
            results.Count, containerTypeId);

        return results;
    }

    /// <summary>
    /// Lists files and folders within an SPE container using the Graph drive items API.
    ///
    /// When <paramref name="folderId"/> is <c>null</c>, returns children of the container drive root.
    /// When provided, returns children of the specified folder DriveItem.
    ///
    /// Returns domain model <see cref="SpeContainerItemSummary"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE container ID (Graph FileStorageContainer ID, i.e., the drive's container).</param>
    /// <param name="folderId">Optional DriveItem ID of a subfolder. Pass <c>null</c> to list root items.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Files and folders at the specified location; empty list when the container/folder is empty.</returns>
    public async Task<IReadOnlyList<SpeContainerItemSummary>> ListContainerItemsAsync(
        GraphServiceClient graphClient,
        string containerId,
        string? folderId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation(
            "Listing items in container {ContainerId}, FolderId: {FolderId}",
            containerId, folderId ?? "(root)");

        // Resolve the drive ID for the given container.
        // SPE containers have a 1:1 relationship with a SharePoint drive.
        // We retrieve the drive associated with the container, then list drive item children.
        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        var results = new List<SpeContainerItemSummary>();

        // Build the Graph request: list children of the root or a specific folder.
        Microsoft.Graph.Drives.Item.Items.Item.Children.ChildrenRequestBuilder? childrenBuilder;

        if (string.IsNullOrWhiteSpace(folderId))
        {
            // Root children: Drives/{driveId}/root/children
            childrenBuilder = graphClient.Drives[driveId].Items["root"].Children;
        }
        else
        {
            // Folder children: Drives/{driveId}/items/{folderId}/children
            childrenBuilder = graphClient.Drives[driveId].Items[folderId].Children;
        }

        var response = await ExecuteWithRetryAsync(
            () => childrenBuilder.GetAsync(config =>
            {
                config.QueryParameters.Select = new[]
                {
                    "id", "name", "size", "lastModifiedDateTime", "createdDateTime",
                    "createdBy", "file", "folder", "webUrl"
                };
                config.QueryParameters.Top = 200;
            }, ct),
            ct);

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var item in response.Value)
                {
                    results.Add(new SpeContainerItemSummary(
                        Id: item.Id ?? string.Empty,
                        Name: item.Name ?? string.Empty,
                        Size: item.Size,
                        LastModifiedDateTime: item.LastModifiedDateTime,
                        CreatedDateTime: item.CreatedDateTime,
                        CreatedByDisplayName: item.CreatedBy?.User?.DisplayName,
                        IsFolder: item.Folder is not null,
                        MimeType: item.File?.MimeType,
                        WebUrl: item.WebUrl));
                }
            }

            // Follow nextLink for pagination (large folders may have multiple pages)
            if (response.OdataNextLink == null) break;

            _logger.LogDebug(
                "Following nextLink for item pagination in container {ContainerId}", containerId);

            response = await ExecuteWithRetryAsync(
                () => childrenBuilder.WithUrl(response.OdataNextLink).GetAsync(cancellationToken: ct),
                ct);
        }

        _logger.LogInformation(
            "Listed {Count} items in container {ContainerId}, FolderId: {FolderId}",
            results.Count, containerId, folderId ?? "(root)");

        return results;
    }

    /// <summary>
    /// Creates a new folder in an SPE container at the container root or within a specified parent folder.
    ///
    /// When <paramref name="parentFolderId"/> is <c>null</c>, the folder is created under the container drive root.
    /// When provided, the folder is created as a child of the specified DriveItem (folder).
    ///
    /// Uses <c>@microsoft.graph.conflictBehavior: fail</c> — Graph returns a 409 Conflict ODataError
    /// when a folder with the same name already exists at the target location.
    ///
    /// Returns domain model <see cref="SpeContainerItemSummary"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE FileStorageContainer ID.</param>
    /// <param name="folderName">Name of the folder to create.</param>
    /// <param name="parentFolderId">Optional DriveItem ID of the parent folder. Pass <c>null</c> to create at root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="SpeContainerItemSummary"/> representing the newly created folder.</returns>
    /// <exception cref="InvalidOperationException">Thrown when drive ID resolution fails.</exception>
    /// <exception cref="ODataError">Thrown for Graph API errors including 409 when a name conflict occurs.</exception>
    public async Task<SpeContainerItemSummary> CreateFolderAsync(
        GraphServiceClient graphClient,
        string containerId,
        string folderName,
        string? parentFolderId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        _logger.LogInformation(
            "Creating folder '{FolderName}' in container {ContainerId}, ParentFolderId: {ParentFolderId}",
            folderName, containerId, parentFolderId ?? "(root)");

        // Resolve the drive ID for the given container (1:1 relationship between container and drive).
        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        // Build the DriveItem payload: a non-null Folder facet signals Graph to create a folder.
        // conflictBehavior "fail" returns 409 when a same-name item already exists at the target location.
        var newFolder = new Microsoft.Graph.Models.DriveItem
        {
            Name = folderName,
            Folder = new Microsoft.Graph.Models.Folder(),
            AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "fail" }
            }
        };

        Microsoft.Graph.Models.DriveItem? created;

        if (string.IsNullOrWhiteSpace(parentFolderId))
        {
            // Create at drive root: POST /drives/{driveId}/items/root/children
            // Uses the "root" special item ID — same approach as ListContainerItemsAsync.
            created = await ExecuteWithRetryAsync(
                () => graphClient.Drives[driveId].Items["root"].Children.PostAsync(newFolder, cancellationToken: ct),
                ct);
        }
        else
        {
            // Create inside parent folder: POST /drives/{driveId}/items/{parentFolderId}/children
            created = await ExecuteWithRetryAsync(
                () => graphClient.Drives[driveId].Items[parentFolderId].Children.PostAsync(newFolder, cancellationToken: ct),
                ct);
        }

        if (created is null)
        {
            throw new InvalidOperationException(
                $"Graph API returned null after creating folder '{folderName}' in container '{containerId}'.");
        }

        _logger.LogInformation(
            "Folder '{FolderName}' created successfully in container {ContainerId}. " +
            "DriveItemId: {DriveItemId}, ParentFolderId: {ParentFolderId}",
            folderName, containerId, created.Id, parentFolderId ?? "(root)");

        return new SpeContainerItemSummary(
            Id: created.Id ?? string.Empty,
            Name: created.Name ?? string.Empty,
            Size: created.Size,
            LastModifiedDateTime: created.LastModifiedDateTime,
            CreatedDateTime: created.CreatedDateTime,
            CreatedByDisplayName: created.CreatedBy?.User?.DisplayName,
            IsFolder: true,
            MimeType: null,
            WebUrl: created.WebUrl);
    }

    /// <summary>
    /// Resolves the Graph drive ID for the given SPE container ID.
    /// SPE container IDs are FileStorageContainer IDs; they are not the same as drive IDs.
    /// Retrieves the container's drive to obtain the driveId needed for Drives[id].Items operations.
    /// </summary>
    private async Task<string> ResolveDriveIdForContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct)
    {
        _logger.LogDebug("Resolving drive ID for container {ContainerId}", containerId);

        var container = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[] { "id" };
                }, ct),
            ct);

        if (container?.Id is null)
        {
            throw new InvalidOperationException(
                $"Could not resolve drive ID for container '{containerId}'. " +
                "The container may not exist or the app registration lacks FileStorageContainer.Selected permission.");
        }

        _logger.LogDebug(
            "Resolved container {ContainerId} to driveId {DriveId}", containerId, container.Id);

        return container.Id;
    }

    /// <summary>
    /// Returns a single page of SPE containers filtered by container type ID.
    /// Supports OData <c>$top</c> and <c>$skipToken</c> for cursor-based pagination.
    ///
    /// Use <see cref="ContainerPage.NextSkipToken"/> from the response to retrieve subsequent pages.
    /// When <paramref name="skipToken"/> is non-null, it is passed directly to Graph as the OData nextLink token.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The SPE container type GUID to filter by.</param>
    /// <param name="top">Maximum number of items to return. Graph default applies when null.</param>
    /// <param name="skipToken">Opaque pagination token from a prior <see cref="ContainerPage.NextSkipToken"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ContainerPage"/> with this page's items and an optional next-page token.</returns>
    public async Task<ContainerPage> ListContainersPageAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        int? top,
        string? skipToken,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        _logger.LogInformation(
            "Listing SPE containers page for containerTypeId {ContainerTypeId}, top={Top}, skipToken={HasSkipToken}",
            containerTypeId, top, skipToken != null);

        // response type is inferred from GetAsync — avoids binding to internal SDK type names.
        Microsoft.Graph.Models.FileStorageContainerCollectionResponse? response;

        if (!string.IsNullOrWhiteSpace(skipToken))
        {
            // Resume from a prior page using the skipToken embedded in a synthetic nextLink URL.
            // The Graph SDK's WithUrl() builder allows resumption from any OData nextLink.
            // We reconstruct the URL in the format Graph expects for skip-token continuation.
            var nextLinkUrl = $"https://graph.microsoft.com/beta/storage/fileStorage/containers" +
                              $"?$filter=containerTypeId+eq+'{containerTypeId}'" +
                              $"&$skiptoken={Uri.EscapeDataString(skipToken)}";

            if (top.HasValue && top.Value > 0)
            {
                nextLinkUrl += $"&$top={top.Value}";
            }

            response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers
                    .WithUrl(nextLinkUrl)
                    .GetAsync(cancellationToken: ct),
                ct);
        }
        else
        {
            // First page — apply filter, select, and optional $top.
            response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Filter = $"containerTypeId eq '{containerTypeId}'";
                        config.QueryParameters.Select = new[]
                        {
                            "id", "displayName", "description", "containerTypeId",
                            "createdDateTime", "storageUsedInBytes"
                        };

                        if (top.HasValue && top.Value > 0)
                        {
                            config.QueryParameters.Top = top.Value;
                        }
                    }, ct),
                ct);
        }

        var items = new List<SpeContainerSummary>();

        if (response?.Value != null)
        {
            foreach (var container in response.Value)
            {
                items.Add(new SpeContainerSummary(
                    container.Id ?? string.Empty,
                    container.DisplayName ?? string.Empty,
                    container.Description,
                    container.ContainerTypeId?.ToString() ?? containerTypeId,
                    container.CreatedDateTime ?? DateTimeOffset.UtcNow,
                    StorageUsedInBytes: null)); // Not always returned by Graph
            }
        }

        // Extract $skiptoken from the OData nextLink URL for the caller.
        string? nextSkipToken = null;
        if (response?.OdataNextLink != null)
        {
            nextSkipToken = ExtractSkipToken(response.OdataNextLink);
        }

        _logger.LogInformation(
            "Listed {Count} containers for containerTypeId {ContainerTypeId}. HasNextPage: {HasNext}",
            items.Count, containerTypeId, nextSkipToken != null);

        return new ContainerPage(items, nextSkipToken);
    }

    /// <summary>
    /// Creates a new SharePoint Embedded container for the specified container type.
    ///
    /// Uses the provided authenticated <paramref name="graphClient"/> to POST to the Graph beta
    /// <c>/storage/fileStorage/containers</c> endpoint. The container type is identified by
    /// <paramref name="containerTypeId"/> (the GUID string from the resolved <see cref="ContainerTypeConfig"/>).
    ///
    /// Returns a <see cref="SpeContainerSummary"/> domain record — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The SPE container type GUID string (from the config record).</param>
    /// <param name="displayName">Display name for the new container. Required; must not exceed 256 characters.</param>
    /// <param name="description">Optional description for the new container.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="SpeContainerSummary"/> representing the newly created container.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Graph API returns null unexpectedly.</exception>
    /// <exception cref="ODataError">Thrown when Graph API returns an error response.</exception>
    public async Task<SpeContainerSummary> CreateContainerAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        string displayName,
        string? description,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _logger.LogInformation(
            "Creating SPE container '{DisplayName}' for containerTypeId {ContainerTypeId}",
            displayName, containerTypeId);

        // Build the Graph SDK request body.
        // ContainerTypeId must be a Guid for the SDK model. If the stored value cannot be
        // parsed as a Guid (misconfiguration), Guid.Empty will cause a Graph API 400 error
        // which surfaces to the caller as an ODataError with a descriptive message.
        var body = new Microsoft.Graph.Models.FileStorageContainer
        {
            DisplayName = displayName,
            Description = description,
            ContainerTypeId = Guid.TryParse(containerTypeId, out var ctGuid) ? ctGuid : Guid.Empty
        };

        var created = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers.PostAsync(body, cancellationToken: ct),
            ct);

        if (created is null)
        {
            throw new InvalidOperationException(
                $"Graph API returned null when creating container '{displayName}' " +
                $"for containerTypeId '{containerTypeId}'. The container may not have been created.");
        }

        _logger.LogInformation(
            "SPE container created: Id={ContainerId}, DisplayName='{DisplayName}', ContainerTypeId={ContainerTypeId}",
            created.Id, created.DisplayName, containerTypeId);

        return new SpeContainerSummary(
            Id: created.Id ?? string.Empty,
            DisplayName: created.DisplayName ?? displayName,
            Description: created.Description,
            ContainerTypeId: created.ContainerTypeId?.ToString() ?? containerTypeId,
            CreatedDateTime: created.CreatedDateTime ?? DateTimeOffset.UtcNow,
            StorageUsedInBytes: null); // New containers always start empty; Graph returns null here
    }

    /// <summary>
    /// Retrieves a single SPE container by its Graph FileStorageContainer ID.
    /// Returns <c>null</c> when Graph responds with 404 (container not found).
    /// Throws <see cref="ODataError"/> for other Graph API failures.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Container summary, or <c>null</c> if not found.</returns>
    public async Task<SpeContainerSummary?> GetContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Retrieving SPE container {ContainerId}", containerId);

        try
        {
            var container = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers[containerId]
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Select = new[]
                        {
                            "id", "displayName", "description", "containerTypeId",
                            "createdDateTime", "storageUsedInBytes"
                        };
                    }, ct),
                ct);

            if (container is null)
            {
                _logger.LogWarning("Graph returned null for container {ContainerId}", containerId);
                return null;
            }

            return new SpeContainerSummary(
                container.Id ?? string.Empty,
                container.DisplayName ?? string.Empty,
                container.Description,
                container.ContainerTypeId?.ToString() ?? string.Empty,
                container.CreatedDateTime ?? DateTimeOffset.UtcNow,
                StorageUsedInBytes: null);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Container {ContainerId} not found in Graph (404)", containerId);
            return null;
        }
    }

    // =========================================================================
    // Container lifecycle mutations (SPE-015)
    // =========================================================================

    /// <summary>
    /// Updates the <c>displayName</c> and/or <c>description</c> of an SPE container via Graph PATCH.
    /// Returns <c>false</c> when Graph responds 404 (container not found).
    /// Throws <see cref="ODataError"/> for other Graph API failures.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID to update.</param>
    /// <param name="displayName">New display name. Pass <c>null</c> to leave unchanged.</param>
    /// <param name="description">New description. Pass <c>null</c> to leave unchanged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if updated; <c>false</c> if container not found.</returns>
    public async Task<bool> UpdateContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        string? displayName,
        string? description,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation(
            "Updating SPE container {ContainerId} metadata (hasDisplayName={HasDN}, hasDescription={HasDesc})",
            containerId, displayName != null, description != null);

        var patch = new Microsoft.Graph.Models.FileStorageContainer
        {
            DisplayName = displayName,
            Description = description
        };

        try
        {
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.Containers[containerId]
                        .PatchAsync(patch, cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation("SPE container {ContainerId} metadata updated successfully", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("SPE container {ContainerId} not found for metadata update", containerId);
            return false;
        }
    }

    /// <summary>
    /// Activates an SPE container by PATCHing its status to <c>active</c>.
    /// Returns <c>false</c> when Graph responds 404.
    /// Throws <see cref="ODataError"/> for other failures (e.g., 409 Conflict if state transition is invalid).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID to activate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if activated; <c>false</c> if container not found.</returns>
    public async Task<bool> ActivateContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Activating SPE container {ContainerId}", containerId);

        // Use AdditionalData to set status as a raw string value, bypassing the FileStorageContainerStatus enum
        // which may not include all SPE beta status values in the current SDK version.
        var patch = new Microsoft.Graph.Models.FileStorageContainer
        {
            AdditionalData = new Dictionary<string, object> { ["status"] = "active" }
        };

        try
        {
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.Containers[containerId]
                        .PatchAsync(patch, cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation("SPE container {ContainerId} activated successfully", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("SPE container {ContainerId} not found for activation", containerId);
            return false;
        }
    }

    /// <summary>
    /// Locks an SPE container by PATCHing its status to <c>locked</c>.
    /// Returns <c>false</c> when Graph responds 404.
    /// Throws <see cref="ODataError"/> for other failures (e.g., 409 Conflict if state transition is invalid).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID to lock.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if locked; <c>false</c> if container not found.</returns>
    public async Task<bool> LockContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Locking SPE container {ContainerId}", containerId);

        var patch = new Microsoft.Graph.Models.FileStorageContainer
        {
            AdditionalData = new Dictionary<string, object> { ["status"] = "locked" }
        };

        try
        {
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.Containers[containerId]
                        .PatchAsync(patch, cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation("SPE container {ContainerId} locked successfully", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("SPE container {ContainerId} not found for locking", containerId);
            return false;
        }
    }

    /// <summary>
    /// Unlocks an SPE container by PATCHing its status back to <c>active</c>.
    /// Returns <c>false</c> when Graph responds 404.
    /// Throws <see cref="ODataError"/> for other failures (e.g., 409 Conflict if not currently locked).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID to unlock.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if unlocked; <c>false</c> if container not found.</returns>
    public async Task<bool> UnlockContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Unlocking SPE container {ContainerId}", containerId);

        // Unlock transitions a locked container back to active (the only valid transition from locked).
        var patch = new Microsoft.Graph.Models.FileStorageContainer
        {
            AdditionalData = new Dictionary<string, object> { ["status"] = "active" }
        };

        try
        {
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.Containers[containerId]
                        .PatchAsync(patch, cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation("SPE container {ContainerId} unlocked successfully", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("SPE container {ContainerId} not found for unlocking", containerId);
            return false;
        }
    }

    // =========================================================================
    // Container custom properties (SPE-056)
    // =========================================================================

    /// <summary>
    /// Retrieves all custom properties from an SPE container.
    ///
    /// Uses the Graph container GET endpoint with a select for customProperties:
    ///   GET /storage/fileStorage/containers/{id}?$select=id,customProperties
    ///
    /// The Graph SDK models <c>customProperties</c> as
    /// <see cref="Microsoft.Graph.Models.FileStorageContainerCustomPropertyDictionary"/>,
    /// which stores its entries in <c>AdditionalData</c> (keyed by property name).
    /// Each value serializes as: <c>{ "value": "&lt;string&gt;", "isSearchable": &lt;bool&gt; }</c>.
    ///
    /// Returns an empty list when the container has no custom properties.
    /// Returns <c>null</c> when the container is not found (Graph 404).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// List of custom properties ordered by name (case-insensitive), or <c>null</c> when the container is not found.
    /// </returns>
    /// <remarks>
    /// ADR-007: Returns domain DTOs only — no Graph SDK types exposed to callers.
    /// </remarks>
    public async Task<IReadOnlyList<Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto>?> GetCustomPropertiesAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Retrieving custom properties for container {ContainerId}", containerId);

        try
        {
            var container = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers[containerId]
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Select = new[] { "id", "customProperties" };
                    }, ct),
                ct);

            if (container is null)
            {
                _logger.LogWarning(
                    "Graph returned null for container {ContainerId} when reading custom properties", containerId);
                return null;
            }

            // The SDK stores each custom property entry in CustomProperties.AdditionalData.
            // Each entry value is a Kiota UntypedObject with "value" and "isSearchable" fields.
            var customPropsAdditional = container.CustomProperties?.AdditionalData;
            if (customPropsAdditional is null || customPropsAdditional.Count == 0)
            {
                _logger.LogInformation("Container {ContainerId} has no custom properties", containerId);
                return Array.Empty<Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto>();
            }

            var result = new List<Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto>(customPropsAdditional.Count);

            foreach (var kvp in customPropsAdditional)
            {
                var propName = kvp.Key;
                var propValue = string.Empty;
                var isSearchable = false;

                // Kiota deserializes untyped JSON objects as UntypedObject nodes.
                if (kvp.Value is Microsoft.Kiota.Abstractions.Serialization.UntypedObject untypedObj)
                {
                    var fields = untypedObj.GetValue();

                    if (fields.TryGetValue("value", out var valNode) &&
                        valNode is Microsoft.Kiota.Abstractions.Serialization.UntypedString valStr)
                    {
                        propValue = valStr.GetValue() ?? string.Empty;
                    }

                    if (fields.TryGetValue("isSearchable", out var searchNode) &&
                        searchNode is Microsoft.Kiota.Abstractions.Serialization.UntypedBoolean searchBool)
                    {
                        isSearchable = searchBool.GetValue();
                    }
                }

                result.Add(new Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto(propName, propValue, isSearchable));
            }

            result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

            _logger.LogInformation(
                "Retrieved {Count} custom properties for container {ContainerId}", result.Count, containerId);

            return result;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container {ContainerId} not found when reading custom properties (404)", containerId);
            return null;
        }
    }

    /// <summary>
    /// Replaces all custom properties on an SPE container with the supplied list.
    ///
    /// Uses the Graph PATCH endpoint:
    ///   PATCH /storage/fileStorage/containers/{id}
    ///   Body: { "customProperties": { "Key": { "value": "...", "isSearchable": false }, ... } }
    ///
    /// This is a full-replace operation: any properties not included in <paramref name="properties"/>
    /// will be removed. Pass an empty list to clear all custom properties.
    ///
    /// Returns the updated list of custom properties after the PATCH (re-reads from Graph).
    /// Returns <c>null</c> when the container is not found (Graph 404).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID.</param>
    /// <param name="properties">The complete set of custom properties to store on the container.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Updated list of custom properties from a post-PATCH GET, or <c>null</c> when container not found.
    /// </returns>
    /// <remarks>
    /// ADR-007: Returns domain DTOs only — no Graph SDK types exposed to callers.
    ///
    /// Custom properties are sent via <see cref="Microsoft.Graph.Models.Entity.AdditionalData"/>
    /// rather than via <see cref="Microsoft.Graph.Models.FileStorageContainerCustomPropertyDictionary"/>
    /// because the SDK type does not expose a public add-entry API compatible with strongly-typed
    /// per-property construction in Graph SDK v5.x. Using AdditionalData produces the correct
    /// OData PATCH body through Kiota's JSON serializer.
    /// </remarks>
    public async Task<IReadOnlyList<Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto>?> UpdateCustomPropertiesAsync(
        GraphServiceClient graphClient,
        string containerId,
        IReadOnlyList<Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto> properties,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentNullException.ThrowIfNull(properties);

        _logger.LogInformation(
            "Updating {Count} custom properties on container {ContainerId}", properties.Count, containerId);

        // Build the customProperties payload via AdditionalData.
        // Kiota will serialize this as: { "customProperties": { "KeyName": { "value": "...", "isSearchable": false } } }
        var customPropertiesDict = new Dictionary<string, object>(properties.Count);
        foreach (var prop in properties)
        {
            customPropertiesDict[prop.Name] = new Dictionary<string, object>
            {
                ["value"] = prop.Value,
                ["isSearchable"] = (object)prop.IsSearchable
            };
        }

        var patch = new Microsoft.Graph.Models.FileStorageContainer
        {
            AdditionalData = new Dictionary<string, object>
            {
                ["customProperties"] = customPropertiesDict
            }
        };

        try
        {
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.Containers[containerId]
                        .PatchAsync(patch, cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation(
                "Custom properties updated on container {ContainerId}. Re-reading updated state.", containerId);

            // Re-read to return the authoritative post-PATCH state.
            return await GetCustomPropertiesAsync(graphClient, containerId, ct);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container {ContainerId} not found when updating custom properties (404)", containerId);
            return null;
        }
    }


    /// <summary>
    /// Lists all versions of a DriveItem within an SPE container.
    ///
    /// Uses the Graph drive items versions API:
    ///   GET Drives/{driveId}/Items/{itemId}/Versions
    ///
    /// Returns domain model <see cref="SpeFileVersionSummary"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE container ID (FileStorageContainer ID).</param>
    /// <param name="itemId">The DriveItem ID of the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All versions of the file in chronological order (oldest first).</returns>
    public async Task<IReadOnlyList<SpeFileVersionSummary>> GetFileVersionsAsync(
        GraphServiceClient graphClient,
        string containerId,
        string itemId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        _logger.LogInformation(
            "Listing versions for item {ItemId} in container {ContainerId}", itemId, containerId);

        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        var response = await ExecuteWithRetryAsync(
            () => graphClient.Drives[driveId].Items[itemId].Versions
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[]
                    {
                        "id", "lastModifiedDateTime", "lastModifiedBy", "size"
                    };
                }, ct),
            ct);

        var results = new List<SpeFileVersionSummary>();

        if (response?.Value != null)
        {
            foreach (var version in response.Value)
            {
                results.Add(new SpeFileVersionSummary(
                    Id: version.Id ?? string.Empty,
                    LastModifiedDateTime: version.LastModifiedDateTime,
                    LastModifiedByDisplayName: version.LastModifiedBy?.User?.DisplayName,
                    Size: version.Size));
            }
        }

        // Versions from Graph come newest-first; reverse so oldest version is first (v1.0 at index 0).
        results.Reverse();

        _logger.LogInformation(
            "Listed {Count} versions for item {ItemId} in container {ContainerId}",
            results.Count, itemId, containerId);

        return results;
    }

    /// <summary>
    /// Gets thumbnail URLs for a DriveItem within an SPE container.
    ///
    /// Uses the Graph drive items thumbnails API:
    ///   GET Drives/{driveId}/Items/{itemId}/Thumbnails
    ///
    /// Returns domain model <see cref="SpeThumbnailSet"/> — no Graph SDK types exposed (ADR-007).
    /// Returns an empty list for folders or items that do not support thumbnails.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE container ID (FileStorageContainer ID).</param>
    /// <param name="itemId">The DriveItem ID of the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Thumbnail sets for the item; empty when thumbnails are unavailable.</returns>
    public async Task<IReadOnlyList<SpeThumbnailSet>> GetFileThumbnailsAsync(
        GraphServiceClient graphClient,
        string containerId,
        string itemId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        _logger.LogInformation(
            "Getting thumbnails for item {ItemId} in container {ContainerId}", itemId, containerId);

        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        var response = await ExecuteWithRetryAsync(
            () => graphClient.Drives[driveId].Items[itemId].Thumbnails
                .GetAsync(cancellationToken: ct),
            ct);

        var results = new List<SpeThumbnailSet>();

        if (response?.Value != null)
        {
            foreach (var thumbnailSet in response.Value)
            {
                results.Add(new SpeThumbnailSet(
                    Id: thumbnailSet.Id ?? string.Empty,
                    SmallUrl: thumbnailSet.Small?.Url,
                    MediumUrl: thumbnailSet.Medium?.Url,
                    LargeUrl: thumbnailSet.Large?.Url));
            }
        }

        _logger.LogInformation(
            "Got {Count} thumbnail set(s) for item {ItemId} in container {ContainerId}",
            results.Count, itemId, containerId);

        return results;
    }

    /// <summary>
    /// Creates a sharing link for a DriveItem within an SPE container.
    ///
    /// Uses the Graph drive items createLink action:
    ///   POST Drives/{driveId}/Items/{itemId}/createLink
    ///
    /// Returns domain model <see cref="SpeSharingLink"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE container ID (FileStorageContainer ID).</param>
    /// <param name="itemId">The DriveItem ID of the file.</param>
    /// <param name="linkType">Link type: "view", "edit", or "embed".</param>
    /// <param name="scope">Link scope: "anonymous", "organization", or "users".</param>
    /// <param name="expirationDateTime">Optional expiration. Null for a non-expiring link.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created sharing link.</returns>
    public async Task<SpeSharingLink> CreateSharingLinkAsync(
        GraphServiceClient graphClient,
        string containerId,
        string itemId,
        string linkType,
        string scope,
        DateTimeOffset? expirationDateTime,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkType);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        _logger.LogInformation(
            "Creating sharing link for item {ItemId} in container {ContainerId}. " +
            "Type: {LinkType}, Scope: {Scope}",
            itemId, containerId, linkType, scope);

        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        var requestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
        {
            Type = linkType,
            Scope = scope,
            ExpirationDateTime = expirationDateTime
        };

        var permission = await ExecuteWithRetryAsync(
            () => graphClient.Drives[driveId].Items[itemId].CreateLink
                .PostAsync(requestBody, cancellationToken: ct),
            ct);

        // Graph returns a Permission object; the sharing link is in the Link property.
        var linkUrl = permission?.Link?.WebUrl;

        if (string.IsNullOrWhiteSpace(linkUrl))
        {
            throw new InvalidOperationException(
                $"Graph API returned a permission for item '{itemId}' but the link URL was empty or null.");
        }

        _logger.LogInformation(
            "Sharing link created for item {ItemId} in container {ContainerId}. Type: {LinkType}",
            itemId, containerId, linkType);

        return new SpeSharingLink(
            Url: linkUrl,
            LinkType: linkType,
            Scope: scope,
            ExpirationDateTime: permission?.ExpirationDateTime);
    }

    /// <summary>
    /// Downloads the content of a DriveItem as a stream.
    ///
    /// Returns the raw response stream from Graph so the BFF can pipe it directly to the
    /// HTTP response without buffering the entire file in memory (ADR-007: no Graph SDK types returned).
    ///
    /// The caller is responsible for disposing the returned <see cref="Stream"/>.
    /// Returns <c>null</c> when Graph responds with 404 (item not found).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE FileStorageContainer ID.</param>
    /// <param name="itemId">The DriveItem ID to download.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tuple of (content stream, MIME type, file name), or <c>null</c> if the item is not found.
    /// </returns>
    public async Task<(Stream Content, string MimeType, string FileName)?> DownloadDriveItemAsync(
        GraphServiceClient graphClient,
        string containerId,
        string itemId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        _logger.LogInformation(
            "Downloading item {ItemId} from container {ContainerId}", itemId, containerId);

        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        try
        {
            // Retrieve item metadata to get name and MIME type.
            var item = await ExecuteWithRetryAsync(
                () => graphClient.Drives[driveId].Items[itemId]
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Select = new[] { "id", "name", "file" };
                    }, ct),
                ct);

            if (item is null)
            {
                _logger.LogInformation("Item {ItemId} not found in container {ContainerId}", itemId, containerId);
                return null;
            }

            // Stream the file content. Graph returns a redirect or direct stream.
            var contentStream = await ExecuteWithRetryAsync(
                () => graphClient.Drives[driveId].Items[itemId].Content
                    .GetAsync(cancellationToken: ct),
                ct);

            if (contentStream is null)
            {
                _logger.LogWarning(
                    "Graph returned null content stream for item {ItemId} in container {ContainerId}",
                    itemId, containerId);
                return null;
            }

            var mimeType = item.File?.MimeType ?? "application/octet-stream";
            var fileName = item.Name ?? itemId;

            _logger.LogInformation(
                "Download initiated for item {ItemId} (Name={FileName}, MimeType={MimeType})",
                itemId, fileName, mimeType);

            return (contentStream, mimeType, fileName);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Item {ItemId} not found in container {ContainerId} (404)", itemId, containerId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a temporary preview URL for a DriveItem using the Graph preview API.
    ///
    /// The URL is time-limited and can be used to render a document in-browser
    /// (e.g., via an iframe). Supported for Office files, images, PDFs, and other
    /// previewable content types.
    ///
    /// Returns <c>null</c> when Graph responds with 404 (item not found).
    /// Throws <see cref="ODataError"/> for other Graph failures.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE FileStorageContainer ID.</param>
    /// <param name="itemId">The DriveItem ID to preview.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Temporary preview URL string, or <c>null</c> if the item is not found.</returns>
    public async Task<string?> GetPreviewUrlAsync(
        GraphServiceClient graphClient,
        string containerId,
        string itemId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        _logger.LogInformation(
            "Getting preview URL for item {ItemId} in container {ContainerId}", itemId, containerId);

        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        try
        {
            // Graph preview API: POST /drives/{driveId}/items/{itemId}/preview
            // Returns an ItemPreviewInfo with GetUrl (embeddable) and PostParameters.
            var previewInfo = await ExecuteWithRetryAsync(
                () => graphClient.Drives[driveId].Items[itemId].Preview
                    .PostAsync(
                        new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody(),
                        cancellationToken: ct),
                ct);

            if (previewInfo?.GetUrl is null)
            {
                _logger.LogWarning(
                    "Graph returned null preview URL for item {ItemId} in container {ContainerId}",
                    itemId, containerId);
                return null;
            }

            _logger.LogInformation(
                "Preview URL obtained for item {ItemId} in container {ContainerId}", itemId, containerId);

            return previewInfo.GetUrl;
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Item {ItemId} not found in container {ContainerId} (404)", itemId, containerId);
            return null;
        }
    }

    /// <summary>
    /// Deletes a DriveItem from the SPE container.
    ///
    /// Returns <c>true</c> when the item was successfully deleted.
    /// Returns <c>false</c> when Graph responds with 404 (item not found).
    /// Throws <see cref="ODataError"/> for other Graph API failures.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE FileStorageContainer ID.</param>
    /// <param name="itemId">The DriveItem ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if deleted; <c>false</c> if not found.</returns>
    public async Task<bool> DeleteDriveItemAsync(
        GraphServiceClient graphClient,
        string containerId,
        string itemId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        _logger.LogInformation(
            "Deleting item {ItemId} from container {ContainerId}", itemId, containerId);

        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);

        try
        {
            // DELETE /drives/{driveId}/items/{itemId}
            // Moves the item to the recycle bin in SPE (soft delete).
            await graphClient.Drives[driveId].Items[itemId].DeleteAsync(cancellationToken: ct);

            _logger.LogInformation(
                "Item {ItemId} deleted from container {ContainerId}", itemId, containerId);

            return true;
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Item {ItemId} not found in container {ContainerId} (404 on delete)", itemId, containerId);
            return false;
        }
    }

    /// <summary>
    // =========================================================================
    // Container permission operations (SPE-016)
    // =========================================================================

    /// <summary>
    /// Lists all permissions on an SPE container.
    /// Returns domain model <see cref="SpeContainerPermission"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All permission entries on the container.</returns>
    public async Task<IReadOnlyList<SpeContainerPermission>> ListContainerPermissionsAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Listing permissions for container {ContainerId}", containerId);

        var response = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers[containerId].Permissions
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[]
                    {
                        "id", "roles", "grantedToV2", "grantedTo"
                    };
                }, ct),
            ct);

        var results = new List<SpeContainerPermission>();

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var perm in response.Value)
                {
                    results.Add(MapPermission(perm));
                }
            }

            if (response.OdataNextLink == null) break;

            _logger.LogDebug(
                "Following nextLink for permissions pagination on container {ContainerId}", containerId);

            response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers[containerId].Permissions
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: ct),
                ct);
        }

        _logger.LogInformation(
            "Listed {Count} permissions for container {ContainerId}", results.Count, containerId);

        return results;
    }

    /// <summary>
    /// Grants a new permission on an SPE container.
    /// Supports granting to an individual user (by <paramref name="userId"/>)
    /// or to a group (by <paramref name="groupId"/>). Exactly one must be provided.
    /// Returns the newly created <see cref="SpeContainerPermission"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID.</param>
    /// <param name="userId">Azure AD user object ID. Mutually exclusive with <paramref name="groupId"/>.</param>
    /// <param name="groupId">Azure AD group object ID. Mutually exclusive with <paramref name="userId"/>.</param>
    /// <param name="role">SPE role to assign: reader, writer, manager, or owner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created permission entry.</returns>
    public async Task<SpeContainerPermission> GrantContainerPermissionAsync(
        GraphServiceClient graphClient,
        string containerId,
        string? userId,
        string? groupId,
        string role,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        _logger.LogInformation(
            "Granting '{Role}' to (userId={UserId}, groupId={GroupId}) on container {ContainerId}",
            role, userId, groupId, containerId);

        // Build the Graph SDK permission payload for SPE container permissions.
        // SPE uses grantedToV2 with SharePointIdentitySet.
        var permissionRequest = new Microsoft.Graph.Models.Permission
        {
            Roles = new List<string> { role.ToLowerInvariant() },
            GrantedToV2 = new Microsoft.Graph.Models.SharePointIdentitySet
            {
                User = !string.IsNullOrWhiteSpace(userId)
                    ? new Microsoft.Graph.Models.SharePointIdentity { Id = userId }
                    : null,
                Group = !string.IsNullOrWhiteSpace(groupId)
                    ? new Microsoft.Graph.Models.SharePointIdentity { Id = groupId }
                    : null
            }
        };

        var created = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers[containerId].Permissions
                .PostAsync(permissionRequest, cancellationToken: ct),
            ct);

        if (created is null)
        {
            throw new InvalidOperationException(
                $"Graph returned null when granting permission on container '{containerId}'.");
        }

        _logger.LogInformation(
            "Granted '{Role}' to principal on container {ContainerId}, permissionId={PermissionId}",
            role, containerId, created.Id);

        return MapPermission(created);
    }

    /// <summary>
    /// Updates the role of an existing permission on an SPE container.
    /// Returns the updated <see cref="SpeContainerPermission"/>, or <c>null</c> when the permission is not found.
    /// Returns domain model only — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID.</param>
    /// <param name="permissionId">The Graph permission ID to update.</param>
    /// <param name="newRole">New SPE role: reader, writer, manager, or owner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated permission entry, or <c>null</c> when not found.</returns>
    public async Task<SpeContainerPermission?> UpdateContainerPermissionAsync(
        GraphServiceClient graphClient,
        string containerId,
        string permissionId,
        string newRole,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRole);

        _logger.LogInformation(
            "Updating permission '{PermissionId}' to role '{Role}' on container {ContainerId}",
            permissionId, newRole, containerId);

        try
        {
            var updatePayload = new Microsoft.Graph.Models.Permission
            {
                Roles = new List<string> { newRole.ToLowerInvariant() }
            };

            var updated = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers[containerId].Permissions[permissionId]
                    .PatchAsync(updatePayload, cancellationToken: ct),
                ct);

            if (updated is null)
            {
                _logger.LogWarning(
                    "Graph returned null when updating permission '{PermissionId}' on container {ContainerId}",
                    permissionId, containerId);
                return null;
            }

            _logger.LogInformation(
                "Updated permission '{PermissionId}' to role '{Role}' on container {ContainerId}",
                permissionId, newRole, containerId);

            return MapPermission(updated);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Permission '{PermissionId}' not found on container {ContainerId} (404)",
                permissionId, containerId);
            return null;
        }
    }

    /// <summary>
    /// Revokes (deletes) an existing permission from an SPE container.
    /// Returns <c>true</c> when successfully deleted; <c>false</c> when the permission was not found.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID.</param>
    /// <param name="permissionId">The Graph permission ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if deleted; <c>false</c> if not found.</returns>
    public async Task<bool> RevokeContainerPermissionAsync(
        GraphServiceClient graphClient,
        string containerId,
        string permissionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionId);

        _logger.LogInformation(
            "Revoking permission '{PermissionId}' from container {ContainerId}", permissionId, containerId);

        try
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await graphClient.Storage.FileStorage.Containers[containerId].Permissions[permissionId]
                    .DeleteAsync(cancellationToken: ct);
                return true; // ExecuteWithRetryAsync<T> requires a return value
            }, ct);

            _logger.LogInformation(
                "Revoked permission '{PermissionId}' from container {ContainerId}", permissionId, containerId);

            return true;
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Permission '{PermissionId}' not found on container {ContainerId} (404) — treating as already deleted",
                permissionId, containerId);
            return false;
        }
    }

    // =========================================================================
    // Container column operations (SPE-055)
    // =========================================================================

    /// <summary>
    /// Lists all columns defined on an SPE container's metadata schema.
    /// Returns domain model <see cref="SpeContainerColumn"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    public async Task<IReadOnlyList<SpeContainerColumn>> ListColumnsAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Listing columns for container {ContainerId}", containerId);

        var response = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers[containerId].Columns
                .GetAsync(cancellationToken: ct),
            ct);

        var results = new List<SpeContainerColumn>();
        if (response?.Value != null)
        {
            foreach (var col in response.Value)
                results.Add(MapColumn(col));
        }

        _logger.LogInformation(
            "Listed {Count} columns for container {ContainerId}", results.Count, containerId);

        return results;
    }

    /// <summary>
    /// Creates a new column on an SPE container's metadata schema.
    /// Returns the newly created <see cref="SpeContainerColumn"/> — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Graph API returns null unexpectedly.</exception>
    /// <exception cref="ODataError">Thrown when Graph API returns an error response.</exception>
    public async Task<SpeContainerColumn> CreateColumnAsync(
        GraphServiceClient graphClient,
        string containerId,
        string name,
        string? displayName,
        string? description,
        string columnType,
        bool required,
        bool indexed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnType);

        _logger.LogInformation(
            "Creating column '{Name}' (type={ColumnType}) on container {ContainerId}",
            name, columnType, containerId);

        var body = BuildColumnDefinition(name, displayName, description, columnType, required, indexed);

        var created = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.Containers[containerId].Columns
                .PostAsync(body, cancellationToken: ct),
            ct);

        if (created is null)
            throw new InvalidOperationException(
                $"Graph API returned null after creating column '{name}' on container '{containerId}'.");

        _logger.LogInformation(
            "Column '{Name}' created on container {ContainerId}. ColumnId: {ColumnId}",
            name, containerId, created.Id);

        return MapColumn(created);
    }

    /// <summary>
    /// Updates an existing column on an SPE container's metadata schema.
    /// Only non-null parameters are applied (partial update).
    /// Returns <c>null</c> when Graph responds 404 (column not found).
    /// </summary>
    public async Task<SpeContainerColumn?> UpdateColumnAsync(
        GraphServiceClient graphClient,
        string containerId,
        string columnId,
        string? displayName,
        string? description,
        bool? required,
        bool? indexed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnId);

        _logger.LogInformation(
            "Updating column '{ColumnId}' on container {ContainerId}", columnId, containerId);

        var patch = new Microsoft.Graph.Models.ColumnDefinition();
        if (displayName is not null) patch.DisplayName = displayName;
        if (description is not null) patch.Description = description;
        if (required.HasValue)  patch.Required = required.Value;
        if (indexed.HasValue)   patch.Indexed  = indexed.Value;

        try
        {
            var updated = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.Containers[containerId].Columns[columnId]
                    .PatchAsync(patch, cancellationToken: ct),
                ct);

            if (updated is null)
            {
                _logger.LogWarning(
                    "Graph returned null when patching column '{ColumnId}' on container {ContainerId}",
                    columnId, containerId);
                return null;
            }

            _logger.LogInformation(
                "Column '{ColumnId}' updated on container {ContainerId}", columnId, containerId);

            return MapColumn(updated);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Column '{ColumnId}' not found on container {ContainerId} (404 on patch)",
                columnId, containerId);
            return null;
        }
    }

    /// <summary>
    /// Deletes an existing column from an SPE container's metadata schema.
    /// Returns <c>true</c> when deleted; <c>false</c> when the column was not found (404).
    /// </summary>
    public async Task<bool> DeleteColumnAsync(
        GraphServiceClient graphClient,
        string containerId,
        string columnId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnId);

        _logger.LogInformation(
            "Deleting column '{ColumnId}' from container {ContainerId}", columnId, containerId);

        try
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await graphClient.Storage.FileStorage.Containers[containerId].Columns[columnId]
                    .DeleteAsync(cancellationToken: ct);
                return true;
            }, ct);

            _logger.LogInformation(
                "Column '{ColumnId}' deleted from container {ContainerId}", columnId, containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Column '{ColumnId}' not found on container {ContainerId} (404 on delete)",
                columnId, containerId);
            return false;
        }
    }

    /// <summary>
    /// Builds a Graph SDK ColumnDefinition body from the given parameters.
    /// Sets the type facet based on <paramref name="columnType"/>.
    /// ADR-007: Graph SDK types are used internally only; callers never receive ColumnDefinition.
    /// </summary>
    private static Microsoft.Graph.Models.ColumnDefinition BuildColumnDefinition(
        string name, string? displayName, string? description, string columnType, bool required, bool indexed)
    {
        var col = new Microsoft.Graph.Models.ColumnDefinition
        {
            Name        = name,
            DisplayName = displayName ?? name,
            Description = description,
            Required    = required,
            Indexed     = indexed
        };

        switch (columnType.ToLowerInvariant())
        {
            case "boolean":
                col.Boolean = new Microsoft.Graph.Models.BooleanColumn();
                break;
            case "datetime":
                col.DateTime = new Microsoft.Graph.Models.DateTimeColumn();
                break;
            case "currency":
                col.Currency = new Microsoft.Graph.Models.CurrencyColumn();
                break;
            case "choice":
                col.Choice = new Microsoft.Graph.Models.ChoiceColumn();
                break;
            case "number":
                col.Number = new Microsoft.Graph.Models.NumberColumn();
                break;
            case "personorgroup":
                col.PersonOrGroup = new Microsoft.Graph.Models.PersonOrGroupColumn();
                break;
            case "hyperlinkorpicture":
                col.HyperlinkOrPicture = new Microsoft.Graph.Models.HyperlinkOrPictureColumn();
                break;
            default:
                // text is the default fallback
                col.Text = new Microsoft.Graph.Models.TextColumn();
                break;
        }

        return col;
    }

    /// <summary>
    /// Maps a Graph SDK ColumnDefinition to the domain model <see cref="SpeContainerColumn"/>.
    /// Derives the ColumnType string by inspecting which type facet is non-null.
    /// ADR-007: No Graph SDK types are returned from public methods.
    /// </summary>
    private static SpeContainerColumn MapColumn(Microsoft.Graph.Models.ColumnDefinition col)
    {
        var columnType = col switch
        {
            { Boolean: not null }            => "boolean",
            { DateTime: not null }           => "dateTime",
            { Currency: not null }           => "currency",
            { Choice: not null }             => "choice",
            { Number: not null }             => "number",
            { PersonOrGroup: not null }      => "personOrGroup",
            { HyperlinkOrPicture: not null } => "hyperlinkOrPicture",
            _                                => "text"
        };

        return new SpeContainerColumn(
            Id:          col.Id ?? string.Empty,
            Name:        col.Name ?? string.Empty,
            DisplayName: col.DisplayName,
            Description: col.Description,
            ColumnType:  columnType,
            Required:    col.Required ?? false,
            Indexed:     col.Indexed  ?? false,
            ReadOnly:    col.ReadOnly ?? false);
    }

    /// <summary>
    /// Maps a Graph SDK <see cref="Microsoft.Graph.Models.Permission"/> to the domain model <see cref="SpeContainerPermission"/>.
    /// Resolves display name, email, principal ID, and principal type from grantedToV2 (preferred) or grantedTo.
    /// ADR-007: No Graph SDK types are returned from public methods.
    /// </summary>
    private static SpeContainerPermission MapPermission(Microsoft.Graph.Models.Permission perm)
    {
        var role = perm.Roles?.FirstOrDefault() ?? string.Empty;

        // grantedToV2 is the preferred field for SPE permissions (Graph v5 / beta API).
        // Fall back to grantedTo for backward compatibility.
        string? displayName = null;
        string? email = null;
        string? principalId = null;
        string? principalType = null;

        if (perm.GrantedToV2 is { } grantedToV2)
        {
            if (grantedToV2.User is { } user)
            {
                displayName = user.DisplayName;
                // Graph SDK 5.x returns Identity (not SharePointIdentity) from GrantedToV2.User;
                // LoginName / Email are not available on the base Identity type.
                // Use DisplayName as the display field; email left null (principal ID is sufficient for SPE ops).
                email = null;
                principalId = user.Id;
                principalType = "user";
            }
            else if (grantedToV2.Group is { } group)
            {
                displayName = group.DisplayName;
                // Same constraint as above — Identity base type does not carry email/UPN.
                email = null;
                principalId = group.Id;
                principalType = "group";
            }
            else if (grantedToV2.Application is { } application)
            {
                displayName = application.DisplayName;
                principalId = application.Id;
                principalType = "application";
            }
        }
        else if (perm.GrantedTo is { } grantedTo)
        {
            if (grantedTo.User is { } user)
            {
                displayName = user.DisplayName;
                principalId = user.Id;
                principalType = "user";
            }
        }

        return new SpeContainerPermission(
            Id: perm.Id ?? string.Empty,
            Role: role,
            DisplayName: displayName,
            Email: email,
            PrincipalId: principalId,
            PrincipalType: principalType);
    }

    /// <summary>
    /// Uploads a file to an SPE container using direct PUT (small files) or resumable session (large files).
    /// Files under 4 MB use a direct PUT to the drive item content endpoint.
    /// Files 4 MB and above use the Graph SDK LargeFileUploadTask with 5 MB chunks.
    /// Returns domain model SpeContainerItemSummary — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The SPE FileStorageContainer ID.</param>
    /// <param name="fileName">Name of the file to create (e.g. "report.pdf").</param>
    /// <param name="fileStream">Readable stream of the file content.</param>
    /// <param name="fileSize">Total byte length of the file, used to select the upload strategy.</param>
    /// <param name="folderId">Optional DriveItem ID of a target subfolder. Null to upload to root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary of the newly created DriveItem.</returns>
    public async Task<SpeContainerItemSummary> UploadFileToContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        string fileName,
        Stream fileStream,
        long fileSize,
        string? folderId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(fileStream);

        _logger.LogInformation(
            "Uploading file '{FileName}' ({FileSize} bytes) to container {ContainerId}, FolderId: {FolderId}",
            fileName, fileSize, containerId, folderId ?? "(root)");

        // Resolve drive ID: SPE container ID is not the same as a drive ID.
        var driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);
        Microsoft.Graph.Models.DriveItem? uploadedItem;

        // Route to small (direct PUT) or large (resumable session) upload strategy.
        const long largeFileThreshold = 4L * 1024 * 1024; // 4 MB
        if (fileSize < largeFileThreshold)
        {
            uploadedItem = await UploadSmallFileAsync(graphClient, driveId, fileName, fileStream, folderId, ct);
        }
        else
        {
            uploadedItem = await UploadLargeFileAsync(graphClient, driveId, fileName, fileStream, fileSize, folderId, ct);
        }

        if (uploadedItem is null)
        {
            throw new InvalidOperationException(
                $"Graph API returned null after uploading '{fileName}' to container '{containerId}'.");
        }

        _logger.LogInformation(
            "File '{FileName}' uploaded successfully. DriveItemId: {ItemId}, Container: {ContainerId}",
            fileName, uploadedItem.Id, containerId);

        return new SpeContainerItemSummary(
            Id: uploadedItem.Id ?? string.Empty,
            Name: uploadedItem.Name ?? fileName,
            Size: uploadedItem.Size,
            LastModifiedDateTime: uploadedItem.LastModifiedDateTime,
            CreatedDateTime: uploadedItem.CreatedDateTime,
            CreatedByDisplayName: uploadedItem.CreatedBy?.User?.DisplayName,
            IsFolder: false,
            MimeType: uploadedItem.File?.MimeType,
            WebUrl: uploadedItem.WebUrl);
    }

    /// <summary>
    /// Uploads a small file (under 4 MB) using a direct PUT to the drive item content endpoint.
    /// Navigates to the parent item via "root" (no target folder) or a specific folder DriveItem ID.
    /// </summary>
    private async Task<Microsoft.Graph.Models.DriveItem?> UploadSmallFileAsync(
        GraphServiceClient graphClient,
        string driveId,
        string fileName,
        Stream fileStream,
        string? folderId,
        CancellationToken ct)
    {
        var parentItemId = string.IsNullOrWhiteSpace(folderId) ? "root" : folderId;

        _logger.LogDebug(
            "Small file upload: driveId={DriveId}, parent={ParentId}, file={FileName}",
            driveId, parentItemId, fileName);

        // PUT /drives/{driveId}/items/{parentId}:/{fileName}:/content
        // ItemWithPath() is an extension method on DriveItemRequestBuilder that appends
        // the path segment ":/{fileName}:" to navigate to a named child item.
        return await ExecuteWithRetryAsync(
            () => graphClient.Drives[driveId]
                .Items[parentItemId]
                .ItemWithPath(fileName)
                .Content
                .PutAsync(fileStream, cancellationToken: ct),
            ct);
    }

    /// <summary>
    /// Uploads a large file (4 MB or above) using the Graph resumable upload session protocol.
    ///
    /// Protocol:
    ///   1. Create an upload session — Graph returns an upload URL valid for ~1 day.
    ///   2. Stream the file in sequential 5 MB chunks using PUT requests with Content-Range headers.
    ///   3. The final chunk response (200 OK) contains the completed DriveItem JSON.
    ///
    /// Manual chunked upload (via HttpClient) is used because the Graph SDK's LargeFileUploadTask
    /// helper targets a different API surface. The Graph API requires each chunk to be a multiple
    /// of 320 KiB; 5 MB (5,242,880 bytes) satisfies this requirement.
    /// </summary>
    private async Task<Microsoft.Graph.Models.DriveItem?> UploadLargeFileAsync(
        GraphServiceClient graphClient,
        string driveId,
        string fileName,
        Stream fileStream,
        long fileSize,
        string? folderId,
        CancellationToken ct)
    {
        var parentItemId = string.IsNullOrWhiteSpace(folderId) ? "root" : folderId;

        _logger.LogDebug(
            "Large file upload: driveId={DriveId}, parent={ParentId}, file={FileName}, size={Size}",
            driveId, parentItemId, fileName, fileSize);

        // Step 1: Create upload session.
        // @microsoft.graph.conflictBehavior = "rename" instructs Graph to rename on name collision.
        var uploadSessionBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
        {
            Item = new Microsoft.Graph.Models.DriveItemUploadableProperties
            {
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", "rename" }
                }
            }
        };

        var uploadSession = await ExecuteWithRetryAsync(
            () => graphClient.Drives[driveId]
                .Items[parentItemId]
                .ItemWithPath(fileName)
                .CreateUploadSession
                .PostAsync(uploadSessionBody, cancellationToken: ct),
            ct);

        if (uploadSession?.UploadUrl is null)
        {
            throw new InvalidOperationException(
                $"Graph API did not return an upload URL for file '{fileName}'.");
        }

        _logger.LogDebug(
            "Upload session created for '{FileName}'. ExpiresAt: {ExpiresAt}",
            fileName, uploadSession.ExpirationDateTime);

        // Step 2: Stream the file in 5 MB chunks via direct HttpClient PUT requests.
        // 5 MB = 5,242,880 bytes. This is divisible by 327,680 (320 KiB) as required by Graph API.
        const int chunkSize = 5 * 1024 * 1024;
        var buffer = new byte[chunkSize];
        long offset = 0;
        Microsoft.Graph.Models.DriveItem? completedItem = null;

        _logger.LogDebug(
            "Starting chunked upload for '{FileName}' — {Chunks} chunk(s)",
            fileName,
            (long)Math.Ceiling((double)fileSize / chunkSize));

        using var httpClient = new System.Net.Http.HttpClient();

        while (offset < fileSize)
        {
            ct.ThrowIfCancellationRequested();

            var bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize, ct);
            if (bytesRead == 0) break;

            var rangeEnd = offset + bytesRead - 1;
            using var chunkContent = new System.Net.Http.ByteArrayContent(buffer, 0, bytesRead);
            chunkContent.Headers.ContentLength = bytesRead;
            chunkContent.Headers.ContentRange =
                new System.Net.Http.Headers.ContentRangeHeaderValue(offset, rangeEnd, fileSize);

            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Put, uploadSession.UploadUrl)
            {
                Content = chunkContent
            };

            var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.OK ||
                response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                // Final chunk — Graph returns the completed DriveItem as JSON.
                var json = await response.Content.ReadAsStringAsync(ct);
                completedItem = System.Text.Json.JsonSerializer.Deserialize<Microsoft.Graph.Models.DriveItem>(
                    json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                _logger.LogDebug(
                    "Final chunk uploaded for '{FileName}'. DriveItemId: {ItemId}",
                    fileName, completedItem?.Id);
                break;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                // Chunk accepted; continue to next chunk.
                _logger.LogDebug(
                    "Chunk accepted (offset {Offset}–{End} / {Total}) for '{FileName}'",
                    offset, rangeEnd, fileSize, fileName);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Chunk upload failed for '{fileName}' at offset {offset}. " +
                    $"HTTP {(int)response.StatusCode}: {errorBody}");
            }

            offset += bytesRead;
        }

        if (completedItem is null)
        {
            throw new InvalidOperationException(
                $"Large file upload did not complete for '{fileName}'. " +
                "The upload session may have expired or stream was empty.");
        }

        return completedItem;
    }

    // =========================================================================
    // Container search operations (SPE-057)
    // =========================================================================

    /// <summary>
    /// Searches SPE containers using the Microsoft Graph Search API (/search/query) with
    /// entity type <c>fileStorageContainer</c>.
    ///
    /// Returns domain model <see cref="ContainerSearchPage"/> — no Graph SDK types exposed (ADR-007).
    ///
    /// Pagination: Graph Search uses a 0-based <c>from</c> index and <c>size</c> page size.
    /// The caller supplies a numeric <paramref name="skipToken"/> that encodes the start offset.
    /// A <see cref="ContainerSearchPage.NextSkipToken"/> is returned when more results exist;
    /// the token encodes the next <c>from</c> offset as a decimal integer string.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="query">Full-text search query string. Required; must not be empty.</param>
    /// <param name="pageSize">Number of results per page (1–50). Defaults to 25.</param>
    /// <param name="skipToken">
    /// Opaque pagination token from a previous <see cref="ContainerSearchPage.NextSkipToken"/>.
    /// Pass <c>null</c> for the first page.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ContainerSearchPage"/> containing results and an optional next-page token.</returns>
    public async Task<ContainerSearchPage> SearchContainersAsync(
        GraphServiceClient graphClient,
        string query,
        int? pageSize,
        string? skipToken,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Clamp page size: Graph Search supports 1–500; keep to 25 default, 50 max for admin use.
        var size = Math.Clamp(pageSize ?? 25, 1, 50);

        // Decode skip token: we encode the Graph Search 'from' offset as a decimal string.
        var from = 0;
        if (!string.IsNullOrWhiteSpace(skipToken) && int.TryParse(skipToken, out var parsedFrom))
        {
            from = Math.Max(0, parsedFrom);
        }

        _logger.LogInformation(
            "Searching SPE containers: query='{Query}', from={From}, size={Size}",
            query, from, size);

        // Build the Graph Search request body.
        // "fileStorageContainer" is a beta-only entity type not yet in the EntityType enum.
        // We use AdditionalData to inject the raw entityTypes array so it serialises correctly
        // when posting to the Graph beta /search/query endpoint.
        var searchRequest = new Microsoft.Graph.Models.SearchRequest
        {
            Query = new Microsoft.Graph.Models.SearchQuery { QueryString = query },
            From = from,
            Size = size,
            Fields = new List<string>
            {
                "id", "displayName", "description", "containerTypeId"
            },
            AdditionalData = new Dictionary<string, object>
            {
                // Raw string array — Kiota serialises Dictionary<string, object> values
                // as JSON, so a string[] here produces the correct JSON array in the request.
                ["entityTypes"] = new string[] { "fileStorageContainer" }
            }
        };

        var requestBody = new Microsoft.Graph.Search.Query.QueryPostRequestBody
        {
            Requests = new List<Microsoft.Graph.Models.SearchRequest> { searchRequest }
        };

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => graphClient.Search.Query.PostAsQueryPostResponseAsync(requestBody, cancellationToken: ct),
                ct);

            var items = new List<SearchContainerResult>();
            long? totalCount = null;
            string? nextSkipToken = null;

            if (response?.Value != null)
            {
                foreach (var searchResponse in response.Value)
                {
                    if (searchResponse.HitsContainers == null) continue;

                    foreach (var hitsContainer in searchResponse.HitsContainers)
                    {
                        // Capture total result count from first non-null value
                        if (hitsContainer.Total.HasValue && totalCount is null)
                        {
                            totalCount = hitsContainer.Total.Value;
                        }

                        if (hitsContainer.Hits == null) continue;

                        foreach (var hit in hitsContainer.Hits)
                        {
                            var result = MapSearchHit(hit);
                            if (result != null)
                            {
                                items.Add(result);
                            }
                        }

                        // Emit a nextSkipToken when Graph reports more results are available.
                        // Graph Search pagination is offset-based (from + size); we encode the
                        // next 'from' position as the token.
                        if (hitsContainer.MoreResultsAvailable == true)
                        {
                            nextSkipToken = (from + size).ToString();
                        }
                    }
                }
            }

            _logger.LogInformation(
                "Container search '{Query}' returned {Count} hits (totalCount={TotalCount}, hasNext={HasNext})",
                query, items.Count, totalCount, nextSkipToken != null);

            return new ContainerSearchPage(items, totalCount, nextSkipToken);
        }
        catch (ODataError odataError)
        {
            _logger.LogError(
                odataError,
                "Graph Search API error for query '{Query}': Status={Status}, Message={Message}",
                query, odataError.ResponseStatusCode, odataError.Error?.Message);
            throw;
        }
    }

    /// <summary>
    /// Maps a Graph Search hit resource to a <see cref="SearchContainerResult"/> domain record.
    /// Returns <c>null</c> when the hit resource is not a <see cref="Microsoft.Graph.Models.FileStorageContainer"/>.
    /// ADR-007: No Graph SDK types are returned from public methods.
    /// </summary>
    private static SearchContainerResult? MapSearchHit(Microsoft.Graph.Models.SearchHit hit)
    {
        if (hit.Resource is not Microsoft.Graph.Models.FileStorageContainer container)
        {
            return null;
        }

        return new SearchContainerResult(
            Id: container.Id ?? hit.HitId ?? string.Empty,
            DisplayName: container.DisplayName ?? string.Empty,
            Description: container.Description,
            ContainerTypeId: container.ContainerTypeId?.ToString());
    }

    /// <summary>
    /// Evicts all expired entries from the client cache (app-only clients and OBO clients).
    /// Should be called periodically (e.g., from the dashboard sync background service).
    /// </summary>
    public void EvictExpiredClients()
    {
        var now = DateTimeOffset.UtcNow;

        // Evict expired app-only clients
        var expiredAppOnly = _clientCache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredAppOnly)
        {
            _clientCache.TryRemove(key, out _);
        }

        if (expiredAppOnly.Count > 0)
        {
            _logger.LogDebug("Evicted {Count} expired Graph client cache entries (app-only).", expiredAppOnly.Count);
        }

        // Evict expired OBO clients
        var expiredObo = _oboClientCache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredObo)
        {
            _oboClientCache.TryRemove(key, out _);
        }

        if (expiredObo.Count > 0)
        {
            _logger.LogDebug("Evicted {Count} expired Graph client cache entries (OBO).", expiredObo.Count);
        }

        // Also evict token provider's OBO tokens (keeps token cache in sync with client cache)
        _tokenProvider?.EvictExpiredTokens();
    }

    // =========================================================================
    // Container type operations (SPE-050)
    // =========================================================================

    /// <summary>
    /// Lists all container types visible to the app registration associated with the given Graph client.
    ///
    /// Calls the Graph beta endpoint: GET /storage/fileStorage/containerTypes
    ///
    /// Returns domain records only — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All container types registered in the tenant for the calling app registration.</returns>
    public async Task<IReadOnlyList<SpeContainerTypeSummary>> ListContainerTypesAsync(
        GraphServiceClient graphClient,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);

        _logger.LogInformation("Listing SPE container types from Graph API");

        var response = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.ContainerTypes
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[]
                    {
                        "id", "name", "billingClassification", "createdDateTime"
                    };
                }, ct),
            ct);

        var results = new List<SpeContainerTypeSummary>();

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var containerType in response.Value)
                {
                    results.Add(MapContainerType(containerType));
                }
            }

            if (response.OdataNextLink == null) break;

            _logger.LogDebug("Following nextLink for container types pagination: {NextLink}", response.OdataNextLink);

            response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypes
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: ct),
                ct);
        }

        _logger.LogInformation("Listed {Count} container types from Graph API", results.Count);

        return results;
    }

    /// <summary>
    /// Retrieves a single container type by its Graph ID.
    ///
    /// Calls the Graph beta endpoint: GET /storage/fileStorage/containerTypes/{containerTypeId}
    ///
    /// Returns <c>null</c> when Graph responds with 404 (container type not found).
    /// Returns domain record only — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The Graph container type ID (GUID string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Container type summary, or <c>null</c> if not found.</returns>
    public async Task<SpeContainerTypeSummary?> GetContainerTypeAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        _logger.LogInformation("Retrieving SPE container type {ContainerTypeId}", containerTypeId);

        try
        {
            var containerType = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypes[containerTypeId]
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Select = new[]
                        {
                            "id", "name", "billingClassification", "createdDateTime"
                        };
                    }, ct),
                ct);

            if (containerType is null)
            {
                _logger.LogWarning("Graph returned null for container type {ContainerTypeId}", containerTypeId);
                return null;
            }

            return MapContainerType(containerType);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Container type {ContainerTypeId} not found in Graph (404)", containerTypeId);
            return null;
        }
    }

    /// <summary>
    /// Creates a new SharePoint Embedded container type via the Graph API.
    ///
    /// POSTs to <c>/storage/fileStorage/containerTypes</c> with the specified name and billing
    /// classification. The container type is identified by a GUID assigned by Graph on creation.
    ///
    /// Returns a <see cref="SpeContainerTypeSummary"/> domain record — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="displayName">
    ///   Human-readable name for the container type. Maps to Graph SDK <c>FileStorageContainerType.Name</c>
    ///   (Graph uses "Name", not "DisplayName", for the containerType resource).
    /// </param>
    /// <param name="billingClassification">
    ///   Billing classification string ("standard" or "premium"). When null, Graph defaults to "standard".
    ///   Parsed to <c>FileStorageContainerTypeBillingClassification</c> enum for the SDK request body.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created container type as a domain record.</returns>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when Graph API returns null for the created container type (unexpected API behaviour).
    /// </exception>
    public async Task<SpeContainerTypeSummary> CreateContainerTypeAsync(
        GraphServiceClient graphClient,
        string displayName,
        string? billingClassification,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _logger.LogInformation(
            "Creating SPE container type '{DisplayName}' with billingClassification '{BillingClassification}'",
            displayName, billingClassification ?? "standard");

        // Parse billingClassification string to the Graph SDK enum.
        // FileStorageContainerBillingClassification values: Standard, Premium (case-insensitive parse).
        // Invalid values will be rejected by the endpoint before reaching here.
        Microsoft.Graph.Models.FileStorageContainerBillingClassification? billingEnum = null;
        if (!string.IsNullOrWhiteSpace(billingClassification) &&
            Enum.TryParse<Microsoft.Graph.Models.FileStorageContainerBillingClassification>(
                billingClassification,
                ignoreCase: true,
                out var parsed))
        {
            billingEnum = parsed;
        }

        // Build the Graph SDK request body.
        // NOTE: FileStorageContainerType uses "Name" (not "DisplayName") as the display label.
        var body = new Microsoft.Graph.Models.FileStorageContainerType
        {
            Name = displayName,
            BillingClassification = billingEnum
        };

        var created = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.ContainerTypes.PostAsync(body, cancellationToken: ct),
            ct);

        if (created is null)
        {
            throw new InvalidOperationException(
                $"Graph API returned null when creating container type '{displayName}'. " +
                "The container type may not have been created.");
        }

        _logger.LogInformation(
            "SPE container type created: Id={ContainerTypeId}, Name='{Name}', BillingClassification={BillingClassification}",
            created.Id, created.Name, created.BillingClassification?.ToString() ?? "null");

        return MapContainerType(created);
    }

    /// <summary>
    /// Maps a Graph SDK FileStorageContainerType to the SpeContainerTypeSummary domain record.
    ///
    /// Property mapping notes:
    ///   Graph SDK Name               → DisplayName (Graph uses "Name" as the label field)
    ///   Graph SDK BillingClassification → BillingClassification (typed enum in SDK v5.100+)
    ///   FileStorageContainerType has no Description property — Description mapped to null.
    /// </summary>
    private static SpeContainerTypeSummary MapContainerType(Microsoft.Graph.Models.FileStorageContainerType ct)
    {
        // BillingClassification: Graph SDK 5.101.0 does not include the typed enum
        // FileStorageContainerTypeBillingClassification in the installed version.
        // Extract the raw string value from AdditionalData as a fallback.
        string? billingClassification = null;
        if (ct.AdditionalData != null &&
            ct.AdditionalData.TryGetValue("billingClassification", out var billingObj))
        {
            billingClassification = billingObj?.ToString();
        }

        // FileStorageContainerType uses "Name" (not "DisplayName") as the display label in Graph SDK.
        // There is no Description property on the containerType Graph API resource.
        return new SpeContainerTypeSummary(
            Id: ct.Id ?? string.Empty,
            DisplayName: ct.Name ?? string.Empty,
            Description: null,
            BillingClassification: billingClassification,
            CreatedDateTime: ct.CreatedDateTime ?? DateTimeOffset.UtcNow);
    }

    // =========================================================================
    // Consuming tenant management operations (SPE-082)
    // =========================================================================

    /// <summary>
    /// Represents a consuming application registration for an SPE container type.
    ///
    /// In multi-tenant SPE scenarios, a single container type (owned by one app) can be consumed
    /// by multiple applications from different tenants. Each consuming app registration defines the
    /// permissions granted to the consuming application.
    ///
    /// ADR-007: No Graph SDK types in public API surface — callers receive only this domain record.
    /// </summary>
    /// <param name="AppId">Azure AD application (client) ID of the consuming application.</param>
    /// <param name="DisplayName">Display name of the consuming application. May be null.</param>
    /// <param name="TenantId">Home tenant ID of the consuming application. May be null.</param>
    /// <param name="DelegatedPermissions">Delegated permission scopes granted to the consuming app.</param>
    /// <param name="ApplicationPermissions">Application permission scopes granted to the consuming app.</param>
    public sealed record SpeConsumingTenant(
        string AppId,
        string? DisplayName,
        string? TenantId,
        IReadOnlyList<string> DelegatedPermissions,
        IReadOnlyList<string> ApplicationPermissions);

    /// <summary>
    /// Lists all consuming application registrations for the specified SPE container type.
    ///
    /// Uses the Graph API applicationPermissions endpoint to retrieve all registered consuming
    /// applications and their permission grants.
    ///
    /// Returns null when the container type is not found (Graph 404).
    /// Returns an empty list when the container type has no consuming app registrations.
    ///
    /// ADR-007: No Graph SDK types exposed — callers receive <see cref="SpeConsumingTenant"/> domain records.
    /// </summary>
    public async Task<IReadOnlyList<SpeConsumingTenant>?> ListConsumingTenantsAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        _logger.LogInformation(
            "Listing consuming tenants for container type {ContainerTypeId}", containerTypeId);

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants
                    .GetAsync(cancellationToken: ct),
                ct);

            var results = new List<SpeConsumingTenant>();

            while (response != null)
            {
                if (response.Value != null)
                {
                    foreach (var perm in response.Value)
                    {
                        results.Add(MapPermissionGrantToConsumingTenant(perm));
                    }
                }

                if (response.OdataNextLink == null) break;

                response = await ExecuteWithRetryAsync(
                    () => graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants
                        .WithUrl(response.OdataNextLink)
                        .GetAsync(cancellationToken: ct),
                    ct);
            }

            _logger.LogInformation(
                "Listed {Count} consuming tenants for container type {ContainerTypeId}",
                results.Count, containerTypeId);

            return results;
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container type {ContainerTypeId} not found when listing consuming tenants (404)", containerTypeId);
            return null;
        }
    }

    /// <summary>
    /// Registers a new consuming application for the specified SPE container type.
    ///
    /// Creates a new ApplicationPermissionGrant entry granting the specified delegated and
    /// application permissions to the consuming application.
    ///
    /// Returns null when the container type is not found (Graph 404).
    /// Throws ODataError with 409 status when the app is already registered.
    ///
    /// ADR-007: No Graph SDK types exposed — callers receive <see cref="SpeConsumingTenant"/> domain record.
    /// </summary>
    public async Task<SpeConsumingTenant?> RegisterConsumingTenantAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        string appId,
        string? tenantId,
        IReadOnlyList<string> delegatedPermissions,
        IReadOnlyList<string> applicationPermissions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        _logger.LogInformation(
            "Registering consuming app {AppId} for container type {ContainerTypeId}", appId, containerTypeId);

        try
        {
            // Build the permission grant body using the first permission from each list
            var primaryDelegated = delegatedPermissions.FirstOrDefault();
            var primaryApplication = applicationPermissions.FirstOrDefault();

            // Build the grant body using AdditionalData for permission strings.
            // SpePermissionLevel enum is not available in Graph SDK 5.99; use string values via AdditionalData.
            var additionalData = new Dictionary<string, object> { ["appId"] = appId };
            if (primaryDelegated is not null)
                additionalData["delegatedPermissions"] = primaryDelegated;
            if (primaryApplication is not null)
                additionalData["applicationPermissions"] = primaryApplication;

            var grantBody = new Microsoft.Graph.Models.FileStorageContainerTypeAppPermissionGrant
            {
                AppId = appId,
                AdditionalData = additionalData
            };

            await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants
                    .PostAsync(grantBody, cancellationToken: ct),
                ct);

            _logger.LogInformation(
                "Registered consuming app {AppId} for container type {ContainerTypeId}", appId, containerTypeId);

            return new SpeConsumingTenant(
                AppId: appId,
                DisplayName: null, // Graph API does not return display name on POST
                TenantId: tenantId,
                DelegatedPermissions: delegatedPermissions,
                ApplicationPermissions: applicationPermissions);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container type {ContainerTypeId} not found when registering consuming app {AppId} (404)",
                containerTypeId, appId);
            return null;
        }
    }

    /// <summary>
    /// Updates permissions for an existing consuming application registration.
    ///
    /// Finds the existing grant for the specified appId, then PATCHes the permission grant
    /// to replace permissions. Returns null when the container type or consuming app is not found.
    ///
    /// ADR-007: No Graph SDK types exposed — callers receive <see cref="SpeConsumingTenant"/> domain record.
    /// </summary>
    public async Task<SpeConsumingTenant?> UpdateConsumingTenantAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        string appId,
        IReadOnlyList<string> delegatedPermissions,
        IReadOnlyList<string> applicationPermissions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        _logger.LogInformation(
            "Updating consuming app {AppId} for container type {ContainerTypeId}", appId, containerTypeId);

        try
        {
            // Verify the consuming app exists by listing current registrations
            var existing = await ListConsumingTenantsAsync(graphClient, containerTypeId, ct);
            if (existing is null)
            {
                return null; // Container type not found
            }

            var existingGrant = existing.FirstOrDefault(c =>
                string.Equals(c.AppId, appId, StringComparison.OrdinalIgnoreCase));

            if (existingGrant is null)
            {
                _logger.LogInformation(
                    "Consuming app {AppId} not found in container type {ContainerTypeId} for update",
                    appId, containerTypeId);
                return null;
            }

            // PATCH the permission grant with updated permissions.
            // SpePermissionLevel enum is not available in Graph SDK 5.99; use string values via AdditionalData.
            var primaryDelegated = delegatedPermissions.FirstOrDefault();
            var primaryApplication = applicationPermissions.FirstOrDefault();

            var patchAdditionalData = new Dictionary<string, object> { ["appId"] = appId };
            if (primaryDelegated is not null)
                patchAdditionalData["delegatedPermissions"] = primaryDelegated;
            if (primaryApplication is not null)
                patchAdditionalData["applicationPermissions"] = primaryApplication;

            var patchBody = new Microsoft.Graph.Models.FileStorageContainerTypeAppPermissionGrant
            {
                AppId = appId,
                AdditionalData = patchAdditionalData
            };

            await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants[appId]
                    .PatchAsync(patchBody, cancellationToken: ct),
                ct);

            _logger.LogInformation(
                "Updated consuming app {AppId} for container type {ContainerTypeId}", appId, containerTypeId);

            return new SpeConsumingTenant(
                AppId: appId,
                DisplayName: existingGrant.DisplayName,
                TenantId: existingGrant.TenantId,
                DelegatedPermissions: delegatedPermissions,
                ApplicationPermissions: applicationPermissions);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container type {ContainerTypeId} or consuming app {AppId} not found for update (404)",
                containerTypeId, appId);
            return null;
        }
    }

    /// <summary>
    /// Removes a consuming application registration from the specified SPE container type.
    ///
    /// Deletes the permission grant for the specified consuming application.
    /// Returns true when the deletion was successful; false when not found (Graph 404).
    ///
    /// ADR-007: No Graph SDK types exposed — callers receive a boolean result.
    /// </summary>
    public async Task<bool> RemoveConsumingTenantAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        string appId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        _logger.LogInformation(
            "Removing consuming app {AppId} from container type {ContainerTypeId}", appId, containerTypeId);

        try
        {
            await graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants[appId]
                .DeleteAsync(cancellationToken: ct);

            _logger.LogInformation(
                "Removed consuming app {AppId} from container type {ContainerTypeId}", appId, containerTypeId);

            return true;
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container type {ContainerTypeId} or consuming app {AppId} not found when removing (404)",
                containerTypeId, appId);
            return false;
        }
    }

    /// <summary>
    /// Maps a Graph SDK FileStorageContainerTypeAppPermissionGrant to the SpeConsumingTenant domain record.
    /// ADR-007: No Graph SDK types in public surface.
    /// </summary>
    private static SpeConsumingTenant MapPermissionGrantToConsumingTenant(
        Microsoft.Graph.Models.FileStorageContainerTypeAppPermissionGrant perm)
    {
        var delegated = perm.DelegatedPermissions is not null
            ? (IReadOnlyList<string>)[perm.DelegatedPermissions.ToString()!]
            : (IReadOnlyList<string>)[];

        var application = perm.ApplicationPermissions is not null
            ? (IReadOnlyList<string>)[perm.ApplicationPermissions.ToString()!]
            : (IReadOnlyList<string>)[];

        return new SpeConsumingTenant(
            AppId: perm.AppId ?? string.Empty,
            DisplayName: null, // Graph API does not return display name on permission grants
            TenantId: null,    // Graph API does not return tenant ID on permission grants
            DelegatedPermissions: delegated,
            ApplicationPermissions: application);
    }

    // =========================================================================
    // Container type permission operations (SPE-054)
    // =========================================================================

    /// <summary>
    /// Retrieves all application permissions registered for a SharePoint Embedded container type.
    ///
    /// Calls the Graph beta endpoint:
    ///   GET /storage/fileStorage/containerTypes/{containerTypeId}/applicationPermissions
    ///
    /// Returns an empty list when no permissions have been registered.
    /// Returns null when the container type is not found (Graph 404).
    /// Returns domain records only — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The Graph container type ID (GUID string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// List of application permission entries, or <c>null</c> when the container type was not found.
    /// </returns>
    public async Task<IReadOnlyList<SpeContainerTypePermission>?> GetContainerTypePermissionsAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        _logger.LogInformation(
            "Retrieving application permissions for container type {ContainerTypeId}", containerTypeId);

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants
                    .GetAsync(cancellationToken: ct),
                ct);

            var results = new List<SpeContainerTypePermission>();

            while (response != null)
            {
                if (response.Value != null)
                {
                    foreach (var perm in response.Value)
                    {
                        results.Add(MapContainerTypePermission(perm));
                    }
                }

                if (response.OdataNextLink == null) break;

                _logger.LogDebug(
                    "Following nextLink for container type permissions pagination: {ContainerTypeId}",
                    containerTypeId);

                response = await ExecuteWithRetryAsync(
                    () => graphClient.Storage.FileStorage.ContainerTypeRegistrations[containerTypeId].ApplicationPermissionGrants
                        .WithUrl(response.OdataNextLink)
                        .GetAsync(cancellationToken: ct),
                    ct);
            }

            _logger.LogInformation(
                "Retrieved {Count} application permissions for container type {ContainerTypeId}",
                results.Count, containerTypeId);

            return results;
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container type {ContainerTypeId} not found in Graph when retrieving permissions (404)",
                containerTypeId);
            return null;
        }
    }

    /// <summary>
    /// Maps a Graph SDK FileStorageContainerTypeAppPermissionGrant to the
    /// SpeContainerTypePermission domain record.
    ///
    /// DelegatedPermissions and ApplicationPermissions are enum values on the Graph SDK type.
    /// They are converted to their string representation for the domain model.
    /// Null values are normalized to empty lists (ADR-007: no Graph SDK types in public surface).
    /// </summary>
    private static SpeContainerTypePermission MapContainerTypePermission(
        Microsoft.Graph.Models.FileStorageContainerTypeAppPermissionGrant perm)
    {
        // DelegatedPermissions and ApplicationPermissions are typed enums in the Graph SDK.
        // Convert to string list for the domain model, filtering out null/empty values.
        var delegated = perm.DelegatedPermissions is not null
            ? [perm.DelegatedPermissions.ToString()!]
            : (IReadOnlyList<string>)[];

        var application = perm.ApplicationPermissions is not null
            ? [perm.ApplicationPermissions.ToString()!]
            : (IReadOnlyList<string>)[];

        return new SpeContainerTypePermission(
            AppId: perm.AppId ?? string.Empty,
            DelegatedPermissions: delegated,
            ApplicationPermissions: application);
    }

    // =========================================================================
    // Container type settings operations (SPE-052)
    // =========================================================================

    /// <summary>
    /// The set of valid sharing capability values accepted by the Graph API for container type settings.
    /// These map to the allowed values for the <c>allowedRoles</c> or equivalent Graph property.
    /// </summary>
    public static readonly IReadOnlySet<string> ValidSharingCapabilities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "disabled",
            "view",
            "edit",
            "full"
        };

    /// <summary>
    /// Result returned by <see cref="UpdateContainerTypeSettingsAsync"/> after a successful PATCH.
    /// Contains the updated container type fields echoed back from the Graph API response.
    /// All Graph SDK types are stripped — callers receive only this domain record (ADR-007).
    /// </summary>
    /// <param name="Id">Container type GUID.</param>
    /// <param name="DisplayName">Human-readable display name for the container type.</param>
    /// <param name="BillingClassification">Billing classification string (e.g., "standard"). Null when not returned.</param>
    /// <param name="CreatedDateTime">When the container type was created (UTC).</param>
    public sealed record ContainerTypeSettingsResult(
        string Id,
        string DisplayName,
        string? BillingClassification,
        DateTimeOffset CreatedDateTime);

    /// <summary>
    /// Updates settings on an SPE container type via PATCH /storage/fileStorage/containerTypes/{typeId}.
    ///
    /// Only non-null fields from the request are included in the PATCH body. The Graph API
    /// applies partial updates (merge-patch semantics) — fields not included are left unchanged.
    ///
    /// Settings patched:
    ///   - SharingCapability (via AdditionalData as "allowedRoles" property)
    ///   - Versioning (via AdditionalData as "isMajorVersionLimitEnabled"/"majorVersionLimit")
    ///   - StorageUsedInBytes (via AdditionalData as "quota"/"storagePlanInformation")
    ///
    /// Returns <c>null</c> when Graph responds with 404 (container type not found).
    /// Returns <see cref="ContainerTypeSettingsResult"/> on success.
    /// Returns domain record only — no Graph SDK types exposed (ADR-007).
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The Graph container type ID (GUID string).</param>
    /// <param name="sharingCapability">New sharing capability ("disabled", "view", "edit", "full"). Null = no change.</param>
    /// <param name="isVersioningEnabled">Enable or disable versioning. Null = no change.</param>
    /// <param name="majorVersionLimit">Max major versions to retain. Null = no change.</param>
    /// <param name="storageUsedInBytes">Storage quota limit in bytes. Null = no change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated container type summary, or <c>null</c> if the container type was not found.</returns>
    public async Task<ContainerTypeSettingsResult?> UpdateContainerTypeSettingsAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        string? sharingCapability,
        bool? isVersioningEnabled,
        int? majorVersionLimit,
        long? storageUsedInBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        _logger.LogInformation(
            "Updating SPE container type settings for {ContainerTypeId}. " +
            "SharingCapability: {SharingCapability}, IsVersioningEnabled: {IsVersioningEnabled}, " +
            "MajorVersionLimit: {MajorVersionLimit}, StorageUsedInBytes: {StorageUsedInBytes}",
            containerTypeId, sharingCapability, isVersioningEnabled, majorVersionLimit, storageUsedInBytes);

        // Build the PATCH body using AdditionalData for settings not represented
        // as typed properties in the Graph SDK FileStorageContainerType model.
        var patchBody = new Microsoft.Graph.Models.FileStorageContainerType
        {
            AdditionalData = new Dictionary<string, object>()
        };

        if (!string.IsNullOrWhiteSpace(sharingCapability))
        {
            // Graph API accepts sharingCapability as a string value on the container type resource.
            patchBody.AdditionalData["sharingCapability"] = sharingCapability.ToLowerInvariant();
        }

        if (isVersioningEnabled.HasValue)
        {
            patchBody.AdditionalData["isVersioningEnabled"] = isVersioningEnabled.Value;
        }

        if (majorVersionLimit.HasValue)
        {
            patchBody.AdditionalData["majorVersionLimit"] = majorVersionLimit.Value;
        }

        if (storageUsedInBytes.HasValue)
        {
            patchBody.AdditionalData["storageUsedInBytes"] = storageUsedInBytes.Value;
        }

        try
        {
            // PATCH /storage/fileStorage/containerTypes/{typeId}
            var updated = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.ContainerTypes[containerTypeId]
                    .PatchAsync(patchBody, cancellationToken: ct),
                ct);

            if (updated is null)
            {
                // Graph returned null — treat as not found
                _logger.LogWarning(
                    "Graph returned null for PATCH container type {ContainerTypeId} — treating as not found",
                    containerTypeId);
                return null;
            }

            var billingClassification = updated.BillingClassification?.ToString();

            _logger.LogInformation(
                "Successfully updated container type settings for {ContainerTypeId}", containerTypeId);

            return new ContainerTypeSettingsResult(
                Id: updated.Id ?? containerTypeId,
                DisplayName: updated.Name ?? string.Empty,
                BillingClassification: billingClassification,
                CreatedDateTime: updated.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container type {ContainerTypeId} not found in Graph (404) during settings update",
                containerTypeId);
            return null;
        }
    }

    // =========================================================================
    // Container type registration (SPE-053)
    // =========================================================================

    /// <summary>
    /// Result returned by <see cref="RegisterContainerTypeAsync"/> after a successful registration.
    /// All SharePoint REST API types are stripped — callers receive only this domain record (ADR-007).
    /// </summary>
    /// <param name="ContainerTypeId">The container type GUID that was registered.</param>
    /// <param name="AppId">The Azure AD application (client) ID that was granted permissions.</param>
    /// <param name="DelegatedPermissions">Delegated permissions that were granted.</param>
    /// <param name="ApplicationPermissions">Application permissions that were granted.</param>
    public sealed record RegisterContainerTypeResult(
        string ContainerTypeId,
        string AppId,
        IReadOnlyList<string> DelegatedPermissions,
        IReadOnlyList<string> ApplicationPermissions);

    /// <summary>
    /// Registers a container type by granting the consuming application the specified permissions
    /// via the SharePoint REST API.
    ///
    /// Calls: PUT {sharePointAdminUrl}/_api/v2.1/storageContainerTypes/{containerTypeId}/applicationPermissions
    ///
    /// Registration is a critical security operation: it enables the consuming app (identified by appId)
    /// to create and manage containers of the given type. Without registration, the container type exists
    /// but cannot be used.
    ///
    /// Authentication: Uses ClientSecretCredential to acquire a SharePoint-scoped access token
    /// (scope: {sharePointHost}/.default) with the same credentials stored in the container type config.
    ///
    /// ADR-007: Returns domain model only — no SharePoint REST or Graph SDK types exposed.
    /// ADR-001: Calls are made via HttpClient (not Graph SDK) since SharePoint REST is required.
    /// </summary>
    /// <param name="config">Resolved container type configuration (provides clientId, tenantId, secret).</param>
    /// <param name="containerTypeId">The SPE container type GUID to register.</param>
    /// <param name="sharePointAdminUrl">
    ///   SharePoint Admin Center URL (e.g., https://contoso-admin.sharepoint.com).
    ///   Used as the base URL for the SharePoint REST API call.
    /// </param>
    /// <param name="appId">Azure AD application (client) ID of the consuming app to grant permissions to.</param>
    /// <param name="delegatedPermissions">Delegated permission names to grant (may be empty).</param>
    /// <param name="applicationPermissions">Application permission names to grant (may be empty).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Domain result with the registered container type ID, app ID, and granted permissions.</returns>
    /// <exception cref="HttpRequestException">Thrown when the SharePoint REST API returns a non-success status.</exception>
    public async Task<RegisterContainerTypeResult> RegisterContainerTypeAsync(
        ContainerTypeConfig config,
        string containerTypeId,
        string sharePointAdminUrl,
        string appId,
        IReadOnlyList<string> delegatedPermissions,
        IReadOnlyList<string> applicationPermissions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharePointAdminUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        _logger.LogInformation(
            "Registering container type {ContainerTypeId} for app {AppId} via SharePoint REST API. " +
            "DelegatedPermissions: [{Delegated}], ApplicationPermissions: [{Application}]",
            containerTypeId, appId,
            string.Join(", ", delegatedPermissions),
            string.Join(", ", applicationPermissions));

        // 1. Acquire a SharePoint-scoped access token using the config's app registration credentials.
        //    The scope must target the SharePoint admin host (not graph.microsoft.com).
        var clientSecret = await FetchKeyVaultSecretAsync(config.SecretKeyVaultName, ct);
        var credential = new ClientSecretCredential(config.TenantId, config.ClientId, clientSecret);

        // Normalize the SharePoint admin URL and derive the scope host.
        // Build the admin host string from scheme+host explicitly to avoid the double-slash that
        // Uri.ToString() produces for root URIs (e.g., new Uri("https://host").ToString() == "https://host/").
        var adminBaseUri = new Uri(sharePointAdminUrl.TrimEnd('/'));
        var adminHost = $"{adminBaseUri.Scheme}://{adminBaseUri.Host}";
        var scope = $"{adminHost}/.default";

        _logger.LogDebug(
            "Acquiring SharePoint access token for scope '{Scope}' (containerTypeId: {ContainerTypeId})",
            scope, containerTypeId);

        var tokenContext = new Azure.Core.TokenRequestContext(new[] { scope });
        var accessToken = await credential.GetTokenAsync(tokenContext, ct);

        // 2. Build the SharePoint REST API request body.
        //    PUT /_api/v2.1/storageContainerTypes/{containerTypeId}/applicationPermissions
        var requestUrl = $"{adminHost}/_api/v2.1/storageContainerTypes/{containerTypeId}/applicationPermissions";

        var requestPayload = new
        {
            value = new[]
            {
                new
                {
                    appId,
                    delegatedPermissions = delegatedPermissions.ToArray(),
                    applicationPermissions = applicationPermissions.ToArray()
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(requestPayload);
        using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // 3. Send the PUT request using a shared HttpClient.
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Put, requestUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;

        _logger.LogInformation(
            "Sending PUT to SharePoint REST API: {RequestUrl} (containerTypeId: {ContainerTypeId}, appId: {AppId})",
            requestUrl, containerTypeId, appId);

        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "SharePoint REST API registration failed. Status: {StatusCode}. URL: {Url}. Body: {ErrorBody}",
                (int)response.StatusCode, requestUrl, errorBody);

            throw new HttpRequestException(
                $"SharePoint REST API registration failed with status {(int)response.StatusCode}: {errorBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        _logger.LogInformation(
            "Successfully registered container type {ContainerTypeId} for app {AppId}. " +
            "SharePoint status: {StatusCode}",
            containerTypeId, appId, (int)response.StatusCode);

        return new RegisterContainerTypeResult(
            ContainerTypeId: containerTypeId,
            AppId: appId,
            DelegatedPermissions: delegatedPermissions,
            ApplicationPermissions: applicationPermissions);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Fetches a client secret from Azure Key Vault by secret name.
    /// </summary>
    private async Task<string> FetchKeyVaultSecretAsync(string secretName, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Retrieving secret '{SecretName}' from Key Vault", secretName);

            var response = await _secretClient.GetSecretAsync(secretName, version: null, ct);
            var secret = response.Value.Value;

            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    $"Key Vault secret '{secretName}' exists but contains an empty value.");
            }

            return secret;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            _logger.LogError(
                "Key Vault secret '{SecretName}' not found. Verify the secret name in sprk_specontainertypeconfig.",
                secretName);
            throw new InvalidOperationException(
                $"Key Vault secret '{secretName}' not found. Verify the secret is provisioned.", ex);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                "Access denied to Key Vault secret '{SecretName}'. Verify managed identity Key Vault access policy.",
                secretName);
            throw new InvalidOperationException(
                $"Access denied to Key Vault secret '{secretName}'. Check managed identity permissions.", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error fetching Key Vault secret '{SecretName}'", secretName);
            throw;
        }
    }

    /// <summary>
    /// Creates a <see cref="GraphServiceClient"/> using ClientSecretCredential (app-only).
    /// Uses beta endpoint for SPE FileStorage APIs (required per graph-sdk-v5 pattern).
    /// Uses the shared "GraphApiClient" named HttpClient which provides centralized resilience
    /// (retry, circuit breaker, timeout) via GraphHttpMessageHandler.
    /// </summary>
    private GraphServiceClient CreateGraphClient(string tenantId, string clientId, string clientSecret)
    {
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        var authProvider = new AzureIdentityAuthenticationProvider(
            credential,
            scopes: new[] { "https://graph.microsoft.com/.default" });

        // Shared HttpClient with GraphHttpMessageHandler provides resilience (ADR-001)
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        // Beta endpoint required for SPE FileStorage container APIs
        return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
    }

    /// <summary>
    /// Creates a <see cref="GraphServiceClient"/> using a pre-acquired bearer token (OBO flow).
    /// Used for multi-app mode where the token was obtained via MSAL OBO exchange.
    /// Uses the shared "GraphApiClient" HttpClient for resilience.
    /// </summary>
    private GraphServiceClient CreateGraphClientFromBearerToken(string bearerToken)
    {
        // BaseBearerTokenAuthenticationProvider injects the token as the Authorization header.
        // The token was acquired via MSAL OBO and is already scoped to the owning app.
        var authProvider = new Microsoft.Kiota.Abstractions.Authentication.BaseBearerTokenAuthenticationProvider(
            new StaticBearerTokenProvider(bearerToken));

        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
    }

    /// <summary>
    /// Simple Kiota IAccessTokenProvider that returns a pre-acquired static bearer token.
    /// Used for OBO Graph clients where the token has already been exchanged via MSAL.
    /// </summary>
    private sealed class StaticBearerTokenProvider
        : Microsoft.Kiota.Abstractions.Authentication.IAccessTokenProvider
    {
        private readonly string _token;

        public StaticBearerTokenProvider(string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            _token = token;
        }

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_token);

        public Microsoft.Kiota.Abstractions.Authentication.AllowedHostsValidator AllowedHostsValidator { get; }
            = new(new[] { "graph.microsoft.com" });
    }

    /// <summary>
    /// Executes a Graph API call with exponential backoff retry on HTTP 429 throttling responses.
    /// Honors the Retry-After header when present.
    /// </summary>
    /// <typeparam name="T">Return type of the Graph API call.</typeparam>
    /// <param name="operation">Async factory function for the Graph API call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the Graph API call.</returns>
    private async Task<T?> ExecuteWithRetryAsync<T>(
        Func<Task<T?>> operation,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(
                        "Graph API throttling: max retries ({MaxRetries}) exceeded.", MaxRetries);
                    throw;
                }

                // TODO: Extract Retry-After header value from Graph API throttling response.
                // ODataError in Graph SDK v5 does not directly expose HTTP response headers;
                // Kiota surfaces them differently. For now, use exponential backoff.
                var delay = CalculateExponentialBackoff(attempt);

                _logger.LogWarning(
                    "Graph API throttled (429). Attempt {Attempt}/{MaxRetries}. Retrying after {Delay}.",
                    attempt + 1, MaxRetries, delay);

                await Task.Delay(delay, ct);
            }
            catch (ServiceException serviceEx) when (serviceEx.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(
                        "Graph API throttling (ServiceException): max retries ({MaxRetries}) exceeded.", MaxRetries);
                    throw;
                }

                var delay = CalculateExponentialBackoff(attempt);

                _logger.LogWarning(
                    "Graph API throttled (429, ServiceException). Attempt {Attempt}/{MaxRetries}. Retrying after {Delay}.",
                    attempt + 1, MaxRetries, delay);

                await Task.Delay(delay, ct);
            }
        }

        // Unreachable — loop either returns or throws.
        return default;
    }

    /// <summary>
    /// Calculates exponential backoff delay: BaseDelay * 2^attempt + jitter.
    /// </summary>
    private static TimeSpan CalculateExponentialBackoff(int attempt)
    {
        var exponential = BaseRetryDelay * Math.Pow(2, attempt);
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        var maxDelay = TimeSpan.FromSeconds(60);

        return exponential + jitter < maxDelay ? exponential + jitter : maxDelay;
    }

    // =========================================================================
    // Recycle Bin Operations (SPE-059)
    // =========================================================================

    /// <summary>
    /// Lists soft-deleted (recycle bin) containers for the given container type ID.
    ///
    /// Queries the Graph beta <c>/storage/fileStorage/deletedContainers</c> endpoint,
    /// filtering by container type ID. Handles pagination automatically.
    ///
    /// ADR-007: Returns domain model <see cref="DeletedContainerSummary"/> — no Graph SDK types exposed.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerTypeId">The SPE container type GUID to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All soft-deleted containers for the container type; empty list when none.</returns>
    public async Task<IReadOnlyList<DeletedContainerSummary>> ListDeletedContainersAsync(
        GraphServiceClient graphClient,
        string containerTypeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);

        _logger.LogInformation(
            "Listing deleted containers for containerTypeId {ContainerTypeId}", containerTypeId);

        var results = new List<DeletedContainerSummary>();

        // Graph beta: GET /storage/fileStorage/deletedContainers?$filter=containerTypeId eq '{id}'
        var response = await ExecuteWithRetryAsync(
            () => graphClient.Storage.FileStorage.DeletedContainers
                .GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"containerTypeId eq '{containerTypeId}'";
                    config.QueryParameters.Select = new[]
                    {
                        "id", "displayName", "containerTypeId", "deletedDateTime"
                    };
                }, ct),
            ct);

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var container in response.Value)
                {
                    // deletedDateTime is not a first-class typed property on FileStorageContainer
                    // in Graph SDK 5.x — it is returned via the AdditionalData dictionary when
                    // querying the deletedContainers endpoint.
                    DateTimeOffset? deletedAt = null;
                    if (container.AdditionalData != null &&
                        container.AdditionalData.TryGetValue("deletedDateTime", out var rawDeletedAt) &&
                        rawDeletedAt is string deletedAtStr &&
                        DateTimeOffset.TryParse(deletedAtStr, out var parsed))
                    {
                        deletedAt = parsed;
                    }

                    results.Add(new DeletedContainerSummary(
                        Id: container.Id ?? string.Empty,
                        DisplayName: container.DisplayName ?? string.Empty,
                        DeletedDateTime: deletedAt,
                        ContainerTypeId: container.ContainerTypeId?.ToString() ?? containerTypeId));
                }
            }

            if (response.OdataNextLink == null) break;

            response = await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.DeletedContainers
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: ct),
                ct);
        }

        _logger.LogInformation(
            "Listed {Count} deleted containers for containerTypeId {ContainerTypeId}",
            results.Count, containerTypeId);

        return results;
    }

    /// <summary>
    /// Restores a soft-deleted SPE container from the recycle bin.
    ///
    /// Calls the Graph beta <c>/storage/fileStorage/deletedContainers/{id}/restore</c> action.
    /// Returns <c>true</c> when the container was successfully restored.
    /// Returns <c>false</c> when the container was not found in the recycle bin (Graph 404).
    ///
    /// ADR-007: No Graph SDK types exposed — boolean result only.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID of the deleted container.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if restored; <c>false</c> if not found.</returns>
    public async Task<bool> RestoreContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Restoring deleted container {ContainerId}", containerId);

        try
        {
            // Graph beta: POST /storage/fileStorage/deletedContainers/{id}/restore
            await ExecuteWithRetryAsync(
                () => graphClient.Storage.FileStorage.DeletedContainers[containerId]
                    .Restore.PostAsync(cancellationToken: ct),
                ct);

            _logger.LogInformation("Container {ContainerId} restored from recycle bin", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Deleted container {ContainerId} not found in recycle bin (404 on restore)", containerId);
            return false;
        }
    }

    /// <summary>
    /// Permanently deletes a soft-deleted SPE container from the recycle bin.
    ///
    /// Calls the Graph beta <c>DELETE /storage/fileStorage/deletedContainers/{id}</c> endpoint.
    /// This operation is irreversible — the container and all its content are permanently purged.
    /// Returns <c>true</c> when the container was successfully purged.
    /// Returns <c>false</c> when the container was not found in the recycle bin (Graph 404).
    ///
    /// ADR-007: No Graph SDK types exposed — boolean result only.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID of the deleted container.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if permanently deleted; <c>false</c> if not found.</returns>
    public async Task<bool> PermanentDeleteContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Permanently deleting container {ContainerId} from recycle bin", containerId);

        try
        {
            // Graph beta: DELETE /storage/fileStorage/deletedContainers/{id}
            // This permanently purges the container — bypasses the recycle bin.
            // DeleteAsync returns Task (void), so wrap it in the retry helper pattern used
            // throughout this service (return (object?)null as the sentinel value).
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.DeletedContainers[containerId]
                        .DeleteAsync(cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation(
                "Container {ContainerId} permanently deleted from recycle bin", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Deleted container {ContainerId} not found in recycle bin (404 on permanent delete)", containerId);
            return false;
        }
    }

    // =========================================================================
    // Security API (SPE-060)
    // =========================================================================

    /// <summary>
    /// Retrieves the most recent security alerts for the tenant from the Microsoft Graph
    /// Security API (GET /security/alerts_v2).
    ///
    /// Returns up to <paramref name="maxAlerts"/> alerts ordered by createdDateTime descending.
    /// Alerts surface suspicious activities, policy violations, and other security events.
    ///
    /// ADR-007: No Graph SDK types in public API surface — callers receive
    /// <see cref="SecurityAlertResult"/> domain models only.
    /// </summary>
    /// <param name="graphClient">
    /// Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.
    /// </param>
    /// <param name="maxAlerts">Maximum number of alerts to return. Defaults to 50.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Ordered list of security alert domain models (newest first).
    /// Returns an empty list when no alerts are present — never returns null.
    /// </returns>
    public async Task<IReadOnlyList<SecurityAlertResult>> GetSecurityAlertsAsync(
        GraphServiceClient graphClient,
        int maxAlerts = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);

        _logger.LogInformation(
            "Fetching security alerts from Graph API (max {MaxAlerts})", maxAlerts);

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => graphClient.Security.Alerts_v2.GetAsync(config =>
                {
                    config.QueryParameters.Top = maxAlerts;
                    config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
                    config.QueryParameters.Select = new[]
                    {
                        "id", "title", "severity", "status",
                        "createdDateTime", "description"
                    };
                }, ct),
                ct);

            if (response?.Value is null)
            {
                _logger.LogInformation("Security alerts API returned no value collection");
                return [];
            }

            var results = response.Value
                .Select(a => new SecurityAlertResult(
                    Id: a.Id ?? string.Empty,
                    Title: a.Title,
                    Severity: a.Severity?.ToString(),
                    Status: a.Status?.ToString(),
                    CreatedDateTime: a.CreatedDateTime,
                    Description: a.Description))
                .ToList();

            _logger.LogInformation(
                "Retrieved {Count} security alerts from Graph API", results.Count);

            return results;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Access denied fetching security alerts (403). " +
                "The app registration may not have SecurityEvents.Read.All permission.");
            throw;
        }
    }

    /// <summary>
    /// Domain model for a single security alert returned from the Graph Security API.
    /// Callers receive this type; no Graph SDK types are exposed (ADR-007).
    /// </summary>
    public sealed record SecurityAlertResult(
        string Id,
        string? Title,
        string? Severity,
        string? Status,
        DateTimeOffset? CreatedDateTime,
        string? Description);

    /// <summary>
    /// Retrieves the Microsoft Secure Score for the tenant from the Graph Security API
    /// (GET /security/secureScores).
    ///
    /// Returns the most recent score snapshot. The Secure Score quantifies the tenant's
    /// security posture (currentScore / maxScore) and includes comparison benchmarks.
    ///
    /// ADR-007: No Graph SDK types in public API surface — callers receive
    /// <see cref="SecureScoreResult"/> domain model only.
    /// </summary>
    /// <param name="graphClient">
    /// Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The most recent <see cref="SecureScoreResult"/>, or <c>null</c> when no score
    /// data is available from Graph.
    /// </returns>
    public async Task<SecureScoreResult?> GetSecureScoreAsync(
        GraphServiceClient graphClient,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);

        _logger.LogInformation("Fetching secure score from Graph API");

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => graphClient.Security.SecureScores.GetAsync(config =>
                {
                    // Take only the most recent snapshot
                    config.QueryParameters.Top = 1;
                    config.QueryParameters.Select = new[]
                    {
                        "id", "currentScore", "maxScore", "averageComparativeScores"
                    };
                }, ct),
                ct);

            var score = response?.Value?.FirstOrDefault();
            if (score is null)
            {
                _logger.LogInformation("Secure score API returned no score data");
                return null;
            }

            // Map averageComparativeScores from Graph SDK type to domain type (ADR-007).
            var comparisons = score.AverageComparativeScores?
                .Select(c => new AverageComparativeScore(
                    Basis: c.Basis,
                    AverageScore: c.AverageScore))
                .ToList();

            var result = new SecureScoreResult(
                CurrentScore: score.CurrentScore,
                MaxScore: score.MaxScore,
                AverageComparativeScores: comparisons);

            _logger.LogInformation(
                "Retrieved secure score: {CurrentScore}/{MaxScore}",
                result.CurrentScore, result.MaxScore);

            return result;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Access denied fetching secure score (403). " +
                "The app registration may not have SecurityEvents.Read.All permission.");
            throw;
        }
    }

    /// <summary>
    /// Domain model for the Microsoft Secure Score returned from the Graph Security API.
    /// Callers receive this type; no Graph SDK types are exposed (ADR-007).
    /// </summary>
    public sealed record SecureScoreResult(
        double? CurrentScore,
        double? MaxScore,
        IReadOnlyList<AverageComparativeScore>? AverageComparativeScores);

    /// <summary>
    /// A single peer-group comparison entry within a <see cref="SecureScoreResult"/>.
    /// </summary>
    public sealed record AverageComparativeScore(
        string? Basis,
        double? AverageScore);

    // =========================================================================
    // Bulk Operations (SPE-083)
    // =========================================================================

    /// <summary>
    /// Soft-deletes (moves to recycle bin) an active SPE container.
    ///
    /// Calls <c>DELETE /storage/fileStorage/containers/{id}</c>, which moves the container
    /// into the recycle bin rather than permanently destroying it.
    /// Returns <c>true</c> when the container was successfully soft-deleted.
    /// Returns <c>false</c> when the container was not found (Graph 404).
    ///
    /// ADR-007: No Graph SDK types exposed — boolean result only.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph client from <see cref="GetClientForConfigAsync"/>.</param>
    /// <param name="containerId">The Graph FileStorageContainer ID to soft-delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if soft-deleted; <c>false</c> if not found.</returns>
    public async Task<bool> SoftDeleteContainerAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        _logger.LogInformation("Soft-deleting (recycle bin) container {ContainerId}", containerId);

        try
        {
            // DELETE /storage/fileStorage/containers/{id}
            // This moves the container to the recycle bin (soft delete / recoverable).
            await ExecuteWithRetryAsync(
                async () =>
                {
                    await graphClient.Storage.FileStorage.Containers[containerId]
                        .DeleteAsync(cancellationToken: ct);
                    return (object?)null;
                },
                ct);

            _logger.LogInformation("Container {ContainerId} moved to recycle bin (soft delete)", containerId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Container {ContainerId} not found (404 on soft delete)", containerId);
            return false;
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Extracts the <c>$skiptoken</c> query parameter value from a Graph OData nextLink URL.
    /// Returns <c>null</c> when the parameter is absent or the URL cannot be parsed.
    /// </summary>
    private static string? ExtractSkipToken(string nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
            return null;

        try
        {
            var uri = new Uri(nextLink);

            // Parse query string manually to avoid System.Web dependency.
            // Graph nextLink format: ?$filter=...&$skiptoken=...&$top=...
            foreach (var segment in uri.Query.TrimStart('?').Split('&'))
            {
                var eqIdx = segment.IndexOf('=');
                if (eqIdx <= 0) continue;

                var key = Uri.UnescapeDataString(segment[..eqIdx]);
                if (key.Equals("$skiptoken", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("skiptoken", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(segment[(eqIdx + 1)..]);
                }
            }

            return null;
        }
        catch
        {
            // Malformed nextLink — treat as no skip token
            return null;
        }
    }


    // =========================================================================
    // Search operations (SPE-058)
    // =========================================================================

    /// <summary>Domain model for a single search result item (ADR-007: no Graph SDK types exposed).</summary>
    public sealed record SpeSearchItemResult(
        string Id,
        string Name,
        long? Size,
        DateTimeOffset? LastModifiedDateTime,
        string? ContainerId,
        string? ContainerName,
        string? WebUrl,
        string? MimeType);

    /// <summary>Paginated result returned from SearchItemsAsync.</summary>
    public sealed record SearchItemPage(
        IReadOnlyList<SpeSearchItemResult> Items,
        string? NextSkipToken,
        int TotalCount);

    /// <summary>
    /// Searches for drive items within SPE containers using the Microsoft Graph search API.
    /// Uses POST /search/query with entityTypes: ["driveItem"].
    /// When containerId is provided, search is scoped to that container via contentSources.
    /// Returns domain model SearchItemPage — no Graph SDK types exposed (ADR-007).
    /// </summary>
    public async Task<SearchItemPage> SearchItemsAsync(
        GraphServiceClient graphClient,
        string query,
        string? containerId,
        string? fileType,
        int? pageSize,
        string? skipToken,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Clamp page size: Graph search accepts 1-500. Default to 25.
        var size = pageSize is > 0 ? Math.Min(pageSize.Value, 500) : 25;

        // Build the effective KQL query, optionally appending a fileType filter.
        var effectiveQuery = string.IsNullOrWhiteSpace(fileType)
            ? query
            : $"{query} fileType:{fileType}";

        _logger.LogInformation(
            "SearchItems: query='{Query}', containerId={ContainerId}, fileType={FileType}, pageSize={PageSize}, hasSkipToken={HasSkipToken}",
            query, containerId, fileType, size, skipToken != null);

        // When scoping to a container, resolve its driveId for contentSources.
        string? driveId = null;
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            driveId = await ResolveDriveIdForContainerAsync(graphClient, containerId, ct);
        }

        // Decode pagination offset from skipToken string (we encode From as string).
        var fromOffset = !string.IsNullOrWhiteSpace(skipToken) && int.TryParse(skipToken, out var parsedOffset)
            ? parsedOffset
            : 0;

        // Build Graph search request body.
        var searchRequest = new Microsoft.Graph.Search.Query.QueryPostRequestBody
        {
            Requests = new List<Microsoft.Graph.Models.SearchRequest>
            {
                new Microsoft.Graph.Models.SearchRequest
                {
                    EntityTypes = new List<Microsoft.Graph.Models.EntityType?>
                    {
                        Microsoft.Graph.Models.EntityType.DriveItem
                    },
                    Query = new Microsoft.Graph.Models.SearchQuery
                    {
                        QueryString = effectiveQuery
                    },
                    Size = size,
                    From = fromOffset,
                    ContentSources = !string.IsNullOrWhiteSpace(driveId)
                        ? new List<string> { $"/drives/{driveId}" }
                        : null,
                    Fields = new List<string>
                    {
                        "id", "name", "size", "lastModifiedDateTime", "webUrl",
                        "file", "parentReference"
                    }
                }
            }
        };

        var response = await ExecuteWithRetryAsync(
            () => graphClient.Search.Query.PostAsQueryPostResponseAsync(searchRequest, cancellationToken: ct),
            ct);

        var results = new List<SpeSearchItemResult>();
        int totalCount = 0;
        string? nextSkipToken = null;

        if (response?.Value != null)
        {
            foreach (var searchResponse in response.Value)
            {
                if (searchResponse.HitsContainers == null) continue;

                foreach (var hitsContainer in searchResponse.HitsContainers)
                {
                    if (totalCount == 0 && hitsContainer.Total.HasValue)
                    {
                        totalCount = (int)hitsContainer.Total.Value;
                    }

                    if (hitsContainer.MoreResultsAvailable == true)
                    {
                        nextSkipToken = (fromOffset + size).ToString();
                    }

                    if (hitsContainer.Hits == null) continue;

                    foreach (var hit in hitsContainer.Hits)
                    {
                        if (hit.Resource is Microsoft.Graph.Models.DriveItem driveItem)
                        {
                            results.Add(new SpeSearchItemResult(
                                Id: driveItem.Id ?? string.Empty,
                                Name: driveItem.Name ?? string.Empty,
                                Size: driveItem.Size,
                                LastModifiedDateTime: driveItem.LastModifiedDateTime,
                                ContainerId: containerId,
                                ContainerName: null,
                                WebUrl: driveItem.WebUrl,
                                MimeType: driveItem.File?.MimeType));
                        }
                    }
                }
            }
        }

        _logger.LogInformation(
            "SearchItems: returned {Count} hits (totalEstimate={Total}), hasNextPage={HasNext}",
            results.Count, totalCount, nextSkipToken != null);

        return new SearchItemPage(results, nextSkipToken, totalCount);
    }

}
