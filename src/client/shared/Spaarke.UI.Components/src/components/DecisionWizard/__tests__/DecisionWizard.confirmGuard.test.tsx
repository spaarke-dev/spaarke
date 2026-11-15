/**
 * The Confirm guard of the decision wizard (spaarkeai-mcp-bff-api-r1 task 032 fix round, NFR-06: a decision is recorded only by an explicit
 * Confirm). Next and Record decision are the same button, so a double click on Next at the last action step, or a held Enter, used to
 * move to Confirm and record the decision before the person had seen it. With the optional `confirmGuardMs` seam on:
 *  - a double click on Next records nothing (the button is disabled while the guard is up)
 *  - a held Enter records nothing (focus moves off the button onto the Confirm step)
 *  - a deliberate click once the guard has passed records exactly once
 * Without the seam (the Console's default before it opts in) nothing changes; the other suites pin that.
 *
 * Interaction is by DOM events, like the other wizard suites; a key press is emulated as a browser does it (keydown at the focused
 * element, then a click when that element is a button), because jsdom does not turn Enter into a click.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { DO_ID, doItem, doPlan } from '../__fixtures__/decisionWizardFixtures';
import { CONFIRM_GUARD_MS } from '../DecisionItemWizard';
import type { DecisionCommitFailure, DecisionReview } from '../decisionWizardTypes';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });

// One action, no Next steps: Next on the action step goes straight to Confirm, which is the case that records unseen.
const plan = { ...doPlan, nextSteps: [] };
const GUARD = 400;

function setup(props: Partial<Parameters<typeof renderWizard>[0]> = {}) {
  const service = makeService({ plans: { [DO_ID]: plan } });
  const view = renderWizard({
    service,
    loadItem: makeLoader([doItem]),
    itemIds: [DO_ID],
    startItemId: DO_ID,
    ...props,
  });
  return { service, ...view };
}

/** At the last action step (Mark complete), with the focus on its Next button. */
async function toLastActionStep() {
  await screen.findByTestId('step-found');
  fireEvent.click(button('Next'));
  await screen.findByTestId('step-action-mark-complete');
  const next = button('Next');
  next.focus();
  return next;
}

/** The browser's Enter on the focused element: keydown, then a click when it is an enabled button (what holding Enter repeats). */
function pressEnter(repeat: boolean) {
  const target = document.activeElement ?? document.body;
  const proceeded = fireEvent.keyDown(target, { key: 'Enter', code: 'Enter', repeat });
  if (proceeded && target instanceof HTMLButtonElement && !target.disabled) fireEvent.click(target);
}

describe('Confirm guard (optional seam)', () => {
  it('the default guard is half a second', () => {
    expect(CONFIRM_GUARD_MS).toBe(500);
  });

  it('a double click on Next at the last action step moves to Confirm and records nothing', async () => {
    const { service } = setup({ confirmGuardMs: GUARD });
    const next = await toLastActionStep();

    fireEvent.click(next);
    fireEvent.click(next); // the second click of the double click: the same node, now Record decision

    expect(await screen.findByTestId('step-confirm')).toBeInTheDocument();
    expect(button('Record decision')).toBeDisabled();
    expect(service.commit).not.toHaveBeenCalled();
  });

  it('a held Enter on Next records nothing: focus leaves the button when Confirm appears', async () => {
    const { service } = setup({ confirmGuardMs: GUARD });
    await toLastActionStep();

    pressEnter(false);
    await screen.findByTestId('step-confirm');
    pressEnter(true);
    pressEnter(true);
    pressEnter(true);

    expect(document.activeElement?.contains(screen.getByTestId('step-confirm'))).toBe(true);
    expect(service.commit).not.toHaveBeenCalled();
  });

  it('a deliberate click once the guard has passed records exactly once', async () => {
    const { service } = setup({ confirmGuardMs: GUARD });
    const next = await toLastActionStep();
    fireEvent.click(next);
    await screen.findByTestId('step-confirm');
    expect(button('Record decision')).toBeDisabled();

    await waitFor(() => expect(button('Record decision')).toBeEnabled(), { timeout: 3000 });
    const record = button('Record decision');
    fireEvent.click(record);
    fireEvent.click(record); // a second click while the request is out sends nothing (single-flight)

    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
  });

  it('without the seam there is no guard: Confirm records at once, as it always did', async () => {
    const { service } = setup();
    const next = await toLastActionStep();
    fireEvent.click(next);
    await screen.findByTestId('step-confirm');

    expect(button('Record decision')).toBeEnabled();
    fireEvent.click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
  });
});

describe('Confirm guard on the send-choice footer (resumed unfinished review)', () => {
  const REVIEW_ID = 'cccccccc-0000-0000-0000-000000000001';
  const failure = (overrides: Partial<DecisionCommitFailure> = {}): DecisionCommitFailure => ({
    httpStatus: 409,
    title: 'The decision was not recorded',
    detail: 'Step 3 failed.',
    reasonCode: 'ontology.action.write_failed',
    correlationId: 'trace-1',
    failedStep: 3,
    landed: [{ entity: 'sprk_servicerequest', id: 'sr-1' }],
    possiblyLanded: [],
    steps: [],
    recordMayExist: false,
    sentButNotRecorded: false,
    resumable: true,
    replayed: true,
    ...overrides,
  });
  const notSentInquiry = () =>
    failure({
      steps: [
        {
          key: 'action:send-budget-inquiry',
          code: 'send-budget-inquiry',
          phase: 4,
          status: 'Failed',
          written: [{ entity: 'sprk_servicerequest', id: 'sr-1' }],
          possiblyWritten: [],
        },
      ],
    });

  /** The wizard opened on the item again; the plan read returns the caller's unfinished review. */
  function resume(unfinishedFailure: DecisionCommitFailure) {
    const review: DecisionReview = {
      reviewId: REVIEW_ID,
      confirmed: true,
      actions: [{ code: 'mark-complete', taken: true }],
    };
    const resumed = {
      ...plan,
      unfinished: { reviewId: review.reviewId, signalId: DO_ID, review, failure: unfinishedFailure },
    };
    const service = makeService({ plans: { [DO_ID]: resumed } });
    renderWizard({
      service,
      loadItem: makeLoader([doItem]),
      itemIds: [DO_ID],
      startItemId: DO_ID,
      confirmGuardMs: GUARD,
    });
    return service;
  }

  async function toConfirm() {
    await toLastActionStep();
    fireEvent.click(button('Next'));
    await screen.findByTestId('step-confirm');
  }

  const cases: Array<[string, () => DecisionCommitFailure, string]> = [
    ['Send again', () => failure({ sendChoiceRequired: true }), 'send-again'],
    ['Record without sending', () => failure({ sendChoiceRequired: true }), 'record-without-sending'],
    ['Retry recording', notSentInquiry, 'retry-recording'],
  ];

  it.each(cases)(
    '%s posts nothing on arrival and posts once after the guard has passed',
    async (name, make, testId) => {
      const service = resume(make());
      await toConfirm();

      const target = screen.getByTestId(testId);
      expect(target).toHaveTextContent(name);
      expect(target).toBeDisabled();
      fireEvent.click(target);
      fireEvent.click(target); // the second click of a double click on Next
      expect(service.commit).not.toHaveBeenCalled();

      await waitFor(() => expect(screen.getByTestId(testId)).toBeEnabled(), { timeout: 3000 });
      fireEvent.click(screen.getByTestId(testId));
      await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
      expect(service.commit.mock.calls[0][1]).toMatchObject({ reviewId: REVIEW_ID, retry: true });
    }
  );
});
