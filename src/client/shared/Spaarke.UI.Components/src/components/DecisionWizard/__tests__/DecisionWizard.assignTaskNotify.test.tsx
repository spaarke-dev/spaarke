/**
 * DecisionWizard - Assign Task's "Send email notification" (task 168; D-194, owner UAT round 2 item 2). The stated contract:
 *  - the step shows a checkbox built from the plan's Boolean parameter (no switch on the action), ticked by default
 *  - Record decision posts notify: true when it is left ticked, and notify: false when it is unticked
 *  - a commit warning that the email did not go reads as a quiet sentence on the recorded decision
 *  - must-not-fire: Create budget's step (any other action) has no checkbox
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { DecisionAction, DecisionPlan, DecisionReview } from '../decisionWizardTypes';
import { describeWarning } from '../decisionDisplay';
import { CONTACT, PATH_B_ID, createBudgetPlan, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });

/** Assign Task as the plan route sends it (DecisionActionCatalog, task 168). */
const assignTask: DecisionAction = {
  code: 'assign-task',
  label: 'Assign Task',
  workType: null,
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    { code: 'assignee', label: 'Assign to', kind: 'Person', required: true },
    { code: 'description', label: 'What to do', kind: 'TextArea', required: true },
    { code: 'dueDate', label: 'Due', kind: 'Date', required: false },
    {
      code: 'notify',
      label: 'Send email notification',
      kind: 'Boolean',
      required: false,
      hint: 'Emails the person the task, and notifies them in Spaarke',
      defaultValue: 'true',
    },
  ],
  effectLines: [
    'Creates a task (an event of type Task) linked to the record, assigned to the person above, when the decision is recorded',
  ],
  excludes: [],
};

const assignTaskPlan: DecisionPlan = { ...pathBPlan, actions: [assignTask], nextSteps: [] };

function setup(plan: DecisionPlan = assignTaskPlan) {
  const service = makeService({ plans: { [PATH_B_ID]: plan } });
  const openLookup = jest.fn(async () => CONTACT);
  const view = renderWizard({
    service,
    loadItem: makeLoader([pathBItem]),
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
    openLookup,
  });
  return { service, ...view };
}

async function openStep() {
  await screen.findByTestId('step-found');
  fireEvent.click(button('Next'));
  return screen.findByTestId('step-action-assign-task');
}

async function fillAndRecord(untick: boolean): Promise<DecisionReview> {
  const { service } = setup();
  await openStep();
  fireEvent.click(screen.getByRole('textbox', { name: /^Assign to/ }));
  await waitFor(() => expect(screen.getByRole('textbox', { name: /^Assign to/ })).toHaveValue(CONTACT.name));
  fireEvent.change(screen.getByRole('textbox', { name: /^What to do/ }), { target: { value: 'Call the client' } });
  if (untick) fireEvent.click(screen.getByRole('checkbox', { name: /^Send email notification/ }));
  fireEvent.click(button('Next'));
  await screen.findByTestId('step-confirm');
  fireEvent.click(button('Record decision'));
  await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
  return service.commit.mock.calls[0][1] as DecisionReview;
}

describe('Assign Task: Send email notification', () => {
  it('shows a checkbox, ticked by default, after the task fields', async () => {
    setup();
    const step = await openStep();

    const fields = within(step)
      .getAllByTestId(/^field-/)
      .map(f => f.getAttribute('data-testid'));
    expect(fields).toEqual(['field-assignee', 'field-description', 'field-dueDate', 'field-notify']);
    const box = screen.getByRole('checkbox', { name: /^Send email notification/ });
    expect(box).toBeChecked();
    expect(within(screen.getByTestId('field-notify')).getByText(/Emails the person the task/)).toBeInTheDocument();
  });

  it('posts notify: true when it is left ticked', async () => {
    const review = await fillAndRecord(false);

    expect(review.actions).toEqual([
      {
        code: 'assign-task',
        taken: true,
        values: { assignee: CONTACT.id, description: 'Call the client', notify: true },
      },
    ]);
  }, 30000);

  it('posts notify: false when it is unticked', async () => {
    const review = await fillAndRecord(true);

    expect(review.actions[0].values).toEqual({ assignee: CONTACT.id, description: 'Call the client', notify: false });
  }, 30000);

  it('a commit warning that the email did not go reads as a sentence, not a code', () => {
    expect(describeWarning('assignee_email_not_sent')).toBe(
      'The task was assigned, but the email to the assignee could not be sent.'
    );
    expect(describeWarning('assignee_email_missing')).toBe(
      'The task was assigned, but the assignee has no email address, so no email was sent.'
    );
  });

  it('D-199: each reason the gate refused the email reads as its own sentence', () => {
    expect(describeWarning('assignee_email_not_sent_no_access')).toBe(
      'The task was assigned, but no email was sent: the assignee does not have access to this record.'
    );
    expect(describeWarning('assignee_email_not_sent_several_users')).toBe(
      "The task was assigned, but no email was sent: the assignee's contact is linked to more than one user."
    );
    expect(describeWarning('assignee_email_not_sent_unverified')).toBe(
      'The task was assigned, but no email was sent: whether the assignee can see this record could not be checked.'
    );
  });
});

describe('must not fire', () => {
  it("Create budget's step has no checkbox", async () => {
    setup(createBudgetPlan);
    await screen.findByTestId('step-found');
    fireEvent.click(button('Next'));
    const step = await screen.findByTestId('step-action-create-budget');

    expect(within(step).queryByRole('checkbox')).toBeNull();
    expect(within(step).queryByTestId('field-notify')).toBeNull();
  });
});
