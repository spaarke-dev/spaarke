/**
 * AccessGrantModal — a `/grant` whose automatic invite did not complete is shown to staff.
 *
 * `/grant` writes the grant and then invites an unbound contact. When that invite does not complete, the response's
 * `invite` field says `Failed` with a reason code; the grant stands, but the person cannot sign in yet. The modal shows
 * that with the same notice and Retry the `/invite-and-grant` organisation-check failure gets. The grant already exists, so
 * Retry invites the person through `/invite` (which always sends) rather than granting again: a re-grant can answer an
 * invite whose send failed as still pending. A `/grant` whose invite was sent, or that reports no invite, shows no such
 * notice.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
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

function grantOk(invite: unknown): Response {
  return jsonResponse({ accessRecordId: 'g-1', speContainerMembershipGranted: false, invite });
}

function problemResponse(status: number, reasonCode: string): Response {
  const body = { status, title: 'Service Unavailable', reasonCode };
  return { ok: false, status, json: async () => body, text: async () => JSON.stringify(body) } as unknown as Response;
}

const INVITE_SENT = jsonResponse({ status: 'Provisioned', emailSent: true });

/**
 * Answers every read; `/grant` answers the next of `grantAnswers` and `/invite` the next of `inviteAnswers` (the last
 * of each repeats).
 */
function fetchWith(grantAnswers: Response[], inviteAnswers: Response[] = [INVITE_SENT]): jest.Mock {
  let call = 0;
  let inviteCall = 0;
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) throw new Error('not expected: the contact is granted through /grant');
    if (url.endsWith('/api/v1/external-access/invite')) {
      const answer = inviteAnswers[Math.min(inviteCall, inviteAnswers.length - 1)];
      inviteCall += 1;
      return answer;
    }
    if (url.includes('/grant')) {
      const answer = grantAnswers[Math.min(call, grantAnswers.length - 1)];
      call += 1;
      return answer;
    }
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
    // Routed through `/grant` (the core that reports the automatic invite in `invite`).
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

const grantCalls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/api/v1/external-access/grant'));
const inviteCalls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/api/v1/external-access/invite'));
const bodyOf = (call: unknown[]) => JSON.parse(String((call[1] as RequestInit).body));

describe('AccessGrantModal — a /grant whose invite did not complete', () => {
  it('invite Failed / tenant_lookup_unavailable: shows the notice with Retry, and Retry invites the person through /invite', async () => {
    const fetchMock = fetchWith([
      grantOk({ status: 'Failed', errorCode: 'sdap.access.invite.tenant_lookup_unavailable' }),
    ]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(
      await screen.findByText(/Couldn't check the organisation of Gene Gatekeeper, so no invite was sent\./)
    ).toBeInTheDocument();
    expect(screen.getByText(/cannot sign in yet\. Retry\./)).toBeInTheDocument();
    expect(screen.queryByText(/portal account/i)).not.toBeInTheDocument();
    expect(grantCalls(fetchMock)).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    // The request that sends the invite: /invite for the person's email on this record, at the level they hold.
    const invites = inviteCalls(fetchMock);
    expect(invites).toHaveLength(1);
    expect((invites[0][1] as RequestInit).method).toBe('POST');
    expect(bodyOf(invites[0])).toEqual({
      email: CANDIDATE.email,
      recordType: 'project',
      recordId: 'project-1',
      accessLevel: 100000001,
    });
    // MUST NOT FIRE: the grant already exists, so Retry does not grant again.
    expect(grantCalls(fetchMock)).toHaveLength(1);
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument());
  });

  it('invite Failed / invite failed: Retry invites through /invite, never a second /grant', async () => {
    const fetchMock = fetchWith([grantOk({ status: 'Failed', errorCode: 'sdap.access.invite.failed' })]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();
    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(inviteCalls(fetchMock)).toHaveLength(1);
    expect(bodyOf(inviteCalls(fetchMock)[0]).email).toBe(CANDIDATE.email);
    expect(grantCalls(fetchMock)).toHaveLength(1);
  });

  it('a Retry whose invite still does not send keeps the person staged, with the notice and Retry', async () => {
    const fetchMock = fetchWith(
      [grantOk({ status: 'Failed', errorCode: 'sdap.access.invite.failed' })],
      [
        problemResponse(503, 'sdap.access.invite.tenant_lookup_unavailable'),
        jsonResponse({ status: 'Provisioned', emailSent: false, emailErrorCode: 'sdap.access.invite.email_not_sent' }),
        INVITE_SENT,
      ]
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();
    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(
      await screen.findByText(/Couldn't check the organisation of Gene Gatekeeper, so no invite was sent\./)
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(inviteCalls(fetchMock)).toHaveLength(2));
    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(inviteCalls(fetchMock)).toHaveLength(3);
    expect(grantCalls(fetchMock)).toHaveLength(1);
  });

  it('invite Failed for another reason: says no invite was sent and offers Retry', async () => {
    const fetchMock = fetchWith([grantOk({ status: 'Failed', errorCode: 'sdap.access.invite.email_not_sent' })]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it.each([
    ['Sent', { status: 'Sent' }],
    ['NotNeeded', { status: 'NotNeeded' }],
    ['absent', undefined],
    ['null', null],
  ])('invite %s: no invite notice and no Retry', async (_label, invite) => {
    const fetchMock = fetchWith([grantOk(invite)]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/no invite was sent/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    expect(inviteCalls(fetchMock)).toHaveLength(0);
  });
});
