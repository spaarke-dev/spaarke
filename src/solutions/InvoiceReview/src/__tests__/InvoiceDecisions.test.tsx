import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import type { ApprovalState } from '../services/decisionApi';
import type { InvoiceReview } from '../services/invoiceReviewApi';
import {
  INVOICE_ID,
  SUBMISSION_ID,
  approval,
  blockedSubmission,
  decisionRecord,
  failed,
  flagged,
  forbidden,
  heldReview,
  invoiceContext,
  line,
  lineId,
  listSourceFiles,
  mockApi,
  mockDecisionApi,
  ok,
  problem,
  review,
  type MockDecisionApi,
} from './testKit';

interface RenderOpts {
  theme?: typeof webLightTheme;
  notifyStep?: React.ComponentProps<typeof InvoiceReviewWithDecisions>['notifyStep'];
}

function renderDecisions(reviews: InvoiceReview | InvoiceReview[], decision: MockDecisionApi, opts: RenderOpts = {}) {
  const api = mockApi(reviews);
  const view = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceReviewWithDecisions
        invoiceId={INVOICE_ID}
        api={api as never}
        decisionApi={decision as never}
        pollIntervalMs={60_000}
        notifyStep={opts.notifyStep}
      />
    </FluentProvider>
  );
  return { api, view };
}

/** An approval that turns into `after` once the test's decision call has run (the server's state after the write). */
function statefulApproval(decision: MockDecisionApi, before: ApprovalState, after: ApprovalState, writes: Array<'approve' | 'reject' | 'returnToFirm'>) {
  let done = false;
  decision.getApproval.mockImplementation(async () => ok(done ? after : before));
  for (const w of writes) {
    const original = decision[w].getMockImplementation()!;
    decision[w].mockImplementation(async (...args: unknown[]) => {
      const r = await original(...args);
      done = true;
      return r;
    });
  }
}

const clean = () => review([line(1), line(2)]);

async function openWizard(): Promise<HTMLElement> {
  fireEvent.click(await screen.findByTestId('decide-invoice'));
  return screen.findByRole('dialog', { name: 'Decide invoice', hidden: true });
}

const next = (d: HTMLElement) => within(d).getByRole('button', { name: 'Next', hidden: true });
const radio = (d: HTMLElement, name: RegExp) => within(d).getByRole('radio', { name, hidden: true });
const finishBtn = (d: HTMLElement, name: RegExp) => within(d).getByRole('button', { name, hidden: true });

async function toConfirmViaReject(d: HTMLElement, reason: string) {
  fireEvent.click(radio(d, /^Reject/));
  fireEvent.click(next(d));
  fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: reason } });
  fireEvent.click(next(d));
  await within(d).findByTestId('confirm-decision');
}

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// Header: Awaiting approvers
// ─────────────────────────────────────────────────────────────────────────────

describe('Awaiting approvers (header)', () => {
  it('names the people the invoice waits for', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const awaiting = await screen.findByTestId('awaiting');
    expect(awaiting).toHaveTextContent('Awaiting: Pat Approver, Sam Second');
    expect(awaiting).toHaveAttribute('data-tone', 'neutral');
    expect(decision.getApproval).toHaveBeenCalledTimes(1);
    expect(decision.getApproval).toHaveBeenCalledWith(INVOICE_ID);
  });

  it('flags an invoice nobody may approve (zero approvers)', async () => {
    const decision = mockDecisionApi(approval({ eligibleApprovers: [], noEligibleApprover: true, canApprove: false, callerEligible: false }));
    renderDecisions(clean(), decision);
    const awaiting = await screen.findByTestId('awaiting');
    expect(awaiting).toHaveAttribute('data-tone', 'warning');
    expect(awaiting).toHaveTextContent('No eligible approver');
  });

  it('says so when the approvers cannot be read, and the page still works', async () => {
    const decision = mockDecisionApi();
    decision.getApproval.mockResolvedValue(failed(problem(500, null)));
    renderDecisions(clean(), decision);
    expect(await screen.findByTestId('awaiting')).toHaveTextContent('Approvers are not available right now.');
    expect(screen.getByTestId('review-title')).toBeInTheDocument();
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
  });

  it('retries a failed first read: Retry asks again and Decide invoice appears', async () => {
    const decision = mockDecisionApi();
    decision.getApproval.mockResolvedValueOnce(failed(problem(503, null)));
    renderDecisions(clean(), decision);
    expect(await screen.findByTestId('awaiting')).toHaveTextContent('Approvers are not available right now.');
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
    // One read for one review signature: nothing asks again by itself.
    expect(decision.getApproval).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByTestId('awaiting-retry'));
    expect(await screen.findByTestId('decide-invoice')).toBeInTheDocument();
    expect(decision.getApproval).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('awaiting')).toHaveTextContent('Awaiting: Pat Approver, Sam Second');
    expect(screen.queryByTestId('awaiting-retry')).not.toBeInTheDocument();
  });

  it('does not keep the signature of a failed read: the next re-read of the same review asks again by itself', async () => {
    jest.useFakeTimers();
    try {
      // Line 2 stays Processing, so every poll brings back a review with the SAME approval signature.
      const pending = review([line(1), line(2, { status: 'Pending', statusLabel: 'Processing' })]);
      const decision = mockDecisionApi();
      decision.getApproval.mockResolvedValueOnce(failed(problem(503, null)));
      const api = mockApi(pending);
      // The server sends a new object on every read (the mock would hand back the same one, which React does not re-render).
      api.getReview.mockImplementation(async () => ok(JSON.parse(JSON.stringify(pending)) as InvoiceReview));
      render(
        <FluentProvider theme={webLightTheme}>
          <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={api as never} decisionApi={decision as never} pollIntervalMs={1000} />
        </FluentProvider>
      );
      await act(async () => {
        await Promise.resolve();
      });
      expect(await screen.findByTestId('awaiting')).toHaveTextContent('Approvers are not available right now.');
      expect(decision.getApproval).toHaveBeenCalledTimes(1);
      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(decision.getApproval).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.getByTestId('awaiting')).toHaveTextContent('Awaiting: Pat Approver, Sam Second'));
    } finally {
      jest.useRealTimers();
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Decide invoice: who sees what (server signals only)
// ─────────────────────────────────────────────────────────────────────────────

describe('Decide invoice is shown only from the server signals', () => {
  it('shows Decide invoice to an eligible approver', async () => {
    renderDecisions(clean(), mockDecisionApi());
    expect(await screen.findByTestId('decide-invoice')).toHaveTextContent('Decide invoice');
  });

  it('shows nothing to a viewer who is not an eligible approver', async () => {
    renderDecisions(clean(), mockDecisionApi(approval({ callerEligible: false, canApprove: false })));
    await screen.findByTestId('awaiting');
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
    expect(screen.queryByTestId('view-decision')).not.toBeInTheDocument();
  });

  it('shows nothing to an eligible approver without Write on the invoice (the server flag)', async () => {
    renderDecisions(review([line(1)], { flags: { canRead: true, canDecide: false } }), mockDecisionApi());
    await screen.findByTestId('awaiting');
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Approve
// ─────────────────────────────────────────────────────────────────────────────

describe('Approve', () => {
  it('approves from the confirm step with no body, then re-reads the page and the approval', async () => {
    const decision = mockDecisionApi();
    statefulApproval(decision, approval(), approval({ status: 'Approved', canApprove: false, decidedBy: 'Pat Approver', approvedAmount: 5000 }), ['approve']);
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();

    expect(radio(d, /^Approve/)).toBeChecked();
    expect(within(d).getByTestId('wizard-approved')).toHaveTextContent('5,000.00');
    fireEvent.click(next(d)); // Decision -> Invoice adjustment
    await within(d).findByLabelText('Adjustment value');
    fireEvent.click(within(d).getByRole('button', { name: 'Skip', hidden: true }));
    expect(await within(d).findByTestId('confirm-decision')).toHaveTextContent('Approve');
    expect(within(d).getByTestId('confirm-invoice-adj')).toHaveTextContent('None');

    const reads = api.getReview.mock.calls.length;
    fireEvent.click(finishBtn(d, /Approve .*5,000\.00 & record/));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledWith(INVOICE_ID));
    expect(await within(d).findByText('Approved')).toBeInTheDocument();
    expect(decision.reject).not.toHaveBeenCalled();
    expect(decision.returnToFirm).not.toHaveBeenCalled();
    // The page shows the server's state after the write.
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
  });

  it('adds an invoice adjustment through route 102 and shows the server totals, with no amount computed here', async () => {
    const decision = mockDecisionApi();
    const adj = line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy', approvedAmount: -250 });
    const base = clean();
    const adjusted = review([line(1), line(2), adj], { totals: { ...base.totals, invoiceAdjustments: -250, approved: 4750 } });
    renderDecisions([base, adjusted], decision);
    const d = await openWizard();
    fireEvent.click(next(d));

    fireEvent.change(await within(d).findByLabelText('Adjustment value'), { target: { value: '5' } });
    // Unsaved adjustment text blocks Next; it must be added or cleared.
    expect(next(d)).toBeDisabled();
    expect(within(d).getByTestId('add-adjustment')).toBeDisabled(); // no reason yet
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Courtesy' } });
    fireEvent.click(within(d).getByTestId('add-adjustment'));

    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(1));
    expect(decision.addAdjustment).toHaveBeenCalledWith(INVOICE_ID, {
      adjustmentId: expect.stringMatching(/^[0-9a-f-]{36}$/),
      method: 'percent',
      value: 5,
      reason: 'Courtesy',
      ruleId: null,
    });
    // The totals shown are the re-read ones.
    await waitFor(() => expect(within(d).getByTestId('wizard-invoice-adj')).toHaveTextContent('250.00'));
    expect(within(d).getByTestId('wizard-approved')).toHaveTextContent('4,750.00');
    expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toHaveTextContent('Courtesy');
    await waitFor(() => expect(next(d)).toBeEnabled());

    fireEvent.click(next(d));
    expect(await within(d).findByTestId('confirm-invoice-adj')).toHaveTextContent('Courtesy');
    expect(within(d).getByTestId('confirm-approved')).toHaveTextContent('4,750.00');
    expect(finishBtn(d, /Approve .*4,750\.00 & record/)).toBeEnabled();
  });

  it('sends the chosen fixed amount and rule, and shows a refusal with its code without losing the form', async () => {
    const decision = mockDecisionApi();
    // InvoiceAdjustmentService: an adjustment that would take the invoice total below zero is a 400 negative_total.
    decision.addAdjustment.mockResolvedValue(failed(problem(400, 'ebilling.adjustment.negative_total', { detail: 'The adjustment is more than the fees.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));

    fireEvent.click(await within(d).findByRole('radio', { name: 'Fixed amount', hidden: true }));
    fireEvent.change(within(d).getByLabelText('Adjustment value'), { target: { value: '9000' } });
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Too high' } });
    fireEvent.click(within(d).getByLabelText('Adjustment rule'));
    fireEvent.click(await screen.findByRole('option', { name: /Block billing/, hidden: true }));
    fireEvent.click(within(d).getByTestId('add-adjustment'));

    expect(await within(d).findByTestId('adjustment-error')).toHaveTextContent('The adjustment is more than the fees. (ebilling.adjustment.negative_total)');
    expect(decision.addAdjustment).toHaveBeenCalledWith(INVOICE_ID, expect.objectContaining({ method: 'fixed', value: 9000, ruleId: 'r1' }));
    // Nothing changed: the typed values are still there and the totals are the old ones.
    expect(within(d).getByLabelText('Adjustment value')).toHaveValue(9000);
    expect(within(d).getByTestId('wizard-approved')).toHaveTextContent('5,000.00');
  });

  it('repeats a partly saved adjustment (503) with the same client id', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(failed(problem(503, 'ebilling.adjustment.incomplete', { detail: 'The line was saved but the totals were not.' })));
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.change(await within(d).findByLabelText('Adjustment value'), { target: { value: '5' } });
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Courtesy' } });
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(d).getByTestId('add-adjustment'));

    expect(await within(d).findByTestId('adjustment-error')).toHaveTextContent('repeat it to finish');
    // The page shows the server's state (the line was written) before Repeat is offered.
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    const again = within(d).getByTestId('add-adjustment');
    expect(again).toHaveTextContent('Repeat');
    fireEvent.click(again);
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    expect(decision.addAdjustment.mock.calls[1][1].adjustmentId).toBe(decision.addAdjustment.mock.calls[0][1].adjustmentId);
  });

  const fillAdjustment = async (d: HTMLElement) => {
    fireEvent.click(next(d));
    fireEvent.change(await within(d).findByLabelText('Adjustment value'), { target: { value: '5' } });
    fireEvent.change(within(d).getByLabelText('Adjustment reason'), { target: { value: 'Courtesy' } });
  };
  const adjusted = () => {
    const adj = line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy', approvedAmount: -250 });
    const base = clean();
    return review([line(1), line(2), adj], { totals: { ...base.totals, invoiceAdjustments: -250, approved: 4750 } });
  };

  it('after a 503 incomplete the page shows the re-read totals, and Skip goes to a Confirm step with them', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(failed(problem(503, 'ebilling.adjustment.incomplete', { detail: 'The line was saved but the totals were not.' })));
    // The first read is the clean invoice; the re-read after the 503 shows the adjustment the server did write.
    renderDecisions([clean(), adjusted()], decision);
    const d = await openWizard();
    await fillAdjustment(d);
    fireEvent.click(within(d).getByTestId('add-adjustment'));

    expect(await within(d).findByTestId('adjustment-error')).toHaveTextContent('repeat it to finish');
    await waitFor(() => expect(within(d).getByTestId('wizard-approved')).toHaveTextContent('4,750.00'));
    expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toHaveTextContent('Courtesy');

    fireEvent.click(within(d).getByRole('button', { name: 'Skip', hidden: true }));
    expect(await within(d).findByTestId('confirm-invoice-adj')).toHaveTextContent('Courtesy');
    expect(within(d).getByTestId('confirm-approved')).toHaveTextContent('4,750.00');
    expect(finishBtn(d, /Approve .*4,750\.00 & record/)).toBeEnabled();
  });

  it('locks the adjustment inputs while Repeat shows, so an edit cannot resend the same id with other content', async () => {
    const decision = mockDecisionApi();
    decision.addAdjustment.mockResolvedValueOnce(failed(problem(503, 'ebilling.adjustment.incomplete', { detail: 'The line was saved but the totals were not.' })));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    await fillAdjustment(d);
    // Before the failure the inputs are editable.
    expect(within(d).getByLabelText('Adjustment value')).toBeEnabled();
    fireEvent.click(within(d).getByTestId('add-adjustment'));

    await waitFor(() => expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat'));
    expect(within(d).getByLabelText('Adjustment value')).toBeDisabled();
    expect(within(d).getByLabelText('Adjustment reason')).toBeDisabled();
    expect(within(d).getByLabelText('Adjustment rule')).toBeDisabled();
    expect(within(d).getByRole('radio', { name: 'Percent of fees', hidden: true })).toBeDisabled();
    expect(within(d).getByRole('radio', { name: 'Fixed amount', hidden: true })).toBeDisabled();
    // Clear is the way out: it drops the unsent form and mints a new id.
    fireEvent.click(within(d).getByRole('button', { name: 'Clear', hidden: true }));
    await waitFor(() => expect(within(d).getByLabelText('Adjustment value')).toBeEnabled());
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Add adjustment');
  });

  it('shows an error inside the step, keeps the form and the same id, and adds nothing twice, when an add is saved but its re-read fails', async () => {
    const decision = mockDecisionApi();
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    await fillAdjustment(d);
    // The add succeeds (200); the re-read of the review that follows fails.
    api.getReview.mockResolvedValueOnce(failed(problem(503, null)));
    fireEvent.click(within(d).getByTestId('add-adjustment'));

    expect(await within(d).findByTestId('adjustment-error')).toHaveTextContent('could not be read back from the server');
    // Not a cleared form: the typed values stay, locked, and the button is a Repeat of the same request.
    expect(within(d).getByLabelText('Adjustment value')).toHaveValue(5);
    expect(within(d).getByLabelText('Adjustment value')).toBeDisabled();
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Repeat');
    expect(decision.addAdjustment).toHaveBeenCalledTimes(1);

    fireEvent.click(within(d).getByTestId('add-adjustment'));
    await waitFor(() => expect(decision.addAdjustment).toHaveBeenCalledTimes(2));
    // The repeat carries the same id (the server answers "already applied"), never a new one.
    expect(decision.addAdjustment.mock.calls[1][1].adjustmentId).toBe(decision.addAdjustment.mock.calls[0][1].adjustmentId);
    // The second re-read works: the form clears and the error goes.
    await waitFor(() => expect(within(d).queryByTestId('adjustment-error')).not.toBeInTheDocument());
    expect(within(d).getByLabelText('Adjustment value')).toHaveValue(null);
    expect(within(d).getByTestId('add-adjustment')).toHaveTextContent('Add adjustment');
  });

  it('keeps the remove form open with an error when the removal is saved but the re-read fails', async () => {
    const decision = mockDecisionApi();
    const adj = line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy' });
    const { api } = renderDecisions([review([line(1), adj]), review([line(1)])], decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByTestId(`remove-adjustment-${lineId(90)}`));
    const confirm = await screen.findByRole('dialog', { name: 'Remove adjustment', hidden: true });
    api.getReview.mockResolvedValueOnce(failed(problem(503, null)));
    fireEvent.click(within(confirm).getByRole('button', { name: 'Remove', hidden: true }));

    expect(await within(confirm).findByTestId('remove-error')).toHaveTextContent('could not be read back from the server');
    expect(decision.removeAdjustment).toHaveBeenCalledTimes(1);
    // Remove again answers "already applied" and re-reads; the list then shows the server's state.
    fireEvent.click(within(confirm).getByRole('button', { name: 'Remove', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Remove adjustment', hidden: true })).not.toBeInTheDocument());
    await waitFor(() => expect(within(d).queryByTestId(`adjustment-${lineId(90)}`)).not.toBeInTheDocument());
    expect(decision.removeAdjustment).toHaveBeenCalledTimes(2);
  });

  it('removes an adjustment after a confirm form, through route 102', async () => {
    const decision = mockDecisionApi();
    const adj = line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy' });
    const base = review([line(1), adj], { totals: { billed: 5000, fees: 5000, expenses: 0, lineAdjustments: 0, invoiceAdjustments: -250, approved: 4750 } });
    const { api } = renderDecisions([base, review([line(1)])], decision);
    const d = await openWizard();
    fireEvent.click(next(d));

    fireEvent.click(await within(d).findByTestId(`remove-adjustment-${lineId(90)}`));
    const confirm = await screen.findByRole('dialog', { name: 'Remove adjustment', hidden: true });
    expect(decision.removeAdjustment).not.toHaveBeenCalled();
    fireEvent.click(within(confirm).getByRole('button', { name: 'Remove', hidden: true }));
    await waitFor(() => expect(decision.removeAdjustment).toHaveBeenCalledWith(INVOICE_ID, lineId(90)));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Remove adjustment', hidden: true })).not.toBeInTheDocument());
    await waitFor(() => expect(within(d).queryByTestId(`adjustment-${lineId(90)}`)).not.toBeInTheDocument());
    expect(api.getReview.mock.calls.length).toBeGreaterThan(1);
  });

  it('keeps the remove form open with the code when the removal is refused', async () => {
    const decision = mockDecisionApi();
    decision.removeAdjustment.mockResolvedValue(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const adj = line(90, { type: 'Adjustment', amount: -250, status: 'Accepted', statusLabel: 'Accepted', reason: 'Courtesy' });
    renderDecisions(review([line(1), adj]), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByTestId(`remove-adjustment-${lineId(90)}`));
    const confirm = await screen.findByRole('dialog', { name: 'Remove adjustment', hidden: true });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Remove', hidden: true }));
    expect(await within(confirm).findByTestId('remove-error')).toHaveTextContent('The invoice is no longer in review. (ebilling.invoice.not_in_review)');
    expect(within(d).getByTestId(`adjustment-${lineId(90)}`)).toBeInTheDocument();
  });

  it('disables Approve and says why while lines are open (I-2), but still offers Reject and Return', async () => {
    const decision = mockDecisionApi(approval({ canApprove: false, openLines: { pending: 0, flagged: 2, total: 2 } }));
    renderDecisions(review([flagged(1), flagged(2)]), decision);
    const d = await openWizard();
    expect(radio(d, /^Approve/)).toBeDisabled();
    expect(radio(d, /^Approve/)).not.toBeChecked();
    expect(within(d).getByText('Not yet: 2 lines to decide.')).toBeInTheDocument();
    expect(radio(d, /^Reject/)).toBeEnabled();
    expect(radio(d, /^Return to firm/)).toBeEnabled();
    expect(next(d)).toBeDisabled();
    fireEvent.click(radio(d, /^Return to firm/));
    expect(next(d)).toBeEnabled();
  });

  it('says that approving accepts the undecided clean lines as billed (D-72)', async () => {
    renderDecisions(clean(), mockDecisionApi(approval({ undecidedCleanLines: 2 })));
    const d = await openWizard();
    expect(within(d).getByText(/2 clean lines have no decision yet; approving accepts them as billed\./)).toBeInTheDocument();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    expect(await within(d).findByTestId('confirm-clean-note')).toHaveTextContent('approving accepts them as billed (accepted in bulk, not counted as reviewer labels)');
  });

  it('shows a 403 with its code and changes nothing on the page', async () => {
    const decision = mockDecisionApi();
    decision.approve.mockResolvedValue(failed(forbidden('ebilling.approver.not_eligible', { detail: 'You are not an approver for this invoice.' })));
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    await within(d).findByTestId('confirm-decision');
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    fireEvent.click(finishBtn(d, /Approve .*& record/));

    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('You are not an approver for this invoice. (ebilling.approver.not_eligible)');
    // Nothing changed client-side: still on the confirm step, no success screen, nothing re-read, the button still there.
    expect(within(d).getByTestId('confirm-decision')).toBeInTheDocument();
    expect(within(d).queryByTestId('wizard-done')).not.toBeInTheDocument();
    expect(api.getReview.mock.calls.length).toBe(reads);
    expect(decision.getApproval.mock.calls.length).toBe(approvals);
    expect(screen.getByTestId('decide-invoice')).toBeInTheDocument();
  });

  it('shows a 409 (open lines) with its code, and re-reads the page and the approval it is out of date with', async () => {
    const decision = mockDecisionApi();
    // InvoiceApprovalCodes.LinesOpen
    decision.approve.mockResolvedValue(failed(problem(409, 'ebilling.approve.lines_open', { detail: '1 line still needs a decision.' })));
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    await within(d).findByTestId('confirm-decision');
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    fireEvent.click(finishBtn(d, /Approve .*& record/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('1 line still needs a decision. (ebilling.approve.lines_open)');
    // A 409 means the page or the approval changed under the wizard: both are re-read, and the wizard stays on the Confirm step.
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    await waitFor(() => expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals));
    expect(within(d).getByTestId('confirm-decision')).toBeInTheDocument();
    expect(within(d).queryByTestId('wizard-done')).not.toBeInTheDocument();
  });

  it.each([['ebilling.approval.already_decided'], ['ebilling.approval.changed'], ['ebilling.approval.corrections_pending']])(
    're-reads the page and the approval on a 409 %s',
    async code => {
      const decision = mockDecisionApi();
      decision.approve.mockResolvedValue(failed(problem(409, code, { detail: 'The invoice changed.' })));
      const { api } = renderDecisions(clean(), decision);
      const d = await openWizard();
      fireEvent.click(next(d));
      fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
      await within(d).findByTestId('confirm-decision');
      const reads = api.getReview.mock.calls.length;
      const approvals = decision.getApproval.mock.calls.length;
      fireEvent.click(finishBtn(d, /Approve .*& record/));
      expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent(`The invoice changed. (${code})`);
      await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
      await waitFor(() => expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals));
    }
  );

  it("treats the approve route's own 503 (ebilling.decision.incomplete, with failedStep) as saved in part: re-reads both and says so", async () => {
    const decision = mockDecisionApi();
    // What InvoiceApprovalService.FromLineResult answers when the bulk accept stopped after some lines were written.
    decision.approve.mockResolvedValueOnce(
      failed(
        problem(503, 'ebilling.decision.incomplete', {
          detail: 'Some of the clean lines were accepted.',
          failedStep: 'accept-clean',
          committedLineIds: [lineId(1)],
        })
      )
    );
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    await within(d).findByTestId('confirm-decision');
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    fireEvent.click(finishBtn(d, /Approve .*& record/));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Some of the clean lines were accepted. (ebilling.decision.incomplete)');
    expect(alert).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals);
    // Finish again sends the same request.
    fireEvent.click(finishBtn(d, /Approve .*& record/));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledTimes(2));
    expect(await within(d).findByTestId('wizard-done')).toBeInTheDocument();
  });

  it('on a 503 that saved part of the approval, shows the server state and repeats the same request', async () => {
    const decision = mockDecisionApi();
    decision.approve.mockResolvedValueOnce(failed(problem(503, 'ebilling.approval.incomplete', { detail: 'The approval was recorded but the invoice did not close.' })));
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    await within(d).findByTestId('confirm-decision');
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(finishBtn(d, /Approve .*& record/));

    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    fireEvent.click(finishBtn(d, /Approve .*& record/));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledTimes(2));
    expect(decision.approve.mock.calls[1]).toEqual(decision.approve.mock.calls[0]);
    expect(await within(d).findByTestId('wizard-done')).toBeInTheDocument();
  });

  it('says the invoice was already approved when the server answers alreadyApproved', async () => {
    const decision = mockDecisionApi();
    decision.approve.mockResolvedValue(ok({ outcome: 'alreadyApproved', decisionId: 'd', decisionRecordId: 'r', state: null }));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(next(d));
    fireEvent.click(await within(d).findByRole('button', { name: 'Skip', hidden: true }));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(finishBtn(d, /Approve .*& record/));
    expect(await within(d).findByText('Already approved')).toBeInTheDocument();
    expect(within(d).getByText(/no second Decision Record was written/)).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Reject and Return
// ─────────────────────────────────────────────────────────────────────────────

describe('Reject and Return to firm', () => {
  it('rejects only with a reason, and sends the trimmed reason', async () => {
    const decision = mockDecisionApi();
    statefulApproval(decision, approval(), approval({ status: 'Rejected', canApprove: false, decidedBy: 'Pat Approver' }), ['reject']);
    renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(radio(d, /^Reject/));
    fireEvent.click(next(d));

    const reason = within(d).getByLabelText('Reason for the decision');
    expect(within(d).getByText(/Rejection is final/)).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    fireEvent.change(reason, { target: { value: '   ' } });
    expect(next(d)).toBeDisabled();
    fireEvent.change(reason, { target: { value: '  Duplicate of an earlier invoice  ' } });
    fireEvent.click(next(d));

    expect(await within(d).findByTestId('confirm-reason')).toHaveTextContent('Duplicate of an earlier invoice');
    fireEvent.click(finishBtn(d, /^Reject & record$/));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledWith(INVOICE_ID, 'Duplicate of an earlier invoice'));
    expect(await within(d).findByText('Rejected')).toBeInTheDocument();
    expect(decision.approve).not.toHaveBeenCalled();
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
  });

  it('returns to the firm with a reason', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(radio(d, /^Return to firm/));
    fireEvent.click(next(d));
    expect(within(d).queryByText(/Rejection is final/)).not.toBeInTheDocument();
    fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: 'Please correct the rates' } });
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(finishBtn(d, /^Return to firm & record$/));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledWith(INVOICE_ID, 'Please correct the rates'));
    expect(await within(d).findByText('Returned to firm')).toBeInTheDocument();
  });

  it('shows a refused reject (409 with its code), writes nothing and re-reads the page it is out of date with', async () => {
    const decision = mockDecisionApi();
    decision.reject.mockResolvedValue(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const { api } = renderDecisions(clean(), decision);
    const d = await openWizard();
    await toConfirmViaReject(d, 'Not payable');
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(finishBtn(d, /^Reject & record$/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('The invoice is no longer in review. (ebilling.invoice.not_in_review)');
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    expect(within(d).queryByTestId('wizard-done')).not.toBeInTheDocument();
  });

  it('shows a refused return without a detail as a fixed sentence with the code (403)', async () => {
    const decision = mockDecisionApi();
    decision.returnToFirm.mockResolvedValue(failed(forbidden('ebilling.approver.not_eligible')));
    renderDecisions(clean(), decision);
    const d = await openWizard();
    fireEvent.click(radio(d, /^Return to firm/));
    fireEvent.click(next(d));
    fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: 'Fix it' } });
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-decision');
    fireEvent.click(finishBtn(d, /^Return to firm & record$/));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('You do not have permission to do this. (ebilling.approver.not_eligible)');
  });

  it('has no notify step unless one is supplied', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    fireEvent.click(radio(d, /^Reject/));
    expect(within(d).queryByText('Notify firm')).not.toBeInTheDocument();
    fireEvent.click(next(d));
    fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: 'No' } });
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('confirm-decision')).toBeInTheDocument();
  });

  it('shows a supplied notify step on the reject and return path only, and it can be skipped', async () => {
    const notifyStep = {
      label: 'Notify firm',
      renderContent: () => <div data-testid="notify-stub">Stub</div>,
      canAdvance: () => false,
    };
    renderDecisions(clean(), mockDecisionApi(), { notifyStep });
    const d = await openWizard();

    // Approve path: no notify step.
    expect(radio(d, /^Approve/)).toBeChecked();
    expect(within(d).queryByText('Notify firm')).not.toBeInTheDocument();

    fireEvent.click(radio(d, /^Reject/));
    expect(within(d).getByText('Notify firm')).toBeInTheDocument();
    fireEvent.click(next(d));
    fireEvent.change(within(d).getByLabelText('Reason for the decision'), { target: { value: 'No' } });
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('notify-stub')).toBeInTheDocument();
    // canAdvance is false, but the slot is skippable.
    expect(next(d)).toBeDisabled();
    fireEvent.click(within(d).getByRole('button', { name: 'Skip', hidden: true }));
    expect(await within(d).findByTestId('confirm-decision')).toBeInTheDocument();
  });

  it('switching back to Approve drops the reason step again', async () => {
    renderDecisions(clean(), mockDecisionApi());
    const d = await openWizard();
    fireEvent.click(radio(d, /^Reject/));
    expect(within(d).getByText('Reason')).toBeInTheDocument();
    fireEvent.click(radio(d, /^Approve/));
    expect(within(d).queryByText('Reason')).not.toBeInTheDocument();
    expect(within(d).getByText('Invoice adjustment')).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Read-only reuse after a decision
// ─────────────────────────────────────────────────────────────────────────────

describe('the wizard reopened on a decided invoice', () => {
  const decidedReview = () => {
    const base = review([line(1, { status: 'Accepted', statusLabel: 'Accepted', approvedAmount: 1125 })]);
    base.header.status = 'Approved';
    base.decisionHistory = [
      decisionRecord({ recordId: 'rec-inv', number: 'DR-0042', actionCode: 'approve-invoice', subject: 'invoice', lineId: null, recordClass: 'Judgement' }),
    ];
    return base;
  };
  const decidedApproval = () =>
    approval({ status: 'Approved', canApprove: false, callerEligible: true, decidedBy: 'Pat Approver', decidedOn: '2026-11-03T09:00:00Z', approvedAmount: 5000 });

  it('offers View decision, not Decide, and opens the recorded decision with its status bar', async () => {
    const decision = mockDecisionApi(decidedApproval());
    renderDecisions(decidedReview(), decision);
    fireEvent.click(await screen.findByTestId('view-decision'));
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();

    const d = await screen.findByRole('dialog', { name: 'Invoice decision', hidden: true });
    const bar = within(d).getByTestId('status-bar');
    expect(bar).toHaveTextContent('Approved');
    expect(bar).toHaveTextContent('DR-0042');
    expect(bar).toHaveTextContent('by Pat Approver');

    // Nothing here can write: every choice is locked and the footer is Close, Back, Next.
    expect(radio(d, /^Approve/)).toBeChecked();
    expect(radio(d, /^Approve/)).toBeDisabled();
    expect(radio(d, /^Reject/)).toBeDisabled();
    expect(within(d).getByTestId('wizard-close')).toBeInTheDocument();
    expect(within(d).queryByRole('button', { name: /& record/, hidden: true })).not.toBeInTheDocument();
    expect(within(d).queryByRole('button', { name: 'Skip', hidden: true })).not.toBeInTheDocument();

    fireEvent.click(next(d));
    expect(await within(d).findByTestId('wizard-totals')).toBeInTheDocument();
    expect(within(d).queryByTestId('add-adjustment')).not.toBeInTheDocument();
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('confirm-decision')).toHaveTextContent('Approve');
    expect(within(d).getByText(/Recorded by Pat Approver/)).toBeInTheDocument();
    expect(within(d).queryByRole('button', { name: 'Next', hidden: true })).not.toBeInTheDocument();

    fireEvent.click(within(d).getByTestId('wizard-close'));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Invoice decision', hidden: true })).not.toBeInTheDocument());
    for (const w of [decision.approve, decision.reject, decision.returnToFirm, decision.addAdjustment, decision.removeAdjustment]) {
      expect(w).not.toHaveBeenCalled();
    }
  });

  it('shows a rejected invoice with its reason', async () => {
    const rej = review([line(1)]);
    rej.header.status = 'Rejected';
    rej.decisionHistory = [
      decisionRecord({ recordId: 'rec-r', number: 'DR-0050', actionCode: 'reject-invoice', subject: 'invoice', lineId: null, reason: 'Duplicate of an earlier invoice' }),
    ];
    renderDecisions(rej, mockDecisionApi(approval({ status: 'Rejected', canApprove: false, decidedBy: 'Pat Approver' })));
    fireEvent.click(await screen.findByTestId('view-decision'));
    const d = await screen.findByRole('dialog', { name: 'Invoice decision', hidden: true });
    expect(within(d).getByTestId('status-bar')).toHaveTextContent('Rejected');
    expect(within(d).getByTestId('status-bar')).toHaveTextContent('Reason: Duplicate of an earlier invoice');
    fireEvent.click(next(d));
    expect(await within(d).findByLabelText('Reason for the decision')).toHaveValue('Duplicate of an earlier invoice');
    expect(within(d).getByLabelText('Reason for the decision')).toHaveAttribute('readonly');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Blocked submission
// ─────────────────────────────────────────────────────────────────────────────

describe('the blocked-submission view', () => {
  const held = () => approval({ held: true, canApprove: false });

  it('explains the hold, lists the blocking findings, and offers Override and Return; Decide is not offered', async () => {
    const decision = mockDecisionApi(held());
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    expect(banner).toHaveTextContent('This invoice is on hold');
    await waitFor(() => expect(decision.getBlocked).toHaveBeenCalledWith(SUBMISSION_ID));
    expect(await within(banner).findByTestId('blocked-findings')).toHaveTextContent('DUP-INV');
    expect(within(banner).getByTestId('blocked-findings')).toHaveTextContent('Looks like a duplicate of an earlier invoice');
    expect(within(banner).getByTestId('override-block')).toBeInTheDocument();
    expect(within(banner).getByTestId('return-blocked')).toBeInTheDocument();
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
    expect(screen.getByTestId('awaiting')).toHaveAttribute('data-tone', 'warning');
  });

  it('has no banner for an invoice that is not held', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    await screen.findByTestId('awaiting');
    expect(screen.queryByTestId('blocked-banner')).not.toBeInTheDocument();
    expect(decision.getBlocked).not.toHaveBeenCalled();
  });

  it('hides Override from a caller the server says cannot override, and says why; Return stays', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok(blockedSubmission({ canOverride: false })));
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    expect(await within(banner).findByTestId('return-blocked')).toBeInTheDocument();
    expect(within(banner).queryByTestId('override-block')).not.toBeInTheDocument();
    expect(within(banner).getByTestId('no-override')).toHaveTextContent('eBilling Override privilege');
  });

  it('hides Return when the server says it is not available, and offers nothing when neither is', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok(blockedSubmission({ canOverride: false, canReturn: false })));
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    await waitFor(() => expect(decision.getBlocked).toHaveBeenCalled());
    await waitFor(() => expect(within(banner).getByTestId('no-override')).toBeInTheDocument());
    expect(within(banner).queryByTestId('return-blocked')).not.toBeInTheDocument();
    expect(within(banner).queryByRole('button')).not.toBeInTheDocument();
  });

  it('overrides only with a reason, sends the trimmed reason, and the hold clears from the re-read', async () => {
    const decision = mockDecisionApi(held());
    const { api } = renderDecisions([heldReview(), review([line(1)])], decision);
    fireEvent.click(await screen.findByTestId('override-block'));
    const dialog = await screen.findByRole('dialog', { name: 'Override the block', hidden: true });
    const submit = within(dialog).getByRole('button', { name: 'Override', hidden: true });
    expect(submit).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: '   ' } });
    expect(submit).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: '  Known resubmission  ' } });
    expect(submit).toBeEnabled();
    fireEvent.click(submit);

    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledWith(SUBMISSION_ID, 'Known resubmission'));
    expect(decision.returnBlocked).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByTestId('blocked-banner')).not.toBeInTheDocument());
    expect(api.getReview.mock.calls.length).toBeGreaterThan(1);
  });

  it('shows a refused override (403 with its code) in the form and changes nothing', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValue(failed(forbidden('ebilling.blocked.not_authorized', { detail: 'Overriding needs the eBilling Override privilege.' })));
    const { api } = renderDecisions(heldReview(), decision);
    fireEvent.click(await screen.findByTestId('override-block'));
    const dialog = await screen.findByRole('dialog', { name: 'Override the block', hidden: true });
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Known resubmission' } });
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));

    expect(await within(dialog).findByTestId('blocked-error')).toHaveTextContent('Overriding needs the eBilling Override privilege. (ebilling.blocked.not_authorized)');
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Known resubmission');
    expect(api.getReview.mock.calls.length).toBe(reads);
    expect(screen.getByTestId('blocked-banner')).toBeInTheDocument();
  });

  it('returns the submission to the firm with a reason, needing no privilege', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok(blockedSubmission({ canOverride: false })));
    renderDecisions([heldReview(), review([line(1)])], decision);
    fireEvent.click(await screen.findByTestId('return-blocked'));
    const dialog = await screen.findByRole('dialog', { name: 'Return to firm', hidden: true });
    const submit = within(dialog).getByRole('button', { name: 'Return', hidden: true });
    expect(submit).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Please resubmit without the duplicate' } });
    fireEvent.click(submit);
    await waitFor(() => expect(decision.returnBlocked).toHaveBeenCalledWith(SUBMISSION_ID, 'Please resubmit without the duplicate'));
    expect(decision.overrideBlocked).not.toHaveBeenCalled();
  });

  it('says the reasons could not be loaded and offers no decision when the blocked read fails, only Retry', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(failed(problem(500, null)));
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    expect(await within(banner).findByTestId('blocked-unavailable')).toHaveTextContent('could not be loaded');
    expect(within(banner).queryByTestId('override-block')).not.toBeInTheDocument();
    expect(within(banner).queryByTestId('return-blocked')).not.toBeInTheDocument();
    expect(within(banner).getAllByRole('button')).toHaveLength(1);
    expect(within(banner).getByTestId('blocked-retry')).toHaveTextContent('Retry');
  });

  it('retries a failed blocked read: Retry asks again and the findings and Override appear', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValueOnce(failed(problem(500, null)));
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    fireEvent.click(await within(banner).findByTestId('blocked-retry'));
    expect(await within(banner).findByTestId('blocked-findings')).toHaveTextContent('DUP-INV');
    expect(within(banner).getByTestId('override-block')).toBeInTheDocument();
    expect(within(banner).queryByTestId('blocked-unavailable')).not.toBeInTheDocument();
    expect(decision.getBlocked).toHaveBeenCalledTimes(2);
  });

  it('on 503 ebilling.blocked.unavailable (which can come after the decision stood) re-reads the page, the approval and the blocked view before showing the error', async () => {
    const decision = mockDecisionApi(held());
    decision.overrideBlocked.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'The decision was recorded but not every Signal closed.' }))
    );
    const { api } = renderDecisions(heldReview(), decision);
    fireEvent.click(await screen.findByTestId('override-block'));
    const dialog = await screen.findByRole('dialog', { name: 'Override the block', hidden: true });
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Known resubmission' } });
    await waitFor(() => expect(decision.getBlocked).toHaveBeenCalledTimes(1));
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));

    const error = await within(dialog).findByTestId('blocked-error');
    expect(error).toHaveTextContent('(ebilling.blocked.unavailable)');
    expect(error).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals);
    expect(decision.getBlocked).toHaveBeenCalledTimes(2);
    // Repeating the same request finishes it.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Override', hidden: true }));
    await waitFor(() => expect(decision.overrideBlocked).toHaveBeenCalledTimes(2));
    expect(decision.overrideBlocked.mock.calls[1]).toEqual(decision.overrideBlocked.mock.calls[0]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Panes: Context, Related work, History
// ─────────────────────────────────────────────────────────────────────────────

describe('Context pane', () => {
  it('reads the context when the tab opens and shows each available section', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    await screen.findByTestId('review-title');
    expect(decision.getContext).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('tab', { name: 'Context', hidden: true }));
    const tab = await screen.findByTestId('context-tab');
    expect(decision.getContext).toHaveBeenCalledWith(INVOICE_ID);
    expect(within(tab).getByTestId('ctx-matter-or-project')).toHaveTextContent('Example matter');
    expect(within(tab).getByTestId('ctx-budget')).toHaveTextContent('58,000.00');
    expect(within(tab).getByTestId('ctx-budget')).toHaveTextContent('42%');
    expect(within(tab).getByTestId('ctx-prior-invoices')).toHaveTextContent('INV-2025-77001');
    expect(within(tab).getByTestId('ctx-disallow-history')).toHaveTextContent('Rate above approved (3)');
    expect(within(tab).getByTestId('ctx-rate-history')).toHaveTextContent('A. Partner');
  });

  it('says in place which section is not tracked and which is unavailable, without hiding the others', async () => {
    const decision = mockDecisionApi();
    decision.getContext.mockResolvedValue(
      ok(
        invoiceContext({
          budget: { status: 'notTracked', data: null, code: null, message: null },
          rateHistory: { status: 'unavailable', data: null, code: 'ebilling.context.rate_history_unavailable', message: 'Rates could not be read.' },
          priorInvoices: { status: 'unavailable', data: null, code: null, message: null },
        })
      )
    );
    renderDecisions(clean(), decision);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'Context', hidden: true }));
    const tab = await screen.findByTestId('context-tab');
    expect(within(within(tab).getByTestId('ctx-budget')).getByTestId('ctx-notice')).toHaveTextContent('The budget is not tracked for this record.');
    expect(within(tab).getByTestId('ctx-budget')).toHaveAttribute('data-status', 'notTracked');
    expect(within(within(tab).getByTestId('ctx-rate-history')).getByTestId('ctx-notice')).toHaveTextContent(
      'Rates could not be read. (ebilling.context.rate_history_unavailable)'
    );
    expect(within(within(tab).getByTestId('ctx-prior-invoices')).getByTestId('ctx-notice')).toHaveTextContent('Prior invoices is not available right now.');
    // The sections that are available still show.
    expect(within(tab).getByTestId('ctx-matter-or-project')).toHaveTextContent('Example matter');
    expect(within(tab).getByTestId('ctx-disallow-history')).toHaveTextContent('Invoices considered');
  });

  it('shows a failed read with Retry', async () => {
    const decision = mockDecisionApi();
    decision.getContext.mockResolvedValueOnce(failed(problem(500, null)));
    renderDecisions(clean(), decision);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'Context', hidden: true }));
    const err = await screen.findByTestId('ctx-error');
    fireEvent.click(within(err).getByRole('button', { name: 'Retry', hidden: true }));
    expect(await screen.findByTestId('context-tab')).toBeInTheDocument();
    expect(decision.getContext).toHaveBeenCalledTimes(2);
  });
});

describe('Related work pane', () => {
  const pick = async (n: number) => {
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId(`line-row-${lineId(n)}`));
  };

  it('asks for a line first, and searches only when the reviewer asks', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'Related work', hidden: true }));
    expect(await screen.findByTestId('related-no-line')).toBeInTheDocument();
    expect(decision.getRelatedWork).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('tab', { name: 'Review', hidden: true }));
    await pick(2);
    fireEvent.click(screen.getByRole('tab', { name: 'Related work', hidden: true }));
    const tab = await screen.findByTestId('related-work-tab');
    expect(decision.getRelatedWork).not.toHaveBeenCalled(); // on demand (107 latency)
    fireEvent.click(within(tab).getByTestId('find-related'));
    await waitFor(() => expect(decision.getRelatedWork).toHaveBeenCalledWith(INVOICE_ID, lineId(2)));
    expect(await within(tab).findByTestId('related-items')).toHaveTextContent('Draft agreement.docx');
    expect(within(tab).getByTestId('find-related')).toHaveTextContent('Search again');
  });

  it('says why an empty answer is empty, in words', async () => {
    const decision = mockDecisionApi();
    decision.getRelatedWork.mockResolvedValue(
      ok({ invoiceId: INVOICE_ID, lineId: lineId(1), status: 'empty', reason: 'none-in-window', code: null, message: null, windowDays: 7, truncated: false, items: [] })
    );
    renderDecisions(clean(), decision);
    await pick(1);
    fireEvent.click(screen.getByRole('tab', { name: 'Related work', hidden: true }));
    fireEvent.click(await screen.findByTestId('find-related'));
    expect(await screen.findByTestId('related-empty')).toHaveTextContent('No documents by this timekeeper on this record within 7 days of the line date.');
  });

  it('shows the server message and code when the search is unavailable', async () => {
    const decision = mockDecisionApi();
    decision.getRelatedWork.mockResolvedValue(
      ok({ invoiceId: INVOICE_ID, lineId: lineId(1), status: 'unavailable', reason: null, code: 'ebilling.related.search_unavailable', message: 'Search is not available.', windowDays: null, truncated: false, items: [] })
    );
    renderDecisions(clean(), decision);
    await pick(1);
    fireEvent.click(screen.getByRole('tab', { name: 'Related work', hidden: true }));
    fireEvent.click(await screen.findByTestId('find-related'));
    expect(await screen.findByTestId('related-unavailable')).toHaveTextContent('Search is not available. (ebilling.related.search_unavailable)');
  });

  it('shows a failed search with the server code, and a different line starts again', async () => {
    const decision = mockDecisionApi();
    decision.getRelatedWork.mockResolvedValueOnce(failed(problem(403, 'ebilling.related.forbidden', { detail: 'No access to the record.' })));
    renderDecisions(clean(), decision);
    await pick(1);
    fireEvent.click(screen.getByRole('tab', { name: 'Related work', hidden: true }));
    fireEvent.click(await screen.findByTestId('find-related'));
    expect(await screen.findByTestId('related-error')).toHaveTextContent('No access to the record. (ebilling.related.forbidden)');

    fireEvent.click(screen.getByRole('tab', { name: 'Review', hidden: true }));
    await pick(2);
    fireEvent.click(screen.getByRole('tab', { name: 'Related work', hidden: true }));
    const tab = await screen.findByTestId('related-work-tab');
    expect(within(tab).queryByTestId('related-error')).not.toBeInTheDocument();
    expect(within(tab).getByTestId('find-related')).toHaveTextContent('Find related work');
  });
});

describe('History pane', () => {
  it('lists the Decision Records newest first with the eBilling words', async () => {
    const records = [
      decisionRecord({ recordId: 'r1', number: 'DR-0001', actionCode: 'accept-line', lineId: lineId(1), decidedOn: '2026-11-01T09:00:00Z' }),
      decisionRecord({ recordId: 'r2', number: 'DR-0002', actionCode: 'reduce-line', lineId: lineId(2), decidedOn: '2026-11-02T09:00:00Z', reason: 'Rate above approved' }),
      decisionRecord({ recordId: 'r3', number: 'DR-0003', actionCode: 'disallow-line', lineId: lineId(1), decidedOn: '2026-11-03T09:00:00Z', recordClass: 'Judgement' }),
    ];
    renderDecisions(review([line(1), line(2)], { decisionHistory: records }), mockDecisionApi());
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'History', hidden: true }));
    const tab = await screen.findByTestId('history-tab');
    const rows = within(tab).getAllByTestId('record-row');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('Line 1 disallowed');
    expect(rows[0]).toHaveTextContent('Disallowed');
    expect(rows[0]).toHaveTextContent('DR-0003');
    expect(rows[1]).toHaveTextContent('Line 2 reduced');
    expect(rows[2]).toHaveTextContent('Line 1 accepted');
    // The row is append-only: its only link is the disclosure, no edit or delete control.
    expect(within(tab).queryByRole('button', { name: /edit|delete|remove/i, hidden: true })).not.toBeInTheDocument();
  });

  it('says so when nothing is recorded', async () => {
    renderDecisions(clean(), mockDecisionApi());
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'History', hidden: true }));
    expect(await screen.findByTestId('history-empty')).toHaveTextContent('No decisions or corrections are recorded on this invoice yet.');
  });

  it('lists corrections with the decisions, newest first, with the value as read and the final value', async () => {
    const records = [decisionRecord({ recordId: 'r1', number: 'DR-0001', actionCode: 'accept-line', lineId: lineId(1), decidedOn: '2026-11-01T09:00:00Z' })];
    const { api } = renderDecisions(review([line(1), line(2)], { decisionHistory: records }), mockDecisionApi());
    api.listCorrections.mockResolvedValue(
      ok([
        { id: 'c1', lineId: lineId(2), field: 'Hours', aiValue: '25', finalValue: '2.5', outcome: 'Corrected', isLabel: true, aiConfidence: 0.4, reviewedOn: '2026-11-02T09:00:00Z' },
        { id: 'c2', lineId: null, field: 'FirmInvoiceNumber', aiValue: 'INV-1', finalValue: 'INV-2', outcome: 'Corrected', isLabel: true, aiConfidence: null, reviewedOn: '2026-11-03T09:00:00Z' },
      ])
    );
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'History', hidden: true }));
    const tab = await screen.findByTestId('history-tab');
    await waitFor(() => expect(within(tab).getAllByTestId('record-row')).toHaveLength(3));
    const rows = within(tab).getAllByTestId('record-row');
    expect(rows[0]).toHaveTextContent('Invoice · Firm invoice number corrected');
    expect(rows[1]).toHaveTextContent('Line 2 · Hours corrected');
    expect(rows[1]).toHaveTextContent('Corrected');
    expect(rows[2]).toHaveTextContent('Line 1 accepted');
    expect(tab.querySelectorAll('[data-kind="correction"]')).toHaveLength(2);
    expect(api.listCorrections).toHaveBeenCalledWith(INVOICE_ID);
  });

  it('still lists the decisions, and says so, when the corrections cannot be read', async () => {
    const records = [decisionRecord({ recordId: 'r1', number: 'DR-0001', actionCode: 'accept-line', lineId: lineId(1) })];
    const { api } = renderDecisions(review([line(1)], { decisionHistory: records }), mockDecisionApi());
    api.listCorrections.mockResolvedValue(failed(problem(500, null)));
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'History', hidden: true }));
    expect(await screen.findByTestId('history-corrections-failed')).toHaveTextContent('only the Decision Records are listed');
    expect(screen.getAllByTestId('record-row')).toHaveLength(1);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Dark theme and source guards
// ─────────────────────────────────────────────────────────────────────────────

describe('dark theme', () => {
  it('opens the wizard and the blocked form under the dark theme with the same content', async () => {
    const decision = mockDecisionApi();
    renderDecisions(clean(), decision, { theme: webDarkTheme });
    const d = await openWizard();
    expect(radio(d, /^Approve/)).toBeChecked();
    expect(within(d).getByTestId('wizard-totals')).toBeInTheDocument();
  });

  it('shows the blocked banner and its form under the dark theme', async () => {
    const decision = mockDecisionApi(approval({ held: true, canApprove: false }));
    renderDecisions(heldReview(), decision, { theme: webDarkTheme });
    fireEvent.click(await screen.findByTestId('override-block'));
    expect(await screen.findByRole('dialog', { name: 'Override the block', hidden: true })).toBeInTheDocument();
  });
});

describe('source guards (task 111)', () => {
  const srcRoot = path.resolve(__dirname, '..');
  // The same walker task 110's colour guard uses (testKit.listSourceFiles).
  const rel = listSourceFiles().map(f => path.relative(srcRoot, f).replace(/\\/g, '/'));

  it("task 110's colour-literal guard walks the decision and tab folders (it is not vacuous for task 111)", () => {
    // The guard in InvoiceReviewPage.test.tsx walks the page with testKit.listSourceFiles: the very function asserted here, not a copy of it.
    const guarded = rel;
    for (const f of [
      'components/decision/steps.tsx',
      'components/decision/InvoiceDecisionWizard.tsx',
      'components/decision/BlockedBanner.tsx',
      'components/tabs/ContextTab.tsx',
      'components/tabs/HistoryTab.tsx',
      'components/decision/ApprovalProvider.tsx',
      'components/tabs/RelatedWorkTab.tsx',
      'services/decisionApi.ts',
      'services/decisionModel.ts',
    ]) {
      expect(guarded).toContain(f);
    }
    // Nothing is skipped but the tests.
    expect(guarded.some(f => f.includes('__tests__'))).toBe(false);
  });

  it('takes nothing from the prototype and calls no Dataverse route or table directly', () => {
    for (const f of listSourceFiles()) {
      const text = fs.readFileSync(f, 'utf8');
      expect(text).not.toMatch(/spaarke-prototype/);
      expect(text).not.toMatch(/\/api\/data\/v9|Xrm\.WebApi/);
    }
  });

  it('decides nothing the server owns: no total, amount or eligibility is computed in the decision files', () => {
    const decisionFiles = rel.filter(f => /^(components\/decision|components\/tabs)\//.test(f));
    expect(decisionFiles.length).toBeGreaterThan(5);
    for (const f of decisionFiles) {
      const text = fs.readFileSync(path.join(srcRoot, f), 'utf8');
      // No arithmetic on money fields.
      expect(text).not.toMatch(/\.(approved|billed|amount|approvedAmount|invoiceAdjustments)\s*[-+*/]\s*[\w(]/);
      expect(text).not.toMatch(/reduce\(\s*\(?\s*\w+\s*,\s*\w+\s*\)?\s*=>\s*\w+\s*\+/);
    }
  });
});
