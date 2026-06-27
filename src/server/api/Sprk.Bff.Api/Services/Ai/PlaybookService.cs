using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing analysis playbooks in Dataverse.
/// Uses Web API for CRUD operations and N:N relationship management.
/// </summary>
public class PlaybookService : IPlaybookService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<PlaybookService> _logger;
    private readonly ITenantCache _cache;

    // NFR-08 system-level cache allow-listed (FR-05 redis remediation r1):
    // IPlaybookService.GetByNameAsync(name) has no tenantId in scope — playbook-by-name lookup
    // is org-wide per ADR-029 (single Redis instance per BFF org). Using the "system" sentinel
    // as the ITenantCache tenant scope preserves the wrapper invariant while documenting the
    // system-level cache exception. On-wire key: spaarke:tenant:system:playbook-by-name:{name}:v1
    private const string SystemTenantSentinel = "system";
    private const string CacheResource = "playbook-by-name";
    private const int CacheVersion = 1;
    private AccessToken? _currentToken;

    private const string EntitySetName = "sprk_analysisplaybooks";
    private const string EntityLogicalName = "sprk_analysisplaybook";

    // N:N relationship names (from Task 020 verification)
    private const string ActionRelationship = "sprk_analysisplaybook_action";
    private const string SkillRelationship = "sprk_playbook_skill";
    private const string KnowledgeRelationship = "sprk_playbook_knowledge";
    private const string ToolRelationship = "sprk_playbook_tool";

    // sprk_indexstatus option codes (chat-routing-redesign-r1 FR-13, task 030 schema
    // verification). These are the single source of truth for the numeric option-set
    // codes across PlaybookService, PlaybookEmbeddingService, PlaybookIndexingService,
    // and PlaybookIndexDriftDetectionJob.
    private const int IndexStatusNotIndexed = 100_000_000;
    private const int IndexStatusPending = 100_000_001;
    private const int IndexStatusIndexed = 100_000_002;
    private const int IndexStatusStale = 100_000_003;
    private const int IndexStatusFailed = 100_000_004;

    /// <summary>
    /// Default <c>sprk_indexstatus</c> value for newly-created or never-indexed playbooks.
    /// Public so that <see cref="PlaybookIndexDriftDetectionJob"/> can use the same default.
    /// </summary>
    public const int NotIndexedCode = IndexStatusNotIndexed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PlaybookService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<PlaybookService> logger,
        ITenantCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _credential = credential;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        // IMPORTANT: BaseAddress must end with trailing slash, otherwise relative URLs replace the last segment
        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Initialized PlaybookService - DataverseUrl: {DataverseUrl}, Constructed ApiUrl: {ApiUrl}, BaseAddress: {BaseAddress}",
            dataverseUrl, _apiUrl, _httpClient.BaseAddress);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token for PlaybookService");
        }
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> CreatePlaybookAsync(
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Creating playbook: {Name}", request.Name);

        // Create playbook entity
        // NOTE: sprk_istemplate removed until Dataverse schema is updated (causes 400 if column doesn't exist)
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = request.Name,
            ["sprk_description"] = request.Description,
            ["sprk_ispublic"] = request.IsPublic
        };

        // Add output type lookup if provided
        if (request.OutputTypeId.HasValue)
        {
            payload["sprk_OutputTypeId@odata.bind"] = $"/sprk_aioutputtypes({request.OutputTypeId.Value})";
        }

        var response = await _httpClient.PostAsJsonAsync(EntitySetName, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Extract ID from OData-EntityId header
        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to get entity ID from response");
        var playbookId = Guid.Parse(entityIdHeader.Split('(', ')')[1]);

        _logger.LogInformation("Created playbook {Id}: {Name}", playbookId, request.Name);

        // Associate N:N relationships
        await AssociateRelationshipsAsync(playbookId, request, cancellationToken);

        // Return the created playbook
        return await GetPlaybookAsync(playbookId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created playbook");
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> UpdatePlaybookAsync(
        Guid playbookId,
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Updating playbook {Id}: {Name}", playbookId, request.Name);

        // Update playbook entity
        // NOTE: sprk_istemplate removed until Dataverse schema is updated (causes 400 if column doesn't exist)
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = request.Name,
            ["sprk_description"] = request.Description,
            ["sprk_ispublic"] = request.IsPublic
        };

        // Add output type lookup if provided
        if (request.OutputTypeId.HasValue)
        {
            payload["sprk_OutputTypeId@odata.bind"] = $"/sprk_aioutputtypes({request.OutputTypeId.Value})";
        }

        var url = $"{EntitySetName}({playbookId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Updated playbook {Id}", playbookId);

        // Update N:N relationships (clear existing and re-associate)
        await ClearRelationshipsAsync(playbookId, cancellationToken);
        await AssociateRelationshipsAsync(playbookId, request, cancellationToken);

        // Return the updated playbook
        return await GetPlaybookAsync(playbookId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated playbook");
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse?> GetPlaybookAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // NOTE: OutputTypeId field removed - output types are N:N relationship, not lookup
        // NOTE: sprk_istemplate removed until Dataverse schema is updated
        // NOTE: sprk_jps_matching_metadata added by chat-routing-redesign-r1 task 031/036 — required by FR-10/FR-12
        // NOTE: sprk_indexstatus / sprk_indexhash / sprk_lastindexedat added by chat-routing-redesign-r1
        //       task 034 follow-up (Gap 2) — required by FR-13 drift detection and Gap 5 hash-on-index
        var select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_jps_matching_metadata,sprk_indexstatus,sprk_indexhash,sprk_lastindexedat,sprk_ispublic,_ownerid_value,createdon,modifiedon,sprk_playbookcapabilities";
        var url = $"{EntitySetName}({playbookId})?$select={select}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<PlaybookEntity>(JsonOptions, cancellationToken);
        if (entity == null) return null;

        // Load N:N relationships
        var actionIds = await GetRelatedIdsAsync(playbookId, ActionRelationship, "sprk_analysisactions", "sprk_analysisactionid", cancellationToken);
        var skillIds = await GetRelatedIdsAsync(playbookId, SkillRelationship, "sprk_analysisskills", "sprk_analysisskillid", cancellationToken);
        var knowledgeIds = await GetRelatedIdsAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", "sprk_analysisknowledgeid", cancellationToken);
        var toolIds = await GetRelatedIdsAsync(playbookId, ToolRelationship, "sprk_analysistools", "sprk_analysistoolid", cancellationToken);

        return new PlaybookResponse
        {
            Id = entity.Id,
            Name = entity.Name ?? string.Empty,
            Description = entity.Description,
            JpsMatchingMetadata = entity.JpsMatchingMetadata,
            IndexStatusCode = entity.IndexStatusCode ?? NotIndexedCode,
            IndexHash = entity.IndexHash,
            LastIndexedAt = entity.LastIndexedAt,
            OutputTypeId = entity.OutputTypeId,
            IsPublic = entity.IsPublic ?? false,
            IsTemplate = entity.IsTemplate ?? false,
            OwnerId = entity.OwnerId ?? Guid.Empty,
            Capabilities = ParseCapabilities(entity.Capabilities),
            ActionIds = actionIds,
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds,
            ToolIds = toolIds,
            CreatedOn = entity.CreatedOn ?? DateTime.UtcNow,
            ModifiedOn = entity.ModifiedOn ?? DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<bool> UserHasAccessAsync(
        Guid playbookId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"{EntitySetName}({playbookId})?$select=sprk_ispublic,_ownerid_value";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<PlaybookEntity>(JsonOptions, cancellationToken);
        if (entity == null) return false;

        // User has access if they own the playbook or it's public
        return entity.OwnerId == userId || (entity.IsPublic ?? false);
    }

    /// <inheritdoc />
    public Task<PlaybookValidationResult> ValidateAsync(
        SavePlaybookRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Name is required");
        }
        else if (request.Name.Length > 200)
        {
            errors.Add("Name cannot exceed 200 characters");
        }

        if (request.Description?.Length > 4000)
        {
            errors.Add("Description cannot exceed 4000 characters");
        }

        // Validate that at least one action or tool is specified
        var hasActions = request.ActionIds?.Length > 0;
        var hasTools = request.ToolIds?.Length > 0;
        if (!hasActions && !hasTools)
        {
            errors.Add("At least one action or tool must be specified");
        }

        var result = errors.Count == 0
            ? PlaybookValidationResult.Success()
            : PlaybookValidationResult.Failure(errors.ToArray());

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<PlaybookListResponse> ListUserPlaybooksAsync(
        Guid userId,
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var pageSize = query.GetNormalizedPageSize();
        var skip = query.GetSkip();

        // Build OData filter for user-owned playbooks
        var filter = $"_ownerid_value eq {userId}";
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            filter += $" and contains(sprk_name, '{EscapeODataString(query.NameFilter)}')";
        }
        // NOTE: OutputTypeId filter removed - output types are N:N relationship, not lookup
        // Filtering by output type would require a separate N:N query
        if (query.OutputTypeId.HasValue)
        {
            _logger.LogWarning("OutputTypeId filtering not supported - output types use N:N relationship");
        }

        return await ExecuteListQueryAsync(filter, query, pageSize, skip, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlaybookListResponse> ListPublicPlaybooksAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var pageSize = query.GetNormalizedPageSize();
        var skip = query.GetSkip();

        // Build OData filter for public playbooks
        var filter = "sprk_ispublic eq true";
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            filter += $" and contains(sprk_name, '{EscapeODataString(query.NameFilter)}')";
        }
        // NOTE: OutputTypeId filter removed - output types are N:N relationship, not lookup
        // Filtering by output type would require a separate N:N query
        if (query.OutputTypeId.HasValue)
        {
            _logger.LogWarning("OutputTypeId filtering not supported - output types use N:N relationship");
        }

        return await ExecuteListQueryAsync(filter, query, pageSize, skip, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // NFR-08 system-level allow-listed: tenant scope = "system" (see class header).
        // The cache id is the playbook name itself.
        try
        {
            var cachedPlaybook = await _cache.GetAsync<PlaybookResponse>(
                SystemTenantSentinel, CacheResource, name, CacheVersion, ct: cancellationToken);
            if (cachedPlaybook != null)
            {
                _logger.LogDebug("[PLAYBOOK] Cache HIT for playbook name '{Name}'", name);
                return cachedPlaybook;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PLAYBOOK] Cache read failed for '{Name}'", name);
        }

        _logger.LogDebug("[PLAYBOOK] Cache MISS for playbook name '{Name}', querying Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Query by name - exact match, case-insensitive per Dataverse default
        // NOTE: OutputTypeId field removed - output types are N:N relationship, not lookup
        // NOTE: sprk_istemplate removed until Dataverse schema is updated (causes 400 if column doesn't exist)
        // NOTE: sprk_jps_matching_metadata added by chat-routing-redesign-r1 task 031/036 — required by FR-10/FR-12
        // NOTE: sprk_indexstatus / sprk_indexhash / sprk_lastindexedat added by chat-routing-redesign-r1
        //       task 034 follow-up (Gap 2) — required by FR-13 drift detection and Gap 5 hash-on-index
        var select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_jps_matching_metadata,sprk_indexstatus,sprk_indexhash,sprk_lastindexedat,sprk_ispublic,_ownerid_value,createdon,modifiedon,sprk_playbookcapabilities";
        var filter = $"sprk_name eq '{EscapeODataString(name)}'";
        var url = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";

        _logger.LogInformation("[PLAYBOOK] Querying Dataverse for playbook: {Name}", name);
        _logger.LogInformation("[PLAYBOOK] BaseAddress: {BaseAddress}", _httpClient.BaseAddress);
        _logger.LogInformation("[PLAYBOOK] Relative URL: {RelativeUrl}", url);
        _logger.LogInformation("[PLAYBOOK] Full URL will be: {FullUrl}", new Uri(_httpClient.BaseAddress!, url));

        var response = await _httpClient.GetAsync(url, cancellationToken);

        _logger.LogInformation("[PLAYBOOK] Query response: StatusCode={StatusCode}, ReasonPhrase={ReasonPhrase}",
            response.StatusCode, response.ReasonPhrase);

        // Log full response body on error to diagnose Dataverse issues
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("[PLAYBOOK] Dataverse error response: {StatusCode} - {Body}", response.StatusCode, errorBody);
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        if (result?.Value == null || result.Value.Length == 0)
        {
            _logger.LogWarning("[PLAYBOOK] Playbook '{Name}' not found in Dataverse", name);
            throw PlaybookNotFoundException.ByName(name);
        }

        var entity = JsonSerializer.Deserialize<PlaybookEntity>(result.Value[0].GetRawText(), JsonOptions);
        if (entity == null)
        {
            throw PlaybookNotFoundException.ByName(name);
        }

        // Load N:N relationships
        var playbookId = entity.Id;
        _logger.LogInformation("[PLAYBOOK] Loading N:N relationships for playbook ID: {PlaybookId}", playbookId);

        var actionIds = await GetRelatedIdsAsync(playbookId, ActionRelationship, "sprk_analysisactions", "sprk_analysisactionid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} actions", actionIds.Length);

        var skillIds = await GetRelatedIdsAsync(playbookId, SkillRelationship, "sprk_analysisskills", "sprk_analysisskillid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} skills", skillIds.Length);

        var knowledgeIds = await GetRelatedIdsAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", "sprk_analysisknowledgeid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} knowledge sources", knowledgeIds.Length);

        var toolIds = await GetRelatedIdsAsync(playbookId, ToolRelationship, "sprk_analysistools", "sprk_analysistoolid", cancellationToken);
        _logger.LogInformation("[PLAYBOOK] Loaded {Count} tools", toolIds.Length);

        var playbook = new PlaybookResponse
        {
            Id = entity.Id,
            Name = entity.Name ?? string.Empty,
            Description = entity.Description,
            JpsMatchingMetadata = entity.JpsMatchingMetadata,
            IndexStatusCode = entity.IndexStatusCode ?? NotIndexedCode,
            IndexHash = entity.IndexHash,
            LastIndexedAt = entity.LastIndexedAt,
            OutputTypeId = entity.OutputTypeId,
            IsPublic = entity.IsPublic ?? false,
            IsTemplate = entity.IsTemplate ?? false,
            OwnerId = entity.OwnerId ?? Guid.Empty,
            Capabilities = ParseCapabilities(entity.Capabilities),
            ActionIds = actionIds,
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds,
            ToolIds = toolIds,
            CreatedOn = entity.CreatedOn ?? DateTime.UtcNow,
            ModifiedOn = entity.ModifiedOn ?? DateTime.UtcNow
        };

        // Cache result for 24 hours (playbooks are relatively stable).
        // NFR-08 system-level allow-listed: tenant scope = "system" (see class header).
        try
        {
            await _cache.SetAsync(
                SystemTenantSentinel, CacheResource, name, CacheVersion,
                playbook, TimeSpan.FromHours(24), ct: cancellationToken);
            _logger.LogDebug("[PLAYBOOK] Cached playbook '{Name}' for 24 hours", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PLAYBOOK] Failed to cache playbook '{Name}'", name);
        }

        _logger.LogInformation("[PLAYBOOK] Retrieved playbook '{Name}' (ID: {Id})", name, playbook.Id);
        return playbook;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PlaybookResponse> ListAllActivePlaybooksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // Tenant-wide enumeration of all Active (statecode == 0) playbooks for the
        // chat-routing-redesign-r1 FR-13 drift-detection job. Per-tenant scoping is
        // upstream via JobContract.SubjectId — this method intentionally returns all
        // statecode=0 rows visible to the BFF's Dataverse application user.
        //
        // The projection mirrors GetPlaybookAsync (FR-13 Gap 2) so drift comparison can
        // run from this single read — no per-row GetPlaybookAsync round-trip needed for
        // the hash comparison itself (N:N relationships are not required for drift
        // detection — those are loaded only when the drift job needs to MarkStale).
        const string select =
            "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_jps_matching_metadata," +
            "sprk_indexstatus,sprk_indexhash,sprk_lastindexedat," +
            "sprk_ispublic,_ownerid_value,createdon,modifiedon,sprk_playbookcapabilities";
        const string filter = "statecode eq 0";
        const int pageSize = 100;

        // Initial page URL.
        var pageUrl = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top={pageSize}";

        while (!string.IsNullOrEmpty(pageUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _httpClient.GetAsync(pageUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsMissingEntityResponse(response.StatusCode, body))
                {
                    _logger.LogWarning(
                        "[PLAYBOOK] Dataverse table '{EntitySet}' missing-entity during ListAllActivePlaybooksAsync " +
                        "({StatusCode}). Yielding zero rows.",
                        EntitySetName, response.StatusCode);
                    yield break;
                }
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
            if (result?.Value is null)
            {
                yield break;
            }

            foreach (var element in result.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entity = JsonSerializer.Deserialize<PlaybookEntity>(element.GetRawText(), JsonOptions);
                if (entity is null) continue;

                // N:N relationships are NOT loaded here — drift detection only needs the
                // fields driving the embed-input hash composition (PlaybookName, Description,
                // TriggerPhrases, RecordType, EntityType, Capabilities, JpsMatchingMetadata)
                // plus the tracking fields. Empty arrays are returned for relationship
                // collections; callers needing relationships should call GetPlaybookAsync.
                yield return new PlaybookResponse
                {
                    Id = entity.Id,
                    Name = entity.Name ?? string.Empty,
                    Description = entity.Description,
                    JpsMatchingMetadata = entity.JpsMatchingMetadata,
                    IndexStatusCode = entity.IndexStatusCode ?? NotIndexedCode,
                    IndexHash = entity.IndexHash,
                    LastIndexedAt = entity.LastIndexedAt,
                    OutputTypeId = entity.OutputTypeId,
                    IsPublic = entity.IsPublic ?? false,
                    IsTemplate = entity.IsTemplate ?? false,
                    OwnerId = entity.OwnerId ?? Guid.Empty,
                    Capabilities = ParseCapabilities(entity.Capabilities),
                    CreatedOn = entity.CreatedOn ?? DateTime.UtcNow,
                    ModifiedOn = entity.ModifiedOn ?? DateTime.UtcNow
                };
            }

            // Follow @odata.nextLink for subsequent pages. Dataverse returns absolute URLs
            // here; HttpClient.GetAsync handles either absolute or BaseAddress-relative.
            pageUrl = result.NextLink ?? string.Empty;
        }
    }

    /// <inheritdoc />
    public async Task UpdateIndexStatusAsync(
        Guid playbookId,
        int statusCode,
        string? indexHash,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // ADR-015: Log only playbook ID + status code. Never log indexHash content or
        // lastError content (lastError flows to Dataverse for admin visibility but
        // MUST NOT enter BFF logs).
        _logger.LogInformation(
            "Updating playbook index status: PlaybookId={PlaybookId}, StatusCode={StatusCode}",
            playbookId, statusCode);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_indexstatus"] = statusCode
        };

        // sprk_indexhash: only emit the field when the caller provided a non-null value.
        // JsonOptions has DefaultIgnoreCondition = WhenWritingNull (PlaybookService static
        // member), so a null value here is omitted from the PATCH body and the existing
        // Dataverse value is preserved unchanged (correct for Stale/Failed — admins want
        // to see what the hash WAS, not lose it).
        if (indexHash is not null)
        {
            payload["sprk_indexhash"] = indexHash;
        }

        // Stamp sprk_lastindexedat only on successful index completion. Stale / Failed
        // transitions leave the previous timestamp intact so admins can see when the
        // playbook was last successfully indexed.
        if (statusCode == IndexStatusIndexed)
        {
            payload["sprk_lastindexedat"] = DateTime.UtcNow;
        }

        // sprk_lastindexerror: write the truncated error message when provided (admin-visible
        // diagnostic), or clear to empty string when null (caller explicitly cleared the field
        // — e.g., successful index after a previous failure). Empty string IS serialized
        // (WhenWritingNull only suppresses literal nulls), so it does reach Dataverse and
        // clears the cell.
        payload["sprk_lastindexerror"] = lastError ?? string.Empty;

        var url = $"{EntitySetName}({playbookId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<PlaybookListResponse> ExecuteListQueryAsync(
        string filter,
        PlaybookQueryParameters query,
        int pageSize,
        int skip,
        CancellationToken cancellationToken)
    {
        // Get total count first.
        // Missing-entity tolerance (e.g., fresh dev env without `sprk_analysisplaybook` table):
        // Dataverse returns 404 or 400 with "Resource not found" / "Could not find a property
        // named" — treat as empty list rather than throwing, so chat tool detection +
        // capability resolution can proceed gracefully.
        var countUrl = $"{EntitySetName}/$count?$filter={Uri.EscapeDataString(filter)}";
        var countResponse = await _httpClient.GetAsync(countUrl, cancellationToken);

        if (!countResponse.IsSuccessStatusCode)
        {
            var countBody = await countResponse.Content.ReadAsStringAsync(cancellationToken);
            if (IsMissingEntityResponse(countResponse.StatusCode, countBody))
            {
                _logger.LogWarning(
                    "[PLAYBOOK] Dataverse table '{EntitySet}' is not provisioned in this environment " +
                    "({StatusCode}). Returning empty playbook list.",
                    EntitySetName, countResponse.StatusCode);
                return new PlaybookListResponse
                {
                    Items = [],
                    TotalCount = 0,
                    Page = query.Page,
                    PageSize = pageSize
                };
            }
            countResponse.EnsureSuccessStatusCode();
        }

        var totalCount = int.Parse(await countResponse.Content.ReadAsStringAsync(cancellationToken));

        // Build order by
        var orderBy = GetOrderByClause(query.SortBy, query.SortDescending);

        // Get paginated results
        // NOTE: OutputTypeId field removed - output types are N:N relationship, not lookup
        // NOTE: sprk_istemplate removed until Dataverse schema is updated
        var select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_ispublic,_ownerid_value,modifiedon,sprk_playbookcapabilities";
        var url = $"{EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}&$top={pageSize}&$skip={skip}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsMissingEntityResponse(response.StatusCode, body))
            {
                _logger.LogWarning(
                    "[PLAYBOOK] Dataverse query for '{EntitySet}' returned missing-entity " +
                    "response ({StatusCode}). Returning empty playbook list.",
                    EntitySetName, response.StatusCode);
                return new PlaybookListResponse
                {
                    Items = [],
                    TotalCount = 0,
                    Page = query.Page,
                    PageSize = pageSize
                };
            }
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
        var items = result?.Value?.Select(MapToPlaybookSummary).ToArray() ?? [];

        return new PlaybookListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Detects whether a non-success Dataverse response indicates the queried entity
    /// (table) does not exist in this environment. Treated as a graceful "no results"
    /// condition rather than an error so the chat pipeline and Daily Briefing can
    /// degrade gracefully when fresh environments lack schema.
    /// </summary>
    internal static bool IsMissingEntityResponse(System.Net.HttpStatusCode statusCode, string body)
    {
        if (statusCode == System.Net.HttpStatusCode.NotFound)
            return true;

        if (statusCode == System.Net.HttpStatusCode.BadRequest && !string.IsNullOrEmpty(body))
        {
            return body.Contains("Resource not found for the segment", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Could not find a property named", StringComparison.OrdinalIgnoreCase)
                || body.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static PlaybookSummary MapToPlaybookSummary(JsonElement element)
    {
        return new PlaybookSummary
        {
            Id = element.TryGetProperty("sprk_analysisplaybookid", out var idProp) ? idProp.GetGuid() : Guid.Empty,
            Name = element.TryGetProperty("sprk_name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
            Description = element.TryGetProperty("sprk_description", out var descProp) ? descProp.GetString() : null,
            // NOTE: OutputTypeId always null - output types are N:N relationship, not lookup
            OutputTypeId = null,
            IsPublic = element.TryGetProperty("sprk_ispublic", out var publicProp) && publicProp.GetBoolean(),
            IsTemplate = element.TryGetProperty("sprk_istemplate", out var templateProp) && templateProp.GetBoolean(),
            OwnerId = element.TryGetProperty("_ownerid_value", out var ownerProp) ? ownerProp.GetGuid() : Guid.Empty,
            ModifiedOn = element.TryGetProperty("modifiedon", out var modProp) ? modProp.GetDateTime() : DateTime.UtcNow
        };
    }

    private static string GetOrderByClause(string sortBy, bool descending)
    {
        // Map friendly names to Dataverse column names
        var column = sortBy.ToLowerInvariant() switch
        {
            "name" => "sprk_name",
            "createdon" => "createdon",
            "modifiedon" => "modifiedon",
            _ => "modifiedon"
        };

        return descending ? $"{column} desc" : column;
    }

    private static string EscapeODataString(string value)
    {
        // Escape single quotes for OData filter
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Parse Dataverse multi-select choice field (sprk_playbookcapabilities) into capability strings.
    ///
    /// Dataverse returns multi-select picklists as comma-separated integer option values,
    /// e.g. "100000000,100000001,100000006". This maps them to <see cref="PlaybookCapabilities"/>
    /// string constants used by the tool resolution pipeline.
    /// </summary>
    private static string[] ParseCapabilities(string? rawCapabilities)
    {
        if (string.IsNullOrWhiteSpace(rawCapabilities))
            return [];

        var capabilities = new List<string>();
        foreach (var part in rawCapabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var optionValue))
            {
                var capability = optionValue switch
                {
                    100000000 => PlaybookCapabilities.Search,
                    100000001 => PlaybookCapabilities.Analyze,
                    100000002 => PlaybookCapabilities.WriteBack,
                    100000003 => PlaybookCapabilities.Reanalyze,
                    100000004 => PlaybookCapabilities.SelectionRevise,
                    100000005 => PlaybookCapabilities.WebSearch,
                    100000006 => PlaybookCapabilities.Summarize,
                    _ => null
                };

                if (capability != null)
                    capabilities.Add(capability);
            }
        }

        return capabilities.ToArray();
    }

    /// <inheritdoc />
    public async Task<CanvasLayoutResponse?> GetCanvasLayoutAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"{EntitySetName}({playbookId})?$select=sprk_canvaslayoutjson,modifiedon";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        CanvasLayoutDto? layout = null;
        if (root.TryGetProperty("sprk_canvaslayoutjson", out var layoutProp) &&
            layoutProp.ValueKind != JsonValueKind.Null)
        {
            var layoutJson = layoutProp.GetString();
            if (!string.IsNullOrEmpty(layoutJson))
            {
                try
                {
                    layout = JsonSerializer.Deserialize<CanvasLayoutDto>(layoutJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize canvas layout for playbook {PlaybookId}", playbookId);
                }
            }
        }

        DateTime? modifiedOn = null;
        if (root.TryGetProperty("modifiedon", out var modProp) &&
            modProp.ValueKind != JsonValueKind.Null)
        {
            modifiedOn = modProp.GetDateTime();
        }

        return new CanvasLayoutResponse
        {
            PlaybookId = playbookId,
            Layout = layout,
            ModifiedOn = modifiedOn
        };
    }

    /// <inheritdoc />
    public async Task<CanvasLayoutResponse> SaveCanvasLayoutAsync(
        Guid playbookId,
        CanvasLayoutDto layout,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Saving canvas layout for playbook {PlaybookId}", playbookId);

        // Serialize the layout to JSON
        var layoutJson = JsonSerializer.Serialize(layout, JsonOptions);

        // Update the playbook's canvas layout field
        var payload = new Dictionary<string, object?>
        {
            ["sprk_canvaslayoutjson"] = layoutJson
        };

        var url = $"{EntitySetName}({playbookId})";
        var response = await _httpClient.PatchAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Saved canvas layout for playbook {PlaybookId}", playbookId);

        // Return the saved layout
        return new CanvasLayoutResponse
        {
            PlaybookId = playbookId,
            Layout = layout,
            ModifiedOn = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<PlaybookListResponse> ListTemplatesAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        // NOTE: sprk_istemplate column doesn't exist in Dataverse yet
        // Return empty list until schema is updated to add the column
        // Original filter was: sprk_istemplate eq true
        _logger.LogWarning("ListTemplatesAsync called but sprk_istemplate column not in Dataverse schema - returning empty list");

        await Task.CompletedTask; // Suppress async warning

        return new PlaybookListResponse
        {
            Items = [],
            TotalCount = 0,
            Page = query.Page,
            PageSize = query.GetNormalizedPageSize()
        };
    }

    /// <inheritdoc />
    public async Task<PlaybookResponse> ClonePlaybookAsync(
        Guid sourcePlaybookId,
        Guid userId,
        string? newName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Cloning playbook {SourceId} for user {UserId}", sourcePlaybookId, userId);

        // Get the source playbook with all relationships
        var source = await GetPlaybookAsync(sourcePlaybookId, cancellationToken)
            ?? throw PlaybookNotFoundException.ById(sourcePlaybookId);

        // Generate name for clone
        var cloneName = newName ?? $"{source.Name} (Copy)";

        // Get canvas layout from source
        var canvasLayout = await GetCanvasLayoutAsync(sourcePlaybookId, cancellationToken);

        // Create the clone request
        var cloneRequest = new SavePlaybookRequest
        {
            Name = cloneName,
            Description = source.Description,
            OutputTypeId = source.OutputTypeId,
            IsPublic = false, // Clones are private by default
            IsTemplate = false, // Clones are not templates
            ActionIds = source.ActionIds,
            SkillIds = source.SkillIds,
            KnowledgeIds = source.KnowledgeIds,
            ToolIds = source.ToolIds
        };

        // Create the new playbook
        var clonedPlaybook = await CreatePlaybookAsync(cloneRequest, userId, cancellationToken);

        _logger.LogInformation("Created clone {CloneId} from source {SourceId}", clonedPlaybook.Id, sourcePlaybookId);

        // Copy canvas layout if present
        if (canvasLayout?.Layout != null)
        {
            await SaveCanvasLayoutAsync(clonedPlaybook.Id, canvasLayout.Layout, cancellationToken);
            _logger.LogInformation("Copied canvas layout to clone {CloneId}", clonedPlaybook.Id);
        }

        // Return the cloned playbook with updated data
        return await GetPlaybookAsync(clonedPlaybook.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve cloned playbook");
    }

    #region Private Helper Methods

    private async Task AssociateRelationshipsAsync(
        Guid playbookId,
        SavePlaybookRequest request,
        CancellationToken cancellationToken)
    {
        // Associate actions
        if (request.ActionIds?.Length > 0)
        {
            foreach (var actionId in request.ActionIds)
            {
                await AssociateAsync(playbookId, ActionRelationship, "sprk_analysisactions", actionId, cancellationToken);
            }
        }

        // Associate skills
        if (request.SkillIds?.Length > 0)
        {
            foreach (var skillId in request.SkillIds)
            {
                await AssociateAsync(playbookId, SkillRelationship, "sprk_analysisskills", skillId, cancellationToken);
            }
        }

        // Associate knowledge
        if (request.KnowledgeIds?.Length > 0)
        {
            foreach (var knowledgeId in request.KnowledgeIds)
            {
                await AssociateAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", knowledgeId, cancellationToken);
            }
        }

        // Associate tools
        if (request.ToolIds?.Length > 0)
        {
            foreach (var toolId in request.ToolIds)
            {
                await AssociateAsync(playbookId, ToolRelationship, "sprk_analysistools", toolId, cancellationToken);
            }
        }
    }

    private async Task AssociateAsync(
        Guid playbookId,
        string relationshipName,
        string targetEntitySet,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var url = $"{EntitySetName}({playbookId})/{relationshipName}/$ref";
        var payload = new Dictionary<string, string>
        {
            ["@odata.id"] = $"{_apiUrl}/{targetEntitySet}({targetId})"
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogDebug("Associated {Relationship}: {PlaybookId} -> {TargetId}", relationshipName, playbookId, targetId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Association may already exist - log and continue
            _logger.LogWarning("Association may already exist: {Relationship} {PlaybookId} -> {TargetId}", relationshipName, playbookId, targetId);
        }
    }

    private async Task ClearRelationshipsAsync(Guid playbookId, CancellationToken cancellationToken)
    {
        // Clear all N:N relationships for the playbook
        await ClearRelationshipAsync(playbookId, ActionRelationship, "sprk_analysisactions", "sprk_analysisactionid", cancellationToken);
        await ClearRelationshipAsync(playbookId, SkillRelationship, "sprk_analysisskills", "sprk_analysisskillid", cancellationToken);
        await ClearRelationshipAsync(playbookId, KnowledgeRelationship, "sprk_analysisknowledges", "sprk_analysisknowledgeid", cancellationToken);
        await ClearRelationshipAsync(playbookId, ToolRelationship, "sprk_analysistools", "sprk_analysistoolid", cancellationToken);
    }

    private async Task ClearRelationshipAsync(
        Guid playbookId,
        string relationshipName,
        string targetEntitySet,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        // Get existing related IDs
        var existingIds = await GetRelatedIdsAsync(playbookId, relationshipName, targetEntitySet, targetIdField, cancellationToken);

        // Disassociate each
        foreach (var targetId in existingIds)
        {
            var url = $"{EntitySetName}({playbookId})/{relationshipName}({targetId})/$ref";
            try
            {
                var response = await _httpClient.DeleteAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to disassociate {Relationship}: {PlaybookId} -> {TargetId}", relationshipName, playbookId, targetId);
            }
        }
    }

    private async Task<Guid[]> GetRelatedIdsAsync(
        Guid playbookId,
        string relationshipName,
        string targetEntitySet,
        string targetIdField,
        CancellationToken cancellationToken)
    {
        var url = $"{EntitySetName}({playbookId})/{relationshipName}?$select={targetIdField}";

        _logger.LogInformation(
            "[N:N QUERY] Querying relationship '{Relationship}' for playbook {PlaybookId}",
            relationshipName, playbookId);
        _logger.LogInformation("[N:N QUERY] Full URL: {Url}", url);
        _logger.LogInformation("[N:N QUERY] Target field: {Field}", targetIdField);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            _logger.LogInformation(
                "[N:N QUERY] Response status: {StatusCode} - {ReasonPhrase}",
                response.StatusCode, response.ReasonPhrase);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "[N:N QUERY] Failed with status {StatusCode}. Response body: {Body}",
                    response.StatusCode, errorBody);
                return [];
            }

            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[N:N QUERY] Raw JSON response: {Json}", rawJson);

            var result = System.Text.Json.JsonSerializer.Deserialize<ODataCollectionResponse>(rawJson, JsonOptions);
            if (result?.Value == null)
            {
                _logger.LogWarning("[N:N QUERY] Deserialized result is null or has no value array");
                return [];
            }

            _logger.LogInformation("[N:N QUERY] Found {Count} items in response", result.Value.Length);

            var ids = new List<Guid>();
            for (int i = 0; i < result.Value.Length; i++)
            {
                var item = result.Value[i];
                _logger.LogInformation("[N:N QUERY] Item {Index} raw JSON: {ItemJson}", i, item.GetRawText());

                if (item.TryGetProperty(targetIdField, out var idElement))
                {
                    var guid = idElement.GetGuid();
                    _logger.LogInformation("[N:N QUERY] Item {Index} - extracted {Field} = {Guid}", i, targetIdField, guid);
                    ids.Add(guid);
                }
                else
                {
                    _logger.LogWarning(
                        "[N:N QUERY] Item {Index} - property '{Field}' not found. Available properties: {Properties}",
                        i, targetIdField, string.Join(", ", item.EnumerateObject().Select(p => p.Name)));
                }
            }

            var validIds = ids.Where(id => id != Guid.Empty).ToArray();
            _logger.LogInformation(
                "[N:N QUERY] Returning {Count} valid GUIDs for relationship '{Relationship}'",
                validIds.Length, relationshipName);

            return validIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[N:N QUERY] Exception querying relationship '{Relationship}': {Message}", relationshipName, ex.Message);
            return [];
        }
    }

    #endregion

    #region Internal Types

    private class PlaybookEntity
    {
        [JsonPropertyName("sprk_analysisplaybookid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        /// <summary>
        /// Raw JSON content from the Dataverse <c>sprk_jps_matching_metadata</c> Memo column
        /// (chat-routing-redesign-r1 task 031). Consumed by the embedding composer (FR-10) and
        /// the indexing validation gate (FR-12).
        /// </summary>
        [JsonPropertyName("sprk_jps_matching_metadata")]
        public string? JpsMatchingMetadata { get; set; }

        /// <summary>
        /// Numeric option-set value for <c>sprk_indexstatus</c> (chat-routing-redesign-r1
        /// FR-13). Null when the playbook has never been indexed (mapped to NotIndexedCode).
        /// </summary>
        [JsonPropertyName("sprk_indexstatus")]
        public int? IndexStatusCode { get; set; }

        /// <summary>
        /// SHA-256 hex digest of the canonical embed-input string at last successful index
        /// time (chat-routing-redesign-r1 FR-13). Null until the playbook has been indexed.
        /// </summary>
        [JsonPropertyName("sprk_indexhash")]
        public string? IndexHash { get; set; }

        /// <summary>
        /// UTC timestamp of the most recent successful index operation (chat-routing-redesign-r1
        /// FR-13). Null until the playbook has been indexed.
        /// </summary>
        [JsonPropertyName("sprk_lastindexedat")]
        public DateTime? LastIndexedAt { get; set; }

        [JsonPropertyName("sprk_ispublic")]
        public bool? IsPublic { get; set; }

        [JsonPropertyName("sprk_istemplate")]
        public bool? IsTemplate { get; set; }

        [JsonPropertyName("_sprk_outputtypeid_value")]
        public Guid? OutputTypeId { get; set; }

        [JsonPropertyName("_ownerid_value")]
        public Guid? OwnerId { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }

        [JsonPropertyName("modifiedon")]
        public DateTime? ModifiedOn { get; set; }

        /// <summary>
        /// Multi-select choice field (sprk_playbookcapabilities global option set).
        /// Dataverse Web API returns multi-select picklists as a comma-separated string
        /// of integer option values, e.g. "100000000,100000001,100000006".
        /// </summary>
        [JsonPropertyName("sprk_playbookcapabilities")]
        public string? Capabilities { get; set; }
    }

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public JsonElement[]? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    #endregion
}
