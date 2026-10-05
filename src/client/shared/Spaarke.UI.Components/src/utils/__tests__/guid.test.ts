/**
 * guid.test.ts — coverage for the canonical `cleanGuid` (C-7,
 * spaarke-ontology-platform-r1 reuse audit, item U1 / ADR-044).
 */

import { cleanGuid } from '../guid';

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
