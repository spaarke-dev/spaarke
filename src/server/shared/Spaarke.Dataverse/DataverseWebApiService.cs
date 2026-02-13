using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

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

        // Query without $select to get all fields including lookup values
        // Lookup values are returned as _lookupfieldname_value format automatically
        var url = $"{_entitySetName}({guid})";
        _logger.LogInformation("[DATAVERSE-DEBUG] GetDocumentAsync: Querying {Url}", url);

        try
        {
            // Request formatted values for lookup field display names
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

            var response = await _httpClient.SendAsync(request, ct);
            _logger.LogInformation("[DATAVERSE-DEBUG] GetDocumentAsync: Response status {StatusCode}", response.StatusCode);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[DATAVERSE-DEBUG] Document not found: {Id}", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null)
            {
                _logger.LogWarning("[DATAVERSE-DEBUG] GetDocumentAsync: Response data was null for {Id}", id);
                return null;
            }

            _logger.LogInformation("[DATAVERSE-DEBUG] GetDocumentAsync: Got {FieldCount} fields for {Id}", data.Count, id);
            return MapToDocumentEntity(data, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "[DATAVERSE-DEBUG] Failed to retrieve document {Id}", id);
            throw;
        }
    }

    public async Task<AnalysisEntity?> GetAnalysisAsync(string id, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(id, out var guid))
        {
            _logger.LogWarning("Invalid analysis ID format: {Id}", id);
            return null;
        }

        var url = $"sprk_analysises({guid})?$select=sprk_name,sprk_workingdocument,sprk_chathistory,statuscode,createdon,modifiedon,_sprk_documentid_value";
        _logger.LogDebug("Retrieving analysis: {Id}", id);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Analysis not found: {Id}", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null) return null;

            return new AnalysisEntity
            {
                Id = guid,
                Name = data.TryGetValue("sprk_name", out var name) ? name.GetString() : null,
                DocumentId = data.TryGetValue("_sprk_documentid_value", out var docId) && docId.ValueKind != JsonValueKind.Null
                    ? Guid.Parse(docId.GetString()!) : Guid.Empty,
                WorkingDocument = data.TryGetValue("sprk_workingdocument", out var wd) && wd.ValueKind != JsonValueKind.Null
                    ? wd.GetString() : null,
                ChatHistory = data.TryGetValue("sprk_chathistory", out var ch) && ch.ValueKind != JsonValueKind.Null
                    ? ch.GetString() : null,
                StatusCode = data.TryGetValue("statuscode", out var status) ? status.GetInt32() : 0,
                CreatedOn = data.TryGetValue("createdon", out var created) ? created.GetDateTime() : DateTime.MinValue,
                ModifiedOn = data.TryGetValue("modifiedon", out var modified) ? modified.GetDateTime() : DateTime.MinValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Failed to retrieve analysis {Id}", id);
            throw;
        }
    }

    public async Task<AnalysisActionEntity?> GetAnalysisActionAsync(string id, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(id, out var guid))
        {
            _logger.LogWarning("Invalid analysis action ID format: {Id}", id);
            return null;
        }

        var url = $"sprk_analysisactions({guid})?$select=sprk_name,sprk_description,sprk_systemprompt,sprk_sortorder";
        _logger.LogDebug("Retrieving analysis action: {Id}", id);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Analysis action not found: {Id}", id);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken: ct);
            if (data == null) return null;

            return new AnalysisActionEntity
            {
                Id = guid,
                Name = data.TryGetValue("sprk_name", out var name) ? name.GetString() : null,
                Description = data.TryGetValue("sprk_description", out var desc) && desc.ValueKind != JsonValueKind.Null
                    ? desc.GetString() : null,
                SystemPrompt = data.TryGetValue("sprk_systemprompt", out var prompt) && prompt.ValueKind != JsonValueKind.Null
                    ? prompt.GetString() : null,
                SortOrder = data.TryGetValue("sprk_sortorder", out var sort) && sort.ValueKind != JsonValueKind.Null
                    ? sort.GetInt32() : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Failed to retrieve analysis action {Id}", id);
            throw;
        }
    }

    public async Task<Guid> CreateAnalysisAsync(Guid documentId, string? name = null, Guid? playbookId = null, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var payload = new Dictionary<string, object>
        {
            ["sprk_name"] = name ?? $"Analysis {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            ["sprk_documentid@odata.bind"] = $"/sprk_documents({documentId})",
            ["statuscode"] = 1 // Active
        };

        // Set playbook lookup if provided
        if (playbookId.HasValue)
        {
            payload["sprk_playbookid@odata.bind"] = $"/sprk_analysisplaybooks({playbookId.Value})";
        }

        var response = await _httpClient.PostAsJsonAsync("sprk_analysises", payload, ct);
        response.EnsureSuccessStatusCode();

        var location = response.Headers.Location?.ToString();
        if (location == null)
        {
            throw new InvalidOperationException("Failed to create analysis: No location header returned");
        }

        // Extract ID from location header (e.g., ".../sprk_analysises(guid)")
        var match = System.Text.RegularExpressions.Regex.Match(location, @"\(([a-fA-F0-9-]+)\)");
        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var analysisId))
        {
            throw new InvalidOperationException($"Failed to parse analysis ID from location: {location}");
        }

        _logger.LogInformation("[DATAVERSE-API] Created analysis {AnalysisId} for document {DocumentId}", analysisId, documentId);
        return analysisId;
    }

    public async Task<Guid> CreateAnalysisOutputAsync(AnalysisOutputEntity output, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var payload = new Dictionary<string, object>
        {
            ["sprk_name"] = output.Name ?? "Output",
            ["sprk_value"] = output.Value ?? string.Empty,
            ["sprk_analysisid@odata.bind"] = $"/sprk_analysises({output.AnalysisId})"
        };

        if (output.OutputTypeId.HasValue)
        {
            payload["sprk_outputtypeid@odata.bind"] = $"/sprk_aioutputtypes({output.OutputTypeId.Value})";
        }

        if (output.SortOrder.HasValue)
        {
            payload["sprk_sortorder"] = output.SortOrder.Value;
        }

        var response = await _httpClient.PostAsJsonAsync("sprk_analysisoutputs", payload, ct);
        response.EnsureSuccessStatusCode();

        var location = response.Headers.Location?.ToString();
        if (location == null)
        {
            throw new InvalidOperationException("Failed to create analysis output: No location header returned");
        }

        var match = System.Text.RegularExpressions.Regex.Match(location, @"\(([a-fA-F0-9-]+)\)");
        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var outputId))
        {
            throw new InvalidOperationException($"Failed to parse output ID from location: {location}");
        }

        _logger.LogDebug("[DATAVERSE-API] Created analysis output {OutputId} for analysis {AnalysisId}", outputId, output.AnalysisId);
        return outputId;
    }

    public async Task UpdateDocumentFieldsAsync(string documentId, Dictionary<string, object?> fields, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!Guid.TryParse(documentId, out var guid))
        {
            throw new ArgumentException($"Invalid document ID format: {documentId}", nameof(documentId));
        }

        var payload = new Dictionary<string, object?>();
        foreach (var field in fields)
        {
            if (field.Value != null)
            {
                payload[field.Key] = field.Value;
            }
        }

        var response = await _httpClient.PatchAsJsonAsync($"sprk_documents({guid})", payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("[DATAVERSE-API] Updated document {DocumentId} with {FieldCount} fields", documentId, fields.Count);
    }

    public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
    {
        // Not implemented in Web API version - use DataverseServiceClientImpl
        throw new NotImplementedException("RetrieveAsync is not implemented in Web API version. Use DataverseServiceClientImpl for this operation.");
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
        if (request.MimeType != null) payload["sprk_mimetype"] = request.MimeType;
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
        // Note: sprk_parentdocumentid was removed from schema - use ParentDocumentLookup instead
        if (request.ParentFileName != null) payload["sprk_parentfilename"] = request.ParentFileName;
        if (request.ParentGraphItemId != null) payload["sprk_parentgraphitemid"] = request.ParentGraphItemId;
        if (request.EmailParentId != null) payload["sprk_emailparentid"] = request.EmailParentId;
        // ParentDocumentLookup uses @odata.bind for lookup fields
        if (request.ParentDocumentLookup.HasValue)
            payload["sprk_ParentDocument@odata.bind"] = $"/sprk_documents({request.ParentDocumentLookup.Value})";

        // Record association lookups (Phase 2 - Record Matching)
        if (request.MatterLookup.HasValue)
            payload["sprk_Matter@odata.bind"] = $"/sprk_matters({request.MatterLookup.Value})";
        if (request.ProjectLookup.HasValue)
            payload["sprk_Project@odata.bind"] = $"/sprk_projects({request.ProjectLookup.Value})";
        if (request.InvoiceLookup.HasValue)
            payload["sprk_Invoice@odata.bind"] = $"/sprk_invoices({request.InvoiceLookup.Value})";

        // Source type (choice field)
        if (request.SourceType.HasValue)
            payload["sprk_sourcetype"] = request.SourceType.Value;

        // Search index tracking fields
        if (request.SearchIndexed.HasValue) payload["sprk_searchindexed"] = request.SearchIndexed.Value;
        if (request.SearchIndexName != null) payload["sprk_searchindexname"] = request.SearchIndexName;
        if (request.SearchIndexedOn.HasValue) payload["sprk_searchindexedon"] = request.SearchIndexedOn.Value;

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
        // Debug: Log lookup field values for relationship queries
        var matterId = GetStringValue(data, "_sprk_matter_value");
        var projectId = GetStringValue(data, "_sprk_project_value");
        var invoiceId = GetStringValue(data, "_sprk_invoice_value");
        _logger.LogWarning("[DATAVERSE-DEBUG] MapToDocumentEntity for {Id}: MatterId={MatterId}, ProjectId={ProjectId}, InvoiceId={InvoiceId}",
            id, matterId ?? "(null)", projectId ?? "(null)", invoiceId ?? "(null)");

        return new DocumentEntity
        {
            Id = id,
            Name = GetStringValue(data, "sprk_documentname") ?? "Untitled",
            Description = GetStringValue(data, "sprk_documentdescription"),
            ContainerId = GetStringValue(data, "_sprk_containerid_value"),
            HasFile = GetBoolValue(data, "sprk_hasfile"),
            FileName = GetStringValue(data, "sprk_filename"),
            FileSize = GetLongValue(data, "sprk_filesize"),
            MimeType = GetStringValue(data, "sprk_mimetype"),
            GraphItemId = GetStringValue(data, "sprk_graphitemid"),
            GraphDriveId = GetStringValue(data, "sprk_graphdriveid"),
            Status = GetIntValue(data, "statuscode").HasValue
                ? (DocumentStatus)GetIntValue(data, "statuscode")!.Value
                : DocumentStatus.Draft,
            CreatedOn = GetDateTimeValue(data, "createdon") ?? DateTime.UtcNow,
            ModifiedOn = GetDateTimeValue(data, "modifiedon") ?? DateTime.UtcNow,

            // Email metadata fields
            EmailSubject = GetStringValue(data, "sprk_emailsubject"),
            EmailFrom = GetStringValue(data, "sprk_emailfrom"),
            EmailTo = GetStringValue(data, "sprk_emailto"),
            EmailCc = GetStringValue(data, "sprk_emailcc"),
            EmailDate = GetDateTimeValue(data, "sprk_emaildate"),
            EmailBody = GetStringValue(data, "sprk_emailbody"),
            IsEmailArchive = GetNullableBoolValue(data, "sprk_isemailarchive"),
            // ParentDocumentId is the lookup value (GUID string) from sprk_parentdocument lookup
            ParentDocumentId = GetStringValue(data, "_sprk_parentdocument_value"),
            EmailConversationIndex = GetStringValue(data, "sprk_emailconversationindex"),

            // Record association lookups (for relationship queries)
            MatterId = GetStringValue(data, "_sprk_matter_value"),
            MatterName = GetStringValue(data, "_sprk_matter_value@OData.Community.Display.V1.FormattedValue"),
            ProjectId = GetStringValue(data, "_sprk_project_value"),
            ProjectName = GetStringValue(data, "_sprk_project_value@OData.Community.Display.V1.FormattedValue"),
            InvoiceId = GetStringValue(data, "_sprk_invoice_value"),
            InvoiceName = GetStringValue(data, "_sprk_invoice_value@OData.Community.Display.V1.FormattedValue")
        };
    }

    private bool? GetNullableBoolValue(Dictionary<string, JsonElement> data, string key)
    {
        if (data.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return null;
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
    // Email-to-Document Operations (Phase 4)
    // ========================================

    /// <summary>
    /// Get the main .eml document record by email activity lookup.
    /// Queries: $filter=_sprk_email_value eq {emailId} and sprk_isemailarchive eq true
    /// </summary>
    public async Task<DocumentEntity?> GetDocumentByEmailLookupAsync(Guid emailId, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        // OData filter for email lookup (lookup value is stored as _sprk_email_value)
        // and IsEmailArchive=true (main .eml document, not attachments)
        var filter = $"_sprk_email_value eq {emailId} and sprk_isemailarchive eq true";
        var url = $"{_entitySetName}?$filter={Uri.EscapeDataString(filter)}&$top=1";

        _logger.LogDebug("Querying document by email lookup: {EmailId}", emailId);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (result?.Value == null || result.Value.Count == 0)
            {
                _logger.LogDebug("No document found for email lookup {EmailId}", emailId);
                return null;
            }

            var data = result.Value[0];
            var id = data.TryGetValue("sprk_documentid", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;

            var document = MapToDocumentEntity(data, id);
            _logger.LogDebug("Found document {DocumentId} for email lookup {EmailId}", document.Id, emailId);
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying document by email lookup {EmailId}", emailId);
            throw;
        }
    }

    /// <summary>
    /// Get child documents (attachments) by parent document lookup.
    /// Queries: $filter=_sprk_parentdocument_value eq {parentDocumentId}
    /// </summary>
    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByParentAsync(Guid parentDocumentId, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        // OData filter for parent document lookup (lookup value is stored as _sprk_parentdocument_value)
        var filter = $"_sprk_parentdocument_value eq {parentDocumentId}";
        var url = $"{_entitySetName}?$filter={Uri.EscapeDataString(filter)}";

        _logger.LogDebug("Querying child documents by parent: {ParentDocumentId}", parentDocumentId);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (result?.Value == null || result.Value.Count == 0)
            {
                _logger.LogDebug("No child documents found for parent {ParentDocumentId}", parentDocumentId);
                return Enumerable.Empty<DocumentEntity>();
            }

            var documents = result.Value.Select(data =>
            {
                var id = data.TryGetValue("sprk_documentid", out var idElement)
                    ? idElement.GetString() ?? string.Empty
                    : string.Empty;
                return MapToDocumentEntity(data, id);
            }).ToList();

            _logger.LogDebug("Found {Count} child documents for parent {ParentDocumentId}", documents.Count, parentDocumentId);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying child documents by parent {ParentDocumentId}", parentDocumentId);
            throw;
        }
    }

    // ========================================
    // Relationship Query Operations (Visualization)
    // ========================================

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByMatterAsync(Guid matterId, Guid? excludeDocumentId = null, CancellationToken ct = default)
    {
        return await GetDocumentsByLookupAsync("_sprk_matter_value", matterId, excludeDocumentId, "Matter", ct);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByProjectAsync(Guid projectId, Guid? excludeDocumentId = null, CancellationToken ct = default)
    {
        return await GetDocumentsByLookupAsync("_sprk_project_value", projectId, excludeDocumentId, "Project", ct);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByInvoiceAsync(Guid invoiceId, Guid? excludeDocumentId = null, CancellationToken ct = default)
    {
        return await GetDocumentsByLookupAsync("_sprk_invoice_value", invoiceId, excludeDocumentId, "Invoice", ct);
    }

    /// <summary>
    /// Generic helper to query documents by a lookup field value.
    /// </summary>
    private async Task<IEnumerable<DocumentEntity>> GetDocumentsByLookupAsync(
        string lookupField,
        Guid lookupValue,
        Guid? excludeDocumentId,
        string entityDisplayName,
        CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        var filter = $"{lookupField} eq {lookupValue}";
        if (excludeDocumentId.HasValue)
        {
            filter += $" and sprk_documentid ne {excludeDocumentId.Value}";
        }

        var url = $"{_entitySetName}?$filter={Uri.EscapeDataString(filter)}&$top=50";

        _logger.LogDebug("Querying documents by {EntityDisplayName}: {LookupValue}", entityDisplayName, lookupValue);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (result?.Value == null || result.Value.Count == 0)
            {
                _logger.LogDebug("No documents found for {EntityDisplayName} {LookupValue}", entityDisplayName, lookupValue);
                return Enumerable.Empty<DocumentEntity>();
            }

            var documents = result.Value.Select(data =>
            {
                var id = data.TryGetValue("sprk_documentid", out var idElement)
                    ? idElement.GetString() ?? string.Empty
                    : string.Empty;
                return MapToDocumentEntity(data, id);
            }).ToList();

            _logger.LogDebug("Found {Count} documents for {EntityDisplayName} {LookupValue}", documents.Count, entityDisplayName, lookupValue);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents by {EntityDisplayName} {LookupValue}", entityDisplayName, lookupValue);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByConversationIndexAsync(string conversationIndexPrefix, Guid? excludeDocumentId = null, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        // ConversationIndex prefix match: first 44 chars identify the thread root
        // Use startswith for matching all emails in the same thread
        var filter = $"startswith(sprk_emailconversationindex,'{conversationIndexPrefix}')";
        if (excludeDocumentId.HasValue)
        {
            filter += $" and sprk_documentid ne {excludeDocumentId.Value}";
        }

        var url = $"{_entitySetName}?$filter={Uri.EscapeDataString(filter)}&$top=50";

        _logger.LogDebug("Querying documents by ConversationIndex prefix: {Prefix}", conversationIndexPrefix);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (result?.Value == null || result.Value.Count == 0)
            {
                _logger.LogDebug("No documents found for ConversationIndex prefix {Prefix}", conversationIndexPrefix);
                return Enumerable.Empty<DocumentEntity>();
            }

            var documents = result.Value.Select(data =>
            {
                var id = data.TryGetValue("sprk_documentid", out var idElement)
                    ? idElement.GetString() ?? string.Empty
                    : string.Empty;
                return MapToDocumentEntity(data, id);
            }).ToList();

            _logger.LogDebug("Found {Count} documents for ConversationIndex prefix {Prefix}", documents.Count, conversationIndexPrefix);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents by ConversationIndex prefix {Prefix}", conversationIndexPrefix);
            throw;
        }
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

    // ========================================
    // Office Add-in Operations (SDAP Project)
    // Stubs - Not implemented in Web API service
    // ========================================

    public Task<Guid> CreateProcessingJobAsync(object request, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task UpdateProcessingJobAsync(Guid id, object request, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task<object?> GetProcessingJobAsync(Guid id, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task<object?> GetProcessingJobByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task<Guid> CreateEmailArtifactAsync(object request, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task<object?> GetEmailArtifactAsync(Guid id, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task<Guid> CreateAttachmentArtifactAsync(object request, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    public Task<object?> GetAttachmentArtifactAsync(Guid id, CancellationToken ct = default)
    {
        throw new NotImplementedException("Office add-in operations require DataverseServiceClientImpl");
    }

    // ========================================
    // Event Management Operations (Events and Workflow Automation R1)
    // ========================================

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
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        // Build OData query
        var filters = new List<string>();

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
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
        await EnsureAuthenticatedAsync(ct);

        var url = $"sprk_events({id})?$select=sprk_eventid,sprk_eventname,sprk_description,_sprk_eventtype_ref_value,sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordtype,_sprk_regardingaccount_value,_sprk_regardinganalysis_value,_sprk_regardingcontact_value,_sprk_regardinginvoice_value,_sprk_regardingmatter_value,_sprk_regardingproject_value,_sprk_regardingbudget_value,_sprk_regardingworkassignment_value,sprk_basedate,sprk_duedate,sprk_completeddate,statecode,statuscode,sprk_priority,sprk_source,sprk_remindat,_sprk_relatedevent_value,sprk_relatedeventtype,sprk_relatedeventoffsettype,createdon,modifiedon&$expand=sprk_eventtype_ref($select=sprk_name)";

        _logger.LogDebug("Getting event: {Id}", id);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
        await EnsureAuthenticatedAsync(ct);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_eventname"] = request.Name,
            ["sprk_description"] = request.Description,
            ["statuscode"] = 3, // Open
            ["statecode"] = 0,  // Active
            ["sprk_source"] = 0 // User
        };

        if (request.EventTypeId.HasValue)
            payload["sprk_eventtype_ref@odata.bind"] = $"/sprk_eventtypes({request.EventTypeId.Value})";

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

        var response = await _httpClient.PostAsJsonAsync("sprk_events", payload, ct);
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
        await EnsureAuthenticatedAsync(ct);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_eventname"] = request.Name;

        if (request.Description != null)
            payload["sprk_description"] = request.Description;

        if (request.EventTypeId.HasValue)
            payload["sprk_eventtype_ref@odata.bind"] = $"/sprk_eventtypes({request.EventTypeId.Value})";

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

        var response = await _httpClient.PatchAsJsonAsync($"sprk_events({id})", payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Event updated: {Id}", id);
    }

    public async Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

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

        var response = await _httpClient.PatchAsJsonAsync($"sprk_events({id})", payload, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Event status updated: {Id}", id);
    }

    public async Task<EventLogEntity[]> QueryEventLogsAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var url = $"sprk_eventlogs?$filter=_sprk_event_value eq {eventId}&$select=sprk_eventlogid,sprk_eventlogname,_sprk_event_value,sprk_action,sprk_description,createdon,_createdby_value&$orderby=createdon desc";

        _logger.LogDebug("Querying event logs for event: {EventId}", eventId);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
        await EnsureAuthenticatedAsync(ct);

        var logName = $"Event Log - {EventLogAction.GetDisplayName(action)} - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";

        var payload = new Dictionary<string, object?>
        {
            ["sprk_eventlogname"] = logName,
            ["sprk_event@odata.bind"] = $"/sprk_events({eventId})",
            ["sprk_action"] = action,
            ["sprk_description"] = description
        };

        _logger.LogInformation("Creating event log for event {EventId}: {Action}", eventId, EventLogAction.GetDisplayName(action));

        var response = await _httpClient.PostAsJsonAsync("sprk_eventlogs", payload, ct);
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
        await EnsureAuthenticatedAsync(ct);

        var filterQuery = activeOnly ? "$filter=statecode eq 0&" : "";
        var url = $"sprk_eventtypes?{filterQuery}$select=sprk_eventtypeid,sprk_name,sprk_eventcode,sprk_description,statecode,sprk_requiresduedate,sprk_requiresbasedate&$orderby=sprk_name asc";

        _logger.LogDebug("Getting event types (activeOnly={ActiveOnly})", activeOnly);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
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
        await EnsureAuthenticatedAsync(ct);

        var url = $"sprk_eventtypes({id})?$select=sprk_eventtypeid,sprk_name,sprk_eventcode,sprk_description,statecode,sprk_requiresduedate,sprk_requiresbasedate";

        _logger.LogDebug("Getting event type: {Id}", id);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

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

    public async Task<FieldMappingProfileEntity[]> QueryFieldMappingProfilesAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var url = "sprk_fieldmappingprofiles?$filter=sprk_isactive eq true&$select=sprk_fieldmappingprofileid,sprk_name,sprk_sourceentity,sprk_targetentity,sprk_mappingdirection,sprk_syncmode,sprk_isactive,sprk_description&$orderby=sprk_name asc";

        _logger.LogDebug("Querying field mapping profiles");

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null)
                return Array.Empty<FieldMappingProfileEntity>();

            return data.Value.Select(MapToFieldMappingProfileEntity).ToArray();
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
        await EnsureAuthenticatedAsync(ct);

        var url = $"sprk_fieldmappingprofiles?$filter=sprk_sourceentity eq '{sourceEntity}' and sprk_targetentity eq '{targetEntity}' and sprk_isactive eq true&$select=sprk_fieldmappingprofileid,sprk_name,sprk_sourceentity,sprk_targetentity,sprk_mappingdirection,sprk_syncmode,sprk_isactive,sprk_description";

        _logger.LogDebug("Getting field mapping profile: {Source} -> {Target}", sourceEntity, targetEntity);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null || data.Value.Count == 0)
                return null;

            var profile = MapToFieldMappingProfileEntity(data.Value[0]);

            // Load rules for this profile
            profile.Rules = (await GetFieldMappingRulesAsync(profile.Id, true, ct)).ToList();

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting field mapping profile for {Source} -> {Target}", sourceEntity, targetEntity);
            throw;
        }
    }

    public async Task<FieldMappingRuleEntity[]> GetFieldMappingRulesAsync(
        Guid profileId,
        bool activeOnly = true,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var filterQuery = activeOnly
            ? $"$filter=_sprk_fieldmappingprofile_value eq {profileId} and sprk_isactive eq true&"
            : $"$filter=_sprk_fieldmappingprofile_value eq {profileId}&";

        var url = $"sprk_fieldmappingrules?{filterQuery}$select=sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_sourcefield,sprk_sourcefieldtype,sprk_targetfield,sprk_targetfieldtype,sprk_compatibilitymode,sprk_isrequired,sprk_defaultvalue,sprk_iscascadingsource,sprk_executionorder,sprk_isactive&$orderby=sprk_executionorder asc";

        _logger.LogDebug("Getting field mapping rules for profile: {ProfileId}", profileId);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
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
        await EnsureAuthenticatedAsync(ct);

        var entitySetName = await GetEntitySetNameAsync(entityLogicalName, ct);
        var selectFields = string.Join(",", fields);
        var url = $"{entitySetName}({recordId})?$select={selectFields}";

        _logger.LogDebug("Retrieving record fields: {Entity}({Id})", entityLogicalName, recordId);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
        await EnsureAuthenticatedAsync(ct);

        var entitySetName = await GetEntitySetNameAsync(childEntityLogicalName, ct);
        var primaryKey = $"{childEntityLogicalName}id";
        var url = $"{entitySetName}?$filter=_{parentLookupField}_value eq {parentRecordId}&$select={primaryKey}";

        _logger.LogDebug("Querying child records: {Entity} by {LookupField} = {ParentId}", childEntityLogicalName, parentLookupField, parentRecordId);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
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

    public async Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        if (fields.Count == 0)
        {
            _logger.LogDebug("No fields to update for {Entity}({Id})", entityLogicalName, recordId);
            return;
        }

        var entitySetName = await GetEntitySetNameAsync(entityLogicalName, ct);

        _logger.LogInformation("Updating record fields: {Entity}({Id}), {FieldCount} fields", entityLogicalName, recordId, fields.Count);

        var response = await _httpClient.PatchAsJsonAsync($"{entitySetName}({recordId})", fields, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Record updated: {Entity}({Id})", entityLogicalName, recordId);
    }

    // ========================================
    // KPI Assessment Operations (Matter Performance KPI R1)
    // ========================================

    public async Task<KpiAssessmentRecord[]> QueryKpiAssessmentsAsync(
        Guid parentId,
        string parentLookupField = "sprk_matter",
        int? performanceArea = null,
        int top = 0,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var entitySetName = await GetEntitySetNameAsync("sprk_kpiassessment", ct);

        // Build OData filter: parent lookup (matter or project) + optional performance area
        // OData lookup filter uses _<fieldname>_value format
        var filter = $"_{parentLookupField}_value eq {parentId}";
        if (performanceArea.HasValue)
        {
            filter += $" and sprk_performancearea eq {performanceArea.Value}";
        }

        var url = $"{entitySetName}?$filter={filter}&$select=sprk_kpiassessmentid,sprk_kpigradescore,createdon&$orderby=createdon desc";
        if (top > 0)
        {
            url += $"&$top={top}";
        }

        _logger.LogDebug(
            "Querying KPI assessments for {ParentField}={ParentId}, area={Area}, top={Top}",
            parentLookupField, parentId, performanceArea, top);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
            if (data == null)
                return Array.Empty<KpiAssessmentRecord>();

            return data.Value
                .Select(d => new KpiAssessmentRecord
                {
                    Id = d.TryGetValue("sprk_kpiassessmentid", out var id) && id.ValueKind != JsonValueKind.Null
                        ? Guid.Parse(id.GetString()!) : Guid.Empty,
                    Grade = d.TryGetValue("sprk_kpigradescore", out var grade) && grade.ValueKind != JsonValueKind.Null
                        ? grade.GetInt32() : 0,
                    CreatedOn = d.TryGetValue("createdon", out var created) && created.ValueKind != JsonValueKind.Null
                        ? DateTime.Parse(created.GetString()!) : DateTime.MinValue
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying KPI assessments for {ParentField}={ParentId}", parentLookupField, parentId);
            throw;
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

    private FieldMappingProfileEntity MapToFieldMappingProfileEntity(Dictionary<string, JsonElement> data)
    {
        return new FieldMappingProfileEntity
        {
            Id = data.TryGetValue("sprk_fieldmappingprofileid", out var id) && id.ValueKind != JsonValueKind.Null
                ? Guid.Parse(id.GetString()!) : Guid.Empty,
            Name = data.TryGetValue("sprk_name", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString()! : string.Empty,
            SourceEntity = data.TryGetValue("sprk_sourceentity", out var src) && src.ValueKind != JsonValueKind.Null
                ? src.GetString()! : string.Empty,
            TargetEntity = data.TryGetValue("sprk_targetentity", out var tgt) && tgt.ValueKind != JsonValueKind.Null
                ? tgt.GetString()! : string.Empty,
            MappingDirection = data.TryGetValue("sprk_mappingdirection", out var dir) ? dir.GetInt32() : 0,
            SyncMode = data.TryGetValue("sprk_syncmode", out var mode) ? mode.GetInt32() : 0,
            IsActive = data.TryGetValue("sprk_isactive", out var active) && active.GetBoolean(),
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
            SourceFieldType = data.TryGetValue("sprk_sourcefieldtype", out var sft) ? sft.GetInt32() : 0,
            TargetField = data.TryGetValue("sprk_targetfield", out var tf) && tf.ValueKind != JsonValueKind.Null
                ? tf.GetString()! : string.Empty,
            TargetFieldType = data.TryGetValue("sprk_targetfieldtype", out var tft) ? tft.GetInt32() : 0,
            CompatibilityMode = data.TryGetValue("sprk_compatibilitymode", out var cm) ? cm.GetInt32() : 0,
            IsRequired = data.TryGetValue("sprk_isrequired", out var req) && req.GetBoolean(),
            DefaultValue = data.TryGetValue("sprk_defaultvalue", out var dv) && dv.ValueKind != JsonValueKind.Null
                ? dv.GetString() : null,
            IsCascadingSource = data.TryGetValue("sprk_iscascadingsource", out var cs) && cs.GetBoolean(),
            ExecutionOrder = data.TryGetValue("sprk_executionorder", out var eo) ? eo.GetInt32() : 0,
            IsActive = data.TryGetValue("sprk_isactive", out var active) && active.GetBoolean()
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

    // ========================================
    // Generic Entity Operations (Finance Intelligence Module R1)
    // Stubs - Use DataverseServiceClientImpl for these operations
    // ========================================

    public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "CreateAsync is implemented in DataverseServiceClientImpl. " +
            "Configure DI to use ServiceClient implementation for finance entity operations.");
    }

    public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "UpdateAsync is implemented in DataverseServiceClientImpl. " +
            "Configure DI to use ServiceClient implementation for finance entity operations.");
    }

    public Task BulkUpdateAsync(
        string entityLogicalName,
        List<(Guid id, Dictionary<string, object> fields)> updates,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "BulkUpdateAsync is implemented in DataverseServiceClientImpl. " +
            "Configure DI to use ServiceClient implementation for finance entity operations.");
    }

    public Task<Entity> RetrieveByAlternateKeyAsync(
        string entityLogicalName,
        KeyAttributeCollection alternateKeyValues,
        string[]? columns = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "RetrieveByAlternateKeyAsync is implemented in DataverseServiceClientImpl. " +
            "Configure DI to use ServiceClient implementation for alternate key lookups.");
    }
}
