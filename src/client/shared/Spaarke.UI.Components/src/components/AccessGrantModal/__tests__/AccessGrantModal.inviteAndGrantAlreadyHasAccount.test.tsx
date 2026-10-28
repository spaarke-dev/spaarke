/**
 * AccessGrantModal — `/invite-and-grant` to a person who already has an account (slice C2c).
 *
 * The server writes the grant and sends no invite email: `emailSent: false` with
 * `sdap.access.invite.contact_already_has_account`. The grant email is the person's notice, so the modal reports the
 * person as GRANTED: cleared from the staged selection, with no Retry and no "invite not sent" notice. A person whose
 * invite email genuinely did not send is not this answer.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IAccessGrantCandidate } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const CANDIDATE: IAccessGrantCandidate = {
  contactId: 'contact-1',
  fullName: 'Gene Gatekeeper',
  email: 'gene@customer.example',
  role: 'Assigned Attorney 1',
};

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function fetchWith(inviteAndGrantBody: unknown): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) return jsonResponse(inviteAndGrantBody);
    if (url.endsWith('/api/v1/external-access/invite')) throw new Error('not expected: no invite retry is offered');
    if (url.includes('/grant')) throw new Error('not expected: the contact is granted through /invite-and-grant');
    return jsonResponse({});
  });
}

function makeProps(authenticatedFetch: jest.Mock): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'project-1',
    recordType: 'project',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => [CANDIDATE]),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => false),
    canGrantAccess: true,
  };
}

function levelComboFor(name: string): HTMLElement {
  let el: HTMLElement | null = screen.getByText(name);
  while (el && !el.querySelector('[role="combobox"]')) el = el.parentElement;
  const combo = el?.querySelector('[role="combobox"]') as HTMLElement | null;
  if (!combo) throw new Error(`No access-level dropdown found for row "${name}"`);
  return combo;
}

async function addCandidate(): Promise<void> {
  await screen.findByText(CANDIDATE.fullName);
  fireEvent.click(screen.getByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` }));
  fireEvent.click(levelComboFor(CANDIDATE.fullName));
  fireEvent.click(await screen.findByRole('option', { name: 'Collaborate' }));
  fireEvent.click(screen.getByRole('button', { name: /^Add \(\d+\)$/ }));
}

describe('AccessGrantModal — /invite-and-grant to a person who already has an account', () => {
  it('emailSent false with contact_already_has_account: reported as granted, not staged, no Retry', async () => {
    const fetchMock = fetchWith({
      contactId: 'contact-1',
      onboardStatus: 'AlreadyProvisioned',
      accessRecordId: 'g-1',
      portalUrl: 'https://portal.example',
      grantedAccessLevel: 100000001,
      narrowed: false,
      notificationFailed: false,
      emailSent: false,
      emailErrorCode: INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE,
    });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/No invite was sent/)).not.toBeInTheDocument();
    expect(screen.queryByText(/already has an account/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    await waitFor(() =>
      expect((screen.getByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` }) as HTMLInputElement).checked).toBe(
        false
      )
    );
  });
});
