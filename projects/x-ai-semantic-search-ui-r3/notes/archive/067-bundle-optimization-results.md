# 067 — Bundle Size Optimization Results

> **Date**: 2026-02-25
> **Pre-optimization**: ~1,186 KiB (1.13 MiB) minified
> **Post-optimization**: ~1,186 KiB (1.13 MiB) minified
> **Build time improvement**: ~21s → ~10s (esbuild-loader)

---

## Changes Applied

### 1. Replaced ts-loader with esbuild-loader
- **Impact**: Build time ~2x faster. Better ESM preservation for tree-shaking.
- esbuild-loader transpiles TypeScript while preserving ES module syntax, allowing webpack
  to perform more effective tree-shaking on barrel re-exports.

### 2. Added explicit Terser configuration
- `drop_console: true` — removes all console.* calls (MSAL, app debug logging)
- `drop_debugger: true` — removes debugger statements
- `passes: 2` — two compression passes for better minification
- `comments: false` — strips all comments from output

### 3. Added tree-shaking optimization flags
- `usedExports: true` — marks unused exports for elimination
- `sideEffects: true` — respects package.json `"sideEffects": false` declarations

### 4. Added webpack-bundle-analyzer
- Run `npm run build:analyze` for interactive treemap visualization
- Dev-only dependency, zero production impact

---

## Bundle Size Analysis

**No significant size change** from these optimizations. Reason:

The dominant contributor (~33-37% of bundle) is `@fluentui/react-components` umbrella package
which re-exports 55 component sub-packages. Despite tree-shaking flags, webpack cannot fully
eliminate unused sub-packages from the barrel re-export chain because module initialization
side effects are not fully analyzable.

**The single biggest optimization opportunity remains**: migrating from the umbrella
`@fluentui/react-components` to direct sub-package imports (`@fluentui/react-button`,
`@fluentui/react-table`, etc.). This would save an estimated **150-250 KiB (13-22%)**
but requires updating imports in 12+ source files and adding 15+ sub-package dependencies.

---

## React.lazy / Code Splitting: SKIPPED

**Reason**: Dataverse web resources must be a single self-contained HTML file.
Code splitting via React.lazy() would produce separate chunk files that cannot be
loaded in the Dataverse web resource context. The `import()` syntax with
`webpackMode: "eager"` would still include all code in the bundle (deferred parse only).

This optimization provides **no real download size savings** in the single-file constraint.
Documented per task POML constraint.

---

## d3 Imports: ALREADY OPTIMAL

Only `d3-force` is imported directly (not the full `d3` library).
`package.json` lists `"d3-force": "^3.0.0"` as a direct dependency.
The `useClusterLayout.ts` hook imports only the needed force functions.

---

## Fluent UI Imports: NO `import *` FOUND

All Fluent UI imports use named imports from `@fluentui/react-components`.
No `import *` patterns found across all source files.

---

## Future Optimization Roadmap

| Priority | Action | Expected Savings | Effort |
|----------|--------|-----------------|--------|
| **P0** | Migrate to Fluent sub-package imports | 150-250 KiB | 3-4 hours |
| **P2** | Evaluate lighter graph library | 180-220 KiB | 8-16 hours |
| **P3** | Custom force layout (replace d3-force) | 25-35 KiB | 4-6 hours |

---

## Files Modified

- `webpack.config.js` — esbuild-loader, Terser config, usedExports, sideEffects, bundle analyzer
- `package.json` — added `build:analyze` script, added devDependencies (esbuild-loader, terser-webpack-plugin, webpack-bundle-analyzer)
