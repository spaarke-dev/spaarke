# Repository Structure - SPE File Viewer Project

**Date**: 2025-01-21
**Status**: Planning

---

## 📁 Current Spaarke Repository Structure

```
spaarke/
├── src/                           # Source code
│   ├── api/                       # Backend APIs
│   │   ├── Spaarke.Integration.Api/      # Integration API
│   │   └── Spe.Bff.Api/                  # SPE Backend-for-Frontend API ✅
│   │
│   ├── controls/                  # PCF Controls
│   │   ├── UniversalQuickCreate/         # Quick Create PCF ✅
│   │   ├── UniversalDatasetGrid/         # Dataset Grid PCF
│   │   └── [SpeFileViewer/]              # 🆕 File Viewer PCF (TO CREATE)
│   │
│   ├── dataverse/                 # Dataverse plugins and Custom APIs
│   │   └── Spaarke.CustomApiProxy/       # Custom API Proxy plugins ✅
│   │       ├── Plugins/
│   │       │   └── Spaarke.Dataverse.CustomApiProxy/
│   │       │       ├── BaseProxyPlugin.cs         ✅
│   │       │       └── GetDocumentFileUrlPlugin.cs ✅ (TO RENAME)
│   │       └── src/
│   │
│   ├── plugins/                   # Other Dataverse plugins
│   │   └── Spaarke.Dataverse.Plugins/
│   │
│   ├── shared/                    # Shared libraries
│   │   ├── Spaarke.Core/
│   │   ├── Spaarke.Dataverse/
│   │   └── Spaarke.UI.Components/
│   │
│   ├── Entities/                  # Dataverse entity definitions
│   │   ├── sprk_Document/                ✅
│   │   └── sprk_Container/
│   │
│   ├── solutions/                 # Dataverse solutions
│   │   └── UniversalDatasetGridSolution/
│   │
│   ├── office-addins/             # Office Add-ins
│   │   ├── outlook-addin/
│   │   └── word-addin/
│   │
│   └── agents/                    # AI agents
│       ├── copilot-studio/
│       └── semantic-kernel/
│
├── dev/                           # Development documentation
│   ├── projects/                  # Project-specific docs
│   │   ├── spe-file-viewer/             # 🆕 This project! ✅
│   │   ├── quickcreate_pcf_component/
│   │   ├── dataset_pcf_component/
│   │   ├── sdap_project/
│   │   └── email_save_SPE/
│   │
│   ├── ai-workspace/              # AI development workspace
│   ├── onboarding/                # Developer onboarding
│   └── .github/                   # GitHub workflows
│
├── docs/                          # User/system documentation
│   └── development/
│
└── packages/                      # npm workspace packages
    └── sdap-client/
```

---

## 🎯 SPE File Viewer Project - Component Locations

This project touches **3 main areas** of the repository:

### 1. Backend API (SDAP BFF API) ✅ Existing

**Location**: `src/api/Spe.Bff.Api/`

**Files to Modify**:
```
src/api/Spe.Bff.Api/
├── Api/
│   └── FileAccessEndpoints.cs              # UPDATE: Add /preview-url endpoint
├── Services/
│   ├── SpeFileStore.cs                    # UPDATE: Add GetPreviewUrlAsync()
│   └── DataverseService.cs                # UPDATE: Add ValidateDocumentAccessAsync()
├── Models/
│   └── SpeFileStoreDtos.cs                # ✅ Already has DTOs
└── Program.cs                              # Verify DI registration
```

**Changes**:
- Add `/api/documents/{id}/preview-url` endpoint
- Add UAC validation logic
- Verify app-only GraphServiceClient configuration

---

### 2. Dataverse Plugin ✅ Existing

**Location**: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/`

**Files to Modify**:
```
src/dataverse/Spaarke.CustomApiProxy/
└── Plugins/
    └── Spaarke.Dataverse.CustomApiProxy/
        ├── BaseProxyPlugin.cs                   # ✅ No changes (already supports app-only)
        ├── GetDocumentFileUrlPlugin.cs         # RENAME to GetFilePreviewUrlPlugin.cs
        └── Spaarke.Dataverse.CustomApiProxy.csproj
```

**Changes**:
- Rename plugin file and class
- Simplify to thin proxy (no endpoint type logic)
- Add correlation ID to output

---

### 3. PCF Control 🆕 NEW

**Location**: `src/controls/SpeFileViewer/` (TO CREATE)

**Recommended Structure**:
```
src/controls/SpeFileViewer/
├── SpeFileViewer/                          # PCF control code
│   ├── components/                         # React components
│   │   ├── FileViewer.tsx                  # Main viewer component
│   │   ├── LoadingSpinner.tsx              # Loading state
│   │   └── ErrorMessage.tsx                # Error display
│   │
│   ├── services/                           # Services
│   │   └── CustomApiService.ts             # Custom API calls
│   │
│   ├── types/                              # TypeScript types
│   │   └── types.ts                        # Interfaces
│   │
│   ├── generated/                          # PCF generated files
│   │   └── ManifestTypes.d.ts
│   │
│   ├── ControlManifest.Input.xml           # PCF manifest
│   └── index.ts                            # PCF entry point
│
├── SpeFileViewerSolution/                  # Dataverse solution (for deployment)
│   ├── Other/
│   ├── src/
│   └── SpeFileViewerSolution.cdsproj
│
├── docs/                                   # Component docs
│   └── README.md
│
├── package.json                            # npm dependencies
├── package-lock.json
├── tsconfig.json                           # TypeScript config
├── pcfconfig.json                          # PCF config
├── .gitignore
└── SpeFileViewer.pcfproj                   # MSBuild project

```

**Pattern**: Follows existing UniversalQuickCreate structure ✅

---

## 📚 Documentation Location

**Location**: `dev/projects/spe-file-viewer/` ✅ Already created!

**Files** (already in place):
```
dev/projects/spe-file-viewer/
├── README.md                                        # Project overview ✅
├── REPOSITORY-STRUCTURE.md                          # This file ✅
├── SPE-FILE-VIEWER-IMPLEMENTATION-GUIDE.md          # Master guide ✅
├── STEP-1-BACKEND-UPDATES.md                        # Step 1 ✅
├── STEP-2-CUSTOM-API-REGISTRATION.md                # Step 2 ✅
├── STEP-3-PCF-CONTROL-DEVELOPMENT.md                # Step 3 ✅
├── STEP-4-DEPLOYMENT-INTEGRATION.md                 # Step 4 ✅
├── STEP-5-TESTING.md                                # Step 5 ✅
├── IMPLEMENTATION-PLAN-FILE-VIEWER.md               # Comprehensive plan ✅
├── GPT-DESIGN-FEEDBACK-FILE-VIEWER.md               # Design guidance ✅
├── TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md        # Technical analysis ✅
├── CUSTOM-API-FILE-ACCESS-SOLUTION.md               # Solution overview ✅
└── DEPLOYMENT-STEPS-CUSTOM-API.md                   # Deployment guide ✅
```

---

## 🗂️ Recommended File Organization by Phase

### Phase 1: Backend Updates

**Files to Modify**:
1. `src/api/Spe.Bff.Api/Services/SpeFileStore.cs` (or create)
2. `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`
3. `src/api/Spe.Bff.Api/Services/DataverseService.cs`
4. `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetDocumentFileUrlPlugin.cs`

### Phase 2: Custom API Registration

**Dataverse Records** (not files):
- External Service Config (sprk_externalserviceconfig)
- Custom API (customapi)
- Custom API Response Properties (customapiresponseproperty)
- Plugin Step (sdkmessageprocessingstep)

### Phase 3: PCF Control Development

**New Directory**: `src/controls/SpeFileViewer/`

**Files to Create**:
- All PCF control files (see structure above)
- Follow UniversalQuickCreate pattern

### Phase 4: Deployment

**Build Artifacts**:
- `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/bin/Release/net462/Spaarke.Dataverse.CustomApiProxy.dll`
- `src/controls/SpeFileViewer/bin/Release/SpeFileViewer_1_0_0_0.zip`

**Azure Deployment**:
- SDAP BFF API deployed to Azure App Service

### Phase 5: Testing

**Test Scripts Location**: `dev/projects/spe-file-viewer/tests/` (optional)

---

## 🔄 Comparison with Existing PCF Controls

### UniversalQuickCreate (Reference Pattern)

```
src/controls/UniversalQuickCreate/
├── UniversalQuickCreate/              # Control code
│   ├── components/                    # React components
│   ├── services/                      # Services
│   ├── types/                         # TypeScript types
│   ├── utils/                         # Utilities
│   ├── config/                        # Config
│   └── index.ts                       # Entry point
├── UniversalQuickCreateSolution/      # Dataverse solution
├── docs/                              # Documentation
├── package.json
└── UniversalQuickCreate.pcfproj
```

### SpeFileViewer (New - Follow Same Pattern) ✅

```
src/controls/SpeFileViewer/
├── SpeFileViewer/                     # Control code (same pattern)
│   ├── components/                    # React components
│   ├── services/                      # Services
│   ├── types/                         # TypeScript types
│   └── index.ts                       # Entry point
├── SpeFileViewerSolution/             # Dataverse solution (same pattern)
├── docs/                              # Documentation
├── package.json
└── SpeFileViewer.pcfproj
```

**Consistency**: ✅ Structure matches existing patterns

---

## 🎯 Decision: PCF Location Strategy

### Option 1: Separate PCF Project (RECOMMENDED) ✅

**Location**: `src/controls/SpeFileViewer/`

**Pros**:
- ✅ Matches existing pattern (UniversalQuickCreate, UniversalDatasetGrid)
- ✅ Standalone solution package
- ✅ Independent versioning
- ✅ Easier to test and deploy independently
- ✅ Clear separation of concerns

**Cons**:
- Additional directory (minor)

### Option 2: Add to UniversalQuickCreate Solution

**Location**: `src/controls/UniversalQuickCreate/SpeFileViewer/`

**Pros**:
- Reuse existing build infrastructure
- Shared dependencies

**Cons**:
- ❌ Mixing concerns (Quick Create ≠ File Viewer)
- ❌ Tight coupling
- ❌ Harder to version independently
- ❌ Doesn't match existing pattern

### ✅ DECISION: Use Option 1 (Separate PCF Project)

**Rationale**:
1. Follows established repository pattern
2. File Viewer is a distinct feature
3. Independent lifecycle and versioning
4. Cleaner architecture

---

## 🚀 Implementation Checklist

### Pre-Implementation
- [x] Documentation directory created (`dev/projects/spe-file-viewer/`)
- [x] All step documents created
- [ ] Review and approve repository structure
- [ ] Ensure no conflicts with existing code

### Phase 1: Backend (Existing Files)
- [ ] `src/api/Spe.Bff.Api/Services/SpeFileStore.cs` - Updated
- [ ] `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs` - Updated
- [ ] `src/api/Spe.Bff.Api/Services/DataverseService.cs` - Updated
- [ ] `src/dataverse/Spaarke.CustomApiProxy/Plugins/.../GetFilePreviewUrlPlugin.cs` - Renamed & updated

### Phase 2: Custom API (Dataverse)
- [ ] External Service Config - Created in Dataverse
- [ ] Custom API - Registered in Dataverse
- [ ] Plugin Assembly - Registered in Dataverse

### Phase 3: PCF Control (New Directory)
- [ ] `src/controls/SpeFileViewer/` - Directory created
- [ ] All PCF files created (following UniversalQuickCreate pattern)
- [ ] Built and tested locally

### Phase 4: Deployment
- [ ] SDAP BFF API deployed to Azure
- [ ] PCF solution imported to Dataverse
- [ ] Document form configured

### Phase 5: Testing
- [ ] All tests passed
- [ ] Documentation updated

---

## 📝 Notes for Developers

### Working with Multiple Components

**Backend API Changes**:
```bash
cd c:/code_files/spaarke/src/api/Spe.Bff.Api
# Make changes
dotnet build
dotnet test
```

**Plugin Changes**:
```bash
cd c:/code_files/spaarke/src/dataverse/Spaarke.CustomApiProxy
# Make changes
dotnet build -c Release
# DLL location: Plugins/Spaarke.Dataverse.CustomApiProxy/bin/Release/net462/
```

**PCF Control Changes**:
```bash
cd c:/code_files/spaarke/src/controls/SpeFileViewer
# Make changes
npm run build
npm start watch  # For local testing
```

### Git Workflow

**Branching Strategy**:
```bash
# Create feature branch
git checkout -b feature/spe-file-viewer

# Work on each phase
git add <files>
git commit -m "Phase 1: Backend updates"

# Continue through all phases
```

**Recommended Commits**:
1. "Phase 1: Update BFF API and plugin"
2. "Phase 2: Register Custom API in Dataverse"
3. "Phase 3: Implement SpeFileViewer PCF control"
4. "Phase 4: Deploy and integrate components"
5. "Phase 5: Add tests and documentation"

---

## ✅ Structure Review Checklist

Before starting implementation:

- [x] Documentation location confirmed (`dev/projects/spe-file-viewer/`)
- [ ] PCF control location decided (`src/controls/SpeFileViewer/`) ← **APPROVE THIS**
- [ ] Backend API location confirmed (`src/api/Spe.Bff.Api/`) ✅
- [ ] Plugin location confirmed (`src/dataverse/Spaarke.CustomApiProxy/`) ✅
- [ ] Build artifact paths understood
- [ ] Git branching strategy agreed
- [ ] No conflicts with existing code identified

---

## 🎉 Ready to Start?

Once you approve this structure, we'll proceed with:

1. **Create PCF directory**: `src/controls/SpeFileViewer/`
2. **Begin Phase 1**: Backend updates (existing files)
3. **Follow step documents**: Implementation in order

**Next Step**: Please review and approve this repository structure, then we'll create the PCF directory and begin implementation!

---

**Document Version**: 1.0
**Last Updated**: 2025-01-21
**Status**: Awaiting Approval
