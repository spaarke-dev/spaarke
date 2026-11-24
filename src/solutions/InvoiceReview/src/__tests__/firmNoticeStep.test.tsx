/**
 * Task 132ui: the Notify step of the invoice decision wizard, one or more tests per acceptance criterion (AC1 to AC9; AC10 is the rest of the
 * package passing). Through the real page, wizard shell and step with mocked BFF clients (the harness of noteBoxesDecisions.test.tsx).
 *
 * What is asserted is what the client is asked to send and what the reviewer sees: the draft asked for once per kind, the notice carried only
 * when it was sent on purpose (never empty strings), the server's refusal shown under the notice fields, a failed draft that never blocks the
 * decision, the limits as hints, and the unsure-outcome lock that carries the notice and the send choice.
 */

import '@testing-library/jest-dom';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import type { FirmNoticeDraft } from '../services/decisionApi';
import type { ApiResult, InvoiceReview } from '../services/invoiceReviewApi';
import {
  INVOICE_ID,
  approval,
  decidedReview,
  decisionRecord,
  failed,
  firmNoticeDraft,
  line,
  listSourceFiles,
  mockApi,
  mockDecisionApi,
  ok,
  problem,
  review,
  strandedApproval,
  type MockDecisionApi,
} from './testKit';
import * as fs from 'fs';

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
const WIZARD = { name: 'Decide invoice', hidden: true } as const;

async function openWizard(): Promise<HTMLElement> {
  fireEvent.click(await screen.findByTestId('decide-invoice'));
  return screen.findByRole('dialog', WIZARD);
}
async function closeWizard(d: HTMLElement) {
  fireEvent.click(within(d).getByRole('button', { name: 'Cancel', hidden: true }));
  await waitFor(() => expect(screen.queryByRole('dialog', WIZARD)).not.toBeInTheDocument());
}

const btn = (d: HTMLElement, name: string | RegExp) => within(d).getByRole('button', { name, hidden: true });
const next = (d: HTMLElement) => btn(d, 'Next');
const back = (d: HTMLElement) => btn(d, 'Back');
const skip = (d: HTMLElement) => btn(d, 'Skip');
const radio = (d: HTMLElement, name: RegExp) => within(d).getByRole('radio', { name, hidden: true });
const sendSwitch = (d: HTMLElement) => within(d).getByRole('switch', { name: 'Send this notice to the firm', hidden: true });
const subjectBox = (d: HTMLElement) => within(d).getByLabelText('Notice subject') as HTMLInputElement;
const proseBox = (d: HTMLElement) => within(d).getByLabelText('Notice message') as HTMLTextAreaElement;
const reasonBox = (d: HTMLElement) => within(d).getByLabelText('Reason for the decision') as HTMLTextAreaElement;
const type = (el: HTMLElement, value: string) => fireEvent.change(el, { target: { value } });

const REJECT = /^Reject/;
const RETURN = /^Return to firm/;
const FINISH_REJECT = /^Reject & record$/;
const FINISH_RETURN = /^Return to firm & record$/;

/** Choose an outcome, give a reason, and arrive on the Notify step (draft not awaited). */
async function toNotify(d: HTMLElement, which: RegExp, reason = 'Not payable as billed') {
  fireEvent.click(radio(d, which));
  fireEvent.click(next(d));
  type(reasonBox(d), reason);
  fireEvent.click(next(d));
  await within(d).findByTestId('notify-step');
}
/** Arrive on the Notify step and wait for the draft to load into the fields. */
async function toLoadedNotify(d: HTMLElement, which: RegExp, reason?: string) {
  await toNotify(d, which, reason);
  await within(d).findByTestId('notice-subject');
}
async function toConfirm(d: HTMLElement) {
  fireEvent.click(next(d));
  await within(d).findByTestId('confirm-decision');
}

const incomplete = () => failed(problem(503, 'ebilling.invoice.decision_incomplete', { detail: 'The decision was saved but the invoice did not close.' }));

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// AC1 and AC2: where the step shows, and when the draft is asked for
// ─────────────────────────────────────────────────────────────────────────────

describe('AC1: where the Notify step shows', () => {
  it('Reject and Return each show it after Reason; Approve does not (no draft is asked for on Approve)', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    expect(radio(d, /^Approve/)).toBeChecked();
    expect(within(d).queryByText('Notify firm')).not.toBeInTheDocument();
    // Through the whole Approve path: no Notify step, no draft.
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    await within(d).findByTestId('confirm-decision');
    expect(within(d).queryByTestId('notify-step')).not.toBeInTheDocument();
    expect(within(d).queryByTestId('confirm-notice')).not.toBeInTheDocument();
    expect(decision.getFirmNoticeDraft).not.toHaveBeenCalled();
  });

  it('a decided (read-only) wizard never shows it, and asks for no draft', async () => {
    const rej = review([line(1)]);
    rej.header.status = 'Rejected';
    rej.decisionHistory = [decisionRecord({ recordId: 'rec-r', number: 'DR-0050', actionCode: 'reject-invoice', subject: 'invoice', lineId: null, reason: 'Duplicate of an earlier invoice' })];
    const decision = mockDecisionApi(approval({ status: 'Rejected', canApprove: false, decidedBy: 'Pat Approver' }));
    renderDecisions(rej, decision);
    fireEvent.click(await screen.findByTestId('view-decision'));
    const d = await screen.findByRole('dialog', { name: 'Invoice decision', hidden: true });
    expect(within(d).queryByText('Notify firm')).not.toBeInTheDocument();
    fireEvent.click(next(d)); // Decision -> Reason
    fireEvent.click(next(d)); // Reason -> Recorded decision (no Notify between)
    await within(d).findByTestId('confirm-decision');
    expect(within(d).queryByTestId('notify-step')).not.toBeInTheDocument();
    expect(within(d).queryByTestId('confirm-notice')).not.toBeInTheDocument();
    expect(decision.getFirmNoticeDraft).not.toHaveBeenCalled();
  });
});

describe('AC2: the draft is asked for once per kind, with the kind of the outcome', () => {
  it('asks only when the step is first shown, once per kind, and keeps each kind\'s edits apart', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();

    // Not before the step is shown: choosing Reject and writing the reason asks for nothing.
    fireEvent.click(radio(d, REJECT));
    fireEvent.click(next(d));
    type(reasonBox(d), 'Not payable as billed');
    expect(decision.getFirmNoticeDraft).not.toHaveBeenCalled();
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-subject');
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);
    expect(decision.getFirmNoticeDraft).toHaveBeenLastCalledWith(INVOICE_ID, 'InvoiceRejected');
    type(subjectBox(d), 'Reject subject edited');
    type(proseBox(d), 'Reject message edited');

    // Leave the step and come back: no second request, the edit is still there.
    fireEvent.click(back(d));
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-subject');
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);
    expect(subjectBox(d).value).toBe('Reject subject edited');

    // Switch the decision to Return: its own draft, asked for once, with its own kind.
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, RETURN));
    fireEvent.click(next(d));
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-subject');
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(2);
    expect(decision.getFirmNoticeDraft).toHaveBeenLastCalledWith(INVOICE_ID, 'ReturnedToFirm');
    expect(subjectBox(d).value).toBe(firmNoticeDraft({ kind: 'ReturnedToFirm' }).subject);
    type(subjectBox(d), 'Return subject edited');

    // And back to Reject, then Return again: each kind kept its own edit, and nothing was asked for again.
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, REJECT));
    fireEvent.click(next(d));
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-subject');
    expect(subjectBox(d).value).toBe('Reject subject edited');
    expect(proseBox(d).value).toBe('Reject message edited');
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, RETURN));
    fireEvent.click(next(d));
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-subject');
    expect(subjectBox(d).value).toBe('Return subject edited');
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(2);
  });

  it('a new opening of the wizard asks again (once per opened wizard)', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    await closeWizard(d);
    const again = await openWizard();
    await toLoadedNotify(again, REJECT);
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(2);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// What the reviewer sees on the step
// ─────────────────────────────────────────────────────────────────────────────

describe('the step: draft, source line, counters, restore', () => {
  it('shows the drafted subject and message, the source line, the counters, and that Spaarke adds the table', async () => {
    const decision = mockDecisionApi();
    let landDraft!: (r: ApiResult<FirmNoticeDraft>) => void;
    decision.getFirmNoticeDraft.mockImplementationOnce(() => new Promise<ApiResult<FirmNoticeDraft>>(r => (landDraft = r)));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toNotify(d, REJECT);
    await waitFor(() => expect(landDraft).toBeDefined());
    // The draft is out: the step says so and Next is held until it arrives.
    expect(within(d).getByTestId('notice-loading')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    await act(async () => landDraft(ok(firmNoticeDraft())));
    await within(d).findByTestId('notice-subject');
    const draft = firmNoticeDraft();
    expect(subjectBox(d).value).toBe(draft.subject);
    expect(proseBox(d).value).toBe(draft.prose);
    expect(sendSwitch(d)).toBeChecked();
    expect(within(d).getByTestId('notice-source')).toHaveTextContent('Drafted by AI');
    // Subject counter excludes the tracking number Spaarke appends.
    expect(within(d).getByTestId('notice-subject-count')).toHaveTextContent(`${'Your invoice was rejected'.length} / 120 characters`);
    expect(within(d).getByTestId('notice-prose-count')).toHaveTextContent(`${draft.prose.length} / 1,500 characters`);
    expect(within(d).getByTestId('notice-added-by-spaarke')).toHaveTextContent('findings table, the amounts and the clause quotes are added by Spaarke');
    expect(subjectBox(d)).not.toHaveAttribute('maxlength');
    expect(proseBox(d)).not.toHaveAttribute('maxlength');
    expect(next(d)).toBeEnabled();
  });

  it('says the standard wording was used when the AI was not available (source Template)', async () => {
    const decision = mockDecisionApi();
    decision.getFirmNoticeDraft.mockResolvedValue(ok(firmNoticeDraft({ source: 'Template', actionVersion: null, fallbackReason: 'ai.disabled' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    expect(within(d).getByTestId('notice-source')).toHaveTextContent('standard wording');
    expect(within(d).getByTestId('notice-source')).not.toHaveTextContent('Drafted by AI');
  });

  it('Restore draft puts the drafted subject and message back', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    const draft = firmNoticeDraft();
    type(subjectBox(d), 'Something else');
    type(proseBox(d), 'Another message');
    fireEvent.click(within(d).getByTestId('notice-restore'));
    expect(subjectBox(d).value).toBe(draft.subject);
    expect(proseBox(d).value).toBe(draft.prose);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AC3 and AC4: what the request carries, and what Confirm says
// ─────────────────────────────────────────────────────────────────────────────

describe('AC3 / AC4: the request carries the edited, trimmed notice only when it was sent on purpose', () => {
  it('Reject with sending on carries exactly the edited, trimmed subject and message (and the note), and Confirm says the firm is emailed it', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate of an earlier invoice');
    type(subjectBox(d), '   Your invoice is rejected (INV-2025-78232.1)  ');
    type(proseBox(d), '   We cannot pay this invoice.\nPlease see the findings.   ');
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('The firm will be emailed this notice');
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('Your invoice is rejected (INV-2025-78232.1)');
    fireEvent.change(within(d).getByLabelText('Note for the decision'), { target: { value: 'internal note' } });
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject).toHaveBeenCalledWith(INVOICE_ID, 'Duplicate of an earlier invoice', 'internal note', {
      subject: 'Your invoice is rejected (INV-2025-78232.1)',
      prose: 'We cannot pay this invoice.\nPlease see the findings.',
    });
    expect(await within(d).findByText('Rejected')).toBeInTheDocument();
  });

  it('Return with sending on carries the Return draft (no note: an undefined note, then the notice)', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, RETURN, 'Please correct the rates');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_RETURN));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(1));
    const d3 = firmNoticeDraft({ kind: 'ReturnedToFirm' });
    expect(decision.returnToFirm).toHaveBeenCalledWith(INVOICE_ID, 'Please correct the rates', undefined, { subject: d3.subject, prose: d3.prose });
    expect(await within(d).findByText('Returned to firm')).toBeInTheDocument();
  });

  it('with the send choice turned off the request carries NO notice fields at all, and Confirm says the firm gets no email', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    fireEvent.click(sendSwitch(d));
    expect(sendSwitch(d)).not.toBeChecked();
    expect(within(d).getByTestId('notice-off')).toHaveTextContent('The firm gets no email');
    expect(within(d).queryByTestId('notice-subject')).not.toBeInTheDocument();
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    // Exactly the old call: no notice argument, not even an undefined one.
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Duplicate']);
    expect(decision.reject.mock.calls[0]).toHaveLength(2);
  });

  it('with the step skipped the request carries NO notice fields, and Confirm says the firm gets no email', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, RETURN, 'Please correct the rates');
    // The draft is loaded and sending is on, but the step is skipped.
    expect(sendSwitch(d)).toBeChecked();
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(d, FINISH_RETURN));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(1));
    expect(decision.returnToFirm.mock.calls[0]).toEqual([INVOICE_ID, 'Please correct the rates']);
    expect(decision.returnToFirm.mock.calls[0]).toHaveLength(2);
  });

  it('going Back to the Notify step after a Skip and continuing with Next sends the notice (Skip is not remembered)', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(back(d));
    await within(d).findByTestId('notice-subject');
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('The firm will be emailed this notice');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0][3]).toEqual({ subject: firmNoticeDraft().subject, prose: firmNoticeDraft().prose });
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AC5: the server refuses the notice
// ─────────────────────────────────────────────────────────────────────────────

describe('AC5: a 400 ebilling.notice_refused is shown under the notice fields and the decision is not shown as made', () => {
  const refusal = () =>
    failed(problem(400, 'ebilling.notice_refused', { detail: 'The notice to the firm was not accepted (prose.digit). Nothing was decided. Edit the notice and try again.' }));

  it('shows the refusal under the fields, keeps the wizard open and the page unread, and lets the reviewer edit and finish again', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(refusal());
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    type(proseBox(d), 'We cannot pay invoice 5.');
    await toConfirm(d);
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    fireEvent.click(btn(d, FINISH_REJECT));

    // On the Confirm step the refusal is shown with the way back; no success screen, nothing re-read, no second alert from the shell.
    const onConfirm = await within(d).findByTestId('confirm-notice-refused');
    expect(onConfirm).toHaveTextContent('The notice to the firm was not accepted (prose.digit). Nothing was decided. Edit the notice and try again. (ebilling.notice_refused)');
    expect(within(d).queryByTestId('wizard-done')).not.toBeInTheDocument();
    expect(within(d).queryByRole('alert', { hidden: true })).not.toBeInTheDocument();
    expect(api.getReview.mock.calls.length).toBe(reads);
    expect(decision.getApproval.mock.calls.length).toBe(approvals);
    expect(screen.getByTestId('decide-invoice')).toBeInTheDocument();

    // On the Notify step it is under the notice fields, with the text the reviewer typed still there.
    fireEvent.click(back(d));
    const under = await within(d).findByTestId('notice-refused');
    expect(under).toHaveTextContent('(prose.digit)');
    expect(proseBox(d).value).toBe('We cannot pay invoice 5.');

    // Editing clears the refusal; the corrected notice is sent and the decision is made.
    type(proseBox(d), 'We cannot pay this invoice.');
    expect(within(d).queryByTestId('notice-refused')).not.toBeInTheDocument();
    await toConfirm(d);
    expect(within(d).queryByTestId('confirm-notice-refused')).not.toBeInTheDocument();
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1][3]).toEqual({ subject: firmNoticeDraft().subject, prose: 'We cannot pay this invoice.' });
    expect(await within(d).findByText('Rejected')).toBeInTheDocument();
  });

  it('the reviewer can also turn the notice off after a refusal and finish without it', async () => {
    const decision = mockDecisionApi();
    decision.returnToFirm.mockResolvedValueOnce(refusal());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, RETURN, 'Please correct the rates');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_RETURN));
    await within(d).findByTestId('confirm-notice-refused');
    fireEvent.click(back(d));
    fireEvent.click(sendSwitch(d));
    expect(within(d).queryByTestId('notice-refused')).not.toBeInTheDocument();
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_RETURN));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(2));
    expect(decision.returnToFirm.mock.calls[1]).toEqual([INVOICE_ID, 'Please correct the rates']);
  });

  it('a refused notice is not an unsure outcome: nothing is locked, and the reopened wizard is a fresh one', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(refusal());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('confirm-notice-refused');
    expect(within(d).queryByTestId('decision-note-locked')).not.toBeInTheDocument();
    await closeWizard(d);
    const again = await openWizard();
    expect(radio(again, /^Approve/)).toBeChecked();
  });

  it('(must-not-fire) another 400 is still the wizard\'s own error bar, not the notice refusal', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(problem(400, 'ebilling.reason_required', { detail: 'A reason is required.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('A reason is required. (ebilling.reason_required)');
    expect(within(d).queryByTestId('confirm-notice-refused')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AC6: a draft that fails never blocks the decision
// ─────────────────────────────────────────────────────────────────────────────

describe('AC6: a failed draft shows the message and code with Retry, and the reviewer can still finish without a notice', () => {
  it.each([
    ['409 not applicable', problem(409, 'ebilling.firm-notice.not_applicable', { detail: 'The invoice is not in a state this notice describes.' }), 'The invoice is not in a state this notice describes. (ebilling.firm-notice.not_applicable)'],
    ['503 unavailable', problem(503, 'ebilling.firm-notice.unavailable', { detail: 'The notice could not be drafted right now. Nothing was changed.' }), 'The notice could not be drafted right now. Nothing was changed. (ebilling.firm-notice.unavailable)'],
    ['503 disabled', problem(503, 'ebilling.disabled', {}), 'eBilling is not enabled in this environment. (ebilling.disabled)'],
    // The rate limiter's own answer (RateLimitingModule OnRejected): a detail and no errorCode, so the status stands in for the code.
    ['429 rate limited', problem(429, null, { detail: 'Rate limit exceeded. Please retry after the specified duration.' }), 'Rate limit exceeded. Please retry after the specified duration. (HTTP 429)'],
    ['no answer (network)', new Error('Failed to fetch'), 'The request could not be sent. Check your connection and retry.'],
  ])('%s: the message is shown with Retry, and the decision is sent with no notice', async (_label, err, text) => {
    const decision = mockDecisionApi();
    decision.getFirmNoticeDraft.mockResolvedValueOnce(failed(err));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toNotify(d, REJECT, 'Duplicate');
    expect(await within(d).findByTestId('notice-draft-error-text')).toHaveTextContent(text, { normalizeWhitespace: true });
    expect(within(d).getByTestId('notice-retry')).toBeInTheDocument();
    expect(within(d).queryByTestId('notice-subject')).not.toBeInTheDocument();
    // Never blocks: Next works, Confirm says no email, and the request carries no notice.
    expect(next(d)).toBeEnabled();
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Duplicate']);
  });

  it('Retry asks again, and a draft that then loads is sent on by default', async () => {
    const decision = mockDecisionApi();
    decision.getFirmNoticeDraft.mockResolvedValueOnce(failed(problem(503, 'ebilling.firm-notice.unavailable', { detail: 'Not now.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toNotify(d, REJECT, 'Duplicate');
    await within(d).findByTestId('notice-retry');
    fireEvent.click(within(d).getByTestId('notice-retry'));
    await within(d).findByTestId('notice-subject');
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(2);
    expect(sendSwitch(d)).toBeChecked();
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0][3]).toEqual({ subject: firmNoticeDraft().subject, prose: firmNoticeDraft().prose });
  });

  it('nothing is sent with a half-loaded draft: Next is held while it loads, Skip sends no notice, and a draft that lands later changes nothing', async () => {
    const decision = mockDecisionApi();
    let landDraft!: (r: ApiResult<FirmNoticeDraft>) => void;
    decision.getFirmNoticeDraft.mockImplementationOnce(() => new Promise<ApiResult<FirmNoticeDraft>>(r => (landDraft = r)));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toNotify(d, REJECT, 'Duplicate');
    await waitFor(() => expect(landDraft).toBeDefined());
    expect(within(d).getByTestId('notice-loading')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    // The draft arrives while the Confirm step is showing: the words and the request do not change.
    await act(async () => landDraft(ok(firmNoticeDraft())));
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Duplicate']);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AC7: the limits are hints that hold Next, and nothing is cut off
// ─────────────────────────────────────────────────────────────────────────────

describe('AC7: a subject over 120 or a message over 1500 characters, or a blank one with sending on, holds Next with a field message', () => {
  const SUFFIX = ' (INV-2025-78232.1)';

  it('the subject: 121 characters hold Next (the tracking number the server adds is not counted), 120 do not, and nothing is cut off', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    type(subjectBox(d), 'a'.repeat(121) + SUFFIX);
    expect(subjectBox(d).value).toBe('a'.repeat(121) + SUFFIX); // not cut off
    expect(within(d).getByText('The subject may be at most 120 characters.')).toBeInTheDocument();
    expect(within(d).getByTestId('notice-subject-count')).toHaveTextContent('121 / 120 characters');
    expect(next(d)).toBeDisabled();
    type(subjectBox(d), 'a'.repeat(120) + SUFFIX);
    expect(within(d).queryByText('The subject may be at most 120 characters.')).not.toBeInTheDocument();
    expect(within(d).getByTestId('notice-subject-count')).toHaveTextContent('120 / 120 characters');
    expect(next(d)).toBeEnabled();
  });

  it('the message: 1501 characters hold Next, 1500 do not, and nothing is cut off', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    type(proseBox(d), 'b'.repeat(1501));
    expect(proseBox(d).value).toHaveLength(1501);
    expect(within(d).getByText('The message may be at most 1500 characters.')).toBeInTheDocument();
    expect(within(d).getByTestId('notice-prose-count')).toHaveTextContent('1,501 / 1,500 characters');
    expect(next(d)).toBeDisabled();
    type(proseBox(d), 'b'.repeat(1500));
    expect(next(d)).toBeEnabled();
  });

  it('blank (or spaces only) with sending on holds Next, with a message per field', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    type(subjectBox(d), '    ');
    expect(within(d).getByText('Enter a subject.')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    type(subjectBox(d), 'A subject');
    expect(next(d)).toBeEnabled();
    type(proseBox(d), ' \n ');
    expect(within(d).getByText('Enter a message.')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
  });

  it('(must-not-fire) with sending off, a blank or over-long notice holds nothing: the decision goes without it', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    type(proseBox(d), 'b'.repeat(1501));
    expect(next(d)).toBeDisabled();
    fireEvent.click(sendSwitch(d));
    expect(next(d)).toBeEnabled();
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Duplicate']);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AC8: an unsure outcome locks the notice and the send choice
// ─────────────────────────────────────────────────────────────────────────────

describe('AC8: a decision that came back unsure reopens with the same reason, note, notice and send choice, locked', () => {
  it('with the notice on: the reopened wizard shows the same edited notice, locked, and the repeat is the same request', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate of an earlier invoice');
    type(subjectBox(d), 'Edited subject (INV-2025-78232.1)');
    type(proseBox(d), 'Edited message.');
    await toConfirm(d);
    fireEvent.change(within(d).getByLabelText('Note for the decision'), { target: { value: 'The first note' } });
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('decision-note-locked');
    expect(decision.reject).toHaveBeenCalledTimes(1);
    await closeWizard(d);

    const again = await openWizard();
    expect(radio(again, REJECT)).toBeChecked();
    fireEvent.click(next(again));
    expect(reasonBox(again).value).toBe('Duplicate of an earlier invoice');
    fireEvent.click(next(again));
    await within(again).findByTestId('notify-step');
    // The same notice and choice, locked; no new draft was asked for.
    expect(sendSwitch(again)).toBeChecked();
    expect(sendSwitch(again)).toBeDisabled();
    expect(subjectBox(again).value).toBe('Edited subject (INV-2025-78232.1)');
    expect(subjectBox(again)).toHaveAttribute('readonly');
    expect(proseBox(again).value).toBe('Edited message.');
    expect(proseBox(again)).toHaveAttribute('readonly');
    expect(within(again).getByTestId('notice-locked')).toBeInTheDocument();
    expect(within(again).queryByTestId('notice-restore')).not.toBeInTheDocument();
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);

    // Skip does not drop a locked notice: the repeat is the same request.
    fireEvent.click(skip(again));
    await within(again).findByTestId('confirm-decision');
    expect(within(again).getByTestId('confirm-notice')).toHaveTextContent('The firm will be emailed this notice');
    fireEvent.click(btn(again, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[1][3]).toEqual({ subject: 'Edited subject (INV-2025-78232.1)', prose: 'Edited message.' });
  });

  it('with the notice off (turned off, or the step skipped): the choice is locked off and the repeat carries no notice', async () => {
    const decision = mockDecisionApi();
    decision.returnToFirm.mockResolvedValueOnce(failed(new Error('Failed to fetch')));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, RETURN, 'Please correct the rates');
    // Skipped with a draft loaded and sending on: the request carried none.
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(btn(d, FINISH_RETURN));
    await within(d).findByTestId('decision-note-locked');
    expect(decision.returnToFirm.mock.calls[0]).toEqual([INVOICE_ID, 'Please correct the rates']);
    // Back on the Notify step of the same wizard: locked off, however the shell was left.
    fireEvent.click(back(d));
    await within(d).findByTestId('notify-step');
    expect(sendSwitch(d)).not.toBeChecked();
    expect(sendSwitch(d)).toBeDisabled();
    await closeWizard(d);

    const again = await openWizard();
    fireEvent.click(next(again));
    fireEvent.click(next(again));
    await within(again).findByTestId('notify-step');
    expect(sendSwitch(again)).not.toBeChecked();
    expect(sendSwitch(again)).toBeDisabled();
    fireEvent.click(next(again));
    await within(again).findByTestId('confirm-decision');
    expect(within(again).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(again, FINISH_RETURN));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(2));
    expect(decision.returnToFirm.mock.calls[1]).toEqual(decision.returnToFirm.mock.calls[0]);
  });

  it('a draft that lands after the decision came back unsure does not change what the repeat carries', async () => {
    const decision = mockDecisionApi();
    let landDraft!: (r: ApiResult<FirmNoticeDraft>) => void;
    decision.getFirmNoticeDraft.mockImplementationOnce(() => new Promise<ApiResult<FirmNoticeDraft>>(r => (landDraft = r)));
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toNotify(d, REJECT, 'Duplicate');
    await waitFor(() => expect(landDraft).toBeDefined());
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('decision-note-locked');
    await act(async () => landDraft(ok(firmNoticeDraft())));
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[1]).toHaveLength(2);
  });

  it('(must-not-fire) a 503 that saved nothing locks nothing: the notice stays editable', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.unavailable', { detail: 'The service is busy. Try again.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('The service is busy. Try again.');
    fireEvent.click(back(d));
    await within(d).findByTestId('notice-subject');
    expect(subjectBox(d)).not.toHaveAttribute('readonly');
    expect(sendSwitch(d)).toBeEnabled();
    expect(within(d).queryByTestId('notice-locked')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AC9: no unsafe HTML
// ─────────────────────────────────────────────────────────────────────────────

/** The unsafe-HTML shapes in a source text: `dangerouslySetInnerHTML`, `innerHTML =`, and an `<iframe` without `sandbox=""`. */
function unsafeHtml(source: string): string[] {
  const found: string[] = [];
  if (/dangerouslySetInnerHTML/.test(source)) found.push('dangerouslySetInnerHTML');
  if (/\.(inner|outer)HTML\s*=/.test(source)) found.push('innerHTML assignment');
  for (const m of source.matchAll(/<iframe\b[^>]*>/g)) if (!/\bsandbox=(""|\{''\}|\{""\})/.test(m[0])) found.push('iframe without an empty sandbox');
  return found;
}

describe('AC9: no unsafe HTML in the page', () => {
  it('(controls) the scan fires on each unsafe shape and stays quiet on the safe one', () => {
    expect(unsafeHtml('<div dangerouslySetInnerHTML={{ __html: x }} />')).toEqual(['dangerouslySetInnerHTML']);
    expect(unsafeHtml('el.innerHTML = html;')).toEqual(['innerHTML assignment']);
    expect(unsafeHtml('<iframe srcDoc={html} />')).toEqual(['iframe without an empty sandbox']);
    expect(unsafeHtml('<iframe sandbox="allow-scripts" srcDoc={html} />')).toEqual(['iframe without an empty sandbox']);
    expect(unsafeHtml('<iframe sandbox="" srcDoc={html} />')).toEqual([]);
    expect(unsafeHtml('<div>{text}</div>')).toEqual([]);
  });

  it('no source file of the page uses dangerouslySetInnerHTML or an innerHTML assignment, and any iframe is sandboxed with no allowances', () => {
    const offenders = listSourceFiles().flatMap(f => unsafeHtml(fs.readFileSync(f, 'utf8')).map(w => `${f}: ${w}`));
    expect(offenders).toEqual([]);
  });

  it('the draft\'s HTML is never rendered: a script in it does not reach the page', async () => {
    const decision = mockDecisionApi();
    decision.getFirmNoticeDraft.mockResolvedValue(ok(firmNoticeDraft({ html: '<p id="from-draft">hello</p><script>window.__x = 1</script>' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT);
    expect(document.getElementById('from-draft')).toBeNull();
    expect((window as unknown as { __x?: number }).__x).toBeUndefined();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Review round 1
// ─────────────────────────────────────────────────────────────────────────────

/** The open Finish form of the stranded-decision banner (found by role, then by its title text: an open surface may be aria-hidden). */
async function finishDialog(): Promise<HTMLElement> {
  return waitFor(() => {
    const d = screen.getAllByRole('dialog', { hidden: true }).find(x => within(x).queryByText('Finish decision') !== null);
    if (!d) throw new Error('the Finish form is not open');
    return d;
  });
}
const finishBtn = (d: HTMLElement) => within(d).getByRole('button', { name: 'Finish', hidden: true });

/**
 * A page whose first reject (or return) comes back 503 incomplete and leaves the invoice stranded, as the server reports it (the approval read
 * shows `incompleteDecision`, the review shows the new status): the banner's Finish is then the only way to finish it.
 */
function strandingPage(verb: 'Reject' | 'Return' = 'Reject') {
  const decision = mockDecisionApi();
  const world = { state: approval(), review: clean() };
  decision.getApproval.mockImplementation(async () => ok(world.state));
  const strand = async () => {
    world.state = strandedApproval(verb);
    world.review = decidedReview(verb);
    return incomplete();
  };
  (verb === 'Reject' ? decision.reject : decision.returnToFirm).mockImplementationOnce(strand);
  const { api } = renderDecisions(clean(), decision);
  api.getReview.mockImplementation(async () => ok(world.review));
  return { decision, world };
}

/** Reject through the wizard with the notice as it stands (after `tweak`), send it, and close the wizard that came back unsure. */
async function sendThenStrand(finishName: RegExp, which: RegExp, tweak?: (d: HTMLElement) => void) {
  const d = await openWizard();
  await toLoadedNotify(d, which, 'Duplicate of an earlier invoice');
  tweak?.(d);
  await toConfirm(d);
  fireEvent.click(btn(d, finishName));
  await within(d).findByTestId('decision-note-locked');
  await closeWizard(d);
}

describe('Review round 1 F1-a: the banner\'s Finish repeats the wizard\'s request, notice included', () => {
  it('Reject with the notice on, 503 incomplete, then Finish: the banner call equals the wizard\'s first call (notice included) and the form says so', async () => {
    const { decision } = strandingPage('Reject');
    await sendThenStrand(FINISH_REJECT, REJECT, d => {
      type(subjectBox(d), 'Edited subject (INV-2025-78232.1)');
      type(proseBox(d), 'Edited message.');
    });
    expect(decision.reject).toHaveBeenCalledTimes(1);
    expect(decision.reject.mock.calls[0][3]).toEqual({ subject: 'Edited subject (INV-2025-78232.1)', prose: 'Edited message.' });

    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    expect(within(dialog).getByTestId('finish-decision-notice')).toHaveTextContent('The firm will be emailed this notice, with the findings table: “Edited subject (INV-2025-78232.1)”.');
    fireEvent.click(finishBtn(dialog));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[1][3]).toEqual({ subject: 'Edited subject (INV-2025-78232.1)', prose: 'Edited message.' });
  });

  it('Return with a note: the banner call equals the wizard\'s first call, note and notice', async () => {
    const { decision } = strandingPage('Return');
    const d = await openWizard();
    await toLoadedNotify(d, RETURN, 'Please correct the rates');
    await toConfirm(d);
    fireEvent.change(within(d).getByLabelText('Note for the decision'), { target: { value: 'A note' } });
    fireEvent.click(btn(d, FINISH_RETURN));
    await within(d).findByTestId('decision-note-locked');
    await closeWizard(d);
    fireEvent.click(await screen.findByTestId('finish-decision'));
    fireEvent.click(finishBtn(await finishDialog()));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(2));
    expect(decision.returnToFirm.mock.calls[1]).toEqual(decision.returnToFirm.mock.calls[0]);
    expect(decision.returnToFirm.mock.calls[1][2]).toBe('A note');
    const draft = firmNoticeDraft({ kind: 'ReturnedToFirm' });
    expect(decision.returnToFirm.mock.calls[1][3]).toEqual({ subject: draft.subject, prose: draft.prose });
  });

  it('(must-not-fire) a wizard lock with the notice off carries none: the banner says no email and its call has no notice fields', async () => {
    const { decision } = strandingPage('Reject');
    await sendThenStrand(FINISH_REJECT, REJECT, d => fireEvent.click(sendSwitch(d)));
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Duplicate of an earlier invoice']);

    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    expect(within(dialog).getByTestId('finish-decision-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(finishBtn(dialog));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[1]).toHaveLength(2);
  });

  it('ISS-041 (must-not-fire): with the notice kept in memory (on, or off) the Finish form offers no Notify step and asks for no second draft', async () => {
    for (const tweak of [undefined, (d: HTMLElement) => fireEvent.click(sendSwitch(d))]) {
      const { decision } = strandingPage('Reject');
      await sendThenStrand(FINISH_REJECT, REJECT, tweak);
      expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);
      fireEvent.click(await screen.findByTestId('finish-decision'));
      const dialog = await finishDialog();
      expect(within(dialog).queryByTestId('notify-step')).not.toBeInTheDocument();
      expect(within(dialog).getByTestId('finish-decision-notice')).toBeInTheDocument();
      expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);
      cleanup();
    }
  });

  it('a Finish that comes back unsure again keeps the notice in the lock: the next Finish still carries it', async () => {
    const { decision } = strandingPage('Reject');
    await sendThenStrand(FINISH_REJECT, REJECT);
    decision.reject.mockResolvedValueOnce(incomplete());
    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    fireEvent.click(finishBtn(dialog));
    await within(dialog).findByTestId('finish-decision-error');
    expect(decision.reject).toHaveBeenCalledTimes(2);
    expect(within(dialog).getByTestId('finish-decision-notice')).toHaveTextContent('The firm will be emailed this notice');
    fireEvent.click(finishBtn(dialog));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(3));
    expect(decision.reject.mock.calls[2]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[2][3]).toBeDefined();
  });

  it('a 400 notice_refused on the banner is shown in the form: nothing is re-read and the form stays open to try again', async () => {
    const { decision } = strandingPage('Reject');
    await sendThenStrand(FINISH_REJECT, REJECT);
    decision.reject.mockResolvedValueOnce(
      failed(problem(400, 'ebilling.notice_refused', { detail: 'The notice to the firm was not accepted (prose.digit). Nothing was decided. Edit the notice and try again.' }))
    );
    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    const reads = decision.getApproval.mock.calls.length;
    fireEvent.click(finishBtn(dialog));
    expect(await within(dialog).findByTestId('finish-decision-notice-refused')).toHaveTextContent('(prose.digit)');
    expect(within(dialog).queryByTestId('finish-decision-error')).not.toBeInTheDocument();
    expect(decision.getApproval.mock.calls.length).toBe(reads);
    expect(finishBtn(dialog)).toBeEnabled();
  });
});

describe('Review round 1 F1-b: a refusal of the notice is not shown once there is no such notice', () => {
  const refusal = () => failed(problem(400, 'ebilling.notice_refused', { detail: 'The notice to the firm was not accepted (prose.digit). Nothing was decided. Edit the notice and try again.' }));

  async function refusedOnReject() {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(refusal());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('confirm-notice-refused');
    return { d, decision };
  }

  it('(1) Skip: Confirm then shows only "no email", and the refusal is gone from the Notify fields too', async () => {
    const { d } = await refusedOnReject();
    fireEvent.click(back(d));
    expect(await within(d).findByTestId('notice-refused')).toBeInTheDocument();
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    expect(within(d).queryByTestId('confirm-notice-refused')).not.toBeInTheDocument();
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(back(d));
    await within(d).findByTestId('notify-step');
    expect(within(d).queryByTestId('notice-refused')).not.toBeInTheDocument();
  });

  it('(2) switching to Return: no refusal under Return\'s notice, and none on its Confirm', async () => {
    const { d } = await refusedOnReject();
    fireEvent.click(back(d)); // Notify
    fireEvent.click(back(d)); // Reason
    fireEvent.click(back(d)); // Decision
    fireEvent.click(radio(d, RETURN));
    fireEvent.click(next(d));
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-subject');
    expect(within(d).queryByTestId('notice-refused')).not.toBeInTheDocument();
    await toConfirm(d);
    expect(within(d).queryByTestId('confirm-notice-refused')).not.toBeInTheDocument();
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('The firm will be emailed this notice');
  });

  it('(3) switching to Approve: Confirm shows no refusal', async () => {
    const { d } = await refusedOnReject();
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(back(d));
    fireEvent.click(radio(d, /^Approve/));
    fireEvent.click(next(d));
    fireEvent.click(skip(d));
    await within(d).findByTestId('confirm-decision');
    expect(within(d).queryByTestId('confirm-notice-refused')).not.toBeInTheDocument();
    expect(within(d).queryByTestId('confirm-notice')).not.toBeInTheDocument();
  });

  it('(must-not-fire) with the notice still on, the refusal stays on both steps until the reviewer edits it', async () => {
    const { d } = await refusedOnReject();
    expect(within(d).getByTestId('confirm-notice-refused')).toBeInTheDocument();
    fireEvent.click(back(d));
    expect(await within(d).findByTestId('notice-refused')).toBeInTheDocument();
  });
});

describe('Review round 1 F1-c: a locked notice keeps its tracking suffix, so it is not over the limit', () => {
  it('a 110-character subject plus the tracking number reads 110 / 120 with no error before and after the lock', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    type(subjectBox(d), `${'x'.repeat(110)} (INV-2025-78232.1)`);
    expect(within(d).getByTestId('notice-subject-count')).toHaveTextContent('110 / 120 characters');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('decision-note-locked');
    // The same wizard, Back to the Notify step: the entry now comes from the lock.
    fireEvent.click(back(d));
    await within(d).findByTestId('notify-step');
    expect(within(d).getByTestId('notice-subject-count')).toHaveTextContent('110 / 120 characters');
    expect(within(d).queryByText(/at most 120/)).not.toBeInTheDocument();
    await closeWizard(d);

    const again = await openWizard();
    fireEvent.click(next(again));
    fireEvent.click(next(again));
    await within(again).findByTestId('notify-step');
    expect(within(again).getByTestId('notice-subject-count')).toHaveTextContent('110 / 120 characters');
    expect(within(again).queryByText(/at most 120/)).not.toBeInTheDocument();
  });
});

describe('Review round 1 K2: a draft that failed with no code of its own shows its HTTP status', () => {
  it('(must-not-fire) no answer at all (status 0) shows no HTTP tag', async () => {
    const decision = mockDecisionApi();
    decision.getFirmNoticeDraft.mockResolvedValueOnce(failed(new Error('Failed to fetch')));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toNotify(d, REJECT, 'Duplicate');
    expect(await within(d).findByTestId('notice-draft-error-text')).not.toHaveTextContent('HTTP');
  });
});

describe('Review round 1 K4: the counters and the refusal are tied to their fields', () => {
  it('each counter is the description of its input, and the refusal bar describes both fields while it shows', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(problem(400, 'ebilling.notice_refused', { detail: 'The notice to the firm was not accepted (prose.digit). Nothing was decided.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    const describedBy = (el: HTMLElement) => (el.getAttribute('aria-describedby') ?? '').split(' ');
    expect(within(d).getByTestId('notice-subject-count').id).not.toBe('');
    expect(describedBy(subjectBox(d))).toContain(within(d).getByTestId('notice-subject-count').id);
    expect(describedBy(proseBox(d))).toContain(within(d).getByTestId('notice-prose-count').id);
    // The hint no longer contradicts a subject that already shows the tracking number.
    expect(within(d).getByText('The tracking number in brackets is added by Spaarke and does not count toward the limit.')).toBeInTheDocument();
    expect(within(d).queryByTestId('notice-refused')).not.toBeInTheDocument();

    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('confirm-notice-refused');
    fireEvent.click(back(d));
    const bar = await within(d).findByTestId('notice-refused');
    expect(bar.id).not.toBe('');
    expect(describedBy(subjectBox(d))).toEqual(expect.arrayContaining([bar.id, within(d).getByTestId('notice-subject-count').id]));
    expect(describedBy(proseBox(d))).toEqual(expect.arrayContaining([bar.id, within(d).getByTestId('notice-prose-count').id]));
  });
});

describe('Review round 2: a locked repeat that the server refuses on the notice is not stuck', () => {
  const refusal = () =>
    failed(problem(400, 'ebilling.notice_refused', { detail: 'The notice to the firm was not accepted (prose.digit). Nothing was decided. Edit the notice and try again.' }));

  /** No answer to the first send (locked), then the reopened repeat answers 400 notice_refused: the wizard on its Confirm step. */
  async function lockedThenRefused() {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(new Error('Failed to fetch')));
    decision.reject.mockResolvedValueOnce(refusal());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate of an earlier invoice');
    type(proseBox(d), 'We cannot pay invoice 5.');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('decision-note-locked');
    await closeWizard(d);

    const again = await openWizard();
    fireEvent.click(next(again));
    fireEvent.click(next(again));
    await within(again).findByTestId('notify-step');
    expect(subjectBox(again)).toHaveAttribute('readonly');
    fireEvent.click(next(again));
    await within(again).findByTestId('confirm-decision');
    fireEvent.click(btn(again, FINISH_REJECT));
    await within(again).findByTestId('confirm-notice-refused');
    expect(decision.reject).toHaveBeenCalledTimes(2);
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    return { decision, d: again };
  }

  it('the refusal proves nothing was decided: the lock is dropped, the notice is editable, and the next Finish sends the edited notice', async () => {
    const { decision, d } = await lockedThenRefused();
    // The refusal stays; the note box is editable again; the reason and the typed notice are as they were.
    expect(within(d).queryByTestId('decision-note-locked')).not.toBeInTheDocument();
    expect(within(d).getByLabelText('Note for the decision')).not.toHaveAttribute('readonly');
    fireEvent.click(back(d));
    await within(d).findByTestId('notify-step');
    expect(within(d).getByTestId('notice-refused')).toHaveTextContent('(prose.digit)');
    expect(within(d).queryByTestId('notice-locked')).not.toBeInTheDocument();
    expect(subjectBox(d)).not.toHaveAttribute('readonly');
    expect(proseBox(d)).not.toHaveAttribute('readonly');
    expect(proseBox(d).value).toBe('We cannot pay invoice 5.');
    expect(sendSwitch(d)).toBeEnabled();
    fireEvent.click(back(d));
    expect(reasonBox(d).value).toBe('Duplicate of an earlier invoice');
    fireEvent.click(next(d));
    await within(d).findByTestId('notice-prose');
    type(proseBox(d), 'We cannot pay this invoice.');
    await toConfirm(d);
    expect(within(d).queryByTestId('confirm-notice-refused')).not.toBeInTheDocument();
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(3));
    expect(decision.reject.mock.calls[2][3]).toEqual({ subject: firmNoticeDraft().subject, prose: 'We cannot pay this invoice.' });
    expect(await within(d).findByText('Rejected')).toBeInTheDocument();
  });

  it('the reviewer can turn the notice off, and the next Finish carries none', async () => {
    const { decision, d } = await lockedThenRefused();
    fireEvent.click(back(d));
    await within(d).findByTestId('notify-step');
    expect(sendSwitch(d)).toBeEnabled();
    fireEvent.click(sendSwitch(d));
    expect(sendSwitch(d)).not.toBeChecked();
    expect(within(d).queryByTestId('notice-refused')).not.toBeInTheDocument();
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(3));
    expect(decision.reject.mock.calls[2]).toEqual([INVOICE_ID, 'Duplicate of an earlier invoice']);
  });

  it('the page-level lock is gone too: closing and reopening the wizard gives a fresh one', async () => {
    const { d } = await lockedThenRefused();
    await closeWizard(d);
    const fresh = await openWizard();
    expect(radio(fresh, /^Approve/)).toBeChecked();
  });

  it('(must-not-fire) a locked repeat that fails another way keeps its lock: the notice stays read-only', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValueOnce(failed(new Error('Failed to fetch')));
    decision.reject.mockResolvedValueOnce(incomplete());
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await toLoadedNotify(d, REJECT, 'Duplicate');
    await toConfirm(d);
    fireEvent.click(btn(d, FINISH_REJECT));
    await within(d).findByTestId('decision-note-locked');
    fireEvent.click(btn(d, FINISH_REJECT));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(await within(d).findByRole('alert', { hidden: true })).toBeInTheDocument();
    expect(within(d).getByTestId('decision-note-locked')).toBeInTheDocument();
    fireEvent.click(back(d));
    await within(d).findByTestId('notify-step');
    expect(subjectBox(d)).toHaveAttribute('readonly');
    expect(sendSwitch(d)).toBeDisabled();
  });
});
