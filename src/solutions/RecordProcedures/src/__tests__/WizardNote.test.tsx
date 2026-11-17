/**
 * Task 084h-4 (EB-D-238, EB-D-239): the optional Note box on the Confirm step of every wizard plan, and the warning bar when a change saved but
 * its Decision Record was neither written nor queued (`decisionRecord.status` NotRecorded).
 *
 * Through the real panel, the real wizard shell and the real clients, with a fake BFF (the same harness as Wizards.test.tsx). Each act's write
 * is read off the recorded call: the POST bodies carry `note` only when one was typed; the DELETE has a JSON body `{ note }` only then.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { RecordProceduresView, RuleItem } from '../services/recordProceduresApi';
import { MAX_NOTE_CHARS } from '../services/overridesApi';
import { MAX_REASON_CHARS } from '../components/wizards/plan';
import { NOT_RECORDED_LINE } from '../components/wizards/steps';
import { FENWICK, MATTER, TODAY, V, matterProcedures, matterRules, override, procedure, rule } from './fixtures';
import { button, chooseRadio, createdBody, next, openProcedure, openRule, previewBody, procedureBody, ready, refusal, removedBody, renderHarness, respond, toChoices, toConfirm, typeReason } from './wizardHarness';
import type { WriteCall } from './testKit';

const WARNING_TEXT = 'The change was saved, but its decision record could not be written. Tell your administrator.';

const noteBox = (d: HTMLElement) => within(d).getByLabelText(/^Note/) as HTMLTextAreaElement;
const typeNote = (d: HTMLElement, text: string) => fireEvent.change(noteBox(d), { target: { value: text } });
const bodyOf = (c: WriteCall) => c.body as Record<string, unknown>;
const writes = (calls: WriteCall[]) => calls.filter(c => c.route !== 'procedure' && c.route !== 'preview');

const NOT_RECORDED = { status: 'NotRecorded', recordId: null, errorCode: 'assignment.decision_record_failed' };
const QUEUED = { status: 'Queued', recordId: null, errorCode: null };
const RECORDED = { status: 'Recorded', recordId: 'dr-1', errorCode: null };

const versions = procedureBody([
  { id: 'ocg-v1', n: 1, status: 'Superseded', from: '2025-01-01', to: '2026-01-01' },
  { id: 'ocg-v2', n: 2, status: 'Active', from: '2026-01-01' },
]);

/** Exclude a Procedure here (one POST), up to the Confirm step. */
async function toExcludeConfirm() {
  const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
  await toChoices(d);
  chooseRadio(d, /^Exclude here/);
  typeReason(d, 'Not travelling');
  await toConfirm(d);
  return d;
}

describe('the Note box: on the Confirm step of every plan, optional, and limited like a reason', () => {
  it('is on the Confirm step, empty, optional, with a count against the limit (2000) and no maxlength to cut a paste', async () => {
    renderHarness({ writes: respond({ assign: () => createdBody() }) });
    const d = await toExcludeConfirm();
    const box = noteBox(d);
    expect(box.value).toBe('');
    expect(box).not.toBeRequired();
    expect(box).not.toHaveAttribute('maxlength');
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent('0 / 2,000 characters');
    expect(MAX_NOTE_CHARS).toBe(MAX_REASON_CHARS);
    expect(MAX_REASON_CHARS).toBe(2000);
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent(/Optional/);
    // Typing a note does not stop Finish.
    typeNote(d, 'A note');
    expect(button(d, 'Save override')).toBeEnabled();
  });

  it('is not on any step before Confirm (Choose / On this record / Impact)', async () => {
    renderHarness({ writes: respond({ assign: () => createdBody() }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    expect(within(d).queryByLabelText(/^Note/)).not.toBeInTheDocument();
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    expect(within(d).queryByLabelText(/^Note/)).not.toBeInTheDocument();
  });

  it('a note with a control character is held at Confirm: Finish is off, the problem is said, nothing is sent', async () => {
    const h = renderHarness({ writes: respond({ assign: () => createdBody() }) });
    const d = await toExcludeConfirm();
    typeNote(d, 'bell\u0007here');
    await waitFor(() => expect(button(d, 'Save override')).toBeDisabled());
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent('The note holds a control character');
    expect(writes(h.writeCalls)).toHaveLength(0);
    // Line breaks and tabs are fine; fixing the note frees Finish.
    typeNote(d, 'line one\nline two\tend');
    await waitFor(() => expect(button(d, 'Save override')).toBeEnabled());
  });

  it('a note over 2000 characters (after trimming) is held; exactly 2000 is sent', async () => {
    const h = renderHarness({ writes: respond({ assign: () => createdBody() }) });
    const d = await toExcludeConfirm();
    typeNote(d, 'x'.repeat(2001));
    await waitFor(() => expect(button(d, 'Save override')).toBeDisabled());
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent('at most 2000 characters');
    typeNote(d, ` ${'x'.repeat(2000)} `);
    await waitFor(() => expect(button(d, 'Save override')).toBeEnabled());
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(bodyOf(h.writeCalls[0]).note).toBe('x'.repeat(2000));
  });

  it('review round 1: a 2,600-character paste is kept whole in the box (never cut), the problem and the count are shown, Finish is held and nothing is sent', async () => {
    const h = renderHarness({ writes: respond({ assign: () => createdBody() }) });
    const d = await toExcludeConfirm();
    typeNote(d, 'y'.repeat(2600));
    expect(noteBox(d).value).toHaveLength(2600);
    // jsdom does not cut a scripted change, a browser does cut a paste at maxlength: so the attribute itself must be absent.
    expect(noteBox(d)).not.toHaveAttribute('maxlength');
    await waitFor(() => expect(button(d, 'Save override')).toBeDisabled());
    const step = within(d).getByTestId('confirm-step');
    expect(step).toHaveTextContent('at most 2000 characters');
    expect(step).toHaveTextContent('2,600 / 2,000 characters');
    expect(writes(h.writeCalls)).toHaveLength(0);
    // Cutting it back to the limit frees Finish and sends the whole 2,000, not a different cut.
    typeNote(d, 'y'.repeat(2000));
    expect(step).toHaveTextContent('2,000 / 2,000 characters');
    await waitFor(() => expect(button(d, 'Save override')).toBeEnabled());
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(bodyOf(h.writeCalls[0]).note).toBe('y'.repeat(2000));
  });
});

describe('each act sends the note when typed, and omits the key when blank', () => {
  it('Procedure Exclude here: POST procedure-assignments with the trimmed note', async () => {
    const h = renderHarness({ writes: respond({ assign: () => createdBody({ mode: 'Excluded' }) }) });
    const d = await toExcludeConfirm();
    typeNote(d, '  Approved by the partner  ');
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(h.writeCalls).toHaveLength(1);
    expect(bodyOf(h.writeCalls[0])).toEqual({ procedureId: 'p-ocg', mode: 'Excluded', reason: 'Not travelling', note: 'Approved by the partner' });
  });

  it.each([
    ['nothing typed', ''],
    ['spaces', '    '],
    ['a line break and a tab', ' \n\t '],
  ])('Procedure Exclude here with %s: the note key is absent (not null, not "")', async (_, text) => {
    const h = renderHarness({ writes: respond({ assign: () => createdBody({ mode: 'Excluded' }) }) });
    const d = await toExcludeConfirm();
    if (text) typeNote(d, text);
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    const sent = bodyOf(h.writeCalls[0]);
    expect(sent).toEqual({ procedureId: 'p-ocg', mode: 'Excluded', reason: 'Not travelling' });
    expect(Object.keys(sent)).not.toContain('note');
  });

  it('Procedure Pin: POST with { procedureId, mode: Pinned, pinnedVersionId, reason, note }', async () => {
    const h = renderHarness({ writes: respond({ procedure: () => versions, assign: () => createdBody({ mode: 'Pinned' }) }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Pin a version/);
    await waitFor(() => expect(h.writeCalls.map(c => c.route)).toContain('procedure'));
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    fireEvent.click(await screen.findByRole('option', { name: /^v1/, hidden: true }));
    typeReason(d, 'Keep the 2025 rates');
    await toConfirm(d);
    typeNote(d, 'Client request');
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(bodyOf(h.writeCalls.find(c => c.route === 'assign')!)).toEqual({ procedureId: 'p-ocg', mode: 'Pinned', pinnedVersionId: 'ocg-v1', reason: 'Keep the 2025 rates', note: 'Client request' });
  });

  it('Rule Exclude here: POST rule-overrides/exclude with the note, and without the key when blank', async () => {
    const withNote = renderHarness({ writes: respond({ exclude: () => createdBody({ kind: 'RuleExcluded' }) }) });
    let d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Fixed format');
    await toConfirm(d);
    typeNote(d, 'Agreed with the firm');
    fireEvent.click(button(d, 'Exclude rule'));
    await within(d).findByTestId('success-body');
    expect(bodyOf(withNote.writeCalls[0])).toEqual({ policyId: 'r-block', reason: 'Fixed format', note: 'Agreed with the firm' });
    withNote.unmount();

    const blank = renderHarness({ writes: respond({ exclude: () => createdBody({ kind: 'RuleExcluded' }) }) });
    d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Fixed format');
    await toConfirm(d);
    fireEvent.click(button(d, 'Exclude rule'));
    await within(d).findByTestId('success-body');
    expect(Object.keys(bodyOf(blank.writeCalls[0]))).not.toContain('note');
  });

  it('Rule Change for this record: POST rule-overrides/change with the note', async () => {
    const hours: RuleItem = rule({ policyId: 'r-hours', code: 'HOURS', name: 'Excessive daily hours', ruleType: 'DailyHoursCap', parameterSummary: '11 hours per timekeeper per day', ruleSetId: 'rs-hours', ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 11 } });
    const h = renderHarness({
      rules: { ...matterRules, rules: [hours] },
      writes: respond({
        change: () => ({
          override: createdBody({ kind: 'RuleChanged' }),
          recordRule: { ruleSetId: 'rs-new', policyId: 'pol-new', code: 'HOURS', versionId: 'v', versionNumber: 1, basedOnVersionId: 'v-2', scope: 'Record', firmId: null },
          closeComplete: true,
          affectedText: null,
          visibleOpenInvoices: null,
          message: null,
        }),
      }),
    });
    const d = await openRule('r-hours', 'HOURS · Excessive daily hours');
    await toChoices(d);
    chooseRadio(d, /^Change for this record/);
    typeReason(d, 'Staffing');
    fireEvent.click(next(d));
    await within(d).findByTestId('values-step');
    fireEvent.change(within(d).getByLabelText(/Most hours per day/), { target: { value: '12' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    typeNote(d, 'Cap raised for trial week');
    fireEvent.click(button(d, 'Save record rule'));
    await within(d).findByTestId('success-body');
    expect(bodyOf(h.writeCalls[0])).toEqual({ policyId: 'r-hours', reason: 'Staffing', ruleBody: JSON.stringify({ type: 'DailyHoursCap', maxHoursPerDay: 12 }), note: 'Cap raised for trial week' });
  });

  it('a removal (DELETE) sends a JSON body { note } with a JSON content type only when a note is given', async () => {
    const withNote = renderHarness({ writes: respond({ preview: () => previewBody(), remove: () => removedBody() }) });
    let d = await openProcedure('p-instr', 'Acme v. Northwind billing instructions');
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    typeNote(d, ' No longer needed ');
    fireEvent.click(button(d, 'Remove override'));
    await within(d).findByTestId('success-body');
    const del = withNote.writeCalls.find(c => c.route === 'remove')!;
    expect(del.method).toBe('DELETE');
    expect(del.body).toEqual({ note: 'No longer needed' });
    expect(del.headers).toEqual({ 'Content-Type': 'application/json' });
    withNote.unmount();

    const blank = renderHarness({ writes: respond({ preview: () => previewBody(), remove: () => removedBody() }) });
    d = await openProcedure('p-instr', 'Acme v. Northwind billing instructions');
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    typeNote(d, '   ');
    fireEvent.click(button(d, 'Remove override'));
    await within(d).findByTestId('success-body');
    const bare = blank.writeCalls.find(c => c.route === 'remove')!;
    expect(bare.method).toBe('DELETE');
    expect(bare.body).toBeUndefined();
    expect(bare.headers).toBeUndefined();
  });

  it('unpin (As set on the parent removes the own pin): DELETE with the note', async () => {
    const own = override({ rowId: 'own-pin', mode: 'Pinned', chainRecord: MATTER, pinnedVersion: V.rate1 });
    const par = override({ rowId: 'par-pin', mode: 'Pinned', chainRecord: { ...MATTER, distance: 1 }, pinnedVersion: V.rate2 });
    const view: RecordProceduresView = {
      ...matterProcedures,
      gaps: [],
      procedures: [procedure({ id: 'p-x', name: 'Rates with a parent', appliesTo: 'Firm', firm: FENWICK, pinned: true, overrides: [own, par] })],
    };
    const h = renderHarness({ procedures: view, writes: respond({ preview: () => previewBody(), remove: () => removedBody() }) });
    const d = await openProcedure('p-x', 'Rates with a parent');
    await toChoices(d);
    chooseRadio(d, /^As set on the parent/);
    await toConfirm(d);
    typeNote(d, 'Follow the parent');
    fireEvent.click(button(d, 'Remove override'));
    await within(d).findByTestId('success-body');
    expect(writes(h.writeCalls).map(c => `${c.method} ${c.overrideId}`)).toEqual(['DELETE own-pin']);
    expect(writes(h.writeCalls)[0].body).toEqual({ note: 'Follow the parent' });
  });

  it('Pin-to-Follow style plan (remove, then create): the same note goes on BOTH calls', async () => {
    const h = renderHarness({
      writes: respond({ preview: () => previewBody(), remove: () => removedBody(), assign: () => createdBody({ mode: 'Excluded' }) }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Rates no longer apply');
    await toConfirm(d);
    typeNote(d, 'Switching approach');
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    const ws = writes(h.writeCalls);
    expect(ws.map(c => c.route)).toEqual(['remove', 'assign']);
    expect(ws[0].body).toEqual({ note: 'Switching approach' });
    expect(bodyOf(ws[1]).note).toBe('Switching approach');
  });
});

describe('the NotRecorded warning', () => {
  const finishExclude = async (answer: unknown) => {
    renderHarness({ writes: respond({ assign: () => answer }) });
    const d = await toExcludeConfirm();
    fireEvent.click(button(d, 'Save override'));
    const body = await within(d).findByTestId('success-body');
    return { d, body };
  };

  it('NotRecorded: the success screen carries the warning bar with the exact words', async () => {
    expect(NOT_RECORDED_LINE).toBe(WARNING_TEXT);
    const { d, body } = await finishExclude(createdBody({ mode: 'Excluded', decisionRecord: NOT_RECORDED }));
    const bar = within(d).getByTestId('success-not-recorded');
    expect(bar).toHaveTextContent(WARNING_TEXT);
    expect(body).toContainElement(bar);
    // The change itself still reads as saved.
    expect(within(d).getByTestId('success-o21')).toBeInTheDocument();
  });

  it.each([
    ['Queued', QUEUED],
    ['Recorded', RECORDED],
  ])('%s: no warning (the server retries a queued record)', async (_, decisionRecord) => {
    const { d, body } = await finishExclude(createdBody({ mode: 'Excluded', decisionRecord }));
    expect(within(d).queryByTestId('success-not-recorded')).not.toBeInTheDocument();
    expect(body).not.toHaveTextContent(/decision record/i);
  });

  it.each([
    ['no decisionRecord member (an older server)', createdBody({ mode: 'Excluded' })],
    ['decisionRecord null', createdBody({ mode: 'Excluded', decisionRecord: null })],
    ['an unknown status', createdBody({ mode: 'Excluded', decisionRecord: { status: 'Pending' } })],
  ])('%s: no warning', async (_, answer) => {
    const { d, body } = await finishExclude(answer);
    expect(within(d).queryByTestId('success-not-recorded')).not.toBeInTheDocument();
    expect(body).not.toHaveTextContent(/decision record/i);
  });

  it('a removal whose answer says NotRecorded warns; one that says Recorded does not', async () => {
    const run = async (decisionRecord: unknown) => {
      const h = renderHarness({ writes: respond({ preview: () => previewBody(), remove: () => removedBody({ decisionRecord }) }) });
      const d = await openProcedure('p-instr', 'Acme v. Northwind billing instructions');
      await toChoices(d);
      chooseRadio(d, /^Remove from this record/);
      await toConfirm(d);
      fireEvent.click(button(d, 'Remove override'));
      await within(d).findByTestId('success-body');
      const shown = within(d).queryByTestId('success-not-recorded') !== null;
      h.unmount();
      return shown;
    };
    expect(await run(NOT_RECORDED)).toBe(true);
    expect(await run(RECORDED)).toBe(false);
  });

  it('a rule change carries the member inside `override`: NotRecorded there warns, and Queued there does not', async () => {
    const hours: RuleItem = rule({ policyId: 'r-hours', code: 'HOURS', name: 'Excessive daily hours', ruleType: 'DailyHoursCap', parameterSummary: '11 hours per timekeeper per day', ruleSetId: 'rs-hours', ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 11 } });
    const run = async (decisionRecord: unknown) => {
      const h = renderHarness({
        rules: { ...matterRules, rules: [hours] },
        writes: respond({
          change: () => ({
            override: createdBody({ kind: 'RuleChanged', decisionRecord }),
            recordRule: { ruleSetId: 'rs-new', policyId: 'pol-new', code: 'HOURS', versionId: 'v', versionNumber: 1, basedOnVersionId: 'v-2', scope: 'Record', firmId: null },
            closeComplete: true,
            affectedText: null,
            visibleOpenInvoices: null,
            message: null,
          }),
        }),
      });
      const d = await openRule('r-hours', 'HOURS · Excessive daily hours');
      await toChoices(d);
      chooseRadio(d, /^Change for this record/);
      typeReason(d, 'Staffing');
      fireEvent.click(next(d));
      await within(d).findByTestId('values-step');
      fireEvent.change(within(d).getByLabelText(/Most hours per day/), { target: { value: '12' } });
      await waitFor(() => expect(next(d)).toBeEnabled());
      fireEvent.click(next(d));
      await within(d).findByTestId('impact-step');
      await waitFor(() => expect(next(d)).toBeEnabled());
      fireEvent.click(next(d));
      await within(d).findByTestId('confirm-step');
      fireEvent.click(button(d, 'Save record rule'));
      await within(d).findByTestId('success-body');
      const shown = within(d).queryByTestId('success-not-recorded') !== null;
      h.unmount();
      return shown;
    };
    expect(await run(NOT_RECORDED)).toBe(true);
    expect(await run(QUEUED)).toBe(false);
  });

  it('a plan of two calls whose second is refused after the first said NotRecorded keeps the warning in the error text', async () => {
    const h = renderHarness({
      writes: respond({
        preview: () => previewBody(),
        remove: () => removedBody({ decisionRecord: NOT_RECORDED }),
        assign: () => refusal(409, 'Conflict', 'assignment.duplicate', { detail: 'Remove that override first.' }),
      }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'r');
    await toConfirm(d);
    fireEvent.click(button(d, 'Save override'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Conflict');
    expect(alert).toHaveTextContent(WARNING_TEXT);
    expect(h.writeCalls.filter(c => c.route === 'remove')).toHaveLength(1);
  });
});

describe('the server\'s 400 for a note is shown with its code', () => {
  it.each([
    ['assignment.note_too_long', 'The note is longer than 2000 characters.'],
    ['assignment.note_invalid_characters', 'The note holds a control character. Only line breaks and tabs are allowed.'],
  ])('%s: the detail and the code are shown, the wizard stays open on Confirm', async (code, detail) => {
    renderHarness({ writes: respond({ assign: () => refusal(400, 'Bad Request', code, { detail }) }) });
    const d = await toExcludeConfirm();
    typeNote(d, 'fine to the page');
    fireEvent.click(button(d, 'Save override'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent(detail);
    expect(alert).toHaveTextContent(`(${code})`);
    expect(within(d).getByTestId('confirm-step')).toBeInTheDocument();
    expect(within(d).queryByTestId('success-body')).not.toBeInTheDocument();
    // The typed note is still there for the user to fix.
    expect(noteBox(d).value).toBe('fine to the page');
  });

  it('another 400 does not get a code appended', async () => {
    renderHarness({ writes: respond({ assign: () => refusal(400, 'Bad Request', 'assignment.invalid', { detail: 'Not valid.' }) }) });
    const d = await toExcludeConfirm();
    fireEvent.click(button(d, 'Save override'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).not.toHaveTextContent('(assignment.invalid)');
  });
});

describe('the Assign Procedure wizard has the Note box too', () => {
  const assignable = {
    record: MATTER,
    date: TODAY,
    firm: FENWICK,
    firmSource: 'Record',
    q: null,
    bound: 50,
    tooMany: false,
    procedures: [
      {
        id: 'p-lit',
        name: 'Litigation billing instructions',
        type: 'Guideline',
        appliesTo: 'AssignedRecordsOnly',
        firm: FENWICK,
        statusIfAssigned: 'InForce',
        versionInForce: { id: 'lit-v2', number: 2, status: 'Active', from: '2026-01-01', to: null },
        gap: null,
      },
    ],
    notOffered: [],
    label: 'If assigned now',
  };

  const run = async (note: string | null) => {
    const h = renderHarness({ assignable: () => assignable, writes: respond({ assign: () => createdBody({ procedureId: 'p-lit', decisionRecord: NOT_RECORDED }) }) });
    await ready();
    fireEvent.click(screen.getByTestId('assign-procedure'));
    const d = await screen.findByRole('dialog', { name: 'Assign Procedure', hidden: true });
    await within(d).findByTestId('assign-list');
    chooseRadio(d, /^Litigation billing instructions/);
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    if (note !== null) typeNote(d, note);
    fireEvent.click(button(d, 'Assign Procedure'));
    await within(d).findByTestId('success-body');
    expect(within(d).getByTestId('success-not-recorded')).toHaveTextContent(WARNING_TEXT);
    return bodyOf(h.writeCalls.find(c => c.route === 'assign')!);
  };

  it('Follow latest with a note sends it', async () => {
    expect(await run('Assigned for the trial')).toEqual({ procedureId: 'p-lit', mode: 'FollowLatest', note: 'Assigned for the trial' });
  });

  it('a blank note is not sent', async () => {
    expect(await run('  ')).toEqual({ procedureId: 'p-lit', mode: 'FollowLatest' });
  });
});
