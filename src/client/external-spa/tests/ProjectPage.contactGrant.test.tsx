/**
 * unified-access-control-r2 task 140 (#1063) — contact-side Grant Access in the external SPA, asserted by RENDERING
 * ProjectPage's Contacts tab (owner C4: Collaborate and Full Access may grant; View Only may not; Q1: at or below the
 * caller's own level). The page, the tab, the dialog and the issued-grants list run for real; only the network seams
 * (bff-client, web-api-client) and the context hook's data source are substituted.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel, ApiError } from '../src/types';

// ── Network seams ────────────────────────────────────────────────────────────
const grantAccessAsContact = vi.fn();
const listContactGrants = vi.fn();
const revokeContactGrant = vi.fn();

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return {
    ...actual,
    grantAccessAsContact: (...args: unknown[]) => grantAccessAsContact(...args),
    listContactGrants: (...args: unknown[]) => listContactGrants(...args),
    revokeContactGrant: (...args: unknown[]) => revokeContactGrant(...args),
  };
});

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    getProjectById: vi.fn().mockResolvedValue({
      sprk_projectid: 'p-140',
      sprk_name: 'Project 140',
      sprk_referencenumber: 'PRJ-140',
      sprk_status: 1,
      sprk_issecure: false,
    }),
    getContacts: vi.fn().mockResolvedValue([]),
    getOrganizations: vi.fn().mockResolvedValue([]),
  };
});

// The caller's level for the project, as /me reports it (the hook's data source — useAccessLevel itself runs for real).
let meLevel = 'Collaborate';
vi.mock('../src/hooks/useExternalContext', () => ({
  useExternalContext: () => ({
    context: {
      contactId: 'c-grantor',
      email: 'grantor@firm-a.example',
      projects: [{ projectId: 'p-140', accessLevel: meLevel }],
    },
    isLoading: false,
    error: null,
  }),
}));

// Tabs this test is not about.
vi.mock('../src/components/DocumentLibrary', () => ({ DocumentLibrary: () => null }));
vi.mock('../src/components/EventsCalendar', () => ({ EventsCalendar: () => null }));
vi.mock('../src/components/SmartTodo', () => ({ SmartTodo: () => null }));

import { ProjectPage } from '../src/pages/ProjectPage';
import { canInvite, grantableLevels } from '../src/hooks/useAccessLevel';

async function openContactsTab(level: string): Promise<void> {
  meLevel = level;
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={['/project/p-140']}>
        <Routes>
          <Route path="/project/:id" element={<ProjectPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>
  );
  fireEvent.click(await screen.findByRole('tab', { name: /Contacts/ }));
}

beforeEach(() => {
  grantAccessAsContact.mockReset();
  listContactGrants.mockReset().mockResolvedValue({ grants: [] });
  revokeContactGrant.mockReset();
});

describe('Contacts tab — who may grant (owner C4)', () => {
  it.each(['Collaborate', 'FullAccess'])('shows Invite User and the issued-grants list for %s', async level => {
    await openContactsTab(level);

    expect(await screen.findByRole('button', { name: /Invite User/ })).toBeTruthy();
    expect(await screen.findByText('Access you granted')).toBeTruthy();
  });

  it('hides Invite User for View Only (and lists nothing)', async () => {
    await openContactsTab('ViewOnly');

    await screen.findByRole('tab', { name: /Contacts/, selected: true });
    expect(screen.queryByRole('button', { name: /Invite User/ })).toBeNull();
    expect(screen.queryByText('Access you granted')).toBeNull();
    expect(listContactGrants).not.toHaveBeenCalled();
  });

  it('capability helpers agree: Collaborate and Full Access, levels at or below the caller', () => {
    expect(canInvite(AccessLevel.ViewOnly)).toBe(false);
    expect(canInvite(AccessLevel.Collaborate)).toBe(true);
    expect(grantableLevels(AccessLevel.Collaborate)).toEqual([AccessLevel.ViewOnly, AccessLevel.Collaborate]);
    expect(grantableLevels(AccessLevel.FullAccess)).toEqual([
      AccessLevel.ViewOnly,
      AccessLevel.Collaborate,
      AccessLevel.FullAccess,
    ]);
    expect(grantableLevels(AccessLevel.ViewOnly)).toEqual([]);
  });
});

describe('Grant dialog (owner Q1, Q2)', () => {
  async function openDialog(level: string): Promise<HTMLElement> {
    await openContactsTab(level);
    fireEvent.click(await screen.findByRole('button', { name: /Invite User/ }));
    // Fluent's modalizer (tabster) toggles aria-hidden while the surface opens in jsdom, so roles inside it are
    // queried with hidden: true; it is still the one element with role="dialog".
    const dialog = await screen.findByRole('dialog', { hidden: true });
    await within(dialog).findByRole('combobox', { hidden: true });
    return dialog;
  }

  it('offers a Collaborate caller only View Only and Collaborate', async () => {
    const dialog = await openDialog('Collaborate');
    const options = within(dialog)
      .getAllByRole('option', { hidden: true })
      .map(o => o.textContent);

    expect(options).toEqual(['View Only', 'Collaborate']);
  });

  it('offers a Full Access caller every level', async () => {
    const dialog = await openDialog('FullAccess');
    const options = within(dialog)
      .getAllByRole('option', { hidden: true })
      .map(o => o.textContent);

    expect(options).toEqual(['View Only', 'Collaborate', 'Full Access']);
  });

  it('posts the colleague by email to the contact-side route, with no organization member', async () => {
    grantAccessAsContact.mockResolvedValue({
      accessRecordId: 'g-1',
      granteeContactId: 'c-colleague',
      grantedAccessLevel: AccessLevel.Collaborate,
      narrowed: false,
      expiryDate: '2027-01-02',
      expiryNarrowed: false,
    });
    const dialog = await openDialog('Collaborate');

    fireEvent.change(await within(dialog).findByRole('textbox', { hidden: true }), {
      target: { value: 'colleague@firm-a.example' },
    });
    fireEvent.change(within(dialog).getByRole('combobox', { hidden: true }), {
      target: { value: String(AccessLevel.Collaborate) },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: /Grant access/, hidden: true }));

    await screen.findByText(/now has/);
    expect(grantAccessAsContact).toHaveBeenCalledWith({
      recordType: 'project',
      recordId: 'p-140',
      granteeEmail: 'colleague@firm-a.example',
      accessLevel: AccessLevel.Collaborate,
    });
    expect(Object.keys(grantAccessAsContact.mock.calls[0][0])).not.toContain('organizationId');
    // The list refreshes after a grant (initial load + one reload).
    await waitFor(() => expect(listContactGrants).toHaveBeenCalledTimes(2));
  });

  it('reports narrowing and a shortened expiry', async () => {
    grantAccessAsContact.mockResolvedValue({
      accessRecordId: 'g-2',
      granteeContactId: 'c-colleague',
      grantedAccessLevel: AccessLevel.Collaborate,
      narrowed: true,
      expiryDate: '2026-11-01',
      expiryNarrowed: true,
    });
    const dialog = await openDialog('Collaborate');

    fireEvent.change(await within(dialog).findByRole('textbox', { hidden: true }), {
      target: { value: 'colleague@firm-a.example' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: /Grant access/, hidden: true }));

    expect(await screen.findByText(/You can give at most your own access/)).toBeTruthy();
    expect(screen.getByText(/when your own access to this project ends/)).toBeTruthy();
  });

  it('shows the server refusal message verbatim', async () => {
    const detail =
      'new.person@firm-a.example is not yet a member of your organization in this system; ask the record’s team to add them. Nothing was granted.';
    grantAccessAsContact.mockRejectedValue(
      new ApiError(
        422,
        JSON.stringify({ status: 422, detail, reasonCode: 'sdap.access.contact_grant.grantee_not_in_organization' })
      )
    );
    const dialog = await openDialog('FullAccess');

    fireEvent.change(await within(dialog).findByRole('textbox', { hidden: true }), {
      target: { value: 'new.person@firm-a.example' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: /Grant access/, hidden: true }));

    expect(await screen.findByText(detail)).toBeTruthy();
  });
});

describe('Issued grants list', () => {
  it('lists the caller’s grants and revokes one by id', async () => {
    listContactGrants.mockResolvedValue({
      grants: [
        {
          accessRecordId: 'g-9',
          contactId: 'c-colleague',
          fullName: 'Casey Colleague',
          email: 'colleague@firm-a.example',
          accessLevel: AccessLevel.ViewOnly,
          expiryDate: '2027-01-02',
        },
      ],
    });
    revokeContactGrant.mockResolvedValue({ accessRecordId: 'g-9', deactivatedCount: 1, accessRemainsFromOthers: true });
    await openContactsTab('Collaborate');

    fireEvent.click(await screen.findByRole('button', { name: /Revoke access for Casey Colleague/ }));

    await waitFor(() => expect(revokeContactGrant).toHaveBeenCalledWith('g-9'));
    expect(await screen.findByText(/They still have access someone else gave them/)).toBeTruthy();
  });

  it('shows a revoke refusal message verbatim', async () => {
    listContactGrants.mockResolvedValue({
      grants: [
        {
          accessRecordId: 'g-8',
          contactId: 'c',
          fullName: 'Casey',
          email: null,
          accessLevel: 100000000,
          expiryDate: null,
        },
      ],
    });
    revokeContactGrant.mockRejectedValue(
      new ApiError(404, JSON.stringify({ detail: 'No access that you granted was found with this id.' }))
    );
    await openContactsTab('FullAccess');

    fireEvent.click(await screen.findByRole('button', { name: /Revoke access for Casey/ }));

    expect(await screen.findByText('No access that you granted was found with this id.')).toBeTruthy();
  });

  it('after a revoke refused because someone else took the grant over, shows the message and re-reads the list', async () => {
    // Session 27 round 42 item 1: an internal user took the row over while the contact was revoking — 409
    // managed_elsewhere. The row is no longer the caller's, so the re-read list no longer shows it.
    listContactGrants
      .mockResolvedValueOnce({
        grants: [
          {
            accessRecordId: 'g-7',
            contactId: 'c-colleague',
            fullName: 'Casey Colleague',
            email: 'colleague@firm-a.example',
            accessLevel: AccessLevel.ViewOnly,
            expiryDate: '2027-01-02',
          },
        ],
      })
      .mockResolvedValue({ grants: [] });
    const managedElsewhere =
      "This person already has access to this record that was granted by someone else; ask them or the record's team " +
      'to change it. Nothing was changed.';
    revokeContactGrant.mockRejectedValue(
      new ApiError(
        409,
        JSON.stringify({ detail: managedElsewhere, reasonCode: 'sdap.access.contact_grant.managed_elsewhere' })
      )
    );
    await openContactsTab('Collaborate');

    fireEvent.click(await screen.findByRole('button', { name: /Revoke access for Casey Colleague/ }));

    expect(await screen.findByText(managedElsewhere)).toBeTruthy();
    await waitFor(() => expect(listContactGrants).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('You have not given anyone access to this project.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Revoke access for Casey Colleague/ })).toBeNull();
  });
});

describe('No call to the internal Manage Access group', () => {
  it('no source file in the external SPA names /api/v1/external-access', () => {
    const root = path.resolve(__dirname, '../src');
    const offenders: string[] = [];
    const walk = (dir: string): void => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          walk(full);
        } else if (
          /\.(ts|tsx)$/.test(entry.name) &&
          fs.readFileSync(full, 'utf8').includes('/api/v1/external-access')
        ) {
          offenders.push(path.relative(root, full));
        }
      }
    };
    walk(root);

    expect(offenders).toEqual([]);
  });
});
