/**
 * Task 095f-2ui (the pattern of RecordProcedures' serverWordsPin.test.ts): the words and the one number the Note boxes hold, pinned to the
 * BFF's C# source. Nothing at run time ties the page to the server, so a server rename would otherwise silently turn a field message into a
 * page error (a renamed 400 code), stop a removal's list reload (a renamed `id_reused`), or let the page count a different limit.
 * Read from the source files, in the style of the other source-scan tests of this solution.
 *
 * Each test fails if a pinned value changes on either side. The controls prove the scan can fail: they run the same extraction on text
 * that must NOT match.
 */

import * as fs from 'fs';
import * as path from 'path';
import { CODE_ADJUSTMENT_ID_REUSED } from '../services/decisionApi';
import { staysInNoteDialog } from '../services/invoiceReviewApi';
import { CODE_NOTE_INVALID_CHARACTERS, CODE_NOTE_TOO_LONG, NOTE_ERROR_CODES, NOTE_MAX_CHARS } from '../services/noteModel';

const REPO = path.resolve(__dirname, '../../../../..');
const BFF = 'src/server/api/Sprk.Bff.Api';
const read = (rel: string) => fs.readFileSync(path.join(REPO, rel), 'utf8');

/** Every `const string Name = "value";` (any modifiers) in a source text, as a name-to-value map. */
function stringConsts(source: string): Record<string, string> {
  return Object.fromEntries([...source.matchAll(/\bconst\s+string\s+(\w+)\s*=\s*"([^"\\]*)"\s*;/g)].map(m => [m[1], m[2]]));
}

/** An integer constant: `const int Name = 2000;`. */
function intConst(source: string, name: string): number | null {
  const m = new RegExp(`\\bconst\\s+int\\s+${name}\\s*=\\s*(\\d+)\\s*;`).exec(source);
  return m ? Number(m[1]) : null;
}

/** The parameter list of `record Name(...)`, as the member names in order (`Guid DecisionId, string? Note = null` gives `DecisionId, Note`). */
function recordMembers(source: string, name: string): string[] | null {
  const m = new RegExp(`\\brecord\\s+${name}\\s*\\(([^)]*(?:\\([^)]*\\)[^)]*)*)\\)\\s*;`).exec(source);
  if (!m) return null;
  return m[1]
    .split(/,(?![^<]*>)/)
    .map(p => p.trim())
    .filter(p => p.length > 0)
    .map(p => p.split('=')[0].trim().split(/\s+/).pop() as string);
}

describe('the scan helpers (controls: they fail on text that does not match)', () => {
  it('stringConsts finds a constant and nothing in a text without one', () => {
    expect(stringConsts('public const string A = "x.y";')).toEqual({ A: 'x.y' });
    expect(stringConsts('public const int A = 3;')).toEqual({});
    expect(intConst('public const int Max = 2000;', 'Max')).toBe(2000);
    expect(intConst('public const int Max = Other.Max;', 'Max')).toBeNull();
  });

  it('recordMembers reads the members of a record, and null for a record that is not there', () => {
    expect(recordMembers('public sealed record R(Guid DecisionId, string? Note = null);', 'R')).toEqual(['DecisionId', 'Note']);
    expect(recordMembers('public sealed record R(Guid DecisionId);', 'R')).toEqual(['DecisionId']);
    expect(recordMembers('public sealed record Other(Guid X);', 'R')).toBeNull();
  });
});

describe('the refused-note codes (the 400s shown under the Note box)', () => {
  const note = () => stringConsts(read(`${BFF}/Services/Finance/InvoiceCorrectionNote.cs`));

  it('(control) the scan finds both of the server\'s note codes in InvoiceCorrectionNote.cs', () => {
    expect(note().NoteTooLongCode).toBe('ebilling.note_too_long');
    expect(note().NoteInvalidCharactersCode).toBe('ebilling.note_invalid_characters');
  });

  it('the page names exactly the server\'s two note codes', () => {
    expect(CODE_NOTE_TOO_LONG).toBe(note().NoteTooLongCode);
    expect(CODE_NOTE_INVALID_CHARACTERS).toBe(note().NoteInvalidCharactersCode);
    expect([...NOTE_ERROR_CODES].sort()).toEqual([note().NoteTooLongCode, note().NoteInvalidCharactersCode].sort());
  });

  it('the server answers a refused note as a coded 400 through DecisionRefusalOf (not a field error)', () => {
    const src = read(`${BFF}/Services/Finance/InvoiceCorrectionNote.cs`);
    expect(src).toMatch(/Refusal\.TooLong\s*=>\s*\(NoteTooLongCode,/);
    expect(src).toMatch(/Refusal\.ControlCharacter\s*=>\s*\(NoteInvalidCharactersCode,/);
  });
});

describe('the note limit', () => {
  it('is the server\'s InvoiceCorrectionNote.MaxLength, the line-decision ReasonMaxLength (2000), counted after trimming', () => {
    const note = read(`${BFF}/Services/Finance/InvoiceCorrectionNote.cs`);
    expect(/public const int MaxLength\s*=\s*InvoiceLineDecisionService\.ReasonMaxLength\s*;/.test(note)).toBe(true);
    const limit = intConst(read(`${BFF}/Services/Finance/InvoiceLineDecisionService.cs`), 'ReasonMaxLength');
    expect(limit).toBe(2000);
    expect(NOTE_MAX_CHARS).toBe(limit);
    // The server trims first, then counts (the page counts the same way).
    expect(note).toMatch(/var trimmed = raw\?\.Trim\(\);/);
    expect(note).toMatch(/trimmed\.Length > MaxLength/);
  });
});

describe('the id-reused code that reloads the adjustments list', () => {
  it('is the server\'s AdjustmentCodes.IdReused', () => {
    const codes = stringConsts(read(`${BFF}/Services/Finance/InvoiceAdjustmentService.cs`));
    expect(codes.IdReused).toBe('ebilling.adjustment.id_reused');
    expect(CODE_ADJUSTMENT_ID_REUSED).toBe(codes.IdReused);
  });
});

describe('the request bodies: `note` is the server\'s member name on every decision, and Accept all Clean has none', () => {
  const finance = (file: string) => read(`${BFF}/Api/Finance/${file}`);
  const overrideSource = () => read(`${BFF}/Services/Finance/SubmissionOverrideService.cs`);

  it('approve, reject and return, add and remove adjustment, override and blocked return, line decisions and corrections carry Note', () => {
    expect(recordMembers(finance('InvoiceApprovalEndpoints.cs'), 'InvoiceApprovalRequest')).toEqual(['Note']);
    expect(recordMembers(finance('InvoiceRejectReturnEndpoints.cs'), 'InvoiceDecisionRequest')).toEqual(['Reason', 'Note']);
    expect(recordMembers(finance('InvoiceAdjustmentEndpoints.cs'), 'AddAdjustmentRequest')).toEqual(['AdjustmentId', 'Method', 'Value', 'Reason', 'RuleId', 'Note']);
    expect(recordMembers(finance('InvoiceAdjustmentEndpoints.cs'), 'RemoveAdjustmentRequest')).toEqual(['Note']);
    expect(recordMembers(overrideSource(), 'BlockedDecisionRequest')).toEqual(['Reason', 'Note']);
    expect(recordMembers(finance('InvoiceLineReviewEndpoints.cs'), 'SingleLineDecisionRequest')).toEqual(['DecisionId', 'Action', 'Reason', 'ApprovedAmount', 'Note']);
    expect(recordMembers(finance('InvoiceLineReviewEndpoints.cs'), 'MultiLineDecisionRequest')).toEqual(['DecisionId', 'Action', 'Reason', 'Lines', 'Note']);
    expect(recordMembers(finance('InvoiceCorrectionEndpoints.cs'), 'CorrectionRequest')).toEqual(['Field', 'Value', 'Confirm', 'Note']);
  });

  it('(must-not-fire) Accept all Clean takes the decision id only: the page has no note to send there', () => {
    expect(recordMembers(finance('InvoiceLineReviewEndpoints.cs'), 'AcceptCleanRequest')).toEqual(['DecisionId']);
  });
});

describe('the 409 codes a dialog keeps open for a resend (review round 1)', () => {
  const lineCodes = () => stringConsts(read(`${BFF}/Services/Finance/InvoiceLineDecisionService.cs`));
  const correctionCodes = () => stringConsts(read(`${BFF}/Services/Finance/InvoiceCorrectionService.cs`));
  const conflict = (errorCode: string | null, status = 409) => ({ status, errorCode, detail: null, signInFailed: false, failedStep: null, committedLineIds: [] });

  it('every server code the page keeps a dialog open for is one the server really sends', () => {
    const l = lineCodes();
    const c = correctionCodes();
    const stays = [l.Busy, l.LinePending, l.CorrectionsPending, l.TotalsRefused, c.FirmMissing, c.CodeSetUnavailable, c.ExtractionUnavailable];
    for (const code of stays) {
      expect(typeof code).toBe('string');
      expect(staysInNoteDialog(conflict(code))).toBe(true);
    }
  });

  it('(must-not-fire) the server codes that say the invoice or line is no longer what the form was about close the dialog', () => {
    const l = lineCodes();
    const c = correctionCodes();
    const closes = [l.NotInReview, l.LineNotDecidable, l.LineChanged, c.InvoiceChanged, c.LineNotCorrectable, c.NoProvenance, c.ProcedureGap, c.TooLarge];
    for (const code of closes) {
      expect(typeof code).toBe('string');
      expect(staysInNoteDialog(conflict(code))).toBe(false);
    }
    // Only a 409 with a code is ever kept open.
    expect(staysInNoteDialog(conflict(l.Busy, 503))).toBe(false);
    expect(staysInNoteDialog(conflict(null))).toBe(false);
  });

  it('a "concurrent_change" code of any area is kept open too', () => {
    expect(staysInNoteDialog(conflict('ebilling.blocked.concurrent_change'))).toBe(true);
    expect(staysInNoteDialog(conflict('ebilling.invoice.concurrent_change'))).toBe(true);
  });
});
