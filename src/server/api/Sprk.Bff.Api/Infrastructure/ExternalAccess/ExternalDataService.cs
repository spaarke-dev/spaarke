using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Queries Dataverse for project data on behalf of authenticated external users.
/// Uses managed identity (app-only) tokens to read sprk_projects, sprk_documents,
/// sprk_todos, contacts, and accounts from Dataverse.
///
/// All data access is gated by the ExternalCallerAuthorizationFilter — only records
/// belonging to projects in the caller's ExternalCallerContext.Participations are served.
///
/// ADR-009: Redis caching not applied here — participation is cached by ExternalParticipationService.
///          Individual data queries are short-lived; add caching only if profiling shows need.
///
/// ADR-024: To-do regarding context is applied via the four resolver fields
///          (sprk_regardingrecordtype, sprk_regardingrecordid, sprk_regardingrecordname,
///          sprk_regardingrecordurl) atomically with the specific sprk_regardingproject lookup.
///          Mirrors <see cref="Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder"/> semantics
///          but over the Dataverse Web API (no ServiceClient eager connection).
///
/// smart-todo-decoupling-r3 (FR-29): Replaces the legacy event-based to-do model
/// with first-class sprk_todo. Breaking change documented at
/// projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md.
/// </summary>
public class ExternalDataService
{
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly ILogger<ExternalDataService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    // Per-instance cache of sprk_recordtype_ref lookups (entity logical name → GUID + display name).
    // Mirrors TodoRegardingBuilder's _recordTypeRefCache.
    private readonly Dictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();
    private readonly SemaphoreSlim _recordTypeCacheSemaphore = new(1, 1);

    // ---------------------------------------------------------------------------
    // Private Dataverse deserialization row types
    // ---------------------------------------------------------------------------

    private sealed class ODataResult<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }

    private sealed class ProjectRow
    {
        [JsonPropertyName("sprk_projectid")] public string? SprkProjectid { get; set; }
        [JsonPropertyName("sprk_projectname")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_projectnumber")] public string? SprkReferencenumber { get; set; }
        [JsonPropertyName("sprk_projectdescription")] public string? SprkDescription { get; set; }
        [JsonPropertyName("sprk_issecure")] public bool? SprkIssecure { get; set; }
        [JsonPropertyName("statecode")] public int? SprkStatus { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("modifiedon")] public string? Modifiedon { get; set; }
    }

    private sealed class DocumentRow
    {
        [JsonPropertyName("sprk_documentid")] public string? SprkDocumentid { get; set; }
        [JsonPropertyName("sprk_documentname")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_documenttype")] public string? SprkDocumenttype { get; set; }
        [JsonPropertyName("sprk_filesummary")] public string? SprkSummary { get; set; }
        [JsonPropertyName("_sprk_project_value")] public string? SprkProjectidValue { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
    }

    /// <summary>
    /// Dataverse row for <c>sprk_todo</c>. Replaces legacy <c>EventRow</c>.
    /// </summary>
    private sealed class TodoRow
    {
        [JsonPropertyName("sprk_todoid")] public string? SprkTodoid { get; set; }
        [JsonPropertyName("sprk_name")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_notes")] public string? SprkNotes { get; set; }
        [JsonPropertyName("sprk_duedate")] public string? SprkDuedate { get; set; }
        [JsonPropertyName("sprk_priorityscore")] public int? SprkPriorityscore { get; set; }
        [JsonPropertyName("sprk_effortscore")] public int? SprkEffortscore { get; set; }
        [JsonPropertyName("sprk_todocolumn")] public int? SprkTodocolumn { get; set; }
        [JsonPropertyName("sprk_todopinned")] public bool? SprkTodopinned { get; set; }
        [JsonPropertyName("statecode")] public int? Statecode { get; set; }
        [JsonPropertyName("statuscode")] public int? Statuscode { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("_sprk_regardingproject_value")] public string? SprkRegardingprojectValue { get; set; }
        [JsonPropertyName("sprk_regardingrecordid")] public string? SprkRegardingrecordid { get; set; }
        [JsonPropertyName("sprk_regardingrecordname")] public string? SprkRegardingrecordname { get; set; }
        [JsonPropertyName("sprk_regardingrecordurl")] public string? SprkRegardingrecordurl { get; set; }
    }

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")] public string? Contactid { get; set; }
        [JsonPropertyName("fullname")] public string? Fullname { get; set; }
        [JsonPropertyName("firstname")] public string? Firstname { get; set; }
        [JsonPropertyName("lastname")] public string? Lastname { get; set; }
        [JsonPropertyName("emailaddress1")] public string? Emailaddress1 { get; set; }
        [JsonPropertyName("telephone1")] public string? Telephone1 { get; set; }
        [JsonPropertyName("jobtitle")] public string? Jobtitle { get; set; }
        [JsonPropertyName("_parentcustomerid_value")] public string? ParentcustomeridValue { get; set; }
    }

    private sealed class AccountRow
    {
        [JsonPropertyName("accountid")] public string? Accountid { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("websiteurl")] public string? Websiteurl { get; set; }
        [JsonPropertyName("telephone1")] public string? Telephone1 { get; set; }
        [JsonPropertyName("address1_city")] public string? Address1City { get; set; }
        [JsonPropertyName("address1_country")] public string? Address1Country { get; set; }
    }

    private sealed class AccessLinkRow
    {
        [JsonPropertyName("_sprk_contact_value")] public string? ContactId { get; set; }
    }

    /// <summary>
    /// Dataverse row for <c>sprk_recordtype_ref</c>. Used to resolve the regarding-record-type
    /// lookup target when applying resolver fields per ADR-024.
    /// </summary>
    private sealed class RecordTypeRefRow
    {
        [JsonPropertyName("sprk_recordtype_refid")] public string? Id { get; set; }
        [JsonPropertyName("sprk_recorddisplayname")] public string? DisplayName { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------------

    public ExternalDataService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<ExternalDataService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _credential = credential;
        _logger = logger;
    }

    // ---------------------------------------------------------------------------
    // Project queries
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Retrieves multiple projects by their IDs.
    /// Used by the workspace home page to list the user's accessible projects.
    /// </summary>
    public async Task<IReadOnlyList<ExternalProjectDto>> GetProjectsAsync(
        IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.ToList();
        if (ids.Count == 0) return [];

        var select = "sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon";
        var idFilter = string.Join(" or ", ids.Select(id => $"sprk_projectid eq {id}"));
        var url = $"{GetApiUrl()}/sprk_projects?$filter={Uri.EscapeDataString(idFilter)}&$select={select}&$orderby=sprk_name asc";

        var rows = await GetCollectionAsync<ProjectRow>(url, ct);
        return rows.Select(MapProject).ToList();
    }

    /// <summary>Retrieves a single project by ID.</summary>
    public async Task<ExternalProjectDto?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon";
        var url = $"{GetApiUrl()}/sprk_projects({projectId})?$select={select}";

        var row = await GetSingleAsync<ProjectRow>(url, ct);
        return row is null ? null : MapProject(row);
    }

    // ---------------------------------------------------------------------------
    // Document queries
    // ---------------------------------------------------------------------------

    /// <summary>Retrieves all documents belonging to the specified project.</summary>
    public async Task<IReadOnlyList<ExternalDocumentDto>> GetDocumentsAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value,createdon";
        var filter = Uri.EscapeDataString($"_sprk_project_value eq {projectId}");
        var url = $"{GetApiUrl()}/sprk_documents?$filter={filter}&$select={select}&$orderby=createdon desc&$top=200";

        var rows = await GetCollectionAsync<DocumentRow>(url, ct);
        return rows.Select(MapDocument).ToList();
    }

    // ---------------------------------------------------------------------------
    // To Do queries and mutations (smart-todo-decoupling-r3 FR-29)
    //
    // Replaces the legacy event-based to-do surface. To-dos are scoped
    // to a project via sprk_regardingproject (one of the 11 regarding lookups
    // on sprk_todo). When a create writes a project association, the four
    // resolver fields are applied atomically per ADR-024.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Retrieves all <c>sprk_todo</c> records whose regarding-project equals the supplied project id.
    /// </summary>
    /// <remarks>
    /// Uses the <c>sprk_regardingproject</c> lookup (one of 11 entity-specific regarding
    /// lookups on <c>sprk_todo</c>). NOT a polymorphic regarding lookup per ADR-024.
    /// </remarks>
    public async Task<IReadOnlyList<ExternalTodoDto>> GetTodosAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_todoid,sprk_name,sprk_notes,sprk_duedate,sprk_priorityscore,sprk_effortscore," +
                     "sprk_todocolumn,sprk_todopinned,statecode,statuscode,createdon," +
                     "_sprk_regardingproject_value,sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordurl";
        var filter = Uri.EscapeDataString($"_sprk_regardingproject_value eq {projectId}");
        var url = $"{GetApiUrl()}/sprk_todos?$filter={filter}&$select={select}&$orderby=sprk_duedate asc&$top=200";

        var rows = await GetCollectionAsync<TodoRow>(url, ct);
        return rows.Select(MapTodo).ToList();
    }

    /// <summary>
    /// Creates a new <c>sprk_todo</c> record in Dataverse, associated with the supplied project via
    /// <c>sprk_regardingproject</c>. The four resolver fields are populated atomically per ADR-024.
    /// </summary>
    /// <remarks>
    /// Returns the created to-do (Dataverse returns the created entity via Prefer: return=representation).
    /// Mirrors the regarding-application semantics of
    /// <see cref="Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder"/> over the Web API path.
    /// </remarks>
    public async Task<ExternalTodoDto> CreateTodoAsync(
        Guid projectId, CreateExternalTodoRequest request, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id must be a non-empty GUID.", nameof(projectId));

        var token = await GetAppOnlyTokenAsync(ct);

        // Look up project display name first — needed for sprk_regardingrecordname.
        // Returns null only if the project does not exist; we surface that as 400 via the caller.
        var project = await GetProjectByIdAsync(projectId, ct);
        var projectDisplayName = project?.SprkName ?? string.Empty;

        // Build the Dataverse to-do payload
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.SprkName))
            body["sprk_name"] = request.SprkName;
        if (!string.IsNullOrEmpty(request.SprkNotes))
            body["sprk_notes"] = request.SprkNotes;
        if (request.SprkDuedate is not null)
            body["sprk_duedate"] = request.SprkDuedate;
        if (request.SprkPriorityscore.HasValue)
            body["sprk_priorityscore"] = request.SprkPriorityscore.Value;
        if (request.SprkEffortscore.HasValue)
            body["sprk_effortscore"] = request.SprkEffortscore.Value;
        if (request.SprkTodocolumn.HasValue)
            body["sprk_todocolumn"] = request.SprkTodocolumn.Value;
        if (request.SprkTodopinned.HasValue)
            body["sprk_todopinned"] = request.SprkTodopinned.Value;

        // ADR-024: regarding-project lookup + 4 resolver fields applied atomically.
        body["sprk_regardingproject@odata.bind"] = $"/sprk_projects({projectId})";
        await ApplyResolverFieldsAsync(body, "sprk_project", projectId, projectDisplayName, ct);

        var url = $"{GetApiUrl()}/sprk_todos";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Headers.Add("Prefer", "return=representation");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[EXT-DATA] Create to-do failed for project {ProjectId}: {Status} — {Body}",
                projectId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to create to-do: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<TodoRow>(ct);
        if (row is null)
            throw new InvalidOperationException("Dataverse returned no to-do data after create");

        return MapTodo(row);
    }

    /// <summary>
    /// Updates an existing <c>sprk_todo</c> record (PATCH semantics — only provided fields are changed).
    /// </summary>
    /// <remarks>
    /// Regarding context cannot be changed via this surface — to re-parent a to-do, use the
    /// internal model-driven-app form which applies the resolver fields atomically per ADR-024.
    /// </remarks>
    public async Task UpdateTodoAsync(
        Guid todoId, UpdateExternalTodoRequest request, CancellationToken ct = default)
    {
        var token = await GetAppOnlyTokenAsync(ct);

        var body = new Dictionary<string, object?>();
        if (request.SprkName is not null) body["sprk_name"] = request.SprkName;
        if (request.SprkNotes is not null) body["sprk_notes"] = request.SprkNotes;
        if (request.SprkDuedate is not null) body["sprk_duedate"] = request.SprkDuedate;
        if (request.SprkPriorityscore.HasValue) body["sprk_priorityscore"] = request.SprkPriorityscore.Value;
        if (request.SprkEffortscore.HasValue) body["sprk_effortscore"] = request.SprkEffortscore.Value;
        if (request.SprkTodocolumn.HasValue) body["sprk_todocolumn"] = request.SprkTodocolumn.Value;
        if (request.SprkTodopinned.HasValue) body["sprk_todopinned"] = request.SprkTodopinned.Value;
        if (request.Statuscode.HasValue) body["statuscode"] = request.Statuscode.Value;

        if (body.Count == 0)
        {
            _logger.LogDebug("[EXT-DATA] UpdateTodo called with no fields to update for to-do {TodoId}", todoId);
            return;
        }

        var url = $"{GetApiUrl()}/sprk_todos({todoId})";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[EXT-DATA] Update to-do failed for to-do {TodoId}: {Status} — {Body}",
                todoId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to update to-do: {response.StatusCode}");
        }
    }

    // ---------------------------------------------------------------------------
    // Contact and organization queries
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Retrieves contacts with active access to the specified project.
    /// Queries sprk_externalrecordaccess to find contact IDs, then fetches contact details.
    /// </summary>
    public async Task<IReadOnlyList<ExternalContactDto>> GetContactsAsync(Guid projectId, CancellationToken ct = default)
    {
        // Step 1: Get contact IDs from the access junction table
        var contactIds = await GetProjectContactIdsAsync(projectId, ct);
        if (contactIds.Count == 0) return [];

        // Step 2: Fetch contact details for those IDs
        var select = "contactid,fullname,firstname,lastname,emailaddress1,telephone1,jobtitle,_parentcustomerid_value";
        var idFilter = string.Join(" or ", contactIds.Select(id => $"contactid eq {id}"));
        var url = $"{GetApiUrl()}/contacts?$filter={Uri.EscapeDataString(idFilter)}&$select={select}&$orderby=fullname asc";

        var rows = await GetCollectionAsync<ContactRow>(url, ct);
        return rows.Select(MapContact).ToList();
    }

    /// <summary>
    /// Retrieves organizations (accounts) linked to the project via project contacts.
    /// </summary>
    public async Task<IReadOnlyList<ExternalOrganizationDto>> GetOrganizationsAsync(Guid projectId, CancellationToken ct = default)
    {
        // Step 1: Get contacts for the project
        var contacts = await GetContactsAsync(projectId, ct);

        // Step 2: Collect unique account IDs from contacts
        var accountIds = contacts
            .Where(c => !string.IsNullOrEmpty(c.ParentcustomeridValue))
            .Select(c => c.ParentcustomeridValue!)
            .Distinct()
            .ToList();

        if (accountIds.Count == 0) return [];

        // Step 3: Fetch account details
        var select = "accountid,name,websiteurl,telephone1,address1_city,address1_country";
        var idFilter = string.Join(" or ", accountIds.Select(id => $"accountid eq {id}"));
        var url = $"{GetApiUrl()}/accounts?$filter={Uri.EscapeDataString(idFilter)}&$select={select}&$orderby=name asc";

        var rows = await GetCollectionAsync<AccountRow>(url, ct);
        return rows.Select(MapOrganization).ToList();
    }

    // ---------------------------------------------------------------------------
    // ADR-024 Resolver-field application (Web API path)
    //
    // Mirrors Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder.ApplyResolverFieldsAsync
    // but operates over the Dataverse Web API (no ServiceClient eager connect). When
    // sprk_recordtype_ref cannot be resolved, the type field is left unset and a warning
    // is logged — non-fatal, mirroring the SDK-path behaviour (correctness intact; only
    // the cross-entity-view icon is lost).
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Apply the four resolver fields to a to-do request body, alongside the specific regarding lookup.
    /// </summary>
    private async Task ApplyResolverFieldsAsync(
        Dictionary<string, object?> body,
        string regardingEntityName,
        Guid regardingId,
        string regardingDisplayName,
        CancellationToken ct)
    {
        if (regardingId == Guid.Empty)
            throw new ArgumentException("Regarding id must be a non-empty GUID.", nameof(regardingId));
        if (string.IsNullOrWhiteSpace(regardingEntityName))
            throw new ArgumentException("Regarding entity name is required.", nameof(regardingEntityName));

        var cleanId = regardingId.ToString("D").ToLowerInvariant();
        body["sprk_regardingrecordid"] = cleanId;
        body["sprk_regardingrecordname"] = regardingDisplayName ?? string.Empty;
        body["sprk_regardingrecordurl"] = BuildRecordUrl(regardingEntityName, cleanId);

        var recordTypeRef = await ResolveRecordTypeRefAsync(regardingEntityName, ct);
        if (recordTypeRef.HasValue)
        {
            body["sprk_regardingrecordtype@odata.bind"] =
                $"/sprk_recordtype_refs({recordTypeRef.Value.Id})";
        }
        else
        {
            _logger.LogWarning(
                "[EXT-DATA] sprk_recordtype_ref not found for entity '{Entity}'. Resolver type field left unset.",
                regardingEntityName);
        }
    }

    /// <summary>
    /// Build a Dataverse model-driven-app record URL for the resolver.
    /// </summary>
    /// <remarks>
    /// Returns a RELATIVE URL — the host origin is resolved by the model-driven app at click
    /// time. No org URL or tenant id is hard-coded here (product portability).
    /// </remarks>
    internal static string BuildRecordUrl(string entityLogicalName, string recordId)
    {
        return $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }

    /// <summary>
    /// Resolve the <c>sprk_recordtype_ref</c> GUID + display name for an entity logical name.
    /// Cached per service instance.
    /// </summary>
    private async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        await _recordTypeCacheSemaphore.WaitAsync(ct);
        try
        {
            // Re-check after acquiring lock
            if (_recordTypeRefCache.TryGetValue(entityLogicalName, out cached))
                return cached;

            var filter = Uri.EscapeDataString($"sprk_recordentitylogicalname eq '{entityLogicalName}'");
            var url = $"{GetApiUrl()}/sprk_recordtype_refs?$filter={filter}" +
                      "&$select=sprk_recordtype_refid,sprk_recorddisplayname&$top=1";

            var rows = await GetCollectionAsync<RecordTypeRefRow>(url, ct);
            var row = rows.FirstOrDefault();
            if (row?.Id is not null && Guid.TryParse(row.Id, out var refId))
            {
                var entry = (
                    Id: refId,
                    DisplayName: row.DisplayName ?? entityLogicalName
                );
                _recordTypeRefCache[entityLogicalName] = entry;
                return entry;
            }

            _recordTypeRefCache[entityLogicalName] = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EXT-DATA] Failed to query sprk_recordtype_ref for '{Entity}'. Caching negative result.",
                entityLogicalName);
            _recordTypeRefCache[entityLogicalName] = null;
            return null;
        }
        finally
        {
            _recordTypeCacheSemaphore.Release();
        }
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> GetProjectContactIdsAsync(Guid projectId, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"_sprk_project_value eq {projectId} and statecode eq 0");
        var url = $"{GetApiUrl()}/sprk_externalrecordaccesses?$filter={filter}&$select=_sprk_contact_value&$top=200";

        var rows = await GetCollectionAsync<AccessLinkRow>(url, ct);
        return rows
            .Where(r => !string.IsNullOrEmpty(r.ContactId))
            .Select(r => r.ContactId!)
            .Distinct()
            .ToList();
    }

    private async Task<IReadOnlyList<TRow>> GetCollectionAsync<TRow>(string url, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] GET collection failed: {Status} — {Url}", response.StatusCode, url);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResult<TRow>>(ct);
            return result?.Value ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error fetching collection: {Url}", url);
            return [];
        }
    }

    private async Task<TRow?> GetSingleAsync<TRow>(string url, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return default;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] GET single failed: {Status} — {Url}", response.StatusCode, url);
                return default;
            }

            return await response.Content.ReadFromJsonAsync<TRow>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error fetching single record: {Url}", url);
            return default;
        }
    }

    private async Task<string> GetAppOnlyTokenAsync(CancellationToken ct)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
            return _currentToken.Value.Token;

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new TimeoutException("Timed out waiting for Dataverse token");

        try
        {
            // Double-check inside the lock
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
                return _currentToken.Value.Token;

            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
                ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext([scope]), ct);

            _logger.LogDebug("[EXT-DATA] Acquired new Dataverse token, expires {ExpiresOn}", _currentToken.Value.ExpiresOn);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private string GetApiUrl()
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");
        return $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
    }

    // ---------------------------------------------------------------------------
    // Row → DTO mappers
    // ---------------------------------------------------------------------------

    private static ExternalProjectDto MapProject(ProjectRow r) => new()
    {
        SprkProjectid = r.SprkProjectid ?? "",
        SprkName = r.SprkName ?? "",
        SprkReferencenumber = r.SprkReferencenumber,
        SprkDescription = r.SprkDescription,
        SprkIssecure = r.SprkIssecure,
        SprkStatus = r.SprkStatus,
        Createdon = r.Createdon,
        Modifiedon = r.Modifiedon,
    };

    private static ExternalDocumentDto MapDocument(DocumentRow r) => new()
    {
        SprkDocumentid = r.SprkDocumentid ?? "",
        SprkName = r.SprkName ?? "",
        SprkDocumenttype = r.SprkDocumenttype,
        SprkSummary = r.SprkSummary,
        SprkProjectidValue = r.SprkProjectidValue,
        Createdon = r.Createdon,
    };

    private static ExternalTodoDto MapTodo(TodoRow r) => new()
    {
        SprkTodoid = r.SprkTodoid ?? "",
        SprkName = r.SprkName ?? "",
        SprkNotes = r.SprkNotes,
        SprkDuedate = r.SprkDuedate,
        SprkPriorityscore = r.SprkPriorityscore,
        SprkEffortscore = r.SprkEffortscore,
        SprkTodocolumn = r.SprkTodocolumn,
        SprkTodopinned = r.SprkTodopinned,
        Statecode = r.Statecode,
        Statuscode = r.Statuscode,
        Createdon = r.Createdon,
        SprkRegardingprojectValue = r.SprkRegardingprojectValue,
        SprkRegardingrecordid = r.SprkRegardingrecordid,
        SprkRegardingrecordname = r.SprkRegardingrecordname,
        SprkRegardingrecordurl = r.SprkRegardingrecordurl,
    };

    private static ExternalContactDto MapContact(ContactRow r) => new()
    {
        Contactid = r.Contactid ?? "",
        Fullname = r.Fullname,
        Firstname = r.Firstname,
        Lastname = r.Lastname,
        Emailaddress1 = r.Emailaddress1,
        Telephone1 = r.Telephone1,
        Jobtitle = r.Jobtitle,
        ParentcustomeridValue = r.ParentcustomeridValue,
    };

    private static ExternalOrganizationDto MapOrganization(AccountRow r) => new()
    {
        Accountid = r.Accountid ?? "",
        Name = r.Name ?? "",
        Websiteurl = r.Websiteurl,
        Telephone1 = r.Telephone1,
        Address1City = r.Address1City,
        Address1Country = r.Address1Country,
    };
}
