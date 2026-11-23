/**
 * The version a web-resource script declares in its own source, for the "the real script ran" check in tests that inject
 * the script into jsdom.
 *
 * Those tests used to pin the literal ("1.28.2"), so every version bump of the script broke a test that was about
 * something else (quality-alert #1906: `sprk_DocumentOperations.js` went to 1.28.3 and four delete-conflict tests failed).
 * Reading the value from the file keeps the check meaningful: the injected script must have run and set its version
 * object to what its source says.
 */
import * as fs from 'fs';

/**
 * Returns the string assigned by the first `<key>: "x"` or `<key> = "x"` in `file` (default key `version`).
 * Throws when the file declares none, so a renamed or removed declaration fails the test loudly instead of comparing
 * against `undefined`.
 */
export function declaredScriptVersion(file: string, key = 'version'): string {
  const text = fs.readFileSync(file, 'utf8');
  const match = new RegExp(`\\b${key}\\s*[:=]\\s*["']([^"']+)["']`).exec(text);
  if (!match) {
    throw new Error(`${file} declares no ${key} string`);
  }
  return match[1];
}
