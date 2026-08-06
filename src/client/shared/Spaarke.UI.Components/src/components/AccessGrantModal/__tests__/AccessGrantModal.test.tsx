/**
 * AccessGrantModal — unit tests (task 041, teams-app-r1).
 *
 * Covers the task's `<ui-tests>`:
 *  - modal-opens-from-person-icon
 *  - approve-writes-grant (external branch → /invite-and-grant, the literal
 *    endpoint named by task 041 step 4; provenance is a display-only concept —
 *    see AccessGrantModal.tsx's module doc comment and the task's final report
 *    for why the built `sprk_externalrecordaccess` table has no provenance
 *    column to write to)
 *  - grant-revocable
 *  - adr-021-dark-mode
 *  - console-error-check
 *
 * Plus coverage for: the internal-workforce-contact branch (routes to /grant,
 * not /invite-and-grant, and surfaces the escalated notify-pending notice);
 * the `canGrantAccess=false` defense-in-depth gate; and the named-contact
 * picker add flow.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IAccessGrantCandidate, IAccessGrantRecord, IContactSearchResult } from '../types';

const renderWithTheme = (ui: React.ReactElement, theme = webLightTheme) =>
  render(<FluentProvider theme={theme}>{ui}</FluentProvider>);

const CANDIDATE_EXTERNAL: IAccessGrantCandidate = {
  contactId: 'contact-ext-1',
  fullName: 'Jane Outside',
  email: 'jane@outsidefirm.com',
  role: 'Assigned Attorney 1',
};

const CANDIDATE_INTERNAL: IAccessGrantCandidate = {
  contactId: 'contact-int-1',
  fullName: 'Ralph Internal',
  email: 'ralph@spaarke.com',
  role: 'Assigned Paralegal 1',
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

function jsonResponse(body: unknown, ok = true): Response {
  return {
    ok,
    status: ok ? 200 : 500,
    json: async () => body,
  } as unknown as Response;
}

function makeProps(overrides?: Partial<IAccessGrantModalProps>): IAccessGrantModalProps {
  const fetchCandidates = jest.fn(async () => [CANDIDATE_EXTERNAL, CANDIDATE_INTERNAL]);
  const fetchExistingGrants = jest.fn(async () => [EXISTING_GRANT]);
  const searchContacts = jest.fn(async (): Promise<IContactSearchResult[]> => []);
  const isInternalContact = jest.fn(async (contactId: string) => contactId === CANDIDATE_INTERNAL.contactId);
  const authenticatedFetch = jest.fn(async (url: string) => {
    if (url.includes('/invite-and-grant')) {
      return jsonResponse({
        contactId: CANDIDATE_EXTERNAL.contactId,
        onboardStatus: 'Provisioned',
        accessRecordId: 'new-1',
        portalUrl: 'https://portal',
      });
    }
    if (url.includes('/grant')) {
      return jsonResponse({ accessRecordId: 'new-2', speContainerMembershipGranted: false });
    }
    if (url.includes('/revoke')) {
      return jsonResponse({ speRevoked: false, webRoleRemoved: false });
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
    ...overrides,
  };
}

describe('AccessGrantModal (task 041)', () => {
  describe('modal-opens-from-person-icon', () => {
    it('renders the candidate list, named-contact picker, and existing-grants list when opened', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);

      expect(await screen.findByText('Jane Outside')).toBeInTheDocument();
      expect(screen.getByText('Ralph Internal')).toBeInTheDocument();
      expect(screen.getByText('Add a named contact')).toBeInTheDocument();
      expect(screen.getByPlaceholderText('Search contacts by name or email…')).toBeInTheDocument();
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
    });

    it('does not render the functional UI when the modal is closed', () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ open: false })} />);
      expect(screen.queryByText('Jane Outside')).not.toBeInTheDocument();
    });
  });

  describe('approve-writes-grant', () => {
    it('approving an external candidate calls /invite-and-grant and the refreshed grant list reflects it', async () => {
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');

      // Select the external candidate and approve.
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Jane Outside' }));

      // After approval, fetchExistingGrants is called again — mock it to now
      // include the newly-approved contact with provenance 'membership-approved'
      // (display-only; the built endpoint has no provenance column — see the
      // module doc comment).
      (props.fetchExistingGrants as jest.Mock).mockResolvedValueOnce([EXISTING_GRANT]).mockResolvedValueOnce([
        EXISTING_GRANT,
        {
          accessRecordId: 'new-1',
          contactId: CANDIDATE_EXTERNAL.contactId,
          fullName: CANDIDATE_EXTERNAL.fullName,
          email: CANDIDATE_EXTERNAL.email,
          accessLevel: 100000000,
          grantedByName: 'Current User',
          grantedDate: new Date().toISOString(),
          provenance: 'membership-approved',
        },
      ]);

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
      const body = JSON.parse(init.body as string);
      expect(body).toMatchObject({ email: CANDIDATE_EXTERNAL.email, projectId: 'project-1' });

      expect(await screen.findByText(/membership-approved/)).toBeInTheDocument();
    });

    it('approving an internal candidate calls /grant (not /invite-and-grant) and surfaces a notify-pending notice', async () => {
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Ralph Internal');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ralph Internal' }));
      fireEvent.click(screen.getByRole('button', { name: /Approve selected/ }));

      await waitFor(() => {
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/grant',
          expect.objectContaining({ method: 'POST' })
        );
      });
      expect(props.authenticatedFetch).not.toHaveBeenCalledWith(
        '/api/v1/external-access/invite-and-grant',
        expect.anything()
      );

      expect(await screen.findByText(/Internal notify \(deep-link\) is not yet available/)).toBeInTheDocument();
    });
  });

  describe('grant-revocable', () => {
    it('revoking an existing grant calls /revoke and removes it from the list after confirm', async () => {
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Prior Grantee');
      fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));

      // Confirm dialog appears (dismiss="alert" -> Fluent renders role="alertdialog",
      // not "dialog" — query by the (now duplicated) "Revoke" button label instead of
      // a role-container lookup; the LAST match is the confirm dialog's primary button).
      expect(await screen.findByText('Revoke access?')).toBeInTheDocument();

      (props.fetchExistingGrants as jest.Mock).mockResolvedValueOnce([]);

      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);

      await waitFor(() => {
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/revoke',
          expect.objectContaining({ method: 'POST' })
        );
      });

      const [, init] = (props.authenticatedFetch as jest.Mock).mock.calls.find(
        (c: [string, RequestInit]) => c[0] === '/api/v1/external-access/revoke'
      ) as [string, RequestInit];
      const body = JSON.parse(init.body as string);
      expect(body).toMatchObject({
        accessRecordId: 'grant-1',
        contactId: 'contact-existing-1',
        projectId: 'project-1',
      });

      await waitFor(() => {
        expect(screen.queryByText('Prior Grantee')).not.toBeInTheDocument();
      });
    });
  });

  describe('canGrantAccess=false — defense in depth', () => {
    it('renders a not-authorized state and never fetches candidate/grant data', () => {
      const props = makeProps({ canGrantAccess: false });
      renderWithTheme(<AccessGrantModal {...props} />);

      expect(screen.getByText(/do not have permission to grant or revoke access/i)).toBeInTheDocument();
      expect(props.fetchCandidates).not.toHaveBeenCalled();
      expect(props.fetchExistingGrants).not.toHaveBeenCalled();
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders all sections correctly under webDarkTheme', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />, webDarkTheme);

      expect(await screen.findByText('Jane Outside')).toBeInTheDocument();
      expect(screen.getByText('Add a named contact')).toBeInTheDocument();
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
      expect(screen.getByText('Current access')).toBeInTheDocument();
    });
  });

  describe('console-error-check', () => {
    it('opens, approves, and revokes with zero console errors/warnings', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Jane Outside' }));
      fireEvent.click(screen.getByRole('button', { name: /Approve selected/ }));
      await waitFor(() => expect(props.authenticatedFetch).toHaveBeenCalled());

      await screen.findByText('Prior Grantee');
      fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
      await screen.findByText('Revoke access?');
      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);
      await waitFor(() =>
        expect(props.authenticatedFetch).toHaveBeenCalledWith('/api/v1/external-access/revoke', expect.anything())
      );

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });

  describe('named-contact picker', () => {
    it('searching and selecting a contact enables Add, and Add grants access', async () => {
      const namedResult: IContactSearchResult = {
        contactId: 'contact-named-1',
        fullName: 'Nora Named',
        email: 'nora@example.com',
      };
      const props = makeProps({
        searchContacts: jest.fn(async () => [namedResult]),
        isInternalContact: jest.fn(async () => false),
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');

      const input = screen.getByPlaceholderText('Search contacts by name or email…');
      fireEvent.focus(input);
      fireEvent.click(input);
      fireEvent.input(input, { target: { value: 'Nora' } });

      const option = await screen.findByRole('option', { name: /Nora Named/ });
      fireEvent.click(option);

      const addButton = screen.getByRole('button', { name: 'Add' });
      expect(addButton).not.toBeDisabled();
      fireEvent.click(addButton);

      await waitFor(() => {
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/invite-and-grant',
          expect.objectContaining({ method: 'POST' })
        );
      });
    });
  });
});
