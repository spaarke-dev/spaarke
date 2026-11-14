#!/usr/bin/env node
// check-bundle-size.mjs - the widget size and self-containment gate (spaarkeai-mcp-bff-api-r1 task 030, NFR-03, O-16).
//
//   node scripts/check-bundle-size.mjs [path-to-html]      (default: dist/index.html)
//
// Exit 1 when:
//  - the built HTML is missing, or is larger than 3 MB;
//  - its folder (dist/) holds anything besides that one file: the BFF embeds ONE html file as the MCP resource, so a sibling file would be
//    a 404 inside the host frame;
//  - the HTML loads anything external: a <script src>, a stylesheet <link>, an src/href/srcset/poster/action/data attribute whose value is
//    http(s):// or //, a CSS url(http...) or url(//...), an @import of one, or a dynamic import("http...") (the widget declares an empty CSP,
//    so every one of these is blocked in the host frame and the widget would render broken).
// Namespace and schema identifiers inside the code (xmlns "http://www.w3.org/2000/svg", JSON-schema "$schema" URLs, error-decoder links in
// a string) are NOT loads and do not trip it: only the forms above do. Warns above 1 MB (the aim).
// Phase 0 "Size" (notes/phase0-results.md): 1, 3 and 6 MB widgets all render; init cost is flat but a big widget paints later
// (6 MB: about 8 s), so the budget is a latency decision, not a hard platform limit.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, resolve } from "node:path";

const WARN_BYTES = 1024 * 1024;
const FAIL_BYTES = 3 * 1024 * 1024;

const file = resolve(process.argv[2] ?? "dist/index.html");

if (!existsSync(file)) {
  console.error(`check-bundle-size: ${file} does not exist. Run "vite build" first.`);
  process.exit(1);
}

const bytes = statSync(file).size;
const mb = (bytes / (1024 * 1024)).toFixed(2);

// The folder must hold exactly this one file.
const siblings = readdirSync(dirname(file)).filter(name => name !== basename(file));
if (siblings.length > 0) {
  console.error(
    `check-bundle-size: ${dirname(file)} holds ${siblings.length} file(s) besides ${basename(file)} (${siblings.slice(0, 5).join(", ")}); the bundle must be one self-contained file.`
  );
  process.exit(1);
}

const html = readFileSync(file, "utf8");
const REMOTE = String.raw`(?:https?:)?\/\/`;
const external = [
  ...html.matchAll(/<script\b[^>]*\bsrc\s*=/gi),
  ...html.matchAll(/<link\b[^>]*\brel\s*=\s*["']?stylesheet["']?[^>]*>/gi),
  // An attribute that loads a resource, pointing at another origin. (A bare "href" in code text is not followed by http(s):// or //.)
  ...html.matchAll(new RegExp(String.raw`\b(?:src|href|srcset|poster|action|data)\s*=\s*["']?\s*${REMOTE}`, "gi")),
  // CSS url(...) in lower case only (a JavaScript "new URL(...)" is not a load), not part of a longer name.
  ...html.matchAll(new RegExp(String.raw`(?<![\w$])url\(\s*["']?\s*${REMOTE}`, "g")),
  ...html.matchAll(new RegExp(String.raw`@import\s+(?:url\()?\s*["']?\s*${REMOTE}`, "gi")),
  // Dynamic import of a remote module.
  ...html.matchAll(new RegExp(String.raw`\bimport\s*\(\s*["'\x60]\s*${REMOTE}`, "g")),
];
if (external.length > 0) {
  const sample = [...new Set(external.map(m => m[0].slice(0, 80)))].slice(0, 5).join(" | ");
  console.error(
    `check-bundle-size: ${file} loads ${external.length} external resource(s) (${sample}); the bundle must be one self-contained file with no network origins.`
  );
  process.exit(1);
}

if (bytes > FAIL_BYTES) {
  console.error(`check-bundle-size: FAIL ${file} is ${bytes} bytes (${mb} MB), over the ${FAIL_BYTES} byte (3 MB) ceiling (NFR-03).`);
  process.exit(1);
}

if (bytes > WARN_BYTES) {
  console.warn(`check-bundle-size: WARN ${file} is ${bytes} bytes (${mb} MB), over the 1 MB aim (NFR-03); under the 3 MB ceiling.`);
} else {
  console.log(`check-bundle-size: OK ${file} is ${bytes} bytes (${mb} MB), under the 1 MB aim.`);
}
