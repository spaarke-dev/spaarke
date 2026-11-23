/**
 * Guard for OfficeNaaStrategy: it learns whether MSAL's Office NAA bridge came up from MSAL's own Info log line
 * "Nested App Auth Bridge available: true|false" (MSAL has no public API for it). A newer MSAL that renames or
 * minifies that line would make `isNaaActive()` report false everywhere without any other failure; this test makes
 * such an upgrade fail here instead.
 */

import * as fs from 'fs';
import * as path from 'path';

function msalPackageRoot(): string {
  let dir = path.dirname(require.resolve('@azure/msal-browser'));
  while (dir !== path.dirname(dir)) {
    const pkg = path.join(dir, 'package.json');
    if (fs.existsSync(pkg) && JSON.parse(fs.readFileSync(pkg, 'utf8')).name === '@azure/msal-browser') return dir;
    dir = path.dirname(dir);
  }
  throw new Error('@azure/msal-browser package root not found');
}

function filesUnder(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name);
    return entry.isDirectory() ? filesUnder(full) : [full];
  });
}

it('the installed MSAL still logs "Nested App Auth Bridge available: ${available}"', () => {
  const dist = path.join(msalPackageRoot(), 'dist');
  const found = filesUnder(dist)
    .filter(file => /\.(mjs|js|cjs)$/.test(file))
    .some(file => fs.readFileSync(file, 'utf8').includes('Nested App Auth Bridge available: ${this.available}'));

  expect(found).toBe(true);
});
