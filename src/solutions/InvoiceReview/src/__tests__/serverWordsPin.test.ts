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
import {
  CODE_ADJUSTMENT_ID_REUSED,
  BLOCKED_REFUSAL_SENTENCES,
  CODE_BLOCKED_BOTH_DECISIONS_RECORDED,
  CODE_BLOCKED_CONCURRENT_CHANGE,
  CODE_BLOCKED_IN_PROGRESS,
  CODE_BLOCKED_NOTHING_STRANDED,
  CODE_BLOCKED_OPERATOR_NOT_PERMITTED,
  CODE_BLOCKED_OWN_DECISION_PENDING,
  CODE_BLOCKED_RECORD_UNAVAILABLE,
  CODE_BLOCKED_STILL_PROCESSING,
  isFinishStaleRefusal,
  isBlockedDefiniteRefusal,
  isBlockedOutcomeUnknown,
} from '../services/decisionApi';
import { staysInNoteDialog } from '../services/invoiceReviewApi';
import { incompleteOutcome } from '../services/decisionModel';
import { strandedApproval } from './testKit';
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

describe('the blocked-submission 409 that may be answered after the Decision Record (review rounds 2 and 3)', () => {
  const blockedCodes = () => stringConsts(read(`${BFF}/Services/Finance/SubmissionOverrideService.cs`));
  const conflict = (errorCode: string | null, status = 409) => ({ status, errorCode, detail: null, signInFailed: false, failedStep: null, committedLineIds: [] });

  it('is the server\'s BlockedSubmissionErrors.ConcurrentChange, and treated as an unknown outcome for the form (a record MAY have been written; locking is conservative)', () => {
    expect(blockedCodes().ConcurrentChange).toBe('ebilling.blocked.concurrent_change');
    expect(CODE_BLOCKED_CONCURRENT_CHANGE).toBe(blockedCodes().ConcurrentChange);
    expect(isBlockedOutcomeUnknown(conflict(blockedCodes().ConcurrentChange))).toBe(true);
  });

  it('(round 3) the two transient 409s are the server\'s, and every other 4xx refusal is definite (it releases a lock); unknown, note, transient and 5xx answers are not', () => {
    const b = blockedCodes();
    expect(CODE_BLOCKED_IN_PROGRESS).toBe(b.InProgress);
    expect(CODE_BLOCKED_STILL_PROCESSING).toBe(b.StillProcessing);
    for (const code of [b.DecisionConflict, b.RecordRefused, b.FindingsTooLarge, b.NotBlocked, b.AlreadyReturned, b.AlreadyOverridden, b.InvoiceNotHeld]) {
      expect(typeof code).toBe('string');
      expect(isBlockedDefiniteRefusal(conflict(code))).toBe(true);
    }
    expect(isBlockedDefiniteRefusal(conflict(b.NotAuthorized, 403))).toBe(true);
    // Not definite: transient, unknown, a refused note, and anything that is not a 4xx.
    expect(isBlockedDefiniteRefusal(conflict(b.InProgress))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict(b.StillProcessing))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict(b.ConcurrentChange))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict(b.Unavailable, 503))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict(null, 0))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict('ebilling.note_too_long', 400))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict(b.NotBlocked, 500))).toBe(false);
    // Round 4: a sign-in that expired, a timeout and a rate limit can still finish later.
    for (const status of [401, 408, 429]) expect(isBlockedDefiniteRefusal(conflict(null, status))).toBe(false);
  });

  it('(must-not-fire) the 409s answered before any record is written are not unknown outcomes', () => {
    const b = blockedCodes();
    for (const code of [b.NotBlocked, b.AlreadyOverridden, b.AlreadyReturned, b.InvoiceNotHeld, b.InProgress, b.StillProcessing, b.DecisionConflict]) {
      expect(typeof code).toBe('string');
      expect(isBlockedOutcomeUnknown(conflict(code))).toBe(false);
    }
    // The same code on another status is not it.
    expect(isBlockedOutcomeUnknown(conflict(blockedCodes().ConcurrentChange, 500))).toBe(false);
  });
});

describe('Finish as operator (ISS-034, EB-D-244): the route, the hints and the refusal codes', () => {
  const src = () => read(`${BFF}/Services/Finance/SubmissionOverrideService.cs`);
  const codes = () => stringConsts(src());
  const conflict = (errorCode: string | null, status = 409) => ({ status, errorCode, detail: null, signInFailed: false, failedStep: null, committedLineIds: [] });

  it('the page posts to the route the server maps, with the body the other blocked decisions take (reason, note)', () => {
    expect(read(`${BFF}/Api/Finance/SubmissionOverrideEndpoints.cs`)).toMatch(/MapPost\("\/\{submissionId:guid\}\/finish-stranded",\s*FinishStrandedAsync\)/);
    expect(recordMembers(src(), 'BlockedDecisionRequest')).toEqual(['Reason', 'Note']);
  });

  it('the hints the page reads are the server\'s members: StrandedDecision(Verb, RecordId, CallerIsDecider) and CanFinishAsOperator', () => {
    expect(recordMembers(src(), 'BlockedStrandedDecision')).toEqual(['Verb', 'RecordId', 'CallerIsDecider']);
    expect(src()).toMatch(/public BlockedStrandedDecision\? StrandedDecision \{ get; init; \}/);
    expect(src()).toMatch(/public bool CanFinishAsOperator \{ get; init; \}/);
    expect(src()).toMatch(/verb == Verb\.Override \? "Override" : "Return"/);
  });

  it('the five new codes are the server\'s', () => {
    const c = codes();
    expect(CODE_BLOCKED_OPERATOR_NOT_PERMITTED).toBe(c.OperatorNotPermitted);
    expect(CODE_BLOCKED_NOTHING_STRANDED).toBe(c.NothingStranded);
    expect(CODE_BLOCKED_BOTH_DECISIONS_RECORDED).toBe(c.BothDecisionsRecorded);
    expect(CODE_BLOCKED_OWN_DECISION_PENDING).toBe(c.OwnDecisionPending);
    expect(CODE_BLOCKED_RECORD_UNAVAILABLE).toBe(c.RecordUnavailable);
  });

  it('every code with a fixed sentence is one the server defines, and the fixed sentences for the new codes are the server\'s own words', () => {
    const c = codes();
    const defined = new Set(Object.values(c));
    for (const code of Object.keys(BLOCKED_REFUSAL_SENTENCES)) expect(defined.has(code)).toBe(true);
    expect(BLOCKED_REFUSAL_SENTENCES[c.NothingStranded]).toBe(c.NothingStrandedDetail);
    expect(BLOCKED_REFUSAL_SENTENCES[c.BothDecisionsRecorded]).toBe(c.BothDecisionsRecordedDetail);
    expect(BLOCKED_REFUSAL_SENTENCES[c.OwnDecisionPending]).toBe(c.OwnDecisionDetail);
  });

  it('every finish refusal is a definite 4xx refusal (never an unknown outcome), except record_unavailable, which changed nothing and is a plain 503', () => {
    const c = codes();
    for (const code of [c.NothingStranded, c.BothDecisionsRecorded, c.OwnDecisionPending]) {
      expect(isBlockedOutcomeUnknown(conflict(code))).toBe(false);
      expect(isBlockedDefiniteRefusal(conflict(code))).toBe(true);
      expect(isFinishStaleRefusal(conflict(code))).toBe(true);
    }
    expect(isBlockedDefiniteRefusal(conflict(c.OperatorNotPermitted, 403))).toBe(true);
    expect(isFinishStaleRefusal(conflict(c.OperatorNotPermitted, 403))).toBe(true);
    expect(isBlockedOutcomeUnknown(conflict(c.RecordUnavailable, 503))).toBe(false);
    expect(isBlockedDefiniteRefusal(conflict(c.RecordUnavailable, 503))).toBe(false);
    expect(isFinishStaleRefusal(conflict(c.RecordUnavailable, 503))).toBe(false);
    // The finish route returns invoice_not_held (a stale-state refusal); a finished decision is a 200 there, not AlreadyOverridden / AlreadyReturned.
    expect(isFinishStaleRefusal(conflict(c.InvoiceNotHeld))).toBe(true);
    expect(isFinishStaleRefusal(conflict(c.AlreadyOverridden))).toBe(false);
    expect(isFinishStaleRefusal(conflict(c.AlreadyReturned))).toBe(false);
  });
});

describe('the 409 codes a dialog keeps open for a resend (review round 1)', () => {
  const lineCodes = () => stringConsts(read(`${BFF}/Services/Finance/InvoiceLineDecisionService.cs`));
  const correctionCodes = () => stringConsts(read(`${BFF}/Services/Finance/InvoiceCorrectionService.cs`));
  const conflict = (errorCode: string | null, status = 409) => ({ status, errorCode, detail: null, signInFailed: false, failedStep: null, committedLineIds: [] });

  it('every server code the page keeps a dialog open for is one the server really sends', () => {
    const l = lineCodes();
    const c = correctionCodes();
    const stays = [l.Busy, l.LinePending, l.TotalsRefused, c.CodeSetUnavailable, c.ExtractionUnavailable];
    for (const code of stays) {
      expect(typeof code).toBe('string');
      expect(staysInNoteDialog(conflict(code))).toBe(true);
    }
  });

  it('(round 2) a refusal whose cause lies outside the form closes the dialog: corrections pending on other lines, and no firm on the invoice', () => {
    const l = lineCodes();
    const c = correctionCodes();
    for (const code of [l.CorrectionsPending, c.FirmMissing]) {
      expect(typeof code).toBe('string');
      expect(staysInNoteDialog(conflict(code))).toBe(false);
    }
  });

  it('(must-not-fire) the server codes that say the invoice or line is no longer what the form was about close the dialog', () => {
    const l = lineCodes();
    const c = correctionCodes();
    const closes = [l.NotInReview, l.LineNotDecidable, l.LineChanged, c.InvoiceChanged, c.LineNotCorrectable, c.NoProvenance, c.ProcedureGap, c.TooLarge, l.CorrectionsPending, c.FirmMissing];
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

describe('Finish decision (ISS-032): the approval state\'s hint', () => {
  const src = () => read(`${BFF}/Services/Finance/InvoiceApprovalService.cs`);

  it('the hint the page reads is the server\'s member: IncompleteDecision(Verb, FailedStep) on the approval state', () => {
    expect(recordMembers(src(), 'InvoiceIncompleteDecisionDto')).toEqual(['Verb', 'FailedStep']);
    expect(src()).toMatch(/InvoiceIncompleteDecisionDto\? IncompleteDecision = null\);/);
  });

  it('the verbs the page knows are the server\'s words (the StrandedKind names, sent as kind.ToString())', () => {
    const kinds = /private enum StrandedKind\s*\{([^}]*)\}/.exec(src());
    expect(kinds).not.toBeNull();
    expect(kinds![1].split(',').map(k => k.trim()).filter(Boolean)).toEqual(['Approve', 'Reject', 'Return']);
    expect(src()).toMatch(/new InvoiceIncompleteDecisionDto\(kind\.ToString\(\), step\)/);
    for (const verb of ['Approve', 'Reject', 'Return'] as const) expect(incompleteOutcome(strandedApproval(verb))).not.toBeNull();
  });
});
