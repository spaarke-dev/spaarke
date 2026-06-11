#!/usr/bin/env node
// Inline upload of a Dataverse web resource using node's built-in fetch.
//
// This is the canonical workaround for surfaces whose Deploy-*.ps1 wrapper
// is unreliable. The 2026-06-10 incident: Deploy-ReportingCodePage.ps1 runs
// `npm run build` and chokes when Vite's Rollup emits /* #__PURE__ */ warnings
// to stderr (PowerShell's $ErrorActionPreference='Stop' treats native stderr
// as fatal). This script skips the build (caller is responsible) and uploads
// the existing dist directly.
//
// Compared to a curl-based equivalent, fetch avoids the execSync ENOBUFS issue
// when the Dataverse Web API echoes the existing content in a GET response
// (see SKILL.md Failure Mode F-5).
//
// Usage:
//   node scripts/master-deploy/deploy-webresource-inline.mjs <wrName> <distFile> [orgUrl]
//
// Args:
//   wrName    — e.g. sprk_reporting, sprk_alldocuments, sprk_smarttodo
//   distFile  — path to the built HTML/JS (absolute or repo-rooted)
//   orgUrl    — default: https://spaarkedev1.crm.dynamics.com
//
// Requires: `az login` already executed in the shell session.

import { execSync } from 'child_process';
import { readFileSync, existsSync } from 'fs';

const [, , wrName, distFileArg, orgUrlArg] = process.argv;

if (!wrName || !distFileArg) {
  console.error('Usage: deploy-webresource-inline.mjs <wrName> <distFile> [orgUrl]');
  process.exit(2);
}

const orgUrl = orgUrlArg || 'https://spaarkedev1.crm.dynamics.com';
const apiUrl = `${orgUrl}/api/data/v9.2`;
const distFile = distFileArg.replace(/\\/g, '/');

if (!existsSync(distFile)) {
  console.error(`Missing: ${distFile}`);
  process.exit(1);
}

const fileBytes = readFileSync(distFile);
const fileSizeKb = Math.round(fileBytes.length / 1024);
const fileContent = fileBytes.toString('base64');
console.log(`Loaded ${distFile} (${fileSizeKb} KB)`);

console.log('Getting access token...');
const accessToken = execSync(
  `"C:/Program Files/Microsoft SDKs/Azure/CLI2/wbin/az.cmd" account get-access-token --resource ${orgUrl} --query accessToken -o tsv`,
  { encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 }
).trim();

const headers = {
  Authorization: `Bearer ${accessToken}`,
  Accept: 'application/json',
  'OData-Version': '4.0',
};

console.log(`Looking up ${wrName}...`);
const searchResp = await fetch(`${apiUrl}/webresourceset?$filter=name eq '${wrName}'`, { headers });
const searchData = await searchResp.json();
if (!searchData.value || searchData.value.length === 0) {
  console.error(`Web resource ${wrName} not found`);
  process.exit(1);
}
const webResourceId = searchData.value[0].webresourceid;
console.log(`Found: ${webResourceId}`);

console.log('PATCHing content...');
const patchResp = await fetch(`${apiUrl}/webresourceset(${webResourceId})`, {
  method: 'PATCH',
  headers: { ...headers, 'Content-Type': 'application/json' },
  body: JSON.stringify({ content: fileContent }),
});
console.log(`PATCH: ${patchResp.status} ${patchResp.statusText}`);
if (!patchResp.ok) {
  console.error(await patchResp.text());
  process.exit(1);
}

console.log('Publishing...');
const publishResp = await fetch(`${apiUrl}/PublishXml`, {
  method: 'POST',
  headers: { ...headers, 'Content-Type': 'application/json' },
  body: JSON.stringify({
    ParameterXml: `<importexportxml><webresources><webresource>${webResourceId}</webresource></webresources></importexportxml>`,
  }),
});
console.log(`PUBLISH: ${publishResp.status} ${publishResp.statusText}`);
if (!publishResp.ok) {
  console.error(await publishResp.text());
  process.exit(1);
}

console.log(`\n✓ Deployed ${wrName} (${fileSizeKb} KB) to ${orgUrl}`);
