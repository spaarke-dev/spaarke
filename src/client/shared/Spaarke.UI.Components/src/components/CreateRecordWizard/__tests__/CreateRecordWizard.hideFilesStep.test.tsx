/**
 * CreateRecordWizard — hideFilesStep behavior (task 040)
 *
 * Verifies that the `ICreateRecordWizardConfig.hideFilesStep` flag controls
 * whether the "Add file(s)" step appears in the wizard sequence:
 *   - hideFilesStep = false/undefined (default) → the step is shown (unchanged behavior)
 *   - hideFilesStep = true                      → the step is omitted entirely
 *
 * Added for `CreateReportCardWizard` (owner decision: no Add Files step —
 * see notes/field-manifests/reportcard.md). These are DOM-level assertions
 * against the rendered wizard stepper — no mock-internals or DI assertions
 * (ADR-038).
 *
 * @see src/.../CreateRecordWizard/CreateRecordWizard.tsx (filesSteps branch)
 * @see ./CreateRecordWizard.lockAssociation.test.tsx (sibling reference shape)
 */

import * as React from 'react';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CreateRecordWizard } from '../CreateRecordWizard';
import type { ICreateRecordWizardConfig, ICreateRecordWizardProps } from '../types';

// The component ignores `webApi` (search callbacks flow via config); a minimal
// stub satisfies the required prop type without touching Dataverse.
const noopWebApi: ICreateRecordWizardProps['webApi'] = {
  retrieveMultipleRecords: async () => ({ entities: [] }),
  retrieveRecord: async () => ({}),
  createRecord: async () => ({ id: 'stub' }),
};

function buildConfig(hideFilesStep?: boolean): ICreateRecordWizardConfig {
  return {
    title: 'Create New Report Card',
    entityLabel: 'report card',
    hideFilesStep,
    infoStep: {
      id: 'create-record',
      label: 'Report Card Details',
      canAdvance: () => false,
      renderContent: () => <div>report card info</div>,
    },
    onFinish: async () => ({ title: 'done', body: <div>ok</div>, actions: <div /> }),
  };
}

function renderWizard(hideFilesStep?: boolean) {
  renderWithProviders(
    <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(hideFilesStep)} />
  );
}

describe('CreateRecordWizard — hideFilesStep', () => {
  it('shows the "Add file(s)" step when hideFilesStep is undefined (default behavior)', () => {
    renderWizard(undefined);

    expect(screen.queryAllByText('Add file(s)').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Report Card Details').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Next Steps').length).toBeGreaterThan(0);
  });

  it('omits the "Add file(s)" step entirely when hideFilesStep is true', () => {
    renderWizard(true);

    expect(screen.queryAllByText('Add file(s)').length).toBe(0);
    // The remaining standard steps still render (wizard is otherwise intact).
    expect(screen.queryAllByText('Report Card Details').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Next Steps').length).toBeGreaterThan(0);
  });
});
