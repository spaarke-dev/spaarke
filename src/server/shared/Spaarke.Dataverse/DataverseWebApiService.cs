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

    public async Task<Guid> CreateAnalysisAsync(Guid documentId, string? name = null, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var payload = new Dictionary<string, object>
        {
            ["sprk_name"] = name ?? $"Analysis {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            ["sprk_documentid@odata.bind"] = $"/sprk_documents({documentId})",
            ["statuscode"] = 1 // Active
        };

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

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();
    }
}
