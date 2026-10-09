/**
 * AccessGrantModal — a child record whose access follows its parent (unified-access-control-r2 task 175, owner round
 * 84: "a child's access always follows its parent, both ways, and is locked while it has a parent").
 *
 * Covers: with `followsParents` the modal hides every write affordance (+ Contact / + Organization / + User, the
 * candidates and their level dropdowns, Add, Suggested Access Grant/Dismiss, Revoke), keeps Current Access and the No
 * Access List read-only, and names the parent in an info bar with a working link; a parentless record is unchanged;
 * the no-permission state stays distinct; the server's 409 `sdap.access.access_follows_parent` on a write is the same
 * locked state, not a raw error; an inherited user share (`inheritedFrom` on `/user-shares`) is a read-only row that
 * still carries the No Access marker; the No Access List names a direct parent only.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type {
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
  followsParentFallbackMessage,
  otherParentsSuffix,
  describeInheritedShare,
} from '../followsParent';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';
const PARENT_MATTER_ID = 'dddddddd-0000-0000-0000-000000000001';
const PARENT_PROJECT_ID = 'dddddddd-0000-0000-0000-000000000002';
const GRANDPARENT_ID = 'dddddddd-0000-0000-0000-000000000009';
const USER_ID = 'cccccccc-0000-0000-0000-000000000001';
const INHERITED_USER_ID = 'cccccccc-0000-0000-0000-000000000002';

const PARENT_MATTER: IFollowsParent = { recordType: 'matter', recordId: PARENT_MATTER_ID, name: 'Acme v. Beta' };

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
  /** Answers a POST write (revoke / share-user / grant); default 200 `{}`. */
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

function posts(props: IAccessGrantModalProps): string[] {
  return (props.authenticatedFetch as unknown as jest.Mock).mock.calls
    .filter(([, init]: [string, RequestInit | undefined]) => init?.method === 'POST')
    .map(([url]: [string]) => url);
}

function currentAccessRow(name: string): HTMLElement {
  const rows = screen
    .getAllByText(name, { exact: false })
    .map(el => el.closest('[data-access-state]') as HTMLElement | null)
    .filter((r): r is HTMLElement => r !== null);
  if (rows.length !== 1) throw new Error(`Expected one Current Access row for "${name}", found ${rows.length}`);
  return rows[0];
}

/** Every write affordance the lock removes. */
function expectNoWriteAffordances(): void {
  expect(screen.queryByRole('button', { name: 'Add contact' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Add organization' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Add user' })).not.toBeInTheDocument();
  expect(screen.queryByText('Add Access Permissions')).not.toBeInTheDocument();
  expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /^Add \(/ })).not.toBeInTheDocument();
  expect(screen.queryByText('Suggested Access')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /^Grant / })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /^Dismiss / })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
}

describe('AccessGrantModal — locked: access follows the parent (task 175)', () => {
  it('hides every write affordance and keeps Current Access and the No Access List read-only', async () => {
    const props = makeProps({
      overrides: { followsParents: [PARENT_MATTER], onOpenParent: jest.fn() },
      shares: [{ systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000000 }],
      suggestions: [PENDING_SUGGESTION],
    });
    renderModal(props);
    await loaded();

    expectNoWriteAffordances();
    // Read-only content stays.
    expect(screen.getByText('Current Access')).toBeInTheDocument();
    expect(screen.getByText('Carla Contact')).toBeInTheDocument();
    expect(screen.getByText(/Uma User/)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'No Access List' })).toBeInTheDocument();
    // The lock is not the no-permission state, nor the Write-required deny.
    expect(screen.queryByText(/You do not have permission/)).not.toBeInTheDocument();
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
  });

  it('names the parent in an info bar whose link opens it', async () => {
    const onOpenParent = jest.fn();
    renderModal(makeProps({ overrides: { followsParents: [PARENT_MATTER], onOpenParent } }));
    await loaded();

    expect(screen.getByText('Access follows the parent', { selector: '.fui-MessageBarTitle' })).toBeInTheDocument();
    const link = screen.getByRole('button', { name: 'Acme v. Beta' });
    const bar = link.closest('.fui-MessageBar') as HTMLElement;
    expect(bar).toHaveTextContent('Access follows the parent matter Acme v. Beta; manage it there.');
    fireEvent.click(link);
    expect(onOpenParent).toHaveBeenCalledWith(PARENT_MATTER);
  });

  it('names the first of several parents and counts the others; an unnamed parent links by its type', async () => {
    const onOpenParent = jest.fn();
    const unnamed: IFollowsParent = { recordType: 'project', recordId: PARENT_PROJECT_ID, name: null };
    renderModal(makeProps({ overrides: { followsParents: [unnamed, PARENT_MATTER, PARENT_MATTER], onOpenParent } }));
    await loaded();

    const link = screen.getByRole('button', { name: 'project' });
    expect(link.closest('.fui-MessageBar')).toHaveTextContent(
      'Access follows the parent project and 2 others; manage it there.'
    );
    fireEvent.click(link);
    expect(onOpenParent).toHaveBeenCalledWith(unnamed);
  });

  it('without onOpenParent names the parent as text, no link', async () => {
    renderModal(makeProps({ overrides: { followsParents: [PARENT_MATTER] } }));
    await loaded();

    expect(screen.queryByRole('button', { name: 'Acme v. Beta' })).not.toBeInTheDocument();
    expect(screen.getByText('Acme v. Beta').tagName).toBe('STRONG');
  });

  it('replaces the Access Permission bar (its "+ User" advice does not apply on a locked record)', async () => {
    renderModal(makeProps({ overrides: { followsParents: [PARENT_MATTER], accessPermissionState: 'restricted' } }));
    await loaded();

    expect(screen.queryByText('Restricted Access')).not.toBeInTheDocument();
    expect(screen.getByText('Access follows the parent', { selector: '.fui-MessageBarTitle' })).toBeInTheDocument();
  });

  it('Save closes without writing anything', async () => {
    const props = makeProps({ overrides: { followsParents: [PARENT_MATTER] } });
    renderModal(props);
    await loaded();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(props.onClose).toHaveBeenCalled());
    expect(posts(props)).toEqual([]);
  });

  it('the no-permission state stays distinct from the lock', async () => {
    renderModal(makeProps({ overrides: { followsParents: [PARENT_MATTER], canGrantAccess: false } }));

    expect(
      await screen.findByText('You do not have permission to grant or revoke access for this record.')
    ).toBeInTheDocument();
    expect(screen.queryByText('Access follows the parent')).not.toBeInTheDocument();
  });
});

describe('AccessGrantModal — a parentless record is unchanged (task 175)', () => {
  it.each([
    ['followsParents omitted', undefined],
    ['followsParents empty', [] as IFollowsParent[]],
  ])('%s: every write affordance is offered and no parent bar is shown', async (_label, followsParents) => {
    renderModal(
      makeProps({
        overrides: { followsParents },
        shares: [{ systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000000 }],
        suggestions: [PENDING_SUGGESTION],
      })
    );
    await loaded();

    expect(screen.queryByText('Access follows the parent')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add contact' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add organization' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add user' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Select Cody Candidate' })).toBeInTheDocument();
    expect(screen.getAllByRole('combobox').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Grant Pat Paralegal' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Dismiss Pat Paralegal' })).toBeInTheDocument();
    // The contact grant and the direct user share are both revocable.
    expect(screen.getAllByRole('button', { name: 'Revoke' })).toHaveLength(2);
  });
});

describe('AccessGrantModal — the 409 access_follows_parent refusal (task 175)', () => {
  const refusal = (body: Record<string, unknown>) => () =>
    json({ status: 409, reasonCode: 'sdap.access.access_follows_parent', ...body }, 409);

  it('a refused Revoke becomes the locked state naming the parent, not an error', async () => {
    const onOpenParent = jest.fn();
    const props = makeProps({
      overrides: { onOpenParent },
      write: refusal({
        detail: 'Access to this work assignment follows the matter Acme v. Beta. Change it there.',
        parentRecordType: 'matter',
        parentRecordId: PARENT_MATTER_ID,
        parentName: 'Acme v. Beta',
      }),
    });
    renderModal(props);
    await loaded();

    fireEvent.click(within(currentAccessRow('Carla Contact')).getByRole('button', { name: 'Revoke' }));
    const dialog = await screen.findByRole('alertdialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke' }));

    const link = await screen.findByRole('button', { name: 'Acme v. Beta' });
    expect(link.closest('.fui-MessageBar')).toHaveTextContent(
      'Access follows the parent matter Acme v. Beta; manage it there.'
    );
    fireEvent.click(link);
    expect(onOpenParent).toHaveBeenCalledWith(PARENT_MATTER);
    expectNoWriteAffordances();
    expect(screen.queryByText(/Failed to revoke/)).not.toBeInTheDocument();
    expect(screen.queryByText('Write access required')).not.toBeInTheDocument();
  });

  it('without a parent in the body it shows the server sentence', async () => {
    const props = makeProps({
      write: refusal({ detail: 'Access to this project follows the matter it is filed under. Change it there.' }),
    });
    renderModal(props);
    await loaded();

    fireEvent.click(within(currentAccessRow('Carla Contact')).getByRole('button', { name: 'Revoke' }));
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Revoke' }));

    expect(
      await screen.findByText('Access to this project follows the matter it is filed under. Change it there.')
    ).toBeInTheDocument();
    expectNoWriteAffordances();
  });

  it('without a detail it shows the designed sentence for the parent type', async () => {
    const props = makeProps({ write: refusal({ parentRecordType: 'project' }) });
    renderModal(props);
    await loaded();

    fireEvent.click(within(currentAccessRow('Carla Contact')).getByRole('button', { name: 'Revoke' }));
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Revoke' }));

    expect(await screen.findByText(followsParentFallbackMessage('project'))).toBeInTheDocument();
    expect(followsParentFallbackMessage('project')).toBe(
      'Access to this record follows the project it is filed under. Change it there.'
    );
  });

  it('a refused Add stops the batch and shows the lock with no error notice', async () => {
    const props = makeProps({
      write: refusal({
        detail: 'Access follows the parent.',
        parentRecordType: 'matter',
        parentRecordId: PARENT_MATTER_ID,
        parentName: 'Acme v. Beta',
      }),
    });
    renderModal(props);
    await loaded();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Cody Candidate' }));
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: 'View Only' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add (1)' }));

    expect(await screen.findByText('Acme v. Beta')).toBeInTheDocument();
    expectNoWriteAffordances();
    expect(screen.queryByText('Error')).not.toBeInTheDocument();
    expect(posts(props)).toHaveLength(1);
  });

  it('a 409 with another reason code is not the lock', async () => {
    const props = makeProps({ write: () => json({ reasonCode: 'sdap.access.grant.would_lower_existing' }, 409) });
    renderModal(props);
    await loaded();

    fireEvent.click(within(currentAccessRow('Carla Contact')).getByRole('button', { name: 'Revoke' }));
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Revoke' }));

    expect(await screen.findByText(/Failed to revoke access for Carla Contact/)).toBeInTheDocument();
    expect(screen.queryByText('Access follows the parent')).not.toBeInTheDocument();
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
  const DIRECT: IShareFixture = { systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000000 };

  it('is a read-only row, labelled with its parent, even on a parentless record', async () => {
    renderModal(makeProps({ shares: [INHERITED, DIRECT] }));
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
      makeProps({
        shares: [INHERITED],
        noAccessEntries: [entry({})],
        overrides: { followsParents: [PARENT_MATTER] },
      })
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
    renderModal(makeProps({ shares: [{ ...DIRECT, inheritedFrom: null }] }));
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
    // A record further up (not a direct parent) is labelled by its type alone; so is an unnamed direct parent.
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
    // This record itself is never given a parent's name.
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

  it('counts the other parents', () => {
    expect(otherParentsSuffix(1)).toBe('');
    expect(otherParentsSuffix(2)).toBe(' and 1 other');
    expect(otherParentsSuffix(4)).toBe(' and 3 others');
  });
});
