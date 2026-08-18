using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse service implementation using Web API (REST) instead of ServiceClient.
/// Provides full compatibility without WCF/System.ServiceModel dependencies, using
/// IHttpClientFactory for proper HttpClient management.
/// </summary>
/// <remarks>
/// <para><b>Runtime surface (RED-4 hardening, 2026-08-16):</b> this class serves ONLY the narrow
/// interfaces <see cref="IEventDataverseService"/> and <see cref="IFieldMappingDataverseService"/>
/// (wired in <c>GraphModule.cs</c>), plus the concrete-injected impersonation/POA methods
/// (<c>RetrieveMultipleImpersonatedAsync</c>, <c>GrantAccessAsync</c>, <c>GetSharedSystemUserIdsAsync</c>)
/// reached via the <c>IImpersonatedCommunicationQuery</c> / <c>IDataverseAccessGrantService</c> seams in
/// <c>CommunicationModule.cs</c>. Document / analysis / generic-entity / processing-job / KPI /
/// communication-query / health capability all resolve to <see cref="DataverseServiceClientImpl"/>
/// (SDK) — the former implementations of those surfaces here were runtime-dead and were removed.
/// See <c>docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md</c>.</para>
/// </remarks>
public class DataverseWebApiService : IEventDataverseService, IFieldMappingDataverseService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseWebApiService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    public DataverseWebApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseWebApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        // ADR-028 §24 (#3b): prefer Managed Identity when enabled; ClientSecret is the local-dev fallback.
        // The secret path is retained (do NOT remove Dataverse:ClientSecret) until MI attribution is proven live.
        var useManagedIdentity = string.Equals(
            configuration["Graph:ManagedIdentity:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

        if (useManagedIdentity)
        {
            var miClientId = configuration["ManagedIdentity:ClientId"]
                ?? configuration["Graph:ManagedIdentity:ClientId"];
            var options = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrEmpty(miClientId))
                options.ManagedIdentityClientId = miClientId;
            _credential = new DefaultAzureCredential(options);
            _logger.LogInformation(
                "DataverseWebApiService using Managed Identity (ADR-028; clientId {ClientId}) for {ApiUrl}",
                miClientId ?? "(system-assigned)", _apiUrl);
        }
        else
        {
            var tenantId = configuration["TENANT_ID"];
            var clientId = configuration["API_APP_ID"];
            var clientSecret = configuration["Dataverse:ClientSecret"];

            if (string.IsNullOrEmpty(tenantId))
                throw new InvalidOperationException("TENANT_ID configuration is required (Managed Identity disabled)");
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("API_APP_ID configuration is required (Managed Identity disabled)");
            if (string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("Dataverse:ClientSecret configuration is required (Managed Identity disabled)");

            _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _logger.LogInformation(
                "DataverseWebApiService using ClientSecret credential (local-dev fallback) for {ApiUrl}", _apiUrl);
        }

        // BaseAddress MUST end with a trailing slash: with a relative request URI (e.g.
        // "sprk_recordtype_refs?..."), .NET's RFC-3986 resolution drops the last path segment
        // of a slash-less base — turning "/api/data/v9.2" into "/api/data/sprk_recordtype_refs"
        // (version dropped), which Dataverse answers with HTTP 500. (_apiUrl stays slash-less
        // for the scope derivation below.)
        _httpClient.BaseAddress = new Uri(_apiUrl + "/");
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Initialized Dataverse Web API service for {ApiUrl}", _apiUrl);
    }

    /// <summary>
    /// Thread-safe token refresh using SemaphoreSlim with double-check locking.
    /// Returns the current valid token for use in per-request Authorization headers.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: token is still valid (no lock needed)
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _currentToken.Value.Token;
        }

        // Slow path: acquire semaphore for token refresh (30s timeout to prevent deadlocks)
        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            throw new TimeoutException("Timed out waiting for Dataverse token refresh");
        }

        try
        {
            // Double-check: another thread may have refreshed while we waited
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _currentToken.Value.Token;
            }

            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                cancellationToken);

            _logger.LogDebug("Refreshed Dataverse access token");
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    /// <summary>
    /// Creates an HttpRequestMessage with per-request Authorization and standard OData headers.
    /// This avoids mutating shared DefaultRequestHeaders on the HttpClient.
    /// </summary>
    /// <param name="impersonateSystemUserId">
    /// OPTIONAL Dataverse <c>systemuserid</c> to impersonate for this request (adds the <c>MSCRMCallerID</c>
    /// header via <see cref="DataverseImpersonation"/>). <c>null</c>/<see cref="Guid.Empty"/> (the default) leaves
    /// the request app-only and byte-unchanged — existing consumers pass nothing and are unaffected. Added for the
    /// messaging read path (messaging-communication-app-r1), where Dataverse does row-level filtering natively.
    /// </param>
    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(
        HttpMethod method, string url, CancellationToken cancellationToken = default, Guid? impersonateSystemUserId = null)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        DataverseImpersonation.Apply(request, impersonateSystemUserId);
        return request;
    }

    /// <summary>
    /// Sends a GET request with per-request auth headers. When <paramref name="impersonateSystemUserId"/> is a real
    /// user id the request runs AS that Dataverse user (MSCRMCallerID impersonation); otherwise it is app-only.
    /// </summary>
    private async Task<HttpResponseMessage> SendGetAsync(string url, CancellationToken ct = default, Guid? impersonateSystemUserId = null)
    {
        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct, impersonateSystemUserId);
        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Sends a POST request with JSON body and per-request auth headers.
    /// </summary>
    private async Task<HttpResponseMessage> SendPostAsJsonAsync<T>(string url, T payload, CancellationToken ct = default)
    {
        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url, ct);
        request.Content = JsonContent.Create(payload);
        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Sends a PATCH request with JSON body and per-request auth headers. When
    /// <paramref name="impersonateSystemUserId"/> is a real Dataverse <c>systemuserid</c>, the PATCH runs AS that
    /// user (<c>MSCRMCallerID</c> impersonation — effective privileges = intersection of app user + impersonated
    /// user, honest <c>modifiedby</c>); null/empty = app-only (existing callers byte-unchanged). This is the
    /// write-plane counterpart of the impersonated read (<see cref="RetrieveMultipleImpersonatedAsync"/>), added
    /// for the Job B apply path (task 031).
    /// </summary>
    private async Task<HttpResponseMessage> SendPatchAsJsonAsync<T>(string url, T payload, CancellationToken ct = default, Guid? impersonateSystemUserId = null)
    {
        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Patch, url, ct, impersonateSystemUserId);
        request.Content = JsonContent.Create(payload);
        return await _httpClient.SendAsync(request, ct);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _entitySetNameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves an entity's Dataverse <c>EntitySetName</c> — the plural collection name used in Web API URLs
    /// (e.g. <c>sprk_fieldmappingprofile</c> → <c>sprk_fieldmappingprofiles</c>) — via the
    /// <c>EntityDefinitions</c> metadata endpoint. Cached in-memory per logical name (entity set names are
    /// immutable for the lifetime of the connected environment). Metadata is environment-scoped, so no
    /// impersonation is applied here; the calling read/write carries impersonation separately.
    /// </summary>
    /// <remarks>
    /// DEF-2 fix (RED-4 B, 2026-08-16): this was previously a <c>throw new NotImplementedException</c> stub,
    /// which broke every field-mapping read / child-query / write routed to this impl —
    /// <see cref="RetrieveRecordFieldsAsync"/>, <see cref="QueryChildRecordIdsAsync"/> and
    /// <c>UpdateRecordFieldsAsync</c> all call it as their first operation (surfaced as an HTTP 500 in the
    /// compose cold-session UAT). Now implemented, mirroring <see cref="GetEntityObjectTypeCodeAsync"/>. It
    /// fails LOUD (throws <see cref="InvalidOperationException"/>) on a metadata error or missing set name —
    /// this is a write-path URL dependency, so a silent empty would corrupt the request URL.
    /// </remarks>
    public async Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default)
    {
        if (_entitySetNameCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var url = $"EntityDefinitions(LogicalName='{entityLogicalName}')?$select=EntitySetName";
        var response = await SendGetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "GetEntitySetNameAsync failed for '{Entity}': {StatusCode}", entityLogicalName, response.StatusCode);
            throw new InvalidOperationException(
                $"Failed to query EntitySetName metadata for entity '{entityLogicalName}': " +
                $"HTTP {(int)response.StatusCode} {response.StatusCode}.");
        }

        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
        var entitySetName = data != null
            && data.TryGetValue("EntitySetName", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()
            : null;

        if (string.IsNullOrEmpty(entitySetName))
            throw new InvalidOperationException(
                $"Entity '{entityLogicalName}' has no EntitySetName in Dataverse metadata " +
                "(entity not found or not queryable via the Web API metadata endpoint).");

        _entitySetNameCache[entityLogicalName] = entitySetName;
        return entitySetName;
    }

    public async Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
        int? regardingRecordType = null,
        string? regardingRecordId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        Guid? ownerUserId = null,
        CancellationToken ct = default)
    {
        // Build OData query
        var filters = new List<string>();

        // Owner filter — scopes results to a specific user (used by Copilot integration)
        if (ownerUserId.HasValue)
            filters.Add($"_ownerid_value eq {ownerUserId.Value}");

        if (regardingRecordType.HasValue)
            filters.Add($"sprk_regardingrecordtype eq {regardingRecordType.Value}");

        if (!string.IsNullOrEmpty(regardingRecordId))
            filters.Add($"sprk_regardingrecordid eq '{regardingRecordId}'");

        if (eventTypeId.HasValue)
            filters.Add($"_sprk_eventtype_ref_value eq {eventTypeId.Value}");

        if (statusCode.HasValue)
            filters.Add($"statuscode eq {statusCode.Value}");

        if (priority.HasValue)
            filters.Add($"sprk_priority eq {priority.Value}");

        if (dueDateFrom.HasValue)
            filters.Add($"sprk_duedate ge {dueDateFrom.Value:yyyy-MM-dd}");

        if (dueDateTo.HasValue)
            filters.Add($"sprk_duedate le {dueDateTo.Value:yyyy-MM-dd}");

        var filterQuery = filters.Count > 0 ? $"$filter={string.Join(" and ", filters)}&" : "";
        var url = $"sprk_events?{filterQuery}$select=sprk_eventid,sprk_eventname,sprk_description,_sprk_eventtype_ref_value,sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordtype,sprk_basedate,sprk_duedate,sprk_completeddate,statecode,statuscode,sprk_priority,sprk_source,createdon,modifiedon&$expand=sprk_eventtype_ref($select=sprk_name)&$orderby=sprk_duedate asc,createdon desc&$skip={skip}&$top={top}&$count=true";

        _logger.LogDebug("Querying events: {Url}", url);

        try
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
            request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCountResponse>(cancellationToken: ct);
            if (data == null)
                return (Array.Empty<EventEntity>(), 0);

            var events = data.Value.Select(MapToEventEntity).ToArray();
            return (events, data.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying events");
            throw;
        }
    }

    public async Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default)
    {

        var url = $"sprk_events({id})?$select=sprk_eventid,sprk_eventname,sprk_description,_sprk_eventtype_ref_value,sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordtype,_sprk_regardingaccount_value,_sprk_regardinganalysis_value,_sprk_regardingcontact_value,_sprk_regardinginvoice_value,_sprk_regardingmatter_value,_sprk_regardingproject_value,_sprk_regardingbudget_value,_sprk_regardingworkassignment_value,sprk_basedate,sprk_duedate,sprk_completeddate,statecode,statuscode,sprk_priority,sprk_source,sprk_remindat,_sprk_relatedevent_value,sprk_relatedeventtype,sprk_relatedeventoffsettype,createdon,modifiedon&$expand=sprk_eventtype_ref($select=sprk_name)";

        _logger.LogDebug("Getting event: {Id}", id);

        try
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
            request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

            var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Event not found: {Id}", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null) return null;

            return MapToEventEntity(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event {Id}", id);
            throw;
        }
    }

    public async Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default)
    {

        var payload = new Dictionary<string, object?>
        {
            ["sprk_eventname"] = request.Name,
            ["sprk_description"] = request.Description,
            ["statuscode"] = 3, // Open
            ["statecode"] = 0,  // Active
            ["sprk_source"] = 0 // User
        };

        if (request.EventTypeId.HasValue)
            payload["sprk_EventType_Ref@odata.bind"] = $"/sprk_eventtype_refs({request.EventTypeId.Value})"; // R5 002: nav prop sprk_EventType_Ref + correct collection sprk_eventtype_refs (metadata-verified; sprk_eventtypes does not exist)

        if (request.BaseDate.HasValue)
            payload["sprk_basedate"] = request.BaseDate.Value.ToString("yyyy-MM-dd");

        if (request.DueDate.HasValue)
            payload["sprk_duedate"] = request.DueDate.Value.ToString("yyyy-MM-dd");

        if (request.Priority.HasValue)
            payload["sprk_priority"] = request.Priority.Value;

        if (request.RegardingRecordType.HasValue)
        {
            payload["sprk_regardingrecordtype"] = request.RegardingRecordType.Value;
            payload["sprk_regardingrecordid"] = request.RegardingRecordId;
            payload["sprk_regardingrecordname"] = request.RegardingRecordName;

            // Set entity-specific lookup based on record type
            var lookupField = RegardingRecordType.GetLookupFieldName(request.RegardingRecordType.Value);
            var entityName = RegardingRecordType.GetEntityLogicalName(request.RegardingRecordType.Value);
            if (lookupField != null && entityName != null && !string.IsNullOrEmpty(request.RegardingRecordId))
            {
                payload[$"{lookupField}@odata.bind"] = $"/{entityName}s({request.RegardingRecordId})";
            }
        }

        _logger.LogInformation("Creating event: {Name}", request.Name);

        var response = await SendPostAsJsonAsync("sprk_events", payload, ct);
        response.EnsureSuccessStatusCode();

        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader != null)
        {
            var idString = entityIdHeader.Split('(', ')')[1];
            var id = Guid.Parse(idString);
            var createdOn = DateTime.UtcNow;

            _logger.LogInformation("Event created: {Id}", id);
            return (id, createdOn);
        }

        throw new InvalidOperationException("Failed to extract entity ID from create response");
    }

    public async Task UpdateEventAsync(Guid id, UpdateEventRequest request, CancellationToken ct = default)
    {

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_eventname"] = request.Name;

        if (request.Description != null)
            payload["sprk_description"] = request.Description;

        if (request.EventTypeId.HasValue)
            payload["sprk_EventType_Ref@odata.bind"] = $"/sprk_eventtype_refs({request.EventTypeId.Value})"; // R5 002: nav prop sprk_EventType_Ref + correct collection sprk_eventtype_refs (metadata-verified; sprk_eventtypes does not exist)

        if (request.BaseDate.HasValue)
            payload["sprk_basedate"] = request.BaseDate.Value.ToString("yyyy-MM-dd");

        if (request.DueDate.HasValue)
            payload["sprk_duedate"] = request.DueDate.Value.ToString("yyyy-MM-dd");

        if (request.Priority.HasValue)
            payload["sprk_priority"] = request.Priority.Value;

        if (request.StatusCode.HasValue)
            payload["statuscode"] = request.StatusCode.Value;

        if (request.RegardingRecordType.HasValue)
        {
            payload["sprk_regardingrecordtype"] = request.RegardingRecordType.Value;
            payload["sprk_regardingrecordid"] = request.RegardingRecordId;
            payload["sprk_regardingrecordname"] = request.RegardingRecordName;
        }

        if (payload.Count == 0)
        {
            _logger.LogDebug("No fields to update for event {Id}", id);
            return;
        }

        _logger.LogInformation("Updating event: {Id}", id);

        var response = await SendPatchAsJsonAsync($"sprk_events({id})", payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Event updated: {Id}", id);
    }

    public async Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default)
    {

        var payload = new Dictionary<string, object?>
        {
            ["statuscode"] = statusCode
        };

        // Set statecode based on statuscode
        // Draft(1), Planned(2), Open(3), OnHold(4) = Active(0)
        // Completed(5), Cancelled(6), Deleted(7) = Inactive(1)
        payload["statecode"] = statusCode >= 5 ? 1 : 0;

        if (completedDate.HasValue)
            payload["sprk_completeddate"] = completedDate.Value.ToString("yyyy-MM-dd");

        _logger.LogInformation("Updating event status: {Id} -> {StatusCode}", id, statusCode);

        var response = await SendPatchAsJsonAsync($"sprk_events({id})", payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Event status updated: {Id}", id);
    }

    public async Task<EventLogEntity[]> QueryEventLogsAsync(Guid eventId, CancellationToken ct = default)
    {

        var url = $"sprk_eventlogs?$filter=_sprk_event_value eq {eventId}&$select=sprk_eventlogid,sprk_eventlogname,_sprk_event_value,sprk_action,sprk_description,createdon,_createdby_value&$orderby=createdon desc";

        _logger.LogDebug("Querying event logs for event: {EventId}", eventId);

        try
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
            request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null)
                return Array.Empty<EventLogEntity>();

            return data.Value.Select(MapToEventLogEntity).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying event logs for event {EventId}", eventId);
            throw;
        }
    }

    public async Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, CancellationToken ct = default)
    {

        var logName = $"Event Log - {EventLogAction.GetDisplayName(action)} - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";

        var payload = new Dictionary<string, object?>
        {
            ["sprk_eventlogname"] = logName,
            ["sprk_Event@odata.bind"] = $"/sprk_events({eventId})", // R5 002: PascalCase nav prop (metadata-verified)
            ["sprk_action"] = action,
            ["sprk_description"] = description
        };

        _logger.LogInformation("Creating event log for event {EventId}: {Action}", eventId, EventLogAction.GetDisplayName(action));

        var response = await SendPostAsJsonAsync("sprk_eventlogs", payload, ct);
        response.EnsureSuccessStatusCode();

        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader != null)
        {
            var idString = entityIdHeader.Split('(', ')')[1];
            var id = Guid.Parse(idString);

            _logger.LogDebug("Event log created: {Id}", id);
            return id;
        }

        throw new InvalidOperationException("Failed to extract entity ID from create response");
    }

    public async Task<EventTypeEntity[]> GetEventTypesAsync(bool activeOnly = true, CancellationToken ct = default)
    {

        var filterQuery = activeOnly ? "$filter=statecode eq 0&" : "";
        var url = $"sprk_eventtypes?{filterQuery}$select=sprk_eventtypeid,sprk_name,sprk_eventcode,sprk_description,statecode,sprk_requiresduedate,sprk_requiresbasedate&$orderby=sprk_name asc";

        _logger.LogDebug("Getting event types (activeOnly={ActiveOnly})", activeOnly);

        try
        {
            var response = await SendGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null)
                return Array.Empty<EventTypeEntity>();

            return data.Value.Select(MapToEventTypeEntity).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event types");
            throw;
        }
    }

    public async Task<EventTypeEntity?> GetEventTypeAsync(Guid id, CancellationToken ct = default)
    {

        var url = $"sprk_eventtypes({id})?$select=sprk_eventtypeid,sprk_name,sprk_eventcode,sprk_description,statecode,sprk_requiresduedate,sprk_requiresbasedate";

        _logger.LogDebug("Getting event type: {Id}", id);

        try
        {
            var response = await SendGetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Event type not found: {Id}", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null) return null;

            return MapToEventTypeEntity(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event type {Id}", id);
            throw;
        }
    }

    // ========================================
    // Field Mapping Operations (Events and Workflow Automation R1)
    // ========================================
    //
    // SRFR-056 (2026-07-08): sprk_fieldmappingprofile has NEVER had sprk_sourceentity,
    // sprk_targetentity, or sprk_isactive columns. The actual schema uses lookups to
    // sprk_recordtype_ref catalog + statecode:
    //
    //   sprk_sourcerecordtype  LOOKUP → sprk_recordtype_ref  (raw: _sprk_sourcerecordtype_value)
    //   sprk_targetrecordtype  LOOKUP → sprk_recordtype_ref  (raw: _sprk_targetrecordtype_value)
    //   statecode              STATE   (0=Active, 1=Inactive)
    //
    // sprk_recordtype_ref maps GUIDs to Dataverse entity logical names via sprk_recordlogicalname.
    // Profile queries use a two-step lookup: (1) resolve logical names -> GUIDs, (2) filter profile
    // by the resolved GUIDs. The prior code queried non-existent columns and returned 500 in prod
    // once auth (SRFR-053) started allowing requests through.

    /// <summary>
    /// Two-step helper: resolves entity logical names to sprk_recordtype_ref GUIDs.
    /// </summary>
    /// <param name="logicalNames">Distinct entity logical names to look up (e.g. "sprk_matter").</param>
    /// <returns>Dictionary from logical name to record-type-ref GUID. Names not found are omitted.</returns>
    private async Task<Dictionary<string, Guid>> LookupRecordTypeIdsAsync(
        IEnumerable<string> logicalNames,
        CancellationToken ct = default)
    {
        var distinctNames = logicalNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctNames.Length == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Build OData filter: sprk_recordlogicalname eq 'x' or sprk_recordlogicalname eq 'y' ...
        // Each value is single-quote-wrapped (escape internal apostrophes per OData rules).
        var orClauses = string.Join(
            " or ",
            distinctNames.Select(n => $"sprk_recordlogicalname eq '{n.Replace("'", "''")}'"));

        var url =
            $"sprk_recordtype_refs?" +
            $"$select=sprk_recordtype_refid,sprk_recordlogicalname&" +
            $"$filter={orClauses}";

        var response = await SendGetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (data == null) return map;

        foreach (var row in data.Value)
        {
            if (!row.TryGetValue("sprk_recordlogicalname", out var nameEl) || nameEl.ValueKind == JsonValueKind.Null)
                continue;
            if (!row.TryGetValue("sprk_recordtype_refid", out var idEl) || idEl.ValueKind == JsonValueKind.Null)
                continue;

            var name = nameEl.GetString();
            var idStr = idEl.GetString();
            if (name is null || idStr is null || !Guid.TryParse(idStr, out var id))
                continue;

            map[name] = id;
        }

        return map;
    }

    public async Task<FieldMappingProfileEntity[]> QueryFieldMappingProfilesAsync(CancellationToken ct = default)
    {
        // Return all active profiles. This method has no source/target filter; the caller
        // (GetProfilesAsync in FieldMappingEndpoints) applies client-side filtering.
        // We must resolve source/target lookup GUIDs back to logical names so the DTO
        // SourceEntity/TargetEntity fields are populated (client-side filter depends on this).
        var url =
            "sprk_fieldmappingprofiles?" +
            "$filter=statecode eq 0&" +
            "$select=sprk_fieldmappingprofileid,sprk_name,_sprk_sourcerecordtype_value,_sprk_targetrecordtype_value,sprk_capabilitymode,sprk_defaultvalue,sprk_description,statecode&" +
            "$orderby=sprk_name asc";

        _logger.LogDebug("Querying field mapping profiles (statecode=0)");

        try
        {
            var response = await SendGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null || data.Value.Count == 0)
                return Array.Empty<FieldMappingProfileEntity>();

            // Collect all referenced record-type-ref GUIDs across profiles, then reverse-lookup.
            var refIds = new HashSet<Guid>();
            foreach (var row in data.Value)
            {
                if (row.TryGetValue("_sprk_sourcerecordtype_value", out var s) && s.ValueKind != JsonValueKind.Null &&
                    Guid.TryParse(s.GetString(), out var sId))
                    refIds.Add(sId);
                if (row.TryGetValue("_sprk_targetrecordtype_value", out var t) && t.ValueKind != JsonValueKind.Null &&
                    Guid.TryParse(t.GetString(), out var tId))
                    refIds.Add(tId);
            }

            var idToName = await GetRecordTypeNamesByIdsAsync(refIds, ct);

            return data.Value
                .Select(row => MapToFieldMappingProfileEntity(row, idToName))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying field mapping profiles");
            throw;
        }
    }

    public async Task<FieldMappingProfileEntity?> GetFieldMappingProfileAsync(
        string sourceEntity,
        string targetEntity,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Getting field mapping profile: {Source} -> {Target}", sourceEntity, targetEntity);

        try
        {
            // Step 1: resolve source + target logical names -> record-type-ref GUIDs.
            var idMap = await LookupRecordTypeIdsAsync(new[] { sourceEntity, targetEntity }, ct);
            if (!idMap.TryGetValue(sourceEntity, out var sourceRefId) ||
                !idMap.TryGetValue(targetEntity, out var targetRefId))
            {
                _logger.LogDebug(
                    "Record-type-ref catalog entry not found for source={Source} or target={Target}; no profile can match",
                    sourceEntity, targetEntity);
                return null;
            }

            // Step 2: query the profile filtered by resolved lookup GUIDs.
            var url =
                $"sprk_fieldmappingprofiles?" +
                $"$filter=_sprk_sourcerecordtype_value eq {sourceRefId} and _sprk_targetrecordtype_value eq {targetRefId} and statecode eq 0&" +
                $"$select=sprk_fieldmappingprofileid,sprk_name,_sprk_sourcerecordtype_value,_sprk_targetrecordtype_value,sprk_capabilitymode,sprk_defaultvalue,sprk_description,statecode";

            var response = await SendGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null || data.Value.Count == 0)
                return null;

            // We already have the two names; construct the id->name map so the mapper populates them.
            var idToName = new Dictionary<Guid, string>
            {
                [sourceRefId] = sourceEntity,
                [targetRefId] = targetEntity
            };

            var profile = MapToFieldMappingProfileEntity(data.Value[0], idToName);

            // Load rules for this profile (separate 1:N call).
            profile.Rules = (await GetFieldMappingRulesAsync(profile.Id, true, ct)).ToList();

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting field mapping profile for {Source} -> {Target}", sourceEntity, targetEntity);
            throw;
        }
    }

    /// <summary>
    /// Reverse-lookup helper: fetches sprk_recordlogicalname values for a set of ref GUIDs.
    /// Used by QueryFieldMappingProfilesAsync to populate SourceEntity/TargetEntity on DTOs.
    /// </summary>
    private async Task<Dictionary<Guid, string>> GetRecordTypeNamesByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default)
    {
        var distinctIds = ids.Where(g => g != Guid.Empty).Distinct().ToArray();
        var map = new Dictionary<Guid, string>();
        if (distinctIds.Length == 0)
            return map;

        var orClauses = string.Join(
            " or ",
            distinctIds.Select(id => $"sprk_recordtype_refid eq {id}"));

        var url =
            $"sprk_recordtype_refs?" +
            $"$select=sprk_recordtype_refid,sprk_recordlogicalname&" +
            $"$filter={orClauses}";

        var response = await SendGetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        if (data == null)
            return map;

        foreach (var row in data.Value)
        {
            if (!row.TryGetValue("sprk_recordtype_refid", out var idEl) || idEl.ValueKind == JsonValueKind.Null)
                continue;
            if (!row.TryGetValue("sprk_recordlogicalname", out var nameEl) || nameEl.ValueKind == JsonValueKind.Null)
                continue;

            var idStr = idEl.GetString();
            var name = nameEl.GetString();
            if (idStr is null || name is null || !Guid.TryParse(idStr, out var id))
                continue;

            map[id] = name;
        }

        return map;
    }

    public async Task<FieldMappingRuleEntity[]> GetFieldMappingRulesAsync(
        Guid profileId,
        bool activeOnly = true,
        CancellationToken ct = default)
    {

        var filterQuery = activeOnly
            ? $"$filter=_sprk_fieldmappingprofile_value eq {profileId} and sprk_isactive eq true&"
            : $"$filter=_sprk_fieldmappingprofile_value eq {profileId}&";

        var url = $"sprk_fieldmappingrules?{filterQuery}$select=sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_sourcefield,sprk_sourcefieldtype,sprk_targetfield,sprk_targetfieldtype,sprk_mapping_type,sprk_compatibilitymode,sprk_isrequired,sprk_defaultvalue,sprk_expression,sprk_iscascadingsource,sprk_executionorder,sprk_isactive&$orderby=sprk_executionorder asc";

        _logger.LogDebug("Getting field mapping rules for profile: {ProfileId}", profileId);

        try
        {
            var response = await SendGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null)
                return Array.Empty<FieldMappingRuleEntity>();

            return data.Value.Select(MapToFieldMappingRuleEntity).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting field mapping rules for profile {ProfileId}", profileId);
            throw;
        }
    }

    public async Task<Dictionary<string, object?>> RetrieveRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        string[] fields,
        CancellationToken ct = default)
    {

        var entitySetName = await GetEntitySetNameAsync(entityLogicalName, ct);
        var selectFields = string.Join(",", fields);
        var url = $"{entitySetName}({recordId})?$select={selectFields}";

        _logger.LogDebug("Retrieving record fields: {Entity}({Id})", entityLogicalName, recordId);

        try
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
            request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

            var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Record not found: {Entity}({Id})", entityLogicalName, recordId);
                return new Dictionary<string, object?>();
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null)
                return new Dictionary<string, object?>();

            var result = new Dictionary<string, object?>();
            foreach (var field in fields)
            {
                if (data.TryGetValue(field, out var value))
                {
                    result[field] = ConvertJsonElementToObject(value);
                }
                else
                {
                    result[field] = null;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving record fields: {Entity}({Id})", entityLogicalName, recordId);
            throw;
        }
    }

    public async Task<Guid[]> QueryChildRecordIdsAsync(
        string childEntityLogicalName,
        string parentLookupField,
        Guid parentRecordId,
        CancellationToken ct = default)
    {

        var entitySetName = await GetEntitySetNameAsync(childEntityLogicalName, ct);
        var primaryKey = $"{childEntityLogicalName}id";
        var url = $"{entitySetName}?$filter=_{parentLookupField}_value eq {parentRecordId}&$select={primaryKey}";

        _logger.LogDebug("Querying child records: {Entity} by {LookupField} = {ParentId}", childEntityLogicalName, parentLookupField, parentRecordId);

        try
        {
            var response = await SendGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null)
                return Array.Empty<Guid>();

            return data.Value
                .Where(d => d.TryGetValue(primaryKey, out _))
                .Select(d => Guid.Parse(d[primaryKey].GetString()!))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying child records: {Entity} by {LookupField}", childEntityLogicalName, parentLookupField);
            throw;
        }
    }

    /// <summary>
    /// Runs an OData GET against <paramref name="entitySetName"/> IMPERSONATING the given caller
    /// (<c>MSCRMCallerID</c> = <paramref name="callerSystemUserId"/>), returning the raw rows. Because the query
    /// runs AS the caller, Dataverse applies row-level security natively — the result is EXACTLY the rows that user
    /// may read (honoring ownership, role depth, BU, teams, sharing, hierarchy) in a single query. This is the read
    /// primitive for the messaging thread-read / unread endpoints (messaging-communication-app-r1 task 050); the
    /// caller then applies the internal-only + privilege business rules (<c>CommunicationAccessFilter</c>) on top.
    /// <para>
    /// SECURITY: a <see cref="Guid.Empty"/> caller is REJECTED — the read path MUST NOT fall back to an app-only
    /// (org-scoped) query, which would return rows the user cannot see (fail closed, NFR-06). Requires the BFF app
    /// user to hold <c>prvActOnBehalfOfAnotherUser</c> (owner config; a go-live prerequisite).
    /// </para>
    /// </summary>
    /// <param name="entitySetName">The OData entity SET name, e.g. <c>sprk_communications</c> (plural collection name).</param>
    /// <param name="odataQuery">
    /// The OData query string WITHOUT a leading <c>?</c> (e.g. <c>$filter=...&amp;$select=...&amp;$orderby=...</c>),
    /// or null/empty for none. The caller is responsible for OData-escaping values.
    /// </param>
    /// <param name="callerSystemUserId">The Dataverse <c>systemuserid</c> to impersonate. MUST NOT be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The impersonated rows as attribute-keyed dictionaries (empty when none match).</returns>
    public async Task<IReadOnlyList<Dictionary<string, JsonElement>>> RetrieveMultipleImpersonatedAsync(
        string entitySetName,
        string? odataQuery,
        Guid callerSystemUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entitySetName))
            throw new ArgumentException("Entity set name must be provided.", nameof(entitySetName));

        if (callerSystemUserId == Guid.Empty)
            throw new ArgumentException(
                "An impersonated read requires a non-empty caller systemuserid; refusing to issue an app-only query on the access-scoped read path (fail closed).",
                nameof(callerSystemUserId));

        var url = string.IsNullOrWhiteSpace(odataQuery)
            ? entitySetName
            : $"{entitySetName}?{odataQuery}";

        _logger.LogDebug(
            "[DATAVERSE-IMPERSONATE] GET {EntitySet} as caller {CallerSystemUserId}", entitySetName, callerSystemUserId);

        // Request FormattedValue annotations so a lookup's display name (e.g. sprk_sentby → the sender's
        // systemuser display name) rides the SAME impersonated row as the lookup value itself. This is the
        // canonical Dataverse way to project a related record's name without a second query OR a broken/
        // never-written denormalized name column (messaging-r3 2026-07-22 — replaced the unqueryable
        // sprk_sentbyname column). Annotations are additive extra keys ("{field}@OData.Community.Display.
        // V1.FormattedValue") that non-communication readers of this impersonated seam simply ignore.
        using var request = await CreateAuthenticatedRequestAsync(
            HttpMethod.Get, url, ct, impersonateSystemUserId: callerSystemUserId);
        request.Headers.TryAddWithoutValidation(
            "Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        return data?.Value ?? new List<Dictionary<string, JsonElement>>();
    }

    // ── POA (principalobjectaccess) primitives — messaging-communication-app-r1 task 043 ──────────────
    // Direct 1:1 thread + per-message access is expressed as narrow Dataverse OWNERSHIP + "Manage access"
    // (POA) shares (owner decision 2026-07-16, notes/access-model-decision.md — the SAME OOB GrantAccess/POA
    // mechanism PlaybookSharingService already uses for playbook team-sharing). These three primitives are
    // the minimal generic surface a POA-based caller needs: resolve an entity's ObjectTypeCode (POA's
    // objectid is polymorphic and requires it), grant access to a systemuser principal, and read back the
    // systemuser principals with an active share. Added HERE (not a new HTTP client) because this class is
    // already the BFF's established generic Web API singleton (IEventDataverseService /
    // IFieldMappingDataverseService / IImpersonatedCommunicationQuery all extend it) — CLAUDE.md §11: extend
    // the existing generic Dataverse Web API surface rather than stand up a second one.

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _objectTypeCodeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves an entity's Dataverse <c>ObjectTypeCode</c> (required to disambiguate <c>principalobjectaccess</c>'s
    /// polymorphic <c>objectid</c>). Cached in-memory per logical name — object type codes are immutable for the
    /// lifetime of the connected environment.
    /// </summary>
    public async Task<int> GetEntityObjectTypeCodeAsync(string entityLogicalName, CancellationToken ct = default)
    {
        if (_objectTypeCodeCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var url = $"EntityDefinitions(LogicalName='{entityLogicalName}')?$select=ObjectTypeCode";
        var response = await SendGetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetEntityObjectTypeCodeAsync failed for '{Entity}': {StatusCode}", entityLogicalName, response.StatusCode);
            return 0;
        }

        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
        var code = data != null
            && data.TryGetValue("ObjectTypeCode", out var codeElement)
            && codeElement.TryGetInt32(out var parsed)
            ? parsed
            : 0;

        if (code != 0)
            _objectTypeCodeCache[entityLogicalName] = code;

        return code;
    }

    /// <summary>
    /// Grants POA access on a record to a <c>systemuser</c> principal — the OOB "Manage access" (GrantAccess)
    /// mechanism, app-only. <paramref name="accessRightsCsv"/> is the Dataverse <c>AccessMask</c> literal (e.g.
    /// <c>"ReadAccess"</c> or <c>"ReadAccess,AppendAccess"</c>).
    /// </summary>
    public async Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        Guid principalSystemUserId,
        string accessRightsCsv,
        CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["Target"] = new Dictionary<string, object>
            {
                ["@odata.id"] = $"{entitySetName}({recordId})",
            },
            ["PrincipalAccess"] = new Dictionary<string, object>
            {
                ["Principal"] = new Dictionary<string, object>
                {
                    ["@odata.id"] = $"systemusers({principalSystemUserId})",
                },
                ["AccessMask"] = accessRightsCsv,
            },
        };

        var response = await SendPostAsJsonAsync("GrantAccess", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Reads the principals with an ACTIVE POA share on a record, as <c>systemuser</c> ids. Assumes every
    /// share on the record was written by a Spaarke systemuser-only grant flow (R1 Direct-thread scope is
    /// internal-only per <c>notes/access-model-decision.md</c> config prerequisite #3 — no team/contact
    /// shares are written by this path, so no <c>principaltypecode</c> filter is needed to disambiguate).
    /// Fails soft: a lookup error returns an empty list rather than throwing (callers treat "no shares" as
    /// the safe default — see <see cref="Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService"/>).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetSharedSystemUserIdsAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
    {
        var objectTypeCode = await GetEntityObjectTypeCodeAsync(entityLogicalName, ct);
        if (objectTypeCode == 0)
            return Array.Empty<Guid>();

        var url = $"principalobjectaccessset?$filter=objectid eq {recordId} and objecttypecode eq {objectTypeCode}&$select=principalid";
        var response = await SendGetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetSharedSystemUserIdsAsync failed for {Entity}({RecordId}): {StatusCode}",
                entityLogicalName, recordId, response.StatusCode);
            return Array.Empty<Guid>();
        }

        var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        if (data?.Value is null)
            return Array.Empty<Guid>();

        return data.Value
            .Select(row => row.TryGetValue("principalid", out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)
                ? g
                : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
    }

    public async Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default,
        Guid? impersonateSystemUserId = null)
    {

        if (fields.Count == 0)
        {
            _logger.LogDebug("No fields to update for {Entity}({Id})", entityLogicalName, recordId);
            return;
        }

        var entitySetName = await GetEntitySetNameAsync(entityLogicalName, ct);

        _logger.LogInformation(
            "Updating record fields: {Entity}({Id}), {FieldCount} fields{Impersonation}",
            entityLogicalName, recordId, fields.Count,
            impersonateSystemUserId is { } imp && imp != Guid.Empty ? $" (impersonating {imp})" : string.Empty);

        var response = await SendPatchAsJsonAsync($"{entitySetName}({recordId})", fields, ct, impersonateSystemUserId);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            // R7 W12 debug: include the serialized payload body (with values, not just field
            // names) so we can identify AI outputs that Dataverse rejects (e.g., object-shape
            // values written to a string column). Values truncated at 500 chars per field to
            // keep the log line bounded.
            var truncatedFields = fields.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)(kvp.Value?.ToString() is { Length: > 500 } s ? s[..500] + "..." : kvp.Value));
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(truncatedFields);
            _logger.LogError(
                "UpdateRecordFieldsAsync PATCH failed: {StatusCode} for {Entity}({Id}). Fields: [{FieldNames}]. Payload: {Payload}. Response: {ErrorBody}",
                response.StatusCode,
                entityLogicalName,
                recordId,
                string.Join(", ", fields.Keys),
                payloadJson,
                errorBody);
            response.EnsureSuccessStatusCode(); // still throw for the caller
        }

        _logger.LogDebug("Record updated: {Entity}({Id})", entityLogicalName, recordId);
    }

    /// <summary>
    /// Get a field mapping profile with its rules in a single request using $expand.
    /// Eliminates the second Dataverse call for rules by expanding the 1:N relationship
    /// inline. Falls back to sequential calls if $expand fails.
    /// </summary>
    public async Task<FieldMappingProfileEntity?> GetFieldMappingProfileWithRulesAsync(
        string sourceEntity,
        string targetEntity,
        bool activeRulesOnly = true,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Getting field mapping profile with rules via $expand: {Source} -> {Target}",
            sourceEntity, targetEntity);

        try
        {
            // Step 1: resolve source + target logical names -> record-type-ref GUIDs (SRFR-056).
            var idMap = await LookupRecordTypeIdsAsync(new[] { sourceEntity, targetEntity }, ct);
            if (!idMap.TryGetValue(sourceEntity, out var sourceRefId) ||
                !idMap.TryGetValue(targetEntity, out var targetRefId))
            {
                _logger.LogDebug(
                    "Record-type-ref catalog entry not found for source={Source} or target={Target}; no profile can match",
                    sourceEntity, targetEntity);
                return null;
            }

            // Step 2: query profile + expand rules in one round-trip filtered by resolved GUIDs.
            // sprk_fieldmappingrule_FieldMappingProfile_sprk_fieldmappingprofile is the 1:N navigation property name.
            var ruleSelect = "$select=sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_sourcefield,sprk_sourcefieldtype,sprk_targetfield,sprk_targetfieldtype,sprk_mapping_type,sprk_compatibilitymode,sprk_isrequired,sprk_defaultvalue,sprk_expression,sprk_iscascadingsource,sprk_executionorder,sprk_isactive";
            var ruleFilter = activeRulesOnly ? ";$filter=sprk_isactive eq true" : "";
            var ruleOrderBy = ";$orderby=sprk_executionorder asc";

            var url =
                $"sprk_fieldmappingprofiles?" +
                $"$filter=_sprk_sourcerecordtype_value eq {sourceRefId} and _sprk_targetrecordtype_value eq {targetRefId} and statecode eq 0" +
                $"&$select=sprk_fieldmappingprofileid,sprk_name,_sprk_sourcerecordtype_value,_sprk_targetrecordtype_value,sprk_capabilitymode,sprk_defaultvalue,sprk_description,statecode" +
                $"&$expand=sprk_fieldmappingrule_FieldMappingProfile_sprk_fieldmappingprofile({ruleSelect}{ruleFilter}{ruleOrderBy})";

            var response = await SendGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null || data.Value.Count == 0)
                return null;

            // Populate reverse-lookup map so the mapper writes source/target logical names on the DTO.
            var idToName = new Dictionary<Guid, string>
            {
                [sourceRefId] = sourceEntity,
                [targetRefId] = targetEntity
            };

            var profile = MapToFieldMappingProfileEntity(data.Value[0], idToName);

            // Extract expanded rules from the response
            if (data.Value[0].TryGetValue("sprk_fieldmappingrule_FieldMappingProfile_sprk_fieldmappingprofile", out var rulesElement)
                && rulesElement.ValueKind == JsonValueKind.Array)
            {
                var rules = new List<FieldMappingRuleEntity>();
                foreach (var ruleJson in rulesElement.EnumerateArray())
                {
                    var ruleDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ruleJson.GetRawText());
                    if (ruleDict != null)
                    {
                        rules.Add(MapToFieldMappingRuleEntity(ruleDict));
                    }
                }
                profile.Rules = rules;
            }
            else
            {
                profile.Rules = new List<FieldMappingRuleEntity>();
            }

            _logger.LogDebug(
                "Retrieved profile {ProfileName} with {RuleCount} rules via $expand",
                profile.Name, profile.Rules.Count);

            return profile;
        }
        catch (Exception ex)
        {
            // Fall back to sequential calls if $expand fails
            _logger.LogWarning(ex,
                "$expand failed for field mapping profile, falling back to sequential calls");

            return await GetFieldMappingProfileAsync(sourceEntity, targetEntity, ct);
        }
    }

    // ========================================
    // Entity Mapping Helpers
    // ========================================

    private EventEntity MapToEventEntity(Dictionary<string, JsonElement> data)
    {
        return new EventEntity
        {
            Id = data.TryGetValue("sprk_eventid", out var id) && id.ValueKind != JsonValueKind.Null
                ? Guid.Parse(id.GetString()!) : Guid.Empty,
            Name = data.TryGetValue("sprk_eventname", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString()! : string.Empty,
            Description = data.TryGetValue("sprk_description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString() : null,
            EventTypeId = data.TryGetValue("_sprk_eventtype_ref_value", out var etId) && etId.ValueKind != JsonValueKind.Null
                ? Guid.Parse(etId.GetString()!) : null,
            EventTypeName = data.TryGetValue("sprk_eventtype_ref", out var et) && et.ValueKind == JsonValueKind.Object
                ? et.GetProperty("sprk_name").GetString() : null,
            StateCode = data.TryGetValue("statecode", out var state) ? state.GetInt32() : 0,
            StatusCode = data.TryGetValue("statuscode", out var status) ? status.GetInt32() : 1,
            BaseDate = data.TryGetValue("sprk_basedate", out var bd) && bd.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(bd.GetString()!) : null,
            DueDate = data.TryGetValue("sprk_duedate", out var dd) && dd.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(dd.GetString()!) : null,
            CompletedDate = data.TryGetValue("sprk_completeddate", out var cd) && cd.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(cd.GetString()!) : null,
            Priority = data.TryGetValue("sprk_priority", out var pri) && pri.ValueKind != JsonValueKind.Null
                ? pri.GetInt32() : null,
            Source = data.TryGetValue("sprk_source", out var src) && src.ValueKind != JsonValueKind.Null
                ? src.GetInt32() : null,
            RemindAt = data.TryGetValue("sprk_remindat", out var ra) && ra.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(ra.GetString()!) : null,
            RelatedEventId = data.TryGetValue("_sprk_relatedevent_value", out var reId) && reId.ValueKind != JsonValueKind.Null
                ? Guid.Parse(reId.GetString()!) : null,
            RelatedEventType = data.TryGetValue("sprk_relatedeventtype", out var ret) && ret.ValueKind != JsonValueKind.Null
                ? ret.GetInt32() : null,
            RelatedEventOffsetType = data.TryGetValue("sprk_relatedeventoffsettype", out var reot) && reot.ValueKind != JsonValueKind.Null
                ? reot.GetInt32() : null,
            RegardingRecordId = data.TryGetValue("sprk_regardingrecordid", out var rrid) && rrid.ValueKind != JsonValueKind.Null
                ? rrid.GetString() : null,
            RegardingRecordName = data.TryGetValue("sprk_regardingrecordname", out var rrn) && rrn.ValueKind != JsonValueKind.Null
                ? rrn.GetString() : null,
            RegardingRecordType = data.TryGetValue("sprk_regardingrecordtype", out var rrt) && rrt.ValueKind != JsonValueKind.Null
                ? rrt.GetInt32() : null,
            RegardingAccountId = data.TryGetValue("_sprk_regardingaccount_value", out var racc) && racc.ValueKind != JsonValueKind.Null
                ? Guid.Parse(racc.GetString()!) : null,
            RegardingAnalysisId = data.TryGetValue("_sprk_regardinganalysis_value", out var rana) && rana.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rana.GetString()!) : null,
            RegardingContactId = data.TryGetValue("_sprk_regardingcontact_value", out var rcon) && rcon.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rcon.GetString()!) : null,
            RegardingInvoiceId = data.TryGetValue("_sprk_regardinginvoice_value", out var rinv) && rinv.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rinv.GetString()!) : null,
            RegardingMatterId = data.TryGetValue("_sprk_regardingmatter_value", out var rmat) && rmat.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rmat.GetString()!) : null,
            RegardingProjectId = data.TryGetValue("_sprk_regardingproject_value", out var rproj) && rproj.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rproj.GetString()!) : null,
            RegardingBudgetId = data.TryGetValue("_sprk_regardingbudget_value", out var rbud) && rbud.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rbud.GetString()!) : null,
            RegardingWorkAssignmentId = data.TryGetValue("_sprk_regardingworkassignment_value", out var rwa) && rwa.ValueKind != JsonValueKind.Null
                ? Guid.Parse(rwa.GetString()!) : null,
            CreatedOn = data.TryGetValue("createdon", out var created) && created.ValueKind != JsonValueKind.Null
                ? created.GetDateTime() : DateTime.MinValue,
            ModifiedOn = data.TryGetValue("modifiedon", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? modified.GetDateTime() : DateTime.MinValue
        };
    }

    private EventLogEntity MapToEventLogEntity(Dictionary<string, JsonElement> data)
    {
        return new EventLogEntity
        {
            Id = data.TryGetValue("sprk_eventlogid", out var id) && id.ValueKind != JsonValueKind.Null
                ? Guid.Parse(id.GetString()!) : Guid.Empty,
            Name = data.TryGetValue("sprk_eventlogname", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString() : null,
            EventId = data.TryGetValue("_sprk_event_value", out var evId) && evId.ValueKind != JsonValueKind.Null
                ? Guid.Parse(evId.GetString()!) : Guid.Empty,
            Action = data.TryGetValue("sprk_action", out var action) ? action.GetInt32() : 0,
            Description = data.TryGetValue("sprk_description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString() : null,
            CreatedOn = data.TryGetValue("createdon", out var created) && created.ValueKind != JsonValueKind.Null
                ? created.GetDateTime() : DateTime.MinValue,
            CreatedById = data.TryGetValue("_createdby_value", out var cbId) && cbId.ValueKind != JsonValueKind.Null
                ? Guid.Parse(cbId.GetString()!) : null,
            CreatedByName = data.TryGetValue("_createdby_value@OData.Community.Display.V1.FormattedValue", out var cbName) && cbName.ValueKind != JsonValueKind.Null
                ? cbName.GetString() : null
        };
    }

    private EventTypeEntity MapToEventTypeEntity(Dictionary<string, JsonElement> data)
    {
        return new EventTypeEntity
        {
            Id = data.TryGetValue("sprk_eventtypeid", out var id) && id.ValueKind != JsonValueKind.Null
                ? Guid.Parse(id.GetString()!) : Guid.Empty,
            Name = data.TryGetValue("sprk_name", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString()! : string.Empty,
            EventCode = data.TryGetValue("sprk_eventcode", out var code) && code.ValueKind != JsonValueKind.Null
                ? code.GetString() : null,
            Description = data.TryGetValue("sprk_description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString() : null,
            StateCode = data.TryGetValue("statecode", out var state) ? state.GetInt32() : 0,
            RequiresDueDate = data.TryGetValue("sprk_requiresduedate", out var rdd) && rdd.ValueKind != JsonValueKind.Null
                ? rdd.GetInt32() : null,
            RequiresBaseDate = data.TryGetValue("sprk_requiresbasedate", out var rbd) && rbd.ValueKind != JsonValueKind.Null
                ? rbd.GetInt32() : null
        };
    }

    /// <summary>
    /// Maps a Dataverse sprk_fieldmappingprofile row into the entity DTO.
    /// The Dataverse schema uses lookups to sprk_recordtype_ref for source/target instead of
    /// plain text columns. The <paramref name="recordTypeIdToLogicalName"/> map allows the caller
    /// to populate SourceEntity/TargetEntity with the human-readable logical names required by
    /// consumers. If the map is missing an entry, the string is left empty.
    ///
    /// Legacy fields (sprk_mappingdirection, sprk_syncmode, sprk_isactive) do NOT exist on the
    /// deployed entity — they are always defaulted (MappingDirection=0=ParentToChild,
    /// SyncMode=0=OneTime, IsActive derived from statecode).
    /// </summary>
    private FieldMappingProfileEntity MapToFieldMappingProfileEntity(
        Dictionary<string, JsonElement> data,
        IReadOnlyDictionary<Guid, string>? recordTypeIdToLogicalName = null)
    {
        // Read the raw lookup GUID values (OData exposes them as _<fieldname>_value).
        Guid? sourceRefId = data.TryGetValue("_sprk_sourcerecordtype_value", out var srcVal) && srcVal.ValueKind != JsonValueKind.Null
            ? Guid.Parse(srcVal.GetString()!) : null;
        Guid? targetRefId = data.TryGetValue("_sprk_targetrecordtype_value", out var tgtVal) && tgtVal.ValueKind != JsonValueKind.Null
            ? Guid.Parse(tgtVal.GetString()!) : null;

        string sourceEntity = string.Empty;
        string targetEntity = string.Empty;
        if (recordTypeIdToLogicalName != null)
        {
            if (sourceRefId.HasValue && recordTypeIdToLogicalName.TryGetValue(sourceRefId.Value, out var s))
                sourceEntity = s;
            if (targetRefId.HasValue && recordTypeIdToLogicalName.TryGetValue(targetRefId.Value, out var t))
                targetEntity = t;
        }

        // statecode: 0=Active, 1=Inactive on Dataverse convention.
        var isActive = data.TryGetValue("statecode", out var state) && state.ValueKind != JsonValueKind.Null
            && state.GetInt32() == 0;

        return new FieldMappingProfileEntity
        {
            Id = data.TryGetValue("sprk_fieldmappingprofileid", out var id) && id.ValueKind != JsonValueKind.Null
                ? Guid.Parse(id.GetString()!) : Guid.Empty,
            Name = data.TryGetValue("sprk_name", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString()! : string.Empty,
            SourceEntity = sourceEntity,
            TargetEntity = targetEntity,
            // MappingDirection + SyncMode do not exist on the deployed entity; default to
            // ParentToChild + OneTime. If future schema adds these back, this mapper is where
            // to re-hydrate them.
            MappingDirection = 0,
            SyncMode = 0,
            IsActive = isActive,
            Description = data.TryGetValue("sprk_description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString() : null
        };
    }

    private FieldMappingRuleEntity MapToFieldMappingRuleEntity(Dictionary<string, JsonElement> data)
    {
        return new FieldMappingRuleEntity
        {
            Id = data.TryGetValue("sprk_fieldmappingruleid", out var id) && id.ValueKind != JsonValueKind.Null
                ? Guid.Parse(id.GetString()!) : Guid.Empty,
            Name = data.TryGetValue("sprk_name", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString()! : string.Empty,
            ProfileId = data.TryGetValue("_sprk_fieldmappingprofile_value", out var pid) && pid.ValueKind != JsonValueKind.Null
                ? Guid.Parse(pid.GetString()!) : Guid.Empty,
            SourceField = data.TryGetValue("sprk_sourcefield", out var sf) && sf.ValueKind != JsonValueKind.Null
                ? sf.GetString()! : string.Empty,
            SourceFieldType = data.TryGetValue("sprk_sourcefieldtype", out var sft) && sft.ValueKind != JsonValueKind.Null ? sft.GetInt32() : 0,
            TargetField = data.TryGetValue("sprk_targetfield", out var tf) && tf.ValueKind != JsonValueKind.Null
                ? tf.GetString()! : string.Empty,
            TargetFieldType = data.TryGetValue("sprk_targetfieldtype", out var tft) && tft.ValueKind != JsonValueKind.Null ? tft.GetInt32() : 0,
            MappingType = data.TryGetValue("sprk_mapping_type", out var mt) && mt.ValueKind != JsonValueKind.Null ? mt.GetInt32() : 0,
            CompatibilityMode = data.TryGetValue("sprk_compatibilitymode", out var cm) && cm.ValueKind != JsonValueKind.Null ? cm.GetInt32() : 0,
            IsRequired = data.TryGetValue("sprk_isrequired", out var req) && req.ValueKind != JsonValueKind.Null && req.GetBoolean(),
            DefaultValue = data.TryGetValue("sprk_defaultvalue", out var dv) && dv.ValueKind != JsonValueKind.Null
                ? dv.GetString() : null,
            Expression = data.TryGetValue("sprk_expression", out var expr) && expr.ValueKind != JsonValueKind.Null
                ? expr.GetString() : null,
            IsCascadingSource = data.TryGetValue("sprk_iscascadingsource", out var cs) && cs.ValueKind != JsonValueKind.Null && cs.GetBoolean(),
            ExecutionOrder = data.TryGetValue("sprk_executionorder", out var eo) && eo.ValueKind != JsonValueKind.Null ? eo.GetInt32() : 0,
            IsActive = data.TryGetValue("sprk_isactive", out var active) && active.ValueKind != JsonValueKind.Null && active.GetBoolean()
        };
    }

    private static object? ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();
    }

    private class ODataCountResponse
    {
        [JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();

        [JsonPropertyName("@odata.count")]
        public int Count { get; set; }
    }
}
