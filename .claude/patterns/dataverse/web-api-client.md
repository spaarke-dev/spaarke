# Web API Client Pattern

> **Domain**: Dataverse REST API Access
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-007, ADR-010

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` | Pure REST client (ManagedIdentity) |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | ServiceClient wrapper (ClientSecret) |
| `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | Service interface |

---

## Two Client Approaches

### 1. DataverseWebApiClient (Pure REST)

For .NET 8 without System.ServiceModel dependencies (lines 16-162):

```csharp
public class DataverseWebApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;

    public DataverseWebApiClient(IConfiguration configuration, ILogger<DataverseWebApiClient> logger)
    {
        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = configuration["ManagedIdentity:ClientId"]
        });

        _httpClient = new HttpClient { BaseAddress = new Uri(_apiUrl) };
    }
}
```

### 2. DataverseServiceClientImpl (SDK)

Using Microsoft.PowerPlatform.Dataverse.Client (lines 17-74):

```csharp
public class DataverseServiceClientImpl : IDataverseService, IDisposable
{
    private readonly ServiceClient _serviceClient;

    public DataverseServiceClientImpl(IConfiguration configuration, ILogger logger)
    {
        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["API_CLIENT_SECRET"];

        // Connection string method (Microsoft recommended)
        var connectionString = $"AuthType=ClientSecret;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret}";
        _serviceClient = new ServiceClient(connectionString);

        if (!_serviceClient.IsReady)
            throw new InvalidOperationException($"Failed to connect: {_serviceClient.LastError}");
    }
}
```

---

## Authentication Pattern

Token refresh with 5-minute buffer (DataverseWebApiClient lines 52-67):

```csharp
private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
{
    // Refresh if expired or within 5 minutes of expiry
    if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
    {
        var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
        _currentToken = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { scope }), ct);

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);
    }
}
```

---

## CRUD Operations (Web API)

### Create (POST)

Extract ID from OData-EntityId header (lines 84-102):

```csharp
public async Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken ct = default)
{
    await EnsureAuthenticatedAsync(ct);

    var response = await _httpClient.PostAsJsonAsync(entitySetName, entity, ct);
    response.EnsureSuccessStatusCode();

    // Extract ID from header: /api/data/v9.2/sprk_documents(guid)
    var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
    var idString = entityIdHeader.Split('(', ')')[1];
    return Guid.Parse(idString);
}
```

### Retrieve (GET)

With optional $select (lines 69-82):

```csharp
public async Task<T?> RetrieveAsync<T>(string entitySetName, Guid id, string? select = null, CancellationToken ct = default)
{
    await EnsureAuthenticatedAsync(ct);

    var query = select != null ? $"?$select={select}" : "";
    var url = $"{entitySetName}({id}){query}";

    var response = await _httpClient.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
}
```

### Update (PATCH)

```csharp
public async Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken ct = default)
{
    await EnsureAuthenticatedAsync(ct);

    var url = $"{entitySetName}({id})";
    var response = await _httpClient.PatchAsJsonAsync(url, entity, ct);
    response.EnsureSuccessStatusCode();
}
```

### Query with OData

Build query parameters (lines 128-147):

```csharp
public async Task<List<T>> QueryAsync<T>(
    string entitySetName,
    string? filter = null,
    string? select = null,
    int? top = null,
    CancellationToken ct = default)
{
    var queryParams = new List<string>();
    if (filter != null) queryParams.Add($"$filter={filter}");
    if (select != null) queryParams.Add($"$select={select}");
    if (top != null) queryParams.Add($"$top={top}");

    var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
    var url = $"{entitySetName}{query}";

    var response = await _httpClient.GetAsync(url, ct);
    var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<T>>(ct);
    return result?.Value ?? new List<T>();
}
```

---

## OData Response Wrapper

```csharp
private class ODataCollectionResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = new();

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}
```

---

## Key Points

1. **Pure REST vs SDK** - Use DataverseWebApiClient for .NET 8 compatibility
2. **5-minute token buffer** - Refresh before expiry
3. **OData-EntityId header** - Extract GUID from create response
4. **Logging** - Log URL at debug level for troubleshooting
5. **Async all the way** - All operations use CancellationToken

---

## Related Patterns

- [Entity Operations](entity-operations.md) - Late-bound entity patterns
- [Relationship Navigation](relationship-navigation.md) - @odata.bind patterns

---

**Lines**: ~120
