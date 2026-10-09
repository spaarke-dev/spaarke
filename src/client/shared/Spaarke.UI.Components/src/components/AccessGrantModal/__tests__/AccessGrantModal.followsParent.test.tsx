/**
 * AccessGrantModal and the Access Permission pill on a record filed under a matter or project (unified-access-control-r2
 * task 175; owner round 87, refining round 84): the parent sets a FLOOR. The child may be made stricter, never looser;
 * its grants and shares are unaffected.
 *
 * Covers: a child keeps every grant affordance; the parent bar ("Minimum access comes from the parent …") shows WITH the
 * Access Permission bar (coordinator O-4) and says whether each effective value is inherited or set on this record; a
 * parentless record shows no parent bar; the server's 409 `sdap.access.access_follows_parent` is an inline refusal of
 * that action, never a modal-wide state; an inherited user share is a read-only row that still carries the No Access
 * marker; the No Access List names a direct parent only; the pure floor rules — the pill's option filter,
 * `parentUnverifiable` making the pill read-only, the inherited / set-on-this-record display.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type {
  IAccessFloor,
  IAccessGrantModalProps,
  IAccessGrantRecord,
  IFollowsParent,
  IRecordNoAccessEntry,
  IUserPick,
} from '../types';
import type { IAssignedAccessEntry } from '../AccessGrantModal';
import {
  classifyCurrentAccessRow,
  describeCoverage,
  suppressionFor,
  vetoFor,
  buildVetoIndex,
  contactIdsToCheck,
} from '../noAccess';
import {
  parseFollowsParents,
  parseAccessFloor,
  followsParentFallbackMessage,
  otherParentsSuffix,
  describeInheritedShare,
  resolveAccessPermissionPill,
  isLooserThanFloor,
  accessPermissionStateOf,
  accessPermissionOrigin,
  secureOrigin,
  describeOrigin,
  describeEffectiveAccess,
} from '../followsParent';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';
const PARENT_MATTER_ID = 'dddddddd-0000-0000-0000-000000000001';
const PARENT_PROJECT_ID = 'dddddddd-0000-0000-0000-000000000002';
const GRANDPARENT_ID = 'dddddddd-0000-0000-0000-000000000009';
const USER_ID = 'cccccccc-0000-0000-0000-000000000001';
const INHERITED_USER_ID = 'cccccccc-0000-0000-0000-000000000002';

const PARENT_MATTER: IFollowsParent = { recordType: 'matter', recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' };
const RESTRICTED_FLOOR: IAccessFloor = {
  floorSecure: true,
  floorAccessPermission: 'restricted',
  parentUnverifiable: false,
};

/** The host's raw option values (the PCF's `sprk_accesspermission`): Standard / Limited / Restricted. */
const VALUES = { limited: 100000001, restricted: 100000002 };
const OPTIONS = [
  { value: 100000000, label: 'Standard' },
  { value: 100000001, label: 'Limited' },
  { value: 100000002, label: 'Restricted' },
];

const CONTACT_GRANT: IAccessGrantRecord = {
  accessRecordId: 'grant-contact-1',
  contactId: 'aaaaaaaa-0000-0000-0000-000000000001',
  fullName: 'Carla Contact',
  accessLevel: 100000001,
  grantedByName: 'Alice Admin',
  grantedDate: '2026-09-01T00:00:00Z',
};

const PENDING_SUGGESTION: IAssignedAccessEntry = {
  entryId: 'entry-1',
  sourceField: 'sprk_assignedparalegal1',
  sourceFieldLabel: 'Assigned Paralegal 1',
  subjectKind: 'contact',
  subjectId: 'aaaaaaaa-0000-0000-0000-000000000005',
  subjectName: 'Pat Paralegal',
  systemUserId: null,
  accessRecordId: null,
  state: 'PendingConfirmation',
  reason: null,
  residualAccessTerms: [],
};

interface IShareFixture {
  systemUserId: string;
  fullName: string;
  accessLevel: number;
  modifiedOn?: string;
  inheritedFrom?: { recordType: string; recordId: string } | null;
}

function json(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

function noAccessBody(entries: IRecordNoAccessEntry[], secure = 'doesNotApply') {
  return {
    recordType: 'sprk_workassignment',
    recordId: RECORD_ID,
    secure,
    noAccess: entries.some(e => e.inForce === true) ? 'applies' : 'doesNotApply',
    entriesState: 'complete',
    entries,
  };
}

function entry(overrides: Partial<IRecordNoAccessEntry>): IRecordNoAccessEntry {
  return {
    entryId: 'entry-1',
    name: 'Wall',
    subjectKind: 'systemuser',
    subjectId: INHERITED_USER_ID,
    subjectName: 'Ingrid Inherited',
    objectKind: 'record',
    objectOrganizationId: null,
    objectOrganizationName: null,
    coveredRecordType: 'sprk_matter',
    coveredRecordId: PARENT_MATTER_ID,
    viaSecureParent: true,
    alsoViaSecureParent: false,
    malformed: false,
    inForce: true,
    notInForceReason: null,
    modifiedById: null,
    modifiedByName: null,
    modifiedOn: null,
    ...overrides,
  };
}

interface ISetup {
  shares?: IShareFixture[];
  suggestions?: IAssignedAccessEntry[];
  noAccessEntries?: IRecordNoAccessEntry[];
  /** Answers a POST write (revoke / share-user / grant / dismiss); default 200 `{ deactivatedCount: 1 }`. */
  write?: (url: string) => Response;
  overrides?: Partial<IAccessGrantModalProps>;
}

function makeProps(setup: ISetup = {}): IAccessGrantModalProps {
  const authenticatedFetch = jest.fn(async (url: string, init?: RequestInit) => {
    if (url.includes('/no-access')) return json(noAccessBody(setup.noAccessEntries ?? []));
    if (url.includes('/user-shares')) return json({ shares: setup.shares ?? [] });
    if (url.includes('/assigned-access?')) return json({ entries: setup.suggestions ?? [] });
    if (init?.method === 'POST' && setup.write) return setup.write(url);
    return json({ deactivatedCount: 1 });
  });
  return {
    open: true,
    onClose: jest.fn(),
    recordId: RECORD_ID,
    recordType: 'workassignment',
    canGrantAccess: true,
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => [
      { contactId: 'aaaaaaaa-0000-0000-0000-000000000007', fullName: 'Cody Candidate', role: 'Assigned Attorney 1' },
    ]),
    fetchExistingGrants: jest.fn(async () => [CONTACT_GRANT]),
    searchContacts: jest.fn(async () => []),
    pickContact: jest.fn(async () => null),
    pickOrganization: jest.fn(async () => null),
    pickUser: jest.fn(async (): Promise<IUserPick | null> => null),
    isInternalContact: jest.fn(async () => false),
    ...setup.overrides,
  };
}

const renderModal = (props: IAccessGrantModalProps) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <AccessGrantModal {...props} />
    </FluentProvider>
  );

/** Waits for the load to finish (the No Access List renders only after it). */
async function loaded(): Promise<void> {
  await screen.findByRole('region', { name: 'No Access List' });
  await waitFor(() => expect(screen.queryByText('Loading access data…')).not.toBeInTheDocument());
}

function currentAccessRow(name: string): HTMLElement {
  const rows = screen
    .getAllByText(name, { exact: false })
    .map(el => el.closest('[data-access-state]') as HTMLElement | null)
    .filter((r): r is HTMLElement => r !== null);
  if (rows.length !== 1) throw new Error(`Expected one Current Access row for "${name}", found ${rows.length}`);
  return rows[0];
}

/** The info bar naming the parent, or `null`. */
function parentBar(): HTMLElement | null {
  const title = screen.queryByText('Minimum access from the parent', { selector: '.fui-MessageBarTitle' });
  return title ? (title.closest('.fui-MessageBar') as HTMLElement) : null;
}

/** Every grant affordance (owner round 87: all stay available on a child). */
function expectEveryGrantAffordance(): void {
  expect(screen.getByRole('button', { name: 'Add contact' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Add organization' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Add user' })).toBeEnabled();
  expect(screen.getByRole('checkbox', { name: 'Select Cody Candidate' })).toBeEnabled();
  expect(screen.getAllByRole('combobox').length).toBeGreaterThan(0);
  expect(screen.getByRole('button', { name: /^Add \(/ })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Grant Pat Paralegal' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Dismiss Pat Paralegal' })).toBeEnabled();
  // The contact grant and the direct user share are both revocable.
  for (const revoke of screen.getAllByRole('button', { name: 'Revoke' })) expect(revoke).toBeEnabled();
  expect(screen.getAllByRole('button', { name: 'Revoke' })).toHaveLength(2);
}

const DIRECT_SHARE: IShareFixture = { systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000000 };

describe('AccessGrantModal — a record with a parent (task 175, owner round 87)', () => {
  it('keeps every grant affordance', async () => {
    renderModal(
      makeProps({
        overrides: { followsParents: [PARENT_MATTER], onOpenParent: jest.fn(), accessFloor: RESTRICTED_FLOOR },
        shares: [DIRECT_SHARE],
        suggestions: [PENDING_SUGGESTION],
      })
    );
    await loaded();

    expectEveryGrantAffordance();
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
  });

  it('names the parent in an info bar whose link opens it, WITH the Access Permission bar', async () => {
    const onOpenParent = jest.fn();
    renderModal(
      makeProps({
        overrides: {
          followsParents: [PARENT_MATTER],
          onOpenParent,
          accessPermissionState: 'restricted',
          isSecureRecord: true,
        },
      })
    );
    await loaded();

    const bar = parentBar();
    expect(bar).toHaveTextContent(
      'Minimum access comes from the parent matter Acme v. Beta: this record can be made stricter but not looser.'
    );
    // Coordinator O-4: the Access Permission bar stays.
    expect(screen.getByText('Secure – Restricted', { selector: '.fui-MessageBarTitle' })).toBeInTheDocument();
    fireEvent.click(within(bar!).getByRole('button', { name: 'Acme v. Beta' }));
    expect(onOpenParent).toHaveBeenCalledWith(PARENT_MATTER);
  });

  it('says whether each effective value is inherited or set on this record', async () => {
    renderModal(
      makeProps({
        overrides: {
          followsParents: [PARENT_MATTER],
          accessFloor: { floorSecure: false, floorAccessPermission: 'limited', parentUnverifiable: false },
          recordAccessPermission: 'limited',
          isSecureRecord: true,
        },
      })
    );
    await loaded();

    const bar = parentBar()!;
    expect(
      within(bar).getByText('Access Permission: Limited (inherited from Matter Acme v. Beta)')
    ).toBeInTheDocument();
    expect(within(bar).getByText('Secure (set on this record)')).toBeInTheDocument();
  });

  it('names the first of several parents and counts the others; an unnamed parent links by its type', async () => {
    const onOpenParent = jest.fn();
    const unnamed: IFollowsParent = { recordType: 'project', recordId: PARENT_PROJECT_ID, name: null };
    renderModal(makeProps({ overrides: { followsParents: [unnamed, PARENT_MATTER, PARENT_MATTER], onOpenParent } }));
    await loaded();

    const bar = parentBar()!;
    expect(bar).toHaveTextContent(
      'Minimum access comes from the parent project and 2 others: this record can be made stricter but not looser.'
    );
    fireEvent.click(within(bar).getByRole('button', { name: 'project' }));
    expect(onOpenParent).toHaveBeenCalledWith(unnamed);
  });

  it('without onOpenParent names the parent as text, no link', async () => {
    renderModal(makeProps({ overrides: { followsParents: [PARENT_MATTER] } }));
    await loaded();

    const bar = parentBar()!;
    expect(within(bar).queryByRole('button')).not.toBeInTheDocument();
    expect(within(bar).getByText('Acme v. Beta').tagName).toBe('STRONG');
  });

  it('the no-permission state is unchanged on a child', async () => {
    renderModal(makeProps({ overrides: { followsParents: [PARENT_MATTER], canGrantAccess: false } }));

    expect(
      await screen.findByText('You do not have permission to grant or revoke access for this record.')
    ).toBeInTheDocument();
    expect(parentBar()).toBeNull();
  });
});

describe('AccessGrantModal — a parentless record (task 175)', () => {
  it.each([
    ['followsParents omitted', undefined],
    ['followsParents empty', [] as IFollowsParent[]],
  ])('%s: every grant affordance and no parent bar', async (_label, followsParents) => {
    renderModal(
      makeProps({ overrides: { followsParents }, shares: [DIRECT_SHARE], suggestions: [PENDING_SUGGESTION] })
    );
    await loaded();

    expect(parentBar()).toBeNull();
    expectEveryGrantAffordance();
  });
});

describe('AccessGrantModal — the 409 access_follows_parent is an inline refusal (task 175)', () => {
  const refusal = (body: Record<string, unknown>) => (): Response =>
    json({ status: 409, reasonCode: 'sdap.access.access_follows_parent', ...body }, 409);
  const SERVER_SENTENCE = 'This would make the work assignment less restricted than the matter Acme v. Beta.';

  async function revokeCarla(): Promise<void> {
    fireEvent.click(within(currentAccessRow('Carla Contact')).getByRole('button', { name: 'Revoke' }));
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Revoke' }));
  }

  it('a refused Revoke shows the server sentence for that action and nothing else changes', async () => {
    renderModal(
      makeProps({
        shares: [DIRECT_SHARE],
        suggestions: [PENDING_SUGGESTION],
        write: refusal({ detail: SERVER_SENTENCE }),
      })
    );
    await loaded();

    await revokeCarla();

    expect(await screen.findByText(SERVER_SENTENCE)).toBeInTheDocument();
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
    expect(parentBar()).toBeNull();
    // Not a modal-wide state: every affordance stays, enabled.
    expectEveryGrantAffordance();
  });

  it('without a detail it shows the designed sentence for the parent type', async () => {
    renderModal(makeProps({ write: refusal({ parentRecordType: 'project' }) }));
    await loaded();

    await revokeCarla();

    expect(await screen.findByText(followsParentFallbackMessage('project'))).toBeInTheDocument();
    expect(followsParentFallbackMessage('project')).toBe(
      "This change would make this record's access looser than the project it is filed under. A record can be made " +
        'stricter than its parent, but not looser.'
    );
  });

  it('a refused Add item is reported by name and the rest of the batch is not stopped', async () => {
    const props = makeProps({ write: refusal({ detail: SERVER_SENTENCE }) });
    renderModal(props);
    await loaded();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Cody Candidate' }));
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: 'View Only' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add (1)' }));

    expect(await screen.findByText(`Granted access to 0 of 1. Cody Candidate: ${SERVER_SENTENCE}`)).toBeInTheDocument();
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add contact' })).toBeEnabled();
  });

  it('a refused suggestion Grant or Dismiss shows the sentence inline', async () => {
    renderModal(makeProps({ suggestions: [PENDING_SUGGESTION], write: refusal({ detail: SERVER_SENTENCE }) }));
    await loaded();

    fireEvent.click(screen.getByRole('button', { name: 'Grant Pat Paralegal' }));
    expect(await screen.findByText(SERVER_SENTENCE)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss Pat Paralegal' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Dismiss Pat Paralegal' })).toBeEnabled());
    expect(screen.getByText(SERVER_SENTENCE)).toBeInTheDocument();
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
  });

  it('a 409 with another reason code is an ordinary failure', async () => {
    renderModal(makeProps({ write: () => json({ reasonCode: 'sdap.access.grant.would_lower_existing' }, 409) }));
    await loaded();

    await revokeCarla();

    expect(await screen.findByText(/Failed to revoke access for Carla Contact/)).toBeInTheDocument();
  });
});

describe('AccessGrantModal — inherited user shares (task 175)', () => {
  const INHERITED: IShareFixture = {
    systemUserId: INHERITED_USER_ID,
    fullName: 'Ingrid Inherited',
    accessLevel: 100000001,
    modifiedOn: '2026-10-01T00:00:00Z',
    inheritedFrom: { recordType: 'matter', recordId: PARENT_MATTER_ID },
  };

  it('is a read-only row, labelled with its parent', async () => {
    renderModal(makeProps({ shares: [INHERITED, DIRECT_SHARE], overrides: { followsParents: [PARENT_MATTER] } }));
    await loaded();

    const row = currentAccessRow('Ingrid Inherited');
    expect(row).toHaveTextContent('Inherited from the matter');
    expect(within(row).getByText('Inherited')).toBeInTheDocument();
    expect(within(row).getByText('Collaborate')).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
    // A direct share on the same record stays revocable.
    expect(within(currentAccessRow('Uma User')).getByRole('button', { name: 'Revoke' })).toBeInTheDocument();
  });

  it('still carries the No Access marker of a user wall in force, naming the direct parent it reaches through', async () => {
    renderModal(
      makeProps({ shares: [INHERITED], noAccessEntries: [entry({})], overrides: { followsParents: [PARENT_MATTER] } })
    );
    await loaded();

    const row = currentAccessRow('Ingrid Inherited');
    expect(row).toHaveAttribute('data-access-state', 'vetoed');
    expect(within(row).getByText('No Access')).toBeInTheDocument();
    const section = screen.getByRole('region', { name: 'No Access List' });
    expect(
      within(section).getByText('User · Through the secure matter this record is filed under: Acme v. Beta')
    ).toBeInTheDocument();
  });

  it('a share with inheritedFrom null is a direct, revocable share', async () => {
    renderModal(makeProps({ shares: [{ ...DIRECT_SHARE, inheritedFrom: null }] }));
    await loaded();

    const row = currentAccessRow('Uma User');
    expect(row).toHaveTextContent('Internal user share');
    expect(within(row).getByRole('button', { name: 'Revoke' })).toBeInTheDocument();
  });
});

describe('task 175 rules (pure)', () => {
  const inherited: IAccessGrantRecord = {
    contactId: INHERITED_USER_ID,
    fullName: 'Ingrid Inherited',
    accessLevel: 100000001,
    provenance: 'inherited',
    inheritedFrom: { recordType: 'project', recordId: PARENT_PROJECT_ID },
  };

  it('classifies an inherited share as its own kind, marked like a user share', () => {
    expect(classifyCurrentAccessRow(inherited)).toBe('inherited');
    const index = buildVetoIndex([entry({})]);
    expect(vetoFor(inherited, 'inherited', index, new Map())).toBe(vetoFor(inherited, 'share', index, new Map()));
    expect(vetoFor(inherited, 'inherited', index, new Map())).toMatch(/^On the No Access list/);
    expect(suppressionFor('inherited', 'restricted', true)).toBeNull();
    expect(contactIdsToCheck([inherited])).toEqual([]);
    expect(describeInheritedShare(inherited)).toBe('Inherited from the project');
    expect(describeInheritedShare({ ...inherited, inheritedFrom: { recordType: '', recordId: '' } })).toBe(
      'Inherited from a parent record'
    );
  });

  it('names the covering record only when it is a direct parent', () => {
    const viaParent = entry({});
    expect(describeCoverage(viaParent, [{ recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' }])).toBe(
      'Through the secure matter this record is filed under: Acme v. Beta'
    );
    const viaGrandparent = entry({ coveredRecordId: GRANDPARENT_ID });
    expect(describeCoverage(viaGrandparent, [{ recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' }])).toBe(
      'Through the secure matter this record is filed under'
    );
    expect(describeCoverage(viaParent, [{ recordId: PARENT_MATTER_ID, name: null }])).toBe(
      'Through the secure matter this record is filed under'
    );
    expect(
      describeCoverage(
        entry({
          objectKind: 'organization',
          objectOrganizationName: 'Acme LLP',
          coveredRecordId: `{${PARENT_MATTER_ID.toUpperCase()}}`,
        }),
        [{ recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' }]
      )
    ).toBe(
      'Every record referencing Acme LLP, reaching this one through the secure matter it is filed under: Acme v. Beta'
    );
    expect(
      describeCoverage(entry({ viaSecureParent: false, coveredRecordId: PARENT_MATTER_ID }), [PARENT_MATTER])
    ).toBe('This record');
  });

  it('parses followsParents, skipping entries that are not a matter or project with an id', () => {
    expect(
      parseFollowsParents([
        { recordType: 'matter', recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' },
        { recordType: 'project', recordId: PARENT_PROJECT_ID, name: null },
        { recordType: 'workassignment', recordId: 'x', name: 'n' },
        { recordType: 'matter', recordId: '', name: 'n' },
        null,
      ])
    ).toEqual([
      { recordType: 'matter', recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' },
      { recordType: 'project', recordId: PARENT_PROJECT_ID, name: null },
    ]);
    expect(parseFollowsParents(undefined)).toEqual([]);
    expect(parseFollowsParents({})).toEqual([]);
  });

  it('parses the floor; absent or off-contract fields are no floor', () => {
    expect(
      parseAccessFloor({ floorSecure: true, floorAccessPermission: 'restricted', parentUnverifiable: false })
    ).toEqual(RESTRICTED_FLOOR);
    expect(parseAccessFloor({})).toEqual({ floorSecure: null, floorAccessPermission: null, parentUnverifiable: false });
    expect(
      parseAccessFloor({ floorSecure: 'yes', floorAccessPermission: 'unknown', parentUnverifiable: 'true' })
    ).toEqual({ floorSecure: null, floorAccessPermission: null, parentUnverifiable: false });
    expect(parseAccessFloor(null).parentUnverifiable).toBe(false);
    expect(parseAccessFloor({ parentUnverifiable: true, floorSecure: null, floorAccessPermission: null })).toEqual({
      floorSecure: null,
      floorAccessPermission: null,
      parentUnverifiable: true,
    });
  });

  it('counts the other parents', () => {
    expect(otherParentsSuffix(1)).toBe('');
    expect(otherParentsSuffix(2)).toBe(' and 1 other');
    expect(otherParentsSuffix(4)).toBe(' and 3 others');
  });
});

describe('the Access Permission pill under a floor (pure, task 175 round 87)', () => {
  const floor = (floorAccessPermission: IAccessFloor['floorAccessPermission']): IAccessFloor => ({
    floorSecure: null,
    floorAccessPermission,
    parentUnverifiable: false,
  });
  const labels = (r: { options: Array<{ label: string }> }) => r.options.map(o => o.label);

  it('offers only the options at or above the floor, and stays editable', () => {
    expect(resolveAccessPermissionPill(OPTIONS, floor('restricted'), VALUES)).toEqual({
      options: [OPTIONS[2]],
      readOnly: false,
    });
    expect(labels(resolveAccessPermissionPill(OPTIONS, floor('limited'), VALUES))).toEqual(['Limited', 'Restricted']);
    expect(labels(resolveAccessPermissionPill(OPTIONS, floor('standard'), VALUES))).toEqual([
      'Standard',
      'Limited',
      'Restricted',
    ]);
  });

  it('is unchanged without a floor (parentless, or an older BFF)', () => {
    expect(resolveAccessPermissionPill(OPTIONS, null, VALUES)).toEqual({ options: OPTIONS, readOnly: false });
    expect(resolveAccessPermissionPill(OPTIONS, undefined, VALUES)).toEqual({ options: OPTIONS, readOnly: false });
    expect(resolveAccessPermissionPill(OPTIONS, floor(null), VALUES)).toEqual({ options: OPTIONS, readOnly: false });
  });

  it('is read-only when what the record is filed under could not be read (fail closed)', () => {
    expect(
      resolveAccessPermissionPill(
        OPTIONS,
        { floorSecure: null, floorAccessPermission: null, parentUnverifiable: true },
        VALUES
      )
    ).toEqual({ options: OPTIONS, readOnly: true });
  });

  it('is read-only rather than offering a looser value when no option meets the floor', () => {
    expect(resolveAccessPermissionPill(OPTIONS.slice(0, 2), floor('restricted'), VALUES)).toEqual({
      options: OPTIONS.slice(0, 2),
      readOnly: true,
    });
  });

  it('detects a value looser than the floor (the host ignores such a pick)', () => {
    expect(isLooserThanFloor(100000000, 'limited', VALUES)).toBe(true);
    expect(isLooserThanFloor(null, 'limited', VALUES)).toBe(true);
    expect(isLooserThanFloor(100000001, 'limited', VALUES)).toBe(false);
    expect(isLooserThanFloor(100000002, 'limited', VALUES)).toBe(false);
    expect(isLooserThanFloor(100000000, null, VALUES)).toBe(false);
    expect(accessPermissionStateOf(100000002, VALUES)).toBe('restricted');
    expect(accessPermissionStateOf(null, VALUES)).toBe('standard');
  });
});

describe('inherited or set on this record (pure, task 175 round 87)', () => {
  it('Access Permission: above the floor is set here; at a non-Standard floor is inherited; Standard/Standard says nothing', () => {
    expect(accessPermissionOrigin('restricted', 'limited', [PARENT_MATTER])).toEqual({ kind: 'setOnRecord' });
    expect(accessPermissionOrigin('restricted', 'restricted', [PARENT_MATTER])).toEqual({
      kind: 'inherited',
      parent: PARENT_MATTER,
    });
    expect(accessPermissionOrigin('limited', 'limited', [])).toEqual({ kind: 'inherited', parent: null });
    expect(accessPermissionOrigin('standard', 'standard', [PARENT_MATTER])).toBeNull();
    expect(accessPermissionOrigin('limited', 'standard', [PARENT_MATTER])).toEqual({ kind: 'setOnRecord' });
    expect(accessPermissionOrigin('restricted', null, [PARENT_MATTER])).toBeNull();
  });

  it('Secure: with a secure floor inherited, without one set on this record, unknown floor says nothing', () => {
    expect(secureOrigin(true, true, [PARENT_MATTER])).toEqual({ kind: 'inherited', parent: PARENT_MATTER });
    expect(secureOrigin(true, false, [PARENT_MATTER])).toEqual({ kind: 'setOnRecord' });
    expect(secureOrigin(true, null, [PARENT_MATTER])).toBeNull();
    expect(secureOrigin(false, true, [PARENT_MATTER])).toBeNull();
  });

  it('reads as a person reads it', () => {
    expect(describeOrigin({ kind: 'inherited', parent: PARENT_MATTER })).toBe('inherited from Matter Acme v. Beta');
    expect(describeOrigin({ kind: 'inherited', parent: { ...PARENT_MATTER, recordType: 'project', name: null } })).toBe(
      'inherited from the parent project'
    );
    expect(describeOrigin({ kind: 'setOnRecord' })).toBe('set on this record');
    expect(describeOrigin(null)).toBe('');
    expect(
      describeEffectiveAccess({
        accessPermission: 'restricted',
        isSecure: true,
        floor: RESTRICTED_FLOOR,
        parents: [PARENT_MATTER],
      })
    ).toEqual([
      'Access Permission: Restricted (inherited from Matter Acme v. Beta)',
      'Secure (inherited from Matter Acme v. Beta)',
    ]);
    // A stored value below the floor is enforced as the floor.
    expect(
      describeEffectiveAccess({
        accessPermission: 'standard',
        isSecure: false,
        floor: { floorSecure: false, floorAccessPermission: 'limited', parentUnverifiable: false },
        parents: [PARENT_MATTER],
      })
    ).toEqual(['Access Permission: Limited (inherited from Matter Acme v. Beta)']);
    expect(
      describeEffectiveAccess({
        accessPermission: 'standard',
        isSecure: false,
        floor: { floorSecure: false, floorAccessPermission: 'standard', parentUnverifiable: false },
        parents: [PARENT_MATTER],
      })
    ).toEqual(['Access Permission: Standard']);
  });
});
