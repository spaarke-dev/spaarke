/**
 * AccessGrantModal — grant OUTCOMES from /grant (task 139, unified-access-control-r2; owner Q1: "cap every grant at
 * the grantor's own level"). Task 193 (#2338): every contact goes to /grant, which may answer with a share with the
 * colleague the contact represents, or refuse because of who the contact represents:
 *  - the contact is linked to several internal users (409 `sdap.access.grant.contact_represents_several_users`),
 *  - who it represents could not be read (503 `sdap.access.grant.contact_representation_unreadable`),
 *  - the one colleague cannot receive a share (the share core's 422s, e.g. `sdap.access.user_share.user_disabled`).
 * Those are named by the contact, like a `/share-user` refusal is named by the user.
 *
 * The server now caps every manual grant at the caller's own level and says so
 * (`narrowed: true`, additive response field), and refuses with a person-facing
 * ProblemDetails `detail` when:
 *  - the grantee already holds more than the caller can grant (409
 *    `sdap.access.grant.would_lower_existing`),
 *  - the grantee is on the record's No Access list (422 `sdap.access.grant.grantee_denied`),
 *  - the caller's own access allows granting nothing (403 `sdap.access.grant.caller_cannot_grant`),
 *  - whether the grantee is on the No Access list could not be checked (503
 *    `sdap.access.grant.no_access_unverifiable` — task 142, owner round 13 item 4 / round 18).
 * And `/unshare-user` refuses to remove the last person who can open a secure record
 * (409 `sdap.access.user_share.last_reader_on_secure_record`, owner round 3 S5).
 *
 * Each refusal must render the server's own sentence — never the modal's generic
 * "N failed. Please try again." (a policy refusal fails the same way on retry; the
 * 503's own sentence says whether to retry), and never the delegation banner (the
 * caller DOES hold Write; that is a different state).
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { confirmRevoke } from '../../../__tests__/helpers/confirmDialog';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import { apiErrorFor, throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IAccessGrantCandidate } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const CANDIDATE: IAccessGrantCandidate = {
  contactId: 'contact-1',
  fullName: 'Gene Gatekeeper',
  email: 'gene@outsidefirm.com',
  role: 'Assigned Attorney 1',
};

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 500): Response {
  // A BFF ProblemDetails always carries `status`.
  return {
    ok,
    status,
    json: async () => (ok ? body : { status, ...(body as Record<string, unknown>) }),
  } as unknown as Response;
}

function problem(status: number, reasonCode: string, detail: string): Response {
  return jsonResponse({ title: 'Refused', detail, reasonCode }, false, status);
}

/** Answers every read; the grant routes answer whatever `grantResponse` says. */
function fetchWith(grantResponse: (url: string) => Response | null, shares: unknown[] = []): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    const overridden = grantResponse(url);
    if (overridden) return overridden;
    if (url.includes('/user-shares')) return jsonResponse({ shares });
    if (url.includes('/invite-and-grant')) throw new Error('not expected: every contact is granted through /grant');
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

async function grantCandidateAt(optionLabel: string): Promise<void> {
  await screen.findByText(CANDIDATE.fullName);
  fireEvent.click(screen.getByRole('checkbox', { name: `Select ${CANDIDATE.fullName}` }));
  fireEvent.click(levelComboFor(CANDIDATE.fullName));
  fireEvent.click(await screen.findByRole('option', { name: optionLabel }));
  fireEvent.click(screen.getByRole('button', { name: /^Add \(\d+\)$/ }));
}

describe('AccessGrantModal — /grant outcomes (task 139; task 193)', () => {
  /** `/grant`'s own answer, not a `/user-shares` read or anything else. */
  const isGrant = (url: string) => url.endsWith('/api/v1/external-access/grant');

  describe('narrowed to the caller’s own level', () => {
    it('renders the narrowed notice when /grant reports narrowed: true', async () => {
      const fetchMock = fetchWith(url =>
        isGrant(url)
          ? jsonResponse({
              accessRecordId: 'g-2',
              speContainerMembershipGranted: false,
              grantedAccessLevel: 100000001,
              narrowed: true,
            })
          : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText(/narrowed to your own access level/)).toBeInTheDocument();
    });

    it('positive twin: narrowed: false renders a plain success, no narrowed notice', async () => {
      const fetchMock = fetchWith(url =>
        isGrant(url)
          ? jsonResponse({
              accessRecordId: 'g-2',
              speContainerMembershipGranted: false,
              grantedAccessLevel: 100000002,
              narrowed: false,
            })
          : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText('Granted access to 1 item(s).')).toBeInTheDocument();
      expect(screen.queryByText(/narrowed/)).not.toBeInTheDocument();
    });
  });

  describe('refusals render the server’s detail', () => {
    it.each([
      [
        409,
        'sdap.access.grant.would_lower_existing',
        'They already have more access than you can grant (you can grant up to Collaborate). Nothing was changed, so their existing access stays as it is.',
      ],
      [
        422,
        'sdap.access.grant.grantee_denied',
        "This contact or organization cannot be given access to this record: it is on the record's No Access list. Nothing was granted.",
      ],
      [
        403,
        'sdap.access.grant.caller_cannot_grant',
        'You can only give someone the access you have on this record, and your own access to it could not be confirmed. Nothing was granted. Try again; if it persists, ask someone with access to the record.',
      ],
      [
        503,
        'sdap.access.grant.no_access_unverifiable',
        "Whether this contact or organization is on the record's No Access list could not be checked, so nothing was granted. Try again in a moment.",
      ],
    ])('a %i %s from /grant shows its detail, not a generic failure', async (status, reasonCode, detail) => {
      const fetchMock = fetchWith(url => (isGrant(url) ? problem(status, reasonCode, detail) : null));
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(
        await screen.findByText(new RegExp(detail.slice(0, 40).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
      ).toBeInTheDocument();
      expect(screen.queryByText(/failed\. Please try again/)).not.toBeInTheDocument();
      expect(screen.queryByText(/You need Write access to this record/)).not.toBeInTheDocument();
    });
  });

  // Task 193 (#2338): /grant refuses because of who the contact represents. Each is named by the contact, never the
  // generic "N failed. Please try again.", and nothing was written.
  describe('refusals about who the contact represents are named by the contact', () => {
    it.each([
      [
        409,
        'sdap.access.grant.contact_represents_several_users',
        'This contact is linked to more than one internal user, so nothing was given. Add the colleague you mean directly.',
        `Gene Gatekeeper: This contact is linked to more than one internal user, so nothing was given. Add the colleague you mean directly.`,
      ],
      [
        503,
        'sdap.access.grant.contact_representation_unreadable',
        'Whether this contact is one of your colleagues could not be checked, so nothing was given. Try again.',
        `Gene Gatekeeper: Whether this contact is one of your colleagues could not be checked, so nothing was given. Try again.`,
      ],
      [
        422,
        'sdap.access.user_share.user_disabled',
        'This user is disabled, so the record was not shared with them.',
        'Gene Gatekeeper (linked to an internal user): This user is disabled, so the record was not shared with them.',
      ],
      [
        422,
        'sdap.access.user_share.user_not_a_person',
        'This is an application, support or delegated-administration account, not a person, so the record was not shared with it.',
        'Gene Gatekeeper (linked to an internal user): This is an application, support or delegated-administration account, not a person, so the record was not shared with it.',
      ],
      [
        422,
        'sdap.access.user_share.user_not_internal',
        'This record is Restricted to internal users, and this user is flagged as external, so it was not shared with them.',
        'Gene Gatekeeper is linked to a user flagged as external. Restricted records cannot be shared with external users.',
      ],
    ])('a %i %s names the contact', async (status, reasonCode, detail, shown) => {
      const fetchMock = fetchWith(url => (isGrant(url) ? problem(status, reasonCode, detail) : null));
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText(`Granted access to 0 of 1. ${shown}`)).toBeInTheDocument();
      expect(screen.queryByText(/failed\. Please try again/)).not.toBeInTheDocument();
    });

    it('MUST NOT FIRE: an unrelated 500 from /grant is still the generic failure', async () => {
      const fetchMock = fetchWith(url =>
        isGrant(url) ? problem(500, 'sdap.access.contact_grant.grant_failed', 'Something broke.') : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText('Granted access to 0 of 1; 1 failed. Please try again.')).toBeInTheDocument();
      expect(screen.queryByText(/Gene Gatekeeper:/)).not.toBeInTheDocument();
    });
  });

  describe('/unshare-user — the last person on a secure record (owner round 3, S5)', () => {
    it('shows the server’s detail and keeps the share row', async () => {
      const detail =
        'This is the last person who can open this secure record, so their access cannot be removed. Share the record with someone else first, then remove this share.';
      const fetchMock = fetchWith(
        url =>
          url.includes('/unshare-user')
            ? problem(409, 'sdap.access.user_share.last_reader_on_secure_record', detail)
            : null,
        [
          {
            systemUserId: 'systemuser-9',
            fullName: 'Shared Sam',
            accessRightsMask: 262167,
            accessLevel: 100000001,
            modifiedOn: '2026-10-01T00:00:00Z',
          },
        ]
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await screen.findByText('Shared Sam');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);
      expect(await screen.findByText('Revoke access?')).toBeInTheDocument();
      await confirmRevoke();

      expect(await screen.findByText(/last person who can open this secure record/)).toBeInTheDocument();
      await waitFor(() => expect(screen.getByText('Shared Sam')).toBeInTheDocument());
      expect(screen.queryByText(/Failed to revoke access/)).not.toBeInTheDocument();
    });
  });
});
