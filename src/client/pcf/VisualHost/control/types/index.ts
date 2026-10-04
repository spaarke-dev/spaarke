/**
 * Visual Host PCF Types — re-export shim (VHVU-041).
 *
 * The presentational view-model types live in `@spaarke/visuals`. This shim
 * keeps every existing `'../types'` import across ChartRenderer /
 * VisualHostRoot / services / the self-fetch visuals working with a
 * single-line change.
 *
 * C-15 (spaarke-ontology-platform-r1 reuse audit X7) repointed VisualHost's
 * OTHER `@spaarke/visuals` imports (components, `resolveCardConfig`, the
 * logger's source) to the bare `@spaarke/visuals` package specifier — this
 * PCF's `moduleResolution: "node"` (tsconfig.json) is the pre-TS5 "Node10"
 * algorithm, which reads a bare specifier's `main`/`types` fields but does
 * NOT understand `package.json#exports` subpaths, so `@spaarke/visuals/types`
 * (confirmed via `tsc --traceResolution`) does not resolve. This ONE shim is
 * deliberately left on the deep-relative path rather than switched to the
 * bare specifier: unlike the other call sites, this file's whole job is a
 * wildcard re-export of the types module's full surface, and the bare
 * specifier's barrel ALSO carries every component + util, which would widen
 * this "types" facade into components territory. Narrowing the import to the
 * exact types-module path keeps this shim's shape honest.
 */

export * from '../../../../shared/Spaarke.Visuals/src/types';
