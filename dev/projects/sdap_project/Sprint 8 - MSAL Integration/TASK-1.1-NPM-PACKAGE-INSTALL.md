# Task 1.1: Install MSAL.js NPM Package

**Sprint:** 8 - MSAL Integration
**Task:** 1.1 of 5 (Phase 1)
**Duration:** 30 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Install @azure/msal-browser NPM package in Universal Dataset Grid PCF control project.**

**Success Criteria:**
- ✅ `@azure/msal-browser` package installed in `package.json`
- ✅ No installation errors
- ✅ Package version is latest stable (2.x)
- ✅ `npm list @azure/msal-browser` shows package installed

---

## Context

### Why This Task Matters

We're pivoting Sprint 8 from Custom API Proxy (which violates ADR-002) to MSAL.js client-side authentication. This task installs the foundational MSAL.js library that will enable the PCF control to acquire user tokens via SSO silent flow.

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ MSAL.js runs **client-side** in PCF control (no plugins)
- ✅ No HTTP calls from Dataverse plugins
- ✅ No long-running plugin operations

**ADR-006 (Prefer PCF):**
- ✅ Using PCF control (TypeScript/React) for UI authentication
- ✅ Modern UI patterns with MSAL.js

**ADR-007 (Storage Seam Minimalism):**
- ✅ MSAL.js will call existing `SpeFileStore` facade via OBO endpoints
- ✅ No new abstractions needed

### Sprint 4 Integration

Sprint 4 delivered dual authentication (Managed Identity + OBO) in `Spe.Bff.Api`:
- ✅ `TokenHelper.ExtractBearerToken()` expects `Authorization: Bearer <token>` header
- ✅ OBO endpoints ready at `/api/obo/*`
- ✅ `SpeFileStore` has `*AsUserAsync()` methods ready

**What's missing:** PCF control needs to acquire user token to send in Authorization header.

**This task:** First step to solve that - install MSAL.js library.

---

## Prerequisites

**Before starting:**
- ✅ Node.js installed (v18+)
- ✅ NPM installed (v9+)
- ✅ Git bash or PowerShell terminal
- ✅ Access to `c:\code_files\spaarke\` repository

**Files/Folders:**
- Location: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/`
- Existing: `package.json`, `node_modules/` (if dependencies already installed)

---

## Step-by-Step Instructions

### Step 1: Navigate to PCF Project Directory

**Command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
```

**Expected:** Terminal prompt shows:
```
c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid>
```

---

### Step 2: Verify Current Package.json

**Command:**
```bash
cat package.json
```

**Expected Output:** Should see existing dependencies:
```json
{
  "name": "UniversalDatasetGrid",
  "version": "1.0.0",
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    // ... other dependencies
  }
}
```

**Verify:** `@azure/msal-browser` is **NOT** in dependencies (it shouldn't be yet).

---

### Step 3: Install @azure/msal-browser

**Command:**
```bash
npm install @azure/msal-browser --save
```

**Expected Output:**
```
added 1 package, and audited 123 packages in 3s

14 packages are looking for funding
  run `npm fund` for details

found 0 vulnerabilities
```

**What this does:**
- Downloads `@azure/msal-browser` from npm registry
- Adds to `node_modules/@azure/msal-browser/`
- Updates `package.json` dependencies section
- Updates `package-lock.json` with exact version

---

### Step 4: Verify Installation

**Command:**
```bash
npm list @azure/msal-browser
```

**Expected Output:**
```
UniversalDatasetGrid@1.0.0 c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
└── @azure/msal-browser@2.38.4
```

**Version:** Should be 2.38.x or newer (2.x is latest stable branch).

---

### Step 5: Check Updated Package.json

**Command:**
```bash
cat package.json | grep -A 1 "@azure/msal-browser"
```

**Expected Output:**
```json
    "@azure/msal-browser": "^2.38.4",
```

**Verify:**
- ✅ Package listed in `dependencies` section (not `devDependencies`)
- ✅ Version starts with `^2.` (caret means "compatible with 2.x")

---

### Step 6: Test Build (Ensure No Errors)

**Command:**
```bash
npm run build
```

**Expected Output:**
```
> UniversalDatasetGrid@1.0.0 build
> pcf-scripts build

Build started...
[pcf-scripts] Compiling TypeScript...
[pcf-scripts] Bundling...
Build completed successfully.
```

**Verify:**
- ✅ No errors related to MSAL.js
- ✅ Build completes successfully
- ✅ `out/` directory created with bundle

**Note:** We haven't imported MSAL yet, so it won't be in the bundle. This just ensures the package doesn't break existing build.

---

## Verification Checklist

**Task 1.1 complete when:**

- ✅ `npm install @azure/msal-browser --save` executed successfully
- ✅ `npm list @azure/msal-browser` shows package installed with version 2.38.x+
- ✅ `package.json` includes `"@azure/msal-browser": "^2.38.4"` in dependencies
- ✅ `node_modules/@azure/msal-browser/` directory exists
- ✅ `npm run build` completes without errors

**Quick verification command:**
```bash
# All-in-one check
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
npm list @azure/msal-browser && \
npm run build
```

**Expected:** Package listed, build succeeds.

---

## Troubleshooting

### Issue 1: "npm: command not found"

**Cause:** Node.js/NPM not installed or not in PATH.

**Fix:**
```bash
# Check if Node.js installed
node --version  # Should show v18.x or higher

# Check if NPM installed
npm --version   # Should show v9.x or higher

# If not installed:
# Download from https://nodejs.org/ and install
```

---

### Issue 2: "EACCES: permission denied"

**Cause:** NPM trying to write to protected directory.

**Fix (Windows):**
```bash
# Run terminal as Administrator
# OR change npm global directory:
npm config set prefix "C:\Users\<YourUser>\npm-global"
```

---

### Issue 3: "ERR! code ENOTFOUND registry.npmjs.org"

**Cause:** Network issue, firewall blocking NPM registry.

**Fix:**
```bash
# Check internet connection
ping registry.npmjs.org

# If behind corporate proxy:
npm config set proxy http://proxy.company.com:8080
npm config set https-proxy http://proxy.company.com:8080

# Retry installation
npm install @azure/msal-browser --save
```

---

### Issue 4: "No matching version found for @azure/msal-browser"

**Cause:** Package name typo or NPM cache issue.

**Fix:**
```bash
# Clear NPM cache
npm cache clean --force

# Retry with explicit version
npm install @azure/msal-browser@2.38.4 --save
```

---

### Issue 5: "npm WARN deprecated" warnings

**Cause:** Some transitive dependencies of MSAL may have deprecation warnings.

**Expected behavior:** This is **normal** and safe to ignore. MSAL.js v2 is actively maintained.

**Example warning:**
```
npm WARN deprecated <some-package>: This package is deprecated
```

**Action:** ✅ Ignore warnings, proceed to verification.

---

## Next Steps

**After Task 1.1 completion:**

✅ **Task 1.1 Complete** - MSAL.js package installed

➡️ **Task 1.2: Create Directory Structure and TypeScript Interfaces**
- Create `services/auth/` directory
- Create `types/auth.ts` with TypeScript interfaces
- Define `IAuthProvider`, `AuthToken`, `AuthError` types

**See:** `TASK-1.2-CREATE-DIRECTORY-STRUCTURE.md`

---

## Files Modified

**Modified:**
- `package.json` - Added `@azure/msal-browser` to dependencies
- `package-lock.json` - Added exact version lockfile entry

**Created:**
- `node_modules/@azure/msal-browser/` - Package files

---

## Related Documentation

- [MSAL.js Documentation](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-js-overview)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)
- [Sprint 8 Pivot Document](./SPRINT-8-PIVOT-TO-MSAL.md)
- [ADR-002: No Heavy Plugins](../../../docs/adr/ADR-002-no-heavy-plugins.md)
- [ADR-006: Prefer PCF](../../../docs/adr/ADR-006-prefer-pcf-over-webresources.md)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 1.2 - Create Directory Structure
**Estimated Duration:** 30 minutes

---
