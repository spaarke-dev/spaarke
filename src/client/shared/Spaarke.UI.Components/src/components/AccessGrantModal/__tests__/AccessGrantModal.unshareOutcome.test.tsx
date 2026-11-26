/**
 * AccessGrantModal — what removing a share says about its paired workforce-app grant, and what the share row says that
 * grant gives (task 193 G4b).
 *
 * `/unshare-user` answers `pairedGrant` (`ended` | `none` | `endFailed` | `unconfirmed` | `fileAccessUnconfirmed`) on the
 * 200 and on its related-records-pending 500; `pairedGrantEnded` is the older field, read only as a fallback. A grant
 * not confirmed ended is never called removed: the notice says the person's workforce app access may still be active,
 * promises no timing, and offers Try again, which repeats `/unshare-user` (the server's way to retry the end).
 *
 * Pass B F2: the share row's note describes the paired grant as it is NOW — blocked by the No Access list, cancelled
 * while the record is Restricted, expired, or until when — never "until {date}" for a grant that gives nothing.
 */

import * as React from 'react';
import { render, screen, fireEvent, within, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  AccessGrantModal,
  pairedGrantNotEndedSentence,
  pairedGrantOutcomeOf,
  pairedShareNote,
  unshareNotice,
  unshareRelatedPendingNotice,
  type PairedGrantOutcome,
} from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import { confirmRevoke } from '../../../__tests__/helpers/confirmDialog';
import type { IAccessGrantModalProps, IAccessGrantRecord } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const UNSHARE_USER = '/api/v1/external-access/unshare-user';
const ADA_USER_ID = 'systemuser-ada';
const ADA_CONTACT_ID = 'contact-ada';
const UNTIL = '2027-01-26';
const PAIRED_GRANT_ID = '11111111-2222-3333-4444-555555555555';

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
  contactId: ADA_CONTACT_ID,
  fullName: 'Ada (contact)',
  accessLevel: 100000001,
  grantedByName: 'Alice Admin',
  grantedDate: '2026-10-28',
  expiresDate: UNTIL,
  pairedWith: { id: ADA_USER_ID, kind: 'share' },
};

interface IWorld {
  unshare?: () => Response;
  noAccessEntries?: unknown[];
}

function worldFetch(world: IWorld): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [ADA_SHARE_JSON] });
    if (url === UNSHARE_USER && world.unshare) return world.unshare();
    if (url.includes('/no-access') && world.noAccessEntries) {
      return jsonResponse({
        recordType: 'sprk_matter',
        recordId: 'matter-1',
        secure: 'doesNotApply',
        noAccess: 'applies',
        entriesState: 'complete',
        entries: world.noAccessEntries,
      });
    }
    return jsonResponse({});
  });
}

function makeProps(fetchMock: jest.Mock, overrides: Partial<IAccessGrantModalProps> = {}): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-1',
    recordType: 'matter',
    authenticatedFetch: fetchMock as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => [PAIRED_GRANT]),
    searchContacts: jest.fn(async () => []),
    pickPerson: jest.fn(async () => null),
    pickOrganization: jest.fn(async () => null),
    canGrantAccess: true,
    ...overrides,
  };
}

function rowOf(name: string): HTMLElement {
  let el: HTMLElement | null = screen.getByText(name);
  while (el && !el.hasAttribute('data-access-state')) el = el.parentElement;
  if (!el) throw new Error(`No Current Access row for "${name}"`);
  return el;
}

const unsharePosts = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.filter(
    ([u, init]) => u === UNSHARE_USER && (init as RequestInit | undefined)?.method === 'POST'
  );

async function removeAdasShare(): Promise<void> {
  await screen.findByText('Ada Lovelace');
  fireEvent.click(within(rowOf('Ada Lovelace')).getByRole('button', { name: 'Revoke' }));
  await confirmRevoke();
}

const ADA = { systemUserId: ADA_USER_ID, fullName: 'Ada Lovelace' };

/** Every not-confirmed outcome, and the sentence the notice uses for it. */
const NOT_ENDED: Array<{ outcome: PairedGrantOutcome; sentence: string }> = [
  {
    outcome: 'endFailed',
    sentence:
      'Their workforce app access could not be removed, so it may still be active; it is still listed under Current ' +
      'Access. Try again.',
  },
  {
    outcome: 'unconfirmed',
    sentence: 'Whether they also had workforce app access could not be checked, so it may still be active. Try again.',
  },
  {
    outcome: 'fileAccessUnconfirmed',
    sentence:
      'Their workforce app access was ended, but removing its file access could not be confirmed, so they may still be ' +
      "able to open this record's files from their workforce app. Try again.",
  },
  {
    outcome: 'notConfirmed',
    sentence: 'Their workforce app access could not be confirmed removed, so it may still be active. Try again.',
  },
];

describe('AccessGrantModal — removing a share: the paired grant outcome (task 193 G4b)', () => {
  describe('reading the answer', () => {
    it('reads pairedGrant first, and pairedGrantEnded only as the fallback', () => {
      for (const v of ['ended', 'none', 'endFailed', 'unconfirmed', 'fileAccessUnconfirmed']) {
        // pairedGrant wins whatever the older field says.
        expect(pairedGrantOutcomeOf(v, true)).toBe(v);
        expect(pairedGrantOutcomeOf(v, false)).toBe(v);
        expect(pairedGrantOutcomeOf(v, undefined)).toBe(v);
      }
      expect(pairedGrantOutcomeOf(undefined, true)).toBe('ended');
      expect(pairedGrantOutcomeOf(undefined, false)).toBe('notConfirmed');
      expect(pairedGrantOutcomeOf(undefined, undefined)).toBe('none');
      // A value this client does not know falls back to the older field, never to "ended".
      expect(pairedGrantOutcomeOf('somethingNew', false)).toBe('notConfirmed');
      expect(pairedGrantOutcomeOf('somethingNew', undefined)).toBe('none');
      expect(pairedGrantOutcomeOf(42, true)).toBe('ended');
    });

    it.each(NOT_ENDED)(
      '$outcome: may still be active, Try again, never "removed", no timing',
      ({ outcome, sentence }) => {
        expect(pairedGrantNotEndedSentence(outcome)).toBe(sentence);
        const notice = unshareNotice(ADA, outcome);
        expect(notice).toEqual({
          intent: 'warning',
          text: `Removed Ada Lovelace's share. ${sentence}`,
          retryUnshare: ADA,
        });
        expect(sentence).toMatch(/may still/);
        // "Removed X's share." is true; nothing claims the workforce app access was removed.
        expect(notice.text).not.toMatch(/and their workforce app access/);
        expect(sentence).not.toMatch(/^Their workforce app access was removed|access was removed\./);
        expect(notice.text).not.toMatch(/within|tomorrow|overnight|daily|hours?\b|minutes?\b|automatically/i);
      }
    );

    it('ended and none: success, no Try again', () => {
      expect(unshareNotice(ADA, 'ended')).toEqual({
        intent: 'success',
        text: "Removed Ada Lovelace's share and their workforce app access.",
      });
      expect(unshareNotice(ADA, 'none')).toEqual({ intent: 'success', text: "Removed Ada Lovelace's share." });
      expect(pairedGrantNotEndedSentence('ended')).toBeNull();
      expect(pairedGrantNotEndedSentence('none')).toBeNull();
    });

    it("a Try again's own answer speaks of the workforce app access only (the share was already gone)", () => {
      expect(unshareNotice(ADA, 'ended', true)).toEqual({
        intent: 'success',
        text: "Removed Ada Lovelace's workforce app access.",
      });
      expect(unshareNotice(ADA, 'none', true)).toEqual({
        intent: 'success',
        text: 'Ada Lovelace has no workforce app access to remove on this record.',
      });
      expect(unshareNotice(ADA, 'endFailed', true)).toEqual({
        intent: 'warning',
        text: pairedGrantNotEndedSentence('endFailed'),
        retryUnshare: ADA,
      });
    });

    it('related records pending: the detail, then the sentence and Try again only when not confirmed ended', () => {
      expect(unshareRelatedPendingNotice(ADA, 'Detail.', 'unconfirmed')).toEqual({
        intent: 'warning',
        text: `Detail. ${pairedGrantNotEndedSentence('unconfirmed')}`,
        retryUnshare: ADA,
      });
      expect(unshareRelatedPendingNotice(ADA, 'Detail.', 'ended')).toEqual({ intent: 'warning', text: 'Detail.' });
      expect(unshareRelatedPendingNotice(ADA, 'Detail.', 'none')).toEqual({ intent: 'warning', text: 'Detail.' });
    });
  });

  describe('in the modal', () => {
    it.each(['endFailed', 'unconfirmed', 'fileAccessUnconfirmed'])(
      'pairedGrant %s on the 200: the notice says so and offers Try again',
      async outcome => {
        const fetchMock = worldFetch({
          // The older field says false; pairedGrant names which case it is.
          unshare: () =>
            jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrantEnded: false, pairedGrant: outcome }),
        });
        renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

        await removeAdasShare();

        expect(
          await screen.findByText(
            `Removed Ada Lovelace's share. ${pairedGrantNotEndedSentence(outcome as PairedGrantOutcome)}`
          )
        ).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled();
      }
    );

    it('pairedGrant wins over pairedGrantEnded: fileAccessUnconfirmed is not reported as the generic case', async () => {
      const fetchMock = worldFetch({
        unshare: () =>
          jsonResponse({
            systemUserId: ADA_USER_ID,
            removed: true,
            pairedGrantEnded: false,
            pairedGrant: 'fileAccessUnconfirmed',
          }),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();

      await screen.findByText(/removing its file access could not be confirmed/);
      expect(screen.queryByText(/could not be confirmed removed/)).not.toBeInTheDocument();
    });

    it('pairedGrant none: "Removed X\'s share." and no Try again (MUST NOT FIRE)', async () => {
      const fetchMock = worldFetch({
        unshare: () => jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrant: 'none' }),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();

      expect(await screen.findByText("Removed Ada Lovelace's share.")).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    });

    it('Try again repeats /unshare-user for the same person, and its "ended" answer clears the warning', async () => {
      const answers = [
        jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrantEnded: false, pairedGrant: 'endFailed' }),
        jsonResponse({ systemUserId: ADA_USER_ID, removed: false, pairedGrantEnded: true, pairedGrant: 'ended' }),
      ];
      const fetchMock = worldFetch({ unshare: () => answers.shift()! });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();
      fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

      expect(await screen.findByText("Removed Ada Lovelace's workforce app access.")).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
      const posts = unsharePosts(fetchMock);
      expect(posts).toHaveLength(2);
      for (const [, init] of posts) {
        expect(JSON.parse(String((init as RequestInit).body))).toEqual({
          recordType: 'matter',
          recordId: 'matter-1',
          systemUserId: ADA_USER_ID,
        });
      }
    });

    it('Try again that still cannot end it keeps the warning and Try again', async () => {
      const answers = [
        jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrant: 'unconfirmed', pairedGrantEnded: false }),
        jsonResponse({ systemUserId: ADA_USER_ID, removed: false, pairedGrant: 'endFailed', pairedGrantEnded: false }),
      ];
      const fetchMock = worldFetch({ unshare: () => answers.shift()! });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();
      fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

      expect(await screen.findByText(pairedGrantNotEndedSentence('endFailed')!)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled();
    });

    it('Try again whose request fails: says nothing is known, and Try again stays', async () => {
      const answers = [
        () =>
          jsonResponse({ systemUserId: ADA_USER_ID, removed: true, pairedGrant: 'endFailed', pairedGrantEnded: false }),
        () => problemResponse({ title: 'Error', detail: 'Boom.' }, 500),
      ];
      const fetchMock = worldFetch({ unshare: () => answers.shift()!() });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();
      fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

      expect(
        await screen.findByText(
          "Whether Ada Lovelace's workforce app access was removed could not be checked just now. Try again."
        )
      ).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled();
    });

    it('related records pending with pairedGrant unconfirmed: the detail, the sentence and Try again', async () => {
      const fetchMock = worldFetch({
        unshare: () =>
          problemResponse(
            {
              title: 'Related records not all updated',
              detail: '1 related record is not updated yet.',
              reasonCode: 'sdap.access.user_share.children_incomplete',
              pairedGrantEnded: false,
              pairedGrant: 'unconfirmed',
            },
            500
          ),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();

      expect(
        await screen.findByText(`1 related record is not updated yet. ${pairedGrantNotEndedSentence('unconfirmed')}`)
      ).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled();
    });

    it('related records pending with pairedGrant ended: the detail alone, no Try again (MUST NOT FIRE)', async () => {
      const fetchMock = worldFetch({
        unshare: () =>
          problemResponse(
            {
              title: 'Related records not all updated',
              detail: '1 related record is not updated yet.',
              reasonCode: 'sdap.access.user_share.children_incomplete',
              pairedGrantEnded: true,
              pairedGrant: 'ended',
            },
            500
          ),
      });
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await removeAdasShare();

      expect(await screen.findByText('1 related record is not updated yet.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    });
  });
});

describe('AccessGrantModal — the share row describes its paired grant as it is now (task 193 G4b, pass B F2)', () => {
  it('pure: blocked, cancelled, expired, live', () => {
    expect(pairedShareNote([PAIRED_GRANT], 'vetoed')).toBe('(no workforce app access: blocked by the No Access list)');
    expect(pairedShareNote([PAIRED_GRANT], 'suppressed')).toBe(
      '(no workforce app access while this record is Restricted)'
    );
    expect(pairedShareNote([{ ...PAIRED_GRANT, isExpired: true }])).toBe(
      `(workforce app access expired ${displayDate(UNTIL)})`
    );
    expect(pairedShareNote([PAIRED_GRANT], null)).toBe(`(workforce app access until ${displayDate(UNTIL)})`);
  });

  it('a Restricted record: the note says the paired grant gives no workforce app access', async () => {
    const fetchMock = worldFetch({});
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, { accessPermissionState: 'restricted' })} />);

    await screen.findByText('Ada Lovelace');
    const shareRow = rowOf('Ada Lovelace');
    expect(
      within(shareRow).getByText(/\(no workforce app access while this record is Restricted\)/)
    ).toBeInTheDocument();
    expect(within(shareRow).queryByText(/workforce app access until/)).not.toBeInTheDocument();
    // The grant row carries the same reason with its own marker.
    expect(rowOf('Ada (contact)')).toHaveAttribute('data-access-state', 'suppressed');
  });

  it("the paired grant's contact on the No Access list: the note says the list blocks it", async () => {
    const fetchMock = worldFetch({
      noAccessEntries: [
        {
          entryId: 'entry-1',
          name: 'Wall',
          subjectKind: 'contact',
          subjectId: ADA_CONTACT_ID,
          subjectName: 'Ada (wall entry)',
          objectKind: 'record',
          objectOrganizationId: null,
          objectOrganizationName: null,
          coveredRecordType: 'sprk_matter',
          coveredRecordId: 'matter-1',
          viaSecureParent: false,
          alsoViaSecureParent: false,
          malformed: false,
          inForce: true,
          notInForceReason: null,
          modifiedById: 'u-1',
          modifiedByName: 'Alice Admin',
          modifiedOn: '2026-10-01T00:00:00Z',
        },
      ],
    });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await waitFor(() => expect(rowOf('Ada (contact)')).toHaveAttribute('data-access-state', 'vetoed'));
    const shareRow = rowOf('Ada Lovelace');
    expect(
      within(shareRow).getByText(/\(no workforce app access: blocked by the No Access list\)/)
    ).toBeInTheDocument();
    expect(within(shareRow).queryByText(/workforce app access until/)).not.toBeInTheDocument();
  });

  it('MUST NOT FIRE: a Standard record and no wall keep "until {date}"', async () => {
    const fetchMock = worldFetch({});
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

    await screen.findByText('Ada Lovelace');
    expect(
      within(rowOf('Ada Lovelace')).getByText(new RegExp(`\\(workforce app access until ${displayDate(UNTIL)}\\)`))
    ).toBeInTheDocument();
    expect(screen.queryByText(/no workforce app access/)).not.toBeInTheDocument();
  });
});
