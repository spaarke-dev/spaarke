/**
 * AccessGrantModal — slice B3: the kind of sign-in an invite is for is decided by the server from the email.
 *
 * - A `/grant` whose person will sign in with their work account (`invite.status: 'WorkAccountInvited'`) is reported as
 *   "invited — will sign in with their work account". (Task 193: every contact goes to `/grant`.)
 * - A person whose organisation could not be checked: `/grant` writes the grant first and reports that as its invite's
 *   `errorCode` (grantInviteFailed covers the notice and the Retry through `/invite`). `/grant` never answers it as a 503,
 *   so the modal has no branch for one (task 193 removed the `/invite-and-grant` one).
 * - The modal offers no way to choose the kind of sign-in: no portal-account option, no tenant field.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
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

function grantOk(inviteStatus: string): Response {
  return jsonResponse({
    accessRecordId: 'g-1',
    speContainerMembershipGranted: false,
    invite: { status: inviteStatus },
  });
}

/** Answers every read; `/grant` answers the next of `grantAnswers` (the last repeats). */
function fetchWith(grantAnswers: Response[]): jest.Mock {
  let call = 0;
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) throw new Error('not expected: every contact is granted through /grant');
    if (url.endsWith('/api/v1/external-access/grant')) {
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

describe('AccessGrantModal — work-account invites (slice B3)', () => {
  it('reports a WorkAccountInvited result as invited, signing in with their work account', async () => {
    const fetchMock = fetchWith([grantOk('WorkAccountInvited')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(
      await screen.findByText(`Granted access to 1 item(s). Gene Gatekeeper: ${WORK_ACCOUNT_INVITED_SENTENCE}`)
    ).toBeInTheDocument();
  });

  it('a portal-account result says nothing about a work account', async () => {
    const fetchMock = fetchWith([grantOk('Sent')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/work account/)).not.toBeInTheDocument();
  });

  it('the request never chooses the kind of sign-in: no override field is sent and no portal option is shown', async () => {
    const fetchMock = fetchWith([grantOk('WorkAccountInvited')]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();
    await screen.findByText(/will sign in with their work account/);

    const body = JSON.parse(String((grantCalls(fetchMock)[0][1] as RequestInit).body));
    expect(body).not.toHaveProperty('sendAsPortalAccount');
    expect(body).not.toHaveProperty('invitedTenantId');
    expect(screen.queryByText(/portal account/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/tenant/i)).not.toBeInTheDocument();
  });
});
