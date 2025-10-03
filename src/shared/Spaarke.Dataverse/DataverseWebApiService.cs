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
        var managedIdentityClientId = configuration["ManagedIdentity:ClientId"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        if (string.IsNullOrEmpty(managedIdentityClientId))
            throw new InvalidOperationException("ManagedIdentity:ClientId configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });

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
            sprk_description = request.Description,
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
            _logger.LogError(ex, "Failed to retrieve document {Id}", id);
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
        if (request.Description != null) payload["sprk_description"] = request.Description;
        if (request.FileName != null) payload["sprk_filename"] = request.FileName;
        if (request.FileSize.HasValue) payload["sprk_filesize"] = request.FileSize.Value;
        if (request.MimeType != null) payload["sprk_filetype"] = request.MimeType;
        if (request.GraphItemId != null) payload["sprk_graphitemid"] = request.GraphItemId;
        if (request.GraphDriveId != null) payload["sprk_graphdriveid"] = request.GraphDriveId;
        if (request.HasFile.HasValue) payload["sprk_hasfile"] = request.HasFile.Value;
        if (request.Status.HasValue) payload["statuscode"] = (int)request.Status.Value;

        var url = $"{_entitySetName}({guid})";
        _logger.LogInformation("Updating document: {Id}", id);

        var response = await _httpClient.PatchAsJsonAsync(url, payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Document updated successfully: {Id}", id);
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
        try
        {
            await EnsureAuthenticatedAsync();

            // Simple WhoAmI request
            var response = await _httpClient.GetAsync("WhoAmI");
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Dataverse connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataverse connection test failed");
            return false;
        }
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
            _logger.LogError(ex, "Dataverse document operations test failed");
            return false;
        }
    }

    private DocumentEntity MapToDocumentEntity(Dictionary<string, JsonElement> data, string id)
    {
        return new DocumentEntity
        {
            Id = id,
            Name = GetStringValue(data, "sprk_documentname") ?? "Untitled",
            Description = GetStringValue(data, "sprk_description"),
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

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();
    }
}
