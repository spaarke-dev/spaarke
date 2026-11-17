/**
 * ISS-034 (#2888), task iss034b: Invoice Review offers "Finish as operator" on a stranded blocked decision (EB-D-244).
 *
 * A blocked submission's Override or Return that another person recorded and did not finish holds the invoice. The server says who may
 * finish it (`canFinishAsOperator` and a `strandedDecision` the caller did not record); the page only shows what the server says. Through the
 * real page, blocked banner and form with mocked BFF clients (the harness of noteReviewRound1Decisions.test.tsx).
 */

import '@testing-library/jest-dom';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import { finishIntro } from '../components/decision/BlockedBanner';
import { BLOCKED_REFUSAL_SENTENCES, createInvoiceDecisionApi, decisionFailureMessage, decisionRoutes, isFinishStaleRefusal } from '../services/decisionApi';
import { toFailure, type InvoiceReview } from '../services/invoiceReviewApi';
import {
  INVOICE_ID,
  STRANDED_RECORD_ID,
  SUBMISSION_ID,
  approval,
  blockedSubmission,
  failed,
  forbidden,
  heldReview,
  line,
  mockApi,
  mockDecisionApi,
  ok,
  problem,
  review,
  strandedSubmission,
  type MockDecisionApi,
} from './testKit';

function renderDecisions(reviews: InvoiceReview | InvoiceReview[], decision: MockDecisionApi, theme = webLightTheme) {
  const api = mockApi(reviews);
  render(
    <FluentProvider theme={theme}>
      <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={api as never} decisionApi={decision as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
  return { api };
}

const held = () => approval({ held: true, canApprove: false });
const FINISH = { name: 'Finish as operator', hidden: true } as const;
const dialogOf = (name = 'Finish as operator') => screen.findByRole('dialog', { name, hidden: true });
const reasonOf = (d: HTMLElement) => within(d).getByLabelText('Reason') as HTMLTextAreaElement;
const noteOf = (d: HTMLElement) => within(d).getByLabelText('Note') as HTMLTextAreaElement;
const submitBtn = (d: HTMLElement, name = 'Finish') => within(d).getByRole('button', { name, hidden: true });
const cancel = (d: HTMLElement) => fireEvent.click(within(d).getByRole('button', { name: 'Cancel', hidden: true }));
const noAnswer = () => failed(new Error('Failed to fetch'));

/** A decision API whose blocked read says another person's decision is stranded and the caller may finish it. */
function strandedApi(verb: 'Override' | 'Return' = 'Override') {
  const decision = mockDecisionApi(held());
  decision.getBlocked.mockResolvedValue(ok(strandedSubmission(verb)));
  return decision;
}

async function openFinish(decision: MockDecisionApi, reviews: InvoiceReview | InvoiceReview[] = [heldReview(), heldReview()]) {
  const { api } = renderDecisions(reviews, decision);
  fireEvent.click(await screen.findByTestId('finish-stranded'));
  const dialog = await dialogOf();
  return { dialog, api };
}

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// When it shows
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish as operator: shown only on the server\'s word', () => {
  it('shows for a stranded decision the server says the caller may finish, with Override and Return hidden when the server hides them', async () => {
    const decision = strandedApi();
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    expect(await within(banner).findByTestId('finish-stranded')).toHaveTextContent('Finish as operator');
    expect(within(banner).queryByTestId('override-block')).not.toBeInTheDocument();
    expect(within(banner).queryByTestId('return-blocked')).not.toBeInTheDocument();
  });

  it('shows for a stranded Return too', async () => {
    renderDecisions(heldReview(), strandedApi('Return'));
    expect(await screen.findByTestId('finish-stranded')).toBeInTheDocument();
  });

  it.each([
    ['the server does not say the caller may finish (a non-admin)', strandedSubmission('Override', { canFinishAsOperator: false })],
    ['the server leaves the flag out (an older server)', (() => { const b = strandedSubmission(); delete b.canFinishAsOperator; return b; })()],
    ['the server names no stranded decision', strandedSubmission('Override', { strandedDecision: null })],
    ['the server leaves the decision out (an older server)', (() => { const b = strandedSubmission(); delete b.strandedDecision; return b; })()],
    ['the caller is the stored decider', strandedSubmission('Override', { strandedDecision: { verb: 'Override', recordId: STRANDED_RECORD_ID, callerIsDecider: true } })],
    ['the caller is the stored decider of a Return', strandedSubmission('Return', { strandedDecision: { verb: 'Return', recordId: STRANDED_RECORD_ID, callerIsDecider: true } })],
  ])('(must-not-fire) is not offered when %s', async (_, blocked) => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok({ ...blocked, canOverride: true, canReturn: true }));
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    await within(banner).findByTestId('override-block');
    expect(within(banner).queryByTestId('finish-stranded')).not.toBeInTheDocument();
  });

  it('(must-not-fire) an ordinary blocked submission offers Override and Return and no finish', async () => {
    renderDecisions(heldReview(), mockDecisionApi(held()));
    const banner = await screen.findByTestId('blocked-banner');
    expect(await within(banner).findByTestId('override-block')).toBeInTheDocument();
    expect(within(banner).getByTestId('return-blocked')).toBeInTheDocument();
    expect(within(banner).queryByTestId('finish-stranded')).not.toBeInTheDocument();
  });

  it('(must-not-fire) a failed blocked read offers no finish, only Retry', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(failed(problem(500, null)));
    renderDecisions(heldReview(), decision);
    const banner = await screen.findByTestId('blocked-banner');
    await within(banner).findByTestId('blocked-retry');
    expect(within(banner).queryByTestId('finish-stranded')).not.toBeInTheDocument();
  });

  it('(must-not-fire) an invoice that is not held has no banner and no finish', async () => {
    const decision = strandedApi();
    renderDecisions(review([line(1)]), decision);
    await screen.findByTestId('review-title');
    expect(screen.queryByTestId('finish-stranded')).not.toBeInTheDocument();
    expect(decision.getBlocked).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// The dialog
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish as operator: the dialog', () => {
  it('names a pending Override recorded by another person and says it finishes exactly as recorded', async () => {
    const { dialog } = await openFinish(strandedApi('Override'));
    const text = within(dialog).getByTestId('finish-stranded-intro');
    expect(text).toHaveTextContent('Another person recorded an override of this submission');
    expect(text).toHaveTextContent('exactly as recorded');
    expect(text).toHaveTextContent('released into the normal review');
    expect(submitBtn(dialog)).toBeDisabled();
  });

  it('names a pending Return recorded by another person', async () => {
    const { dialog } = await openFinish(strandedApi('Return'));
    const text = within(dialog).getByTestId('finish-stranded-intro');
    expect(text).toHaveTextContent('Another person recorded a return of this submission to the firm');
    expect(text).toHaveTextContent('exactly as recorded');
    expect(text).toHaveTextContent('goes back to the firm');
  });

  it('(control) the generic wording is used only for a verb the page does not know', () => {
    expect(finishIntro('Override')).toMatch(/override/);
    expect(finishIntro('Return')).toMatch(/return/);
    expect(finishIntro(null)).toMatch(/Another person's decision/);
    expect(finishIntro(null)).not.toMatch(/override|return/);
  });

  it('needs a reason (blank does not count), sends the trimmed reason with no note key, and refreshes: the hold clears from the re-read', async () => {
    const decision = strandedApi();
    const { dialog, api } = await openFinish(decision, [heldReview(), review([line(1)])]);
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.change(reasonOf(dialog), { target: { value: '   ' } });
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.change(reasonOf(dialog), { target: { value: '  The decider has left the firm  ' } });
    expect(submitBtn(dialog)).toBeEnabled();
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(submitBtn(dialog));

    await waitFor(() => expect(decision.finishStranded).toHaveBeenCalledTimes(1));
    expect(decision.finishStranded).toHaveBeenCalledWith(SUBMISSION_ID, 'The decider has left the firm');
    expect(decision.overrideBlocked).not.toHaveBeenCalled();
    expect(decision.returnBlocked).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByTestId('blocked-banner')).not.toBeInTheDocument());
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
  });

  it('sends the note as typed when there is one, and refreshes the review, the approval and the blocked view', async () => {
    const decision = strandedApi();
    const { dialog, api } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'Raised with the billing team' } });
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    const blockedReads = decision.getBlocked.mock.calls.length;
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.finishStranded).toHaveBeenCalledWith(SUBMISSION_ID, 'The decider has left the firm', 'Raised with the billing team'));
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    await waitFor(() => expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals));
    await waitFor(() => expect(decision.getBlocked.mock.calls.length).toBeGreaterThan(blockedReads));
  });

  it('a blank note is no note (the key is left out), and a note over the limit blocks the send', async () => {
    const decision = strandedApi();
    const { dialog } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'x'.repeat(2001) } });
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.change(noteOf(dialog), { target: { value: '   ' } });
    expect(submitBtn(dialog)).toBeEnabled();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.finishStranded).toHaveBeenCalledTimes(1));
    expect(decision.finishStranded.mock.calls[0]).toEqual([SUBMISSION_ID, 'The decider has left the firm']);
  });

  it('a server-refused note (400) shows under the Note box, keeps the form editable and does not lock it', async () => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(failed(problem(400, 'ebilling.note_invalid_characters', { detail: 'The note holds a control character.' })));
    const { dialog } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'Bad note' } });
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(within(within(dialog).getByTestId('blocked-note-field')).getByText(/control character/)).toBeInTheDocument());
    expect(within(dialog).queryByTestId('blocked-error')).not.toBeInTheDocument();
    expect(reasonOf(dialog)).not.toHaveAttribute('readonly');
  });

  it('works under the dark theme with the same content', async () => {
    const decision = strandedApi();
    renderDecisions(heldReview(), decision, webDarkTheme);
    fireEvent.click(await screen.findByTestId('finish-stranded'));
    const dialog = await dialogOf();
    expect(within(dialog).getByTestId('finish-stranded-intro')).toHaveTextContent('exactly as recorded');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Refusals, by errorCode
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish as operator: each refusal shows its message', () => {
  // [label, failure, the sentence shown when the server sends no detail, the code shown with it]
  const refusals: Array<[string, () => ReturnType<typeof failed>, string, string]> = [
    ['403 operator_not_permitted', () => failed(forbidden('ebilling.blocked.operator_not_permitted')), "You cannot finish another person's decision on this submission.", 'ebilling.blocked.operator_not_permitted'],
    ['409 nothing_stranded', () => failed(problem(409, 'ebilling.blocked.nothing_stranded')), 'No recorded override or return of this submission is waiting to be finished.', 'ebilling.blocked.nothing_stranded'],
    ['409 both_decisions_recorded', () => failed(problem(409, 'ebilling.blocked.both_decisions_recorded')), 'Contact support.', 'ebilling.blocked.both_decisions_recorded'],
    ['409 own_decision_pending', () => failed(problem(409, 'ebilling.blocked.own_decision_pending')), 'Repeat your own decision to finish it', 'ebilling.blocked.own_decision_pending'],
    ['503 record_unavailable', () => failed(problem(503, 'ebilling.blocked.record_unavailable')), 'Nothing was changed. Try again.', 'ebilling.blocked.record_unavailable'],
    ['409 already_overridden', () => failed(problem(409, 'ebilling.blocked.already_overridden')), 'already overridden', 'ebilling.blocked.already_overridden'],
    ['409 already_returned', () => failed(problem(409, 'ebilling.blocked.already_returned')), 'already returned', 'ebilling.blocked.already_returned'],
    ['403 not_authorized (FR-51)', () => failed(forbidden('ebilling.blocked.not_authorized')), 'rights to close the signals', 'ebilling.blocked.not_authorized'],
    ['409 decision_conflict', () => failed(problem(409, 'ebilling.blocked.decision_conflict')), 'different decision is already recorded', 'ebilling.blocked.decision_conflict'],
    ['409 other_decision_pending', () => failed(problem(409, 'ebilling.blocked.other_decision_pending')), 'recorded but has not finished', 'ebilling.blocked.other_decision_pending'],
  ];

  it.each(refusals)('%s: the dialog stays open with the sentence and the code, the reason is kept, and nothing is locked', async (_, refusal, sentence, code) => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(refusal());
    const { dialog } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'A note' } });
    fireEvent.click(submitBtn(dialog));
    const error = await within(dialog).findByTestId('blocked-error');
    expect(error).toHaveTextContent(sentence);
    expect(error).toHaveTextContent(`(${code})`);
    expect(reasonOf(dialog)).toHaveValue('The decider has left the firm');
    expect(reasonOf(dialog)).not.toHaveAttribute('readonly');
    expect(noteOf(dialog)).not.toHaveAttribute('readonly');
    expect(within(dialog).queryByTestId('blocked-note-locked')).not.toBeInTheDocument();
    expect(dialog).toBeInTheDocument();
  });

  it('the server\'s own sentence wins over the fixed one, and is shown with the code', async () => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.nothing_stranded', { detail: 'Nothing is waiting here.' })));
    const { dialog } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    fireEvent.click(submitBtn(dialog));
    const error = await within(dialog).findByTestId('blocked-error');
    expect(error).toHaveTextContent('Nothing is waiting here. (ebilling.blocked.nothing_stranded)');
    expect(error).not.toHaveTextContent('No recorded override or return');
  });

  it.each([
    ['403 operator_not_permitted', () => failed(forbidden('ebilling.blocked.operator_not_permitted'))],
    ['409 nothing_stranded', () => failed(problem(409, 'ebilling.blocked.nothing_stranded'))],
    ['409 own_decision_pending', () => failed(problem(409, 'ebilling.blocked.own_decision_pending'))],
    ['409 both_decisions_recorded', () => failed(problem(409, 'ebilling.blocked.both_decisions_recorded'))],
    ['409 already_overridden', () => failed(problem(409, 'ebilling.blocked.already_overridden'))],
    ['409 already_returned', () => failed(problem(409, 'ebilling.blocked.already_returned'))],
  ])('%s says the page is out of date: the review, the approval and the blocked view are re-read', async (_, refusal) => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(refusal());
    const { dialog, api } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    const reads = api.getReview.mock.calls.length;
    const approvals = decision.getApproval.mock.calls.length;
    const blockedReads = decision.getBlocked.mock.calls.length;
    fireEvent.click(submitBtn(dialog));
    await within(dialog).findByTestId('blocked-error');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    expect(decision.getApproval.mock.calls.length).toBeGreaterThan(approvals);
    expect(decision.getBlocked.mock.calls.length).toBeGreaterThan(blockedReads);
  });

  it.each([
    ['503 record_unavailable', () => failed(problem(503, 'ebilling.blocked.record_unavailable'))],
    ['403 not_authorized (FR-51)', () => failed(forbidden('ebilling.blocked.not_authorized'))],
    ['409 decision_conflict', () => failed(problem(409, 'ebilling.blocked.decision_conflict'))],
  ])('(must-not-fire) %s is just an error: no extra re-read', async (_, refusal) => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(refusal());
    const { dialog, api } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    const reads = api.getReview.mock.calls.length;
    const blockedReads = decision.getBlocked.mock.calls.length;
    fireEvent.click(submitBtn(dialog));
    await within(dialog).findByTestId('blocked-error');
    expect(api.getReview.mock.calls.length).toBe(reads);
    expect(decision.getBlocked.mock.calls.length).toBe(blockedReads);
  });

  it('(control) the fixed sentences and the stale-state check are exactly what the tests above assume', () => {
    const f = (status: number, errorCode: string | null) => toFailure(problem(status, errorCode));
    expect(decisionFailureMessage(f(409, 'ebilling.blocked.nothing_stranded'))).toBe(`${BLOCKED_REFUSAL_SENTENCES['ebilling.blocked.nothing_stranded']} (ebilling.blocked.nothing_stranded)`);
    // A code with no fixed sentence keeps the per-status fallback.
    expect(decisionFailureMessage(f(409, 'ebilling.blocked.in_progress'))).toBe('The invoice changed or cannot be changed now. Close this and reload the page. (ebilling.blocked.in_progress)');
    expect(decisionFailureMessage(f(403, null))).toBe('You do not have permission to do this.');
    expect(decisionFailureMessage(f(409, 'constructor'))).toBe('The invoice changed or cannot be changed now. Close this and reload the page. (constructor)');
    expect(isFinishStaleRefusal(f(409, 'ebilling.blocked.nothing_stranded'))).toBe(true);
    expect(isFinishStaleRefusal(f(403, 'ebilling.blocked.operator_not_permitted'))).toBe(true);
    // The same code on another status is not it, nor is an ordinary refusal.
    expect(isFinishStaleRefusal(f(500, 'ebilling.blocked.nothing_stranded'))).toBe(false);
    expect(isFinishStaleRefusal(f(409, 'ebilling.blocked.decision_conflict'))).toBe(false);
    expect(isFinishStaleRefusal(f(503, 'ebilling.blocked.record_unavailable'))).toBe(false);
    expect(isFinishStaleRefusal(f(409, null))).toBe(false);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// An unknown outcome locks the dialog (the decisionLocks pattern)
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish as operator: an unsure send locks the dialog, and the repeat is the same request', () => {
  async function sendFinish(decision: MockDecisionApi) {
    const { dialog } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'The first note' } });
    fireEvent.click(submitBtn(dialog));
    return dialog;
  }

  it.each([
    ['saved in part (503)', () => failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'The decision was recorded but not every Signal closed.' }))],
    ['a 409 concurrent_change (a record may exist)', () => failed(problem(409, 'ebilling.blocked.concurrent_change', { detail: 'The invoice changed.' }))],
    ['no answer at all', () => noAnswer()],
  ])('a send %s locks the reason and the note, reopening shows them locked, and the repeat is identical', async (_, fail) => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(fail());
    const dialog = await sendFinish(decision);
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).toHaveAttribute('readonly');
    expect(noteOf(dialog)).toHaveAttribute('readonly');
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', FINISH)).not.toBeInTheDocument());

    fireEvent.click(await screen.findByTestId('finish-stranded'));
    const again = await dialogOf();
    expect(reasonOf(again).value).toBe('The decider has left the firm');
    expect(noteOf(again).value).toBe('The first note');
    expect(reasonOf(again)).toHaveAttribute('readonly');
    expect(within(again).getByTestId('blocked-note-locked')).toBeInTheDocument();
    fireEvent.click(submitBtn(again));
    await waitFor(() => expect(decision.finishStranded).toHaveBeenCalledTimes(2));
    expect(decision.finishStranded.mock.calls[1]).toEqual(decision.finishStranded.mock.calls[0]);
    // The repeat finished it (the mock's default answer): the lock goes with it.
    await waitFor(() => expect(screen.queryByRole('dialog', FINISH)).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('finish-stranded'));
    const fresh = await dialogOf();
    expect(reasonOf(fresh).value).toBe('');
    expect(reasonOf(fresh)).not.toHaveAttribute('readonly');
  });

  it('an unsure send re-reads the page before showing the error (the finish may have stood)', async () => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })));
    const { dialog, api } = await openFinish(decision);
    fireEvent.change(reasonOf(dialog), { target: { value: 'The decider has left the firm' } });
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(submitBtn(dialog));
    const error = await within(dialog).findByTestId('blocked-error');
    expect(error).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
  });

  it('a locked finish makes Override and Return warn and refuse, and the lock is kept', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok(strandedSubmission('Override', { canOverride: true, canReturn: true })));
    decision.finishStranded.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })));
    const dialog = await sendFinish(decision);
    await within(dialog).findByTestId('blocked-error');
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', FINISH)).not.toBeInTheDocument());

    fireEvent.click(await screen.findByTestId('override-block'));
    const other = await dialogOf('Override the block');
    expect(within(other).getByTestId('blocked-other-lock')).toHaveTextContent('A finish may have been saved in part. Submit it again to finish it before overriding.');
    fireEvent.change(reasonOf(other), { target: { value: 'Known resubmission' } });
    expect(within(other).getByRole('button', { name: 'Override', hidden: true })).toBeDisabled();
    cancel(other);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());

    fireEvent.click(await screen.findByTestId('return-blocked'));
    const ret = await dialogOf('Return to firm');
    expect(within(ret).getByTestId('blocked-other-lock')).toHaveTextContent('A finish may have been saved in part. Submit it again to finish it before returning.');
    expect(decision.overrideBlocked).not.toHaveBeenCalled();
    expect(decision.returnBlocked).not.toHaveBeenCalled();
  });

  it('an unfinished Override or Return makes the finish warn and refuse', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok(strandedSubmission('Override', { canOverride: true, canReturn: true })));
    decision.overrideBlocked.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })));
    renderDecisions([heldReview(), heldReview()], decision);
    fireEvent.click(await screen.findByTestId('override-block'));
    const first = await dialogOf('Override the block');
    fireEvent.change(reasonOf(first), { target: { value: 'Known resubmission' } });
    fireEvent.click(within(first).getByRole('button', { name: 'Override', hidden: true }));
    await within(first).findByTestId('blocked-error');
    cancel(first);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Override the block', hidden: true })).not.toBeInTheDocument());

    fireEvent.click(await screen.findByTestId('finish-stranded'));
    const other = await dialogOf();
    expect(within(other).getByTestId('blocked-other-lock')).toHaveTextContent('An Override may have been saved in part. Submit it again to finish it before finishing as operator.');
    expect(reasonOf(other).value).toBe('');
    fireEvent.change(reasonOf(other), { target: { value: 'The decider has left the firm' } });
    expect(submitBtn(other)).toBeDisabled();
    expect(decision.finishStranded).not.toHaveBeenCalled();
  });

  it('a repeat of the locked finish answered with a definite refusal clears the lock, so Override can be sent again', async () => {
    const decision = mockDecisionApi(held());
    decision.getBlocked.mockResolvedValue(ok(strandedSubmission('Override', { canOverride: true, canReturn: true })));
    decision.finishStranded
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' })))
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.nothing_stranded')));
    const dialog = await sendFinish(decision);
    await waitFor(() => expect(reasonOf(dialog)).toHaveAttribute('readonly'));
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.finishStranded).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(reasonOf(dialog)).not.toHaveAttribute('readonly'));
    expect(within(dialog).getByTestId('blocked-error')).toHaveTextContent('(ebilling.blocked.nothing_stranded)');
    cancel(dialog);
    await waitFor(() => expect(screen.queryByRole('dialog', FINISH)).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('override-block'));
    const other = await dialogOf('Override the block');
    expect(within(other).queryByTestId('blocked-other-lock')).not.toBeInTheDocument();
  });

  it.each([
    ['a 409 in_progress (transient)', () => failed(problem(409, 'ebilling.blocked.in_progress', { detail: 'Another request is running.' }))],
    ['a 409 still_processing (transient)', () => failed(problem(409, 'ebilling.blocked.still_processing', { detail: 'Still processing.' }))],
    ['a 401 (sign-in expired)', () => failed(problem(401, null, { detail: 'Sign in again.' }))],
    ['no answer at all', () => noAnswer()],
  ])('(must-not-fire) a repeat of the locked finish answered %s keeps the lock', async (_, again) => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(failed(problem(503, 'ebilling.blocked.unavailable', { detail: 'Saved in part.' }))).mockResolvedValueOnce(again());
    const dialog = await sendFinish(decision);
    await waitFor(() => expect(reasonOf(dialog)).toHaveAttribute('readonly'));
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.finishStranded).toHaveBeenCalledTimes(2));
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).toHaveAttribute('readonly');
    expect(noteOf(dialog)).toHaveAttribute('readonly');
  });

  it('(must-not-fire) a FIRST send answered with a definite refusal does not lock', async () => {
    const decision = strandedApi();
    decision.finishStranded.mockResolvedValueOnce(failed(problem(409, 'ebilling.blocked.own_decision_pending')));
    const dialog = await sendFinish(decision);
    await within(dialog).findByTestId('blocked-error');
    expect(reasonOf(dialog)).not.toHaveAttribute('readonly');
    expect(noteOf(dialog)).not.toHaveAttribute('readonly');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// The client
// ─────────────────────────────────────────────────────────────────────────────

describe('the finish-stranded client call', () => {
  const BASE = 'https://bff.example.test';
  const okResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body });
  const fetchReturning = (body: unknown) => jest.fn(async (_url: string, _init?: RequestInit) => okResponse(body) as never);

  it('posts to /api/finance/submissions/{id}/finish-stranded with the reason, and the note only when there is one', async () => {
    expect(decisionRoutes.finishStranded(BASE, SUBMISSION_ID)).toBe(`${BASE}/api/finance/submissions/${SUBMISSION_ID}/finish-stranded`);
    const fetchFn = fetchReturning({ outcome: 'Overridden' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    expect((await api.finishStranded(SUBMISSION_ID, 'The decider has left')).ok).toBe(true);
    await api.finishStranded(SUBMISSION_ID, 'The decider has left', 'A note');
    await api.finishStranded(SUBMISSION_ID, 'The decider has left', '   ');
    const sent = fetchFn.mock.calls.map(c => [c[0], c[1]?.method, JSON.parse(String(c[1]?.body))]);
    expect(sent).toEqual([
      [decisionRoutes.finishStranded(BASE, SUBMISSION_ID), 'POST', { reason: 'The decider has left' }],
      [decisionRoutes.finishStranded(BASE, SUBMISSION_ID), 'POST', { reason: 'The decider has left', note: 'A note' }],
      [decisionRoutes.finishStranded(BASE, SUBMISSION_ID), 'POST', { reason: 'The decider has left' }],
    ]);
  });

  it('refuses a body that is not a decision outcome', async () => {
    const bad = await createInvoiceDecisionApi(fetchReturning({ nope: 1 }), BASE).finishStranded(SUBMISSION_ID, 'r');
    expect(bad.ok).toBe(false);
  });

  it('reads the hints off the blocked view as the server writes them (camelCase)', async () => {
    const view = strandedSubmission('Return');
    const got = await createInvoiceDecisionApi(fetchReturning(JSON.parse(JSON.stringify(view))), BASE).getBlocked(SUBMISSION_ID);
    expect(got.ok && got.value.canFinishAsOperator).toBe(true);
    expect(got.ok && got.value.strandedDecision).toEqual({ verb: 'Return', recordId: STRANDED_RECORD_ID, callerIsDecider: false });
    // An older server that sends neither still reads as a valid blocked view.
    const plain = JSON.parse(JSON.stringify(blockedSubmission()));
    delete plain.strandedDecision;
    delete plain.canFinishAsOperator;
    expect((await createInvoiceDecisionApi(fetchReturning(plain), BASE).getBlocked(SUBMISSION_ID)).ok).toBe(true);
  });
});
