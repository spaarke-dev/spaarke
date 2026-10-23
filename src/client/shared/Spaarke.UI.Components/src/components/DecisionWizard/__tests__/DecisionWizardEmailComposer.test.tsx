/**
 * DecisionWizard: the message step in the shared EmailComposer (task 160; D-179, owner 2026-10-23).
 *  - An action whose plan entry says `composer: "email"` renders the EmailComposer, compose-only: no Send, no From row, no
 *    External / Internal Only tags. Its To, Subject and Message flow into the step's values through `onStateChange` and are
 *    committed in the existing shape (`to` = the addresses joined with "; "). Other parameters (the outside firm) stay fields.
 *  - To starts with the plan's `prefillTo` (the outside firm's contact); without one it starts empty and Next is disabled until
 *    To, Subject and Message are all present.
 *  - The To lookup opens the host's side-pane lookup for contacts (task 158's `openLookup`) and adds the pick's email address;
 *    a pick with no address adds nothing.
 *  - Must-not-fire: an action without `composer` keeps one field per parameter.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { DecisionAction, DecisionOpenLookup, DecisionPlan, DecisionReview } from '../decisionWizardTypes';
import { DecisionWizard } from '../DecisionWizard';
import { PATH_B_ID, matterCore, pathBItem, pathBPlan, sendInquiry } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const click = (element: Element) => fireEvent.click(element);
const button = (name: RegExp) => screen.getByRole('button', { name });

const LEAD = { contactId: 'cccccccc-0000-0000-0000-0000000000aa', name: 'Lead Counsel', email: 'lead@firm.example' };

function planWith(action: DecisionAction): DecisionPlan {
  return { ...pathBPlan, actions: [action, ...pathBPlan.actions.slice(1)] };
}

const composerInquiry = (prefillTo: DecisionAction['prefillTo'] = null): DecisionAction => ({
  ...sendInquiry,
  composer: 'email',
  prefillTo,
});

function setup(plan: DecisionPlan, overrides: Partial<React.ComponentProps<typeof DecisionWizard>> = {}) {
  const service = makeService({ plans: { [PATH_B_ID]: plan } });
  const view = renderWizard({
    service,
    loadItem: makeLoader([pathBItem]),
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
    ...overrides,
  });
  return { service, ...view };
}

async function toInquiry() {
  await screen.findByTestId('step-found');
  click(button(/^next$/i));
  return screen.findByTestId('step-action-send-budget-inquiry');
}

async function recordAfterInquiry(service: ReturnType<typeof makeService>) {
  click(button(/^next$/i));
  for (let i = 0; i < 8 && !screen.queryByTestId('step-confirm'); i++) {
    const nextButton = screen.getByRole('button', { name: /^next$/i }) as HTMLButtonElement;
    click(nextButton.disabled ? button(/^skip$/i) : nextButton);
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  await screen.findByTestId('step-confirm');
  click(button(/^record decision$/i));
  await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
  return service.commit.mock.calls[0][1] as DecisionReview;
}

describe('message step in the EmailComposer (task 160)', () => {
  it('renders the composer compose-only: no Send, no From row, no tags; the outside firm stays a field', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    expect(within(step).getByRole('region', { name: 'Email composer' })).toBeInTheDocument();
    expect(within(step).queryByRole('button', { name: /^send$/i })).toBeNull();
    expect(within(step).queryByRole('group', { name: 'From' })).toBeNull();
    expect(within(step).queryByRole('radiogroup', { name: 'Who can see this message' })).toBeNull();
    // To / Subject / Message are the composer's, not three separate wizard fields.
    expect(within(step).queryByTestId('field-to')).toBeNull();
    expect(within(step).queryByTestId('field-subject')).toBeNull();
    expect(within(step).queryByTestId('field-body')).toBeNull();
    expect(within(step).getByTestId('field-outsideFirm')).toBeInTheDocument();
  });

  it('starts To with the pre-fill and the templated subject and message, so Next is enabled', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    expect(within(step).getByText(LEAD.email)).toBeInTheDocument();
    expect(within(step).getByRole('textbox', { name: 'Subject' })).not.toHaveValue('');
    expect(within(step).getByRole('textbox', { name: 'Message body' })).not.toHaveValue('');
    expect(button(/^next$/i)).toBeEnabled();
  });

  it('with no pre-fill To is empty and Next is disabled until an address is added', async () => {
    setup(planWith(composerInquiry(null)));
    const step = await toInquiry();

    expect(button(/^next$/i)).toBeDisabled();
    const to = within(step).getByRole('textbox', { name: 'To' });
    fireEvent.change(to, { target: { value: 'typed@firm.example' } });
    fireEvent.keyDown(to, { key: 'Enter' });

    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
  });

  it('Next is disabled when the subject or the message is cleared', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: '' } });
    await waitFor(() => expect(button(/^next$/i)).toBeDisabled());
    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: 'Budget question' } });
    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
    fireEvent.change(within(step).getByRole('textbox', { name: 'Message body' }), { target: { value: '  ' } });
    await waitFor(() => expect(button(/^next$/i)).toBeDisabled());
  });

  it('commits the composer state in the existing shape: addresses joined with "; ", the edited subject and message', async () => {
    const { service } = setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    const to = within(step).getByRole('textbox', { name: 'To' });
    fireEvent.change(to, { target: { value: 'second@firm.example' } });
    fireEvent.keyDown(to, { key: 'Enter' });
    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: 'Budget question' } });
    fireEvent.change(within(step).getByRole('textbox', { name: 'Message body' }), {
      target: { value: 'Why the overage?' },
    });

    const review = await recordAfterInquiry(service);
    expect(review.actions.find(a => a.code === 'send-budget-inquiry')).toEqual({
      code: 'send-budget-inquiry',
      taken: true,
      values: { to: 'lead@firm.example; second@firm.example', subject: 'Budget question', body: 'Why the overage?' },
    });
  }, 30000);

  it('the To lookup opens the side pane for contacts and adds the pick by its email address', async () => {
    const openLookup = Object.assign(
      jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
        id: 'cccccccc-0000-0000-0000-0000000000bb',
        name: 'Dana Whitfield',
        email: 'dana@firm.example',
      })),
      { supportedEntities: ['contact', 'sprk_organization', 'sprk_budget'] as readonly string[] }
    );
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    await waitFor(() =>
      expect(openLookup).toHaveBeenCalledWith({
        kind: 'Person',
        entity: 'contact',
        parameterCode: 'to',
        core: { recordType: matterCore.recordType, recordId: matterCore.recordId },
      })
    );
    expect(await within(step).findByText(/Dana Whitfield|dana@firm\.example/)).toBeInTheDocument();
    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
  });

  it('a side-pane pick with no email address adds nothing', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
      id: 'cccccccc-0000-0000-0000-0000000000cc',
      name: 'No Mail',
    }));
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(within(step).queryByText('No Mail')).toBeNull();
    expect(button(/^next$/i)).toBeDisabled();
  });

  it('no side-pane lookup control when the host has no openLookup', async () => {
    setup(planWith(composerInquiry(null)));
    const step = await toInquiry();

    expect(within(step).queryByRole('button', { name: 'Look up To recipients' })).toBeNull();
  });

  it('must-not-fire: an action without a composer keeps one field per parameter', async () => {
    setup(planWith(sendInquiry));
    const step = await toInquiry();

    expect(within(step).queryByRole('region', { name: 'Email composer' })).toBeNull();
    expect(within(step).getByTestId('field-to')).toBeInTheDocument();
    expect(within(step).getByTestId('field-subject')).toBeInTheDocument();
    expect(within(step).getByTestId('field-body')).toBeInTheDocument();
  });
});
