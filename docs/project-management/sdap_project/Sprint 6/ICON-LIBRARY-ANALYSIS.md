# Fluent UI Icons: Availability and Usage Analysis

**Date:** October 4, 2025
**Issue:** Are Fluent UI icons available system-wide or hardcoded into Universal Grid?
**Status:** 📋 **ANALYSIS COMPLETE**

---

## TL;DR - Current State

**❌ Icons are NOT available system-wide currently**

The Fluent UI icons (`@fluentui/react-icons`) are currently:
- Installed locally in each PCF control's `node_modules`
- Bundled into each PCF control's bundle.js
- **Hard-coded into the Universal Grid package** (3.8 MB bundle includes icons)

**✅ But they CAN be made available system-wide**

We already have `@spaarke/ui-components` shared library that:
- Has `@fluentui/react-icons` as a peer dependency
- Can export icon components for reuse
- Should be the central place for all Fluent UI icon access

---

## Current Architecture

### Universal Dataset Grid (Current)

```
Universal Dataset Grid PCF Control
├── node_modules/
│   └── @fluentui/react-icons@2.0.311  (4.67 MB - all 2000+ icons)
├── components/
│   └── CommandBar.tsx
│       └── imports 4 specific icons:
│           - Add24Regular
│           - Delete24Regular
│           - ArrowUpload24Regular
│           - ArrowDownload24Regular
└── bundle.js (3.8 MB) ← Icons bundled here
```

**Problem:**
- Each PCF control that needs icons will install `@fluentui/react-icons` separately
- Each control's bundle will include icon code (even with tree-shaking)
- No central icon library = code duplication

---

### Spaarke.UI.Components (Shared Library)

**Location:** `src/shared/Spaarke.UI.Components/`
**Package:** `@spaarke/ui-components` v1.0.0
**Status:** ✅ Already exists!

**package.json:**
```json
{
  "name": "@spaarke/ui-components",
  "peerDependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.220",  // ← Icons as peer dependency
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  }
}
```

**Structure:**
```
src/shared/Spaarke.UI.Components/
├── src/
│   ├── components/     (shared React components)
│   ├── hooks/          (shared hooks)
│   ├── theme/          (Fluent UI theme)
│   ├── types/          (TypeScript types)
│   ├── utils/          (utilities)
│   └── index.ts        (exports everything)
├── dist/               (compiled output)
└── spaarke-ui-components-1.0.0.tgz  (195 KB package)
```

**Key insight:** This library already has `@fluentui/react-icons` as a peer dependency, meaning it's designed to use icons but doesn't bundle them.

---

## Problem: Icon Bundle Size

### Webpack Tree-Shaking Limitation

**Observation from build logs:**
```
[BABEL] Note: The code generator has deoptimised the styling of:
- chunk-0.js (500+ KB)
- chunk-1.js (500+ KB)
- chunk-2.js (500+ KB)
... (24 chunks total)
```

**Issue:** Webpack includes ALL icon chunks from `@fluentui/react-icons` even though we only import 4 specific icons.

**Why?**
- Fluent UI icons are organized in chunks (chunk-0.js, chunk-1.js, etc.)
- Webpack's tree-shaking can't eliminate unused chunks
- Result: ~2 MB of icon code in bundle vs ~8 KB for 4 icons

**Package size:**
- `@fluentui/react-icons` NPM package: 4.67 MB (all 2000+ icons)
- Icons actually used: 4 icons = ~8 KB
- Icons bundled in Universal Grid: ~2 MB (due to chunking)

---

## Recommended Solution

### Option A: Create Icon Library in @spaarke/ui-components ✅ **RECOMMENDED**

**Benefits:**
- ✅ Central icon registry for entire system
- ✅ PCF controls, Canvas apps, Model-driven apps, Power Pages all use same icons
- ✅ Consistent icon usage across platform
- ✅ Single source of truth for icon inventory
- ✅ Type-safe icon names
- ✅ Easy to add new icons (one place)

**Implementation:**

**Step 1:** Create icon registry in `@spaarke/ui-components`

```typescript
// src/shared/Spaarke.UI.Components/src/icons/index.ts

/**
 * Spaarke Icon Library - Central registry for all Fluent UI icons
 *
 * Usage in PCF controls, Canvas apps, Model-driven apps, Power Pages:
 * import { SpkIcons } from '@spaarke/ui-components';
 * <Button icon={<SpkIcons.Add />} />
 */

import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular,
    // Add more as needed...
} from '@fluentui/react-icons';

/**
 * Spaarke icon collection - all icons used in the system.
 */
export const SpkIcons = {
    // File operations
    Add: Add24Regular,
    Delete: Delete24Regular,
    Upload: ArrowUpload24Regular,
    Download: ArrowDownload24Regular,

    // Navigation (add as needed)
    // Home: Home24Regular,
    // Settings: Settings24Regular,
    // ...

} as const;

/**
 * Icon name type for type-safe usage.
 */
export type SpkIconName = keyof typeof SpkIcons;
```

**Step 2:** Export from main package

```typescript
// src/shared/Spaarke.UI.Components/src/index.ts
export * from "./icons";
```

**Step 3:** Use in Universal Grid

```typescript
// src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx

// BEFORE (hardcoded imports):
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular
} from '@fluentui/react-icons';

// AFTER (from shared library):
import { SpkIcons } from '@spaarke/ui-components';

// Usage:
<Button icon={<SpkIcons.Add />}>Add File</Button>
<Button icon={<SpkIcons.Delete />}>Remove File</Button>
```

**Step 4:** Use in other applications

```typescript
// Canvas apps, Model-driven apps, Power Pages, etc.
import { SpkIcons } from '@spaarke/ui-components';

// Navigation menu
<MenuItem icon={<SpkIcons.Home />} label="Home" />
<MenuItem icon={<SpkIcons.Settings />} label="Settings" />
```

---

### Bundle Size Impact

**Current (without shared library):**
- Universal Grid bundle: 3.8 MB (includes ~2 MB icons)
- Each new PCF control: +3.8 MB (duplicates icons)
- 3 PCF controls: 11.4 MB total

**With shared library:**
- @spaarke/ui-components: 195 KB (no icons bundled, peer dependency)
- Universal Grid bundle: 3.8 MB (includes ~2 MB icons - same as now)
- Each new PCF control: ~1.8 MB (no icon duplication!)
- 3 PCF controls: ~7.4 MB total

**Savings:** 11.4 MB - 7.4 MB = **4 MB saved** across 3 controls

**But wait - why doesn't the shared library reduce Universal Grid bundle?**

Because PCF controls bundle their dependencies. The shared library approach:
1. Doesn't reduce individual bundle sizes (each still bundles dependencies)
2. **But provides:**
   - Central icon registry (add once, use everywhere)
   - Consistent naming (SpkIcons.Add not Add24Regular)
   - Type safety (can't use icons not in registry)
   - Easier maintenance (one place to add/remove icons)

---

### Option B: Icon Font (Future Optimization)

**Benefits:**
- Drastically smaller bundle size (~50 KB for all icons)
- Icons loaded once, cached by browser
- No webpack chunking issues

**Drawbacks:**
- Not type-safe
- Harder to maintain
- Requires build pipeline changes

**Recommendation:** Consider for future optimization if bundle size becomes critical.

---

## Usage Scenarios

### Scenario 1: PCF Controls (Current - Universal Grid)

**Now:**
```typescript
import { Add24Regular } from '@fluentui/react-icons';
<Button icon={<Add24Regular />} />
```

**With shared library:**
```typescript
import { SpkIcons } from '@spaarke/ui-components';
<Button icon={<SpkIcons.Add />} />
```

---

### Scenario 2: Canvas Apps

Canvas apps can't directly use React components, but can use Power Apps Component Framework (PCF) controls.

**Approach:**
1. Create icon PCF control using `@spaarke/ui-components`
2. Canvas app references the PCF control
3. Icon rendered via PCF control

---

### Scenario 3: Model-Driven Apps

**Navigation icons:**
```typescript
// Custom web resource for sitemap icons
import { SpkIcons } from '@spaarke/ui-components';

export function getSitemapIcon(area: string): React.ReactElement {
    switch (area) {
        case 'documents': return <SpkIcons.Document />;
        case 'settings': return <SpkIcons.Settings />;
        default: return <SpkIcons.Home />;
    }
}
```

---

### Scenario 4: Power Pages

**Portal navigation:**
```typescript
// Portal web template
import { SpkIcons } from '@spaarke/ui-components';

export const PortalNav: React.FC = () => (
    <nav>
        <NavItem icon={<SpkIcons.Home />} href="/" />
        <NavItem icon={<SpkIcons.Documents />} href="/documents" />
    </nav>
);
```

---

## Icon Inventory

### Currently Used (Universal Grid)

| Icon | Component | Size | Usage |
|------|-----------|------|-------|
| Add24Regular | `<SpkIcons.Add />` | ~2 KB | Add File button |
| Delete24Regular | `<SpkIcons.Delete />` | ~2 KB | Remove File button |
| ArrowUpload24Regular | `<SpkIcons.Upload />` | ~2 KB | Update File button |
| ArrowDownload24Regular | `<SpkIcons.Download />` | ~2 KB | Download button |

**Total:** 4 icons, ~8 KB

### Future Icons (Estimated)

| Area | Icons Needed | Examples |
|------|-------------|----------|
| Navigation | 10-15 | Home, Settings, Documents, Dashboard, Reports |
| File operations | 5-10 | Upload, Download, Delete, Edit, Preview |
| Status | 5-10 | Success, Error, Warning, Info, Pending |
| Actions | 10-15 | Save, Cancel, Edit, Add, Remove, Refresh |

**Estimated total:** 40-50 icons, ~100 KB actual size

---

## Implementation Plan

### Phase 1: Create Icon Registry (1 hour)

**File:** `src/shared/Spaarke.UI.Components/src/icons/index.ts`

```typescript
export const SpkIcons = {
    // File operations (from Universal Grid)
    Add: Add24Regular,
    Delete: Delete24Regular,
    Upload: ArrowUpload24Regular,
    Download: ArrowDownload24Regular,
} as const;

export type SpkIconName = keyof typeof SpkIcons;
```

**Export from package:**
```typescript
// src/shared/Spaarke.UI.Components/src/index.ts
export * from "./icons";
```

**Build and package:**
```bash
cd src/shared/Spaarke.UI.Components
npm run build
npm pack
```

---

### Phase 2: Update Universal Grid (30 minutes)

**Install updated shared library:**
```bash
cd src/controls/UniversalDatasetGrid
npm install ../../shared/Spaarke.UI.Components/spaarke-ui-components-1.0.0.tgz
```

**Update CommandBar.tsx:**
```typescript
// Remove direct icon imports
- import {
-     Add24Regular,
-     Delete24Regular,
-     ArrowUpload24Regular,
-     ArrowDownload24Regular
- } from '@fluentui/react-icons';

// Add shared library import
+ import { SpkIcons } from '@spaarke/ui-components';

// Update JSX
- <Button icon={<Add24Regular />}>Add File</Button>
+ <Button icon={<SpkIcons.Add />}>Add File</Button>
```

**Build and verify:**
```bash
npm run build
# Bundle size should remain ~3.8 MB (no change, just cleaner imports)
```

---

### Phase 3: Document Icon Usage (30 minutes)

Create icon usage guide in `@spaarke/ui-components` README.

---

## Answers to Your Question

### Q: Are icons available in a library we have in our code?

**A: Yes and No.**

**Yes:** We have `@spaarke/ui-components` shared library with `@fluentui/react-icons` as a peer dependency.

**No:** The icons are not currently exported from `@spaarke/ui-components`. They need to be added (1 hour work).

---

### Q: Are they hard coded into the Universal Grid package?

**A: Yes, currently.**

The icons are imported directly in `CommandBar.tsx` and bundled into `bundle.js` (3.8 MB).

**But:** This is easily fixable by using `@spaarke/ui-components` as the icon source (30 minutes work).

---

## Recommendation

✅ **Implement Option A: Create Icon Library in @spaarke/ui-components**

**Timeline:**
- Phase 1 (Icon Registry): 1 hour
- Phase 2 (Update Universal Grid): 30 minutes
- Phase 3 (Documentation): 30 minutes
- **Total: 2 hours**

**Benefits:**
1. ✅ Icons available system-wide (PCF controls, Canvas, Model-driven, Power Pages)
2. ✅ Central registry = easier maintenance
3. ✅ Type-safe usage
4. ✅ Consistent naming across platform
5. ✅ Prevents future code duplication

**Timing:**
- Can do now (before continuing Phase 2) - **RECOMMENDED**
- OR do later (after Phase 2 complete)
- OR do in separate sprint (technical debt)

---

## Decision Required

**Option 1:** Implement icon library now (2 hours)
- Pros: Clean architecture from the start, no refactoring later
- Cons: Delays Phase 2 progress by 2 hours

**Option 2:** Continue Phase 2, implement icon library later
- Pros: Maintain momentum on Sprint 6
- Cons: Will need to refactor Universal Grid later (30 min rework)

**Option 3:** Create icon library in parallel with Phase 2
- Pros: Best of both worlds
- Cons: Context switching

**Your preference?**
