/**
 * AccessGrantModal — "Add person" and the colleague's workforce-app access (task 193 G3b).
 *
 * Adding an internal colleague shares the record with them (this app, no end date) and, unless a reason applies, also
 * grants the contact that represents them, paired with the share, so the record shows in their workforce app until a
 * date. The server answers `workforceApp { visible, reason, until }` (`/grant`, `/share-user` and their
 * related-records-pending 500), and the notice says it plainly: "Shared with X", then "visible in their workforce app
 * until {date}" or why not (one sentence per reason group; never a code or an id), then — when the add carried an
 * Expiration — that the SHARE itself has no end date.
 *
 * Current Access lists a share and its paired grant together (`pairedWith`), removing the share says it removes both,
 * removing the grant alone says the share stays, and `/unshare-user`'s answer about the paired grant is worded truthfully.
 * G3d: no note promises timing (the daily clean-up ends the grant only when its writes are on), and the reload keeps a
 * grant listed once its share is gone. G4b: `pairedGrant` is read first; a grant not confirmed ended may still be active,
 * and the notice offers Try again (`AccessGrantModal.unshareOutcome.test.tsx`).
 * G3e: a grant counts as paired only while its share is listed; one whose share is not listed is worded without a share
 * (row, revoke confirmation, success notice).
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  AccessGrantModal,
  COLLEAGUE_FALLBACK_NAME,
  REVOKE_PAIRED_SHARE_SENTENCE,
  SHARE_EXPIRY_NOT_APPLIED_SENTENCE,
  groupPairedAccess,
  pairedGrantNotEndedSentence,
  pairedShareNote,
  shareExpiryPairedSentence,
  revokePairedGrantSentence,
  revokeUnlistedShareGrantSentence,
  sharedWithColleagueSentence,
  unlistedShareGrantMeta,
  unshareNotice,
  workforceAppNotShownSentence,
} from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import { confirmRevoke } from '../../../__tests__/helpers/confirmDialog';
import type {
  IAccessGrantModalProps,
  IAccessGrantRecord,
  IContactSearchResult,
  IPersonPick,
  IUserPick,
} from '../types';
import { personFromContact, personFromUser } from './personPickDouble';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const GRANT = '/api/v1/external-access/grant';
const SHARE_USER = '/api/v1/external-access/share-user';
const UNSHARE_USER = '/api/v1/external-access/unshare-user';
const REVOKE = '/api/v1/external-access/revoke';

const ADA_CONTACT: IContactSearchResult = {
  contactId: 'contact-ada',
  fullName: 'Ada (contact)',
  email: 'ada@x.example',
};
const ADA_USER_ID = 'systemuser-ada';
const UMA_USER: IUserPick = { id: 'systemuser-uma', name: 'Uma Userton' };
const UNTIL = '2027-01-26';
const PAIRED_GRANT_ID = '11111111-2222-3333-4444-555555555555';

/** A bare `yyyy-MM-dd` as the modal shows it (`formatGrantDate`). */
function displayDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function problemResponse(body: unknown, status: number): Response {
  return {
    ok: false,
    status,
    json: async () => ({ status, ...(body as Record<string, unknown>) }),
  } as unknown as Response;
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

const VISIBLE = { visible: true, reason: null, until: UNTIL, accessRecordId: PAIRED_GRANT_ID };
const notVisible = (reason: string) => ({ visible: false, reason, until: null, accessRecordId: null });

/** `/grant` answers `grant`; `/share-user` answers `shareUser`; every other read is empty. */
function fetchWith(answers: { grant?: () => Response; shareUser?: () => Response }): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url === GRANT && answers.grant) return answers.grant();
    if (url === SHARE_USER && answers.shareUser) return answers.shareUser();
    return jsonResponse({});
  });
}

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

const LEAD = "They can open the record in this app, but it won't show in their workforce app";

/** Every reason the server can give, by group, and the clause the notice uses for it. */
const REASON_GROUPS: Array<{ group: string; codes: string[]; sentence: string }> = [
  {
    group: 'Restricted',
    codes: ['sdap.access.grant.record_restricted'],
    sentence: `${LEAD} because the record is Restricted.`,
  },
  {
    group: 'no contact, several contacts, or a contact representing other users',
    codes: [
      'sdap.access.share.workforce_no_linked_contact',
      'sdap.access.share.workforce_several_linked_contacts',
      'sdap.access.grant.contact_represents_several_users',
    ],
    sentence: `${LEAD} because that needs exactly one contact record linked only to them.`,
  },
  {
    group: 'pairing column missing',
    codes: ['sdap.access.share.workforce_pairing_column_missing'],
    sentence: `${LEAD} because workforce app access is not set up in this environment yet.`,
  },
  {
    group: 'pairing unreadable, grant write failed, link unreadable, or another read fault',
    codes: [
      'sdap.access.share.workforce_pairing_unreadable',
      'sdap.access.share.workforce_grant_failed',
      'sdap.access.share.workforce_link_unreadable',
      'sdap.access.grant.caller_rights_unreadable',
      'sdap.access.grant.policy_unreadable',
      'sdap.access.grant.owner_unreadable',
      'sdap.access.grant.no_access_unverifiable',
    ],
    sentence: `${LEAD} because that access could not be set up just now; adding them again retries it.`,
  },
  {
    group: 'contact on the No Access List',
    codes: ['sdap.access.grant.grantee_denied'],
    sentence: `${LEAD} because their contact is on this record's No Access List.`,
  },
  {
    group: 'their contact already holds more',
    codes: ['sdap.access.grant.would_lower_existing'],
    sentence:
      'Their contact already holds a higher workforce app grant on this record, which is left as it is. If that ' +
      'grant has expired, someone with access at that level can renew it.',
  },
  {
    group: 'any other or no reason',
    codes: ['sdap.access.grant.caller_cannot_grant', 'sdap.access.grant.something_new', ''],
    sentence: `${LEAD} because that access could not be given.`,
  },
];

describe('AccessGrantModal — Add person and workforce-app access (task 193 G3b)', () => {
  describe('the notice after a successful Add person', () => {
    it('/grant visible: "Shared with X — visible in their workforce app until {date}."', async () => {
      const fetchMock = fetchWith({ grant: () => jsonResponse({ ...SHARED_WITH_ADA, workforceApp: VISIBLE }) });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

      await addPerson(ADA_CONTACT.fullName);

      expect(
        await screen.findByText(
          `Shared with Ada Lovelace — visible in their workforce app until ${displayDate(UNTIL)}.`
        )
      ).toBeInTheDocument();
    });

    it('/share-user (a user picked directly) visible: the same sentence', async () => {
      const fetchMock = fetchWith({
        shareUser: () =>
          jsonResponse({ systemUserId: UMA_USER.id, outcome: 'created', narrowed: false, workforceApp: VISIBLE }),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromUser(UMA_USER))} />);

      await addPerson(UMA_USER.name);

      expect(
        await screen.findByText(`Shared with Uma Userton — visible in their workforce app until ${displayDate(UNTIL)}.`)
      ).toBeInTheDocument();
    });

    it('/grant not visible (Restricted): this app yes, the workforce app no, and why — no code shown', async () => {
      const fetchMock = fetchWith({
        grant: () =>
          jsonResponse({ ...SHARED_WITH_ADA, workforceApp: notVisible('sdap.access.grant.record_restricted') }),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

      await addPerson(ADA_CONTACT.fullName);

      expect(
        await screen.findByText(`Shared with Ada Lovelace. ${LEAD} because the record is Restricted.`)
      ).toBeInTheDocument();
      expect(screen.queryByText(/sdap\./)).not.toBeInTheDocument();
    });

    it('/share-user not visible (no linked contact): the contact sentence', async () => {
      const fetchMock = fetchWith({
        shareUser: () =>
          jsonResponse({
            systemUserId: UMA_USER.id,
            outcome: 'created',
            narrowed: false,
            workforceApp: notVisible('sdap.access.share.workforce_no_linked_contact'),
          }),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromUser(UMA_USER))} />);

      await addPerson(UMA_USER.name);

      expect(
        await screen.findByText(
          `Shared with Uma Userton. ${LEAD} because that needs exactly one contact record linked only to them.`
        )
      ).toBeInTheDocument();
    });

    it('visible with an Expiration: the workforce date, then the share itself has no end date', async () => {
      const fetchMock = fetchWith({
        grant: () => jsonResponse({ ...SHARED_WITH_ADA, expiryNotApplied: true, workforceApp: VISIBLE }),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

      await addPerson(ADA_CONTACT.fullName);

      expect(
        await screen.findByText(
          `Shared with Ada Lovelace — visible in their workforce app until ${displayDate(UNTIL)}. ` +
            shareExpiryPairedSentence(UNTIL)
        )
      ).toBeInTheDocument();
    });

    it('a /share-user whose related records are pending carries the workforce-app outcome too', async () => {
      const fetchMock = fetchWith({
        shareUser: () =>
          problemResponse(
            {
              title: 'Shared',
              detail: '2 related records are not updated yet; they complete automatically.',
              reasonCode: 'sdap.access.user_share.children_incomplete',
              workforceApp: notVisible('sdap.access.share.workforce_pairing_column_missing'),
            },
            500
          ),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromUser(UMA_USER))} />);

      await addPerson(UMA_USER.name);

      expect(
        await screen.findByText(
          `Shared with Uma Userton. ${LEAD} because workforce app access is not set up in this environment yet. ` +
            '2 related records are not updated yet; they complete automatically.'
        )
      ).toBeInTheDocument();
    });

    it('a /grant share whose related records are pending carries the workforce-app outcome too', async () => {
      const fetchMock = fetchWith({
        grant: () =>
          problemResponse(
            {
              title: 'Shared',
              detail: '2 related records are not updated yet; they complete automatically.',
              reasonCode: 'sdap.access.user_share.children_incomplete',
              sharedWithUser: { systemUserId: ADA_USER_ID, fullName: 'Ada Lovelace' },
              workforceApp: VISIBLE,
            },
            500
          ),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, personFromContact(ADA_CONTACT))} />);

      await addPerson(ADA_CONTACT.fullName);

      expect(
        await screen.findByText(
          `Shared with Ada Lovelace — visible in their workforce app until ${displayDate(UNTIL)}. ` +
            '2 related records are not updated yet; they complete automatically.'
        )
      ).toBeInTheDocument();
    });

    it('a user suggestion through /share-user carries the workforce-app outcome', async () => {
      const fetchMock = throwingAuthenticatedFetch(async (url: string) => {
        if (url.includes('/assigned-access?')) {
          return jsonResponse({
            entries: [
              {
                entryId: 'entry-2',
                sourceField: 'sprk_assignedto',
                sourceFieldLabel: 'Assigned To',
                subjectKind: 'contact',
                subjectId: ADA_CONTACT.contactId,
                subjectName: 'Ada Lovelace',
                systemUserId: ADA_USER_ID,
                accessRecordId: null,
                state: 'PendingConfirmation',
                reason: null,
                residualAccessTerms: [],
              },
            ],
          });
        }
        if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
        if (url === SHARE_USER) {
          return jsonResponse({
            systemUserId: ADA_USER_ID,
            outcome: 'created',
            narrowed: false,
            workforceApp: VISIBLE,
          });
        }
        return jsonResponse({});
      });
      renderWithTheme(
        <AccessGrantModal {...makeProps(fetchMock, null, { accessPermissionState: 'limited', isSecureRecord: true })} />
      );

      fireEvent.click(await screen.findByRole('button', { name: 'Grant Ada Lovelace' }));

      expect(
        await screen.findByText(
          `Shared with Ada Lovelace — visible in their workforce app until ${displayDate(UNTIL)}. Suggested from Assigned To.`
        )
      ).toBeInTheDocument();
    });

    it.each(REASON_GROUPS)('every reason in a group reads as one plain sentence: $group', ({ codes, sentence }) => {
      for (const code of codes) {
        expect(workforceAppNotShownSentence(code || undefined)).toBe(sentence);
        const text = sharedWithColleagueSentence({
          name: 'Ada Lovelace',
          expiryNotApplied: false,
          workforceApp: { visible: false, reason: code || undefined },
        });
        expect(text).toBe(`Shared with Ada Lovelace. ${sentence}`);
        // Plain words: never a code, an id or a date in a not-visible sentence.
        expect(text).not.toMatch(/sdap|_|\d/);
      }
    });

    it('the sentences, pure: visible, not visible, with and without an Expiration, and no workforceApp', () => {
      const until = displayDate(UNTIL);
      expect(
        sharedWithColleagueSentence({
          name: 'Ada Lovelace',
          expiryNotApplied: false,
          workforceApp: { visible: true, until: UNTIL },
        })
      ).toBe(`Shared with Ada Lovelace — visible in their workforce app until ${until}.`);
      expect(
        sharedWithColleagueSentence({
          name: COLLEAGUE_FALLBACK_NAME,
          expiryNotApplied: true,
          workforceApp: { visible: false, reason: 'sdap.access.grant.record_restricted' },
        })
      ).toBe(
        `Shared with your colleague. ${LEAD} because the record is Restricted. ${SHARE_EXPIRY_NOT_APPLIED_SENTENCE}`
      );
      // G4b: visible with an Expiration: the share has no end date, the workforce app access ends on its date.
      expect(
        sharedWithColleagueSentence({
          name: 'Ada Lovelace',
          expiryNotApplied: true,
          workforceApp: { visible: true, until: UNTIL },
        })
      ).toBe(
        `Shared with Ada Lovelace — visible in their workforce app until ${until}. The share itself has no end date; only ` +
          `their workforce app access ends on ${until}.`
      );
      // An older server (no workforceApp): unchanged lead; the Expiration sentence names the share.
      expect(sharedWithColleagueSentence({ name: 'Ada Lovelace', expiryNotApplied: false })).toBe(
        'Shared with Ada Lovelace — internal user.'
      );
      expect(sharedWithColleagueSentence({ name: 'Ada Lovelace', expiryNotApplied: true })).toBe(
        `Shared with Ada Lovelace. ${SHARE_EXPIRY_NOT_APPLIED_SENTENCE}`
      );
      expect(SHARE_EXPIRY_NOT_APPLIED_SENTENCE).toMatch(/^The share itself has no end date/);
    });
  });

  describe('Current Access: a share and its paired workforce-app grant', () => {
    const ADA_SHARE_JSON = {
      systemUserId: ADA_USER_ID,
      fullName: 'Ada Lovelace',
      accessRightsMask: 262167,
      accessLevel: 100000001,
      modifiedOn: '2026-10-28T00:00:00Z',
      pairedWith: { id: PAIRED_GRANT_ID, kind: 'grant' },
    };
    const PAIRED_GRANT: IAccessGrantRecord = {
      accessRecordId: PAIRED_GRANT_ID,
      contactId: ADA_CONTACT.contactId,
      fullName: 'Ada (contact)',
      accessLevel: 100000001,
      grantedByName: 'Alice Admin',
      grantedDate: '2026-10-28',
      expiresDate: UNTIL,
      pairedWith: { id: ADA_USER_ID, kind: 'share' },
    };
    const OTHER_GRANT: IAccessGrantRecord = {
      accessRecordId: 'grant-other',
      contactId: 'contact-bo',
      fullName: 'Bo Outside',
      accessLevel: 100000000,
      grantedByName: 'Alice Admin',
      grantedDate: '2026-10-01',
      expiresDate: '2026-12-30',
    };

    function currentAccessFetch(unshare?: () => Response, shareJson: object = ADA_SHARE_JSON): jest.Mock {
      return throwingAuthenticatedFetch(async (url: string) => {
        if (url.includes('/user-shares')) return jsonResponse({ shares: [shareJson] });
        if (url === UNSHARE_USER && unshare) return unshare();
        if (url === REVOKE) return jsonResponse({ deactivatedCount: 1, speContainerOutcome: 'NotAttempted' });
        return jsonResponse({});
      });
    }

    function rowOf(name: string): HTMLElement {
      let el: HTMLElement | null = screen.getByText(name);
      while (el && !el.hasAttribute('data-access-state')) el = el.parentElement;
      if (!el) throw new Error(`No Current Access row for "${name}"`);
      return el;
    }

    const postsTo = (fetchMock: jest.Mock, url: string) =>
      fetchMock.mock.calls.filter(([u, init]) => u === url && (init as RequestInit | undefined)?.method === 'POST');

    it('lists the paired grant right under its share, notes the date on the share row, and says what the grant is', async () => {
      const fetchMock = currentAccessFetch();
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT, OTHER_GRANT]) })}
        />
      );

      await screen.findByText('Ada Lovelace');
      const names = Array.from(document.body.querySelectorAll('[data-access-state]')).map(
        r => r.querySelector('a, span')?.textContent?.trim() ?? ''
      );
      // The paired grant moved from the top of the list to right under Ada's share.
      expect(names.map(n => n.replace(/^\s+/, ''))).toEqual(['Bo Outside', 'Ada Lovelace', 'Ada (contact)']);

      const shareRow = rowOf('Ada Lovelace');
      expect(shareRow).toHaveAttribute('data-paired', 'share');
      expect(
        within(shareRow).getByText(new RegExp(`\\(workforce app access until ${displayDate(UNTIL)}\\)`))
      ).toBeInTheDocument();
      const grantRow = rowOf('Ada (contact)');
      expect(grantRow).toHaveAttribute('data-paired', 'grant');
      expect(
        within(grantRow).getByText(/^Workforce app access that goes with Ada Lovelace's share · granted /)
      ).toBeInTheDocument();
      // The share row offers ONE action, the share's own Revoke; the grant is never revoked from it.
      expect(within(shareRow).getAllByRole('button')).toHaveLength(1);
      expect(rowOf('Bo Outside')).not.toHaveAttribute('data-paired');
    });

    it('removing the share: the confirmation says both go, and only /unshare-user is called', async () => {
      const fetchMock = currentAccessFetch(() =>
        jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrantEnded: true })
      );
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]) })}
        />
      );

      await screen.findByText('Ada Lovelace');
      fireEvent.click(within(rowOf('Ada Lovelace')).getByRole('button', { name: 'Revoke' }));
      const confirm = await screen.findByRole('alertdialog', { hidden: true });
      expect(within(confirm).getByText(REVOKE_PAIRED_SHARE_SENTENCE)).toBeInTheDocument();

      await confirmRevoke();

      expect(
        await screen.findByText("Removed Ada Lovelace's share and their workforce app access.")
      ).toBeInTheDocument();
      expect(postsTo(fetchMock, UNSHARE_USER)).toHaveLength(1);
      expect(postsTo(fetchMock, REVOKE)).toHaveLength(0);
    });

    it('pairedGrantEnded: false (an older answer, no pairedGrant): may still be active, with Try again', async () => {
      const fetchMock = currentAccessFetch(() =>
        jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrantEnded: false })
      );
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]) })}
        />
      );

      await screen.findByText('Ada Lovelace');
      fireEvent.click(within(rowOf('Ada Lovelace')).getByRole('button', { name: 'Revoke' }));
      await confirmRevoke();

      expect(
        await screen.findByText(
          "Removed Ada Lovelace's share. Their workforce app access could not be confirmed removed, so it may still be " +
            'active. Try again.'
        )
      ).toBeInTheDocument();
      expect(screen.getByText('Notice')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled();
    });

    it('pairedGrant endFailed, and the reload lists the grant without its share: a standalone row with Revoke', async () => {
      // The reload after the removal no longer lists Ada's share; her paired grant is still active.
      let shareRemoved = false;
      const fetchMock = throwingAuthenticatedFetch(async (url: string) => {
        if (url.includes('/user-shares')) return jsonResponse({ shares: shareRemoved ? [] : [ADA_SHARE_JSON] });
        if (url === UNSHARE_USER) {
          shareRemoved = true;
          return jsonResponse({
            systemUserId: ADA_USER_ID,
            removed: true,
            pairedGrantEnded: false,
            pairedGrant: 'endFailed',
          });
        }
        return jsonResponse({});
      });
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]) })}
        />
      );

      await screen.findByText('Ada Lovelace');
      fireEvent.click(within(rowOf('Ada Lovelace')).getByRole('button', { name: 'Revoke' }));
      await confirmRevoke();

      expect(
        await screen.findByText(`Removed Ada Lovelace's share. ${pairedGrantNotEndedSentence('endFailed')}`)
      ).toBeInTheDocument();
      // The share row is gone; the grant the notice points to is listed on its own, with its Revoke.
      expect(screen.queryByText('Ada Lovelace')).not.toBeInTheDocument();
      const rows = Array.from(document.body.querySelectorAll('[data-access-state]'));
      expect(rows).toHaveLength(1);
      const grantRow = rowOf('Ada (contact)');
      expect(rows[0]).toBe(grantRow);
      // G3e: no longer paired in the UI — its share is not listed — so it is worded without a share.
      expect(grantRow).not.toHaveAttribute('data-paired');
      expect(within(grantRow).getByText(/^Workforce app access for Ada \(contact\) · granted /)).toBeInTheDocument();
      expect(within(grantRow).queryByText(/share/i)).not.toBeInTheDocument();
      expect(within(grantRow).getByRole('button', { name: 'Revoke' })).toBeEnabled();
    });

    it('a paired grant whose share is not listed: the row, the confirmation and the notice never mention a share', async () => {
      // `/user-shares` lists no share, so the grant's `pairedWith` names a share that is not there.
      const fetchMock = throwingAuthenticatedFetch(async (url: string) => {
        if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
        if (url === REVOKE) return jsonResponse({ deactivatedCount: 1, speContainerOutcome: 'NotAttempted' });
        return jsonResponse({});
      });
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]) })}
        />
      );

      await screen.findByText('Ada (contact)');
      const grantRow = rowOf('Ada (contact)');
      expect(grantRow).not.toHaveAttribute('data-paired');
      expect(
        within(grantRow).getByText(`Workforce app access for Ada (contact) · granted ${displayDate('2026-10-28')}`)
      ).toBeInTheDocument();
      expect(within(grantRow).queryByText(/share/i)).not.toBeInTheDocument();

      fireEvent.click(within(grantRow).getByRole('button', { name: 'Revoke' }));
      const confirm = await screen.findByRole('alertdialog', { hidden: true });
      expect(
        within(confirm).getByText(
          "Remove Ada (contact)'s workforce app access? The record will no longer show in their workforce app."
        )
      ).toBeInTheDocument();
      expect(confirm.textContent ?? '').not.toMatch(/share/i);

      await confirmRevoke();

      const notice = await screen.findByText("Removed Ada (contact)'s workforce app access.");
      expect(notice).toBeInTheDocument();
      expect(screen.queryByText(/Their share stays/)).not.toBeInTheDocument();
      expect(postsTo(fetchMock, REVOKE)).toHaveLength(1);
      expect(postsTo(fetchMock, UNSHARE_USER)).toHaveLength(0);
    });

    it('pairedGrant endFailed on a share removed while related records are pending: the note follows the detail', async () => {
      const fetchMock = currentAccessFetch(() =>
        problemResponse(
          {
            title: 'Removed',
            detail: '1 related record is not updated yet; it completes automatically.',
            reasonCode: 'sdap.access.user_share.children_incomplete',
            pairedGrantEnded: false,
            pairedGrant: 'endFailed',
          },
          500
        )
      );
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]) })}
        />
      );

      await screen.findByText('Ada Lovelace');
      fireEvent.click(within(rowOf('Ada Lovelace')).getByRole('button', { name: 'Revoke' }));
      await confirmRevoke();

      expect(
        await screen.findByText(
          `1 related record is not updated yet; it completes automatically. ${pairedGrantNotEndedSentence('endFailed')}`
        )
      ).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled();
    });

    it('removing the paired grant alone: the confirmation and the notice say the share stays', async () => {
      const fetchMock = currentAccessFetch();
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]) })}
        />
      );

      await screen.findByText('Ada (contact)');
      fireEvent.click(within(rowOf('Ada (contact)')).getByRole('button', { name: 'Revoke' }));
      const confirm = await screen.findByRole('alertdialog', { hidden: true });
      expect(within(confirm).getByText(revokePairedGrantSentence('Ada (contact)'))).toBeInTheDocument();
      expect(within(confirm).queryByText(/immediately lose access/)).not.toBeInTheDocument();
      // G3e: the share is listed, so the standalone wording is not used.
      expect(within(confirm).queryByText(revokeUnlistedShareGrantSentence('Ada (contact)'))).not.toBeInTheDocument();

      await confirmRevoke();

      expect(
        await screen.findByText("Removed Ada (contact)'s workforce app access. Their share stays.")
      ).toBeInTheDocument();
      expect(postsTo(fetchMock, REVOKE)).toHaveLength(1);
      expect(postsTo(fetchMock, UNSHARE_USER)).toHaveLength(0);
    });

    it('MUST NOT FIRE: an unpaired share and grant keep the plain confirmations and notices', async () => {
      const unpairedShare = { ...ADA_SHARE_JSON, pairedWith: undefined };
      const fetchMock = currentAccessFetch(
        () => jsonResponse({ systemUserId: ADA_USER_ID, removed: true }),
        unpairedShare
      );
      renderWithTheme(
        <AccessGrantModal
          {...makeProps(fetchMock, null, { fetchExistingGrants: jest.fn(async () => [OTHER_GRANT]) })}
        />
      );

      await screen.findByText('Ada Lovelace');
      // The modal renders in a portal, so the body is searched (never the render container).
      expect(document.body.querySelectorAll('[data-access-state]').length).toBeGreaterThan(0);
      expect(document.body.querySelector('[data-paired]')).toBeNull();
      expect(screen.queryByText(/workforce app/)).not.toBeInTheDocument();

      fireEvent.click(within(rowOf('Ada Lovelace')).getByRole('button', { name: 'Revoke' }));
      const confirm = await screen.findByRole('alertdialog', { hidden: true });
      expect(within(confirm).queryByText(REVOKE_PAIRED_SHARE_SENTENCE)).not.toBeInTheDocument();
      expect(within(confirm).getByText(/immediately lose access/)).toBeInTheDocument();

      await confirmRevoke();

      expect(await screen.findByText("Removed Ada Lovelace's share.")).toBeInTheDocument();
      expect(screen.queryByText(/could not be removed just now/)).not.toBeInTheDocument();
    });

    it('groupPairedAccess, pure: either side names the pair; an unlisted share leaves the grant in place', () => {
      const share: IAccessGrantRecord = {
        contactId: ADA_USER_ID.toUpperCase(),
        fullName: 'Ada Lovelace',
        accessLevel: 100000001,
        provenance: 'share',
      };
      const grantSideOnly: IAccessGrantRecord = { ...PAIRED_GRANT };
      const shareSideOnly: IAccessGrantRecord = { ...PAIRED_GRANT, pairedWith: undefined };
      const orphan: IAccessGrantRecord = {
        ...PAIRED_GRANT,
        accessRecordId: 'g-orphan',
        pairedWith: { id: 'su-gone', kind: 'share' },
      };

      // Grant-side pairing only (the share row's own pairedWith absent); the share id compared case-insensitively.
      let entries = groupPairedAccess([grantSideOnly, OTHER_GRANT, share]);
      expect(entries.map(e => e.row.fullName)).toEqual(['Bo Outside', 'Ada Lovelace', 'Ada (contact)']);
      expect(entries[1].pairedGrants).toEqual([grantSideOnly]);
      expect(entries[2].pairedShare).toBe(share);

      // Share-side pairing only.
      entries = groupPairedAccess([
        shareSideOnly,
        { ...share, pairedWith: { id: PAIRED_GRANT_ID.toUpperCase(), kind: 'grant' } },
      ]);
      expect(entries.map(e => e.row.fullName)).toEqual(['Ada Lovelace', 'Ada (contact)']);

      // A grant whose share is not listed stays where it is, marked as going with a share.
      entries = groupPairedAccess([orphan, OTHER_GRANT]);
      expect(entries.map(e => e.row.fullName)).toEqual(['Ada (contact)', 'Bo Outside']);
      expect(entries[0].pairedShare).toBeNull();
      expect(entries[1].pairedShare).toBeUndefined();
    });

    it('the notes and notices, pure', () => {
      expect(pairedShareNote([PAIRED_GRANT])).toBe(`(workforce app access until ${displayDate(UNTIL)})`);
      expect(pairedShareNote([{ ...PAIRED_GRANT, isExpired: true }])).toBe(
        `(workforce app access expired ${displayDate(UNTIL)})`
      );
      // G4b: a grant the server pairs but Current Access does not list is not described; an undated one gives nothing.
      expect(pairedShareNote([])).toBe('(paired workforce app access not listed)');
      expect(pairedShareNote([{ ...PAIRED_GRANT, expiresDate: undefined }])).toBe(
        '(workforce app access has no date yet, so it gives no access)'
      );
      expect(unlistedShareGrantMeta('Ada (contact)', '2026-10-28')).toBe(
        `Workforce app access for Ada (contact) · granted ${displayDate('2026-10-28')}`
      );
      expect(revokeUnlistedShareGrantSentence('Ada (contact)')).toBe(
        "Remove Ada (contact)'s workforce app access? The record will no longer show in their workforce app."
      );
      const ada = { systemUserId: ADA_USER_ID, fullName: 'Ada' };
      expect(unshareNotice(ada, 'ended')).toEqual({
        intent: 'success',
        text: "Removed Ada's share and their workforce app access.",
      });
      expect(unshareNotice(ada, 'notConfirmed')).toEqual({
        intent: 'warning',
        text: "Removed Ada's share. Their workforce app access could not be confirmed removed, so it may still be active. Try again.",
        retryUnshare: ada,
      });
      expect(unshareNotice(ada, 'none')).toEqual({ intent: 'success', text: "Removed Ada's share." });
    });

    it('no notice promises when workforce-app access goes: "within a day" is nowhere in the modal', () => {
      // The daily clean-up ends a paired grant only when its writes are on (off by default), so no timing is promised.
      const src = fs.readFileSync(path.join(__dirname, '../AccessGrantModal.tsx'), 'utf8');
      expect(src).not.toMatch(/within a day/i);
    });
  });
});
