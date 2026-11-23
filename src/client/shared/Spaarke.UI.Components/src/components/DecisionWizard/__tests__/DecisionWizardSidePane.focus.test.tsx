/**
 * DecisionWizard side-pane lookup focus return - task 200.
 *
 * After the host's side-pane lookup behind a Person or Lookup parameter settles (pick, cancel or failure), keyboard
 * focus goes back to that field's input: the platform pane leaves focus on <body> or on the link behind the dialog.
 * Focus the user or host put on another control inside the wizard on purpose is left alone.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import type { DecisionOpenLookup } from '../decisionWizardTypes';
import {
  BUDGET_A,
  BUDGET_B,
  DO_ID,
  PATH_B_ID,
  doPlan,
  pathBItem,
  pathBPlan,
} from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeLookup, makeService, renderWizard } from './decisionTestKit';

const plans = { [PATH_B_ID]: pathBPlan, [DO_ID]: doPlan };
const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const budgetField = () => screen.getByRole('textbox', { name: /^Budget to revise/ });

async function openReviseBudget(openLookup: DecisionOpenLookup) {
  const service = makeService({ plans });
  renderWizard({
    service,
    loadItem: makeLoader([pathBItem]),
    itemIds: [pathBItem.row.signalId],
    startItemId: pathBItem.row.signalId,
    openLookup,
    lookup: makeLookup([BUDGET_A, BUDGET_B]),
  });
  await screen.findByTestId('step-found');
  fireEvent.click(button('Next'));
  await screen.findByTestId('step-action-send-budget-inquiry');
  fireEvent.click(button('Skip'));
  await screen.findByTestId('step-action-revise-budget');
}

describe('DecisionWizard side-pane lookup: focus returns to the field input (task 200)', () => {
  let outside: HTMLButtonElement;
  beforeEach(() => {
    outside = document.createElement('button');
    document.body.appendChild(outside);
  });
  afterEach(() => outside.remove());

  it('pick: the field shows the pick and focus ends on its input', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => {
      outside.focus();
      return BUDGET_B;
    });
    await openReviseBudget(openLookup);

    fireEvent.click(budgetField());

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(budgetField()).toHaveValue(BUDGET_B.name));
    await waitFor(() => expect(document.activeElement).toBe(budgetField()));
  });

  it('cancel (null): focus ends on the field input', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => {
      outside.focus();
      return null;
    });
    await openReviseBudget(openLookup);

    fireEvent.click(budgetField());

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(document.activeElement).toBe(budgetField()));
  });

  it('failure (rejects): treated as a cancel, focus ends on the field input', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => {
      outside.focus();
      throw new Error('no Xrm host');
    });
    await openReviseBudget(openLookup);

    fireEvent.click(budgetField());

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(document.activeElement).toBe(budgetField()));
  });

  it('does not move focus that is on another control inside the wizard when the lookup settles', async () => {
    const reason = () => screen.getByRole('textbox', { name: /^Reason for revision/ });
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => {
      reason().focus(); // put there on purpose, inside the same dialog
      return null;
    });
    await openReviseBudget(openLookup);

    fireEvent.click(budgetField());

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 30));
    });
    expect(document.activeElement).toBe(reason());
  });
});
