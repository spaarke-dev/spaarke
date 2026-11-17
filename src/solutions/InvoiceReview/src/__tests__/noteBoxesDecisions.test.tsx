/**
 * Task 095f-2ui: the optional Note box on the invoice decisions: the decision wizard (approve, reject, return), the blocked-submission
 * Override and Return, and the invoice adjustment (add and remove).
 *
 * Through the real page, wizard shell and forms with mocked BFF clients (the harness of InvoiceDecisions.test.tsx). What is asserted is what
 * the client is asked to send, and what the reviewer sees: the note as typed (or no key when blank), a refused note under the Note box and not
 * as a page error, the reason and note kept across a 409 or a 503, no page reload on `concurrent_change`, Repeat resending the exact request,
 * and the removal's `id_reused` re-reading the adjustments list.
 */

import '@testing-library/jest-dom';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import type { InvoiceReview } from '../services/invoiceReviewApi';
import {
  INVOICE_ID,
  SUBMISSION_ID,
  approval,
  failed,
  heldReview,
  line,
  lineId,
  mockApi,
  mockDecisionApi,
  problem,
  review,
  type MockDecisionApi,
} from './testKit';

function renderDecisions(reviews: InvoiceReview | InvoiceReview[], decision: MockDecisionApi) {
  const api = mockApi(reviews);
  render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={api as never} decisionApi={decision as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
  return { api };
}

const clean = () => review([line(1), line(2)]);

async function openWizard(): Promise<HTMLElement> {
  fireEvent.click(await screen.findByTestId('decide-invoice'));
  return screen.findByRole('dialog', { name: 'Decide invoice', hidden: true });
}

const next = (d: HTMLElement) => within(d).getByRole('button', { name: 'Next', hidden: true });
const radio = (d: HTMLElement, name: RegExp) => within(d).getByRole('radio', { name, hidden: true });
const finishBtn = (d: HTMLElement, name: RegExp) => within(d).getByRole('button', { name, hidden: true });
const noteBox = (d: HTMLElement) => within(d).getByLabelText('Note for the decision') as HTMLTextAreaElement;
const typeNote = (d: HTMLElement, text: string) => fireEvent.change(noteBox(d), { target: { value: text } });

async function toConfirmViaReject(d: HTMLElement, reason = 'Duplicate of an earlier invoice') {
  fireEvent.click(radio(d, /^Reject/));
  fireEvent.click(next(d));
  fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: reason } });
  fireEvent.click(next(d));
  await within(d).findByTestId('confirm-decision');
}

async function toConfirmViaReturn(d: HTMLElement, reason = 'Please correct the rates') {
  fireEvent.click(radio(d, /^Return/));
  fireEvent.click(next(d));
  fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: reason } });
  fireEvent.click(next(d));
  await within(d).findByTestId('confirm-decision');
}

async function toConfirmViaApprove(d: HTMLElement) {
  fireEvent.click(next(d)); // Decision -> Invoice adjustment
  await within(d).findByLabelText('Adjustment value');
  fireEvent.click(within(d).getByRole('button', { name: 'Skip', hidden: true }));
  await within(d).findByTestId('confirm-decision');
}

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// The decision wizard
// ─────────────────────────────────────────────────────────────────────────────

describe('the decision wizard: the Note box on the Confirm step', () => {
  it('is on the Confirm step only, empty, optional, counted against 2000, with no maxlength', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    expect(within(d).queryByLabelText('Note for the decision')).not.toBeInTheDocument();
    await toConfirmViaReject(d);
    expect(noteBox(d).value).toBe('');
    expect(noteBox(d)).not.toBeRequired();
    expect(noteBox(d)).not.toHaveAttribute('maxlength');
    expect(within(d).getByTestId('decision-note-count')).toHaveTextContent('0 / 2,000 characters');
    typeNote(d, 'Second duplicate this month');
    expect(within(d).getByTestId('decision-note-count')).toHaveTextContent('27 / 2,000 characters');
    // Typing a note does not stop Finish.
    expect(finishBtn(d, /Reject & record/)).toBeEnabled();
  });

  it('approve sends the note as typed (the decision id and amount are the server\'s)', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaApprove(d);
    typeNote(d, '  Agreed with the partner  ');
    fireEvent.click(finishBtn(d, /Approve .*5,000\.00 & record/));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledWith(INVOICE_ID, '  Agreed with the partner  '));
  });

  it('reject sends the reason and the note', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d, 'Duplicate of an earlier invoice');
    typeNote(d, 'Second duplicate this month');
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledWith(INVOICE_ID, 'Duplicate of an earlier invoice', 'Second duplicate this month'));
  });

  it('return sends the reason and the note', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReturn(d, 'Please correct the rates');
    typeNote(d, 'Rates only; hours are fine');
    fireEvent.click(finishBtn(d, /Return to firm & record/));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledWith(INVOICE_ID, 'Please correct the rates', 'Rates only; hours are fine'));
  });

  it.each([
    ['nothing typed', ''],
    ['spaces', '     '],
    ['a line break and a tab', ' \n\t '],
  ])('with %s no note is sent: the call is exactly the old one', async (_, text) => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d);
    if (text) typeNote(d, text);
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    // Two arguments only: no third argument (not undefined, not "", not null).
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Duplicate of an earlier invoice']);
    expect(decision.reject.mock.calls[0]).toHaveLength(2);
  });

  it('approve with no note is the old call: the invoice id alone', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaApprove(d);
    fireEvent.click(finishBtn(d, /Approve .*5,000\.00 & record/));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledTimes(1));
    expect(decision.approve.mock.calls[0]).toEqual([INVOICE_ID]);
  });

  it('a paste over 2000 characters is kept whole and counted, Finish is held and nothing is sent; at 2000 it sends the whole note', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d);
    typeNote(d, 'z'.repeat(2600));
    expect(noteBox(d).value).toHaveLength(2600);
    expect(noteBox(d)).not.toHaveAttribute('maxlength');
    expect(within(d).getByTestId('decision-note-count')).toHaveTextContent('2,600 / 2,000 characters');
    expect(within(d).getByTestId('decision-note-field')).toHaveTextContent('The note may be at most 2000 characters.');
    await waitFor(() => expect(finishBtn(d, /Reject & record/)).toBeDisabled());
    expect(decision.reject).not.toHaveBeenCalled();
    typeNote(d, ` ${'z'.repeat(2000)} `);
    await waitFor(() => expect(finishBtn(d, /Reject & record/)).toBeEnabled());
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0][2]).toBe(` ${'z'.repeat(2000)} `);
  });

  it.each([
    ['ebilling.note_too_long', 'The note may be at most 2000 characters.'],
    ['ebilling.note_invalid_characters', 'The note contains a control character that cannot be saved. Remove it and try again.'],
  ])('a 400 %s shows its sentence under the Note box (not as the page error) and keeps the form', async (code, detail) => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValue(failed(problem(400, code, { detail })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d);
    typeNote(d, 'a note');
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(within(within(d).getByTestId('decision-note-field')).getByText(detail)).toBeInTheDocument());
    // Review round 1: the field message is the only message. The shell's error bar says nothing (no second alert outside the Note field), and
    // the wizard stays on the Confirm step with Finish available for another try.
    // (Fluent's field message is an alert too, so any alert that is not inside the Note field would be the shell's bar.)
    const outsideField = within(d)
      .queryAllByRole('alert', { hidden: true })
      .filter(a => !within(d).getByTestId('decision-note-field').contains(a));
    expect(outsideField).toHaveLength(0);
    expect(within(d).queryByText(/The note was not accepted/)).not.toBeInTheDocument();
    expect(within(d).getByTestId('confirm-decision')).toBeInTheDocument();
    expect(finishBtn(d, /Reject & record/)).toBeEnabled();
    expect(noteBox(d).value).toBe('a note');
    expect(noteBox(d)).not.toHaveAttribute('readonly');
    // Editing the note clears the message.
    typeNote(d, 'a shorter note');
    expect(within(within(d).getByTestId('decision-note-field')).queryByText(detail)).not.toBeInTheDocument();
  });

  it('keeps the reason and the note across a 409, and Finish sends them again', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.changed', { detail: 'The invoice changed.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d, 'Duplicate of an earlier invoice');
    typeNote(d, 'Keep this note');
    fireEvent.click(finishBtn(d, /Reject & record/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('The invoice changed.');
    expect(noteBox(d).value).toBe('Keep this note');
    expect(within(d).getByTestId('confirm-reason')).toHaveTextContent('Duplicate of an earlier invoice');
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual([INVOICE_ID, 'Duplicate of an earlier invoice', 'Keep this note']);
  });

  it('after a 503 that saved part of the decision the note is locked, and the repeat is the exact same request', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.invoice.decision_incomplete', { detail: 'The decision was saved but the invoice did not close.' }))
    );
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d);
    typeNote(d, 'The first note');
    fireEvent.click(finishBtn(d, /Reject & record/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(noteBox(d)).toHaveAttribute('readonly');
    expect(within(d).getByTestId('decision-note-locked')).toBeInTheDocument();
    expect(noteBox(d).value).toBe('The first note');
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[1][2]).toBe('The first note');
  });

  it('a decided invoice reopens read-only with no Note box', async () => {
    const rej = review([line(1)]);
    rej.header.status = 'Rejected';
    renderDecisions(rej, mockDecisionApi(approval({ status: 'Rejected', canApprove: false, decidedBy: 'Pat Approver' })));
    fireEvent.click(await screen.findByTestId('view-decision'));
    const d = await screen.findByRole('dialog', { name: 'Invoice decision', hidden: true });
    fireEvent.click(next(d));
    await within(d).findByLabelText('Reason for the decision');
    expect(within(d).queryByLabelText('Note for the decision')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// The blocked submission: override and return
// ─────────────────────────────────────────────────────────────────────────────

describe('the blocked submission: the Note box on Override and Return', () => {
  const held = () => approval({ held: true, canApprove: false });
  const dialogOf = (name: string) => screen.findByRole('dialog', { name, hidden: true });
  const blockedNote = (dialog: HTMLElement) => within(dialog).getByLabelText('Note') as HTMLTextAreaElement;

  async function openOverride(decision: MockDecisionApi, reviews: InvoiceReview | InvoiceReview[] = heldReview()) {
    const { api } = renderDecisions(reviews, decision);
    fireEvent.click(await screen.findByTestId('override-block'));
    const dialog = await dialogOf('Override the block');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Known resubmission' } });
    return { api, dialog };
  }

  it('override sends the reason and the note as typed', async () => {
    const decision = mockDecisionApi(held());
    const { dialog } = await openOverride(decision, [heldReview(), review([line(1)])]);
    expect(blockedNote(dialog)).not.toHaveAttribute('maxlength');
    expect(within(dialog).getByTestId('blocked-note-count')).toHaveTextContent('0 / 2,000 characters');
    fireEvent.change(blockedNote(dialog), { target: { value: ' Checked with the firm ' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledWith(SUBMISSION_ID, 'Known resubmission', ' Checked with the firm '));
  });

  it('blocked return sends the reason and the note as typed', async () => {
    const decision = mockDecisionApi(held());
    renderDecisions([heldReview(), review([line(1)])], decision);
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const dialog = await dialogOf('Return to firm');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Please resubmit without the duplicate' } });
    fireEvent.change(blockedNote(dialog), { target: { value: 'Duplicate of INV-7' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Return', hidden: true }));
    await waitFor(() => expect(decision.returnBlocked).toHaveBeenCalledWith(SUBMISSION_ID, 'Please resubmit without the duplicate', 'Duplicate of INV-7'));
  });

  it.each([
    ['nothing typed', ''],
    ['spaces', '   '],
  ])('with %s no note is sent: the call is exactly the old one', async (_, text) => {
    const decision = mockDecisionApi(held());
    const { dialog } = await openOverride(decision, [heldReview(), review([line(1)])]);
    if (text) fireEvent.change(blockedNote(dialog), { target: { value: text } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(1));
    expect(decision.overrideBlocked.mock.calls[0]).toEqual([SUBMISSION_ID, 'Known resubmission']);
  });

  it('holds Override off for a note over 2000 characters, and says so under the box without cutting the paste', async () => {
    const decision = mockDecisionApi(held());
    const { dialog } = await openOverride(decision);
    fireEvent.change(blockedNote(dialog), { target: { value: 'q'.repeat(2500) } });
    expect(blockedNote(dialog).value).toHaveLength(2500);
    expect(within(dialog).getByTestId('blocked-note-count')).toHaveTextContent('2,500 / 2,000 characters');
    expect(within(dialog).getByTestId('blocked-note-field')).toHaveTextContent('The note may be at most 2000 characters.');
    expect(within(dialog).getByRole('button', { name: 'Override', hidden: true })).toBeDisabled();
    expect(decision.overrideBlocked).not.toHaveBeenCalled();
  });

  it('a 400 note code is a message under the Note box, not the form error, and the form keeps both texts', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValue(failed(problem(400, 'ebilling.note_invalid_characters', { detail: 'The note contains a control character that cannot be saved. Remove it and try again.' })));
    const { dialog } = await openOverride(decision);
    fireEvent.change(blockedNote(dialog), { target: { value: 'bad\u0007note' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    await waitFor(() => expect(within(within(dialog).getByTestId('blocked-note-field')).getByText(/control character/)).toBeInTheDocument());
    expect(within(dialog).queryByTestId('blocked-error')).not.toBeInTheDocument();
    expect(blockedNote(dialog).value).toBe('bad\u0007note');
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Known resubmission');
  });

  it('a 409 concurrent_change (a record MAY have been written; locking is conservative) locks the reason and the note, re-reads the page, and the resend is the same request', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(
      failed(problem(409, 'ebilling.blocked.concurrent_change', { detail: 'The invoice changed while the override was running.' }))
    );
    const { api, dialog } = await openOverride(decision);
    fireEvent.change(blockedNote(dialog), { target: { value: 'Keep this note' } });
    await waitFor(() => expect(decision.getBlocked).toHaveBeenCalledTimes(1));
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    expect(await within(dialog).findByTestId('blocked-error')).toHaveTextContent('(ebilling.blocked.concurrent_change)');
    expect(blockedNote(dialog).value).toBe('Keep this note');
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Known resubmission');
    // Both are locked: a repeat finishes from the STORED reason and note, so an edit would be dropped without a word.
    expect(blockedNote(dialog)).toHaveAttribute('readonly');
    expect(within(dialog).getByLabelText('Reason')).toHaveAttribute('readonly');
    expect(within(dialog).getByTestId('blocked-note-locked')).toBeInTheDocument();
    // The page was read again, to show what the server holds (the hold may be gone).
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    expect(decision.overrideBlocked.mock.calls[1]).toEqual([SUBMISSION_ID, 'Known resubmission', 'Keep this note']);
  });

  it('(must-not-fire) a 409 answered BEFORE any record (not_blocked) keeps the form editable and does not re-read the page', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.not_blocked', { detail: 'This submission is not blocked.' })));
    const { api, dialog } = await openOverride(decision);
    fireEvent.change(blockedNote(dialog), { target: { value: 'Keep this note' } });
    await waitFor(() => expect(decision.getBlocked).toHaveBeenCalledTimes(1));
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    expect(await within(dialog).findByTestId('blocked-error')).toHaveTextContent('(ebilling.blocked.not_blocked)');
    expect(blockedNote(dialog)).not.toHaveAttribute('readonly');
    expect(within(dialog).getByLabelText('Reason')).not.toHaveAttribute('readonly');
    expect(within(dialog).queryByTestId('blocked-note-locked')).not.toBeInTheDocument();
    expect(api.getReview.mock.calls.length).toBe(reads);
    expect(decision.getBlocked).toHaveBeenCalledTimes(1);
  });

  it('after a 503 that saved part of the decision the note is locked and the repeat is the exact same request', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'The decision was recorded but not every Signal closed.' }))
    );
    const { dialog } = await openOverride(decision);
    fireEvent.change(blockedNote(dialog), { target: { value: 'The first note' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    expect(await within(dialog).findByTestId('blocked-error')).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(blockedNote(dialog)).toHaveAttribute('readonly');
    expect(blockedNote(dialog).value).toBe('The first note');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    expect(decision.overrideBlocked.mock.calls[1]).toEqual(decision.overrideBlocked.mock.calls[0]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// The invoice adjustment: add and remove
// ─────────────────────────────────────────────────────────────────────────────

describe('the invoice adjustment: the Note box on add and remove', () => {
  const adj = () => line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy' });
  const withAdjustment = () => review([line(1), adj()], { totals: { billed: 5000, fees: 5000, expenses: 0, lineAdjustments: 0, invoiceAdjustments: -250, approved: 4750 } });

  async function toAdjust(decision: MockDecisionApi, reviews: InvoiceReview | InvoiceReview[] = clean()) {
    const { api } = renderDecisions(reviews, decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.change(await within(d).findByLabelText('Adjustment value'), { target: { value: '5' } });
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Courtesy' } });
    return { api, d };
  }
  const addNote = (d: HTMLElement) => within(d).getByLabelText('Adjustment note') as HTMLTextAreaElement;

  it('add sends the note as typed with the method, value, reason and rule', async () => {
    const decision = mockDecisionApi();
    const { d } = await toAdjust(decision);
    expect(addNote(d)).not.toHaveAttribute('maxlength');
    expect(within(d).getByTestId('adjustment-note-count')).toHaveTextContent('0 / 2,000 characters');
    fireEvent.change(addNote(d), { target: { value: ' Partner agreed ' } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(1));
    expect(decision.addAdjustment).toHaveBeenCalledWith(INVOICE_ID, {
      adjustmentId: expect.stringMatching(/^[0-9a-f-]{36}$/),
      method: 'percent',
      value: 5,
      reason: 'Courtesy',
      ruleId: null,
      note: ' Partner agreed ',
    });
  });

  it.each([
    ['nothing typed', ''],
    ['spaces', '   '],
  ])('add with %s: the note key is absent (not null, not "")', async (_, text) => {
    const decision = mockDecisionApi();
    const { d } = await toAdjust(decision);
    if (text) fireEvent.change(addNote(d), { target: { value: text } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(1));
    expect(Object.keys(decision.addAdjustment.mock.calls[0][1])).toEqual(['adjustmentId', 'method', 'value', 'reason', 'ruleId']);
  });

  it('holds Add off for a note over 2000 characters', async () => {
    const decision = mockDecisionApi();
    const { d } = await toAdjust(decision);
    fireEvent.change(addNote(d), { target: { value: 'n'.repeat(2100) } });
    expect(addNote(d).value).toHaveLength(2100);
    expect(within(d).getByTestId('adjustment-note-field')).toHaveTextContent('The note may be at most 2000 characters.');
    expect(within(d).getByTestId('add-adjustment')).toBeDisabled();
    expect(decision.addAdjustment).not.toHaveBeenCalled();
  });

  it('a 400 note code is a message under the Note box, not the adjustment error, and the form keeps everything', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValue(failed(problem(400, 'ebilling.note_too_long', { detail: 'The note may be at most 2000 characters.' })));
    const { d } = await toAdjust(decision);
    fireEvent.change(addNote(d), { target: { value: 'a note' } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(within(d).getByTestId('adjustment-note-field')).getByText('The note may be at most 2000 characters.')).toBeInTheDocument());
    expect(within(d).queryByTestId('adjustment-error')).not.toBeInTheDocument();
    expect(addNote(d).value).toBe('a note');
    expect(within(d).getByLabelText('Adjustment value')).toHaveValue(5);
    expect(within(d).getByLabelText('Adjustment reason')).toHaveValue('Courtesy');
    // The form is still open for another try: Add (not Repeat), enabled.
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Add adjustment');
  });

  it('a partly saved add (503) locks the note and Repeat resends the same adjustment id and the same note', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(failed(problem(503, 'ebilling.adjustment.incomplete', { detail: 'The line was saved but the totals were not.' })));
    const { d } = await toAdjust(decision);
    fireEvent.change(addNote(d), { target: { value: 'The first note' } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    expect(await within(d).findByTestId('adjustment-error')).toHaveTextContent('repeat it to finish');
    expect(addNote(d)).toBeDisabled();
    expect(addNote(d).value).toBe('The first note');
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    expect(decision.addAdjustment.mock.calls[1][1]).toEqual(decision.addAdjustment.mock.calls[0][1]);
    expect(decision.addAdjustment.mock.calls[1][1].note).toBe('The first note');
  });

  it('the note is cleared with the rest of the form once the adjustment is added', async () => {
    const decision = mockDecisionApi();
    const { d } = await toAdjust(decision, [clean(), withAdjustment()]);
    fireEvent.change(addNote(d), { target: { value: 'Partner agreed' } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toBeInTheDocument());
    await waitFor(() => expect(addNote(d).value).toBe(''));
  });

  async function toRemoveForm(decision: MockDecisionApi, reviews: InvoiceReview | InvoiceReview[]) {
    const { api } = renderDecisions(reviews, decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByTestId(`remove-adjustment-${lineId(90)}`));
    const confirm = await screen.findByRole('dialog', { name: 'Remove adjustment', hidden: true });
    return { api, d, confirm };
  }
  const removeNote = (confirm: HTMLElement) => within(confirm).getByLabelText('Removal note') as HTMLTextAreaElement;
  const submitRemove = (confirm: HTMLElement) => fireEvent.click(within(confirm).getByRole('button', { name: 'Remove', hidden: true }));

  it('remove sends the note in the DELETE', async () => {
    const decision = mockDecisionApi();
    const { confirm } = await toRemoveForm(decision, [withAdjustment(), review([line(1)])]);
    expect(removeNote(confirm)).not.toHaveAttribute('maxlength');
    fireEvent.change(removeNote(confirm), { target: { value: 'Entered twice' } });
    submitRemove(confirm);
    await waitFor(() => expect(decision.removeAdjustment).toHaveBeenCalledWith(INVOICE_ID, lineId(90), 'Entered twice'));
  });

  it('remove with no note is the old call', async () => {
    const decision = mockDecisionApi();
    const { confirm } = await toRemoveForm(decision, [withAdjustment(), review([line(1)])]);
    fireEvent.change(removeNote(confirm), { target: { value: '   ' } });
    submitRemove(confirm);
    await waitFor(() => expect(decision.removeAdjustment).toHaveBeenCalledTimes(1));
    expect(decision.removeAdjustment.mock.calls[0]).toEqual([INVOICE_ID, lineId(90)]);
  });

  it('a 400 note code on a removal is a message under the Note box, and the form stays open with the note', async () => {
    const decision = mockDecisionApi();
    decision.removeAdjustment.mockResolvedValue(failed(problem(400, 'ebilling.note_invalid_characters', { detail: 'The note contains a control character that cannot be saved. Remove it and try again.' })));
    const { confirm } = await toRemoveForm(decision, [withAdjustment(), review([line(1)])]);
    fireEvent.change(removeNote(confirm), { target: { value: 'bad\u0007' } });
    submitRemove(confirm);
    await waitFor(() => expect(within(within(confirm).getByTestId('remove-note-field')).getByText(/control character/)).toBeInTheDocument());
    expect(within(confirm).queryByTestId('remove-error')).not.toBeInTheDocument();
    expect(removeNote(confirm).value).toBe('bad\u0007');
  });

  it('a removal answered 409 ebilling.adjustment.id_reused re-reads the adjustments list; the adjustment is gone, so the form closes and the server\'s sentence stays on the step', async () => {
    const decision = mockDecisionApi();
    decision.removeAdjustment.mockResolvedValue(
      failed(problem(409, 'ebilling.adjustment.id_reused', { detail: 'This adjustment was already removed by another reviewer.' }))
    );
    // The list as the server holds it after the other reviewer's removal.
    const { api, d, confirm } = await toRemoveForm(decision, [withAdjustment(), review([line(1)])]);
    expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toBeInTheDocument();
    const reads = api.getReview.mock.calls.length;
    fireEvent.change(removeNote(confirm), { target: { value: 'Entered twice' } });
    submitRemove(confirm);
    await waitFor(() => expect(within(d).getByTestId('remove-notice')).toHaveTextContent('This adjustment was already removed by another reviewer. (ebilling.adjustment.id_reused)'));
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    await waitFor(() => expect(within(d).queryByTestId(`adjustment-${lineId(90)}`)).not.toBeInTheDocument());
    // Nothing is left to remove: the removal form is closed, not left open on a line that no longer exists.
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Remove adjustment', hidden: true })).not.toBeInTheDocument());
  });

  it('(must-not-fire) a 409 id_reused whose re-read still lists the adjustment keeps the form open with the sentence and the note', async () => {
    const decision = mockDecisionApi();
    decision.removeAdjustment.mockResolvedValue(
      failed(problem(409, 'ebilling.adjustment.id_reused', { detail: 'A removal with another note was already recorded.' }))
    );
    const { api, d, confirm } = await toRemoveForm(decision, [withAdjustment(), withAdjustment()]);
    const reads = api.getReview.mock.calls.length;
    fireEvent.change(removeNote(confirm), { target: { value: 'Entered twice' } });
    submitRemove(confirm);
    expect(await within(confirm).findByTestId('remove-error')).toHaveTextContent('A removal with another note was already recorded. (ebilling.adjustment.id_reused)');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toBeInTheDocument();
    expect(removeNote(confirm).value).toBe('Entered twice');
    expect(within(d).queryByTestId('remove-notice')).not.toBeInTheDocument();
  });

  it('(must-not-fire) another 409 on a removal does not re-read the list', async () => {
    const decision = mockDecisionApi();
    decision.removeAdjustment.mockResolvedValue(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const { api, confirm } = await toRemoveForm(decision, [withAdjustment(), review([line(1)])]);
    const reads = api.getReview.mock.calls.length;
    submitRemove(confirm);
    expect(await within(confirm).findByTestId('remove-error')).toHaveTextContent('(ebilling.invoice.not_in_review)');
    expect(api.getReview.mock.calls.length).toBe(reads);
  });
});
