# Pattern: Graph Client Factory (OBO Flow)

**Use For**: Creating Graph clients with token caching
**Task**: Implementing OBO token exchange with 97% latency reduction
**Time**: 30 minutes

---

## Quick Copy-Paste

```csharp
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Authentication.Azure;

namespace Spe.Bff.Api.Infrastructure.Graph;

public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfidentialClientApplication _cca;
    private readonly GraphTokenCache _tokenCache;
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string? _clientSecret;

    public GraphClientFactory(
        IHttpClientFactory httpClientFactory,
        GraphTokenCache tokenCache,
        IConfiguration configuration,
        ILogger<GraphClientFactory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenCache = tokenCache;
        _logger = logger;

        _tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID not configured");
        _clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID not configured");
        _clientSecret = configuration["API_CLIENT_SECRET"];

        var ccaBuilder = ConfidentialClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}");

        if (!string.IsNullOrWhiteSpace(_clientSecret))
            ccaBuilder = ccaBuilder.WithClientSecret(_clientSecret);

        _cca = ccaBuilder.Build();

        _logger.LogInformation(
            "GraphClientFactory initialized for client {ClientId}",
            _clientId.Substring(0, 8) + "...");
    }

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Step 1: Check cache first (95% hit rate expected)
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
        var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedToken != null)
        {
            _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..8]);
            return CreateGraphClientWithToken(cachedToken);
        }

        _logger.LogDebug("Cache MISS for token hash {Hash}..., performing OBO", tokenHash[..8]);

        // Step 2: Perform OBO exchange (~200ms)
        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[]
            {
                "https://graph.microsoft.com/Sites.FullControl.All",
                "https://graph.microsoft.com/Files.ReadWrite.All"
            },
            userAssertion: new UserAssertion(userAccessToken))
            .ExecuteAsync();

        _logger.LogInformation("OBO token exchange completed, caching for 55 minutes");

        // Step 3: Cache token (55-min TTL, 5-min buffer)
        await _tokenCache.SetTokenAsync(
            tokenHash,
            result.AccessToken,
            TimeSpan.FromMinutes(55));

        return CreateGraphClientWithToken(result.AccessToken);
    }

    public GraphServiceClient CreateAppOnlyClient()
    {
        TokenCredential credential;

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            credential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
            _logger.LogDebug("Creating app-only client with ClientSecretCredential");
        }
        else
        {
            credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
                ExcludeSharedTokenCacheCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeVisualStudioCredential = true
            });
            _logger.LogDebug("Creating app-only client with DefaultAzureCredential");
        }

        var authProvider = new AzureIdentityAuthenticationProvider(
            credential,
            scopes: new[] { "https://graph.microsoft.com/.default" });

        var httpClient = _httpClientFactory.CreateClient("GraphClient");
        return new GraphServiceClient(httpClient, authProvider);
    }

    private GraphServiceClient CreateGraphClientWithToken(string accessToken)
    {
        var tokenCredential = new SimpleTokenCredential(accessToken);
        var authProvider = new AzureIdentityAuthenticationProvider(
            tokenCredential,
            scopes: new[] { "https://graph.microsoft.com/.default" });

        var httpClient = _httpClientFactory.CreateClient("GraphClient");
        return new GraphServiceClient(httpClient, authProvider);
    }
}
```

---

## Performance Impact

**Without Caching**:
- Every request: 200ms OBO exchange
- 1000 requests/hour: **3.3 minutes** waiting

**With Caching** (ADR-009):
- Cache hit (95%): 5ms
- Cache miss (5%): 200ms
- Average: **~15ms** (97% reduction)

---

## Key Points

1. **Check cache first** - 95% of requests hit cache
2. **55-minute TTL** - 5-minute buffer before 60-minute token expiration
3. **SHA256 hash** - User token hashed for cache key security
4. **Named HttpClient** - Uses IHttpClientFactory for Polly policies
5. **Two auth patterns**:
   - `CreateOnBehalfOfClientAsync()` - User context (OBO)
   - `CreateAppOnlyClient()` - App context (admin ops)

---

## Checklist

- [ ] Injects `GraphTokenCache` (Singleton)
- [ ] Injects `IHttpClientFactory`
- [ ] Checks cache before OBO exchange
- [ ] Caches tokens with 55-minute TTL
- [ ] Logs cache hits/misses
- [ ] Uses SHA256 hash for cache keys
- [ ] Both OBO and app-only methods implemented
- [ ] Named HttpClient ("GraphClient") used

---

## Related Files

- Create: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`
- Requires: `GraphTokenCache` (see `service-graph-token-cache.md`)
- Requires: `IGraphClientFactory` interface
- Used by: `SpeFileStore`

---

## DI Registration

```csharp
// In DocumentsModuleExtensions.cs
services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
services.AddSingleton<GraphTokenCache>();
```
