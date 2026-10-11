// @vitest-environment node
//
// The dist checker's pure functions (scripts/check-dist.mjs; task 042 review, FR-18, #1566): must-fire and
// must-not-fire controls on fixture strings shaped like the minified bundle, plus the ratchet in both directions.
// `npm run check:dist` runs the same functions over the real build output.
import { describe, expect, it } from 'vitest';
import {
  MOCK_MARKERS,
  OUT_OF_PLANE_BASELINE,
  evaluateScan,
  mergeCounts,
  scanDistText,
  // @ts-expect-error - plain .mjs script, no type declarations
} from '../scripts/check-dist.mjs';

const judge = (text: string, baseline: Record<string, number> = {}): string[] =>
  evaluateScan(scanDistText(text), baseline);

describe('check-dist: mock markers (must fire)', () => {
  it.each(MOCK_MARKERS as string[])('a bundle containing "%s" fails', marker => {
    const findings = judge(`var a=1;var u={name:"${marker}"};`);
    expect(findings.some(f => f.includes(`mock marker "${marker}"`))).toBe(true);
  });

  it('a VITE_DEV_MOCK that is not the pinned "false" fails', () => {
    expect(judge('const E={DEV:!0,VITE_DEV_MOCK:"true"};')).toHaveLength(1);
    expect(judge('const k="VITE_DEV_MOCK";')).toHaveLength(1);
  });

  it('counts every occurrence', () => {
    expect(scanDistText('"Jane Smith","Jane Smith"').markers).toEqual({ 'Jane Smith': 2 });
  });
});

describe('check-dist: mock markers (must not fire)', () => {
  it('the build-pinned env constant VITE_DEV_MOCK:"false" is tolerated', () => {
    expect(
      judge('const E={BASE_URL:"/",DEV:!1,MODE:"production",VITE_DEV_MOCK:"false",VITE_MSAL_CLIENT_ID:"x"};')
    ).toEqual([]);
  });

  it('ordinary text near a marker word does not fire', () => {
    expect(judge('const s="Smith & Jane law firm"; const r="Rivera";')).toEqual([]);
  });
});

describe('check-dist: out-of-plane /api/ strings', () => {
  it('fires on a route outside /api/v1/external', () => {
    const findings = judge('fetch(`${base}/api/ai/search`)');
    expect(findings).toHaveLength(1);
    expect(findings[0]).toContain('"/api/ai/search"');
  });

  it.each([
    '"/api/obo/"',
    '"/api/documents/upload"',
    '"/api/containers/"',
    '`${t}/api/dataverse/${id}`',
    '"/api/v1/internal/me"',
    '"/api/v1/externalx"', // a prefix lookalike
    '"/api/v10/external/me"',
  ])('fires on %s', text => {
    expect(judge(`x=${text};`)).toHaveLength(1);
  });

  it.each([
    '"/api/v1/external/me"',
    '"/api/v1/external"',
    '`${e.bffBaseUrl}/api/v1/external`',
    '`/api/v1/external/projects/${e}/documents`',
    '"/api/v1/external/api/dataverse"', // the grid client's path under the external prefix
    '"/api/v1/external?x=1"',
    '"https://example.test/not-an-api-path"',
  ])('does not fire on %s', text => {
    expect(judge(`x=${text};`)).toEqual([]);
  });

  it('reports the fragment up to the template hole', () => {
    expect(scanDistText('`${r}/api/dataverse/${encodeURIComponent(i)}`').apiPaths).toEqual({ '/api/dataverse/': 1 });
  });
});

describe('check-dist: the ratchet baseline', () => {
  const baseline = { '/api/ai/analysis/create': 1, '/api/dataverse/fetch': 2 };
  const known = '`${t}/api/ai/analysis/create`; a=`${b}/api/dataverse/fetch`; c=`${d}/api/dataverse/fetch`;';

  it('exactly the baseline passes', () => {
    expect(judge(known, baseline)).toEqual([]);
  });

  it('a new string fails', () => {
    const findings = judge(`${known} z="/api/ai/search";`, baseline);
    expect(findings).toHaveLength(1);
    expect(findings[0]).toContain('"/api/ai/search"');
  });

  it('a baselined string used more often than the baseline allows fails', () => {
    const findings = judge(`${known} z="/api/ai/analysis/create";`, baseline);
    expect(findings).toHaveLength(1);
    expect(findings[0]).toContain('baseline allows 1');
  });

  it('a baseline entry that disappears fails, so the baseline shrinks with the code', () => {
    const findings = judge('a=`${b}/api/dataverse/fetch`; c=`${d}/api/dataverse/fetch`;', baseline);
    expect(findings).toHaveLength(1);
    expect(findings[0]).toContain('baseline entry "/api/ai/analysis/create"');
  });

  it('a baseline entry that appears fewer times than listed fails', () => {
    const findings = judge('`${t}/api/ai/analysis/create`; a=`${b}/api/dataverse/fetch`;', baseline);
    expect(findings).toHaveLength(1);
    expect(findings[0]).toContain('baseline entry "/api/dataverse/fetch" expects 2');
  });

  it('counts add up across files', () => {
    const merged = mergeCounts({}, scanDistText('"/api/x/"').apiPaths);
    mergeCounts(merged, scanDistText('"/api/x/"').apiPaths);
    expect(merged).toEqual({ '/api/x/': 2 });
  });
});

describe('check-dist: the shipped baseline lists only the two pending features', () => {
  it('is the playbook create route and the shared dataverse adapter family, nothing else', () => {
    const keys = Object.keys(OUT_OF_PLANE_BASELINE).sort();
    expect(keys[0]).toBe('/api/ai/analysis/create');
    expect(keys.slice(1).every(k => k.startsWith('/api/dataverse/'))).toBe(true);
  });
});
