# Correct Approach: Library Control + Selective Imports

**Date:** 2025-10-04
**Status:** 📋 **PLANNING**
**Approach:** Use dependent library pattern + selective Fluent UI imports

---

## Understanding the Correct Solution

After reviewing both documents:
1. [PCF-V9-PACKAGING.md](../../../docs/PCF-V9-PACKAGING.md)
2. [KM-FLUENT-DESIGN-DEPENDENT-LIBRARIES.md](../../../docs/KM-FLUENT-DESIGN-DEPENDENT-LIBRARIES.md)

The correct approach combines TWO strategies:

### Strategy 1: Selective Fluent UI Imports (from PCF-V9-PACKAGING.md)
**Problem:** Importing granular packages increases bundle size
```typescript
// ❌ WRONG - Multiple granular packages
import { Button } from '@fluentui/react-button';
import { Tooltip } from '@fluentui/react-tooltip';
import { Dialog } from '@fluentui/react-dialog';
```

**Solution:** Use converged entrypoint
```typescript
// ✅ CORRECT - Single converged import
import {
  Button,
  Tooltip,
  Dialog,
  FluentProvider,
  tokens
} from '@fluentui/react-components';
```

**Benefit:** Better tree-shaking, smaller bundle

### Strategy 2: Dependent Library Pattern (from KM-FLUENT-DESIGN-DEPENDENT-LIBRARIES.md)
**Problem:** Each PCF control bundles React + Fluent UI separately
```
Control A: bundle.js (10 MB with React + Fluent)
Control B: bundle.js (10 MB with React + Fluent)
Control C: bundle.js (10 MB with React + Fluent)
Total: 30 MB of duplicated libraries
```

**Solution:** Create Library Control with shared dependencies
```
Library Control: Contains React + Fluent UI (shared)
Control A: bundle.js (50 KB) → depends on Library
Control B: bundle.js (50 KB) → depends on Library
Control C: bundle.js (50 KB) → depends on Library
Total: Shared library + 150 KB of control code
```

**Benefit:** Load React + Fluent once, reuse across all controls

---

## User's Clarification

> "the issue is not Fluent UI version. You can and should use the latest version. the issue is the packaging together of react+fluent and including the entire library. the document explains how to create the component library and only use what is required."

**Translation:**
1. ✅ Use latest Fluent UI v9
2. ✅ Use selective imports (not entire library)
3. ✅ Use @spaarke/ui-components (component library approach)
4. ❌ Don't bundle React + Fluent in each control
5. ❌ Don't use vanilla JS

---

## Current State Analysis

### What We Have (Vanilla JS - WRONG)
- ✅ 28 KB bundle
- ❌ No React
- ❌ No Fluent UI v9
- ❌ No accessibility
- ❌ No theming
- ❌ Cannot use @spaarke/ui-components

### What We Should Have (React + Selective Imports - CORRECT)
- ✅ React + Fluent UI v9 components
- ✅ Selective imports from @fluentui/react-components
- ✅ Use @spaarke/ui-components for icons/shared components
- ✅ Externalize React + Fluent via webpack config
- ✅ Bundle < 500 KB (control code only)

---

## Implementation Plan

### Option A: Simple Approach (No Library Control Yet)

**For Sprint 6, use this simpler approach:**

1. **Restore React + Fluent UI code**
   - Bring back React CommandBar.tsx
   - Bring back ThemeProvider.ts
   - Use @spaarke/ui-components for icons

2. **Use selective imports**
   ```typescript
   // Converged import (single package)
   import {
     Button,
     Tooltip,
     FluentProvider,
     tokens
   } from '@fluentui/react-components';

   // Icons from separate package
   import { Add24Regular } from '@fluentui/react-icons';

   // OR use our shared library
   import { SprkIcons } from '@spaarke/ui-components/dist/icons';
   ```

3. **Configure webpack externals**
   Create `webpack.config.js`:
   ```javascript
   module.exports = {
     externals: {
       'react': 'React',
       'react-dom': 'ReactDOM',
       '@fluentui/react-components': 'FluentUIReact'
     }
   };
   ```

4. **Update package.json**
   ```json
   {
     "dependencies": {},
     "devDependencies": {
       "@fluentui/react-components": "^9.54.0",
       "@fluentui/react-icons": "^2.0.311",
       "react": "^18.2.0",
       "react-dom": "^18.2.0"
     }
   }
   ```

5. **Add featureconfig.json**
   ```json
   {
     "pcfAllowCustomWebpack": "on"
   }
   ```

**Expected Result:**
- Bundle: ~500 KB (control code + some Fluent UI, but externalized React)
- React/ReactDOM externalized (loaded from environment)
- Fluent UI components included but tree-shaken

### Option B: Complete Approach (Create Library Control)

**For future (Sprint 7+):**

1. **Create Spaarke.UI.LibraryControl**
   - New PCF project
   - Contains React + Fluent UI + @spaarke/ui-components
   - Exports libraries globally
   - Deployed once

2. **Update Universal Grid to depend on Library**
   Add to `ControlManifest.Input.xml`:
   ```xml
   <resources>
     <dependency
       type="control"
       name="sprk_Spaarke.UI.LibraryControl"
       order="1"
     />
     <code path="index.ts" order="2" />
   </resources>
   ```

3. **Configure webpack externals**
   ```javascript
   module.exports = {
     externals: {
       'react': 'SprLibReact',
       'react-dom': 'SprLibReactDOM',
       '@fluentui/react-components': 'SprLibFluentUI',
       '@spaarke/ui-components': 'SprLibComponents'
     }
   };
   ```

**Expected Result:**
- Library Control: ~5 MB (contains all shared libraries)
- Universal Grid: ~50 KB (just control code)
- Other controls: ~50 KB each (all depend on Library)

---

## Immediate Recommendation for Sprint 6

**Use Option A (Simple Approach):**

1. Revert vanilla JS CommandBar
2. Restore React CommandBar.tsx with selective imports
3. Configure webpack externals for React/ReactDOM
4. Keep using @spaarke/ui-components for icons
5. Target bundle < 1 MB (externalized React but bundled Fluent components)

**Why:**
- Faster to implement (no new Library Control needed)
- Gets us back to React + Fluent UI v9
- Reduces bundle size significantly
- Maintains ADR compliance
- Can migrate to Library Control in Sprint 7

---

## Key Insights

### From PCF-V9-PACKAGING.md:
1. Use converged import: `@fluentui/react-components`
2. Don't use granular packages: `@fluentui/react-button`, etc.
3. Platform can provide React/Fluent (platform-library approach)

### From KM-FLUENT-DESIGN-DEPENDENT-LIBRARIES.md:
1. Create Library Control for shared dependencies
2. Use webpack externals to exclude bundling
3. Dependent controls load library on demand or upfront

### User's Direction:
1. Use latest Fluent UI v9 ✅
2. Use selective imports (not entire library) ✅
3. Use component library approach (@spaarke/ui-components) ✅
4. Don't bundle everything together ✅

---

## Next Steps

### Immediate (Sprint 6):
1. ✅ Revert vanilla TypeScript CommandBar
2. ✅ Recreate React CommandBar.tsx with selective imports
3. ✅ Add webpack.config.js with externals
4. ✅ Add featureconfig.json
5. ✅ Update package.json (move to devDependencies)
6. ✅ Build and verify bundle < 1 MB
7. ✅ Test deployment to Dataverse

### Future (Sprint 7):
1. Create Spaarke.UI.LibraryControl (dependent library)
2. Move React + Fluent UI to Library Control
3. Update all controls to use dependency pattern
4. Achieve ~50 KB bundles per control

---

## Conclusion

**The vanilla JS approach was WRONG.**

**The correct approach is:**
- ✅ Use React + Fluent UI v9
- ✅ Use selective imports (@fluentui/react-components)
- ✅ Externalize React via webpack
- ✅ Use @spaarke/ui-components
- ✅ Target < 1 MB bundle initially
- ✅ Migrate to Library Control pattern later

This maintains:
- Fluent UI v9 compliance
- Accessibility
- Theming
- Component library usage
- ADR compliance
- Reasonable bundle size
