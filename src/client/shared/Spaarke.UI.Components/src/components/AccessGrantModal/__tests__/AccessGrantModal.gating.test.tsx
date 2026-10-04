/**
 * AccessGrantModal — Access-Permission sharing gate tests (FR-14 Option A; made
 * real by unified-access-control-r2 task 138, owner round 2 item 3 + O1 FINAL).
 *
 * The owner's model, which the server enforces at read AND write time:
 * - Restricted: no contact-based access. "+ Contact", "+ Organization" and the
 *   role-based candidate (contact) rows are NOT rendered. "+ User" (an internal
 *   POA share), user rows, their level dropdowns and Add stay ENABLED — the old
 *   "Restricted disables + User" contradiction is fixed. Revoke stays available.
 * - Limited (also what a Secure record maps to): "+ Organization" is not
 *   rendered; "+ Contact", candidates and "+ User" stay enabled.
 * - Standard: everything.
 * Each non-standard state shows ONE explanatory MessageBar, and a server refusal
 * (422) surfaces its own `detail` text.
 *
 * Also asserts the `sprk_accesslevel` independence criterion: the per-grant
 * access level (chosen per row) is unaffected by `accessPermissionState`.
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal, describeAccessPermission } from '../AccessGrantModal';
import { resolveAccessPermissionState } from '../accessPermissionState';
import type {
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
  IUserPick,
  AccessPermissionState,
} from '../types';

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
  accessLevel: 100000001, // Collaborate — distinct from ViewOnly, to prove independence.
  grantedByName: 'Alice Admin',
  grantedDate: '2026-07-01T00:00:00Z',
  provenance: 'named',
};

const PICKED_USER: IUserPick = { id: 'user-7', name: 'Colleague Seven' };

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response;
}

function makeProps(overrides?: Partial<IAccessGrantModalProps>): IAccessGrantModalProps {
  const fetchCandidates = jest.fn(async () => [CANDIDATE]);
  const fetchExistingGrants = jest.fn(async () => [EXISTING_GRANT]);
  const searchContacts = jest.fn(async (): Promise<IContactSearchResult[]> => []);
  const isInternalContact = jest.fn(async () => false);
  const pickContact = jest.fn(
    async (): Promise<IContactSearchResult | null> => ({
      contactId: 'contact-picked',
      fullName: 'Picked Person',
      email: 'picked@outsidefirm.com',
    })
  );
  const pickOrganization = jest.fn(async (): Promise<IOrganizationPick | null> => ({ id: 'org-9', name: 'Acme LLP' }));
  const pickUser = jest.fn(async (): Promise<IUserPick | null> => PICKED_USER);
  const authenticatedFetch = jest.fn(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/share-user')) return jsonResponse({ systemUserId: PICKED_USER.id, narrowed: false });
    if (url.includes('/invite-and-grant')) {
      return jsonResponse({ accessRecordId: 'new-1', onboardStatus: 'Provisioned', portalUrl: 'https://portal' });
    }
    if (url.includes('/grant')) {
      return jsonResponse({ accessRecordId: 'new-2', speContainerMembershipGranted: false });
    }
    return jsonResponse({});
  });

  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'project-1',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates,
    fetchExistingGrants,
    searchContacts,
    isInternalContact,
    // Stated EXPLICITLY since task 118 inverted the default to `false` (fail closed). This file is about
    // the Access-Permission sharing gate, which is a DIFFERENT gate from the delegation one — so it must
    // say the delegation answer was "yes" in order to test the other gate at all.
    canGrantAccess: true,
    pickContact,
    pickOrganization,
    pickUser,
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

function fetchUrls(props: IAccessGrantModalProps): string[] {
  return (props.authenticatedFetch as jest.Mock).mock.calls.map((c: [string]) => c[0]);
}

describe('AccessGrantModal — Access-Permission sharing gate (task 138)', () => {
  describe('Restricted: no contact-based access, internal sharing unaffected', () => {
    it('does NOT render "+ Contact", "+ Organization" or the candidate contact rows', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Prior Grantee');

      expect(screen.queryByRole('button', { name: 'Add contact' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Add organization' })).not.toBeInTheDocument();
      expect(screen.queryByText('Gene Gatekeeper')).not.toBeInTheDocument();
      expect(screen.queryByRole('checkbox', { name: 'Select Gene Gatekeeper' })).not.toBeInTheDocument();
    });

    it('keeps "+ User" ENABLED, and a staged user row\'s level dropdown + Add issue POST /share-user', async () => {
      const props = makeProps({ accessPermissionState: 'restricted' });
      renderWithTheme(<AccessGrantModal {...props} />);
      await screen.findByText('Prior Grantee');

      const addUser = screen.getByRole('button', { name: 'Add user' });
      expect(addUser).not.toBeDisabled();
      fireEvent.click(addUser);

      await screen.findByText('Colleague Seven');
      expect(levelComboFor('Colleague Seven')).not.toBeDisabled();
      await pickLevelFor('Colleague Seven', 'Collaborate');
      expect(addButton()).not.toBeDisabled();
      fireEvent.click(addButton());

      await waitFor(() => expect(fetchUrls(props)).toContain('/api/v1/external-access/share-user'));
      expect(fetchUrls(props)).not.toContain('/api/v1/external-access/grant');
      expect(fetchUrls(props)).not.toContain('/api/v1/external-access/invite-and-grant');
    });

    it('still allows revoking existing access', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Prior Grantee');
      expect(screen.getByRole('button', { name: 'Revoke' })).not.toBeDisabled();
    });

    it('shows ONE "Restricted Access" message bar that agrees with the enabled controls', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);
      await screen.findByText('Prior Grantee');

      expect(screen.getByText('Restricted Access')).toBeInTheDocument();
      expect(screen.getByText(/Only internal users can be given access to this record/)).toBeInTheDocument();
      expect(screen.getByText(/share it with a colleague \(\+ User\)/)).toBeInTheDocument();
      expect(screen.queryByText('Secure – Restricted')).not.toBeInTheDocument();
    });

    it('a SECURE + Restricted record shows "Secure – Restricted" (owner O1 FINAL) — one bar, no duplicate', async () => {
      renderWithTheme(
        <AccessGrantModal {...makeProps({ accessPermissionState: 'restricted', isSecureRecord: true })} />
      );
      await screen.findByText('Prior Grantee');

      expect(screen.getByText('Secure – Restricted')).toBeInTheDocument();
      expect(screen.queryByText('Restricted Access')).not.toBeInTheDocument();
      expect(screen.queryByText('Secure')).not.toBeInTheDocument();
    });
  });

  describe('Limited (and Secure, which maps to Limited)', () => {
    it('does NOT render "+ Organization"; "+ Contact", candidates and "+ User" stay enabled', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'limited' })} />);
      await screen.findByText('Gene Gatekeeper');

      expect(screen.queryByRole('button', { name: 'Add organization' })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Add contact' })).not.toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add user' })).not.toBeDisabled();
      expect(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' })).not.toBeDisabled();
    });

    it('shows the "Limited Access" explanation', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'limited' })} />);
      await screen.findByText('Gene Gatekeeper');

      expect(screen.getByText('Limited Access')).toBeInTheDocument();
      expect(screen.getByText(/only through grants made to them by name/)).toBeInTheDocument();
    });

    it('a SECURE record (passed as limited) shows the "Secure" explanation instead', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'limited', isSecureRecord: true })} />);
      await screen.findByText('Gene Gatekeeper');

      expect(screen.getByText('Secure')).toBeInTheDocument();
      expect(screen.getByText(/This record is secure/)).toBeInTheDocument();
      expect(screen.queryByText('Limited Access')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Add organization' })).not.toBeInTheDocument();
    });

    it('allows granting a candidate (a named, direct contact grant)', async () => {
      const props = makeProps({ accessPermissionState: 'limited' });
      renderWithTheme(<AccessGrantModal {...props} />);
      await screen.findByText('Gene Gatekeeper');

      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' }));
      await pickLevelFor('Gene Gatekeeper', 'View Only');
      fireEvent.click(addButton());

      await waitFor(() => expect(fetchUrls(props)).toContain('/api/v1/external-access/invite-and-grant'));
    });
  });

  describe('Standard', () => {
    it('offers every option and shows no permission banner', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'standard' })} />);
      await screen.findByText('Gene Gatekeeper');

      expect(screen.getByRole('button', { name: 'Add contact' })).not.toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add organization' })).not.toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add user' })).not.toBeDisabled();
      for (const title of ['Restricted Access', 'Limited Access', 'Secure', 'Secure – Restricted']) {
        expect(screen.queryByText(title)).not.toBeInTheDocument();
      }
    });

    it('defaults to Standard when accessPermissionState is omitted', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.getByRole('button', { name: 'Add organization' })).toBeInTheDocument();
      expect(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' })).not.toBeDisabled();
    });

    it('no standing-grant option is offered anywhere (removed in v1.0.24)', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'standard' })} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByRole('checkbox', { name: /standing/i })).not.toBeInTheDocument();
    });
  });

  describe('a server refusal surfaces its own detail', () => {
    it('shows the 422 detail text, not a generic error', async () => {
      const detail =
        "This record's Access Permission is Restricted, so only internal users can be given access. Nothing was granted.";
      const props = makeProps({ accessPermissionState: 'standard' });
      (props.authenticatedFetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
        if (url.includes('/invite-and-grant') || url.includes('/grant')) {
          return jsonResponse({ reasonCode: 'sdap.access.grant.record_restricted', detail }, 422);
        }
        return jsonResponse({});
      });
      renderWithTheme(<AccessGrantModal {...props} />);
      await screen.findByText('Gene Gatekeeper');

      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' }));
      await pickLevelFor('Gene Gatekeeper', 'View Only');
      fireEvent.click(addButton());

      expect(await screen.findByText(new RegExp('Nothing was granted'))).toBeInTheDocument();
      expect(screen.queryByText(/Please try again/)).not.toBeInTheDocument();
    });
  });

  describe('module documentation', () => {
    it('no longer claims a standing-grant option is hidden by Limited', () => {
      const src = fs.readFileSync(path.join(__dirname, '../AccessGrantModal.tsx'), 'utf8');
      expect(src).not.toMatch(/hides the standing-grant option/);
      expect(src).toMatch(/There is NO standing-grant control in this modal/);
    });
  });

  describe('adr-021-dark-mode', () => {
    const states: AccessPermissionState[] = ['restricted', 'limited', 'standard'];

    it.each(states)('renders the %s gating state under webDarkTheme with no console errors', async state => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: state })} />, webDarkTheme);

      await screen.findByText('Prior Grantee');
      expect(screen.getByText('Add Access Permissions')).toBeInTheDocument();
      if (state === 'restricted') {
        expect(screen.getByText('Restricted Access')).toBeInTheDocument();
      }

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });

  describe('sprk_accesslevel independence', () => {
    it('the existing-grant access-level badge is identical across all three states', async () => {
      for (const state of ['restricted', 'limited', 'standard'] as AccessPermissionState[]) {
        const { unmount } = renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: state })} />);
        await screen.findByText('Prior Grantee');
        // EXISTING_GRANT.accessLevel = 100000001 → "Collaborate".
        expect(screen.getByText('Collaborate')).toBeInTheDocument();
        unmount();
      }
    });

    it('a grant written under Limited carries the same chosen accessLevel as one under Standard', async () => {
      const levels: Record<string, number> = {};

      for (const state of ['limited', 'standard'] as AccessPermissionState[]) {
        const props = makeProps({ accessPermissionState: state });
        const { unmount } = renderWithTheme(<AccessGrantModal {...props} />);

        await screen.findByText('Gene Gatekeeper');
        fireEvent.click(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' }));
        await pickLevelFor('Gene Gatekeeper', 'Full Access');
        fireEvent.click(addButton());

        await waitFor(() =>
          expect(props.authenticatedFetch).toHaveBeenCalledWith(
            '/api/v1/external-access/invite-and-grant',
            expect.objectContaining({ method: 'POST' })
          )
        );
        const call = (props.authenticatedFetch as jest.Mock).mock.calls.find(
          (c: [string, RequestInit]) => c[0] === '/api/v1/external-access/invite-and-grant'
        ) as [string, RequestInit];
        levels[state] = (JSON.parse(call[1].body as string) as { accessLevel: number }).accessLevel;
        unmount();
      }

      expect(levels.limited).toBe(100000002);
      expect(levels.standard).toBe(100000002);
      expect(levels.limited).toBe(levels.standard);
    });
  });
});

describe('describeAccessPermission — banner copy (owner O1 FINAL)', () => {
  it.each([
    ['standard', false, null],
    ['standard', true, 'Secure'],
    ['limited', false, 'Limited Access'],
    ['limited', true, 'Secure'],
    ['restricted', false, 'Restricted Access'],
    ['restricted', true, 'Secure – Restricted'],
  ] as [AccessPermissionState, boolean, string | null][])('%s, secure=%s → %s', (state, secure, title) => {
    expect(describeAccessPermission(state, secure)?.title ?? null).toBe(title);
  });
});

/**
 * Criterion 12 — the host's mapping, as a pure function. The TrackingFieldTrio PCF supplies its own root
 * option integers (Limited 100000001 / Restricted 100000002) and its `sprk_issecure` read; the rules are here.
 */
describe('resolveAccessPermissionState — the PCF host mapping (fail closed)', () => {
  const VALUES = { limited: 100000001, restricted: 100000002 };

  it.each([
    // value       isSecure    expected
    [100000002, false, 'restricted'],
    [100000002, true, 'restricted'],
    [100000002, null, 'restricted'],
    [100000001, false, 'limited'],
    [100000001, true, 'limited'],
    [100000000, true, 'limited'],
    [null, true, 'limited'],
    [100000000, null, 'limited'],
    [null, null, 'limited'],
    [100000000, undefined, 'limited'],
    [100000000, false, 'standard'],
    [null, false, 'standard'],
  ] as [number | null, boolean | null | undefined, AccessPermissionState][])(
    'accesspermission %s, issecure %s → %s',
    (value, isSecure, expected) => {
      expect(resolveAccessPermissionState(value, isSecure, VALUES)).toBe(expected);
    }
  );
});
