/**
 * AccessGrantModal — grant OUTCOMES from /grant and /invite-and-grant (task 139,
 * unified-access-control-r2; owner Q1: "cap every grant at the grantor's own level").
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
  return { ok, status, json: async () => (ok ? body : { status, ...(body as Record<string, unknown>) }) } as unknown as Response;
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
    if (url.includes('/invite-and-grant')) {
      return jsonResponse({
        contactId: 'contact-1',
        onboardStatus: 'AlreadyProvisioned',
        accessRecordId: 'g-1',
        portalUrl: 'https://portal',
      });
    }
    if (url.includes('/grant')) return jsonResponse({ accessRecordId: 'g-2', speContainerMembershipGranted: false });
    return jsonResponse({});
  });
}

function makeProps(authenticatedFetch: jest.Mock, internal = false): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'project-1',
    recordType: 'project',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => [CANDIDATE]),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => internal),
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

describe('AccessGrantModal — /grant and /invite-and-grant outcomes (task 139)', () => {
  describe('narrowed to the caller’s own level', () => {
    it('renders the narrowed notice when /invite-and-grant reports narrowed: true', async () => {
      const fetchMock = fetchWith(url =>
        url.includes('/invite-and-grant')
          ? jsonResponse({
              contactId: 'contact-1',
              onboardStatus: 'AlreadyProvisioned',
              accessRecordId: 'g-1',
              portalUrl: 'https://portal',
              grantedAccessLevel: 100000001,
              narrowed: true,
            })
          : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText(/narrowed to your own access level/)).toBeInTheDocument();
    });

    it('renders the narrowed notice when /grant (an internal contact) reports narrowed: true', async () => {
      const fetchMock = fetchWith(url =>
        url.includes('/grant') && !url.includes('invite')
          ? jsonResponse({
              accessRecordId: 'g-2',
              speContainerMembershipGranted: false,
              grantedAccessLevel: 100000001,
              narrowed: true,
            })
          : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, true)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText(/narrowed to your own access level/)).toBeInTheDocument();
    });

    it('positive twin: narrowed: false renders a plain success, no narrowed notice', async () => {
      const fetchMock = fetchWith(url =>
        url.includes('/invite-and-grant')
          ? jsonResponse({
              contactId: 'contact-1',
              onboardStatus: 'AlreadyProvisioned',
              accessRecordId: 'g-1',
              portalUrl: 'https://portal',
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
    ])('a %i %s from /invite-and-grant shows its detail, not a generic failure', async (status, reasonCode, detail) => {
      const fetchMock = fetchWith(url =>
        url.includes('/invite-and-grant') ? problem(status, reasonCode, detail) : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock)} />);

      await grantCandidateAt('Full Access');

      expect(
        await screen.findByText(new RegExp(detail.slice(0, 40).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
      ).toBeInTheDocument();
      expect(screen.queryByText(/failed\. Please try again/)).not.toBeInTheDocument();
      expect(screen.queryByText(/You need Write access to this record/)).not.toBeInTheDocument();
    });

    it('a 409 would_lower_existing from /grant (an internal contact) shows its detail', async () => {
      const detail =
        'They already have more access than you can grant. Nothing was changed, so their existing access stays as it is.';
      const fetchMock = fetchWith(url =>
        url.includes('/grant') && !url.includes('invite')
          ? problem(409, 'sdap.access.grant.would_lower_existing', detail)
          : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, true)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText(/already have more access than you can grant/)).toBeInTheDocument();
    });

    it('a 503 no_access_unverifiable from /grant (an internal contact) shows its detail, not the generic failure', async () => {
      const detail =
        "Whether this contact or organization is on the record's No Access list could not be checked, so nothing was granted. Try again in a moment.";
      const fetchMock = fetchWith(url =>
        url.includes('/grant') && !url.includes('invite')
          ? problem(503, 'sdap.access.grant.no_access_unverifiable', detail)
          : null
      );
      renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, true)} />);

      await grantCandidateAt('Full Access');

      expect(await screen.findByText(/No Access list could not be checked/)).toBeInTheDocument();
      expect(screen.queryByText(/failed\. Please try again/)).not.toBeInTheDocument();
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
      const confirmButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(confirmButtons[confirmButtons.length - 1]);

      expect(await screen.findByText(/last person who can open this secure record/)).toBeInTheDocument();
      await waitFor(() => expect(screen.getByText('Shared Sam')).toBeInTheDocument());
      expect(screen.queryByText(/Failed to revoke access/)).not.toBeInTheDocument();
    });
  });
});
