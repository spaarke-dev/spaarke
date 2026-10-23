/**
 * DecisionWizard - the binding behaviour of the decision wizard (task 058; D-17, D-18, D-19, D-23, D-26, D-53, D-69;
 * HANDOFF 1.1 / 1.4 L127-144 @ ae1cc9f). Built on fixtures of tasks 036 / 034 / 043's contracts. The stated contract:
 *  - the steps come from the plan: What was found, one step per action in plan order (recommendation first, badged),
 *    Next steps, Confirm; Next takes, Skip leaves out
 *  - excludes remove later steps and say so; skipping every action is a dismissal that needs a reason (Other needs detail)
 *  - Record decision posts ONE review with a review id, once (a double click sends one), and nothing is written before it;
 *    a failure lists the writes that landed and no record
 *  - Escape and the backdrop never close; closing with unrecorded choices on ANY item asks Discard and close / Keep deciding
 *  - a decided item opens read-only on its last step under the status bar with the fact snapshot, and Back walks the values sent
 *  - negative: no navigateTo, no WizardModal, no wrapper Dialog, no colour literal, no predicted record number, no Report Card
 *    claim, no Assistant-draft badge, no AI call
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { DecisionCommitError } from '../decisionWizardTypes';
import type { DecisionCommitFailure, DecisionReview, DecisionWizardItem } from '../decisionWizardTypes';
import { DecisionWizard } from '../DecisionWizard';
import {
  BUDGET_A,
  BUDGET_B,
  DECIDED_ID,
  DO_ID,
  OTHER_ID,
  PATH_B_ID,
  SENTENCE,
  matterCore,
  otherRow,
  THRESHOLD_ID,
  TODAY,
  approveFirstPlan,
  commitResult,
  decidedItem,
  dismissedItem,
  doItem,
  doPlan,
  notFiledItem,
  pathBItem,
  pathBPlan,
  suppression,
  supersededItem,
  thresholdItem,
} from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeLookup, makeService, renderWizard } from './decisionTestKit';

/**
 * Interaction goes through DOM events (the idiom of the other Wizard tests), not a user-event simulation: user-event moves
 * focus, and Fluent's focus manager (tabster) answers in jsdom on a 100 ms timer by blurring the field mid-typing and marking
 * the dialog aria-hidden, which is jsdom behaviour and not the wizard's.
 */
const user = () => ({
  click: async (element: Element) => {
    // A click on a field focuses it (that is what opens a lookup's results); the event is sent without moving real focus.
    if (element instanceof HTMLInputElement) fireEvent.focus(element);
    fireEvent.click(element);
  },
  selectOptions: async (element: Element, value: string) => {
    fireEvent.change(element, { target: { value } });
  },
});
const enter = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } });
const stepper = () => within(screen.getByRole('navigation', { name: 'Wizard steps' }));
const stepLabels = () =>
  stepper()
    .getAllByRole('listitem')
    .map(li => (li.textContent ?? '').trim());
const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const maybeButton = (name: string) => screen.queryByRole('button', { name: exact(name) });

const plans = {
  [PATH_B_ID]: pathBPlan,
  [THRESHOLD_ID]: approveFirstPlan,
  [DO_ID]: doPlan,
  [notFiledItem.row.signalId]: pathBPlan,
  [DECIDED_ID]: pathBPlan,
  [OTHER_ID]: pathBPlan,
  [dismissedItem.row.signalId]: pathBPlan,
};

function setup(
  items = [pathBItem, thresholdItem],
  startItemId = items[0].row.signalId,
  overrides: Partial<React.ComponentProps<typeof DecisionWizard>> = {}
) {
  const service = makeService({ plans });
  const loadItem = makeLoader(items);
  const lookup = makeLookup();
  const view = renderWizard({
    service,
    loadItem,
    lookup,
    itemIds: items.map(i => i.row.signalId),
    startItemId,
    ...overrides,
  });
  return { service, loadItem, lookup, ...view };
}

/** Wait for the item to load and the first step to show. */
const loaded = () => screen.findByTestId('step-found');

async function next(u: ReturnType<typeof user>) {
  await u.click(button('Next'));
}

async function fillInquiry(u: ReturnType<typeof user>) {
  enter(screen.getByRole('textbox', { name: /^To\b/ }), 'budgets@acme.example');
}

async function fillRevise(u: ReturnType<typeof user>) {
  enter(screen.getByRole('textbox', { name: /^New budget/ }), '250000');
  enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Appeal added');
}

describe('the steps come from the plan', () => {
  it('shows What was found, one step per action in plan order, Next steps and Confirm; the recommendation is badged', async () => {
    const u = user();
    setup();
    await loaded();
    expect(stepLabels()).toEqual([
      'What was found',
      'Send budget inquiry',
      'Revise budget',
      'Approve variance',
      'Next steps',
      'Confirm',
    ]);
    await next(u);
    expect(await screen.findByTestId('step-action-send-budget-inquiry')).toBeInTheDocument();
    expect(screen.getByTestId('recommended-badge')).toBeInTheDocument();
    enter(screen.getByRole('textbox', { name: /^To\b/ }), 'a@b.example');
    await next(u);
    expect(await screen.findByTestId('step-action-revise-budget')).toBeInTheDocument();
    expect(screen.queryByTestId('recommended-badge')).toBeNull();
  });

  it('What was found keeps the sentence literal, shows evidence lines (a missing fact reads Missing), and collapses how it was determined', async () => {
    setup();
    const found = await loaded();
    expect(within(found).getByTestId('rule-sentence')).toHaveTextContent(SENTENCE);
    const lines = within(found).getAllByTestId('evidence-line');
    expect(lines.map(l => l.getAttribute('data-tier'))).toEqual(['Fact', 'Interpretation', 'Missing']);
    expect(lines[2]).toHaveTextContent('Spend snapshot: no value recorded');
    // How this was determined is collapsed until opened.
    const how = within(found).getByRole('button', { name: /how this was determined/i });
    expect(how).toHaveAttribute('aria-expanded', 'false');
    await user().click(how);
    expect(await within(found).findByText(/no budget revision has been recorded since/i)).toBeVisible();
    // Other open items on the matter, same lane.
    expect(within(found).getByTestId('other-open-items')).toHaveTextContent('Second invoice from Acme LLP');
    expect(within(found).getByTestId('other-open-items')).toHaveTextContent('Acme v. Beta');
  });

  it('a Not filed item shows no other open items and offers no Also resolve (D-35)', async () => {
    const u = user();
    setup([notFiledItem]);
    const found = await loaded();
    expect(within(found).queryByTestId('other-open-items')).toBeNull();
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    await screen.findByTestId('step-confirm');
    expect(screen.queryByTestId('also-resolve')).toBeNull();
  });

  it('Also resolve is offered on a Decide item and not on a Do item', async () => {
    const u = user();
    setup([pathBItem]);
    await loaded();
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    expect(await screen.findByTestId('also-resolve')).toHaveTextContent('Second invoice from Acme LLP');
  });

  it('a Do item never shows Also resolve', async () => {
    const u = user();
    setup([doItem]);
    await loaded();
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    await screen.findByTestId('step-confirm');
    expect(screen.queryByTestId('also-resolve')).toBeNull();
  });
});

describe('Path B review records once', () => {
  it('take Send budget inquiry and Revise budget, skip Approve variance, add one To Do, Confirm, Record decision', async () => {
    const u = user();
    const { service, onRecorded } = setup([pathBItem, thresholdItem]);
    await loaded();
    await next(u);

    // Send budget inquiry: the body is a TEMPLATE over the item's own data (D-53), marked as prefilled.
    const body = (await screen.findByRole('textbox', { name: /^Message/ })) as HTMLTextAreaElement;
    expect(body.value).toContain(SENTENCE);
    expect(body.value).toContain('Acme v. Beta');
    expect(screen.getAllByText(/prefilled from this item/i).length).toBeGreaterThan(0);
    await fillInquiry(u);
    await next(u);

    // Revise budget: a matter with one budget has it pre-selected (D-18).
    await screen.findByTestId('step-action-revise-budget');
    expect(await screen.findByText(BUDGET_A.name)).toBeInTheDocument();
    await fillRevise(u);
    await next(u);

    // Taking Revise budget, which comes first in plan order, removes Approve variance: the next step in line is Next steps.
    // Next steps: add one To Do.
    await screen.findByTestId('step-next');
    await u.click(screen.getByRole('checkbox', { name: /add to do/i }));
    await waitFor(() =>
      expect(stepLabels()).toEqual([
        'What was found',
        'Send budget inquiry',
        'Revise budget',
        'Next steps',
        'Add To Do',
        'Confirm',
      ])
    );
    await next(u);
    await screen.findByTestId('step-next-add-todo');
    enter(screen.getByRole('textbox', { name: /^To Do/ }), 'Chase the budget reply');
    await u.click(screen.getByPlaceholderText(/search assigned to/i));
    await u.click(await screen.findByText('Dana Whitfield'));
    fireEvent.change(screen.getByLabelText(/^Due/), { target: { value: '2026-10-20' } });
    await next(u);

    // Confirm: every offered action, taken or skipped; the derived class and why; no predicted number, no Report Card claim.
    const confirm = await screen.findByTestId('step-confirm');
    expect(within(confirm).getByTestId('offered-send-budget-inquiry')).toHaveTextContent('Taken');
    expect(within(confirm).getByTestId('offered-revise-budget')).toHaveTextContent('Taken');
    expect(within(confirm).getByTestId('offered-approve-variance')).toHaveTextContent('Skipped');
    expect(within(confirm).getByTestId('offered-approve-variance')).toHaveTextContent(
      'Cannot be combined with Revise budget'
    );
    expect(within(confirm).getByTestId('next-steps-added')).toHaveTextContent('Add To Do');
    expect(within(confirm).getByTestId('record-class')).toHaveTextContent('a judgement');
    expect(within(confirm).getByTestId('record-class-why')).toHaveTextContent(/next step/i);
    expect(confirm).toHaveTextContent(/new Decision Record/);
    expect(confirm.textContent).not.toMatch(/DR-\d+/);
    expect(confirm.textContent).not.toMatch(/report card/i);
    // Nothing has been written yet (D-17).
    expect(service.commit).not.toHaveBeenCalled();

    await u.click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const [signalId, review] = service.commit.mock.calls[0] as [string, DecisionReview];
    expect(signalId).toBe(PATH_B_ID);
    expect(review.reviewId).toMatch(/^[0-9a-f-]{36}$/i);
    expect(review.confirmed).toBe(true);
    expect(review.actions.map(a => [a.code, a.taken])).toEqual([
      ['send-budget-inquiry', true],
      ['revise-budget', true],
      ['approve-variance', false],
    ]);
    expect(review.actions[1].values).toMatchObject({ budget: BUDGET_A.id, amount: 250000, reason: 'Appeal added' });
    expect(review.nextSteps).toEqual([
      { code: 'add-todo', values: expect.objectContaining({ title: 'Chase the budget reply', dueDate: '2026-10-20' }) },
    ]);
    expect(review.dismissal).toBeUndefined();
    expect(onRecorded).toHaveBeenCalledWith(expect.objectContaining({ signalId: PATH_B_ID }));

    // The item flips to read-only in place, focus on the status bar; Next open item moves on.
    const bar = await screen.findByTestId('status-bar');
    expect(bar).toHaveTextContent('Recorded');
    await waitFor(() => expect(bar).toHaveFocus());
    expect(maybeButton('Record decision')).toBeNull();
    expect(screen.queryByRole('textbox')).toBeNull();
    await u.click(button('Next open item'));
    await screen.findByTestId('step-found');
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
  });

  it('a double click on Record decision sends exactly one request, and the button stays disabled after success', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    let settle: (() => void) | undefined;
    service.commit.mockImplementation(
      () =>
        new Promise(resolve => {
          settle = () => resolve(commitResult());
        })
    );
    await loaded();
    await next(u);
    enter(screen.getByRole('textbox', { name: /^Approved overage/ }), '10');
    await next(u);
    // Taking Approve variance removed Revise budget: Send budget inquiry is next, then Next steps.
    await u.click(button('Skip'));
    await next(u);
    await screen.findByTestId('step-confirm');
    const record = button('Record decision');
    fireEvent.click(record);
    fireEvent.click(record);
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    // In flight: the primary button is the disabled "Recording..." one.
    expect(button('Recording...')).toBeDisabled();
    settle?.();
    await screen.findByTestId('status-bar');
    expect(service.commit).toHaveBeenCalledTimes(1);
    expect(maybeButton('Record decision')).toBeNull();
  });
});

describe('excludes and dismissal', () => {
  it('taking Approve variance removes Revise budget, says so, and Confirm lists it as not combinable', async () => {
    const u = user();
    setup([thresholdItem]);
    await loaded();
    expect(stepLabels()).toEqual([
      'What was found',
      'Approve variance',
      'Revise budget',
      'Send budget inquiry',
      'Next steps',
      'Confirm',
    ]);
    await next(u);
    await screen.findByTestId('step-action-approve-variance');
    expect(screen.getByTestId('excludes-note')).toHaveTextContent('Taking this removes Revise budget from this review');
    // D-19: this action's own line says the Decision Record is the approval and nothing else is written.
    expect(screen.getByTestId('effect-lines')).toHaveTextContent(
      'The Decision Record is the approval; nothing else is written'
    );
    enter(screen.getByRole('textbox', { name: /^Approved overage/ }), '10');
    await next(u);
    // The next step in line is Send budget inquiry: Revise budget is gone from the stepper.
    await screen.findByTestId('step-action-send-budget-inquiry');
    expect(stepLabels()).toEqual([
      'What was found',
      'Approve variance',
      'Send budget inquiry',
      'Next steps',
      'Confirm',
    ]);
    await u.click(button('Skip'));
    await next(u);
    const confirm = await screen.findByTestId('step-confirm');
    expect(within(confirm).getByTestId('offered-revise-budget')).toHaveTextContent(
      'Cannot be combined with Approve variance'
    );
    expect(within(confirm).getByTestId('offered-revise-budget')).toHaveTextContent('Skipped');
  });

  it('skipping the earlier action instead leaves the later step in place', async () => {
    const u = user();
    setup([thresholdItem]);
    await loaded();
    await next(u);
    await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    expect(await screen.findByTestId('step-action-revise-budget')).toBeInTheDocument();
    expect(stepLabels()).toContain('Revise budget');
  });

  it('skipping every action turns Confirm into a dismissal: a reason is required, Other needs detail, the suppression effect is shown', async () => {
    const u = user();
    const { service } = setup([pathBItem]);
    await loaded();
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    const confirm = await screen.findByTestId('step-confirm');
    expect(within(confirm).getByRole('heading', { name: 'Dismiss and record' })).toBeInTheDocument();
    expect(within(confirm).getByTestId('record-class')).toHaveTextContent('a dismissal');
    // Task 034's (policy, matter) count: one counted dismissal so far, so this would be 2 of 3.
    expect(within(confirm).getByTestId('suppression-effect')).toHaveTextContent('Choose a reason');
    const record = button('Dismiss and record');
    expect(record).toBeDisabled();

    const reason = within(confirm).getByRole('combobox', { name: /reason for dismissing/i });
    // The lane's closed list, not free text.
    expect(
      within(reason)
        .getAllByRole('option')
        .map(o => o.textContent)
    ).toEqual(['Choose a reason...', 'Already approved offline', 'Wrong matter - misresolved', 'Other']);
    await u.selectOptions(reason, 'already-approved-offline');
    expect(within(confirm).getByTestId('suppression-effect')).toHaveTextContent(
      '2 of 3 in the last 30 days for Acme v. Beta'
    );
    expect(record).toBeEnabled();
    await u.selectOptions(reason, 'wrong-matter-misresolved');
    expect(within(confirm).getByTestId('suppression-effect')).toHaveTextContent('does not count toward suppressing');

    await u.selectOptions(reason, 'other');
    expect(record).toBeDisabled();
    enter(within(confirm).getByRole('textbox', { name: /^Detail/ }), 'Handled by phone');
    expect(record).toBeEnabled();
    await u.click(record);
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const review = service.commit.mock.calls[0][1] as DecisionReview;
    expect(review.dismissal).toEqual({ code: 'other', detail: 'Handled by phone' });
    expect(review.actions.every(a => !a.taken)).toBe(true);
    expect(review.nextSteps).toBeUndefined();
  });

  it('a dismissal whose count cannot be read says so instead of guessing', async () => {
    const u = user();
    const { service } = setup([pathBItem]);
    service.getSuppression.mockRejectedValue(new Error('boom'));
    await loaded();
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    expect(await screen.findByTestId('suppression-effect')).toHaveTextContent(/could not be read/i);
  });

  it('a rule that is already suppressed says so', async () => {
    const u = user();
    const service = makeService({
      plans,
      suppressions: {
        [PATH_B_ID]: suppression({ suppressedUntil: new Date(Date.now() + 10 * 86400000).toISOString() }),
      },
    });
    renderWizard({
      service,
      loadItem: makeLoader([pathBItem]),
      itemIds: [PATH_B_ID],
      startItemId: PATH_B_ID,
      today: TODAY,
    });
    await loaded();
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    expect(await screen.findByTestId('suppression-effect')).toHaveTextContent(
      /already suppressed for Acme v\. Beta until/i
    );
  });
});

describe('the budget picker (D-18)', () => {
  it('a matter with several budgets asks which one; Next waits for the pick', async () => {
    const u = user();
    const lookup = makeLookup([BUDGET_A, BUDGET_B]);
    setup([pathBItem], PATH_B_ID, { lookup });
    await loaded();
    await next(u);
    await fillInquiry(u);
    await next(u);
    await screen.findByTestId('step-action-revise-budget');
    await fillRevise(u);
    // Not pre-selected, so the required lookup blocks Next.
    expect(screen.queryByText(BUDGET_A.name)).toBeNull();
    expect(button('Next')).toBeDisabled();
    await u.click(screen.getByPlaceholderText(/search budget to revise/i));
    await u.click(await screen.findByText(BUDGET_B.name));
    expect(button('Next')).toBeEnabled();
    expect(lookup).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'Lookup',
        entity: 'sprk_budget',
        core: { recordType: 'sprk_matter', recordId: pathBItem.core?.recordId },
      })
    );
  });
});

describe('Escape and the backdrop never close; closing asks when anything is unrecorded', () => {
  it('opening and browsing without choosing anything closes without asking', async () => {
    const u = user();
    const { onClose } = setup();
    await loaded();
    await next(u);
    await u.click(button('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Discard unrecorded choices?')).toBeNull();
  });

  it('Escape and the backdrop do nothing; the close button opens the nested Discard check with Keep deciding on the left', async () => {
    const u = user();
    const { onClose } = setup();
    await loaded();
    await next(u);
    enter(await screen.findByRole('textbox', { name: /^To\b/ }), 'typed');

    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' });
    const backdrop = document.querySelector('.fui-DialogSurface__backdrop, [class*="backdrop"]');
    if (backdrop) fireEvent.click(backdrop);
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByText('Discard unrecorded choices?')).toBeNull();

    await u.click(button('Close'));
    expect(await screen.findByText('Discard unrecorded choices?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    const check = await screen.findByRole('alertdialog', { hidden: true });
    const buttons = within(check)
      .getAllByRole('button', { hidden: true })
      .map(b => b.textContent);
    expect(buttons.indexOf('Keep deciding')).toBeLessThan(buttons.indexOf('Discard and close'));

    await u.click(within(check).getByRole('button', { name: 'Keep deciding', hidden: true }));
    expect(onClose).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByText('Discard unrecorded choices?')).toBeNull());
    expect(((await screen.findByRole('textbox', { name: /^To\b/, hidden: true })) as HTMLInputElement).value).toBe('typed');

    await u.click(screen.getByRole('button', { name: exact('Close'), hidden: true }));
    const again = await screen.findByRole('alertdialog', { hidden: true });
    // Fluent's focus manager (tabster) marks a re-opened nested dialog aria-hidden in jsdom; the button is still the real one.
    await u.click(within(again).getByRole('button', { name: 'Discard and close', hidden: true }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('unrecorded choices on ANOTHER browsed item still ask', async () => {
    const u = user();
    const { onClose } = setup();
    await loaded();
    await next(u);
    enter(await screen.findByRole('textbox', { name: /^To\b/ }), 'typed');
    await u.click(screen.getByRole('button', { name: /next record/i }));
    await screen.findByTestId('step-found');
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
    await u.click(button('Close'));
    expect(await screen.findByText('Discard unrecorded choices?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe('browse', () => {
  it('walks the list the wizard was opened from, no wrap, and keeps what was typed on each item', async () => {
    const u = user();
    setup([pathBItem, thresholdItem]);
    await loaded();
    expect(screen.getByText('1 of 2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /previous record/i })).toBeDisabled();
    await next(u);
    enter(await screen.findByRole('textbox', { name: /^To\b/ }), 'kept@acme.example');

    await u.click(screen.getByRole('button', { name: /next record/i }));
    await screen.findByTestId('step-found');
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
    // The subject is the modal title and the headline of What was found.
    expect((await screen.findAllByText('Spend to date against budget')).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /next record/i })).toBeDisabled();

    await u.click(screen.getByRole('button', { name: /previous record/i }));
    await screen.findByTestId('step-found');
    expect(screen.getByText('1 of 2')).toBeInTheDocument();
    await loaded();
    await next(u);
    expect(((await screen.findByRole('textbox', { name: /^To\b/ })) as HTMLInputElement).value).toBe(
      'kept@acme.example'
    );
  });

  it('an item that cannot be loaded says so and can be tried again; browsing past it still works', async () => {
    const u = user();
    const items = [pathBItem, thresholdItem];
    const loadItem = jest
      .fn()
      .mockRejectedValueOnce(new Error('The item could not be read.'))
      .mockImplementation(async (id: string) => items.find(i => i.row.signalId === id));
    setup(items, PATH_B_ID, { loadItem });
    expect(await screen.findByTestId('item-error')).toHaveTextContent('The item could not be read.');
    await u.click(button('Try again'));
    await loaded();
  });
});

describe('a failed record lists the writes that landed and writes no record (D-17)', () => {
  const failure = (overrides: Partial<DecisionCommitFailure> = {}): DecisionCommitFailure => ({
    httpStatus: 500,
    title: 'The decision was not recorded',
    detail: 'Step 3 failed.',
    reasonCode: 'ontology.decision.step_failed',
    correlationId: 'trace-9',
    failedStep: 3,
    landed: [{ entity: 'sprk_budgetrevision', id: 'rev-1' }],
    possiblyLanded: [],
    steps: [
      {
        key: 'revise-budget',
        code: 'revise-budget',
        phase: 3,
        status: 'Done',
        written: [{ entity: 'sprk_budgetrevision', id: 'rev-1' }],
        possiblyWritten: [],
      },
    ],
    recordMayExist: false,
    sentButNotRecorded: false,
    resumable: true,
    replayed: false,
    ...overrides,
  });

  async function toConfirm(u: ReturnType<typeof user>) {
    await loaded();
    await next(u);
    enter(screen.getByRole('textbox', { name: /^Approved overage/ }), '10');
    await next(u);
    await u.click(button('Skip'));
    await next(u);
    await screen.findByTestId('step-confirm');
  }

  it('shows the landed writes and no record, then retries the SAME review with retry set', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(new DecisionCommitError(failure()));
    await toConfirm(u);
    await u.click(button('Record decision'));
    const panel = await screen.findByTestId('commit-failure');
    // Plain words: the action that was carried out, no entity name, no id.
    expect(within(panel).getByTestId('landed-writes')).toHaveTextContent('Revise budget');
    expect(panel.textContent).not.toMatch(/sprk_|rev-1|HTTP/);
    expect(panel).toHaveTextContent('The decision was not recorded');
    expect(within(panel).getByTestId('record-status')).toHaveTextContent('No Decision Record was written.');
    expect(panel).toHaveTextContent('trace-9');
    // Nothing flipped: still editable, still the Confirm step.
    expect(screen.queryByTestId('status-bar')).toBeNull();

    await u.click(button('Retry recording'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(2));
    const first = service.commit.mock.calls[0][1] as DecisionReview;
    const second = service.commit.mock.calls[1][1] as DecisionReview;
    expect(second.reviewId).toBe(first.reviewId);
    expect(first.retry).toBeUndefined();
    expect(second.retry).toBe(true);
    await screen.findByTestId('status-bar');
  });

  it('a send that went out and cannot be resumed offers no second attempt', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(
      new DecisionCommitError(
        failure({
          resumable: false,
          sentButNotRecorded: true,
          landed: [],
          possiblyLanded: [{ entity: 'sprk_communication', id: 'm-1' }],
          steps: [],
        })
      )
    );
    await toConfirm(u);
    await u.click(button('Record decision'));
    const panel = await screen.findByTestId('commit-failure');
    expect(panel).toHaveTextContent('A message was sent but the decision was not recorded.');
    expect(within(panel).getByTestId('possibly-landed-writes')).toHaveTextContent('Message');
    expect(panel.textContent).not.toMatch(/sprk_|m-1/);
    expect(button('Record decision')).toBeDisabled();
  });

  it('an unknown outcome (no answer) offers Try again with the same review id', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(
      new DecisionCommitError(
        failure({
          httpStatus: 0,
          resumable: false,
          landed: [],
          title: 'The decision may not have been recorded',
          detail: 'No answer.',
        })
      )
    );
    await toConfirm(u);
    await u.click(button('Record decision'));
    expect(await screen.findByTestId('record-status')).toHaveTextContent('not known whether the decision was recorded');
    await u.click(button('Try again'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(2));
    expect((service.commit.mock.calls[1][1] as DecisionReview).reviewId).toBe(
      (service.commit.mock.calls[0][1] as DecisionReview).reviewId
    );
    expect((service.commit.mock.calls[1][1] as DecisionReview).retry).toBeUndefined();
  });

  it('after a plain refusal, editing the review gives it a fresh review id', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(
      new DecisionCommitError(
        failure({ httpStatus: 422, resumable: false, landed: [], title: 'Unprocessable Entity', detail: 'Refused.' })
      )
    );
    await toConfirm(u);
    await u.click(button('Record decision'));
    const refusal = await screen.findByTestId('commit-failure');
    expect(refusal).toHaveTextContent('The decision was refused');
    expect(refusal.textContent).not.toMatch(/Unprocessable|Conflict|HTTP/);
    await u.click(button('Back'));
    await u.click(button('Back'));
    await u.click(button('Back'));
    await screen.findByTestId('step-action-approve-variance');
    enter(screen.getByRole('textbox', { name: /^Approved overage/ }), '');
    enter(screen.getByRole('textbox', { name: /^Approved overage/ }), '20');
    await next(u);
    await u.click(button('Skip'));
    await next(u);
    await u.click(await screen.findByRole('button', { name: /^record decision$/i }));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(2));
    expect((service.commit.mock.calls[1][1] as DecisionReview).reviewId).not.toBe(
      (service.commit.mock.calls[0][1] as DecisionReview).reviewId
    );
  });
});

describe('a decided item is read-only (replay)', () => {
  it('opens on its last step under the status bar with the fact snapshot, not today’s data', async () => {
    setup([decidedItem]);
    const confirm = await screen.findByTestId('step-confirm');
    const bar = screen.getByTestId('status-bar');
    expect(bar).toHaveAttribute('data-state', 'Decided');
    expect(bar).toHaveTextContent('DR-00042');
    expect(bar).toHaveTextContent('Pat Reviewer');
    expect(bar.compareDocumentPosition(confirm) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(confirm).getByTestId('offered-actions')).toHaveTextContent('Approve variance');
    // No edit / delete / record affordance for a record (R-13, FR-21).
    expect(screen.queryByRole('button', { name: /edit|delete|record decision|dismiss and record/i })).toBeNull();
    expect(screen.queryByRole('textbox')).toBeNull();

    const user_ = user();
    // Confirm -> Next steps -> Approve variance -> Revise budget -> Send budget inquiry -> What was found
    for (let i = 0; i < 5; i++) await user_.click(button('Back'));
    const found = await screen.findByTestId('step-found');
    expect(within(found).getByTestId('snapshot-note')).toBeInTheDocument();
    expect(found).toHaveTextContent('Invoice 4411 dated 3 Oct 2026 (as recorded)');
    expect(found).not.toHaveTextContent('TODAYS VALUE');
  });

  it('Back walks the values actually sent, per action', async () => {
    const u = user();
    setup([decidedItem]);
    await screen.findByTestId('step-confirm');
    // Confirm -> Next steps -> Approve variance -> Revise budget
    await u.click(button('Back'));
    expect(await screen.findByTestId('step-next')).toHaveTextContent('Add To Do');
    await u.click(button('Back'));
    const approve = await screen.findByTestId('step-action-approve-variance');
    expect(within(approve).getByTestId('taken-badge')).toHaveTextContent('Skipped');
    await u.click(button('Back'));
    const revise = await screen.findByTestId('step-action-revise-budget');
    expect(within(revise).getByTestId('taken-badge')).toHaveTextContent('Taken');
    const values = within(revise).getByTestId('sent-values');
    expect(values).toHaveTextContent('Reason for revision');
    expect(values).toHaveTextContent('Appeal added');
    expect(values.textContent).toMatch(/250[,. ]?000/);
    expect(values.textContent).not.toContain(BUDGET_A.id);
    await u.click(button('Back'));
    const inquiry = await screen.findByTestId('step-action-send-budget-inquiry');
    expect(within(inquiry).getByTestId('sent-values')).toHaveTextContent('budgets@acme.example');
    expect(screen.queryByTestId('recommended-badge')).not.toBeNull();
  });

  it('a dismissed item shows the dismissal reason in the status bar', async () => {
    setup([dismissedItem]);
    const bar = await screen.findByTestId('status-bar');
    expect(bar).toHaveAttribute('data-state', 'Dismissed');
    expect(bar).toHaveTextContent('Reason: Already approved offline');
    expect(await screen.findByTestId('dismissal-reason')).toHaveTextContent('Already approved offline');
  });

  it('an item that closed without a person deciding it shows only what was found, with who replaced it', async () => {
    setup([supersededItem]);
    const bar = await screen.findByTestId('status-bar');
    expect(bar).toHaveAttribute('data-state', 'Superseded');
    expect(bar).toHaveTextContent('Superseded by DR-00050');
    expect(await screen.findByTestId('step-found')).toBeInTheDocument();
    expect(stepLabels()).toEqual(['What was found']);
  });
});

describe('D-53: drafts are templates, not AI', () => {
  it('makes no call but the plan, the dismissal count, the item read and the one commit; shows no Assistant-draft badge', async () => {
    const u = user();
    const { service, loadItem } = setup([pathBItem]);
    await loaded();
    await next(u);
    await screen.findByTestId('step-action-send-budget-inquiry');
    expect(document.body.textContent).not.toMatch(/assistant draft|drafted by|ai draft/i);
    expect(service.getPlan).toHaveBeenCalledTimes(1);
    expect(service.getSuppression).toHaveBeenCalledTimes(1);
    expect(service.commit).not.toHaveBeenCalled();
    expect(loadItem).toHaveBeenCalledTimes(1);
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Findings of the independent review of task 058 (each of these failed before its fix)
// ---------------------------------------------------------------------------------------------------------------------

const otherItem: DecisionWizardItem = {
  row: otherRow,
  core: matterCore,
  sentence: SENTENCE,
  evidence: [],
  otherOpenItems: [],
};

const failedOutcome = (overrides: Partial<DecisionCommitFailure> = {}): DecisionCommitFailure => ({
  httpStatus: 500,
  title: 'The decision was not recorded',
  detail: 'Step 3 failed.',
  reasonCode: 'ontology.decision.step_failed',
  correlationId: 'trace-1',
  failedStep: 3,
  landed: [{ entity: 'sprk_budgetrevision', id: 'rev-1' }],
  possiblyLanded: [],
  steps: [],
  recordMayExist: false,
  sentButNotRecorded: false,
  resumable: true,
  replayed: false,
  ...overrides,
});

const reviewShape = (r: DecisionReview) => ({ actions: r.actions, nextSteps: r.nextSteps, dismissal: r.dismissal });

/** The threshold item: Approve variance (10), skip Send budget inquiry, Confirm. */
async function thresholdToConfirm(u: ReturnType<typeof user>) {
  await loaded();
  await next(u);
  enter(screen.getByRole('textbox', { name: /^Approved overage/ }), '10');
  await next(u);
  await u.click(button('Skip'));
  await next(u);
  await screen.findByTestId('step-confirm');
}

/** The Path B item with every action skipped: the dismissal Confirm. */
async function pathBToDismissal(u: ReturnType<typeof user>) {
  await loaded();
  await next(u);
  for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
  await next(u);
  await screen.findByTestId('step-confirm');
}

describe('a failed attempt pins the review (duplicate-write hazard)', () => {
  it('keeps the review id and takes retry from the pinned failure through a conflict, and locks the steps', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(new DecisionCommitError(failedOutcome())).mockRejectedValueOnce(
      new DecisionCommitError(
        failedOutcome({
          httpStatus: 409,
          resumable: false,
          landed: [],
          reasonCode: 'ontology.decision.review_in_progress',
        })
      )
    );
    await thresholdToConfirm(u);
    await u.click(button('Record decision'));
    await screen.findByTestId('commit-failure');

    // The steps are read-only now: the route has seen this decision, and a changed one under the same id is refused.
    await u.click(button('Back'));
    await u.click(button('Back'));
    await u.click(button('Back'));
    await screen.findByTestId('step-action-approve-variance');
    expect(screen.getByRole('textbox', { name: /^Approved overage/ })).toBeDisabled();
    expect(screen.getByTestId('locked-note')).toBeInTheDocument();
    await next(u);
    await u.click(button('Skip'));
    await next(u);
    await screen.findByTestId('step-confirm');

    await u.click(button('Retry recording'));
    await waitFor(() => expect(screen.getByTestId('commit-failure')).toHaveTextContent('already being recorded'));
    // The conflict says nothing about whether the FIRST attempt can be resumed: the next try still resumes it.
    await u.click(button('Retry recording'));
    await screen.findByTestId('status-bar');

    const reviews = service.commit.mock.calls.map(c => c[1] as DecisionReview);
    expect(reviews).toHaveLength(3);
    expect(new Set(reviews.map(r => r.reviewId)).size).toBe(1);
    expect(reviews.map(r => r.retry)).toEqual([undefined, true, true]);
    expect(reviews.map(reviewShape)).toEqual([
      reviewShape(reviews[0]),
      reviewShape(reviews[0]),
      reviewShape(reviews[0]),
    ]);
  });

  it('a Failed outcome with nothing landed also keeps the id (a new id would run landed steps again)', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(
      new DecisionCommitError(failedOutcome({ landed: [], resumable: false, possiblyLanded: [] }))
    );
    await thresholdToConfirm(u);
    await u.click(button('Record decision'));
    await screen.findByTestId('commit-failure');
    await u.click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(2));
    const [first, second] = service.commit.mock.calls.map(c => c[1] as DecisionReview);
    expect(second.reviewId).toBe(first.reviewId);
    expect(second.retry).toBeUndefined();
  });
});

describe('a pinned review is posted exactly as first sent', () => {
  it('an item that closed before the first attempt stays out of the retry, so the content (and the id) still match', async () => {
    const u = user();
    const { service } = setup([pathBItem, otherItem]);
    await pathBToDismissal(u);
    enter(screen.getByRole('combobox', { name: /reason for dismissing/i }), 'already-approved-offline');
    await u.click(screen.getByRole('checkbox', { name: /second invoice/i }));

    // The ticked item is decided on its own, here, before this one is recorded.
    await u.click(screen.getByRole('button', { name: /next record/i }));
    await pathBToDismissal(u);
    enter(screen.getByRole('combobox', { name: /reason for dismissing/i }), 'already-approved-offline');
    await u.click(button('Dismiss and record'));
    await screen.findByTestId('status-bar');
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    service.commit.mockClear();
    service.commit.mockRejectedValueOnce(new DecisionCommitError(failedOutcome({ landed: [] })));

    await u.click(screen.getByRole('button', { name: /previous record/i }));
    await screen.findByTestId('step-found');
    // The shell's step rings start fresh on a remount; the draft keeps the choices, so each action is skipped again.
    await next(u);
    for (let i = 0; i < 3; i++) await u.click(await screen.findByRole('button', { name: /^skip$/i }));
    await next(u);
    await screen.findByTestId('step-confirm');
    await u.click(button('Dismiss and record'));
    await screen.findByTestId('commit-failure');
    await u.click(button('Retry recording'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(2));

    const [first, second] = service.commit.mock.calls.map(c => c[1] as DecisionReview);
    expect(first.alsoResolve ?? []).toEqual([]);
    expect(second.alsoResolve ?? []).toEqual([]);
    expect(second.reviewId).toBe(first.reviewId);
    expect(second.retry).toBe(true);
  }, 30000);
});

describe('recording lands on Confirm', () => {
  it('after Record the closed item is on its Confirm step', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    await thresholdToConfirm(u);
    await u.click(button('Record decision'));
    const bar = await screen.findByTestId('status-bar');
    expect(service.commit).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(bar).toHaveFocus());
    const confirm = await screen.findByTestId('step-confirm');
    expect(within(confirm).getByTestId('offered-actions')).toHaveTextContent('Approve variance');
    expect(screen.queryByTestId('step-action-send-budget-inquiry')).toBeNull();
    expect(screen.queryByTestId('step-found')).toBeNull();
    // Back still walks the steps that were sent, from the last step (a closed item lists every action the plan offered).
    await u.click(button('Back'));
    expect(await screen.findByTestId('step-action-send-budget-inquiry')).toBeInTheDocument();
  });
});

describe('one action that excludes several later steps', () => {
  it('Next on Mark complete removes both Reschedule and Reassign and lands on what follows', async () => {
    const u = user();
    setup([doItem]);
    await loaded();
    await next(u);
    await screen.findByTestId('step-action-mark-complete');
    await next(u);
    expect(await screen.findByTestId('step-next')).toBeInTheDocument();
    await waitFor(() => expect(stepLabels()).toEqual(['What was found', 'Mark complete', 'Next steps', 'Confirm']));
    await next(u);
    const confirm = await screen.findByTestId('step-confirm');
    expect(within(confirm).getByTestId('offered-reschedule')).toHaveTextContent(
      'Cannot be combined with Mark complete'
    );
    expect(within(confirm).getByTestId('offered-reassign')).toHaveTextContent('Cannot be combined with Mark complete');
  });
});

describe('dismissal detail follows its reason', () => {
  it('detail typed for Other is not posted once another reason is chosen', async () => {
    const u = user();
    const { service } = setup([pathBItem]);
    await pathBToDismissal(u);
    const reason = screen.getByRole('combobox', { name: /reason for dismissing/i });
    enter(reason, 'other');
    enter(screen.getByRole('textbox', { name: /^Detail/ }), 'stale words');
    enter(reason, 'already-approved-offline');
    expect(screen.queryByRole('textbox', { name: /^Detail/ })).toBeNull();
    await u.click(button('Dismiss and record'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    expect((service.commit.mock.calls[0][1] as DecisionReview).dismissal).toEqual({ code: 'already-approved-offline' });
  });
});

describe('what the route says it closed', () => {
  it('an item it left open stays open and its warning is shown in words', async () => {
    const u = user();
    const { service } = setup([pathBItem, otherItem]);
    service.commit.mockResolvedValueOnce(
      commitResult({
        signals: [
          { signalId: PATH_B_ID, state: 'Dismissed' },
          { signalId: OTHER_ID, state: 'Unchanged', warning: 'signal_close_failed' },
        ],
      })
    );
    await pathBToDismissal(u);
    enter(screen.getByRole('combobox', { name: /reason for dismissing/i }), 'already-approved-offline');
    await u.click(screen.getByRole('checkbox', { name: /second invoice/i }));
    await u.click(button('Dismiss and record'));
    const bar = await screen.findByTestId('status-bar');
    expect((service.commit.mock.calls[0][1] as DecisionReview).alsoResolve).toEqual([OTHER_ID]);
    // The other item was not closed: it is still counted, and the person is told.
    expect(bar).toHaveTextContent('1 other open item left');
    expect(screen.getByTestId('record-warnings')).toHaveTextContent('An item could not be closed and was left open.');
    expect(screen.getByTestId('record-warnings').textContent).not.toMatch(/signal_close_failed/);
    expect(button('Next open item')).toBeInTheDocument();
  });

  it('an inquiry that was sent leaves the item open: the bar says so', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockResolvedValueOnce(commitResult({ signals: [{ signalId: THRESHOLD_ID, state: 'InquirySent' }] }));
    await thresholdToConfirm(u);
    await u.click(button('Record decision'));
    const bar = await screen.findByTestId('status-bar');
    expect(bar).toHaveAttribute('data-state', 'Open');
    expect(bar).toHaveTextContent('Inquiry sent, still open');
    expect(screen.getByTestId('left-open')).toBeInTheDocument();
  });

  it('an item recorded here is no longer listed as another open item on the item that named it', async () => {
    const u = user();
    setup([pathBItem, otherItem], OTHER_ID);
    await pathBToDismissal(u);
    enter(screen.getByRole('combobox', { name: /reason for dismissing/i }), 'already-approved-offline');
    await u.click(button('Dismiss and record'));
    await screen.findByTestId('status-bar');
    await u.click(screen.getByRole('button', { name: /previous record/i }));
    const found = await screen.findByTestId('step-found');
    expect(within(found).queryByTestId('other-open-items')).toBeNull();
  });
});

describe('words a person can act on', () => {
  it('a refusal reads as a refusal, with the changed-review text rewritten', async () => {
    const u = user();
    const { service } = setup([thresholdItem]);
    service.commit.mockRejectedValueOnce(
      new DecisionCommitError(
        failedOutcome({
          httpStatus: 422,
          title: 'Unprocessable Entity',
          detail: 'This review id was already used for a different decision.',
          reasonCode: 'ontology.decision.review_changed',
          landed: [],
          resumable: false,
        })
      )
    );
    await thresholdToConfirm(u);
    await u.click(button('Record decision'));
    const panel = await screen.findByTestId('commit-failure');
    expect(panel).toHaveTextContent('The decision was refused');
    expect(panel).toHaveTextContent('Put your choices back as they were');
    expect(panel.textContent).not.toMatch(/review id|Unprocessable|ontology\./);
  });
});

describe('an amount is never read differently from how it was meant', () => {
  it('a comma that could be a decimal comma is refused, so Next stays disabled', async () => {
    const u = user();
    setup([thresholdItem]);
    await loaded();
    await next(u);
    const amount = screen.getByRole('textbox', { name: /^Approved overage/ });
    enter(amount, '1,5');
    expect(button('Next')).toBeDisabled();
    expect(screen.getByText(/digits with a period/i)).toBeInTheDocument();
    enter(amount, '1,250.50');
    expect(button('Next')).toBeEnabled();
  });
});

describe('a decided item whose plan cannot be read', () => {
  it('still opens read-only with its status bar and snapshot, without per-action steps', async () => {
    const service = makeService({ plans: {} });
    renderWizard({
      service,
      loadItem: makeLoader([decidedItem]),
      itemIds: [DECIDED_ID],
      startItemId: DECIDED_ID,
    });
    expect(await screen.findByTestId('status-bar')).toHaveAttribute('data-state', 'Decided');
    expect(await screen.findByTestId('step-confirm')).toBeInTheDocument();
    expect(screen.queryByTestId('item-error')).toBeNull();
  });
});

describe('a closed item whose Decision Record was not supplied (the worklist closed sets, task 059)', () => {
  // The Signal read route returns how an item closed, not the record. Claiming "Skipped" for every action would be false.
  const noRecord = {
    ...decidedItem,
    resolution: { resolutionType: 'Acted' as const, decidedOn: '2026-10-09T09:00:00Z' },
  };

  it('opens read-only on What was found under its status bar, with no per-action steps to replay', async () => {
    const service = makeService({ plans });
    renderWizard({ service, loadItem: makeLoader([noRecord]), itemIds: [DECIDED_ID], startItemId: DECIDED_ID });
    expect(await screen.findByTestId('status-bar')).toHaveAttribute('data-state', 'Decided');
    expect(await screen.findByTestId('step-found')).toBeInTheDocument();
    expect(screen.queryByTestId('step-confirm')).toBeNull();
    expect(screen.queryByTestId('taken-badge')).toBeNull();
    expect(screen.queryByRole('textbox')).toBeNull();
  });

  it('a record with steps is still replayed (the guard is on a missing record only)', async () => {
    setup([decidedItem]);
    expect(await screen.findByTestId('step-confirm')).toBeInTheDocument();
  });

  it('a suppressed item reads "To decide" with its own note, read-only', async () => {
    const suppressed = {
      ...decidedItem,
      resolution: {
        resolutionType: 'ConditionCleared' as const,
        leftOpen: true,
        note: 'Not raised again until 28 Oct',
      },
    };
    const service = makeService({ plans });
    renderWizard({ service, loadItem: makeLoader([suppressed]), itemIds: [DECIDED_ID], startItemId: DECIDED_ID });
    const bar = await screen.findByTestId('status-bar');
    expect(bar).toHaveAttribute('data-state', 'Open');
    expect(bar).toHaveTextContent('Not raised again until 28 Oct');
    expect(bar).not.toHaveTextContent('Inquiry sent');
  });

  it('a withheld evidence line reads "Details withheld (restricted record)", not "No value recorded"', async () => {
    const withheld = {
      ...noRecord,
      evidence: [
        { kind: 'sprk_communication', ref: 'c1', tier: 'Interpretation' as const, text: null, withheld: true },
      ],
    };
    const service = makeService({ plans });
    renderWizard({ service, loadItem: makeLoader([withheld]), itemIds: [DECIDED_ID], startItemId: DECIDED_ID });
    const found = await screen.findByTestId('step-found');
    expect(within(found).getByTestId('evidence-line')).toHaveTextContent('Details withheld (restricted record)');
    expect(found).not.toHaveTextContent('No value recorded');
  });
});

/** The BFF's AI routes are /api/ai/... (chat, analysis, capabilities); /api/v1/ai/... is the versioned form. */
const AI_ROUTE = /openai|assistant draft|\/api\/(v1\/)?ai(\/|\b)/i;

describe('ADR-021 / D-26 guards', () => {
  it('the AI-route scan fires on the real AI routes and not on the signals routes (control)', () => {
    expect(AI_ROUTE.test("fetch('/api/ai/chat/sessions')")).toBe(true);
    expect(AI_ROUTE.test('`${base}/api/v1/ai/analysis`')).toBe(true);
    expect(AI_ROUTE.test('new OpenAI()')).toBe(true);
    expect(AI_ROUTE.test('/api/v1/signals/1/decision-plan')).toBe(false);
  });

  const dir = path.resolve(__dirname, '..');
  const sources = (): Array<{ file: string; text: string }> => {
    const out: Array<{ file: string; text: string }> = [];
    const walk = (d: string) => {
      for (const entry of fs.readdirSync(d, { withFileTypes: true })) {
        if (entry.name === '__tests__' || entry.name === '__fixtures__') continue;
        const full = path.join(d, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (/\.(ts|tsx)$/.test(entry.name))
          out.push({ file: path.relative(dir, full), text: fs.readFileSync(full, 'utf8') });
      }
    };
    walk(dir);
    return out;
  };

  it('is never opened with navigateTo or WizardModal and builds no Dialog of its own', () => {
    for (const { file, text } of sources()) {
      const code = text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
      expect({ file, navigateTo: /navigateTo/.test(code) }).toEqual({ file, navigateTo: false });
      expect({ file, wizardModal: /WizardModal/.test(code) }).toEqual({ file, wizardModal: false });
      expect({ file, dialog: /<Dialog\b|DialogSurface/.test(code) }).toEqual({ file, dialog: false });
    }
  });

  it('uses Fluent tokens only: no hex, rgb(), hsl() or named-colour literal', () => {
    for (const { file, text } of sources()) {
      const code = text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
      expect({ file, hex: /#[0-9a-fA-F]{3,8}\b/.test(code) }).toEqual({ file, hex: false });
      expect({ file, fn: /\b(rgb|rgba|hsl|hsla)\(/.test(code) }).toEqual({ file, fn: false });
      expect({ file, named: /:\s*['"](white|black|red|green|blue|gray|grey)['"]/.test(code) }).toEqual({
        file,
        named: false,
      });
    }
  });

  it('has no AI call and no draft badge in source', () => {
    for (const { file, text } of sources()) {
      const code = text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
      expect({ file, ai: AI_ROUTE.test(code) }).toEqual({ file, ai: false });
    }
  });

  it('renders under a dark theme', async () => {
    const service = makeService({ plans });
    render(
      <FluentProvider theme={webDarkTheme}>
        <DecisionWizard
          service={service}
          loadItem={makeLoader([pathBItem])}
          itemIds={[PATH_B_ID]}
          startItemId={PATH_B_ID}
          onClose={jest.fn()}
          today={TODAY}
        />
      </FluentProvider>
    );
    expect(await screen.findByTestId('step-found')).toBeInTheDocument();
  });
});
