# Task 6.5: Build and Deploy PCF Control

## Task Prompt (For AI Agent)

```
You are working on Task 6.5 of Phase 6: Build and Deploy PCF Control

BEFORE starting work:
1. Read this entire task document carefully
2. Verify Tasks 6.2, 6.3, and 6.4 are ALL complete - all code changes must be done
3. Review current deployment state: check if solution already exists in Dataverse
4. Ensure PAC CLI is installed and authenticated
5. Update status section with current state

DURING work:
1. Execute deployment steps in exact order (Step 1-11)
2. Do NOT skip steps - caching issues require all steps
3. Document actual results vs expected results for each step
4. If any step fails, refer to troubleshooting section before proceeding
5. Take screenshots of version badge after deployment
6. Update deployment checklist as you complete each step

CRITICAL CACHING STEPS:
- Step 7: Import solution with --force-overwrite
- Step 8: Remove and re-add PCF control from form (REQUIRED for cache clear)
- Step 9: Publish all customizations
- Step 10: Hard browser cache clear (Ctrl+Shift+Delete or DevTools)
- Step 11: Verify version shows V2.2.0 (if still V2.1.0, repeat steps 8-10)

AFTER completing work:
1. Verify version badge shows V2.2.0 in browser
2. Verify no JavaScript errors in browser console
3. Complete deployment checklist
4. Take screenshot of successful deployment
5. Fill in task owner, completion date, and status
6. Mark task as Complete and proceed to Task 6.6

Your goal: Successfully deploy v2.2.0 to Dataverse and confirm it's active in browser (not cached v2.1.0).
```

---

## Overview

**Task ID:** 6.5
**Phase:** 6 - PCF Control Document Record Creation Fix
**Duration:** 1 day
**Dependencies:** Tasks 6.2, 6.3, 6.4 (all code changes complete)
**Status:** Ready to Start

---

## Objective

Build the PCF control, deploy to Dataverse, handle caching issues, and verify the new version is active.

---

## Prerequisites

- All code changes from Tasks 6.2-6.4 complete
- PAC CLI installed and configured
- Authenticated to Dataverse org: https://spaarkedev1.crm.dynamics.com
- PowerShell or Bash terminal

---

## Deployment Process

### Step 1: Clean Previous Build

```bash
cd C:\code_files\spaarke\src\controls\UniversalQuickCreate

# Clean previous build artifacts
npm run clean

# Alternative if clean script not available:
rm -rf out
rm -rf UniversalQuickCreateSolution/bin
rm -rf UniversalQuickCreateSolution/obj
```

---

### Step 2: Install Dependencies

```bash
# Ensure all dependencies are installed
npm install

# Verify no vulnerabilities
npm audit
```

---

### Step 3: Build PCF Control

```bash
# Build the control
npm run build

# Expected output:
# - No TypeScript errors
# - No linting warnings
# - out/controls/UniversalQuickCreate folder created with compiled JS
```

**Verify Build Success:**
```bash
# Check output folder exists
ls out/controls/UniversalQuickCreate/

# Should contain:
# - bundle.js
# - ControlManifest.xml
# - Other compiled assets
```

---

### Step 4: Increment Solution Version (Optional but Recommended)

**File:** `UniversalQuickCreateSolution/src/Other/Solution.xml`

Find the `<Version>` element and increment:

```xml
<!-- BEFORE -->
<Version>2.1.0.0</Version>

<!-- AFTER -->
<Version>2.2.0.0</Version>
```

This helps track solution versions in Dataverse.

---

### Step 5: Build Solution Package

```bash
# Build the solution package
msbuild /t:Rebuild /p:Configuration=Release UniversalQuickCreateSolution/UniversalQuickCreateSolution.cdsproj

# Expected output:
# - UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip created
```

**Alternative using dotnet:**
```bash
dotnet msbuild /t:Rebuild /p:Configuration=Release UniversalQuickCreateSolution/UniversalQuickCreateSolution.cdsproj
```

**Verify Solution Package:**
```bash
ls -lh UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip
# Should show file size (typically 50-500KB)
```

---

### Step 6: Authenticate to Dataverse

```bash
# List existing auth profiles
pac auth list

# If not authenticated or need to refresh:
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Select auth profile if multiple exist:
pac auth select --index 1
```

---

### Step 7: Import Solution to Dataverse

#### Option A: Using PAC CLI (Recommended)

```bash
pac solution import \
  --path UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip \
  --force-overwrite \
  --publish-changes \
  --skip-dependency-check
```

**Expected Output:**
```
Importing solution...
Solution import completed successfully.
Publishing changes...
Publish completed.
```

#### Option B: Using Power Platform UI

1. Navigate to https://make.powerapps.com
2. Select environment (spaarkedev1)
3. Solutions → Import solution
4. Browse and select `UniversalQuickCreateSolution.zip`
5. Click Import
6. Wait for import to complete
7. Publish all customizations

---

### Step 8: Handle Dataverse Caching

**Critical:** Dataverse aggressively caches PCF controls. Version increment alone may not force reload.

#### Method 1: Remove and Re-Add Control

1. Open the form where PCF control is used (e.g., Document form, Matter form)
2. Edit form → Remove Universal Quick Create control
3. Save form
4. Publish form
5. Re-open form in edit mode
6. Add Universal Quick Create control back
7. Save and publish form

#### Method 2: Clear Solution Cache (Advanced)

```bash
# Delete the PCF control from solution
pac pcf delete --namespace SpaarkePCF --name UniversalQuickCreate

# Re-import solution
pac solution import \
  --path UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip \
  --force-overwrite \
  --publish-changes
```

---

### Step 9: Publish All Customizations

```bash
# Publish all customizations
pac solution publish --all

# Alternative using Power Platform UI:
# Settings → Solutions → Select solution → Publish all customizations
```

---

### Step 10: Clear Browser Cache

**Critical:** Browser caches PCF control files. Hard refresh required.

#### Chrome/Edge:
1. Open DevTools (F12)
2. Right-click refresh button
3. Select "Empty Cache and Hard Reload"

#### Firefox:
1. Ctrl + Shift + Delete
2. Select "Cached Web Content"
3. Click "Clear Now"

#### Alternative: Incognito/Private Window
```
Open the Custom Page in incognito mode to bypass cache entirely
```

---

### Step 11: Verify Deployment

#### Check Version in Browser

1. Open Custom Page with PCF control
2. Open browser DevTools (F12) → Console
3. Look for version badge or log: `V2.2.0`

#### Check Version in Dataverse

```bash
# Query solution version
pac solution list

# Look for UniversalQuickCreateSolution with version 2.2.0.0
```

#### Check Control Manifest

```bash
# Get control details
pac pcf list

# Verify UniversalQuickCreate shows version 2.2.0
```

---

## Troubleshooting

### Issue: Build Fails with TypeScript Errors

**Solution:**
```bash
# Check TypeScript version
npm list typescript

# Reinstall dependencies
rm -rf node_modules package-lock.json
npm install

# Try build again
npm run build
```

### Issue: msbuild Command Not Found

**Solution:**
```bash
# Use dotnet msbuild instead
dotnet msbuild /t:Rebuild /p:Configuration=Release UniversalQuickCreateSolution/UniversalQuickCreateSolution.cdsproj

# Or use Visual Studio Developer Command Prompt
```

### Issue: Solution Import Fails

**Common Causes:**
- Solution already exists (use --force-overwrite)
- Dependent solution missing (use --skip-dependency-check)
- Authentication expired (run `pac auth create` again)

**Solution:**
```bash
# Force overwrite existing solution
pac solution import \
  --path UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip \
  --force-overwrite \
  --publish-changes \
  --skip-dependency-check \
  --async
```

### Issue: Browser Still Shows Old Version (V2.1.0)

**Solution (Nuclear Option):**
1. Delete browser cache completely
2. Remove PCF control from form
3. Publish form
4. Wait 5 minutes
5. Re-add PCF control to form
6. Publish form
7. Open in incognito window
8. Check version

### Issue: "undeclared property" Error Still Occurs

**This means the code changes didn't deploy correctly. Verify:**
1. Solution import succeeded
2. Customizations published
3. Browser cache cleared
4. Version badge shows V2.2.0
5. Check browser console for JavaScript errors

**If version shows V2.2.0 but error persists:**
- Code changes may not have been built correctly
- Review Task 6.3 changes
- Verify MetadataService.ts exists in out/ folder

---

## Deployment Checklist

- [ ] Previous build cleaned
- [ ] Dependencies installed
- [ ] npm run build successful (no errors)
- [ ] Solution package built (.zip created)
- [ ] Authenticated to Dataverse
- [ ] Solution imported successfully
- [ ] PCF control removed and re-added to form
- [ ] All customizations published
- [ ] Browser cache cleared (hard refresh)
- [ ] Version V2.2.0 visible in browser
- [ ] No JavaScript errors in console

---

## Success Criteria

- [ ] Solution deployed to Dataverse environment
- [ ] Version badge shows "V2.2.0" in browser
- [ ] No build or deployment errors
- [ ] Dataverse caching handled successfully
- [ ] Browser displays updated control
- [ ] Ready for Task 6.6 (testing)

---

## Next Steps

Once deployment is complete and version verified:

1. Proceed to [TASK-6.6-TESTING-VALIDATION.md](./TASK-6.6-TESTING-VALIDATION.md)
2. Execute comprehensive test plan
3. Verify Document record creation succeeds

---

## Reference

See [KM-PCF-COMPONENT-DEPLOYMENT.md](../../../docs/KM-PCF-COMPONENT-DEPLOYMENT.md) for additional deployment guidance and troubleshooting.

---

**Task Owner:** _______________
**Completion Date:** _______________
**Reviewed By:** _______________
**Status:** ⬜ Not Started | ⬜ In Progress | ⬜ Blocked | ⬜ Complete
