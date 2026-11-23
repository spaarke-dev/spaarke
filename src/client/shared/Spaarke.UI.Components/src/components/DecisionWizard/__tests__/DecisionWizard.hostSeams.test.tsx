/**
 * The optional host seams of the decision wizard added for the MCP widget (spaarkeai-mcp-bff-api-r1 task 032). Each is OFF by default, so
 * the Console's wizard is unchanged; these tests pin both sides of every seam:
 *  - `collectBecause` (FR-15): Confirm shows an optional "Because" box and the review carries it, trimmed; without the seam there is no box
 *    and no `because` member in the review, so the Console posts exactly what it did
 *  - `signalId` on the lookup request: the search is told which Work Item the wizard is deciding (the widget's tool is scoped by it)
 *  - `embedded`: the wizard renders in the page with no dialog (the widget's master-detail fallback); without it, it is the SprkModal wizard
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { PATH_B_ID, approveFirstPlan, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import { BUDGET_A, BUDGET_B } from '../__fixtures__/decisionWizardFixtures';
import type { DecisionReview } from '../decisionWizardTypes';
import {
  buildReview,
  emptyDraft,
  hasUnrecordedChoices,
  withBecause,
  withDecision,
  actionScope,
} from '../decisionDraft';
import { makeLoader, makeLookup, makeService, renderWizard } from './decisionTestKit';

const enter = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } });
const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const click = (element: Element) => {
  if (element instanceof HTMLInputElement) fireEvent.focus(element);
  fireEvent.click(element);
};

const plans = { [PATH_B_ID]: pathBPlan };

function setup(props: Partial<Parameters<typeof renderWizard>[0]> = {}) {
  const service = makeService({ plans });
  const lookup = makeLookup();
  const view = renderWizard({
    service,
    loadItem: makeLoader([pathBItem]),
    lookup,
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
    ...props,
  });
  return { service, lookup, ...view };
}

/** Skips every action to reach a dismissal's Confirm step, and picks the first reason. */
async function toDismissalConfirm() {
  await screen.findByTestId('step-found');
  click(button('Next'));
  for (let i = 0; i < 3; i++) click(await screen.findByRole('button', { name: /^skip$/i }));
  click(button('Next'));
  const confirm = await screen.findByTestId('step-confirm');
  fireEvent.change(within(confirm).getByRole('combobox', { name: /reason for dismissing/i }), {
    target: { value: 'already-approved-offline' },
  });
  return confirm;
}

describe('collectBecause (FR-15)', () => {
  it('shows an optional Because box on Confirm and sends what was typed, trimmed, in the review', async () => {
    const { service } = setup({ collectBecause: true });
    const confirm = await toDismissalConfirm();

    const box = within(confirm).getByRole('textbox', { name: /^Because/ });
    expect(within(confirm).getByTestId('because')).toBeInTheDocument();
    enter(box, '  The matter settled last week.  ');
    click(button('Dismiss and record'));

    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const review = service.commit.mock.calls[0][1] as DecisionReview;
    expect(review.because).toBe('The matter settled last week.');
  });

  it('is optional: leaving it empty (or blank) records without it', async () => {
    const { service } = setup({ collectBecause: true });
    const confirm = await toDismissalConfirm();
    enter(within(confirm).getByRole('textbox', { name: /^Because/ }), '   ');
    click(button('Dismiss and record'));

    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    expect(service.commit.mock.calls[0][1]).not.toHaveProperty('because');
  });

  it('without the seam (the Console) there is no Because box and the review has no because member', async () => {
    const { service } = setup();
    const confirm = await toDismissalConfirm();
    expect(within(confirm).queryByTestId('because')).toBeNull();
    expect(within(confirm).queryByRole('textbox', { name: /^Because/ })).toBeNull();
    click(button('Dismiss and record'));

    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    expect(service.commit.mock.calls[0][1]).not.toHaveProperty('because');
  });

  it('typed text counts as an unrecorded choice, so closing asks before discarding it', async () => {
    const { onClose } = setup({ collectBecause: true });
    const confirm = await toDismissalConfirm();
    enter(within(confirm).getByRole('textbox', { name: /^Because/ }), 'Because.');
    click(button('Close'));
    expect(onClose).not.toHaveBeenCalled();
    expect(await screen.findByText('Discard unrecorded choices?')).toBeInTheDocument();
  });
});

describe('because in the draft', () => {
  it('buildReview adds it only when it has text, and a draft without it builds the review it always did', () => {
    const taken = withDecision(
      emptyDraft('11111111-1111-4111-8111-111111111111'),
      actionScope('approve-variance'),
      'taken'
    );
    const without = buildReview(approveFirstPlan, taken);
    expect(without).not.toHaveProperty('because');
    expect(buildReview(approveFirstPlan, withBecause(taken, '   '))).toEqual(without);
    expect(buildReview(approveFirstPlan, withBecause(taken, ' Why. '))).toEqual({ ...without, because: 'Why.' });
  });

  it('a typed because is a choice; a blank one is not', () => {
    const d = emptyDraft('11111111-1111-4111-8111-111111111111');
    expect(hasUnrecordedChoices(d)).toBe(false);
    expect(hasUnrecordedChoices(withBecause(d, '  '))).toBe(false);
    expect(hasUnrecordedChoices(withBecause(d, 'x'))).toBe(true);
  });
});

describe('signalId on the lookup request', () => {
  it('names the Work Item the wizard is deciding, alongside the core record the Console scopes by', async () => {
    const lookup = makeLookup([BUDGET_A, BUDGET_B]);
    setup({ lookup });
    await screen.findByTestId('step-found');
    click(button('Next'));
    enter(screen.getByRole('textbox', { name: /^To\b/ }), 'a@b.example');
    click(button('Next'));
    await screen.findByTestId('step-action-revise-budget');
    click(screen.getByPlaceholderText(/search budget to revise/i));

    await waitFor(() =>
      expect(lookup).toHaveBeenCalledWith(
        expect.objectContaining({
          kind: 'Lookup',
          entity: 'sprk_budget',
          signalId: PATH_B_ID,
          core: { recordType: 'sprk_matter', recordId: pathBItem.core?.recordId },
        })
      )
    );
  });
});

describe('embedded', () => {
  it('renders the wizard in the page with no dialog', async () => {
    setup({ embedded: true });
    expect(await screen.findByTestId('step-found')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByRole('navigation', { name: 'Wizard steps' })).toBeInTheDocument();
  });

  it('is the SprkModal dialog by default', async () => {
    setup();
    expect(await screen.findByTestId('step-found')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
