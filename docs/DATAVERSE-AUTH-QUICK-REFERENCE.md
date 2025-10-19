# Dataverse Authentication - Quick Reference Card

**Print this and keep it handy!**

---

## The One Rule

> **ALWAYS use `ServiceClient` with connection string for Dataverse S2S authentication.**
>
> **NEVER use custom HttpClient with manual token handling.**

---

## Connection String Format

```
AuthType=ClientSecret;
SkipDiscovery=true;
Url={dataverseUrl};
ClientId={clientId};
ClientSecret={clientSecret};
RequireNewInstance=true
```

---

## Code Template

```csharp
using Microsoft.PowerPlatform.Dataverse.Client;

var connectionString = $"AuthType=ClientSecret;SkipDiscovery=true;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret};RequireNewInstance=true";
var serviceClient = new ServiceClient(connectionString);

if (!serviceClient.IsReady)
{
    throw new InvalidOperationException($"Failed to connect: {serviceClient.LastError}");
}
```

---

## Required Configuration

| Setting | Value |
|---------|-------|
| `TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `API_APP_ID` | `170c98e1-d486-4355-bcbe-170454e0207c` |
| `Dataverse__ServiceUrl` | `https://spaarkedev1.api.crm.dynamics.com` |
| `Dataverse__ClientSecret` | (from KeyVault: `BFF-API-ClientSecret`) |

---

## Health Check

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse
```

**Expected**: `{"status":"healthy","message":"Dataverse connection successful"}`

---

## PowerShell Verification

```powershell
$tokenResponse = Invoke-RestMethod -Method Post `
    -Uri "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token" `
    -Body @{
        grant_type = "client_credentials"
        client_id = "170c98e1-d486-4355-bcbe-170454e0207c"
        client_secret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj"
        scope = "https://spaarkedev1.api.crm.dynamics.com/.default"
    }

Invoke-RestMethod -Method Get `
    -Uri "https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/WhoAmI" `
    -Headers @{ Authorization = "Bearer $($tokenResponse.access_token)" }
```

**If PowerShell works but .NET doesn't**: You're not using `ServiceClient` correctly.

---

## Required NuGet Package

```xml
<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
```

Version: `1.1.32` (managed in `Directory.Packages.props`)

---

## Application User Checklist

Power Platform Admin Center → SPAARKE DEV 1 → Application users:

- [ ] App ID: `170c98e1-d486-4355-bcbe-170454e0207c`
- [ ] Name: "Spaarke DSM-SPE Dev 2"
- [ ] Status: **Active** ✅
- [ ] Security Role: **System Administrator** ✅

---

## Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| HTTP 500 from Dataverse | Using custom HttpClient | Use ServiceClient |
| HTTP 401 Unauthorized | Client secret expired | Update KeyVault secret |
| HTTP 403 Forbidden | Missing Security Role | Add role to App User |
| `IsReady = false` | Wrong URL or credentials | Check configuration |
| 503 health check | App User not created | Create App User in Dataverse |

---

## Full Documentation

📖 See: `docs/DATAVERSE-AUTHENTICATION-GUIDE.md`

---

**Last Updated**: 2025-10-06
**Verified**: ✅ Sprint 7A deployment
