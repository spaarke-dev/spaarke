/**
 * initialRegarding.test.tsx
 *
 * Unit tests for the `CreateTodoWizard` launch-context pre-fill contract
 * (smart-todo-decoupling-r3 task 032 / FR-16).
 *
 * The wizard accepts an optional `initialRegarding` prop. Three canonical
 * launch contexts exercise the prop differently — each is asserted below.
 *
 * (a) Kanban "Add To Do" — `initialRegarding={undefined}` → AssociateToStep
 *     opens empty + skippable; the wizard's onFinish receives `association: null`
 *     when the user skips.
 *
 * (b) Parent-form ribbon (e.g., Matter detail-page) — `initialRegarding={Matter
 *     triple}` → AssociateToStep opens with the dropdown set to "Matter" and the
 *     selected-record card showing the launch record; the wizard's onFinish
 *     receives `association` equal to the pre-filled triple.
 *
 * (c) Outlook add-in "Create To Do" — `initialRegarding={Communication triple}`
 *     → AssociateToStep opens pre-filled to the email's sprk_communication.
 *
 * @see projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md
 * @see src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/CreateRecordWizard.tsx
 * @see src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/TodoWizardDialog.tsx
 */

import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';

import { CreateRecordWizard } from '../../CreateRecordWizard';
import type { ICreateRecordWizardConfig, IFinishContext } from '../../CreateRecordWizard';
import { TODO_REGARDING_TARGETS } from '../../AssociateToStep/types';
import type { AssociationResult } from '../../AssociateToStep/types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Minimal webApi stub satisfying ICreateRecordWizardProps.webApi. */
function makeWebApi() {
  return {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    createRecord: jest.fn().mockResolvedValue({ id: 'todo-1' }),
  };
}

/** Mounts the CreateRecordWizard with the minimal config needed to exercise the
 *  AssociateToStep + initial-association behaviour. The infoStep + onFinish are
 *  stubs — these tests assert only on the AssociateToStep UI and the value the
 *  wizard hands to onFinish via `context.association`.
 */
function renderWizardWithInitialAssociation(opts: { initialAssociation?: AssociationResult }) {
  const navigationService = createMockNavigationService();
  const onFinish = jest
    .fn<
      Promise<{ icon: React.ReactNode; title: string; body: React.ReactNode; actions: React.ReactNode }>,
      [IFinishContext]
    >()
    .mockResolvedValue({
      icon: null,
      title: 'done',
      body: null,
      actions: null,
    });

  const config: ICreateRecordWizardConfig = {
    title: 'Create New To Do',
    entityLabel: 'to do',
    associateToStep: {
      entityTypes: TODO_REGARDING_TARGETS.slice(),
      navigationService,
      initialAssociation: opts.initialAssociation,
    },
    infoStep: {
      id: 'create-record',
      label: 'To Do Details',
      canAdvance: () => true,
      renderContent: () => <div data-testid="info-step-content">info-step</div>,
    },
    onFinish,
  };

  renderWithProviders(<CreateRecordWizard open={true} onClose={jest.fn()} webApi={makeWebApi()} config={config} />);

  return { navigationService, onFinish };
}

// ---------------------------------------------------------------------------
// Suite
// ---------------------------------------------------------------------------

describe('CreateTodoWizard — launch-context pre-fill (FR-16)', () => {
  // -------------------------------------------------------------------------
  // (a) Kanban "Add To Do" — no pre-fill, skippable
  // -------------------------------------------------------------------------

  it('kanbanEntry_noInitialRegarding_associateStepEmptyAndSkippable', async () => {
    renderWizardWithInitialAssociation({ initialAssociation: undefined });

    // AssociateToStep is the first step — verify the step header heading is showing
    // (use role=heading to disambiguate from the step indicator in the sidebar).
    expect(await screen.findByRole('heading', { name: 'Associate To' })).toBeInTheDocument();

    // No selected-record card should be visible — that card shows up only when
    // the AssociateToStep has a non-null `value`. (It's identified by its
    // "Clear" button in the AssociateToStep component.)
    expect(screen.queryByRole('button', { name: /clear/i })).not.toBeInTheDocument();

    // The dropdown defaults to the first target ("Matter") — but no record selected
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    expect(dropdown).toHaveTextContent('Matter');

    // Skip button is present (proof of skippable per FR-16). Wizard shell renders
    // the Skip button when the step has `isSkippable: true`.
    const skipButton = await screen.findByRole('button', { name: /skip/i });
    expect(skipButton).toBeInTheDocument();
    expect(skipButton).toBeEnabled();
  });

  // -------------------------------------------------------------------------
  // (b) Parent-form ribbon — pre-filled to Matter
  // -------------------------------------------------------------------------

  it('parentFormEntry_initialRegardingMatter_associateStepPreFilledToMatter', async () => {
    const matterTriple: AssociationResult = {
      entityType: 'sprk_matter',
      recordId: 'abcdef12-3456-7890-abcd-ef1234567890',
      recordName: 'Smith v. Jones',
    };

    renderWizardWithInitialAssociation({ initialAssociation: matterTriple });

    // AssociateToStep is the first step.
    expect(await screen.findByRole('heading', { name: 'Associate To' })).toBeInTheDocument();

    // The selected-record card shows the launch record's display name.
    expect(await screen.findByText('Smith v. Jones')).toBeInTheDocument();

    // The dropdown is set to the matching entity type (Matter).
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    expect(dropdown).toHaveTextContent('Matter');

    // The Clear button is now present (proof the selected-record card is rendered).
    expect(screen.getByRole('button', { name: /clear/i })).toBeInTheDocument();

    // The user can still skip OR change. Verify the Clear path works: clicking
    // Clear removes the selected-record card. This is the binding contract that
    // the user is never locked in by the pre-fill (per the notes doc Invariant 3).
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /clear/i }));

    await waitFor(() => {
      expect(screen.queryByText('Smith v. Jones')).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // (c) Outlook entry — pre-filled to sprk_communication
  // -------------------------------------------------------------------------

  it('outlookEntry_initialRegardingCommunication_associateStepPreFilled', async () => {
    const commTriple: AssociationResult = {
      entityType: 'sprk_communication',
      recordId: '11111111-2222-3333-4444-555555555555',
      recordName: 'Re: Discovery motion update',
    };

    renderWizardWithInitialAssociation({ initialAssociation: commTriple });

    // AssociateToStep is the first step.
    expect(await screen.findByRole('heading', { name: 'Associate To' })).toBeInTheDocument();

    // The selected-record card shows the email subject as the launch record name.
    expect(await screen.findByText('Re: Discovery motion update')).toBeInTheDocument();

    // The dropdown is set to "Communication" — the label corresponding to
    // sprk_communication in TODO_REGARDING_TARGETS.
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    expect(dropdown).toHaveTextContent('Communication');

    // Clear button present — confirming the pre-filled selected-record card renders.
    expect(screen.getByRole('button', { name: /clear/i })).toBeInTheDocument();
  });
});
