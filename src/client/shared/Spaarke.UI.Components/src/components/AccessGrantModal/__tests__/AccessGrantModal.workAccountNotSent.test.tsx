/**
 * AccessGrantModal — C5: a work-account invite whose notice was not sent is never reported as "invited".
 *
 * The rule: a person is shown as invited only when their invite email went out. When the server reports the email as not
 * sent (`emailSent: false` with `sdap.access.invite.email_not_sent`), the person is shown with the not-sent notice and
 * Retry, whatever onboarding status accompanies it (an older server still answered `WorkAccountInvited` there). An email
 * reported as sent (`emailSent: true`) never offers Retry.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  AccessGrantModal,
  INVITE_EMAIL_NOT_SENT_REASON_CODE,
  WORK_ACCOUNT_INVITED_SENTENCE,
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

const NOT_SENT = { emailSent: false, emailErrorCode: INVITE_EMAIL_NOT_SENT_REASON_CODE };

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function inviteAndGrantBody(fields: Record<string, unknown>): Record<string, unknown> {
  return {
    contactId: 'contact-1',
    accessRecordId: 'g-1',
    portalUrl: 'https://portal.example',
    grantedAccessLevel: 100000001,
    narrowed: false,
    notificationFailed: false,
    ...fields,
  };
}

function fetchWith(inviteAndGrant: Record<string, unknown>, invite: Record<string, unknown>): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/invite-and-grant')) return jsonResponse(inviteAndGrant);
    if (url.endsWith('/api/v1/external-access/invite')) return jsonResponse(invite);
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

describe('AccessGrantModal — a work-account invite whose notice was not sent (C5)', () => {
  it.each([
    ['WorkAccountInvited (an older server)', 'WorkAccountInvited'],
    ['the not-sent status', 'Failed'],
  ])(
    'onboardStatus %s with emailSent false: the not-sent notice and Retry, never "invited"',
    async (_label, status) => {
      const fetchMock = fetchWith(inviteAndGrantBody({ onboardStatus: status, ...NOT_SENT }), {
        status: 'WorkAccountInvited',
        emailSent: true,
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await addCandidate();

      expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
      expect(screen.queryByText(new RegExp(WORK_ACCOUNT_INVITED_SENTENCE))).not.toBeInTheDocument();
    }
  );

  it('a Retry whose work-account notice is again not sent keeps the person staged, never "invited"', async () => {
    const fetchMock = fetchWith(inviteAndGrantBody({ onboardStatus: 'WorkAccountInvited', ...NOT_SENT }), {
      status: 'WorkAccountInvited',
      ...NOT_SENT,
    });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();
    await screen.findByText(/No invite was sent to Gene Gatekeeper\./);
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/external-access/invite')).toHaveLength(1));
    expect(await screen.findByText(/No invite was sent to Gene Gatekeeper\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(screen.queryByText(new RegExp(WORK_ACCOUNT_INVITED_SENTENCE))).not.toBeInTheDocument();
  });

  it('MUST NOT FIRE: WorkAccountInvited with emailSent true is "invited", with no Retry', async () => {
    const fetchMock = fetchWith(inviteAndGrantBody({ onboardStatus: 'WorkAccountInvited', emailSent: true }), {});
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(
      await screen.findByText(`Granted access to 1 item(s). Gene Gatekeeper: ${WORK_ACCOUNT_INVITED_SENTENCE}`)
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });

  it('MUST NOT FIRE: an email reported sent never offers Retry, whatever else the response carries', async () => {
    const fetchMock = fetchWith(
      inviteAndGrantBody({
        onboardStatus: 'Provisioned',
        emailSent: true,
        invite: { status: 'Failed', errorCode: INVITE_EMAIL_NOT_SENT_REASON_CODE },
      }),
      {}
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await addCandidate();

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/no invite was sent/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });
});
