using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Queries Dataverse for project data on behalf of authenticated external users.
/// Uses managed identity (app-only) tokens to read sprk_projects, sprk_documents,
/// sprk_events, contacts, and accounts from Dataverse.
///
/// All data access is gated by the ExternalCallerAuthorizationFilter — only records
/// belonging to projects in the caller's ExternalCallerContext.Participations are served.
///
/// ADR-009: Redis caching not applied here — participation is cached by ExternalParticipationService.
///          Individual data queries are short-lived; add caching only if profiling shows need.
/// </summary>
public class ExternalDataService
{
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalDataService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

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

    private sealed class EventRow
    {
        [JsonPropertyName("sprk_eventid")] public string? SprkEventid { get; set; }
        [JsonPropertyName("sprk_eventname")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_duedate")] public string? SprkDuedate { get; set; }
        [JsonPropertyName("sprk_eventstatus")] public int? SprkStatus { get; set; }
        [JsonPropertyName("sprk_todoflag")] public bool? SprkTodoflag { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("_sprk_regardingproject_value")] public string? SprkProjectidValue { get; set; }
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

    // ---------------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------------

    public ExternalDataService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ExternalDataService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
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
    // Event queries and mutations
    // ---------------------------------------------------------------------------

    /// <summary>Retrieves all events belonging to the specified project.</summary>
    public async Task<IReadOnlyList<ExternalEventDto>> GetEventsAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_eventid,sprk_eventname,sprk_duedate,sprk_eventstatus,sprk_todoflag,_sprk_regardingproject_value,createdon";
        var filter = Uri.EscapeDataString($"_sprk_regardingproject_value eq {projectId}");
        var url = $"{GetApiUrl()}/sprk_events?$filter={filter}&$select={select}&$orderby=sprk_duedate asc&$top=200";

        var rows = await GetCollectionAsync<EventRow>(url, ct);
        return rows.Select(MapEvent).ToList();
    }

    /// <summary>
    /// Creates a new event record in Dataverse linked to the specified project.
    /// Returns the created event (Dataverse returns the created entity via Prefer: return=representation).
    /// </summary>
    public async Task<ExternalEventDto> CreateEventAsync(
        Guid projectId, CreateExternalEventRequest request, CancellationToken ct = default)
    {
        var token = await GetAppOnlyTokenAsync(ct);

        // Build the Dataverse event payload
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.SprkName))
            body["sprk_name"] = request.SprkName;
        if (request.SprkDuedate is not null)
            body["sprk_duedate"] = request.SprkDuedate;
        if (request.SprkStatus.HasValue)
            body["sprk_status"] = request.SprkStatus.Value;
        if (request.SprkTodoflag.HasValue)
            body["sprk_todoflag"] = request.SprkTodoflag.Value;

        // OData bind to associate the event with the project
        body["sprk_projectid@odata.bind"] = $"/sprk_projects({projectId})";

        var url = $"{GetApiUrl()}/sprk_events";
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
            _logger.LogWarning("[EXT-DATA] Create event failed for project {ProjectId}: {Status} — {Body}",
                projectId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to create event: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<EventRow>(ct);
        if (row is null)
            throw new InvalidOperationException("Dataverse returned no event data after create");

        return MapEvent(row);
    }

    /// <summary>
    /// Updates an existing event record in Dataverse (PATCH semantics — only provided fields are changed).
    /// </summary>
    public async Task UpdateEventAsync(
        Guid eventId, UpdateExternalEventRequest request, CancellationToken ct = default)
    {
        var token = await GetAppOnlyTokenAsync(ct);

        var body = new Dictionary<string, object?>();
        if (request.SprkName is not null) body["sprk_name"] = request.SprkName;
        if (request.SprkDuedate is not null) body["sprk_duedate"] = request.SprkDuedate;
        if (request.SprkStatus.HasValue) body["sprk_status"] = request.SprkStatus.Value;
        if (request.SprkTodoflag.HasValue) body["sprk_todoflag"] = request.SprkTodoflag.Value;

        if (body.Count == 0)
        {
            _logger.LogDebug("[EXT-DATA] UpdateEvent called with no fields to update for event {EventId}", eventId);
            return;
        }

        var url = $"{GetApiUrl()}/sprk_events({eventId})";
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
            _logger.LogWarning("[EXT-DATA] Update event failed for event {EventId}: {Status} — {Body}",
                eventId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to update event: {response.StatusCode}");
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

            var managedIdentityClientId = _configuration["ManagedIdentity:ClientId"]
                ?? throw new InvalidOperationException("ManagedIdentity:ClientId is required");

            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = managedIdentityClientId
            });

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await credential.GetTokenAsync(new TokenRequestContext([scope]), ct);

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

    private static ExternalEventDto MapEvent(EventRow r) => new()
    {
        SprkEventid = r.SprkEventid ?? "",
        SprkName = r.SprkName ?? "",
        SprkDuedate = r.SprkDuedate,
        SprkStatus = r.SprkStatus,
        SprkTodoflag = r.SprkTodoflag,
        Createdon = r.Createdon,
        SprkProjectidValue = r.SprkProjectidValue,
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
