// The playbook page and SemanticSearch are not part of the external SPA (spaarke-SPA-external-access-platform-r3 task 042,
// owner decision (a) 2026-10-11, FR-18, #1566): the external plane has no playbook read, analysis-create or search route.
// The real App routes run (route pages are stubs, MSAL is a fake); a direct visit to the old route must render the
// app's own 404 view, not a page, and nothing may reach the BFF.
import * as React from 'react';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    accounts: [{ homeAccountId: 'a', username: 'jane@partner.test', name: 'Jane Partner', tenantId: 'partner' }],
    instance: { logoutRedirect: vi.fn() },
    inProgress: 'none',
  }),
}));
vi.mock('../src/components/AuthGuard', () => ({
  AuthGuard: ({ children }: { children: React.ReactNode }) => children,
}));
vi.mock('../src/pages/WorkspaceHomePage', () => ({
  WorkspaceHomePage: () => React.createElement('p', null, 'HOME-STUB'),
}));
vi.mock('../src/pages/ProjectPage', () => ({ ProjectPage: () => React.createElement('p', null, 'PROJECT-STUB') }));
vi.mock('../src/pages/DocumentUploadPage', () => ({
  DocumentUploadPage: () => React.createElement('p', null, 'UPLOAD-STUB'),
}));
vi.mock('../src/pages/SettingsPage', () => ({ SettingsPage: () => React.createElement('p', null, 'SETTINGS-STUB') }));

const SRC = path.resolve(__dirname, '../src');

let fetchSpy: ReturnType<typeof vi.fn<(url: unknown) => Promise<never>>>;

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  fetchSpy = vi.fn(() => Promise.reject(new Error('unexpected fetch in a not-found route')));
  vi.stubGlobal('fetch', fetchSpy);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.pushState({}, '', '/');
});

async function visit(url: string) {
  window.history.pushState({}, '', url);
  const { App } = await import('../src/App');
  render(React.createElement(App, { teamsHost: false }));
}

describe('removed routes render the in-app not-found page', () => {
  it.each([
    '/playbooks/sprk_project/00000000-0000-0000-0000-000000000001',
    '/playbooks/sprk_matter/00000000-0000-0000-0000-000000000002?intent=summarize',
    '/playbooks',
    '/search',
    '/semantic-search',
    '/project/00000000-0000-0000-0000-000000000001/search',
  ])('%s', { timeout: 60000 }, async url => {
    await visit(url);
    expect(await screen.findByText('Page not found')).toBeTruthy();
    expect(screen.getByRole('button', { name: /Back to my workspace/ })).toBeTruthy();
    for (const stub of ['HOME-STUB', 'PROJECT-STUB', 'UPLOAD-STUB', 'SETTINGS-STUB']) {
      expect(screen.queryByText(stub), stub).toBeNull();
    }
    // No page, so no BFF call at all: nothing out of plane, nothing in plane. (The Teams SDK fetches its own
    // valid-domains list from a CDN when the module loads; that is not a BFF call.)
    const bffCalls = fetchSpy.mock.calls.map(([url]) => String(url)).filter(url => url.includes('/api/'));
    expect(bffCalls).toEqual([]);
  });

  it(
    'the surviving routes still render their pages (the 404 is not a catch-all regression)',
    { timeout: 60000 },
    async () => {
      await visit('/upload');
      expect(await screen.findByText('UPLOAD-STUB')).toBeTruthy();
      expect(screen.queryByText('Page not found')).toBeNull();
    }
  );
});

describe('nothing points at the removed pages', () => {
  it('the page and component files are gone', () => {
    for (const rel of [
      'pages/PlaybookLibraryPage.tsx',
      'components/SemanticSearch.tsx',
      'components/AiToolbar.tsx',
      'hooks/usePlaybookExecution.ts',
    ]) {
      expect(fs.existsSync(path.join(SRC, rel)), rel).toBe(false);
    }
  });

  it('no widget descriptor (library card, tab, default) names a playbook or search surface', async () => {
    const { WIDGET_DEFINITIONS } = await import('../src/registry/widgetRegistry');
    const surfaced = WIDGET_DEFINITIONS.map(d => `${d.id} ${d.title} ${d.ariaLabel} ${d.description}`).filter(text =>
      /playbook|semantic|ai[ -]?search|document search|\bsearch\b/i.test(text)
    );
    expect(surfaced).toEqual([]);
  });

  it('no quick-start action, nav link or deep link names the route', () => {
    // Every UI file that can render a link, button target or quick-start entry; comments are irrelevant to behaviour,
    // so a string literal or JSX attribute naming the route is what this looks for.
    const offenders: string[] = [];
    const walk = (dir: string) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (/\.(ts|tsx)$/.test(entry.name)) {
          const rel = path.relative(SRC, full).replace(/\\/g, '/');
          const code = fs
            .readFileSync(full, 'utf8')
            .replace(/\/\*[\s\S]*?\*\//g, '')
            .split('\n')
            .map(l => l.replace(/(^|[^:"'`\\])\/\/.*$/, '$1'))
            .join('\n');
          if (
            /['"`]\/?(playbooks|semantic-search)\b|path=["'][^"']*\/search|navigate\([^)]*(playbooks|search)/i.test(
              code
            )
          ) {
            offenders.push(rel);
          }
        }
      }
    };
    walk(SRC);
    expect(offenders).toEqual([]);
  });
});
