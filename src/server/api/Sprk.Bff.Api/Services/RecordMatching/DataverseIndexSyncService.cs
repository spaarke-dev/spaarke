using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.RecordMatching;

/// <summary>
/// Syncs Dataverse records (Matters, Projects, Invoices) to Azure AI Search index.
/// Enables record matching for document intelligence features.
/// </summary>
public class DataverseIndexSyncService : IDataverseIndexSyncService
{
    private readonly HttpClient _httpClient;
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<DataverseIndexSyncService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;
    private readonly string _apiUrl;

    // Supported entity types for indexing
    private static readonly Dictionary<string, EntityConfig> SupportedEntities = new()
    {
        ["sprk_matter"] = new EntityConfig
        {
            EntitySetName = "sprk_matters",
            NameField = "sprk_mattername",
            DescriptionField = "sprk_description",
            ReferenceField = "sprk_matternumber",
            SelectFields = "sprk_matterid,sprk_mattername,sprk_description,sprk_matternumber,modifiedon,_sprk_client_value"
        },
        ["sprk_project"] = new EntityConfig
        {
            EntitySetName = "sprk_projects",
            NameField = "sprk_projectname",
            DescriptionField = "sprk_description",
            ReferenceField = "sprk_projectnumber",
            SelectFields = "sprk_projectid,sprk_projectname,sprk_description,sprk_projectnumber,modifiedon"
        },
        ["sprk_invoice"] = new EntityConfig
        {
            EntitySetName = "sprk_invoices",
            NameField = "sprk_invoicename",
            DescriptionField = "sprk_description",
            ReferenceField = "sprk_invoicenumber",
            SelectFields = "sprk_invoiceid,sprk_invoicename,sprk_description,sprk_invoicenumber,modifiedon,_sprk_matter_value"
        }
    };

    public DataverseIndexSyncService(
        HttpClient httpClient,
        IOptions<DocumentIntelligenceOptions> options,
        IConfiguration configuration,
        ILogger<DataverseIndexSyncService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;

        // Initialize Dataverse connection
        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");
        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["Dataverse:ClientSecret"]
            ?? throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Initialize Azure AI Search clients
        var searchEndpoint = _options.AiSearchEndpoint
            ?? throw new InvalidOperationException("DocumentIntelligence:AiSearchEndpoint is required for record matching");
        var searchKey = _options.AiSearchKey
            ?? throw new InvalidOperationException("DocumentIntelligence:AiSearchKey is required for record matching");

        var searchCredential = new AzureKeyCredential(searchKey);
        _searchClient = new SearchClient(new Uri(searchEndpoint), _options.AiSearchIndexName, searchCredential);
        _indexClient = new SearchIndexClient(new Uri(searchEndpoint), searchCredential);

        _logger.LogInformation("Initialized DataverseIndexSyncService for index {IndexName}", _options.AiSearchIndexName);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var baseUrl = _apiUrl.Replace("/api/data/v9.2", "");
            var scope = $"{baseUrl}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);
            _logger.LogDebug("Refreshed Dataverse access token for index sync");
        }
    }

    public async Task<IndexSyncResult> BulkSyncAsync(IEnumerable<string>? recordTypes = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexSyncResult();
        var typesToSync = recordTypes?.ToList() ?? SupportedEntities.Keys.ToList();

        _logger.LogInformation("Starting bulk sync for record types: {Types}", string.Join(", ", typesToSync));

        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            var allDocuments = new List<SearchIndexDocument>();

            foreach (var entityName in typesToSync)
            {
                if (!SupportedEntities.TryGetValue(entityName, out var config))
                {
                    _logger.LogWarning("Unsupported entity type: {EntityName}", entityName);
                    result.Errors.Add($"Unsupported entity type: {entityName}");
                    continue;
                }

                try
                {
                    var documents = await FetchAndTransformRecordsAsync(entityName, config, null, cancellationToken);
                    allDocuments.AddRange(documents);
                    result.RecordsByType[entityName] = documents.Count;
                    _logger.LogInformation("Fetched {Count} records for {EntityName}", documents.Count, entityName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch records for {EntityName}", entityName);
                    result.Errors.Add($"Failed to fetch {entityName}: {ex.Message}");
                }
            }

            result.RecordsProcessed = allDocuments.Count;

            // Upload to search index in batches
            if (allDocuments.Count > 0)
            {
                var indexed = await UploadDocumentsAsync(allDocuments, cancellationToken);
                result.RecordsIndexed = indexed;
                result.RecordsFailed = allDocuments.Count - indexed;
            }

            result.Success = result.Errors.Count == 0 && result.RecordsFailed == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk sync failed");
            result.Errors.Add($"Bulk sync failed: {ex.Message}");
            result.Success = false;
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Bulk sync completed: {Processed} processed, {Indexed} indexed, {Failed} failed in {Duration}",
            result.RecordsProcessed, result.RecordsIndexed, result.RecordsFailed, result.Duration);

        return result;
    }

    public async Task<IndexSyncResult> IncrementalSyncAsync(DateTimeOffset since, IEnumerable<string>? recordTypes = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexSyncResult();
        var typesToSync = recordTypes?.ToList() ?? SupportedEntities.Keys.ToList();

        _logger.LogInformation("Starting incremental sync since {Since} for types: {Types}", since, string.Join(", ", typesToSync));

        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            var allDocuments = new List<SearchIndexDocument>();

            foreach (var entityName in typesToSync)
            {
                if (!SupportedEntities.TryGetValue(entityName, out var config))
                {
                    continue;
                }

                try
                {
                    var documents = await FetchAndTransformRecordsAsync(entityName, config, since, cancellationToken);
                    allDocuments.AddRange(documents);
                    result.RecordsByType[entityName] = documents.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch records for {EntityName}", entityName);
                    result.Errors.Add($"Failed to fetch {entityName}: {ex.Message}");
                }
            }

            result.RecordsProcessed = allDocuments.Count;

            if (allDocuments.Count > 0)
            {
                var indexed = await UploadDocumentsAsync(allDocuments, cancellationToken);
                result.RecordsIndexed = indexed;
                result.RecordsFailed = allDocuments.Count - indexed;
            }

            result.Success = result.Errors.Count == 0 && result.RecordsFailed == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental sync failed");
            result.Errors.Add($"Incremental sync failed: {ex.Message}");
            result.Success = false;
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        return result;
    }

    public async Task SyncRecordAsync(string entityName, Guid recordId, CancellationToken cancellationToken = default)
    {
        if (!SupportedEntities.TryGetValue(entityName, out var config))
        {
            _logger.LogWarning("Cannot sync unsupported entity type: {EntityName}", entityName);
            return;
        }

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"{config.EntitySetName}({recordId})?$select={config.SelectFields}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Record not found: {EntityName}/{RecordId}", entityName, recordId);
            return;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var record = JsonDocument.Parse(json).RootElement;

        var document = TransformToDocument(entityName, config, record);
        await _searchClient.MergeOrUploadDocumentsAsync(new[] { document }, cancellationToken: cancellationToken);

        _logger.LogInformation("Synced record {EntityName}/{RecordId}", entityName, recordId);
    }

    public async Task RemoveRecordAsync(string entityName, Guid recordId, CancellationToken cancellationToken = default)
    {
        var documentId = $"{entityName}_{recordId}";
        await _searchClient.DeleteDocumentsAsync("id", new[] { documentId }, cancellationToken: cancellationToken);
        _logger.LogInformation("Removed record from index: {DocumentId}", documentId);
    }

    public async Task<IndexSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new IndexSyncStatus
        {
            IndexName = _options.AiSearchIndexName
        };

        try
        {
            var indexStats = await _indexClient.GetIndexStatisticsAsync(_options.AiSearchIndexName, cancellationToken);
            status.DocumentCount = indexStats.Value.DocumentCount;
            status.IsHealthy = true;

            // Get document counts by type using facets
            var searchOptions = new SearchOptions
            {
                Size = 0,
                Facets = { "recordType" }
            };

            var results = await _searchClient.SearchAsync<SearchIndexDocument>("*", searchOptions, cancellationToken);

            if (results.Value.Facets.TryGetValue("recordType", out var facets))
            {
                foreach (var facet in facets)
                {
                    status.DocumentsByType[facet.Value.ToString()!] = (int)(facet.Count ?? 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get index status");
            status.IsHealthy = false;
        }

        return status;
    }

    private async Task<List<SearchIndexDocument>> FetchAndTransformRecordsAsync(
        string entityName,
        EntityConfig config,
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken)
    {
        var documents = new List<SearchIndexDocument>();
        var filter = modifiedSince.HasValue
            ? $"modifiedon gt {modifiedSince.Value:yyyy-MM-ddTHH:mm:ssZ}"
            : null;

        var url = $"{config.EntitySetName}?$select={config.SelectFields}";
        if (!string.IsNullOrEmpty(filter))
        {
            url += $"&$filter={filter}";
        }

        string? nextLink = url;

        while (!string.IsNullOrEmpty(nextLink))
        {
            var response = await _httpClient.GetAsync(nextLink, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var records))
            {
                foreach (var record in records.EnumerateArray())
                {
                    var searchDoc = TransformToDocument(entityName, config, record);
                    documents.Add(searchDoc);
                }
            }

            // Handle paging
            nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkElement)
                ? nextLinkElement.GetString()
                : null;
        }

        return documents;
    }

    private SearchIndexDocument TransformToDocument(string entityName, EntityConfig config, JsonElement record)
    {
        var recordId = GetPropertyString(record, $"{entityName}id");
        var name = GetPropertyString(record, config.NameField);
        var description = GetPropertyString(record, config.DescriptionField);
        var reference = GetPropertyString(record, config.ReferenceField);
        var modifiedOn = GetPropertyDateTime(record, "modifiedon");

        var document = new SearchIndexDocument
        {
            Id = $"{entityName}_{recordId}",
            RecordType = entityName,
            DataverseEntityName = entityName,
            DataverseRecordId = recordId,
            RecordName = name,
            RecordDescription = description,
            LastModified = modifiedOn
        };

        // Add reference number if present
        if (!string.IsNullOrEmpty(reference))
        {
            document.ReferenceNumbers = new List<string> { reference };
        }

        // Build keywords from name, description, and reference
        var keywordParts = new List<string>();
        if (!string.IsNullOrEmpty(name)) keywordParts.Add(name);
        if (!string.IsNullOrEmpty(reference)) keywordParts.Add(reference);
        document.Keywords = string.Join(" ", keywordParts);

        // TODO: In future, expand lookups to get organization/people names
        // For now, leave Organizations and People empty

        return document;
    }

    private async Task<int> UploadDocumentsAsync(List<SearchIndexDocument> documents, CancellationToken cancellationToken)
    {
        const int batchSize = 1000;
        var indexed = 0;

        for (var i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();

            try
            {
                var response = await _searchClient.MergeOrUploadDocumentsAsync(batch, cancellationToken: cancellationToken);
                indexed += response.Value.Results.Count(r => r.Succeeded);

                var failed = response.Value.Results.Where(r => !r.Succeeded).ToList();
                if (failed.Any())
                {
                    foreach (var failure in failed.Take(5))
                    {
                        _logger.LogWarning("Failed to index document {Key}: {Error}", failure.Key, failure.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload batch starting at index {Index}", i);
            }
        }

        return indexed;
    }

    private static string GetPropertyString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset? GetPropertyDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var str = prop.GetString();
            if (DateTimeOffset.TryParse(str, out var dt))
                return dt;
        }
        return null;
    }

    private class EntityConfig
    {
        public string EntitySetName { get; set; } = string.Empty;
        public string NameField { get; set; } = string.Empty;
        public string DescriptionField { get; set; } = string.Empty;
        public string ReferenceField { get; set; } = string.Empty;
        public string SelectFields { get; set; } = string.Empty;
    }
}
