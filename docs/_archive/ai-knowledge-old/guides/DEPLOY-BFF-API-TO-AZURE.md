# Deploying Sprk.Bff.Api to Azure

> **Last Updated**: December 18, 2025
>
> **Purpose**: End-to-end procedure for making changes to BFF API and deploying to Azure.

---

## Overview

The **Sprk.Bff.Api** is the Backend-for-Frontend service that provides:
- SharePoint Embedded file operations
- Dataverse integration
- On-Behalf-Of (OBO) authentication
- Document checkout/checkin workflows
- AI service orchestration

**Deployment Target**: Azure App Service (`spe-api-dev-67e2xz`)

---

## Prerequisites

### Tools Required

| Tool | Version | Installation |
|------|---------|--------------|
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| Azure CLI | 2.50+ | `winget install Microsoft.AzureCLI` |
| Git | Latest | `winget install Git.Git` |
| PowerShell | 7.0+ | Pre-installed on Windows |

### Azure Access

1. **Azure Subscription**: Access to `spe-infrastructure-westus2` resource group
2. **App Service**: Contributor role on `spe-api-dev-67e2xz`
3. **Authentication**: Run `az login` before deployment

### Verify Azure CLI Login

```bash
# Check current login
az account show

# If not logged in or wrong subscription
az login
az account set --subscription "<subscription-name>"
```

---

## Development Workflow

### 1. Create Feature Branch

```bash
# From master branch
git checkout master
git pull origin master
git checkout -b feature/your-feature-name
```

### 2. Make Code Changes

**Project Location**: `src/server/api/Sprk.Bff.Api/`

**Key Directories**:
```
Sprk.Bff.Api/
├── Api/                    # Endpoint definitions (Minimal API)
├── Services/               # Business logic services
├── Models/                 # Request/response models
├── Infrastructure/         # DI, filters, middleware
└── Program.cs              # Entry point and configuration
```

### 3. Build Locally

```bash
# Build the entire solution
dotnet build

# Build only the BFF API
dotnet build src/server/api/Sprk.Bff.Api/

# Build in Release mode (recommended before deployment)
dotnet build src/server/api/Sprk.Bff.Api/ -c Release
```

### 4. Run Tests

```bash
# Run all tests
dotnet test

# Run only BFF API tests
dotnet test tests/unit/Sprk.Bff.Api.Tests/

# Run with verbose output
dotnet test tests/unit/Sprk.Bff.Api.Tests/ -v n
```

### 5. Run Locally (Optional)

```bash
# Start the API locally
dotnet run --project src/server/api/Sprk.Bff.Api/

# API available at:
# - https://localhost:5001 (HTTPS)
# - http://localhost:5000 (HTTP)

# Health check endpoints:
# - GET /healthz
# - GET /ping
```

---

## Manual Deployment (Feature Branches)

Use manual deployment during active development on feature branches.

### Step 1: Build Release Artifacts

```bash
# Create release build
dotnet publish src/server/api/Sprk.Bff.Api -c Release -o ./publish
```

**Expected Output**: `./publish/` directory containing:
- `Sprk.Bff.Api.dll`
- `Sprk.Bff.Api.exe`
- All dependency DLLs
- `appsettings.json`
- `web.config`

### Step 2: Create Deployment Package

```powershell
# PowerShell (Windows)
Compress-Archive -Path './publish/*' -DestinationPath './deploy.zip' -Force
```

```bash
# Alternative: Git Bash with PowerShell
powershell -Command "Compress-Archive -Path './publish/*' -DestinationPath './deploy.zip' -Force"
```

### Step 3: Deploy to Azure

```bash
# Deploy the zip package
az webapp deploy \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz \
    --src-path ./deploy.zip \
    --type zip
```

**Expected Output**:
```
Getting scm site credentials for zip deployment
Starting zip deployment. This operation can take a while to complete ...
Deployment endpoint responded with status code 202
```

### Step 4: Restart Application (Recommended)

```bash
# Restart to ensure clean state
az webapp restart \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz
```

### Step 5: Verify Deployment

```bash
# Check health endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping

# Expected response:
# {"service":"Spe.Bff.Api","status":"healthy","timestamp":"..."}
```

### Quick Deploy Script (All Steps)

```bash
# One-liner for quick deployment
dotnet publish src/server/api/Sprk.Bff.Api -c Release -o ./publish && \
powershell -Command "Compress-Archive -Path './publish/*' -DestinationPath './deploy.zip' -Force" && \
az webapp deploy --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --src-path ./deploy.zip --type zip && \
az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
```

---

## CI/CD Deployment (Production)

Production deployments are automated via GitHub Actions.

### Workflows

| Workflow | File | Trigger | Target |
|----------|------|---------|--------|
| Deploy to Azure | `.github/workflows/deploy-to-azure.yml` | Push to `main`, manual dispatch | `spe-api-dev-67e2xz` |
| Deploy Staging | `.github/workflows/deploy-staging.yml` | After SDAP CI completes on `master` | Staging slot |

### Triggering CI/CD Deployment

**Option 1: Merge to Main/Master**
```bash
# Create PR and merge
git push origin feature/your-feature-name
# Create PR via GitHub UI
# Merge triggers automatic deployment
```

**Option 2: Manual Workflow Dispatch**
1. Go to GitHub Actions
2. Select "Deploy SPE Infrastructure to Azure"
3. Click "Run workflow"
4. Select environment (dev/staging/prod)
5. Click "Run workflow"

### CI/CD Pipeline Stages

```
┌─────────────────┐     ┌─────────────────────┐     ┌─────────────────┐
│  build-and-test │────▶│ deploy-infrastructure│────▶│   deploy-api    │
│                 │     │                     │     │                 │
│ - Restore       │     │ - Deploy Bicep      │     │ - Download      │
│ - Build         │     │ - Update monitoring │     │   artifacts     │
│ - Test          │     │                     │     │ - Deploy to     │
│ - Publish       │     │                     │     │   App Service   │
│ - Upload        │     │                     │     │ - Update        │
│   artifacts     │     │                     │     │   settings      │
└─────────────────┘     └─────────────────────┘     └─────────────────┘
                                                            │
                                                            ▼
                                               ┌─────────────────────────┐
                                               │ run-integration-tests   │
                                               │                         │
                                               │ - Run tests against     │
                                               │   deployed API          │
                                               └─────────────────────────┘
```

---

## Verification Checklist

After deployment, verify the following:

### Health Checks

```bash
# Basic health
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping

# Detailed health (if available)
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

### API Endpoints

```bash
# Test a protected endpoint (requires auth token)
curl -H "Authorization: Bearer <token>" \
    https://spe-api-dev-67e2xz.azurewebsites.net/api/documents
```

### View Logs

```bash
# Stream live logs (Ctrl+C to stop)
az webapp log tail \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz

# View recent logs
az webapp log download \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz \
    --log-file ./logs.zip
```

---

## Troubleshooting

### Build Failures

**Error: DLL locked by running process**
```bash
# Solution: Build in Release mode
dotnet build -c Release

# Or stop any running dotnet processes
taskkill /F /IM dotnet.exe
```

**Error: Missing dependencies**
```bash
# Restore NuGet packages
dotnet restore
```

### Deployment Failures

**Error: 401 Unauthorized**
```bash
# Re-authenticate with Azure
az login
az account set --subscription "<subscription-name>"
```

**Error: 404 Resource not found**
```bash
# Verify resource group and app name
az webapp list --resource-group spe-infrastructure-westus2 --output table
```

**Error: Deployment timeout**
```bash
# Check deployment status
az webapp deployment list-publishing-profiles \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz
```

### Runtime Errors

**500 Internal Server Error**
```bash
# Check application logs
az webapp log tail \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz

# Check App Insights (if configured)
# Azure Portal > App Service > Application Insights
```

**Configuration Issues**
```bash
# List current app settings
az webapp config appsettings list \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz \
    --output table
```

---

## Environment Configuration

### App Settings

Key app settings managed via Azure Portal or CI/CD:

| Setting | Description |
|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Environment name (dev/staging/prod) |
| `UAMI_CLIENT_ID` | User-Assigned Managed Identity client ID |
| `TENANT_ID` | Azure AD tenant ID |
| `API_APP_ID` | BFF API app registration ID |

### Secrets

Secrets are stored in Azure Key Vault and referenced via app settings:
- `AzureAd:ClientSecret`
- `SharePointEmbedded:ContainerTypeId`

---

## Rollback Procedure

If deployment causes issues:

### Option 1: Redeploy Previous Version

```bash
# Find previous deployment
az webapp deployment list \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz

# Redeploy from Git
git checkout <previous-commit>
# Follow manual deployment steps
```

### Option 2: Slot Swap (Staging Only)

```bash
# Swap staging back to production
az webapp deployment slot swap \
    --resource-group spe-infrastructure-westus2 \
    --name spe-api-dev-67e2xz \
    --slot staging \
    --target-slot production
```

---

## Best Practices

1. **Always test locally** before deploying
2. **Run tests** before manual deployment
3. **Use feature branches** for development
4. **Deploy to dev first** before staging/production
5. **Monitor logs** after deployment
6. **Keep deployments small** - deploy frequently with smaller changes
7. **Document breaking changes** in PR descriptions

---

## Related Documentation

- [BFF API Module Guide](../../server/api/Sprk.Bff.Api/CLAUDE.md)
- [ADR-001: Minimal API and Workers](../../reference/adr/ADR-001-minimal-api-and-workers.md)
- [ADR-008: Endpoint Filters](../../reference/adr/ADR-008-endpoint-filters-for-resource-authorization.md)
- [Azure Resource Naming](../../reference/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)

---

*Last updated: December 2025*
