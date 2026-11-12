/**
 * AccessGrantModal — a user picked with "Add person" (task 193 G4b).
 *
 * Pass B F3: a user share carries the record's applied Expiration (`expiryDate`) like every other add, because the share
 * writes a paired workforce-app grant that takes that date. The share itself has no end date; `/share-user` answers
 * `expiryNotApplied: true` whenever a date was sent, and the notice says which part the date reached. A past date is the
 * server's 400 `sdap.access.grant.expiry_in_past`, shown as its sentence; an unusable Expiration in the field holds the
 * add back before anything is sent.
 *
 * K3: two people with the same name are told apart in the list — by their detail line, and by a short ID when that
 * matches too — and each row is selected by its id, so the add goes to the one chosen.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, distinguishSameNames } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IAccessGrantRecord, IPersonPick, IUserPick } from '../types';
import { personFromUser } from './personPickDouble';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const SHARE_USER = '/api/v1/external-access/share-user';
const UMA: IUserPick = { id: 'systemuser-uma', name: 'Uma Userton' };
const UNTIL = '2027-01-26';

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function problemResponse(body: unknown, status: number): Response {
  return {
    ok: false,
    status,
    json: async () => ({ status, ...(body as Record<string, unknown>) }),
  } as unknown as Response;
}

/** The UTC calendar date `days` from today — the route's "today". */
function utcDate(days = 0): string {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + days))
    .toISOString()
    .slice(0, 10);
}

function displayDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function externalShare(expiresDate: string): IAccessGrantRecord {
  return {
    accessRecordId: 'grant-ann',
    contactId: 'contact-ann',
    fullName: 'Ann Able',
    accessLevel: 100000001,
    grantedByName: 'Alice Admin',
    grantedDate: '2026-10-01',
    expiresDate,
    provenance: 'named',
  };
}

function fetchWith(shareUser: () => Response): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url === SHARE_USER) return shareUser();
    return jsonResponse({});
  });
}

const CREATED = { systemUserId: UMA.id, accessLevel: 100000001, outcome: 'created', narrowed: false };

function makeProps(
  fetchMock: jest.Mock,
  picks: Array<IPersonPick | null>,
  overrides: Partial<IAccessGrantModalProps> = {}
): IAccessGrantModalProps {
  const queue = [...picks];
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-1',
    recordType: 'matter',
    authenticatedFetch: fetchMock as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    pickPerson: jest.fn(async () => queue.shift() ?? null),
    pickOrganization: jest.fn(async () => null),
    canGrantAccess: true,
    ...overrides,
  };
}

/** The level dropdown in the "Add Access Permissions" row of the given checkbox. */
function levelComboIn(start: HTMLElement): HTMLElement {
  let el: HTMLElement | null = start;
  while (el && !el.querySelector('[role="combobox"]')) el = el.parentElement;
  const combo = el?.querySelector('[role="combobox"]') as HTMLElement | null;
  if (!combo) throw new Error('No access-level dropdown found for the row');
  return combo;
}

async function pickLevelIn(checkbox: HTMLElement, optionLabel: string): Promise<void> {
  fireEvent.click(levelComboIn(checkbox));
  fireEvent.click(await screen.findByRole('option', { name: optionLabel }));
}

function addButton(): HTMLElement {
  return screen.getByRole('button', { name: /^Add \(\d+\)$/ });
}

const shareUserBodies = (fetchMock: jest.Mock): Array<Record<string, unknown>> =>
  (fetchMock.mock.calls as Array<[string, RequestInit | undefined]>)
    .filter(([u, init]) => u === SHARE_USER && init?.method === 'POST')
    .map(([, init]) => JSON.parse(String(init!.body)) as Record<string, unknown>);

async function addUma(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Add person' }));
  const box = await screen.findByRole('checkbox', { name: /^Select Uma Userton/ });
  await pickLevelIn(box, 'Collaborate');
  fireEvent.click(addButton());
}

async function expiryInput(): Promise<HTMLInputElement> {
  return (await screen.findByTestId('share-expiry-input')) as HTMLInputElement;
}

describe('AccessGrantModal — a user add carries the record Expiration (task 193 G4b, pass B F3)', () => {
  it('no share yet: /share-user is sent the default, UTC today + 90', async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe(utcDate(90)));

    await addUma();

    await waitFor(() => expect(shareUserBodies(fetchMock)).toHaveLength(1));
    expect(shareUserBodies(fetchMock)[0]).toEqual({
      recordType: 'matter',
      recordId: 'matter-1',
      systemUserId: UMA.id,
      accessLevel: 100000001,
      expiryDate: utcDate(90),
    });
  });

  it("shares exist: /share-user is sent the shares' date", async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(
      <AccessGrantModal
        {...makeProps(fetchMock, [personFromUser(UMA)], {
          fetchExistingGrants: jest.fn(async () => [externalShare('2099-03-01')]),
        })}
      />
    );
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe('2099-03-01'));

    await addUma();

    await waitFor(() => expect(shareUserBodies(fetchMock)).toHaveLength(1));
    expect(shareUserBodies(fetchMock)[0].expiryDate).toBe('2099-03-01');
  });

  it('negative: a past date in the field holds a user-only add back with a message; nothing is sent', async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);
    const input = await expiryInput();
    await waitFor(() => expect(input.value).toBe(utcDate(90)));
    fireEvent.change(input, { target: { value: '2000-01-01' } });

    await addUma();

    expect(
      await screen.findByText(`Choose ${displayDate(utcDate())} or later before adding people.`)
    ).toBeInTheDocument();
    expect(shareUserBodies(fetchMock)).toHaveLength(0);
  });

  it("a 400 expiry_in_past from /share-user shows the server's sentence", async () => {
    const DETAIL = 'ExpiryDate 2026-10-16 is in the past. Choose today (2026-10-17) or a later date.';
    const fetchMock = fetchWith(() =>
      problemResponse({ title: 'Bad request', detail: DETAIL, reasonCode: 'sdap.access.grant.expiry_in_past' }, 400)
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);

    await addUma();

    expect(await screen.findByText(new RegExp(DETAIL.replace(/[.()]/g, '\\$&')))).toBeInTheDocument();
    expect(screen.queryByText(/Shared with Uma Userton/)).not.toBeInTheDocument();
  });

  it('expiryNotApplied with workforce app access given: the date is the workforce app access end, not the share', async () => {
    const fetchMock = fetchWith(() =>
      jsonResponse({ ...CREATED, expiryNotApplied: true, workforceApp: { visible: true, until: UNTIL } })
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);

    await addUma();

    expect(
      await screen.findByText(
        `Shared with Uma Userton — visible in their workforce app until ${displayDate(UNTIL)}. The share itself has no ` +
          `end date; only their workforce app access ends on ${displayDate(UNTIL)}.`
      )
    ).toBeInTheDocument();
  });

  it('expiryNotApplied with no workforce app access: the Expiration was not applied', async () => {
    const fetchMock = fetchWith(() => jsonResponse({ ...CREATED, expiryNotApplied: true }));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);

    await addUma();

    expect(
      await screen.findByText(
        'Shared with Uma Userton. The share itself has no end date, so the Expiration was not applied to it.'
      )
    ).toBeInTheDocument();
  });

  it('MUST NOT FIRE: no expiryNotApplied, no end-date sentence', async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);

    await addUma();

    expect(await screen.findByText(/Shared with Uma Userton/)).toBeInTheDocument();
    expect(screen.queryByText(/no end date/)).not.toBeInTheDocument();
  });
});

describe('AccessGrantModal — two people with the same name (task 193 G4b, K3)', () => {
  const SAM_A: IUserPick = { id: '{AAAAAAAA-1111-1111-1111-111111111111}', name: 'Sam Same' };
  const SAM_B: IUserPick = { id: 'bbbbbbbb-2222-2222-2222-222222222222', name: 'Sam Same' };

  it('pure: same name and detail get a short ID; a different detail is enough; a unique name is unchanged', () => {
    const out = distinguishSameNames([
      { id: SAM_A.id, name: 'Sam Same', meta: 'System user' },
      { id: SAM_B.id, name: ' sam same ', meta: 'System user' },
      { id: 'c-1', name: 'Kim Kay', meta: 'System user · kim@one.example' },
      { id: 'c-2', name: 'Kim Kay', meta: 'System user · kim@two.example' },
      { id: 'c-3', name: 'Lee Lone', meta: 'System user' },
    ]);
    expect(out[0]).toMatchObject({ meta: 'System user · ID aaaaaaaa', label: 'Sam Same (System user · ID aaaaaaaa)' });
    expect(out[1]).toMatchObject({ meta: 'System user · ID bbbbbbbb' });
    expect(out[2]).toMatchObject({
      meta: 'System user · kim@one.example',
      label: 'Kim Kay (System user · kim@one.example)',
    });
    expect(out[3]).toMatchObject({ meta: 'System user · kim@two.example' });
    expect(out[4]).toEqual({ id: 'c-3', name: 'Lee Lone', meta: 'System user' });
  });

  it('pure: when the short IDs clash too, the whole ID is shown', () => {
    const out = distinguishSameNames([
      { id: 'abcdef01-0000-0000-0000-000000000001', name: 'Sam Same', meta: 'System user' },
      { id: 'abcdef01-0000-0000-0000-000000000002', name: 'Sam Same', meta: 'System user' },
    ]);
    expect(out[0].meta).toBe('System user · ID abcdef01-0000-0000-0000-000000000001');
    expect(out[1].meta).toBe('System user · ID abcdef01-0000-0000-0000-000000000002');
  });

  it('two users of the same name, no email: each row shows a distinct ID, and the add goes to the one chosen', async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(SAM_A), personFromUser(SAM_B)])} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Add person' }));
    await screen.findByRole('checkbox', { name: 'Select Sam Same' });
    fireEvent.click(screen.getByRole('button', { name: 'Add person' }));

    const boxA = await screen.findByRole('checkbox', { name: 'Select Sam Same (System user · ID aaaaaaaa)' });
    const boxB = screen.getByRole('checkbox', { name: 'Select Sam Same (System user · ID bbbbbbbb)' });
    expect(screen.getByText('System user · ID aaaaaaaa')).toBeInTheDocument();
    expect(screen.getByText('System user · ID bbbbbbbb')).toBeInTheDocument();

    // Both picks arrive selected; keep only the second.
    if ((boxA as HTMLInputElement).checked) fireEvent.click(boxA);
    if (!(boxB as HTMLInputElement).checked) fireEvent.click(boxB);
    await pickLevelIn(boxB, 'Collaborate');
    fireEvent.click(addButton());

    await waitFor(() => expect(shareUserBodies(fetchMock)).toHaveLength(1));
    expect(shareUserBodies(fetchMock)[0].systemUserId).toBe(SAM_B.id);
  });

  it('MUST NOT FIRE: same name with different emails shows the emails, no ID', async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(
      <AccessGrantModal
        {...makeProps(fetchMock, [
          personFromUser({ ...SAM_A, email: 'sam@one.example' }),
          personFromUser({ ...SAM_B, email: 'sam@two.example' }),
        ])}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Add person' }));
    await screen.findByRole('checkbox', { name: 'Select Sam Same' });
    fireEvent.click(screen.getByRole('button', { name: 'Add person' }));

    expect(
      await screen.findByRole('checkbox', { name: 'Select Sam Same (System user · sam@one.example)' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('checkbox', { name: 'Select Sam Same (System user · sam@two.example)' })
    ).toBeInTheDocument();
    expect(screen.queryByText(/· ID /)).not.toBeInTheDocument();
  });

  it('MUST NOT FIRE: a single user keeps the plain name', async () => {
    const fetchMock = fetchWith(() => jsonResponse(CREATED));
    renderWithTheme(<AccessGrantModal {...makeProps(fetchMock, [personFromUser(UMA)])} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Add person' }));

    expect(await screen.findByRole('checkbox', { name: 'Select Uma Userton' })).toBeInTheDocument();
    expect(screen.queryByText(/· ID /)).not.toBeInTheDocument();
  });
});
