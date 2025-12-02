# Sprint 7A - Task 2: Build and Bundle Verification

**Task:** Build and Bundle Verification
**Status:** 📋 Ready to Execute
**Estimated Time:** 30 minutes
**Prerequisites:** Task 1 completed

---

## Goal

Ensure MSAL dependencies are correctly bundled and control builds without errors.

## Success Criteria

- [ ] Build completes with 0 errors, 0 warnings
- [ ] MSAL package (@azure/msal-browser) included in bundle
- [ ] Bundle size within acceptable limits (<600 KiB for dev build)

---

## Step 2.1: Verify MSAL Package Installation

### Action

```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Check MSAL package is installed
npm list @azure/msal-browser
```

### Expected Output

```
pcf-project@1.0.0
└── @azure/msal-browser@4.24.1
```

### Success Criteria

- [x] @azure/msal-browser version 4.24.1 installed
- [ ] No package resolution errors
- [ ] No peer dependency warnings

### If Package is Missing

```bash
# Install MSAL package
npm install @azure/msal-browser@4.24.1

# Verify installation
npm list @azure/msal-browser
```

---

## Step 2.2: Clean Build

### Action

```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Clean previous build artifacts
npm run clean

# Rebuild control (development build)
npm run build
```

### Expected Output

```
> webpack --mode development

webpack 5.x.x compiled successfully in X ms

Build complete:
- bundle.js: ~540 KiB
- 0 errors
- 0 warnings (except performance hints - acceptable)
```

### Success Criteria

- [ ] Build completes without errors
- [ ] Build completes without critical warnings
- [ ] Bundle.js created in `out/controls/UniversalDatasetGrid/`
- [ ] Bundle size between 500-600 KiB (development build)

### Performance Warning (Expected and OK)

You may see this warning - **this is acceptable**:
```
WARNING in asset size limit: The following asset(s) exceed the recommended size limit (244 KiB).
```

This is expected for a complex PCF control with Fluent UI v9. The production build will be much smaller.

---

## Step 2.3: TypeScript Compilation Check

### Action

```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Check TypeScript compilation separately
npx tsc --noEmit
```

### Expected Output

```
(No output - TypeScript compiled successfully)
```

### Success Criteria

- [ ] No TypeScript errors
- [ ] No type mismatches
- [ ] All MSAL types resolved correctly

### If TypeScript Errors Occur

Common issues and fixes:

**Issue:** Cannot find module '@azure/msal-browser'
```bash
# Reinstall dependencies
npm install
```

**Issue:** Type errors in auth files
- Check `services/auth/MsalAuthProvider.ts` for type imports
- Verify `types/auth.ts` exists and is correct

---

## Step 2.4: Verify Bundle Contents

### Action

```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Check if MSAL is included in bundle
grep -i "PublicClientApplication" out/controls/UniversalDatasetGrid/bundle.js | head -3

# Check for ssoSilent method
grep -i "ssoSilent" out/controls/UniversalDatasetGrid/bundle.js | head -3
```

### Expected Output

Should find references to:
- `PublicClientApplication` class
- `ssoSilent` method
- MSAL configuration objects

### Success Criteria

- [ ] MSAL code is present in bundle
- [ ] PublicClientApplication class is included
- [ ] ssoSilent method is included
- [ ] No MSAL import errors in bundle

---

## Step 2.5: Bundle Size Analysis

### Check Bundle Size

```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Check bundle size (development build)
ls -lh out/controls/UniversalDatasetGrid/bundle.js
```

### Expected Size Ranges

| Build Type | Expected Size | Max Size | Status |
|------------|---------------|----------|--------|
| Development | 500-600 KiB | 1 MiB | Current build |
| Production | 200-300 KiB | 500 KiB | Not tested yet |

### Bundle Composition (Estimated)

Development build (~540 KiB):
- React + ReactDOM: ~150 KiB
- Fluent UI v9: ~250 KiB
- MSAL Browser: ~80 KiB
- Application Code: ~60 KiB

### Success Criteria

- [ ] Development bundle < 600 KiB
- [ ] Bundle size documented
- [ ] No unexpected size increases

---

## Troubleshooting Guide

### Build Fails with TypeScript Errors

```bash
# Clear node_modules and reinstall
rm -rf node_modules package-lock.json
npm install

# Rebuild
npm run build
```

### Build Fails with Webpack Errors

```bash
# Check webpack configuration
cat webpack.config.js

# Ensure all entry points exist
ls -la index.ts
```

### MSAL Import Errors in Bundle

```bash
# Verify MSAL is in package.json dependencies
cat package.json | grep msal

# Reinstall specific version
npm install @azure/msal-browser@4.24.1 --save
```

### Bundle Size Too Large (>1 MiB)

This shouldn't happen, but if it does:
```bash
# Analyze bundle
npx webpack-bundle-analyzer out/controls/UniversalDatasetGrid/bundle.js
```

---

## Task 2 Completion Checklist

### Build Verification Complete

- [ ] Step 2.1: MSAL package verified (@azure/msal-browser@4.24.1)
- [ ] Step 2.2: Clean build successful (0 errors)
- [ ] Step 2.3: TypeScript compilation passed
- [ ] Step 2.4: Bundle contents verified (MSAL included)
- [ ] Step 2.5: Bundle size documented and acceptable

### Build Metrics

Record your build results:

**Package Version:**
- @azure/msal-browser: ______

**Build Results:**
- Errors: ______
- Warnings: ______ (performance warnings OK)
- Build Time: ______ seconds

**Bundle Size:**
- Development Build: ______ KiB
- Status: ✅ / ⚠️ / ❌

### Issues Found

Document any build issues:
- _________________________
- _________________________

---

## Expected Outcome

✅ **Build should succeed** - MSAL package is already installed from Sprint 8.

The control should build cleanly with MSAL dependencies included in the bundle.

---

## Next Steps

After completing Task 2:
- → **If build passes:** Proceed to [TASK-7A.3-MANUAL-TESTING.md](TASK-7A.3-MANUAL-TESTING.md)
- → **If build fails:** Troubleshoot and resolve issues before continuing

---

## Quick Commands Reference

```bash
# Full build verification sequence
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npm list @azure/msal-browser
npm run clean
npm run build
npx tsc --noEmit
ls -lh out/controls/UniversalDatasetGrid/bundle.js
```

---

**Task Owner:** Sprint 7A MSAL Compliance
**Created:** October 6, 2025
**Estimated Completion:** 30 minutes
**Previous Task:** [TASK-7A.1-CODE-REVIEW.md](TASK-7A.1-CODE-REVIEW.md)
**Next Task:** [TASK-7A.3-MANUAL-TESTING.md](TASK-7A.3-MANUAL-TESTING.md)
