/**
 * CreateRecordWizard — lockAssociation behavior (task 014 / design §5.5)
 *
 * Verifies that the `IAssociateToStepConfig.lockAssociation` flag controls
 * whether the "Associate To" step appears in the wizard sequence:
 *   - lockAssociation = false (default) → the step is shown (unchanged behavior)
 *   - lockAssociation = true            → the step is hidden entirely
 *
 * These are DOM-level assertions against the rendered wizard stepper — no
 * mock-internals or DI assertions (ADR-038). The seeded `initialAssociation`
 * still reaches `onFinish` in locked mode; that pass-through is exercised by the
 * consuming wizard's finish flow (EventService resolver tests cover the payload).
 *
 * @see src/.../CreateRecordWizard/CreateRecordWizard.tsx (step-hiding branch)
 * @see src/.../AssociateToStep/__tests__/AssociateToStep.test.tsx (locked render)
 */

import * as React from 'react';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import { CreateRecordWizard } from '../CreateRecordWizard';
import type { ICreateRecordWizardConfig, ICreateRecordWizardProps } from '../types';

// The component ignores `webApi` (search callbacks flow via config); a minimal
// stub satisfies the required prop type without touching Dataverse.
const noopWebApi: ICreateRecordWizardProps['webApi'] = {
  retrieveMultipleRecords: async () => ({ entities: [] }),
  retrieveRecord: async () => ({}),
  createRecord: async () => ({ id: 'stub' }),
};

function buildConfig(lockAssociation: boolean): ICreateRecordWizardConfig {
  return {
    title: 'Create New Event',
    entityLabel: 'event',
    associateToStep: {
      entityTypes: [{ label: 'Matter', entityType: 'sprk_matter' }],
      navigationService: createMockNavigationService(),
      initialAssociation: { entityType: 'sprk_matter', recordId: 'abc-123', recordName: 'Smith v. Jones' },
      lockAssociation,
    },
    infoStep: {
      id: 'create-record',
      label: 'Event Details',
      canAdvance: () => false,
      renderContent: () => <div>event info</div>,
    },
    onFinish: async () => ({ title: 'done', body: <div>ok</div>, actions: <div /> }),
  };
}

function renderWizard(lockAssociation: boolean) {
  renderWithProviders(
    <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(lockAssociation)} />
  );
}

describe('CreateRecordWizard — lockAssociation', () => {
  it('shows the "Associate To" step when lockAssociation is false (default behavior)', () => {
    renderWizard(false);

    // Stepper label (and, as the active first step, its content header) are present.
    expect(screen.queryAllByText('Associate To').length).toBeGreaterThan(0);
    // Sanity: the rest of the standard sequence is present too.
    expect(screen.queryAllByText('Add file(s)').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Event Details').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Next Steps').length).toBeGreaterThan(0);
  });

  it('hides the "Associate To" step entirely when lockAssociation is true', () => {
    renderWizard(true);

    // The Associate-To step is gone from both the stepper and the content.
    expect(screen.queryAllByText('Associate To').length).toBe(0);
    // The remaining standard steps still render (wizard is otherwise intact).
    expect(screen.queryAllByText('Add file(s)').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Event Details').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Next Steps').length).toBeGreaterThan(0);
  });
});
