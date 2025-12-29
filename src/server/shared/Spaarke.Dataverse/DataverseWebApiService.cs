using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse service implementation using Web API (REST) instead of ServiceClient.
/// Provides full .NET 8.0 compatibility without WCF/System.ServiceModel dependencies.
/// Uses IHttpClientFactory for proper HttpClient management.
/// </summary>
public class DataverseWebApiService : IDataverseService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseWebApiService> _logger;
    private AccessToken? _currentToken;
    private readonly string _entitySetName = "sprk_documents";

    public DataverseWebApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseWebApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["Dataverse:ClientSecret"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("TENANT_ID configuration is required");

        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("API_APP_ID configuration is required");

        if (string.IsNullOrEmpty(clientSecret))
            throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Initialized Dataverse Web API service for {ApiUrl}", _apiUrl);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token");
        }
    }

    public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var payload = new
        {
            sprk_documentname = request.Name,
            sprk_documentdescription = request.Description,
            statuscode = 1, // Draft
            statecode = 0    // Active state
        };

        _logger.LogInformation("Creating document: {Name}", request.Name);

        var response = await _httpClient.PostAsJsonAsync(_entitySetName, payload, ct);
        response.EnsureSuccessStatusCode();

        // Extract ID from OData-EntityId header: ...sprk_documents(guid)
        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader != null)
        {
            var idString = entityIdHeader.Split('(', ')')[1];
            _logger.LogInformation("Document created with ID: {Id}", idString);
            return idString;
        }

        throw new InvalidOperationException("Failed to extract entity ID from create response");
    }

    public async Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(id, out var guid))
        {
            _logger.LogWarning("Invalid document ID format: {Id}", id);
            return null;
        }

        var url = $"{_entitySetName}({guid})";
        _logger.LogDebug("Retrieving document: {Id}", id);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Document not found: {Id}", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null) return null;

            return MapToDocumentEntity(data, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Failed to retrieve document {Id}", id);
            throw;
        }
    }

    public async Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(id, out var guid))
        {
            throw new ArgumentException($"Invalid document ID format: {id}", nameof(id));
        }

        var payload = new Dictionary<string, object?>();

        if (request.Name != null) payload["sprk_documentname"] = request.Name;
        if (request.Description != null) payload["sprk_documentdescription"] = request.Description;
        if (request.FileName != null) payload["sprk_filename"] = request.FileName;
        if (request.FileSize.HasValue) payload["sprk_filesize"] = request.FileSize.Value;
        if (request.MimeType != null) payload["sprk_filetype"] = request.MimeType;
        if (request.GraphItemId != null) payload["sprk_graphitemid"] = request.GraphItemId;
        if (request.GraphDriveId != null) payload["sprk_graphdriveid"] = request.GraphDriveId;
        if (request.HasFile.HasValue) payload["sprk_hasfile"] = request.HasFile.Value;
        if (request.Status.HasValue) payload["statuscode"] = (int)request.Status.Value;

        // AI Analysis fields
        if (request.Summary != null) payload["sprk_filesummary"] = request.Summary;
        if (request.TlDr != null) payload["sprk_filetldr"] = request.TlDr;
        if (request.Keywords != null) payload["sprk_filekeywords"] = request.Keywords;
        if (request.SummaryStatus.HasValue) payload["sprk_filesummarystatus"] = request.SummaryStatus.Value;

        // Extracted entities fields
        if (request.ExtractOrganization != null) payload["sprk_extractorganization"] = request.ExtractOrganization;
        if (request.ExtractPeople != null) payload["sprk_extractpeople"] = request.ExtractPeople;
        if (request.ExtractFees != null) payload["sprk_extractfees"] = request.ExtractFees;
        if (request.ExtractDates != null) payload["sprk_extractdates"] = request.ExtractDates;
        if (request.ExtractReference != null) payload["sprk_extractreference"] = request.ExtractReference;
        if (request.ExtractDocumentType != null) payload["sprk_extractdocumenttype"] = request.ExtractDocumentType;
        if (request.DocumentType.HasValue) payload["sprk_documenttype"] = request.DocumentType.Value;

        // Email metadata fields (for .eml and .msg files)
        if (request.EmailSubject != null) payload["sprk_emailsubject"] = request.EmailSubject;
        if (request.EmailFrom != null) payload["sprk_emailfrom"] = request.EmailFrom;
        if (request.EmailTo != null) payload["sprk_emailto"] = request.EmailTo;
        if (request.EmailDate.HasValue) payload["sprk_emaildate"] = request.EmailDate.Value;
        if (request.EmailBody != null) payload["sprk_emailbody"] = request.EmailBody;
        if (request.EmailCc != null) payload["sprk_emailcc"] = request.EmailCc;
        if (request.EmailMessageId != null) payload["sprk_emailmessageid"] = request.EmailMessageId;
        if (request.EmailDirection.HasValue) payload["sprk_emaildirection"] = request.EmailDirection.Value;
        if (request.EmailTrackingToken != null) payload["sprk_emailtrackingtoken"] = request.EmailTrackingToken;
        if (request.EmailConversationIndex != null) payload["sprk_emailconversationindex"] = request.EmailConversationIndex;
        if (request.IsEmailArchive.HasValue) payload["sprk_isemailarchive"] = request.IsEmailArchive.Value;
        if (request.RelationshipType.HasValue) payload["sprk_relationshiptype"] = request.RelationshipType.Value;
        if (request.Attachments != null) payload["sprk_attachments"] = request.Attachments;
        // Email activity lookup uses @odata.bind
        if (request.EmailLookup.HasValue)
            payload["sprk_Email@odata.bind"] = $"/emails({request.EmailLookup.Value})";

        // Parent document fields (for email attachments)
        if (request.ParentDocumentId != null) payload["sprk_parentdocumentid"] = request.ParentDocumentId;
        if (request.ParentFileName != null) payload["sprk_parentfilename"] = request.ParentFileName;
        if (request.ParentGraphItemId != null) payload["sprk_parentgraphitemid"] = request.ParentGraphItemId;
        // ParentDocumentLookup uses @odata.bind for lookup fields
        if (request.ParentDocumentLookup.HasValue)
            payload["sprk_ParentDocumentName@odata.bind"] = $"/sprk_documents({request.ParentDocumentLookup.Value})";

        // Record association lookups (Phase 2 - Record Matching)
        if (request.MatterLookup.HasValue)
            payload["sprk_Matter@odata.bind"] = $"/sprk_matters({request.MatterLookup.Value})";
        if (request.ProjectLookup.HasValue)
            payload["sprk_Project@odata.bind"] = $"/sprk_projects({request.ProjectLookup.Value})";
        if (request.InvoiceLookup.HasValue)
            payload["sprk_Invoice@odata.bind"] = $"/sprk_invoices({request.InvoiceLookup.Value})";

        var url = $"{_entitySetName}({guid})";

        // Log the actual payload for debugging email field persistence
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        _logger.LogInformation(
            "Updating document {Id} with payload ({FieldCount} fields): {Payload}",
            id, payload.Count, payloadJson);

        // Check specifically for email fields in the payload
        var emailFieldsInPayload = payload.Keys.Where(k => k.StartsWith("sprk_email")).ToList();
        if (emailFieldsInPayload.Any())
        {
            _logger.LogInformation(
                "Email fields in payload for document {Id}: {EmailFields}",
                id, string.Join(", ", emailFieldsInPayload));
        }

        var response = await _httpClient.PatchAsJsonAsync(url, payload, ct);

        // Log response status and any error details
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to update document {Id}: Status={Status}, Response={Response}",
                id, response.StatusCode, errorContent);
        }

        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Document updated successfully: {Id}, Status={Status}", id, response.StatusCode);
    }

    public async Task DeleteDocumentAsync(string id, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(id, out var guid))
        {
            throw new ArgumentException($"Invalid document ID format: {id}", nameof(id));
        }

        var url = $"{_entitySetName}({guid})";
        _logger.LogInformation("Deleting document: {Id}", id);

        var response = await _httpClient.DeleteAsync(url, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Document deleted successfully: {Id}", id);
    }

    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(containerId, out _))
        {
            throw new ArgumentException($"Invalid container ID format: {containerId}", nameof(containerId));
        }

        // OData filter for container lookup
        var filter = $"_sprk_containerid_value eq {containerId}";
        var url = $"{_entitySetName}?$filter={Uri.EscapeDataString(filter)}";

        _logger.LogDebug("Querying documents by container: {ContainerId}", containerId);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        if (result?.Value == null) return Enumerable.Empty<DocumentEntity>();

        return result.Value.Select(data => MapToDocumentEntity(data, data["sprk_documentid"].GetString() ?? "")).ToList();
    }

    public async Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        // For now, return FullControl - actual implementation would query Dataverse security
        // This would use RetrievePrincipalAccess function in Web API
        _logger.LogDebug("Checking access for user {UserId} on document {DocumentId}", userId, documentId);

        await Task.CompletedTask;
        return DocumentAccessLevel.FullControl;
    }

    public async Task<bool> TestConnectionAsync()
    {
        await EnsureAuthenticatedAsync();

        // Simple WhoAmI request
        var response = await _httpClient.GetAsync("WhoAmI");
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Dataverse connection test successful");
        return true;
    }

    public async Task<bool> TestDocumentOperationsAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();

            // Test query
            var url = $"{_entitySetName}?$top=1";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Dataverse document operations test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Dataverse document operations test failed");
            return false;
        }
    }

    private DocumentEntity MapToDocumentEntity(Dictionary<string, JsonElement> data, string id)
    {
        return new DocumentEntity
        {
            Id = id,
            Name = GetStringValue(data, "sprk_documentname") ?? "Untitled",
            Description = GetStringValue(data, "sprk_documentdescription"),
            ContainerId = GetStringValue(data, "_sprk_containerid_value"),
            HasFile = GetBoolValue(data, "sprk_hasfile"),
            FileName = GetStringValue(data, "sprk_filename"),
            FileSize = GetLongValue(data, "sprk_filesize"),
            MimeType = GetStringValue(data, "sprk_filetype"),
            GraphItemId = GetStringValue(data, "sprk_graphitemid"),
            GraphDriveId = GetStringValue(data, "sprk_graphdriveid"),
            Status = GetIntValue(data, "statuscode").HasValue
                ? (DocumentStatus)GetIntValue(data, "statuscode")!.Value
                : DocumentStatus.Draft,
            CreatedOn = GetDateTimeValue(data, "createdon") ?? DateTime.UtcNow,
            ModifiedOn = GetDateTimeValue(data, "modifiedon") ?? DateTime.UtcNow
        };
    }

    private string? GetStringValue(Dictionary<string, JsonElement> data, string key)
    {
        if (data.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }
        return null;
    }

    private bool GetBoolValue(Dictionary<string, JsonElement> data, string key)
    {
        if (data.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    private long? GetLongValue(Dictionary<string, JsonElement> data, string key)
    {
        if (data.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt64();
        }
        return null;
    }

    private int? GetIntValue(Dictionary<string, JsonElement> data, string key)
    {
        if (data.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt32();
        }
        return null;
    }

    private DateTime? GetDateTimeValue(Dictionary<string, JsonElement> data, string key)
    {
        if (data.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(element.GetString(), out var date))
            {
                return date;
            }
        }
        return null;
    }

    // ========================================
    // Metadata Operations (Phase 7)
    // NOTE: Web API implementation not currently used (ServiceClient is preferred)
    // These are stub implementations to satisfy the interface contract
    // ========================================

    public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "GetEntitySetNameAsync called on DataverseWebApiService (not implemented). " +
            "Consider using DataverseServiceClientImpl for metadata operations.");

        throw new NotImplementedException(
            "Metadata operations via Web API are not yet implemented. " +
            "Use DataverseServiceClientImpl for full metadata support.");
    }

    public Task<LookupNavigationMetadata> GetLookupNavigationAsync(
        string childEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "GetLookupNavigationAsync called on DataverseWebApiService (not implemented). " +
            "Consider using DataverseServiceClientImpl for metadata operations.");

        throw new NotImplementedException(
            "Metadata operations via Web API are not yet implemented. " +
            "Use DataverseServiceClientImpl for full metadata support.");
    }

    public Task<string> GetCollectionNavigationAsync(
        string parentEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "GetCollectionNavigationAsync called on DataverseWebApiService (not implemented). " +
            "Consider using DataverseServiceClientImpl for metadata operations.");

        throw new NotImplementedException(
            "Metadata operations via Web API are not yet implemented. " +
            "Use DataverseServiceClientImpl for full metadata support.");
    }

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();
    }
}
