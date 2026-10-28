/**
 * AccessGrantModal — `/invite-and-grant` whose invite email was not sent (C2d F2).
 *
 * The server wrote the grant, attempted the invite email and could not send it: `emailSent: false` with
 * `sdap.access.invite.email_not_sent`. Nobody holds the code, so the person cannot sign in yet. The modal takes the same
 * path as a `/grant` whose invite failed: the not-sent notice, and Retry through `/invite` (which writes no grant). The
 * other answers are not that path: `contact_already_has_account` is a grant with no invite due, `true` is a sent invite,
 * and `null` or absent (a server that does not report it) says nothing, so the modal claims neither a send nor a failure.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  AccessGrantModal,
  INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE,
  INVITE_EMAIL_NOT_SENT_REASON_CODE,
} from '../AccessGrantModal';
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

function inviteAndGrantBody(email: Record<string, unknown>): Record<string, unknown> {
  return {
    contactId: 'contact-1',
    onboardStatus: 'Provisioned',
    accessRecordId: 'g-1',
    portalUrl: 'https://portal.example',
    grantedAccessLevel: 100000001,
    narrowed: false,
    notificationFailed: false,
    ...email,
  };
}

function fetchWith(inviteAndGrant: Record<string, unknown>): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) return jsonResponse(inviteAndGrant);
    if (url.endsWith('/api/v1/external-access/invite')) return jsonResponse({ status: 'Provisioned', emailSent: true });
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
    // External with an email: routed through `/invite-and-grant`.
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

const callsTo = (fetchMock: jest.Mock, suffix: string) =>
  fetchMock.mock.calls.filter(([url]) => String(url).endsWith(suffix));
const bodyOf = (call: unknown[]) => JSON.parse(String((call[1] as RequestInit).body));

describe('AccessGrantModal — /invite-and-grant whose invite email was not sent', () => {
  it('emailSent false with email_not_sent: the not-sent notice and Retry, and Retry invites through /invite', async () => {
    const fetchMock = fetchWith(
      inviteAndGrantBody({ emailSent: false, emailErrorCode: INVITE_EMAIL_NOT_SENT_REASON_CODE })
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();
    expect(screen.getByText(/cannot sign in yet\. Retry\./)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/external-access/invite-and-grant')).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    const invites = callsTo(fetchMock, '/api/v1/external-access/invite');
    expect(invites).toHaveLength(1);
    expect(bodyOf(invites[0])).toEqual({
      email: CANDIDATE.email,
      recordType: 'project',
      recordId: 'project-1',
      accessLevel: 100000001,
    });
    // The grant already exists: Retry does not grant again.
    expect(callsTo(fetchMock, '/api/v1/external-access/invite-and-grant')).toHaveLength(1);
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument());
  });

  it.each([
    [
      'false with contact_already_has_account',
      { emailSent: false, emailErrorCode: INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE },
    ],
    ['true', { emailSent: true, emailErrorCode: null }],
    ['null (a server that does not report it)', { emailSent: null, emailErrorCode: null }],
    ['absent (a server that does not report it)', {}],
  ])('emailSent %s: granted, no not-sent notice, no Retry, no claim that an invite was sent', async (_label, email) => {
    const fetchMock = fetchWith(inviteAndGrantBody(email));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/no invite was sent/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/invite (was |has been )?sent\b/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/invitation sent/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/external-access/invite')).toHaveLength(0);
    await waitFor(() =>
      expect((screen.getByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` }) as HTMLInputElement).checked).toBe(
        false
      )
    );
  });
});
