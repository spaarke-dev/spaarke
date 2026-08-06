/**
 * AccessGrantModal — Access-Permission sharing gate tests (task 043,
 * teams-app-r1, spec FR-14 Option A).
 *
 * Covers the task's `<ui-tests>`:
 *  - restricted-blocks-external-grants
 *  - limited-disables-standing
 *  - standard-enables-all
 *  - adr-021-dark-mode
 *
 * Plus the `sprk_accesslevel` independence acceptance criterion: changing
 * `accessPermissionState` must never alter the access-level value a grant is
 * written with, nor the access-level badge shown for an existing grant.
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
  accessLevel: 100000001, // Collaborate — distinct from the default ViewOnly, to prove independence.
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
  const onSetStandingGrant = jest.fn(async () => undefined);
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
    onSetStandingGrant,
    ...overrides,
  };
}

describe('AccessGrantModal — Access-Permission sharing gate (task 043)', () => {
  describe('restricted-blocks-external-grants', () => {
    it('disables the approve-candidate action and shows an explanatory message', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Gene Gatekeeper');

      expect(screen.getByText(/external access is off for this record/i)).toBeInTheDocument();
      expect(screen.getByText(/access permission is set to restricted/i)).toBeInTheDocument();

      expect(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' })).toBeDisabled();
      expect(screen.getByRole('button', { name: /Approve selected/ })).toBeDisabled();
    });

    it('disables the add-named-contact action', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Add a named contact');

      expect(screen.getByPlaceholderText('Search contacts by name or email…')).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
    });

    it('still allows reviewing and revoking existing access', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'restricted' })} />);

      await screen.findByText('Prior Grantee');
      expect(screen.getByRole('button', { name: 'Revoke' })).not.toBeDisabled();
    });

    it('does not render the explanatory banner for limited or standard', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'limited' })} />);
      await screen.findByText('Gene Gatekeeper');
      expect(screen.queryByText(/external access is off for this record/i)).not.toBeInTheDocument();
    });
  });

  describe('limited-disables-standing', () => {
    it('allows approve-candidate and add-named-contact but hides the standing-grant option', async () => {
      const props = makeProps({ accessPermissionState: 'limited' });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');

      // No blocking banner, no disabled candidate checkbox.
      expect(screen.queryByText(/external access is off for this record/i)).not.toBeInTheDocument();
      const candidateCheckbox = screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' });
      expect(candidateCheckbox).not.toBeDisabled();

      // Standing-grant option is unavailable (hidden) in both sections.
      expect(screen.queryByRole('checkbox', { name: 'Standing grant' })).not.toBeInTheDocument();

      // Approving still works (named/approved grants remain available).
      fireEvent.click(candidateCheckbox);
      const approveButton = screen.getByRole('button', { name: /Approve selected/ });
      expect(approveButton).not.toBeDisabled();

      fireEvent.click(approveButton);

      await waitFor(() => {
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/invite-and-grant',
          expect.objectContaining({ method: 'POST' })
        );
      });

      // The standing-grant callback must never fire — the option was never
      // offered, so there is nothing to have set.
      expect(props.onSetStandingGrant).not.toHaveBeenCalled();
    });

    it('add-named-contact search input remains enabled', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: 'limited' })} />);
      await screen.findByText('Add a named contact');
      expect(screen.getByPlaceholderText('Search contacts by name or email…')).not.toBeDisabled();
    });
  });

  describe('standard-enables-all', () => {
    it('offers every grant type, including the standing-grant option, matching the task-041 baseline', async () => {
      const props = makeProps({ accessPermissionState: 'standard' });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Gene Gatekeeper');

      expect(screen.queryByText(/external access is off for this record/i)).not.toBeInTheDocument();

      const candidateCheckbox = screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' });
      expect(candidateCheckbox).not.toBeDisabled();
      fireEvent.click(candidateCheckbox);

      // Standing-grant option IS offered (onSetStandingGrant supplied + standard state).
      const standingCheckboxes = screen.getAllByRole('checkbox', { name: 'Standing grant' });
      expect(standingCheckboxes.length).toBeGreaterThan(0);
      expect(standingCheckboxes[0]).not.toBeDisabled(); // enabled once the candidate is selected.

      expect(screen.getByRole('button', { name: /Approve selected/ })).not.toBeDisabled();
      expect(screen.getByPlaceholderText('Search contacts by name or email…')).not.toBeDisabled();
    });

    it('defaults to standard behavior when accessPermissionState is omitted entirely', async () => {
      // No accessPermissionState in overrides at all — proves the default
      // preserves task 041's unmodified baseline for existing callers.
      renderWithTheme(<AccessGrantModal {...makeProps()} />);
      await screen.findByText('Gene Gatekeeper');

      expect(screen.queryByText(/external access is off for this record/i)).not.toBeInTheDocument();
      expect(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' })).not.toBeDisabled();
      expect(screen.getAllByRole('checkbox', { name: 'Standing grant' }).length).toBeGreaterThan(0);
    });
  });

  describe('adr-021-dark-mode', () => {
    const states: AccessPermissionState[] = ['restricted', 'limited', 'standard'];

    it.each(states)('renders the %s gating state correctly under webDarkTheme with no console errors', async state => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: state })} />, webDarkTheme);

      await screen.findByText('Gene Gatekeeper');
      expect(screen.getByText('Add a named contact')).toBeInTheDocument();
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();

      if (state === 'restricted') {
        expect(screen.getByText(/external access is off for this record/i)).toBeInTheDocument();
      }

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });

  describe('sprk_accesslevel independence', () => {
    it('the existing-grant access-level badge is identical across all three Access-Permission states', async () => {
      for (const state of ['restricted', 'limited', 'standard'] as AccessPermissionState[]) {
        const { unmount } = renderWithTheme(<AccessGrantModal {...makeProps({ accessPermissionState: state })} />);
        await screen.findByText('Prior Grantee');
        // EXISTING_GRANT.accessLevel = 100000001 → DEFAULT_ACCESS_LEVEL_OPTIONS label "Collaborate".
        expect(screen.getByText('Collaborate')).toBeInTheDocument();
        unmount();
      }
    });

    it('a grant written while Limited carries the same accessLevel as one written while Standard', async () => {
      const bodiesByState: Record<string, unknown> = {};

      for (const state of ['limited', 'standard'] as AccessPermissionState[]) {
        const props = makeProps({ accessPermissionState: state, defaultAccessLevel: 100000002 });
        const { unmount } = renderWithTheme(<AccessGrantModal {...props} />);

        await screen.findByText('Gene Gatekeeper');
        fireEvent.click(screen.getByRole('checkbox', { name: 'Select Gene Gatekeeper' }));
        fireEvent.click(screen.getByRole('button', { name: /Approve selected/ }));

        await waitFor(() => {
          expect(props.authenticatedFetch).toHaveBeenCalledWith(
            '/api/v1/external-access/invite-and-grant',
            expect.objectContaining({ method: 'POST' })
          );
        });

        const [, init] = (props.authenticatedFetch as jest.Mock).mock.calls.find(
          (c: [string, RequestInit]) => c[0] === '/api/v1/external-access/invite-and-grant'
        ) as [string, RequestInit];
        bodiesByState[state] = JSON.parse(init.body as string);
        unmount();
      }

      expect((bodiesByState.limited as { accessLevel: number }).accessLevel).toBe(100000002);
      expect((bodiesByState.standard as { accessLevel: number }).accessLevel).toBe(100000002);
      expect((bodiesByState.limited as { accessLevel: number }).accessLevel).toBe(
        (bodiesByState.standard as { accessLevel: number }).accessLevel
      );
    });
  });
});
