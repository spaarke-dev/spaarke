/**
 * @jest-environment node
 *
 * The size and self-containment gate (task 030, NFR-03): runs scripts/check-bundle-size.mjs as the build does, against padded fixtures.
 * Each fixture lives in its own folder, because the gate also requires the folder to hold exactly one file (dist/ shape).
 */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const SCRIPT = resolve(__dirname, '../../scripts/check-bundle-size.mjs');
const BUILT = resolve(__dirname, '../../dist/index.html');
const MB = 1024 * 1024;

let root: string;
let counter = 0;

beforeAll(() => {
  root = mkdtempSync(join(tmpdir(), 'spaarke-widget-size-'));
});

afterAll(() => {
  rmSync(root, { recursive: true, force: true });
});

/** A self-contained page padded to `bytes` with a comment inside the inline script, alone in a fresh folder. */
function fixture(bytes: number, extraHead = '', extraScript = ''): string {
  const dir = join(root, `f${counter++}`);
  mkdirSync(dir);
  const head = `<!doctype html><html><head>${extraHead}</head><body><div id="root"></div><script type="module">${extraScript}/*`;
  const tail = `*/</script></body></html>`;
  const file = join(dir, 'index.html');
  writeFileSync(file, head + 'x'.repeat(Math.max(0, bytes - head.length - tail.length)) + tail);
  return file;
}

function run(file: string) {
  const result = spawnSync(process.execPath, [SCRIPT, file], { encoding: 'utf8' });
  return { code: result.status, out: `${result.stdout}${result.stderr}` };
}

describe('check-bundle-size.mjs: size', () => {
  it('passes a bundle under 1 MB without a warning', () => {
    const result = run(fixture(500 * 1024));
    expect(result.code).toBe(0);
    expect(result.out).toContain('OK');
    expect(result.out).not.toContain('WARN');
  });

  it('passes but warns between 1 MB and 3 MB', () => {
    const result = run(fixture(2 * MB));
    expect(result.code).toBe(0);
    expect(result.out).toContain('WARN');
  });

  it('passes a bundle of exactly 3 MB', () => {
    expect(run(fixture(3 * MB)).code).toBe(0);
  });

  it('FAILS a bundle one byte over 3 MB (negative: the padded fixture)', () => {
    const result = run(fixture(3 * MB + 1));
    expect(result.code).toBe(1);
    expect(result.out).toContain('FAIL');
  });

  it('FAILS when the built file is missing', () => {
    const result = run(join(root, 'does-not-exist.html'));
    expect(result.code).toBe(1);
    expect(result.out).toContain('does not exist');
  });
});

describe('check-bundle-size.mjs: one self-contained file', () => {
  it('FAILS a page with a script src or a stylesheet link', () => {
    expect(run(fixture(1000, '<script type="module" src="./assets/index.js"></script>')).code).toBe(1);
    expect(run(fixture(1000, '<link rel="stylesheet" href="./assets/index.css">')).code).toBe(1);
  });

  it('FAILS when dist/ holds any other file or folder next to the page', () => {
    const sibling = fixture(1000);
    writeFileSync(join(sibling, '..', 'index.js'), '// stray');
    const folder = fixture(1000);
    mkdirSync(join(folder, '..', 'assets'));

    const a = run(sibling);
    const b = run(folder);

    expect(a.code).toBe(1);
    expect(a.out).toContain('index.js');
    expect(b.code).toBe(1);
    expect(b.out).toContain('assets');
  });

  it.each([
    ['an img src over https', '<img src="https://cdn.example.com/logo.png">'],
    ['a protocol-relative src', "<img src='//cdn.example.com/logo.png'>"],
    ['an unquoted http href', '<a href=http://example.com/x>x</a>'],
    ['a preload link href', '<link rel="preload" href="https://fonts.example.com/f.woff2" as="font">'],
    ['a CSS url(https)', '<style>@font-face{src:url(https://fonts.example.com/f.woff2)}</style>'],
    ['a quoted CSS url("//")', '<style>body{background:url("//cdn.example.com/b.png")}</style>'],
    ['a CSS @import', '<style>@import "https://fonts.example.com/css";</style>'],
  ])('FAILS %s', (_name, head) => {
    const result = run(fixture(1000, head));
    expect(result.code).toBe(1);
    expect(result.out).toContain('external');
  });

  it('FAILS a dynamic import of a remote module in the inline script', () => {
    expect(run(fixture(1000, '', 'import("https://cdn.example.com/m.js");')).code).toBe(1);
    expect(run(fixture(1000, '', "import('//cdn.example.com/m.js');")).code).toBe(1);
  });

  it('PASSES namespace and schema strings in the code, and data: URIs (they load nothing)', () => {
    const script = [
      'const svg="http://www.w3.org/2000/svg";',
      'const xlink="http://www.w3.org/1999/xlink";',
      'const schema="https://json-schema.org/draft/2020-12/schema";',
      'const dec="https://reactjs.org/docs/error-decoder.html?invariant="+1;',
      'const u=new URL("https://github.com/cfworker");',
      'const o={href:"#x",src:"data:image/png;base64,AAAA"};',
    ].join('');
    const result = run(fixture(1000, '<style>.a{background:url(data:image/png;base64,AAAA)}</style>', script));
    expect(result.code).toBe(0);
    expect(result.out).toContain('OK');
  });

  const built = existsSync(BUILT) ? it : it.skip;
  built('PASSES the real built widget (dist/index.html), when it has been built', () => {
    expect(readFileSync(BUILT, 'utf8').length).toBeGreaterThan(1000);
    const result = run(BUILT);
    expect(result.code).toBe(0);
  });
});
