/**
 * AccessGrantModal — the read-only No Access List, walled-off rows and cancelled rows (unified-access-control-r2 task
 * 067, owner round 59 item 3; task 066 folded in). Contract: notes/phase4-access-report-contract.md (task 064).
 *
 * Covers: the section lists 064's entries with their in-force state and is read-only; `notShown` hides it; every
 * untrustworthy answer is an error state, never "no entries"; only an in-force entry marks a Current Access row walled
 * off (contact, user share, organization grant, contact in a walled organization); Secure / Limited / Restricted
 * cancel the rows the server cancels; veto and cancellation look different; no level dropdown offers "No Access".
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type {
  IAccessGrantModalProps,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
  IUserPick,
  IRecordNoAccessEntry,
} from '../types';
import { parseNoAccessResponse, describeCoverage, suppressionFor, vetoFor, buildVetoIndex } from '../noAccess';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';
const CONTACT_ID = 'aaaaaaaa-0000-0000-0000-000000000001';
const CONTACT_B_ID = 'aaaaaaaa-0000-0000-0000-000000000002';
const STANDING_ID = 'aaaaaaaa-0000-0000-0000-000000000003';
const ORG_ID = 'bbbbbbbb-0000-0000-0000-000000000001';
const WALLED_ORG_ID = 'bbbbbbbb-0000-0000-0000-000000000009';
const USER_ID = 'cccccccc-0000-0000-0000-000000000001';
const PARENT_MATTER_ID = 'dddddddd-0000-0000-0000-000000000001';

const CONTACT_GRANT: IAccessGrantRecord = {
  accessRecordId: 'grant-contact-1',
  contactId: CONTACT_ID,
  fullName: 'Walter Walled',
  accessLevel: 100000002,
  grantedByName: 'Alice Admin',
  grantedDate: '2026-09-01T00:00:00Z',
};
const CONTACT_B_GRANT: IAccessGrantRecord = {
  accessRecordId: 'grant-contact-2',
  contactId: CONTACT_B_ID,
  fullName: 'Betty Bystander',
  accessLevel: 100000001,
  grantedByName: 'Alice Admin',
  grantedDate: '2026-09-01T00:00:00Z',
};
const ORG_GRANT: IAccessGrantRecord = {
  accessRecordId: 'grant-org-1',
  contactId: ORG_ID,
  fullName: 'All contacts at Acme LLP',
  accessLevel: 100000000,
  provenance: 'organization',
};
const STANDING_ROW: IAccessGrantRecord = {
  contactId: STANDING_ID,
  fullName: 'Sam Standing',
  accessLevel: 100000000,
  provenance: 'standing',
};

function entry(overrides: Partial<IRecordNoAccessEntry>): IRecordNoAccessEntry {
  return {
    entryId: 'entry-1',
    name: 'Wall',
    subjectKind: 'contact',
    subjectId: CONTACT_ID,
    subjectName: 'Walter Walled',
    objectKind: 'record',
    objectOrganizationId: null,
    objectOrganizationName: null,
    coveredRecordType: 'sprk_matter',
    coveredRecordId: RECORD_ID,
    viaSecureParent: false,
    alsoViaSecureParent: false,
    malformed: false,
    inForce: true,
    notInForceReason: null,
    modifiedById: 'u-1',
    modifiedByName: 'Ada Administrator',
    modifiedOn: '2026-10-01T00:00:00Z',
    ...overrides,
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

function noAccessBody(entries: IRecordNoAccessEntry[] | null, entriesState = 'complete', recordId = RECORD_ID) {
  return {
    recordType: 'sprk_matter',
    recordId,
    secure: 'applies',
    noAccess: entries?.some(e => e.inForce === true) ? 'applies' : 'doesNotApply',
    entriesState,
    entries,
  };
}

interface ISetup {
  noAccess?: () => Promise<Response>;
  grants?: IAccessGrantRecord[];
  standing?: IAccessGrantRecord[];
  shares?: Array<{ systemUserId: string; fullName: string; accessLevel: number }>;
  overrides?: Partial<IAccessGrantModalProps>;
}

function makeProps(setup: ISetup = {}): IAccessGrantModalProps {
  const noAccess = setup.noAccess ?? (async () => jsonResponse(noAccessBody([])));
  const authenticatedFetch = jest.fn(async (url: string) => {
    if (url.includes('/no-access')) return noAccess();
    if (url.includes('/user-shares')) return jsonResponse({ shares: setup.shares ?? [] });
    if (url.includes('/assigned-access')) return jsonResponse({ entries: [] });
    return jsonResponse({});
  });
  return {
    open: true,
    onClose: jest.fn(),
    recordId: RECORD_ID,
    recordType: 'matter',
    canGrantAccess: true,
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => setup.grants ?? [CONTACT_GRANT]),
    fetchStandingContacts: jest.fn(async () => setup.standing ?? []),
    searchContacts: jest.fn(async (): Promise<IContactSearchResult[]> => []),
    isInternalContact: jest.fn(async () => false),
    ...setup.overrides,
  };
}

const renderModal = (props: IAccessGrantModalProps, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <AccessGrantModal {...props} />
    </FluentProvider>
  );

/** The No Access List region (rendered only after loading). */
async function noAccessSection(): Promise<HTMLElement> {
  return screen.findByRole('region', { name: 'No Access List' });
}

/** The Current Access row containing the given name. */
function currentAccessRow(name: string): HTMLElement {
  // The same name can also appear in the No Access List; only Current Access rows carry data-access-state.
  const rows = screen
    .getAllByText(name)
    .map(el => el.closest('[data-access-state]') as HTMLElement | null)
    .filter((r): r is HTMLElement => r !== null);
  if (rows.length !== 1) throw new Error(`Expected one Current Access row for "${name}", found ${rows.length}`);
  return rows[0];
}

function noAccessCalls(props: IAccessGrantModalProps): Array<[string, RequestInit | undefined]> {
  return (props.authenticatedFetch as unknown as jest.Mock).mock.calls.filter((c: [string]) =>
    c[0].includes('/no-access')
  );
}

describe('AccessGrantModal — No Access List (task 067)', () => {
  it('reads the record through 064 with a canonical id, as a GET', async () => {
    const props = makeProps({ overrides: { recordId: `{${RECORD_ID.toUpperCase()}}` } });
    renderModal(props);
    await noAccessSection();
    const calls = noAccessCalls(props);
    expect(calls).toHaveLength(1);
    expect(calls[0][0]).toBe(`/api/v1/records/sprk_matter/${RECORD_ID}/no-access`);
    expect(calls[0][1]?.method).toBe('GET');
  });

  it.each([
    ['project', 'sprk_project'],
    ['workassignment', 'sprk_workassignment'],
  ] as const)('uses the %s route table', async (recordType, table) => {
    const props = makeProps({ overrides: { recordType } });
    renderModal(props);
    await noAccessSection();
    expect(noAccessCalls(props)[0][0]).toBe(`/api/v1/records/${table}/${RECORD_ID}/no-access`);
  });

  it('lists every entry with who, scope, in-force state and last change, and offers no add or remove', async () => {
    const entries = [
      entry({ entryId: 'e1' }),
      entry({
        entryId: 'e2',
        subjectKind: 'organization',
        subjectId: WALLED_ORG_ID,
        subjectName: 'Rival & Co',
        objectKind: 'organization',
        objectOrganizationId: ORG_ID,
        objectOrganizationName: 'Acme LLP',
      }),
      entry({
        entryId: 'e3',
        subjectKind: 'systemuser',
        subjectId: USER_ID,
        subjectName: 'Uma User',
        coveredRecordType: 'sprk_matter',
        coveredRecordId: PARENT_MATTER_ID,
        viaSecureParent: true,
      }),
      entry({
        entryId: 'e4',
        subjectKind: 'systemuser',
        subjectId: 'cccccccc-0000-0000-0000-000000000002',
        subjectName: 'Nora Nonsecure',
        inForce: false,
        notInForceReason: 'userWallOnNonSecureRecord',
      }),
      entry({
        entryId: 'e5',
        subjectKind: 'systemuser',
        subjectId: 'cccccccc-0000-0000-0000-000000000003',
        subjectName: 'Una Unknown',
        inForce: null,
        notInForceReason: 'secureStateUnknown',
      }),
      entry({
        entryId: 'e6',
        subjectKind: null,
        subjectId: null,
        subjectName: null,
        name: 'Broken wall',
        objectKind: null,
        malformed: true,
        inForce: false,
        notInForceReason: 'malformed',
      }),
      entry({ entryId: 'e7', subjectId: CONTACT_B_ID, subjectName: 'Also Parent', alsoViaSecureParent: true }),
    ];
    renderModal(makeProps({ noAccess: async () => jsonResponse(noAccessBody(entries)) }));
    const section = await noAccessSection();
    const s = within(section);

    expect(s.getByText(/Read-only here: an access administrator adds and removes entries/)).toBeInTheDocument();
    expect(s.getByText('Contact · This record')).toBeInTheDocument();
    expect(s.getByText('Organization (all its people) · Every record referencing Acme LLP')).toBeInTheDocument();
    expect(s.getByText('User · Through the secure matter this record is filed under')).toBeInTheDocument();
    expect(s.getByText('Contact · This record, and again through a secure parent')).toBeInTheDocument();
    expect(s.getByText('Not in force here: user walls apply only to secure records.')).toBeInTheDocument();
    expect(s.getByText(/Undetermined: whether this record is secure could not be read/)).toBeInTheDocument();
    expect(s.getByText(/Incomplete entry: it walls nobody off/)).toBeInTheDocument();
    expect(s.getByText('Broken wall')).toBeInTheDocument();
    expect(s.getAllByText(/Last changed by Ada Administrator on/).length).toBe(entries.length);

    expect(s.getAllByText('No Access')).toHaveLength(4); // e1, e2, e3, e7
    expect(s.getAllByText('Not in force')).toHaveLength(2); // e4, e6
    expect(s.getAllByText('Undetermined')).toHaveLength(1); // e5

    // Read-only: authoring is task 154's No Access Entries form.
    expect(s.queryAllByRole('button')).toHaveLength(0);
    expect(s.queryAllByRole('combobox')).toHaveLength(0);
  });

  it('warns, for a truncated list, that Current Access rows are marked from the listed entries only', async () => {
    // Betty is walled by an entry beyond the first 100: her row cannot be marked, and the section must say so.
    renderModal(
      makeProps({
        grants: [CONTACT_GRANT, CONTACT_B_GRANT],
        noAccess: async () => jsonResponse(noAccessBody([entry({})], 'truncated')),
      })
    );
    const section = await noAccessSection();
    const warning = within(section).getByText(/More entries cover this record than are listed here/);
    expect(warning).toHaveTextContent('the first 1 are shown');
    expect(warning).toHaveTextContent('Current Access rows are marked from the listed entries only');
    // Warning style, like the organization "could not be checked" note (a MessageBar, not a meta line).
    expect(warning.closest('.fui-MessageBar')).not.toBeNull();
    expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('vetoed');
    expect(currentAccessRow('Betty Bystander').getAttribute('data-access-state')).toBe('active');
  });

  it('a complete list carries no "marked from the listed entries only" warning', async () => {
    renderModal(makeProps({ noAccess: async () => jsonResponse(noAccessBody([entry({})])) }));
    const section = await noAccessSection();
    expect(within(section).queryByText(/marked from the listed entries only/)).not.toBeInTheDocument();
  });

  it('says nobody is listed only for a complete, empty list', async () => {
    renderModal(makeProps());
    const section = await noAccessSection();
    expect(within(section).getByText("No one is on this record's No Access List.")).toBeInTheDocument();
  });

  it('is not shown when 064 answers notShown (the caller lacks Write, owner O2)', async () => {
    renderModal(makeProps({ noAccess: async () => jsonResponse(noAccessBody(null, 'notShown')) }));
    await screen.findByText('Current Access');
    expect(screen.queryByRole('region', { name: 'No Access List' })).not.toBeInTheDocument();
  });

  it('sends no No Access request when the caller may not manage access (the modal stays closed to them)', async () => {
    const props = makeProps({ overrides: { canGrantAccess: false } });
    renderModal(props);
    await screen.findByText(/You do not have permission/);
    expect(noAccessCalls(props)).toHaveLength(0);
  });

  const failures: Array<[string, () => Promise<Response>]> = [
    ['a 404 (the route’s uniform refusal)', async () => jsonResponse({ reasonCode: 'x' }, 404)],
    ['a 403', async () => jsonResponse({ reasonCode: 'sdap.access.deny.delegation_write_required' }, 403)],
    ['a 500', async () => jsonResponse({}, 500)],
    ['entriesState unavailable', async () => jsonResponse(noAccessBody(null, 'unavailable'))],
    ['an unknown entriesState', async () => jsonResponse(noAccessBody([], 'someday'))],
    ['an answer about another record', async () => jsonResponse(noAccessBody([], 'complete', PARENT_MATTER_ID))],
    ['a listed state without entries', async () => jsonResponse(noAccessBody(null, 'complete'))],
    ['an off-contract entry', async () => jsonResponse(noAccessBody([{ ...entry({}), inForce: 'yes' } as never]))],
    [
      'an unparseable body',
      async () =>
        ({
          ok: true,
          status: 200,
          json: async () => {
            throw new SyntaxError('bad json');
          },
        }) as unknown as Response,
    ],
    [
      'a network failure',
      async () => {
        throw new TypeError('offline');
      },
    ],
  ];

  it.each(failures)('renders an error state, never "no entries", for %s', async (_label, noAccess) => {
    renderModal(makeProps({ noAccess }));
    const section = await noAccessSection();
    expect(within(section).getByText('No Access List unavailable')).toBeInTheDocument();
    expect(within(section).queryByText(/No one is on/)).not.toBeInTheDocument();
    // Not the delegation banner, and the rest of the modal still loads.
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
    expect(screen.getByText('Walter Walled')).toBeInTheDocument();
    expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('active');
  });
});

describe('AccessGrantModal — walled-off Current Access rows (task 067)', () => {
  it('marks a contact walled off by an in-force entry: No Access over a struck level, Revoke still available', async () => {
    renderModal(makeProps({ noAccess: async () => jsonResponse(noAccessBody([entry({})])) }));
    await noAccessSection();
    const row = currentAccessRow('Walter Walled');
    expect(row.getAttribute('data-access-state')).toBe('vetoed');
    expect(within(row).getByText('No Access')).toBeInTheDocument();
    expect(within(row).getByText('Full Access')).toBeInTheDocument();
    expect(within(row).getByText('On the No Access list: this grant gives no access.')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'Revoke' })).toBeEnabled();
  });

  it('matches the contact id canonically (braces, case)', async () => {
    const braced = entry({ subjectId: `{${CONTACT_ID.toUpperCase()}}` });
    renderModal(makeProps({ noAccess: async () => jsonResponse(noAccessBody([braced])) }));
    await noAccessSection();
    expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('vetoed');
  });

  it.each([
    ['not in force', { inForce: false, notInForceReason: 'userWallOnNonSecureRecord' }],
    ['undetermined', { inForce: null, notInForceReason: 'secureStateUnknown' }],
    ['malformed', { inForce: false, notInForceReason: 'malformed', malformed: true }],
  ] as const)('does NOT mark a row for an entry that is %s', async (_label, overrides) => {
    renderModal(makeProps({ noAccess: async () => jsonResponse(noAccessBody([entry({ ...overrides })])) }));
    await noAccessSection();
    const row = currentAccessRow('Walter Walled');
    expect(row.getAttribute('data-access-state')).toBe('active');
    expect(within(row).queryByText('No Access')).not.toBeInTheDocument();
  });

  it("marks an internal user's share for a user wall, without claiming the share gives no access anywhere", async () => {
    const userWall = entry({ subjectKind: 'systemuser', subjectId: USER_ID, subjectName: 'Uma User' });
    renderModal(
      makeProps({
        grants: [],
        shares: [{ systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000001 }],
        noAccess: async () => jsonResponse(noAccessBody([userWall])),
      })
    );
    await noAccessSection();
    const row = currentAccessRow('Uma User');
    expect(row.getAttribute('data-access-state')).toBe('vetoed');
    expect(within(row).getByText(/blocked in Teams and the Spaarke apps/)).toBeInTheDocument();
    expect(within(row).getByText(/revoke it so the model-driven app blocks them too/)).toBeInTheDocument();
  });

  it('marks an organization grant walled off by an organization wall', async () => {
    const orgWall = entry({ subjectKind: 'organization', subjectId: ORG_ID, subjectName: 'Acme LLP' });
    renderModal(makeProps({ grants: [ORG_GRANT], noAccess: async () => jsonResponse(noAccessBody([orgWall])) }));
    await noAccessSection();
    const row = currentAccessRow('All contacts at Acme LLP');
    expect(row.getAttribute('data-access-state')).toBe('vetoed');
    expect(within(row).getByText(/This organization is on the No Access list/)).toBeInTheDocument();
  });

  it('marks a contact who belongs to a walled organization, asking the host only about those contacts and walls', async () => {
    const orgWall = entry({ subjectKind: 'organization', subjectId: WALLED_ORG_ID, subjectName: 'Rival & Co' });
    const fetchContactOrganizationMemberships = jest.fn(async () => [
      { contactId: CONTACT_ID.toUpperCase(), organizationId: `{${WALLED_ORG_ID}}` },
    ]);
    renderModal(
      makeProps({
        grants: [CONTACT_GRANT, CONTACT_B_GRANT, ORG_GRANT],
        standing: [STANDING_ROW],
        noAccess: async () => jsonResponse(noAccessBody([orgWall])),
        overrides: { fetchContactOrganizationMemberships },
      })
    );
    await noAccessSection();
    await waitFor(() => expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('vetoed'));
    expect(
      within(currentAccessRow('Walter Walled')).getByText(
        'Rival & Co is on the No Access list and this contact belongs to it: this grant gives no access.'
      )
    ).toBeInTheDocument();
    expect(currentAccessRow('Betty Bystander').getAttribute('data-access-state')).toBe('active');
    expect(fetchContactOrganizationMemberships).toHaveBeenCalledTimes(1);
    const [contactIds, orgIds] = fetchContactOrganizationMemberships.mock.calls[0] as unknown as [string[], string[]];
    expect(contactIds.sort()).toEqual([CONTACT_ID, CONTACT_B_ID, STANDING_ID].sort());
    expect(orgIds).toEqual([WALLED_ORG_ID]);
  });

  it('does not ask the host about memberships when no organization wall is in force', async () => {
    const fetchContactOrganizationMemberships = jest.fn(async () => []);
    const notInForceOrgWall = entry({
      entryId: 'entry-2',
      subjectKind: 'organization',
      subjectId: WALLED_ORG_ID,
      inForce: false,
      notInForceReason: 'malformed',
    });
    renderModal(
      makeProps({
        noAccess: async () => jsonResponse(noAccessBody([entry({ subjectId: CONTACT_B_ID }), notInForceOrgWall])),
        overrides: { fetchContactOrganizationMemberships },
      })
    );
    await noAccessSection();
    expect(fetchContactOrganizationMemberships).not.toHaveBeenCalled();
  });

  it.each([
    ['the membership read fails', jest.fn(async () => Promise.reject(new Error('403')))],
    ['the host has no membership read', undefined],
  ])('says contacts could not be checked against an organization wall when %s', async (_label, fetcher) => {
    const orgWall = entry({ subjectKind: 'organization', subjectId: WALLED_ORG_ID, subjectName: 'Rival & Co' });
    renderModal(
      makeProps({
        noAccess: async () => jsonResponse(noAccessBody([orgWall])),
        overrides: { fetchContactOrganizationMemberships: fetcher },
      })
    );
    const section = await noAccessSection();
    expect(
      within(section).getByText(/whether the contacts in Current Access belong to it could not be checked/)
    ).toBeInTheDocument();
    expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('active');
  });
});

describe('AccessGrantModal — rows the record policy cancels (task 066, folded into 067)', () => {
  const allRows = () =>
    makeProps({
      grants: [CONTACT_GRANT, ORG_GRANT],
      standing: [STANDING_ROW],
      shares: [{ systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000001 }],
    });

  it('Restricted: every contact-based row has no effect; an internal user share is untouched', async () => {
    renderModal({ ...allRows(), accessPermissionState: 'restricted' });
    await noAccessSection();
    for (const name of ['Walter Walled', 'All contacts at Acme LLP', 'Sam Standing']) {
      const row = currentAccessRow(name);
      expect(row.getAttribute('data-access-state')).toBe('suppressed');
      expect(within(row).getByText('No effect')).toBeInTheDocument();
      expect(
        within(row).getByText('No effect: this record is Restricted, so contacts get no access.')
      ).toBeInTheDocument();
    }
    const shareRow = currentAccessRow('Uma User');
    expect(shareRow.getAttribute('data-access-state')).toBe('active');
  });

  it('Secure: organization-wide and standing rows have no effect; a named grant stays active', async () => {
    renderModal({ ...allRows(), accessPermissionState: 'limited', isSecureRecord: true });
    await noAccessSection();
    expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('active');
    expect(
      within(currentAccessRow('All contacts at Acme LLP')).getByText(
        'No effect: this record is secure, so organization-wide grants give no access.'
      )
    ).toBeInTheDocument();
    expect(
      within(currentAccessRow('Sam Standing')).getByText(
        'No effect: this record is secure, so standing grants give no access.'
      )
    ).toBeInTheDocument();
  });

  it('Limited (not secure) names Limited; Secure – Restricted names it', () => {
    expect(suppressionFor('standing', 'limited', false)).toBe(
      'No effect: this record is Limited, so standing grants give no access.'
    );
    expect(suppressionFor('contact', 'restricted', true)).toBe(
      'No effect: this record is Secure – Restricted, so contacts get no access.'
    );
    expect(suppressionFor('share', 'restricted', true)).toBeNull();
    expect(suppressionFor('contact', 'limited', true)).toBeNull();
  });

  it('Standard: nothing is cancelled', async () => {
    renderModal(allRows());
    await noAccessSection();
    for (const name of ['Walter Walled', 'All contacts at Acme LLP', 'Sam Standing']) {
      expect(currentAccessRow(name).getAttribute('data-access-state')).toBe('active');
    }
  });

  it('a wall wins over cancellation, and the two look different', async () => {
    renderModal({
      ...makeProps({
        grants: [CONTACT_GRANT, CONTACT_B_GRANT],
        noAccess: async () => jsonResponse(noAccessBody([entry({})])),
      }),
      accessPermissionState: 'restricted',
    });
    await noAccessSection();
    const walled = currentAccessRow('Walter Walled');
    const cancelled = currentAccessRow('Betty Bystander');
    expect(walled.getAttribute('data-access-state')).toBe('vetoed');
    expect(within(walled).queryByText('No effect')).not.toBeInTheDocument();
    expect(within(walled).getByText('No Access')).toBeInTheDocument();
    expect(cancelled.getAttribute('data-access-state')).toBe('suppressed');
    expect(within(cancelled).queryByText('No Access')).not.toBeInTheDocument();
    // Different reason lines, different classes (danger status vs neutral italic).
    const vetoLine = within(walled).getByText(/On the No Access list/);
    const cancelLine = within(cancelled).getByText(/No effect: this record is Restricted/);
    expect(vetoLine.className).not.toBe(cancelLine.className);
  });
});

describe('AccessGrantModal — "No Access" is a veto, never a level (spec FR-23)', () => {
  it('no level dropdown offers "No Access" (contact, organization and user rows)', async () => {
    const props = makeProps({
      noAccess: async () => jsonResponse(noAccessBody([entry({})])),
      overrides: {
        pickContact: jest.fn(async (): Promise<IContactSearchResult | null> => ({
          contactId: 'contact-new',
          fullName: 'Nina New',
        })),
        pickOrganization: jest.fn(async (): Promise<IOrganizationPick | null> => ({ id: 'org-new', name: 'NewOrg' })),
        pickUser: jest.fn(async (): Promise<IUserPick | null> => ({ id: 'user-new', name: 'Ulf New' })),
      },
    });
    renderModal(props);
    await noAccessSection();
    fireEvent.click(screen.getByRole('button', { name: 'Add contact' }));
    await screen.findByText('Nina New');
    fireEvent.click(screen.getByRole('button', { name: 'Add organization' }));
    await screen.findByText('NewOrg');
    fireEvent.click(screen.getByRole('button', { name: 'Add user' }));
    await screen.findByText('Ulf New');

    const combos = screen.getAllByRole('combobox');
    expect(combos).toHaveLength(3);
    for (const combo of combos) {
      fireEvent.click(combo);
      const options = await screen.findAllByRole('option');
      const labels = options.map(o => o.textContent);
      expect(labels).toEqual(['View Only', 'Collaborate', 'Full Access']);
      expect(labels.some(l => /no access/i.test(l ?? ''))).toBe(false);
      fireEvent.click(combo);
    }
  });
});

describe('AccessGrantModal — No Access List in dark mode (ADR-021)', () => {
  it('renders the section, the veto and the cancelled marker under the dark theme', async () => {
    renderModal(
      {
        ...makeProps({
          grants: [CONTACT_GRANT, ORG_GRANT],
          noAccess: async () => jsonResponse(noAccessBody([entry({})])),
        }),
        accessPermissionState: 'limited',
        isSecureRecord: true,
      },
      webDarkTheme
    );
    const section = await noAccessSection();
    expect(within(section).getByText('No Access')).toBeInTheDocument();
    expect(currentAccessRow('Walter Walled').getAttribute('data-access-state')).toBe('vetoed');
    expect(currentAccessRow('All contacts at Acme LLP').getAttribute('data-access-state')).toBe('suppressed');
  });
});

describe('noAccess helpers', () => {
  it('parse rejects an entry with an unknown subject kind', () => {
    expect(parseNoAccessResponse(noAccessBody([{ ...entry({}), subjectKind: 'team' } as never]), RECORD_ID)).toEqual({
      kind: 'error',
    });
  });

  it('parse accepts the braced form of the record id', () => {
    const state = parseNoAccessResponse(noAccessBody([entry({})]), `{${RECORD_ID.toUpperCase()}}`);
    expect(state.kind).toBe('list');
  });

  it('describes an organization wall reached through a secure parent', () => {
    expect(
      describeCoverage(entry({ objectKind: 'organization', objectOrganizationName: 'Acme LLP', viaSecureParent: true }))
    ).toBe('Every record referencing Acme LLP, reaching this one through the secure matter it is filed under');
  });

  it('only an in-force entry vetoes', () => {
    const index = buildVetoIndex([entry({ inForce: null }), entry({ entryId: 'x', subjectId: CONTACT_B_ID })]);
    expect(vetoFor(CONTACT_GRANT, 'contact', index, new Map())).toBeNull();
    expect(vetoFor(CONTACT_B_GRANT, 'contact', index, new Map())).not.toBeNull();
  });
});

/** A promise whose resolution the test controls. */
function deferred<T>(): { promise: Promise<T>; resolve: (v: T) => void } {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>(r => {
    resolve = r;
  });
  return { promise, resolve };
}

describe('AccessGrantModal — overlapping loads never show another record (verifier F4-a)', () => {
  const RECORD_A = 'eeeeeeee-0000-0000-0000-00000000000a';
  const RECORD_B = 'eeeeeeee-0000-0000-0000-00000000000b';
  const PERSON_A: IAccessGrantRecord = { ...CONTACT_B_GRANT, accessRecordId: 'grant-a', fullName: 'Alpha Person' };
  const PERSON_B: IAccessGrantRecord = { ...CONTACT_GRANT, accessRecordId: 'grant-b', fullName: 'Bravo Person' };

  /** One modal whose loads for A and B the test resolves in any order; the host rebinds A → B between opens. */
  function setup() {
    const noAccessA = deferred<Response>();
    const noAccessB = deferred<Response>();
    const grantsA = deferred<IAccessGrantRecord[]>();
    const grantsB = deferred<IAccessGrantRecord[]>();
    let current = RECORD_A;
    const authenticatedFetch = jest.fn(async (url: string) => {
      if (url.includes(`/${RECORD_A}/no-access`)) return noAccessA.promise;
      if (url.includes(`/${RECORD_B}/no-access`)) return noAccessB.promise;
      if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
      if (url.includes('/assigned-access')) return jsonResponse({ entries: [] });
      return jsonResponse({});
    });
    // Like the host's fetchExistingGrants, it reads the record bound at call time.
    const fetchExistingGrants = jest.fn(() => (current === RECORD_A ? grantsA.promise : grantsB.promise));
    const props = (recordId: string, open: boolean): IAccessGrantModalProps =>
      makeProps({
        overrides: {
          recordId,
          open,
          fetchExistingGrants,
          authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
        },
      });
    const view = renderModal(props(RECORD_A, true));
    const rebindToB = () => {
      view.rerender(
        <FluentProvider theme={webLightTheme}>
          <AccessGrantModal {...props(RECORD_A, false)} />
        </FluentProvider>
      );
      current = RECORD_B;
      view.rerender(
        <FluentProvider theme={webLightTheme}>
          <AccessGrantModal {...props(RECORD_B, true)} />
        </FluentProvider>
      );
    };
    // The host rebinds while the modal stays open (no close/reopen, so no new load starts).
    const rebindWhileOpen = () => {
      current = RECORD_B;
      view.rerender(
        <FluentProvider theme={webLightTheme}>
          <AccessGrantModal {...props(RECORD_B, true)} />
        </FluentProvider>
      );
    };
    return { noAccessA, noAccessB, grantsA, grantsB, rebindToB, rebindWhileOpen };
  }

  const wallOnBravo = (recordId: string) =>
    jsonResponse({ ...noAccessBody([entry({ subjectName: 'Bravo Person' })]), recordId });
  const emptyListFor = (recordId: string) => jsonResponse(noAccessBody([], 'complete', recordId));

  it("an answer for A arriving after B's is dropped: B keeps its own walls and rows", async () => {
    const t = setup();
    t.rebindToB();
    t.noAccessB.resolve(wallOnBravo(RECORD_B));
    t.grantsB.resolve([PERSON_B]);
    const section = await noAccessSection();
    expect(within(section).getByText('No Access')).toBeInTheDocument();

    // A's load lands last: "nobody is listed" and A's grant row must not replace B's.
    t.noAccessA.resolve(emptyListFor(RECORD_A));
    t.grantsA.resolve([PERSON_A]);
    await new Promise(r => setTimeout(r, 20));
    expect(within(await noAccessSection()).queryByText(/No one is on/)).not.toBeInTheDocument();
    expect(screen.queryByText('Alpha Person')).not.toBeInTheDocument();
    expect(currentAccessRow('Bravo Person').getAttribute('data-access-state')).toBe('vetoed');
  });

  it("an older load finishing first neither shows A's data nor ends B's loading state", async () => {
    const t = setup();
    t.rebindToB();
    t.noAccessA.resolve(emptyListFor(RECORD_A));
    t.grantsA.resolve([PERSON_A]);
    await new Promise(r => setTimeout(r, 20));
    expect(screen.getByText('Loading access data…')).toBeInTheDocument();
    expect(screen.queryByText('Alpha Person')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'No Access List' })).not.toBeInTheDocument();

    t.noAccessB.resolve(wallOnBravo(RECORD_B));
    t.grantsB.resolve([PERSON_B]);
    const section = await noAccessSection();
    expect(within(section).getByText('No Access')).toBeInTheDocument();
    expect(screen.queryByText('Alpha Person')).not.toBeInTheDocument();
  });

  it("A's answer arriving after the modal was rebound to B while open is checked against B: an error, not A's list", async () => {
    const t = setup();
    t.rebindWhileOpen();
    t.noAccessA.resolve(emptyListFor(RECORD_A));
    t.grantsA.resolve([PERSON_A]);
    const section = await noAccessSection();
    expect(within(section).getByText('No Access List unavailable')).toBeInTheDocument();
    expect(within(section).queryByText(/No one is on/)).not.toBeInTheDocument();
  });

  it("B's request answered with A's body (wrong echo) is an error, never A's list", async () => {
    const t = setup();
    t.rebindToB();
    t.noAccessB.resolve(emptyListFor(RECORD_A));
    t.grantsB.resolve([PERSON_B]);
    const section = await noAccessSection();
    expect(within(section).getByText('No Access List unavailable')).toBeInTheDocument();
    expect(within(section).queryByText(/No one is on/)).not.toBeInTheDocument();
  });
});
