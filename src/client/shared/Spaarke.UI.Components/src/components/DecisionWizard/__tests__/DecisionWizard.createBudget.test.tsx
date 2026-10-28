/**
 * DecisionWizard - the Create budget step (task 167; D-193, owner UAT round 2 items 5 and 6). The stated contract:
 *  - the step shows Category, Year, Start date, End date, Period and Currency, built from the plan's parameters (no switch on the action)
 *  - Category, Period and Currency are selects over the options the PLAN carries (live metadata, resolved server side)
 *  - Currency starts at the plan's default (the matter's business unit currency), marked as prefilled, and can be changed
 *  - an End date before the Start date says so and keeps the step from going on
 *  - Record decision posts the values, the currency included
 *  - must-not-fire: Revise budget's step has none of these fields
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { DecisionReview } from '../decisionWizardTypes';
import { DOLLAR_ID, EURO_ID, PATH_B_ID, createBudgetPlan, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const enter = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } });
const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });

function setup(plan = createBudgetPlan) {
  const service = makeService({ plans: { [PATH_B_ID]: plan } });
  const view = renderWizard({
    service,
    loadItem: makeLoader([pathBItem]),
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
  });
  return { service, ...view };
}

async function openStep() {
  await screen.findByTestId('step-found');
  fireEvent.click(button('Next'));
  return screen.findByTestId('step-action-create-budget');
}

const select = (label: RegExp) => screen.getByRole('combobox', { name: label }) as HTMLSelectElement;

describe('the Create budget step', () => {
  it('shows the budget fields in the plan order, with the plan options in the selects', async () => {
    setup();
    await openStep();

    const fields = within(screen.getByTestId('parameter-fields'));
    expect(fields.getAllByTestId(/^field-/).map(f => f.getAttribute('data-testid'))).toEqual([
      'field-amount',
      'field-category',
      'field-year',
      'field-startDate',
      'field-endDate',
      'field-period',
      'field-currency',
      'field-reason',
    ]);
    expect(Array.from(select(/^Category/).options).map(o => o.textContent)).toEqual(['Choose...', 'Fees', 'Expenses']);
    expect(Array.from(select(/^Period/).options).map(o => o.textContent)).toEqual(['Choose...', 'Annual', 'Quarter 1']);
    expect(Array.from(select(/^Currency/).options).map(o => o.textContent)).toEqual(['Choose...', 'US Dollar (USD)', 'Euro (EUR)']);
  });

  it('starts Currency at the business unit default, marked as prefilled, and lets the user change it', async () => {
    setup();
    await openStep();

    expect(select(/^Currency/).value).toBe(EURO_ID);
    expect(within(screen.getByTestId('field-currency')).getByText(/Prefilled from this item/)).toBeInTheDocument();

    enter(select(/^Currency/), DOLLAR_ID);
    expect(select(/^Currency/).value).toBe(DOLLAR_ID);
    expect(within(screen.getByTestId('field-currency')).queryByText(/Prefilled from this item/)).toBeNull();
  });

  it('says so when the End date is before the Start date, and Next stays disabled until it is fixed', async () => {
    setup();
    await openStep();
    enter(screen.getByRole('textbox', { name: /^Budget amount/ }), '250000');
    enter(screen.getByRole('textbox', { name: /^Reason for the budget/ }), 'No budget was ever set');
    expect(button('Next')).toBeEnabled();

    enter(screen.getByLabelText(/^Start date/), '2026-06-02');
    enter(screen.getByLabelText(/^End date/), '2026-06-01');

    expect(within(screen.getByTestId('field-endDate')).getByText('End date cannot be before Start date.')).toBeInTheDocument();
    expect(button('Next')).toBeDisabled();

    enter(screen.getByLabelText(/^End date/), '2026-06-02');
    expect(screen.queryByText('End date cannot be before Start date.')).toBeNull();
    expect(button('Next')).toBeEnabled();
  });

  it('posts the entered fields and the currency with the review', async () => {
    const { service } = setup();
    await openStep();
    enter(screen.getByRole('textbox', { name: /^Budget amount/ }), '250000');
    enter(select(/^Category/), '100000001');
    enter(screen.getByRole('textbox', { name: /^Year/ }), '2026');
    enter(screen.getByLabelText(/^Start date/), '2026-01-01');
    enter(screen.getByLabelText(/^End date/), '2026-12-31');
    enter(select(/^Period/), '0');
    enter(screen.getByRole('textbox', { name: /^Reason for the budget/ }), 'No budget was ever set');
    fireEvent.click(button('Next'));
    await screen.findByTestId('step-confirm');
    fireEvent.click(button('Record decision'));

    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const review = service.commit.mock.calls[0][1] as DecisionReview;
    expect(review.actions).toEqual([
      {
        code: 'create-budget',
        taken: true,
        values: {
          amount: 250000,
          category: '100000001',
          year: '2026',
          startDate: '2026-01-01',
          endDate: '2026-12-31',
          period: '0',
          currency: EURO_ID,
          reason: 'No budget was ever set',
        },
      },
    ]);
  });
});

describe('must not fire', () => {
  it("Revise budget's step has none of the budget fields", async () => {
    setup(pathBPlan);
    await screen.findByTestId('step-found');
    fireEvent.click(button('Next'));
    enter(screen.getByRole('textbox', { name: /^To\b/ }), 'a@b.example');
    fireEvent.click(button('Next'));
    const revise = await screen.findByTestId('step-action-revise-budget');

    expect(within(revise).queryByTestId('field-currency')).toBeNull();
    expect(within(revise).queryByTestId('field-category')).toBeNull();
    expect(within(revise).queryByTestId('field-endDate')).toBeNull();
  });
});
