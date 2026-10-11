// @vitest-environment node
// (node: this file imports vite.config.ts, whose esbuild refuses jsdom's TextEncoder; nothing here needs a DOM.)
//
// Out-of-plane routes and production mock mode (spaarke-SPA-external-access-platform-r3 task 042, FR-18, #1566).
//
// 1. A source scan of src/: no BFF route outside /api/v1/external/**, no shared adapter that builds one, no page-built
//    token-acquiring fetch, no direct read of the mock flag, and no playbook / search / AI surface. The allow-list is
//    EMPTY and there is no pending list: the owner chose (a) on 2026-10-11 — the playbook page and SemanticSearch are
//    removed from the external SPA (the external plane has no playbook read, analysis-create or search route), so any
//    file that calls one is a violation wherever it sits.
// 2. EVERY build (any --mode, any NODE_ENV) refuses VITE_DEV_MOCK=true and pins it to "false" (vite.config.ts
//    `refuseDevMockInBuild`, `devMockBuildDefine`).
import * as fs from 'node:fs';
import * as path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';

const SRC = path.resolve(__dirname, '../src');

/** Deliberate exceptions — none (POML step 6). */
const ALLOWED: Readonly<Record<string, string>> = {};

/** Files that may acquire a BFF token or call fetch: the auth layer and the 041 grid client (both external-only). */
const TOKEN_FETCH_OWNERS = (rel: string) => rel.startsWith('auth/') || rel === 'services/gridDataverseClient.ts';

/** Files that may name the mock flag: the single switch and the env typing. */
const DEV_MOCK_OWNERS = new Set(['dev-mock.ts', 'vite-env.d.ts']);

/** Strip block and line comments (a `//` after `:` — a URL scheme — or a quote is kept). */
function stripComments(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, m => m.replace(/[^\n]/g, ' '))
    .split('\n')
    .map(line => line.replace(/(^|[^:"'`\\])\/\/.*$/, '$1'))
    .join('\n');
}

interface Violation {
  file: string;
  line: number;
  rule: string;
  text: string;
}

/** Scan one source file's text. `rel` is its path under src/ with forward slashes. */
function scanSource(rel: string, source: string): Violation[] {
  const found: Violation[] = [];
  const lines = stripComments(source).split('\n');
  lines.forEach((text, i) => {
    const hit = (rule: string) => found.push({ file: rel, line: i + 1, rule, text: text.trim() });
    // A BFF route: `/api/…` not preceded by `.` (a relative module path such as '../api/me-client') once every
    // external-plane path is removed — `/api/v1/external` followed by `/`, a quote or the end, with the rest of its
    // path (`/api/v1/external/api/dataverse` is external; `/api/v1/external-access` is NOT).
    const withoutExternal = text.replace(/\/api\/v1\/external(?![\w-])[^'"`\s)]*/g, '');
    if (/(?<!\.)\/api\//.test(withoutExternal)) hit('route outside /api/v1/external');
    // Shared adapters that build their own non-external routes (/api/dataverse, /api/obo, /api/ai/analysis).
    if (
      /from\s+['"][^'"]*(utils\/adapters\/bff(DataService|UploadService)Adapter|components\/PlaybookLibraryShell|components\/Playbook\/analysisService)['"]/.test(
        text
      )
    )
      hit('shared adapter with non-external routes');
    // The playbook / search / AI surface is not part of the external SPA (owner decision (a), task 042): the external
    // plane maps no `/api/v1/external/{ai,search,playbook}` route, so a call to one is dead and a re-added page that
    // reaches the shared playbook / search UI would call the workforce routes.
    if (/\/api\/v1\/external\/(ai|search|playbooks?)(?![\w-])/.test(text)) hit('playbook / search / AI route');
    if (
      /from\s+['"][^'"]*(ui-components[^'"]*\/(Playbook|SemanticSearch)[^'"]*|\/(PlaybookLibraryPage|SemanticSearch|AiToolbar|usePlaybookExecution))['"]/.test(
        text
      )
    )
      hit('playbook / search / AI module');
    if (
      !TOKEN_FETCH_OWNERS(rel) &&
      /\b(acquireBffToken|acquireActiveBffToken)\b|(?<![\w.])fetch\(|window\.fetch\(/.test(text)
    )
      hit('page-built token / fetch');
    if (!DEV_MOCK_OWNERS.has(rel) && /VITE_DEV_MOCK/.test(text)) hit('direct mock-flag read');
  });
  return found;
}

function sourceFiles(dir: string): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...sourceFiles(full));
    else if (/\.(ts|tsx)$/.test(entry.name)) out.push(full);
  }
  return out;
}

function scanSrc(): Violation[] {
  return sourceFiles(SRC).flatMap(full =>
    scanSource(path.relative(SRC, full).replace(/\\/g, '/'), fs.readFileSync(full, 'utf8'))
  );
}

describe('source scan: every BFF call from src/ is on the external plane', () => {
  it('no violation outside the (empty) allow-list', () => {
    const unexpected = scanSrc().filter(v => !(v.file in ALLOWED));
    expect(unexpected.map(v => `${v.file}:${v.line} [${v.rule}] ${v.text}`)).toEqual([]);
  });

  it('the allow-list is empty', () => {
    expect(Object.keys(ALLOWED)).toEqual([]);
  });

  it('the scan walks the source tree (a vacuous pass would hide everything)', () => {
    const files = sourceFiles(SRC).map(f => path.relative(SRC, f).replace(/\\/g, '/'));
    expect(files).toEqual(expect.arrayContaining(['App.tsx', 'auth/bff-client.ts', 'pages/DocumentUploadPage.tsx']));
  });

  it('DocumentUploadPage is clean (external upload route via bffApiCall)', () => {
    expect(scanSrc().filter(v => v.file === 'pages/DocumentUploadPage.tsx')).toEqual([]);
  });

  describe('scanner controls', () => {
    it.each([
      ["bffApiCall('/api/ai/search', { method: 'POST' })", 'route outside /api/v1/external'],
      ['const url = `${baseUrl}/api/dataverse/${entity}`;', 'route outside /api/v1/external'],
      ["bffApiCall('/api/v1/external-access/grant')", 'route outside /api/v1/external'],
      ["fetch('https://host.example/api/obo/records/x')", 'route outside /api/v1/external'],
      [
        "import { createBffUploadService } from '@spaarke/ui-components/utils/adapters/bffUploadServiceAdapter';",
        'shared adapter with non-external routes',
      ],
      [
        "import type { AuthenticatedFetch } from '@spaarke/ui-components/utils/adapters/bffDataServiceAdapter';",
        'shared adapter with non-external routes',
      ],
      ['const token = await acquireBffToken();', 'page-built token / fetch'],
      ["bffApiCall('/api/v1/external/ai/playbook', { method: 'POST' })", 'playbook / search / AI route'],
      ["bffApiCall('/api/v1/external/search', { method: 'POST' })", 'playbook / search / AI route'],
      ["bffApiCall('/api/v1/external/playbooks')", 'playbook / search / AI route'],
      [
        "import { PlaybookLibraryShell } from '@spaarke/ui-components/components/PlaybookLibraryShell';",
        'playbook / search / AI module',
      ],
      ["import { SemanticSearch } from '../components/SemanticSearch';", 'playbook / search / AI module'],
      ["import { PlaybookLibraryPage } from './pages/PlaybookLibraryPage';", 'playbook / search / AI module'],
      ["import { usePlaybookExecution } from '../hooks/usePlaybookExecution';", 'playbook / search / AI module'],
      ['return fetch(url, init);', 'page-built token / fetch'],
      ["if (import.meta.env.VITE_DEV_MOCK === 'true') {", 'direct mock-flag read'],
    ])('fires on %s', (line, rule) => {
      expect(scanSource('pages/Sample.tsx', line).map(v => v.rule)).toContain(rule);
    });

    it.each([
      "bffApiCall('/api/v1/external/me')",
      "bffApiCall('/api/v1/external/airline-notes')",
      "import { SettingsPage } from './pages/SettingsPage';",
      'bffApiCall(`/api/v1/external/${root.segment}/${id}/documents`)',
      'return `${backend.bffBaseUrl}/api/v1/external`;',
      "const EXTERNAL_DATA_PATH = '/api/v1/external/api/dataverse';",
      "import { getProjects } from '../api/web-api-client';",
      '// was POST /api/ai/search before task 042',
      '/* the /api/dataverse proxy */',
      "const x = dataService.fetch('a');",
      'if (isDevMockEnabled()) {',
    ])('does not fire on %s', line => {
      expect(scanSource('pages/Sample.tsx', line)).toEqual([]);
    });

    it('the auth layer and the grid client may acquire tokens and fetch; nobody else may', () => {
      expect(scanSource('auth/bff-client.ts', 'return fetch(url, init);')).toEqual([]);
      expect(scanSource('services/gridDataverseClient.ts', 'await acquireActiveBffToken(backend);')).toEqual([]);
      expect(scanSource('hooks/useX.ts', 'await acquireActiveBffToken(backend);')).toHaveLength(1);
    });
  });
});

describe('every build refuses VITE_DEV_MOCK=true and pins it off (vite.config.ts)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  async function guardPlugin() {
    const config = (await import('../vite.config')).default as { plugins: Array<{ name?: string; config?: unknown }> };
    const plugin = config.plugins.flat().find(p => p && p.name === 'spaarke-refuse-dev-mock-in-production');
    expect(plugin, 'the guard plugin is registered').toBeTruthy();
    return (command: 'build' | 'serve', mode: string) =>
      (plugin!.config as (c: object, e: { command: string; mode: string }) => unknown).call({}, {}, { command, mode });
  }

  it('a production build with the flag in the environment fails', async () => {
    vi.stubEnv('VITE_DEV_MOCK', 'true');
    const run = await guardPlugin();
    expect(() => run('build', 'production')).toThrow(/VITE_DEV_MOCK=true is refused for a build/);
  });

  it.each(['staging', 'development', 'test', 'preview'])(
    'a "%s"-mode build with the flag in the environment fails (not only --mode production)',
    async mode => {
      vi.stubEnv('VITE_DEV_MOCK', 'true');
      const run = await guardPlugin();
      expect(() => run('build', mode)).toThrow(/VITE_DEV_MOCK=true is refused for a build/);
    }
  );

  it('a build under NODE_ENV=development (where Vite reports DEV === true) with the flag still fails', async () => {
    vi.stubEnv('NODE_ENV', 'development');
    vi.stubEnv('VITE_DEV_MOCK', 'true');
    const run = await guardPlugin();
    expect(() => run('build', 'staging')).toThrow(/refused for a build/);
  });

  it('a build without the flag, and the dev server with it, are allowed', async () => {
    const run = await guardPlugin();
    expect(() => run('build', 'production')).not.toThrow();
    expect(() => run('build', 'staging')).not.toThrow();
    vi.stubEnv('VITE_DEV_MOCK', 'true');
    expect(() => run('serve', 'development')).not.toThrow();
  });

  it('every build pins import.meta.env.VITE_DEV_MOCK to the literal "false"; the dev server does not', async () => {
    const run = await guardPlugin();
    expect(run('build', 'production')).toEqual({ define: { 'import.meta.env.VITE_DEV_MOCK': '"false"' } });
    expect(run('build', 'staging')).toEqual({ define: { 'import.meta.env.VITE_DEV_MOCK': '"false"' } });
    expect(run('serve', 'development')).toEqual({ define: {} });
  });

  it.each([
    ['build', 'production', 'true', true],
    ['build', 'production', 'TRUE', false], // the exact value 'true' is the only one dev-mock.ts honours
    ['build', 'staging', 'true', true],
    ['build', 'development', 'true', true],
    ['build', 'anything-else', 'true', true],
    ['build', 'production', 'false', false],
    ['build', 'staging', 'false', false],
    ['build', 'production', '', false],
    ['build', 'production', undefined, false],
    ['serve', 'production', 'true', false],
    ['serve', 'development', 'true', false],
  ] as const)('refuseDevMockInBuild(%s, %s, %s) throws: %s', async (command, mode, flag, throws) => {
    const { refuseDevMockInBuild } = await import('../vite.config');
    // `mode` is deliberately not an argument: no mode is exempt.
    void mode;
    const call = () => refuseDevMockInBuild(command, flag);
    if (throws) expect(call).toThrow();
    else expect(call).not.toThrow();
  });
});
