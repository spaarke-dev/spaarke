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

const readXmlPermissions = (relative: string): string =>
  /<Permissions>\s*([A-Za-z]+)\s*<\/Permissions>/.exec(fs.readFileSync(path.join(ROOT, relative), 'utf8'))![1];

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
  legacyXmlPermissions: {
    mail: readXmlPermissions('outlook/outlook-manifest.xml'),
    document: readXmlPermissions('word/word-manifest.xml'),
  },
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
    // Task 115: the package carries no webApplicationInfo (customer-tenant deployment failed on it, AADSTS700016).
    expect(merged.webApplicationInfo).toBeUndefined();

    expect(() => buildFromSources({ appId: CLIENT_ID })).toThrow(/its OWN GUID/);
    expect(() => buildFromSources({ appId: OUTLOOK_XML_ID })).toThrow(/its OWN GUID/);
    expect(() => buildFromSources({ appId: WORD_XML_ID })).toThrow(/its OWN GUID/);
  });

  it('rejects a 4-part version, which the unified manifest does not accept', () => {
    expect(() => buildFromSources({ version: '1.1.0.0' })).toThrow(/3-part/);
  });
});

describe('permission parity — the package may never grant less than the live XML add-ins it replaces', () => {
  it('grants the unified equivalent of each live XML <Permissions>, and refuses to build without it', () => {
    const merged = buildFromSources();
    const granted = merged.authorization.permissions.resourceSpecific;
    for (const host of ['outlook/outlook-manifest.xml', 'word/word-manifest.xml']) {
      const required = merge.XML_PERMISSION_TO_RSC[readXmlPermissions(host)];
      expect(required).toBeDefined();
      expect(granted).toContainEqual({ name: required, type: 'Delegated' });
    }

    // The defect this guard exists for: the Outlook JSON once declared NO permissions, so a package built from it
    // installed cleanly and could not read the email. Remove the Outlook entry and the build must refuse.
    const outlook = readJson('outlook/manifest.json');
    outlook.authorization.permissions.resourceSpecific = [];
    expect(() => merge.mergeUnifiedManifest(outlook, readJson('word/manifest.json'), options())).toThrow(
      /would install and then fail at runtime/
    );

    // The XML <AppDomains> carry over as validDomains (full https origins, as Microsoft's converter emits them).
    expect(merged.validDomains).toEqual(expect.arrayContaining(['https://login.microsoftonline.com']));

    // A live XML permission with no known unified equivalent is refused, not silently skipped.
    expect(() =>
      buildFromSources({ legacyXmlPermissions: { mail: 'SomeFuturePermission', document: 'ReadWriteDocument' } })
    ).toThrow(/no entry in XML_PERMISSION_TO_RSC/);
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

describe('Open Spaarke replaces Share on the Word ribbon (task 089, UAT-10)', () => {
  const readText = (relative: string) => fs.readFileSync(path.join(ROOT, relative), 'utf8');

  it('the package, the Word JSON and the Word XML all carry "Open Spaarke" (with a tooltip) and nothing references shareDocument', () => {
    const merged = buildFromSources();
    const wordControls = merged.extensions[0].ribbons
      .filter((r: Json) => r.requirements.scopes[0] === 'document')
      .flatMap((r: Json) => r.tabs.flatMap((t: Json) => t.groups.flatMap((g: Json) => g.controls)));

    const openSpaarke = wordControls.find((c: Json) => c.actionId === 'openSpaarke');
    expect(openSpaarke).toMatchObject({ id: 'WordOpenSpaarkeButton', label: 'Open Spaarke' });
    expect(openSpaarke.supertip.title).toBe('Open Spaarke');
    expect(openSpaarke.supertip.description).toEqual(expect.any(String));
    // Where Share was: the last Word button, after Save and Quick Save.
    expect(wordControls.map((c: Json) => c.id)).toEqual([
      'WordSaveButton',
      'WordQuickSaveButton',
      'WordOpenSpaarkeButton',
    ]);

    const xml = readText('word/word-manifest.xml');
    expect(xml).toMatch(/<FunctionName>openSpaarke<\/FunctionName>/);
    expect(xml).toMatch(/id="OpenSpaarkeButton\.Label" DefaultValue="Open Spaarke"/);
    expect(xml).toMatch(/id="OpenSpaarkeButton\.SupertipText" DefaultValue="[^"]+"/);

    for (const source of [
      JSON.stringify(merged),
      readText('word/manifest.json'),
      xml,
      readText('word/commands/index.ts'),
    ]) {
      expect(source).not.toMatch(/shareDocument|ShareButton/);
    }
  });

  it('the Word JSON and the Word XML versions move together (XML is the 3-part version plus ".0")', () => {
    const jsonVersion: string = readJson('word/manifest.json').version;
    const xmlVersion = /<Version>\s*([\d.]+)\s*<\/Version>/.exec(readText('word/word-manifest.xml'))![1];
    expect(jsonVersion).toMatch(/^\d+\.\d+\.\d+$/);
    expect(xmlVersion).toBe(`${jsonVersion}.0`);
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
