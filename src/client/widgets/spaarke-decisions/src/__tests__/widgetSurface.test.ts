/**
 * @jest-environment node
 *
 * Source guard for the widget's isolation rules (task 030; spec FR-12, ADR-021): it declares no network origin, holds no token, stores
 * no theme, and uses semantic tokens only. The widget talks to the server only through `callServerTool`, so a fetch, XHR, socket or
 * beacon anywhere in its source is a defect; the one function named fetch is the shim in mcpFetch.ts, which must not call the network either.
 * Must-fire controls prove each pattern matches what it is meant to catch.
 */
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
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

/**
 * The built page, which also holds the shared library's code (a source scan cannot see it). The one browser-storage use it may contain is
 * the shared lane panel's `useLaneOpen`: the Decide lane's open or closed state, under the key below, read and written inside try/catch.
 * The POML permits exactly that (a per-device layout preference, no theme and no data). Any other storage access in the built file fails.
 */
const BUILT = resolve(__dirname, '../../dist/index.html');
const LANE_OPEN_KEY = 'spaarke.worklist.laneOpen';
const ANY_STORAGE = /localStorage|sessionStorage|indexedDB|openDatabase|document\.cookie|\bcaches\b/;
const escapeRegExp = (text: string) => text.replace(/[$()*+.?[\\\]^{|}]/g, '\\$&');

/** What remains of the built source after the allowed lane-open reads and writes are removed; empty means no other storage use. */
function unallowedStorageUse(built: string): string[] {
  const keyVars = [...built.matchAll(/(?:const|let|var)\s+([\w$]+)\s*=\s*["']spaarke\.worklist\.laneOpen["']/g)].map(m =>
    escapeRegExp(m[1])
  );
  let rest = built;
  for (const key of keyVars) {
    rest = rest
      .replace(new RegExp(`(?:window\\.)?localStorage\\.getItem\\(\\s*${key}\\s*\\)`, 'g'), '')
      .replace(new RegExp(`(?:window\\.)?localStorage\\.setItem\\(\\s*${key}\\s*,`, 'g'), '');
  }
  return [...rest.matchAll(new RegExp(ANY_STORAGE.source, 'g'))].map(m =>
    rest.slice(Math.max(0, m.index! - 30), m.index! + 60)
  );
}

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

  const built = existsSync(BUILT) ? it : it.skip; // dist/ exists after `npm run build`; the size gate test follows the same rule
  built('the built page uses browser storage for the lane open/closed state only (useLaneOpen), nothing else', () => {
    const html = readFileSync(BUILT, 'utf8');
    expect(html).toContain(LANE_OPEN_KEY); // the one allowed use is really there (the guard is looking at the right file)
    expect(unallowedStorageUse(html)).toEqual([]);
  });

  it('takes the theme from the host context only (ADR-021)', () => {
    expect(files.filter(f => PAGE_THEME.test(code(f))).map(label)).toEqual([]);
  });

  it('writes no hex, rgb or hsl colour (Fluent v9 semantic tokens only, ADR-021)', () => {
    expect(files.filter(f => RAW_COLOUR.test(code(f))).map(label)).toEqual([]);
  });
});

describe('built-file storage guard controls', () => {
  const allowed = `const Fx="${LANE_OPEN_KEY}";function Zx(){try{const e=window.localStorage.getItem(Fx);return e}catch{}}function w(t){try{window.localStorage.setItem(Fx,JSON.stringify(t))}catch{}}`;

  it('passes the lane-open read and write exactly as the shared hook builds', () => {
    expect(unallowedStorageUse(allowed)).toEqual([]);
  });

  it('allows a minified key name with a dollar sign', () => {
    const dollar = allowed.replace(/Fx/g, '$x');
    expect(unallowedStorageUse(dollar)).toEqual([]);
  });

  it.each([
    ['another localStorage key', allowed + 'localStorage.setItem("theme","dark")'],
    ['a different variable as the key', allowed + 'window.localStorage.getItem(Qq)'],
    ['another localStorage method on the lane key', allowed + 'localStorage.removeItem(Fx)'],
    ['sessionStorage', allowed + 'sessionStorage.getItem(Fx)'],
    ['indexedDB', allowed + 'indexedDB.open("x")'],
    ['a cookie', allowed + 'document.cookie="a=1"'],
  ])('fails on %s', (_name, text) => {
    expect(unallowedStorageUse(text).length).toBeGreaterThan(0);
  });

  it('fails on localStorage when no lane key is declared at all', () => {
    expect(unallowedStorageUse('window.localStorage.getItem(Fx)').length).toBeGreaterThan(0);
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
