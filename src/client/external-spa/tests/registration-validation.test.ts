/**
 * Client-side registration validation (task 058): the same closed rule as the BFF, so a bad form sends nothing.
 * Control characters are built with String.fromCharCode, because the external-SPA source scan refuses unicode escapes.
 */
import { describe, expect, it } from 'vitest';
import { MAX_DEPARTMENT_LENGTH, MAX_NAME_LENGTH, validateRegistrationInput } from '../src/auth/registration-client';

const ch = (code: number): string => String.fromCharCode(code);

describe('validateRegistrationInput', () => {
  it('accepts ordinary names and an absent department', () => {
    expect(validateRegistrationInput({ firstName: 'Ana', lastName: 'Müller-Ölz' })).toEqual({});
  });

  it.each([0x00, 0x09, 0x0a, 0x1f, 0x7f])('refuses control character code %i in a name', code => {
    const errors = validateRegistrationInput({ firstName: `An${ch(code)}a`, lastName: 'Smith' });
    expect(errors.firstName).toMatch(/not allowed/);
    expect(errors.lastName).toBeUndefined();
  });

  it('refuses a control character in the department', () => {
    expect(
      validateRegistrationInput({ firstName: 'A', lastName: 'B', department: `Le${ch(0x1b)}gal` }).department
    ).toMatch(/not allowed/);
  });

  it('accepts the first printable characters next to the control ranges (0x20, 0x7e, 0x80)', () => {
    expect(validateRegistrationInput({ firstName: `A${ch(0x20)}B`, lastName: `C${ch(0x7e)}D${ch(0x80)}` })).toEqual({});
  });

  it('requires both names and enforces the length limits', () => {
    const errors = validateRegistrationInput({
      firstName: '  ',
      lastName: 'x'.repeat(MAX_NAME_LENGTH + 1),
      department: 'y'.repeat(MAX_DEPARTMENT_LENGTH + 1),
    });
    expect(errors.firstName).toMatch(/required/);
    expect(errors.lastName).toMatch(/at most/);
    expect(errors.department).toMatch(/at most/);
  });
});
