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
/// Uses ClientSecretCredential authentication (same approach as Graph/SPE) for server-to-server scenarios.
/// Requires TENANT_ID, API_APP_ID, and API_CLIENT_SECRET configuration.
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
        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        // Use same authentication approach as Graph/SPE (ClientSecretCredential)
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["API_CLIENT_SECRET"];

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                "Dataverse authentication requires TENANT_ID, API_APP_ID, and API_CLIENT_SECRET configuration. " +
                "These should match the same values used for Graph/SPE authentication.");
        }

        _logger.LogInformation("Initializing Dataverse ServiceClient with ClientSecret for {DataverseUrl}", dataverseUrl);
        _logger.LogInformation("Using ClientId (masked): ...{Suffix}", clientId.Substring(Math.Max(0, clientId.Length - 8)));

        try
        {
            // Use connection string method (Microsoft's recommended approach for server-to-server auth)
            // Format: AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=xxx;ClientSecret=xxx
            var connectionString = $"AuthType=ClientSecret;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret}";

            _logger.LogInformation("Using ClientSecret authentication (connection string method)");

            // Create ServiceClient using connection string
            _serviceClient = new ServiceClient(connectionString);

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
            _logger.LogError(exception: ex, message: "Exception initializing Dataverse ServiceClient");
            throw;
        }
    }

    public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        var document = new Entity("sprk_document");
        document["sprk_documentname"] = request.Name;

        if (!string.IsNullOrEmpty(request.Description))
            document["sprk_documentdescription"] = request.Description;

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
            new ColumnSet("sprk_documentname", "sprk_documentdescription", "sprk_containerid",
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

        // ═══════════════════════════════════════════════════════════════════════════
        // Basic Document Properties
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.Name != null)
            document["sprk_documentname"] = request.Name;

        if (request.Description != null)
            document["sprk_documentdescription"] = request.Description;

        if (request.HasFile.HasValue)
            document["sprk_hasfile"] = request.HasFile.Value;

        if (request.FileName != null)
            document["sprk_filename"] = request.FileName;

        if (request.FileSize.HasValue)
            document["sprk_filesize"] = request.FileSize.Value;

        if (request.MimeType != null)
            document["sprk_filetype"] = request.MimeType;

        if (request.GraphItemId != null)
            document["sprk_graphitemid"] = request.GraphItemId;

        if (request.GraphDriveId != null)
            document["sprk_graphdriveid"] = request.GraphDriveId;

        if (request.Status.HasValue)
            document["statuscode"] = new OptionSetValue((int)request.Status.Value);

        // ═══════════════════════════════════════════════════════════════════════════
        // AI Analysis Fields
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.Summary != null)
            document["sprk_filesummary"] = request.Summary;

        if (request.TlDr != null)
            document["sprk_filetldr"] = request.TlDr;

        if (request.Keywords != null)
            document["sprk_filekeywords"] = request.Keywords;

        if (request.SummaryStatus.HasValue)
            document["sprk_filesummarystatus"] = new OptionSetValue(request.SummaryStatus.Value);

        // ═══════════════════════════════════════════════════════════════════════════
        // Extracted Entities Fields
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.ExtractOrganization != null)
            document["sprk_extractorganization"] = request.ExtractOrganization;

        if (request.ExtractPeople != null)
            document["sprk_extractpeople"] = request.ExtractPeople;

        if (request.ExtractFees != null)
            document["sprk_extractfees"] = request.ExtractFees;

        if (request.ExtractDates != null)
            document["sprk_extractdates"] = request.ExtractDates;

        if (request.ExtractReference != null)
            document["sprk_extractreference"] = request.ExtractReference;

        if (request.ExtractDocumentType != null)
            document["sprk_extractdocumenttype"] = request.ExtractDocumentType;

        if (request.DocumentType.HasValue)
            document["sprk_documenttype"] = new OptionSetValue(request.DocumentType.Value);

        // ═══════════════════════════════════════════════════════════════════════════
        // Email Metadata Fields (for .eml and .msg files)
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.EmailSubject != null)
            document["sprk_emailsubject"] = request.EmailSubject;

        if (request.EmailFrom != null)
            document["sprk_emailfrom"] = request.EmailFrom;

        if (request.EmailTo != null)
            document["sprk_emailto"] = request.EmailTo;

        if (request.EmailDate.HasValue)
            document["sprk_emaildate"] = request.EmailDate.Value;

        if (request.EmailBody != null)
            document["sprk_emailbody"] = request.EmailBody;

        if (request.Attachments != null)
            document["sprk_attachments"] = request.Attachments;

        // ═══════════════════════════════════════════════════════════════════════════
        // Parent Document Fields (for email attachments)
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.ParentDocumentId != null)
            document["sprk_parentdocumentid"] = request.ParentDocumentId;

        if (request.ParentFileName != null)
            document["sprk_parentfilename"] = request.ParentFileName;

        if (request.ParentGraphItemId != null)
            document["sprk_parentgraphitemid"] = request.ParentGraphItemId;

        if (request.ParentDocumentLookup.HasValue)
            document["sprk_parentdocumentname"] = new EntityReference("sprk_document", request.ParentDocumentLookup.Value);

        await _serviceClient.UpdateAsync(document, ct);
        _logger.LogInformation("Document updated: {DocumentId} ({FieldCount} fields)", id, document.Attributes.Count);
    }

    public async Task DeleteDocumentAsync(string id, CancellationToken ct = default)
    {
        await _serviceClient.DeleteAsync("sprk_document", Guid.Parse(id), ct);
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
            _logger.LogError(exception: ex, message: "Dataverse document operations test failed");
            return false;
        }
    }

    public Task<DocumentAccessLevel> GetDocumentAccessAsync(string documentId, string userId, CancellationToken ct = default)
    {
        // Simplified - assume full access for application user
        // Reference parameters to avoid IDE0060 (interface implementation requires these signatures)
        _ = documentId;
        _ = userId;
        _ = ct;
        return Task.FromResult(DocumentAccessLevel.FullControl);
    }

    public Task<DocumentAccessLevel> GetUserAccessAsync(string resourceId, string userId, CancellationToken ct = default)
    {
        // Simplified - assume full access for application user
        return Task.FromResult(DocumentAccessLevel.FullControl);
    }

    // ========================================
    // Metadata Operations (Phase 7)
    // ========================================

    public async Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default)
    {
        _logger.LogDebug("Querying entity set name for {EntityLogicalName}", entityLogicalName);

        try
        {
            var request = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity
            };

            var response = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)await Task.Run(() =>
                _serviceClient.Execute(request), ct);

            var entitySetName = response.EntityMetadata.EntitySetName;

            _logger.LogInformation(
                "Retrieved entity set name for {EntityLogicalName}: {EntitySetName}",
                entityLogicalName,
                entitySetName);

            return entitySetName;
        }
        catch (Exception ex) when (ex.Message.Contains("Could not find") || ex.Message.Contains("does not exist"))
        {
            _logger.LogError(exception: ex, message: "Entity '{EntityLogicalName}' not found in Dataverse metadata", entityLogicalName);
            throw new InvalidOperationException(
                $"Entity '{entityLogicalName}' not found in Dataverse metadata.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(exception: ex, message: "Insufficient permissions to query EntityDefinitions metadata");
            throw new UnauthorizedAccessException(
                "Insufficient permissions to query EntityDefinitions metadata. " +
                "Ensure the application user has 'Read' permission on Entity Definitions.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Failed to retrieve entity set name for {EntityLogicalName}", entityLogicalName);
            throw new InvalidOperationException(
                $"Failed to query metadata for entity '{entityLogicalName}': {ex.Message}", ex);
        }
    }

    public async Task<LookupNavigationMetadata> GetLookupNavigationAsync(
        string childEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Querying lookup navigation metadata: Child={ChildEntity}, Relationship={Relationship}",
            childEntityLogicalName,
            relationshipSchemaName);

        try
        {
            var request = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
            {
                LogicalName = childEntityLogicalName,
                EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Relationships | Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes
            };

            var response = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)await Task.Run(() =>
                _serviceClient.Execute(request), ct);

            // Find the relationship
            var relationship = response.EntityMetadata.ManyToOneRelationships
                .FirstOrDefault(r => r.SchemaName == relationshipSchemaName);

            if (relationship == null)
            {
                var availableRelationships = response.EntityMetadata.ManyToOneRelationships
                    .Select(r => r.SchemaName)
                    .ToList();

                _logger.LogWarning(
                    "Relationship {RelationshipSchemaName} not found on {EntityLogicalName}. " +
                    "Available relationships: {AvailableRelationships}",
                    relationshipSchemaName,
                    childEntityLogicalName,
                    string.Join(", ", availableRelationships));

                throw new InvalidOperationException(
                    $"Relationship '{relationshipSchemaName}' not found on entity '{childEntityLogicalName}'. " +
                    $"Verify the relationship exists and the schema name is correct. " +
                    $"Available relationships: {string.Join(", ", availableRelationships)}");
            }

            // Find the lookup attribute
            var attribute = response.EntityMetadata.Attributes
                .OfType<Microsoft.Xrm.Sdk.Metadata.LookupAttributeMetadata>()
                .FirstOrDefault(a => a.LogicalName == relationship.ReferencingAttribute);

            if (attribute == null)
            {
                _logger.LogError(
                    "Lookup attribute '{ReferencingAttribute}' not found on entity '{EntityLogicalName}'",
                    relationship.ReferencingAttribute,
                    childEntityLogicalName);

                throw new InvalidOperationException(
                    $"Lookup attribute '{relationship.ReferencingAttribute}' not found on entity '{childEntityLogicalName}'");
            }

            var metadata = new LookupNavigationMetadata
            {
                LogicalName = attribute.LogicalName,
                SchemaName = attribute.SchemaName,
                NavigationPropertyName = relationship.ReferencingEntityNavigationPropertyName,
                TargetEntityLogicalName = relationship.ReferencedEntity
            };

            _logger.LogInformation(
                "Retrieved lookup navigation metadata for {ChildEntity}.{Relationship}: NavProperty={NavProperty} (LogicalName={LogicalName}, SchemaName={SchemaName})",
                childEntityLogicalName,
                relationshipSchemaName,
                metadata.NavigationPropertyName,
                metadata.LogicalName,
                metadata.SchemaName);

            return metadata;
        }
        catch (Exception ex) when (ex.Message.Contains("Could not find") || ex.Message.Contains("does not exist"))
        {
            _logger.LogError(exception: ex, message: "Entity '{EntityLogicalName}' not found in Dataverse metadata", childEntityLogicalName);
            throw new InvalidOperationException(
                $"Entity '{childEntityLogicalName}' not found in Dataverse metadata.", ex);
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw relationship/attribute not found exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve lookup navigation metadata for {ChildEntity}.{Relationship}",
                childEntityLogicalName,
                relationshipSchemaName);
            throw new InvalidOperationException(
                $"Failed to query lookup navigation metadata for '{childEntityLogicalName}.{relationshipSchemaName}': {ex.Message}", ex);
        }
    }

    public async Task<string> GetCollectionNavigationAsync(
        string parentEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Querying collection navigation property: Parent={ParentEntity}, Relationship={Relationship}",
            parentEntityLogicalName,
            relationshipSchemaName);

        try
        {
            var request = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
            {
                LogicalName = parentEntityLogicalName,
                EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Relationships
            };

            var response = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)await Task.Run(() =>
                _serviceClient.Execute(request), ct);

            // Find the relationship
            var relationship = response.EntityMetadata.OneToManyRelationships
                .FirstOrDefault(r => r.SchemaName == relationshipSchemaName);

            if (relationship == null)
            {
                var availableRelationships = response.EntityMetadata.OneToManyRelationships
                    .Select(r => r.SchemaName)
                    .ToList();

                _logger.LogWarning(
                    "Relationship {RelationshipSchemaName} not found on {EntityLogicalName}. " +
                    "Available relationships: {AvailableRelationships}",
                    relationshipSchemaName,
                    parentEntityLogicalName,
                    string.Join(", ", availableRelationships));

                throw new InvalidOperationException(
                    $"Relationship '{relationshipSchemaName}' not found on entity '{parentEntityLogicalName}'. " +
                    $"Verify the relationship exists and the schema name is correct. " +
                    $"Available relationships: {string.Join(", ", availableRelationships)}");
            }

            var collectionPropertyName = relationship.ReferencedEntityNavigationPropertyName;

            _logger.LogInformation(
                "Retrieved collection navigation property for {ParentEntity}.{Relationship}: {CollectionProperty}",
                parentEntityLogicalName,
                relationshipSchemaName,
                collectionPropertyName);

            return collectionPropertyName;
        }
        catch (Exception ex) when (ex.Message.Contains("Could not find") || ex.Message.Contains("does not exist"))
        {
            _logger.LogError(exception: ex, message: "Entity '{EntityLogicalName}' not found in Dataverse metadata", parentEntityLogicalName);
            throw new InvalidOperationException(
                $"Entity '{parentEntityLogicalName}' not found in Dataverse metadata.", ex);
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw relationship not found exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to retrieve collection navigation property for {ParentEntity}.{Relationship}",
                parentEntityLogicalName,
                relationshipSchemaName);
            throw new InvalidOperationException(
                $"Failed to query collection navigation property for '{parentEntityLogicalName}.{relationshipSchemaName}': {ex.Message}", ex);
        }
    }

    private DocumentEntity MapToDocumentEntity(Entity entity)
    {
        return new DocumentEntity
        {
            Id = entity.Id.ToString(),
            Name = entity.GetAttributeValue<string>("sprk_documentname") ?? "Untitled",
            Description = entity.GetAttributeValue<string>("sprk_documentdescription"),
            ContainerId = entity.GetAttributeValue<EntityReference>("sprk_containerid")?.Id.ToString(),
            HasFile = entity.GetAttributeValue<bool>("sprk_hasfile"),
            FileName = entity.GetAttributeValue<string>("sprk_filename"),
            FileSize = entity.Contains("sprk_filesize") ? (long?)entity.GetAttributeValue<int>("sprk_filesize") : null,
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
