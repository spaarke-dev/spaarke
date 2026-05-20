/**
 * Library version string, surfaced via console.info on `SpaarkeAuthProvider`
 * initialization. Used to detect un-rebuilt consumers in the wild (INV-8 —
 * Bundling Reality): a stale PCF bundle running v1.x in a deployment where
 * the rest of the surface has been rebuilt to v2.x is visible in browser
 * console as a version-mismatch log instead of a silent regression.
 *
 * Bump on any breaking API change. Keep in sync with `package.json#version`
 * (no auto-derive — `import ... from '../package.json'` is fragile across
 * bundler configs and we don't want a runtime JSON fetch in browser builds).
 */
export const VERSION = '2.0.0';
