# PCF Deployment Verification - R2

> **Date**: 2025-12-28 (Updated: 2025-12-29)
> **Tasks**: 001 (AnalysisBuilder), 002 (AnalysisWorkspace)

---

## Versioning Scheme

PCF controls have **three version numbers** to track:

| Version Type | Description | Where It Lives |
|--------------|-------------|----------------|
| **Dataverse Version** | Auto-incremented by `pac pcf push` | Shown in Dataverse UI, tracked per deployment |
| **Bundle Version** | Developer-defined in source code | `ControlManifest.Input.xml`, footer constant |
| **Solution Version** | Solution package version | `solution.xml` in solution project |

> **Note**: `pac pcf push` increments Dataverse version independently of source versions.

---

## Deployment Status

### Currently Deployed Controls

| Control | Dataverse Version | Package Date | Status |
|---------|-------------------|--------------|--------|
| AnalysisBuilder | v1.12.0 | 2025-12-12 | ✅ Deployed |
| AnalysisWorkspace | v1.0.29 | 2025-12-17 | ✅ Deployed |

---

## AnalysisBuilderSolution.zip

**Package Location**: `src/client/pcf/AnalysisBuilder/solution/bin/Release/AnalysisBuilderSolution.zip`
**Built**: 2025-12-12

### solution.xml

| Property | Value |
|----------|-------|
| UniqueName | AnalysisBuilder |
| Version | 1.0 |
| Managed | 0 (Unmanaged) |
| Publisher | Spaarke (sprk) |

### ControlManifest.xml

| Property | Value |
|----------|-------|
| Namespace | Spaarke.Controls |
| Constructor | AnalysisBuilder |
| Version | 1.0.0 |
| Description | AI Analysis configuration dialog with playbook selection, action/scope configuration, and execution (v1.0.0) |
| Platform Libraries | React 16.14.0, Fluent 9.46.2 |
| Built By | pac 1.51.1 |

### Package Contents

| File | Size |
|------|------|
| bundle.js | 183 KB |
| bundle.js.LICENSE.txt | 4 KB |
| ControlManifest.xml | 3 KB |
| css/AnalysisBuilder.css | 10 KB |
| solution.xml | 5 KB |
| customizations.xml | 1 KB |

### Input/Output Properties

**Inputs:**
- `documentId` (required) - GUID of sprk_document record
- `documentName` - Display name
- `containerId` - SharePoint Embedded Container ID
- `fileId` - SharePoint Embedded File ID
- `apiBaseUrl` - BFF API base URL

**Outputs:**
- `selectedPlaybookId`, `selectedActionId`, `selectedSkillIds`, `selectedKnowledgeIds`, `selectedToolIds`
- `createdAnalysisId` - Created analysis record GUID
- `shouldClose` - Signal to close dialog

---

## AnalysisWorkspaceSolution.zip

**Package Location**: `src/client/pcf/AnalysisWorkspace/solution/bin/Release/AnalysisWorkspaceSolution.zip`
**Built**: 2025-12-17

### solution.xml

| Property | Value |
|----------|-------|
| UniqueName | AnalysisWorkspaceSolution |
| Version | 1.0.18 |
| Managed | 1 (Managed) |
| Publisher | Spaarke (sprk) |

### ControlManifest.xml

| Property | Value |
|----------|-------|
| Namespace | Spaarke.Controls |
| Constructor | AnalysisWorkspace |
| Version | 1.0.32 |
| Description | Fix: better design-mode detection, download for Open in App (v1.0.32) |
| Platform Libraries | React 16.14.0, Fluent 9.46.2 |
| Built By | pac 1.51.1 |

### Package Contents

| File | Size |
|------|------|
| bundle.js | 645 KB |
| bundle.js.LICENSE.txt | 3 KB |
| ControlManifest.xml | 2 KB |
| css/AnalysisWorkspace.css | 3 KB |
| solution.xml | 5 KB |
| customizations.xml | 1 KB |

### Input/Output Properties

**Inputs:**
- `analysisId` (required, bound) - GUID of sprk_analysis record
- `documentId` - Parent document GUID (auto-fetched if not provided)
- `containerId` - SharePoint Embedded Container ID
- `fileId` - SharePoint Embedded File ID
- `apiBaseUrl` - BFF API base URL

**Outputs:**
- `workingDocumentContent` - Current working document (Markdown)
- `chatHistory` - Serialized chat history (JSON)
- `analysisStatus` - Draft, InProgress, Completed, Failed

---

## Source Code Version Locations

To update versions before deployment, modify these files:

**AnalysisBuilder:**
1. `src/client/pcf/AnalysisBuilder/control/ControlManifest.Input.xml` - `version` attribute
2. `src/client/pcf/AnalysisBuilder/control/components/AnalysisBuilderApp.tsx` - `VERSION` constant
3. `src/client/pcf/AnalysisBuilder/solution/src/Other/Solution.xml` - `Version` element
4. `src/client/pcf/AnalysisBuilder/solution/src/Controls/ControlManifest.xml` - extracted manifest

**AnalysisWorkspace:**
1. `src/client/pcf/AnalysisWorkspace/control/ControlManifest.Input.xml`
2. `src/client/pcf/AnalysisWorkspace/control/components/AnalysisWorkspaceApp.tsx`
3. `src/client/pcf/AnalysisWorkspace/solution/src/Other/Solution.xml`
4. `src/client/pcf/AnalysisWorkspace/solution/src/Controls/ControlManifest.xml`

---

## Deployed Solutions in Dataverse

| Solution | Version | Managed | Purpose |
|----------|---------|---------|---------|
| PowerAppsToolsTemp_sprk | 1.0 | No | Dev PCF deployments (pac pcf push) |
| AnalysisWorkspaceSolution | 1.0.18 | Yes | Production package |
| AnalysisRibbons | 1.1.0.0 | No | Ribbon customizations |
| AnalysisWebResources | 1.1.0.0 | No | JavaScript web resources |

---

## Update Workflow

### Development Updates (pac pcf push)

```bash
# 1. Navigate to control folder
cd src/client/pcf/AnalysisBuilder  # or AnalysisWorkspace

# 2. Update version in source files (optional - for tracking)

# 3. Build the control
npm run build

# 4. Deploy using PAC CLI (increments Dataverse version automatically)
pac pcf push --publisher-prefix sprk

# 5. Verify deployment
pac solution list
```

### Creating Solution Package (for solution import)

```bash
# 1. Update version in all 4 locations

# 2. Build the solution
cd src/client/pcf/AnalysisBuilder/solution
dotnet build --configuration Release

# 3. Package at: bin/Release/AnalysisBuilderSolution.zip
```

---

## Task Outcomes

### Task 001: AnalysisBuilder
**Status**: ✅ VERIFIED (already deployed at v1.12.0)

### Task 002: AnalysisWorkspace
**Status**: ✅ VERIFIED (already deployed at v1.0.29 on 2025-12-17)

---

*Documented: 2025-12-28 | Updated: 2025-12-29*
