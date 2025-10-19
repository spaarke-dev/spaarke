# KM-PCF-COMPONENT-DEPLOYMENT

**Knowledge Management Article**
**Category:** Power Apps Component Framework (PCF)
**Tags:** PCF, Dataverse, Deployment, Power Apps CLI
**Last Updated:** 2025-10-18
**Purpose:** Quick reference for deploying PCF controls with all required components to Dataverse

---

## Overview

This guide covers deploying **Power Apps Component Framework (PCF) controls** to Dataverse environments. A complete PCF deployment includes all required components:

- **bundle.js** - Compiled TypeScript/JavaScript control code
- **ControlManifest.xml** - Control metadata and property definitions
- **Web resources** - CSS files, images, additional JavaScript files
- **Custom pages** - Canvas pages for dialogs (if applicable)
- **Solution metadata** - Version, dependencies, publisher info

---

## Required Components Checklist

Before deploying, verify these components exist:

### **Core PCF Components (Always Required)**
- [ ] **Source code built:** `out/controls/[ControlName]/bundle.js` exists
- [ ] **Manifest compiled:** `out/controls/[ControlName]/ControlManifest.xml` exists
- [ ] **Build is fresh:** Bundle timestamp ≥ source code timestamp

### **Additional Components (Context-Dependent)**
- [ ] **Web resources:** CSS, images in `out/controls/[ControlName]/` folder
- [ ] **Custom pages:** Canvas page JSON in solution `CustomPages/` folder
- [ ] **Solution metadata:** `[SolutionName]/src/Other/Solution.xml` configured
- [ ] **Project reference:** `.cdsproj` includes `<ProjectReference>` to PCF project

---

## ⚠️ CRITICAL: Dataverse 5MB Web Resource Limit

**Dataverse Hard Limit:** Web resources (including PCF bundle.js files) cannot exceed **5MB (5,242,880 bytes)**

### Development vs Production Builds

PCF controls can be built in two modes with drastically different bundle sizes:

| Build Mode | Bundle Size | Source Maps | Minified | Dataverse Compatible | When to Use |
|------------|-------------|-------------|----------|---------------------|-------------|
| **Development** | 10-25 MB | ✅ Yes (.map files) | ❌ No | ❌ **Exceeds 5MB limit** | Local debugging only |
| **Production** | 0.5-3 MB | ❌ No | ✅ Yes | ✅ **Fits in 5MB** | Dataverse deployment |

### How Production Mode Works

**Configuration in .pcfproj file:**

Both UniversalQuickCreate and UniversalDatasetGrid force production builds:

```xml
<!-- File: [ControlName].pcfproj -->
<!-- Force production builds for deployment to stay under 5MB limit -->
<PropertyGroup>
  <PcfBuildMode>production</PcfBuildMode>
</PropertyGroup>
```

**Effect:**
- `npm run build` → Production bundle (minified, no source maps)
- `dotnet build` → Uses production bundle automatically
- Bundle size: 1-3 MB (well under 5MB limit)

### Verify Bundle Size Before Deployment

**ALWAYS check bundle size before importing:**

```bash
# Check bundle size in human-readable format
ls -lh out/controls/[ControlName]/bundle.js

# Expected output (production):
# -rw-r--r-- 1 user 1.8M Oct 18 bundle.js  ✅ Under 5MB

# Check if under 5MB limit (automated)
BUNDLE_SIZE=$(stat -c%s out/controls/[ControlName]/bundle.js 2>/dev/null || stat -f%z out/controls/[ControlName]/bundle.js)
BUNDLE_MB=$((BUNDLE_SIZE / 1024 / 1024))

if [ $BUNDLE_SIZE -gt 5242880 ]; then
  echo "❌ ERROR: Bundle exceeds 5MB limit ($BUNDLE_MB MB)"
  echo "Cannot deploy to Dataverse - build as production mode"
  exit 1
else
  echo "✅ Bundle size OK: $BUNDLE_MB MB (under 5MB limit)"
fi
```

### Troubleshooting: Bundle Exceeds 5MB

**Symptom:** Bundle.js is 10-25 MB after build

**Cause:** Development mode enabled or `PcfBuildMode` not set to production

**Fix:**

```bash
# 1. Verify .pcfproj has production mode
cat [ControlName].pcfproj | grep -A2 "PcfBuildMode"

# Should show:
# <PropertyGroup>
#   <PcfBuildMode>production</PcfBuildMode>
# </PropertyGroup>

# 2. If missing or wrong, edit .pcfproj and add:
# <PropertyGroup>
#   <PcfBuildMode>production</PcfBuildMode>
# </PropertyGroup>

# 3. Clean and rebuild
npm run clean
npm run build

# 4. Verify new bundle size
ls -lh out/controls/[ControlName]/bundle.js
# Should now be 1-3 MB
```

**If still over 5MB after setting production mode:**

- Remove unused npm dependencies
- Use tree-shaking compatible imports (import specific functions, not entire libraries)
- Lazy load heavy components with React.lazy()
- Run `npm dedupe` to remove duplicate dependencies

### Pre-Deployment Checklist

Before deploying ANY PCF control to Dataverse:

- [ ] Verify `.pcfproj` has `<PcfBuildMode>production</PcfBuildMode>`
- [ ] Run `npm run clean` before building
- [ ] Check bundle size: `ls -lh out/controls/[ControlName]/bundle.js`
- [ ] Verify bundle under 5MB: Should show 1-3 MB, not 10-25 MB
- [ ] Verify no source maps: `ls out/controls/[ControlName]/*.map` should be empty or not exist
- [ ] Check bundle in ZIP: `unzip -l bin/Release/*.zip | grep bundle.js` (solution import only)

---

## Deployment Methods Comparison

| Aspect | Method 1: `pac pcf push` | Method 2: Solution Import |
|--------|--------------------------|---------------------------|
| **Speed** | ⚡ Fast (30-60 sec) | 🐢 Slower (2-5 min) |
| **Use Case** | Quick iterations, testing | Production, complete deployments |
| **Includes** | Control code only | Control + web resources + pages |
| **Solution** | Temporary (PowerAppsToolsTemp_*) | Named, versioned solution |
| **Web Resources** | ❌ No | ✅ Yes |
| **Custom Pages** | ❌ No | ✅ Yes |
| **Best For** | Development cycles | Production releases |

---

## Method 1: Rapid Deploy with `pac pcf push`

**Use When:**
- Iterating on control code (TypeScript/React changes)
- Testing OAuth, API integrations, UI changes
- Need fast feedback loop
- Don't need web resources or custom pages

**Limitations:**
- Does NOT deploy web resources (JavaScript files, CSS, images)
- Does NOT deploy custom pages
- Creates temporary solution name
- Not suitable for production

---

### Pre-Deploy Checklist

```bash
# Verify in correct directory
pwd
# Should show: /c/code_files/spaarke/src/controls/[ControlName]

# Verify built output exists
ls -lh out/controls/[ControlName]/bundle.js
# Should show recent timestamp

# Verify authenticated to Dataverse
pac auth list
# Should show * next to target environment
```

---

### Build and Deploy Steps

```bash
# 1. Navigate to control directory
cd src/controls/[ControlName]

# EXAMPLE: For UniversalQuickCreate
# cd src/controls/UniversalQuickCreate

# 2. Clean previous build (recommended)
npm run clean

# 3. Build control
npm run build

# Expected output:
# [build] Initializing...
# [build] Validating manifest...
# [build] Compiling and bundling control...
# webpack compiled successfully
# [build] Succeeded

# 4. Verify build output
ls -lh out/controls/[ControlName]/bundle.js

# 5. Deploy to Dataverse
pac pcf push --publisher-prefix [PublisherPrefix]

# EXAMPLE: For Spaarke controls
# pac pcf push --publisher-prefix sprk

# Expected output:
# Using publisher prefix 'sprk'.
# Building the temporary solution wrapper...
# Build succeeded.
# Importing the temporary solution wrapper...
# Solution Imported successfully.
# Publishing All Customizations...
# Published All Customizations.
# Updating the control in the current org: done.
```

**Duration:** 30-90 seconds

---

### Post-Deploy Verification

```bash
# 1. Verify solution imported
pac solution list | grep -i "[ControlName]"

# EXAMPLE output:
# PowerAppsToolsTemp_sprk  PowerAppsToolsTemp_sprk  1.0  False

# 2. Check in Power Apps Maker Portal
# - Go to: https://make.powerapps.com
# - Select your environment
# - Navigate to: Solutions → PowerAppsToolsTemp_sprk → Controls
# - Verify control appears: [PublisherPrefix]_[Namespace].[ControlName]

# 3. Test on form (if already configured)
# - Open form with control
# - Verify control renders
# - Check browser console (F12) for errors
```

---

### Troubleshooting Method 1

**Issue: "Power Apps component framework project file with extension pcfproj was not found"**

**Cause:** Running command from wrong directory

**Fix:**
```bash
# Verify you're in the control directory (contains .pcfproj file)
ls *.pcfproj
# Should show: [ControlName].pcfproj

# If not found, navigate to correct directory
cd src/controls/[ControlName]
```

---

**Issue: "Build failed - Directory.Packages.props conflicts"**

**Cause:** Central package management conflicts with PCF project

**Fix:**
```bash
# Disable temporarily
mv /c/code_files/spaarke/Directory.Packages.props{,.disabled}

# Run deploy
pac pcf push --publisher-prefix sprk

# Re-enable after
mv /c/code_files/spaarke/Directory.Packages.props{.disabled,}
```

---

**Issue: "Control deploys but doesn't work on form"**

**Cause:** Likely missing web resources (not deployed by `pac pcf push`)

**Fix:** Use Method 2 (Solution Import) instead

---

## Method 2: Complete Solution Deployment

**Use When:**
- Deploying with web resources (additional JS, CSS, images)
- Deploying with custom pages (dialog-based controls)
- Production deployments
- Need versioned, named solution
- Deploying to QA/Prod environments

**What Gets Deployed:**
- ✅ Control code (bundle.js)
- ✅ Control manifest (ControlManifest.xml)
- ✅ Web resources (all files in solution WebResources folder)
- ✅ Custom pages (canvas pages for dialogs)
- ✅ Solution metadata (version, dependencies)

---

### Pre-Deploy Checklist

```bash
# 1. Verify control source exists
ls -la src/controls/[ControlName]/[ControlName]/index.ts

# 2. Verify solution project exists
ls -la src/controls/[ControlName]/[ControlName]Solution/[SolutionName].cdsproj

# 3. Verify solution has ProjectReference to control
grep -i "ProjectReference" src/controls/[ControlName]/[ControlName]Solution/[SolutionName].cdsproj
# MUST show: <ProjectReference Include="..\[ControlName].pcfproj" />

# 4. Verify solution type configuration
grep -i "SolutionPackageType" src/controls/[ControlName]/[ControlName]Solution/[SolutionName].cdsproj
# For DEV: Should show <SolutionPackageType>Unmanaged</SolutionPackageType>
# For PROD: Should show <SolutionPackageType>Managed</SolutionPackageType>

# 5. Check authentication
pac auth list
# Verify * next to target environment
```

---

### Solution Structure

**Required Folder Structure:**
```
src/controls/[ControlName]/
├── [ControlName].pcfproj                    # PCF project file
├── package.json                              # npm dependencies
├── [ControlName]/                            # Source code folder
│   ├── index.ts                             # Control entry point
│   ├── ControlManifest.Input.xml            # Manifest definition
│   ├── components/                          # React/UI components
│   ├── services/                            # Business logic
│   └── css/                                 # Styles
│
├── out/controls/[ControlName]/              # Build output (generated)
│   ├── bundle.js                            # ⚠️ THIS MUST EXIST
│   ├── ControlManifest.xml                  # ⚠️ THIS MUST EXIST
│   └── css/                                 # Compiled styles
│
└── [ControlName]Solution/                   # Solution project
    ├── [SolutionName].cdsproj               # ⚠️ Must reference PCF project
    ├── src/
    │   ├── Other/
    │   │   └── Solution.xml                 # Version, metadata
    │   ├── WebResources/                    # Additional web resources
    │   │   └── [PublisherPrefix]_*.js      # Extra JS files (if any)
    │   └── CustomPages/                     # Canvas pages (if any)
    │       └── [PageName].json
    └── bin/Release/                         # Build output (generated)
        └── [SolutionName].zip               # ⚠️ Final deployment artifact
```

---

### Build Steps

```bash
# 1. Navigate to control directory
cd src/controls/[ControlName]

# EXAMPLE: UniversalQuickCreate
# cd src/controls/UniversalQuickCreate

# 2. Clean and build PCF control
npm run clean
npm run build

# 3. Verify control built successfully
ls -lh out/controls/[ControlName]/bundle.js
# Note the timestamp - will verify this is in solution package

# 4. Navigate to solution directory
cd [ControlName]Solution

# EXAMPLE:
# cd UniversalQuickCreateSolution

# 5. Handle Directory.Packages.props (if exists)
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props" "/c/code_files/spaarke/Directory.Packages.props.disabled"
    echo "Disabled Directory.Packages.props"
fi

# 6. Build solution (Unmanaged for development)
dotnet build --configuration Release

# CRITICAL: Verify output shows correct package type
# Look for this line in output:
#   "Unmanaged Pack complete."  (for dev environments)
#   OR
#   "Managed Pack complete."    (for production)
#
# If wrong type, stop and fix .cdsproj <SolutionPackageType> setting

# 7. Restore Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props.disabled" "/c/code_files/spaarke/Directory.Packages.props"
    echo "Restored Directory.Packages.props"
fi

# 8. Verify solution package created
ls -lh bin/Release/[SolutionName].zip
```

**Expected Build Output:**
```
Building the temporary solution wrapper.
  Determining projects to restore...
  Restored [ControlName].pcfproj

  > npm run build
  [build] Succeeded

  Running Solution Packager to build package type: Unmanaged bin\Release\[SolutionName].zip

  Processing Component: CustomControls
   - [PublisherPrefix].[Namespace].[ControlName]
  Processing Component: WebResources
   - [PublisherPrefix]_bundle.js
   - [PublisherPrefix]_ControlManifest.xml

  Unmanaged Pack complete.

  Solution: bin\Release\[SolutionName].zip generated.
  Build succeeded.
```

---

### ⚠️ CRITICAL: Verify Solution Contents

**THIS STEP PREVENTS 90% OF DEPLOYMENT FAILURES**

Before importing to Dataverse, verify the solution package contains all required components:

```bash
# List all files in solution package
unzip -l bin/Release/[SolutionName].zip

# Verify PCF control components present
unzip -l bin/Release/[SolutionName].zip | grep -E "bundle.js|ControlManifest.xml"

# MUST see output like:
#   WebResources/[PublisherPrefix]_[Namespace].[ControlName]/bundle.js
#   WebResources/[PublisherPrefix]_[Namespace].[ControlName]/ControlManifest.xml
#
# If these are MISSING:
#   - Solution will import successfully
#   - Control will appear in Solutions → Controls
#   - BUT control will NOT work (no code deployed)
```

**If bundle.js or ControlManifest.xml are missing:**

```bash
# 1. Check ProjectReference in .cdsproj
cat [SolutionName].cdsproj | grep ProjectReference
# MUST show: <ProjectReference Include="..\[ControlName].pcfproj" />

# 2. If missing, add it (or use Method 1: pac pcf push instead)

# 3. Rebuild solution
dotnet clean
dotnet build --configuration Release

# 4. Verify again
unzip -l bin/Release/[SolutionName].zip | grep bundle.js
```

---

### Deploy to Dataverse

```bash
# 1. Check for existing solution
pac solution list --environment [EnvironmentUrl] | grep -i "[SolutionName]"

# EXAMPLE:
# pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep UniversalQuickCreate

# Check the last column (Managed):
#   False = Unmanaged solution exists
#   True  = Managed solution exists

# 2. If deploying Unmanaged but Managed exists, DELETE IT FIRST
# (Cannot import Unmanaged over Managed)
pac solution delete \
  --solution-name [SolutionName] \
  --environment [EnvironmentUrl]

# EXAMPLE:
# pac solution delete \
#   --solution-name UniversalQuickCreateSolution \
#   --environment https://spaarkedev1.crm.dynamics.com/

# 3. Import solution
pac solution import \
  --path bin/Release/[SolutionName].zip \
  --environment [EnvironmentUrl] \
  --async

# EXAMPLE:
# pac solution import \
#   --path bin/Release/UniversalQuickCreateSolution.zip \
#   --environment https://spaarkedev1.crm.dynamics.com/ \
#   --async

# Expected output:
# Connected as [user@domain.com]
# Connected to... [ENVIRONMENT NAME]
#
# Solution Importing...
# Waiting for asynchronous operation to complete...
# Processing asynchronous operation... execution time: 00:00:04
# ...
# Asynchronous operation completed successfully within 00:00:24
# Solution Imported successfully.

# 4. Verify import
pac solution list --environment [EnvironmentUrl] | grep -i "[SolutionName]"

# Verify Managed column shows expected value:
#   False = Unmanaged (correct for dev)
#   True  = Managed (correct for prod)
```

**Import Duration:** 30 seconds - 2 minutes depending on solution size

---

### Publish Customizations

**IMPORTANT:** Solution import does NOT automatically publish customizations

**Option A: Via Power Apps Maker Portal (Recommended)**

1. Go to: https://make.powerapps.com
2. Select target environment (top right)
3. Navigate to: **Solutions**
4. Click: **Publish all customizations** (top toolbar)
5. Wait for "Published successfully" message

**Option B: Via PAC CLI**

```bash
pac solution publish --environment [EnvironmentUrl]
```

---

### Post-Deploy Verification

```bash
# 1. Verify solution in environment
pac solution list --environment [EnvironmentUrl] | grep "[SolutionName]"

# Check output:
#   - Version number matches expectation
#   - Managed = False (for dev) or True (for prod)

# 2. Export solution to verify contents (optional but recommended)
pac solution export \
  --name [SolutionName] \
  --path ./verify_deployed.zip \
  --environment [EnvironmentUrl] \
  --overwrite

# 3. Verify bundle.js was deployed
unzip -l verify_deployed.zip | grep bundle.js

# If bundle.js present: ✅ Complete deployment successful
# If bundle.js missing: ❌ Deployment incomplete (see troubleshooting)

# 4. Check in Power Apps Maker Portal
# Navigate to: Solutions → [SolutionName] → Controls
# Verify control appears with correct name

# 5. Test control on form
# - Open form with control configured
# - Open browser console (F12)
# - Verify no JavaScript errors
# - Test control functionality
```

---

### Troubleshooting Method 2

**Issue: "Solution is already installed as managed solution and package is attempting to install in unmanaged mode"**

**Cause:** Trying to import Unmanaged solution when Managed solution already exists

**Fix:**
```bash
# Delete existing Managed solution first
pac solution delete \
  --solution-name [SolutionName] \
  --environment [EnvironmentUrl]

# Then retry import
pac solution import --path bin/Release/[SolutionName].zip --environment [EnvironmentUrl] --async
```

---

**Issue: "Solution imports successfully but control doesn't work"**

**Cause:** Web resources (bundle.js, manifest) not included in solution package

**Symptoms:**
- Control appears in Solutions → Controls
- Control not available in form designer, OR
- Control available but shows blank/error when added to form

**Diagnosis:**
```bash
# Export the deployed solution
pac solution export --name [SolutionName] --path ./check.zip --environment [EnvironmentUrl] --overwrite

# Check if bundle.js is present
unzip -l check.zip | grep bundle.js

# If NO output: Web resources missing from deployment
```

**Fix:**
```bash
# 1. Verify ProjectReference in .cdsproj
cat [SolutionName].cdsproj | grep ProjectReference
# MUST include: <ProjectReference Include="..\[ControlName].pcfproj" />

# 2. If missing, add it:
# Edit [SolutionName].cdsproj
# Add this inside <Project> tag:
#   <ItemGroup>
#     <ProjectReference Include="..\[ControlName].pcfproj" />
#   </ItemGroup>

# 3. Rebuild solution
dotnet clean
dotnet build --configuration Release

# 4. Verify bundle.js in package BEFORE deploying
unzip -l bin/Release/[SolutionName].zip | grep bundle.js

# 5. If still missing: Use Method 1 (pac pcf push) instead
```

---

**Issue: "Build shows 'Managed Pack complete' but I want Unmanaged"**

**Cause:** `.cdsproj` file configured for Managed solution type

**Fix:**
```bash
# 1. Edit .cdsproj file
# Find or add this section:
#   <PropertyGroup>
#     <SolutionPackageType>Unmanaged</SolutionPackageType>
#   </PropertyGroup>

# 2. If section is commented out, uncomment it:
# Before:
#   <!--
#   <PropertyGroup>
#     <SolutionPackageType>Managed</SolutionPackageType>
#   </PropertyGroup>
#   -->
#
# After:
#   <PropertyGroup>
#     <SolutionPackageType>Unmanaged</SolutionPackageType>
#   </PropertyGroup>

# 3. Rebuild
dotnet clean
dotnet build --configuration Release

# 4. Verify output shows "Unmanaged Pack complete"
```

---

**Issue: "Control not appearing in form designer after publish"**

**Cause:** Browser cache or customizations not published

**Fix:**
```bash
# 1. Publish customizations (if not done)
# Via portal: Solutions → Publish all customizations

# 2. Clear browser cache
# - Hard refresh: Ctrl + Shift + R (Windows) or Cmd + Shift + R (Mac)
# - Or clear browser cache completely

# 3. Sign out and sign back in to Power Apps

# 4. Verify control namespace matches
# In form designer, search for: [PublisherPrefix]_[Namespace].[ControlName]
```

---

## Complete Deployment Workflow (Copy-Paste Template)

**Use this for any PCF control deployment:**

```bash
#!/bin/bash
# PCF Control Deployment Script
# Customize the variables below for your control

# ============================================
# CONFIGURATION (Customize these)
# ============================================
CONTROL_NAME="YourControlName"              # e.g., UniversalQuickCreate
SOLUTION_NAME="YourSolutionName"            # e.g., UniversalQuickCreateSolution
PUBLISHER_PREFIX="sprk"                     # Your publisher prefix
ENVIRONMENT_URL="https://spaarkedev1.crm.dynamics.com/"  # Target Dataverse environment
SOLUTION_TYPE="Unmanaged"                   # Unmanaged for dev, Managed for prod

# ============================================
# PRE-DEPLOYMENT VERIFICATION
# ============================================
echo "=== Pre-Deployment Verification ==="

# Check authentication
echo "Checking authentication..."
pac auth list | grep -q "*" || { echo "ERROR: Not authenticated to Dataverse"; exit 1; }

# Check source exists
echo "Checking source files..."
[ -f "src/controls/${CONTROL_NAME}/${CONTROL_NAME}/index.ts" ] || { echo "ERROR: Control source not found"; exit 1; }

# ============================================
# BUILD PCF CONTROL
# ============================================
echo ""
echo "=== Building PCF Control ==="
cd "src/controls/${CONTROL_NAME}" || exit 1

npm run clean
npm run build

# Verify build output
[ -f "out/controls/${CONTROL_NAME}/bundle.js" ] || { echo "ERROR: Build failed - bundle.js not found"; exit 1; }
echo "✅ PCF control built successfully"

# ============================================
# BUILD SOLUTION
# ============================================
echo ""
echo "=== Building Solution ==="
cd "${CONTROL_NAME}Solution" || exit 1

# Disable Directory.Packages.props if exists
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props" "/c/code_files/spaarke/Directory.Packages.props.disabled"
fi

# Build solution
dotnet build --configuration Release

# Restore Directory.Packages.props
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then
    mv "/c/code_files/spaarke/Directory.Packages.props.disabled" "/c/code_files/spaarke/Directory.Packages.props"
fi

# Verify solution package
[ -f "bin/Release/${SOLUTION_NAME}.zip" ] || { echo "ERROR: Solution build failed"; exit 1; }
echo "✅ Solution package created"

# ============================================
# VERIFY PACKAGE CONTENTS (CRITICAL)
# ============================================
echo ""
echo "=== Verifying Package Contents ==="
BUNDLE_CHECK=$(unzip -l "bin/Release/${SOLUTION_NAME}.zip" | grep -c "bundle.js")

if [ "$BUNDLE_CHECK" -eq 0 ]; then
    echo "❌ ERROR: bundle.js NOT found in solution package"
    echo "Solution will import but control will not work"
    echo "Check ProjectReference in ${SOLUTION_NAME}.cdsproj"
    exit 1
fi

echo "✅ bundle.js found in package"

# ============================================
# DEPLOY TO DATAVERSE
# ============================================
echo ""
echo "=== Deploying to Dataverse ==="

# Check for existing solution
EXISTING=$(pac solution list --environment "${ENVIRONMENT_URL}" | grep -i "${SOLUTION_NAME}")

if [ -n "$EXISTING" ]; then
    echo "Solution already exists:"
    echo "$EXISTING"

    # Check if Managed when we want Unmanaged (or vice versa)
    if [[ "$EXISTING" =~ "True" ]] && [ "$SOLUTION_TYPE" == "Unmanaged" ]; then
        echo "⚠️  Managed solution exists but deploying Unmanaged - deleting existing"
        pac solution delete --solution-name "${SOLUTION_NAME}" --environment "${ENVIRONMENT_URL}"
    fi
fi

# Import solution
echo "Importing solution..."
pac solution import \
    --path "bin/Release/${SOLUTION_NAME}.zip" \
    --environment "${ENVIRONMENT_URL}" \
    --async

# ============================================
# POST-DEPLOYMENT VERIFICATION
# ============================================
echo ""
echo "=== Post-Deployment Verification ==="

# Verify import
DEPLOYED=$(pac solution list --environment "${ENVIRONMENT_URL}" | grep -i "${SOLUTION_NAME}")

if [ -n "$DEPLOYED" ]; then
    echo "✅ Solution deployed successfully:"
    echo "$DEPLOYED"
else
    echo "❌ ERROR: Solution not found in environment after import"
    exit 1
fi

echo ""
echo "=== Deployment Complete ==="
echo "Next steps:"
echo "1. Publish customizations in Power Apps maker portal"
echo "2. Verify control appears in Solutions → ${SOLUTION_NAME} → Controls"
echo "3. Test control on form"
```

---

## Quick Reference Commands

### Authentication
```bash
pac auth list                          # Show auth profiles
pac auth create --url [EnvironmentUrl] # Authenticate
pac auth select --index 2              # Switch profile
```

### Build
```bash
npm run clean                          # Clean PCF build
npm run build                          # Build PCF control
dotnet build --configuration Release   # Build solution
```

### Verify
```bash
ls -lh out/controls/*/bundle.js                    # Check PCF build
ls -lh */bin/Release/*.zip                         # Check solution package
unzip -l bin/Release/*.zip | grep bundle.js        # Verify contents
pac auth list | grep "*"                           # Check auth
```

### Deploy
```bash
pac pcf push --publisher-prefix sprk               # Method 1: Rapid
pac solution import --path [zip] --environment [url] --async  # Method 2: Complete
pac solution delete --solution-name [name]         # Delete existing
pac solution list --environment [url]              # List solutions
```

---

## Environment-Specific Examples

### Spaarke Dev 1 (Development)
```bash
ENVIRONMENT_URL="https://spaarkedev1.crm.dynamics.com/"
PUBLISHER_PREFIX="sprk"
SOLUTION_TYPE="Unmanaged"
```

### Example Controls in Spaarke Project
```bash
# UniversalQuickCreate - File upload dialog control
CONTROL_NAME="UniversalQuickCreate"
SOLUTION_NAME="UniversalQuickCreateSolution"

# UniversalDatasetGrid - Grid control with MSAL auth
CONTROL_NAME="UniversalDatasetGrid"
SOLUTION_NAME="UniversalDatasetGridSolution"
```

---

## Related Documentation

- **Comprehensive Guide:** [KM-DATAVERSE-SOLUTION-DEPLOYMENT-FULL-GUIDE.md](./KM-DATAVERSE-SOLUTION-DEPLOYMENT-FULL-GUIDE.md)
- **File Verification:** [VERIFICATION-CHECKLIST.md](../VERIFICATION-CHECKLIST.md)
- **Microsoft Docs:** [PCF Overview](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/overview)

---

## Document History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-10-18 | Initial quick reference guide |
| 1.1 | 2025-10-18 | Added critical 5MB web resource limit section, production build mode configuration, bundle size verification |

---

**Document Version:** 1.1
**Created:** 2025-10-18
**Last Updated:** 2025-10-18
**Last Verified:** 2025-10-18 (Spaarke Dev 1)
**Verified Controls:** UniversalQuickCreate, UniversalDatasetGrid
**Related:** [KM-DATAVERSE-SOLUTION-DEPLOYMENT-FULL-GUIDE.md](./KM-DATAVERSE-SOLUTION-DEPLOYMENT-FULL-GUIDE.md) (Comprehensive Guide)
