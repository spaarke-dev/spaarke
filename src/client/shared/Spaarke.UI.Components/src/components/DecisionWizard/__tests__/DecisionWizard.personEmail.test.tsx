/**
 * DecisionWizard - the Assign Task person picker and the Confirm summary show the contact's email (spaarke-ontology-platform-r1
 * task 211, DI-170, #3062). Three active contacts can share a full name; the name alone cannot tell them apart.
 *  - each Person option in the inline picker shows the email under the name
 *  - the picked person reads "Name (email)" in the field and in the Confirm summary; with no email, just "Name"
 *  - the side-pane pick (the Console) reads the same, from the email the host returns with the pick
 *  - the commit payload is unchanged: the contact's id, never the shown text
 *  - must-not-fire: a Lookup (not a Person) and a host that already puts the email in the name get no second line
 * Names and addresses here are invented (example.com).
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { DecisionAction, DecisionLookupSearch, DecisionPlan, DecisionReview } from '../decisionWizardTypes';
import { PATH_B_ID, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });

const FIRST: ILookupItem = {
  id: 'cccccccc-0000-0000-0000-0000000000a1',
  name: 'Sam Rivera',
  email: 'sam.rivera@example.com',
  entityType: 'contact',
};
const SECOND: ILookupItem = {
  id: 'cccccccc-0000-0000-0000-0000000000a2',
  name: 'Sam Rivera',
  email: 'srivera@example.com',
  entityType: 'contact',
};
const NO_EMAIL: ILookupItem = { id: 'cccccccc-0000-0000-0000-0000000000a3', name: 'Sam Rivera', entityType: 'contact' };

const assignTask: DecisionAction = {
  code: 'assign-task',
  label: 'Assign Task',
  workType: null,
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    { code: 'assignee', label: 'Assign to', kind: 'Person', required: true },
    { code: 'description', label: 'What to do', kind: 'TextArea', required: true },
  ],
  effectLines: ['Creates a task assigned to the person you choose, when the decision is recorded'],
  excludes: [],
};
const plan: DecisionPlan = { ...pathBPlan, actions: [assignTask], nextSteps: [] };

function setup(opts: { lookup?: DecisionLookupSearch; openLookup?: jest.Mock } = {}) {
  const service = makeService({ plans: { [PATH_B_ID]: plan } });
  const lookup: DecisionLookupSearch = opts.lookup ?? (async () => [FIRST, SECOND, NO_EMAIL]);
  renderWizard({
    service,
    loadItem: makeLoader([pathBItem]),
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
    lookup,
    ...(opts.openLookup ? { openLookup: opts.openLookup as never } : {}),
  });
  return { service };
}

async function toStep() {
  await screen.findByTestId('step-found');
  fireEvent.click(button('Next'));
  await screen.findByTestId('step-action-assign-task');
}

const assigneeInput = () => screen.getByRole('textbox', { name: /^Assign to/ });

/** Fills What to do, goes to Confirm, and returns once it shows. */
async function toConfirm() {
  fireEvent.change(screen.getByRole('textbox', { name: /^What to do/ }), { target: { value: 'Call the client' } });
  fireEvent.click(button('Next'));
  await screen.findByTestId('step-confirm');
}

async function record(service: ReturnType<typeof setup>['service']): Promise<DecisionReview> {
  fireEvent.click(button('Record decision'));
  await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
  return service.commit.mock.calls[0][1] as DecisionReview;
}

const confirmAssignee = () => {
  const row = within(screen.getByTestId('offered-assign-task'));
  return row.getByText('Assign to').closest('dt')?.nextElementSibling?.textContent;
};

describe('inline Person picker', () => {
  it('shows each contact email under the name, so same-name contacts can be told apart', async () => {
    setup();
    await toStep();

    fireEvent.focus(assigneeInput());
    const options = await screen.findAllByRole('option');

    expect(options).toHaveLength(3);
    expect(within(options[0]).getByText('sam.rivera@example.com')).toBeInTheDocument();
    expect(within(options[1]).getByText('srivera@example.com')).toBeInTheDocument();
    // A contact with no email shows the name only: no empty line, no empty parentheses.
    expect(options[2].textContent).toBe('Sam Rivera');
  });

  it('the pick reads "Name (email)" in the field and in Confirm, and the commit sends the id only', async () => {
    const { service } = setup();
    await toStep();

    fireEvent.focus(assigneeInput());
    fireEvent.click((await screen.findAllByRole('option'))[1]);
    expect(await screen.findByText('Sam Rivera (srivera@example.com)')).toBeInTheDocument();
    await toConfirm();
    expect(confirmAssignee()).toBe('Sam Rivera (srivera@example.com)');
    const review = await record(service);

    expect(review.actions).toEqual([
      { code: 'assign-task', taken: true, values: { assignee: SECOND.id, description: 'Call the client' } },
    ]);
  }, 30000);

  it('Confirm reads "Name (email)" for a contact with an email', async () => {
    setup();
    await toStep();

    fireEvent.focus(assigneeInput());
    fireEvent.click((await screen.findAllByRole('option'))[0]);
    await toConfirm();

    expect(confirmAssignee()).toBe('Sam Rivera (sam.rivera@example.com)');
  }, 30000);

  it('must-not-fire: Confirm reads "Name" alone for a contact with no email', async () => {
    setup();
    await toStep();

    fireEvent.focus(assigneeInput());
    fireEvent.click((await screen.findAllByRole('option'))[2]);
    await toConfirm();

    expect(confirmAssignee()).toBe('Sam Rivera');
  }, 30000);

  it('must-not-fire: a host that already puts the email in the name (the Console search) shows it once', async () => {
    const formatted: ILookupItem = {
      id: FIRST.id,
      name: 'Sam Rivera (sam.rivera@example.com)',
      email: 'sam.rivera@example.com',
      entityType: 'contact',
    };
    setup({ lookup: async () => [formatted] });
    await toStep();

    fireEvent.focus(assigneeInput());
    const [option] = await screen.findAllByRole('option');

    expect(option.textContent).toBe('Sam Rivera (sam.rivera@example.com)');
    expect(option.children).toHaveLength(1);
    fireEvent.click(option);
    await toConfirm();
    expect(confirmAssignee()).toBe('Sam Rivera (sam.rivera@example.com)');
  }, 30000);
});

describe('side-pane Person pick', () => {
  it('Confirm reads "Name (email)" from the email the host returns with the pick; the commit sends the id only', async () => {
    const openLookup = jest.fn(async () => ({ id: FIRST.id, name: FIRST.name, email: FIRST.email }));
    const { service } = setup({ openLookup });
    await toStep();

    fireEvent.click(assigneeInput());
    await waitFor(() => expect(assigneeInput()).toHaveValue('Sam Rivera (sam.rivera@example.com)'));
    await toConfirm();
    expect(confirmAssignee()).toBe('Sam Rivera (sam.rivera@example.com)');
    const review = await record(service);

    expect(review.actions[0].values).toEqual({ assignee: FIRST.id, description: 'Call the client' });
  }, 30000);

  it('must-not-fire: a pick with no email reads "Name" in the field and in Confirm', async () => {
    const openLookup = jest.fn(async () => ({ id: NO_EMAIL.id, name: NO_EMAIL.name }));
    setup({ openLookup });
    await toStep();

    fireEvent.click(assigneeInput());
    await waitFor(() => expect(assigneeInput()).toHaveValue('Sam Rivera'));
    await toConfirm();

    expect(confirmAssignee()).toBe('Sam Rivera');
  }, 30000);
});
