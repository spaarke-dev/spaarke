/**
 * CreateProjectWizard.provisioningRetry.test.tsx — the wizard's provisioning-failure state and its
 * "Try securing again" action (unified-access-control-r2 task 133, C11).
 *
 * The defect class this pins is FR-31's: a sentence that renders and is untrue. Before task 133 the wizard called
 * provisioning once and had no retry, so any copy saying "you can try again" pointed at nothing. Now the server tells
 * the same caller they may call again for several states, and `SecureProvisioningOutcome` (rendered in the wizard's
 * success body) carries the advice AND the button together, keyed on the classified result's `retryable`. Assertions
 * are on RENDERED output and on the request the button sends, not on props.
 */
import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SecureProvisioningOutcome } from '../SecureProvisioningOutcome';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';
import {
  classifyProvisioningFailure,
  type IProvisionProjectResult,
  type IProvisioningFailureExtensions,
} from '../provisioningService';

const BFF = 'https://bff.example.test';
const PROJECT_ID = '11111111-1111-1111-1111-111111111111';

const failure = (reasonCode: string, extensions?: IProvisioningFailureExtensions): IProvisionProjectResult => ({
  success: false,
  reasonCode,
  ...classifyProvisioningFailure(reasonCode, extensions),
});

const okResponse = {
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

describe('SecureProvisioningOutcome — the wizard provisioning-failure state', () => {
  let consoleInfo: jest.SpyInstance;
  let consoleError: jest.SpyInstance;

  beforeEach(() => {
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => {});
    consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleInfo.mockRestore();
    consoleError.mockRestore();
  });

  it.each([
    'sdap.provision.creator_share_failed',
    'sdap.provision.container_creation_failed',
    'sdap.provision.container_not_recorded',
    'sdap.provision.owner_assignment_unverified',
    'sdap.provision.creator_unresolved',
    // Task 133 b2: a read failed before any change — the same caller may call again.
    'sdap.provision.container_ownership_unreadable',
    'sdap.provision.resume_creator_unavailable#unreadable',
  ])(
    'renders "Try securing again" for the retryable state %s, which re-calls for the SAME project and then shows success',
    async state => {
      const [reasonCode, creatorState] = state.split('#');
      const authFetch = jest.fn().mockResolvedValue(okResponse);
      renderWithProviders(
        <SecureProvisioningOutcome
          projectId={PROJECT_ID}
          projectRef="Acme"
          initialResult={failure(reasonCode, creatorState ? { creatorState } : undefined)}
          authenticatedFetch={authFetch as never}
          bffBaseUrl={BFF}
        />
      );

      expect(screen.getByText(/You can try securing it again\./)).toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: 'Try securing again' }));

      await waitFor(() => expect(screen.getByText(/now secured, with its own document container/)).toBeInTheDocument());
      expect(authFetch).toHaveBeenCalledTimes(1);
      const [url, init] = authFetch.mock.calls[0];
      expect(url).toBe(`${BFF}/api/v1/external-access/provision-project`);
      expect(JSON.parse(init.body).projectId).toBe(PROJECT_ID);
      expect(screen.queryByRole('button', { name: 'Try securing again' })).not.toBeInTheDocument();
    }
  );

  it.each([
    // Only an administrator can finish these: the creator may no longer pass the Write gate.
    'sdap.provision.creator_share_failed_resumable',
    'sdap.provision.resume_creator_unavailable',
    'sdap.provision.owner_assignment_failed',
    'sdap.provision.secure_bu_not_found',
    // Task 133 b2: another record holds the container already on the project — an administrator decides.
    'sdap.provision.container_shared_with_another_record',
  ])('offers no retry — neither the button nor the advice — for the non-retryable state %s', reasonCode => {
    const authFetch = jest.fn();
    renderWithProviders(
      <SecureProvisioningOutcome
        projectId={PROJECT_ID}
        initialResult={failure(reasonCode)}
        authenticatedFetch={authFetch as never}
        bffBaseUrl={BFF}
      />
    );

    expect(screen.getByText(failure(reasonCode).errorMessage!)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Try securing again' })).not.toBeInTheDocument();
    expect(screen.queryByText(/try securing it again/i)).not.toBeInTheDocument();
  });

  // Owner round 14 item 3 (task 133 c1-r4): the same two codes the retry list above offers a retry for are NOT
  // retryable when the server says Dataverse REFUSED the read (the service's sign-in or Read privilege): calling again
  // would be refused every time, so neither the button nor the advice renders.
  it.each<[string, IProvisioningFailureExtensions]>([
    ['sdap.provision.container_ownership_unreadable', { containerOwnershipState: 'refused' }],
    ['sdap.provision.resume_creator_unavailable', { creatorState: 'refused' }],
  ])('offers no retry for %s when the read was refused (%o)', (reasonCode, extensions) => {
    const authFetch = jest.fn();
    renderWithProviders(
      <SecureProvisioningOutcome
        projectId={PROJECT_ID}
        initialResult={failure(reasonCode, extensions)}
        authenticatedFetch={authFetch as never}
        bffBaseUrl={BFF}
      />
    );

    expect(screen.getByText(failure(reasonCode, extensions).errorMessage!)).toBeInTheDocument();
    expect(screen.getByText(/service's permission/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Try securing again' })).not.toBeInTheDocument();
    expect(screen.queryByText(/try securing it again/i)).not.toBeInTheDocument();
  });

  it('when the retry lands in a state only an administrator can finish, takes the action away', async () => {
    const authFetch = jest.fn().mockRejectedValue(
      apiErrorFor(500, {
        title: 'Provisioning failed',
        status: 500,
        reasonCode: 'sdap.provision.creator_share_failed_resumable',
        detail: 'operator text',
      })
    );
    renderWithProviders(
      <SecureProvisioningOutcome
        projectId={PROJECT_ID}
        initialResult={failure('sdap.provision.creator_share_failed')}
        authenticatedFetch={authFetch as never}
        bffBaseUrl={BFF}
      />
    );

    await userEvent.click(screen.getByRole('button', { name: 'Try securing again' }));

    await waitFor(() => expect(screen.getByText(/An administrator needs to finish securing it/)).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Try securing again' })).not.toBeInTheDocument();
    expect(screen.queryByText(/operator text/)).not.toBeInTheDocument();
  });
});
