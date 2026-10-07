// Task 099 (ADR-012 amended 2026-10-05 / ADR-044): this package's local `cleanGuid` copy is deleted — the add-in
// consumes the ONE shared implementation by exact-path alias (`@spaarke/ui-components/guid`, webpack + jest +
// tsconfig), and this suite now pins THAT implementation's contract as the add-in depends on it.
import { cleanGuid } from '@spaarke/ui-components/guid';

describe('cleanGuid', () => {
  it('returns an already-bare-lowercase GUID unchanged (no-op)', () => {
    expect(cleanGuid('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee')).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });

  it('strips braces', () => {
    expect(cleanGuid('{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}')).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });

  it('lowercases an uppercase (registry-format) GUID', () => {
    expect(cleanGuid('{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}')).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });

  it('trims surrounding whitespace', () => {
    expect(cleanGuid('  aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee  ')).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });

  it('returns an empty string for null', () => {
    expect(cleanGuid(null)).toBe('');
  });

  it('returns an empty string for undefined', () => {
    expect(cleanGuid(undefined)).toBe('');
  });

  it('returns an empty string for an empty string', () => {
    expect(cleanGuid('')).toBe('');
  });
});
