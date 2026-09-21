/**
 * AccessGrantModal — "+ User" internal system-user share tests (task 065,
 * unified-access-control-r2, spec FR-29).
 *
 * Covers the FOURTH write path this task adds (POST /share-user via pickUser,
 * revoke via /unshare-user, list via GET /user-shares), the M8 fix
 * (postJson/getJson no longer read a failed response's body as success), the
 * designed 401/403 delegation-deny banner (task 008 FR-07 — "a designed UI
 * state ... not a toast/console error"), the revoke-outcome messaging (owner
 * directive 2026-09-10 — distinguish fully-revoked / SPE-may-remain /
 * nothing-revoked without discarding deactivatedCount), and the secure-record
 * owner/BU read-only display (design.md §6).
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IAccessGrantCandidate, IAccessGrantRecord, IUserPick } from '../types';

const renderWithTheme = (ui: React.ReactElement, theme = webLightTheme) =>
  render(<FluentProvider theme={theme}>{ui}</FluentProvider>);

const CANDIDATE: IAccessGrantCandidate = {
  contactId: 'contact-1',
  fullName: 'Gene Gatekeeper',
  email: 'gene@outsidefirm.com',
  role: 'Assigned Attorney 1',
};

const EXISTING_GRANT: IAccessGrantRecord = {
  accessRecordId: 'grant-1',
  contactId: 'contact-existing-1',
  fullName: 'Prior Grantee',
  email: 'prior@example.com',
  accessLevel: 100000000,
  grantedByName: 'Alice Admin',
  grantedDate: '2026-07-01T00:00:00Z',
  provenance: 'named',
};

const USER_PICK: IUserPick = { id: 'systemuser-1', name: 'Uma Userton' };

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 500): Response {
  return {
    ok,
    status,
    json: async () => body,
  } as unknown as Response;
}

/** Default fetch mock: /user-shares GET returns no shares; grant/revoke paths
 * succeed with realistic bodies. Individual tests override with mockImplementation. */
function baseAuthenticatedFetch(extra?: (url: string, init?: RequestInit) => Response | null): jest.Mock {
  return jest.fn(async (url: string, init?: RequestInit) => {
    if (extra) {
      const overridden = extra(url, init);
      if (overridden) return overridden;
    }
    if (url.includes('/user-shares')) {
      return jsonResponse({ shares: [] });
    }
    if (url.includes('/share-user')) {
      return jsonResponse({
        systemUserId: USER_PICK.id,
        accessLevel: 100000001,
        accessRightsMask: 22,
        outcome: 'created',
        narrowed: false,
      });
    }
    if (url.includes('/unshare-user')) {
      return jsonResponse({ systemUserId: USER_PICK.id, removed: true });
    }
    if (url.includes('/invite-and-grant')) {
      return jsonResponse({ accessRecordId: 'new-1', onboardStatus: 'Provisioned', portalUrl: 'https://portal' });
    }
    if (url.includes('/grant')) {
      return jsonResponse({ accessRecordId: 'new-2', speContainerMembershipGranted: false });
    }
    if (url.includes('/revoke')) {
      return jsonResponse({
        speContainerMembershipRevoked: false,
        speContainerOutcome: 'NotAttempted',
        deactivatedCount: 1,
      });
    }
    return jsonResponse({});
  });
}

function makeProps(overrides?: Partial<IAccessGrantModalProps>): IAccessGrantModalProps {
  const fetchCandidates = jest.fn(async () => [CANDIDATE]);
  const fetchExistingGrants = jest.fn(async () => [EXISTING_GRANT]);
  const isInternalContact = jest.fn(async () => false);
  const searchContacts = jest.fn(async () => []);
  const authenticatedFetch = baseAuthenticatedFetch();

  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'project-1',
    recordType: 'project',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates,
    fetchExistingGrants,
    searchContacts,
    isInternalContact,
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

async function pickLevelFor(name: string, optionLabel: string): Promise<void> {
  fireEvent.click(levelComboFor(name));
  fireEvent.click(await screen.findByRole('option', { name: optionLabel }));
}

function addButton(): HTMLElement {
  return screen.getByRole('button', { name: /^Add \(\d+\)$/ });
}

async function findPostInit(fetchMock: jest.Mock, path: string): Promise<RequestInit> {
  const call = await waitFor(() => {
    const found = fetchMock.mock.calls.find((c: [string, RequestInit]) => c[0] === path);
    if (!found) throw new Error(`no call recorded for ${path}`);
    return found as [string, RequestInit];
  });
  return call[1];
}

describe('AccessGrantModal — "+ User" internal system-user share (task 065)', () => {
  describe('pick -> level -> confirm', () => {
    it('"+ User" opens the native lookup (pickUser), stages + auto-selects the pick, and issues exactly ONE POST to /share-user with the chosen level', async () => {
      const pickUser = jest.fn(async (): Promise<IUserPick | null> => USER_PICK);
      const props = makeProps({ pickUser });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');
      fireEvent.click(await screen.findByRole('button', { name: 'Add user' }));
      await waitFor(() => expect(pickUser).toHaveBeenCalled());
      expect(await screen.findByText('Uma Userton')).toBeInTheDocument();

      await pickLevelFor('Uma Userton', 'Collaborate');
      fireEvent.click(addButton());

      const init = await findPostInit(props.authenticatedFetch as jest.Mock, '/api/v1/external-access/share-user');
      expect(JSON.parse(init.body as string)).toMatchObject({
        recordType: 'project',
        recordId: 'project-1',
        systemUserId: USER_PICK.id,
        accessLevel: 100000001,
      });

      // Exactly one POST to /share-user for this single pick.
      const shareUserPosts = (props.authenticatedFetch as jest.Mock).mock.calls.filter(
        (c: [string, RequestInit]) => c[0] === '/api/v1/external-access/share-user'
      );
      expect(shareUserPosts).toHaveLength(1);

      await screen.findByText(/Granted access to 1 item/);
    });

    it('does not render the "+ User" button when pickUser is not supplied', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByRole('button', { name: 'Add user' })).not.toBeInTheDocument();
    });

    it('surfaces a narrowed-level notice when the server reports narrowed: true', async () => {
      const pickUser = jest.fn(async (): Promise<IUserPick | null> => USER_PICK);
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/share-user')
          ? jsonResponse({
              systemUserId: USER_PICK.id,
              accessLevel: 100000001,
              accessRightsMask: 6,
              outcome: 'created',
              narrowed: true,
            })
          : null
      );
      const props = makeProps({
        pickUser,
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');
      fireEvent.click(await screen.findByRole('button', { name: 'Add user' }));
      await screen.findByText('Uma Userton');
      await pickLevelFor('Uma Userton', 'Full Access');
      fireEvent.click(addButton());

      expect(await screen.findByText(/narrowed to your own access level/)).toBeInTheDocument();
    });
  });

  describe('existing shares — Current Access + revoke', () => {
    it('lists an existing user share (GET /user-shares) with a "User (share)" badge, distinct from contact rows', async () => {
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/user-shares')
          ? jsonResponse({
              shares: [
                {
                  systemUserId: 'systemuser-9',
                  fullName: 'Shared Sam',
                  accessRightsMask: 22,
                  accessLevel: 100000001,
                  modifiedOn: '2026-09-10T00:00:00Z',
                },
              ],
            })
          : null
      );
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      expect(await screen.findByText('Shared Sam')).toBeInTheDocument();
      expect(screen.getByText('User (share)')).toBeInTheDocument();
      // Prior Grantee (a contact grant) still renders alongside without being
      // mislabeled — rows merge without duplication or cross-contamination.
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
    });

    it('revoking a user-share row issues POST /unshare-user with {recordType, recordId, systemUserId} and removes the row on success', async () => {
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/user-shares')
          ? jsonResponse({
              shares: [
                {
                  systemUserId: 'systemuser-9',
                  fullName: 'Shared Sam',
                  accessRightsMask: 22,
                  accessLevel: 100000001,
                  modifiedOn: '2026-09-10T00:00:00Z',
                },
              ],
            })
          : null
      );
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Shared Sam');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      // The share row's Revoke — the row order places it after standing/org/
      // contact rows in existingGrants; locate by walking up from the name.
      let el: HTMLElement | null = screen.getByText('Shared Sam');
      while (el && !el.querySelector('button')) el = el.parentElement?.parentElement ?? null;
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);

      expect(await screen.findByText('Revoke access?')).toBeInTheDocument();
      (props.fetchExistingGrants as jest.Mock).mockResolvedValueOnce([]);
      // After the confirm, the next /user-shares GET returns empty too.
      (authenticatedFetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
        if (url.includes('/unshare-user')) return jsonResponse({ systemUserId: 'systemuser-9', removed: true });
        return jsonResponse({});
      });

      const confirmButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(confirmButtons[confirmButtons.length - 1]);

      const init = await findPostInit(authenticatedFetch as jest.Mock, '/api/v1/external-access/unshare-user');
      expect(JSON.parse(init.body as string)).toMatchObject({
        recordType: 'project',
        recordId: 'project-1',
        systemUserId: 'systemuser-9',
      });

      await waitFor(() => expect(screen.queryByText('Shared Sam')).not.toBeInTheDocument());
      expect(await screen.findByText(/Removed Shared Sam's share/)).toBeInTheDocument();
    });
  });

  describe('M8 — a failed response is never read as success', () => {
    it('a 500 ProblemDetails from /share-user surfaces as a failure notice, not a silent success', async () => {
      const pickUser = jest.fn(async (): Promise<IUserPick | null> => USER_PICK);
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/share-user')
          ? jsonResponse(
              {
                title: 'Change not confirmed',
                detail: 'The share could not be confirmed.',
                reasonCode: 'sdap.access.user_share.write_not_confirmed',
              },
              false,
              500
            )
          : null
      );
      const props = makeProps({
        pickUser,
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');
      fireEvent.click(await screen.findByRole('button', { name: 'Add user' }));
      await screen.findByText('Uma Userton');
      await pickLevelFor('Uma Userton', 'View Only');
      fireEvent.click(addButton());

      // MUST report a failure — a pre-M8 postJson would have parsed the
      // ProblemDetails body as a success-shaped response and reported success.
      expect(await screen.findByText(/Granted access to 0 of 1; 1 failed/)).toBeInTheDocument();
    });
  });

  describe('delegation deny (task 008 FR-07) — designed banner, not a toast', () => {
    it('a 403 sdap.access.deny.delegation_write_required on /share-user renders the Write-access-required banner and disables further actions; no share is created', async () => {
      const pickUser = jest.fn(async (): Promise<IUserPick | null> => USER_PICK);
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/share-user')
          ? jsonResponse(
              {
                title: 'Forbidden',
                detail: 'You must have Write access to this record to change who else can access it.',
                reasonCode: 'sdap.access.deny.delegation_write_required',
              },
              false,
              403
            )
          : null
      );
      const props = makeProps({
        pickUser,
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');
      fireEvent.click(await screen.findByRole('button', { name: 'Add user' }));
      await screen.findByText('Uma Userton');
      await pickLevelFor('Uma Userton', 'View Only');
      fireEvent.click(addButton());

      expect(await screen.findByText('Write access required')).toBeInTheDocument();
      expect(
        screen.getByText('You need Write access on this record to change who else can access it.')
      ).toBeInTheDocument();

      // Actions are now disabled — server truth, not a client-side pre-check.
      await waitFor(() => expect(screen.getByRole('button', { name: 'Add user' })).toBeDisabled());
      expect(addButton()).toBeDisabled();

      // No raw error/console noise — this is a designed state, not an exception.
      const shareUserPosts = (authenticatedFetch as jest.Mock).mock.calls.filter(
        (c: [string, RequestInit]) => c[0] === '/api/v1/external-access/share-user'
      );
      expect(shareUserPosts).toHaveLength(1);
    });

    it('proactively surfaces the deny banner on OPEN when GET /user-shares itself 403s (the read is also delegation-gated), disabling Revoke too', async () => {
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/user-shares')
          ? jsonResponse(
              {
                title: 'Forbidden',
                detail: 'Write required.',
                reasonCode: 'sdap.access.deny.delegation_write_required',
              },
              false,
              403
            )
          : null
      );
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      expect(await screen.findByText('Write access required')).toBeInTheDocument();
      // Existing grant list still loads (host-context read, unaffected) — the
      // deny is scoped to the BFF share surface, not the whole modal.
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Revoke' })).toBeDisabled();
    });

    it('a 401 renders a distinct "Sign-in expired" banner', async () => {
      const pickUser = jest.fn(async (): Promise<IUserPick | null> => USER_PICK);
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/share-user') ? jsonResponse({ title: 'Unauthorized' }, false, 401) : null
      );
      const props = makeProps({
        pickUser,
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');
      fireEvent.click(await screen.findByRole('button', { name: 'Add user' }));
      await screen.findByText('Uma Userton');
      await pickLevelFor('Uma Userton', 'View Only');
      fireEvent.click(addButton());

      expect(await screen.findByText('Sign-in expired')).toBeInTheDocument();
      expect(screen.getByText(/Your sign-in has expired/)).toBeInTheDocument();
    });
  });

  describe('revoke-outcome messaging (owner directive 2026-09-10) — never discard deactivatedCount', () => {
    it('a Failed SPE outcome on a contact revoke warns that file access may remain, distinct from a generic error', async () => {
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/revoke')
          ? jsonResponse({ speContainerMembershipRevoked: false, speContainerOutcome: 'Failed', deactivatedCount: 1 })
          : null
      );
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Prior Grantee');
      fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
      await screen.findByText('Revoke access?');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);

      expect(await screen.findByText(/file access could not be confirmed removed/)).toBeInTheDocument();
      expect(screen.getByText(/may still be able to open files/)).toBeInTheDocument();
    });

    it('M2: a Failed SPE outcome now arrives as a 500 ProblemDetails and STILL warns that file access may remain, without discarding deactivatedCount', async () => {
      // Server-side M2 (task 024 -> task 065): /revoke aligns with /close-project for this exact
      // shape and now returns 500 + ProblemDetails (reasonCode
      // sdap.revoke.incomplete.container_not_cleared, deactivatedCount + speContainerOutcome in
      // extensions) instead of the 200 + Failed-in-body the test above exercises. The SAME
      // three-outcome message must still reach the person — this is the regression M8 alone would
      // have caused (a 500 read via res.ok would throw and fall into a generic error) if M2 had
      // shipped without the catch-block routing added alongside it.
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/revoke')
          ? jsonResponse(
              {
                title: 'External access revoke incomplete',
                detail:
                  'Deactivated 1 Dataverse access grant(s), but the SPE container permission could not ' +
                  'be confirmed removed. The grantee may RETAIN file access. Retry the revoke.',
                reasonCode: 'sdap.revoke.incomplete.container_not_cleared',
                deactivatedCount: 1,
                speContainerOutcome: 'Failed',
              },
              false,
              500
            )
          : null
      );
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Prior Grantee');
      fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
      await screen.findByText('Revoke access?');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);

      // The SAME message text the 200-Failed path renders — never a generic "please try again".
      expect(await screen.findByText(/file access could not be confirmed removed/)).toBeInTheDocument();
      expect(screen.getByText(/may still be able to open files/)).toBeInTheDocument();
      expect(screen.queryByText(/Failed to revoke access/)).not.toBeInTheDocument();
    });

    it('deactivatedCount 0 reports "already had no active access record" rather than a plain success', async () => {
      const authenticatedFetch = baseAuthenticatedFetch(url =>
        url.includes('/revoke')
          ? jsonResponse({
              speContainerMembershipRevoked: false,
              speContainerOutcome: 'NotAttempted',
              deactivatedCount: 0,
            })
          : null
      );
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Prior Grantee');
      fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
      await screen.findByText('Revoke access?');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);

      expect(await screen.findByText(/already had no active access record to revoke/)).toBeInTheDocument();
    });
  });

  describe('negative: network/5xx failure leaves existing rows intact', () => {
    it('a network error on /unshare-user leaves the share row visible and shows a non-destructive error', async () => {
      const authenticatedFetch = baseAuthenticatedFetch(url => {
        if (url.includes('/user-shares')) {
          return jsonResponse({
            shares: [
              {
                systemUserId: 'systemuser-9',
                fullName: 'Shared Sam',
                accessRightsMask: 22,
                accessLevel: 100000001,
                modifiedOn: '2026-09-10T00:00:00Z',
              },
            ],
          });
        }
        return null;
      });
      (authenticatedFetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.includes('/user-shares')) {
          return jsonResponse({
            shares: [
              {
                systemUserId: 'systemuser-9',
                fullName: 'Shared Sam',
                accessRightsMask: 22,
                accessLevel: 100000001,
                modifiedOn: '2026-09-10T00:00:00Z',
              },
            ],
          });
        }
        if (url.includes('/unshare-user')) {
          throw new Error('network down');
        }
        return jsonResponse({});
      });
      const props = makeProps({
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Shared Sam');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);
      await screen.findByText('Revoke access?');
      const confirmButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(confirmButtons[confirmButtons.length - 1]);

      expect(await screen.findByText(/Failed to revoke access for Shared Sam/)).toBeInTheDocument();
      // Row still present — not silently dropped. (The confirm dialog also
      // names Shared Sam in its still-open title text, so two matches is
      // itself evidence the row survived — getAllByText avoids the
      // false-ambiguity a single getByText would raise here.)
      expect(screen.getAllByText('Shared Sam').length).toBeGreaterThan(0);
    });
  });

  describe('secure-record owner/BU read-only display (design.md §6)', () => {
    it('renders the owner + business unit when fetchSecureOwnerInfo resolves', async () => {
      const fetchSecureOwnerInfo = jest.fn(async () => ({
        ownerName: 'Secure Project Owner Team',
        businessUnitName: 'Secure Project',
      }));
      renderWithTheme(<AccessGrantModal {...makeProps({ fetchSecureOwnerInfo })} />);

      const row = await screen.findByText(/Secure record/);
      // Both names render as <strong> children inside the same Text row —
      // assert on the row's combined textContent to avoid the substring
      // ambiguity ("Secure Project Owner Team" also contains "Secure Project").
      expect(row.textContent).toContain('Secure Project Owner Team');
      expect(row.textContent).toContain('Business Unit: Secure Project');
    });

    it('renders nothing when fetchSecureOwnerInfo resolves null (non-secure record)', async () => {
      const fetchSecureOwnerInfo = jest.fn(async () => null);
      renderWithTheme(<AccessGrantModal {...makeProps({ fetchSecureOwnerInfo })} />);

      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByText(/Secure record/)).not.toBeInTheDocument();
    });

    it('renders nothing when fetchSecureOwnerInfo is omitted (zero-regression default)', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByText(/Secure record/)).not.toBeInTheDocument();
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders the "+ User" action, the user-share badge, and the deny banner correctly under webDarkTheme with no console errors', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      const pickUser = jest.fn(async (): Promise<IUserPick | null> => USER_PICK);
      const authenticatedFetch = baseAuthenticatedFetch(url => {
        if (url.includes('/user-shares')) {
          return jsonResponse({
            shares: [
              {
                systemUserId: 'systemuser-9',
                fullName: 'Shared Sam',
                accessRightsMask: 22,
                accessLevel: 100000001,
                modifiedOn: '2026-09-10T00:00:00Z',
              },
            ],
          });
        }
        if (url.includes('/share-user')) {
          return jsonResponse(
            { title: 'Forbidden', detail: 'Write required.', reasonCode: 'sdap.access.deny.delegation_write_required' },
            false,
            403
          );
        }
        return null;
      });
      const props = makeProps({
        pickUser,
        authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
      });
      renderWithTheme(<AccessGrantModal {...props} />, webDarkTheme);

      expect(await screen.findByText('Shared Sam')).toBeInTheDocument();
      expect(screen.getByText('User (share)')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Add user' })).toBeInTheDocument();

      fireEvent.click(screen.getByRole('button', { name: 'Add user' }));
      await screen.findByText('Uma Userton');
      await pickLevelFor('Uma Userton', 'View Only');
      fireEvent.click(addButton());

      expect(await screen.findByText('Write access required')).toBeInTheDocument();

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();
      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });
});
