# Service Principal Authentication Pattern

> **Domain**: OAuth / Service-to-Service Auth
> **Last Validated**: 2025-12-25
> **Source ADRs**: ADR-004, ADR-016

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ServicePrincipalAuth.cs` | SP token acquisition |
| `src/server/api/Sprk.Bff.Api/Services/DataverseClient.cs` | Dataverse SP auth |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/AppOnlyGraphClient.cs` | Graph SP auth |

---

## Service Principal vs OBO

| Scenario | Use |
|----------|-----|
| User-delegated access (user context) | OBO Flow |
| Background jobs (no user) | Service Principal |
| Admin operations | Service Principal |
| Cross-tenant access | Service Principal |

---

## Implementation Pattern

### 1. ConfidentialClientApplication Setup

```csharp
public class ServicePrincipalAuth
{
    private readonly IConfidentialClientApplication _cca;
    private readonly ILogger<ServicePrincipalAuth> _logger;

    public ServicePrincipalAuth(IOptions<AzureAdOptions> options, ILogger<ServicePrincipalAuth> logger)
    {
        _logger = logger;
        var config = options.Value;

        _cca = ConfidentialClientApplicationBuilder
            .Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId)
            .WithClientSecret(config.ClientSecret)
            .Build();
    }

    public async Task<string> AcquireTokenAsync(string[] scopes)
    {
        try
        {
            var result = await _cca
                .AcquireTokenForClient(scopes)
                .ExecuteAsync();

            return result.AccessToken;
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex, "Failed to acquire SP token: {ErrorCode}", ex.ErrorCode);
            throw;
        }
    }
}
```

### 2. Scopes for Common Services

```csharp
// Graph API - app permissions
private static readonly string[] GraphScopes = new[]
{
    "https://graph.microsoft.com/.default"
};

// Dataverse - app permissions
private static readonly string[] DataverseScopes = new[]
{
    "https://{org}.crm.dynamics.com/.default"
};

// SharePoint - app permissions
private static readonly string[] SharePointScopes = new[]
{
    "https://{tenant}.sharepoint.com/.default"
};
```

### 3. Usage in Background Workers

```csharp
public class DocumentProcessingWorker : BackgroundService
{
    private readonly ServicePrincipalAuth _spAuth;
    private readonly IHttpClientFactory _httpClientFactory;

    public DocumentProcessingWorker(
        ServicePrincipalAuth spAuth,
        IHttpClientFactory httpClientFactory)
    {
        _spAuth = spAuth;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Acquire app-only token
            var token = await _spAuth.AcquireTokenAsync(
                new[] { "https://graph.microsoft.com/.default" });

            var client = _httpClientFactory.CreateClient("GraphApi");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            // Make Graph API calls with app permissions
            await ProcessDocumentsAsync(client, stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

---

## Token Caching

MSAL caches tokens automatically, but for distributed scenarios:

```csharp
public class ServicePrincipalAuth
{
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _tokenCacheDuration = TimeSpan.FromMinutes(55);

    public async Task<string> AcquireTokenAsync(string[] scopes)
    {
        var cacheKey = $"sp_token:{string.Join(",", scopes)}";

        // Check cache first
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
            return cached;

        // Acquire fresh token
        var result = await _cca.AcquireTokenForClient(scopes).ExecuteAsync();

        // Cache with 55-minute TTL (tokens valid for 60 min)
        await _cache.SetStringAsync(cacheKey, result.AccessToken,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _tokenCacheDuration });

        return result.AccessToken;
    }
}
```

---

## Certificate Authentication (Production)

```csharp
// Prefer certificates over secrets in production
_cca = ConfidentialClientApplicationBuilder
    .Create(config.ClientId)
    .WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId)
    .WithCertificate(LoadCertificate(config.CertificateThumbprint))
    .Build();

private static X509Certificate2 LoadCertificate(string thumbprint)
{
    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadOnly);

    var certs = store.Certificates.Find(
        X509FindType.FindByThumbprint, thumbprint, validOnly: false);

    return certs.Count > 0
        ? certs[0]
        : throw new InvalidOperationException($"Certificate {thumbprint} not found");
}
```

---

## Managed Identity (Azure)

```csharp
// Use DefaultAzureCredential in Azure (App Service, Functions, AKS)
public class ManagedIdentityAuth
{
    private readonly TokenCredential _credential;

    public ManagedIdentityAuth()
    {
        _credential = new DefaultAzureCredential();
    }

    public async Task<string> AcquireTokenAsync(string resource)
    {
        var context = new TokenRequestContext(new[] { $"{resource}/.default" });
        var token = await _credential.GetTokenAsync(context);
        return token.Token;
    }
}
```

---

## Service Registration

```csharp
// Program.cs
services.Configure<AzureAdOptions>(configuration.GetSection("AzureAd"));
services.AddSingleton<ServicePrincipalAuth>();

// For Managed Identity
services.AddSingleton<TokenCredential>(sp =>
    new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential = true,
        ExcludeVisualStudioCredential = true
    }));
```

---

## Error Handling

```csharp
try
{
    var token = await _spAuth.AcquireTokenAsync(scopes);
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS700016")
{
    // App not found in tenant
    _logger.LogError("Service principal not configured in tenant");
    throw new InvalidOperationException("Authentication configuration error");
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS7000215")
{
    // Invalid client secret
    _logger.LogError("Invalid client secret");
    throw new InvalidOperationException("Authentication configuration error");
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS70011")
{
    // Invalid scope
    _logger.LogError("Invalid scope: {Scopes}", string.Join(", ", scopes));
    throw;
}
```

---

## Key Points

1. **Always use `.default` scope** for client credentials flow
2. **Prefer managed identity** in Azure (no secrets to manage)
3. **Use certificates** in production (more secure than secrets)
4. **Cache tokens** to reduce token requests (55-min TTL)
5. **Separate app registrations** for SP auth vs user auth

---

## Related Patterns

- [OAuth Scopes](oauth-scopes.md) - Scope format requirements
- [OBO Flow](obo-flow.md) - User-delegated authentication
- [Token Caching](token-caching.md) - Distributed token caching

---

**Lines**: ~200
