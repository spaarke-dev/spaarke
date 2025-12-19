# Dataverse OAuth Authentication

> **Last Verified**: December 3, 2025
> **Applies To**: .NET apps connecting to Dataverse Web API, Server-to-server (S2S) scenarios
> **Owner**: Spaarke Engineering
> **Related ADRs**: ADR-001 (Minimal API patterns)

## TL;DR

- **What**: OAuth 2.0 authentication for Dataverse using MSAL library
- **When to use**: Any .NET app calling Dataverse Web API
- **Key constraint**: Use MSAL (not ADAL - deprecated); use `ServiceClient` (not `CrmServiceClient`)
- **Primary pattern**: `DelegatingHandler` for token refresh with `HttpClient`
- **Gotcha**: Tokens expire in ~60 minutes; must implement auto-refresh

## Applies When

| Condition | This Article Applies |
|-----------|---------------------|
| .NET app calling Dataverse Web API | ✅ Yes |
| Interactive user authentication | ✅ Yes |
| Server-to-server (S2S) with app identity | ✅ Yes |
| PCF control calling Dataverse | ❌ No - use PCF context auth |
| Plugin calling external API | ❌ No - see plugin patterns |

## Key Patterns

### Pattern 1: ServiceClient Connection String (Simplest)

**Use when**: Quick setup, console apps, background services

```csharp
// User credentials (interactive)
string connectionString = @"AuthType=OAuth;
    Url=https://yourorg.crm.dynamics.com;
    ClientId=51f81489-12ee-4a9e-aaae-a2591f45987d;
    RedirectUri=http://localhost;
    LoginPrompt=Auto";

using var svc = new ServiceClient(connectionString);
if (svc.IsReady) { /* use svc */ }
```

**Critical details**:
- `ServiceClient` uses MSAL internally (correct)
- `CrmServiceClient` uses ADAL (deprecated - avoid)

---

### Pattern 2: S2S with Client Secret

**Use when**: Background services, scheduled jobs, no interactive user

```csharp
string connectionString = $@"AuthType=ClientSecret;
    SkipDiscovery=true;
    Url=https://yourorg.crm.dynamics.com;
    ClientId={appId};
    Secret={clientSecret};
    RequireNewInstance=true";

using var svc = new ServiceClient(connectionString);
```

**Critical details**:
- Requires app registration with client secret
- Requires application user in Dataverse bound to app registration
- Does NOT consume a paid license

---

### Pattern 3: S2S with Certificate (Production)

**Use when**: Production server-to-server, higher security requirements

```csharp
string connectionString = $@"AuthType=Certificate;
    SkipDiscovery=true;
    Url=https://yourorg.crm.dynamics.com;
    ClientId={appId};
    Thumbprint={certThumbprint};
    RequireNewInstance=true";

using var svc = new ServiceClient(connectionString);
```

---

### Pattern 4: DelegatingHandler for HttpClient (Web API)

**Use when**: Direct Web API calls, need token auto-refresh

```csharp
public class OAuthMessageHandler : DelegatingHandler
{
    private readonly IPublicClientApplication _authBuilder;
    private readonly string[] _scopes;
    
    public OAuthMessageHandler(string serviceUrl, string clientId, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _authBuilder = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithRedirectUri("http://localhost")
            .Build();
        _scopes = new[] { $"{serviceUrl}/user_impersonation" };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accounts = await _authBuilder.GetAccountsAsync();
        AuthenticationResult token;
        try
        {
            token = await _authBuilder.AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException)
        {
            token = await _authBuilder.AcquireTokenInteractive(_scopes)
                .ExecuteAsync(cancellationToken);
        }
        
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

**Critical details**:
- Always try `AcquireTokenSilent` first (uses cache)
- Fall back to interactive only when needed
- Token auto-refreshes on each request

## Common Mistakes

| ❌ Mistake | Why It's Wrong | ✅ Correct Approach |
|-----------|----------------|---------------------|
| Using `CrmServiceClient` | Uses deprecated ADAL library | Use `ServiceClient` |
| Caching token without refresh | Tokens expire in ~60 min | Use `DelegatingHandler` or `ServiceClient` |
| Using user credentials for S2S | Consumes paid license, security risk | Use app registration + application user |
| Scope: `/.default` for public client | Wrong scope type | Use `/user_impersonation` for public clients |
| Scope: `/user_impersonation` for confidential | Wrong scope type | Use `/.default` for confidential clients |
| Hardcoding secrets in code | Security vulnerability | Use Key Vault or environment variables |

## Configuration / Setup

**App Registration Requirements:**

| Scenario | API Permission | Secret/Cert |
|----------|---------------|-------------|
| Interactive user | `Dynamics CRM > user_impersonation` (delegated) | None |
| S2S application | None (uses app user) | Client secret OR certificate |

**Dataverse Application User (S2S only):**
1. Create custom security role with required privileges
2. Create application user in Power Platform Admin Center
3. Bind to app registration's Client ID
4. Assign custom security role

## Error Handling

| Error Scenario | Handling Pattern |
|----------------|------------------|
| `MsalUiRequiredException` | Token cache miss - trigger interactive auth |
| `MsalServiceException` | Auth service error - check app registration |
| 401 Unauthorized | Token expired or invalid - force new token |
| 403 Forbidden | User/app lacks Dataverse privileges |

## Related Resources

| Resource | Purpose |
|----------|---------|
| [MSAL Overview](https://learn.microsoft.com/azure/active-directory/develop/msal-overview) | Official MSAL documentation |
| [Register Dataverse App](https://learn.microsoft.com/power-apps/developer/data-platform/walkthrough-register-app-azure-active-directory) | App registration walkthrough |
| [Create Application User](https://learn.microsoft.com/power-platform/admin/manage-application-users) | S2S user setup |

## Verification Checklist

- [x] Code examples compile without errors
- [x] Patterns align with current ADRs
- [x] No deprecated APIs referenced (ADAL removed)
- [ ] Tested against current version of dependencies
- [x] Common mistakes verified from actual incidents/PRs

---

*Article version: 1.0 | Last verified by: AI Agent on December 3, 2025*
