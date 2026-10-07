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
/// (<c>RetrieveMultipleImpersonatedAsync</c>, <c>GrantAccessAsync</c>, <c>RevokeAccessAsync</c>, <c>GetPrincipalAccessAsync</c>)
/// reached via the <c>IImpersonatedCommunicationQuery</c> / <c>IDataverseRecordShareService</c> seams in
/// <c>CommunicationModule.cs</c>. Document / analysis / generic-entity / processing-job / KPI /
/// communication-query / health capability all resolve to <see cref="DataverseServiceClientImpl"/>
/// (SDK) — the former implementations of those surfaces here were runtime-dead and were removed.
/// See <c>docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md</c>.</para>
///
/// <para><b>Every POA share write notifies</b> (unified-access-control-r2 task 132, main-session round 55): GrantAccessAsync,
/// ModifyAccessAsync and RevokeAccessAsync send through one private method that, on every path, tells the
/// <see cref="IRecordShareWriteObserver"/> passed to the constructor which record it wrote — in the BFF, the access-cache
/// invalidator. It is a property of the write, so it holds for every caller and every way of invoking it.</para>
/// </remarks>
public class DataverseWebApiService : IEventDataverseService, IFieldMappingDataverseService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseWebApiService> _logger;
    private readonly IRecordShareWriteObserver _shareWriteObserver;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    /// <param name="shareWriteObserver">
    /// Told after every POA share write this client makes (unified-access-control-r2 task 132, round 55) — in the BFF,
    /// its access-cache invalidator. Required, with no default, so no host gets a client whose share writes silently
    /// tell nobody; a host with no access cache passes one that does nothing. See <see cref="IRecordShareWriteObserver"/>.
    /// </param>
    /// <param name="confidentialClients">
    /// Ordered credential provider (auth-v4 task 021/022), supplied by the BFF. Used ONLY in the
    /// managed-identity-disabled branch, where it replaces an inline <c>ClientSecretCredential</c>.
    /// Nullable with a null default so existing fixtures constructing this type directly keep
    /// compiling (NFR-04); a null provider is fatal only if that branch is actually taken.
    /// </param>
    public DataverseWebApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseWebApiService> logger,
        IRecordShareWriteObserver shareWriteObserver,
        IConfidentialClientProvider? confidentialClients = null)
        : this(httpClient, configuration, logger, shareWriteObserver, confidentialClients, credential: null)
    {
    }

    /// <summary>
    /// Test seam (unified-access-control-r2 task 104). A non-null <paramref name="credential"/> bypasses
    /// credential SELECTION: neither the managed-identity nor the ordered-provider branch runs. Tests use it to
    /// send a real request through the production request builder without a network token.
    /// <para><b>Protected on purpose.</b> Dependency injection only considers public constructors, so the
    /// container's singleton <c>TokenCredential</c> (<c>Program.cs</c>) can never be injected here, however this
    /// type is registered. The public optional parameter on <see cref="DataverseWebApiClient"/> does not have
    /// that protection. Tests reach this constructor through a subclass.</para>
    /// </summary>
    protected DataverseWebApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseWebApiService> logger,
        IRecordShareWriteObserver shareWriteObserver,
        IConfidentialClientProvider? confidentialClients,
        TokenCredential? credential)
    {
        _httpClient = httpClient;
        _logger = logger;
        _shareWriteObserver = shareWriteObserver ?? throw new ArgumentNullException(nameof(shareWriteObserver));

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        // ADR-028 §24 (#3b): prefer Managed Identity when enabled. Task 022: the non-MI branch is no
        // longer "the ClientSecret fallback" — it is ordered credential selection, which reaches the
        // secret only as its last option.
        var useManagedIdentity = string.Equals(
            configuration["Graph:ManagedIdentity:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

        if (credential is not null)
        {
            // Explicitly supplied (tests only): selection is bypassed. See this protected ctor's summary.
            _credential = credential;
            _logger.LogInformation(
                "DataverseWebApiService using an injected TokenCredential (selection bypassed) for {ApiUrl}", _apiUrl);
        }
        else if (useManagedIdentity)
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

            if (string.IsNullOrEmpty(tenantId))
                throw new InvalidOperationException("TENANT_ID configuration is required (Managed Identity disabled)");
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("API_APP_ID configuration is required (Managed Identity disabled)");

            // auth-v4 task 022 (FR-B3): the app registration's app-only token, credential chosen by
            // ordered selection instead of an inline ClientSecretCredential built from
            // Dataverse:ClientSecret. Same identity, same client-credentials grant; only the proof
            // changes. Dataverse:ClientSecret now has NO consumer in src/ — booked for deletion at
            // task 033 alongside Graph:ClientSecret (see notes/decisions/024-relax-config-validators.md).
            if (confidentialClients is null)
                throw new InvalidOperationException(
                    "An IConfidentialClientProvider is required when Managed Identity is disabled "
                    + "(Graph:ManagedIdentity:Enabled is not true). Inside the BFF it is registered by "
                    + "AuthorizationModule.AddCredentialSelection.");

            _credential = new ConfidentialClientTokenCredential(confidentialClients, tenantId, clientId);
            _logger.LogInformation(
                "DataverseWebApiService using the ordered credential provider (ADR-028 A4) for {ApiUrl}", _apiUrl);
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
    /// header via <see cref="DataverseImpersonation.ApplyAsSystemUser"/>). <c>null</c> (the default) leaves the
    /// request app-only and byte-unchanged — existing consumers pass nothing and are unaffected.
    /// <see cref="Guid.Empty"/> is REFUSED (task 104, fail closed): it used to be read as "no impersonation",
    /// which turned a missing caller id into a silent app-only request. Added for the messaging read path
    /// (messaging-communication-app-r1), where Dataverse does row-level filtering natively.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="impersonateSystemUserId"/> is <see cref="Guid.Empty"/>. Thrown before a token is acquired
    /// or anything is sent.
    /// </exception>
    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(
        HttpMethod method, string url, CancellationToken cancellationToken = default, Guid? impersonateSystemUserId = null)
    {
        var request = new HttpRequestMessage(method, url);
        try
        {
            // Impersonation first, so an empty caller id is refused before any I/O.
            if (impersonateSystemUserId is { } callerSystemUserId)
                DataverseImpersonation.ApplyAsSystemUser(request, callerSystemUserId);

            var token = await GetAccessTokenAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends a GET request with per-request auth headers. When <paramref name="impersonateSystemUserId"/> is
    /// supplied the request runs AS that Dataverse user (MSCRMCallerID impersonation; <see cref="Guid.Empty"/> is
    /// refused). When it is null the request is app-only.
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
    /// user, honest <c>modifiedby</c>); null = app-only (existing callers byte-unchanged); <see cref="Guid.Empty"/>
    /// is refused (task 104). This is the
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

    public Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
        int? regardingRecordType = null,
        Guid? regardingRecordId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        Guid? ownerUserId = null,
        IReadOnlyCollection<int>? excludeStatusCodes = null,
        CancellationToken ct = default)
    {
        // APP-ONLY: background callers only (TodoGenerationService). See the interface remarks.
        var url = BuildEventQueryUrl(
            regardingRecordType, regardingRecordId, regardingRecordTypeRefId: null,
            eventTypeId, statusCode, priority, dueDateFrom, dueDateTo, skip, top,
            new EventOwnershipScope(ownerUserId, null, null), excludeStatusCodes);

        return QueryEventsCoreAsync(url, skip, top, impersonateSystemUserId: null, ct);
    }

    public Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsCallerAsync(
        Guid callerSystemUserId,
        int? regardingRecordType = null,
        Guid? regardingRecordId = null,
        Guid? regardingRecordTypeRefId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        EventOwnershipScope? mine = null,
        CancellationToken ct = default)
    {
        // Refused BEFORE the URL is built or anything is sent: an empty caller would otherwise be one bug away from
        // the app-only query this method exists to replace (task 104's fail-closed rule, restated at the entry).
        if (callerSystemUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "A caller-scoped event query requires a non-empty caller systemuserid; refusing to issue an app-only "
                + "query on the access-scoped read path (fail closed).",
                nameof(callerSystemUserId));
        }

        var url = BuildEventQueryUrl(
            regardingRecordType, regardingRecordId, regardingRecordTypeRefId,
            eventTypeId, statusCode, priority, dueDateFrom, dueDateTo, skip, top, mine, excludeStatusCodes: null);

        return QueryEventsCoreAsync(url, skip, top, callerSystemUserId, ct);
    }

    /// <summary>
    /// The Dataverse Web API rejects <c>$top</c> above this (and rejects <c>$skip</c> outright), so a page window
    /// <c>skip + top</c> may not exceed it. The endpoint validates the same bound as a 400.
    /// </summary>
    internal const int MaxEventQueryWindow = 5000;

    /// <summary>
    /// The <c>$select</c> of the event list. Lookups are selected as <c>_x_value</c> (a lookup's logical name is not a
    /// queryable property — live 400 on 2026-10-03); the regarding type is <c>_sprk_regardingrecordtype_value</c>, and
    /// the eight typed lookups the API's 0-7 types use are selected so the mapper can derive the type by value.
    /// </summary>
    internal const string EventListSelect =
        "sprk_eventid,sprk_eventname,sprk_description,_sprk_eventtype_ref_value,"
        + "sprk_regardingrecordid,sprk_regardingrecordname,_sprk_regardingrecordtype_value,"
        + "_sprk_regardingproject_value,_sprk_regardingmatter_value,_sprk_regardinginvoice_value,"
        + "_sprk_regardinganalysis_value,_sprk_regardingaccount_value,_sprk_regardingcontact_value,"
        + "_sprk_regardingworkassignment_value,_sprk_regardingbudget_value,"
        + "sprk_basedate,sprk_duedate,sprk_completeddate,statecode,statuscode,sprk_priority,sprk_source,"
        + "createdon,modifiedon";

    /// <summary>
    /// The <c>$select</c> of a single event: the list's columns plus the reminder/related-event columns. There is no
    /// <c>sprk_relatedevent</c> lookup on <c>sprk_event</c> (live 2026-10-03), so <c>_sprk_relatedevent_value</c> is
    /// not selected — selecting it 400'd every GET /{id}.
    /// </summary>
    internal const string EventGetSelect =
        EventListSelect + ",sprk_remindat,sprk_relatedeventtype,sprk_relatedeventoffsettype";

    /// <summary>
    /// The event-type <c>$expand</c>, by NAVIGATION property (<c>sprk_EventType_Ref</c>). The logical name
    /// <c>sprk_eventtype_ref</c> is not a property of the entity type (live 400 on 2026-10-03).
    /// </summary>
    internal const string EventTypeExpand = "sprk_EventType_Ref($select=sprk_name)";

    /// <summary>
    /// The ONE URL builder for both event queries (app-only and caller-scoped), so the trimmed list and the
    /// background scan cannot drift apart (unified-access-control-r2 task 159, #1098).
    /// </summary>
    /// <remarks>
    /// <para><b>No caller text reaches <c>$filter</c>.</b> Every clause is built from a typed value — a
    /// <see cref="Guid"/> formatted "D", an <see cref="int"/>, or a <see cref="DateTime"/> formatted yyyy-MM-dd with
    /// the invariant culture. The previous builder concatenated a caller string into
    /// <c>sprk_regardingrecordid eq '…'</c>, so <c>x' or sprk_regardingrecordid ne 'zz</c> escaped the owner clause.</para>
    /// <para><b>The regarding filters read the lookup as a lookup.</b> An id (with or without a type) filters on
    /// <c>sprk_regardingrecordid</c>, plus the type's typed lookup when the type is given; a type alone filters on
    /// <c>_sprk_regardingrecordtype_value</c> = its <c>sprk_recordtype_ref</c> row.</para>
    /// <para><b>Paging without <c>$skip</c></b>, which the Dataverse Web API rejects ("Skip Clause is not supported in
    /// CRM"): the request asks for the first <c>skip + top</c> rows, and the caller drops the first <c>skip</c>.
    /// <c>$count=true</c> still counts the whole (for the caller-scoped query: the whole TRIMMED) result.</para>
    /// </remarks>
    internal static string BuildEventQueryUrl(
        int? regardingRecordType,
        Guid? regardingRecordId,
        Guid? regardingRecordTypeRefId,
        Guid? eventTypeId,
        int? statusCode,
        int? priority,
        DateTime? dueDateFrom,
        DateTime? dueDateTo,
        int skip,
        int top,
        EventOwnershipScope? mine,
        IReadOnlyCollection<int>? excludeStatusCodes = null)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip), skip, "skip must not be negative.");
        if (top < 1)
            throw new ArgumentOutOfRangeException(nameof(top), top, "top must be at least 1.");
        if ((long)skip + top > MaxEventQueryWindow)
            throw new ArgumentOutOfRangeException(
                nameof(skip), skip, $"skip + top must not exceed {MaxEventQueryWindow} (the Dataverse $top limit).");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var filters = new List<string>();

        // "Mine" (task 097 round 8, owner decision B): owned by the caller, OR assigned to the caller's linked contact
        // (task 152 S1), OR created by the caller as sprk_createdbyperson (task 146 c1-r1 — a SYSTEMUSER lookup). An
        // app-only create is owned by a TEAM (I-6), so ownership alone would hide every event the BFF made for the caller.
        if (mine?.ToFilter(inv) is { } mineFilter)
            filters.Add(mineFilter);

        if (regardingRecordId is { } regardingId)
        {
            filters.Add(string.Create(inv, $"sprk_regardingrecordid eq '{regardingId:D}'"));

            if (regardingRecordType is { } typeWithId)
            {
                var lookup = RegardingRecordType.GetLookupFieldName(typeWithId)
                    ?? throw new ArgumentOutOfRangeException(
                        nameof(regardingRecordType), typeWithId, "Unknown regarding record type.");
                filters.Add(string.Create(inv, $"_{lookup}_value eq {regardingId:D}"));
            }
        }
        else if (regardingRecordType is { } typeOnly)
        {
            if (RegardingRecordType.GetLookupFieldName(typeOnly) is null)
                throw new ArgumentOutOfRangeException(nameof(regardingRecordType), typeOnly, "Unknown regarding record type.");

            // sprk_regardingrecordtype is a LOOKUP to sprk_recordtype_ref, never an option set. Filtering on it needs
            // the environment's row id, which only the BFF resolves; without one there is nothing true to filter on.
            if (regardingRecordTypeRefId is not { } refId || refId == Guid.Empty)
                throw new ArgumentException(
                    "A regarding-type filter without a regarding id needs that type's sprk_recordtype_ref row.",
                    nameof(regardingRecordTypeRefId));

            filters.Add(string.Create(inv, $"_sprk_regardingrecordtype_value eq {refId:D}"));
        }

        if (eventTypeId is { } eventType)
            filters.Add(string.Create(inv, $"_sprk_eventtype_ref_value eq {eventType:D}"));

        if (statusCode is { } status)
            filters.Add(string.Create(inv, $"statuscode eq {status}"));

        if (priority is { } prio)
            filters.Add(string.Create(inv, $"sprk_priority eq {prio}"));

        if (dueDateFrom is { } from)
            filters.Add(string.Create(inv, $"sprk_duedate ge {from:yyyy-MM-dd}"));

        if (dueDateTo is { } to)
            filters.Add(string.Create(inv, $"sprk_duedate le {to:yyyy-MM-dd}"));

        // Task 097 review F5: an exclusion is applied IN the query, so a page is cut from the rows that qualify (an
        // in-memory filter after $top would silently drop qualifying rows past the window).
        foreach (var excluded in excludeStatusCodes ?? Array.Empty<int>())
            filters.Add(string.Create(inv, $"statuscode ne {excluded}"));

        var filterQuery = filters.Count > 0 ? $"$filter={string.Join(" and ", filters)}&" : "";
        // Task 097 review F4: sprk_eventid is the final tiebreaker, so the skip+top window is deterministic across pages.
        return string.Create(inv,
            $"sprk_events?{filterQuery}$select={EventListSelect}&$expand={EventTypeExpand}"
            + $"&$orderby=sprk_duedate asc,createdon desc,sprk_eventid asc&$top={skip + top}&$count=true");
    }

    private async Task<(EventEntity[] Items, int TotalCount)> QueryEventsCoreAsync(
        string url, int skip, int top, Guid? impersonateSystemUserId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Querying events (impersonated: {Impersonated}): {Url}", impersonateSystemUserId.HasValue, url);

        try
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct, impersonateSystemUserId);
            request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCountResponse>(cancellationToken: ct);
            if (data == null)
                return (Array.Empty<EventEntity>(), 0);

            var events = data.Value.Skip(skip).Take(top).Select(MapToEventEntity).ToArray();
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

        var url = $"sprk_events({id:D})?$select={EventGetSelect}&$expand={EventTypeExpand}";

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
        ArgumentNullException.ThrowIfNull(request);

        // Refuses (throws) before anything is sent when the request carries no owner (task 146, in the builder).
        var payload = BuildCreateEventPayload(request);

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

    /// <summary>
    /// The Web API body <see cref="CreateEventAsync"/> POSTs to <c>sprk_events</c>. Internal (InternalsVisibleTo the
    /// BFF unit tests) so the payload — including the unified-access-control-r2 task 152 <c>sprk_AssignedTo</c> bind —
    /// is asserted without intercepting the HTTP transport (ADR-038 bans <c>Mock&lt;HttpMessageHandler&gt;</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The request carries no <see cref="CreateEventRequest.OwningTeamId"/>
    /// (unified-access-control-r2 task 146): no payload without an owner is ever built.</exception>
    internal static Dictionary<string, object?> BuildCreateEventPayload(CreateEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // unified-access-control-r2 task 146: the owner is resolved upstream (the regarding record's team; the named
        // Secure team for a secure one) and its absence REFUSES — never an app-owned event in the root business unit.
        if (request.OwningTeamId is not { } owningTeamId || owningTeamId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "CreateEventAsync requires CreateEventRequest.OwningTeamId (resolved by IRecordOwnershipResolver); "
                + "refusing to create an app-owned sprk_event (task 146).");
        }

        var payload = new Dictionary<string, object?>
        {
            ["sprk_eventname"] = request.Name,
            ["sprk_description"] = request.Description,
            // Live sprk_event option set (task 159, notes §0.2 (e)): Open = 659490001, statecode 0 (Active). The old
            // value 3 is not a statuscode of this table.
            ["statuscode"] = EventStatusCode.Open,
            ["statecode"] = EventStatusCode.GetStateCode(EventStatusCode.Open),
            ["sprk_source"] = 0, // User
            ["ownerid@odata.bind"] = $"/teams({owningTeamId})", // task 146 — resolved upstream; the builder refuses without it
        };

        // Task 146 c1-r1 (owner round 13 item 9): the person who asked — createdby is the application user here.
        RecordCreatorPersonColumn.BindIfKnown(payload, request.CreatedByPersonId);

        if (request.EventTypeId.HasValue)
            payload["sprk_EventType_Ref@odata.bind"] = $"/sprk_eventtype_refs({request.EventTypeId.Value})"; // R5 002: nav prop sprk_EventType_Ref + correct collection sprk_eventtype_refs (metadata-verified; sprk_eventtypes does not exist)

        if (request.BaseDate.HasValue)
            payload["sprk_basedate"] = request.BaseDate.Value.ToString("yyyy-MM-dd");

        if (request.DueDate.HasValue)
            payload["sprk_duedate"] = request.DueDate.Value.ToString("yyyy-MM-dd");

        if (request.Priority.HasValue)
            payload["sprk_priority"] = request.Priority.Value;

        // unified-access-control-r2 task 152 (owner S1): the person the event is FOR. PascalCase navigation property,
        // the same bind the client event wizard uses (CreateEventWizard eventService).
        if (request.AssignedToContactId is { } assignedTo && assignedTo != Guid.Empty)
            payload["sprk_AssignedTo@odata.bind"] = $"/contacts({assignedTo:D})";

        if (request.RegardingRecordType.HasValue)
            AddRegardingWriteSet(payload, request);

        return payload;
    }

    /// <summary><c>sprk_event.sprk_regardingrecordname</c> length (live metadata, 2026-10-03).</summary>
    internal const int EventRegardingRecordNameMaxLength = 1000;

    /// <summary><c>sprk_event.sprk_regardingrecordnumber</c> length (live metadata, 2026-10-03).</summary>
    internal const int EventRegardingRecordNumberMaxLength = 100;

    /// <summary>
    /// Shapes the ADR-024 regarding write set into the create body, in this order: (i) the target's own typed lookup
    /// by its live NAVIGATION property and its live entity SET; (ii) the record-type lookup to its
    /// <c>sprk_recordtype_ref</c> row, left out when the environment has none; (iii) the denormalized id, name, url
    /// and number; (iv) the FR-26 core-ancestor stamps.
    /// </summary>
    /// <remarks>
    /// <b>Only shapes.</b> Every value is resolved by the BFF (task 159 LAYERING note). A missing entity set is a
    /// programming error and throws — this method never derives a set name (no "logical name + s") and never
    /// writes <c>sprk_regardingrecordtype</c> as a number or any <c>_x_value</c> key.
    /// </remarks>
    private static void AddRegardingWriteSet(Dictionary<string, object?> payload, CreateEventRequest request)
    {
        var regardingType = request.RegardingRecordType!.Value;
        var lookup = RegardingRecordType.GetLookupFieldName(regardingType)
            ?? throw new ArgumentOutOfRangeException(nameof(request), regardingType, "Unknown regarding record type.");
        var navigation = RegardingRecordType.GetEventNavigationProperty(lookup)
            ?? throw new InvalidOperationException($"No sprk_event navigation property is known for '{lookup}'.");

        if (request.RegardingRecordId is not { } targetId || targetId == Guid.Empty)
            throw new ArgumentException("A regarding write needs the regarding record id.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegardingEntitySetName))
            throw new ArgumentException(
                "A regarding write needs the target's entity set, resolved from live metadata by the caller.",
                nameof(request));

        // (i) the target's own typed lookup
        payload[$"{navigation}@odata.bind"] = $"/{request.RegardingEntitySetName}({targetId:D})";

        // (ii) the record type, as a lookup to the environment's sprk_recordtype_ref row
        if (request.RegardingRecordTypeRefId is { } refId && refId != Guid.Empty)
            payload[$"{RegardingRecordType.EventRecordTypeNavigationProperty}@odata.bind"] =
                $"/{RegardingRecordType.RecordTypeRefEntitySet}({refId:D})";

        // (iii) the denormalized resolver fields
        payload["sprk_regardingrecordid"] = targetId.ToString("D");
        payload["sprk_regardingrecordname"] = Truncate(request.RegardingRecordName, EventRegardingRecordNameMaxLength);
        payload["sprk_regardingrecordurl"] = request.RegardingRecordUrl;
        if (!string.IsNullOrEmpty(request.RegardingRecordNumber))
            payload["sprk_regardingrecordnumber"] = Truncate(request.RegardingRecordNumber, EventRegardingRecordNumberMaxLength);

        // (iv) the core-ancestor stamps (the target's own lookup is never among them — DeriveForHostAsync skips it)
        foreach (var stamp in request.RegardingCoreStamps ?? [])
        {
            var stampNavigation = RegardingRecordType.GetEventNavigationProperty(stamp.LookupAttribute)
                ?? throw new InvalidOperationException(
                    $"No sprk_event navigation property is known for core stamp '{stamp.LookupAttribute}'.");
            if (string.IsNullOrWhiteSpace(stamp.EntitySetName) || stamp.RecordId == Guid.Empty)
                throw new ArgumentException($"Core stamp '{stamp.LookupAttribute}' has no entity set or id.", nameof(request));

            payload[$"{stampNavigation}@odata.bind"] = $"/{stamp.EntitySetName}({stamp.RecordId:D})";
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;

    // ── sprk_event status: the live option set and its statecode pairing have ONE home, EventStatusCode (Models.cs) ──
    // (task 097 and unified-access-control-r2 task 159 fixed the same values independently; merged into one.)

    public async Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default)
    {

        var payload = new Dictionary<string, object?>
        {
            ["statuscode"] = statusCode
        };

        // The statecode the live statuscode belongs to (task 159); an unknown value throws (task 097).
        payload["statecode"] = EventStatusCode.GetStateCode(statusCode);

        if (completedDate.HasValue)
            payload["sprk_completeddate"] = completedDate.Value.ToString("yyyy-MM-dd");

        _logger.LogInformation("Updating event status: {Id} -> {StatusCode}", id, statusCode);

        var response = await SendPatchAsJsonAsync($"sprk_events({id})", payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Event status updated: {Id}", id);
    }

    /// <summary><c>sprk_eventlog.sprk_eventlogname</c> length (live metadata).</summary>
    internal const int EventLogNameMaxLength = 850;

    /// <summary>
    /// The <c>sprk_eventlog</c> create body. Task 097 review F1: the table has NO <c>sprk_description</c> column (live
    /// spaarkedev1 — only sprk_eventlogname, sprk_action, sprk_event, sprk_createdbyperson and system columns), so the
    /// former body was a 400 on every write and every event log silently failed. The description is folded into the
    /// name (cut to 850) and read back from it.
    /// </summary>
    internal static Dictionary<string, object?> BuildCreateEventLogPayload(Guid eventId, int action, string? description, DateTime utcNow)
    {
        var name = $"{EventLogAction.GetDisplayName(action)} - {utcNow:yyyy-MM-dd HH:mm:ss} UTC";
        if (!string.IsNullOrWhiteSpace(description))
            name = $"{name} - {description}";
        return new Dictionary<string, object?>
        {
            ["sprk_eventlogname"] = name.Length > EventLogNameMaxLength ? name[..EventLogNameMaxLength] : name,
            ["sprk_Event@odata.bind"] = $"/sprk_events({eventId})", // R5 002: PascalCase nav prop (metadata-verified)
            ["sprk_action"] = action,
        };
    }
    public async Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, Guid? owningTeamId, Guid? createdByPersonId = null, CancellationToken ct = default)
    {

        var payload = BuildCreateEventLogPayload(eventId, action, description, DateTime.UtcNow);
        if (owningTeamId is { } teamId && teamId != Guid.Empty)
        {
            // Task 146: owned like its event (the caller resolved it). Unset only for an event that is not team-owned.
            payload["ownerid@odata.bind"] = $"/teams({teamId})";
        }

        // Task 146 c1-r1 (owner round 13 item 9): the person whose change the log records.
        RecordCreatorPersonColumn.BindIfKnown(payload, createdByPersonId);

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
    /// Grants POA access on a record to a principal — the OOB "Manage access" (GrantAccess) mechanism,
    /// app-only. <paramref name="accessRightsCsv"/> is the Dataverse <c>AccessMask</c> literal (e.g.
    /// <c>"ReadAccess"</c> or <c>"ReadAccess,AppendAccess"</c>).
    /// </summary>
    /// <remarks>
    /// <para>unified-access-control-r2 task 060: generalized from systemuser-only to any
    /// <see cref="DataversePrincipalKind"/>. This is the ONE place a <c>GrantAccess</c> payload is built —
    /// <c>PlaybookSharingService</c>'s private team-grant duplicate was deleted in favour of it.</para>
    ///
    /// <para><b>Creates a share; do not use it to change one</b> (task 063). Microsoft Learn does not document what
    /// GrantAccess does to a principal that already holds a share on the record, so it cannot be relied on to LOWER
    /// rights. A caller that means "this principal's rights are now exactly X" reads the shares first
    /// (<see cref="GetPrincipalAccessOrThrowAsync"/>) and calls <see cref="ModifyAccessAsync"/> for an existing
    /// share.</para>
    /// </remarks>
    public async Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default)
        => await SendShareWriteAsync(
            "GrantAccess", RecordShareWrite.Grant, entitySetName, recordId,
            PrincipalAccessPayload(entitySetName, recordId, principal, accessRightsCsv), ct);

    /// <summary>
    /// Replaces the rights of a principal's EXISTING POA share on a record — the OOB <c>ModifyAccess</c> action,
    /// app-only. Same key and payload shape as
    /// <see cref="GrantAccessAsync(string, Guid, DataversePrincipalRef, string, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 063. Microsoft Learn documents ModifyAccess as REPLACING the principal's access
    /// mask, which is what a level change needs: a downgrade from Full Access to View Only must leave Read, not Read
    /// plus the Write and Delete a GrantAccess might keep. It does not document ModifyAccess for a principal that
    /// holds no share; use <see cref="GrantAccessAsync"/> for that.
    /// </remarks>
    public async Task ModifyAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default)
        => await SendShareWriteAsync(
            "ModifyAccess", RecordShareWrite.Modify, entitySetName, recordId,
            PrincipalAccessPayload(entitySetName, recordId, principal, accessRightsCsv), ct);

    /// <summary>
    /// The <c>Target</c> + <c>PrincipalAccess</c> body that GrantAccess and ModifyAccess both take — built once, so
    /// the two actions cannot address a record or a principal differently.
    /// </summary>
    private static Dictionary<string, object> PrincipalAccessPayload(
        string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv)
        => new()
        {
            ["Target"] = new Dictionary<string, object>
            {
                ["@odata.id"] = $"{entitySetName}({recordId})",
            },
            ["PrincipalAccess"] = new Dictionary<string, object>
            {
                ["Principal"] = new Dictionary<string, object>
                {
                    ["@odata.id"] = $"{principal.Kind.ToEntitySet()}({principal.Id})",
                },
                ["AccessMask"] = accessRightsCsv,
            },
        };

    /// <summary>
    /// Revokes a principal's POA share on a record — the OOB <c>RevokeAccess</c> action, app-only.
    /// </summary>
    /// <remarks>
    /// <para>unified-access-control-r2 task 060. Takes the SAME key shape as
    /// <see cref="GrantAccessAsync(string, Guid, DataversePrincipalRef, string, CancellationToken)"/>
    /// (entity set + record id + principal) — the A-13/FR-16 matcher lesson: a revoke keyed differently
    /// from its grant silently fails to match the row it was meant to remove.</para>
    ///
    /// <para><b>Not documented as idempotent</b> (corrected by task 063). This comment used to promise that revoking a
    /// share that does not exist is a Dataverse no-op; Microsoft Learn does not document that case. A caller that must
    /// be idempotent reads the shares first (<see cref="GetPrincipalAccessOrThrowAsync"/>) and skips the call when
    /// there is nothing to revoke.</para>
    ///
    /// <para><b>Not for the owner's own share.</b> Dataverse refuses an app-only revoke of the share held by the record's
    /// CURRENT owning user (0x80040223). A caller that may be revoking that share uses
    /// <see cref="RevokeAccessAsync(string, Guid, DataversePrincipalRef, DataversePrincipalRef, CancellationToken)"/>.</para>
    /// </remarks>
    public async Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        CancellationToken ct = default)
        => await SendShareWriteAsync(
            "RevokeAccess", RecordShareWrite.Revoke, entitySetName, recordId,
            RevokePayload(entitySetName, recordId, principal), ct);

    /// <summary>
    /// Revokes a principal's POA share on a record whose CURRENT owner the caller knows — app-only, except for the one
    /// share Dataverse lets nobody but its holder revoke: the share of the record's owning USER, which is revoked AS that
    /// user.
    /// </summary>
    /// <remarks>
    /// <para>unified-access-control-r2 (live on dev 2026-10-06). Dataverse refuses an app-only RevokeAccess of a share held
    /// by the record's current owning user — HTTP 400 <c>0x80040223</c> "Only owner can revoke access to the owner" — and
    /// accepts the same request sent as that user (<c>MSCRMCallerID</c>, 204; the BFF's application user holds
    /// <c>prvActOnBehalfOfAnotherUser</c>). So when <paramref name="recordOwner"/> is a systemuser AND is
    /// <paramref name="principal"/>, the request runs as the principal, through the shared request builder
    /// (<see cref="DataverseImpersonation.ApplyAsSystemUser"/>). The impersonated identity is therefore always the revokee
    /// itself and only when it owns the record: no other principal is ever impersonated, and a team (owner or principal)
    /// never is — every other combination is byte-for-byte the app-only revoke above.</para>
    /// <para>Same name as the app-only revoke on purpose: the POA guards recognise a client share write by the three names
    /// GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync, and this one sends through the same notifying sender.</para>
    /// </remarks>
    /// <param name="recordOwner">
    /// The record's CURRENT owner, as the caller last read it back. Used only to decide whether this is the owner's own
    /// share.
    /// </param>
    public async Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        DataversePrincipalRef recordOwner,
        CancellationToken ct = default)
        => await SendShareWriteAsync(
            "RevokeAccess", RecordShareWrite.Revoke, entitySetName, recordId,
            RevokePayload(entitySetName, recordId, principal), ct,
            impersonateSystemUserId: IsOwnersOwnShare(principal, recordOwner) ? principal.Id : null);

    /// <summary>
    /// Whether <paramref name="principal"/>'s share is the one held by the record's current owning USER — the share
    /// Dataverse lets only that user revoke. A team (as principal or as owner) never is.
    /// </summary>
    private static bool IsOwnersOwnShare(DataversePrincipalRef principal, DataversePrincipalRef recordOwner)
        => recordOwner.Kind == DataversePrincipalKind.SystemUser
           && principal.Kind == DataversePrincipalKind.SystemUser
           && recordOwner.Id == principal.Id
           && principal.Id != Guid.Empty;

    /// <summary>The <c>Target</c> + <c>Revokee</c> body RevokeAccess takes — the same key shape as a grant's.</summary>
    private static Dictionary<string, object> RevokePayload(string entitySetName, Guid recordId, DataversePrincipalRef principal)
        => new()
        {
            ["Target"] = new Dictionary<string, object>
            {
                ["@odata.id"] = $"{entitySetName}({recordId})",
            },
            ["Revokee"] = new Dictionary<string, object>
            {
                ["@odata.id"] = $"{principal.Kind.ToEntitySet()}({principal.Id})",
            },
        };

    /// <summary>
    /// Sends one POA share write and then, on EVERY path (returned, refused, thrown, cancelled), tells the
    /// <see cref="IRecordShareWriteObserver"/> which record it addressed. GrantAccessAsync, ModifyAccessAsync and
    /// RevokeAccessAsync all send through it.
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 132, main-session round 55. The access-cache eviction a share write needs used to be
    /// made by a caller-side seam, so it held only for callers that went through the seam. Here it is a property of the
    /// write: any call of the three share writes notifies — whoever made it and however (directly, through a delegate,
    /// reflection, a late binder, an expression tree). What lies outside it is a request that does not go through those
    /// methods: a raw HTTP call, including one assembled from this class's private members by reflection (its generic POST
    /// helper, request builder or <see cref="HttpClient"/>) — task 132's notes record that as a known limit.
    /// <para><paramref name="impersonateSystemUserId"/>: null (every grant and modify, and every revoke but the owner's
    /// own) sends app-only; set only by <see cref="RevokeAccessAsync(string, Guid, DataversePrincipalRef, DataversePrincipalRef, CancellationToken)"/> for
    /// the record owner's own share.</para>
    /// </remarks>
    private async Task SendShareWriteAsync(
        string action,
        RecordShareWrite write,
        string entitySetName,
        Guid recordId,
        Dictionary<string, object> payload,
        CancellationToken ct,
        Guid? impersonateSystemUserId = null)
    {
        try
        {
            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, action, ct, impersonateSystemUserId);
            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            // A write that reports failure, or whose caller went away, can still have committed: notify on every path.
            await NotifyShareWrittenAsync(entitySetName, recordId, write);
        }
    }

    /// <summary>
    /// Tells the observer about one share write. Never throws — the write's own outcome (its return or its exception) is
    /// what the caller sees — and is not bound to the caller's token: the write may already have committed, and a caller
    /// that went away must not leave the clean-up undone.
    /// </summary>
    private async Task NotifyShareWrittenAsync(string entitySetName, Guid recordId, RecordShareWrite write)
    {
        try
        {
            await _shareWriteObserver.OnRecordShareWrittenAsync(entitySetName, recordId, write, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ACCESS-EVICT] The share-write observer threw after the {Write} on {EntitySet} {RecordId}; the share " +
                "write's own outcome stands and the cached entries lapse on their TTLs.", write, entitySetName, recordId);
        }
    }

    /// <summary>
    /// Reads the principals with an ACTIVE POA share on a record, with the kind and rights mask of each
    /// share. Fails soft: a lookup error returns an empty list rather than throwing (callers treat
    /// "no shares" as the safe default — see
    /// <see cref="Sprk.Bff.Api.Services.Access.IDataverseRecordShareService"/>).
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 060: replaces <c>GetSharedSystemUserIdsAsync</c>, which selected only
    /// <c>principalid</c> and ASSUMED every share on the record was a systemuser. Now that teams are shared
    /// through this same seam, the assumption is unsafe, so <c>principaltypecode</c> is selected and the
    /// principal is returned typed — callers filter for the kind they mean. Rows whose principal type this
    /// seam does not model are skipped rather than guessed at.
    ///
    /// <para><b>Never decide a write from this read</b> (task 063). Its empty answer means "no shares" OR "the read
    /// failed", and a caller writing on it cannot tell which. Use <see cref="GetPrincipalAccessOrThrowAsync"/>.</para>
    /// </remarks>
    public async Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
    {
        // No object-type-code lookup: POA's objecttypecode holds the LOGICAL NAME (see PrincipalAccessQuery),
        // which the caller already supplied. This also removes a failure mode — the metadata read that
        // returned 0 and silently produced an empty share list.
        var response = await SendGetAsync(PrincipalAccessQuery(recordId, entityLogicalName), ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetPrincipalAccessAsync failed for {Entity}({RecordId}): {StatusCode}",
                entityLogicalName, recordId, response.StatusCode);
            return Array.Empty<DataversePrincipalAccess>();
        }

        var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        if (data?.Value is null)
            return Array.Empty<DataversePrincipalAccess>();

        return ReadPrincipalAccessRows(data.Value, entityLogicalName, recordId, strict: false);
    }

    /// <summary>
    /// Reads every principal with a POA share on a record — the same rows as <see cref="GetPrincipalAccessAsync"/> —
    /// but THROWS when it cannot give the complete answer, instead of answering with an empty list.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a second read</b> (unified-access-control-r2 task 063). The soft read's empty list is safe for a
    /// caller that only displays shares. It is not safe for one that decides a WRITE from them: "no share" picks
    /// GrantAccess for a principal who already holds one — and GrantAccess on an existing share is not documented to
    /// replace its rights — and reports "nothing to remove" for a share that exists. Here the answer is complete, or
    /// it is an exception.</para>
    ///
    /// <para><b>Incomplete counts as failed</b>: the table's object type code or the rows could not be read; a row has
    /// no readable principal or rights mask (to a caller deciding a write, an unreadable row is a share it cannot
    /// see); or the response continues on another page (<c>@odata.nextLink</c>), whose rows could include the very
    /// principal being changed. Rows naming a principal kind this seam does not model are skipped, as in the soft
    /// read: they are complete answers about principals no caller here asks about.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shares could not be read completely.</exception>
    public async Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
    {
        // No object-type-code lookup: POA's objecttypecode holds the LOGICAL NAME (see PrincipalAccessQuery),
        // which the caller already supplied. The "object type code could not be read" refusal is retired with
        // it — that metadata read is no longer on this path, so it can no longer fail it.
        using var response = await SendGetAsync(PrincipalAccessQuery(recordId, entityLogicalName), ct);
        if (!response.IsSuccessStatusCode)
            throw ShareReadFailed(entityLogicalName, recordId,
                $"Dataverse answered {(int)response.StatusCode} {response.StatusCode}");

        var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        if (data?.Value is null)
            throw ShareReadFailed(entityLogicalName, recordId, "the response carried no rows");

        if (data.NextLink is not null)
            throw ShareReadFailed(entityLogicalName, recordId, "the shares continue on another page");

        return ReadPrincipalAccessRows(data.Value, entityLogicalName, recordId, strict: true);
    }

    /// <summary>
    /// <paramref name="principalSystemUserId"/>'s EFFECTIVE rights on one record, as Dataverse answers them:
    /// <c>RetrievePrincipalAccess</c> bound to that user and asked AS that user (<c>MSCRMCallerID</c> impersonation) —
    /// the same question <c>CallerRecordAccessProbe</c> asks under a caller's own token, for a writer that acts for a user
    /// by impersonation and holds no token of theirs (unified-access-control-r2 task 146 c1-r1, owner round 13 item 8: a
    /// playbook that impersonates a user is checked under F3 as that user).
    /// </summary>
    /// <remarks>
    /// <para><b>An answer versus a fault.</b> <c>403</c> and <c>404</c> are Dataverse's answer that the user cannot see the
    /// record (404 is how it reports a record a principal cannot read), so they are <see cref="AccessRights.None"/>. Any
    /// other failure — throttling, a 5xx, an unreadable body — THROWS: a fault is never read as "no rights", so a caller
    /// can tell a refusal from a check that could not run.</para>
    /// </remarks>
    /// <param name="entitySetName">The record's Web API entity set (e.g. <c>sprk_matters</c>), from an explicit table.</param>
    /// <exception cref="ArgumentException"><paramref name="principalSystemUserId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="HttpRequestException">Dataverse did not answer.</exception>
    public async Task<AccessRights> RetrievePrincipalRightsAsync(
        Guid principalSystemUserId,
        string entitySetName,
        Guid recordId,
        CancellationToken ct = default)
    {
        if (principalSystemUserId == Guid.Empty)
            throw new ArgumentException("A principal systemuserid is required.", nameof(principalSystemUserId));

        var target = Uri.EscapeDataString($"{{\"@odata.id\":\"{entitySetName}({recordId:D})\"}}");
        using var response = await SendGetAsync(
            $"systemusers({principalSystemUserId:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1={target}",
            ct,
            impersonateSystemUserId: principalSystemUserId);

        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogInformation(
                "RetrievePrincipalAccess as {Principal} on {EntitySet}({RecordId}): {StatusCode} — no rights.",
                principalSystemUserId, entitySetName, recordId, (int)response.StatusCode);
            return AccessRights.None;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"RetrievePrincipalAccess as {principalSystemUserId:D} on {entitySetName}({recordId:D}) answered "
                + $"{(int)response.StatusCode} {response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(body);
        var rights = document.RootElement.TryGetProperty("AccessRights", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

        // An absent or empty rights string is an authoritative "no rights": Dataverse answered, and the answer was nothing.
        return DataverseAccessRightsMapper.FromAccessRightsString(rights);
    }

    /// <summary>How many records one batched share read names — keeps the OData filter far below the URL limit.</summary>
    public const int PrincipalAccessBatchSize = 25;

    /// <summary>
    /// The strict share read (<see cref="GetPrincipalAccessOrThrowAsync"/>) for MANY records of one table: every record
    /// asked about is in the answer (an empty list when it has no share), or the call throws.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a batched read</b> (unified-access-control-r2 task 149). The secure-child share synchronizer compares
    /// every child of a secure record with its root's shares; one GET per child would spend the application user's
    /// Dataverse request budget on a schedule. The records are read <see cref="PrincipalAccessBatchSize"/> at a time,
    /// with the same query shape the single read uses (logical-name <c>objecttypecode</c>, <c>changedon</c>; see
    /// <see cref="PrincipalAccessQuery"/>) plus <c>objectid</c>, verified live on spaarkedev1 on 2026-10-02.</para>
    /// <para><b>Same strictness, per batch.</b> A refused read, a missing <c>value</c>, a second page, a row with no
    /// readable record, principal, mask or <c>changedon</c>, or a row for a record that was not asked about: the whole
    /// call throws, so a caller deciding writes from it never mistakes "could not read" for "no share".</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shares could not be read completely.</exception>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
        string entityLogicalName,
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityLogicalName);
        ArgumentNullException.ThrowIfNull(recordIds);

        var answer = new Dictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>();
        foreach (var batch in recordIds.Where(id => id != Guid.Empty).Distinct().Chunk(PrincipalAccessBatchSize))
        {
            var asked = batch.ToHashSet();
            using var response = await SendGetAsync(PrincipalAccessBatchQuery(batch, entityLogicalName), ct);
            if (!response.IsSuccessStatusCode)
                throw ShareReadFailed(entityLogicalName, batch[0],
                    $"Dataverse answered {(int)response.StatusCode} {response.StatusCode} for a batch of {batch.Length}");

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data?.Value is null)
                throw ShareReadFailed(entityLogicalName, batch[0], "the batched response carried no rows");

            if (data.NextLink is not null)
                throw ShareReadFailed(entityLogicalName, batch[0], "the batched shares continue on another page");

            var byRecord = asked.ToDictionary(id => id, _ => new List<Dictionary<string, JsonElement>>());
            foreach (var row in data.Value)
            {
                if (!TryReadGuid(row, "objectid", out var objectId) || !byRecord.TryGetValue(objectId, out var rows))
                    throw ShareReadFailed(entityLogicalName, batch[0], "a share row names no record that was asked about");

                rows.Add(row);
            }

            foreach (var (id, rows) in byRecord)
                answer[id] = ReadPrincipalAccessRows(rows, entityLogicalName, id, strict: true);
        }

        return answer;
    }

    /// <summary>The batched read's query: every POA row of the named records of one table.</summary>
    private static string PrincipalAccessBatchQuery(IReadOnlyCollection<Guid> recordIds, string entityLogicalName)
        => $"principalobjectaccessset?$filter=objecttypecode eq '{entityLogicalName}' and ("
            + string.Join(" or ", recordIds.Select(id => $"objectid eq {id}"))
            + ")&$select=objectid,principalid,principaltypecode,accessrightsmask,changedon";

    /// <summary>The query both share reads issue: every POA row of one record.</summary>
    /// <remarks>
    /// 🔴 <b>Three faults were fixed here on 2026-09-22, each masking the next</b> — found because
    /// <c>GET /api/v1/external-access/user-shares</c> answered 500 for every caller, admin included, and
    /// verified one at a time against live Dataverse:
    /// <list type="number">
    /// <item><c>modifiedon</c> <b>does not exist on POA</b> — the column is <c>changedon</c>. Dataverse answered
    /// <c>400 "Could not find a property named 'modifiedon' on type Microsoft.Dynamics.CRM.principalobjectaccess"</c>.</item>
    /// <item><c>objecttypecode</c> on POA is <b><c>Edm.String</c></b>, not an integer. An unquoted numeric operand gave
    /// <c>400 "A binary operator with incompatible types … Found operand types 'Edm.String' and 'Edm.Int32'"</c>.</item>
    /// <item>The string it holds is the <b>LOGICAL NAME</b>, not the numeric type code rendered as text. Quoting the
    /// number gave <c>400 "The entity with a name = '10473' … was not found in the MetadataCache"</c>.</item>
    /// </list>
    /// <para>So the entity's numeric object-type code is <b>not an input to this query at all</b>; the logical name
    /// the caller already holds is. Verified: the corrected query answers <c>200</c>.</para>
    /// <para><b>Why this went unnoticed</b>: the SOFT read (<see cref="GetPrincipalAccessAsync"/>) shares this query
    /// and turns any non-success into an EMPTY LIST, so every share read in this environment reported "no shares"
    /// rather than failing. That is exactly the defect task 108 exists to remove, sitting inside the very query task
    /// 108's strict read depends on — the strict read is what finally surfaced it, by refusing instead of inventing
    /// an empty answer.</para>
    /// </remarks>
    private static string PrincipalAccessQuery(Guid recordId, string entityLogicalName)
        => $"principalobjectaccessset?$filter=objectid eq {recordId} and objecttypecode eq '{entityLogicalName}'"
            + "&$select=principalid,principaltypecode,accessrightsmask,changedon";

    /// <summary>
    /// Turns POA rows into typed shares. Rows naming a principal kind this seam does not model are skipped in both
    /// modes. A row whose principal or rights mask cannot be read is skipped (principal) or read as mask 0 when
    /// <paramref name="strict"/> is false — the soft read's long-standing behaviour — and THROWS when it is true.
    /// </summary>
    private static List<DataversePrincipalAccess> ReadPrincipalAccessRows(
        List<Dictionary<string, JsonElement>> rows, string entityLogicalName, Guid recordId, bool strict)
    {
        var results = new List<DataversePrincipalAccess>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryReadGuid(row, "principalid", out var principalId)
                || !TryReadPrincipalKind(row, out var kind))
            {
                if (strict)
                    throw ShareReadFailed(entityLogicalName, recordId, "a share row has no readable principal");

                continue;
            }

            if (kind is null)
                continue;

            if (!TryReadInt(row, "accessrightsmask", out var mask) && strict)
                throw ShareReadFailed(entityLogicalName, recordId, $"the share of {principalId} has no readable rights mask");

            // changedon is in the $select, so a value that cannot be read means an anomalous response. The soft
            // read keeps its long-standing fallback — its callers only display the value. The strict read refuses:
            // "incomplete counts as failed" must not carry an exception that reports a share as changed just now.
            // 🔴 The column is changedon, NOT modifiedon: POA has no modifiedon (see PrincipalAccessQuery). While
            // this read asked for modifiedon, the $select itself 400'd, so no row ever reached this branch.
            DateTimeOffset modifiedOn;
            if (row.TryGetValue("changedon", out var modifiedElement)
                && modifiedElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(modifiedElement.GetString(), out var parsedModified))
            {
                modifiedOn = parsedModified;
            }
            else if (strict)
            {
                throw ShareReadFailed(
                    entityLogicalName, recordId, $"the share of {principalId} has no readable changedon");
            }
            else
            {
                modifiedOn = DateTimeOffset.UtcNow;
            }

            results.Add(new DataversePrincipalAccess(
                new DataversePrincipalRef(kind.Value, principalId), mask, modifiedOn));
        }

        return results;
    }

    private static InvalidOperationException ShareReadFailed(string entityLogicalName, Guid recordId, string reason)
        => new($"The shares on {entityLogicalName}({recordId}) could not be read completely: {reason}.");

    private static bool TryReadGuid(Dictionary<string, JsonElement> row, string property, out Guid value)
    {
        value = Guid.Empty;
        return row.TryGetValue(property, out var element)
            && element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out value);
    }

    /// <summary>
    /// Reads a POA row's <c>principaltypecode</c>. <c>false</c> when the value is absent or is neither a number nor a
    /// string (unreadable); <c>true</c> with a <c>null</c> kind for a principal type this seam does not model.
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 139 (verified live on spaarkedev1, 2026-10-02): the Web API returns this
    /// EntityName column as the logical-name STRING — <c>"principaltypecode":"systemuser"</c> — not as the object type
    /// code. This method used to accept only a JSON number, so the soft read silently skipped every share and the
    /// strict read refused every record that had one. Both forms are accepted; the numeric one is kept for any reader
    /// (or test double) that supplies the code.
    /// </remarks>
    private static bool TryReadPrincipalKind(Dictionary<string, JsonElement> row, out DataversePrincipalKind? kind)
    {
        kind = null;
        if (!row.TryGetValue("principaltypecode", out var element))
            return false;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt32(out var code):
                kind = DataversePrincipalRefExtensions.FromPrincipalTypeCode(code);
                return true;
            case JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()):
                kind = DataversePrincipalRefExtensions.FromPrincipalTypeName(element.GetString());
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadInt(Dictionary<string, JsonElement> row, string property, out int value)
    {
        value = 0;
        return row.TryGetValue(property, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    public async Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default,
        Guid? impersonateSystemUserId = null)
    {
        // An empty caller id is refused by CreateAuthenticatedRequestAsync (task 104) before the PATCH is sent. That
        // request builder is the single enforcement point for every impersonated request, so there is deliberately
        // no second guard here to mask it; only the app-only EntitySetName lookup below may run first.
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

    /// <inheritdoc />
    public async Task UpdateExistingRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default)
    {
        if (fields.Count == 0)
        {
            _logger.LogDebug("No fields to update for {Entity}({Id})", entityLogicalName, recordId);
            return;
        }

        var entitySetName = await GetEntitySetNameAsync(entityLogicalName, ct);

        _logger.LogInformation(
            "Updating EXISTING record fields (If-Match: *, no create): {Entity}({Id}), {FieldCount} fields",
            entityLogicalName, recordId, fields.Count);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Patch, $"{entitySetName}({recordId})", ct);

        // "Prevent create in upsert": If-Match: * makes the PATCH apply only to a row that already exists.
        // Without it the Web API creates a row with this id (the behaviour UpdateRecordFieldsAsync keeps).
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        request.Content = JsonContent.Create(fields);

        using var response = await _httpClient.SendAsync(request, ct);

        // A missing row fails the precondition. Dataverse documents 404 for this case; 412 is also treated as
        // "not found" so a platform variation cannot turn into a silent 500 or, worse, a retry that creates.
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            _logger.LogWarning(
                "Update-only PATCH refused: {Entity}({Id}) does not exist ({StatusCode}); nothing was created",
                entityLogicalName, recordId, (int)response.StatusCode);
            throw new KeyNotFoundException($"{entityLogicalName} record was not found.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "UpdateExistingRecordFieldsAsync PATCH failed: {StatusCode} for {Entity}({Id}). Fields: [{FieldNames}]. Response: {ErrorBody}",
                response.StatusCode, entityLogicalName, recordId, string.Join(", ", fields.Keys), errorBody);
            response.EnsureSuccessStatusCode(); // still throw for the caller
        }

        _logger.LogDebug("Existing record updated: {Entity}({Id})", entityLogicalName, recordId);
    }

    /// <inheritdoc />
    public async Task UpdateRecordFieldsIfUnchangedAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        long expectedVersion,
        CancellationToken ct = default)
    {
        if (fields.Count == 0)
        {
            _logger.LogDebug("No fields to update for {Entity}({Id})", entityLogicalName, recordId);
            return;
        }

        var entitySetName = await GetEntitySetNameAsync(entityLogicalName, ct);

        _logger.LogInformation(
            "Updating record fields only if unchanged (If-Match version {Version}): {Entity}({Id}), {FieldCount} fields",
            expectedVersion, entityLogicalName, recordId, fields.Count);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Patch, $"{entitySetName}({recordId})", ct);

        // A Dataverse row's ETag is W/"<versionnumber>" (verified live, spaarkedev1 2026-10-01). A specific ETag
        // also prevents create: a row that does not exist cannot match it.
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(
            $"\"{expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"", isWeak: true));
        request.Content = JsonContent.Create(fields);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Conditional PATCH refused: {Entity}({Id}) does not exist; nothing was written", entityLogicalName, recordId);
            throw new KeyNotFoundException($"{entityLogicalName} record was not found.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            _logger.LogWarning(
                "Conditional PATCH refused: {Entity}({Id}) changed since version {Version}; nothing was written",
                entityLogicalName, recordId, expectedVersion);
            throw new System.Data.DBConcurrencyException(
                $"{entityLogicalName} record changed since it was read; the update was not applied.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "UpdateRecordFieldsIfUnchangedAsync PATCH failed: {StatusCode} for {Entity}({Id}). Fields: [{FieldNames}]. Response: {ErrorBody}",
                response.StatusCode, entityLogicalName, recordId, string.Join(", ", fields.Keys), errorBody);
            response.EnsureSuccessStatusCode(); // still throw for the caller
        }

        _logger.LogDebug("Record updated at expected version: {Entity}({Id})", entityLogicalName, recordId);
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

    /// <summary>
    /// The API's 0-7 regarding type of an event row, derived from its LOOKUPS (task 159, #1098): the one of the eight
    /// typed lookups the API's types use whose value equals <c>sprk_regardingrecordid</c>. Null when none does.
    /// </summary>
    /// <remarks>
    /// By value, not by "which lookup is set": a correctly written event regarding an invoice or analysis ALSO
    /// carries its core ancestor's stamp in sprk_regardingmatter/project/workassignment, so "the set lookup" would be
    /// ambiguous. <c>sprk_regardingrecordtype</c> is a lookup to <c>sprk_recordtype_ref</c> and is never read as a
    /// number.
    /// </remarks>
    internal static int? DeriveRegardingRecordType(Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("sprk_regardingrecordid", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idElement.GetString(), out var regardingId)
            || regardingId == Guid.Empty)
        {
            return null;
        }

        for (var type = RegardingRecordType.Project; type <= RegardingRecordType.Budget; type++)
        {
            var lookup = RegardingRecordType.GetLookupFieldName(type);
            if (lookup is not null
                && data.TryGetValue($"_{lookup}_value", out var value)
                && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var lookupId)
                && lookupId == regardingId)
            {
                return type;
            }
        }

        return null;
    }

    internal static EventEntity MapToEventEntity(Dictionary<string, JsonElement> data)
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
            // The expanded event type comes back under its NAVIGATION property (sprk_EventType_Ref); the logical name
            // is accepted too so a reader of either shape keeps the display name.
            EventTypeName = (data.TryGetValue("sprk_EventType_Ref", out var et) || data.TryGetValue("sprk_eventtype_ref", out et))
                && et.ValueKind == JsonValueKind.Object && et.TryGetProperty("sprk_name", out var etName)
                ? etName.GetString() : null,
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
            RegardingRecordType = DeriveRegardingRecordType(data),
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

        /// <summary>
        /// Present when more rows exist than this page returned. Read only by callers that must know the answer is
        /// complete (<see cref="GetPrincipalAccessOrThrowAsync"/>); every other reader keeps ignoring it.
        /// </summary>
        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private class ODataCountResponse
    {
        [JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();

        [JsonPropertyName("@odata.count")]
        public int Count { get; set; }
    }
}
