/**
 * Tests for the combined Outlook + Word app package (spaarkeai-word-add-in-r1 task 078, FR-05).
 *
 * Runs the merge against the REAL source manifests (outlook/manifest.json, word/manifest.json) and the REAL XML
 * add-in ids, so an edit to either host's manifest that would produce an invalid package fails CI — not an
 * admin-center upload. Scope: the package invariants, the id separation the task exists to make, the
 * dead-button guard, icon repair, the TEST variant's safety properties, and that Word's commands script
 * actually registers every action the package declares.
 */

import * as fs from 'fs';
import * as path from 'path';

// eslint-disable-next-line @typescript-eslint/no-var-requires
const merge = require('../mergeUnifiedManifest');

const ROOT = path.resolve(__dirname, '..', '..');
const readJson = (relative: string) => JSON.parse(fs.readFileSync(path.join(ROOT, relative), 'utf8'));
const readXmlId = (relative: string): string =>
  /<Id>\s*([0-9a-fA-F-]{36})\s*<\/Id>/.exec(fs.readFileSync(path.join(ROOT, relative), 'utf8'))![1];

const OUTLOOK_XML_ID = readXmlId('outlook/outlook-manifest.xml');
const WORD_XML_ID = readXmlId('word/word-manifest.xml');
const CLIENT_ID = 'c1258e2d-1688-49d2-ac99-a7485ebd9995';
const APP_ID = 'e68f3cb1-3702-4a58-8c02-972e7d1667eb';
const TEST_APP_ID = 'b490de25-d155-44cd-8825-6e125102dd84';

// Mirrors webpack's check: an …/assets/<file> URL "exists" when the file is in shared/assets.
const assetExists = (url: string): boolean => {
  const match = /\/assets\/([^/?#]+)$/.exec(url || '');
  return Boolean(match) && fs.existsSync(path.join(ROOT, 'shared', 'assets', match![1]));
};

const options = (overrides: Record<string, unknown> = {}) => ({
  appId: APP_ID,
  clientId: CLIENT_ID,
  version: '1.1.0',
  legacyXmlIds: { mail: OUTLOOK_XML_ID, document: WORD_XML_ID },
  assetExists,
  ...overrides,
});

const buildFromSources = (overrides: Record<string, unknown> = {}) =>
  merge.mergeUnifiedManifest(readJson('outlook/manifest.json'), readJson('word/manifest.json'), options(overrides));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Json = any;
const idsOf = (manifest: Json) => {
  const extension = manifest.extensions[0];
  return {
    runtimes: extension.runtimes.map((r: Json) => r.id),
    actions: extension.runtimes.flatMap((r: Json) => r.actions.map((a: Json) => a.id)),
    controls: extension.ribbons.flatMap((r: Json) =>
      r.tabs.flatMap((t: Json) => t.groups.flatMap((g: Json) => g.controls.map((c: Json) => c.id)))
    ),
  };
};

describe('mergeUnifiedManifest — the REAL source manifests produce one valid package', () => {
  it('keeps every Outlook id exactly as authored, scopes every runtime and ribbon to one host, and hides both XML add-ins', () => {
    const merged = buildFromSources();
    const outlookIds = idsOf(readJson('outlook/manifest.json'));
    const mergedIds = idsOf(merged);

    // Outlook's JSON is the precedent FR-05 names: the package is a strict superset of it.
    for (const id of outlookIds.runtimes) expect(mergedIds.runtimes).toContain(id);
    for (const id of outlookIds.actions) expect(mergedIds.actions).toContain(id);
    for (const id of outlookIds.controls) expect(mergedIds.controls).toContain(id);

    const extension = merged.extensions[0];
    expect(extension.requirements.scopes).toEqual(['mail', 'document']);
    for (const node of [...extension.runtimes, ...extension.ribbons]) {
      expect(node.requirements.scopes).toHaveLength(1);
    }
    expect(extension.alternates).toEqual([
      { requirements: { scopes: ['mail'] }, hide: { customOfficeAddin: { officeAddinId: OUTLOOK_XML_ID } } },
      { requirements: { scopes: ['document'] }, hide: { customOfficeAddin: { officeAddinId: WORD_XML_ID } } },
    ]);
    expect(merged.manifestVersion).toBe('1.30');
  });

  it('separates the package id from the Entra client id — and refuses to conflate them again', () => {
    const merged = buildFromSources();
    expect(merged.id).toBe(APP_ID);
    expect(merged.webApplicationInfo.id).toBe(CLIENT_ID);

    expect(() => buildFromSources({ appId: CLIENT_ID })).toThrow(/its OWN GUID/);
    expect(() => buildFromSources({ appId: OUTLOOK_XML_ID })).toThrow(/its OWN GUID/);
    expect(() => buildFromSources({ appId: WORD_XML_ID })).toThrow(/its OWN GUID/);
  });

  it('rejects a 4-part version, which the unified manifest does not accept', () => {
    expect(() => buildFromSources({ version: '1.1.0.0' })).toThrow(/3-part/);
  });
});

describe('the dead-button guard — an executeFunction id and its registration must move together', () => {
  it('throws, naming the commands file, when a Word executeFunction collides with an Outlook one and has no rename', () => {
    const word = readJson('word/manifest.json');
    // Outlook already declares `grantAccess`; a Word action of the same id has no mapping in WORD_FUNCTION_RENAMES.
    word.extensions[0].runtimes
      .find((r: Json) => r.id === 'CommandRuntime')
      .actions.push({ id: 'grantAccess', type: 'executeFunction' });

    expect(() => merge.mergeUnifiedManifest(readJson('outlook/manifest.json'), word, options())).toThrow(
      /word\/commands\/index\.ts/
    );
  });

  it('word/commands/index.ts registers every Word executeFunction action the package declares', () => {
    const merged = buildFromSources();
    const wordActions = merged.extensions[0].runtimes
      .filter((r: Json) => r.requirements.scopes[0] === 'document')
      .flatMap((r: Json) => r.actions)
      .filter((a: Json) => a.type === 'executeFunction')
      .map((a: Json) => a.id);

    const commandsSource = fs.readFileSync(path.join(ROOT, 'word', 'commands', 'index.ts'), 'utf8');
    const registered = new Set(Array.from(commandsSource.matchAll(/associate\?*\.?\(\s*'([^']+)'/g), m => m[1]));

    expect(wordActions.length).toBeGreaterThan(0);
    for (const actionId of wordActions) {
      expect(registered).toContain(actionId);
    }
  });
});

describe('icons — every icon URL in the package must resolve', () => {
  it('points an icon that does not exist at the same-size Spaarke icon, and fails when no fallback exists', () => {
    const merged = buildFromSources();
    const urls = JSON.stringify(merged).match(/https?:\/\/[^"]+\/assets\/[^"]+/g) ?? [];
    expect(urls.length).toBeGreaterThan(0);
    for (const url of urls) expect(assetExists(url)).toBe(true);
    // The Outlook JSON's save-*/share-*/grant-* icons were never created; none may survive into the package.
    expect(JSON.stringify(merged)).not.toMatch(/assets\/(save|share|grant)-/);

    expect(() => buildFromSources({ assetExists: () => false })).toThrow(/no same-size fallback/);
  });
});

describe('deriveTestVariant — a test upload can never disturb the tester’s working add-ins', () => {
  it('has its own id and a (TEST) name and ribbon labels, and does NOT hide the live XML add-ins', () => {
    const production = buildFromSources();
    const test = merge.deriveTestVariant(production, { appId: TEST_APP_ID });

    expect(test.id).toBe(TEST_APP_ID);
    expect(test.name.short).toBe('Spaarke (TEST)');
    expect(test.extensions[0].alternates).toBeUndefined();
    const labels = test.extensions[0].ribbons.flatMap((r: Json) =>
      r.tabs.flatMap((t: Json) => t.groups.map((g: Json) => g.label))
    );
    for (const label of labels) expect(label).toMatch(/\(TEST\)$/);

    // Production is untouched by deriving the variant.
    expect(production.extensions[0].alternates).toHaveLength(2);
    expect(() => merge.deriveTestVariant(production, { appId: production.id })).toThrow(/own package id/);
  });
});
