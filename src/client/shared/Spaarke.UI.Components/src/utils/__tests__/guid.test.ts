/**
 * guid.test.ts — coverage for the canonical `cleanGuid` (C-7,
 * spaarke-ontology-platform-r1 reuse audit, item U1 / ADR-044).
 */

import { cleanGuid, normalizeRecordRef } from '../guid';

describe('normalizeRecordRef', () => {
  const ID = '39cde3e3-9d15-4e2b-8f3a-000000000001';

  it('normalizes the logical name and the GUID (case, braces, whitespace)', () => {
    expect(normalizeRecordRef(' SPRK_Matter ', `{${ID.toUpperCase()}}`)).toEqual({ entityType: 'sprk_matter', id: ID });
  });

  it('rejects a malformed logical name or GUID (must not pass)', () => {
    expect(normalizeRecordRef('sprk_matter"><x', ID)).toBeUndefined();
    expect(normalizeRecordRef('1sprk', ID)).toBeUndefined();
    expect(normalizeRecordRef('sprk_matter', 'not-a-guid')).toBeUndefined();
    expect(normalizeRecordRef('sprk_matter', `${ID}0`)).toBeUndefined();
  });

  it('rejects anything that is not a string', () => {
    expect(normalizeRecordRef(undefined, ID)).toBeUndefined();
    expect(normalizeRecordRef('sprk_matter', 42)).toBeUndefined();
    expect(normalizeRecordRef({ toString: () => 'sprk_matter' }, ID)).toBeUndefined();
  });
});

describe('cleanGuid', () => {
  it('strips braces from a brace-wrapped GUID', () => {
    expect(cleanGuid('{39cde3e3-9d15-4e2b-8f3a-000000000001}')).toBe('39cde3e3-9d15-4e2b-8f3a-000000000001');
  });

  it('lowercases an uppercase (registry-format) GUID', () => {
    expect(cleanGuid('{39CDE3E3-9D15-4E2B-8F3A-000000000001}')).toBe('39cde3e3-9d15-4e2b-8f3a-000000000001');
  });

  it('trims surrounding whitespace', () => {
    expect(cleanGuid('  39cde3e3-9d15-4e2b-8f3a-000000000001  ')).toBe('39cde3e3-9d15-4e2b-8f3a-000000000001');
  });

  it('is a no-op on an already-bare lowercase GUID', () => {
    const bare = '39cde3e3-9d15-4e2b-8f3a-000000000001';
    expect(cleanGuid(bare)).toBe(bare);
  });

  it('returns an empty string for null, undefined, or empty input', () => {
    expect(cleanGuid(null)).toBe('');
    expect(cleanGuid(undefined)).toBe('');
    expect(cleanGuid('')).toBe('');
  });
});
