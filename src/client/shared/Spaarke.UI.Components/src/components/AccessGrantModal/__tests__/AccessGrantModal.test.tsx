/**
 * AccessGrantModal — unit tests (rewritten for the v1.0.26 UI, owner UAT 2026-08-12).
 *
 * The v1.0.23→v1.0.26 redesign replaced the task-041 "Approve selected" +
 * inline-Combobox + standing-checkbox flow with:
 *  - a single "Add Access Permissions" list (role candidates + looked-up rows),
 *  - a per-row "Pick access level" Dropdown (NO default — a row can't be granted
 *    until a level is chosen),
 *  - an "Add (N)" button (N = checked rows),
 *  - icon-only "+" (aria "Add contact"/"Add organization") that open the host's
 *    NATIVE advanced lookup via pickContact/pickOrganization,
 *  - contact names as links (onOpenContact) opening the Contact record,
 *  - NO standing-grant checkbox (standing is set on the Contact record),
 *  - a "Current Access" list with per-row level badge + Revoke.
 *
 * These tests cover: open/closed, external→/invite-and-grant, internal→/grant +
 * notify-pending, missing-level guard, revoke, canGrantAccess gate, dark mode,
 * polymorphic root, the native pickers (contact + organization), and onOpenContact.
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
} from '../types';

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
      // Real RevokeAccessResponse shape as of task 017. The previous stub said
      // `{ speRevoked, webRoleRemoved }` — neither name ever existed on the DTO, and `webRoleRemoved` is
      // now gone entirely (a Power Pages relic). The modal ignores this body and only checks that the
      // call succeeded, but a stub that mirrors the real contract stops the next reader inferring the
      // wrong one from here.
      return jsonResponse({
        speContainerMembershipRevoked: false,
        speContainerOutcome: 'NotAttempted',
        deactivatedCount: 1,
      });
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

/** Finds the per-row "Pick access level" Dropdown trigger for a named row by
 * walking up from the name text to the row that contains a combobox. Robust to
 * Fluent's hashed class names. */
function levelComboFor(name: string): HTMLElement {
  let el: HTMLElement | null = screen.getByText(name);
  while (el && !el.querySelector('[role="combobox"]')) el = el.parentElement;
  const combo = el?.querySelector('[role="combobox"]') as HTMLElement | null;
  if (!combo) throw new Error(`No access-level dropdown found for row "${name}"`);
  return combo;
}

/** Opens a row's level dropdown and picks the given option label. */
async function pickLevelFor(name: string, optionLabel: string): Promise<void> {
  fireEvent.click(levelComboFor(name));
  fireEvent.click(await screen.findByRole('option', { name: optionLabel }));
}

/** The "Add (N)" grant button. */
function addButton(): HTMLElement {
  return screen.getByRole('button', { name: /^Add \(\d+\)$/ });
}

async function findGrantInit(fetchMock: jest.Mock, path: string): Promise<RequestInit> {
  const call = fetchMock.mock.calls.find((c: [string, RequestInit]) => c[0] === path) as [string, RequestInit];
  return call[1];
}

describe('AccessGrantModal (v1.0.26)', () => {
  describe('open / closed', () => {
    it('renders the "Add Access Permissions" list, candidates, and Current Access when opened', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);

      expect(await screen.findByText('Jane Outside')).toBeInTheDocument();
      expect(screen.getByText('Ralph Internal')).toBeInTheDocument();
      expect(screen.getByText('Add Access Permissions')).toBeInTheDocument();
      expect(screen.getByText('Current Access')).toBeInTheDocument();
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
    });

    it('does not render the functional UI when the modal is closed', () => {
      renderWithTheme(<AccessGrantModal {...makeProps({ open: false })} />);
      expect(screen.queryByText('Jane Outside')).not.toBeInTheDocument();
    });
  });

  describe('grant writes', () => {
    it('granting an external candidate (with a chosen level) calls /invite-and-grant with {recordType, recordId, accessLevel, email}', async () => {
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Jane Outside' }));
      await pickLevelFor('Jane Outside', 'Collaborate');
      fireEvent.click(addButton());

      await waitFor(() => {
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/invite-and-grant',
          expect.objectContaining({ method: 'POST' })
        );
      });
      const init = await findGrantInit(
        props.authenticatedFetch as jest.Mock,
        '/api/v1/external-access/invite-and-grant'
      );
      expect(JSON.parse(init.body as string)).toMatchObject({
        email: CANDIDATE_EXTERNAL.email,
        recordType: 'project',
        recordId: 'project-1',
        accessLevel: 100000001,
      });
    });

    it('granting an internal candidate calls /grant (not /invite-and-grant) and surfaces a notify-pending notice', async () => {
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Ralph Internal');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ralph Internal' }));
      await pickLevelFor('Ralph Internal', 'View Only');
      fireEvent.click(addButton());

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

    it('warns and does not write when a selected row has no access level chosen', async () => {
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Jane Outside' }));
      // No level picked → Add must warn, not write.
      fireEvent.click(addButton());

      expect(await screen.findByText(/Pick an access level for: Jane Outside/)).toBeInTheDocument();
      expect(props.authenticatedFetch).not.toHaveBeenCalled();
    });

    it('sends {recordType, recordId} for a Matter root', async () => {
      const props = makeProps({ recordType: 'matter', recordId: 'matter-9' });
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Jane Outside' }));
      await pickLevelFor('Jane Outside', 'Full Access');
      fireEvent.click(addButton());

      await waitFor(() =>
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/invite-and-grant',
          expect.objectContaining({ method: 'POST' })
        )
      );
      const init = await findGrantInit(
        props.authenticatedFetch as jest.Mock,
        '/api/v1/external-access/invite-and-grant'
      );
      const body = JSON.parse(init.body as string);
      expect(body).toMatchObject({ recordType: 'matter', recordId: 'matter-9', accessLevel: 100000002 });
      expect(body.projectId).toBeUndefined();
    });
  });

  describe('native advanced-lookup pickers', () => {
    it('"+ Contact" opens the native lookup (pickContact), stages + auto-selects the pick, and grants it', async () => {
      const pickContact = jest.fn(
        async (): Promise<IContactSearchResult | null> => ({
          contactId: 'contact-picked-1',
          fullName: 'Picked Person',
          email: 'picked@outsidefirm.com',
        })
      );
      const props = makeProps({
        recordType: 'workassignment',
        recordId: 'wa-3',
        fetchCandidates: jest.fn(async () => []),
        pickContact,
        isInternalContact: jest.fn(async () => false),
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      // No inline search box exists anymore.
      expect(screen.queryByPlaceholderText('Search contacts by name or email…')).not.toBeInTheDocument();

      fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }));
      await waitFor(() => expect(pickContact).toHaveBeenCalled());
      expect(await screen.findByText('Picked Person')).toBeInTheDocument();

      await pickLevelFor('Picked Person', 'View Only');
      fireEvent.click(addButton());

      await waitFor(() =>
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/invite-and-grant',
          expect.objectContaining({ method: 'POST' })
        )
      );
      const init = await findGrantInit(
        props.authenticatedFetch as jest.Mock,
        '/api/v1/external-access/invite-and-grant'
      );
      expect(JSON.parse(init.body as string)).toMatchObject({
        recordType: 'workassignment',
        recordId: 'wa-3',
        email: 'picked@outsidefirm.com',
      });
    });

    it('"+ Organization" opens the native org lookup and grants an organization (organizationId, no contact bind)', async () => {
      const pickOrganization = jest.fn(
        async (): Promise<IOrganizationPick | null> => ({ id: 'org-42', name: 'Acme LLP' })
      );
      const props = makeProps({
        recordType: 'matter',
        recordId: 'matter-7',
        fetchCandidates: jest.fn(async () => []),
        pickOrganization,
      });
      renderWithTheme(<AccessGrantModal {...props} />);

      fireEvent.click(await screen.findByRole('button', { name: 'Add organization' }));
      await waitFor(() => expect(pickOrganization).toHaveBeenCalled());
      expect(await screen.findByText('Acme LLP')).toBeInTheDocument();

      await pickLevelFor('Acme LLP', 'Collaborate');
      fireEvent.click(addButton());

      await waitFor(() =>
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/grant',
          expect.objectContaining({ method: 'POST' })
        )
      );
      const init = await findGrantInit(props.authenticatedFetch as jest.Mock, '/api/v1/external-access/grant');
      const body = JSON.parse(init.body as string);
      expect(body).toMatchObject({
        recordType: 'matter',
        recordId: 'matter-7',
        organizationId: 'org-42',
        accessLevel: 100000001,
      });
      expect(body.contactId).toBeUndefined();
    });

    it('does not render the "+ Contact"/"+ Organization" buttons when the pickers are not supplied', async () => {
      renderWithTheme(<AccessGrantModal {...makeProps()} />);
      await screen.findByText('Jane Outside');
      expect(screen.queryByRole('button', { name: 'Add contact' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Add organization' })).not.toBeInTheDocument();
    });
  });

  describe('onOpenContact', () => {
    it('renders contact names as links that call onOpenContact with the contactId', async () => {
      const onOpenContact = jest.fn();
      renderWithTheme(<AccessGrantModal {...makeProps({ onOpenContact })} />);

      // Current Access row: Prior Grantee → link.
      fireEvent.click(await screen.findByText('Prior Grantee'));
      expect(onOpenContact).toHaveBeenCalledWith('contact-existing-1');
    });
  });

  describe('revoke', () => {
    it('revoking an existing grant calls /revoke and removes it after confirm — with zero console errors/warnings', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Prior Grantee');
      fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));

      expect(await screen.findByText('Revoke access?')).toBeInTheDocument();
      (props.fetchExistingGrants as jest.Mock).mockResolvedValueOnce([]);

      const revokeButtons = screen.getAllByRole('button', { name: 'Revoke' });
      fireEvent.click(revokeButtons[revokeButtons.length - 1]);

      await waitFor(() =>
        expect(props.authenticatedFetch).toHaveBeenCalledWith(
          '/api/v1/external-access/revoke',
          expect.objectContaining({ method: 'POST' })
        )
      );
      const init = await findGrantInit(props.authenticatedFetch as jest.Mock, '/api/v1/external-access/revoke');
      const body = JSON.parse(init.body as string);
      expect(body).toMatchObject({ accessRecordId: 'grant-1', contactId: 'contact-existing-1' });
      expect(body.projectId).toBeUndefined();

      await waitFor(() => expect(screen.queryByText('Prior Grantee')).not.toBeInTheDocument());

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();
      errorSpy.mockRestore();
      warnSpy.mockRestore();
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
      expect(screen.getByText('Add Access Permissions')).toBeInTheDocument();
      expect(screen.getByText('Current Access')).toBeInTheDocument();
      expect(screen.getByText('Prior Grantee')).toBeInTheDocument();
    });
  });

  describe('console-error-check', () => {
    it('opens and grants with zero console errors/warnings', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      const props = makeProps();
      renderWithTheme(<AccessGrantModal {...props} />);

      await screen.findByText('Jane Outside');
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Jane Outside' }));
      await pickLevelFor('Jane Outside', 'View Only');
      fireEvent.click(addButton());
      await screen.findByText(/Granted access to 1 item/);

      // Revoke's console-cleanliness is asserted in the dedicated revoke test —
      // combining grant+revoke here races the grant's trailing loadData() in the
      // full-suite run (flaky), so this check stays scoped to the grant path.
      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });
});
