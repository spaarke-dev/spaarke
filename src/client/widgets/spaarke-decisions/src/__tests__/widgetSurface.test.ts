/**
 * @jest-environment node
 *
 * Source guard for the widget's isolation rules (task 030; spec FR-12, ADR-021): it declares no network origin, holds no token, stores
 * no theme, and uses semantic tokens only. The widget talks to the server only through `callServerTool`, so a fetch, XHR, socket or
 * beacon anywhere in its source is a defect; the one function named fetch is the shim in mcpFetch.ts, which must not call the network either.
 * Must-fire controls prove each pattern matches what it is meant to catch.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';

const SRC = resolve(__dirname, '..');

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap(name => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) return name === '__tests__' ? [] : sourceFiles(full);
    return /\.(ts|tsx)$/.test(name) ? [full] : [];
  });
}

/** Source without comments, so prose that names a forbidden API does not trip the guard. */
function code(file: string): string {
  return readFileSync(file, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/.*$/gm, '$1');
}

const NETWORK =
  /(?<![\w.])fetch\s*\(|\b(?:window|globalThis|self)\.fetch\b|XMLHttpRequest|\bEventSource\b|\bWebSocket\b|sendBeacon/;
const TOKEN = /Authorization|\bBearer\b|access_?token|id_?token|acquireToken|getToken|\bmsal\b|document\.cookie/i;
const STORAGE = /localStorage|sessionStorage|indexedDB|document\.cookie/;
const PAGE_THEME = /detectDarkModeFromNavbar|matchMedia|prefers-color-scheme/;
const RAW_COLOUR = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;

const files = sourceFiles(SRC);
const label = (f: string) => relative(SRC, f).replace(/\\/g, '/');

describe('widget source surface', () => {
  it('scans the real source files (not an empty set)', () => {
    const names = files.map(label);
    expect(names).toEqual(
      expect.arrayContaining([
        'main.tsx',
        'App.tsx',
        'mcp/mcpFetch.ts',
        'mcp/mcpHost.ts',
        'theme/hostTheme.ts',
        'lane/DecideLane.tsx',
        'lane/decideLaneFetch.ts',
        'lane/billingLinkOut.ts',
      ])
    );
  });

  it('calls fetch, XMLHttpRequest, sockets or beacons nowhere, including inside the shim', () => {
    expect(files.filter(f => NETWORK.test(code(f))).map(label)).toEqual([]);
  });

  it('reads or stores no token or credential', () => {
    expect(files.filter(f => TOKEN.test(code(f))).map(label)).toEqual([]);
  });

  it('uses no browser storage (the theme is never stored)', () => {
    expect(files.filter(f => STORAGE.test(code(f))).map(label)).toEqual([]);
  });

  it('takes the theme from the host context only (ADR-021)', () => {
    expect(files.filter(f => PAGE_THEME.test(code(f))).map(label)).toEqual([]);
  });

  it('writes no hex, rgb or hsl colour (Fluent v9 semantic tokens only, ADR-021)', () => {
    expect(files.filter(f => RAW_COLOUR.test(code(f))).map(label)).toEqual([]);
  });
});

describe('guard controls (each pattern fires on what it is meant to catch)', () => {
  it.each([
    ['fetch call', 'await fetch(url)'],
    ['window.fetch', 'window.fetch'],
    ['XHR', 'new XMLHttpRequest()'],
    ['socket', 'new WebSocket(u)'],
    ['beacon', 'navigator.sendBeacon(u)'],
  ])('NETWORK matches a %s', (_n, text) => expect(NETWORK.test(text)).toBe(true));

  it.each([['createMcpFetch(callTool, routes)'], ['const fetchTypes = 1'], ['app.callServerTool({})']])(
    'NETWORK does not match %s',
    text => expect(NETWORK.test(text)).toBe(false)
  );

  it.each([['headers.Authorization'], ['Bearer abc'], ['access_token'], ['acquireToken()'], ['document.cookie']])(
    'TOKEN matches %s',
    text => expect(TOKEN.test(text)).toBe(true)
  );

  it('TOKEN does not match Fluent design tokens', () => {
    expect(TOKEN.test('tokens.colorNeutralBackground1')).toBe(false);
  });

  it.each([['localStorage.setItem'], ['sessionStorage'], ['indexedDB.open']])('STORAGE matches %s', text =>
    expect(STORAGE.test(text)).toBe(true)
  );

  it.each([['#fff'], ['#1a2b3c'], ['rgb(0,0,0)'], ['rgba(0,0,0,.5)'], ['hsl(1,2%,3%)']])(
    'RAW_COLOUR matches %s',
    text => expect(RAW_COLOUR.test(text)).toBe(true)
  );

  it('RAW_COLOUR does not match token references', () => {
    expect(RAW_COLOUR.test('tokens.colorNeutralBackground1')).toBe(false);
  });

  it('code() drops comments so documentation can name a forbidden API', () => {
    const stripped = 'a /* fetch( */ b // XMLHttpRequest\nc'
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/(^|[^:])\/\/.*$/gm, '$1');
    expect(NETWORK.test(stripped)).toBe(false);
  });
});
