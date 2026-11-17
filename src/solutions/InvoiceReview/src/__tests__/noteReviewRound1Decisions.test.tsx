/**
 * Task 095f-2ui, review round 1 (decisions): a request that came back unsure (503 saved in part, or no answer at all) is locked as it was sent
 * and kept at page level, so reopening the wizard or the blocked-submission form repeats the SAME request; the adjustment add and removal
 * forms read the list back instead of guessing; a refused note shows one message, not two.
 *
 * Through the real page, wizard shell and forms with mocked BFF clients (the harness of noteBoxesDecisions.test.tsx).
 */

import '@testing-library/jest-dom';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import { EMPTY_FORM, adjustmentFormDirty } from '../components/decision/wizardContext';
import type { InvoiceReview } from '../services/invoiceReviewApi';
import { INVOICE_ID, approval, failed, heldReview, line, lineId, mockApi, mockDecisionApi, problem, review, type MockDecisionApi } from './testKit';

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
const incomplete = (detail = 'The decision was saved but the invoice did not close.') =>
  failed(problem(503, 'ebilling.invoice.decision_incomplete', { detail }));
const noAnswer = () => failed(new Error('Failed to fetch'));

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// The decision wizard
// ─────────────────────────────────────────────────────────────────────────────

describe('the decision wizard: a refused note and an unsure send', () => {
  const WIZARD = { name: 'Decide invoice', hidden: true } as const;
  async function openWizard(): Promise<HTMLElement> {
    fireEvent.click(await screen.findByTestId('decide-invoice'));
    return screen.findByRole('dialog', WIZARD);
  }
  const next = (d: HTMLElement) => within(d).getByRole('button', { name: 'Next', hidden: true });
  const back = (d: HTMLElement) => within(d).getByRole('button', { name: 'Back', hidden: true });
  const cancel = (d: HTMLElement) => within(d).getByRole('button', { name: 'Cancel', hidden: true });
  const radio = (d: HTMLElement, name: RegExp) => within(d).getByRole('radio', { name, hidden: true });
  const finishBtn = (d: HTMLElement, name: RegExp) => within(d).getByRole('button', { name, hidden: true });
  const noteBox = (d: HTMLElement) => within(d).getByLabelText('Note for the decision') as HTMLTextAreaElement;
  const reasonBox = (d: HTMLElement) => within(d).getByLabelText('Reason for the decision') as HTMLTextAreaElement;

  async function toConfirm(d: HTMLElement, which: RegExp, reason: string) {
    fireEvent.click(radio(d, which));
    fireEvent.click(next(d));
    fireEvent.change(reasonBox(d), { target: { value: reason } });
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-decision');
  }
  async function closeWizard(d: HTMLElement) {
    fireEvent.click(cancel(d));
    await waitFor(() => expect(screen.queryByRole('dialog', WIZARD)).not.toBeInTheDocument());
  }

  it('a refused note is ONE message: under the Note box, with no second alert from the wizard', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValue(failed(problem(400, 'ebilling.note_too_long', { detail: 'The note may be at most 2000 characters.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'a note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    const field = within(d).getByTestId('decision-note-field');
    await waitFor(() => expect(within(field).getByText('The note may be at most 2000 characters.')).toBeInTheDocument());
    const alerts = within(d).queryAllByRole('alert', { hidden: true });
    expect(alerts).toHaveLength(1);
    expect(field.contains(alerts[0])).toBe(true);
    expect(within(d).queryByText(/The note was not accepted/)).not.toBeInTheDocument();
  });

  it('(must-not-fire) another refusal is still the wizard\'s own error bar', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValue(failed(problem(403, 'ebilling.decision.not_authorized', { detail: 'You may not reject this invoice.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.click(finishBtn(d, /Reject & record/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('You may not reject this invoice.');
  });

  it('after a 503 saved in part the REASON is locked with the note, and the repeat is the exact same request', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    expect(noteBox(d)).toHaveAttribute('readonly');
    fireEvent.click(back(d));
    expect(reasonBox(d)).toHaveAttribute('readonly');
    expect(reasonBox(d).value).toBe('Duplicate of an earlier invoice');
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
  });

  it('a send with no answer at all locks the same way', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(noAnswer());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    const reads = decision.getApproval.mock.calls.length;
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    expect(noteBox(d)).toHaveAttribute('readonly');
    // The page and the approval were read again, to see whether the decision was recorded after all.
    await waitFor(() => expect(decision.getApproval.mock.calls.length).toBeGreaterThan(reads));
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
  });

  it('(must-not-fire) a 503 that saved nothing does not lock the reason or the note', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.unavailable', { detail: 'The service is busy. Try again.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.click(finishBtn(d, /Reject & record/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('The service is busy. Try again.');
    expect(within(d).queryByTestId('decision-note-locked')).not.toBeInTheDocument();
    expect(noteBox(d)).not.toHaveAttribute('readonly');
  });

  it('closing and reopening the wizard keeps the locked request: prefilled, locked, and the repeat sends the same reason and note', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    await closeWizard(d);

    const again = await openWizard();
    expect(radio(again, /^Reject/)).toBeChecked();
    fireEvent.click(next(again));
    expect(reasonBox(again).value).toBe('Duplicate of an earlier invoice');
    expect(reasonBox(again)).toHaveAttribute('readonly');
    fireEvent.click(next(again));
    await within(again).findByTestId('confirm-decision');
    expect(noteBox(again).value).toBe('The first note');
    expect(noteBox(again)).toHaveAttribute('readonly');
    expect(within(again).getByTestId('decision-note-locked')).toBeInTheDocument();
    fireEvent.click(finishBtn(again, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
  });

  it('(must-not-fire) once the repeat finishes, the lock is gone: the next opening is an empty form', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('wizard-done');
    // The invoice stays Pending in this mock, so the wizard can open for a new decision: nothing of the old request is offered.
    fireEvent.click(within(d).getByTestId('wizard-done'));
    await waitFor(() => expect(screen.queryByRole('dialog', WIZARD)).not.toBeInTheDocument());
    const again = await openWizard();
    fireEvent.click(radio(again, /^Reject/));
    fireEvent.click(next(again));
    expect(reasonBox(again).value).toBe('');
    expect(reasonBox(again)).not.toHaveAttribute('readonly');
  });

  it('going Back and choosing another outcome is a new request: the form is editable while that outcome is selected', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    // Back to the Decision step and choose Return instead.
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, /^Return/));
    fireEvent.click(next(d));
    expect(reasonBox(d)).not.toHaveAttribute('readonly');
    fireEvent.change(reasonBox(d), { target: { value: 'Please correct the rates' } });
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-decision');
    expect(within(d).queryByTestId('decision-note-locked')).not.toBeInTheDocument();
    expect(noteBox(d)).not.toHaveAttribute('readonly');
    fireEvent.change(noteBox(d), { target: { value: 'A new note' } });
    fireEvent.click(finishBtn(d, /Return to firm & record/));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(1));
    expect(decision.returnToFirm.mock.calls[0]).toEqual([INVOICE_ID, 'Please correct the rates', 'A new note']);
    expect(decision.reject).toHaveBeenCalledTimes(1);
  });

  it('(round 2) the lock outlives a change of outcome: choosing another outcome and closing without sending, the reopened wizard is still the locked Reject', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, /^Return/));
    await closeWizard(d);
    const again = await openWizard();
    expect(radio(again, /^Reject/)).toBeChecked();
    fireEvent.click(next(again));
    expect(reasonBox(again).value).toBe('Duplicate of an earlier invoice');
    expect(reasonBox(again)).toHaveAttribute('readonly');
  });

  it('(round 2) A to B to A: coming back to the locked outcome restores what was sent and locks the form, and the repeat is the same request', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirm(d, /^Reject/, 'Duplicate of an earlier invoice');
    fireEvent.change(noteBox(d), { target: { value: 'The first note' } });
    fireEvent.click(finishBtn(d, /Reject & record/));
    await within(d).findByTestId('decision-note-locked');
    // To Return: editable, the reviewer types something else.
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, /^Return/));
    fireEvent.click(next(d));
    expect(reasonBox(d)).not.toHaveAttribute('readonly');
    fireEvent.change(reasonBox(d), { target: { value: 'Something else entirely' } });
    // And back to Reject: the locked reason and note are restored, and read-only again.
    fireEvent.click(back(d));
    fireEvent.click(radio(d, /^Reject/));
    fireEvent.click(next(d));
    expect(reasonBox(d)).toHaveAttribute('readonly');
    expect(reasonBox(d).value).toBe('Duplicate of an earlier invoice');
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-decision');
    expect(noteBox(d)).toHaveAttribute('readonly');
    expect(noteBox(d).value).toBe('The first note');
    expect(within(d).getByTestId('decision-note-locked')).toBeInTheDocument();
    fireEvent.click(finishBtn(d, /Reject & record/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.returnToFirm).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// The blocked submission
// ─────────────────────────────────────────────────────────────────────────────

describe('the blocked submission: an unsure Override or Return stays locked across reopening', () => {
  const held = () => approval({ held: true, canApprove: false });
  const dialogOf = (name: string) => screen.findByRole('dialog', { name, hidden: true });
  const noteOf = (dialog: HTMLElement) => within(dialog).getByLabelText('Note') as HTMLTextAreaElement;
  const reasonOf = (dialog: HTMLElement) => within(dialog).getByLabelText('Reason') as HTMLTextAreaElement;
  const submit = (dialog: HTMLElement, name: string) => fireEvent.click(within(dialog).getByRole('button', { name, hidden: true }));
  const cancel = (dialog: HTMLElement) => fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel', hidden: true }));

  async function sendOverride(decision: MockDecisionApi) {
    renderDecisions([heldReview(), heldReview()], decision);
    fireEvent.click(await screen.findByTestId('override-block'));
    const dialog = await dialogOf('Override the block');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Known resubmission' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'The first note' } });
    submit(dialog, 'Override');
    return dialog;
  }

  it.each([
    ['saved in part (503)', () => failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'The decision was recorded but not every Signal closed.' }))],
    ['no answer at all', () => noAnswer()],
  ])('a send %s locks the reason and the note, and reopening shows them locked, so the repeat is the same request', async (_, fail) => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(fail());
    const dialog = await sendOverride(decision);
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).toHaveAttribute('readonly');
    expect(noteOf(dialog)).toHaveAttribute('readonly');
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());

    fireEvent.click(await screen.findByTestId('override-block'));
    const again = await dialogOf('Override the block');
    expect(reasonOf(again).value).toBe('Known resubmission');
    expect(noteOf(again).value).toBe('The first note');
    expect(reasonOf(again)).toHaveAttribute('readonly');
    expect(noteOf(again)).toHaveAttribute('readonly');
    expect(within(again).getByTestId('blocked-note-locked')).toBeInTheDocument();
    submit(again, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    expect(decision.overrideBlocked.mock.calls[1]).toEqual(decision.overrideBlocked.mock.calls[0]);
  });

  it('(round 2) the other action opens editable but warns, cannot be submitted while the first is unfinished, and the lock is kept', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })));
    const dialog = await sendOverride(decision);
    await within(dialog).findByTestId('blocked-error');
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const other = await dialogOf('Return to firm');
    // Nothing of the Override is prefilled (it is a different request), and the form can be typed in.
    expect(reasonOf(other).value).toBe('');
    expect(noteOf(other).value).toBe('');
    expect(reasonOf(other)).not.toHaveAttribute('readonly');
    expect(within(other).queryByTestId('blocked-note-locked')).not.toBeInTheDocument();
    // But it says the Override is unfinished, and Return cannot be sent.
    expect(within(other).getByTestId('blocked-other-lock')).toHaveTextContent('An Override may have been saved in part. Submit it again to finish it before returning.');
    fireEvent.change(reasonOf(other), { target: { value: 'Please resubmit' } });
    expect(within(other).getByRole('button', { name: 'Return', hidden: true })).toBeDisabled();
    fireEvent.click(within(other).getByRole('button', { name: 'Return', hidden: true }));
    expect(decision.returnBlocked).not.toHaveBeenCalled();
    cancel(other);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Return to firm', hidden: true })).not.toBeInTheDocument());
    // Going back to Override finds the lock kept: the locked reason and note, and the repeat is the same request.
    fireEvent.click(await screen.findByTestId('override-block'));
    const back = await dialogOf('Override the block');
    expect(reasonOf(back).value).toBe('Known resubmission');
    expect(noteOf(back).value).toBe('The first note');
    expect(reasonOf(back)).toHaveAttribute('readonly');
    expect(within(back).queryByTestId('blocked-other-lock')).not.toBeInTheDocument();
    submit(back, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    expect(decision.overrideBlocked.mock.calls[1]).toEqual(decision.overrideBlocked.mock.calls[0]);
  });

  it('(round 2) the same warning, mirrored, for a Return that came back unsure and an Override opened after it', async () => {
    const decision = mockDecisionApi(held());
    decision.returnBlocked.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })));
    renderDecisions([heldReview(), heldReview()], decision);
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const first = await dialogOf('Return to firm');
    fireEvent.change(reasonOf(first), { target: { value: 'Please resubmit' } });
    submit(first, 'Return');
    await within(first).findByTestId('blocked-error');
    cancel(first);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Return to firm', hidden: true })).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('override-block'));
    const other = await dialogOf('Override the block');
    expect(within(other).getByTestId('blocked-other-lock')).toHaveTextContent('A Return may have been saved in part. Submit it again to finish it before overriding.');
    fireEvent.change(reasonOf(other), { target: { value: 'Known resubmission' } });
    expect(within(other).getByRole('button', { name: 'Override', hidden: true })).toBeDisabled();
  });

  it('(must-not-fire) with no unfinished decision the other action opens with no warning and can be sent', async () => {
    const decision = mockDecisionApi(held());
    renderDecisions([heldReview(), heldReview()], decision);
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const other = await dialogOf('Return to firm');
    expect(within(other).queryByTestId('blocked-other-lock')).not.toBeInTheDocument();
    fireEvent.change(reasonOf(other), { target: { value: 'Please resubmit' } });
    expect(within(other).getByRole('button', { name: 'Return', hidden: true })).toBeEnabled();
  });

  // Review round 3: the way out of the other-action guard. A repeat of the locked action answered with a DEFINITE refusal will never finish, so
  // the lock is released, the refusal shown and the page re-read; otherwise the reviewer is stuck until a browser reload.
  it.each([
    ['409 decision_conflict', () => failed(problem(409, 'ebilling.blocked.decision_conflict', { detail: 'A different decision is already recorded.' }))],
    ['409 not_blocked', () => failed(problem(409, 'ebilling.blocked.not_blocked', { detail: 'This submission is not blocked.' }))],
    ['409 record_refused', () => failed(problem(409, 'ebilling.blocked.record_refused', { detail: 'The record was refused.' }))],
    ['403', () => failed(problem(403, 'ebilling.blocked.not_authorized', { detail: 'You may not override.' }))],
  ])('(round 3) a repeat of the locked Override answered %s clears the lock, shows the refusal, re-reads the page, and Return can then be sent', async (_, refusal) => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' }))).mockResolvedValueOnce(refusal());
    const dialog = await sendOverride(decision);
    await waitFor(() => expect(reasonOf(dialog)).toHaveAttribute('readonly'));
    const approvals = decision.getApproval.mock.calls.length;
    const blockedReads = decision.getBlocked.mock.calls.length;
    submit(dialog, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(reasonOf(dialog)).not.toHaveAttribute('readonly'));
    expect(noteOf(dialog)).not.toHaveAttribute('readonly');
    expect(within(dialog).getByTestId('blocked-error')).toBeInTheDocument();
    await waitFor(() => expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals));
    expect(decision.getBlocked.mock.calls.length).toBeGreaterThan(blockedReads);
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());
    // Return no longer warns and can be sent.
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const other = await dialogOf('Return to firm');
    expect(within(other).queryByTestId('blocked-other-lock')).not.toBeInTheDocument();
    fireEvent.change(reasonOf(other), { target: { value: 'Please resubmit' } });
    submit(other, 'Return');
    await waitFor(() => expect(decision.returnBlocked).toHaveBeenCalledTimes(1));
  });

  it.each([
    ['a 503 saved in part', () => failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' }))],
    ['a 409 concurrent_change', () => failed(problem(409, 'ebilling.blocked.concurrent_change', { detail: 'The invoice changed.' }))],
    ['a 409 in_progress (transient)', () => failed(problem(409, 'ebilling.blocked.in_progress', { detail: 'Another request is running.' }))],
    ['a 409 still_processing (transient)', () => failed(problem(409, 'ebilling.blocked.still_processing', { detail: 'Still processing.' }))],
    ['no answer at all', () => noAnswer()],
    ['a 401 (sign-in expired)', () => failed(problem(401, null, { detail: 'Sign in again.' }))],
    ['a 408 (timed out)', () => failed(problem(408, null, { detail: 'The request timed out.' }))],
    ['a 429 (rate limited)', () => failed(problem(429, null, { detail: 'Too many requests.' }))],
  ])('(must-not-fire) a repeat of the locked Override answered %s keeps the lock, and Return still warns', async (_, again) => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' }))).mockResolvedValueOnce(again());
    const dialog = await sendOverride(decision);
    await waitFor(() => expect(reasonOf(dialog)).toHaveAttribute('readonly'));
    submit(dialog, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).toHaveAttribute('readonly');
    expect(noteOf(dialog)).toHaveAttribute('readonly');
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const other = await dialogOf('Return to firm');
    expect(within(other).getByTestId('blocked-other-lock')).toBeInTheDocument();
    expect(within(other).getByRole('button', { name: 'Return', hidden: true })).toBeDisabled();
  });

  it('(must-not-fire) a FIRST send answered with a definite refusal is just an error: nothing to clear, no extra re-read', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.already_returned', { detail: 'Already returned.' })));
    const dialog = await sendOverride(decision);
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).not.toHaveAttribute('readonly');
    expect(decision.getBlocked).toHaveBeenCalledTimes(1);
  });

  it('(round 2) a 409 concurrent_change may come after the record: it locks like a 503 (conservative), and the repeat finishes it with the same text', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.concurrent_change', { detail: 'The invoice changed.' })));
    const dialog = await sendOverride(decision);
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).toHaveAttribute('readonly');
    expect(noteOf(dialog)).toHaveAttribute('readonly');
    // Closing and reopening keeps it locked, and the other action warns.
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('override-block'));
    const again = await dialogOf('Override the block');
    expect(reasonOf(again).value).toBe('Known resubmission');
    expect(reasonOf(again)).toHaveAttribute('readonly');
    submit(again, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    expect(decision.overrideBlocked.mock.calls[1]).toEqual(decision.overrideBlocked.mock.calls[0]);
    // The repeat finished it (the mock's default answer): the lock goes with it.
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('override-block'));
    const fresh = await dialogOf('Override the block');
    expect(reasonOf(fresh).value).toBe('');
    expect(noteOf(fresh).value).toBe('');
    expect(reasonOf(fresh)).not.toHaveAttribute('readonly');
  });

  it('(must-not-fire) a 409 answered before any record does not lock, and a 503 after it does; once the repeat finishes the next opening is empty', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.already_returned', { detail: 'Already returned.' })))
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })));
    const dialog = await sendOverride(decision);
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).not.toHaveAttribute('readonly');
    expect(noteOf(dialog)).not.toHaveAttribute('readonly');
    submit(dialog, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(noteOf(dialog)).toHaveAttribute('readonly'));
    // The third send finishes it (the mock's default answer): the lock goes with it.
    submit(dialog, 'Override');
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('override-block'));
    const fresh = await dialogOf('Override the block');
    expect(reasonOf(fresh).value).toBe('');
    expect(noteOf(fresh).value).toBe('');
    expect(reasonOf(fresh)).not.toHaveAttribute('readonly');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// The invoice adjustment: add and remove
// ─────────────────────────────────────────────────────────────────────────────

describe('the invoice adjustment after an unsure send or a stale page', () => {
  const adj = () => line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy' });
  const withAdjustment = () => review([line(1), adj()], { totals: { billed: 5000, fees: 5000, expenses: 0, lineAdjustments: 0, invoiceAdjustments: -250, approved: 4750 } });
  const WIZARD = { name: 'Decide invoice', hidden: true } as const;

  async function openWizard(): Promise<HTMLElement> {
    fireEvent.click(await screen.findByTestId('decide-invoice'));
    return screen.findByRole('dialog', WIZARD);
  }
  const next = (d: HTMLElement) => within(d).getByRole('button', { name: 'Next', hidden: true });
  const back = (d: HTMLElement) => within(d).getByRole('button', { name: 'Back', hidden: true });
  const skip = (d: HTMLElement) => within(d).getByRole('button', { name: 'Skip', hidden: true });
  const addNote = (d: HTMLElement) => within(d).getByLabelText('Adjustment note') as HTMLTextAreaElement;

  async function toAdjust(decision: MockDecisionApi, reviews: InvoiceReview | InvoiceReview[] = clean()) {
    const { api } = renderDecisions(reviews, decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.change(await within(d).findByLabelText('Adjustment value'), { target: { value: '5' } });
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Courtesy' } });
    fireEvent.change(addNote(d), { target: { value: 'Partner agreed' } });
    return { api, d };
  }
  const addId = (decision: MockDecisionApi, i: number) => (decision.addAdjustment.mock.calls[i][1] as { adjustmentId: string }).adjustmentId;

  it('an add with no answer locks the form like a 503 does, and Repeat sends the same adjustment id', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(noAnswer());
    const { d } = await toAdjust(decision);
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat'));
    expect(within(d).getByLabelText('Adjustment value')).toBeDisabled();
    expect(within(d).getByLabelText('Adjustment reason')).toBeDisabled();
    expect(addNote(d)).toBeDisabled();
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    expect(decision.addAdjustment.mock.calls[1][1]).toEqual(decision.addAdjustment.mock.calls[0][1]);
  });

  it('(must-not-fire) a 400 or a plain 409 does not lock the add form', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const { d } = await toAdjust(decision);
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await within(d).findByTestId('adjustment-error');
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Add adjustment');
    expect(within(d).getByLabelText('Adjustment value')).toBeEnabled();
    expect(addNote(d)).toBeEnabled();
  });

  it('an add answered 409 id_reused re-reads the list, shows the sentence and keeps the form open', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(
      failed(problem(409, 'ebilling.adjustment.id_reused', { detail: 'This adjustment id was already used for another adjustment.' }))
    );
    const { api, d } = await toAdjust(decision);
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    expect(await within(d).findByTestId('adjustment-error')).toHaveTextContent('This adjustment id was already used for another adjustment. (ebilling.adjustment.id_reused)');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    expect(within(d).getByLabelText('Adjustment value')).toHaveValue(5);
  });

  it('(must-not-fire) another 409 on an add does not re-read the list', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const { api, d } = await toAdjust(decision);
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await within(d).findByTestId('adjustment-error');
    expect(api.getReview.mock.calls.length).toBe(reads);
  });

  it('Clear on a locked form reads the list back first, then starts a new adjustment with a NEW id', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(noAnswer());
    const { api, d } = await toAdjust(decision, [clean(), withAdjustment()]);
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat'));
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(d).getByTestId('clear-adjustment'));
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    // The re-read shows the adjustment (it was saved after all): the list has it, and the form is empty and unlocked.
    await waitFor(() => expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toBeInTheDocument());
    await waitFor(() => expect(within(d).getByLabelText('Adjustment value')).toHaveValue(null));
    expect(within(d).getByLabelText('Adjustment value')).toBeEnabled();
    expect(within(d).getByTestId('adjustment-cleared')).toBeInTheDocument();
    fireEvent.change(within(d).getByLabelText('Adjustment value'), { target: { value: '7' } });
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Another' } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    expect(addId(decision, 1)).not.toBe(addId(decision, 0));
  });

  it('(must-not-fire) Clear on a form that is not locked does not re-read the list', async () => {
    const decision = mockDecisionApi();
    const { api, d } = await toAdjust(decision);
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(d).getByTestId('clear-adjustment'));
    await waitFor(() => expect(within(d).getByLabelText('Adjustment value')).toHaveValue(null));
    expect(api.getReview.mock.calls.length).toBe(reads);
    expect(within(d).queryByTestId('adjustment-cleared')).not.toBeInTheDocument();
  });

  it('Clear on a locked form whose re-read fails keeps the form locked as it was (the earlier add may be saved)', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(noAnswer());
    const { api, d } = await toAdjust(decision);
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat'));
    api.getReview.mockResolvedValueOnce(failed(problem(503, null)));
    fireEvent.click(within(d).getByTestId('clear-adjustment'));
    await waitFor(() => expect(within(d).getByTestId('adjustment-error')).toHaveTextContent('The earlier add may have been saved'));
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat');
    expect(within(d).getByLabelText('Adjustment value')).toHaveValue(5);
    expect(within(d).getByLabelText('Adjustment value')).toBeDisabled();
  });

  it('leaving the step with Skip and coming Back keeps the form locked and the same id', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(noAnswer());
    const { d } = await toAdjust(decision);
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat'));
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(back(d));
    const repeat = await within(d).findByTestId('add-adjustment');
    expect(repeat).toHaveTextContent('Repeat');
    expect(within(d).getByLabelText('Adjustment value')).toBeDisabled();
    fireEvent.click(repeat);
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    expect(decision.addAdjustment.mock.calls[1][1]).toEqual(decision.addAdjustment.mock.calls[0][1]);
  });

  it('(round 2) an add that SUCCEEDS after the reviewer left the step still re-reads and resets the form: Back shows an empty form, not the saved add', async () => {
    const decision = mockDecisionApi();
    let release!: (v: unknown) => void;
    decision.addAdjustment.mockReturnValueOnce(new Promise(resolve => { release = resolve; }) as never);
    const { api, d } = await toAdjust(decision, [clean(), withAdjustment()]);
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(1));
    // The step is left while the add is still out.
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    release({
      ok: true,
      value: { adjustment: { lineId: lineId(90), adjustmentId: 'a1', method: 'percent', value: 5, amount: -250, reason: 'Courtesy', ruleId: null, reviewedOn: '2026-11-03T10:00:00Z' }, removedLineId: null, totals: null, alreadyApplied: false },
    });
    // The page reads the server's state even though the step is gone.
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    fireEvent.click(back(d));
    const value = await within(d).findByLabelText('Adjustment value');
    // The add is saved: the form must not still hold it (a second send would file a second adjustment).
    await waitFor(() => expect(value).toHaveValue(null));
    expect(within(d).getByLabelText('Adjustment reason')).toHaveValue('');
    expect(addNote(d).value).toBe('');
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Add adjustment');
    expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toBeInTheDocument();
  });

  it('closing and reopening the wizard keeps the unsure add: prefilled, locked, and Repeat sends the same id and note', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(noAnswer());
    const { d } = await toAdjust(decision);
    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat'));
    fireEvent.click(within(d).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog', WIZARD)).not.toBeInTheDocument());

    const again = await openWizard();
    fireEvent.click(next(again));
    const value = await within(again).findByLabelText('Adjustment value');
    expect(value).toHaveValue(5);
    expect(value).toBeDisabled();
    expect(within(again).getByLabelText('Adjustment reason')).toHaveValue('Courtesy');
    expect(addNote(again).value).toBe('Partner agreed');
    expect(within(again).getByTestId('add-adjustment')).toHaveTextContent('Repeat');
    fireEvent.click(within(again).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    expect(decision.addAdjustment.mock.calls[1][1]).toEqual(decision.addAdjustment.mock.calls[0][1]);
  });

  it('a note typed alone counts as unsaved text in the adjustment form: Next is held until it is added or cleared', async () => {
    const { d } = await (async () => {
      renderDecisions(clean(), mockDecisionApi());
      const wizard = await openWizard();
      fireEvent.click(next(wizard));
      await within(wizard).findByLabelText('Adjustment value');
      return { d: wizard };
    })();
    expect(next(d)).toBeEnabled();
    fireEvent.change(addNote(d), { target: { value: 'Only a note' } });
    expect(next(d)).toBeDisabled();
    fireEvent.change(addNote(d), { target: { value: '   ' } });
    expect(next(d)).toBeEnabled();
  });

  it('adjustmentFormDirty counts a note, and not a blank one', () => {
    expect(adjustmentFormDirty({ ...EMPTY_FORM, adjNote: 'a note' })).toBe(true);
    expect(adjustmentFormDirty({ ...EMPTY_FORM, adjNote: '   ' })).toBe(false);
    expect(adjustmentFormDirty({ ...EMPTY_FORM })).toBe(false);
    expect(adjustmentFormDirty({ ...EMPTY_FORM, adjValue: '5' })).toBe(true);
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

  it('a removal saved but not read back locks the removal note, and Remove again sends the same note', async () => {
    const decision = mockDecisionApi();
    const { api, confirm } = await toRemoveForm(decision, [withAdjustment(), withAdjustment()]);
    fireEvent.change(removeNote(confirm), { target: { value: 'Entered twice' } });
    api.getReview.mockResolvedValueOnce(failed(problem(503, null)));
    submitRemove(confirm);
    expect(await within(confirm).findByTestId('remove-error')).toHaveTextContent('The adjustment was removed.');
    expect(removeNote(confirm)).toHaveAttribute('readonly');
    expect(removeNote(confirm).value).toBe('Entered twice');
    submitRemove(confirm);
    await waitFor(() => expect(decision.removeAdjustment).toHaveBeenCalledTimes(2));
    expect(decision.removeAdjustment.mock.calls[1]).toEqual(decision.removeAdjustment.mock.calls[0]);
  });

  it('(must-not-fire) a removal that is read back fine does not lock the note of the next removal', async () => {
    const decision = mockDecisionApi();
    decision.removeAdjustment.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const { confirm } = await toRemoveForm(decision, [withAdjustment(), withAdjustment()]);
    submitRemove(confirm);
    await within(confirm).findByTestId('remove-error');
    expect(removeNote(confirm)).not.toHaveAttribute('readonly');
  });
});
