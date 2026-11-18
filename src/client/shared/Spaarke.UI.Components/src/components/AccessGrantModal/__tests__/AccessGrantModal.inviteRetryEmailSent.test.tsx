/**
 * AccessGrantModal — an invite Retry counts as sent only when `/invite` says `emailSent: true` (slice C2b).
 *
 * A person whose grant stands but whose invite was not sent stays staged with Retry. Retry calls `/invite`. The person is
 * cleared only when that response says the email went (`emailSent: true`); `false`, `null` or an absent field keeps them
 * staged with the notice and Retry. A person who already has an account is sent nothing: they stay staged and the notice
 * shows the server's text and the sign-in link to pass on.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE, safeSignInHref } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IAccessGrantCandidate } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const CANDIDATE: IAccessGrantCandidate = {
  contactId: 'contact-1',
  fullName: 'Gene Gatekeeper',
  email: 'gene@customer.example',
  role: 'Assigned Attorney 1',
};

const STAFF_TEXT =
  'This person already has an account, so no invite was sent and nothing was changed. They sign in at the link below; send it to them if they need it.';
const SIGN_IN_URL = 'https://portal.example/?customer=acme';

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

const GRANT_INVITE_FAILED = jsonResponse({
  accessRecordId: 'g-1',
  speContainerMembershipGranted: false,
  invite: { status: 'Failed', errorCode: 'sdap.access.invite.failed' },
});

const INVITE_SENT = jsonResponse({ status: 'Provisioned', emailSent: true });

/** `/grant` answers that the automatic invite failed; `/invite` answers the next of `inviteAnswers` (the last repeats). */
function fetchWith(inviteAnswers: Response[]): jest.Mock {
  let inviteCall = 0;
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) throw new Error('not expected: the contact is granted through /grant');
    if (url.endsWith('/api/v1/external-access/invite')) {
      const answer = inviteAnswers[Math.min(inviteCall, inviteAnswers.length - 1)];
      inviteCall += 1;
      return answer;
    }
    if (url.includes('/grant')) return GRANT_INVITE_FAILED;
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

/** Adds the candidate; `/grant` reports the invite not sent, so the person is staged with Retry. */
async function addCandidateWhoseInviteWasNotSent(): Promise<void> {
  await screen.findByText(CANDIDATE.fullName);
  fireEvent.click(screen.getByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` }));
  fireEvent.click(levelComboFor(CANDIDATE.fullName));
  fireEvent.click(await screen.findByRole('option', { name: 'Collaborate' }));
  fireEvent.click(screen.getByRole('button', { name: /^Add \(\d+\)$/ }));
  expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();
}

const inviteCalls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/api/v1/external-access/invite'));

const isStaged = () =>
  (screen.getByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` }) as HTMLInputElement).checked;

describe('AccessGrantModal — an invite Retry is sent only when emailSent is true', () => {
  it.each([
    ['false', { status: 'Provisioned', emailSent: false, emailErrorCode: 'sdap.access.invite.email_not_sent' }],
    ['null', { status: 'Provisioned', emailSent: null }],
    ['absent', { status: 'Provisioned' }],
  ])('emailSent %s: the person stays staged with the notice and Retry', async (_label, body) => {
    const fetchMock = fetchWith([jsonResponse(body)]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);
    await addCandidateWhoseInviteWasNotSent();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(inviteCalls(fetchMock)).toHaveLength(1));

    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(isStaged()).toBe(true);
    expect(screen.queryByText('Granted access to 1 item(s).')).not.toBeInTheDocument();
  });

  it('emailSent true: the person is cleared (must not fire)', async () => {
    const fetchMock = fetchWith([INVITE_SENT]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);
    await addCandidateWhoseInviteWasNotSent();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument());
  });

  it('already has an account: stays staged, and the notice shows the server text and the sign-in link', async () => {
    const fetchMock = fetchWith([
      jsonResponse({
        status: 'AlreadyProvisioned',
        emailSent: false,
        emailErrorCode: INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE,
        staffMessage: STAFF_TEXT,
        signInUrl: SIGN_IN_URL,
      }),
    ]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);
    await addCandidateWhoseInviteWasNotSent();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    const notice = await screen.findByText(/already has an account/);
    expect(notice.textContent).toContain(`Gene Gatekeeper: ${STAFF_TEXT} ${SIGN_IN_URL}`);
    expect(screen.queryByText(/No invite was sent to Gene Gatekeeper\./)).not.toBeInTheDocument();
    // Retrying would answer the same, so no Retry is offered for this alone.
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    expect(isStaged()).toBe(true);
    expect(inviteCalls(fetchMock)).toHaveLength(1);
  });
});

/**
 * Slice C2c: the sign-in link is a link only when it is an absolute https URL. Any other value (a script URL, a relative
 * path, plain http) is shown as text, never as a link.
 */
describe('AccessGrantModal — the already-has-account sign-in link', () => {
  async function noticeFor(signInUrl: string): Promise<HTMLElement> {
    const fetchMock = fetchWith([
      jsonResponse({
        status: 'AlreadyProvisioned',
        emailSent: false,
        emailErrorCode: INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE,
        staffMessage: STAFF_TEXT,
        signInUrl,
      }),
    ]);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);
    await addCandidateWhoseInviteWasNotSent();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    return screen.findByText(/already has an account/);
  }

  it('an absolute https URL renders as a link to that URL', async () => {
    const notice = await noticeFor(SIGN_IN_URL);

    const link = screen.getByRole('link', { name: SIGN_IN_URL });
    expect(link).toHaveAttribute('href', SIGN_IN_URL);
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    expect(notice.textContent).toContain(`Gene Gatekeeper: ${STAFF_TEXT} ${SIGN_IN_URL}`);
  });

  it.each([
    ['a javascript: URL', 'javascript:void(0)'],
    ['a relative path', '/portal/?customer=acme'],
    ['a protocol-relative URL', '//portal.example/?customer=acme'],
    ['plain http', 'http://portal.example/?customer=acme'],
  ])('%s renders as text, never as a link', async (_label, signInUrl) => {
    const notice = await noticeFor(signInUrl);

    expect(screen.queryByRole('link', { name: signInUrl })).not.toBeInTheDocument();
    expect(document.querySelector(`a[href="${signInUrl}"]`)).toBeNull();
    expect(notice.textContent).toContain(`Gene Gatekeeper: ${STAFF_TEXT} ${signInUrl}`);
  });

  it.each([
    ['https://portal.example/?customer=acme', 'https://portal.example/?customer=acme'],
    ['javascript:void(0)', undefined],
    ['JavaScript:void(0)', undefined],
    ['/relative', undefined],
    ['http://portal.example', undefined],
    ['', undefined],
  ])('safeSignInHref(%p) is %p', (input, expected) => {
    expect(safeSignInHref(input)).toBe(expected);
  });
});
