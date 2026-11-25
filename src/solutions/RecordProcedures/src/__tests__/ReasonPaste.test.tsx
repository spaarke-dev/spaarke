/**
 * ISS-027 (#2878): the Reason box of the Procedure, Rule and Assign wizards never cuts a paste. It used to carry `maxLength`, so a paste over
 * 2,000 characters was cut by the browser with no word and the cut reason was saved. Now (as the Note box since 084h-4) the box keeps the whole
 * text, shows a count against the limit and a message when over it, Next stays off (so nothing can be sent), and trimming to the limit lets the
 * wizard send the full text.
 *
 * Through the real panel, wizard shell and clients with the fake BFF (wizardHarness). jsdom does not cut a scripted change, so "a browser would
 * not cut a paste" is proved by the absence of the `maxlength` attribute, plus the whole 2,600 characters staying in the box.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { MAX_REASON_CHARS, reasonProblem } from '../components/wizards/plan';
import type { AssignableProcedureItem, RecordAssignableProceduresView } from '../services/recordProceduresApi';
import { FENWICK, MATTER, TODAY } from './fixtures';
import { button, chooseRadio, createdBody, next, openProcedure, openRule, previewBody, procedureBody, ready, renderHarness, respond, toChoices, toConfirm, typeReason } from './wizardHarness';
import type { WriteCall } from './testKit';

const PASTE = 'y'.repeat(2600);
const AT_LIMIT = 'y'.repeat(MAX_REASON_CHARS);
const OVER_MESSAGE = 'The reason may be at most 2000 characters.';

const reasonBox = (d: HTMLElement) => within(d).getByLabelText(/^Reason/) as HTMLTextAreaElement;
/** The writes the wizard made (the version read and the removal preview are not writes). */
const writes = (calls: WriteCall[]) => calls.filter(c => c.route !== 'procedure' && c.route !== 'preview');

/** The 2,600-character paste: whole in the box, the count and the message shown, Next off, nothing sent. */
function expectPasteHeld(d: HTMLElement, calls: WriteCall[]) {
  expect(reasonBox(d).value).toHaveLength(2600);
  // jsdom does not cut a scripted change, a browser does cut a paste at maxlength: the attribute itself must be absent.
  expect(reasonBox(d)).not.toHaveAttribute('maxlength');
  expect(d).toHaveTextContent('2,600 / 2,000 characters');
  expect(d).toHaveTextContent(OVER_MESSAGE);
  expect(next(d)).toBeDisabled();
  expect(writes(calls)).toHaveLength(0);
}

describe('reasonProblem: the message for a reason over the limit', () => {
  it.each([
    ['undefined', undefined, null],
    ['blank', '   ', null],
    ['at the limit', AT_LIMIT, null],
    ['at the limit with outer spaces (the server counts trimmed)', `  ${AT_LIMIT}  `, null],
    ['one over', 'y'.repeat(MAX_REASON_CHARS + 1), OVER_MESSAGE],
    ['a 2,600-character paste', PASTE, OVER_MESSAGE],
  ])('%s', (_, text, expected) => {
    expect(reasonProblem(text)).toBe(expected);
  });
});

describe('Procedure wizard: the Reason box', () => {
  it('starts empty at 0 / 2,000 with no maxlength; a 2,600-character paste is kept whole with the message, Next is off and nothing is sent; trimmed to 2,000 it sends the full text', async () => {
    const h = renderHarness({ writes: respond({ assign: () => createdBody({ mode: 'Excluded' }) }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    expect(reasonBox(d).value).toBe('');
    expect(reasonBox(d)).not.toHaveAttribute('maxlength');
    expect(d).toHaveTextContent('0 / 2,000 characters');
    expect(d).not.toHaveTextContent(OVER_MESSAGE);

    typeReason(d, PASTE);
    expectPasteHeld(d, h.writeCalls);

    typeReason(d, AT_LIMIT);
    expect(d).toHaveTextContent('2,000 / 2,000 characters');
    expect(d).not.toHaveTextContent(OVER_MESSAGE);
    await waitFor(() => expect(next(d)).toBeEnabled());
    await toConfirm(d);
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    const sent = writes(h.writeCalls);
    expect(sent).toHaveLength(1);
    expect((sent[0].body as { reason: string }).reason).toBe(AT_LIMIT);
  });

  it('Pin shares the box: a 2,600-character paste holds Next and nothing is sent', async () => {
    const h = renderHarness({ writes: respond({ procedure: () => procedureBody([{ id: 'ocg-v1', n: 1, status: 'Superseded', from: '2025-01-01', to: '2026-01-01' }, { id: 'ocg-v2', n: 2, status: 'Active', from: '2026-01-01' }]), assign: () => createdBody() }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Pin a version/);
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    fireEvent.click(await screen.findByRole('option', { name: /^v1/, hidden: true }));
    typeReason(d, 'Grandfathered');
    expect(next(d)).toBeEnabled();
    typeReason(d, PASTE);
    expectPasteHeld(d, h.writeCalls);
  });
});

describe('Rule wizard: the Reason box', () => {
  it('a 2,600-character paste is kept whole with the message, Next is off and nothing is sent; trimmed to 2,000 it sends the full text', async () => {
    const h = renderHarness({ writes: respond({ exclude: () => createdBody() }) });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    expect(reasonBox(d)).not.toHaveAttribute('maxlength');
    expect(d).toHaveTextContent('0 / 2,000 characters');

    typeReason(d, PASTE);
    expectPasteHeld(d, h.writeCalls);

    typeReason(d, AT_LIMIT);
    expect(d).toHaveTextContent('2,000 / 2,000 characters');
    expect(d).not.toHaveTextContent(OVER_MESSAGE);
    await waitFor(() => expect(next(d)).toBeEnabled());
    await toConfirm(d);
    fireEvent.click(button(d, 'Exclude rule'));
    await within(d).findByTestId('success-body');
    const sent = writes(h.writeCalls);
    expect(sent).toHaveLength(1);
    expect((sent[0].body as { reason: string }).reason).toBe(AT_LIMIT);
  });
});

describe('Assign Procedure wizard: the Reason box', () => {
  const litigation: AssignableProcedureItem = {
    id: 'p-lit',
    name: 'Litigation billing instructions',
    type: 'Guideline',
    appliesTo: 'AssignedRecordsOnly',
    firm: FENWICK,
    statusIfAssigned: 'InForce',
    versionInForce: { id: 'lit-v2', number: 2, status: 'Active', from: '2026-01-01', to: null },
    gap: null,
  };
  const assignable = (): RecordAssignableProceduresView => ({
    record: MATTER,
    date: TODAY,
    firm: FENWICK,
    firmSource: 'Record',
    q: null,
    bound: 50,
    tooMany: false,
    procedures: [litigation],
    notOffered: [],
    label: 'If assigned now',
  });

  it('Pin: a 2,600-character paste is kept whole with the message, Next is off and nothing is sent; trimmed to 2,000 it sends the full text', async () => {
    const versions = procedureBody([
      { id: 'lit-v1', n: 1, status: 'Superseded', from: '2025-01-01', to: '2026-01-01' },
      { id: 'lit-v2', n: 2, status: 'Active', from: '2026-01-01' },
    ]);
    const h = renderHarness({ assignable, writes: respond({ procedure: () => versions, assign: () => createdBody({ mode: 'Pinned' }), preview: () => previewBody() }) });
    await ready();
    fireEvent.click(screen.getByTestId('assign-procedure'));
    const d = await screen.findByRole('dialog', { name: 'Assign Procedure', hidden: true });
    await within(d).findByTestId('assign-list');
    chooseRadio(d, /^Litigation billing instructions/);
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    chooseRadio(d, /^Pin a version/);
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    fireEvent.click(await screen.findByRole('option', { name: /^v1/, hidden: true }));
    expect(reasonBox(d)).not.toHaveAttribute('maxlength');
    expect(d).toHaveTextContent('0 / 2,000 characters');

    typeReason(d, PASTE);
    expectPasteHeld(d, h.writeCalls);

    typeReason(d, AT_LIMIT);
    expect(d).toHaveTextContent('2,000 / 2,000 characters');
    expect(d).not.toHaveTextContent(OVER_MESSAGE);
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Assign Procedure'));
    await within(d).findByTestId('success-body');
    const sent = writes(h.writeCalls);
    expect(sent).toHaveLength(1);
    expect((sent[0].body as { reason: string }).reason).toBe(AT_LIMIT);
  });
});
