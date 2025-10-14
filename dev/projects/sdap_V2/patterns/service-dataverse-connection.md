# Pattern: Dataverse Connection (Client Secret)

**Use For**: Connecting to Dataverse with S2S authentication
**Task**: Implementing Singleton Dataverse client with connection pooling
**Time**: 15 minutes

---

## Quick Copy-Paste

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse service client using client secret authentication.
/// Singleton lifetime for connection pooling (ADR-010).
/// </summary>
public class DataverseServiceClientImpl
{
    private readonly ServiceClient _serviceClient;
    private readonly ILogger<DataverseServiceClientImpl> _logger;

    public DataverseServiceClientImpl(
        IOptions<DataverseOptions> options,
        ILogger<DataverseServiceClientImpl> logger)
    {
        _logger = logger;
        var config = options.Value;

        // Connection string with client secret auth
        var connectionString =
            $"AuthType=ClientSecret;" +
            $"Url={config.ServiceUrl};" +
            $"ClientId={config.ClientId};" +
            $"ClientSecret={config.ClientSecret};" +
            $"RequireNewInstance=false;"; // Enable connection pooling

        try
        {
            _serviceClient = new ServiceClient(connectionString);

            if (!_serviceClient.IsReady)
            {
                var lastError = _serviceClient.LastError;
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse: {lastError}");
            }

            _logger.LogInformation(
                "Connected to Dataverse: {OrgName} (v{Version})",
                _serviceClient.ConnectedOrgFriendlyName,
                _serviceClient.ConnectedOrgVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Dataverse connection");
            throw;
        }
    }

    /// <summary>
    /// Create document record in Dataverse.
    /// </summary>
    public async Task<Guid> CreateDocumentAsync(
        string documentName,
        string containerId,
        string driveItemId,
        long fileSize,
        Guid parentRecordId,
        string parentLookupField,
        CancellationToken cancellationToken = default)
    {
        var entity = new Entity("sprk_document")
        {
            ["sprk_documentname"] = documentName,
            ["sprk_filename"] = documentName,
            ["sprk_graphdriveid"] = containerId,
            ["sprk_graphitemid"] = driveItemId,
            ["sprk_filesize"] = fileSize,
            [parentLookupField] = new EntityReference("sprk_matter", parentRecordId)
        };

        try
        {
            var documentId = await _serviceClient.CreateAsync(entity, cancellationToken);

            _logger.LogInformation(
                "Created document {DocumentId} for file {FileName}",
                documentId, documentName);

            return documentId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create document record for {FileName}",
                documentName);
            throw;
        }
    }

    /// <summary>
    /// Get document by ID.
    /// </summary>
    public async Task<Entity?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sprk_documentid", ConditionOperator.Equal, documentId)
                }
            },
            TopCount = 1
        };

        try
        {
            var results = await _serviceClient.RetrieveMultipleAsync(query, cancellationToken);
            return results.Entities.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve document {DocumentId}", documentId);
            throw;
        }
    }

    /// <summary>
    /// Update document record.
    /// </summary>
    public async Task UpdateDocumentAsync(
        Guid documentId,
        Dictionary<string, object> updates,
        CancellationToken cancellationToken = default)
    {
        var entity = new Entity("sprk_document", documentId);

        foreach (var (key, value) in updates)
        {
            entity[key] = value;
        }

        try
        {
            await _serviceClient.UpdateAsync(entity, cancellationToken);
            _logger.LogInformation("Updated document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update document {DocumentId}", documentId);
            throw;
        }
    }

    /// <summary>
    /// Test Dataverse connection.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var whoAmI = await _serviceClient.ExecuteAsync(
                new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());

            _logger.LogInformation("Dataverse connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataverse connection test failed");
            return false;
        }
    }
}
```

---

## Configuration Class

```csharp
using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

public class DataverseOptions
{
    public const string SectionName = "Dataverse";

    [Required]
    [Url]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
```

---

## appsettings.json

```json
{
  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=...SPRK-DEV-DATAVERSE-URL)",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=...BFF-API-ClientSecret)",
    "MaxRetryAttempts": 3,
    "TimeoutSeconds": 30
  }
}
```

---

## Key Points (ADR-010)

1. **Singleton lifetime** - Connection pooling, thread-safe
2. **Client secret auth** - Not Managed Identity (for now)
3. **Connection pooling** - `RequireNewInstance=false`
4. **Fail-fast** - Validate configuration on startup
5. **Async operations** - All methods use async/await

---

## ❌ WRONG: Managed Identity (Current Implementation)

```csharp
// DON'T DO THIS (current broken implementation)
var credential = new ManagedIdentityCredential();
_serviceClient = new ServiceClient(
    instanceUrl: new Uri(dataverseUrl),
    tokenProviderFunction: async (uri) => { ... }
);
```

**Problem**: Diverges from sdap_V2 architecture, causes connection issues

---

## ✅ CORRECT: Client Secret (Target Architecture)

```csharp
// DO THIS (target architecture)
var connectionString =
    $"AuthType=ClientSecret;" +
    $"Url={config.ServiceUrl};" +
    $"ClientId={config.ClientId};" +
    $"ClientSecret={config.ClientSecret};" +
    $"RequireNewInstance=false;";

_serviceClient = new ServiceClient(connectionString);
```

---

## Checklist

- [ ] Uses client secret authentication (not MI)
- [ ] Connection string includes `RequireNewInstance=false`
- [ ] Registered as Singleton in DI
- [ ] Options pattern with validation
- [ ] Validates `.IsReady` after connection
- [ ] All operations are async
- [ ] Structured logging

---

## Related Files

- Create: `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
- Create: `src/api/Spe.Bff.Api/Configuration/DataverseOptions.cs`
- Requires: Application User in Dataverse (System Administrator role)

---

## DI Registration

```csharp
// In DataverseModuleExtensions.cs (ADR-010)
public static IServiceCollection AddDataverseModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Register as Singleton for connection pooling
    services.AddSingleton<DataverseServiceClientImpl>();

    return services;
}
```

---

## Dataverse Setup Required

1. **App Registration**: SDAP-BFF-SPE-API (`1e40baad...`)
2. **Application User**: Created in Dataverse with System Administrator role
3. **Client Secret**: Stored in Azure Key Vault (`BFF-API-ClientSecret`)
