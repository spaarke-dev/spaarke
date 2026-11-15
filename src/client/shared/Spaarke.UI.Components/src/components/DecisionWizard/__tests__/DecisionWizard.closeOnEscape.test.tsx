/**
 * The optional `closeOnEscape` seam of the decision wizard (spaarkeai-mcp-bff-api-r1 task 032 fix round): Escape closes the wizard, in the
 * modal and in the embedded form, through the SAME discard check as Close. Off by default: the wizard's dismissal is explicit (Escape and
 * the backdrop never close it), as the Console has it. These tests pin both sides, and what Escape must NOT do:
 *  - it never writes: no commit
 *  - with unrecorded choices it asks before discarding (it does not discard silently)
 *  - a list a control has open keeps the key: Escape in an open lookup list closes the list, not the wizard
 *  - nothing closes while a request is out
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import {
  BUDGET_A,
  BUDGET_B,
  DO_ID,
  PATH_B_ID,
  doItem,
  doPlan,
  pathBItem,
  pathBPlan,
} from '../__fixtures__/decisionWizardFixtures';
import type { DecisionCommitResult } from '../decisionWizardTypes';
import { commitResult } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeLookup, makeService, renderWizard } from './decisionTestKit';

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const escape = (target: Element) => fireEvent.keyDown(target, { key: 'Escape', code: 'Escape' });

function setup(props: Partial<Parameters<typeof renderWizard>[0]> = {}) {
  const service = makeService({ plans: { [DO_ID]: { ...doPlan, nextSteps: [] } } });
  const view = renderWizard({
    service,
    loadItem: makeLoader([doItem]),
    itemIds: [DO_ID],
    startItemId: DO_ID,
    ...props,
  });
  return { service, ...view };
}

describe.each([
  ['the modal wizard', false],
  ['the embedded wizard', true],
])('closeOnEscape (%s)', (_name, embedded) => {
  it('Escape closes it without writing anything', async () => {
    const { onClose, service } = setup({ closeOnEscape: true, embedded });
    escape(await screen.findByTestId('step-found'));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(service.commit).not.toHaveBeenCalled();
  });

  it('without the seam Escape closes nothing, as before', async () => {
    const { onClose, service } = setup({ embedded });
    escape(await screen.findByTestId('step-found'));

    await new Promise(resolve => setTimeout(resolve, 50));
    expect(onClose).not.toHaveBeenCalled();
    expect(service.commit).not.toHaveBeenCalled();
  });

  it('with a choice made it asks before discarding, and keeping on deciding closes nothing', async () => {
    const { onClose, service } = setup({ closeOnEscape: true, embedded });
    await screen.findByTestId('step-found');
    fireEvent.click(button('Next')); // to Mark complete
    await screen.findByTestId('step-action-mark-complete');
    fireEvent.click(button('Next')); // takes it: a choice, to Confirm
    escape(await screen.findByTestId('step-confirm'));

    expect(await screen.findByText('Discard unrecorded choices?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Keep deciding' }));
    await waitFor(() => expect(screen.queryByText('Discard unrecorded choices?')).toBeNull());
    expect(onClose).not.toHaveBeenCalled();
    expect(service.commit).not.toHaveBeenCalled();
  });

  it('nothing closes while the decision is being recorded', async () => {
    let finish!: (r: DecisionCommitResult) => void;
    const service = makeService({ plans: { [DO_ID]: { ...doPlan, nextSteps: [] } } });
    service.commit.mockImplementation(() => new Promise<DecisionCommitResult>(resolve => (finish = resolve)));
    const { onClose, onRecorded } = setup({ closeOnEscape: true, embedded, service });
    await screen.findByTestId('step-found');
    fireEvent.click(button('Next'));
    await screen.findByTestId('step-action-mark-complete');
    fireEvent.click(button('Next'));
    await screen.findByTestId('step-confirm');
    fireEvent.click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));

    escape(screen.getByTestId('step-confirm'));
    await new Promise(resolve => setTimeout(resolve, 50));
    expect(onClose).not.toHaveBeenCalled();

    finish(commitResult());
    await waitFor(() => expect(onRecorded).toHaveBeenCalledTimes(1));
  });
});

describe('Escape in a lookup list', () => {
  it('closes the list and leaves the wizard open', async () => {
    const { onClose } = setup({
      closeOnEscape: true,
      embedded: true,
      service: makeService({ plans: { [PATH_B_ID]: pathBPlan } }),
      loadItem: makeLoader([pathBItem]),
      itemIds: [PATH_B_ID],
      startItemId: PATH_B_ID,
      lookup: makeLookup([BUDGET_A, BUDGET_B]),
    });
    await screen.findByTestId('step-found');
    fireEvent.click(button('Next'));
    fireEvent.change(screen.getByRole('textbox', { name: /^To\b/ }), { target: { value: 'a@b.example' } });
    fireEvent.click(button('Next'));
    await screen.findByTestId('step-action-revise-budget');
    const field = screen.getByPlaceholderText(/search budget to revise/i);
    fireEvent.focus(field);
    expect(await screen.findByRole('listbox')).toBeInTheDocument();

    escape(field);

    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByTestId('step-action-revise-budget')).toBeInTheDocument();

    // The list is closed now: the next Escape is the wizard's. A choice was typed, so it asks before discarding.
    escape(field);
    expect(await screen.findByText('Discard unrecorded choices?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
