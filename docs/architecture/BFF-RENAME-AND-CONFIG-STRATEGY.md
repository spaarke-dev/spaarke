# Sprk.Bff.Api Rename and Configuration Strategy

> **Status**: In Progress  
> **Version**: 1.0  
> **Date**: December 4, 2025

## Overview

This document outlines:
1. The rename of `Spe.Bff.Api` → `Sprk.Bff.Api`
2. Azure resource requirements per deployment model
3. Dataverse URL parameterization strategy

---

## 1. BFF Rename: Spe.Bff.Api → Sprk.Bff.Api

### Rationale

The original name `Spe.Bff.Api` (SharePoint Embedded Backend-for-Frontend API) implies it only serves SharePoint Embedded. However, the BFF actually serves as the unified gateway between:

- **Dataverse** - CRM data and business logic
- **SharePoint Embedded (SPE)** - Document storage
- **Azure AI Services** (future) - Document Intelligence, OpenAI, Search

The new name `Sprk.Bff.Api` (Spaarke BFF API) reflects its true role as the central backend orchestration layer.

### Files to Rename

#### Project Files (Physical Rename)
| Current Path | New Path |
|-------------|----------|
| `src/server/api/Spe.Bff.Api/` | `src/server/api/Sprk.Bff.Api/` |
| `src/server/api/Spe.Bff.Api/Spe.Bff.Api.csproj` | `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` |
| `tests/unit/Spe.Bff.Api.Tests/` | `tests/unit/Sprk.Bff.Api.Tests/` |
| `tests/unit/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj` | `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` |

#### Solution File (Spaarke.sln)
- Update project name: `Spe.Bff.Api` → `Sprk.Bff.Api`
- Update project path: `src\server\api\Spe.Bff.Api\Spe.Bff.Api.csproj` → `src\server\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj`
- Update test project name: `Spe.Bff.Api.Tests` → `Sprk.Bff.Api.Tests`
- Update test project path: `tests\unit\Spe.Bff.Api.Tests\Spe.Bff.Api.Tests.csproj` → `tests\unit\Sprk.Bff.Api.Tests\Sprk.Bff.Api.Tests.csproj`

#### Namespace Updates (All .cs Files)
All C# files need namespace updates:
- `namespace Spe.Bff.Api` → `namespace Sprk.Bff.Api`
- `using Spe.Bff.Api` → `using Sprk.Bff.Api`

Files affected:
- `src/server/api/Sprk.Bff.Api/**/*.cs`
- `tests/unit/Sprk.Bff.Api.Tests/**/*.cs`
- `tests/integration/Spe.Integration.Tests/**/*.cs` (uses references)

#### Project File Updates
- `Spe.Bff.Api.csproj`: Update `<AssemblyName>` and `<RootNamespace>`
- `Spe.Bff.Api.Tests.csproj`: Update `<ProjectReference>` path
- `Spaarke.ArchTests.csproj`: Update `<ProjectReference>` path (if exists)

#### Configuration Files
- `appsettings.Development.json`: Update logging category names
- `coverlet.runsettings`: Update Include pattern

#### Documentation Updates
- `README.md`: Update build/run commands
- `CLAUDE.md`: Update path references
- Architecture docs: Update any path references

---

## 2. Azure Resource Inventory by Deployment Model

### Model 1: Spaarke-Hosted (Multi-Tenant)

All infrastructure in Spaarke's Azure subscription, serving multiple customers.

#### Shared Resources (One Instance)
| Resource | Azure Service | Purpose | Notes |
|----------|--------------|---------|-------|
| Key Vault | Azure Key Vault | Secret management | Master vault for shared secrets |
| Redis Cache | Azure Cache for Redis | Token caching, session | Premium for geo-replication |
| Service Bus | Azure Service Bus | Job queue, events | Standard or Premium tier |
| App Service Plan | Azure App Service Plan | Compute hosting | Premium V3 for scale |
| Log Analytics | Azure Log Analytics | Centralized logging | Workspace for all apps |
| Application Insights | Azure Application Insights | APM, telemetry | Connected to Log Analytics |
| Container Registry | Azure Container Registry | Docker images | For containerized deployments |

#### Per-Customer Resources (Isolated)
| Resource | Azure Service | Purpose | Notes |
|----------|--------------|---------|-------|
| Storage Account | Azure Storage | SPE containers, blobs | Customer-isolated data |
| App Registration | Entra ID | BFF auth, Graph/SPE access | Per-tenant identity |
| Key Vault Secrets | Azure Key Vault | Customer-specific secrets | Stored in shared vault with prefixes |

#### AI Resources (Future - Shared)
| Resource | Azure Service | Purpose | Notes |
|----------|--------------|---------|-------|
| Azure OpenAI | Azure OpenAI Service | GPT-4, embeddings | Shared capacity, usage-tracked |
| AI Search | Azure AI Search | Vector/semantic search | Shared index per customer |
| Document Intelligence | Azure AI Document Intelligence | OCR, form extraction | Pay-per-use |

### Model 2: Customer-Hosted (Single Tenant)

All infrastructure in customer's Azure subscription.

#### Resources Per Deployment
| Resource | Azure Service | Purpose | Notes |
|----------|--------------|---------|-------|
| Key Vault | Azure Key Vault | All secrets | Customer owns |
| Redis Cache | Azure Cache for Redis | Token caching | Basic tier sufficient |
| Service Bus | Azure Service Bus | Job queue | Basic tier for single tenant |
| App Service Plan | Azure App Service Plan | BFF hosting | P1v3 minimum |
| App Service | Azure App Service | Sprk.Bff.Api | Single instance |
| Storage Account | Azure Storage | SPE containers | Customer owns all data |
| App Registration | Entra ID | BFF auth | In customer tenant |
| Log Analytics | Azure Log Analytics | Logging | Customer subscription |
| Application Insights | Azure Application Insights | APM | Customer subscription |

#### AI Resources (Optional)
| Resource | Azure Service | Purpose | Notes |
|----------|--------------|---------|-------|
| Azure OpenAI | Azure OpenAI Service | AI features | Customer provisions |
| AI Search | Azure AI Search | Search | Customer provisions |
| Document Intelligence | Azure AI Document Intelligence | OCR | Customer provisions |

---

## 3. Dataverse URL Parameterization

### The Challenge

Every customer has a unique Dataverse environment with a specific URL pattern:
```
https://{org-name}.crm.dynamics.com  (North America)
https://{org-name}.crm4.dynamics.com (EMEA)
https://{org-name}.crm5.dynamics.com (APAC)
https://{org-name}.crm6.dynamics.com (Australia)
https://{org-name}.crm7.dynamics.com (Japan)
https://{org-name}.crm9.dynamics.com (GCC)
https://{org-name}.crm.dynamics.us   (GCC High)
```

This URL must be:
1. Configurable at deployment time
2. Available to the BFF API
3. Available to PCF controls (if they call BFF)
4. Available to Office Add-ins

### Solution Architecture

#### A. BFF API Configuration

**Option 1: App Settings (Recommended)**
```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://customername.crm.dynamics.com",
    "Region": "NAM"
  }
}
```

**Environment Variable Mapping:**
```
DATAVERSE__ENVIRONMENTURL=https://customername.crm.dynamics.com
DATAVERSE__REGION=NAM
```

**Key Vault Reference (Production):**
```json
{
  "Dataverse": {
    "EnvironmentUrl": "@Microsoft.KeyVault(SecretUri=https://vault-name.vault.azure.net/secrets/Dataverse-EnvironmentUrl)"
  }
}
```

#### B. PCF Controls Configuration

PCF controls get Dataverse URL automatically from the Model-Driven App context:
```typescript
// PCF Context provides Dataverse URL automatically
const dataverseUrl = context.page.getClientUrl(); // Returns org URL
```

**If PCF needs to call BFF API:**
```typescript
// BFF URL configured via environment variable or solution setting
const bffUrl = context.webAPI.retrieveEnvironmentVariable("sprk_BffApiUrl");
```

#### C. Office Add-ins Configuration

Add-ins need to know both:
1. **Dataverse URL** - For entity context
2. **BFF API URL** - For backend calls

**Configuration via Manifest:**
```xml
<ExtendedOverrides Url="https://config.spaarke.com/{tenant}/manifest.json" />
```

**Or via Environment Variable lookup:**
```typescript
// Get from Dataverse environment variable
const config = await Xrm.WebApi.retrieveRecord("environmentvariabledefinition", ...);
```

### Configuration Matrix

| Component | Config Method | Source |
|-----------|--------------|--------|
| Sprk.Bff.Api | appsettings.json / Key Vault | Azure App Service Configuration |
| PCF Controls | Context API | Automatic from Dataverse |
| PCF → BFF URL | Environment Variable | `sprk_BffApiUrl` in solution |
| Office Add-ins | Extended Overrides / Env Var | Tenant-specific manifest |
| Workers | appsettings.json / Key Vault | Same as BFF |

### Bicep Parameters

```bicep
// parameters/customer-params.json
{
  "parameters": {
    "dataverseEnvironmentUrl": {
      "value": "https://customername.crm.dynamics.com"
    },
    "dataverseRegion": {
      "value": "NAM"
    },
    "tenantId": {
      "value": "customer-tenant-guid"
    }
  }
}
```

---

## 4. Rename Execution Status

> **Completed**: December 4, 2025

### Pre-Rename Verification
- [x] Solution builds successfully
- [x] Tests can be run

### Step 1: Folder Rename ✅
```powershell
# Completed
Move-Item "src/server/api/Spe.Bff.Api" "src/server/api/Sprk.Bff.Api"
Move-Item "tests/unit/Spe.Bff.Api.Tests" "tests/unit/Sprk.Bff.Api.Tests"
```

### Step 2: File Rename ✅
```powershell
# Completed
Move-Item "src/server/api/Sprk.Bff.Api/Spe.Bff.Api.csproj" "src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj"
Move-Item "src/server/api/Sprk.Bff.Api/Spe.Bff.Api.http" "src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.http"
Move-Item "tests/unit/Sprk.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj" "tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj"
```

### Step 3: Update Solution File ✅
- Project names and paths updated in Spaarke.sln

### Step 4: Update Project Files ✅
- `<AssemblyName>` and `<RootNamespace>` updated
- `<ProjectReference>` paths updated in:
  - tests/unit/Sprk.Bff.Api.Tests/
  - tests/Spaarke.ArchTests/
  - tests/integration/Spe.Integration.Tests/

### Step 5: Update Namespaces ✅
- All .cs files in src/server/api/Sprk.Bff.Api/ updated
- All .cs files in tests/unit/Sprk.Bff.Api.Tests/ updated
- Integration test assertion updated for new service name

### Step 6: Update Configuration ✅
- appsettings.Development.json logging categories
- coverlet.runsettings Include pattern
- .http file variable names

### Step 7: Update Key Documentation ✅
- README.md
- CLAUDE.md (root)
- src/server/api/Sprk.Bff.Api/CLAUDE.md
- tests/CLAUDE.md
- src/server/shared/CLAUDE.md
- docs/reference/architecture/SPAARKE-AI-STRATEGY.md

### Step 8: Post-Rename Verification ✅
- [x] Sprk.Bff.Api builds successfully
- [x] Sprk.Bff.Api.Tests builds successfully
- [x] Tests run (66 passing, 50 failing - pre-existing auth issues)

### Remaining Documentation Updates (Low Priority)
These files have old references but don't affect builds:
- docs/reference/articles/SDAP-ARCHITECTURE-GUIDE-FULL-VERSION.md
- src/server/shared/Spaarke.Core/docs/TECHNICAL-OVERVIEW.md
- src/server/shared/Spaarke.Dataverse/docs/TECHNICAL-OVERVIEW.md
- src/server/api/Sprk.Bff.Api/docs/ (internal docs)
- projects/sdap-fileviewer-enhancements-1/ (project notes)
- tests/manual/RedisValidationTests.ps1
- tests/e2e/specs/spe-file-viewer/performance.spec.ts
- src/client/pcf/ (TypeScript comments)

---

## Next Steps

1. **Execute Rename** - Follow checklist above
2. **Update Azure Resources** - Rename App Service if needed
3. **Update CI/CD** - Update pipeline references
4. **Update Documentation** - Ensure all docs reflect new name
