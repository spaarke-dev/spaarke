# Universal Quick Create Architecture Guide
## Multi-Document Upload Solution for Dataverse

**Version:** 3.0.9
**Last Revised:** December 7, 2025
**Status:** Production Ready
**Environment:** SPAARKE DEV 1 (Dataverse) + Azure WestUS2

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [System Architecture](#system-architecture)
3. [Component Architecture](#component-architecture)
4. [Custom Page Configuration](#custom-page-configuration)
5. [Parameter Hydration](#parameter-hydration)
6. [Dark Mode & Theming](#dark-mode--theming)
7. [Deployment Procedures](#deployment-procedures)
8. [Embedded PCF Version Management](#embedded-pcf-version-management)
9. [Critical Issues & Resolutions](#critical-issues--resolutions)
10. [Troubleshooting Guide](#troubleshooting-guide)
11. [Reference Documentation](#reference-documentation)

---

## Executive Summary

**Universal Quick Create** is a multi-document upload solution that enables users to upload multiple files to SharePoint Embedded from any Dataverse entity form. The solution uses a **Custom Page dialog** approach with a PCF control embedded inside.

### Key Capabilities

- **Multi-File Upload:** Upload up to 10 files simultaneously (10MB each, 100MB total)
- **Universal Entity Support:** Works with any entity configured with a SharePoint container (Matter, Project, Invoice, Account, Contact)
- **Dark Mode Support:** Automatically detects and applies MDA dark mode theme (v3.0.9+)
- **Custom Page Dialog:** Modern side-panel dialog experience using `Xrm.Navigation.navigateTo`
- **Dynamic Height:** Responsive layout that fills the dialog space

### Solution Components

| Component | Type | Purpose |
|-----------|------|---------|
| `sprk_Spaarke.Controls.UniversalDocumentUpload` | PCF Control | File selection, upload UI, Dataverse record creation |
| `sprk_documentuploaddialog_e52db` | Custom Page | Canvas App wrapper hosting the PCF control |
| `sprk_subgrid_commands.js` | Web Resource | JavaScript to open dialog from ribbon button |
| `UniversalQuickCreate` | Dataverse Solution | Container for all components |

---

## System Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         USER BROWSER                                    │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Dataverse Model-Driven App                                       │  │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │  Parent Entity Form (Matter, Project, etc.)                 │  │  │
│  │  │  ┌──────────────────────────────────────────────────────┐  │  │  │
│  │  │  │  Documents Subgrid                                   │  │  │  │
│  │  │  │  [+ Add Documents] ← Ribbon Button                   │  │  │  │
│  │  │  └──────────────────────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────────────────────┘  │  │
│  │                           │                                        │  │
│  │                           │ Xrm.Navigation.navigateTo()            │  │
│  │                           ↓                                        │  │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │  Custom Page Dialog (Side Panel)                            │  │  │
│  │  │  ┌───────────────────────────────────────────────────────┐  │  │  │
│  │  │  │  MDA Dialog Chrome (defaultDialogChromeHeader)        │  │  │  │
│  │  │  │  [File Upload]                              [X]       │  │  │  │
│  │  │  ├───────────────────────────────────────────────────────┤  │  │  │
│  │  │  │  Canvas App Container                                 │  │  │  │
│  │  │  │  ┌─────────────────────────────────────────────────┐  │  │  │  │
│  │  │  │  │  UniversalDocumentUpload PCF Control (v3.0.9)  │  │  │  │  │
│  │  │  │  │  - File Selection UI (Fluent UI v9)            │  │  │  │  │
│  │  │  │  │  - Dark Mode Support                           │  │  │  │  │
│  │  │  │  │  - MSAL Authentication                         │  │  │  │  │
│  │  │  │  │  - Upload Progress                             │  │  │  │  │
│  │  │  │  └─────────────────────────────────────────────────┘  │  │  │  │
│  │  │  └───────────────────────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ HTTPS (OAuth 2.0 Bearer Token)
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│                    SPE BFF API (Azure Web App)                          │
│  POST /upload/file        → Upload file to SharePoint Embedded          │
│  GET  /api/navmap/...     → Get navigation property metadata            │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    ↓                               ↓
┌──────────────────────────┐       ┌─────────────────────────────────┐
│  Microsoft Graph API     │       │  Dataverse Web API               │
│  - SharePoint Embedded   │       │  - Create sprk_document records  │
│  - File Upload           │       │  - Entity metadata queries       │
└──────────────────────────┘       └─────────────────────────────────┘
```

### Data Flow: Upload Process

```
1. User clicks "Add Documents" button on subgrid
           │
           ↓
2. sprk_subgrid_commands.js extracts context:
   - parentEntityName (e.g., "sprk_matter")
   - parentRecordId (GUID)
   - containerId (SharePoint container)
   - parentDisplayName (for UI header)
           │
           ↓
3. Xrm.Navigation.navigateTo() opens Custom Page dialog
   - Passes parameters as JSON in recordId field
           │
           ↓
4. Custom Page OnStart parses parameters
   - Sets varParentEntityName, varParentRecordId, etc.
           │
           ↓
5. PCF Control receives parameters via updateView()
   - Waits for parameters to hydrate (may be empty initially)
   - Initializes when all required parameters present
           │
           ↓
6. User selects files and clicks "Upload"
           │
           ↓
7. PCF authenticates via MSAL.js (silent or popup)
           │
           ↓
8. Files uploaded to SharePoint Embedded via BFF API
   - POST /upload/file for each file
           │
           ↓
9. Dataverse records created via context.webAPI
   - Uses NavMapClient for correct navigation property names
           │
           ↓
10. PCF sets shouldClose = true
    - Custom Page Timer detects change
    - Calls Back() to close dialog
           │
           ↓
11. Subgrid refreshes to show new documents
```

---

## Component Architecture

### 1. PCF Control (UniversalDocumentUpload)

**Technology:** TypeScript, React, Fluent UI v9, MSAL.js
**Version:** 3.0.9
**Location:** `src/client/pcf/UniversalQuickCreate/`

#### Core Components

```typescript
UniversalDocumentUpload (index.ts)
├─ Components
│  ├─ DocumentUploadForm.tsx      // Main UI component
│  ├─ FileSelectionField.tsx      // File picker with drag-drop
│  ├─ UploadProgressBar.tsx       // Upload progress indicator
│  └─ ErrorMessageList.tsx        // Error display
│
├─ Services
│  ├─ MsalAuthProvider.ts         // OAuth 2.0 authentication
│  ├─ SdapApiClient.ts            // BFF API HTTP client
│  ├─ NavMapClient.ts             // Metadata discovery (Phase 7)
│  ├─ FileUploadService.ts        // Single file upload
│  ├─ MultiFileUploadService.ts   // Batch upload orchestration
│  └─ DocumentRecordService.ts    // Dataverse record creation
│
├─ Config
│  └─ EntityDocumentConfig.ts     // Entity-relationship mappings
│
└─ Utils
   └─ logger.ts                   // Console logging utilities
```

#### Control Manifest Properties

```xml
<control namespace="Spaarke.Controls" constructor="UniversalDocumentUpload"
         version="3.0.9" display-name-key="Universal Document Upload">

  <!-- INPUT: Parent Entity Name -->
  <property name="parentEntityName"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <!-- INPUT: Parent Record ID (GUID without braces) -->
  <property name="parentRecordId"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <!-- INPUT: SharePoint Container ID -->
  <property name="containerId"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <!-- INPUT: Display Name for UI -->
  <property name="parentDisplayName"
            of-type="SingleLine.Text"
            usage="input"
            required="false" />

  <!-- INPUT: BFF API Base URL -->
  <property name="sdapApiBaseUrl"
            of-type="SingleLine.Text"
            usage="input"
            required="false"
            default-value="spe-api-dev-67e2xz.azurewebsites.net/api" />

  <!-- OUTPUT: Signal to close dialog -->
  <property name="shouldClose"
            of-type="TwoOptions"
            usage="output"
            required="false" />
</control>
```

#### File Upload Limits

| Limit | Value |
|-------|-------|
| Max Files Per Upload | 10 |
| Max File Size | 10 MB |
| Max Total Size | 100 MB |

---

### 2. Custom Page (sprk_documentuploaddialog_e52db)

**Technology:** Power Apps Canvas App
**Layout:** Blank screen (previously `phoneLayout_FluidGridWithHeaderPageLayout_ver3.0`)
**Location:** `infrastructure/dataverse/ribbon/temp/UQC_unpacked/CanvasApps/`

#### Custom Page Structure

```yaml
App (App.fx.yaml)
├─ OnStart: Dark mode detection, variable initialization
└─ Theme: PowerAppsTheme

Screen: Document Upload (Document Upload.fx.yaml)
├─ Layout: Blank screen (As screen)
├─ Fill: =varBackgroundColor (dark mode aware)
├─ OnVisible: Parse parameters from Param("recordId")
│
├─ Container1 (groupContainer.manualLayoutContainer)
│  ├─ Height: =Parent.Height (responsive)
│  ├─ Width: =Parent.Width (responsive)
│  │
│  └─ UniversalDocumentUpload1 (PCF Control)
│     ├─ Height: =Parent.Height
│     ├─ Width: =Parent.Width
│     ├─ containerId: =varContainerId
│     ├─ parentEntityName: =varParentEntityName
│     ├─ parentRecordId: =varParentRecordId
│     ├─ parentDisplayName: =varParentDisplayName
│     └─ sdapApiBaseUrl: ="https://spe-api-dev-67e2xz.azurewebsites.net/api"
│
└─ Timer1 (timer) - Dialog Close Handler
   ├─ Duration: =100 (ms)
   ├─ Start: =UniversalDocumentUpload1.shouldClose
   ├─ OnTimerEnd: =Back()
   └─ Visible: =false
```

---

### 3. Web Resource (sprk_subgrid_commands.js)

**Purpose:** JavaScript command invoked by ribbon button to open the Custom Page dialog.

#### Entity Configuration

```javascript
const ENTITY_CONFIGURATIONS = {
    "sprk_matter": {
        entityLogicalName: "sprk_matter",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_matternumber", "sprk_name"],
        entityDisplayName: "Matter"
    },
    "sprk_project": {
        entityLogicalName: "sprk_project",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_projectname", "sprk_name"],
        entityDisplayName: "Project"
    },
    "sprk_invoice": {
        entityLogicalName: "sprk_invoice",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_invoicenumber", "name"],
        entityDisplayName: "Invoice"
    },
    "account": {
        entityLogicalName: "account",
        containerIdField: "sprk_containerid",
        displayNameFields: ["name"],
        entityDisplayName: "Account"
    },
    "contact": {
        entityLogicalName: "contact",
        containerIdField: "sprk_containerid",
        displayNameFields: ["fullname", "lastname", "firstname"],
        entityDisplayName: "Contact"
    }
};
```

#### Dialog Navigation

```javascript
function openDocumentUploadDialog(params, selectedControl) {
    // Encode parameters as JSON string
    const dataPayload = JSON.stringify({
        parentEntityName: params.parentEntityName,
        parentRecordId: params.parentRecordId,
        containerId: params.containerId,
        parentDisplayName: params.parentDisplayName
    });

    const pageInput = {
        pageType: "custom",
        name: "sprk_documentuploaddialog_e52db",
        recordId: dataPayload  // JSON passed via recordId (dialog workaround)
    };

    const navigationOptions = {
        target: 2,      // Dialog
        position: 2,    // Right side pane
        width: { value: 640, unit: 'px' }
    };

    Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
        function success(result) {
            // Refresh subgrid after dialog closes
            if (selectedControl && typeof selectedControl.refresh === "function") {
                selectedControl.refresh();
            }
        },
        function error(err) {
            if (err && err.errorCode !== 2) {  // Not user cancellation
                showErrorDialog("Error opening dialog: " + err.message);
            }
        }
    );
}
```

---

## Custom Page Configuration

### Required Components

When setting up the Custom Page, ensure these elements are configured correctly:

#### 1. App.fx.yaml - OnStart Formula

```yaml
App As appinfo:
    BackEnabled: =true
    OnStart: |-
        =Set(varIsDarkMode, Or(
            "themeOption%3Ddarkmode" in Param("flags"),
            "themeOption=darkmode" in Param("flags"),
            Param("themeOption") = "darkmode"
        ));
        Set(varBackgroundColor, If(varIsDarkMode, RGBA(32, 31, 31, 1), RGBA(255, 255, 255, 1)));
        Set(varForegroundColor, If(varIsDarkMode, RGBA(255, 255, 255, 1), RGBA(36, 36, 36, 1)));
        Set(varAccentColor, If(varIsDarkMode, RGBA(96, 205, 255, 1), RGBA(0, 120, 212, 1)));
        Set(varInit, false)
    Theme: =PowerAppsTheme
```

#### 2. Screen Properties

```yaml
# Blank screen format (no built-in header/footer sections)
"'Document Upload' As screen":
    Fill: =varBackgroundColor
    LoadingSpinnerColor: =varAccentColor
    OnVisible: |-
        =If(Not(varInit),
            Set(varInit, true);
            Set(_raw, Param("recordId"));
            If(Not(IsBlank(_raw)),
                Set(_parsed, ParseJSON(_raw));
                Set(varParentEntityName, Text(_parsed.parentEntityName));
                Set(varParentRecordId, Text(_parsed.parentRecordId));
                Set(varContainerId, Text(_parsed.containerId));
                Set(varParentDisplayName, Text(_parsed.parentDisplayName))
            )
        )
```

> **Note:** The blank screen format (`As screen`) is preferred over layout templates like `phoneLayout_FluidGridWithHeaderPageLayout_ver3.0` because layout templates include built-in header/footer sections that don't respect dark mode and create white space in the dialog.

#### 3. Container Configuration

```yaml
Container1 As groupContainer.manualLayoutContainer:
    DropShadow: =DropShadow.None
    Height: =Parent.Height          # IMPORTANT: Dynamic height
    Width: =Parent.Width            # IMPORTANT: Dynamic width
    RadiusBottomLeft: =0
    RadiusBottomRight: =0
    RadiusTopLeft: =0
    RadiusTopRight: =0
    ZIndex: =1
```

#### 4. PCF Control Properties

```yaml
"UniversalDocumentUpload1 As 'Universal Document Upload'":
    containerId: =varContainerId
    DisplayMode: =DisplayMode.Edit
    Height: =Parent.Height          # IMPORTANT: Fill container
    Width: =Parent.Width            # IMPORTANT: Fill container
    OnChange: =
    parentDisplayName: =varParentDisplayName
    parentEntityName: =varParentEntityName
    parentRecordId: =varParentRecordId
    sdapApiBaseUrl: ="https://spe-api-dev-67e2xz.azurewebsites.net/api"
    Tooltip: ="Multi-file upload to Spaarke DMS"
    X: =0
    Y: =0
    ZIndex: =1
```

#### 5. Timer Configuration (Dialog Close Handler)

```yaml
Timer1 As timer:
    AutoPause: =false
    Duration: =100                  # 100ms check interval
    Start: =UniversalDocumentUpload1.shouldClose  # Watch PCF output
    OnTimerEnd: =Back()             # Close dialog when shouldClose=true
    Visible: =false                 # Hidden - runs in background
    ZIndex: =2
```

### Common Configuration Mistakes

| Mistake | Symptom | Fix |
|---------|---------|-----|
| Fixed Height (e.g., `=1366`) | Large white space below content | Use `=Parent.Height` |
| Fixed Width (e.g., `=768`) | Content doesn't fill dialog | Use `=Parent.Width` |
| Missing Timer | Dialog doesn't close after upload | Add Timer1 with Start bound to shouldClose |
| Wrong OnVisible formula | Parameters not parsed | Use ParseJSON on Param("recordId") |
| Missing varInit check | Parameters overwritten on re-render | Wrap OnVisible in `If(Not(varInit), ...)` |

---

## Parameter Hydration

### The Problem

When a Custom Page opens, the PCF control's `updateView()` method is called multiple times:

1. **First call:** Parameters may be empty or `null` (Canvas App still initializing)
2. **Second call:** Parameters may still be empty (binding not yet resolved)
3. **Third+ call:** Parameters finally have values

If the PCF attempts to initialize on the first call with empty parameters, it will fail with "Missing required parameters" errors.

### The Solution

The PCF uses **idempotent initialization** - it waits for all required parameters before initializing:

```typescript
// index.ts - updateView() method
public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Extract parameter values (may be empty on first calls)
    const parentEntityName = context.parameters.parentEntityName?.raw ?? "";
    const parentRecordId = context.parameters.parentRecordId?.raw ?? "";
    const containerId = context.parameters.containerId?.raw ?? "";

    // Only initialize once when all required params are present
    if (!this._initialized) {
        if (parentEntityName && parentRecordId && containerId) {
            logInfo('UniversalDocumentUpload', 'Parameters hydrated - initializing');
            this._initialized = true;
            this.initializeAsync(context);
        } else {
            // Params not ready yet - wait for next updateView call
            logInfo('UniversalDocumentUpload', 'Waiting for parameters to hydrate', {
                hasEntityName: !!parentEntityName,
                hasRecordId: !!parentRecordId,
                hasContainerId: !!containerId
            });
            return;
        }
    }
}
```

### Key Points

- **Never throw errors on empty parameters** - just wait for next updateView
- **Use `_initialized` flag** to prevent re-initialization
- **Log parameter state** for debugging hydration issues

---

## Dark Mode & Theming

### PCF Dark Mode Support (v3.0.9+)

The PCF control automatically detects dark mode from multiple sources:

```typescript
function getEffectiveDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
    const preference = getUserThemePreference();  // localStorage

    // 1. User explicit choice overrides everything
    if (preference === 'dark') return true;
    if (preference === 'light') return false;

    // 2. Check URL flag (Power Apps dark mode flag)
    if (window.location.href.includes('themeOption%3Ddarkmode') ||
        window.location.href.includes('themeOption=darkmode')) {
        return true;
    }

    // 3. Check Power Apps context
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    // 4. Check DOM navbar color (Power Apps fallback)
    const navbar = document.querySelector('[data-id="navbar-container"]');
    if (navbar) {
        const bgColor = window.getComputedStyle(navbar).backgroundColor;
        if (bgColor === 'rgb(10, 10, 10)') return true;
    }

    // 5. Fall back to system preference
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
}
```

### Custom Page Dark Mode Support

The Custom Page detects dark mode in `OnStart` and sets theme variables:

```yaml
OnStart: |-
    =Set(varIsDarkMode, Or(
        "themeOption%3Ddarkmode" in Param("flags"),
        "themeOption=darkmode" in Param("flags"),
        Param("themeOption") = "darkmode"
    ));
    Set(varBackgroundColor, If(varIsDarkMode, RGBA(32, 31, 31, 1), RGBA(255, 255, 255, 1)));
    Set(varForegroundColor, If(varIsDarkMode, RGBA(255, 255, 255, 1), RGBA(36, 36, 36, 1)));
    Set(varAccentColor, If(varIsDarkMode, RGBA(96, 205, 255, 1), RGBA(0, 120, 212, 1)));
```

### Known Limitation: Dialog Chrome Header

**Issue:** When a Custom Page opens as a dialog via `Xrm.Navigation.navigateTo`, the MDA platform wraps it in a dialog chrome (header with title and close button). This chrome does **NOT** respect dark mode and remains white (#FFFFFF).

**Technical Reason:**
- The dialog chrome (`defaultDialogChromeHeader`) is rendered by the MDA platform, not by the Custom Page
- Custom Pages are essentially embedded Canvas Apps with a different rendering path than native MDA dialogs
- Microsoft has not yet extended dark mode support to Custom Page dialogs

**What Works:**
- Native MDA dialogs (Quick Create, Lookups) DO respect dark mode
- The Custom Page content (PCF, screen background) correctly applies dark mode

**What Doesn't Work:**
- The dialog header remains white in dark mode
- No supported API exists to style the dialog chrome

**Workaround Options (Unsupported):**
```javascript
// DOM manipulation from sprk_subgrid_commands.js (unsupported)
setTimeout(() => {
    const isDarkMode = window.location.href.includes('themeOption');
    if (isDarkMode) {
        const headers = document.querySelectorAll('[id*="defaultDialogChromeHeader"]');
        headers.forEach(h => {
            h.style.backgroundColor = '#201F1F';
            h.style.color = '#FFFFFF';
        });
    }
}, 200);
```

**Recommendation:** Accept this limitation until Microsoft adds official support. The Custom Page content is properly themed; only the dialog chrome is affected.

See: `projects/mda-darkmode-theme/notes/DIALOG-CHROME-LIMITATION.md` for full documentation.

---

## Deployment Procedures

### Deploying PCF Control Updates

#### Method 1: pac pcf push (Development)

```powershell
# 1. Disable Central Package Management
$repoRoot = "c:\code_files\spaarke"
if (Test-Path "$repoRoot\Directory.Packages.props") {
    Move-Item "$repoRoot\Directory.Packages.props" "$repoRoot\Directory.Packages.props.disabled"
}

# 2. Build and push
cd "c:\code_files\spaarke\src\client\pcf\UniversalQuickCreate"
npm run build
pac pcf push --publisher-prefix sprk

# 3. Restore Central Package Management
if (Test-Path "$repoRoot\Directory.Packages.props.disabled") {
    Move-Item "$repoRoot\Directory.Packages.props.disabled" "$repoRoot\Directory.Packages.props"
}
```

**Important:** `pac pcf push` deploys to `PowerAppsToolsTemp_sprk` solution, NOT to `UniversalQuickCreate` solution.

#### Method 2: Solution Pack & Import (Production)

```powershell
# 1. Build PCF
cd "c:\code_files\spaarke\src\client\pcf\UniversalQuickCreate"
npm run build

# 2. Copy bundle.js to unpacked solution
$bundleSrc = "out/controls/control/bundle.js"
$bundleDest = "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked\Controls\sprk_Spaarke.Controls.UniversalDocumentUpload\"
Copy-Item $bundleSrc $bundleDest

# 3. Update ControlManifest.xml version if needed

# 4. Pack solution
pac solution pack `
    --zipfile "UQC_updated.zip" `
    --folder "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked" `
    --processCanvasApps

# 5. Import solution
pac solution import --path "UQC_updated.zip" --publish-changes
```

### Deploying Custom Page Updates

```powershell
# 1. Edit YAML files in UQC_unpacked/CanvasApps/src/sprk_documentuploaddialog_e52db/

# 2. Pack solution (--processCanvasApps packs Canvas App sources)
pac solution pack `
    --zipfile "UQC_updated.zip" `
    --folder "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked" `
    --processCanvasApps

# 3. Import solution
pac solution import --path "UQC_updated.zip" --publish-changes
```

---

## Embedded PCF Version Management

### Understanding the Embedded PCF Architecture

**Critical:** The Custom Page contains an **embedded copy** of the PCF control bundle, separate from the PCF registered in Dataverse. This creates a version management challenge.

#### File Locations

| Component | Location | Purpose |
|-----------|----------|---------|
| PCF Source | `src/client/pcf/UniversalQuickCreate/control/` | Source code and manifest |
| PCF Build Output | `src/client/pcf/UniversalQuickCreate/out/controls/control/` | Compiled bundle.js and CSS |
| **Solution Controls Folder** | `UQC_unpacked/Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/` | **PCF component in solution** |
| **Embedded in Canvas App** | `UQC_unpacked/CanvasApps/src/sprk_documentuploaddialog_e52db/Other/Resources/Controls/` | **Copy used by Custom Page** |

> ⚠️ **CRITICAL:** Both the Controls folder AND Canvas App embedded location must be updated when deploying PCF changes. Updating only one location causes version conflicts.

#### The Version Mismatch Problem

When you use `pac pcf push`, it deploys the PCF to a temp solution (`PowerAppsToolsTemp_sprk`), but the Custom Page continues using its **embedded copy**. This causes:

1. **"New component available" prompts** - Custom Page detects version mismatch
2. **Version regression** - Re-importing the solution reverts to embedded version
3. **Inconsistent behavior** - Different PCF versions may be active simultaneously

### Complete PCF Update Workflow

**Always follow this workflow when updating the PCF control:**

```powershell
# ═══════════════════════════════════════════════════════════════════════════════
# STEP 1: Update version in ControlManifest.Input.xml
# ═══════════════════════════════════════════════════════════════════════════════
# Edit: src/client/pcf/UniversalQuickCreate/control/ControlManifest.Input.xml
# Update the version attribute (e.g., version="3.0.10")

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 2: Build the PCF control
# ═══════════════════════════════════════════════════════════════════════════════
cd "c:\code_files\spaarke\src\client\pcf\UniversalQuickCreate"
npm run build

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 3: Copy bundle.js to BOTH locations
# ═══════════════════════════════════════════════════════════════════════════════
$srcBundle = "c:\code_files\spaarke\src\client\pcf\UniversalQuickCreate\out\controls\control\bundle.js"

# Location 1: Controls folder in solution
$destControls = "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked\Controls\sprk_Spaarke.Controls.UniversalDocumentUpload\bundle.js"
Copy-Item $srcBundle $destControls -Force
Write-Host "✓ Copied to Controls folder"

# Location 2: Canvas App embedded location
$destCanvasApp = "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked\CanvasApps\src\sprk_documentuploaddialog_e52db\Other\Resources\Controls\Spaarke.Controls.UniversalDocumentUpload.bundle.js"
Copy-Item $srcBundle $destCanvasApp -Force
Write-Host "✓ Copied to Canvas App embedded location"

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 4: Copy CSS to embedded location (if CSS was modified)
# ═══════════════════════════════════════════════════════════════════════════════
$srcCss = "c:\code_files\spaarke\src\client\pcf\UniversalQuickCreate\out\controls\control\css\UniversalQuickCreate.css"
$destCss = "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked\CanvasApps\src\sprk_documentuploaddialog_e52db\Other\Resources\Controls\css\Spaarke.Controls.UniversalDocumentUpload.UniversalQuickCreate.css"
Copy-Item $srcCss $destCss -Force

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 5: Verify the embedded version matches source
# ═══════════════════════════════════════════════════════════════════════════════
# Check version in embedded bundle:
Select-String -Path $destBundle -Pattern "v\d+\.\d+\.\d+" | Select-Object -First 3

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 6: Pack the solution
# ═══════════════════════════════════════════════════════════════════════════════
pac solution pack `
    --zipfile "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_updated.zip" `
    --folder "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_unpacked" `
    --packagetype Unmanaged

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 7: Import the solution to Dataverse
# ═══════════════════════════════════════════════════════════════════════════════
pac solution import `
    --path "c:\code_files\spaarke\infrastructure\dataverse\ribbon\temp\UQC_updated.zip" `
    --activate-plugins

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 8: Clean up temp solutions (IMPORTANT - prevents version conflicts)
# ═══════════════════════════════════════════════════════════════════════════════
# These temp solutions are created by `pac pcf push` and may contain old PCF versions
# Delete them to prevent "new component available" prompts

# Check for temp solutions
pac solution list | Select-String -Pattern "Temp|temp"

# Delete temp solutions if they exist
pac solution delete --solution-name PCFUpdateTemp 2>$null
pac solution delete --solution-name PowerAppsToolsTemp_sprk 2>$null

# Publish all customizations
pac solution publish --async false

# ═══════════════════════════════════════════════════════════════════════════════
# STEP 9: Verify deployment
# ═══════════════════════════════════════════════════════════════════════════════
# 1. Clear browser cache (hard refresh)
# 2. Open a record and launch the Document Upload dialog
# 3. Verify PCF shows correct version in footer
# 4. Confirm no "new component available" prompt appears
```

### Quick Reference: PCF Deployment Locations

| File | Location | Full Path |
|------|----------|-----------|
| **bundle.js** | Controls Folder | `UQC_unpacked/Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js` |
| **bundle.js** | Canvas App Embedded | `UQC_unpacked/CanvasApps/src/sprk_documentuploaddialog_e52db/Other/Resources/Controls/Spaarke.Controls.UniversalDocumentUpload.bundle.js` |
| **CSS** | Canvas App Embedded | `UQC_unpacked/CanvasApps/src/sprk_documentuploaddialog_e52db/Other/Resources/Controls/css/Spaarke.Controls.UniversalDocumentUpload.UniversalQuickCreate.css` |

> **Note:** The Controls folder bundle.js must match the Canvas App embedded bundle.js - both must be updated from the same build output.

### Preventing Version Issues

| Do | Don't |
|----|-------|
| Always copy build output to embedded location | Use `pac pcf push` alone for production |
| Pack and import the full solution | Rely on Power Apps Studio to pick up changes |
| Verify version in embedded bundle before packing | Assume the embedded version is current |
| Clear browser cache after deployment | Skip verification steps |

---

## Critical Issues & Resolutions

### Issue 1: PCF Version Not Updating

**Symptom:** PCF shows old version after deployment

**Root Cause:** `pac pcf push` deploys to `PowerAppsToolsTemp_sprk` temp solution, but Custom Page may reference PCF from `UniversalQuickCreate` solution.

**Resolution:**
1. Import PCF via `pac pcf push` (updates temp solution)
2. Open Custom Page in Power Apps Studio and save/publish (picks up new version)
3. Or pack entire solution with `--processCanvasApps` and import

### Issue 2: Parameters Not Populating

**Symptom:** PCF shows "waiting for parameters" indefinitely

**Root Cause:**
- Custom Page OnVisible formula not parsing parameters correctly
- Missing `varInit` check causing re-initialization
- Parameter JSON encoding issue

**Resolution:**
1. Verify OnVisible uses `ParseJSON(Param("recordId"))`
2. Ensure `Set(varInit, true)` is first line in OnVisible
3. Check JavaScript passes parameters as JSON string in `recordId`

### Issue 3: Dialog Shows White Space Below Content

**Symptom:** Large white/empty area below PCF content

**Root Cause:** Container and PCF have fixed pixel heights

**Resolution:**
```yaml
# Change from:
Height: =1366

# Change to:
Height: =Parent.Height
```

### Issue 4: Duplicate Close Button

**Symptom:** Two 'X' close buttons visible (one in PCF, one in dialog chrome)

**Root Cause:** PCF v3.0.8 had its own header with close button

**Resolution:** Update to PCF v3.0.9 which removes the internal header (Custom Page dialog provides the close button)

### Issue 5: "New Component Available" Prompt / PCF Version Regression

**Symptom:**
- Custom Page prompts "A new component is available" each time it opens
- PCF reverts to old version after solution import
- Version mismatch between source code and deployed PCF

**Root Cause:** Multiple factors can cause this issue:
1. The Custom Page contains an **embedded copy** of the PCF bundle - must be updated
2. The **Controls folder** in the solution also contains the PCF bundle - must be updated
3. **Temp solutions** from `pac pcf push` (e.g., `PCFUpdateTemp`, `PowerAppsToolsTemp_sprk`) may contain old versions

**Resolution:** Follow the [Complete PCF Update Workflow](#complete-pcf-update-workflow):
1. Build the PCF control
2. Copy `bundle.js` to **BOTH** locations:
   - Controls folder: `UQC_unpacked/Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js`
   - Canvas App: `UQC_unpacked/CanvasApps/.../Controls/Spaarke.Controls.UniversalDocumentUpload.bundle.js`
3. Pack and import the full solution
4. **Delete temp solutions** to prevent version conflicts:
   ```powershell
   pac solution delete --solution-name PCFUpdateTemp
   pac solution delete --solution-name PowerAppsToolsTemp_sprk
   ```
5. Publish all customizations: `pac solution publish --async false`

**Key Files to Update:**
```
UQC_unpacked/
├── Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/
│   └── bundle.js  ← Copy from out/controls/control/bundle.js
└── CanvasApps/src/sprk_documentuploaddialog_e52db/Other/Resources/Controls/
    ├── Spaarke.Controls.UniversalDocumentUpload.bundle.js  ← Copy from out/controls/control/bundle.js
    └── css/
        └── Spaarke.Controls.UniversalDocumentUpload.UniversalQuickCreate.css
```

### Issue 6: Layout Template Footer White Space

**Symptom:** White footer area visible at bottom of dialog, even with `Height: =Parent.Height`

**Root Cause:** Multiple factors can cause white space in Custom Page dialogs:
1. Screen layout template has built-in header/footer sections
2. Canvas App has fixed dimensions with `DocumentLayoutMaintainAspectRatio: true`

**Resolution:** Two changes required:

**1. Change screen to blank format** (Document Upload.fx.yaml):
```yaml
# Change from:
"'Document Upload' As screen.'phoneLayout_FluidGridWithHeaderPageLayout_ver3.0'":

# Change to:
"'Document Upload' As screen":
```

**2. Update CanvasManifest.json** to disable fixed aspect ratio:
```json
"DocumentLayoutHeight": 640,
"DocumentLayoutMaintainAspectRatio": false,
"DocumentLayoutScaleToFit": false,
"DocumentLayoutWidth": 480,
```

| Setting | Recommended | Why |
|---------|-------------|-----|
| `DocumentLayoutMaintainAspectRatio` | `false` | Allows app to stretch to fill dialog |
| `DocumentLayoutScaleToFit` | `false` | Prevents scaling artifacts |
| `DocumentLayoutHeight/Width` | Smaller values (640x480) | Smaller default that expands |

This ensures the Canvas App fills the dialog space regardless of dialog dimensions.

### Issue 7: Temp Solutions Causing Version Conflicts

**Symptom:**
- "New component available" prompt persists even after solution import
- PCF version appears correct in one place but old in another
- Multiple PCF versions active simultaneously

**Root Cause:** The `pac pcf push` command creates temporary solutions (`PCFUpdateTemp`, `PowerAppsToolsTemp_sprk`) that contain old PCF versions. These temp solutions can conflict with the main solution.

**Resolution:**
```powershell
# List solutions to find temp solutions
pac solution list | Select-String -Pattern "Temp|temp"

# Delete temp solutions
pac solution delete --solution-name PCFUpdateTemp
pac solution delete --solution-name PowerAppsToolsTemp_sprk

# Publish all customizations
pac solution publish --async false
```

**Prevention:** Avoid using `pac pcf push` for production deployments. Always use the [Complete PCF Update Workflow](#complete-pcf-update-workflow) which deploys via the main solution.

---

## Troubleshooting Guide

### Debug Logging

Enable browser console to see PCF logs:

```
[UniversalDocumentUpload] Initializing PCF control v3.0.9
[UniversalDocumentUpload] Waiting for parameters to hydrate
[UniversalDocumentUpload] Parameters hydrated - initializing
[UniversalDocumentUpload] Parent context loaded: { entityName: "sprk_matter", ... }
[UniversalDocumentUpload] MSAL authentication initialized ✅
```

### Common Console Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `Missing required parameters` | Parameters not yet hydrated | Wait - PCF will retry on next updateView |
| `Invalid parentRecordId format` | GUID has curly braces | Remove braces in JS: `.replace(/[{}]/g, '')` |
| `Unsupported parent entity` | Entity not in EntityDocumentConfig | Add entity to configuration |
| `MSAL authentication failed` | Token acquisition failed | Check app registration, user permissions |

### Verifying Deployment

1. **Check PCF Version:**
   - Open dialog
   - Look at footer: "v3.0.9 • Built 2025-12-06"

2. **Check Solution Version:**
   ```powershell
   pac solution list | Select-String "UniversalQuickCreate"
   ```

3. **Check Custom Page:**
   - Open in Power Apps Studio
   - Verify control properties match expected values

---

## Reference Documentation

### Related Documents

| Document | Purpose |
|----------|---------|
| `docs/reference/articles/SDAP-ARCHITECTURE-GUIDE-FULL-VERSION.md` | Full SDAP architecture |
| `projects/mda-darkmode-theme/notes/DIALOG-CHROME-LIMITATION.md` | Dark mode dialog chrome issue |
| `.claude/skills/dataverse-deploy/SKILL.md` | Dataverse deployment procedures |
| `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md` | PCF platform library packaging |

### Key Files

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalQuickCreate/control/index.ts` | PCF control entry point |
| `src/client/pcf/UniversalQuickCreate/control/components/DocumentUploadForm.tsx` | Main UI component |
| `src/client/pcf/UniversalQuickCreate/solution/src/WebResources/sprk_subgrid_commands.js` | Ribbon command script |
| `infrastructure/dataverse/ribbon/temp/UQC_unpacked/` | Unpacked solution for editing |

### External Resources

- [Microsoft Learn - navigateTo Reference](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto)
- [Microsoft Learn - Custom Page Known Issues](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-issues)
- [Power Platform Community - Custom Page Dialogs](https://community.powerplatform.com/)

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2025-12-07 | 3.0.9 | Dark mode support, removed duplicate header, responsive heights, blank screen layout, embedded PCF version management documentation |
| 2025-12-06 | 3.0.8 | Parameter hydration fix, Custom Page dialog approach |
| 2025-12-05 | 3.0.4 | Diagnostic logging, form context detection |
| 2025-12-01 | 3.0.0 | Initial Custom Page dialog implementation |

### Document Updates (December 7, 2025)

- Added **Embedded PCF Version Management** section with complete update workflow
- Added **Issue 5** (PCF version regression) and **Issue 6** (layout template footer white space)
- Updated screen layout documentation to reflect blank screen format (`As screen`)
- Added prevention guidelines for version mismatch issues

---

*Document created: December 7, 2025*
*Last updated: December 7, 2025*
