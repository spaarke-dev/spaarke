/**
 * AccessGrantModal — a grant a CONTACT issued from the external SPA (unified-access-control-r2 task 140, owner C4 / Q2).
 *
 * Manage Access (an internal user, Write on the record) sees who issued each row. A contact-issued row carries the
 * contact-typed issuer (`sprk_grantedbycontact` → `grantedByContactName`) and no systemuser issuer; it renders as
 * "Granted by {contact} (external contact)" and stays revocable here through the ordinary `/revoke`.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IAccessGrantRecord } from '../types';

const CONTACT_ISSUED: IAccessGrantRecord = {
  accessRecordId: 'grant-140',
  contactId: 'contact-colleague',
  fullName: 'Casey Colleague',
  accessLevel: 100000001,
  grantedByContactName: 'Gerry Grantor',
  grantedDate: '2026-10-04T00:00:00Z',
  provenance: 'named',
};

const SYSTEMUSER_ISSUED: IAccessGrantRecord = {
  accessRecordId: 'grant-139',
  contactId: 'contact-other',
  fullName: 'Pat Prior',
  accessLevel: 100000000,
  grantedByName: 'Alice Admin',
  grantedDate: '2026-10-01T00:00:00Z',
  provenance: 'named',
};

function ok(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function props(): IAccessGrantModalProps {
  const authenticatedFetch = jest.fn(async (url: string) =>
    url.includes('/revoke')
      ? ok({ speContainerMembershipRevoked: false, speContainerOutcome: 'NotAttempted', deactivatedCount: 1 })
      : ok({})
  );
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'project-140',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => [CONTACT_ISSUED, SYSTEMUSER_ISSUED]),
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => false),
    canGrantAccess: true,
  };
}

describe('AccessGrantModal — contact-issued grants (task 140)', () => {
  it('names the contact issuer of a contact-issued row, and the systemuser of the other', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <AccessGrantModal {...props()} />
      </FluentProvider>
    );

    await screen.findByText('Casey Colleague');
    expect(screen.getByText(/Granted by Gerry Grantor \(external contact\) on/)).toBeInTheDocument();
    expect(screen.getByText(/Granted by Alice Admin on/)).toBeInTheDocument();
    expect(screen.queryByText(/Granted by unknown/)).not.toBeInTheDocument();
  });

  it('lets the internal user revoke a contact-issued grant through the ordinary /revoke', async () => {
    const p = props();
    render(
      <FluentProvider theme={webLightTheme}>
        <AccessGrantModal {...p} />
      </FluentProvider>
    );

    await screen.findByText('Casey Colleague');
    const row = screen.getByText('Casey Colleague').closest('div[class]')!.parentElement!.parentElement!;
    const revokeInRow = Array.from(row.querySelectorAll('button')).find(b => b.textContent === 'Revoke')!;
    fireEvent.click(revokeInRow);
    expect(await screen.findByText('Revoke access?')).toBeInTheDocument();
    const confirm = screen.getAllByRole('button', { name: 'Revoke' });
    fireEvent.click(confirm[confirm.length - 1]);

    await waitFor(() =>
      expect(p.authenticatedFetch).toHaveBeenCalledWith(
        '/api/v1/external-access/revoke',
        expect.objectContaining({ method: 'POST' })
      )
    );
    const call = (p.authenticatedFetch as unknown as jest.Mock).mock.calls.find(
      (c: unknown[]) => c[0] === '/api/v1/external-access/revoke'
    )!;
    expect(JSON.parse((call[1] as RequestInit).body as string)).toMatchObject({ accessRecordId: 'grant-140' });
  });
});
