/**
 * The correction note rules (ebilling-r1 095f-2e2b): the server's `InvoiceCorrectionNote` rules, as the wizard's note box reads them.
 * Controls: each case that must refuse and each that must not.
 */
import {
  CORRECTION_NOTE_MAX_CHARS,
  correctionNoteCount,
  correctionNoteCounterText,
  correctionNoteProblem,
  correctionNoteToSend,
} from '../correctionNote';

describe('correctionNoteProblem', () => {
  it('a Correction needs a note: empty, blank and null refuse', () => {
    for (const note of ['', '   ', '\n\t ', null, undefined]) {
      expect(correctionNoteProblem(note)).toBe('Say why this invoice is corrected.');
    }
  });

  it('counts after trimming: 2,000 passes (padded too), 2,001 refuses', () => {
    expect(CORRECTION_NOTE_MAX_CHARS).toBe(2000);
    expect(correctionNoteProblem('x'.repeat(2000))).toBeNull();
    expect(correctionNoteProblem(`  ${'x'.repeat(2000)}  `)).toBeNull();
    expect(correctionNoteProblem('x'.repeat(2001))).toBe('The note may be at most 2,000 characters.');
  });

  it('refuses the C1 controls the server refuses too (char.IsControl): U+0085 NEL, U+0080 and U+009F', () => {
    for (const bad of ['a\u0085b', 'a\u0080b', 'a\u009Fb']) {
      expect(correctionNoteProblem(bad)).toMatch(/control character/);
    }
    // The first characters after the C1 block are ordinary text.
    expect(correctionNoteProblem('a b')).toBeNull();
  });

  it('refuses a control character other than a line break or a tab', () => {
    for (const bad of ['a\u0000b', 'a\u0007b', 'a\u000Bb', 'a\u000Cb', 'a\u001Fb', 'a\u007Fb']) {
      expect(correctionNoteProblem(bad)).toMatch(/control character/);
    }
    for (const ok of ['a\nb', 'a\r\nb', 'a\tb', 'Ünïcode ✓ — “quoted”']) {
      expect(correctionNoteProblem(ok)).toBeNull();
    }
  });
});

describe('correctionNoteToSend, correctionNoteCount and correctionNoteCounterText', () => {
  it('sends the trimmed note and counts what the server counts', () => {
    expect(correctionNoteToSend('  Wrong rates.  ')).toBe('Wrong rates.');
    expect(correctionNoteToSend(undefined)).toBe('');
    expect(correctionNoteCount('  abc  ')).toBe(3);
    expect(correctionNoteCounterText('  abc  ')).toBe('3 / 2,000 characters');
    expect(correctionNoteCounterText('')).toBe('0 / 2,000 characters');
  });
});
