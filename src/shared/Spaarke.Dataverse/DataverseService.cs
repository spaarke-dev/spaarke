using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse service implementation using managed identity authentication.
/// Provides secure access to Dataverse without storing credentials.
/// </summary>
public class DataverseService : IDataverseService, IDisposable
{
    private readonly ServiceClient _serviceClient;
    private readonly ILogger<DataverseService> _logger;
    private bool _disposed = false;

    public DataverseService(IConfiguration configuration, ILogger<DataverseService> logger)
    {
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var managedIdentityClientId = configuration["ManagedIdentity:ClientId"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        if (string.IsNullOrEmpty(managedIdentityClientId))
            throw new InvalidOperationException("ManagedIdentity:ClientId configuration is required");

        _logger.LogInformation("Initializing Dataverse connection to {DataverseUrl} using managed identity {ClientId}",
            dataverseUrl, managedIdentityClientId);

        try
        {
            _serviceClient = new ServiceClient(
                new Uri(dataverseUrl),
                async (string authority) => {
                    try
                    {
                        _logger.LogDebug("Acquiring token for authority: {Authority}", authority);

                        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                        {
                            ManagedIdentityClientId = managedIdentityClientId
                        });

                        var tokenScope = $"{dataverseUrl}/.default";
                        var tokenRequest = new TokenRequestContext(new[] { tokenScope });
                        var token = await credential.GetTokenAsync(tokenRequest);

                        _logger.LogDebug("Successfully acquired Dataverse token via managed identity");
                        return token.Token;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to acquire Dataverse token for authority {Authority}", authority);
                        throw;
                    }
                }
            );

            // Validate connection on startup
            ValidateConnection();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Dataverse service");
            throw;
        }
    }

    /// <summary>
    /// Validates the Dataverse connection and logs connection details.
    /// </summary>
    private void ValidateConnection()
    {
        try
        {
            if (_serviceClient?.IsReady == true)
            {
                _logger.LogInformation("Dataverse connection established successfully");
                _logger.LogInformation("Connected to organization: {OrgName} ({OrgId})",
                    _serviceClient.ConnectedOrgFriendlyName, _serviceClient.ConnectedOrgId);
            }
            else
            {
                _logger.LogWarning("Dataverse service client is not ready");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate Dataverse connection");
            throw;
        }
    }

    #region Document Operations

    public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Creating document: {DocumentName} in container {ContainerId}",
                request.Name, request.ContainerId);

            var document = new Entity("sprk_document");
            document["sprk_name"] = request.Name;
            document["sprk_containerid"] = new EntityReference("sprk_container", Guid.Parse(request.ContainerId));
            document["sprk_hasfile"] = false;
            document["sprk_status"] = new OptionSetValue(1); // Draft

            if (!string.IsNullOrEmpty(request.Description))
                document["sprk_documentdescription"] = request.Description;

            var documentId = await _serviceClient.CreateAsync(document);

            _logger.LogInformation("Document created successfully with ID: {DocumentId}", documentId);
            return documentId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create document: {DocumentName}", request.Name);
            throw;
        }
    }

    public async Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Retrieving document: {DocumentId}", id);

            var entity = await _serviceClient.RetrieveAsync("sprk_document", Guid.Parse(id),
                new ColumnSet("sprk_name", "sprk_documentdescription", "sprk_containerid", "sprk_hasfile", "sprk_filename",
                             "sprk_filesize", "sprk_mimetype", "sprk_graphitemid", "sprk_graphdriveid",
                             "sprk_status", "createdon", "modifiedon"));

            if (entity == null)
            {
                _logger.LogWarning("Document not found: {DocumentId}", id);
                return null;
            }

            var document = new DocumentEntity
            {
                Id = entity.Id.ToString(),
                Name = entity.GetAttributeValue<string>("sprk_name"),
                Description = entity.GetAttributeValue<string>("sprk_documentdescription"),
                ContainerId = entity.GetAttributeValue<EntityReference>("sprk_containerid")?.Id.ToString(),
                HasFile = entity.GetAttributeValue<bool>("sprk_hasfile"),
                FileName = entity.GetAttributeValue<string>("sprk_filename"),
                FileSize = entity.GetAttributeValue<long?>("sprk_filesize"),
                MimeType = entity.GetAttributeValue<string>("sprk_mimetype"),
                GraphItemId = entity.GetAttributeValue<string>("sprk_graphitemid"),
                GraphDriveId = entity.GetAttributeValue<string>("sprk_graphdriveid"),
                Status = (DocumentStatus)(entity.GetAttributeValue<OptionSetValue>("sprk_status")?.Value ?? 1),
                CreatedOn = entity.GetAttributeValue<DateTime?>("createdon") ?? DateTime.UtcNow,
                ModifiedOn = entity.GetAttributeValue<DateTime?>("modifiedon") ?? DateTime.UtcNow
            };

            _logger.LogDebug("Document retrieved successfully: {DocumentId}", id);
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve document: {DocumentId}", id);
            throw;
        }
    }

    public async Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Updating document: {DocumentId}", id);

            var document = new Entity("sprk_document");
            document.Id = Guid.Parse(id);

            if (!string.IsNullOrEmpty(request.Name))
                document["sprk_name"] = request.Name;

            if (!string.IsNullOrEmpty(request.Description))
                document["sprk_documentdescription"] = request.Description;

            if (!string.IsNullOrEmpty(request.FileName))
                document["sprk_filename"] = request.FileName;

            if (request.FileSize.HasValue)
                document["sprk_filesize"] = request.FileSize.Value;

            if (!string.IsNullOrEmpty(request.MimeType))
                document["sprk_mimetype"] = request.MimeType;

            if (!string.IsNullOrEmpty(request.GraphItemId))
                document["sprk_graphitemid"] = request.GraphItemId;

            if (!string.IsNullOrEmpty(request.GraphDriveId))
                document["sprk_graphdriveid"] = request.GraphDriveId;

            if (request.HasFile.HasValue)
                document["sprk_hasfile"] = request.HasFile.Value;

            if (request.Status.HasValue)
                document["sprk_status"] = new OptionSetValue((int)request.Status.Value);

            await _serviceClient.UpdateAsync(document);

            _logger.LogInformation("Document updated successfully: {DocumentId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update document: {DocumentId}", id);
            throw;
        }
    }

    public async Task DeleteDocumentAsync(string id, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Deleting document: {DocumentId}", id);

            await _serviceClient.DeleteAsync("sprk_document", Guid.Parse(id));

            _logger.LogInformation("Document deleted successfully: {DocumentId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document: {DocumentId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Retrieving documents for container: {ContainerId}", containerId);

            var query = new QueryExpression("sprk_document")
            {
                ColumnSet = new ColumnSet("sprk_name", "sprk_containerid", "sprk_hasfile", "sprk_filename",
                                         "sprk_filesize", "sprk_status", "createdon", "modifiedon"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("sprk_containerid", ConditionOperator.Equal, Guid.Parse(containerId))
                    }
                },
                Orders =
                {
                    new OrderExpression("modifiedon", OrderType.Descending)
                }
            };

            var results = await _serviceClient.RetrieveMultipleAsync(query);
            var documents = results.Entities.Select(entity => new DocumentEntity
            {
                Id = entity.Id.ToString(),
                Name = entity.GetAttributeValue<string>("sprk_name"),
                ContainerId = containerId,
                HasFile = entity.GetAttributeValue<bool>("sprk_hasfile"),
                FileName = entity.GetAttributeValue<string>("sprk_filename"),
                FileSize = entity.GetAttributeValue<long?>("sprk_filesize"),
                Status = (DocumentStatus)(entity.GetAttributeValue<OptionSetValue>("sprk_status")?.Value ?? 1),
                CreatedOn = entity.GetAttributeValue<DateTime?>("createdon") ?? DateTime.UtcNow,
                ModifiedOn = entity.GetAttributeValue<DateTime?>("modifiedon") ?? DateTime.UtcNow
            });

            _logger.LogDebug("Retrieved {DocumentCount} documents for container {ContainerId}",
                documents.Count(), containerId);

            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve documents for container: {ContainerId}", containerId);
            throw;
        }
    }

    #endregion

    #region Access Control

    public async Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Checking user access: User {UserId} to Document {DocumentId}", userId, documentId);

            // For now, implement basic access control
            // In future, this will integrate with more sophisticated authorization rules

            // Check if user has access to the document record
            var accessQuery = new QueryExpression("sprk_document")
            {
                ColumnSet = new ColumnSet("sprk_documentid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("sprk_documentid", ConditionOperator.Equal, Guid.Parse(documentId))
                    }
                },
                TopCount = 1
            };

            var results = await _serviceClient.RetrieveMultipleAsync(accessQuery);

            if (results.Entities.Count == 0)
            {
                _logger.LogDebug("Document not found for access check: {DocumentId}", documentId);
                return DocumentAccessLevel.None;
            }

            // For MVP: If user can query the document, they have full access
            // TODO: Implement proper role-based access control
            _logger.LogDebug("User {UserId} has full access to Document {DocumentId}", userId, documentId);
            return DocumentAccessLevel.FullControl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check user access: User {UserId} to Document {DocumentId}", userId, documentId);
            return DocumentAccessLevel.None;
        }
    }

    #endregion

    #region Connection Testing

    /// <summary>
    /// Tests the Dataverse connection and basic operations.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            _logger.LogInformation("Testing Dataverse connection...");

            // Test 1: Basic connectivity
            var whoAmI = await _serviceClient.ExecuteAsync(new WhoAmIRequest()) as WhoAmIResponse;
            _logger.LogInformation("Connected to Dataverse as user: {UserId} in organization: {OrgId}",
                whoAmI.UserId, whoAmI.OrganizationId);

            // Test 2: Entity metadata access
            var entityQuery = new QueryExpression("sprk_document")
            {
                ColumnSet = new ColumnSet("sprk_documentid"),
                TopCount = 1
            };

            var testResults = await _serviceClient.RetrieveMultipleAsync(entityQuery);
            _logger.LogInformation("Successfully queried spe_document entity. Found {RecordCount} records",
                testResults.Entities.Count);

            _logger.LogInformation("Dataverse connection test completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataverse connection test failed: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Tests document CRUD operations with cleanup.
    /// </summary>
    public async Task<bool> TestDocumentOperationsAsync()
    {
        Guid? testDocumentId = null;

        try
        {
            _logger.LogInformation("Testing document CRUD operations...");

            // Test CREATE
            var testDoc = new Entity("sprk_document");
            testDoc["sprk_name"] = $"Test Document {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            testDoc["sprk_hasfile"] = false;
            testDoc["sprk_status"] = new OptionSetValue(1); // Draft

            testDocumentId = await _serviceClient.CreateAsync(testDoc);
            _logger.LogInformation("✅ CREATE: Created test document: {DocumentId}", testDocumentId);

            // Test READ
            var retrievedDoc = await _serviceClient.RetrieveAsync("sprk_document", testDocumentId.Value,
                new ColumnSet("sprk_name", "sprk_hasfile", "sprk_status"));
            _logger.LogInformation("✅ READ: Retrieved document: {DocumentName}",
                retrievedDoc.GetAttributeValue<string>("sprk_name"));

            // Test UPDATE
            var updateDoc = new Entity("sprk_document");
            updateDoc.Id = testDocumentId.Value;
            updateDoc["sprk_name"] = $"Updated Test Document {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            updateDoc["sprk_hasfile"] = true;

            await _serviceClient.UpdateAsync(updateDoc);
            _logger.LogInformation("✅ UPDATE: Updated test document successfully");

            // Test DELETE
            await _serviceClient.DeleteAsync("sprk_document", testDocumentId.Value);
            _logger.LogInformation("✅ DELETE: Deleted test document successfully");

            testDocumentId = null; // Prevent cleanup attempt

            _logger.LogInformation("All document CRUD operations completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document operations test failed: {Message}", ex.Message);

            // Cleanup test document if it was created
            if (testDocumentId.HasValue)
            {
                try
                {
                    await _serviceClient.DeleteAsync("sprk_document", testDocumentId.Value);
                    _logger.LogInformation("Cleaned up test document: {DocumentId}", testDocumentId);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup test document: {DocumentId}", testDocumentId);
                }
            }

            return false;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _serviceClient?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}