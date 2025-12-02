# SPE File Viewer - Comprehensive Technical Specification
**Version:** 1.2.0 (Current State as of Nov 25, 2025)
**Status:** ❌ DEPLOYMENT BLOCKED - Control not discoverable in Power Apps
**Critical Issue:** Multiple deployment attempts (v1.0.5-v1.2.0) all failed to make control appear in "Get more components"

---

## Executive Summary

### What It Is
A PowerApps Component Framework (PCF) custom control that displays SharePoint Embedded file previews on Dataverse Document forms using MSAL authentication and a Backend-for-Frontend (BFF) API.

### What's Wrong
**After 8 deployment attempts (v1.0.5 through v1.2.0), the control still doesn't appear in "Get more components" or field control lists.**

### Root Cause Analysis Attempts
1. ✅ Fixed control-type from "virtual" to "standard" (v1.0.5)
2. ✅ Added feature-usage with WebAPI and Utility (v1.0.8)
3. ✅ Fixed SpeFileViewerSolution.cdsproj duplicate references (v1.0.9)
4. ✅ Added `<WebResources />` to Customizations.xml (v1.1.0)
5. ✅ Disabled external-service-usage to avoid premium flagging (v1.2.0)
6. ❌ **STILL NOT WORKING**

### Recommended Next Step
**START FRESH** - Compare byte-by-byte with a known working PCF control or rebuild from PCF project template.

---

## Complete Repository Structure

```
c:/code_files/spaarke/src/controls/SpeFileViewer/
│
├── 📁 SpeFileViewer/                       # PCF Control Source Code
│   ├── index.ts                            # Main entry point (235 lines)
│   ├── AuthService.ts                      # MSAL authentication (150 lines)
│   ├── BffClient.ts                        # HTTP client for BFF API (200 lines)
│   ├── FilePreview.tsx                     # React UI component (300 lines)
│   ├── types.ts                            # TypeScript type definitions
│   ├── ControlManifest.Input.xml          # **PCF MANIFEST** (v1.2.0 - 46 lines)
│   ├── css/SpeFileViewer.css              # Styling
│   ├── strings/SpeFileViewer.1033.resx    # Localization (English)
│   └── generated/ManifestTypes.d.ts       # Auto-generated types
│
├── 📁 SpeFileViewerSolution/              # Solution Package Configuration
│   ├── SpeFileViewerSolution.cdsproj      # Solution project file
│   ├── src/Other/
│   │   ├── Customizations.xml             # **CRITICAL** - Control registration
│   │   ├── Solution.xml                   # Solution metadata (Publisher: Spaarke, prefix: sprk)
│   │   └── Relationships.xml              # Empty (no custom relationships)
│   └── bin/Release/
│       ├── SpeFileViewerSolution.zip      # Latest build (549 KB)
│       ├── SpeFileViewerSolution_v1.0.8_FINAL.zip   (195 KB - no web resources)
│       ├── SpeFileViewerSolution_v1.0.9_FINAL.zip   (195 KB - no web resources)
│       ├── SpeFileViewerSolution_v1.1.0_FINAL.zip   (549 KB - with web resources)
│       └── SpeFileViewerSolution_v1.2.0_FINAL.zip   (549 KB - with web resources)
│
├── 📁 out/controls/SpeFileViewer/         # Build Output (Production Bundle)
│   ├── bundle.js                          # Minified control code (679 KB)
│   ├── bundle.js.map                      # Source map
│   ├── ControlManifest.xml               # Processed manifest
│   ├── css/SpeFileViewer.css             # Processed styles
│   └── (localization files)
│
├── 📄 SpeFileViewer.pcfproj               # PCF Project Configuration
├── 📄 package.json                        # NPM dependencies (React 19, MSAL 4.26, Fluent UI 8)
├── 📄 tsconfig.json                       # TypeScript configuration
├── 📄 pcfconfig.json                      # PCF tooling config
│
├── 📄 DEPLOYMENT-v1.0.2.md                # Historical deployment (WORKED)
├── 📄 DEPLOYMENT-v1.0.3.md                # Historical deployment (WORKED as field control)
├── 📄 DEPLOYMENT-v1.0.4.md                # **BROKEN** - Changed to virtual control
├── 📄 IMPLEMENTATION-PLAN-V1.0.5.md       # Plan to fix v1.0.4
├── 📄 IMPLEMENTATION-STATUS-V1.0.5.md     # Status of fixes
├── 📄 FIX-404-ERROR.md                    # Root cause analysis (SharePoint Item ID vs GUID)
├── 📄 TECHNICAL-OVERVIEW.md               # Architecture documentation
└── 📄 PACKAGE-SOLUTION.md                 # Packaging instructions
```

---

## Critical Files Analysis

### 1. ControlManifest.Input.xml (v1.2.0 - CURRENT)

**Location:** `SpeFileViewer/ControlManifest.Input.xml`
**Current State:**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke"
           constructor="SpeFileViewer"
           version="1.2.0"
           display-name-key="SpeFileViewer"
           description-key="SpeFileViewer description"
           control-type="standard">

    <external-service-usage enabled="false">
    </external-service-usage>

    <property name="documentId"
              of-type="SingleLine.Text"
              usage="input"
              required="false" />

    <property name="bffApiUrl"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="https://spe-api-dev-67e2xz.azurewebsites.net" />

    <property name="clientAppId"
              of-type="SingleLine.Text"
              usage="input"
              required="true" />

    <property name="bffAppId"
              of-type="SingleLine.Text"
              usage="input"
              required="true" />

    <property name="tenantId"
              of-type="SingleLine.Text"
              usage="input"
              required="true" />

    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/SpeFileViewer.css" order="1" />
      <resx path="strings/SpeFileViewer.1033.resx" version="1.0.0" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

**Analysis:**
- ✅ `control-type="standard"` - Correct for field OR unbound controls
- ✅ All properties `usage="input"` - Correct for unbound form component
- ✅ `<feature-usage>` declared - Correct (WebAPI + Utility)
- ✅ `external-service-usage enabled="false"` - Not premium
- ✅ Localized strings referenced
- ❓ **MYSTERY**: Meets all senior dev requirements but still doesn't appear!

### 2. Customizations.xml (v1.1.0+)

**Location:** `SpeFileViewerSolution/src/Other/Customizations.xml`
**Current State:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Entities />
  <Roles />
  <Workflows />
  <FieldSecurityProfiles />
  <Templates />
  <EntityMaps />
  <EntityRelationships />
  <OrganizationSettings />
  <optionsets />
  <CustomControls />        <!-- Empty - populated during build -->
  <WebResources />          <!-- Added in v1.1.0 -->
  <SolutionPluginAssemblies />
  <EntityDataProviders />
  <Languages>
    <Language>1033</Language>
  </Languages>
</ImportExportXml>
```

**Analysis:**
- ✅ `<WebResources />` element present (v1.1.0+)
- ✅ `<CustomControls />` element present (populated during build)
- ⚠️ `<CustomControls />` is EMPTY in source - relies on build process to populate

### 3. SpeFileViewerSolution.cdsproj (v1.0.9+)

**Location:** `SpeFileViewerSolution/SpeFileViewerSolution.cdsproj`
**Current State (Relevant Sections):**

```xml
<PropertyGroup>
  <ProjectGuid>f11c3b0d-e61b-4b6a-8d15-18e90940ab15</ProjectGuid>
  <TargetFrameworkVersion>v4.6.2</TargetFrameworkVersion>
  <TargetFramework>net462</TargetFramework>
  <SolutionRootPath>src</SolutionRootPath>
</PropertyGroup>

<PropertyGroup>
  <SolutionPackageType>Unmanaged</SolutionPackageType>
  <SolutionPackageEnableLocalization>false</SolutionPackageEnableLocalization>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.PowerApps.MSBuild.Solution" />
  <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="All" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\SpeFileViewer.pcfproj" />
</ItemGroup>
```

**Analysis:**
- ✅ Single ProjectReference (fixed in v1.0.9)
- ✅ No hardcoded package versions (uses central package management)
- ✅ Matches working UniversalQuickCreate pattern

### 4. Solution.xml

**Location:** `SpeFileViewerSolution/src/Other/Solution.xml`
**Current State:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml version="9.1.0.643" SolutionPackageVersion="9.1" languagecode="1033">
  <SolutionManifest>
    <UniqueName>SpeFileViewerSolution</UniqueName>
    <LocalizedNames>
      <LocalizedName description="SpeFileViewerSolution" languagecode="1033" />
    </LocalizedNames>
    <Version>1.0</Version>
    <Managed>0</Managed>  <!-- Unmanaged -->
    <Publisher>
      <UniqueName>Spaarke</UniqueName>
      <CustomizationPrefix>sprk</CustomizationPrefix>
      <CustomizationOptionValuePrefix>76537</CustomizationOptionValuePrefix>
    </Publisher>
  </SolutionManifest>
</ImportExportXml>
```

**Analysis:**
- ✅ Publisher: Spaarke (prefix: sprk)
- ✅ Unmanaged solution
- ✅ Version 1.0 (matches current practice)

---

## Source Code Components

### 1. index.ts (Main Entry Point)

**Purpose:** PCF lifecycle management, MSAL orchestration, React rendering
**Lines of Code:** ~235
**Key Features:**
- Extracts configuration from manifest properties
- Initializes MSAL with PublicClientApplication
- Acquires access token (3-tier strategy: SSO → Silent → Popup)
- Extracts Document ID (from property OR form context)
- Renders React FilePreview component

**Critical Logic - Document ID Extraction:**
```typescript
private extractDocumentId(context: ComponentFramework.Context<IInputs>): string {
    const rawValue = context.parameters.documentId.raw;

    // Option 1: Use configured documentId (if provided)
    if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
        const trimmed = rawValue.trim();

        // GUID validation (v1.0.5+)
        if (!this.isValidGuid(trimmed)) {
            throw new Error('Document ID must be a GUID format (Dataverse primary key). Do not use SharePoint Item IDs.');
        }

        return trimmed;
    }

    // Option 2: Use form record ID (default - v1.0.3+)
    const recordId = (context.mode as any).contextInfo?.entityId;
    if (recordId && typeof recordId === 'string') {
        if (!this.isValidGuid(recordId)) {
            throw new Error('Form context did not provide a valid GUID.');
        }
        return recordId;
    }

    return '';
}

// GUID format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
private isValidGuid(value: string): boolean {
    const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
    return guidRegex.test(value);
}
```

**Status:** ✅ Logic is CORRECT - v1.0.3 fix (usage="input") + v1.0.5 GUID validation

### 2. AuthService.ts (MSAL Authentication)

**Purpose:** Handle Azure AD authentication, token acquisition, caching
**Lines of Code:** ~150
**Key Features:**
- MSAL PublicClientApplication initialization
- Token acquisition strategies (ssoSilent → acquireTokenSilent → acquireTokenPopup)
- **Named scope construction:** `api://<BFF_APP_ID>/SDAP.Access`
- Token caching in sessionStorage

**Critical Configuration:**
```typescript
constructor(tenantId: string, clientAppId: string, bffAppId: string) {
    this.tenantId = tenantId;
    this.clientAppId = clientAppId;

    // CRITICAL: Named scope for user-delegated access
    this.namedScope = `api://${bffAppId}/SDAP.Access`;

    this.msalConfig = {
        auth: {
            clientId: clientAppId,
            authority: `https://login.microsoftonline.com/${tenantId}`,
            redirectUri: window.location.origin
        },
        cache: {
            cacheLocation: 'sessionStorage'
        }
    };
}
```

**Status:** ✅ Architecture is CORRECT - Two-app pattern with named scope

### 3. BffClient.ts (HTTP Client)

**Purpose:** Make authenticated HTTP calls to BFF API
**Lines of Code:** ~200
**Key Features:**
- GET /api/documents/{documentId}/preview-url
- Bearer token authentication (Authorization header)
- Correlation ID tracking (X-Correlation-Id header)
- Error handling with stable error codes (v1.0.5+)

**Error Code Mapping (v1.0.5+):**
```typescript
switch (errorCode) {
    case 'invalid_id':
        throw new Error('Invalid document ID format. Please contact support.');
    case 'document_not_found':
        throw new Error('Document not found. It may have been deleted.');
    case 'mapping_missing_drive':
    case 'mapping_missing_item':
        throw new Error('This file is still initializing. Please try again in a moment.');
    case 'storage_not_found':
        throw new Error('File has been removed from storage. Contact your administrator.');
    case 'throttled_retry':
        throw new Error('Service is temporarily busy. Please try again in a few seconds.');
}
```

**Status:** ✅ Enhanced error handling implemented (ready for BFF v2)

### 4. FilePreview.tsx (React UI)

**Purpose:** Render file preview iframe, handle loading/error states
**Lines of Code:** ~300
**Key Features:**
- Call BffClient.getPreviewUrl()
- Render Office 365 preview iframe
- Display document metadata (name, size, type)
- Loading spinner
- Error display with retry button
- Fluent UI components (Spinner, MessageBar, DefaultButton)

**Status:** ✅ UI component functional

### 5. types.ts (TypeScript Definitions)

**Purpose:** Define TypeScript interfaces for API contracts
**Lines of Code:** ~50
**Key Types:**
- `FilePreviewDto` - BFF API response
- `BffErrorResponse` - Error format (v1.0.5+ with error codes)
- `DocumentInfo` - Document metadata

**Status:** ✅ Type definitions complete

---

## Build Configuration

### package.json

**Dependencies (Production):**
```json
{
  "@azure/msal-browser": "^4.26.2",      // 200 KB - MSAL authentication
  "@fluentui/react": "^8.125.1",         // 100 KB - Microsoft Fluent UI
  "react": "^19.2.0",                    // 200 KB - React framework
  "react-dom": "^19.2.0",                // 200 KB - React DOM
  "uuid": "^13.0.0"                      // 5 KB - Correlation ID generation
}
```

**Dev Dependencies:**
```json
{
  "pcf-scripts": "^1",                   // PCF build tooling
  "typescript": "^5.8.3",                // TypeScript 5.8
  "typescript-eslint": "^8.31.0",        // Linting
  "@types/react": "^19.2.6",             // React type definitions
  "@types/powerapps-component-framework": "^1.3.16"  // PCF types
}
```

**Bundle Size:**
- Development: 3.03 MB (unminified)
- **Production: 679 KB (minified)** ⚠️ Exceeds 244 KB recommendation

### SpeFileViewer.pcfproj

**Key Settings:**
```xml
<PropertyGroup>
  <Name>SpeFileViewer</Name>
  <OutputPath>$(MSBuildThisFileDirectory)out\controls</OutputPath>
  <PcfBuildMode>production</PcfBuildMode>  <!-- Force production builds -->
  <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
</PropertyGroup>
```

**Build Process:**
1. `npm run build` → Compiles TypeScript, bundles with Webpack
2. `dotnet build SpeFileViewerSolution.cdsproj` → Packages solution ZIP
3. Output: `SpeFileViewerSolution/bin/Release/SpeFileViewerSolution.zip`

---

## Version History & Issues

### v1.0.0-v1.0.2 (Historical - WORKED)
- **Control Type:** `control-type="standard"`
- **Document ID:** `usage="bound"` (required field binding)
- **Status:** ✅ Appeared as field control
- **Issue:** Required binding to wrong field (sprk_graphitemid = SharePoint Item ID)
- **Result:** 404 errors (sent SharePoint Item ID instead of Dataverse GUID)

### v1.0.3 (Historical - WORKED)
- **Fix:** Changed `documentId` from `usage="bound"` to `usage="input"`
- **Goal:** Allow blank input → use form record ID (Dataverse GUID)
- **Status:** ✅ Appeared as field control
- **Deployment:** SUCCESSFUL - Control worked correctly

### v1.0.4 (BROKEN - Root of Current Problems)
- **Change:** `control-type="standard"` → `control-type="virtual"`
- **Reason:** Misunderstood user request for "standalone component"
- **Result:** ❌ Control disappeared from ALL control lists
- **Root Cause:** Virtual controls require `<data-set>` element (for grids)
- **Impact:** Started cascade of failed deployment attempts

### v1.0.5 (First Fix Attempt - FAILED)
- **Fix:** Revert to `control-type="standard"`
- **Added:** GUID validation in extractDocumentId()
- **Added:** Enhanced error handling with stable error codes
- **Result:** ❌ Still doesn't appear
- **Package Size:** 195 KB

### v1.0.6-v1.0.7 (Failed Attempts)
- **v1.0.6:** Added `<feature-usage>` with Utility
- **v1.0.7:** Changed namespace to "Spaarke.Controls"
- **Result:** ❌ Still doesn't appear

### v1.0.8 (Failed Attempt)
- **Fix:** Reverted namespace to "Spaarke", added WebAPI feature
- **Result:** ❌ Still doesn't appear
- **Package Size:** 195 KB

### v1.0.9 (Failed Attempt)
- **Fix:** Removed duplicate ProjectReference in .cdsproj
- **Fix:** Removed hardcoded package versions
- **Result:** ❌ Still doesn't appear
- **Package Size:** 195 KB

### v1.1.0 (Failed Attempt)
- **Fix:** Added `<WebResources />` to Customizations.xml
- **Result:** ❌ Still doesn't appear
- **Package Size:** 549 KB (web resources now included!)
- **Note:** Package size increase indicates web resources ARE being packaged

### v1.2.0 (Current - FAILED)
- **Fix:** Disabled `external-service-usage` (avoid premium flagging)
- **Result:** ❌ Still doesn't appear
- **Package Size:** 549 KB
- **Note:** Matches all senior dev requirements but still fails!

---

## Comparison: Working vs Non-Working

### UniversalQuickCreate (WORKS - Appears in "Add components")

**Manifest:**
```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalDocumentUpload"
         version="3.0.6"
         control-type="standard">

  <external-service-usage enabled="false" />

  <!-- All properties usage="input" -->
  <property name="parentEntityName" usage="input" required="true" />
  <property name="parentRecordId" usage="input" required="true" />
  <property name="containerId" usage="input" required="true" />

  <!-- One OUTPUT property -->
  <property name="shouldClose" usage="output" of-type="TwoOptions" required="false" />

  <feature-usage>
    <uses-feature name="WebAPI" required="true" />
    <uses-feature name="Utility" required="true" />
  </feature-usage>
</control>
```

**Key Difference:** Has ONE output property (`shouldClose`)

### UniversalDatasetGrid (WORKS - Appears in "Add components")

**Manifest:**
```xml
<control namespace="Spaarke.UI.Components"
         constructor="UniversalDatasetGrid"
         version="2.1.4"
         control-type="standard">

  <external-service-usage enabled="false" />

  <!-- Has DATASET element -->
  <data-set name="dataset" display-name-key="Dataset" />

  <property name="configJson" usage="input" required="false" />

  <feature-usage>
    <uses-feature name="WebAPI" required="true" />
  </feature-usage>
</control>
```

**Key Difference:** Has `<data-set>` element (grid control)

### SpeFileViewer v1.2.0 (DOESN'T WORK)

**Manifest:**
```xml
<control namespace="Spaarke"
         constructor="SpeFileViewer"
         version="1.2.0"
         control-type="standard">

  <external-service-usage enabled="false" />

  <!-- All properties usage="input", NO output, NO dataset -->
  <property name="documentId" usage="input" required="false" />
  <property name="bffApiUrl" usage="input" required="false" />
  <property name="clientAppId" usage="input" required="true" />
  <property name="bffAppId" usage="input" required="true" />
  <property name="tenantId" usage="input" required="true" />

  <feature-usage>
    <uses-feature name="WebAPI" required="true" />
    <uses-feature name="Utility" required="true" />
  </feature-usage>
</control>
```

**Hypothesis:** Maybe needs at least ONE `usage="output"` or `usage="bound"` property to be discoverable?

---

## Recommended Diagnostic Steps

### 1. **COMPARE BUILT MANIFESTS (Priority: CRITICAL)**

Extract and compare the ACTUAL ControlManifest.xml from solution packages:

```bash
# Extract v1.0.3 (WORKED) if available
unzip SpeFileViewerSolution_v1.0.3.zip -d /tmp/v1.0.3/

# Extract v1.2.0 (DOESN'T WORK)
unzip SpeFileViewerSolution_v1.2.0_FINAL.zip -d /tmp/v1.2.0/

# Compare manifests
diff /tmp/v1.0.3/Controls/sprk_*/ControlManifest.xml \
     /tmp/v1.2.0/Controls/sprk_*/ControlManifest.xml
```

**Why:** The source manifest might be processed differently during build. Need to see ACTUAL deployed manifest.

### 2. **TEST MINIMAL PCF CONTROL (Priority: HIGH)**

Create a minimal "Hello World" PCF control using official template:

```bash
pac pcf init --namespace Spaarke --name TestControl --template field
npm install
npm run build
# Import and test if THIS appears in "Add components"
```

**Why:** Isolate whether issue is with manifest OR with build/packaging process.

### 3. **EXTRACT WORKING CONTROL PACKAGE (Priority: HIGH)**

If v1.0.3 solution package exists, extract and analyze:

```bash
# Find all old solution packages
find c:/code_files/spaarke -name "*SpeFileViewer*.zip" -type f

# If v1.0.3.zip exists, extract and compare EVERYTHING
```

**Why:** v1.0.3 worked as field control. We need to see EXACTLY what was different.

### 4. **CHECK ENVIRONMENT SETTINGS (Priority: MEDIUM)**

Verify environment has code components enabled:

1. Go to https://admin.powerplatform.microsoft.com
2. Select environment
3. Settings → Features
4. Check "Allow publishing of code components"

**Why:** If disabled, controls won't appear regardless of manifest.

### 5. **TEST WITH BOUND PROPERTY (Priority: MEDIUM)**

Add ONE `usage="bound"` property to manifest:

```xml
<property name="testField"
          of-type="SingleLine.Text"
          usage="bound"
          required="false" />
```

Rebuild and test if THIS makes it discoverable as field control.

**Why:** Pure input-only controls might not be supported in current PCF version.

---

## Recommended Solutions (Ordered by Likelihood)

### Solution 1: Add Output or Bound Property
**Hypothesis:** PCF controls need at least ONE bound or output property to be discoverable.

**Implementation:**
```xml
<!-- Add dummy output property -->
<property name="controlReady"
          of-type="TwoOptions"
          usage="output"
          required="false" />
```

**Likelihood:** 60% - UniversalQuickCreate has output property and works.

### Solution 2: Rebuild from PCF Template
**Hypothesis:** Build process or project structure is corrupted.

**Implementation:**
1. Create new PCF project: `pac pcf init --namespace Spaarke --name SpeFileViewer2 --template field`
2. Copy source files (index.ts, AuthService.ts, etc.) to new project
3. Copy manifest properties to new ControlManifest.Input.xml
4. Build and test

**Likelihood:** 40% - Complete rebuild might fix hidden corruption.

### Solution 3: Use Different Control Type Pattern
**Hypothesis:** Need to use dataset control pattern even for single-item viewer.

**Implementation:**
Add minimal dataset declaration:

```xml
<data-set name="singleItem" display-name-key="Document" />
```

Modify control to accept dataset binding (even if only showing first item).

**Likelihood:** 20% - Hacky but might work like UniversalDatasetGrid.

### Solution 4: Deploy as HTML Web Resource
**Hypothesis:** PCF might be wrong technology for this use case.

**Implementation:**
Convert to HTML web resource with:
- index.html with React app
- Deploy as web resource
- Add to form as HTML component

**Likelihood:** 80% success IF we abandon PCF, but requires architecture change.

---

## Files to Review / Remove / Rebuild

### ✅ KEEP AS-IS (Core Logic is Correct)
- `SpeFileViewer/index.ts` - Entry point logic is sound
- `SpeFileViewer/AuthService.ts` - MSAL implementation is correct
- `SpeFileViewer/BffClient.ts` - HTTP client is correct
- `SpeFileViewer/FilePreview.tsx` - React component is functional
- `SpeFileViewer/types.ts` - Type definitions are correct
- `SpeFileViewer/css/SpeFileViewer.css` - Styling is fine
- `package.json` - Dependencies are correct

### ⚠️ REVIEW (Potentially Incorrect)
- **`SpeFileViewer/ControlManifest.Input.xml`** - Might be missing required element
- **`SpeFileViewerSolution/src/Other/Customizations.xml`** - Empty `<CustomControls />` might be issue
- **`SpeFileViewerSolution/SpeFileViewerSolution.cdsproj`** - Build process might be wrong

### ❌ REMOVE (Not Helpful)
- All `DEPLOYMENT-*.md` docs (historical context only)
- `IMPLEMENTATION-PLAN-V1.0.5.md` (outdated)
- `IMPLEMENTATION-STATUS-V1.0.5.md` (outdated)
- Old solution packages (v1.0.8, v1.0.9, v1.1.0) - Clean up bin/Release folder

### 🔨 REBUILD (Start Fresh)
1. **Option A - Minimal PCF:** Create new PCF project from template, port logic
2. **Option B - HTML Web Resource:** Convert entire control to HTML web resource
3. **Option C - Canvas Component:** Rebuild as Canvas Component (if requirements allow)

---

## Next Immediate Actions

### Action 1: Find v1.0.3 Package (CRITICAL)
```bash
cd c:/code_files/spaarke
find . -name "*v1.0.3*.zip" -o -name "*1.0.3*.zip"
```

If found, extract and compare byte-by-byte with v1.2.0.

### Action 2: Test Minimal Control (HIGH)
Create absolute minimal PCF control and test if IT appears:

```bash
cd c:/code_files/spaarke/src/controls/
pac pcf init --namespace Spaarke --name MinimalTest --template field
cd MinimalTest
npm install
npm run build
# Package and test
```

### Action 3: Add Output Property (MEDIUM)
Try adding output property to v1.2.0 manifest and rebuild:

```xml
<property name="isReady"
          of-type="TwoOptions"
          usage="output"
          required="false" />
```

---

## Conclusion

**Current Status:** Complete deployment failure across 8 versions (v1.0.5-v1.2.0)

**Most Likely Root Cause:** Missing required manifest element OR corrupted build process

**Highest Priority Action:** Find and extract v1.0.3 working package for comparison

**Fallback Option:** Rebuild from scratch using PCF template OR convert to HTML web resource

**Decision Point:** Determine if PCF is correct technology for this use case, or if HTML web resource would be simpler and more reliable.
