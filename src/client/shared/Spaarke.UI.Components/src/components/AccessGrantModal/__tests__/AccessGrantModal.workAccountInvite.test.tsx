/**
 * AccessGrantModal — slice B3: the kind of sign-in an invite is for is decided by the server from the email.
 *
 * - A `/invite-and-grant` whose person will sign in with their work account (`onboardStatus: 'WorkAccountInvited'`) is
 *   reported as "invited — will sign in with their work account".
 * - A 503 `sdap.access.invite.tenant_lookup_unavailable` (the person's organisation could not be checked; nothing was
 *   written) is reported with a Retry action, never as the generic "failed" notice, and Retry adds the same row again.
 * - The modal offers no way to choose the kind of sign-in: no portal-account option, no tenant field.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, WORK_ACCOUNT_INVITED_SENTENCE } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IAccessGrantCandidate } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const CANDIDATE: IAccessGrantCandidate = {
  contactId: 'contact-1',
  fullName: 'Gene Gatekeeper',
  email: 'gene@customer.example',
  role: 'Assigned Attorney 1',
};

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 500): Response {
  return {
    ok,
    status,
    json: async () => (ok ? body : { status, ...(body as Record<string, unknown>) }),
  } as unknown as Response;
}

const LOOKUP_UNAVAILABLE = jsonResponse(
  {
    title: 'Organisation lookup unavailable',
    detail:
      "The person's organisation could not be checked just now, so nothing was created or sent. Try again in a moment.",
    reasonCode: 'sdap.access.invite.tenant_lookup_unavailable',
  },
  false,
  503
);

function inviteAndGrantOk(onboardStatus: string): Response {
  return jsonResponse({ contactId: 'contact-1', onboardStatus, accessRecordId: 'g-1', portalUrl: 'https://portal' });
}

/** Answers every read; `/invite-and-grant` answers the next of `inviteAnswers` (the last repeats). */
function fetchWith(inviteAnswers: Response[]): jest.Mock {
  let call = 0;
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) {
      const answer = inviteAnswers[Math.min(call, inviteAnswers.length - 1)];
      call += 1;
      return answer;
    }
    if (url.includes('/grant')) return jsonResponse({ accessRecordId: 'g-2', speContainerMembershipGranted: false });
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

const inviteCalls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.filter(([url]) => String(url).includes('/invite-and-grant'));

describe('AccessGrantModal — work-account invites (slice B3)', () => {
  it('reports a WorkAccountInvited result as invited, signing in with their work account', async () => {
    const fetchMock = fetchWith([inviteAndGrantOk('WorkAccountInvited')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(
      await screen.findByText(`Granted access to 1 item(s). Gene Gatekeeper: ${WORK_ACCOUNT_INVITED_SENTENCE}`)
    ).toBeInTheDocument();
  });

  it('a portal-account result says nothing about a work account', async () => {
    const fetchMock = fetchWith([inviteAndGrantOk('Provisioned')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/work account/)).not.toBeInTheDocument();
  });

  it('a 503 tenant_lookup_unavailable offers Retry (not the generic failure), and Retry adds the same person again', async () => {
    const fetchMock = fetchWith([LOOKUP_UNAVAILABLE, inviteAndGrantOk('WorkAccountInvited')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText(/Couldn't check the organisation of Gene Gatekeeper/)).toBeInTheDocument();
    expect(screen.queryByText(/failed\. Please try again/)).not.toBeInTheDocument();
    expect(screen.queryByText(/portal account/i)).not.toBeInTheDocument();
    expect(inviteCalls(fetchMock)).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(
      await screen.findByText(`Granted access to 1 item(s). Gene Gatekeeper: ${WORK_ACCOUNT_INVITED_SENTENCE}`)
    ).toBeInTheDocument();
    const calls = inviteCalls(fetchMock);
    expect(calls).toHaveLength(2);
    const retried = JSON.parse(String((calls[1][1] as RequestInit).body));
    expect(retried.email).toBe(CANDIDATE.email);
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument());
  });

  it('the request never chooses the kind of sign-in: no override field is sent and no portal option is shown', async () => {
    const fetchMock = fetchWith([inviteAndGrantOk('WorkAccountInvited')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();
    await screen.findByText(/will sign in with their work account/);

    const body = JSON.parse(String((inviteCalls(fetchMock)[0][1] as RequestInit).body));
    expect(body).not.toHaveProperty('sendAsPortalAccount');
    expect(body).not.toHaveProperty('invitedTenantId');
    expect(screen.queryByText(/portal account/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/tenant/i)).not.toBeInTheDocument();
  });
});
