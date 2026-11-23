/**
 * AccessGrantModal — a role candidate that was shared with the colleague it stands for (task 193, D1-fix2).
 *
 * `/grant` answers a contact that represents an internal user with a share to that user, so no grant row hides the
 * contact from Available. It is hidden only when BOTH server reads the modal already makes say so:
 *  - its Assigned-To ledger entry (`GET /assigned-access`) names the user the contact represents (`systemUserId`), and
 *  - that user holds a share in Current Access now (`GET /user-shares`).
 * A removed share, no ledger entry, an entry naming no user, an entry for another contact, or a failed ledger read
 * leaves the contact offered.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type { IAssignedAccessEntry } from '../AccessGrantModal';
import type { IAccessGrantCandidate, IAccessGrantModalProps } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const CONTACT_ID = '5a5a5a5a-0000-4000-8000-000000000005';
const USER_ID = '7b7b7b7b-0000-4000-8000-000000000007';

const CANDIDATE: IAccessGrantCandidate = {
  contactId: CONTACT_ID,
  fullName: 'Cora Colleague',
  email: 'cora@firm.example',
  role: 'Assigned Attorney 1',
};

/** The ledger entry the `/grant` share adopted (task 193 D1-fix2 (i)): it names the user the contact represents. */
const ADOPTED: IAssignedAccessEntry = {
  entryId: 'entry-5',
  sourceField: 'sprk_assignedattorney1',
  sourceFieldLabel: 'Assigned Attorney 1',
  subjectKind: 'contact',
  subjectId: CONTACT_ID,
  subjectName: 'Cora Colleague',
  systemUserId: USER_ID,
  accessRecordId: null,
  state: 'Adopted',
  reason: 'manual-grant',
  residualAccessTerms: [],
};

const USER_SHARE = { systemUserId: USER_ID, fullName: 'Cora C. User', accessLevel: 100000001 };

function json(body: unknown, ok = true, status = ok ? 200 : 500): Response {
  return {
    ok,
    status,
    json: async () => (ok ? body : { status, ...(body as Record<string, unknown>) }),
  } as unknown as Response;
}

/** Answers `/assigned-access` with `entries` (or a 500 when `null`) and `/user-shares` with `shares`. */
function fetchWith(entries: IAssignedAccessEntry[] | null, shares: unknown[]): jest.Mock {
  return jest.fn(async (url: string) => {
    if (url.includes('/assigned-access?'))
      return entries === null ? json({ title: 'Failed' }, false) : json({ entries });
    if (url.includes('/user-shares')) return json({ shares });
    return json({});
  });
}

function makeProps(fetchMock: jest.Mock): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-1',
    recordType: 'matter',
    authenticatedFetch: fetchMock as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => [CANDIDATE]),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    canGrantAccess: true,
  };
}

const candidateBox = () => screen.queryByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` });

describe('AccessGrantModal — a role candidate shared with the colleague it stands for (task 193)', () => {
  it('is hidden when its ledger entry names the user AND that user holds a share now', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([ADOPTED], [USER_SHARE]))} />);

    // Loaded: Current Access lists the user's share.
    expect(await screen.findByText('Cora C. User')).toBeInTheDocument();
    expect(candidateBox()).not.toBeInTheDocument();
  });

  it('is hidden whatever the GUID spelling of either read (braces, case)', async () => {
    const entry = {
      ...ADOPTED,
      subjectId: `{${CONTACT_ID.toUpperCase()}}`,
      systemUserId: `{${USER_ID.toUpperCase()}}`,
    };
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([entry], [USER_SHARE]))} />);

    expect(await screen.findByText('Cora C. User')).toBeInTheDocument();
    expect(candidateBox()).not.toBeInTheDocument();
  });

  it('is offered again once the share was removed (the ledger entry still names the user)', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([ADOPTED], []))} />);

    expect(await screen.findByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` })).toBeInTheDocument();
  });

  it('is offered when it has no ledger entry, even though the user holds a share', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([], [USER_SHARE]))} />);

    expect(await screen.findByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` })).toBeInTheDocument();
  });

  it('is offered when its ledger entry names no user', async () => {
    const entry = { ...ADOPTED, systemUserId: null };
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([entry], [USER_SHARE]))} />);

    expect(await screen.findByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` })).toBeInTheDocument();
  });

  it('MUST NOT FIRE: an entry for ANOTHER contact naming the shared user leaves this candidate offered', async () => {
    const entry = { ...ADOPTED, subjectId: '9c9c9c9c-0000-4000-8000-000000000009' };
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([entry], [USER_SHARE]))} />);

    expect(await screen.findByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` })).toBeInTheDocument();
  });

  it('is offered when the ledger read failed (never hidden on a read it does not have)', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith(null, [USER_SHARE]))} />);

    expect(await screen.findByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` })).toBeInTheDocument();
  });
});
