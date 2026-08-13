/**
 * AccessGrantModal — Access-Permission sharing gate tests (FR-14 Option A,
 * rewritten for the v1.0.26 UI, owner UAT 2026-08-12).
 *
 * In v1.0.26 the modal's sharing gate simplified: the standing-grant option was
 * REMOVED (standing is now set on the Contact record), so `limited` and
 * `standard` behave identically in this modal — both allow grants. Only
 * `restricted` gates: it blocks all grant actions (candidate checkbox + the
 * native "+ Contact"/"+ Organization" pickers + per-row level dropdowns + Add)
 * behind a "Restricted Access" banner, while still allowing review + revoke of
 * existing grants.
 *
 * Also asserts the `sprk_accesslevel` independence criterion: the per-grant
 * access level (chosen per row) is unaffected by `accessPermissionState`.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type {
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
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

function jsonResponse(body: unknown, ok = true): Response {
  return {
    ok,
    status: ok ? 200 : 500,
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
  const authenticatedFetch = jest.fn(async (url: string) => {
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
    pickContact,
    pickOrganization,
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

describe('AccessGrantModal — Access-Permission sharing gate (v1.0.26)', () => {
  describe('restricted-blocks-external-grants', () => {
    it('shows the Restricted Access banner and disables all grant actions', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Gene Gatekeeper');

      expect(screen.getByText('Restricted Access')).toBeInTheDocument();
      expect(screen.getByText(/only system users may have access/i)).toBeInTheDocument();

      expect(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add contact' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add organization' })).toBeDisabled();
      // The "Add (N)" grant button is disabled under restricted.
      expect(addButton()).toBeDisabled();
    });

    it('still allows reviewing and revoking existing access', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Prior Grantee');
      expect(screen.getByRole('button', { name: 'Revoke' })).not.toBeDisabled();
    });

    it('does not render the Restricted banner for limited or standard', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'limited' })} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByText('Restricted Access')).not.toBeInTheDocument();
    });
  });

  describe('limited + standard both allow grants', () => {
    it.each(['limited', 'standard'] as AccessPermissionState[])('allows granting a candidate under %s', async state => {
      const props = makeProps({ accessPermissionState: state });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByText('Restricted Access')).not.toBeInTheDocument();

      const checkbox = screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' });
      expect(checkbox).not.toBeDisabled();
      fireEvent.click(checkbox);
      await pickLevelFor('Gene Gatekeeper', 'View Only');
      expect(addButton()).not.toBeDisabled();
      fireEvent.click(addButton());

      await waitFor(() =>
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/invite-and-grant',
          expect.objectContaining({ method: 'POST' })
        )
      );
    });

    it('defaults to grant-enabled behavior when accessPermissionState is omitted', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByText('Restricted Access')).not.toBeInTheDocument();
      expect(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' })).not.toBeDisabled();
    });

    it('no standing-grant option is offered anywhere (removed in v1.0.24)', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'standard' })} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByRole('checkbox', { name: /standing/i })).not.toBeInTheDocument();
    });
  });

  describe('adr-021-dark-mode', () => {
    const states: AccessPermissionState[] = ['restricted', 'limited', 'standard'];

    it.each(states)('renders the %s gating state under webDarkTheme with no console errors', async state => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: state })} />, webDarkTheme);

      await screen.findByText('Gene Gatekeeper');
      expect(screen.getByText('Add Access Permissions')).toBeInTheDocument();
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
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
