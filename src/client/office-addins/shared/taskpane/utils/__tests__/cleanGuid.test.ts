import { cleanGuid } from '../cleanGuid';

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
