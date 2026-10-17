/**
 * AccessGrantModal — the toolbar "Expiration" (unified-access-control-r2 task 099c, FR-33, #961; amendments 1-2).
 *
 * One date governs every external share on the record. The picker shows the shares' earliest expiry (and says so when
 * they differ), and applying a date calls `POST /api/v1/external-access/set-record-share-expiry` ONCE with
 * `{ recordType, recordId, expiryDate }` — the date only, never a level (ISS-023). Success re-reads Current Access; a
 * failure is a visible message and the picker goes back to the shares' date. The minimum mirrors the route (UTC today,
 * no maximum). The picker lives inside the Manage Access surface, so a caller the `/can-manage-access` rule refuses never
 * sees it.
 *
 * Every external add (contact, organization) sends the record's APPLIED date as `expiryDate` (owner 2026-09-10, task 097
 * constraint): with no share, the field's value (default today + 90); with shares, their date — never an unapplied
 * edit; a re-add of someone whose live grant ends later moves it to the record date.
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type {
  IAccessGrantCandidate,
  IAccessGrantModalProps,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
} from '../types';

const SET_EXPIRY_PATH = '/api/v1/external-access/set-record-share-expiry';
const RECORD_ID = 'matter-099c';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    // A BFF ProblemDetails always carries `status`.
    json: async () => (status >= 200 && status < 300 ? body : { status, ...(body as Record<string, unknown>) }),
  } as unknown as Response;
}

function share(id: string, name: string, expiresDate: string | undefined, isExpired = false): IAccessGrantRecord {
  return {
    accessRecordId: `grant-${id}`,
    contactId: `contact-${id}`,
    fullName: name,
    accessLevel: 100000001,
    grantedByName: 'Alice Admin',
    grantedDate: '2026-10-01',
    expiresDate,
    isExpired,
    provenance: 'named',
  };
}

/** The `authenticatedFetch` double: `/set-record-share-expiry` answers `expiryAnswer`; every other read answers `{}`. */
function fetchDouble(expiryAnswer: () => Promise<Response>) {
  return jest.fn(async (path: string) => (path === SET_EXPIRY_PATH ? expiryAnswer() : jsonResponse({})));
}

function props(
  fetchExistingGrants: IAccessGrantModalProps['fetchExistingGrants'],
  authenticatedFetch: ReturnType<typeof fetchDouble>,
  overrides: Partial<IAccessGrantModalProps> = {}
): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: RECORD_ID,
    recordType: 'matter',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants,
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => false),
    canGrantAccess: true,
    ...overrides,
  };
}

function renderModal(p: IAccessGrantModalProps) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <AccessGrantModal {...p} />
    </FluentProvider>
  );
}

/** The expiry calls the double received. */
function expiryCalls(fetchMock: ReturnType<typeof fetchDouble>): Array<[string, RequestInit]> {
  return fetchMock.mock.calls.filter(c => c[0] === SET_EXPIRY_PATH) as unknown as Array<[string, RequestInit]>;
}

/** The UTC calendar date `days` from today — the route's "today". */
function utcDate(days = 0): string {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + days))
    .toISOString()
    .slice(0, 10);
}

async function expiryInput(): Promise<HTMLInputElement> {
  return (await screen.findByTestId('share-expiry-input')) as HTMLInputElement;
}

describe('AccessGrantModal — toolbar Expiration (task 099c)', () => {
  it('shows the earliest share expiry and applies a new date with ONE call carrying the date only', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({ updatedCount: 2, expiresDate: '2099-06-30' }));
    const fetchExistingGrants = jest
      .fn()
      .mockResolvedValueOnce([share('a', 'Ann Able', '2099-03-01'), share('b', 'Bo Baker', '2099-03-01')])
      .mockResolvedValue([share('a', 'Ann Able', '2099-06-30'), share('b', 'Bo Baker', '2099-06-30')]);
    renderModal(props(fetchExistingGrants, fetchMock));

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    expect(screen.getByLabelText('Expiration')).toBe(input);
    expect(screen.queryByTestId('share-expiry-divergence')).not.toBeInTheDocument();

    fireEvent.change(input, { target: { value: '2099-06-30' } });
    fireEvent.click(screen.getByTestId('share-expiry-apply'));

    await screen.findByText(/Every external share on this record now ends on/);
    const calls = expiryCalls(fetchMock);
    expect(calls).toHaveLength(1);
    expect(calls[0][1].method).toBe('POST');
    // Exactly these three keys — no accessLevel, no row ids (ISS-023; the server picks the rows).
    expect(JSON.parse(String(calls[0][1].body))).toEqual({
      recordType: 'matter',
      recordId: RECORD_ID,
      expiryDate: '2099-06-30',
    });
  });

  it('refreshes Current Access after a successful change, and the picker then shows the refreshed date', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({ updatedCount: 1, expiresDate: '2099-06-30' }));
    const fetchExistingGrants = jest
      .fn()
      .mockResolvedValueOnce([share('a', 'Ann Able', '2099-03-01')])
      .mockResolvedValue([share('a', 'Ann Able', '2099-06-30')]);
    renderModal(props(fetchExistingGrants, fetchMock));

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    expect(fetchExistingGrants).toHaveBeenCalledTimes(1);

    fireEvent.change(input, { target: { value: '2099-06-30' } });
    fireEvent.keyDown(input, { key: 'Enter' });

    await screen.findByText(/Every external share on this record now ends on/);
    expect(fetchExistingGrants).toHaveBeenCalledTimes(2);
    expect(((await expiryInput()) as HTMLInputElement).value).toBe('2099-06-30');
    expect(screen.queryByTestId('share-expiry-apply')).not.toBeInTheDocument();
  });

  it('negative: a route failure shows the server sentence, reverts the picker and does not claim success', async () => {
    const DETAIL = '2 share(s) on this record are also linked to another record. No expiry was changed.';
    const fetchMock = fetchDouble(async () =>
      jsonResponse({ detail: DETAIL, reasonCode: 'sdap.access.share_expiry.share_spans_records' }, 409)
    );
    const fetchExistingGrants = jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]);
    renderModal(props(fetchExistingGrants, fetchMock));

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    fireEvent.change(input, { target: { value: '2099-06-30' } });
    fireEvent.click(screen.getByTestId('share-expiry-apply'));

    expect(await screen.findByText(DETAIL)).toBeInTheDocument();
    expect(expiryCalls(fetchMock)).toHaveLength(1);
    expect(((await expiryInput()) as HTMLInputElement).value).toBe('2099-03-01');
    expect(screen.queryByText(/now ends on/)).not.toBeInTheDocument();
    // Not an unknown outcome, so the list is not re-read.
    expect(fetchExistingGrants).toHaveBeenCalledTimes(1);
  });

  it('negative: a failure without a server sentence says which date was not applied', async () => {
    const fetchMock = fetchDouble(async () => {
      throw new TypeError('Failed to fetch');
    });
    renderModal(
      props(
        jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]),
        fetchMock
      )
    );

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    fireEvent.change(input, { target: { value: '2099-06-30' } });
    fireEvent.click(screen.getByTestId('share-expiry-apply'));

    expect(await screen.findByText(/The expiration date was not changed to/)).toBeInTheDocument();
    expect(((await expiryInput()) as HTMLInputElement).value).toBe('2099-03-01');
  });

  it('negative: an unconfirmed write re-reads the list so it shows which outcome happened', async () => {
    const DETAIL = 'The expiry could not be confirmed as applied. Reload to see which, then retry if needed.';
    const fetchMock = fetchDouble(async () =>
      jsonResponse({ detail: DETAIL, reasonCode: 'sdap.access.share_expiry.write_failed' }, 500)
    );
    const fetchExistingGrants = jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]);
    renderModal(props(fetchExistingGrants, fetchMock));

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    fireEvent.change(input, { target: { value: '2099-06-30' } });
    fireEvent.click(screen.getByTestId('share-expiry-apply'));

    expect(await screen.findByText(DETAIL)).toBeInTheDocument();
    expect(fetchExistingGrants).toHaveBeenCalledTimes(2);
  });

  it('negative: a date before today (UTC) cannot be applied — it is never sent', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({ updatedCount: 1 }));
    renderModal(
      props(
        jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]),
        fetchMock
      )
    );

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    expect(input.min).toBe(utcDate());

    fireEvent.change(input, { target: { value: '2000-01-01' } });
    expect(screen.getByText('Choose today or a later date.')).toBeInTheDocument();
    const apply = screen.getByTestId('share-expiry-apply');
    expect(apply).toBeDisabled();
    fireEvent.click(apply);
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(expiryCalls(fetchMock)).toHaveLength(0);
  });

  it('states it when the shares end on different dates, and shows the earliest', async () => {
    renderModal(
      props(
        jest.fn(async () => [share('a', 'Ann Able', '2099-05-01'), share('b', 'Bo Baker', '2099-02-01')]),
        fetchDouble(async () => jsonResponse({}))
      )
    );

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-02-01'));
    expect(screen.getByTestId('share-expiry-divergence')).toBeInTheDocument();
  });

  it('shows a lapsed share as expired, and says applying a date renews it', async () => {
    renderModal(
      props(
        jest.fn(async () => [share('a', 'Lee Lapsed', '2026-01-01', true), share('b', 'Liv Live', '2099-01-31')]),
        fetchDouble(async () => jsonResponse({}))
      )
    );

    await screen.findByText('Lee Lapsed');
    expect(screen.getAllByTestId('grant-expired-badge')).toHaveLength(1);
    expect(screen.getByText('Lee Lapsed').closest('[data-access-state]')).toHaveAttribute(
      'data-access-state',
      'expired'
    );
    expect(screen.getByTestId('share-expiry-lapsed')).toBeInTheDocument();
  });

  it('with no share, holds the date the next add sends: today + 90 (UTC), editable, nothing to Apply', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      props(
        jest.fn(async () => []),
        fetchMock
      )
    );

    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe(utcDate(90)));
    expect(input).not.toBeDisabled();
    expect(input.min).toBe(utcDate());
    expect(screen.getByTestId('share-expiry-default')).toBeInTheDocument();

    fireEvent.change(input, { target: { value: '2099-12-31' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(input.value).toBe('2099-12-31');
    expect(screen.queryByTestId('share-expiry-apply')).not.toBeInTheDocument();
    expect(expiryCalls(fetchMock)).toHaveLength(0);
  });

  it('when the grants read fails, shows no date and cannot be changed', async () => {
    renderModal(
      props(
        jest.fn(async () => {
          throw new TypeError('Failed to fetch');
        }),
        fetchDouble(async () => jsonResponse({}))
      )
    );

    await screen.findByTestId('current-access-error');
    const input = await expiryInput();
    expect(input.value).toBe('');
    expect(input).toBeDisabled();
  });

  it('is not shown to a caller who cannot manage access (the /can-manage-access answer)', async () => {
    const fetchExistingGrants = jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]);
    renderModal(
      props(
        fetchExistingGrants,
        fetchDouble(async () => jsonResponse({})),
        { canGrantAccess: false }
      )
    );

    expect(
      await screen.findByText('You do not have permission to grant or revoke access for this record.')
    ).toBeInTheDocument();
    expect(screen.queryByTestId('share-expiry-input')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Expiration')).not.toBeInTheDocument();
  });
});

// ── Adds carry the record's date (task 099c, main-session decision 2026-10-17: option A) ────────────────────────────

const GRANT_PATH = '/api/v1/external-access/grant';
const INVITE_PATH = '/api/v1/external-access/invite-and-grant';

/** The level dropdown in the row containing `start` (AccessGrantModal.test.tsx's helper, by element). */
function levelComboIn(start: HTMLElement): HTMLElement {
  let el: HTMLElement | null = start;
  while (el && !el.querySelector('[role="combobox"]')) el = el.parentElement;
  const combo = el?.querySelector('[role="combobox"]') as HTMLElement | null;
  if (!combo) throw new Error('No access-level dropdown found for the row');
  return combo;
}

/** Picks a level in the "Add Access Permissions" row selected by the given checkbox label. */
async function pickLevelFor(name: string, optionLabel: string): Promise<void> {
  fireEvent.click(levelComboIn(screen.getByRole('checkbox', { name: `Select ${name}` })));
  fireEvent.click(await screen.findByRole('option', { name: optionLabel }));
}

function addButton(): HTMLElement {
  return screen.getByRole('button', { name: /^Add \(\d+\)$/ });
}

/** The parsed bodies the double received for a path. */
function bodiesFor(fetchMock: ReturnType<typeof fetchDouble>, p: string): Array<Record<string, unknown>> {
  return (fetchMock.mock.calls as unknown as Array<[string, RequestInit]>)
    .filter(c => c[0] === p)
    .map(c => JSON.parse(String(c[1].body)) as Record<string, unknown>);
}

const PICKED_CONTACT: IContactSearchResult = { contactId: 'contact-new', fullName: 'Nia New' };
const PICKED_ORG: IOrganizationPick = { id: 'org-new', name: 'Acme LLP' };

/** Adds the looked-up contact (no email: `/grant`) and organization, both at Collaborate, with one Add. */
async function addContactAndOrganization(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }));
  await screen.findByRole('checkbox', { name: 'Select Nia New' });
  fireEvent.click(screen.getByRole('button', { name: 'Add organization' }));
  await screen.findByRole('checkbox', { name: 'Select Acme LLP' });
  await pickLevelFor('Nia New', 'Collaborate');
  await pickLevelFor('Acme LLP', 'Collaborate');
  fireEvent.click(addButton());
}

function addProps(
  fetchExistingGrants: IAccessGrantModalProps['fetchExistingGrants'],
  fetchMock: ReturnType<typeof fetchDouble>,
  overrides: Partial<IAccessGrantModalProps> = {}
): IAccessGrantModalProps {
  return props(fetchExistingGrants, fetchMock, {
    pickContact: jest.fn(async () => PICKED_CONTACT),
    pickOrganization: jest.fn(async () => PICKED_ORG),
    ...overrides,
  });
}

describe('AccessGrantModal — every add sends the record Expiration (task 099c)', () => {
  it('no share: a contact add and an organization add each send today + 90 (UTC), the default', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => []),
        fetchMock
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe(utcDate(90)));

    await addContactAndOrganization();

    await waitFor(() => expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(2));
    const bodies = bodiesFor(fetchMock, GRANT_PATH);
    expect(bodies.find(b => b.contactId === 'contact-new')).toMatchObject({ expiryDate: utcDate(90) });
    expect(bodies.find(b => b.organizationId === 'org-new')).toMatchObject({ expiryDate: utcDate(90) });
    expect(expiryCalls(fetchMock)).toHaveLength(0);
  });

  it('no share: a changed field is what the add sends (invite route included), with no Apply call', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    const candidate: IAccessGrantCandidate = {
      contactId: 'contact-cand',
      fullName: 'Cass Candidate',
      email: 'cass@outsidefirm.com',
      role: 'Assigned Attorney 1',
    };
    renderModal(
      addProps(
        jest.fn(async () => []),
        fetchMock,
        { fetchCandidates: jest.fn(async () => [candidate]) }
      )
    );
    const input = await expiryInput();
    await screen.findByRole('checkbox', { name: 'Select Cass Candidate' });
    fireEvent.change(input, { target: { value: '2099-12-31' } });

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Cass Candidate' }));
    await pickLevelFor('Cass Candidate', 'Collaborate');
    fireEvent.click(addButton());

    await waitFor(() => expect(bodiesFor(fetchMock, INVITE_PATH)).toHaveLength(1));
    expect(bodiesFor(fetchMock, INVITE_PATH)[0]).toMatchObject({
      email: 'cass@outsidefirm.com',
      expiryDate: '2099-12-31',
    });
    expect(expiryCalls(fetchMock)).toHaveLength(0);
  });

  it('negative: no share and a past date — the add is held back with a message, nothing is sent', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => []),
        fetchMock
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe(utcDate(90)));
    fireEvent.change(input, { target: { value: '2000-01-01' } });

    await addContactAndOrganization();

    expect(await screen.findByText('Choose an Expiration of today or later before adding people.')).toBeInTheDocument();
    expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(0);
  });

  it("shares exist: a contact add and an organization add each send the shares' date", async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]),
        fetchMock
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));

    await addContactAndOrganization();

    await waitFor(() => expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(2));
    for (const body of bodiesFor(fetchMock, GRANT_PATH)) expect(body.expiryDate).toBe('2099-03-01');
  });

  it('shares exist: an UNAPPLIED edit is not what an add sends — the applied date is, and the field says so', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => [share('a', 'Ann Able', '2099-03-01')]),
        fetchMock
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    fireEvent.change(input, { target: { value: '2099-09-09' } });
    expect(screen.getByTestId('share-expiry-unapplied')).toBeInTheDocument();

    await addContactAndOrganization();

    await waitFor(() => expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(2));
    for (const body of bodiesFor(fetchMock, GRANT_PATH)) expect(body.expiryDate).toBe('2099-03-01');
    expect(expiryCalls(fetchMock)).toHaveLength(0);
  });

  it('divergent shares: re-adding someone whose grant ends later sends the record (earliest) date', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => [share('a', 'Ann Able', '2099-03-01'), share('b', 'Bo Baker', '2099-08-01')]),
        fetchMock,
        { pickContact: jest.fn(async () => ({ contactId: 'contact-b', fullName: 'Bo Baker' })) }
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));
    expect(screen.getByTestId('share-expiry-divergence')).toHaveTextContent(
      'Apply, or adding a person, sets them to this date.'
    );

    fireEvent.click(screen.getByRole('button', { name: 'Add contact' }));
    await screen.findByRole('checkbox', { name: 'Select Bo Baker' });
    await pickLevelFor('Bo Baker', 'Collaborate');
    fireEvent.click(addButton());

    await waitFor(() => expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(1));
    expect(bodiesFor(fetchMock, GRANT_PATH)[0]).toMatchObject({ contactId: 'contact-b', expiryDate: '2099-03-01' });
  });

  it('a lapsed share is not the record date: an add sends the earliest LIVE date', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => [share('a', 'Lee Lapsed', '2026-01-01', true), share('b', 'Liv Live', '2099-01-31')]),
        fetchMock
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-01-31'));

    await addContactAndOrganization();

    await waitFor(() => expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(2));
    for (const body of bodiesFor(fetchMock, GRANT_PATH)) expect(body.expiryDate).toBe('2099-01-31');
  });

  it('every share lapsed: an add sends no date (the route refuses a past one; the server restores at today + 90)', async () => {
    const fetchMock = fetchDouble(async () => jsonResponse({}));
    renderModal(
      addProps(
        jest.fn(async () => [share('a', 'Lee Lapsed', '2026-01-01', true)]),
        fetchMock
      )
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2026-01-01'));

    await addContactAndOrganization();

    await waitFor(() => expect(bodiesFor(fetchMock, GRANT_PATH)).toHaveLength(2));
    for (const body of bodiesFor(fetchMock, GRANT_PATH)) expect(body).not.toHaveProperty('expiryDate');
  });
});

describe('AccessGrantModal — Expiration theming (task 099c, ADR-021)', () => {
  it('renders with divergent and lapsed shares under webDarkTheme with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    render(
      <FluentProvider theme={webDarkTheme}>
        <AccessGrantModal
          {...props(
            jest.fn(async () => [share('a', 'Lee Lapsed', '2026-01-01', true), share('b', 'Liv Live', '2099-01-31')]),
            fetchDouble(async () => jsonResponse({}))
          )}
        />
      </FluentProvider>
    );
    await screen.findByTestId('share-expiry-divergence');
    expect(screen.getByTestId('share-expiry-lapsed')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
    warnSpy.mockRestore();
  });

  it('styles the Expiration with theme tokens only (no literal colours)', () => {
    const src = fs.readFileSync(path.join(__dirname, '../AccessGrantModal.tsx'), 'utf8');
    const start = src.indexOf('shareExpiryField: {');
    const end = src.indexOf('listArea: {', start);
    expect(start).toBeGreaterThan(-1);
    const block = src.slice(start, end);
    expect(block).toMatch(/tokens\.colorNeutralForeground3/);
    expect(block).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/);
  });
});
