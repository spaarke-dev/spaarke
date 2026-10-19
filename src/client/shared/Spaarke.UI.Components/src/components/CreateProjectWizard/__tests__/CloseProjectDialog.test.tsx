/**
 * CloseProjectDialog — the failure outcomes (#2104).
 *
 * `sdap.closure.incomplete.organization_too_large` is the closure's NON-retryable leaf: a revoked organization has
 * more active members than one closure may sweep, so a retry meets the same bound. The dialog must not offer one:
 * no "The project was not closed. You can retry" line, no "Try Again" button — a WARNING outcome carrying the
 * server's detail (what an administrator must do). Every other failure keeps the retryable error outcome.
 *
 * The fetch double throws the REAL `ApiError` `@spaarke/auth`'s `authenticatedFetch` throws, so the reason code is
 * read from the shape production sees.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CloseProjectDialog } from '../CloseProjectDialog';
import { CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE } from '../closureService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

const TOO_LARGE_DETAIL =
  'All 1 external grants for project p-1 were revoked, but 1 revoked organization(s) have more than 200 active ' +
  'members, more than one closure may sweep, so their members’ SPE container permissions were NOT removed and they ' +
  'may retain file access. Retrying the closure will not change this. An administrator must remove those members’ ' +
  'permissions from the project’s container in the SPE admin app (the container’s Permissions).';

const RETRY_LINE = /The project was not closed\. You can retry/;

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
});
