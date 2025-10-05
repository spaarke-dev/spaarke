# Bundle Size Reality Check

**Date:** 2025-10-04
**Current Build:** 9.82 MiB (still over 5 MB limit)
**Status:** 🔴 Still blocked for deployment

---

## Current Situation

### Build Results with Selective Imports + Webpack Externals

```
asset bundle.js 9.82 MiB [emitted]
modules by path ./node_modules/@fluentui/ 7.54 MiB 133 modules
modules by path ./node_modules/react/ 45 KiB 2 modules
```

**Outcome:**
- ❌ React NOT externalized (45 KB included)
- ❌ Fluent UI NOT reduced (still 7.54 MiB)
- ❌ Still over 5 MB limit (9.82 MiB)

### What We Tried

1. ✅ Selective imports from `@fluentui/react-components` (converged)
2. ✅ Moved React/Fluent to devDependencies
3. ✅ Created webpack.config.js with externals
4. ✅ Created featureconfig.json
5. ❌ **RESULT: Didn't reduce bundle size**

---

## Why Externals Didn't Work

### The Problem

**Webpack externals require runtime availability:**
```javascript
externals: {
  "react": "React",      // Expects window.React to exist
  "react-dom": "ReactDOM" // Expects window.ReactDOM to exist
}
```

**Reality in Dataverse:**
- Dataverse environment does NOT provide window.React
- Dataverse environment does NOT provide window.ReactDOM
- No global React/Fluent UI available at runtime
- Cannot externalize without runtime provider

### Platform-Library Limitations

From PCF-V9-PACKAGING.md, platform-library approach:
```xml
<platform-library name="React" version="16.14.0" />
<platform-library name="Fluent" version="9.46.2" />
```

**Issue:**
- This makes React/Fluent available from platform
- BUT only works for **supported versions**
- Documentation may be outdated or preview-only
- We couldn't verify if this actually works in our environment

---

## The Fundamental Problem

### Fluent UI v9 is Large

Even with selective imports and tree-shaking:
- **@fluentui/react-components**: ~7.5 MB
- Includes: Components, theming, styling (Griffel), utilities
- Tree-shaking helps but base library is still large

### Why So Large?

1. **Griffel (CSS-in-JS)**: ~500 KB
2. **Component library**: ~5 MB
3. **Theme system**: ~500 KB
4. **Utilities and dependencies**: ~1.5 MB
5. **Icon chunks**: Variable (we're using shared library)

**Total**: ~7.5 MB for Fluent UI v9 even with selective imports

---

## Three Real Options

### Option 1: Vanilla JS (What We Did Temporarily)

**Approach:**
- No React
- No Fluent UI
- Pure TypeScript + inline SVG
- Fluent-inspired styling

**Results:**
- ✅ 28 KB bundle
- ✅ Deployable
- ❌ No Fluent UI v9 compliance
- ❌ No accessibility
- ❌ No theming
- ❌ Violates user directive

**Status:** ❌ REJECTED by user

### Option 2: Library Control (Dependent Library Pattern)

**Approach:**
- Create Spaarke.UI.LibraryControl PCF
- Contains React + Fluent UI (deployed once)
- Universal Grid depends on it
- Other controls depend on it

**Setup:**
```xml
<!-- In Universal Grid manifest -->
<dependency
  type="control"
  name="sprk_Spaarke.UI.LibraryControl"
  order="1"
/>
```

**Results (Expected):**
- Library Control: ~10 MB (deployed once to Dataverse)
- Universal Grid: ~50 KB (just control code)
- Other controls: ~50 KB each

**Benefits:**
- ✅ Fluent UI v9 compliance
- ✅ Each control under 5 MB
- ✅ Shared library loaded once
- ✅ Accessibility + theming

**Drawbacks:**
- ⚠️ More complex deployment
- ⚠️ Need to create Library Control first
- ⚠️ All controls must use same React/Fluent versions

**Status:** 🟡 RECOMMENDED but requires additional work

### Option 3: Increase Dataverse Limit

**Approach:**
- Request admin to increase web resource size limit
- Deploy 9.82 MB bundle as-is

**From PCF-V9-PACKAGING.md:**
> "As a last resort, admins can raise the environment's maximum web resource size (up to higher limits)"

**Benefits:**
- ✅ Simplest short-term solution
- ✅ Keep React + Fluent UI v9
- ✅ Full functionality

**Drawbacks:**
- ❌ Not recommended practice
- ❌ May not be approved
- ❌ Performance impact (large download)
- ❌ Only works in environments where limit can be raised

**Status:** 🟡 POSSIBLE but not preferred

---

## Recommendation

### For Sprint 6: Use Option 2 (Library Control)

**Why:**
1. Only solution that meets all requirements:
   - ✅ Fluent UI v9 compliance
   - ✅ Under 5 MB per control
   - ✅ Reusable across controls
   - ✅ Accessibility + theming
   - ✅ ADR compliant

2. Aligns with long-term architecture:
   - Multiple PCF controls planned (Document Viewer, File Editor)
   - All will need React + Fluent UI
   - Shared library avoids duplication

3. Follows documented pattern:
   - KM-FLUENT-DESIGN-DEPENDENT-LIBRARIES.md shows this approach
   - Supported by PCF framework
   - Proven pattern in Power Platform

### Implementation Steps

**Step 1: Create Spaarke.UI.LibraryControl (2-3 hours)**
1. New PCF project in `src/controls/Spaarke.UI.LibraryControl`
2. Install React + Fluent UI as dependencies (not dev)
3. Export libraries to window global scope
4. Build (~10 MB bundle - acceptable for library)
5. Deploy to Dataverse

**Step 2: Update Universal Grid (1 hour)**
1. Add dependency to manifest
2. Configure webpack externals
3. Update featureconfig.json for dependencies
4. Rebuild (expected: ~50-100 KB)

**Step 3: Test Integration (1 hour)**
1. Deploy Library Control
2. Deploy Universal Grid
3. Verify libraries load correctly
4. Test all functionality

**Total Effort:** 4-5 hours

---

## Alternative: Temporary Workaround

If Library Control cannot be created immediately:

**Option: Deploy with increased limit temporarily**
1. Request admin increase limit for DEV environment
2. Deploy 9.82 MB bundle for Sprint 6 testing
3. Create Library Control in Sprint 7
4. Migrate to dependent library pattern
5. Return to standard 5 MB limit

This allows:
- ✅ Continue Sprint 6 with React + Fluent UI
- ✅ Test SDAP integration
- ✅ Meet user directive for Fluent UI v9
- ⚠️ Technical debt to address in Sprint 7

---

## Conclusion

**Current State:**
- 9.82 MiB bundle with React + Fluent UI v9
- Selective imports don't reduce size enough
- Webpack externals don't work without runtime provider
- **Cannot deploy to Dataverse with 5 MB limit**

**Recommended Path:**
1. Create Library Control (Dependent Library pattern)
2. Move React + Fluent UI to shared library
3. Universal Grid depends on library
4. Achieve ~50 KB bundle per control

**Alternative Path (if needed for Sprint 6):**
1. Temporarily increase Dataverse limit
2. Deploy current 9.82 MB bundle
3. Create Library Control in Sprint 7
4. Migrate and return to standard limits

**User Decision Needed:**
- Proceed with Library Control creation now? OR
- Request temporary limit increase for Sprint 6?
