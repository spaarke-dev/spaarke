/**
 * Task 084h-4, review round 1 (F2): the three words and the one number the page holds for the Note, pinned to the server's source.
 * Nothing at run time ties them to the BFF, so a server rename would otherwise silently turn the NotRecorded warning off (or stop a refused
 * note's code being named). Read from the C# source in the style of planFix1.test.ts and the AssignProcedureWizard K1 test.
 *
 * Each test fails if a pinned value is changed on either side: edit the server constant (or the client's) and the equality breaks.
 */

import * as fs from 'fs';
import * as path from 'path';
import { DECISION_RECORD_NOT_RECORDED, MAX_NOTE_CHARS, NOTE_ERROR_CODES, notRecorded } from '../services/overridesApi';
import { MAX_REASON_CHARS } from '../components/wizards/plan';

const REPO = path.resolve(__dirname, '../../../../..');
const BFF = 'src/server/api/Sprk.Bff.Api/Services/Finance';
const read = (rel: string) => fs.readFileSync(path.join(REPO, rel), 'utf8');

const ASSIGNMENT = read(`${BFF}/Procedures/ProcedureAssignmentService.cs`);

/** Every `const string Name = "value";` (any modifiers) in a source text, as a name-to-value map. */
function stringConsts(source: string): Record<string, string> {
  return Object.fromEntries([...source.matchAll(/\bconst\s+string\s+(\w+)\s*=\s*"([^"\\]*)"\s*;/g)].map(m => [m[1], m[2]]));
}

/** The body of `record OverrideDecisionRecord(...) { ... }`, up to its closing brace at the start of a line. */
const decisionRecordBody = () => /record\s+OverrideDecisionRecord\b[^{]*\{([\s\S]*?)\n\}/.exec(ASSIGNMENT)?.[1] ?? '';

/** An integer constant: `const int Name = 2000;` or `= Other.Name;` followed through one named source. */
function intConst(source: string, name: string): number | null {
  const m = new RegExp(`\\bconst\\s+int\\s+${name}\\s*=\\s*(\\d+)\\s*;`).exec(source);
  return m ? Number(m[1]) : null;
}

describe('the decision-record status words the server returns', () => {
  const statuses = () => stringConsts(decisionRecordBody());

  it('(control) the scan finds the server\'s three statuses, and only those', () => {
    expect(statuses()).toEqual({ Recorded: 'Recorded', Queued: 'Queued', NotRecorded: 'NotRecorded' });
  });

  it('the client\'s NotRecorded word is the server\'s NotRecorded constant', () => {
    expect(DECISION_RECORD_NOT_RECORDED).toBe(statuses().NotRecorded);
  });

  it('notRecorded() is true for the server\'s NotRecorded word and false for each of its other words', () => {
    for (const [name, word] of Object.entries(statuses())) {
      expect([name, notRecorded({ decisionRecord: { status: word } })]).toEqual([name, name === 'NotRecorded']);
    }
  });
});

describe('the refused-note codes the server answers', () => {
  const consts = () => stringConsts(ASSIGNMENT);

  it('(control) the scan finds the two note codes in ProcedureAssignmentService.cs', () => {
    expect(consts().CodeNoteTooLong).toBe('assignment.note_too_long');
    expect(consts().CodeNoteInvalidCharacters).toBe('assignment.note_invalid_characters');
  });

  it('the client names exactly the server\'s two note codes', () => {
    expect([...NOTE_ERROR_CODES].sort()).toEqual([consts().CodeNoteTooLong, consts().CodeNoteInvalidCharacters].sort());
  });
});

describe('the note and reason limit', () => {
  it('is the server\'s InvoiceCorrectionNote.MaxLength, the line-decision ReasonMaxLength (2000)', () => {
    const note = read(`${BFF}/InvoiceCorrectionNote.cs`);
    expect(/public const int MaxLength\s*=\s*InvoiceLineDecisionService\.ReasonMaxLength\s*;/.test(note)).toBe(true);
    const limit = intConst(read(`${BFF}/InvoiceLineDecisionService.cs`), 'ReasonMaxLength');
    expect(limit).toBe(2000);
    expect(MAX_NOTE_CHARS).toBe(limit);
  });

  it('the page has one number: a reason\'s limit is the note\'s, and the server\'s MaxReasonChars is the same', () => {
    expect(MAX_REASON_CHARS).toBe(MAX_NOTE_CHARS);
    expect(MAX_REASON_CHARS).toBe(intConst(ASSIGNMENT, 'MaxReasonChars'));
  });

  it('the server reads a note through InvoiceCorrectionNote.Check and answers its TooLong with the note_too_long code', () => {
    expect(ASSIGNMENT).toMatch(/InvoiceCorrectionNote\.Refusal\.TooLong\s*=>\s*Refused<T>\(StatusCodes\.Status400BadRequest,\s*CodeNoteTooLong/);
  });
});
