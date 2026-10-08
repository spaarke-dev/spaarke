/**
 * AccessGrantModal — Assigned-To access (unified-access-control-r2 task 142).
 *
 *  - Criterion 6 (owner A3 = prompt): on a SECURE record an "Assigned *" person is SUGGESTED, naming the source field,
 *    with Grant / Dismiss. Grant writes through the normal path (/grant for a contact, /share-user for a contact that
 *    represents an internal user) at Collaborate; Dismiss declines it (POST /assigned-access/dismiss). The suggestions
 *    come from the SERVER's ledger, never a client list.
 *  - Criterion 17 (owner A2 reversed — standing and organization access STAY): removing an automatic grant of a contact
 *    that still reaches the record through a read-time term SAYS so, naming the term, before and after the removal.
 *  - Provenance: an automatic grant names the field that granted it in Current Access.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, buildRevokeNotice, describeResidualAccess } from '../AccessGrantModal';
import type { IAssignedAccessEntry } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IAccessGrantRecord } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

function json(body: unknown, ok = true, status = ok ? 200 : 500): Response {
  return { ok, status, json: async () => body } as unknown as Response;
}

const PENDING_CONTACT: IAssignedAccessEntry = {
  entryId: 'entry-1',
  sourceField: 'sprk_assignedparalegal1',
  sourceFieldLabel: 'Assigned Paralegal 1',
  subjectKind: 'contact',
  subjectId: 'contact-1',
  subjectName: 'Pat Paralegal',
  systemUserId: null,
  accessRecordId: null,
  state: 'PendingConfirmation',
  reason: null,
  residualAccessTerms: [],
};

const PENDING_LINKED_USER: IAssignedAccessEntry = {
  ...PENDING_CONTACT,
  entryId: 'entry-2',
  sourceField: 'sprk_assignedtointernal',
  sourceFieldLabel: 'Assigned To (Internal)',
  subjectId: 'contact-2',
  subjectName: 'Ines Internal',
  systemUserId: 'user-2',
};

const AUTO_GRANT: IAccessGrantRecord = {
  accessRecordId: 'grant-9',
  contactId: 'contact-9',
  fullName: 'Sam Standing',
  accessLevel: 100000001,
  grantedDate: '2026-10-03T00:00:00Z',
};

const AUTO_GRANT_ENTRY: IAssignedAccessEntry = {
  ...PENDING_CONTACT,
  entryId: 'entry-9',
  sourceField: 'sprk_assignedattorney1',
  sourceFieldLabel: 'Assigned Attorney 1',
  subjectId: 'contact-9',
  subjectName: 'Sam Standing',
  accessRecordId: 'grant-9',
  state: 'Granted',
  residualAccessTerms: ['standing-grant'],
};

/** Answers the modal's reads from `entries`; records every write. */
function fetchWith(entries: IAssignedAccessEntry[], revokeBody: unknown = { deactivatedCount: 1 }): jest.Mock {
  return jest.fn(async (url: string) => {
    if (url.includes('/assigned-access?')) return json({ entries });
    if (url.includes('/user-shares')) return json({ shares: [] });
    if (url.includes('/revoke')) return json(revokeBody);
    return json({});
  });
}

function makeProps(fetchMock: jest.Mock, grants: IAccessGrantRecord[] = []): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-1',
    recordType: 'matter',
    authenticatedFetch: fetchMock as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => grants),
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => false),
    canGrantAccess: true,
    accessPermissionState: 'limited',
    isSecureRecord: true,
  };
}

function bodyOf(fetchMock: jest.Mock, path: string): Record<string, unknown> | undefined {
  const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith(path));
  return call ? JSON.parse((call[1] as RequestInit).body as string) : undefined;
}

describe('AccessGrantModal — Assigned-To suggestions on a secure record (task 142, criterion 6)', () => {
  it('lists a suggestion naming its source field, with Grant and Dismiss', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([PENDING_CONTACT]))} />);

    expect(await screen.findByText('Suggested Access')).toBeInTheDocument();
    expect(screen.getByText(/Pat Paralegal/)).toBeInTheDocument();
    expect(screen.getByText('Suggested from Assigned Paralegal 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Grant Pat Paralegal' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Dismiss Pat Paralegal' })).toBeInTheDocument();
  });

  it('Grant on a contact suggestion writes /grant at Collaborate through the normal path', async () => {
    const fetchMock = fetchWith([PENDING_CONTACT]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Pat Paralegal' }));

    await waitFor(() =>
      expect(bodyOf(fetchMock, '/api/v1/external-access/grant')).toEqual({
        contactId: 'contact-1',
        accessLevel: 100000001,
        recordType: 'matter',
        recordId: 'matter-1',
      })
    );
    expect(await screen.findByText(/suggested from Assigned Paralegal 1/)).toBeInTheDocument();
  });

  it('Grant on a suggestion for a contact that represents an internal user SHARES to that user instead', async () => {
    const fetchMock = fetchWith([PENDING_LINKED_USER]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Ines Internal' }));

    await waitFor(() =>
      expect(bodyOf(fetchMock, '/api/v1/external-access/share-user')).toEqual({
        recordType: 'matter',
        recordId: 'matter-1',
        systemUserId: 'user-2',
        accessLevel: 100000001,
      })
    );
    expect(bodyOf(fetchMock, '/api/v1/external-access/grant')).toBeUndefined();
  });

  it('Grant on a user suggestion refused as external names the person (task 114 owner test, 2026-10-07)', async () => {
    const base = fetchWith([PENDING_LINKED_USER]);
    const fetchMock = jest.fn(async (url: string, init?: RequestInit) =>
      String(url).endsWith('/share-user')
        ? json(
            {
              title: 'Not shared',
              detail:
                'This record is Restricted to internal users, and this user is flagged as external, so it was not shared with them.',
              reasonCode: 'sdap.access.user_share.user_not_internal',
            },
            false,
            422
          )
        : base(url, init)
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Ines Internal' }));

    expect(
      await screen.findByText(
        'System user Ines Internal is an external user. Restricted records cannot be shared with external users.'
      )
    ).toBeInTheDocument();
  });

  it('Grant on a user suggestion whose related records are still updating reports the grant, not a failure', async () => {
    const base = fetchWith([PENDING_LINKED_USER]);
    const fetchMock = jest.fn(async (url: string, init?: RequestInit) =>
      String(url).endsWith('/share-user')
        ? json(
            {
              title: 'Shared',
              detail: '2 related records are not updated yet; they complete automatically.',
              reasonCode: 'sdap.access.user_share.children_incomplete',
            },
            false,
            500
          )
        : base(url, init)
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Ines Internal' }));

    expect(
      await screen.findByText(
        /Granted Ines Internal access \(suggested from Assigned To \(Internal\)\)\. 2 related records are not updated yet/
      )
    ).toBeInTheDocument();
  });

  it('Dismiss declines the suggestion through /assigned-access/dismiss', async () => {
    const fetchMock = fetchWith([PENDING_CONTACT]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Dismiss Pat Paralegal' }));

    await waitFor(() =>
      expect(bodyOf(fetchMock, '/api/v1/external-access/assigned-access/dismiss')).toEqual({
        recordType: 'matter',
        recordId: 'matter-1',
        entryId: 'entry-1',
      })
    );
    expect(await screen.findByText(/will not be suggested again/)).toBeInTheDocument();
  });

  it('a Restricted record offers no CONTACT suggestion (contacts cannot be granted there)', async () => {
    renderWithTheme(
      <AccessGrantModal {...makeProps(fetchWith([PENDING_CONTACT]))} accessPermissionState="restricted" />
    );

    await screen.findByText('Current Access');
    expect(screen.queryByText('Suggested Access')).not.toBeInTheDocument();
  });

  it('shows no Suggested Access section when the server has nothing pending', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith([]))} />);

    await screen.findByText('Current Access');
    expect(screen.queryByText('Suggested Access')).not.toBeInTheDocument();
  });
});

describe('AccessGrantModal — removing an automatic grant that a read-time term still covers (criterion 17)', () => {
  it('names the standing grant BEFORE the removal, in the confirmation', async () => {
    const fetchMock = fetchWith([AUTO_GRANT_ENTRY]);
    renderWithTheme(
      <AccessGrantModal
        {...makeProps(fetchMock, [AUTO_GRANT])}
        isSecureRecord={false}
        accessPermissionState="standard"
      />
    );

    await screen.findByText(/Automatic — Assigned Attorney 1/);
    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));

    expect(await screen.findByText('Revoke access?')).toBeInTheDocument();
    expect(screen.getByText(/still reaches this record through their standing grant/)).toBeInTheDocument();
  });

  it('names it AFTER the removal too, from the server answer', async () => {
    const fetchMock = fetchWith([AUTO_GRANT_ENTRY], { deactivatedCount: 1, residualAccessTerms: ['standing-grant'] });
    renderWithTheme(
      <AccessGrantModal
        {...makeProps(fetchMock, [AUTO_GRANT])}
        isSecureRecord={false}
        accessPermissionState="standard"
      />
    );

    await screen.findByText(/Automatic — Assigned Attorney 1/);
    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
    expect(await screen.findByText('Revoke access?')).toBeInTheDocument();
    const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
    fireEvent.click(revokeButtons[revokeButtons.length - 1]);

    expect(
      await screen.findByText(/Revoked Sam Standing's grant\. Sam Standing still reaches this record/)
    ).toBeInTheDocument();
  });

  it('shows the automatic grant’s provenance in Current Access', async () => {
    renderWithTheme(
      <AccessGrantModal
        {...makeProps(fetchWith([AUTO_GRANT_ENTRY]), [AUTO_GRANT])}
        isSecureRecord={false}
        accessPermissionState="standard"
      />
    );

    expect(await screen.findByText(/Automatic — Assigned Attorney 1/)).toBeInTheDocument();
  });
});

describe('the residual-access copy (pure)', () => {
  it('names each term, and is null when none applies', () => {
    expect(describeResidualAccess('Sam', [])).toBeNull();
    expect(describeResidualAccess('Sam', null)).toBeNull();
    expect(describeResidualAccess('Sam', ['organization-standing-grant'])).toContain("organization's standing grant");
    expect(describeResidualAccess('Sam', ['unknown'])).toContain('could not be checked');
  });

  it('a revoke with residual terms is a warning, never a plain success', () => {
    const notice = buildRevokeNotice('Sam', { deactivatedCount: 1, residualAccessTerms: ['standing-grant'] });
    expect(notice.intent).toBe('warning');
    expect(notice.text).toMatch(/still reaches this record through their standing grant/);
    expect(buildRevokeNotice('Sam', { deactivatedCount: 1 }).intent).toBe('success');
  });

  it("an organization's automatic grant names its people's remaining access through the organization's standing grant", () => {
    const notice = buildRevokeNotice('Acme LLP', {
      deactivatedCount: 1,
      residualAccessTerms: ['organization-members-standing-grant'],
    });
    expect(notice.intent).toBe('warning');
    expect(notice.text).toMatch(/Acme LLP's people still reach this record through the organization's standing grant/);
  });
});
