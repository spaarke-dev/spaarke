using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Dataverse service for sprk_registrationrequest CRUD operations, systemuser sync,
/// and team assignment in the Demo environment.
/// Uses S2S (client secret) auth targeting the Demo Dataverse URL from DemoProvisioningOptions.
/// ADR-010: Registered as concrete type (no interface).
/// </summary>
public class RegistrationDataverseService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<RegistrationDataverseService> _logger;
    private readonly DemoProvisioningOptions _options;
    private readonly TrackingIdGenerator _trackingIdGenerator;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    private const string EntitySetName = "sprk_registrationrequests";
    private const string SystemUserEntitySet = "systemusers";
    private const string TeamEntitySet = "teams";
    private const string BusinessUnitEntitySet = "businessunits";

    public RegistrationDataverseService(
        IConfiguration configuration,
        IOptions<DemoProvisioningOptions> options,
        TrackingIdGenerator trackingIdGenerator,
        ILogger<RegistrationDataverseService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _trackingIdGenerator = trackingIdGenerator;

        // Use the default environment's Dataverse URL (Demo environment, not dev)
        var defaultEnv = _options.Environments.FirstOrDefault(e => e.Name == _options.DefaultEnvironment)
            ?? _options.Environments.First();
        var dataverseUrl = defaultEnv.DataverseUrl;

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        // S2S auth using same app registration as dev (client credentials)
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["API_CLIENT_SECRET"];
        var tenantId = configuration["TENANT_ID"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                "RegistrationDataverseService requires TENANT_ID, API_APP_ID, and API_CLIENT_SECRET configuration.");
        }

        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _logger.LogInformation("RegistrationDataverseService targeting Demo Dataverse at {ApiUrl}", _apiUrl);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_apiUrl.TrimEnd('/') + "/")
        };
        _httpClient.DefaultRequestHeaders.Add("Prefer",
            "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
    }

    #region Token Management

    /// <summary>
    /// Thread-safe token refresh with double-check locking (same pattern as DataverseWebApiClient).
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            return _currentToken.Value.Token;

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new TimeoutException("Timed out waiting for Demo Dataverse token refresh");

        try
        {
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                return _currentToken.Value.Token;

            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), ct);
            _logger.LogDebug("Refreshed Demo Dataverse access token");
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(
        HttpMethod method, string url, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    #endregion

    #region Registration Request CRUD

    /// <summary>
    /// Creates a new sprk_registrationrequest record with auto-generated tracking ID.
    /// Returns the created record ID and tracking ID.
    /// </summary>
    public async Task<(Guid Id, string TrackingId)> CreateRequestAsync(
        RegistrationRequestCreate request, CancellationToken ct = default)
    {
        var trackingId = _trackingIdGenerator.Generate();
        var primaryName = $"{request.FirstName} {request.LastName} - {request.Organization}";

        var entity = new Dictionary<string, object?>
        {
            ["sprk_name"] = primaryName,
            ["sprk_firstname"] = request.FirstName,
            ["sprk_lastname"] = request.LastName,
            ["sprk_email"] = request.Email,
            ["sprk_organization"] = request.Organization,
            ["sprk_trackingid"] = trackingId,
            ["sprk_status"] = (int)RegistrationStatus.Submitted,
            ["sprk_requestdate"] = DateTimeOffset.UtcNow,
            ["sprk_consentaccepted"] = request.ConsentAccepted,
            ["sprk_consentdate"] = request.ConsentAccepted ? DateTimeOffset.UtcNow : null,
        };

        // Optional fields
        if (!string.IsNullOrEmpty(request.JobTitle))
            entity["sprk_jobtitle"] = request.JobTitle;
        if (!string.IsNullOrEmpty(request.Phone))
            entity["sprk_phone"] = request.Phone;
        if (request.UseCase.HasValue)
            entity["sprk_usecase"] = (int)request.UseCase.Value;
        if (request.ReferralSource.HasValue)
            entity["sprk_referralsource"] = (int)request.ReferralSource.Value;
        if (!string.IsNullOrEmpty(request.Notes))
            entity["sprk_notes"] = request.Notes;

        _logger.LogInformation("Creating registration request for {Email} with tracking ID {TrackingId}",
            request.Email, trackingId);

        using var httpRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Post, EntitySetName, ct);
        httpRequest.Content = JsonContent.Create(entity);
        var response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader == null)
            throw new InvalidOperationException("Failed to extract entity ID from Dataverse response");

        var id = Guid.Parse(entityIdHeader.Split('(', ')')[1]);
        _logger.LogInformation("Created registration request {Id} with tracking ID {TrackingId}", id, trackingId);
        return (id, trackingId);
    }

    /// <summary>
    /// Updates the status and related fields on a registration request.
    /// Uses sparse update (only changed fields).
    /// </summary>
    public async Task UpdateRequestStatusAsync(
        Guid requestId,
        RegistrationStatus newStatus,
        Dictionary<string, object?>? additionalFields = null,
        CancellationToken ct = default)
    {
        var entity = new Dictionary<string, object?>
        {
            ["sprk_status"] = (int)newStatus
        };

        if (additionalFields != null)
        {
            foreach (var kvp in additionalFields)
                entity[kvp.Key] = kvp.Value;
        }

        var url = $"{EntitySetName}({requestId})";
        _logger.LogInformation("Updating registration request {Id} to status {Status}", requestId, newStatus);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Patch, url, ct);
        request.Content = JsonContent.Create(entity);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Retrieves a single registration request by ID.
    /// </summary>
    public async Task<RegistrationRequestRecord?> GetRequestByIdAsync(Guid requestId, CancellationToken ct = default)
    {
        var select = string.Join(",", AllColumns);
        var url = $"{EntitySetName}({requestId})?$select={select}";

        _logger.LogDebug("GET registration request {Id}", requestId);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return MapToRecord(json);
    }

    /// <summary>
    /// Queries registration requests filtered by status.
    /// </summary>
    public async Task<List<RegistrationRequestRecord>> GetRequestsByStatusAsync(
        RegistrationStatus status, int? top = null, CancellationToken ct = default)
    {
        var select = string.Join(",", AllColumns);
        var filter = $"sprk_status eq {(int)status}";
        var queryParts = new List<string>
        {
            $"$filter={filter}",
            $"$select={select}",
            "$orderby=sprk_requestdate asc"
        };
        if (top.HasValue)
            queryParts.Add($"$top={top.Value}");

        var url = $"{EntitySetName}?{string.Join("&", queryParts)}";

        _logger.LogDebug("GET registration requests by status {Status}", status);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        return result?.Value?.Select(MapToRecord).ToList() ?? new List<RegistrationRequestRecord>();
    }

    /// <summary>
    /// Checks if an active or pending registration request already exists for the given email.
    /// Returns the existing record if found, null otherwise.
    /// Active statuses: Submitted, Approved, Provisioned (not Rejected, Expired, Revoked).
    /// </summary>
    public async Task<RegistrationRequestRecord?> CheckDuplicateByEmailAsync(
        string email, CancellationToken ct = default)
    {
        // Filter for active statuses: Submitted(0), Approved(1), Provisioned(3)
        var filter = $"sprk_email eq '{EscapeODataValue(email)}' and " +
                     $"(sprk_status eq {(int)RegistrationStatus.Submitted} or " +
                     $"sprk_status eq {(int)RegistrationStatus.Approved} or " +
                     $"sprk_status eq {(int)RegistrationStatus.Provisioned})";
        var select = string.Join(",", AllColumns);
        var url = $"{EntitySetName}?$filter={filter}&$select={select}&$top=1";

        _logger.LogDebug("Checking duplicate registration for email {Email}", email);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        var match = result?.Value?.FirstOrDefault();
        return match != null ? MapToRecord(match.Value) : null;
    }

    #endregion

    #region Systemuser Operations

    /// <summary>
    /// Creates a systemuser in the Demo Dataverse environment.
    /// Uses azureactivedirectoryobjectid binding and associates with the configured business unit.
    /// </summary>
    public async Task<Guid> CreateSystemUserAsync(
        string azureAdObjectId,
        string firstName,
        string lastName,
        string email,
        string businessUnitName,
        CancellationToken ct = default)
    {
        // First, resolve the business unit ID by name
        var buId = await ResolveBusinessUnitIdAsync(businessUnitName, ct);

        var entity = new Dictionary<string, object?>
        {
            ["firstname"] = firstName,
            ["lastname"] = lastName,
            ["internalemailaddress"] = email,
            ["azureactivedirectoryobjectid"] = azureAdObjectId,
            ["accessmode"] = 0, // Read-Write
            ["isdisabled"] = false,
            // Bind to business unit via OData navigation property
            ["businessunitid@odata.bind"] = $"/{BusinessUnitEntitySet}({buId})"
        };

        _logger.LogInformation(
            "Creating systemuser in Demo Dataverse for AAD object {ObjectId}, BU {BuName}",
            azureAdObjectId, businessUnitName);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, SystemUserEntitySet, ct);
        request.Content = JsonContent.Create(entity);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader == null)
            throw new InvalidOperationException("Failed to extract systemuser ID from Dataverse response");

        var userId = Guid.Parse(entityIdHeader.Split('(', ')')[1]);
        _logger.LogInformation("Created systemuser {UserId} in Demo Dataverse", userId);
        return userId;
    }

    #endregion

    #region Team Membership

    /// <summary>
    /// Adds a systemuser to a team via teammembership_association/$ref.
    /// </summary>
    public async Task AddUserToTeamAsync(
        string teamName, Guid systemUserId, CancellationToken ct = default)
    {
        var teamId = await ResolveTeamIdAsync(teamName, ct);
        var navigationUrl = $"{TeamEntitySet}({teamId})/teammembership_association/$ref";

        _logger.LogInformation("Adding systemuser {UserId} to team {TeamName}", systemUserId, teamName);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, navigationUrl, ct);
        var refBody = new Dictionary<string, string>
        {
            ["@odata.id"] = $"{SystemUserEntitySet}({systemUserId})"
        };
        request.Content = JsonContent.Create(refBody);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Added systemuser {UserId} to team {TeamName}", systemUserId, teamName);
    }

    /// <summary>
    /// Removes a systemuser from a team via teammembership_association/$ref DELETE.
    /// </summary>
    public async Task RemoveUserFromTeamAsync(
        string teamName, Guid systemUserId, CancellationToken ct = default)
    {
        var teamId = await ResolveTeamIdAsync(teamName, ct);
        var navigationUrl = $"{TeamEntitySet}({teamId})/teammembership_association({systemUserId})/$ref";

        _logger.LogInformation("Removing systemuser {UserId} from team {TeamName}", systemUserId, teamName);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Delete, navigationUrl, ct);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Removed systemuser {UserId} from team {TeamName}", systemUserId, teamName);
    }

    #endregion

    #region Helpers

    private async Task<Guid> ResolveBusinessUnitIdAsync(string businessUnitName, CancellationToken ct)
    {
        var filter = $"name eq '{EscapeODataValue(businessUnitName)}'";
        var url = $"{BusinessUnitEntitySet}?$filter={filter}&$select=businessunitid&$top=1";

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        var bu = result?.Value?.FirstOrDefault();
        if (bu == null)
            throw new InvalidOperationException($"Business unit '{businessUnitName}' not found in Demo Dataverse");

        return bu.Value.GetProperty("businessunitid").GetGuid();
    }

    private async Task<Guid> ResolveTeamIdAsync(string teamName, CancellationToken ct)
    {
        var filter = $"name eq '{EscapeODataValue(teamName)}'";
        var url = $"{TeamEntitySet}?$filter={filter}&$select=teamid&$top=1";

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        var team = result?.Value?.FirstOrDefault();
        if (team == null)
            throw new InvalidOperationException($"Team '{teamName}' not found in Demo Dataverse");

        return team.Value.GetProperty("teamid").GetGuid();
    }

    private static string EscapeODataValue(string value)
    {
        return value.Replace("'", "''");
    }

    private static readonly string[] AllColumns = new[]
    {
        "sprk_registrationrequestid", "sprk_name", "sprk_firstname", "sprk_lastname",
        "sprk_email", "sprk_organization", "sprk_jobtitle", "sprk_phone",
        "sprk_usecase", "sprk_referralsource", "sprk_notes", "sprk_status",
        "sprk_trackingid", "sprk_requestdate", "sprk_reviewdate", "sprk_rejectionreason",
        "sprk_demousername", "sprk_demouserobjectid", "sprk_provisioneddate",
        "sprk_expirationdate", "sprk_consentaccepted", "sprk_consentdate"
    };

    private static RegistrationRequestRecord MapToRecord(JsonElement json)
    {
        return new RegistrationRequestRecord
        {
            Id = json.TryGetProperty("sprk_registrationrequestid", out var idProp) ? idProp.GetGuid() : Guid.Empty,
            Name = json.TryGetProperty("sprk_name", out var nameProp) ? nameProp.GetString() : null,
            FirstName = json.TryGetProperty("sprk_firstname", out var fnProp) ? fnProp.GetString() : null,
            LastName = json.TryGetProperty("sprk_lastname", out var lnProp) ? lnProp.GetString() : null,
            Email = json.TryGetProperty("sprk_email", out var emailProp) ? emailProp.GetString() : null,
            Organization = json.TryGetProperty("sprk_organization", out var orgProp) ? orgProp.GetString() : null,
            JobTitle = json.TryGetProperty("sprk_jobtitle", out var jtProp) ? jtProp.GetString() : null,
            Phone = json.TryGetProperty("sprk_phone", out var phProp) ? phProp.GetString() : null,
            UseCase = json.TryGetProperty("sprk_usecase", out var ucProp) && ucProp.ValueKind == JsonValueKind.Number
                ? (UseCaseOption)ucProp.GetInt32() : null,
            ReferralSource = json.TryGetProperty("sprk_referralsource", out var rsProp) && rsProp.ValueKind == JsonValueKind.Number
                ? (ReferralSourceOption)rsProp.GetInt32() : null,
            Notes = json.TryGetProperty("sprk_notes", out var notesProp) ? notesProp.GetString() : null,
            Status = json.TryGetProperty("sprk_status", out var statusProp) && statusProp.ValueKind == JsonValueKind.Number
                ? (RegistrationStatus)statusProp.GetInt32() : RegistrationStatus.Submitted,
            TrackingId = json.TryGetProperty("sprk_trackingid", out var tidProp) ? tidProp.GetString() : null,
            RequestDate = json.TryGetProperty("sprk_requestdate", out var rdProp) && rdProp.ValueKind != JsonValueKind.Null
                ? rdProp.GetDateTimeOffset() : null,
            ReviewDate = json.TryGetProperty("sprk_reviewdate", out var rvdProp) && rvdProp.ValueKind != JsonValueKind.Null
                ? rvdProp.GetDateTimeOffset() : null,
            RejectionReason = json.TryGetProperty("sprk_rejectionreason", out var rrProp) ? rrProp.GetString() : null,
            DemoUsername = json.TryGetProperty("sprk_demousername", out var duProp) ? duProp.GetString() : null,
            DemoUserObjectId = json.TryGetProperty("sprk_demouserobjectid", out var doidProp) ? doidProp.GetString() : null,
            ProvisionedDate = json.TryGetProperty("sprk_provisioneddate", out var pdProp) && pdProp.ValueKind != JsonValueKind.Null
                ? pdProp.GetDateTimeOffset() : null,
            ExpirationDate = json.TryGetProperty("sprk_expirationdate", out var edProp) && edProp.ValueKind != JsonValueKind.Null
                ? edProp.GetDateTimeOffset() : null,
            ConsentAccepted = json.TryGetProperty("sprk_consentaccepted", out var caProp) && caProp.ValueKind == JsonValueKind.True,
            ConsentDate = json.TryGetProperty("sprk_consentdate", out var cdProp) && cdProp.ValueKind != JsonValueKind.Null
                ? cdProp.GetDateTimeOffset() : null,
        };
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        _tokenSemaphore?.Dispose();
        _httpClient?.Dispose();
    }

    #endregion
}

#region Models

/// <summary>
/// Registration request status values (maps to sprk_status choice field).
/// </summary>
public enum RegistrationStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    Provisioned = 3,
    Expired = 4,
    Revoked = 5
}

/// <summary>
/// Use case options (maps to sprk_usecase choice field).
/// </summary>
public enum UseCaseOption
{
    DocumentManagement = 0,
    AiAnalysis = 1,
    FinancialIntelligence = 2,
    General = 3
}

/// <summary>
/// Referral source options (maps to sprk_referralsource choice field).
/// </summary>
public enum ReferralSourceOption
{
    Conference = 0,
    Website = 1,
    Referral = 2,
    Search = 3,
    Other = 4
}

/// <summary>
/// Input model for creating a new registration request.
/// </summary>
public class RegistrationRequestCreate
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string Organization { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public UseCaseOption? UseCase { get; set; }
    public ReferralSourceOption? ReferralSource { get; set; }
    public string? Notes { get; set; }
    public bool ConsentAccepted { get; set; }
}

/// <summary>
/// Read model for a registration request record from Dataverse.
/// Maps all sprk_registrationrequest columns.
/// </summary>
public class RegistrationRequestRecord
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public UseCaseOption? UseCase { get; set; }
    public ReferralSourceOption? ReferralSource { get; set; }
    public string? Notes { get; set; }
    public RegistrationStatus Status { get; set; }
    public string? TrackingId { get; set; }
    public DateTimeOffset? RequestDate { get; set; }
    public DateTimeOffset? ReviewDate { get; set; }
    public string? RejectionReason { get; set; }
    public string? DemoUsername { get; set; }
    public string? DemoUserObjectId { get; set; }
    public DateTimeOffset? ProvisionedDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public bool ConsentAccepted { get; set; }
    public DateTimeOffset? ConsentDate { get; set; }
}

/// <summary>
/// OData collection response wrapper for JSON deserialization.
/// </summary>
internal class ODataCollectionResponse
{
    [JsonPropertyName("value")]
    public List<JsonElement>? Value { get; set; }

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

#endregion
