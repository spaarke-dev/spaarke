# KM-DATAVERSE-SOLUTION-DEPLOYMENT-FULL-GUIDE

**Knowledge Management Article**
**Category:** Dataverse Development
**Tags:** Dataverse, Power Apps CLI, Solutions, Deployment, CI/CD, PCF
**Last Updated:** 2025-10-18
**Environment:** Spaarke Dev 1 (https://spaarkedev1.crm.dynamics.com/)

---

## Overview

This comprehensive guide covers the complete lifecycle of Dataverse solution deployment, including PCF controls, plugins, entities, workflows, and full applications. For quick PCF-specific deployment, see [KM-PCF-COMPONENT-DEPLOYMENT.md](./KM-PCF-COMPONENT-DEPLOYMENT.md).

**What You'll Learn:**
- Deploy any type of Dataverse solution component
- Understand Managed vs Unmanaged solutions
- Package PCF controls with all required components
- Verify solution contents before deployment
- Manage solution versions and dependencies
- Implement CI/CD pipelines
- Troubleshoot deployment issues

**⚠️ Quick Reference Available:**
For fast, action-oriented PCF deployment instructions, use [KM-PCF-COMPONENT-DEPLOYMENT.md](./KM-PCF-COMPONENT-DEPLOYMENT.md) (~250 lines, optimized for AI code sessions).

**⚠️ IMPORTANT: Use Unmanaged Solutions for Development**

For **Spaarke Dev 1** and all development environments, **ALWAYS deploy Unmanaged solutions**. This allows:
- ✅ Modifications and updates in the environment
- ✅ Iterative development without deleting/reimporting
- ✅ Quick testing and bug fixes
- ✅ Easy rollback of changes

**Managed solutions should only be used for production deployments.**

---

## Prerequisites

### Required Tools

1. **Power Apps CLI (pac)**
   ```bash
   # Verify installation
   pac --version

   # Expected output
   # Microsoft PowerPlatform CLI
   # Version: 1.29.6+...
   ```

2. **.NET SDK** (for solution builds)
   ```bash
   # Verify installation
   dotnet --version

   # Expected output (6.0 or higher)
   # 6.0.xxx or 8.0.xxx
   ```

3. **MSBuild** (for solution packaging)
   ```bash
   # Locate MSBuild (Windows)
   where.exe msbuild

   # Common location
   # C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe
   ```

4. **Node.js and npm** (for PCF controls)
   ```bash
   # Verify installation
   node --version  # 18.x or higher
   npm --version   # 9.x or higher
   ```

### Required Permissions

- **Environment Access:** System Customizer or System Administrator role
- **Solution Import:** Create and update permissions on Solution entity
- **Customization:** Publish permissions

---

## Spaarke Environment Details

### Current Environments

| Environment | URL | Purpose | User |
|------------|-----|---------|------|
| **Spaarke Dev 1** | https://spaarkedev1.crm.dynamics.com/ | Development/Testing | ralph.schroeder@spaarke.com |
| HIPC Dev 2 | https://hipc-dev-2.crm.dynamics.com/ | Reference | ralph.schroeder@heliosip.com |

### Authentication Profile

```bash
# View current authentication profiles
pac auth list

# Expected output
# Index Active Kind      Name                 User                         Environment   Environment Url
# [1]          UNIVERSAL                      ralph.schroeder@heliosip.com HIPC DEV 2    https://hipc-dev-2.crm.dynamics.com/
# [2]   *      UNIVERSAL SpaarkeDevDeployment ralph.schroeder@spaarke.com  SPAARKE DEV 1 https://spaarkedev1.crm.dynamics.com/
```

---

## ⚠️ CRITICAL: Dataverse 5MB Web Resource Limit

### Understanding the 5MB Hard Limit

**Dataverse Restriction:** Web resources (including PCF bundle.js files) cannot exceed **5MB (5,242,880 bytes)**

**What Happens If You Exceed 5MB:**
- Solution import may fail with error: "Web resource size exceeds maximum allowed size"
- Or import succeeds but control fails to load on forms
- Browser console shows: "Failed to load resource" or timeout errors loading bundle.js

This is a **hard limit enforced by Dataverse** - there are no workarounds or exceptions.

---

### Development vs Production Build Modes

PCF controls can be built in two modes with drastically different file sizes:

#### **Development Build (NOT for Dataverse)**

**How It's Triggered:**
- `npm run build` with `NODE_ENV=development` or no `PcfBuildMode` set

**Characteristics:**
- ✅ Includes source maps (.map files) for debugging
- ✅ Unminified JavaScript (readable variable names, preserved formatting)
- ✅ Debugging symbols and comments preserved
- ✅ Console.log statements included
- ❌ **Bundle size: 10-25 MB** (easily exceeds 5MB limit)

**Example Development Bundle:**
```
out/controls/UniversalQuickCreate/bundle.js: 15.2 MB
out/controls/UniversalQuickCreate/bundle.js.map: 8.4 MB
Total: 23.6 MB ❌ Cannot deploy to Dataverse
```

**Use Case:** Local debugging only (NOT for Dataverse deployment)

---

#### **Production Build (Required for Dataverse)**

**How It's Triggered:**
- Set `<PcfBuildMode>production</PcfBuildMode>` in .pcfproj file
- Or `NODE_ENV=production npm run build`

**Characteristics:**
- ✅ Minified JavaScript (compressed, short variable names)
- ✅ No source maps
- ✅ Tree-shaking removes unused code
- ✅ Dead code elimination
- ✅ Comments stripped
- ✅ Console.log statements removed (with appropriate webpack config)
- ✅ **Bundle size: 500KB-3MB** (fits in 5MB limit)

**Example Production Bundle:**
```
out/controls/UniversalQuickCreate/bundle.js: 1.8 MB
Total: 1.8 MB ✅ Deployable to Dataverse
```

**Use Case:** All Dataverse deployments (dev, QA, production environments)

---

### How Spaarke Repository Handles This

Both UniversalQuickCreate and UniversalDatasetGrid are configured to force production builds to avoid the 5MB issue.

#### **Configuration 1: UniversalQuickCreate (Always Production)**

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate.pcfproj` (lines 25-28)

```xml
<!-- Force production builds for deployment to stay under 5MB limit -->
<PropertyGroup>
  <PcfBuildMode>production</PcfBuildMode>
</PropertyGroup>
```

**Effect:**
- `npm run build` → Production build (always)
- `dotnet build` → Production build (always)
- Cannot create development builds (intentional safety)
- Bundle size: ~1.8 MB

**Pros:**
- ✅ Impossible to accidentally create oversized bundle
- ✅ Always deployment-ready

**Cons:**
- ⚠️ Harder to debug (no source maps)
- ⚠️ Must use browser devtools for debugging minified code

---

#### **Configuration 2: UniversalDatasetGrid (Conditional Production)**

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid.pcfproj` (lines 23-26)

```xml
<!-- Force production builds for deployment to stay under 5MB limit -->
<PropertyGroup Condition="'$(BuildSource)' == 'MSBuild'">
  <PcfBuildMode>production</PcfBuildMode>
</PropertyGroup>
```

**Effect:**
- `npm run build` → **Development build** (npm doesn't set BuildSource variable)
- `dotnet build` → **Production build** (MSBuild sets BuildSource=MSBuild)

**Pros:**
- ✅ Development builds available for local debugging (`npm run build`)
- ✅ Production builds for deployment (`dotnet build`)

**Cons:**
- ⚠️ Can accidentally deploy development build if using wrong command
- ⚠️ Must remember: `dotnet build` for deployment, `npm run build` for local dev

---

### Verification Commands - Check Bundle Size

#### **Check Bundle Size Before Deployment (ALWAYS DO THIS)**

```bash
# Method 1: Human-readable format
ls -lh out/controls/[ControlName]/bundle.js

# Expected output (production):
# -rw-r--r-- 1 user group 1.8M Oct 18 bundle.js  ✅ Good (under 5MB)

# Warning output (development):
# -rw-r--r-- 1 user group 15M Oct 18 bundle.js  ❌ Too large for Dataverse

# Method 2: Exact size in MB
du -h out/controls/[ControlName]/bundle.js
# Output: 1.8M  ✅ Safe to deploy
```

#### **Automated Check (Script-Friendly)**

```bash
# Check if bundle exceeds 5MB limit
BUNDLE_PATH="out/controls/[ControlName]/bundle.js"
BUNDLE_SIZE=$(stat -c%s "$BUNDLE_PATH" 2>/dev/null || stat -f%z "$BUNDLE_PATH")
BUNDLE_MB=$((BUNDLE_SIZE / 1024 / 1024))
MAX_SIZE=5242880  # 5MB in bytes

if [ $BUNDLE_SIZE -gt $MAX_SIZE ]; then
  echo "❌ ERROR: Bundle exceeds 5MB limit"
  echo "   Current size: $BUNDLE_MB MB"
  echo "   Maximum allowed: 5 MB"
  echo "   Cannot deploy to Dataverse - rebuild in production mode"
  exit 1
else
  echo "✅ Bundle size OK: $BUNDLE_MB MB (under 5MB limit)"
fi
```

#### **Check Bundle in Solution Package**

```bash
# After building solution, verify bundle size in ZIP
unzip -l bin/Release/[SolutionName].zip | grep bundle.js

# Expected output (production):
# 1847392  Oct 18 WebResources/cc_sprk_UniversalQuickCreate/bundle.js
# ^^^^^^
# 1.8 MB (in bytes) ✅ Under 5MB limit

# Convert bytes to MB for easier reading
unzip -l bin/Release/[SolutionName].zip | \
  grep bundle.js | \
  awk '{printf "Bundle size: %.2f MB\n", $1/1024/1024}'
# Output: Bundle size: 1.76 MB  ✅ Safe
```

---

### Troubleshooting: Bundle Exceeds 5MB

#### **Issue: Bundle.js is 10-25 MB After Build**

**Symptom:**
```bash
ls -lh out/controls/UniversalQuickCreate/bundle.js
# -rw-r--r-- 1 user 15M Oct 18 bundle.js  ❌ Too large
```

**Root Cause:** Development mode enabled or `PcfBuildMode` not set to production

**Fix Option 1: Verify .pcfproj Configuration**

```bash
# 1. Check current PcfBuildMode setting
cat [ControlName].pcfproj | grep -A2 "PcfBuildMode"

# Should show:
# <PropertyGroup>
#   <PcfBuildMode>production</PcfBuildMode>
# </PropertyGroup>

# 2. If missing, edit .pcfproj and add inside <Project> tag:
<PropertyGroup>
  <PcfBuildMode>production</PcfBuildMode>
</PropertyGroup>

# 3. Clean and rebuild
npm run clean
npm run build

# 4. Verify new bundle size
ls -lh out/controls/[ControlName]/bundle.js
# Should now be 1-3 MB (not 10-25 MB)
```

**Fix Option 2: Force Production Build via Environment Variable**

```bash
# Override build mode via environment variable
NODE_ENV=production npm run build

# Verify bundle size reduced
ls -lh out/controls/[ControlName]/bundle.js
```

**Fix Option 3: Use dotnet build (For DatasetGrid-style config)**

```bash
# If .pcfproj has conditional production mode (BuildSource check)
# Use dotnet build instead of npm run build

cd [ControlName]Solution
dotnet build --configuration Release

# This triggers MSBuild which sets BuildSource=MSBuild
# Activating production mode automatically
```

---

#### **Issue: Still Over 5MB Even in Production Mode**

**If production build still exceeds 5MB, reduce bundle size:**

**1. Remove Unused Dependencies**
```bash
# Check current dependencies
npm list --depth=0

# Remove unused packages
npm uninstall [unused-package]

# Example: If using moment.js but don't need it
npm uninstall moment
```

**2. Use Tree-Shaking Compatible Imports**
```typescript
// ❌ BAD - Imports entire library
import * as _ from 'lodash';
import * as moment from 'moment';

// ✅ GOOD - Imports only needed functions
import debounce from 'lodash/debounce';
import format from 'date-fns/format';
```

**3. Lazy Load Heavy Components**
```typescript
// ✅ Use React.lazy for code splitting
const HeavyFileUploader = React.lazy(() => import('./components/HeavyFileUploader'));

// Wrap in Suspense
<React.Suspense fallback={<div>Loading...</div>}>
  <HeavyFileUploader />
</React.Suspense>
```

**4. Remove Duplicate Dependencies**
```bash
# Deduplicate npm packages
npm dedupe

# Check for duplicate versions
npm list lodash  # Shows all versions of lodash
```

**5. Analyze Bundle Composition**
```bash
# Add webpack-bundle-analyzer to see what's taking space
npm install --save-dev webpack-bundle-analyzer

# Add to package.json scripts:
"analyze": "npm run build -- --analyze"

# Run analysis
npm run analyze
# Opens visualization showing bundle composition
```

---

### Pre-Deployment Bundle Size Checklist

Before deploying ANY PCF control to Dataverse, verify:

- [ ] `.pcfproj` has `<PcfBuildMode>production</PcfBuildMode>`
- [ ] Run `npm run clean` before building (removes cached development builds)
- [ ] Check bundle size: `ls -lh out/controls/[ControlName]/bundle.js`
- [ ] Verify bundle under 5MB: **Should show 1-3 MB, NOT 10-25 MB**
- [ ] Verify no source maps in production: `ls out/controls/[ControlName]/*.map` (should be empty or not exist)
- [ ] If using solution import, verify bundle in ZIP: `unzip -l bin/Release/*.zip | grep bundle.js`
- [ ] Calculate exact size if close to limit: `du -b out/controls/[ControlName]/bundle.js`

**If bundle exceeds 5MB, DO NOT IMPORT** - it will fail or cause runtime errors.

---

## Part 1: Build PCF Control

### Step 1: Clean Previous Build

**Purpose:** Remove stale build artifacts to ensure fresh build

```bash
# Navigate to PCF control directory
cd src/controls/[ControlName]

# Example: UniversalQuickCreate
cd src/controls/UniversalQuickCreate

# Clean build artifacts
npm run clean
```

**Expected Output:**
```
[build] Cleaning build outputs...
[build] Succeeded
```

**AI Vibe Instructions:**
```
Clean previous build for [ControlName] PCF control
```

---

### Step 2: Install Dependencies

```bash
# Ensure all npm packages are installed
npm install
```

**Expected Output:**
```
added X packages in Ys
```

**Troubleshooting:**
- If peer dependency warnings appear, they can usually be ignored
- For errors, try: `rm -rf node_modules && npm install`

---

### Step 3: Build PCF Control

```bash
# Build the control (TypeScript → JavaScript, React bundling)
npm run build
```

**Expected Output:**
```
[build] Initializing...
[build] Validating manifest...
[build] Generating manifest types...
[build] Running ESLint...
[build] Compiling and bundling control...
webpack compiled successfully
[build] Succeeded
```

**Build Artifacts Created:**
- `out/controls/[ControlName]/bundle.js` - Bundled JavaScript
- `out/controls/[ControlName]/ControlManifest.xml` - Manifest
- `out/controls/[ControlName]/css/` - Styles

**AI Vibe Instructions:**
```
Build the [ControlName] PCF control, ensure TypeScript compiles and webpack bundles successfully
```

---

## Part 2: Package Solution

### Step 4: Navigate to Solution Directory

**Solution Structure:**
```
src/controls/
└── [ControlName]/
    ├── [ControlName]/           # PCF control source
    │   ├── index.ts
    │   ├── ControlManifest.Input.xml
    │   └── out/                 # Build output
    └── [ControlName]Solution/   # Solution project
        ├── src/
        ├── Other/
        ├── [SolutionName].cdsproj
        └── bin/Release/         # Final ZIP output
```

```bash
# Navigate to solution directory
cd [ControlName]Solution

# Example: UniversalQuickCreate
cd src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
```

---

### Step 5: Configure Solution for Unmanaged Build

**⚠️ CRITICAL FOR DEVELOPMENT ENVIRONMENTS**

Before building, ensure the solution is configured to build as **Unmanaged**:

1. Open the solution project file: `[SolutionName].cdsproj`
2. Find or add the `SolutionPackageType` property
3. Set to **Unmanaged**

**Edit File:** `[ControlName]Solution/[SolutionName].cdsproj`

```xml
<!-- Find this section (usually around line 19-27) -->
<PropertyGroup>
  <SolutionPackageType>Unmanaged</SolutionPackageType>
  <SolutionPackageEnableLocalization>false</SolutionPackageEnableLocalization>
</PropertyGroup>
```

**If the section is commented out:**
```xml
<!-- BEFORE (commented) -->
<!--
<PropertyGroup>
  <SolutionPackageType>Managed</SolutionPackageType>
  <SolutionPackageEnableLocalization>false</SolutionPackageEnableLocalization>
</PropertyGroup>
-->

<!-- AFTER (uncommented and changed to Unmanaged) -->
<PropertyGroup>
  <SolutionPackageType>Unmanaged</SolutionPackageType>
  <SolutionPackageEnableLocalization>false</SolutionPackageEnableLocalization>
</PropertyGroup>
```

**Why This Matters:**
- **Unmanaged:** Can be modified in environment, easy to update/iterate (USE FOR DEV)
- **Managed:** Locked, cannot be modified, requires delete/reimport for updates (USE FOR PROD)

**AI Vibe Instructions:**
```
Edit [SolutionName].cdsproj file and set SolutionPackageType to Unmanaged for development deployment
```

---

### Step 6: Handle Directory.Packages.props (If Exists)

**Issue:** Central package management can conflict with solution builds

**Solution:** Temporarily disable at repository root

```bash
# Check if file exists and disable it
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props" "/c/code_files/spaarke/Directory.Packages.props.disabled"
fi
```

**Why:** Some solutions use project-level package management instead of central management

**AI Vibe Instructions:**
```
Temporarily disable Directory.Packages.props if it exists at repository root before building solution
```

---

### Step 7: Build Solution

```bash
# Build solution in Release configuration
dotnet build --configuration Release
```

**Expected Output:**
```
Determining projects to restore...
All projects are up-to-date for restore.
Running Solution Packager to build package type: Unmanaged bin\Release\[SolutionName].zip

Packing [path]\obj\Release\Metadata to bin\Release\[SolutionName].zip

Processing Component: Entities
Processing Component: CustomControls
 - Spaarke.Controls.[ControlName]
Processing Component: SolutionPluginAssemblies
...

Unmanaged Pack complete.

Solution: bin\Release\[SolutionName].zip generated.
Solution Package Type: Unmanaged generated.
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.50
```

**⚠️ Verify Package Type:**
- Look for: **"Unmanaged Pack complete"** (NOT "Managed Pack complete")
- Look for: **"Solution Package Type: Unmanaged generated"**

**If you see "Managed Pack complete":**
- Stop! The solution is building as Managed
- Go back to Step 5 and update the .cdsproj file
- Ensure `<SolutionPackageType>Unmanaged</SolutionPackageType>`

**AI Vibe Instructions:**
```
Build the [SolutionName] solution as Unmanaged in Release configuration using dotnet build, verify output shows "Unmanaged Pack complete"
```

---

### Step 8: Restore Directory.Packages.props

```bash
# Re-enable Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props.disabled" "/c/code_files/spaarke/Directory.Packages.props"
fi
```

**Important:** Always restore this file after building to prevent issues with other projects

---

### Step 9: Verify Solution Package

```bash
# Check solution file was created
ls -lh bin/Release/*.zip
```

**Expected Output:**
```
-rw-r--r-- 1 User 4096 1.2M Oct 07 15:38 bin/Release/UniversalQuickCreateSolution.zip
```

**Package Characteristics:**
- **Type:** Unmanaged (for development environments)
- **Size:** Typically 500KB - 5MB depending on components
- **Contents:** CustomControls, Entities, Workflows, etc.

**⚠️ Important Note:**
The ZIP file itself doesn't indicate if it's Managed or Unmanaged. You must rely on the build output message ("Unmanaged Pack complete") to verify.

**AI Vibe Instructions:**
```
Verify the solution package was created successfully in bin/Release/ directory with appropriate size
```

---

## Part 3: Import Solution to Dataverse

### Step 9: Authenticate to Environment

**Option A: Check Existing Authentication**

```bash
# List all authentication profiles
pac auth list
```

**If already authenticated to target environment (marked with *):**
- Skip to Step 10 (Import)

**Option B: Create New Authentication**

```bash
# Authenticate to Spaarke Dev 1
pac auth create --url https://spaarkedev1.crm.dynamics.com/ --name SpaarkeDevDeployment

# This will:
# 1. Open browser for authentication
# 2. Prompt for credentials: ralph.schroeder@spaarke.com
# 3. Create authentication profile
# 4. Mark as active (*)
```

**Expected Output:**
```
Creating new authentication profile...
Opening browser for authentication...
Authentication successful
New authentication profile created: SpaarkeDevDeployment
```

**AI Vibe Instructions:**
```
Authenticate to Spaarke Dev 1 environment at https://spaarkedev1.crm.dynamics.com/ using pac auth create if not already authenticated
```

---

### Step 10: Check for Existing Solution (Important!)

Before importing, check if a solution with the same name already exists:

```bash
# Check existing solutions
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "[SolutionName]"
```

**If solution exists:**

Check the **Managed** column (last column):
- **False** = Unmanaged → Safe to import (will update)
- **True** = Managed → **MUST DELETE FIRST** (see Step 10a below)

---

### Step 10a: Delete Existing Managed Solution (If Needed)

**⚠️ CRITICAL: Cannot import Unmanaged over Managed**

If a Managed version exists, you MUST delete it first:

```bash
# Delete existing managed solution
pac solution delete \
  --solution-name [SolutionName] \
  --environment https://spaarkedev1.crm.dynamics.com/

# Example: UniversalQuickCreate
pac solution delete \
  --solution-name UniversalQuickCreateSolution \
  --environment https://spaarkedev1.crm.dynamics.com/
```

**Expected Output:**
```
Connected as ralph.schroeder@spaarke.com
Connected to... SPAARKE DEV 1

Deleting Dataverse Solution...

Solution deleted successfully.
```

**Why This Is Needed:**
- Dataverse **does not allow** importing Unmanaged solution over Managed solution
- Error: "Solution is already installed as managed solution and package is attempting to install in unmanaged mode"
- Must delete first, then import

**AI Vibe Instructions:**
```
Check if [SolutionName] exists as Managed solution and delete it if found before importing Unmanaged version
```

---

### Step 10b: Import Solution

```bash
# Import solution to environment
pac solution import \
  --path bin/Release/[SolutionName].zip \
  --environment https://spaarkedev1.crm.dynamics.com/ \
  --async

# Example: UniversalQuickCreate
pac solution import \
  --path bin/Release/UniversalQuickCreateSolution.zip \
  --environment https://spaarkedev1.crm.dynamics.com/ \
  --async
```

**Parameters:**
- `--path`: Path to solution ZIP file
- `--environment`: Target Dataverse environment URL
- `--async`: Import asynchronously (recommended for faster response)

**Expected Output:**
```
Connected as ralph.schroeder@spaarke.com
Connected to... SPAARKE DEV 1

Solution Importing...

Waiting for asynchronous operation [operation-id] to complete with timeout of 01:00:00.

Processing asynchronous operation... execution time: 00:00:00 and 0.00% of max time allotted
Processing asynchronous operation... execution time: 00:00:04 and 0.12% of max time allotted
Processing asynchronous operation... execution time: 00:00:08 and 0.23% of max time allotted
...

Asynchronous operation [operation-id] completed successfully within 00:00:24.7747925

Solution Imported successfully. Import ID: [import-id]
```

**Import Timing:**
- Small solutions: 15-30 seconds
- Medium solutions: 30-60 seconds
- Large solutions: 1-3 minutes

**AI Vibe Instructions:**
```
Import [SolutionName] solution to Spaarke Dev 1 environment asynchronously and wait for completion
```

---

### Step 11: Verify Import

```bash
# List solutions in environment
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "[SolutionName]"

# Example: UniversalQuickCreate
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "universal"
```

**Expected Output:**
```
UniversalQuickCreateSolution UniversalQuickCreateSolution          1.0.1          False
```

**Columns:**
- **Name:** Solution unique name
- **Display Name:** Friendly name
- **Version:** Solution version (e.g., 1.0, 1.0.1)
- **Managed:** **False = Unmanaged** (correct for dev), True = Managed (wrong for dev)

**⚠️ Verify Managed Status:**
- Should show **False** for development environments
- If shows **True**, you deployed a Managed solution (not recommended for dev)
- To fix: Delete and redeploy as Unmanaged (see Step 10a)

**AI Vibe Instructions:**
```
Verify [SolutionName] solution was imported successfully by listing solutions in the environment
```

---

## Part 4: Publish Customizations

### Step 12: Publish All Customizations

**Note:** Solution import does NOT automatically publish customizations

```bash
# Publish all customizations in the environment
pac solution publish --environment https://spaarkedev1.crm.dynamics.com/
```

**Alternative: Publish via Power Apps Maker Portal**

1. Navigate to https://make.powerapps.com
2. Select **Spaarke Dev 1** environment (top right)
3. Go to **Solutions**
4. Click **Publish all customizations** (top toolbar)
5. Wait for "Published successfully" message

**What Gets Published:**
- PCF control registrations
- Entity customizations
- Form changes
- Workflow changes
- Plugin registrations

**Publishing Time:** 10-30 seconds

**AI Vibe Instructions:**
```
Publish all customizations in Spaarke Dev 1 environment to make imported solution components active
```

---

## Part 5: Verification and Testing

### Step 13: Verify Control Registration

**Method 1: Via PAC CLI**

```bash
# List PCF controls in solution (requires solution name)
pac solution list --environment https://spaarkedev1.crm.dynamics.com/
```

**Method 2: Via Power Apps Maker Portal**

1. Go to https://make.powerapps.com
2. Select **Spaarke Dev 1** environment
3. Navigate to **Solutions** → **[Your Solution Name]**
4. Expand **Controls** section
5. Verify control appears: `Spaarke.Controls.[ControlName]`

**Method 3: Via Form Designer**

1. Open any entity form (e.g., Account)
2. Add a text field to the form
3. Click field → **Properties** → **Controls** tab
4. Click **+ Add control**
5. Search for your control name
6. **Expected:** Control appears in list

**AI Vibe Instructions:**
```
Verify [ControlName] PCF control is registered by checking Solutions → Controls in Power Apps maker portal
```

---

### Step 14: Test Control on Form

**Quick Test:**

1. Open form with control configured
2. Open browser console (F12)
3. Verify no JavaScript errors
4. Interact with control
5. Verify functionality works

**For PCF Controls:**
- Control renders without errors
- UI elements visible and styled correctly
- Event handlers work (button clicks, file selection, etc.)
- Data binding works (reads/writes values)

---

## Complete Deployment Workflow (AI Vibe Script)

**⚠️ IMPORTANT: This script assumes solution is already configured for Unmanaged build**

```bash
# Full deployment workflow for Spaarke Dev 1 (Unmanaged Solutions)
# Use this complete script for deployments

# 1. Navigate to PCF control directory
cd src/controls/[ControlName]

# 2. Clean and build PCF control
npm run clean
npm run build

# 3. Navigate to solution directory
cd [ControlName]Solution

# 4. Disable Directory.Packages.props (if exists)
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props" "/c/code_files/spaarke/Directory.Packages.props.disabled"
fi

# 5. Build solution (MUST be configured as Unmanaged in .cdsproj)
dotnet build --configuration Release

# 6. Verify build output shows "Unmanaged Pack complete"
# (Check console output - should NOT show "Managed Pack complete")

# 7. Restore Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props.disabled" "/c/code_files/spaarke/Directory.Packages.props"
fi

# 8. Verify package created
ls -lh bin/Release/*.zip

# 9. Check authentication (should show * for Spaarke Dev 1)
pac auth list

# 10. Check for existing solution (especially if Managed)
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "[SolutionName]"

# 11. If Managed solution exists, DELETE IT FIRST
# pac solution delete --solution-name [SolutionName] --environment https://spaarkedev1.crm.dynamics.com/

# 12. Import solution
pac solution import \
  --path bin/Release/[SolutionName].zip \
  --environment https://spaarkedev1.crm.dynamics.com/ \
  --async

# 13. Verify import and check Managed status (should be False)
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "[SolutionName]"
# Should show: [SolutionName] ... [Version] False

# 14. Publish customizations (via portal - see instructions above)
```

**AI Vibe One-Liner:**
```
Deploy [ControlName] PCF control to Spaarke Dev 1 as Unmanaged solution: verify .cdsproj configured for Unmanaged, clean build, check for existing Managed solution and delete if found, package solution, import to https://spaarkedev1.crm.dynamics.com/, verify Managed=False, and publish customizations
```

**Example: UniversalQuickCreate**
```bash
cd src/controls/UniversalQuickCreate
npm run clean && npm run build
cd UniversalQuickCreateSolution

# Disable Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then mv "/c/code_files/spaarke/Directory.Packages.props" "/c/code_files/spaarke/Directory.Packages.props.disabled"; fi

# Build (ensure .cdsproj has <SolutionPackageType>Unmanaged</SolutionPackageType>)
dotnet build --configuration Release

# Restore Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then mv "/c/code_files/spaarke/Directory.Packages.props.disabled" "/c/code_files/spaarke/Directory.Packages.props"; fi

# Check for existing Managed solution
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "universalquickcreate"

# If Managed exists (True in last column), delete it:
# pac solution delete --solution-name UniversalQuickCreateSolution --environment https://spaarkedev1.crm.dynamics.com/

# Import
pac solution import --path bin/Release/UniversalQuickCreateSolution.zip --environment https://spaarkedev1.crm.dynamics.com/ --async

# Verify (should show False for Managed)
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "universalquickcreate"
```

---

## Troubleshooting Guide

### Issue: "pac: command not found"

**Cause:** Power Apps CLI not installed or not in PATH

**Fix:**
```bash
# Install Power Apps CLI
dotnet tool install --global Microsoft.PowerApps.CLI.Tool

# Or update if already installed
dotnet tool update --global Microsoft.PowerApps.CLI.Tool

# Verify installation
pac --version
```

---

### Issue: "Authentication failed"

**Cause:** Invalid credentials or expired token

**Fix:**
```bash
# Clear existing authentication
pac auth clear

# Re-authenticate
pac auth create --url https://spaarkedev1.crm.dynamics.com/
```

---

### Issue: "Solution import failed - solution already exists"

**Cause:** Solution with same name and version already exists

**Fix Option 1: Upgrade Solution**
```bash
# Use upgrade instead of import
pac solution upgrade \
  --path bin/Release/[SolutionName].zip \
  --environment https://spaarkedev1.crm.dynamics.com/
```

**Fix Option 2: Increment Version**

Edit solution XML before building:
```xml
<!-- In [SolutionName]Solution/src/Solution.xml -->
<Version>1.0.1</Version>  <!-- Change from 1.0.0 to 1.0.1 -->
```

Then rebuild and re-import.

**Fix Option 3: Delete Existing Solution**

Via Power Apps maker portal:
1. Go to **Solutions**
2. Select existing solution
3. Click **Delete**
4. Re-import new version

---

### Issue: "Missing dependencies"

**Cause:** Solution depends on components not in target environment

**Fix:**
```bash
# Import dependency solutions first
# Check solution dependencies in maker portal

# Example: If depends on another solution
pac solution import --path DependencySolution.zip --environment [env-url]
pac solution import --path YourSolution.zip --environment [env-url]
```

---

### Issue: "Control manifest invalid"

**Cause:** Syntax error in ControlManifest.Input.xml

**Fix:**
1. Validate XML syntax
2. Check all required attributes present
3. Verify property types match (e.g., TwoOptions, SingleLine.Text)
4. Review build errors for specific line numbers

**Common Errors:**
- Missing closing tags
- Invalid property types
- Incorrect namespace declarations

---

### Issue: "Build failed - MSBuild errors"

**Cause:** Directory.Packages.props conflicts or missing dependencies

**Fix:**
```bash
# Ensure Directory.Packages.props disabled
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props" "/c/code_files/spaarke/Directory.Packages.props.disabled"
fi

# Clean and restore
dotnet clean
dotnet restore

# Rebuild
dotnet build --configuration Release

# Restore Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props.disabled" "/c/code_files/spaarke/Directory.Packages.props"
fi
```

---

### Issue: "Control not appearing in form designer"

**Cause:** Customizations not published or browser cache

**Fix:**
1. Publish all customizations (see Step 12)
2. Clear browser cache (Ctrl+F5)
3. Sign out and sign back in to Power Apps
4. Check control is in correct solution
5. Verify control namespace matches

---

### Issue: "Import timeout"

**Cause:** Large solution or slow network

**Fix:**
```bash
# Increase timeout (default 60 minutes)
pac solution import \
  --path bin/Release/[SolutionName].zip \
  --environment https://spaarkedev1.crm.dynamics.com/ \
  --async \
  --timeout 120  # 120 minutes
```

---

### Issue: "Control imports successfully but doesn't work - missing web resources"

**Symptom:** Control appears in Solutions → Controls but fails when added to forms. Browser console shows 404 errors for bundle.js or ControlManifest.xml.

**Root Cause:** PCF control metadata imported but web resource files (bundle.js, ControlManifest.xml) not included in solution package.

**Diagnosis Commands:**
```bash
# 1. Export solution to inspect contents
pac solution export \
  --name [SolutionName] \
  --path ./exported-solution.zip \
  --environment https://spaarkedev1.crm.dynamics.com/

# 2. Check if web resources are present
unzip -l exported-solution.zip | grep -i "webresource"

# Expected output should include:
# - cc_[Publisher]_[ControlName]/bundle.js
# - cc_[Publisher]_[ControlName]/ControlManifest.xml
# - Any CSS/image files

# 3. Check control manifest references
unzip -p exported-solution.zip customcontrols.xml | grep -i "webresource"
```

**Common Causes:**

1. **Missing ProjectReference in .cdsproj**
   ```xml
   <!-- WRONG - No ProjectReference -->
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <SolutionPackageType>Unmanaged</SolutionPackageType>
     </PropertyGroup>
   </Project>

   <!-- CORRECT - Includes ProjectReference -->
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <SolutionPackageType>Unmanaged</SolutionPackageType>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="../[ControlName]/[ControlName].pcfproj" />
     </ItemGroup>
   </Project>
   ```

2. **PCF control not built before solution build**
   ```bash
   # WRONG - Build solution without building control first
   cd [ControlName]Solution
   dotnet build --configuration Release  # ❌ No bundle.js to include

   # CORRECT - Build control first, then solution
   cd src/controls/[ControlName]
   npm run build  # ✅ Creates bundle.js
   cd [ControlName]Solution
   dotnet build --configuration Release  # ✅ Includes bundle.js
   ```

3. **Using `pac pcf push` instead of solution import**
   - `pac pcf push` deploys control metadata only
   - Does NOT deploy web resources (bundle.js, CSS, etc.)
   - Use solution import for complete deployment

**Fix:**

1. **Verify .cdsproj has ProjectReference:**
   ```bash
   # Check .cdsproj file
   cat [ControlName]Solution/[SolutionName].cdsproj | grep -i "ProjectReference"

   # Should output:
   # <ProjectReference Include="../[ControlName]/[ControlName].pcfproj" />
   ```

2. **Rebuild control and solution:**
   ```bash
   # Navigate to control directory
   cd src/controls/[ControlName]

   # Clean and rebuild control
   npm run clean
   npm run build

   # Navigate to solution directory
   cd [ControlName]Solution

   # Clean and rebuild solution
   dotnet clean
   dotnet build --configuration Release
   ```

3. **Verify bundle.js in package before importing:**
   ```bash
   # CRITICAL VERIFICATION STEP
   unzip -l bin/Release/[SolutionName].zip | grep bundle.js

   # Expected output:
   # WebResources/cc_sprk_UniversalQuickCreate/bundle.js

   # If bundle.js missing, solution build failed - DO NOT IMPORT
   ```

4. **Delete old control and reimport:**
   ```bash
   # Delete existing (incomplete) solution
   pac solution delete \
     --solution-name [SolutionName] \
     --environment https://spaarkedev1.crm.dynamics.com/

   # Import complete solution with web resources
   pac solution import \
     --path bin/Release/[SolutionName].zip \
     --environment https://spaarkedev1.crm.dynamics.com/ \
     --async

   # Publish customizations
   pac solution publish --environment https://spaarkedev1.crm.dynamics.com/
   ```

**Prevention:**
- Always build PCF control (`npm run build`) before building solution
- Always verify bundle.js in ZIP before importing: `unzip -l bin/Release/*.zip | grep bundle.js`
- Use solution import (not `pac pcf push`) for production deployments
- Ensure .cdsproj has ProjectReference to .pcfproj

**Related:** See [KM-PCF-COMPONENT-DEPLOYMENT.md](./KM-PCF-COMPONENT-DEPLOYMENT.md) for detailed packaging verification steps.

---

### Issue: "Cannot import Unmanaged solution - Managed version already exists"

**Error Message:**
```
Solution is already installed as managed solution and package is attempting to install in unmanaged mode
```

**Cause:** Trying to import Unmanaged solution when Managed version of same solution already exists in environment.

**Background:**
- Dataverse does NOT allow Unmanaged solution to overwrite Managed solution
- This is by design - Managed solutions are "locked" for production protection
- Common when switching from production deployment to development testing

**Fix:**
```bash
# 1. Check if solution exists and is Managed
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "[SolutionName]"

# Output shows Managed status in last column:
# [SolutionName]  [DisplayName]  [Version]  True   ← Managed = True

# 2. Delete Managed solution
pac solution delete \
  --solution-name [SolutionName] \
  --environment https://spaarkedev1.crm.dynamics.com/

# 3. Import Unmanaged solution
pac solution import \
  --path bin/Release/[SolutionName].zip \
  --environment https://spaarkedev1.crm.dynamics.com/ \
  --async

# 4. Verify import shows Managed = False
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep -i "[SolutionName]"
# [SolutionName]  [DisplayName]  [Version]  False  ← Managed = False
```

**Important Notes:**
- Deleting Managed solution removes ALL components from environment
- Any forms/workflows using components will break until solution reimported
- Coordinate with users if deleting from shared environment
- For production environments, keep Managed solutions - use Unmanaged only in dev

**Prevention:**
- Use Unmanaged solutions for development environments (SPAARKE DEV 1)
- Use Managed solutions for production environments
- Verify `<SolutionPackageType>Unmanaged</SolutionPackageType>` in .cdsproj before building
- Check build output for "Unmanaged Pack complete" (not "Managed Pack complete")

---

### Issue: "Build shows 'Managed Pack complete' but I configured Unmanaged"

**Symptom:**
```
dotnet build output shows:
Managed Pack complete.
Solution Package Type: Managed generated.
```

**Cause:** .cdsproj file has SolutionPackageType commented out or set to Managed

**Diagnosis:**
```bash
# Check .cdsproj configuration
cat [ControlName]Solution/[SolutionName].cdsproj | grep -A2 "SolutionPackageType"
```

**Common Issues:**

1. **Property Group Commented Out:**
   ```xml
   <!-- WRONG - Commented out -->
   <!--
   <PropertyGroup>
     <SolutionPackageType>Unmanaged</SolutionPackageType>
   </PropertyGroup>
   -->

   <!-- CORRECT - Uncommented -->
   <PropertyGroup>
     <SolutionPackageType>Unmanaged</SolutionPackageType>
   </PropertyGroup>
   ```

2. **Set to Managed:**
   ```xml
   <!-- WRONG - Set to Managed -->
   <PropertyGroup>
     <SolutionPackageType>Managed</SolutionPackageType>
   </PropertyGroup>

   <!-- CORRECT - Set to Unmanaged -->
   <PropertyGroup>
     <SolutionPackageType>Unmanaged</SolutionPackageType>
   </PropertyGroup>
   ```

**Fix:**
1. Edit .cdsproj file and set `<SolutionPackageType>Unmanaged</SolutionPackageType>`
2. Clean build artifacts: `dotnet clean`
3. Rebuild: `dotnet build --configuration Release`
4. Verify output shows "Unmanaged Pack complete"

**Verification:**
```bash
# Build output MUST show:
Unmanaged Pack complete.
Solution: bin\Release\[SolutionName].zip generated.
Solution Package Type: Unmanaged generated.

# NOT:
Managed Pack complete.  # ❌ WRONG
```

---

### Issue: "Working on wrong control - applied fix to DatasetGrid instead of QuickCreate"

**Symptom:** Made code changes but control behavior unchanged when deployed.

**Root Cause:** Multi-control repository with similar folder structures. Easy to edit wrong control's files.

**Example:**
```
src/controls/
├── UniversalDatasetGrid/
│   ├── UniversalDatasetGrid/
│   │   └── services/auth/msalConfig.ts  ← Edited this by mistake
│   └── UniversalDatasetGridSolution/
└── UniversalQuickCreate/
    ├── UniversalQuickCreate/
    │   └── services/auth/msalConfig.ts  ← Should have edited this
    └── UniversalQuickCreateSolution/
```

**Prevention - Pre-Change Verification:**

1. **Use `ls` to verify current directory:**
   ```bash
   # Before editing any file, verify location
   pwd
   ls -la

   # Output should show:
   # UniversalQuickCreate/  (not UniversalDatasetGrid/)
   ```

2. **Check file last modified date before editing:**
   ```bash
   # Verify you're editing current working file
   ls -l [ControlName]/services/auth/msalConfig.ts

   # Compare to expected last modified date
   ```

3. **Verify file path in editor before saving:**
   - VSCode shows full path in tab: `src/controls/UniversalQuickCreate/...`
   - Check path before Ctrl+S

4. **Use grep to find all instances before editing:**
   ```bash
   # Find all msalConfig.ts files
   find src/controls -name "msalConfig.ts"

   # Output shows all locations:
   # src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts
   # src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts

   # Edit the CORRECT one for your task
   ```

**Detection - Post-Change Verification:**

1. **Check file modified timestamp after edit:**
   ```bash
   # Verify timestamp just changed (within last minute)
   ls -l --time-style=full-iso src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts

   # Should show current date/time (2025-10-18 22:XX)
   ```

2. **Use git to see what files changed:**
   ```bash
   # List modified files
   git status

   # Should show:
   # modified: src/controls/UniversalQuickCreate/...  ✅ Correct
   # NOT: src/controls/UniversalDatasetGrid/...  ❌ Wrong
   ```

3. **Review git diff before committing:**
   ```bash
   # Verify changes are in correct control
   git diff src/controls/

   # Check file path at top of diff output
   ```

**Recovery - If Wrong Control Edited:**

1. **Revert accidental changes:**
   ```bash
   # Revert wrong file
   git checkout src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts
   ```

2. **Apply fix to correct control:**
   ```bash
   # Edit correct file
   # src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts
   ```

3. **Rebuild correct control:**
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```

**Lesson Learned:**
- Always verify file path before editing
- Use `pwd` and `ls` liberally
- Check git status before and after changes
- When in doubt, use full absolute paths

---

## Web Resource Deployment

### Understanding Web Resources in PCF Solutions

**What Are Web Resources?**
Web resources are static files deployed to Dataverse that make PCF controls functional:
- **bundle.js** - Compiled JavaScript code (React components, business logic)
- **ControlManifest.xml** - Control metadata and property definitions
- **CSS files** - Styles for control UI
- **Image files** - Icons, logos, etc.
- **Translation files** - Localization resources

**Web Resource Naming Convention:**
```
cc_[Publisher]_[ControlName]/[FileName]

Examples:
cc_sprk_UniversalQuickCreate/bundle.js
cc_sprk_UniversalQuickCreate/ControlManifest.xml
cc_sprk_UniversalQuickCreate/css/UniversalQuickCreate.css
```

### How Web Resources Are Packaged

**Build Process Flow:**
```
1. npm run build (PCF control)
   ↓ Creates: out/controls/[ControlName]/bundle.js

2. dotnet build (Solution project)
   ↓ Reads: .cdsproj ProjectReference → .pcfproj
   ↓ Packages: bundle.js + ControlManifest.xml → solution ZIP
   ↓ Creates: bin/Release/[SolutionName].zip

3. pac solution import (Deployment)
   ↓ Extracts: ZIP → WebResources/ folder
   ↓ Uploads: Web resources to Dataverse
   ↓ Registers: Control metadata + web resource references
```

**Critical Requirement - ProjectReference:**
The .cdsproj file MUST reference the .pcfproj file to include web resources:

```xml
<!-- File: [ControlName]Solution/[SolutionName].cdsproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <SolutionPackageType>Unmanaged</SolutionPackageType>
  </PropertyGroup>

  <!-- CRITICAL: This line includes web resources -->
  <ItemGroup>
    <ProjectReference Include="../[ControlName]/[ControlName].pcfproj" />
  </ItemGroup>
</Project>
```

**Without ProjectReference:**
- Solution builds successfully but contains NO web resources
- Control metadata imports but control doesn't work
- Browser console shows 404 errors for bundle.js

### Verifying Web Resources in Solution Package

**Before Importing - ALWAYS Verify:**

```bash
# Critical verification command
unzip -l bin/Release/[SolutionName].zip | grep -i "bundle.js"

# Expected output:
# WebResources/cc_sprk_UniversalQuickCreate/bundle.js  [size]

# If NO output → Web resources missing, DO NOT IMPORT
```

**Complete Package Inspection:**
```bash
# List all files in solution package
unzip -l bin/Release/[SolutionName].zip

# Expected structure:
# [Content_Types].xml
# customizations.xml
# solution.xml
# WebResources/
#   cc_sprk_[ControlName]/
#     bundle.js              ← REQUIRED
#     ControlManifest.xml    ← REQUIRED
#     css/                   ← If using CSS
#     img/                   ← If using images
```

**Extract and Inspect (Detailed Analysis):**
```bash
# Create temp directory
mkdir -p /tmp/solution-inspect
cd /tmp/solution-inspect

# Extract solution package
unzip /c/code_files/spaarke/src/controls/[ControlName]/[ControlName]Solution/bin/Release/[SolutionName].zip

# Check WebResources folder
ls -lR WebResources/

# Expected output:
# WebResources/cc_sprk_UniversalQuickCreate/:
# total 512
# -rw-r--r-- 1 user group 245678 Oct 18 bundle.js
# -rw-r--r-- 1 user group   4567 Oct 18 ControlManifest.xml
```

**Check Control Manifest References:**
```bash
# Extract and view customcontrols.xml
unzip -p bin/Release/[SolutionName].zip customcontrols.xml | grep -A5 "webresource"

# Should show:
# <code>
#   <webResource>cc_sprk_UniversalQuickCreate/bundle.js</webResource>
# </code>
```

### Post-Deployment Verification

**Verify Web Resources Deployed:**

1. **Via Power Apps Maker Portal:**
   - Navigate to: https://make.powerapps.com
   - Select: Spaarke Dev 1 environment
   - Go to: **Solutions** → [Your Solution] → **Web Resources**
   - Should list: `cc_sprk_[ControlName]/bundle.js`, `ControlManifest.xml`, etc.

2. **Via Browser Console (When Control Loads):**
   ```javascript
   // Open browser console (F12) on form with control
   // Check Network tab for web resource requests

   // Successful requests (200 OK):
   // GET https://spaarkedev1.crm.dynamics.com/WebResources/cc_sprk_UniversalQuickCreate/bundle.js
   // GET https://spaarkedev1.crm.dynamics.com/WebResources/cc_sprk_UniversalQuickCreate/ControlManifest.xml

   // Failed requests indicate missing web resources:
   // GET https://... (404 Not Found)  ← Web resource not deployed
   ```

3. **Via pac CLI (Export and Inspect):**
   ```bash
   # Export deployed solution
   pac solution export \
     --name [SolutionName] \
     --path ./deployed-solution.zip \
     --environment https://spaarkedev1.crm.dynamics.com/

   # Verify web resources in deployed package
   unzip -l deployed-solution.zip | grep -i "webresource"

   # Should match original package contents
   ```

### Troubleshooting Missing Web Resources

**Symptom:** Control imports but doesn't render on forms

**Diagnosis:**
```bash
# 1. Check if web resources in original package
unzip -l bin/Release/[SolutionName].zip | grep bundle.js
# If empty → Build issue

# 2. Check if web resources deployed to Dataverse
pac solution export --name [SolutionName] --path ./check.zip --environment https://spaarkedev1.crm.dynamics.com/
unzip -l check.zip | grep bundle.js
# If empty → Import issue

# 3. Check browser console for 404 errors
# F12 → Console tab → Look for "Failed to load resource: 404"
```

**Fix:**
1. Verify ProjectReference in .cdsproj
2. Rebuild control: `npm run build`
3. Rebuild solution: `dotnet build --configuration Release`
4. Verify bundle.js in ZIP: `unzip -l bin/Release/*.zip | grep bundle.js`
5. Delete old solution from Dataverse
6. Reimport with verified package
7. Publish customizations

**Prevention Checklist:**
- [ ] .cdsproj has `<ProjectReference Include="../[ControlName]/[ControlName].pcfproj" />`
- [ ] PCF control built before solution (`npm run build` first)
- [ ] Solution package verified before import (`unzip -l ... | grep bundle.js`)
- [ ] Import used solution package method (not `pac pcf push`)
- [ ] Customizations published after import

---

## Advanced: CI/CD Integration

### GitHub Actions Workflow

```yaml
name: Deploy to Dataverse

on:
  push:
    branches: [main]
    paths:
      - 'src/controls/**'

jobs:
  deploy:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v3

      - name: Setup Node.js
        uses: actions/setup-node@v3
        with:
          node-version: '18'

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '6.0.x'

      - name: Install Power Apps CLI
        run: dotnet tool install --global Microsoft.PowerApps.CLI.Tool

      - name: Build PCF Control
        run: |
          cd src/controls/UniversalQuickCreate
          npm ci
          npm run build

      - name: Build Solution
        run: |
          cd src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
          dotnet build --configuration Release

      - name: Authenticate to Dataverse
        env:
          DATAVERSE_URL: https://spaarkedev1.crm.dynamics.com/
          DATAVERSE_CLIENT_ID: ${{ secrets.DATAVERSE_CLIENT_ID }}
          DATAVERSE_CLIENT_SECRET: ${{ secrets.DATAVERSE_CLIENT_SECRET }}
        run: |
          pac auth create \
            --url $DATAVERSE_URL \
            --applicationId $DATAVERSE_CLIENT_ID \
            --clientSecret $DATAVERSE_CLIENT_SECRET

      - name: Import Solution
        run: |
          pac solution import \
            --path src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip \
            --environment https://spaarkedev1.crm.dynamics.com/ \
            --async

      - name: Verify Import
        run: |
          pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep UniversalQuickCreate
```

---

## Version Management

### Version Numbering

**Format:** `Major.Minor.Patch`

```xml
<!-- In Solution.xml -->
<Version>1.0.0</Version>
```

**Increment Rules:**
- **Major (1.x.x):** Breaking changes, incompatible with previous versions
- **Minor (x.1.x):** New features, backward compatible
- **Patch (x.x.1):** Bug fixes, backward compatible

**Examples:**
- Initial release: `1.0.0`
- Add new feature: `1.1.0`
- Fix bug: `1.1.1`
- Breaking change: `2.0.0`

### Updating Version

```bash
# 1. Edit version in Solution.xml
# src/controls/[ControlName]/[ControlName]Solution/src/Solution.xml

# 2. Rebuild solution
dotnet build --configuration Release

# 3. Import updated version
pac solution import --path bin/Release/[SolutionName].zip --environment [env-url] --async
```

---

## Managed vs Unmanaged Solutions

### Managed Solutions (Recommended for Production)

**Characteristics:**
- ✅ Cannot be modified in target environment
- ✅ Can be cleanly uninstalled
- ✅ Recommended for production/QA
- ✅ Protects intellectual property

**Build Command:**
```bash
dotnet build --configuration Release  # Creates managed by default
```

**Package Type in Output:**
```
Processing Component: CustomControls
 - Spaarke.Controls.UniversalQuickCreate
...
Managed Pack complete.
```

### Unmanaged Solutions (Development Only)

**Characteristics:**
- ⚠️ Can be modified in target environment
- ⚠️ Cannot be cleanly uninstalled
- ⚠️ Use for active development only
- ⚠️ Not recommended for production

**Build Command:**
```bash
# Build unmanaged solution
pac solution pack --zipfile UnmanagedSolution.zip --packagetype Unmanaged
```

---

## Best Practices

### Pre-Deployment Checklist

- [ ] Code builds successfully (`npm run build`)
- [ ] Solution builds successfully (`dotnet build`)
- [ ] Solution package created (verify ZIP file exists)
- [ ] Authenticated to correct environment (`pac auth list`)
- [ ] Version number incremented (if updating existing solution)
- [ ] Directory.Packages.props handled correctly
- [ ] No uncommitted changes in git (optional but recommended)

### Post-Deployment Checklist

- [ ] Solution appears in environment (`pac solution list`)
- [ ] Control registered (`Solutions → Controls` in portal)
- [ ] Customizations published
- [ ] Control available in form designer
- [ ] Test control on form (basic smoke test)
- [ ] No JavaScript console errors
- [ ] Browser cache cleared if issues occur

### Deployment Hygiene

1. **Always clean before building**
   ```bash
   npm run clean
   dotnet clean
   ```

2. **Use Release configuration for production**
   ```bash
   dotnet build --configuration Release  # Not Debug
   ```

3. **Test in dev before promoting to QA/prod**
   - Dev → QA → Production pipeline
   - Never deploy untested code to production

4. **Document version changes**
   - Keep changelog of what changed in each version
   - Include in commit messages

5. **Back up solutions before major updates**
   ```bash
   # Export existing solution before importing new version
   pac solution export --name ExistingSolution --path backup/
   ```

---

## Quick Reference

### Common Commands

```bash
# Authentication
pac auth list                                    # List profiles
pac auth create --url [env-url]                  # Authenticate
pac auth select --index 2                        # Switch profile
pac auth clear                                   # Clear all auth

# Solutions
pac solution list --environment [env-url]        # List solutions
pac solution import --path [zip] --environment [env-url] --async
pac solution export --name [solution] --path [output-path]
pac solution delete --solution-name [name]

# PCF Controls
npm run clean                                    # Clean build
npm run build                                    # Build control
dotnet build --configuration Release             # Build solution

# Verification
ls -lh bin/Release/*.zip                         # Check package
pac solution list | grep [name]                  # Verify import
```

---

## Related Documentation

- [KM-PCF-DEVELOPMENT](./KM-PCF-DEVELOPMENT.md) - PCF control development guide
- [KM-POWER-APPS-CLI](./KM-POWER-APPS-CLI.md) - Power Apps CLI reference
- [Sprint 7B Implementation Status](../dev/projects/sdap_project/Sprint 7B Doc Quick Create/SPRINT-7B-IMPLEMENTATION-STATUS.md)

---

## Appendix: Environment URLs

### Spaarke Environments

| Name | URL | Purpose |
|------|-----|---------|
| Spaarke Dev 1 | https://spaarkedev1.crm.dynamics.com/ | Active development |
| Spaarke QA | [TBD] | Quality assurance |
| Spaarke Prod | [TBD] | Production |

### HIPC Environments (Reference)

| Name | URL | Purpose |
|------|-----|---------|
| HIPC Dev 2 | https://hipc-dev-2.crm.dynamics.com/ | Reference environment |

---

## Configuring PCF Controls on Quick Create Forms

### Required Field for PCF Control Binding

**Question:** What Table Column should be set when configuring a PCF control on a Quick Create form?

**Answer:** For the UniversalQuickCreate control (and most PCF controls), you need to bind the control to a **text field**.

### For UniversalQuickCreate Control

**Bound Field:** `sprk_fileuploadmetadata` (or create a new Single Line Text field)

**Field Configuration:**
- **Schema Name:** sprk_fileuploadmetadata
- **Display Name:** File Upload Metadata (or any descriptive name)
- **Data Type:** Single Line of Text
- **Purpose:** PCF control binding requirement (value not actually used)

### Steps to Configure on Quick Create Form

1. **Add Field to Form:**
   - Open Quick Create form in form designer
   - From **Table columns** pane (left), drag `sprk_fileuploadmetadata` onto form
   - Field will appear as standard text input

2. **Add PCF Control:**
   - Click the `sprk_fileuploadmetadata` field on form
   - In properties pane → **Components** tab
   - Click **+ Component**
   - Search: "Universal Quick Create"
   - Click **Add**

3. **Configure Control Properties:**
   - **SDAP API Base URL:** `https://localhost:7299/api` (or production URL)
   - **Allow Multiple Files:** Yes
   - **Enable File Upload:** Yes

4. **Set Control Visibility:**
   - In Components list, ensure control is selected
   - **Web:** Select "Universal Quick Create" (radio button)
   - This makes the PCF control visible instead of the text input

5. **Optional: Hide Field Label:**
   - Click field → **Display** tab
   - Uncheck **Show label**
   - The PCF control has its own UI, label is redundant

6. **Save and Publish:**
   - Click **Save**
   - Click **Publish**

### Why a Text Field Is Required

**PCF Controls require a field binding:**
- Power Apps forms require controls to be bound to a field
- Quick Create forms only support field-level controls (not dataset controls)
- The field value itself may not be used by the control
- Binding is a technical requirement of the PCF framework

**For UniversalQuickCreate:**
- The `sprk_fileuploadmetadata` field value is NOT used for storage
- Multi-file metadata is stored in individual Document records
- The field exists solely for PCF control binding

### Complete Quick Create Configuration

**See detailed guide:**
- [QUICK-CREATE-CONFIGURATION-GUIDE.md](../dev/projects/sdap_project/Sprint%207B%20Doc%20Quick%20Create/QUICK-CREATE-CONFIGURATION-GUIDE.md)

**Summary Steps:**
1. ✅ Enable Quick Create on Document entity
2. ✅ Create/open Quick Create form
3. ✅ Add `sprk_fileuploadmetadata` field to form
4. ✅ Add UniversalQuickCreate component to field
5. ✅ Configure component properties
6. ✅ Set visibility to Web
7. ✅ Add optional fields (Title, Description, Owner)
8. ✅ Save and publish
9. ✅ Configure Documents subgrid to show "+ New" button

---

## Support and Troubleshooting

**For deployment issues:**
1. Check this troubleshooting guide (see above)
2. Review build output for errors
3. Check browser console for JavaScript errors
4. Verify authentication and permissions
5. Review [Microsoft Power Apps CLI documentation](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction)

**For Quick Create configuration:**
- See [QUICK-CREATE-CONFIGURATION-GUIDE.md](../dev/projects/sdap_project/Sprint%207B%20Doc%20Quick%20Create/QUICK-CREATE-CONFIGURATION-GUIDE.md)
- Verify solution deployed as Unmanaged
- Ensure control visible in form designer
- Check `sprk_fileuploadmetadata` field exists

**Common Resources:**
- Power Apps CLI GitHub: https://github.com/microsoft/powerplatform-build-tools
- Power Apps Community: https://powerusers.microsoft.com/
- Dataverse Developer Docs: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/

---

## Document History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-10-07 | Initial comprehensive guide |
| 1.1 | 2025-10-07 | Added Quick Create configuration section |
| 2.0 | 2025-10-18 | Major update - Added web resource deployment, package verification, enhanced troubleshooting with real-world error cases, cross-reference to quick guide |
| 2.1 | 2025-10-18 | Added critical 5MB web resource limit section with development vs production build modes, bundle size verification, and troubleshooting |

---

**Document Version:** 2.1
**Author:** Spaarke Development Team
**Last Updated:** 2025-10-18
**Last Verified:** 2025-10-18 (Spaarke Dev 1 - Unmanaged Solution)
**Related:** [KM-PCF-COMPONENT-DEPLOYMENT.md](./KM-PCF-COMPONENT-DEPLOYMENT.md) (Quick Reference Guide)
