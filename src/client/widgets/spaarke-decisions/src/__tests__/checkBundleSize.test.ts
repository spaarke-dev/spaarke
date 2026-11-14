/**
 * @jest-environment node
 *
 * The size gate (task 030, NFR-03): runs scripts/check-bundle-size.mjs as the build does, against padded fixtures.
 */
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const SCRIPT = resolve(__dirname, '../../scripts/check-bundle-size.mjs');
const MB = 1024 * 1024;

let dir: string;

beforeAll(() => {
  dir = mkdtempSync(join(tmpdir(), 'spaarke-widget-size-'));
});

afterAll(() => {
  rmSync(dir, { recursive: true, force: true });
});

/** A self-contained page padded to `bytes` with a comment inside the inline script. */
function fixture(name: string, bytes: number, extraHead = ''): string {
  const head = `<!doctype html><html><head>${extraHead}</head><body><div id="root"></div><script type="module">/*`;
  const tail = `*/</script></body></html>`;
  const file = join(dir, name);
  writeFileSync(file, head + 'x'.repeat(Math.max(0, bytes - head.length - tail.length)) + tail);
  return file;
}

function run(file: string) {
  const result = spawnSync(process.execPath, [SCRIPT, file], { encoding: 'utf8' });
  return { code: result.status, out: `${result.stdout}${result.stderr}` };
}

describe('check-bundle-size.mjs', () => {
  it('passes a bundle under 1 MB without a warning', () => {
    const result = run(fixture('small.html', 500 * 1024));
    expect(result.code).toBe(0);
    expect(result.out).toContain('OK');
    expect(result.out).not.toContain('WARN');
  });

  it('passes but warns between 1 MB and 3 MB', () => {
    const result = run(fixture('medium.html', 2 * MB));
    expect(result.code).toBe(0);
    expect(result.out).toContain('WARN');
  });

  it('passes a bundle of exactly 3 MB', () => {
    expect(run(fixture('edge.html', 3 * MB)).code).toBe(0);
  });

  it('FAILS a bundle one byte over 3 MB (negative: the padded fixture)', () => {
    const result = run(fixture('over.html', 3 * MB + 1));
    expect(result.code).toBe(1);
    expect(result.out).toContain('FAIL');
  });

  it('FAILS when the built file is missing', () => {
    const result = run(join(dir, 'does-not-exist.html'));
    expect(result.code).toBe(1);
    expect(result.out).toContain('does not exist');
  });

  it('FAILS a page that is not self-contained (script src or stylesheet link)', () => {
    expect(run(fixture('script-src.html', 1000, '<script type="module" src="./assets/index.js"></script>')).code).toBe(
      1
    );
    expect(run(fixture('link.html', 1000, '<link rel="stylesheet" href="./assets/index.css">')).code).toBe(1);
  });
});
