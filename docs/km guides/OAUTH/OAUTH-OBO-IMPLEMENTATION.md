KM-1.02-OBO-OAUTH-OBO-IMPLEMENTATION

# OAuth 2.0 OBO Flow - Implementation Patterns

## Pattern 1: Basic OBO (No Caching)
```csharp
public class GraphClientFactory
{
    private readonly IConfidentialClientApplication _cca;
    
    public GraphClientFactory(IConfiguration config)
    {
        _cca = ConfidentialClientApplicationBuilder
            .Create(config["API_APP_ID"])
            .WithClientSecret(config["API_CLIENT_SECRET"])
            .WithAuthority(AzureCloudInstance.AzurePublic, config["TENANT_ID"])
            .Build();
    }
    
    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userToken)
    {
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        var userAssertion = new UserAssertion(userToken);
        
        var result = await _cca
            .AcquireTokenOnBehalfOf(scopes, userAssertion)
            .ExecuteAsync();
        
        return CreateGraphClientWithToken(result.AccessToken);
    }
}
```

## Pattern 2: OBO with Token Caching (Recommended)
```csharp
public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userToken)
{
    var tokenHash = ComputeTokenHash(userToken);
    
    // Check cache first
    var cached = await _tokenCache.GetTokenAsync(tokenHash);
    if (cached != null) return CreateGraphClientWithToken(cached);
    
    // Cache miss - perform OBO
    var result = await _cca
        .AcquireTokenOnBehalfOf(
            new[] { "https://graph.microsoft.com/.default" },
            new UserAssertion(userToken))
        .ExecuteAsync();
    
    // Cache with 55-min TTL (5-min buffer)
    var ttl = result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
    if (ttl > TimeSpan.Zero)
    {
        await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, ttl);
    }
    
    return CreateGraphClientWithToken(result.AccessToken);
}
```

## Pattern 3: Error Handling
```csharp
try
{
    var result = await _cca.AcquireTokenOnBehalfOf(scopes, userAssertion).ExecuteAsync();
    return result.AccessToken;
}
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    throw new UnauthorizedAccessException("User token expired", ex);
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS50013")
{
    throw new UnauthorizedAccessException("Token audience mismatch", ex);
}
```

## Required Configuration
```json
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(...)",
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
}
```

## Validation
```bash
# Test OBO flow manually
curl -X PUT https://localhost:5001/api/obo/files/test.pdf \
  -H "Authorization: Bearer {user-token}" \
  --data-binary @test.pdf

# Check logs for:
# "OBO token acquired successfully"
```