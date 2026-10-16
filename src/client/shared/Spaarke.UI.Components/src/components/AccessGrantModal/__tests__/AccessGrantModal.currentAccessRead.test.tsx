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
import { render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, CURRENT_ACCESS_READ_FAILED_MESSAGE } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IAccessGrantRecord } from '../types';

// Fluent's Badge renders `appearance` only as hashed classes; surface it as a data attribute so a test can read it.
jest.mock('@fluentui/react-components', () => {
  const actual = jest.requireActual('@fluentui/react-components');
  const ReactActual = jest.requireActual('react');
  return {
    ...actual,
    Badge: (p: { appearance?: string }) =>
      ReactActual.createElement(actual.Badge, { ...p, 'data-appearance': p.appearance ?? 'filled' }),
  };
});

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

function props(
  fetchExistingGrants: IAccessGrantModalProps['fetchExistingGrants'],
  overrides: Partial<IAccessGrantModalProps> = {}
): IAccessGrantModalProps {
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
    ...overrides,
  };
}

/** The Current Access rows containing the given name (only Current Access rows carry data-access-state). */
function currentAccessRows(name: string): HTMLElement[] {
  return screen
    .getAllByText(name)
    .map(el => el.closest('[data-access-state]') as HTMLElement | null)
    .filter((r): r is HTMLElement => r !== null);
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
  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

describe('AccessGrantModal — Current Access read failure (task 099a)', () => {
  it('shows the server ProblemDetails detail verbatim and never "No active grants" when the read fails', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw apiError(503, {
            status: 503,
            detail: SERVER_DETAIL,
            reasonCode: 'sdap.access.record_grants.read_failed',
          });
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

describe('AccessGrantModal — a lapsed grant beside a standing row (task 099a, verifier F1)', () => {
  const CONTACT = 'contact-both';
  const LAPSED_GRANT: IAccessGrantRecord = {
    accessRecordId: 'grant-lapsed-both',
    contactId: CONTACT,
    fullName: 'Bo Both',
    accessLevel: 100000001,
    grantedByName: 'Alice Admin',
    grantedDate: '2025-10-01',
    expiresDate: '2026-01-01',
    isExpired: true,
    provenance: 'named',
  };
  const STANDING: IAccessGrantRecord = {
    contactId: CONTACT,
    fullName: 'Bo Both',
    accessLevel: 100000000,
    provenance: 'standing',
  };

  it('lists the standing row (live access) beside the lapsed grant, and marks only the lapsed one expired', async () => {
    renderModal(
      props(
        jest.fn(async () => [LAPSED_GRANT]),
        { fetchStandingContacts: jest.fn(async () => [STANDING]) }
      )
    );

    await screen.findAllByText('Bo Both');
    const rows = currentAccessRows('Bo Both');
    expect(rows).toHaveLength(2);
    const lapsedRow = rows.find(r => within(r).queryByTestId('grant-expired-badge') !== null)!;
    const standingRow = rows.find(r => r !== lapsedRow)!;

    expect(lapsedRow).toHaveAttribute('data-access-state', 'expired');
    expect(within(lapsedRow).getByText('Collaborate')).toHaveAttribute('data-appearance', 'outline');

    expect(standingRow).toHaveAttribute('data-access-state', 'active');
    expect(within(standingRow).queryByTestId('grant-expired-badge')).not.toBeInTheDocument();
    expect(within(standingRow).getByText('Standing')).toHaveAttribute('data-appearance', 'tint');
  });

  it('twin: a live grant still stands in for the standing row (listed once, active, tint)', async () => {
    renderModal(
      props(
        jest.fn(async () => [{ ...LAPSED_GRANT, isExpired: false, expiresDate: '2099-01-31' }]),
        {
          fetchStandingContacts: jest.fn(async () => [STANDING]),
        }
      )
    );

    await screen.findAllByText('Bo Both');
    const rows = currentAccessRows('Bo Both');
    expect(rows).toHaveLength(1);
    expect(rows[0]).toHaveAttribute('data-access-state', 'active');
    expect(within(rows[0]).getByText('Collaborate')).toHaveAttribute('data-appearance', 'tint');
  });

  it('does not offer the contact of a lapsed grant as a fresh candidate beside the lapsed row', async () => {
    renderModal(
      props(
        jest.fn(async () => [LAPSED_GRANT]),
        {
          fetchCandidates: jest.fn(async () => [
            { contactId: CONTACT, fullName: 'Bo Both', role: 'Assigned Attorney 1' },
          ]),
        }
      )
    );

    await screen.findAllByText('Bo Both');
    expect(screen.getAllByText('Bo Both')).toHaveLength(1);
    expect(currentAccessRows('Bo Both')).toHaveLength(1);
  });
});

describe('AccessGrantModal — a capped grants list is stated (task 099a, verifier F2)', () => {
  const LIVE: IAccessGrantRecord = {
    accessRecordId: 'grant-live-cap',
    contactId: 'contact-cap',
    fullName: 'Cap Listed',
    accessLevel: 100000001,
    grantedByName: 'Alice Admin',
    grantedDate: '2026-10-01',
    expiresDate: '2099-01-31',
    isExpired: false,
    provenance: 'named',
  };

  it('shows the warning line when the host says the list is truncated', async () => {
    renderModal(props(jest.fn(async () => ({ grants: [LIVE], truncated: true }))));

    expect(await screen.findByTestId('current-access-truncated')).toHaveTextContent(
      'Only the first 1 grants are shown.'
    );
    expect(screen.getByText('Cap Listed')).toBeInTheDocument();
  });

  it('twin: no warning for a whole list, in either return shape', async () => {
    const { unmount } = renderModal(props(jest.fn(async () => ({ grants: [LIVE], truncated: false }))));
    await screen.findByText('Cap Listed');
    expect(screen.queryByTestId('current-access-truncated')).not.toBeInTheDocument();
    unmount();

    renderModal(props(jest.fn(async () => [LIVE])));
    await screen.findByText('Cap Listed');
    expect(screen.queryByTestId('current-access-truncated')).not.toBeInTheDocument();
  });
});

describe('AccessGrantModal — one failure, one message (task 099a, verifier F1 low)', () => {
  const GENERIC_NOTICE = 'Failed to load access data. Close and reopen to retry.';

  it('a delegation 403 on the grants read shows only the deny banner', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw apiError(403, {
            status: 403,
            detail: 'To change who else can access this record you need Write access.',
            reasonCode: 'sdap.access.deny.delegation_write_required',
          });
        })
      )
    );

    expect(await screen.findByText('Write and Share required')).toBeInTheDocument();
    expect(screen.queryByText(GENERIC_NOTICE)).not.toBeInTheDocument();
    expect(screen.queryByTestId('current-access-error')).not.toBeInTheDocument();
    expect(screen.queryByText(NO_GRANTS_TEXT)).not.toBeInTheDocument();
    expect(screen.getByTestId('current-access-not-shown')).toBeInTheDocument();
  });

  it('a 503 on the grants read shows only the section error', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw apiError(503, {
            status: 503,
            detail: SERVER_DETAIL,
            reasonCode: 'sdap.access.record_grants.read_failed',
          });
        })
      )
    );

    expect(await screen.findByTestId('current-access-error')).toHaveTextContent(SERVER_DETAIL);
    expect(screen.queryByText(GENERIC_NOTICE)).not.toBeInTheDocument();
    expect(screen.queryByText('Write and Share required')).not.toBeInTheDocument();
  });

  it('a failure of something other than the grants read keeps the generic notice', async () => {
    renderModal(
      props(
        jest.fn(async () => []),
        {
          fetchCandidates: jest.fn(async () => {
            throw new TypeError('Failed to fetch');
          }),
        }
      )
    );

    expect(await screen.findByText(GENERIC_NOTICE)).toBeInTheDocument();
    // Verifier pass 2 (F1-low): one message — Current Access is "not shown", not a second error block.
    expect(screen.queryByTestId('current-access-error')).not.toBeInTheDocument();
    expect(screen.getByTestId('current-access-not-shown')).toBeInTheDocument();
    expect(screen.queryByText(NO_GRANTS_TEXT)).not.toBeInTheDocument();
  });

  it('a grants 503 still loads every other section: role candidates, Suggested Access and the secure owner', async () => {
    const suggestion = {
      entryId: 'entry-099a',
      sourceField: 'sprk_assignedtointernal',
      sourceFieldLabel: 'Assigned To (Internal)',
      subjectKind: 'contact',
      subjectId: 'contact-suggested',
      subjectName: 'Sue Suggested',
      systemUserId: 'user-suggested',
      accessRecordId: null,
      state: 'PendingConfirmation',
      reason: null,
      residualAccessTerms: [],
    };
    const authenticatedFetch = jest.fn(async (url: string) =>
      url.includes('/assigned-access?') ? ok({ entries: [suggestion] }) : ok({})
    ) as unknown as IAccessGrantModalProps['authenticatedFetch'];
    renderModal(
      props(
        jest.fn(async () => {
          throw apiError(503, {
            status: 503,
            detail: SERVER_DETAIL,
            reasonCode: 'sdap.access.record_grants.read_failed',
          });
        }),
        {
          recordType: 'matter',
          isSecureRecord: true,
          authenticatedFetch,
          fetchCandidates: jest.fn(async () => [
            { contactId: 'contact-role', fullName: 'Rory Role', role: 'Assigned Attorney 1' },
          ]),
          fetchSecureOwnerInfo: jest.fn(async () => ({ ownerName: 'Olive Owner', businessUnitName: 'Secure BU' })),
        }
      )
    );

    expect(await screen.findByTestId('current-access-error')).toHaveTextContent(SERVER_DETAIL);
    expect(screen.getByText('Rory Role')).toBeInTheDocument();
    expect(screen.getByText('Suggested Access')).toBeInTheDocument();
    expect(screen.getByText(/Sue Suggested/)).toBeInTheDocument();
    expect(screen.getByText('Olive Owner')).toBeInTheDocument();
    expect(screen.queryByText(GENERIC_NOTICE)).not.toBeInTheDocument();
    expect(screen.queryByText('Write and Share required')).not.toBeInTheDocument();
  });
});
