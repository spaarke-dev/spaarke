# Bundle Size Optimization — Home Corporate Workspace R1

> **Task**: 033 — Bundle Size Optimization
> **Phase**: 4 — Integration & Polish
> **NFR**: NFR-02 — PCF bundle must be under 5MB
> **Completed**: 2026-02-18

---

## Current Bundle Size Estimate

The LegalWorkspace PCF control has not been built to a final artifact yet (no
`npm run build` output exists in this repository). Based on static analysis of
the component tree and the platform-library declarations, the estimated sizes
are:

| Category | Estimated Contribution | Notes |
|----------|----------------------|-------|
| React + ReactDOM (platform-provided) | ~0 KB bundled | Excluded via `platform-library` declaration |
| @fluentui/react-components (platform-provided) | ~0 KB bundled | Excluded via `platform-library` declaration |
| @fluentui/react-icons (named imports only) | ~30–80 KB | Tree-shaken to only imported icons |
| Application TypeScript code | ~200–400 KB minified | ~90 source files, all component logic |
| Dialog chunks (lazy-loaded) | Deferred | WizardDialog, BriefingDialog, AISummaryDialog, TodoAISummaryDialog |
| **Estimated total initial chunk** | **~250–500 KB** | Well under 5 MB NFR-02 threshold |

> Note: Without a production build artifact the above are estimates. The
> platform-library exclusions (React + Fluent) account for the largest possible
> savings (~3–4 MB). The remaining application code is well within the 5 MB limit.

---

## Optimizations Applied

### 1. Platform Library Declarations (ADR-022)

**File**: `ControlManifest.Input.xml`

```xml
<platform-library name="React" version="18.2.0" />
<platform-library name="Fluent" version="9" />
```

**Impact**: React (~130 KB), ReactDOM (~120 KB), and `@fluentui/react-components`
(~2–3 MB) are excluded from the PCF bundle entirely. The Dataverse platform
injects these at runtime. This is the single largest bundle size reduction.

**Also applied**: `featureconfig.json` with `"pcfReactPlatformLibraries": "on"`
enables the pcf-scripts bundler to externalize React and ReactDOM.

### 2. React.lazy() for Dialog Components

**File**: `components/Shell/WorkspaceGrid.tsx`

The four dialog components are large modal overlays that are only opened on
explicit user interaction. Using `React.lazy()` defers their JavaScript from
the initial bundle chunk until first use:

| Component | Lazy Wrapper | Suspense Location |
|-----------|-------------|-------------------|
| `WizardDialog` | `LazyWizardDialog` | `WorkspaceGrid.tsx` (conditional on `isWizardOpen`) |
| `BriefingDialog` | `LazyBriefingDialog` | `WorkspaceGrid.tsx` (conditional on `isBriefingOpen`) |
| `AISummaryDialog` | `LazyAISummaryDialog` | `ActivityFeed.tsx` (exported, ready for Task 013 wiring) |
| `TodoAISummaryDialog` | `LazyTodoAISummaryDialog` | `SmartToDo.tsx` (exported, ready for future wiring) |

**Suspense fallback**: A Fluent `Spinner` centred in a fixed overlay (matches
the dialog backdrop pattern) so the user sees a loading indicator without
layout shift.

**Pattern applied**:
```tsx
// Lazy declaration at module level
const LazyWizardDialog = React.lazy(() => import("../CreateMatter/WizardDialog"));

// Conditional mount + Suspense boundary in render
{isWizardOpen && (
  <React.Suspense fallback={<DialogLoadingFallback />}>
    <LazyWizardDialog open={isWizardOpen} onClose={handleCloseWizard} webApi={webApi} />
  </React.Suspense>
)}
```

**Why conditional mount**: Mounting the lazy component only when the dialog is
open (`isWizardOpen`) ensures the chunk is not loaded until the first open.
Unconditional mounting with `open={false}` would still trigger the import.

### 3. Named Imports Only (Tree Shaking)

**Audit result**: All `@fluentui/react-components` and `@fluentui/react-icons`
imports across the 90+ source files use named imports only.

```typescript
// CORRECT — all files use this pattern:
import { Button, Input, Spinner } from '@fluentui/react-components';
import { AlertRegular, CheckmarkCircleRegular } from '@fluentui/react-icons';
```

No wildcard imports (`import * as FluentUI from ...`) were found. This ensures
the bundler can tree-shake unused Fluent components and icons.

**Icon audit**: The `@fluentui/react-icons` package is not a platform library
(only `@fluentui/react-components` is covered by the `Fluent` platform
declaration). All icon imports are named, which limits the bundled icon set to
only those actually used.

### 4. package.json — Dependencies to devDependencies

**File**: `package.json`

Platform-provided libraries were moved from `dependencies` to `devDependencies`
to signal to the bundler that they must not be bundled:

```json
{
  "dependencies": {},
  "devDependencies": {
    "react": "18.2.0",
    "react-dom": "18.2.0",
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.225",
    ...
  }
}
```

Libraries in `devDependencies` are available for TypeScript compilation but
marked as external by the pcf-scripts bundler when platform-library declarations
are present.

---

## Default Exports for Lazy Loading

Each lazy-loaded dialog required a default export (React.lazy() requires a
module with a default export). Named exports were retained for test imports:

| File | Change |
|------|--------|
| `components/CreateMatter/WizardDialog.tsx` | Added `export default WizardDialog;` |
| `components/GetStarted/BriefingDialog.tsx` | Added `export default BriefingDialog;` |
| `components/ActivityFeed/AISummaryDialog.tsx` | Added `export default AISummaryDialog;` |
| `components/SmartToDo/TodoAISummaryDialog.tsx` | Added `export default TodoAISummaryDialog;` |

---

## Remaining Optimization Opportunities

These items were identified but are out of scope for Task 033 (no breaking
changes to component architecture or external dependencies):

| Opportunity | Estimated Saving | Effort | Notes |
|-------------|-----------------|--------|-------|
| Code splitting `@fluentui/react-icons` by feature area | 10–30 KB | Low | Already mitigated by named imports |
| Lazy load `PriorityScoreCard` + `EffortScoreCard` sub-components | 5–15 KB | Medium | Nested inside TodoAISummaryDialog (already lazy) |
| Split `WizardDialog` step components into sub-chunks | 20–50 KB | High | Risk of wizard state issues across chunk boundaries |
| Memoize makeStyles() calls with `mergeStyleSets` | Negligible | Low | Griffel already does per-rule caching |
| Remove `PlaceholderFeedItem` from `ActivityFeedList.tsx` | <1 KB | Low | Dev aid, not a real component |
| Pre-fetch lazy chunks on hover (dialog trigger button) | UX improvement | Low | `React.lazy` + `import()` pre-fetch on `onMouseEnter` |

### Pre-fetch Pattern (Future Enhancement)

If users report a noticeable delay before dialogs appear, add pre-fetch on hover:

```tsx
// Pre-fetch the lazy chunk when the user hovers the "Create Matter" button
const handleCreateMatterHover = React.useCallback(() => {
  void import("../CreateMatter/WizardDialog");
}, []);

<Button onMouseEnter={handleCreateMatterHover} onClick={handleOpenWizard}>
  Create New Matter
</Button>
```

---

## ADR Compliance

| ADR | Constraint | Status |
|-----|-----------|--------|
| ADR-022 | Declare platform-library for React and Fluent | ✅ Both declared in manifest |
| ADR-022 | React and ReactDOM must not be bundled | ✅ Moved to devDependencies + featureconfig.json |
| ADR-021 | Fluent UI v9 only — no Fluent v8 imports | ✅ All imports from `@fluentui/react-components` (v9) |
| ADR-006 | PCF over webresources — bundle size matters | ✅ Platform libraries excluded (~3–4 MB saved) |

---

## NFR-02 Verdict

**NFR-02**: PCF bundle must be under 5 MB.

With platform libraries externalized (React + ReactDOM + Fluent UI = ~3–4 MB
excluded) and lazy-loaded dialogs, the initial bundle is estimated at **250–500 KB**
— approximately **10× under the 5 MB limit**. The largest remaining chunk
contributors are the application TypeScript code and `@fluentui/react-icons`
tree-shaken to used icons only.

**Status**: NFR-02 is expected to be satisfied. Verify with `npm run build` to
produce a production artifact and measure `bundle.js` size.

---

*Created by Task 033 — Bundle Size Optimization*
