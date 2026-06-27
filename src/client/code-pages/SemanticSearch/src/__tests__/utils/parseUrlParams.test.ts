/**
 * Unit tests for parseUrlParams — envelope-key parsing (FR-CP-01).
 *
 * Per FR-CP-01 acceptance, each envelope key has three test cases:
 *  (a) Present with valid value → parsed correctly with correct type
 *  (b) Absent → returns `undefined` (or `[]`/`false` per the parser's contract)
 *  (c) Malformed → returns `undefined` (page must NOT crash)
 *
 * Envelope keys covered (per spec FR-PARITY-01 + FR-CP-01):
 *   theme, query, domain, scope, entityId, savedSearchId, searchIndexName,
 *   threshold, searchMode, fileTypes, dateFrom, dateTo, tags, associatedOnly
 *
 * Test seam: `parseUrlParams(search)` accepts an explicit search-string
 * argument so we don't need to mock `window.location`.
 *
 * @see parseUrlParams.ts
 * @see projects/spaarke-multi-container-multi-index-r1/spec.md — FR-CP-01
 */

import {
  parseUrlParams,
  parseString,
  parseNumber,
  parseIsoDate,
  parseCsv,
  parseBoolean,
  parseSearchMode,
  parseDomain,
} from '../../utils/parseUrlParams';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a Dataverse-style envelope search string (`?data=<encoded-kvp-string>`). */
function envelope(kvps: Record<string, string>): string {
  const inner = Object.entries(kvps)
    .map(([k, v]) => `${k}=${encodeURIComponent(v)}`)
    .join('&');
  return `?data=${encodeURIComponent(inner)}`;
}

// ===========================================================================
// Helper-level tests (per-type parsers)
// ===========================================================================

describe('parseString', () => {
  it('returns trimmed value when present', () => {
    expect(parseString('  hello  ')).toBe('hello');
  });
  it('returns undefined when null', () => {
    expect(parseString(null)).toBeUndefined();
  });
  it('returns undefined when empty/whitespace', () => {
    expect(parseString('')).toBeUndefined();
    expect(parseString('   ')).toBeUndefined();
  });
});

describe('parseNumber', () => {
  it('parses valid number', () => {
    expect(parseNumber('50')).toBe(50);
    expect(parseNumber('  42 ')).toBe(42);
    expect(parseNumber('0.75')).toBe(0.75);
  });
  it('returns undefined when null/empty', () => {
    expect(parseNumber(null)).toBeUndefined();
    expect(parseNumber('')).toBeUndefined();
  });
  it('returns undefined for non-numeric (malformed)', () => {
    expect(parseNumber('abc')).toBeUndefined();
    expect(parseNumber('NaN')).toBeUndefined();
  });
  it('returns undefined when outside min/max bounds', () => {
    expect(parseNumber('150', 0, 100)).toBeUndefined();
    expect(parseNumber('-5', 0, 100)).toBeUndefined();
  });
  it('accepts boundary values', () => {
    expect(parseNumber('0', 0, 100)).toBe(0);
    expect(parseNumber('100', 0, 100)).toBe(100);
  });
});

describe('parseIsoDate', () => {
  it('returns original ISO string when valid', () => {
    expect(parseIsoDate('2026-06-01T00:00:00Z')).toBe('2026-06-01T00:00:00Z');
    expect(parseIsoDate('2026-06-01')).toBe('2026-06-01');
  });
  it('returns undefined when null/empty', () => {
    expect(parseIsoDate(null)).toBeUndefined();
    expect(parseIsoDate('')).toBeUndefined();
  });
  it('returns undefined for malformed input', () => {
    expect(parseIsoDate('notadate')).toBeUndefined();
    expect(parseIsoDate('2026-13-99')).toBeUndefined();
  });
});

describe('parseCsv', () => {
  it('splits CSV and trims items', () => {
    expect(parseCsv('pdf,docx')).toEqual(['pdf', 'docx']);
    expect(parseCsv(' pdf , docx ,  ')).toEqual(['pdf', 'docx']);
  });
  it('returns undefined when null/empty', () => {
    expect(parseCsv(null)).toBeUndefined();
    expect(parseCsv('')).toBeUndefined();
  });
  it('returns undefined when all items are empty after trim', () => {
    expect(parseCsv(',,')).toBeUndefined();
    expect(parseCsv('  ,   ,  ')).toBeUndefined();
  });
  it('preserves single-item arrays', () => {
    expect(parseCsv('pdf')).toEqual(['pdf']);
  });
});

describe('parseBoolean', () => {
  it('parses truthy literals', () => {
    expect(parseBoolean('true')).toBe(true);
    expect(parseBoolean('TRUE')).toBe(true);
    expect(parseBoolean('1')).toBe(true);
    expect(parseBoolean('yes')).toBe(true);
  });
  it('parses falsy literals', () => {
    expect(parseBoolean('false')).toBe(false);
    expect(parseBoolean('FALSE')).toBe(false);
    expect(parseBoolean('0')).toBe(false);
    expect(parseBoolean('no')).toBe(false);
  });
  it('returns undefined when null/empty', () => {
    expect(parseBoolean(null)).toBeUndefined();
    expect(parseBoolean('')).toBeUndefined();
  });
  it('returns undefined for unknown literals (malformed)', () => {
    expect(parseBoolean('maybe')).toBeUndefined();
    expect(parseBoolean('2')).toBeUndefined();
  });
});

describe('parseSearchMode', () => {
  it('accepts each allow-listed literal', () => {
    expect(parseSearchMode('hybrid')).toBe('hybrid');
    expect(parseSearchMode('vectorOnly')).toBe('vectorOnly');
    expect(parseSearchMode('keywordOnly')).toBe('keywordOnly');
  });
  it('returns undefined when null', () => {
    expect(parseSearchMode(null)).toBeUndefined();
  });
  it('returns undefined for unknown / wrong-case input (malformed)', () => {
    expect(parseSearchMode('HYBRID')).toBeUndefined();
    expect(parseSearchMode('rrf')).toBeUndefined();
    expect(parseSearchMode('foo')).toBeUndefined();
  });
});

describe('parseDomain', () => {
  it('accepts allow-listed domains (case-insensitive)', () => {
    expect(parseDomain('documents')).toBe('documents');
    expect(parseDomain('Matters')).toBe('matters');
    expect(parseDomain('PROJECTS')).toBe('projects');
    expect(parseDomain('invoices')).toBe('invoices');
  });
  it('returns undefined when null', () => {
    expect(parseDomain(null)).toBeUndefined();
  });
  it('returns undefined for unknown values', () => {
    expect(parseDomain('contacts')).toBeUndefined();
    expect(parseDomain('')).toBeUndefined();
  });
});

// ===========================================================================
// Integration: parseUrlParams (envelope-level) — present/absent/malformed
// per envelope key. Each block satisfies the FR-CP-01 acceptance criteria.
// ===========================================================================

describe('parseUrlParams — envelope unwrap + per-key coverage', () => {
  // -------------------------------------------------------------------------
  // theme
  // -------------------------------------------------------------------------
  describe('theme', () => {
    it('present — passes through', () => {
      expect(parseUrlParams(envelope({ theme: 'dark' })).theme).toBe('dark');
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).theme).toBeUndefined();
    });
    it('malformed (empty) — undefined', () => {
      expect(parseUrlParams(envelope({ theme: '' })).theme).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // query
  // -------------------------------------------------------------------------
  describe('query', () => {
    it('present — passes through', () => {
      expect(parseUrlParams(envelope({ query: 'contract review' })).query).toBe('contract review');
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).query).toBeUndefined();
    });
    it('malformed (whitespace only) — undefined', () => {
      expect(parseUrlParams(envelope({ query: '   ' })).query).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // domain
  // -------------------------------------------------------------------------
  describe('domain', () => {
    it('present — passes through (case-insensitive)', () => {
      expect(parseUrlParams(envelope({ domain: 'matters' })).domain).toBe('matters');
      expect(parseUrlParams(envelope({ domain: 'PROJECTS' })).domain).toBe('projects');
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).domain).toBeUndefined();
    });
    it('malformed (unknown literal) — undefined', () => {
      expect(parseUrlParams(envelope({ domain: 'contacts' })).domain).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // scope
  // -------------------------------------------------------------------------
  describe('scope', () => {
    it('present — passes through', () => {
      expect(parseUrlParams(envelope({ scope: 'entity' })).scope).toBe('entity');
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).scope).toBeUndefined();
    });
    it('malformed (empty) — undefined', () => {
      expect(parseUrlParams(envelope({ scope: '' })).scope).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // entityId
  // -------------------------------------------------------------------------
  describe('entityId', () => {
    it('present — passes through', () => {
      const guid = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
      expect(parseUrlParams(envelope({ entityId: guid })).entityId).toBe(guid);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).entityId).toBeUndefined();
    });
    it('malformed (empty) — undefined', () => {
      expect(parseUrlParams(envelope({ entityId: '' })).entityId).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // savedSearchId
  // -------------------------------------------------------------------------
  describe('savedSearchId', () => {
    it('present — passes through', () => {
      const id = 'aaaa-bbbb-cccc';
      expect(parseUrlParams(envelope({ savedSearchId: id })).savedSearchId).toBe(id);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).savedSearchId).toBeUndefined();
    });
    it('malformed (empty) — undefined', () => {
      expect(parseUrlParams(envelope({ savedSearchId: '' })).savedSearchId).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // searchIndexName
  // -------------------------------------------------------------------------
  describe('searchIndexName', () => {
    it('present — passes through', () => {
      expect(parseUrlParams(envelope({ searchIndexName: 'sprk-documents-v2' })).searchIndexName).toBe(
        'sprk-documents-v2'
      );
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).searchIndexName).toBeUndefined();
    });
    it('malformed (empty) — undefined', () => {
      expect(parseUrlParams(envelope({ searchIndexName: '' })).searchIndexName).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // threshold
  // -------------------------------------------------------------------------
  describe('threshold', () => {
    it('present — parses as number', () => {
      expect(parseUrlParams(envelope({ threshold: '50' })).threshold).toBe(50);
      expect(parseUrlParams(envelope({ threshold: '0' })).threshold).toBe(0);
      expect(parseUrlParams(envelope({ threshold: '100' })).threshold).toBe(100);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).threshold).toBeUndefined();
    });
    it('malformed (non-numeric) — undefined', () => {
      expect(parseUrlParams(envelope({ threshold: 'abc' })).threshold).toBeUndefined();
    });
    it('malformed (out of range) — undefined', () => {
      expect(parseUrlParams(envelope({ threshold: '150' })).threshold).toBeUndefined();
      expect(parseUrlParams(envelope({ threshold: '-5' })).threshold).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // searchMode
  // -------------------------------------------------------------------------
  describe('searchMode', () => {
    it('present — passes through valid literal', () => {
      expect(parseUrlParams(envelope({ searchMode: 'hybrid' })).searchMode).toBe('hybrid');
      expect(parseUrlParams(envelope({ searchMode: 'vectorOnly' })).searchMode).toBe('vectorOnly');
      expect(parseUrlParams(envelope({ searchMode: 'keywordOnly' })).searchMode).toBe('keywordOnly');
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).searchMode).toBeUndefined();
    });
    it('malformed (unknown literal) — undefined', () => {
      expect(parseUrlParams(envelope({ searchMode: 'rrf' })).searchMode).toBeUndefined();
      expect(parseUrlParams(envelope({ searchMode: 'foo' })).searchMode).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // fileTypes
  // -------------------------------------------------------------------------
  describe('fileTypes', () => {
    it('present — parses CSV into array', () => {
      expect(parseUrlParams(envelope({ fileTypes: 'pdf,docx,xlsx' })).fileTypes).toEqual(['pdf', 'docx', 'xlsx']);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).fileTypes).toBeUndefined();
    });
    it('malformed (empty/commas only) — undefined', () => {
      expect(parseUrlParams(envelope({ fileTypes: ',,' })).fileTypes).toBeUndefined();
      expect(parseUrlParams(envelope({ fileTypes: '' })).fileTypes).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // dateFrom
  // -------------------------------------------------------------------------
  describe('dateFrom', () => {
    it('present — passes through valid ISO string', () => {
      const iso = '2026-06-01T00:00:00Z';
      expect(parseUrlParams(envelope({ dateFrom: iso })).dateFrom).toBe(iso);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).dateFrom).toBeUndefined();
    });
    it('malformed (not a date) — undefined', () => {
      expect(parseUrlParams(envelope({ dateFrom: 'notadate' })).dateFrom).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // dateTo
  // -------------------------------------------------------------------------
  describe('dateTo', () => {
    it('present — passes through valid ISO string', () => {
      const iso = '2026-12-31T23:59:59Z';
      expect(parseUrlParams(envelope({ dateTo: iso })).dateTo).toBe(iso);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).dateTo).toBeUndefined();
    });
    it('malformed (not a date) — undefined', () => {
      expect(parseUrlParams(envelope({ dateTo: 'tomorrow' })).dateTo).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // tags
  // -------------------------------------------------------------------------
  describe('tags', () => {
    it('present — parses CSV into array', () => {
      expect(parseUrlParams(envelope({ tags: 'urgent,legal,review' })).tags).toEqual(['urgent', 'legal', 'review']);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).tags).toBeUndefined();
    });
    it('malformed (empty) — undefined', () => {
      expect(parseUrlParams(envelope({ tags: '' })).tags).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // associatedOnly
  // -------------------------------------------------------------------------
  describe('associatedOnly', () => {
    it('present (true) — parses to true', () => {
      expect(parseUrlParams(envelope({ associatedOnly: 'true' })).associatedOnly).toBe(true);
      expect(parseUrlParams(envelope({ associatedOnly: '1' })).associatedOnly).toBe(true);
    });
    it('present (false) — parses to false', () => {
      expect(parseUrlParams(envelope({ associatedOnly: 'false' })).associatedOnly).toBe(false);
      expect(parseUrlParams(envelope({ associatedOnly: '0' })).associatedOnly).toBe(false);
    });
    it('absent — undefined', () => {
      expect(parseUrlParams(envelope({})).associatedOnly).toBeUndefined();
    });
    it('malformed (unknown literal) — undefined', () => {
      expect(parseUrlParams(envelope({ associatedOnly: 'maybe' })).associatedOnly).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // Backwards-compat invariant: empty envelope → all keys undefined,
  // never throws.
  // -------------------------------------------------------------------------
  describe('backwards-compat', () => {
    it('empty search string — every key undefined, no throw', () => {
      const result = parseUrlParams('');
      expect(result).toEqual({
        theme: undefined,
        query: undefined,
        domain: undefined,
        scope: undefined,
        entityId: undefined,
        savedSearchId: undefined,
        searchIndexName: undefined,
        threshold: undefined,
        searchMode: undefined,
        fileTypes: undefined,
        dateFrom: undefined,
        dateTo: undefined,
        tags: undefined,
        associatedOnly: undefined,
      });
    });

    it('no envelope wrapper (direct URL params) — still parses', () => {
      const result = parseUrlParams('?query=test&threshold=42');
      expect(result.query).toBe('test');
      expect(result.threshold).toBe(42);
    });

    it('full envelope (all keys) — all keys populated correctly', () => {
      const result = parseUrlParams(
        envelope({
          theme: 'dark',
          query: 'contract',
          domain: 'documents',
          scope: 'entity',
          entityId: 'guid-1234',
          savedSearchId: 'saved-9',
          searchIndexName: 'sprk-documents',
          threshold: '75',
          searchMode: 'hybrid',
          fileTypes: 'pdf,docx',
          dateFrom: '2026-01-01',
          dateTo: '2026-12-31',
          tags: 'urgent,legal',
          associatedOnly: 'true',
        })
      );
      expect(result).toEqual({
        theme: 'dark',
        query: 'contract',
        domain: 'documents',
        scope: 'entity',
        entityId: 'guid-1234',
        savedSearchId: 'saved-9',
        searchIndexName: 'sprk-documents',
        threshold: 75,
        searchMode: 'hybrid',
        fileTypes: ['pdf', 'docx'],
        dateFrom: '2026-01-01',
        dateTo: '2026-12-31',
        tags: ['urgent', 'legal'],
        associatedOnly: true,
      });
    });
  });
});
