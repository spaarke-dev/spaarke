# 066 — Bundle Size Analysis: SemanticSearch Code Page

> **Date**: 2026-02-25
> **Current Bundle**: ~1.13 MiB (minified, production mode)
> **Build Target**: Single `bundle.js` inlined into Dataverse HTML web resource
> **Constraint**: NO code splitting — must be a single self-contained HTML file

---

## 1. Current Bundle Size Breakdown

### Estimated Module Sizes (Top 10 by Contribution)

Based on dependency analysis, known published package sizes, and import patterns:

| Rank | Module | Estimated Size (minified) | % of Bundle | Notes |
|------|--------|--------------------------|-------------|-------|
| 1 | `@fluentui/react-components` (umbrella + 55 sub-packages) | ~380-420 KiB | ~33-37% | Barrel re-exports pull in all 55 component packages |
| 2 | `@xyflow/react` + `@xyflow/system` | ~180-220 KiB | ~16-19% | Full graph engine: ReactFlow, zustand, classcat |
| 3 | `react` + `react-dom` (v19.2.4) | ~140-150 KiB | ~12-13% | React 19 runtime — irreducible |
| 4 | `@azure/msal-browser` + `@azure/msal-common` | ~120-140 KiB | ~11-12% | MSAL v3 auth library — irreducible |
| 5 | `@fluentui/react-icons` (v2.0.319) | ~40-60 KiB | ~4-5% | Tree-shakeable, only ~20 icons imported |
| 6 | `@griffel/react` + `@griffel/core` | ~35-45 KiB | ~3-4% | CSS-in-JS engine (Fluent dependency) |
| 7 | d3-force + d3 transitive deps (11 packages) | ~30-40 KiB | ~3% | d3-force, d3-quadtree, d3-dispatch, d3-timer, d3-zoom, etc. |
| 8 | `tabster` | ~25-30 KiB | ~2% | Keyboard navigation (Fluent dependency) |
| 9 | `@fluentui/react-theme` + `@fluentui/tokens` | ~20-25 KiB | ~2% | Theme definitions and design tokens |
| 10 | Application source code (30 .ts/.tsx files) | ~15-20 KiB | ~1-2% | All app components, hooks, services |
| — | Other (@swc/helpers, css-loader runtime, etc.) | ~15-25 KiB | ~1-2% | Webpack runtime, polyfills, CSS inlining |

**Total estimated**: ~1,000-1,175 KiB (~1.0-1.15 MiB)

### Dependency Chain Depth

```
@fluentui/react-components (umbrella)
├── 55 component packages (Accordion, Alert, Avatar, Badge, Button, Card, ...)
│   └── @griffel/react → @griffel/core (CSS-in-JS)
│   └── @fluentui/react-tabster → tabster (keyboard navigation)
│   └── @fluentui/react-positioning → @floating-ui/dom
│   └── @fluentui/react-portal (portal management)
│   └── @fluentui/react-motion (animation system)
│   └── @fluentui/react-virtualizer (virtualized lists)
│   └── ... 49 more component packages NOT used by this app
├── @fluentui/react-theme + @fluentui/tokens
└── @swc/helpers

@xyflow/react
├── @xyflow/system (core graph engine)
├── zustand (state management, ~4 KiB)
└── classcat (~0.5 KiB)

@azure/msal-browser
└── @azure/msal-common (shared MSAL logic)

d3-force
├── d3-dispatch, d3-quadtree, d3-timer
└── (d3-zoom, d3-drag, d3-selection, d3-transition — pulled by @xyflow/system)
```

---

## 2. Fluent UI Component Inventory

### Components Actually Used

From all source files, only these Fluent v9 components are imported:

| Category | Components Used |
|----------|----------------|
| **Layout** | `FluentProvider`, `Divider`, `Text` |
| **Input** | `Button`, `ToggleButton`, `Input`, `Dropdown`, `Option`, `Label`, `Slider`, `Checkbox` |
| **Data** | `DataGrid`, `DataGridHeader`, `DataGridRow`, `DataGridHeaderCell`, `DataGridBody`, `DataGridCell`, `createTableColumn` |
| **Feedback** | `Spinner`, `ProgressBar`, `MessageBar`, `MessageBarBody`, `Badge` |
| **Navigation** | `TabList`, `Tab`, `Toolbar`, `ToolbarButton`, `ToolbarDivider`, `Tooltip` |
| **Menu** | `Menu`, `MenuTrigger`, `MenuPopover`, `MenuList`, `MenuItem`, `MenuGroup`, `MenuGroupHeader`, `MenuDivider` |
| **Styling** | `makeStyles`, `mergeClasses`, `tokens` |
| **Themes** | `webLightTheme`, `webDarkTheme`, `teamsHighContrastTheme`, `Theme` |
| **Utilities** | `useId` |

**Used Fluent sub-packages**: ~15 of 55 (badge, button, checkbox, combobox, divider, field, input, label, menu, message-bar, progress, provider, slider, spinner, table, tabs, text, toolbar, tooltip)

**Unused Fluent sub-packages** (included but never imported): accordion, alert, avatar, breadcrumb, card, carousel, color-picker, dialog, drawer, image, infobutton, infolabel, link, list, nav, overflow, persona, popover, radio, rating, search, select, skeleton, spinbutton, swatch-picker, switch, tag-picker, tags, teaching-popover, textarea, toast, tree, virtualizer (~36 unused packages)

### Icons Imported (20 unique icons)

```
ArrowClockwiseRegular, ArrowDownloadRegular, BriefcaseRegular,
ChevronDoubleLeft20Regular, ChevronDoubleRight20Regular, ChevronDownRegular,
ChevronUpRegular, DatabaseSearchRegular, DeleteRegular, DesktopRegular,
DocumentMultipleRegular, DocumentRegular, FolderRegular, GridRegular,
MailRegular, OpenRegular, ReceiptRegular, SaveRegular, Search20Regular,
StarRegular, TaskListSquareAddRegular, TextBulletListSquareRegular
```

`@fluentui/react-icons` v2 is tree-shakeable with `"sideEffects": false`. Only the 20 imported icons should be included. Each icon is ~1-2 KiB, so total icon footprint is ~30-40 KiB (reasonable).

---

## 3. Optimization Recommendations

Ranked by estimated savings and implementation effort.

### Recommendation 1: Switch to Direct Sub-Package Imports (HIGHEST IMPACT)

**Estimated Savings: 150-250 KiB (13-22%)**
**Effort: Medium (2-3 hours)**
**Risk: Low**
**Safe for Dataverse: Yes**

The umbrella `@fluentui/react-components` re-exports all 55 component sub-packages. Despite `"sideEffects": false`, webpack's tree-shaking with `ts-loader` is imperfect for deep barrel re-exports because TypeScript compilation happens before webpack can analyze the module graph.

**Current (pulls in all sub-packages via barrel):**
```typescript
import { Button, DataGrid, makeStyles, tokens } from "@fluentui/react-components";
```

**Optimized (direct sub-package imports):**
```typescript
import { Button } from "@fluentui/react-button";
import { DataGrid, DataGridHeader, ... } from "@fluentui/react-table";
import { makeStyles, mergeClasses } from "@griffel/react";
import { tokens } from "@fluentui/react-theme";
import { FluentProvider } from "@fluentui/react-provider";
import { Spinner } from "@fluentui/react-spinner";
// ... etc for each component family
```

**Why this works**: Direct imports bypass the umbrella barrel file entirely. Webpack only resolves the specific sub-packages you need (~15 of 55), eliminating the initialization code, style registrations, and type-checking overhead of 36 unused component packages.

**Migration plan:**
1. Map current imports to their sub-package homes (see Fluent docs)
2. Add only the needed `@fluentui/react-*` sub-packages to `package.json`
3. Remove `@fluentui/react-components` from `package.json`
4. Update all import paths across 12 source files
5. Verify build + visual regression

---

### Recommendation 2: Lazy-Load @xyflow/react Behind a Dynamic Import Guard

**Estimated Savings: 150-200 KiB (13-18%) from initial parse**
**Effort: Medium (2-3 hours)**
**Risk: Low**
**Safe for Dataverse: Partially — see notes**

The graph view (`SearchResultsGraph.tsx`) is only used when the user switches to graph mode (default is grid). The entire `@xyflow/react` + `@xyflow/system` + `zustand` stack (~200 KiB) is loaded upfront even when only grid mode is used.

**Since code splitting is NOT possible** in a Dataverse web resource, the JavaScript will always be in the bundle. However, `@xyflow/react` can be conditionally `import()`-ed at runtime to defer parsing/evaluation:

```typescript
// In App.tsx — only load the graph component when user switches to graph view
const SearchResultsGraph = React.lazy(() => import("./components/SearchResultsGraph"));

// Render:
{viewMode === "graph" && (
    <React.Suspense fallback={<Spinner label="Loading graph..." />}>
        <SearchResultsGraph ... />
    </React.Suspense>
)}
```

**Caveat**: This requires webpack `output.chunkFilename` or inline chunk support. For a single-bundle Dataverse web resource, you would need `optimization.splitChunks: false` and use `import()` with `webpackMode: "eager"` to avoid actual chunk splitting. This still helps with **parse-time deferral** but NOT download size.

**Revised assessment**: Without code splitting, this optimization provides **no real savings** in bundle size. It would only help if the web resource loading mechanism supported chunked delivery. **Skip unless architecture changes.**

**Alternative**: Consider whether `@xyflow/react` is worth its ~200 KiB cost. The graph view could potentially be reimplemented with a lighter SVG-based approach (see Recommendation 5).

---

### Recommendation 3: Replace d3-force with a Lightweight Layout Algorithm

**Estimated Savings: 25-35 KiB (2-3%)**
**Effort: High (4-6 hours)**
**Risk: Medium**
**Safe for Dataverse: Yes**

Currently `d3-force` is imported for the `useClusterLayout` hook, but only uses:
- `forceSimulation`, `forceManyBody`, `forceCenter`, `forceCollide`, `forceLink`

This pulls in 11 d3 modules. However, `@xyflow/react` already includes `d3-zoom`, `d3-drag`, `d3-selection`, and `d3-transition` as transitive dependencies through `@xyflow/system`. The additional modules from `d3-force` add ~25-35 KiB.

**Options:**
1. **Custom force layout** (~8 KiB): Implement a simplified force simulation using only `forceSimulation`, `forceManyBody`, `forceCenter`, `forceCollide` from `d3-force` — but these are the exact imports already used. d3-force's ESM modules are already tree-shakeable.
2. **Use `ngraph.forcelayout`** (~5 KiB minified): Lightweight alternative to d3-force for graph layout.
3. **Pre-calculated grid layout**: For cluster visualization, a deterministic grid/circle layout may work as well as force-directed, with zero library dependency.

**Recommendation**: The savings are modest. Only pursue this if Recommendation 5 is also adopted.

---

### Recommendation 4: Add webpack-bundle-analyzer for Continuous Monitoring

**Estimated Savings: 0 KiB (but prevents regression)**
**Effort: Low (30 minutes)**
**Risk: None**
**Safe for Dataverse: Yes**

Add `webpack-bundle-analyzer` as a dev dependency with a `build:analyze` script:

```json
{
  "scripts": {
    "build:analyze": "webpack --config webpack.config.js --env analyze"
  },
  "devDependencies": {
    "webpack-bundle-analyzer": "^4.10.0"
  }
}
```

```javascript
// webpack.config.js
const BundleAnalyzerPlugin = require('webpack-bundle-analyzer').BundleAnalyzerPlugin;

module.exports = (env) => ({
  // ... existing config
  plugins: [
    ...(env?.analyze ? [new BundleAnalyzerPlugin()] : []),
  ],
});
```

This provides a visual treemap of the exact module sizes and makes it easy to identify regressions as new features are added.

---

### Recommendation 5: Replace @xyflow/react with a Lighter SVG Graph Renderer

**Estimated Savings: 180-220 KiB (16-19%)**
**Effort: Very High (8-16 hours)**
**Risk: High**
**Safe for Dataverse: Yes**

`@xyflow/react` is a full-featured graph editing library with pan, zoom, node dragging, minimap, edge routing, etc. The SemanticSearch graph view only uses:
- Read-only node display (no edge editing)
- Pan and zoom
- MiniMap
- Custom node rendering (ClusterNode, RecordNode)
- fitView

A custom SVG renderer with manual `<svg>` + `<g>` transforms could provide pan/zoom at ~5-10 KiB instead of ~200 KiB. Libraries like `react-zoom-pan-pinch` (~15 KiB) provide the pan/zoom behavior.

**Pros**: Massive size reduction, simpler dependency tree
**Cons**: Loss of ReactFlow ecosystem (Controls, MiniMap, edge routing), significant reimplementation effort, potential UX regression

**Recommendation**: Only pursue if bundle size becomes a hard blocker. The 200 KiB cost is justified by the feature richness of the graph view.

---

### Recommendation 6: Enable Production Source Map Stripping + Terser Configuration

**Estimated Savings: 5-15 KiB (1%)**
**Effort: Low (30 minutes)**
**Risk: None**
**Safe for Dataverse: Yes**

The current webpack config uses `mode: 'production'` but has no explicit `optimization` settings. Adding explicit Terser configuration can improve minification:

```javascript
// webpack.config.js
module.exports = {
  mode: 'production',
  optimization: {
    minimize: true,
    minimizer: [
      new (require('terser-webpack-plugin'))({
        terserOptions: {
          compress: {
            drop_console: true,      // Remove console.log/info/debug
            drop_debugger: true,
            pure_funcs: ['console.debug', 'console.info'],
            passes: 2,
          },
          mangle: {
            safari10: true,
          },
          output: {
            comments: false,
          },
        },
      }),
    ],
    usedExports: true,   // Explicit tree-shaking hint
    sideEffects: true,   // Respect package.json sideEffects
  },
};
```

Key gains:
- `drop_console: true` removes all `console.log/warn/error` from MSAL and app code (~3-5 KiB)
- `passes: 2` enables additional compression passes
- `usedExports: true` reinforces tree-shaking
- `sideEffects: true` tells webpack to trust `"sideEffects": false` in packages

---

### Recommendation 7: Consider esbuild-loader Instead of ts-loader

**Estimated Savings: 20-50 KiB (improved tree-shaking)**
**Effort: Low-Medium (1-2 hours)**
**Risk: Low**
**Safe for Dataverse: Yes**

`ts-loader` compiles TypeScript before webpack sees the JavaScript, which can defeat tree-shaking because TypeScript may emit CJS-style require() calls or preserve barrel re-exports that webpack cannot optimize.

`esbuild-loader` (or `swc-loader`) transpiles TypeScript but preserves ES module syntax, allowing webpack to perform better tree-shaking:

```javascript
// webpack.config.js
module.exports = {
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: {
          loader: 'esbuild-loader',
          options: {
            target: 'es2020',
            jsx: 'automatic',
          },
        },
        exclude: /node_modules/,
      },
    ],
  },
};
```

**Benefits**:
- Better tree-shaking of Fluent UI barrel exports
- 5-10x faster build times
- Smaller output due to more efficient code generation

**Caveat**: Some TypeScript-specific features (const enums, namespaces) may not work. Test thoroughly.

---

## 4. Impact/Effort Matrix

| Recommendation | Savings | Effort | Priority |
|---------------|---------|--------|----------|
| 1. Direct sub-package imports | 150-250 KiB | Medium | **P0 - Do First** |
| 4. Bundle analyzer | 0 (monitoring) | Low | **P0 - Do First** |
| 6. Terser configuration | 5-15 KiB | Low | **P1 - Quick Win** |
| 7. esbuild-loader | 20-50 KiB | Low-Medium | **P1 - Quick Win** |
| 3. Replace d3-force | 25-35 KiB | High | P2 - Consider |
| 2. Lazy-load @xyflow | 0 (deferred parse) | Medium | **Skip** (no code splitting) |
| 5. Replace @xyflow entirely | 180-220 KiB | Very High | P3 - Only if blocked |

### Recommended Action Plan

**Phase 1 — Quick Wins (1-2 hours, ~25 KiB savings)**
1. Add `webpack-bundle-analyzer` for exact measurements
2. Configure explicit Terser optimization (drop_console, 2 passes)
3. Add `usedExports: true` and `sideEffects: true` to webpack config

**Phase 2 — High Impact (3-4 hours, ~200 KiB savings)**
4. Switch from `ts-loader` to `esbuild-loader` for better tree-shaking
5. Migrate from `@fluentui/react-components` umbrella to direct sub-package imports
6. Remove unused sub-package dependencies from package.json

**Phase 3 — Diminishing Returns (skip unless needed)**
7. Evaluate lighter d3-force alternatives
8. Evaluate lighter graph renderer alternatives

### Projected Bundle Sizes

| Scenario | Estimated Size | Reduction |
|----------|---------------|-----------|
| **Current** | ~1.13 MiB | — |
| After Phase 1 | ~1.10 MiB | ~3% |
| After Phase 2 | ~0.85-0.90 MiB | ~20-25% |
| After Phase 3 (theoretical) | ~0.65-0.75 MiB | ~35-42% |

---

## 5. Dataverse Web Resource Constraints

### What IS Safe
- Tree-shaking and dead-code elimination
- Direct sub-package imports (Fluent v9 supports this)
- Terser minification configuration
- Switching TypeScript loaders (esbuild-loader, swc-loader)
- Bundle analyzer (dev-only)
- Removing unused dependencies

### What is NOT Safe / Not Possible
- **Code splitting** — Dataverse web resources must be a single HTML file with inlined JS
- **Dynamic `import()` with chunk output** — No chunk loading mechanism in Dataverse
- **CDN externals** — Dataverse does not provide a CDN for React or Fluent UI
- **Service workers** — Not supported in Dataverse web resource context
- **Shared bundles across web resources** — Each web resource is independently loaded

### Dataverse-Specific Consideration: Inline HTML Build

The final deployment artifact is an HTML file with the JS bundle inlined as a `<script>` tag. This means:
- Gzip/Brotli compression at the HTTP level still helps (Dataverse serves gzipped)
- The ~1.13 MiB minified bundle compresses to approximately **350-400 KiB** over the wire (gzipped)
- After Phase 2 optimizations: ~**280-320 KiB** over the wire

---

## 6. Appendix: Full Dependency List

### Production Dependencies (7 direct)

```
@azure/msal-browser         3.30.0   → @azure/msal-common 14.16.1
@fluentui/react-components  9.73.0   → 55 sub-packages + @griffel + tabster
@fluentui/react-icons       2.0.319  → (self-contained, tree-shakeable)
@xyflow/react               12.10.1  → @xyflow/system 0.0.75, zustand, classcat
d3-force                    3.0.0    → d3-dispatch, d3-quadtree, d3-timer
react                       19.2.4   → (self-contained)
react-dom                   19.2.4   → (depends on react, scheduler)
```

### Transitive d3 Packages (11 total, via d3-force + @xyflow/system)

```
d3-color, d3-dispatch, d3-drag, d3-ease, d3-force, d3-interpolate,
d3-quadtree, d3-selection, d3-timer, d3-transition, d3-zoom
```

### Installed @fluentui Sub-Packages (55 component + 10 infrastructure)

**Used (15)**: react-badge, react-button, react-checkbox, react-combobox, react-divider, react-input, react-label, react-menu, react-message-bar, react-progress, react-provider, react-slider, react-spinner, react-table, react-tabs, react-text, react-toolbar, react-tooltip

**Infrastructure (always needed)**: react-theme, react-utilities, react-shared-contexts, react-tabster, react-portal, react-positioning, react-aria, react-jsx-runtime, react-field, react-overflow, react-context-selector, react-motion, tokens, keyboard-keys, priority-overflow

**Unused (36)**: react-accordion, react-alert, react-avatar, react-breadcrumb, react-card, react-carousel, react-color-picker, react-dialog, react-drawer, react-image, react-infobutton, react-infolabel, react-link, react-list, react-nav, react-persona, react-popover, react-radio, react-rating, react-search, react-select, react-skeleton, react-spinbutton, react-swatch-picker, react-switch, react-tag-picker, react-tags, react-teaching-popover, react-textarea, react-toast, react-tree, react-virtualizer

---

*Analysis performed by examining source imports, node_modules structure, package.json dependency trees, and known published package sizes. For exact measurements, run `npm run build:analyze` after implementing Recommendation 4.*
