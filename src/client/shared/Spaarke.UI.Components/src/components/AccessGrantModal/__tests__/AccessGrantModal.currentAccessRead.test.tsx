/**
 * AccessGrantModal — Current Access when the grants read FAILS, and lapsed grants (unified-access-control-r2 task 099a,
 * #1010(a); amendment 2).
 *
 * The host reads the record's grants from `GET /api/v1/external-access/record-grants`, whose `authenticatedFetch` THROWS
 * on a non-2xx (`ApiError` with the parsed ProblemDetails). A failed read must never be shown as "No active grants" — the
 * user would conclude the record is unshared, re-grant, or fail to revoke. The server's ProblemDetails `detail` is shown
 * verbatim; a failure without one gets the generic sentence. A grant past its expiry is marked "Expired {date}", never
 * shown as live access.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, CURRENT_ACCESS_READ_FAILED_MESSAGE } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IAccessGrantRecord } from '../types';

const NO_GRANTS_TEXT = 'No active grants for this record.';
const SERVER_DETAIL =
  'The external access on this record could not be read, so Current Access cannot be shown. ' +
  'Close Manage Access and open it again to retry.';

/** What `@spaarke/auth`'s `authenticatedFetch` throws for a 503 ProblemDetails (matched structurally by name + fields). */
function apiError(status: number, problemDetails: Record<string, unknown> | null): Error {
  const err = new Error(`HTTP ${status}`) as Error & { status: number; problemDetails: unknown };
  err.name = 'ApiError';
  err.status = status;
  err.problemDetails = problemDetails;
  return err;
}

function ok(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function props(fetchExistingGrants: IAccessGrantModalProps['fetchExistingGrants']): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-099a',
    authenticatedFetch: jest.fn(async () => ok({})) as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants,
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => false),
    canGrantAccess: true,
  };
}

function renderModal(p: IAccessGrantModalProps) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <AccessGrantModal {...p} />
    </FluentProvider>
  );
}

/** The same calendar-date rendering the modal uses for a bare `yyyy-MM-dd` (local date, never shifted by UTC). */
function localDate(year: number, month: number, day: number): string {
  return new Date(year, month - 1, day).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

describe('AccessGrantModal — Current Access read failure (task 099a)', () => {
  it('shows the server ProblemDetails detail verbatim and never "No active grants" when the read fails', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw apiError(503, { status: 503, detail: SERVER_DETAIL, reasonCode: 'sdap.access.record_grants.read_failed' });
        })
      )
    );

    const error = await screen.findByTestId('current-access-error');
    expect(error).toHaveTextContent('Current Access unavailable');
    expect(error).toHaveTextContent(SERVER_DETAIL);
    expect(screen.queryByText(NO_GRANTS_TEXT)).not.toBeInTheDocument();
  });

  it('renders the detail as text, never as markup', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw apiError(503, { detail: '<b>not bold</b>' });
        })
      )
    );

    const error = await screen.findByTestId('current-access-error');
    expect(error).toHaveTextContent('<b>not bold</b>');
    expect(error.querySelector('b')).toBeNull();
  });

  it('shows the generic sentence when the failure carries no ProblemDetails detail', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw new TypeError('Failed to fetch');
        })
      )
    );

    const error = await screen.findByTestId('current-access-error');
    expect(error).toHaveTextContent(CURRENT_ACCESS_READ_FAILED_MESSAGE);
    expect(screen.queryByText(NO_GRANTS_TEXT)).not.toBeInTheDocument();
  });

  it('twin: a successful read of a record with no grants still says "No active grants"', async () => {
    renderModal(props(jest.fn(async () => [])));

    expect(await screen.findByText(NO_GRANTS_TEXT)).toBeInTheDocument();
    expect(screen.queryByTestId('current-access-error')).not.toBeInTheDocument();
  });
});

describe('AccessGrantModal — lapsed grants (task 099a, amendment 2)', () => {
  const LAPSED: IAccessGrantRecord = {
    accessRecordId: 'grant-lapsed',
    contactId: 'contact-lapsed',
    fullName: 'Lee Lapsed',
    accessLevel: 100000001,
    grantedByName: 'Alice Admin',
    grantedDate: '2025-10-01',
    expiresDate: '2026-01-01',
    isExpired: true,
    provenance: 'named',
  };
  const LIVE: IAccessGrantRecord = {
    accessRecordId: 'grant-live',
    contactId: 'contact-live',
    fullName: 'Liv Live',
    accessLevel: 100000001,
    grantedByName: 'Alice Admin',
    grantedDate: '2026-10-01',
    expiresDate: '2099-01-31',
    isExpired: false,
    provenance: 'named',
  };

  it('marks a lapsed grant "Expired {date}" on its calendar date, and leaves a live grant unmarked', async () => {
    renderModal(props(jest.fn(async () => [LAPSED, LIVE])));

    await screen.findByText('Lee Lapsed');
    const badges = screen.getAllByTestId('grant-expired-badge');
    expect(badges).toHaveLength(1);
    expect(badges[0]).toHaveTextContent(`Expired ${localDate(2026, 1, 1)}`);
    expect(screen.getByText('Liv Live')).toBeInTheDocument();
  });
});
