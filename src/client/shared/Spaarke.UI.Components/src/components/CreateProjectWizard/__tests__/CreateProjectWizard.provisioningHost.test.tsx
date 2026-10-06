/**
 * CreateProjectWizard.provisioningHost.test.tsx — the WIRING of the provisioning-failure state in the Create Project
 * wizard (unified-access-control-r2 task 133; added by its verifier round 1).
 *
 * `CreateProjectWizard.provisioningRetry.test.tsx` pins `SecureProvisioningOutcome` in isolation. That left the HOST
 * unproven: since task 133 a retryable failure adds NO warning (it is routed to the outcome component instead), so if
 * the wizard stopped rendering `<SecureProvisioningOutcome>`, or inverted the retryable branch, a retryable failure
 * would show "Project created!" with no message at all — and nothing would fail. These tests run the wizard's own
 * `onFinish` (the generic `CreateRecordWizard` shell is replaced by a stub that hands over its config; the project
 * create is substituted; provisioning runs for real against a fetch double) and assert on what its success screen
 * RENDERS: a retryable failure shows "Try securing again" and no warning; a non-retryable one shows its warning and no
 * action.
 */
import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig, IFinishContext, IFollowOnState } from '../../CreateRecordWizard';
import type { IWizardSuccessConfig } from '../../Wizard/wizardShellTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateProjectWizard } from '../CreateProjectWizard';
import { classifyProvisioningFailure } from '../provisioningService';

// The generic wizard shell is replaced by a stub that captures the config the wrapper builds — `onFinish` included.
const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

// The project create is substituted: the record "exists", and provisioning is what is under test.
jest.mock('../projectService', () => ({
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

const finishContext: IFinishContext = {
  uploadedFiles: [],
  speContainerId: '',
  selectedActions: [],
  followOn: {} as IFollowOnState,
  association: null,
  selectedExistingRecord: null,
};

/** Renders the wizard with Secure on, runs its onFinish, and renders the success screen it returns. */
async function finishWithProvisioningFailure(authFetch: jest.Mock): Promise<IWizardSuccessConfig> {
  renderWithProviders(
    <CreateProjectWizard
      open
      onClose={jest.fn()}
      dataService={{} as IDataService}
      authenticatedFetch={authFetch as never}
      bffBaseUrl={BFF}
      initialFormValues={{ projectName: 'Acme', isSecure: true }}
    />
  );
  expect(mockShell.config).toBeDefined();

  const success = await mockShell.config!.onFinish(finishContext);
  renderWithProviders(<>{success.body}</>);
  return success;
}

describe('CreateProjectWizard — routes a provisioning failure to the right designed state (task 133)', () => {
  let consoleInfo: jest.SpyInstance;
  let consoleError: jest.SpyInstance;

  beforeEach(() => {
    mockShell.config = undefined;
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => {});
    consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleInfo.mockRestore();
    consoleError.mockRestore();
  });

  it('renders "Try securing again" with its advice for a retryable failure, and the action secures the SAME project', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValueOnce(problem('sdap.provision.creator_share_failed'))
      .mockResolvedValue(ok);

    const success = await finishWithProvisioningFailure(authFetch);

    const message = classifyProvisioningFailure('sdap.provision.creator_share_failed').errorMessage;
    expect(screen.getByText(new RegExp(message.slice(0, 40)))).toBeInTheDocument();
    expect(screen.getByText(/You can try securing it again\./)).toBeInTheDocument();
    expect(success.warnings ?? []).not.toContain(message);

    await userEvent.click(screen.getByRole('button', { name: 'Try securing again' }));

    await waitFor(() => expect(screen.getByText(/now secured, with its own document container/)).toBeInTheDocument());
    // Task 142: the wizard also syncs the project's Assigned-To access once provisioning has run; the provisioning
    // calls themselves are still exactly two — the first attempt and the retry, on the SAME project.
    const provisionCalls = authFetch.mock.calls.filter(([url]) => String(url).includes('/provision-project'));
    expect(provisionCalls).toHaveLength(2);
    expect(JSON.parse(provisionCalls[1][1].body).projectId).toBe(PROJECT_ID);
  });

  it('syncs a SECURE project’s Assigned-To access only AFTER provisioning ran (task 142, owner A3 = prompt)', async () => {
    const authFetch = jest.fn().mockResolvedValue(ok);

    await finishWithProvisioningFailure(authFetch);

    const urls = authFetch.mock.calls.map(([url]) => String(url));
    const provisionAt = urls.findIndex(u => u.includes('/provision-project'));
    const syncAt = urls.findIndex(u => u.includes('/assigned-access/sync'));
    expect(provisionAt).toBeGreaterThanOrEqual(0);
    expect(syncAt).toBeGreaterThan(provisionAt);
    expect(JSON.parse(authFetch.mock.calls[syncAt][1].body)).toEqual({ recordType: 'project', recordId: PROJECT_ID });
  });

  it('shows a non-retryable failure as its warning, with no action and no retry advice', async () => {
    const authFetch = jest.fn().mockResolvedValue(problem('sdap.provision.creator_share_failed_resumable'));

    const success = await finishWithProvisioningFailure(authFetch);

    expect(success.warnings).toContain(
      classifyProvisioningFailure('sdap.provision.creator_share_failed_resumable').errorMessage
    );
    expect(screen.queryByRole('button', { name: 'Try securing again' })).not.toBeInTheDocument();
    expect(screen.queryByText(/try securing it again/i)).not.toBeInTheDocument();
  });
});
