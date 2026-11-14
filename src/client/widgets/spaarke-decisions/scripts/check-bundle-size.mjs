#!/usr/bin/env node
// check-bundle-size.mjs - the widget size gate (spaarkeai-mcp-bff-api-r1 task 030, NFR-03, O-16).
//
//   node scripts/check-bundle-size.mjs [path-to-html]      (default: dist/index.html)
//
// Exit 1 when the built HTML is missing, is larger than 3 MB, or is not self-contained (a <script src> or a stylesheet <link>: the
// BFF serves ONE html file as the MCP resource, so a sibling file would be a 404 inside the host frame). Warns above 1 MB (the aim).
// Phase 0 "Size" (notes/phase0-results.md): 1, 3 and 6 MB widgets all render; init cost is flat but a big widget paints later
// (6 MB: about 8 s), so the budget is a latency decision, not a hard platform limit.
import { existsSync, readFileSync, statSync } from "node:fs";
import { resolve } from "node:path";

const WARN_BYTES = 1024 * 1024;
const FAIL_BYTES = 3 * 1024 * 1024;

const file = resolve(process.argv[2] ?? "dist/index.html");

if (!existsSync(file)) {
  console.error(`check-bundle-size: ${file} does not exist. Run "vite build" first.`);
  process.exit(1);
}

const bytes = statSync(file).size;
const mb = (bytes / (1024 * 1024)).toFixed(2);

const html = readFileSync(file, "utf8");
const external = [
  ...html.matchAll(/<script\b[^>]*\bsrc\s*=/gi),
  ...html.matchAll(/<link\b[^>]*\brel\s*=\s*["']?stylesheet["']?[^>]*>/gi),
];
if (external.length > 0) {
  console.error(`check-bundle-size: ${file} references ${external.length} external script/stylesheet file(s); the bundle must be one self-contained file.`);
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
