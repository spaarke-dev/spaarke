/**
 * logger — host-side facade over the shared `@spaarke/visuals` logger.
 *
 * INTENTIONAL FACADE (VHVU-060 decision — not a temporary shim). The logger
 * implementation lives in `@spaarke/visuals` (used there by ErrorBoundary +
 * cardConfigResolver). This one-line re-export gives the ~13 host modules a
 * short, stable `'../utils/logger'` import instead of repeating the package
 * import across the codebase. Retained deliberately: it decouples host
 * call-sites from the shared util's physical location and keeps the host
 * imports readable. Repointed to the bare `@spaarke/visuals` package
 * specifier (C-15, spaarke-ontology-platform-r1 reuse audit X7) — this PCF's
 * `moduleResolution: "node"` does not understand `package.json#exports`
 * subpaths (`@spaarke/visuals/utils` does not resolve; see `types/index.ts`
 * for the full explanation), but the bare specifier resolves via the
 * package's `main`/`types` fields, which classic resolution does understand.
 *
 * (The sibling cardConfigResolver + trendAnalysis shims WERE closed in VHVU-060
 * — each had a single importer, so repointing was clean. logger has many, and a
 * facade is the better end state.)
 */

export { logger } from '@spaarke/visuals';
