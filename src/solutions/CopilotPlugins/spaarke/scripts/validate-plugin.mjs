#!/usr/bin/env node
// Build gate for the Spaarke Copilot / Cowork plugin package (spaarkeai-mcp-bff-api-r1 task 040).
//
// The package carries skills only: no agentConnectors (the MCP server is registered once per tenant as the
// federated connector "Spaarke MCP"; notes/decisions.md, 2026-11-23). This gate fails the build on:
//   - a skill folder or frontmatter name that does not match ^spaarke(-[a-z0-9]+)+$, is longer than 64 characters,
//     or differs from its folder (naming note section 1, agreed with spaarke-legal-ai-accelerator-r1, OD-20);
//   - a manifestVersion other than 1.30;
//   - any agentConnectors key;
//   - an agentSkills list that is not exactly the folders under skills/;
//   - a skill description outside 1..1024 characters;
//   - a tenant or client id next to an identity name, a token, a private key, a secret-valued JSON key, or an
//     env.*.json value that is not a placeholder (ADR-028: no secret or tenant id in the package).
//
// Usage: node scripts/validate-plugin.mjs [packageRoot]   (default: the folder above scripts/). Exit 1 on any error.
// No third-party dependencies.

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const SKILL_NAME_PATTERN = /^spaarke(-[a-z0-9]+)+$/;
export const MAX_NAME_LENGTH = 64;
export const MAX_DESCRIPTION_LENGTH = 1024;
export const MAX_SKILLS = 20;
export const REQUIRED_MANIFEST_VERSION = '1.30';

const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
const ZERO_GUID = /^0{8}-0{4}-0{4}-0{4}-0{12}$/;
// An environment placeholder, resolved at build time as env.dev.json does for the April package: ${{NAME}} or <name>.
const PLACEHOLDER = /^(\$\{\{\s*[A-Za-z0-9_]+\s*\}\}|<[^<>]+>)$/;
const SECRET_KEY = /secret|password|passwd|api[_-]?key|token|credential|connection[_-]?string/i;
const IDENTITY_GUID = new RegExp(
  `\\b(?:tenant|tid|client|app(?:lication)?[_-]?id|directory|aad)[\\w-]*["']?\\s*[:=]\\s*["']?${GUID}`,
  'i',
);
const AUTHORITY_GUID = new RegExp(`(?:login\\.microsoftonline\\.com|sts\\.windows\\.net)/${GUID}`, 'i');
const JWT = /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\./;
const PRIVATE_KEY = /-----BEGIN [A-Z ]*PRIVATE KEY-----/;
const SKIP_DIRS = new Set(['node_modules', 'dist', 'scripts', '.git']);
const TEXT_EXTENSIONS = new Set(['.json', '.md']);

/** Errors for one skill name (a folder name or a frontmatter name). `label` says where the name came from. */
export function checkSkillName(name, label) {
  const errors = [];
  if (typeof name !== 'string' || name.length === 0) {
    return [`${label}: the name is missing`];
  }
  if (name.length > MAX_NAME_LENGTH) {
    errors.push(`${label}: '${name}' is ${name.length} characters, longer than ${MAX_NAME_LENGTH}`);
  }
  if (!SKILL_NAME_PATTERN.test(name)) {
    errors.push(`${label}: '${name}' does not match ${SKILL_NAME_PATTERN} (lower-case kebab-case starting with 'spaarke-')`);
  }
  return errors;
}

/** The scalar `key: value` lines of a SKILL.md frontmatter block; a surrounding pair of quotes is removed. Null when there is none. */
export function parseFrontmatter(text) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/.exec(text);
  if (!match) {
    return null;
  }
  const fields = {};
  for (const line of match[1].split(/\r?\n/)) {
    const pair = /^([A-Za-z_][\w-]*):\s*(.*)$/.exec(line);
    if (pair) {
      fields[pair[1]] = pair[2].trim().replace(/^(["'])([\s\S]*)\1$/, '$2');
    }
  }
  return fields;
}

function walk(dir, visit) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) {
        walk(path.join(dir, entry.name), visit);
      }
    } else {
      visit(path.join(dir, entry.name));
    }
  }
}

function skillFolders(root) {
  const dir = path.join(root, 'skills');
  if (!existsSync(dir)) {
    return [];
  }
  return readdirSync(dir, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => e.name)
    .sort();
}

function checkManifest(root, folders, errors) {
  const file = path.join(root, 'appPackage', 'manifest.json');
  if (!existsSync(file)) {
    errors.push('appPackage/manifest.json is missing');
    return;
  }
  let manifest;
  try {
    manifest = JSON.parse(readFileSync(file, 'utf8'));
  } catch (e) {
    errors.push(`appPackage/manifest.json is not valid JSON: ${e.message}`);
    return;
  }

  if (manifest.manifestVersion !== REQUIRED_MANIFEST_VERSION) {
    errors.push(`manifest: manifestVersion is '${manifest.manifestVersion}', it must be exactly '${REQUIRED_MANIFEST_VERSION}'`);
  }
  if (Object.prototype.hasOwnProperty.call(manifest, 'agentConnectors')) {
    errors.push(
      "manifest: 'agentConnectors' must not be present. This package carries skills only; the server is registered once per tenant as the connector 'Spaarke MCP'",
    );
  }

  const skills = manifest.agentSkills;
  if (!Array.isArray(skills) || skills.length === 0) {
    errors.push("manifest: 'agentSkills' must list the skill folders under skills/");
    return;
  }
  if (skills.length > MAX_SKILLS) {
    errors.push(`manifest: agentSkills has ${skills.length} entries, the schema allows at most ${MAX_SKILLS}`);
  }
  const declared = [];
  for (const [i, entry] of skills.entries()) {
    const folder = entry && typeof entry.folder === 'string' ? entry.folder.replace(/^\.\//, '') : null;
    const m = folder === null ? null : /^skills\/([^/]+)\/?$/.exec(folder);
    if (!m) {
      errors.push(`manifest: agentSkills[${i}].folder must be 'skills/<name>' relative to the package root, got ${JSON.stringify(entry?.folder)}`);
    } else {
      declared.push(m[1]);
    }
  }
  const missing = folders.filter((f) => !declared.includes(f));
  const unknown = declared.filter((f) => !folders.includes(f));
  if (missing.length > 0) {
    errors.push(`manifest: agentSkills does not list the skill folder(s) ${missing.map((f) => `'${f}'`).join(', ')} under skills/`);
  }
  if (unknown.length > 0) {
    errors.push(`manifest: agentSkills lists ${unknown.map((f) => `'${f}'`).join(', ')}, which has no folder under skills/`);
  }
  if (new Set(declared).size !== declared.length) {
    errors.push('manifest: agentSkills lists a folder more than once');
  }
}

function checkSkills(root, folders, errors) {
  if (folders.length === 0) {
    errors.push('skills/ holds no skill folder');
  }
  for (const folder of folders) {
    errors.push(...checkSkillName(folder, `skills/${folder} (folder)`));
    const file = path.join(root, 'skills', folder, 'SKILL.md');
    if (!existsSync(file)) {
      errors.push(`skills/${folder}/SKILL.md is missing`);
      continue;
    }
    const fields = parseFrontmatter(readFileSync(file, 'utf8'));
    if (!fields) {
      errors.push(`skills/${folder}/SKILL.md has no frontmatter block`);
      continue;
    }
    errors.push(...checkSkillName(fields.name, `skills/${folder}/SKILL.md (frontmatter name)`));
    if (fields.name && fields.name !== folder) {
      errors.push(`skills/${folder}/SKILL.md: frontmatter name '${fields.name}' must equal the folder name '${folder}'`);
    }
    const length = (fields.description ?? '').length;
    if (length < 1 || length > MAX_DESCRIPTION_LENGTH) {
      errors.push(`skills/${folder}/SKILL.md: description is ${length} characters, it must be 1 to ${MAX_DESCRIPTION_LENGTH}`);
    }
  }
}

function jsonValues(node, trail, visit) {
  if (Array.isArray(node)) {
    node.forEach((v, i) => jsonValues(v, `${trail}[${i}]`, visit));
  } else if (node !== null && typeof node === 'object') {
    for (const [k, v] of Object.entries(node)) {
      jsonValues(v, trail ? `${trail}.${k}` : k, visit);
    }
  } else {
    visit(trail, node);
  }
}

// Deliberately narrow: the manifest id and the $schema URL are GUID/URL text and must pass. Only an identity GUID next to an
// identity name, an authority URL with a GUID, a JWT, a private key and secret-named JSON keys with real values are refused.
function checkSecrets(root, errors) {
  walk(root, (file) => {
    const rel = path.relative(root, file).split(path.sep).join('/');
    const base = path.basename(file);
    if (!TEXT_EXTENSIONS.has(path.extname(file)) || statSync(file).size > 1_000_000) {
      return;
    }
    const text = readFileSync(file, 'utf8');
    for (const [pattern, what] of [
      [IDENTITY_GUID, 'a tenant, client or application id'],
      [AUTHORITY_GUID, 'a tenant id in an authority URL'],
      [JWT, 'a token'],
      [PRIVATE_KEY, 'a private key'],
    ]) {
      if (pattern.test(text)) {
        errors.push(`${rel}: contains ${what}; the package holds none (ADR-028)`);
      }
    }
    if (path.extname(file) !== '.json') {
      return;
    }
    let json;
    try {
      json = JSON.parse(text);
    } catch {
      return;
    }
    const isEnvFile = /^env\..+\.json$/.test(base);
    jsonValues(json, '', (trail, value) => {
      if (typeof value !== 'string' || value === '' || PLACEHOLDER.test(value)) {
        return;
      }
      const key = trail.split('.').pop();
      if (SECRET_KEY.test(key)) {
        errors.push(`${rel}: '${trail}' holds a value; a secret-named key must be empty or a placeholder`);
      } else if (isEnvFile && new RegExp(`^${GUID}$`, 'i').test(value) && !ZERO_GUID.test(value)) {
        errors.push(`${rel}: '${trail}' holds an id; env files hold placeholders, resolved per environment`);
      }
    });
  });
}

/** Validates the package at `root`. Returns the list of errors (empty when the package passes). */
export function validatePackage(root) {
  const errors = [];
  const folders = skillFolders(root);
  checkManifest(root, folders, errors);
  checkSkills(root, folders, errors);
  checkSecrets(root, errors);
  return errors;
}

const isMain = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  // Extra arguments (the client-tests workflow appends test-runner flags to `npm test`) are ignored; the first path-like one is the root.
  const arg = process.argv.slice(2).find((a) => !a.startsWith('-'));
  const root = path.resolve(arg ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
  const errors = validatePackage(root);
  if (errors.length > 0) {
    console.error(`Plugin package ${root} failed validation (${errors.length}):`);
    for (const e of errors) {
      console.error(`  - ${e}`);
    }
    process.exit(1);
  }
  console.log(`Plugin package ${root} is valid: manifest ${REQUIRED_MANIFEST_VERSION}, skills [${skillFolders(root).join(', ')}], no agentConnectors.`);
}
