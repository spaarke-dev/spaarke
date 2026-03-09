# Dataverse On-Behalf-Of (OBO) Flow Pattern

> **Domain**: OAuth / Token Exchange / Dataverse
> **Last Validated**: 2026-03-09
> **Source ADRs**: ADR-004, ADR-009
> **Split from**: obo-flow.md (Graph OBO remains there)

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | OBO implementation + auth check |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/TokenHelper.cs` | Token extraction |

---

## Flow Overview

```
PCF → [Token A] → BFF API → [OBO Exchange] → [Token B] → Dataverse API
                      │
                      ├─→ Extract bearer token (TokenHelper)
                      ├─→ OBO exchange via MSAL (DataverseAccessDataSource)
                      ├─→ Set token on HttpClient headers (CRITICAL)
                      ├─→ Lookup Dataverse user (systemusers query)
                      └─→ Check document access (direct query)
```

### Key Differences from Graph API OBO

| Aspect | Graph API OBO | Dataverse OBO |
|--------|---------------|---------------|
| **Scope** | `https://graph.microsoft.com/.default` | `https://{org}.crm.dynamics.com/.default` |
| **Token caching** | ✅ Redis (55-min TTL) | ❌ No caching (future enhancement) |
| **Authorization API** | Native permissions check | ⚠️ Direct query (RetrievePrincipalAccess doesn't support OBO) |
| **HttpClient setup** | Token set per-request | ⚠️ Must set `DefaultRequestHeaders.Authorization` |
| **Usage context** | SPE file operations | AI document authorization |

---

## Implementation Steps

### Step 1: Extract User Token from Request

```csharp
// AiAuthorizationService.cs
string? userAccessToken = null;
try
{
    userAccessToken = TokenHelper.ExtractBearerToken(httpContext);
    _logger.LogDebug("[AI-AUTH] Extracted user bearer token for OBO authentication");
}
catch (UnauthorizedAccessException ex)
{
    _logger.LogError(ex, "[AI-AUTH] Failed to extract bearer token from request");
    return AuthorizationResult.Denied("Missing or invalid authorization token");
}

// Pass to DataverseAccessDataSource
var accessSnapshot = await _accessDataSource.GetUserAccessAsync(
    userId,
    documentId,
    userAccessToken,  // User's PCF token
    cancellationToken);
```

### Step 2: OBO Token Exchange (Dataverse)

```csharp
// DataverseAccessDataSource.cs
private async Task<string> GetDataverseTokenViaOBOAsync(string userToken, CancellationToken ct)
{
    _logger.LogDebug("[OBO-DIAG] Starting OBO token exchange for Dataverse");

    var cca = ConfidentialClientApplicationBuilder
        .Create(_clientId)                    // BFF App Registration ID
        .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
        .WithClientSecret(_clientSecret)      // API_CLIENT_SECRET
        .Build();

    var scopes = new[] { $"{_dataverseUri}/.default" };  // Dataverse audience

    var result = await cca.AcquireTokenOnBehalfOf(
        scopes,
        new UserAssertion(userToken)          // PCF user token
    ).ExecuteAsync(ct);

    _logger.LogDebug("[OBO-DIAG] OBO token acquired successfully");
    return result.AccessToken;                // Dataverse-scoped token
}
```

### Step 3: Set Authorization Header (CRITICAL)

```csharp
// DataverseAccessDataSource.cs - GetUserAccessAsync()
if (!string.IsNullOrEmpty(userAccessToken))
{
    dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

    // CRITICAL: Set the OBO token on HttpClient headers for all subsequent API calls
    // WITHOUT THIS: HttpClient uses old service principal token → 401 Unauthorized
    _httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", dataverseToken);
}
```

**Why This Is Critical:**
- MSAL OBO exchange returns a token, but doesn't modify HttpClient
- If you don't set the header, HttpClient uses previous token (service principal or null)
- Result: Dataverse API calls fail with 401 Unauthorized despite successful OBO exchange

### Step 4: Authorization Check (Direct Query Pattern)

```csharp
// DataverseAccessDataSource.cs
// APPROACH: Query the document directly using the OBO token.
// If the query succeeds, the user has at least Read access (Dataverse enforces this).
// If it fails with 403/404, they don't have access.

var url = $"sprk_documents({resourceId})?$select=sprk_documentid";
var response = await _httpClient.SendAsync(requestMessage, ct);

if (!response.IsSuccessStatusCode)
{
    // 403 or 404 means no access
    return new List<PermissionRecord>();
}

// Success! Grant Read access
return new List<PermissionRecord>
{
    new PermissionRecord(userId, resourceId, AccessRights.Read)
};
```

**Why Not RetrievePrincipalAccess?**
`RetrievePrincipalAccess` action doesn't work with delegated (OBO) tokens — only with application tokens. Returns 404 error code `0x80060888`.

---

## Error Handling

```csharp
try
{
    var dataverseToken = await GetDataverseTokenViaOBOAsync(userToken, ct);
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", dataverseToken);
}
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    _logger.LogWarning("[OBO-DIAG] User token expired or invalid");
    return AccessSnapshot.Empty();  // Fail-closed: deny access
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS65001")
{
    // User or admin hasn't consented to Dataverse permissions
    _logger.LogError("[OBO-DIAG] Consent required for Dataverse access");
    return AccessSnapshot.Empty();
}
```

---

## Azure AD Configuration

```yaml
BFF App Registration (1e40baad):
  Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c

  API Permissions (Delegated):
    - Dynamics CRM.user_impersonation    # Required for OBO
    - Dynamics CRM.user                  # Required for OBO

  # Secret: l8b8Q~J (expires 2027-12-18)
  # See auth-azure-resources.md "Client Secrets Inventory" for full inventory.
```

---

## Debugging

```kql
// Trace full OBO authorization flow
traces
| where timestamp > ago(1h)
| where message contains "UAC-DIAG" or message contains "AI-AUTH" or message contains "OBO-DIAG"
| project timestamp, message, severityLevel
| order by timestamp desc
```

---

## Related Patterns

- [OBO Flow (Graph)](obo-flow.md) - Graph API OBO pattern
- [UAC Access Control](uac-access-control.md) - Authorization check pattern
- [Service Principal](service-principal.md) - App-only auth (where RPA works)

---

**Lines**: ~170
