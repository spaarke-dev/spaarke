using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse service implementation using ServiceClient for .NET 8.0.
/// Uses Managed Identity authentication for server-to-server scenarios.
/// Per Microsoft documentation: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-dot-net-framework
/// </summary>
public class DataverseServiceClientImpl : IDataverseService, IDisposable
{
    private readonly ServiceClient _serviceClient;
    private readonly ILogger<DataverseServiceClientImpl> _logger;
    private bool _disposed = false;

    public DataverseServiceClientImpl(
        IConfiguration configuration,
        ILogger<DataverseServiceClientImpl> logger)
    {
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var managedIdentityClientId = configuration["ManagedIdentity:ClientId"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _logger.LogInformation("Initializing Dataverse ServiceClient with Managed Identity for {DataverseUrl}", dataverseUrl);

        try
        {
            // Create ManagedIdentityCredential for system-assigned or user-assigned managed identity
            TokenCredential credential;

            if (!string.IsNullOrEmpty(managedIdentityClientId))
            {
                // User-assigned Managed Identity specified
                credential = new ManagedIdentityCredential(managedIdentityClientId);
                _logger.LogInformation("Using user-assigned Managed Identity: {ClientId}",
                    managedIdentityClientId.Substring(Math.Max(0, managedIdentityClientId.Length - 8)));
            }
            else
            {
                // System-assigned Managed Identity (default)
                credential = new ManagedIdentityCredential();
                _logger.LogInformation("Using system-assigned Managed Identity");
            }

            // Create ServiceClient using token provider pattern for Managed Identity
            _serviceClient = new ServiceClient(
                instanceUrl: new Uri(dataverseUrl),
                tokenProviderFunction: async (uri) =>
                {
                    var tokenRequestContext = new TokenRequestContext(new[] { $"{uri}/.default" });
                    var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
                    return token.Token;
                },
                useUniqueInstance: true
            );

            if (!_serviceClient.IsReady)
            {
                var error = _serviceClient.LastError ?? "Unknown error";
                _logger.LogError("Failed to initialize Dataverse ServiceClient: {Error}", error);
                throw new InvalidOperationException($"Failed to connect to Dataverse: {error}");
            }

            _logger.LogInformation("Dataverse ServiceClient connected successfully to {OrgName} ({OrgId})",
                _serviceClient.ConnectedOrgFriendlyName, _serviceClient.ConnectedOrgId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception initializing Dataverse ServiceClient");
            throw;
        }
    }

    public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        var document = new Entity("sprk_document");
        document["sprk_documentname"] = request.Name;

        if (!string.IsNullOrEmpty(request.Description))
            document["sprk_description"] = request.Description;

        document["statuscode"] = new OptionSetValue(1); // Draft
        document["statecode"] = new OptionSetValue(0);  // Active

        var documentId = await _serviceClient.CreateAsync(document, ct);
        _logger.LogInformation("Document created with ID: {DocumentId}", documentId);
        return documentId.ToString();
    }

    public async Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default)
    {
        var entity = await _serviceClient.RetrieveAsync(
            "sprk_document",
            Guid.Parse(id),
            new ColumnSet("sprk_documentname", "sprk_description", "sprk_containerid",
                         "sprk_hasfile", "sprk_filename", "sprk_filesize", "sprk_mimetype",
                         "sprk_graphitemid", "sprk_graphdriveid", "sprk_filepath",
                         "statuscode", "statecode", "createdon", "modifiedon"),
            ct);

        if (entity == null)
            return null;

        return MapToDocumentEntity(entity);
    }

    public async Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
    {
        var document = new Entity("sprk_document", Guid.Parse(id));

        if (request.Name != null)
            document["sprk_documentname"] = request.Name;

        if (request.Description != null)
            document["sprk_description"] = request.Description;

        if (request.HasFile.HasValue)
            document["sprk_hasfile"] = request.HasFile.Value;

        if (request.FileName != null)
            document["sprk_filename"] = request.FileName;

        if (request.FileSize.HasValue)
            document["sprk_filesize"] = request.FileSize.Value;

        if (request.MimeType != null)
            document["sprk_mimetype"] = request.MimeType;

        if (request.GraphItemId != null)
            document["sprk_graphitemid"] = request.GraphItemId;

        if (request.GraphDriveId != null)
            document["sprk_graphdriveid"] = request.GraphDriveId;

        await _serviceClient.UpdateAsync(document, ct);
        _logger.LogInformation("Document updated: {DocumentId}", id);
    }

    public Task DeleteDocumentAsync(string id, CancellationToken ct = default)
    {
        _serviceClient.DeleteAsync("sprk_document", Guid.Parse(id), ct);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default)
    {
        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet("sprk_documentname", "sprk_containerid", "sprk_hasfile",
                                     "sprk_filename", "sprk_graphitemid", "sprk_graphdriveid",
                                     "createdon", "modifiedon"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sprk_containerid", ConditionOperator.Equal, Guid.Parse(containerId))
                }
            }
        };

        var results = await _serviceClient.RetrieveMultipleAsync(query, ct);
        return results.Entities.Select(MapToDocumentEntity).ToList();
    }

    public async Task<bool> TestConnectionAsync()
    {
        await Task.CompletedTask;
        return _serviceClient?.IsReady == true;
    }

    public async Task<bool> TestDocumentOperationsAsync()
    {
        try
        {
            var query = new QueryExpression("sprk_document")
            {
                ColumnSet = new ColumnSet("sprk_documentid"),
                TopCount = 1
            };

            await _serviceClient.RetrieveMultipleAsync(query);
            _logger.LogInformation("Dataverse document operations test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataverse document operations test failed");
            return false;
        }
    }

    public Task<DocumentAccessLevel> GetDocumentAccessAsync(string documentId, string userId, CancellationToken ct = default)
    {
        // Simplified - assume full access for application user
        return Task.FromResult(DocumentAccessLevel.FullControl);
    }

    public Task<DocumentAccessLevel> GetUserAccessAsync(string resourceId, string userId, CancellationToken ct = default)
    {
        // Simplified - assume full access for application user
        return Task.FromResult(DocumentAccessLevel.FullControl);
    }

    private DocumentEntity MapToDocumentEntity(Entity entity)
    {
        return new DocumentEntity
        {
            Id = entity.Id.ToString(),
            Name = entity.GetAttributeValue<string>("sprk_documentname") ?? "Untitled",
            Description = entity.GetAttributeValue<string>("sprk_description"),
            ContainerId = entity.GetAttributeValue<EntityReference>("sprk_containerid")?.Id.ToString(),
            HasFile = entity.GetAttributeValue<bool>("sprk_hasfile"),
            FileName = entity.GetAttributeValue<string>("sprk_filename"),
            FileSize = entity.GetAttributeValue<long?>("sprk_filesize"),
            MimeType = entity.GetAttributeValue<string>("sprk_mimetype"),
            GraphItemId = entity.GetAttributeValue<string>("sprk_graphitemid"),
            GraphDriveId = entity.GetAttributeValue<string>("sprk_graphdriveid"),
            Status = (DocumentStatus)(entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1),
            CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
            ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _serviceClient?.Dispose();
            _disposed = true;
        }
    }
}
