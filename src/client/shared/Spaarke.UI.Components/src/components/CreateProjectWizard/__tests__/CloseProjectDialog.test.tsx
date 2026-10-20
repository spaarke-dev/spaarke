/**
 * CloseProjectDialog — the failure outcomes (#2104).
 *
 * `sdap.closure.incomplete.organization_too_large` is the closure's NON-retryable leaf: a revoked organization has
 * more active members than one closure may sweep, so a retry meets the same bound. The dialog must not offer one:
 * no "The project was not closed. You can retry" line, no "Try Again" button — a WARNING outcome carrying the
 * server's detail (what an administrator must do). `too_many_grants` (#2104 round 2) is the other such leaf. Every other
 * failure keeps the retryable error outcome — and its line says what actually happened for that reason code (#2104
 * round 2, K4): `container_not_cleared` and `partial_revocation` DID revoke access records, so "The project was not
 * closed" would be false there.
 *
 * The fetch double throws the REAL `ApiError` `@spaarke/auth`'s `authenticatedFetch` throws, so the reason code is
 * read from the shape production sees.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CloseProjectDialog } from '../CloseProjectDialog';
import {
  CLOSURE_CONTAINER_NOT_CLEARED_REASON_CODE,
  CLOSURE_ENUMERATION_FAILED_REASON_CODE,
  CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE,
  CLOSURE_PARTIAL_REVOCATION_REASON_CODE,
  CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE,
  CLOSURE_TOO_MANY_GRANTS_REASON_CODE,
} from '../closureService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

const TOO_LARGE_DETAIL =
  'All 1 external grants for project p-1 were revoked, but 1 revoked organization(s) have more than 200 active ' +
  'members, more than one closure may sweep, so their members’ SPE container permissions were NOT removed and they ' +
  'may retain file access. Retrying the closure will not change this. An administrator must remove those members’ ' +
  'permissions from the project’s container in the SPE admin app (the container’s Permissions).';

const RETRY_LINE = /You can retry/;
const NOT_CLOSED_LINE = /The project was not closed/;
const TRY_AGAIN = { name: /try closing the project again/i };

function closureProblem(reasonCode: string | undefined, detail = 'Server detail.') {
  return apiErrorFor(500, {
    title: 'Project closure incomplete',
    status: 500,
    detail,
    ...(reasonCode ? { reasonCode } : {}),
  });
}

function renderDialog(fetchMock: jest.Mock) {
  return renderWithProviders(
    <CloseProjectDialog
      open
      projectId="p-1"
      projectName="Atlas"
      onClose={jest.fn()}
      authenticatedFetch={fetchMock}
      bffBaseUrl="https://bff.example"
    />
  );
}

async function confirmClosure() {
  fireEvent.click(screen.getByRole('button', { name: /confirm and close this secure project/i }));
}

describe('CloseProjectDialog — failure outcomes (#2104)', () => {
  beforeEach(() => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('shows organization_too_large as a warning with the server detail, and offers no retry', async () => {
    const fetchMock = jest.fn().mockRejectedValue(
      apiErrorFor(500, {
        title: 'Project closure incomplete',
        status: 500,
        detail: TOO_LARGE_DETAIL,
        reasonCode: CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE,
      })
    );

    renderDialog(fetchMock);
    await confirmClosure();

    expect(await screen.findByText('Closure incomplete')).toBeInTheDocument();
    expect(screen.getByText(TOO_LARGE_DETAIL)).toBeInTheDocument();
    expect(screen.queryByText(RETRY_LINE)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /try closing the project again/i })).not.toBeInTheDocument();
    expect(screen.queryByText('Closure failed')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /close this dialog/i })).toBeInTheDocument();
  });

  it('keeps a regular failure retryable: the error outcome, the retry line and "Try Again"', async () => {
    const fetchMock = jest.fn().mockRejectedValue(
      apiErrorFor(500, {
        title: 'Project closure incomplete',
        status: 500,
        detail:
          'All 1 external grants for project p-1 were revoked, but the container could not be cleared. Retry the closure.',
        reasonCode: 'sdap.closure.incomplete.container_not_cleared',
      })
    );

    renderDialog(fetchMock);
    await confirmClosure();

    expect(await screen.findByText('Closure failed')).toBeInTheDocument();
    expect(screen.getByText(RETRY_LINE)).toBeInTheDocument();
    const retry = screen.getByRole('button', { name: /try closing the project again/i });

    fireEvent.click(retry);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /confirm and close this secure project/i })).toBeInTheDocument()
    );
  });

  // #2104 round 2 (K4): the line under a retryable failure is true to what the server says happened.
  it.each([
    [
      CLOSURE_CONTAINER_NOT_CLEARED_REASON_CODE,
      'Access records were revoked, but some file access could not be confirmed removed. ' +
        'You can retry or contact your administrator if the issue persists.',
    ],
    [
      CLOSURE_PARTIAL_REVOCATION_REASON_CODE,
      'Some access records were revoked, but others could not be and remain active. ' +
        'You can retry or contact your administrator if the issue persists.',
    ],
  ])(
    '%s: says the access records WERE revoked, never "not closed", and keeps "Try Again"',
    async (reasonCode, line) => {
      renderDialog(jest.fn().mockRejectedValue(closureProblem(reasonCode)));
      await confirmClosure();

      expect(await screen.findByText('Closure failed')).toBeInTheDocument();
      expect(screen.getByText(line)).toBeInTheDocument();
      expect(screen.queryByText(NOT_CLOSED_LINE)).not.toBeInTheDocument();
      expect(screen.getByRole('button', TRY_AGAIN)).toBeInTheDocument();
    }
  );

  it('enumeration_failed: says nothing was revoked and the project was not closed, and keeps "Try Again"', async () => {
    renderDialog(jest.fn().mockRejectedValue(closureProblem(CLOSURE_ENUMERATION_FAILED_REASON_CODE)));
    await confirmClosure();

    expect(
      await screen.findByText(
        'No access was revoked and the project was not closed. ' +
          'You can retry or contact your administrator if the issue persists.'
      )
    ).toBeInTheDocument();
    expect(screen.getByRole('button', TRY_AGAIN)).toBeInTheDocument();
  });

  it('a failure with no reason code claims neither outcome, and keeps "Try Again"', async () => {
    renderDialog(jest.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    await confirmClosure();

    expect(
      await screen.findByText(
        'The closure could not be confirmed complete. You can retry or contact your administrator if the issue persists.'
      )
    ).toBeInTheDocument();
    expect(screen.queryByText(NOT_CLOSED_LINE)).not.toBeInTheDocument();
    expect(screen.getByRole('button', TRY_AGAIN)).toBeInTheDocument();
  });

  it('too_many_grants: a warning with the server detail, and no retry (a retry reads the same rows)', async () => {
    const detail =
      'Project p-1 has more than 4999 active external grants, more than one closure may read. No grants were revoked ' +
      'and external access is UNCHANGED. Retrying the closure will not change that. An administrator must act.';
    renderDialog(jest.fn().mockRejectedValue(closureProblem(CLOSURE_TOO_MANY_GRANTS_REASON_CODE, detail)));
    await confirmClosure();

    expect(await screen.findByText('Closure incomplete')).toBeInTheDocument();
    expect(screen.getByText(detail)).toBeInTheDocument();
    expect(screen.queryByText(RETRY_LINE)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', TRY_AGAIN)).not.toBeInTheDocument();
  });

  it('#2104 round 3 — permission_ambiguous: a warning with the server detail, and no retry (a retry refuses again)', async () => {
    const detail =
      'All 1 external grants for project p-1 were revoked, but 1 revoked grantee(s) match more than one permission on ' +
      'the project’s SPE container, or a permission whose holder cannot be verified, so none of those permissions was ' +
      'removed and they may retain file access. Retrying the closure will not change this. An administrator must ' +
      'resolve the container’s permissions in the SPE admin app (the project’s container, its Permissions).';
    renderDialog(jest.fn().mockRejectedValue(closureProblem(CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE, detail)));
    await confirmClosure();

    expect(await screen.findByText('Closure incomplete')).toBeInTheDocument();
    expect(screen.queryByText('Closure failed')).not.toBeInTheDocument();
    expect(screen.getByText(detail)).toBeInTheDocument();
    expect(screen.queryByText(RETRY_LINE)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', TRY_AGAIN)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /close this dialog/i })).toBeInTheDocument();
  });
});
