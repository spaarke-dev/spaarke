/**
 * logger — host-side facade over the shared `@spaarke/visuals` logger.
 *
 * INTENTIONAL FACADE (VHVU-060 decision — not a temporary shim). The logger
 * implementation lives in `@spaarke/visuals/src/utils/logger` (used there by
 * ErrorBoundary + cardConfigResolver). This one-line re-export gives the ~13
 * host modules a short, stable `'../utils/logger'` import instead of a deep
 * `../../../../shared/Spaarke.Visuals/src/utils/logger` path repeated across the
 * codebase. Retained deliberately: it decouples host call-sites from the shared
 * util's physical location and keeps the host imports readable.
 *
 * (The sibling cardConfigResolver + trendAnalysis shims WERE closed in VHVU-060
 * — each had a single importer, so repointing was clean. logger has many, and a
 * facade is the better end state.)
 */

export * from '../../../../shared/Spaarke.Visuals/src/utils/logger';
