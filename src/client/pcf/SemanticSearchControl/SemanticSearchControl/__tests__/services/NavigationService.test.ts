/**
 * Unit tests for NavigationService — focused on the
 * `buildSemanticSearchEnvelope` envelope contract (FR-PCF-03 + FR-PARITY-01).
 *
 * The encoder MUST:
 * - Include EVERY parity key when set: query, scope, entityId, threshold,
 *   searchMode, fileTypes, dateFrom, dateTo, tags, associatedOnly, theme,
 *   searchIndexName (12 total).
 * - OMIT each key at its default / empty value to keep the URL short.
 * - Round-trip correctly via `URLSearchParams` decoding — which is what
 *   `parseUrlParams` in the code page uses after unwrapping the outer
 *   `?data=...` envelope (see
 *   `src/client/code-pages/SemanticSearch/src/utils/parseUrlParams.ts`).
 *
 * Round-trip note: this test deliberately re-implements ONLY the decode
 * step (`URLSearchParams(decoded)`) instead of importing the code-page
 * `parseUrlParams` — the PCF `tsconfig.json` does not include
 * `src/client/code-pages/...` in its rootDir, and adding it is out of
 * scope for this task. The contract under test is the URL-encoded
 * `key=value&...` shape, which `URLSearchParams` fully verifies; the
 * decode-side parser is independently covered by task 040 tests.
 *
 * @see NavigationService.ts (buildSemanticSearchEnvelope)
 * @see spec.md FR-PCF-03 + FR-PARITY-01
 */

// Mock `@spaarke/auth` BEFORE importing the SUT — NavigationService imports
// `resolveTenantIdSync` from `@spaarke/auth`, which ships as ESM and isn't
// transformed by ts-jest. The envelope encoder doesn't use auth, but the
// module-load path does. `jest.mock` is hoisted above imports.
jest.mock('@spaarke/auth', () => ({
  resolveTenantIdSync: jest.fn().mockReturnValue('test-tenant'),
  authenticatedFetch: jest.fn(),
}));

// Mock `./ThemeService` — its `getEffectiveDarkMode` transitively imports
// `@spaarke/ui-components` (ESM), which ts-jest does not transform.
// The envelope encoder does not use ThemeService.
jest.mock('../../services/ThemeService', () => ({
  getEffectiveDarkMode: jest.fn().mockReturnValue(false),
}));

import { buildSemanticSearchEnvelope } from '../../services/NavigationService';

/**
 * Decode an envelope string into a key->string map, simulating the
 * code-page parser's `new URLSearchParams(decodeURIComponent(data))` flow.
 * Values are returned as strings (single-value per key).
 */
function decodeEnvelope(envelope: string): Record<string, string> {
  const params = new URLSearchParams(envelope);
  const out: Record<string, string> = {};
  params.forEach((v, k) => {
    out[k] = v;
  });
  return out;
}

describe('buildSemanticSearchEnvelope', () => {
  // -------------------------------------------------------------------------
  // FR-PCF-03 — searchIndexName presence / omission
  // -------------------------------------------------------------------------
  describe('searchIndexName (FR-PCF-03)', () => {
    it('encodes searchIndexName when provided', () => {
      const env = buildSemanticSearchEnvelope({}, undefined, 'spaarke-file-index');
      expect(decodeEnvelope(env).searchIndexName).toBe('spaarke-file-index');
    });

    it('omits searchIndexName when empty string', () => {
      const env = buildSemanticSearchEnvelope({}, undefined, '');
      expect(env).not.toContain('searchIndexName');
    });

    it('omits searchIndexName when whitespace-only', () => {
      const env = buildSemanticSearchEnvelope({}, undefined, '   ');
      expect(env).not.toContain('searchIndexName');
    });

    it('omits searchIndexName when undefined', () => {
      const env = buildSemanticSearchEnvelope({}, undefined, undefined);
      expect(env).not.toContain('searchIndexName');
    });

    it('trims surrounding whitespace from searchIndexName', () => {
      const env = buildSemanticSearchEnvelope({}, undefined, '  spaarke-file-index  ');
      expect(decodeEnvelope(env).searchIndexName).toBe('spaarke-file-index');
    });
  });

  // -------------------------------------------------------------------------
  // FR-PARITY-01 — every set parameter included
  // -------------------------------------------------------------------------
  describe('FR-PARITY-01: every key encoded when set (all 12 keys)', () => {
    it('encodes ALL 12 parity keys at once when every field is non-default', () => {
      const env = buildSemanticSearchEnvelope(
        {
          query: 'contract review Q4',
          scope: 'matter',
          entityId: '11111111-2222-3333-4444-555555555555',
          threshold: 50,
          searchMode: 'vectorOnly',
          fileTypes: ['pdf', 'docx'],
          dateFrom: '2026-01-01T00:00:00Z',
          dateTo: '2026-06-01T00:00:00Z',
          tags: ['confidential', 'litigation'],
          associatedOnly: true,
        },
        'dark',
        'spaarke-file-index'
      );

      const decoded = decodeEnvelope(env);

      expect(decoded.query).toBe('contract review Q4');
      expect(decoded.scope).toBe('matter');
      expect(decoded.entityId).toBe('11111111-2222-3333-4444-555555555555');
      expect(decoded.threshold).toBe('50');
      expect(decoded.searchMode).toBe('vectorOnly');
      expect(decoded.fileTypes).toBe('pdf,docx');
      expect(decoded.dateFrom).toBe('2026-01-01T00:00:00Z');
      expect(decoded.dateTo).toBe('2026-06-01T00:00:00Z');
      expect(decoded.tags).toBe('confidential,litigation');
      expect(decoded.associatedOnly).toBe('true');
      expect(decoded.theme).toBe('dark');
      expect(decoded.searchIndexName).toBe('spaarke-file-index');

      // 12 keys total
      expect(Object.keys(decoded).sort()).toEqual(
        [
          'associatedOnly',
          'dateFrom',
          'dateTo',
          'entityId',
          'fileTypes',
          'query',
          'scope',
          'searchIndexName',
          'searchMode',
          'tags',
          'theme',
          'threshold',
        ].sort()
      );
    });
  });

  // -------------------------------------------------------------------------
  // FR-PARITY-01 — every default / empty value omitted
  // -------------------------------------------------------------------------
  describe('FR-PARITY-01: omit-on-default for each key', () => {
    it('produces an empty envelope when all values are defaults / empty', () => {
      const env = buildSemanticSearchEnvelope(
        {
          query: '',
          scope: 'all',
          entityId: '',
          threshold: 0,
          searchMode: 'hybrid',
          fileTypes: [],
          dateFrom: '',
          dateTo: '',
          tags: [],
          associatedOnly: false,
        },
        '',
        ''
      );
      expect(env).toBe('');
    });

    it('omits query when empty string', () => {
      const env = buildSemanticSearchEnvelope({ query: '' }, undefined, undefined);
      expect(env).not.toContain('query');
    });

    it('omits query when whitespace-only', () => {
      const env = buildSemanticSearchEnvelope({ query: '   ' }, undefined, undefined);
      expect(env).not.toContain('query');
    });

    it('omits scope when "all" (parser default)', () => {
      const env = buildSemanticSearchEnvelope({ scope: 'all' }, undefined, undefined);
      expect(env).not.toContain('scope');
    });

    it('omits scope when empty string', () => {
      const env = buildSemanticSearchEnvelope({ scope: '' }, undefined, undefined);
      expect(env).not.toContain('scope');
    });

    it('omits entityId when empty', () => {
      const env = buildSemanticSearchEnvelope({ entityId: '' }, undefined, undefined);
      expect(env).not.toContain('entityId');
    });

    it('omits entityId when null', () => {
      const env = buildSemanticSearchEnvelope({ entityId: null }, undefined, undefined);
      expect(env).not.toContain('entityId');
    });

    it('omits threshold when 0 (no-floor default)', () => {
      const env = buildSemanticSearchEnvelope({ threshold: 0 }, undefined, undefined);
      expect(env).not.toContain('threshold');
    });

    it('omits searchMode when "hybrid" (default)', () => {
      const env = buildSemanticSearchEnvelope({ searchMode: 'hybrid' }, undefined, undefined);
      expect(env).not.toContain('searchMode');
    });

    it('omits fileTypes when empty array', () => {
      const env = buildSemanticSearchEnvelope({ fileTypes: [] }, undefined, undefined);
      expect(env).not.toContain('fileTypes');
    });

    it('omits dateFrom when empty / null', () => {
      const env1 = buildSemanticSearchEnvelope({ dateFrom: '' }, undefined, undefined);
      const env2 = buildSemanticSearchEnvelope({ dateFrom: null }, undefined, undefined);
      expect(env1).not.toContain('dateFrom');
      expect(env2).not.toContain('dateFrom');
    });

    it('omits dateTo when empty / null', () => {
      const env1 = buildSemanticSearchEnvelope({ dateTo: '' }, undefined, undefined);
      const env2 = buildSemanticSearchEnvelope({ dateTo: null }, undefined, undefined);
      expect(env1).not.toContain('dateTo');
      expect(env2).not.toContain('dateTo');
    });

    it('omits tags when empty array', () => {
      const env = buildSemanticSearchEnvelope({ tags: [] }, undefined, undefined);
      expect(env).not.toContain('tags');
    });

    it('omits associatedOnly when false (default)', () => {
      const env = buildSemanticSearchEnvelope({ associatedOnly: false }, undefined, undefined);
      expect(env).not.toContain('associatedOnly');
    });

    it('omits theme when empty / undefined', () => {
      const env1 = buildSemanticSearchEnvelope({}, '', undefined);
      const env2 = buildSemanticSearchEnvelope({}, undefined, undefined);
      const env3 = buildSemanticSearchEnvelope({}, '   ', undefined);
      expect(env1).not.toContain('theme');
      expect(env2).not.toContain('theme');
      expect(env3).not.toContain('theme');
    });
  });

  // -------------------------------------------------------------------------
  // Type-specific encoding behavior
  // -------------------------------------------------------------------------
  describe('per-type encoding behavior', () => {
    it('URL-encodes special characters in query', () => {
      const env = buildSemanticSearchEnvelope({ query: 'foo & bar = baz?' }, undefined, undefined);
      // Raw substring must NOT contain a literal & or = from the value
      // (only the structural separators).
      // Decoded form must round-trip the original value.
      expect(decodeEnvelope(env).query).toBe('foo & bar = baz?');
    });

    it('strips curly braces from entityId (Dataverse GUID literal)', () => {
      const env = buildSemanticSearchEnvelope({ entityId: '{abc-123-def}' }, undefined, undefined);
      expect(decodeEnvelope(env).entityId).toBe('abc-123-def');
    });

    it('encodes searchMode as "vectorOnly" literal', () => {
      const env = buildSemanticSearchEnvelope({ searchMode: 'vectorOnly' }, undefined, undefined);
      expect(decodeEnvelope(env).searchMode).toBe('vectorOnly');
    });

    it('encodes searchMode as "keywordOnly" literal', () => {
      const env = buildSemanticSearchEnvelope({ searchMode: 'keywordOnly' }, undefined, undefined);
      expect(decodeEnvelope(env).searchMode).toBe('keywordOnly');
    });

    it('encodes fileTypes as CSV', () => {
      const env = buildSemanticSearchEnvelope({ fileTypes: ['pdf', 'docx', 'xlsx'] }, undefined, undefined);
      expect(decodeEnvelope(env).fileTypes).toBe('pdf,docx,xlsx');
    });

    it('encodes tags as CSV', () => {
      const env = buildSemanticSearchEnvelope({ tags: ['confidential', 'litigation'] }, undefined, undefined);
      expect(decodeEnvelope(env).tags).toBe('confidential,litigation');
    });

    it('encodes associatedOnly=true as literal "true"', () => {
      const env = buildSemanticSearchEnvelope({ associatedOnly: true }, undefined, undefined);
      expect(decodeEnvelope(env).associatedOnly).toBe('true');
    });

    it('encodes threshold as numeric string', () => {
      const env = buildSemanticSearchEnvelope({ threshold: 75 }, undefined, undefined);
      expect(decodeEnvelope(env).threshold).toBe('75');
    });

    it('round-trips dateFrom / dateTo as ISO strings', () => {
      const from = '2026-01-01T00:00:00Z';
      const to = '2026-06-01T23:59:59Z';
      const env = buildSemanticSearchEnvelope({ dateFrom: from, dateTo: to }, undefined, undefined);
      const decoded = decodeEnvelope(env);
      expect(decoded.dateFrom).toBe(from);
      expect(decoded.dateTo).toBe(to);
    });

    it('guards against non-finite threshold (NaN treated as default)', () => {
      const env = buildSemanticSearchEnvelope({ threshold: NaN }, undefined, undefined);
      expect(env).not.toContain('threshold');
    });
  });

  // -------------------------------------------------------------------------
  // Envelope shape (URL-encoded key=value&...) — structural sanity
  // -------------------------------------------------------------------------
  describe('envelope structural shape', () => {
    it('returns empty string when filters/theme/index are all undefined', () => {
      const env = buildSemanticSearchEnvelope(undefined, undefined, undefined);
      expect(env).toBe('');
    });

    it('joins pairs with single & (no leading/trailing separator)', () => {
      const env = buildSemanticSearchEnvelope({ query: 'hello', scope: 'matter' }, 'dark', 'idx');
      expect(env.startsWith('&')).toBe(false);
      expect(env.endsWith('&')).toBe(false);
      // No double-ampersands
      expect(env.includes('&&')).toBe(false);
      // Each pair has key=value structure
      env.split('&').forEach(pair => {
        expect(pair.split('=').length).toBeGreaterThanOrEqual(2);
      });
    });

    it('produces a string that URLSearchParams can round-trip (parser-compatible)', () => {
      // This mirrors the code-page parser's decode step:
      //   const params = new URLSearchParams(decodeURIComponent(dataEnvelope));
      // ...with the outer decodeURIComponent already applied because the
      // PCF wraps the envelope in encodeURIComponent before navigateTo.
      const env = buildSemanticSearchEnvelope(
        {
          query: 'foo bar',
          scope: 'matter',
          entityId: '{abc}',
          threshold: 50,
          searchMode: 'hybrid', // omitted (default)
          fileTypes: ['pdf'],
          tags: ['x', 'y'],
          associatedOnly: true,
        },
        'dark',
        'idx-1'
      );
      const decoded = decodeEnvelope(env);
      // searchMode='hybrid' is the default → must NOT appear
      expect(decoded.searchMode).toBeUndefined();
      // Everything else present
      expect(decoded.query).toBe('foo bar');
      expect(decoded.scope).toBe('matter');
      expect(decoded.entityId).toBe('abc');
      expect(decoded.threshold).toBe('50');
      expect(decoded.fileTypes).toBe('pdf');
      expect(decoded.tags).toBe('x,y');
      expect(decoded.associatedOnly).toBe('true');
      expect(decoded.theme).toBe('dark');
      expect(decoded.searchIndexName).toBe('idx-1');
    });
  });
});
