/**
 * SummarizeFilesDialog.createProjectAi.test.tsx — the Summarize host hands its BFF fetch and base URL to the Create
 * Project follow-on step (ontology task 239 review). Without them the step's "Generate with AI" button is hidden, so
 * a dropped prop would silently lose the button (it once showed but never called the AI).
 *
 * The wizard shell and the follow-on grid are stubbed (as in the provisioningHost test); the real
 * SummarizeCreateProjectStep renders the real CreateProjectStep, so the assertion is on the rendered button.
 */
import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IWizardShellProps } from '../../Wizard/wizardShellTypes';
import type { IFollowOnGridProps } from '../../WizardFollowOns';
import type { IDataService } from '../../../types/serviceInterfaces';
import { SummarizeFilesDialog } from '../SummarizeFilesDialog';

jest.mock('../../Wizard/WizardShell', () => {
  const ReactActual = jest.requireActual('react');
  return {
    WizardShell: ReactActual.forwardRef((props: IWizardShellProps, ref: unknown) => {
      ReactActual.useImperativeHandle(ref, () => ({ addDynamicStep: () => {}, removeDynamicStep: () => {} }));
      return ReactActual.createElement('div', null, props.steps[2].renderContent({} as never));
    }),
  };
});

jest.mock('../../WizardFollowOns', () => {
  const ReactActual = jest.requireActual('react');
  return {
    SendEmailFollowOnStep: () => null,
    FollowOnGrid: (props: IFollowOnGridProps) =>
      ReactActual.createElement(
        'div',
        null,
        ReactActual.createElement(
          'button',
          { onClick: () => props.onSelectionChange(['create-project']) },
          'select create-project'
        ),
        props.selected.includes('create-project')
          ? props.cards.find(c => c.id === 'create-project')!.renderStep()
          : null
      ),
  };
});

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({ useHandoffFileLeg: () => [] }));

const BFF = 'https://bff.example.test';

const dataService = {
  createRecord: jest.fn(),
  retrieveRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  updateRecord: jest.fn(),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

async function openCreateProjectStep(host: { authenticatedFetch?: unknown; bffBaseUrl?: string }) {
  renderWithProviders(
    <SummarizeFilesDialog
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={host.authenticatedFetch as never}
      bffBaseUrl={host.bffBaseUrl}
    />
  );
  await userEvent.click(screen.getByRole('button', { name: 'select create-project' }));
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Project Description' })).toBeInTheDocument());
}

describe('SummarizeFilesDialog — Create Project follow-on "Generate with AI" wiring (task 239)', () => {
  let spies: jest.SpyInstance[];

  const realFetch = (globalThis as { fetch?: typeof fetch }).fetch;

  beforeEach(() => {
    // jsdom has no global fetch; the dialog falls back to it when the host passes none.
    (globalThis as { fetch?: unknown }).fetch = jest.fn();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => {
    spies.forEach(s => s.mockRestore());
    (globalThis as { fetch?: typeof fetch }).fetch = realFetch;
  });

  it('shows the button when the host passes its authenticated fetch and BFF base URL', async () => {
    await openCreateProjectStep({ authenticatedFetch: jest.fn(), bffBaseUrl: BFF });

    expect(screen.getByRole('button', { name: /generate description with ai/i })).toBeInTheDocument();
  });

  it('shows no button when the host passes neither (the step has nothing to call)', async () => {
    await openCreateProjectStep({});

    expect(screen.queryByRole('button', { name: /generate description with ai/i })).not.toBeInTheDocument();
  });
});
