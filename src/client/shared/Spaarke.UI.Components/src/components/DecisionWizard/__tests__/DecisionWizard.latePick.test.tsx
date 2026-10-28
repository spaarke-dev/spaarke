/**
 * DecisionWizard.latePick - a side-pane pick that is released after the person moved on lands in the item it was made for
 * (ontology task 165, fix round 1, F1-a). The dialog is one mounted component for the whole session, so the draft updater a
 * step holds must be bound to the item it was rendered for, not to whichever item is current when the pick comes back.
 */

import '@testing-library/jest-dom';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import type { DecisionOpenLookup, DecisionWizardItem } from '../decisionWizardTypes';
import { BUDGET_A, BUDGET_B, PATH_B_ID, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeLookup, makeService, renderWizard } from './decisionTestKit';

const SECOND_ID = '66666666-6666-6666-6666-666666666666';
const secondItem: DecisionWizardItem = { ...pathBItem, row: { ...pathBItem.row, signalId: SECOND_ID } };
const plans = { [PATH_B_ID]: pathBPlan, [SECOND_ID]: pathBPlan };

const exact = (name: string) => new RegExp('^' + name + '$', 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const click = (element: Element) => fireEvent.click(element);
const enter = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } });
const budgetField = () => screen.getByRole('textbox', { name: /^Budget to revise/ });

/** Found -> Send budget inquiry (skipped) -> Revise budget. */
async function toReviseBudget() {
  await screen.findByTestId('step-found');
  click(button('Next'));
  await screen.findByTestId('step-action-send-budget-inquiry');
  click(button('Skip'));
  await screen.findByTestId('step-action-revise-budget');
}

/** A pane whose pick is held until the test releases it. */
function heldPane() {
  let release: (picked: { id: string; name: string } | null) => void = () => undefined;
  const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(
    () =>
      new Promise(resolve => {
        release = resolve;
      })
  );
  return {
    openLookup,
    release: async (picked: { id: string; name: string } | null) => {
      await act(async () => {
        release(picked);
      });
    },
  };
}

/** A loader that answers the first item at once and holds the second until released. */
function secondHeld() {
  const base = makeLoader([pathBItem, secondItem]);
  let open: () => void = () => undefined;
  const gate = new Promise<void>(resolve => {
    open = resolve;
  });
  const loadItem = jest.fn(async (id: string) => {
    if (id === SECOND_ID) await gate;
    return base(id);
  });
  return { loadItem, open: () => act(async () => open()) };
}

describe('a late side-pane pick belongs to the item it was made for', () => {
  it('B ready with the same action: the pick fills A, and B stays empty', async () => {
    const pane = heldPane();
    renderWizard({
      service: makeService({ plans }),
      loadItem: makeLoader([pathBItem, secondItem]),
      itemIds: [PATH_B_ID, SECOND_ID],
      startItemId: PATH_B_ID,
      openLookup: pane.openLookup,
      lookup: makeLookup([BUDGET_A, BUDGET_B]),
    });
    await toReviseBudget();
    click(budgetField());
    await waitFor(() => expect(pane.openLookup).toHaveBeenCalledTimes(1));

    // Move to item B while the pane is still open, and stand on the same step.
    click(screen.getByRole('button', { name: /next record/i }));
    await screen.findByText('2 of 2');
    await toReviseBudget();
    expect(budgetField()).toHaveValue('');

    await pane.release(BUDGET_B);
    expect(budgetField()).toHaveValue('');

    // The pick is in A's draft.
    click(screen.getByRole('button', { name: /previous record/i }));
    await screen.findByText('1 of 2');
    await toReviseBudget();
    expect(budgetField()).toHaveValue(BUDGET_B.name);
  }, 30000);

  it('B still loading: the pick is not dropped, it fills A', async () => {
    const pane = heldPane();
    const held = secondHeld();
    renderWizard({
      service: makeService({ plans }),
      loadItem: held.loadItem,
      itemIds: [PATH_B_ID, SECOND_ID],
      startItemId: PATH_B_ID,
      openLookup: pane.openLookup,
      lookup: makeLookup([BUDGET_A, BUDGET_B]),
    });
    await toReviseBudget();
    click(budgetField());
    await waitFor(() => expect(pane.openLookup).toHaveBeenCalledTimes(1));

    click(screen.getByRole('button', { name: /next record/i }));
    await screen.findByTestId('item-loading');
    await pane.release(BUDGET_B);
    await held.open();
    await toReviseBudget();
    expect(budgetField()).toHaveValue('');

    click(screen.getByRole('button', { name: /previous record/i }));
    await screen.findByText('1 of 2');
    await toReviseBudget();
    expect(budgetField()).toHaveValue(BUDGET_B.name);
  }, 30000);

  it('must-not-fire: a pick released while still on the item fills that item', async () => {
    const pane = heldPane();
    renderWizard({
      service: makeService({ plans }),
      loadItem: makeLoader([pathBItem, secondItem]),
      itemIds: [PATH_B_ID, SECOND_ID],
      startItemId: PATH_B_ID,
      openLookup: pane.openLookup,
      lookup: makeLookup([BUDGET_A, BUDGET_B]),
    });
    await toReviseBudget();
    enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Appeal added');
    click(budgetField());
    await waitFor(() => expect(pane.openLookup).toHaveBeenCalledTimes(1));
    await pane.release(BUDGET_B);
    await waitFor(() => expect(budgetField()).toHaveValue(BUDGET_B.name));
  }, 30000);
});
