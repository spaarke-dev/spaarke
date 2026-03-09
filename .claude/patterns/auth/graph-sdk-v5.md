# Graph SDK v5 Pattern

> **Domain**: Microsoft Graph SDK v5 / Client Setup / Auth Modes
> **Last Validated**: 2026-03-09
> **Source ADRs**: ADR-004, ADR-007, ADR-010

---

## Canonical Implementation

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | Factory with ForApp() and ForUserAsync() |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SimpleTokenCredential.cs` | TokenCredential wrapper for pre-acquired tokens |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Http/GraphHttpMessageHandler.cs` | Polly resilience handler |

---

## SDK Version

```xml
<!-- Sprk.Bff.Api.csproj -->
<PackageReference Include="Microsoft.Graph" Version="5.99.0" />
<PackageReference Include="Microsoft.Kiota.Abstractions" Version="..." />
<PackageReference Include="Microsoft.Kiota.Authentication.Azure" Version="..." />
```

**CRITICAL**: All `Microsoft.Kiota.*` packages must be version-aligned. Mismatched Kiota versions cause cryptic runtime errors. After any NuGet update, verify all Kiota packages share the same version.

---

## Graph SDK v5 vs v4 Differences

| Aspect | SDK v4 (Obsolete) | SDK v5 (Current) |
|--------|-------------------|-------------------|
| Auth provider | `DelegateAuthenticationProvider` | `AzureIdentityAuthenticationProvider` |
| Token credential | Manual header injection | `Azure.Core.TokenCredential` |
| Request builders | `client.Me.Request().GetAsync()` | `client.Me.GetAsync()` |
| Error type | `ServiceException` | `ODataError` |
| Package system | `Microsoft.Graph.Auth` | `Microsoft.Kiota.*` |

**Do NOT** follow any documentation showing `DelegateAuthenticationProvider` — that is the v4 API.

---

## Two Auth Modes

### ForApp() — App-Only Authentication

Uses `ClientSecretCredential` from `Azure.Identity`. No user context required.

```csharp
// GraphClientFactory.ForApp() implementation pattern
var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
var authProvider = new AzureIdentityAuthenticationProvider(
    credential,
    scopes: new[] { "https://graph.microsoft.com/.default" }
);

// Named HttpClient with resilience handler (retry, circuit breaker, timeout)
var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

// Use beta endpoint for SPE container operations; v1.0 for standard operations
return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
```

**Use for**: Background jobs, webhook subscriptions, admin container operations, outbound email from shared mailboxes, inbound email monitoring.

### ForUserAsync(HttpContext, CancellationToken) — OBO Authentication

Exchanges user's Bearer token for a Graph token via On-Behalf-Of flow.

```csharp
// GraphClientFactory.ForUserAsync() implementation pattern

// 1. Extract Bearer token from Authorization header
var userAccessToken = TokenHelper.ExtractBearerToken(ctx);

// 2. Check Redis cache first (55-min TTL, keyed by SHA256 hash of user token)
var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);

if (cachedToken != null)
{
    return CreateGraphClientFromToken(cachedToken);  // Cache HIT (~5ms)
}

// 3. Cache MISS — perform OBO exchange (~200ms)
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] { "https://graph.microsoft.com/.default" },  // Always .default
    new UserAssertion(userAccessToken)
).ExecuteAsync();

// 4. Cache the Graph token
await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

return CreateGraphClientFromToken(result.AccessToken);
```

**Use for**: User file operations (SPE), send-as-user email, user info (`/me`), anything where user identity matters.

### SimpleTokenCredential — Bridging Pre-Acquired Tokens to SDK v5

Graph SDK v5 requires a `TokenCredential`. For OBO tokens (already acquired via MSAL), wrap them:

```csharp
// SimpleTokenCredential wraps a pre-acquired token as Azure.Core.TokenCredential
internal sealed class SimpleTokenCredential : TokenCredential
{
    private readonly string _accessToken;

    public SimpleTokenCredential(string accessToken)
    {
        _accessToken = accessToken;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct)
    {
        return new AccessToken(_accessToken, DateTimeOffset.UtcNow.AddMinutes(55));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct)
    {
        return new ValueTask<AccessToken>(new AccessToken(_accessToken, DateTimeOffset.UtcNow.AddMinutes(55)));
    }
}
```

Then create the client:

```csharp
private GraphServiceClient CreateGraphClientFromToken(string accessToken)
{
    var tokenCredential = new SimpleTokenCredential(accessToken);
    var authProvider = new AzureIdentityAuthenticationProvider(
        tokenCredential,
        scopes: new[] { "https://graph.microsoft.com/.default" }
    );
    var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

    // v1.0 for standard operations
    return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/v1.0");
}
```

---

## Graph API Version Selection

| Endpoint | Version | Why |
|----------|---------|-----|
| App-only SPE container operations | `beta` | SPE FileStorage APIs require beta |
| OBO user file operations | `v1.0` | Standard drives/items API works on v1.0 |
| Mail send/read | `v1.0` | Stable mail API |
| Subscriptions/webhooks | `v1.0` | Stable subscription API |

**Rule**: Default to `v1.0`. Only use `beta` when the API is not available on v1.0 (e.g., SPE container management).

---

## Resilience Integration

All Graph clients use a named `HttpClient` with `GraphHttpMessageHandler`:

```csharp
// In Program.cs / DI registration
services.AddHttpClient("GraphApiClient")
    .AddHttpMessageHandler<GraphHttpMessageHandler>();
```

`GraphHttpMessageHandler` provides:
- **Retry**: 3 retries with exponential backoff, honors `Retry-After` header (429 throttling)
- **Circuit Breaker**: Opens after consecutive failures, protects against cascading failure
- **Timeout**: Per-request timeout (configurable via `GraphResilienceOptions`)

See [Resilience Pattern](../api/resilience.md) for configuration details.

---

## Architecture Rules (ADR-007)

- **MUST** access Graph through `IGraphClientFactory` — never construct `GraphServiceClient` directly
- **MUST** wrap file operations behind `SpeFileStore` facade — no Graph SDK types in endpoint code
- **MUST NOT** inject `GraphServiceClient` into endpoint handlers or controllers
- **MUST NOT** expose `Microsoft.Graph.Models` types in API responses — map to DTOs

```
Endpoint → SpeFileStore (facade) → DriveItemOperations → IGraphClientFactory → GraphServiceClient
                                                                                    ↓
                                                                              Microsoft Graph API
```

---

## Adding a New Graph Feature (Step-by-Step)

1. **Determine auth mode**: See [Auth Mode Decision Guide](oauth-scopes.md#auth-mode-decision-guide)
2. **Check existing operations**: See [Graph Endpoints Catalog](graph-endpoints-catalog.md)
3. **Create operation class** in `Infrastructure/Graph/` (e.g., `CalendarOperations.cs`)
4. **Inject `IGraphClientFactory`** — call `ForApp()` or `ForUserAsync()` as needed
5. **Map responses to DTOs** — no `Microsoft.Graph.Models` types in return values
6. **Register in DI** — concrete type, no interface needed (ADR-010)
7. **Handle errors**: Catch `ODataError`, map via `ProblemDetailsHelper.FromGraphException()`
8. **Verify permissions**: Check [oauth-scopes.md](oauth-scopes.md) and add if needed

---

## Related Patterns

- [OAuth Scopes](oauth-scopes.md) - Full permission inventory and decision guide
- [OBO Flow](obo-flow.md) - Token exchange implementation details
- [Service Principal](service-principal.md) - App-only auth configuration
- [Graph Endpoints Catalog](graph-endpoints-catalog.md) - Existing BFF Graph surface
- [Graph Webhooks](graph-webhooks.md) - Subscription lifecycle

---

**Lines**: ~185
