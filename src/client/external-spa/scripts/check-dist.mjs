#!/usr/bin/env node
/**
 * check-dist - scan the BUILT bundle (every .js under dist/) for what must never ship
 * (spaarke-SPA-external-access-platform-r3 task 042 review, FR-18, #1566). Run by `npm run check:dist`, and by the
 * deploy workflow right after `npm run build`.
 *
 * Two checks, both on the output of the real build (the source scan in tests/out-of-plane-routes.test.ts and the
 * vite.config.ts guard act on source and config; this one looks at what a browser would actually download):
 *
 *   1. MOCK MARKERS - the names and ids of the dev-mock identities and canned data (src/dev-mock.ts, src/mocks/,
 *      src/api/me-client.ts personas). Any occurrence fails. The only tolerated mention of VITE_DEV_MOCK is the
 *      pinned build constant `VITE_DEV_MOCK:"false"` that Vite writes into its `import.meta.env` object.
 *   2. OUT-OF-PLANE ROUTES - every "/api/..." path string must be under /api/v1/external. Anything else is a call
 *      to a route the external plane does not own. The RATCHET BASELINE lists only the shared
 *      BffDataverseClient path suffixes (they resolve under /api/v1/external at run time, see OUT_OF_PLANE_BASELINE): any
 *      other such string fails, and an entry that no longer appears (or appears fewer times) fails too.
 *
 * `scanDistText` / `evaluateScan` are pure (tests/check-dist.test.ts feeds them fixture strings); only `main` and
 * `checkDist` touch the file system. Exit code 0 = clean, 1 = findings, 2 = nothing to scan (no dist, or no .js in it).
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** Literal strings that identify the mock identity / canned data / mock switch. Any hit fails. */
export const MOCK_MARKERS = [
  'Jane Smith',
  'Sam Rivera',
  'Dana Okafor',
  'Alex Reyes',
  'dv_persona',
  'VITE_DEV_MOCK',
  'mock-contact-001',
  'mock-project-001',
  'getMockResponse',
  '(Mock)',
];

/** A mention that is NOT a marker: Vite's `import.meta.env` object carrying the build-pinned `VITE_DEV_MOCK:"false"`. */
const TOLERATED_MARKER_MENTIONS = { VITE_DEV_MOCK: 'VITE_DEV_MOCK:"false"' };

/** The only plane-owned prefix an "/api/" string may have. */
const EXTERNAL_PREFIX = /^\/api\/v1\/external(?:[/?]|$)/;

/**
 * "/api/" strings that are allowed in the bundle, with how many times each appears. Since the owner decision (a) of
 * 2026-10-11 (task 042) removed the playbook page and SemanticSearch, the playbook-create route
 * (`/api/ai/analysis/create`) and the `bffDataServiceAdapter` proxy (`/api/dataverse/${entity}`) are GONE from the
 * bundle. What remains are the path SUFFIXES of the shared `BffDataverseClient`: it appends `/api/dataverse/...` to
 * its `bffBaseUrl`, and the only instance in this SPA (`services/gridDataverseClient.ts`, task 041) is built with
 * `{backend.bffBaseUrl}/api/v1/external`, so every request resolves to `/api/v1/external/api/dataverse/...`. A scan of
 * the minified bundle sees only the suffix half. Anything else - a NEW string, or one of these appearing more often -
 * fails; an entry that stops appearing fails too, so the baseline can only shrink.
 */
export const OUT_OF_PLANE_BASELINE = {
  '/api/dataverse/fetch': 1,
  '/api/dataverse/metadata/': 1,
  '/api/dataverse/record/': 1,
  '/api/dataverse/savedqueries/': 1,
  '/api/dataverse/savedquery/': 1,
};

/** Count the occurrences of `needle` in `text`. */
function countOf(text, needle) {
  let n = 0;
  for (let i = text.indexOf(needle); i !== -1; i = text.indexOf(needle, i + needle.length)) n++;
  return n;
}

/**
 * Scan one file's text. Returns `{ markers: {marker: count}, apiPaths: {fragment: count} }`.
 * A fragment is an "/api/..." string cut at the first quote, backtick, whitespace, `$`, `{`, `}` or bracket - so
 * `${base}/api/dataverse/${id}` is "/api/dataverse/". Fragments under /api/v1/external are not reported.
 */
export function scanDistText(text) {
  const markers = {};
  for (const marker of MOCK_MARKERS) {
    let n = countOf(text, marker);
    const tolerated = TOLERATED_MARKER_MENTIONS[marker];
    if (tolerated) n -= countOf(text, tolerated);
    if (n > 0) markers[marker] = n;
  }
  const apiPaths = {};
  for (const match of text.matchAll(/\/api\/[^\s"'`\\${}()<>[\],;]*/g)) {
    const fragment = match[0];
    if (EXTERNAL_PREFIX.test(fragment)) continue;
    apiPaths[fragment] = (apiPaths[fragment] ?? 0) + 1;
  }
  return { markers, apiPaths };
}

/** Add the counts of `from` into `into` (both `{key: count}`). */
export function mergeCounts(into, from) {
  for (const [key, n] of Object.entries(from)) into[key] = (into[key] ?? 0) + n;
  return into;
}

/**
 * Judge the merged scan of every file against the baseline. Returns the findings (empty = pass):
 *   - one per mock marker found;
 *   - one per out-of-plane string not in the baseline, or appearing more often than the baseline allows;
 *   - one per baseline entry that no longer appears, or appears fewer times (shrink the baseline).
 */
export function evaluateScan(scan, baseline = OUT_OF_PLANE_BASELINE) {
  const findings = [];
  for (const [marker, n] of Object.entries(scan.markers)) {
    findings.push(`mock marker "${marker}" found ${n} time(s) in the bundle (mock mode must never ship)`);
  }
  for (const [fragment, n] of Object.entries(scan.apiPaths)) {
    const allowed = Object.prototype.hasOwnProperty.call(baseline, fragment) ? baseline[fragment] : 0;
    if (n > allowed) {
      findings.push(
        allowed === 0
          ? `out-of-plane route string "${fragment}" found ${n} time(s) (every "/api/" path must be under /api/v1/external)`
          : `out-of-plane route string "${fragment}" found ${n} time(s), the baseline allows ${allowed}`
      );
    }
  }
  for (const [fragment, allowed] of Object.entries(baseline)) {
    const n = scan.apiPaths[fragment] ?? 0;
    if (n < allowed) {
      findings.push(
        `baseline entry "${fragment}" expects ${allowed} occurrence(s) but the bundle has ${n} - it is fixed or removed: ` +
          'lower or delete the entry in scripts/check-dist.mjs (OUT_OF_PLANE_BASELINE)'
      );
    }
  }
  return findings;
}

/** All .js files under a directory. */
function listJsFiles(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...listJsFiles(full));
    else if (entry.isFile() && entry.name.endsWith('.js')) out.push(full);
  }
  return out;
}

/** Scan a dist directory; returns `{ files, findings }` (no files when the directory is missing). */
export function checkDist(distDir) {
  if (!fs.existsSync(distDir)) return { files: [], findings: [] };
  const files = listJsFiles(distDir);
  const merged = { markers: {}, apiPaths: {} };
  for (const file of files) {
    const scan = scanDistText(fs.readFileSync(file, 'utf8'));
    mergeCounts(merged.markers, scan.markers);
    mergeCounts(merged.apiPaths, scan.apiPaths);
  }
  return { files, findings: evaluateScan(merged) };
}

function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const distDir = path.join(root, 'dist');
  const { files, findings } = checkDist(distDir);
  if (files.length === 0) {
    console.error(`check-dist: no .js files under ${distDir} - run \`npm run build\` first.`);
    process.exit(2);
  }
  if (findings.length > 0) {
    console.error(`check-dist: ${findings.length} finding(s) in ${files.length} file(s):`);
    for (const f of findings) console.error(`  - ${f}`);
    process.exit(1);
  }
  console.log(`check-dist: ${files.length} file(s) clean (no mock marker; out-of-plane routes match the baseline).`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
