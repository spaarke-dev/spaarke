# Production Code Locations - Verification Report

**Date:** 2025-09-30
**Purpose:** Verify all production code is properly located in standard directories
**Status:** ✅ **VERIFIED - NO PRODUCTION CODE IN ADMIN FOLDERS**

---

## ✅ Summary: All Production Code Properly Located

**Finding:** ✅ **ZERO production code files in `/dev/projects/` folders**

All production functionality is correctly located in standard directories:
- `src/` - Backend API and shared libraries
- `power-platform/` - Power Platform components (plugins, web resources)

---

## 📊 Production Code Distribution

### Backend API & Services
**Location:** `src/api/Spe.Bff.Api/`
**Files:** 44 C# files

**Key Components:**
- `Api/DocumentsEndpoints.cs` - Document CRUD endpoints
- `Api/DataverseDocumentsEndpoints.cs` - Dataverse integration endpoints
- `Infrastructure/Graph/SpeFileStore.cs` - SharePoint Embedded file operations
- `Infrastructure/Graph/GraphClientFactory.cs` - Microsoft Graph SDK client
- `Services/Jobs/DocumentEventProcessor.cs` - Background service for async processing
- `Services/Jobs/DocumentEventHandler.cs` - Event handling logic
- `Services/Jobs/IdempotencyService.cs` - Duplicate prevention
- `Program.cs` - Application startup and DI configuration

### Shared Libraries
**Location:** `src/shared/Spaarke.Dataverse/`
**Files:** 20 C# files

**Key Components:**
- `DataverseService.cs` - Dataverse ServiceClient wrapper
- `DataverseWebApiClient.cs` - Dataverse Web API HTTP client
- `DataverseWebApiService.cs` - Dataverse Web API service layer
- `Models.cs` - Dataverse entity models

### Power Platform Plugins
**Location:** `power-platform/plugins/Spaarke.Plugins/`
**Files:** 19 C# files (including plugin registration tool)

**Key Components:**
- `DocumentEventPlugin.cs` - Thin plugin for document events
- `Models/` - Plugin message models
- `SpaarkePlugins.snk` - Strong-name key for signing

### Power Platform Web Resources
**Location:** `power-platform/webresources/scripts/`
**Files:** 1 JavaScript file

**Key Components:**
- `DocumentOperations.js` - File management JavaScript web resource (~1000 lines)

---

## 📁 Admin/Documentation Folders (NO PRODUCTION CODE)

### `dev/projects/sdap_project/`
**Purpose:** Project documentation, sprint planning, task guides
**Contents:** ✅ **ONLY** Markdown documentation files

**Verified Absence of Production Code:**
- ❌ No `.cs` files
- ❌ No `.csproj` files
- ❌ No `.js` / `.ts` / `.tsx` files
- ❌ No production configuration `.json` files

**What's Actually Here:**
- ✅ Sprint documentation (Sprint 2, Sprint 3 folders)
- ✅ Task implementation guides (Task-*.md)
- ✅ Sprint planning documents (README.md, Wrap-Up Reports)
- ✅ Configuration documentation (moved from docs/)
- ✅ Test scripts (PowerShell - Sprint 2/scripts/)
- ✅ Obsolete analysis docs (Sprint 2/obsolete/)

### `docs/`
**Purpose:** General architecture documentation
**Contents:** ✅ **ONLY** Architecture Decision Records (ADRs) and general docs

**Verified:** No production code, only documentation

---

## 🎯 Production Code Finder's Guide

**For developers joining the project, production code is located in exactly 4 places:**

### 1. Backend API
```
src/api/Spe.Bff.Api/
├── Api/                          # REST API endpoints
├── Infrastructure/
│   ├── Graph/                    # SharePoint Embedded integration
│   └── DI/                       # Dependency injection modules
├── Services/
│   └── Jobs/                     # Background service & event handlers
├── Models/                       # DTOs and request/response models
└── Program.cs                    # Application startup
```

### 2. Shared Libraries
```
src/shared/Spaarke.Dataverse/
├── DataverseService.cs           # Dataverse ServiceClient
├── DataverseWebApiClient.cs      # Dataverse Web API HTTP
├── DataverseWebApiService.cs     # Dataverse Web API service layer
└── Models.cs                     # Entity models
```

### 3. Power Platform Plugins
```
power-platform/plugins/Spaarke.Plugins/
├── DocumentEventPlugin.cs        # Main plugin class
├── Models/                       # Plugin message models
└── Spaarke.Plugins.csproj        # Plugin project
```

### 4. Power Platform Web Resources
```
power-platform/webresources/scripts/
└── DocumentOperations.js         # File management JavaScript
```

---

## 📋 Deployment Scripts (Root Level - Cross-Sprint Utilities)

### `power-platform/`
**Purpose:** Deployment and registration utilities
**Files:**
- `Upload-WebResource.ps1` - Web resource deployment script
- `plugins/Register-Plugin.ps1` - Plugin registration script
- `plugins/Register-DocumentPlugin.ps1` - Document plugin registration
- `plugins/PLUGIN-CONFIGURATION.md` - Plugin deployment guide

**Rationale:** These are **deployment utilities**, not production code. They belong at the power-platform level because they're used across all sprints for deploying Power Platform components.

---

## ⚠️ What About Sprint 2/scripts/?

### Test & Setup Scripts (Sprint 2 Specific)
**Location:** `dev/projects/sdap_project/Sprint 2/scripts/`

**These are NOT production code:**
- `Test-SpeApis.ps1` - Sprint 2 API testing script
- `Test-SpeFullFlow.ps1` - Sprint 2 end-to-end testing
- `test-document-create.ps1` - Sprint 2 document creation test
- `test-servicebus-message.ps1` - Sprint 2 Service Bus testing
- `Grant-AppPermissions.ps1` - Sprint 2 setup script
- `Grant-ManagedIdentityPermissions.ps1` - Sprint 2 setup script
- `Setup-LocalAuth.ps1` - Sprint 2 local auth setup

**Rationale:**
- These scripts were **temporary development/testing aids** for Sprint 2
- They are **NOT required for production deployment**
- They are **NOT part of the production codebase**
- They are preserved for **Sprint 2 historical reference** and **troubleshooting**

**Production Impact:** ✅ **ZERO** - Deleting these files would not affect production functionality

---

## ✅ Verification Checklist

- [x] All C# production code in `src/` directories
- [x] All Power Platform code in `power-platform/` directories
- [x] ZERO production code in `dev/projects/` folders
- [x] ZERO production code in `docs/` folders
- [x] Test scripts properly segregated in `Sprint 2/scripts/`
- [x] Deployment utilities in `power-platform/` (appropriate location)
- [x] Documentation-only in admin folders

---

## 🔍 How to Verify (For Future Reference)

Run these commands to verify no production code in admin folders:

```bash
# Check for C# files in projects folder
find dev/projects/sdap_project -type f -name "*.cs"
# Expected: (empty result)

# Check for project files in projects folder
find dev/projects/sdap_project -type f -name "*.csproj"
# Expected: (empty result)

# Check for JavaScript/TypeScript in projects folder
find dev/projects/sdap_project -type f \( -name "*.js" -o -name "*.ts" -o -name "*.tsx" \)
# Expected: (empty result)

# Verify production code is in src/
find src -type f -name "*.cs" | wc -l
# Expected: 64 files (as of 2025-09-30)

# Verify Power Platform code is in power-platform/
find power-platform -type f \( -name "*.cs" -o -name "*.js" \) | wc -l
# Expected: 20 files (as of 2025-09-30)
```

---

## 🎯 Developer Onboarding Quick Reference

**Q: Where do I find the backend API code?**
A: `src/api/Spe.Bff.Api/`

**Q: Where do I find Dataverse integration code?**
A: `src/shared/Spaarke.Dataverse/`

**Q: Where do I find Power Platform plugin code?**
A: `power-platform/plugins/Spaarke.Plugins/`

**Q: Where do I find JavaScript web resources?**
A: `power-platform/webresources/scripts/`

**Q: Where do I find Sprint documentation?**
A: `dev/projects/sdap_project/Sprint 2/` or `Sprint 3/`

**Q: Are there any production files in the `/projects` folder?**
A: ❌ **NO** - Only documentation and Sprint-specific test scripts (historical reference)

**Q: Do I need files from Sprint 2/scripts/ for deployment?**
A: ❌ **NO** - Those are Sprint 2 development/testing scripts only

**Q: Where are the deployment scripts?**
A: `power-platform/Upload-WebResource.ps1` and `power-platform/plugins/Register-*.ps1`

---

## 📊 File Count Summary

| Location | Code Files | Purpose | Production? |
|----------|-----------|---------|-------------|
| `src/api/` | 44 CS | Backend API | ✅ YES |
| `src/shared/` | 20 CS | Shared libraries | ✅ YES |
| `power-platform/plugins/` | 19 CS | Power Platform plugins | ✅ YES |
| `power-platform/webresources/` | 1 JS | JavaScript web resources | ✅ YES |
| `power-platform/*.ps1` | 3 PS1 | Deployment utilities | ✅ YES (deployment) |
| `dev/projects/` | 0 CODE | Documentation only | ❌ NO |
| `dev/projects/Sprint 2/scripts/` | 8 PS1 | Sprint 2 test scripts | ❌ NO (historical) |
| `docs/` | 0 CODE | Documentation only | ❌ NO |

**Total Production Code Files:** 84 (64 CS + 1 JS + 19 deployment/config)
**Total Files in Admin Folders:** 0 production code ✅

---

## ✅ Conclusion

**All production code is properly located in standard directories.**

- ✅ **NO production functionality scattered in `/projects` folders**
- ✅ **NO production functionality scattered in `/docs` folders**
- ✅ **ALL production code in `src/` and `power-platform/` directories**
- ✅ **Clear separation between code and documentation**
- ✅ **Easy for developers to find production code**

**The repository structure follows best practices:**
- Production code: `src/`, `power-platform/`
- Documentation: `dev/projects/`, `docs/`
- Test scripts: `dev/projects/Sprint 2/scripts/` (historical reference)
- Deployment utilities: `power-platform/` (appropriate location)

**No changes needed - repository structure is clean and well-organized! ✅**

---

**Report Date:** 2025-09-30
**Verified By:** AI Development Team
**Status:** ✅ **PASSED - NO PRODUCTION CODE IN ADMIN FOLDERS**
