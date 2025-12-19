# OAuth 2.0 OBO Flow - Error Reference

> **Source**: Knowledge Management - OAuth/OBO
> **Last Updated**: December 3, 2025
> **Applies To**: Debugging OBO authentication failures

---

## TL;DR

Most OBO errors are: wrong token audience (AADSTS50013), wrong scope format (AADSTS70011), missing consent (AADSTS65001), or expired user token (invalid_grant). Check token claims first.

---

## Applies When

- OBO token acquisition failing
- Seeing AADSTS error codes
- Debugging 401/403 after OBO exchange
- Token validation failures

---

## Quick Diagnostic

```bash
# Decode and check token audience
TOKEN="eyJ0eXAiOiJKV1..."
echo $TOKEN | cut -d'.' -f2 | base64 -d 2>/dev/null | jq .aud

# Expected: "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

```csharp
// C# - Inspect token claims
var handler = new JwtSecurityTokenHandler();
var token = handler.ReadJwtToken(userToken);
Console.WriteLine($"Audience: {token.Audiences.FirstOrDefault()}");
Console.WriteLine($"Issuer: {token.Issuer}");
Console.WriteLine($"Expires: {token.ValidTo}");
```

---

## AADSTS50013: Token Audience Mismatch

**Error:**
```
MsalServiceException: AADSTS50013: Assertion audience claim does not match the required value
```

**Cause:** Incoming token was issued for a different API than yours

**Diagnosis:**
```csharp
var token = new JwtSecurityTokenHandler().ReadJwtToken(userToken);
var audience = token.Audiences.FirstOrDefault();
// If audience != "api://{your-api-id}", that's the problem
```

**Solution:**
```csharp
// Validate audience before OBO
if (token.Audiences.FirstOrDefault() != $"api://{_apiAppId}")
{
    throw new UnauthorizedAccessException(
        $"Token audience mismatch. Expected: api://{_apiAppId}, Got: {token.Audiences.FirstOrDefault()}");
}
```

**Root Cause Check:**
- Client app requesting token with wrong scope
- Client should use `api://{your-api-id}/user_impersonation`

---

## AADSTS70011: Invalid Scope

**Error:**
```
MsalServiceException: AADSTS70011: The provided request must include a 'scope' input parameter
```

**Cause:** Not using `.default` scope for OBO

**Wrong:**
```csharp
var scopes = new[] { "User.Read", "Files.ReadWrite" };  // ❌
```

**Correct:**
```csharp
var scopes = new[] { "https://graph.microsoft.com/.default" };  // ✅
```

**Why:** OBO requires `.default` scope - it grants all pre-consented permissions for the target resource.

---

## AADSTS65001: No Consent

**Error:**
```
MsalServiceException: AADSTS65001: The user or administrator has not consented to use the application
```

**Cause:** Downstream API permissions not consented, or missing `knownClientApplications`

**Solution 1 - Add knownClientApplications:**
```json
// In your API's app manifest (Azure Portal)
{
  "knownClientApplications": [
    "5175798e-f23e-41c3-b09b-7a90b9218189"  // Client app ID
  ]
}
```

**Solution 2 - Admin consent:**
```
https://login.microsoftonline.com/{tenant}/adminconsent?client_id={api-app-id}
```

**Solution 3 - Grant permissions in Azure Portal:**
1. App registrations → Your API → API permissions
2. Add permissions for downstream API (Graph, etc.)
3. Grant admin consent

---

## invalid_grant: User Token Expired

**Error:**
```
MsalServiceException: invalid_grant: AADSTS50173: The provided grant has expired
```

**Cause:** User's access token has expired or been revoked

**Solution:**
```csharp
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    // User must re-authenticate - can't refresh OBO tokens
    throw new UnauthorizedAccessException(
        "Your session has expired. Please sign in again.", ex);
}
```

**Note:** OBO tokens cannot be refreshed - when the original user token expires, user must re-authenticate.

---

## AADSTS7000218: Missing Client Assertion

**Error:**
```
MsalServiceException: AADSTS7000218: The request body must contain the following parameter: 'client_assertion' or 'client_secret'
```

**Cause:** Using PublicClientApplication instead of ConfidentialClientApplication

**Wrong:**
```csharp
var pca = PublicClientApplicationBuilder.Create(clientId).Build();  // ❌
await pca.AcquireTokenOnBehalfOf(...);
```

**Correct:**
```csharp
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(secret)  // ✅ Has secret
    .Build();
await cca.AcquireTokenOnBehalfOf(...);
```

---

## Error → Quick Fix Reference

| Error Code | Quick Fix |
|------------|-----------|
| `AADSTS50013` | Check token audience matches your API ID |
| `AADSTS70011` | Use `.default` scope, not individual permissions |
| `AADSTS65001` | Add `knownClientApplications` or grant admin consent |
| `invalid_grant` | User token expired - user must sign in again |
| `AADSTS7000218` | Use ConfidentialClientApplication with secret |
| `AADSTS500011` | App not registered as Application User in Dataverse |

---

## Related Articles

- [oauth-obo-implementation.md](oauth-obo-implementation.md) - Implementation patterns
- [oauth-obo-anti-patterns.md](oauth-obo-anti-patterns.md) - Common mistakes
- [sdap-troubleshooting.md](sdap-troubleshooting.md) - SDAP-specific errors

---

*Condensed from OAuth/OBO knowledge base*
