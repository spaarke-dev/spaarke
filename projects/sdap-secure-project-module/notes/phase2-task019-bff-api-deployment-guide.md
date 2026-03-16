# Phase 2 — BFF API Deployment Guide (Task 019)

> **Purpose**: Deploy the BFF API with Phase 2 external access endpoints to Azure App Service.
> **Prerequisites**: Tasks 017 (unit tests passing) must be complete.
> **Azure Resource**: `spe-api-dev-67e2xz` (App Service, `spe-infrastructure-westus2` resource group)

---

## Pre-Deployment Checklist

- [ ] All Phase 2 unit tests pass: `dotnet test`
- [ ] Build is clean: `dotnet build src/server/api/Sprk.Bff.Api/` (0 errors, 0 warnings)
- [ ] Azure CLI authenticated: `az account show`
- [ ] App Service `spe-api-dev-67e2xz` is running

---

## New Configuration Required

Before deploying, add the following app settings to the Azure App Service:

### Via Azure Portal (App Service → Configuration → Application settings)

| Setting | Value | Notes |
|---------|-------|-------|
| `PowerPages:BaseUrl` | `https://{your-portal}.powerappsportals.com` | Portal base URL for token validation |
| `PowerPages:SecureProjectParticipantWebRoleId` | `{GUID of web role}` | From Power Pages after task 021 |

### Via Azure CLI

```bash
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings \
    "PowerPages__BaseUrl=https://{your-portal}.powerappsportals.com" \
    "PowerPages__SecureProjectParticipantWebRoleId={web-role-guid}"
```

---

## Deployment Steps

### Step 1: Build

```bash
cd c:\code_files\spaarke-wt-sdap-secure-project-module

# Clean build
dotnet build src/server/api/Sprk.Bff.Api/ --configuration Release
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

### Step 2: Run Unit Tests

```bash
dotnet test tests/Sprk.Bff.Api.Tests/ --configuration Release --no-build
```

Expected: All tests pass (including ExternalAccess tests from task 017).

### Step 3: Deploy to Azure App Service

```bash
# Using deployment script
pwsh scripts/Deploy-BffApi.ps1

# OR manual dotnet publish + az webapp deploy:
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o ./publish

az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path ./publish \
  --type zip
```

### Step 4: Verify Health Check

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: {"status":"Healthy","results":{}}

curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: pong
```

### Step 5: Verify External Access Endpoints Exist

```bash
# Should return 401 (endpoint exists, auth required)
curl -i https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external/me
# Expected: HTTP/1.1 401 Unauthorized

curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/grant \
  -H "Content-Type: application/json" -d "{}"
# Expected: HTTP/1.1 401 Unauthorized

curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/revoke \
  -H "Content-Type: application/json" -d "{}"
# Expected: HTTP/1.1 401 Unauthorized

curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/invite \
  -H "Content-Type: application/json" -d "{}"
# Expected: HTTP/1.1 401 Unauthorized

curl -i -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/close-project \
  -H "Content-Type: application/json" -d "{}"
# Expected: HTTP/1.1 401 Unauthorized
```

### Step 6: Run Test Script (if available)

```bash
# If Test-SdapBffApi.ps1 is configured for external access endpoints:
pwsh scripts/Test-SdapBffApi.ps1 -BaseUrl https://spe-api-dev-67e2xz.azurewebsites.net
```

---

## New Endpoints Deployed

| Method | Route | Purpose | Auth |
|--------|-------|---------|------|
| GET | `/api/v1/external/me` | Portal user context | Portal JWT |
| POST | `/api/v1/external-access/grant` | Grant Contact access | Azure AD |
| POST | `/api/v1/external-access/revoke` | Revoke Contact access | Azure AD |
| POST | `/api/v1/external-access/invite` | Create portal invitation | Azure AD |
| POST | `/api/v1/external-access/close-project` | Close project + cascade revocation | Azure AD |

---

## Rollback

If deployment fails or endpoints don't respond:

```bash
# Restart App Service
az webapp restart \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz

# View logs
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

---

*Deployment guide for task 019 | Phase 2 external access endpoints | 2026-03-16*
