# Work Item 8: Build and Deploy Solution

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 2 hours
**Prerequisites:** Work Items 1-6 completed (code implementation)
**Status:** Ready to Start

---

## Objective

Build the PCF control, package it into a managed solution, and deploy to target Dataverse environment. Verify successful import and control registration.

---

## Context

PCF controls must be built, packaged into a solution, and imported into Dataverse. The build process:
1. Transpiles TypeScript → JavaScript
2. Bundles React and dependencies
3. Generates control manifest
4. Packages into solution ZIP
5. Imports into environment

**Result:** Control available in "Add Control" list in form designer.

---

## Implementation Steps

### Step 1: Clean Previous Build

Remove old build artifacts:

```bash
# Navigate to control directory
cd src/controls/UniversalQuickCreate/UniversalQuickCreate

# Clean
npm run clean

# Verify out/ folder removed
# (If clean script doesn't exist, manually delete out/ folder)
```

**Why:** Ensure fresh build without stale artifacts.

---

### Step 2: Install Dependencies

Ensure all npm packages installed:

```bash
npm install
```

**Expected output:** Dependencies installed without errors.

**Common warnings to ignore:**
- Peer dependency warnings (React versions)
- Optional dependency warnings
- Deprecated package warnings (non-critical)

---

### Step 3: Build PCF Control

Build the control:

```bash
npm run build
```

**This executes:**
1. TypeScript compilation (`tsc`)
2. Webpack bundling (React + dependencies → `bundle.js`)
3. Manifest generation
4. CSS bundling

**Expected output:**
```
> pcf-scripts build

Building...
Generated: out/controls/UniversalQuickCreate/bundle.js
Generated: out/controls/UniversalQuickCreate/ControlManifest.xml
Build completed successfully
```

**Verification:**
```bash
ls out/controls/UniversalQuickCreate/
# Should see: bundle.js, ControlManifest.xml, css/
```

---

### Step 4: Navigate to Solution Directory

```bash
cd ../../../../solutions/SpaarkeControls
```

**Directory structure:**
```
solutions/
└── SpaarkeControls/
    ├── src/
    ├── Other/
    ├── ControlDescriptions.xml
    └── Solution.xml
```

**If solution doesn't exist**, create it:

```bash
# Create solution (one-time setup)
pac solution init --publisher-name Spaarke --publisher-prefix sprk
```

---

### Step 5: Add Control Reference to Solution

If first time, add control to solution:

```bash
pac solution add-reference --path ../../src/controls/UniversalQuickCreate
```

**This creates reference** so solution knows to include the control.

**Verification:**
Check `SpaarkeControls.cdsproj` file contains:
```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\controls\UniversalQuickCreate\UniversalQuickCreate.pcfproj" />
</ItemGroup>
```

---

### Step 6: Restore NuGet Packages

Required for MSBuild:

```bash
# Windows (PowerShell/CMD)
dotnet restore

# Git Bash
dotnet restore
```

**Expected output:**
```
Restore completed in X ms
```

---

### Step 7: Build Solution

**Important:** Temporarily disable `Directory.Packages.props` if it exists:

```bash
# Check if exists
if [ -f "Directory.Packages.props" ]; then mv "Directory.Packages.props" "Directory.Packages.props.disabled"; fi
```

Now build:

```bash
dotnet build --configuration Release
```

**OR using MSBuild directly:**

```bash
# Find MSBuild path
"C:/Program Files/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/MSBuild.exe" /t:build /restore /p:Configuration=Release
```

**Expected output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Re-enable Directory.Packages.props:**

```bash
if [ -f "Directory.Packages.props.disabled" ]; then mv "Directory.Packages.props.disabled" "Directory.Packages.props"; fi
```

---

### Step 8: Package Solution

Create solution ZIP file:

```bash
pac solution pack --zipfile SpaarkeControls.zip --packagetype Managed
```

**Parameters:**
- `--zipfile`: Output file name
- `--packagetype`: Managed (recommended) or Unmanaged

**Expected output:**
```
Solution package created: SpaarkeControls.zip
```

**Verification:**
```bash
ls -lh SpaarkeControls.zip
# Should see file with size ~500KB - 2MB
```

---

### Step 9: Authenticate to Environment

Authenticate PAC CLI to target environment:

```bash
pac auth create --url https://your-org.crm.dynamics.com
```

**This opens browser** for authentication.

**Verify authentication:**
```bash
pac auth list
# Should show active connection with * indicator
```

**Alternative:** Use environment URL from Power Platform Admin Center.

---

### Step 10: Import Solution

Import to Dataverse:

```bash
pac solution import --path SpaarkeControls.zip --async
```

**Parameters:**
- `--path`: Solution ZIP file
- `--async`: Import asynchronously (recommended for large solutions)

**Expected output:**
```
Solution import initiated: [Import Job ID]
```

**Monitor import progress:**

```bash
pac solution list
# Check for "SpaarkeControls" in list
```

**Alternative:** Monitor in Power Apps maker portal:
1. Go to **Solutions**
2. Check for import in progress
3. Wait for "Installed" status

---

### Step 11: Verify Control Registration

After import completes:

1. Go to [Power Apps Maker Portal](https://make.powerapps.com)
2. Navigate to **Solutions** → **SpaarkeControls**
3. Expand **Controls** section
4. Verify **UniversalQuickCreate** appears

**OR test in form designer:**

1. Open any entity form
2. Add a text field
3. Click field → **Controls** → **+ Add control**
4. Search for "Universal Quick Create"
5. Should appear in list

**Verification:** Control appears and can be added to forms.

---

## Build Troubleshooting

### Error: "npm command not found"
**Cause:** Node.js not installed
**Fix:** Install Node.js 18+ from nodejs.org

### Error: "tsc: command not found"
**Cause:** TypeScript not installed
**Fix:** `npm install -g typescript`

### Error: Webpack errors about React
**Cause:** Incompatible React versions
**Fix:** Check package.json has `react@18.2.0` and `react-dom@18.2.0`

### Error: "MSBuild not found"
**Cause:** Visual Studio not installed
**Fix:** Install Visual Studio 2022 with .NET workload

### Error: Import fails with "control already exists"
**Cause:** Control registered from previous import
**Fix:** Delete old solution, re-import, or increment version number

---

## Solution Management

### Version Increment

Before each deployment, update version in `Solution.xml`:

```xml
<Version>1.0.1</Version>
```

Increment:
- Major (1.x.x): Breaking changes
- Minor (x.1.x): New features
- Patch (x.x.1): Bug fixes

### Managed vs Unmanaged

**Managed (Production):**
- Cannot be modified in target environment
- Can be cleanly uninstalled
- Recommended for production

**Unmanaged (Development):**
- Can be modified in target environment
- Used for active development
- Not recommended for production

---

## Deployment Checklist

- [ ] Previous build cleaned
- [ ] Dependencies installed (`npm install`)
- [ ] Control builds successfully (`npm run build`)
- [ ] out/ folder contains bundle.js
- [ ] Solution restored (`dotnet restore`)
- [ ] Directory.Packages.props disabled (if needed)
- [ ] Solution builds successfully (`dotnet build`)
- [ ] Directory.Packages.props re-enabled (if needed)
- [ ] Solution packed (`pac solution pack`)
- [ ] ZIP file created (~500KB - 2MB)
- [ ] Authenticated to environment
- [ ] Solution imported successfully
- [ ] Control appears in Solutions → Controls
- [ ] Control available in form designer
- [ ] Test control on Quick Create form

---

## CI/CD Pipeline (Future Enhancement)

Automate builds with GitHub Actions:

```yaml
name: Build and Deploy PCF

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '18'
      - run: npm ci
      - run: npm run build
      - run: dotnet build --configuration Release
      - run: pac solution pack
      - run: pac solution import --path SpaarkeControls.zip
        env:
          PAC_AUTH_TOKEN: ${{ secrets.DATAVERSE_AUTH }}
```

---

## Verification Commands

```bash
# 1. Verify build outputs
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/out/controls/UniversalQuickCreate/

# 2. Verify solution ZIP
ls -lh solutions/SpaarkeControls/SpaarkeControls.zip

# 3. Check authentication
pac auth list

# 4. List solutions in environment
pac solution list | grep SpaarkeControls
```

---

## Common Deployment Errors

### "Missing dependencies" error
**Fix:** Run `npm install` and `dotnet restore`

### "Control manifest invalid" error
**Fix:** Verify ControlManifest.Input.xml syntax

### "Import failed - solution already exists"
**Fix:** Delete existing solution or use `pac solution upgrade`

### "Access denied" error
**Fix:** Verify user has System Customizer role

---

## Post-Deployment Steps

After successful import:

1. **Proceed to Work Item 7** - Configure Quick Create form
2. **Add control to form** - Bind to sprk_fileuploadmetadata field
3. **Test in browser** - Open Quick Create, verify control loads
4. **Monitor console** - Check for JavaScript errors
5. **Test file upload** - Select files, verify upload works

---

**Status:** Ready for implementation
**Time:** 2 hours
**Prerequisites:** Code complete (Work Items 1-6)
**Next:** Work Item 7 - Configure Quick Create Form
