/**
 * Task 134 slice B2 (ebilling 095f, EB-D-225): the Correction Note on the seam input. `correctionNote` is REQUIRED for a
 * Correction (trimmed, at most 2000 characters, no control character except \n, \r, \t) and never present on a New
 * invoice. Contract: projects/ebilling-r1/notes/095-from-email-contract.md, section 2.
 */
import {
  CORRECTION_NOTE_MAX,
  CORRECTION_NOTE_PROBLEM_MESSAGES,
  buildInvoiceSubmissionInput,
  correctionNoteProblem,
} from '../invoiceSubmissionSeam';

const FIRM = '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77';
const MATTER = '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41';
const INVOICE = '3c9d2b1a-7e4f-4a6b-8c2d-1e0f9a8b7c65';

function build(mode: 'New' | 'Correction', correctionNote?: string) {
  return buildInvoiceSubmissionInput({
    target: { entityType: 'Matter', entityId: MATTER },
    attachments: [],
    marking: {},
    firmId: FIRM,
    firmInvoiceNumber: 'INV-1',
    poNumber: '',
    mode,
    invoiceId: mode === 'Correction' ? INVOICE : null,
    ...(correctionNote !== undefined ? { correctionNote } : {}),
  });
}

describe('buildInvoiceSubmissionInput — correctionNote (095f)', () => {
  it('must-fire: a Correction carries the TRIMMED note', () => {
    const input = build('Correction', '  Wrong rate on line 3.  \n');
    expect(input.correctionNote).toBe('Wrong rate on line 3.');
    expect(input.mode).toBe('Correction');
  });

  it('must-fire: a Correction without a note, or with a blank one, is refused with the note sentence', () => {
    expect(() => build('Correction')).toThrow(CORRECTION_NOTE_PROBLEM_MESSAGES.blank);
    expect(() => build('Correction', '')).toThrow(CORRECTION_NOTE_PROBLEM_MESSAGES.blank);
    expect(() => build('Correction', '   \n\t ')).toThrow(CORRECTION_NOTE_PROBLEM_MESSAGES.blank);
  });

  it('must-fire: a trimmed note over 2000 characters is refused, never truncated', () => {
    expect(() => build('Correction', 'x'.repeat(CORRECTION_NOTE_MAX + 1))).toThrow(
      CORRECTION_NOTE_PROBLEM_MESSAGES['too-long']
    );
  });

  it('must-fire: a control character other than a line break or a tab is refused', () => {
    for (const bad of ['a\u0000b', 'a\u0007b', 'a\u001bb', 'a\u007fb', 'a\u0090b']) {
      expect(() => build('Correction', bad)).toThrow(CORRECTION_NOTE_PROBLEM_MESSAGES['control-characters']);
    }
  });

  it('must-not-fire: exactly 2000 characters is allowed, and the whitespace around it does not count', () => {
    const note = 'x'.repeat(CORRECTION_NOTE_MAX);
    expect(build('Correction', note).correctionNote).toBe(note);
    expect(build('Correction', `   ${note}\n\n  `).correctionNote).toBe(note);
  });

  it('must-not-fire: \\n, \\r and \\t inside a note are allowed and kept', () => {
    const note = 'Line one\nLine two\r\nTabbed\tvalue';
    expect(build('Correction', note).correctionNote).toBe(note);
  });

  it('must-not-fire: a New input never carries the property, even when a note is passed', () => {
    const input = build('New', 'should not be sent');
    expect('correctionNote' in input).toBe(false);
    expect(Object.keys(build('New')).sort()).toEqual(
      ['firmId', 'firmInvoiceNumber', 'invoiceId', 'marks', 'mode', 'poNumber', 'targetEntity'].sort()
    );
  });

  it('contract: a Correction input has the New keys plus correctionNote', () => {
    expect(Object.keys(build('Correction', 'Why')).sort()).toEqual(
      ['correctionNote', 'firmId', 'firmInvoiceNumber', 'invoiceId', 'marks', 'mode', 'poNumber', 'targetEntity'].sort()
    );
  });
});

describe('correctionNoteProblem', () => {
  it('names each problem, and null for a note that can be sent', () => {
    expect(correctionNoteProblem('')).toBe('blank');
    expect(correctionNoteProblem(' \n ')).toBe('blank');
    expect(correctionNoteProblem('y'.repeat(2001))).toBe('too-long');
    expect(correctionNoteProblem('a\u0001')).toBe('control-characters');
    expect(correctionNoteProblem('Fine\nnote\twith tab')).toBeNull();
    expect(correctionNoteProblem('y'.repeat(2000))).toBeNull();
  });
});
