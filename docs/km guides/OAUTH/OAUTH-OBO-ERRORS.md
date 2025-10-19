OAUTH-OBO-ERRORS

# OAuth 2.0 OBO Flow - Error Reference

## AADSTS50013: Token Audience Mismatch

**Error:**
```
MsalServiceException: AADSTS50013: Assertion audience claim does not match
```

**Cause:** Incoming token not intended for your API

**Solution:**
```csharp
var token = new JwtSecurityTokenHandler().ReadJwtToken(userToken);
if (token.Audiences.FirstOrDefault() != $"api://{_apiAppId}")
{
    throw new UnauthorizedAccessException("Token for wrong API");
}
```

---

## AADSTS70011: Invalid Scope

**Error:**
```
MsalServiceException: AADSTS70011: scope input parameter missing
```

**Cause:** Not using `.default` scope

**Solution:**
```csharp
var scopes = new[] { "https://graph.microsoft.com/.default" };
```

---

## AADSTS65001: No Consent

**Error:**
```
MsalServiceException: AADSTS65001: User has not consented
```

**Cause:** Missing `knownClientApplications` or consent

**Solution:**
1. Add to API manifest:
```json
{
  "knownClientApplications": ["170c98e1-..."]
}
```

2. Or request admin consent for API permissions

---

## invalid_grant: User Token Expired

**Error:**
```
MsalServiceException: invalid_grant
```

**Cause:** User's access token expired or revoked

**Solution:**
```csharp
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    throw new UnauthorizedAccessException("Please sign in again", ex);
}
```

---

## Quick Diagnostic
```bash
# Check token audience
jwt=$(echo $TOKEN | cut -d'.' -f2 | base64 -d 2>/dev/null)
echo $jwt | jq .aud

# Expected: "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```