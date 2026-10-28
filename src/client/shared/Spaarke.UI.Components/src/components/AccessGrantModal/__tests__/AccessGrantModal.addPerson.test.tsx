/**
 * AccessGrantModal — one "Add person" entry (task 193, #2338).
 *
 * "+ Contact" and "+ User" are replaced by ONE "Add person" lookup over contacts and system users. The modal never
 * classifies a contact: a picked contact goes to `/grant` (the SERVER decides between a share with the colleague the
 * contact represents and a grant to the contact), a picked user to `/share-user`. When `/grant` answers with
 * `sharedWithUser`, the notice says who the record was shared with ("your colleague" when the server could not read the
 * name), and the refreshed Current Access lists that user's share. A share has no end date: when the add carried the
 * record's Expiration, `/grant` answers `expiryNotApplied: true` and the notice says so. A share is not a grant: its
 * notice LEADS with "Shared with X" and never opens "Granted access" (V-fix item 6), and a share whose related records are
 * still pending (the 500 `children_incomplete`) names the colleague and the end date the same way (V-fix item 1).
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, COLLEAGUE_FALLBACK_NAME, sharedWithColleagueSentence } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IContactSearchResult, IPersonPick, IUserPick } from '../types';
import { personFromContact, personFromUser } from './personPickDouble';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const GRANT = '/api/v1/external-access/grant';
const SHARE_USER = '/api/v1/external-access/share-user';
const INVITE_AND_GRANT = '/api/v1/external-access/invite-and-grant';

const ADA_CONTACT: IContactSearchResult = {
  contactId: 'contact-ada',
  fullName: 'Ada (contact)',
  email: 'ada@spaarke.example',
};
const ADA_USER_ID = 'systemuser-ada';
const UMA_USER: IUserPick = { id: 'systemuser-uma', name: 'Uma Userton' };

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function problemResponse(body: unknown, status: number): Response {
  return { ok: false, status, json: async () => body } as unknown as Response;
}

/** `/grant`'s answer for a share written while some related records of the secure record are not updated yet. */
const PENDING_SHARE_WITH_ADA = {
  title: 'Shared',
  detail: '2 related records are not updated yet; they complete automatically.',
  reasonCode: 'sdap.access.user_share.children_incomplete',
  sharedWithUser: { systemUserId: ADA_USER_ID, fullName: 'Ada Lovelace' },
  expiryNotApplied: true,
};

/** `/grant` answers `grant`; `/share-user` answers a created share; `/user-shares` lists Ada once `/grant` shared. */
function fetchWith(grant: Record<string, unknown>): jest.Mock {
  let shared = false;
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) {
      return jsonResponse({
        shares: shared
          ? [
              {
                systemUserId: ADA_USER_ID,
                fullName: 'Ada Lovelace',
                accessRightsMask: 262167,
                accessLevel: 100000001,
                modifiedOn: '2026-10-28T00:00:00Z',
              },
            ]
          : [],
      });
    }
    if (url === INVITE_AND_GRANT)
      throw new Error('not expected: the modal never routes a contact to /invite-and-grant');
    if (url === GRANT) {
      shared = grant.sharedWithUser != null;
      return jsonResponse(grant);
    }
    if (url === SHARE_USER) return jsonResponse({ systemUserId: UMA_USER.id, outcome: 'created', narrowed: false });
    return jsonResponse({});
  });
}

const SHARED_WITH_ADA = {
  accessRecordId: '00000000-0000-0000-0000-000000000000',
  speContainerMembershipGranted: false,
  grantedAccessLevel: 100000001,
  narrowed: false,
  notificationFailed: false,
  invite: null,
  sharedWithUser: { systemUserId: ADA_USER_ID, fullName: 'Ada Lovelace' },
};

function makeProps(
  authenticatedFetch: jest.Mock,
  pick: IPersonPick | null,
  overrides: Partial<IAccessGrantModalProps> = {}
): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-1',
    recordType: 'matter',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    pickPerson: jest.fn(async () => pick),
    pickOrganization: jest.fn(async () => null),
    canGrantAccess: true,
    ...overrides,
  };
}

function levelComboFor(name: string): HTMLElement {
  let el: HTMLElement | null = screen.getByText(name);
  while (el && !el.querySelector('[role="combobox"]')) el = el.parentElement;
  const combo = el?.querySelector('[role="combobox"]') as HTMLElement | null;
  if (!combo) throw new Error(`No access-level dropdown found for row "${name}"`);
  return combo;
}

async function addPerson(name: string): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Add person' }));
  await screen.findByText(name);
  fireEvent.click(levelComboFor(name));
  fireEvent.click(await screen.findByRole('option', { name: 'Collaborate' }));
  fireEvent.click(screen.getByRole('button', { name: /^Add \(\d+\)$/ }));
}

const postsTo = (fetchMock: jest.Mock, url: string) =>
  fetchMock.mock.calls.filter(([u, init]) => u === url && (init as RequestInit | undefined)?.method === 'POST');
const bodyOf = (call: unknown[]) => JSON.parse(String((call[1] as RequestInit).body)) as Record<string, unknown>;

describe('AccessGrantModal — one "Add person" entry (task 193)', () => {
  it('shows ONE "Add person" button beside "+ Organization", and no "Add contact" or "Add user"', async () => {
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith({}), null)} />);

    expect(await screen.findByRole('button', { name: 'Add person' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add organization' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add contact' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add user' })).not.toBeInTheDocument();
    expect(
      screen.getByText('No contacts, organizations or users yet. Use “Add person” or “+ Organization” to add.')
    ).toBeInTheDocument();
  });

  it('a picked contact posts /grant with its contactId — never /share-user or /invite-and-grant', async () => {
    const fetchMock = fetchWith({ accessRecordId: 'g-1', speContainerMembershipGranted: false });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

    await addPerson(ADA_CONTACT.fullName);

    await waitFor(() => expect(postsTo(fetchMock, GRANT)).toHaveLength(1));
    expect(bodyOf(postsTo(fetchMock, GRANT)[0])).toMatchObject({
      contactId: ADA_CONTACT.contactId,
      recordType: 'matter',
      recordId: 'matter-1',
      accessLevel: 100000001,
    });
    expect(postsTo(fetchMock, SHARE_USER)).toHaveLength(0);
    expect(postsTo(fetchMock, INVITE_AND_GRANT)).toHaveLength(0);
  });

  it('a picked user posts /share-user with its systemUserId — never /grant', async () => {
    const fetchMock = fetchWith({});
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromUser(UMA_USER))} />);

    await addPerson(UMA_USER.name);

    await waitFor(() => expect(postsTo(fetchMock, SHARE_USER)).toHaveLength(1));
    expect(bodyOf(postsTo(fetchMock, SHARE_USER)[0])).toMatchObject({
      systemUserId: UMA_USER.id,
      accessLevel: 100000001,
    });
    expect(postsTo(fetchMock, GRANT)).toHaveLength(0);
  });

  it('a /grant answered with sharedWithUser says who it was shared with, and Current Access lists the user share', async () => {
    const fetchMock = fetchWith(SHARED_WITH_ADA);
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

    await addPerson(ADA_CONTACT.fullName);

    expect(await screen.findByText('Shared with Ada Lovelace — internal user.')).toBeInTheDocument();
    // The refreshed Current Access shows the share as a user share (no grant row was written).
    expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
  });

  it('a sharedWithUser with no name is shown with the neutral label', async () => {
    const fetchMock = fetchWith({ ...SHARED_WITH_ADA, sharedWithUser: { systemUserId: ADA_USER_ID, fullName: null } });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

    await addPerson(ADA_CONTACT.fullName);

    expect(await screen.findByText(`Shared with ${COLLEAGUE_FALLBACK_NAME} — internal user.`)).toBeInTheDocument();
  });

  it('expiryNotApplied: the notice says shares with colleagues have no end date', async () => {
    const fetchMock = fetchWith({ ...SHARED_WITH_ADA, expiryNotApplied: true });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

    await addPerson(ADA_CONTACT.fullName);

    expect(
      await screen.findByText('Shared with Ada Lovelace. Shares with colleagues have no end date.')
    ).toBeInTheDocument();
    // The add did carry the record's Expiration (today + 90 with no share yet); the server did not apply it.
    expect(bodyOf(postsTo(fetchMock, GRANT)[0]).expiryDate).toEqual(expect.any(String));
  });

  it('MUST NOT FIRE: a /grant with no sharedWithUser says nothing about a share', async () => {
    const fetchMock = fetchWith({
      accessRecordId: 'g-1',
      speContainerMembershipGranted: false,
      expiryNotApplied: true,
    });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

    await addPerson(ADA_CONTACT.fullName);

    expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
    expect(screen.queryByText(/Shared with/)).not.toBeInTheDocument();
    expect(screen.queryByText(/no end date/)).not.toBeInTheDocument();
  });

  it('a suggestion Grant that /grant answers with a share says so too', async () => {
    const fetchMock = throwingAuthenticatedFetch(async (url: string) => {
      if (url.includes('/assigned-access?')) {
        return jsonResponse({
          entries: [
            {
              entryId: 'entry-1',
              sourceField: 'sprk_assignedparalegal1',
              sourceFieldLabel: 'Assigned Paralegal 1',
              subjectKind: 'contact',
              subjectId: ADA_CONTACT.contactId,
              subjectName: 'Ada (contact)',
              systemUserId: null,
              accessRecordId: null,
              state: 'PendingConfirmation',
              reason: null,
              residualAccessTerms: [],
            },
          ],
        });
      }
      if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
      if (url === GRANT) return jsonResponse({ ...SHARED_WITH_ADA, expiryNotApplied: true });
      return jsonResponse({});
    });
    renderWithTheme(
      <AccessGrantModal {...makeProps(fetchMock, null, { accessPermissionState: 'limited', isSecureRecord: true })} />
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Ada (contact)' }));

    expect(
      await screen.findByText(
        'Shared with Ada Lovelace. Shares with colleagues have no end date. Suggested from Assigned Paralegal 1.'
      )
    ).toBeInTheDocument();
    expect(screen.queryByText(/^Granted/)).not.toBeInTheDocument();
    expect(postsTo(fetchMock, GRANT)).toHaveLength(1);
  });

  it('a share and a grant in one Add: the notice leads with the share, then counts the other grant', async () => {
    const fetchMock = fetchWith(SHARED_WITH_ADA);
    const pickPerson = jest
      .fn()
      .mockResolvedValueOnce(personFromContact(ADA_CONTACT))
      .mockResolvedValueOnce(personFromUser(UMA_USER));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, null, { pickPerson })} />);

    for (const name of [ADA_CONTACT.fullName, UMA_USER.name]) {
      fireEvent.click(await screen.findByRole('button', { name: 'Add person' }));
      await screen.findByText(name);
      fireEvent.click(levelComboFor(name));
      fireEvent.click(await screen.findByRole('option', { name: 'Collaborate' }));
    }
    fireEvent.click(screen.getByRole('button', { name: 'Add (2)' }));

    expect(
      await screen.findByText('Shared with Ada Lovelace — internal user. Granted access to 1 other item(s).')
    ).toBeInTheDocument();
  });

  it('a /grant share whose related records are pending names the colleague and says there is no end date', async () => {
    const fetchMock = throwingAuthenticatedFetch(async (url: string) => {
      if (url === GRANT) return problemResponse(PENDING_SHARE_WITH_ADA, 500);
      if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
      return jsonResponse({});
    });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

    await addPerson(ADA_CONTACT.fullName);

    expect(
      await screen.findByText(
        'Shared with Ada Lovelace. Shares with colleagues have no end date. 2 related records are not updated yet; ' +
          'they complete automatically.'
      )
    ).toBeInTheDocument();
    expect(screen.queryByText(/^Granted/)).not.toBeInTheDocument();
  });

  it('a suggestion Grant whose /grant share has pending related records names the colleague and the end date', async () => {
    const fetchMock = throwingAuthenticatedFetch(async (url: string) => {
      if (url.includes('/assigned-access?')) {
        return jsonResponse({
          entries: [
            {
              entryId: 'entry-1',
              sourceField: 'sprk_assignedparalegal1',
              sourceFieldLabel: 'Assigned Paralegal 1',
              subjectKind: 'contact',
              subjectId: ADA_CONTACT.contactId,
              subjectName: 'Ada (contact)',
              systemUserId: null,
              accessRecordId: null,
              state: 'PendingConfirmation',
              reason: null,
              residualAccessTerms: [],
            },
          ],
        });
      }
      if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
      if (url === GRANT) return problemResponse(PENDING_SHARE_WITH_ADA, 500);
      return jsonResponse({});
    });
    renderWithTheme(
      <AccessGrantModal {...makeProps(fetchMock, null, { accessPermissionState: 'limited', isSecureRecord: true })} />
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Ada (contact)' }));

    expect(
      await screen.findByText(
        'Shared with Ada Lovelace. Shares with colleagues have no end date. Suggested from Assigned Paralegal 1. ' +
          '2 related records are not updated yet; they complete automatically.'
      )
    ).toBeInTheDocument();
  });

  it('the notice sentences, pure', () => {
    expect(sharedWithColleagueSentence({ name: 'Ada Lovelace', expiryNotApplied: false })).toBe(
      'Shared with Ada Lovelace — internal user.'
    );
    expect(sharedWithColleagueSentence({ name: COLLEAGUE_FALLBACK_NAME, expiryNotApplied: true })).toBe(
      'Shared with your colleague. Shares with colleagues have no end date.'
    );
  });

  it('the modal never classifies a contact: no isInternalContact, and no /invite-and-grant call', () => {
    const src = fs.readFileSync(path.join(__dirname, '../AccessGrantModal.tsx'), 'utf8');
    expect(src).not.toMatch(/isInternalContact/);
    expect(src).not.toMatch(/['`]\/api\/v1\/external-access\/invite-and-grant['`]/);
  });
});
