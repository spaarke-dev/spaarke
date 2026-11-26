/**
 * rawWholeNumberOf — the raw option-set / whole-number reader (R4 task 028 fix round). The module fetch returns an option
 * set as the SDK object `{ value, extensionData }`; the typed route DTOs and older callers pass a number or digit string.
 */
import { describe, expect, it } from 'vitest';
import { rawWholeNumberOf } from '../src/utils/rawWholeNumber';

describe('rawWholeNumberOf', () => {
  it('reads an integer number', () => {
    expect(rawWholeNumberOf(1)).toBe(1);
    expect(rawWholeNumberOf(0)).toBe(0);
    expect(rawWholeNumberOf(659490002)).toBe(659490002);
  });

  it('reads a string of digits (whitespace ignored)', () => {
    expect(rawWholeNumberOf('1')).toBe(1);
    expect(rawWholeNumberOf(' 659490000 ')).toBe(659490000);
  });

  it('reads the SDK object, as measured on the wire and with a capital V', () => {
    expect(rawWholeNumberOf({ value: 1, extensionData: null })).toBe(1);
    expect(rawWholeNumberOf({ value: 659490000 })).toBe(659490000);
    expect(rawWholeNumberOf({ Value: 2 })).toBe(2);
    expect(rawWholeNumberOf({ value: 0, extensionData: null })).toBe(0);
  });

  it('returns null for everything else', () => {
    const notWhole: unknown[] = [
      null,
      undefined,
      '',
      'In Review',
      'abc',
      '1.5',
      '-1',
      '1234567890123',
      1.5,
      NaN,
      Infinity,
      true,
      {},
      [],
      [1],
      { value: null },
      { value: undefined },
      { value: 1.5 },
      { value: '1' },
      { value: { value: 1 } },
      { foo: 1 },
      { extensionData: null },
      () => 1,
    ];
    for (const raw of notWhole) expect(rawWholeNumberOf(raw), String(JSON.stringify(raw))).toBeNull();
  });
});
