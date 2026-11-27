/**
 * @jest-environment node
 *
 * Source guard for the playbook run widget's isolation rules (spaarkeai-mcp-bff-api-r2 task 037c; spec FR-12, FR-16, ADR-021): it declares no
 * network origin, holds no token, stores nothing, renders no HTML from text (the run's results are model output) and uses semantic tokens only.
 * It calls only the status tool and the app-only results tool through the host. Copied from the search results widget's guard (task 035). This
 * file scans the widget's own `src/` and, after `npm run build`, the BUILT page (which also holds the UI framework, which a source scan cannot see). The shared transport and every widget's source are scanned, with the
 * same patterns, by `shared/mcp/__tests__/widgetMcpTransport.test.ts`. Must-fire controls prove each pattern matches what it is meant to catch.
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
const XRM = /\bXrm\b|getXrm/;
/** Rendering text as HTML, or navigating the frame itself: the widget uses neither (links go through the host's openLink). */
const HTML_OR_NAVIGATION =
  /dangerouslySetInnerHTML|\binnerHTML\b|\bouterHTML\b|insertAdjacentHTML|window\.open\s*\(|\blocation\.(?:href|assign|replace)\b/;

/**
 * What the BUILT page must not do. Narrower than the source patterns on purpose: the page holds the UI framework, whose code names a socket
 * type and a media query for its own reasons, neither of which this widget reaches. A request, a stored value, a cookie, a token, MSAL or
 * Xrm is the defect, wherever the code came from.
 */
const BUILT_FORBIDDEN =
  /(?<![\w.])fetch\s*\(|\.fetch\s*\(|XMLHttpRequest|sendBeacon|localStorage|sessionStorage|indexedDB|document\.cookie|acquireToken|\bBearer\b|\bmsal\b|\bXrm\b/;
const BUILT = resolve(__dirname, '../../dist/index.html');

const files = sourceFiles(SRC);
const label = (f: string) => relative(SRC, f).replace(/\\/g, '/');

describe('widget source surface', () => {
  it('scans the real source files (not an empty set)', () => {
    expect(files.map(label)).toEqual(
      expect.arrayContaining([
        'main.tsx',
        'App.tsx',
        'run/playbookRun.ts',
        'run/markdown.tsx',
        'run/useRunController.ts',
      ])
    );
  });

  it('calls fetch, XMLHttpRequest, sockets or beacons nowhere in this widget src/', () => {
    expect(files.filter(f => NETWORK.test(code(f))).map(label)).toEqual([]);
  });

  it('reads or stores no token or credential', () => {
    expect(files.filter(f => TOKEN.test(code(f))).map(label)).toEqual([]);
  });

  it('uses no browser storage', () => {
    expect(files.filter(f => STORAGE.test(code(f))).map(label)).toEqual([]);
  });

  it('touches no Xrm', () => {
    expect(files.filter(f => XRM.test(code(f))).map(label)).toEqual([]);
  });

  it('renders no HTML from text and never navigates or opens a window itself', () => {
    expect(files.filter(f => HTML_OR_NAVIGATION.test(code(f))).map(label)).toEqual([]);
  });

  it('takes the theme from the host context only (ADR-021)', () => {
    expect(files.filter(f => PAGE_THEME.test(code(f))).map(label)).toEqual([]);
  });

  it('writes no hex, rgb or hsl colour (Fluent v9 semantic tokens only, ADR-021)', () => {
    expect(files.filter(f => RAW_COLOUR.test(code(f))).map(label)).toEqual([]);
  });

  it('calls only the status tool and the app-only results tool, and never starts a run', () => {
    const source = readFileSync(join(SRC, 'run/playbookRun.ts'), 'utf8');
    expect(source).toContain("'spaarke_get_playbook_run'");
    expect(source).toContain("'spaarke_get_playbook_run_results'");
    const calls = files.flatMap(f => [...code(f).matchAll(/callTool\(\s*([A-Z_]+|'[^']+')/g)].map(m => m[1]));
    expect(new Set(calls)).toEqual(new Set(['STATUS_TOOL', 'RESULTS_TOOL']));
  });

  // dist/ exists after `npm run build`. The build script runs this file as its last step (npm_lifecycle_event=build), and there a missing dist/
  // fails instead of skipping, so the scan cannot pass vacuously in CI's build-only jobs.
  const built = existsSync(BUILT) || process.env.npm_lifecycle_event === 'build' ? it : it.skip;
  built('the built page makes no request, stores nothing and holds no token', () => {
    const html = readFileSync(BUILT, 'utf8');
    expect(html).toContain('spaarke_get_playbook_run_results'); // the guard is looking at the right file
    expect(html).not.toContain('spaarke_search_documents');
    expect(html.match(new RegExp(BUILT_FORBIDDEN.source, 'g')) ?? []).toEqual([]);
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

  it.each([['createMcpFetch(callTool, routes)'], ['authenticatedFetch(url)'], ['app.callServerTool({})']])(
    'NETWORK does not match %s',
    text => expect(NETWORK.test(text)).toBe(false)
  );

  it.each([['headers.Authorization'], ['Bearer abc'], ['access_token'], ['acquireToken()']])('TOKEN matches %s', text =>
    expect(TOKEN.test(text)).toBe(true)
  );

  it.each([['#fff'], ['rgb(0,0,0)'], ['hsl(1,2%,3%)']])('RAW_COLOUR matches %s', text =>
    expect(RAW_COLOUR.test(text)).toBe(true)
  );

  it.each([['window.Xrm.Utility'], ['getXrm()']])('XRM matches %s', text => expect(XRM.test(text)).toBe(true));

  it.each([
    ['dangerouslySetInnerHTML', '<div dangerouslySetInnerHTML={{__html: x}} />'],
    ['innerHTML', 'el.innerHTML = x'],
    ['window.open', 'window.open(url)'],
    ['location.href', 'window.location.href = url'],
  ])('HTML_OR_NAVIGATION matches %s', (_n, text) => expect(HTML_OR_NAVIGATION.test(text)).toBe(true));

  it.each([
    ['a fetch call', 'a=await fetch(u)'],
    ['a fetch method call', 'await window.fetch(u)'],
    ['a fetch method call on a stored reference', 'const r = await ref.current .fetch (u)'],
    ['XHR', 'new XMLHttpRequest'],
    ['a beacon', 'navigator.sendBeacon(u)'],
    ['localStorage', 'localStorage.getItem(k)'],
    ['sessionStorage', 'sessionStorage.setItem(k,v)'],
    ['indexedDB', 'window.indexedDB.open("db")'],
    ['a cookie', 'document.cookie="a=1"'],
    ['a bearer token', '"Bearer "+t'],
    ['MSAL', 'new msal.PublicClientApplication(c)'],
    ['a token request', 'await app.acquireToken({scopes})'],
    ['Xrm', 'window.Xrm.WebApi'],
  ])('BUILT_FORBIDDEN matches %s', (_n, text) => expect(BUILT_FORBIDDEN.test(text)).toBe(true));

  it.each([['new WebSocket'], ['matchMedia("(prefers-reduced-motion)")'], ['authenticatedFetch(u)'], ['prefetch (x)']])(
    'BUILT_FORBIDDEN does not match framework text %s',
    text => expect(BUILT_FORBIDDEN.test(text)).toBe(false)
  );
});
