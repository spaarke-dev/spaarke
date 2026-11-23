/**
 * `declaredScriptVersion` (quality-alert #1906): the "the real script ran" checks read the version from the script's own
 * source instead of pinning a literal that every version bump breaks.
 */
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { declaredScriptVersion } from './helpers/scriptVersion';

let dir: string;

beforeAll(() => {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'script-version-'));
});

afterAll(() => {
  fs.rmSync(dir, { recursive: true, force: true });
});

function scriptFile(name: string, text: string): string {
  const file = path.join(dir, name);
  fs.writeFileSync(file, text, 'utf8');
  return file;
}

describe('declaredScriptVersion', () => {
  it('reads a `version: "x"` property, the shape of the Config objects', () => {
    expect(declaredScriptVersion(scriptFile('a.js', 'var Config = {\n    version: "1.28.3",\n};'))).toBe('1.28.3');
  });

  it('reads an assignment under another key, the shape of the ribbon (`ns.VERSION = "x"`)', () => {
    expect(declaredScriptVersion(scriptFile('b.js', "ns.VERSION = '1.8.0';"), 'VERSION')).toBe('1.8.0');
  });

  it('takes the first declaration and does not match a longer name (`libraryVersion`, `versionLabel`)', () => {
    const file = scriptFile('c.js', 'libraryVersion: "9.9.9", versionLabel: "x", version: "2.0.1", version: "3.0.0"');
    expect(declaredScriptVersion(file)).toBe('2.0.1');
  });

  it('throws when the script declares no version, so the check cannot pass against undefined', () => {
    expect(() => declaredScriptVersion(scriptFile('d.js', 'var Config = {};'))).toThrow(/declares no version string/);
  });

  it('reads the real scripts the injection tests load', () => {
    const clientRoot = path.resolve(__dirname, '../../../..');
    for (const [file, key] of [
      [path.join(clientRoot, 'webresources/js/sprk_DocumentOperations.js'), 'version'],
      [path.join(clientRoot, 'webresources/js/sprk_access_ribbon.js'), 'VERSION'],
      [path.resolve(clientRoot, '../solutions/webresources/sprk_assignedaccess_postsave.js'), 'version'],
      [path.resolve(clientRoot, '../solutions/webresources/sprk_noaccessentry_postsave.js'), 'version'],
    ]) {
      expect(declaredScriptVersion(file, key)).toMatch(/^\d+\.\d+\.\d+$/);
    }
  });
});
