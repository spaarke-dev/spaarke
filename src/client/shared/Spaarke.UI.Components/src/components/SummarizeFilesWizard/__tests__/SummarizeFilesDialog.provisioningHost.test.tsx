/**
 * SummarizeFilesDialog.provisioningHost.test.tsx — the WIRING of the provisioning-failure state in the second host
 * that provisions a new secure project (unified-access-control-r2 task 133; added by its verifier round 1).
 *
 * Same gap as the Create Project wizard's (see `CreateProjectWizard.provisioningHost.test.tsx`): since task 133 a
 * retryable failure adds no warning here either, so a dropped `<SecureProvisioningOutcome>` render or an inverted
 * retryable branch would swallow the failure silently. The dialog's internal state (selected follow-ons, the Create
 * Project form) is driven through stubs of the wizard shell, the follow-on grid and the project step; the project
 * create is substituted; provisioning runs for real against a fetch double. Assertions are on the RENDERED success
 * screen.
 */
import * as React from 'react';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IWizardShellProps, IWizardSuccessConfig } from '../../Wizard/wizardShellTypes';
import type { IFollowOnGridProps } from '../../WizardFollowOns';
import type { IDataService } from '../../../types/serviceInterfaces';
import { SummarizeFilesDialog } from '../SummarizeFilesDialog';
import { classifyProvisioningFailure, provisionSecureProject } from '../../CreateProjectWizard/provisioningService';

// Provisioning runs for real; the one test of the dialog's never-expected THROW (F6 row 5) overrides it once.
jest.mock('../../CreateProjectWizard/provisioningService', () => {
  const actual = jest.requireActual('../../CreateProjectWizard/provisioningService');
  return { ...actual, provisionSecureProject: jest.fn(actual.provisionSecureProject) };
});

// The shell renders only the Next Steps step and hands over its props (`onFinish` included).
const mockShell: { props?: IWizardShellProps } = {};
jest.mock('../../Wizard/WizardShell', () => {
  const ReactActual = jest.requireActual('react');
  return {
    WizardShell: ReactActual.forwardRef((props: IWizardShellProps, ref: unknown) => {
      mockShell.props = props;
      ReactActual.useImperativeHandle(ref, () => ({ addDynamicStep: () => {}, removeDynamicStep: () => {} }));
      return ReactActual.createElement('div', null, props.steps[2].renderContent({} as never));
    }),
  };
});

// The follow-on grid: one button selects "Create Project", whose step body is then rendered inline.
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

// The Create Project step reports a valid form with Secure on, as the real one does once filled in.
jest.mock('../SummarizeCreateProjectStep', () => {
  const ReactActual = jest.requireActual('react');
  const { EMPTY_PROJECT_FORM } = jest.requireActual('../../CreateProjectWizard/projectFormTypes');
  return {
    SummarizeCreateProjectStep: (props: {
      onValidChange: (v: boolean) => void;
      onFormValues: (v: unknown) => void;
    }) => {
      ReactActual.useEffect(() => {
        props.onFormValues({ ...EMPTY_PROJECT_FORM, projectName: 'Acme', isSecure: true });
        props.onValidChange(true);
      }, []);
      return ReactActual.createElement('span', null, 'create-project-step');
    },
  };
});

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({ useHandoffFileLeg: () => [] }));

jest.mock('../../CreateProjectWizard/projectService', () => ({
  ProjectService: jest.fn().mockImplementation(() => ({
    createProject: jest.fn().mockResolvedValue({
      success: true,
      projectId: '11111111-1111-1111-1111-111111111111',
      projectName: 'Acme',
      warnings: [],
    }),
  })),
}));

const BFF = 'https://bff.example.test';
const PROJECT_ID = '11111111-1111-1111-1111-111111111111';

const problem = (reasonCode: string) =>
  ({
    ok: false,
    status: 500,
    json: async () => ({ reasonCode, detail: 'operator text' }),
  }) as unknown as Response;

const ok = {
  ok: true,
  status: 200,
  json: async () => ({
    businessUnitId: '22222222-2222-2222-2222-222222222222',
    businessUnitName: 'Secure Record',
    ownerTeamId: '33333333-3333-3333-3333-333333333333',
    ownerTeamName: 'Secure Record Owners',
    speContainerId: 'b!container',
    sharedToCreatorSystemUserId: '44444444-4444-4444-4444-444444444444',
    additionalPrincipalsShared: 0,
    resumed: true,
  }),
} as unknown as Response;

/**
 * Opens the dialog, picks Create Project (Secure on), finishes, and renders the success screen it returns. With
 * `noBff` the dialog has no BFF base URL, so it has no service to ask.
 */
async function finishWithProvisioningFailure(authFetch: jest.Mock, noBff = false): Promise<IWizardSuccessConfig> {
  renderWithProviders(
    <SummarizeFilesDialog
      open
      onClose={jest.fn()}
      dataService={{} as IDataService}
      authenticatedFetch={authFetch as never}
      bffBaseUrl={noBff ? undefined : BFF}
    />
  );

  await userEvent.click(screen.getByRole('button', { name: 'select create-project' }));
  await waitFor(() => expect(screen.getByText('create-project-step')).toBeInTheDocument());

  let success: IWizardSuccessConfig | void = undefined;
  await act(async () => {
    success = await mockShell.props!.onFinish();
  });
  renderWithProviders(<>{(success as unknown as IWizardSuccessConfig).body}</>);
  return success as unknown as IWizardSuccessConfig;
}

describe('SummarizeFilesDialog — routes a provisioning failure to the right designed state (task 133)', () => {
  let consoleInfo: jest.SpyInstance;
  let consoleError: jest.SpyInstance;

  beforeEach(() => {
    mockShell.props = undefined;
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => {});
    consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleInfo.mockRestore();
    consoleError.mockRestore();
  });

  it('renders "Try securing again" for a retryable failure, adds no warning, and the action secures the SAME project', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValueOnce(problem('sdap.provision.container_creation_failed'))
      .mockResolvedValue(ok);

    const success = await finishWithProvisioningFailure(authFetch);

    const message = classifyProvisioningFailure('sdap.provision.container_creation_failed').errorMessage;
    expect(screen.getByText(/You can try securing it again\./)).toBeInTheDocument();
    expect((success.warnings ?? []).some(w => w.includes(message))).toBe(false);

    await userEvent.click(screen.getByRole('button', { name: 'Try securing again' }));

    await waitFor(() => expect(screen.getByText(/now secured, with its own document container/)).toBeInTheDocument());
    expect(authFetch).toHaveBeenCalledTimes(2);
    expect(JSON.parse(authFetch.mock.calls[1][1].body).projectId).toBe(PROJECT_ID);
  });

  it('shows a non-retryable failure as its warning, with no action and no retry advice', async () => {
    const authFetch = jest.fn().mockResolvedValue(problem('sdap.provision.resume_creator_unavailable'));

    const success = await finishWithProvisioningFailure(authFetch);

    const message = classifyProvisioningFailure('sdap.provision.resume_creator_unavailable').errorMessage;
    expect((success.warnings ?? []).some(w => w.includes(message))).toBe(true);
    expect(screen.queryByRole('button', { name: 'Try securing again' })).not.toBeInTheDocument();
    expect(screen.queryByText(/try securing it again/i)).not.toBeInTheDocument();
  });

  // Task 150, owner round 10 item 9 (F6): the copy the owner picked for this host's two own messages, verbatim.
  it('with no BFF to ask, says the project was created but not secured (F6 row 4, option A)', async () => {
    const authFetch = jest.fn();
    const success = await finishWithProvisioningFailure(authFetch, true);

    expect(authFetch).not.toHaveBeenCalled();
    expect(success.warnings).toContain(
      'The project was created but not secured, because securing it needs a connection to the Spaarke service that this dialog does not have. An administrator can secure it.'
    );
  });

  it('when securing throws unexpectedly, says it did not finish and names the error (F6 row 5, option A)', async () => {
    (provisionSecureProject as jest.Mock).mockRejectedValueOnce(new Error('boom'));

    const success = await finishWithProvisioningFailure(jest.fn());

    expect(success.warnings).toContain(
      'Securing the project did not finish (boom). The project was created; an administrator can check how far securing it got and finish it.'
    );
  });
});
