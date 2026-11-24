/**
 * ISS-032 (#2883), task iss032: Invoice Review offers "Finish decision" for a reject, return or approve that stopped after its status
 * transition, before its Decision Record.
 *
 * The server says so in the approval state (`incompleteDecision`), and only to the caller whose repeat finishes it; the page only shows what
 * the server says. Finish sends the SAME repeat the open wizard would send (approve, reject or return). Through the real page, banner and form
 * with mocked BFF clients (the harness of finishAsOperator.test.tsx). Dialogs are found by role and checked by their text, never by their
 * accessible name: an open surface can be marked aria-hidden when the page moves under it (ISS-036), which empties the name.
 */

import '@testing-library/jest-dom';
import { configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import {
  awaitingLine,
  decisionAccess,
  finishDecisionIntro,
  incompleteDecisionText,
  incompleteOutcome,
  isRecordedStage,
  recordedReason,
} from '../services/decisionModel';
import type { ApiResult, InvoiceReview } from '../services/invoiceReviewApi';
import type { ApprovalState } from '../services/decisionApi';
import {
  INVOICE_ID,
  approval,
  decidedApproval,
  decidedReview,
  decisionRecord,
  failed,
  firmNoticeDraft,
  forbidden,
  line,
  mockApi,
  mockDecisionApi,
  ok,
  problem,
  review,
  strandedApproval,
  type MockDecisionApi,
} from './testKit';

type Verb = 'Approve' | 'Reject' | 'Return';

function renderDecisions(reviews: InvoiceReview | InvoiceReview[], decision: MockDecisionApi) {
  const api = mockApi(reviews);
  render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={api as never} decisionApi={decision as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
  return { api };
}

/**
 * A decision API whose approval read answers `current` (switched by the test or by a finished repeat). The repeat of `verb` succeeds and the
 * next approval read then shows the decision finished, unless the test overrides the repeat.
 */
function strandedApi(verb: Verb = 'Reject', failedStep: 'portion' | 'record' | 'link' = 'record') {
  const decision = mockDecisionApi();
  const world = { current: strandedApproval(verb, { incompleteDecision: { verb, failedStep } }) as ApprovalState };
  decision.getApproval.mockImplementation(async () => ok(world.current));
  const finish = async () => {
    world.current = decidedApproval(verb);
  };
  decision.approve.mockImplementation(async () => {
    await finish();
    return ok({ outcome: 'approved', decisionId: 'd1', decisionRecordId: 'rec-20', state: decidedApproval('Approve') });
  });
  decision.reject.mockImplementation(async () => {
    await finish();
    return ok({ outcome: 'decided', decision: 'Rejected', decisionId: 'd2', decisionRecordId: 'rec-20' });
  });
  decision.returnToFirm.mockImplementation(async () => {
    await finish();
    return ok({ outcome: 'decided', decision: 'Returned', decisionId: 'd3', decisionRecordId: 'rec-20' });
  });
  return { decision, world };
}

const repeatOf = (decision: MockDecisionApi, verb: Verb) =>
  verb === 'Approve' ? decision.approve : verb === 'Reject' ? decision.reject : decision.returnToFirm;

/** The open Finish form: by role, then checked by its title text (never by accessible name, ISS-036). */
async function finishDialog(): Promise<HTMLElement> {
  return waitFor(() => {
    const d = screen.getAllByRole('dialog', { hidden: true }).find(x => within(x).queryByText('Finish decision') !== null);
    if (!d) throw new Error('the Finish form is not open');
    return d;
  });
}
const anyFinishDialog = () => screen.queryAllByRole('dialog', { hidden: true }).find(x => within(x).queryByText('Finish decision') !== null) ?? null;
const reasonOf = (d: HTMLElement) => within(d).getByTestId('finish-decision-reason') as HTMLTextAreaElement;
const noteOf = (d: HTMLElement) => within(d).getByTestId('finish-decision-note') as HTMLTextAreaElement;
const submitBtn = (d: HTMLElement) => within(d).getByRole('button', { name: 'Finish', hidden: true });
const incomplete = (detail = 'The invoice was rejected but a later step did not finish. Repeat the call to complete it.') =>
  failed(problem(503, 'ebilling.invoice.decision_incomplete', { detail, failedStep: 'record' }));

// ISS-041: after a reload (no notice kept in memory) a reject or return finish offers the Notify step; its draft is asked for when the form opens.
const draftOf = (verb: Verb) => firmNoticeDraft({ kind: verb === 'Return' ? 'ReturnedToFirm' : 'InvoiceRejected' });
const noticeOf = (verb: Verb) => ({ subject: draftOf(verb).subject, prose: draftOf(verb).prose });
const draftLoaded = (d: HTMLElement) => within(d).findByTestId('notice-subject');

async function openFinish(decision: MockDecisionApi, verb: Verb = 'Reject') {
  const r = renderDecisions(decidedReview(verb), decision);
  fireEvent.click(await screen.findByTestId('finish-decision'));
  const dialog = await finishDialog();
  if (verb !== 'Approve') await draftLoaded(dialog);
  return { dialog, ...r };
}

// The flows here chain a send and re-reads; the full suite runs files in parallel, so allow for a slow machine.
configure({ asyncUtilTimeout: 5000 });

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// The page's lenses (pure)
// ─────────────────────────────────────────────────────────────────────────────

describe('the stranded decision: what the page makes of the server\'s hint', () => {
  it.each([
    ['Approve', 'approve'],
    ['Reject', 'reject'],
    ['Return', 'return'],
  ] as const)('%s is a decision to finish, owned by the banner (no wizard button)', (verb, outcome) => {
    const state = strandedApproval(verb);
    expect(incompleteOutcome(state)).toBe(outcome);
    expect(decisionAccess(decidedReview(verb), state, true)).toBe('finish');
    expect(awaitingLine(state)).toEqual({ tone: 'warning', text: expect.stringContaining('not finished') });
  });

  it('(must-not-fire) no hint, a hint left out (an older server) and a verb the page does not know are no decision to finish', () => {
    const left = decidedApproval('Reject');
    delete left.incompleteDecision;
    for (const state of [decidedApproval('Reject'), left, strandedApproval('Reject', { incompleteDecision: { verb: 'Override', failedStep: 'record' } })]) {
      expect(incompleteOutcome(state)).toBeNull();
      expect(decisionAccess(decidedReview('Reject'), state, true)).toBe('view');
    }
    expect(incompleteOutcome(null)).toBeNull();
  });

  it('says what happened and what finishing does; a reject or return asks for the reason again, saying why', () => {
    expect(incompleteDecisionText('reject')).toMatch(/was rejected, but the decision did not finish: its Decision Record was not written/);
    expect(incompleteDecisionText('return')).toMatch(/returned to the firm/);
    expect(incompleteDecisionText('approve')).toMatch(/approval is not on record yet/);
    expect(finishDecisionIntro('reject', false)).toMatch(/reason you gave was not saved, because it is kept only in the Decision Record, so enter it again/);
    expect(finishDecisionIntro('approve', false)).not.toMatch(/reason/);
    expect(finishDecisionIntro('return', true)).toMatch(/repeats the request you sent, with the same reason and note/);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// When it shows
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish decision: shown only on the server\'s word', () => {
  it.each(['Reject', 'Return', 'Approve'] as const)('after a reload, a stranded %s shows the stranded message and "Finish decision", and no "View decision"', async verb => {
    const { decision } = strandedApi(verb);
    renderDecisions(decidedReview(verb), decision);
    const banner = await screen.findByTestId('incomplete-decision-banner');
    expect(within(banner).getByText('This decision did not finish')).toBeInTheDocument();
    expect(within(banner).getByTestId('incomplete-decision-text')).toHaveTextContent('its Decision Record was not written');
    expect(within(banner).getByTestId('finish-decision')).toHaveTextContent('Finish decision');
    expect(screen.queryByTestId('view-decision')).not.toBeInTheDocument();
    expect(screen.queryByTestId('decide-invoice')).not.toBeInTheDocument();
    expect(await screen.findByTestId('awaiting')).toHaveTextContent('not finished');
    // Nothing is sent until the reviewer finishes it.
    expect(repeatOf(decision, verb)).not.toHaveBeenCalled();
  });

  it.each(['Reject', 'Return', 'Approve'] as const)('(must-not-fire) a complete %s shows only "View decision"', async verb => {
    const decision = mockDecisionApi(decidedApproval(verb));
    renderDecisions(decidedReview(verb), decision);
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
    expect(screen.queryByTestId('incomplete-decision-banner')).not.toBeInTheDocument();
    expect(screen.queryByTestId('finish-decision')).not.toBeInTheDocument();
  });

  it('(must-not-fire) an older server that leaves the hint out, and a verb the page does not know, show "View decision" and no Finish', async () => {
    const older = decidedApproval('Reject');
    delete older.incompleteDecision;
    for (const state of [older, strandedApproval('Reject', { incompleteDecision: { verb: 'Something', failedStep: null } })]) {
      const { unmount } = render(
        <FluentProvider theme={webLightTheme}>
          <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={mockApi(decidedReview('Reject')) as never} decisionApi={mockDecisionApi(state) as never} pollIntervalMs={60_000} />
        </FluentProvider>
      );
      expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
      expect(screen.queryByTestId('finish-decision')).not.toBeInTheDocument();
      unmount();
    }
  });

  it('(must-not-fire) a pending invoice offers "Decide invoice…" and no Finish', async () => {
    renderDecisions(review([line(1)]), mockDecisionApi(approval()));
    expect(await screen.findByTestId('decide-invoice')).toBeInTheDocument();
    expect(screen.queryByTestId('incomplete-decision-banner')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Finishing it
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish decision: the repeat', () => {
  it.each(['Reject', 'Return'] as const)('%s: asks for the reason again (blank does not count), sends exactly ONE repeat with the trimmed reason and no note key, then shows the decision read-only', async verb => {
    const { decision } = strandedApi(verb);
    const { dialog, api } = await openFinish(decision, verb);
    expect(within(dialog).getByTestId('finish-decision-intro')).toHaveTextContent('enter it again');
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.change(reasonOf(dialog), { target: { value: '   ' } });
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.change(reasonOf(dialog), { target: { value: '  Rate card not followed  ' } });
    expect(submitBtn(dialog)).toBeEnabled();
    const reads = api.getReview.mock.calls.length;

    fireEvent.click(submitBtn(dialog));
    fireEvent.click(submitBtn(dialog));

    await waitFor(() => expect(screen.queryByTestId('incomplete-decision-banner')).not.toBeInTheDocument());
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
    const repeat = repeatOf(decision, verb);
    expect(repeat).toHaveBeenCalledTimes(1);
    // ISS-041: no notice was kept (a reload), so the form offered the Notify step with its draft on, and the repeat carries it.
    expect(repeat).toHaveBeenCalledWith(INVOICE_ID, 'Rate card not followed', undefined, noticeOf(verb));
    expect(decision.approve).not.toHaveBeenCalled();
    expect(api.getReview.mock.calls.length).toBeGreaterThan(reads);
    await waitFor(() => expect(anyFinishDialog()).toBeNull());
  });

  it('Approve: no reason is asked, the repeat is the approve with no note key, then the decision shows read-only', async () => {
    const { decision } = strandedApi('Approve');
    const { dialog } = await openFinish(decision, 'Approve');
    expect(within(dialog).queryByTestId('finish-decision-reason')).not.toBeInTheDocument();
    expect(submitBtn(dialog)).toBeEnabled();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledTimes(1));
    expect(decision.approve).toHaveBeenCalledWith(INVOICE_ID);
    expect(decision.reject).not.toHaveBeenCalled();
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
    expect(screen.queryByTestId('incomplete-decision-banner')).not.toBeInTheDocument();
  });

  it('sends the note as typed when there is one', async () => {
    const { decision } = strandedApi('Return');
    const { dialog } = await openFinish(decision, 'Return');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Please correct the task codes' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'Second attempt after the outage' } });
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledWith(INVOICE_ID, 'Please correct the task codes', 'Second attempt after the outage', noticeOf('Return')));
  });

  it('the Finish button is disabled while the repeat is in flight, and a second click sends nothing', async () => {
    const { decision, world } = strandedApi('Reject');
    let answer: (r: ApiResult<unknown>) => void = () => undefined;
    decision.reject.mockImplementation(
      () =>
        new Promise(resolve => {
          answer = resolve as never;
        })
    );
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(submitBtn(dialog)).toBeDisabled());
    fireEvent.click(submitBtn(dialog));
    expect(decision.reject).toHaveBeenCalledTimes(1);

    world.current = decidedApproval('Reject');
    answer(ok({ outcome: 'decided', decision: 'Rejected', decisionId: 'd2', decisionRecordId: 'rec-20' }));
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
    expect(decision.reject).toHaveBeenCalledTimes(1);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// A refusal or a 503
// ─────────────────────────────────────────────────────────────────────────────

describe('Finish decision: a refusal or a 503 keeps the affordance', () => {
  it.each([
    ['403 (the close rights)', forbidden('ebilling.decision.not_authorized', { detail: 'You do not have permission to record a decision that resolves this invoice\'s findings.' }), "You do not have permission to record a decision that resolves this invoice's findings. (ebilling.decision.not_authorized)"],
    ['409 (completion refused)', problem(409, 'ebilling.approve.completion_refused', { detail: 'The approval cannot be completed.' }), 'The approval cannot be completed. (ebilling.approve.completion_refused)'],
    ['503 (nothing changed)', problem(503, 'ebilling.invoice.decision_unavailable', { detail: 'The decision could not be completed; nothing was changed. Try again.' }), 'nothing was changed. Try again. (ebilling.invoice.decision_unavailable)'],
  ])('a %s is shown in the form with its code; the reason is kept, the form stays editable and the banner keeps Finish', async (_, err, message) => {
    const { decision } = strandedApi('Reject');
    decision.reject.mockResolvedValue(failed(err));
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.click(submitBtn(dialog));

    expect(await within(dialog).findByTestId('finish-decision-error')).toHaveTextContent(message);
    expect(reasonOf(dialog).value).toBe('Rate card not followed');
    expect(reasonOf(dialog)).not.toHaveAttribute('readonly');
    expect(submitBtn(dialog)).toBeEnabled();
    expect(screen.getByTestId('finish-decision')).toBeInTheDocument();
    expect(screen.queryByTestId('view-decision')).not.toBeInTheDocument();
  });

  it('a 503 saved in part: shown with "repeat it", the reason and note are locked, the banner keeps Finish, and the repeat is the identical request', async () => {
    const { decision, world } = strandedApi('Reject');
    decision.reject.mockResolvedValueOnce(incomplete());
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.change(noteOf(dialog), { target: { value: 'A note' } });
    const reads = decision.getApproval.mock.calls.length;
    fireEvent.click(submitBtn(dialog));

    expect(await within(dialog).findByTestId('finish-decision-error')).toHaveTextContent('It was saved in part: repeat it to finish.');
    expect(within(dialog).getByTestId('finish-decision-locked')).toBeInTheDocument();
    expect(reasonOf(dialog)).toHaveAttribute('readonly');
    expect(noteOf(dialog)).toHaveAttribute('readonly');
    await waitFor(() => expect(decision.getApproval.mock.calls.length).toBeGreaterThan(reads));
    expect(screen.getByTestId('finish-decision')).toBeInTheDocument();

    // The repeat (the default mock now finishes it) is the same request.
    decision.reject.mockImplementation(async () => {
      world.current = decidedApproval('Reject');
      return ok({ outcome: 'decided', decision: 'Rejected', decisionId: 'd2', decisionRecordId: 'rec-20' });
    });
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
  });

  it('a request kept locked survives closing and reopening the form', async () => {
    const { decision } = strandedApi('Return');
    decision.returnToFirm.mockResolvedValueOnce(failed(new Error('Failed to fetch')));
    const { dialog } = await openFinish(decision, 'Return');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Please correct the task codes' } });
    fireEvent.click(submitBtn(dialog));
    await within(dialog).findByTestId('finish-decision-locked');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(anyFinishDialog()).toBeNull());

    fireEvent.click(screen.getByTestId('finish-decision'));
    const again = await finishDialog();
    expect(reasonOf(again).value).toBe('Please correct the task codes');
    expect(reasonOf(again)).toHaveAttribute('readonly');
    expect(within(again).getByTestId('finish-decision-intro')).toHaveTextContent('repeats the request you sent');
  });

  it('a refused note is shown under the Note box and nothing is locked', async () => {
    const { decision } = strandedApi('Approve');
    decision.approve.mockResolvedValue(failed(problem(400, 'ebilling.note_too_long', { detail: 'The note may be at most 2000 characters.' })));
    const { dialog } = await openFinish(decision, 'Approve');
    fireEvent.change(noteOf(dialog), { target: { value: 'a note' } });
    fireEvent.click(submitBtn(dialog));
    const field = within(dialog).getByTestId('finish-decision-note-field');
    await waitFor(() => expect(within(field).getByText('The note may be at most 2000 characters.')).toBeInTheDocument());
    expect(noteOf(dialog)).not.toHaveAttribute('readonly');
    expect(within(dialog).queryByTestId('finish-decision-error')).not.toBeInTheDocument();
  });

  it('an unsure send whose re-read shows the hint gone keeps the open form with its error, so the same request can be sent again', async () => {
    const { decision, world } = strandedApi('Reject');
    decision.reject.mockImplementationOnce(async () => {
      // No answer came back, and the re-read no longer reports the hint (another session finished it meanwhile).
      world.current = decidedApproval('Reject');
      return incomplete();
    });
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(screen.queryByTestId('incomplete-decision-banner')).not.toBeInTheDocument());
    const open = await finishDialog();
    expect(within(open).getByTestId('finish-decision-error')).toHaveTextContent('repeat it to finish');
    fireEvent.click(submitBtn(open));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Review round 1 F4: stranded after its Decision Record (failedStep "link")
// ─────────────────────────────────────────────────────────────────────────────

const ACTION: Readonly<Record<Verb, string>> = { Approve: 'approve-invoice', Reject: 'reject-invoice', Return: 'return-to-firm' };
const RECORDED_REASON = 'Rate card not followed';

/** The decided invoice's review with its invoice-level Decision Record (the review reads it by the invoice, linked or not). */
function recordedReview(verb: Verb, reason: string | null = verb === 'Approve' ? null : RECORDED_REASON): InvoiceReview {
  const base = decidedReview(verb);
  return {
    ...base,
    decisionHistory: [decisionRecord({ recordId: 'rec-20', actionCode: ACTION[verb], subject: 'invoice', lineId: null, outcome: null, reason })],
  };
}

describe('Finish decision after the record ("link"): the follow-up steps', () => {
  it('the lenses: "link" is the recorded stage; the reason comes from this decision\'s invoice-level record only', () => {
    expect(isRecordedStage(strandedApproval('Reject', { incompleteDecision: { verb: 'Reject', failedStep: 'link' } }))).toBe(true);
    expect(isRecordedStage(strandedApproval('Reject'))).toBe(false);
    expect(isRecordedStage(null)).toBe(false);
    expect(recordedReason(recordedReview('Reject'), 'reject')).toBe(RECORDED_REASON);
    expect(recordedReason(recordedReview('Reject'), 'return')).toBeNull();
    expect(recordedReason(recordedReview('Reject', '   '), 'reject')).toBeNull();
    expect(recordedReason(decidedReview('Reject'), 'reject')).toBeNull();
    expect(incompleteDecisionText('reject', true)).toMatch(/decision is recorded, but its follow-up steps did not finish/);
    expect(incompleteDecisionText('reject', true)).not.toMatch(/not written|not on record/);
    expect(finishDecisionIntro('reject', false, true)).toMatch(/already recorded/);
    expect(finishDecisionIntro('reject', true, true)).not.toMatch(/not saved|enter it again/);
  });

  it.each(['Reject', 'Return'] as const)(
    '%s: the banner says the decision is recorded; Finish sends the record\'s reason, read-only, with no note, exactly ONCE, and the decision shows read-only',
    async verb => {
      const { decision } = strandedApi(verb, 'link');
      renderDecisions(recordedReview(verb), decision);
      const banner = await screen.findByTestId('incomplete-decision-banner');
      expect(within(banner).getByTestId('incomplete-decision-text')).toHaveTextContent('the decision is recorded, but its follow-up steps did not finish');
      expect(within(banner).getByTestId('incomplete-decision-text')).not.toHaveTextContent('not written');
      expect(await screen.findByTestId('awaiting')).toHaveTextContent('follow-up steps');

      fireEvent.click(within(banner).getByTestId('finish-decision'));
      const dialog = await finishDialog();
      const intro = within(dialog).getByTestId('finish-decision-intro');
      expect(intro).toHaveTextContent('already recorded');
      expect(intro).not.toHaveTextContent('not saved');
      expect(intro).not.toHaveTextContent('enter it again');
      expect(reasonOf(dialog)).toHaveValue(RECORDED_REASON);
      expect(reasonOf(dialog)).toHaveAttribute('readonly');
      expect(within(dialog).queryByTestId('finish-decision-note')).not.toBeInTheDocument();
      await draftLoaded(dialog);
      expect(submitBtn(dialog)).toBeEnabled();

      fireEvent.click(submitBtn(dialog));
      fireEvent.click(submitBtn(dialog));

      await waitFor(() => expect(screen.queryByTestId('incomplete-decision-banner')).not.toBeInTheDocument());
      expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
      const repeat = repeatOf(decision, verb);
      expect(repeat).toHaveBeenCalledTimes(1);
      expect(repeat).toHaveBeenCalledWith(INVOICE_ID, RECORDED_REASON, undefined, noticeOf(verb));
    }
  );

  it('Approve: nothing is asked (no reason, no note); the repeat is the approve alone, once', async () => {
    const { decision } = strandedApi('Approve', 'link');
    renderDecisions(recordedReview('Approve'), decision);
    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    expect(within(dialog).queryByTestId('finish-decision-reason')).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId('finish-decision-note')).not.toBeInTheDocument();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledTimes(1));
    expect(decision.approve).toHaveBeenCalledWith(INVOICE_ID);
    expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
  });

  it('a reject whose record the review does not carry: Finish is disabled and the form says to reload; nothing is typed or sent', async () => {
    const { decision } = strandedApi('Reject', 'link');
    renderDecisions(decidedReview('Reject'), decision);
    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    expect(within(dialog).getByTestId('finish-decision-no-reason')).toHaveTextContent('Reload the page');
    expect(within(dialog).queryByTestId('finish-decision-reason')).not.toBeInTheDocument();
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.click(submitBtn(dialog));
    expect(decision.reject).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// ISS-041 (#2930): after a reload the notice kept in memory is gone, so the Finish form offers the Notify step
// ─────────────────────────────────────────────────────────────────────────────

const sendSwitch = (d: HTMLElement) => within(d).getByRole('switch', { name: 'Send this notice to the firm', hidden: true });
const subjectBox = (d: HTMLElement) => within(d).getByLabelText('Notice subject') as HTMLInputElement;
const proseBox = (d: HTMLElement) => within(d).getByLabelText('Notice message') as HTMLTextAreaElement;
const refusal = () =>
  failed(problem(400, 'ebilling.notice_refused', { detail: 'The notice to the firm was not accepted (prose.digit). Nothing was decided. Edit the notice and try again.' }));

describe('ISS-041: Finish decision after a reload offers the Notify step for a reject or return', () => {
  it.each(['Reject', 'Return'] as const)(
    '%s: the form offers the Notify step with the server draft (asked once, for this kind); an edited notice is what the repeat carries, trimmed',
    async verb => {
      const { decision } = strandedApi(verb);
      const { dialog } = await openFinish(decision, verb);
      expect(within(dialog).getByTestId('notify-step')).toBeInTheDocument();
      expect(sendSwitch(dialog)).toBeChecked();
      expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);
      expect(decision.getFirmNoticeDraft).toHaveBeenCalledWith(INVOICE_ID, verb === 'Return' ? 'ReturnedToFirm' : 'InvoiceRejected');
      // The notice is the wizard's step: the same fields, counters and limits.
      expect(within(dialog).getByTestId('notice-subject-count')).toBeInTheDocument();
      expect(within(dialog).queryByTestId('finish-decision-notice')).not.toBeInTheDocument();

      fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
      fireEvent.change(subjectBox(dialog), { target: { value: '  Edited subject (INV-2025-78232.1)  ' } });
      fireEvent.change(proseBox(dialog), { target: { value: '  Edited message.  ' } });
      fireEvent.click(submitBtn(dialog));

      await waitFor(() => expect(repeatOf(decision, verb)).toHaveBeenCalledTimes(1));
      expect(repeatOf(decision, verb)).toHaveBeenCalledWith(INVOICE_ID, 'Rate card not followed', undefined, {
        subject: 'Edited subject (INV-2025-78232.1)',
        prose: 'Edited message.',
      });
      expect(await screen.findByTestId('view-decision')).toBeInTheDocument();
    }
  );

  it.each(['Reject', 'Return'] as const)('%s: choosing to send no notice sends none (the old two-argument call), and the form says so', async verb => {
    const { decision } = strandedApi(verb);
    const { dialog } = await openFinish(decision, verb);
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.click(sendSwitch(dialog));
    expect(within(dialog).getByTestId('notice-off')).toHaveTextContent('The firm gets no email');
    expect(submitBtn(dialog)).toBeEnabled();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(repeatOf(decision, verb)).toHaveBeenCalledTimes(1));
    expect(repeatOf(decision, verb).mock.calls[0]).toEqual([INVOICE_ID, 'Rate card not followed']);
  });

  it('Finish is held while the draft loads, so the repeat cannot go out before the reviewer has seen the notice', async () => {
    const { decision } = strandedApi('Reject');
    let answer: (r: ApiResult<unknown>) => void = () => undefined;
    decision.getFirmNoticeDraft.mockImplementation(
      () =>
        new Promise(resolve => {
          answer = resolve as never;
        })
    );
    renderDecisions(decidedReview('Reject'), decision);
    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    expect(await within(dialog).findByTestId('notice-loading')).toBeInTheDocument();
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.click(submitBtn(dialog));
    expect(decision.reject).not.toHaveBeenCalled();

    answer(ok(draftOf('Reject')));
    await draftLoaded(dialog);
    expect(submitBtn(dialog)).toBeEnabled();
  });

  it('a blank or over-limit notice with sending on holds Finish (the Notify step\'s own limits); turning it off releases it', async () => {
    const { decision } = strandedApi('Reject');
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.change(subjectBox(dialog), { target: { value: '   ' } });
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.change(subjectBox(dialog), { target: { value: 'Fine subject' } });
    fireEvent.change(proseBox(dialog), { target: { value: 'x'.repeat(1501) } });
    expect(submitBtn(dialog)).toBeDisabled();
    fireEvent.click(sendSwitch(dialog));
    expect(submitBtn(dialog)).toBeEnabled();
  });

  it('a draft that could not be fetched is shown with Retry and does not block Finish; the repeat then carries no notice', async () => {
    const { decision } = strandedApi('Reject');
    decision.getFirmNoticeDraft.mockResolvedValueOnce(failed(problem(503, 'ebilling.firm_notice.unavailable', { detail: 'The notice could not be drafted.' })));
    renderDecisions(decidedReview('Reject'), decision);
    fireEvent.click(await screen.findByTestId('finish-decision'));
    const dialog = await finishDialog();
    expect(await within(dialog).findByTestId('notice-draft-error')).toHaveTextContent('could not be drafted');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    expect(submitBtn(dialog)).toBeEnabled();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(1));
    expect(decision.reject.mock.calls[0]).toEqual([INVOICE_ID, 'Rate card not followed']);
  });

  it('(must-not-fire) an approve finish shows no Notify fields and asks for no draft', async () => {
    const { decision } = strandedApi('Approve');
    const { dialog } = await openFinish(decision, 'Approve');
    expect(within(dialog).queryByTestId('notify-step')).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId('finish-decision-notice')).not.toBeInTheDocument();
    expect(decision.getFirmNoticeDraft).not.toHaveBeenCalled();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.approve).toHaveBeenCalledTimes(1));
    expect(decision.approve).toHaveBeenCalledWith(INVOICE_ID);
  });

  it('a refused notice is shown under the notice fields (once), nothing is locked, and editing it then finishing sends the edited notice', async () => {
    const { decision } = strandedApi('Reject');
    decision.reject.mockResolvedValueOnce(refusal());
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    const reads = decision.getApproval.mock.calls.length;
    fireEvent.click(submitBtn(dialog));
    expect(await within(dialog).findByTestId('notice-refused')).toHaveTextContent('(prose.digit)');
    expect(within(dialog).queryByTestId('finish-decision-notice-refused')).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId('finish-decision-error')).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId('finish-decision-locked')).not.toBeInTheDocument();
    expect(decision.getApproval.mock.calls.length).toBe(reads);
    expect(subjectBox(dialog)).not.toHaveAttribute('readonly');

    fireEvent.change(proseBox(dialog), { target: { value: 'A corrected message.' } });
    expect(within(dialog).queryByTestId('notice-refused')).not.toBeInTheDocument();
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1][3]).toEqual({ subject: draftOf('Reject').subject, prose: 'A corrected message.' });
  });

  it('an unsure send from the offered form keeps what it sent: reopened, the notice is shown read-only and the repeat is the same request', async () => {
    const { decision, world } = strandedApi('Reject');
    decision.reject.mockResolvedValueOnce(incomplete());
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Rate card not followed' } });
    fireEvent.click(submitBtn(dialog));
    expect(await within(dialog).findByTestId('finish-decision-error')).toHaveTextContent('repeat it to finish');
    // The lock now holds the notice, so the form shows it as a kept one (no second draft, no editing).
    expect(within(dialog).queryByTestId('notify-step')).not.toBeInTheDocument();
    expect(within(dialog).getByTestId('finish-decision-notice')).toHaveTextContent('The firm will be emailed this notice');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(anyFinishDialog()).toBeNull());
    fireEvent.click(screen.getByTestId('finish-decision'));
    const again = await finishDialog();
    expect(within(again).queryByTestId('notify-step')).not.toBeInTheDocument();
    expect(within(again).getByTestId('finish-decision-notice')).toHaveTextContent(draftOf('Reject').subject);
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(1);

    decision.reject.mockImplementation(async () => {
      world.current = decidedApproval('Reject');
      return ok({ outcome: 'decided', decision: 'Rejected', decisionId: 'd2', decisionRecordId: 'rec-20' });
    });
    fireEvent.click(submitBtn(again));
    await waitFor(() => expect(decision.reject).toHaveBeenCalledTimes(2));
    expect(decision.reject.mock.calls[1]).toEqual(decision.reject.mock.calls[0]);
    expect(decision.reject.mock.calls[1][3]).toEqual(noticeOf('Reject'));
  });

  it('an unsure send with the notice turned off keeps that choice: the repeat still carries none', async () => {
    const { decision } = strandedApi('Return');
    decision.returnToFirm.mockResolvedValueOnce(incomplete());
    const { dialog } = await openFinish(decision, 'Return');
    fireEvent.change(reasonOf(dialog), { target: { value: 'Please correct the task codes' } });
    fireEvent.click(sendSwitch(dialog));
    fireEvent.click(submitBtn(dialog));
    await within(dialog).findByTestId('finish-decision-error');
    expect(within(dialog).getByTestId('finish-decision-notice')).toHaveTextContent('No email is sent to the firm');
    fireEvent.click(submitBtn(dialog));
    await waitFor(() => expect(decision.returnToFirm).toHaveBeenCalledTimes(2));
    expect(decision.returnToFirm.mock.calls[1]).toEqual(decision.returnToFirm.mock.calls[0]);
    expect(decision.returnToFirm.mock.calls[1]).toHaveLength(2);
  });

  it('closing and reopening a form that was not sent asks for a fresh draft and forgets the edits', async () => {
    const { decision } = strandedApi('Reject');
    const { dialog } = await openFinish(decision, 'Reject');
    fireEvent.change(proseBox(dialog), { target: { value: 'Edited and abandoned.' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(anyFinishDialog()).toBeNull());
    fireEvent.click(screen.getByTestId('finish-decision'));
    const again = await finishDialog();
    await draftLoaded(again);
    expect(proseBox(again).value).toBe(draftOf('Reject').prose);
    expect(decision.getFirmNoticeDraft).toHaveBeenCalledTimes(2);
  });
});
