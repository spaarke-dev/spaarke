/**
 * "Email members" recipients (unified-access-control-r2 task 196, owner decision 2026-11-12).
 *
 * The recipients are ONE list, de-duplicated by email (case-insensitive), of the record's role contacts, the contacts
 * holding an active grant and the internal users it is shared with; the No Access list and the record's policy apply as
 * they do to Current Access. A failed source never breaks the button and never reads as "no members": the result carries
 * the other sources plus a notice; `empty` only when every source and check succeeded with nobody; `failed` only when
 * every source failed.
 */

import {
  EMAIL_MEMBERS_EMPTY_MESSAGE,
  resolveEmailMembersRecipients,
  type IEmailMembersRootSources,
  type IEmailMembersSources,
} from '../emailMembers';
import type { IAccessGrantCandidate, IAccessGrantRecord, IRecordNoAccessEntry } from '../types';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';
const ROLE_ID = 'aaaaaaaa-0000-0000-0000-000000000001';
const GRANTEE_ID = 'aaaaaaaa-0000-0000-0000-000000000002';
const EXPIRED_ID = 'aaaaaaaa-0000-0000-0000-000000000003';
const WALLED_ID = 'aaaaaaaa-0000-0000-0000-000000000004';
const USER_ID = 'cccccccc-0000-0000-0000-000000000001';
const WALLED_USER_ID = 'cccccccc-0000-0000-0000-000000000002';
const ORG_ID = 'bbbbbbbb-0000-0000-0000-000000000001';

function json(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

function noAccessBody(entries: IRecordNoAccessEntry[], extra: Record<string, unknown> = {}) {
  return {
    recordType: 'sprk_project',
    recordId: RECORD_ID,
    secure: 'doesNotApply',
    noAccess: entries.length > 0 ? 'applies' : 'doesNotApply',
    entriesState: 'complete',
    entries,
    ...extra,
  };
}

function wall(subjectKind: 'contact' | 'systemuser' | 'organization', subjectId: string): IRecordNoAccessEntry {
  return {
    entryId: `wall-${subjectId}`,
    name: 'Wall',
    subjectKind,
    subjectId,
    subjectName: 'Walled',
    objectKind: 'record',
    objectOrganizationId: null,
    objectOrganizationName: null,
    coveredRecordType: 'sprk_project',
    coveredRecordId: RECORD_ID,
    viaSecureParent: false,
    alsoViaSecureParent: false,
    malformed: false,
    inForce: true,
    notInForceReason: null,
    modifiedById: null,
    modifiedByName: null,
    modifiedOn: null,
  };
}

const ROLE: IAccessGrantCandidate = {
  contactId: ROLE_ID,
  fullName: 'Rita Role',
  email: 'rita@example.com',
  role: 'Assigned Attorney 1',
};

function grant(contactId: string, overrides: Partial<IAccessGrantRecord> = {}): IAccessGrantRecord {
  return { accessRecordId: `grant-${contactId}`, contactId, fullName: 'Grantee', accessLevel: 100000001, ...overrides };
}

interface IRootSetup {
  roles?: () => Promise<IAccessGrantCandidate[]>;
  grants?: () => Promise<IAccessGrantRecord[] | { grants: IAccessGrantRecord[]; truncated: boolean }>;
  shares?: () => Response;
  noAccess?: () => Response;
  contactEmails?: Record<string, string> | 'fail';
  userEmails?: Record<string, string> | 'fail';
  memberships?: IRootSources['readContactOrganizationMemberships'];
  overrides?: Partial<IEmailMembersRootSources>;
}
type IRootSources = IEmailMembersRootSources;

/** An email reader as the host's: every row it can read is in the map ('' = the row has no email); an id it was not
 * given is a row the caller cannot read, so it is left out of the map. */
function emailReader(emails: Record<string, string> | 'fail' | undefined) {
  return jest.fn(async (ids: string[]) => {
    if (emails === 'fail') throw new Error('read failed');
    const map = new Map<string, string>();
    for (const id of ids) if (emails && id in emails) map.set(id, emails[id]);
    return map;
  });
}

function sources(setup: IRootSetup = {}): IEmailMembersSources {
  const authenticatedFetch = jest.fn(async (url: string) => {
    if (url.includes('/user-shares')) {
      return setup.shares ? setup.shares() : json({ shares: [] });
    }
    if (url.includes('/no-access')) return setup.noAccess ? setup.noAccess() : json(noAccessBody([]));
    throw new Error(`unexpected ${url}`);
  });
  return {
    readRoleContacts: setup.roles ?? (async () => [ROLE]),
    readContactEmails: emailReader(setup.contactEmails ?? { [GRANTEE_ID]: 'gary@example.com' }),
    root: {
      recordType: 'project',
      recordId: RECORD_ID,
      authenticatedFetch: authenticatedFetch as unknown as IRootSources['authenticatedFetch'],
      readGrants: setup.grants ?? (async () => [grant(GRANTEE_ID)]),
      readUserEmails: emailReader(setup.userEmails ?? { [USER_ID]: 'uma@spaarke.example' }),
      readContactOrganizationMemberships: setup.memberships,
      accessPermissionState: 'standard',
      isSecureRecord: false,
      ...setup.overrides,
    },
  };
}

const SHARE = { systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000000 };

describe('resolveEmailMembersRecipients (task 196)', () => {
  it('is ONE list of role contacts, active grantees and shared users', async () => {
    const result = await resolveEmailMembersRecipients(sources({ shares: () => json({ shares: [SHARE] }) }));
    expect(result).toEqual({
      kind: 'recipients',
      recipients: ['rita@example.com', 'gary@example.com', 'uma@spaarke.example'],
      notice: null,
    });
  });

  it('de-duplicates by email, case-insensitively (first spelling kept)', async () => {
    const result = await resolveEmailMembersRecipients(
      sources({
        grants: async () => [grant(GRANTEE_ID), grant(EXPIRED_ID)],
        contactEmails: { [GRANTEE_ID]: 'RITA@example.com', [EXPIRED_ID]: ' rita@EXAMPLE.com ' },
        shares: () => json({ shares: [SHARE] }),
        userEmails: { [USER_ID]: 'Rita@Example.com' },
      })
    );
    expect(result).toEqual({ kind: 'recipients', recipients: ['rita@example.com'], notice: null });
  });

  it('leaves out an expired grant', async () => {
    const result = await resolveEmailMembersRecipients(
      sources({
        grants: async () => [grant(GRANTEE_ID), grant(EXPIRED_ID, { isExpired: true })],
        contactEmails: { [GRANTEE_ID]: 'gary@example.com', [EXPIRED_ID]: 'expired@example.com' },
      })
    );
    expect(result.kind === 'recipients' && result.recipients).toEqual(['rita@example.com', 'gary@example.com']);
  });

  it('leaves out anyone on the No Access list: a contact (role or grant), a user, an organization member', async () => {
    const memberships = jest.fn(async () => [{ contactId: GRANTEE_ID, organizationId: ORG_ID }]);
    const result = await resolveEmailMembersRecipients(
      sources({
        roles: async () => [
          ROLE,
          { contactId: WALLED_ID, fullName: 'Walt Walled', email: 'walt@example.com', role: 'R' },
        ],
        grants: async () => [grant(GRANTEE_ID)],
        shares: () =>
          json({ shares: [SHARE, { systemUserId: WALLED_USER_ID, fullName: 'Wanda Walled', accessLevel: 100000000 }] }),
        userEmails: { [USER_ID]: 'uma@spaarke.example', [WALLED_USER_ID]: 'wanda@spaarke.example' },
        noAccess: () =>
          json(
            noAccessBody([wall('contact', WALLED_ID), wall('systemuser', WALLED_USER_ID), wall('organization', ORG_ID)])
          ),
        memberships,
      })
    );
    expect(result).toEqual({
      kind: 'recipients',
      recipients: ['rita@example.com', 'uma@spaarke.example'],
      notice: null,
    });
    expect(memberships).toHaveBeenCalledWith(expect.arrayContaining([ROLE_ID, GRANTEE_ID]), [ORG_ID]);
  });

  it('leaves out what Current Access marks "No effect": grants on a Restricted record, an external user share', async () => {
    const result = await resolveEmailMembersRecipients(
      sources({
        shares: () =>
          json({
            shares: [
              SHARE,
              { systemUserId: WALLED_USER_ID, fullName: 'Ed External', accessLevel: 1, externalNoAccess: true },
            ],
          }),
        userEmails: { [USER_ID]: 'uma@spaarke.example', [WALLED_USER_ID]: 'ed@outside.example' },
        overrides: { accessPermissionState: 'restricted' },
      })
    );
    // The role contact stays (the owner's list names role contacts); the grant and the external share do not.
    expect(result).toEqual({
      kind: 'recipients',
      recipients: ['rita@example.com', 'uma@spaarke.example'],
      notice: null,
    });
  });

  it('says so when an organization-wide grant is left out (it names no person)', async () => {
    const result = await resolveEmailMembersRecipients(
      sources({
        grants: async () => [
          {
            accessRecordId: 'org-grant',
            contactId: ORG_ID,
            fullName: 'All contacts at Acme',
            accessLevel: 1,
            provenance: 'organization',
          },
        ],
      })
    );
    expect(result.kind).toBe('recipients');
    expect(result.kind === 'recipients' && result.notice).toMatch(/organization-wide grant are not added/);
  });

  describe('fail visible, never silently narrower', () => {
    it('a failed grants read keeps the other sources and says so', async () => {
      const result = await resolveEmailMembersRecipients(
        sources({
          grants: async () => {
            throw new Error('403');
          },
          shares: () => json({ shares: [SHARE] }),
        })
      );
      expect(result.kind).toBe('recipients');
      if (result.kind !== 'recipients') return;
      expect(result.recipients).toEqual(['rita@example.com', 'uma@spaarke.example']);
      expect(result.notice).toBe(
        'Some people may be missing from the recipients: the people granted access could not be read. Check the list before sending.'
      );
    });

    it('a share read the server refuses (a returned 403) is a failure, not "no shares"', async () => {
      const result = await resolveEmailMembersRecipients(sources({ shares: () => json({ title: 'Forbidden' }, 403) }));
      expect(result.kind === 'recipients' && result.notice).toMatch(
        /the users the record is shared with could not be read/
      );
    });

    it('a failed email read is stated', async () => {
      const result = await resolveEmailMembersRecipients(sources({ contactEmails: 'fail' }));
      expect(result).toEqual({
        kind: 'recipients',
        recipients: ['rita@example.com'],
        notice: expect.stringMatching(/some email addresses could not be read/),
      });
    });

    it('a No Access list that could not be read (or is not shown to the caller) is stated', async () => {
      for (const noAccess of [() => json({}, 404), () => json(noAccessBody([], { entriesState: 'notShown' }))]) {
        const result = await resolveEmailMembersRecipients(sources({ noAccess }));
        // Worded as the real risk: someone listed may be walled off (people were kept, not dropped).
        expect(result.kind === 'recipients' && result.notice).toBe(
          'Someone listed may be walled off from this record: the No Access list could not be checked. Check the list before sending.'
        );
      }
    });

    it('an organization wall whose memberships could not be read is stated', async () => {
      const result = await resolveEmailMembersRecipients(
        sources({
          noAccess: () => json(noAccessBody([wall('organization', ORG_ID)])),
          memberships: async () => {
            throw new Error('read failed');
          },
        })
      );
      expect(result.kind === 'recipients' && result.notice).toMatch(/walled-off organization could not be checked/);
    });

    it('a capped grants read is stated', async () => {
      const result = await resolveEmailMembersRecipients(
        sources({ grants: async () => ({ grants: [grant(GRANTEE_ID)], truncated: true }) })
      );
      expect(result.kind === 'recipients' && result.notice).toMatch(/only the first 1 grants could be read/);
    });

    it('with nobody found but a source failed, the dialog opens with the notice (never the empty state)', async () => {
      const result = await resolveEmailMembersRecipients(
        sources({
          roles: async () => [],
          grants: async () => {
            throw new Error('403');
          },
        })
      );
      expect(result).toEqual({ kind: 'recipients', recipients: [], notice: expect.stringMatching(/granted access/) });
    });

    it('every source failing is an error, not the empty state', async () => {
      const fail = async () => {
        throw new Error('down');
      };
      const result = await resolveEmailMembersRecipients(
        sources({ roles: fail, grants: fail, shares: () => json({}, 500) })
      );
      expect(result).toEqual({ kind: 'failed' });
    });
  });

  it('is the empty state only when every source and check succeeded and nobody has an email', async () => {
    const result = await resolveEmailMembersRecipients(
      sources({
        roles: async () => [{ ...ROLE, email: undefined }],
        grants: async () => [grant(GRANTEE_ID)],
        // Both contacts were read and have no email ('' — not "could not be read").
        contactEmails: { [ROLE_ID]: '', [GRANTEE_ID]: '' },
      })
    );
    expect(result).toEqual({ kind: 'empty' });
    expect(EMAIL_MEMBERS_EMPTY_MESSAGE).toBe(
      'This record has no members with an email address yet. Grant access or assign a role first.'
    );
  });

  it('on a host that is not a grant root, only the role contacts apply, with no notice', async () => {
    expect(await resolveEmailMembersRecipients({ readRoleContacts: async () => [ROLE] })).toEqual({
      kind: 'recipients',
      recipients: ['rita@example.com'],
      notice: null,
    });
    expect(await resolveEmailMembersRecipients({ readRoleContacts: async () => [] })).toEqual({ kind: 'empty' });
  });

  describe('review findings (task 196)', () => {
    it('a granted contact whose row the email read leaves out (privileges) is reported, not taken for "no email"', async () => {
      const result = await resolveEmailMembersRecipients(sources({ contactEmails: {} }));
      expect(result).toEqual({
        kind: 'recipients',
        recipients: ['rita@example.com'],
        notice:
          'Some people may be missing from the recipients: some email addresses could not be read. Check the list before sending.',
      });
    });

    it('a role contact that came without an email is read again: no email is silent, an unreadable row is reported', async () => {
      const roles = async () => [{ ...ROLE, email: undefined }];
      const noEmail = await resolveEmailMembersRecipients(
        sources({ roles, contactEmails: { [ROLE_ID]: '', [GRANTEE_ID]: 'gary@example.com' } })
      );
      expect(noEmail).toEqual({ kind: 'recipients', recipients: ['gary@example.com'], notice: null });

      const unreadable = await resolveEmailMembersRecipients(
        sources({ roles, contactEmails: { [GRANTEE_ID]: 'gary@example.com' } })
      );
      expect(unreadable.kind === 'recipients' && unreadable.notice).toMatch(/some email addresses could not be read/);
    });

    it('a caller who may not manage access is told they cannot see who else has access, not that a read failed', async () => {
      const forbidden = async () => {
        throw new Error('403');
      };
      const result = await resolveEmailMembersRecipients(
        sources({
          grants: forbidden,
          shares: () => json({ title: 'Forbidden' }, 403),
          overrides: { callerManagesAccess: false },
        })
      );
      expect(result).toEqual({
        kind: 'recipients',
        recipients: ['rita@example.com'],
        notice:
          'Some people may be missing from the recipients: you cannot see who else has access to this record (only people who manage its access can). Check the list before sending.',
      });
    });

    it('both kinds of note are worded apart in one notice', async () => {
      const result = await resolveEmailMembersRecipients(
        sources({
          grants: async () => {
            throw new Error('down');
          },
          noAccess: () => json({}, 500),
        })
      );
      expect(result.kind === 'recipients' && result.notice).toBe(
        'Some people may be missing from the recipients: the people granted access could not be read. ' +
          'Someone listed may be walled off from this record: the No Access list could not be checked. ' +
          'Check the list before sending.'
      );
    });

    it("a walled-off contact's address is kept off the list when it arrives through an unwalled share", async () => {
      const result = await resolveEmailMembersRecipients(
        sources({
          grants: async () => [grant(GRANTEE_ID), grant(WALLED_ID)],
          contactEmails: { [GRANTEE_ID]: 'gary@example.com', [WALLED_ID]: 'walt@example.com' },
          shares: () => json({ shares: [SHARE] }),
          // The walled contact's paired user account: same person, same address, not itself on the list.
          userEmails: { [USER_ID]: 'Walt@Example.com' },
          noAccess: () => json(noAccessBody([wall('contact', WALLED_ID)])),
        })
      );
      expect(result).toEqual({
        kind: 'recipients',
        recipients: ['rita@example.com', 'gary@example.com'],
        notice: null,
      });
    });

    it("a walled-off person's address that could not be read is stated (someone listed may be walled off)", async () => {
      const result = await resolveEmailMembersRecipients(
        sources({
          grants: async () => [grant(GRANTEE_ID), grant(WALLED_ID)],
          contactEmails: { [GRANTEE_ID]: 'gary@example.com' },
          noAccess: () => json(noAccessBody([wall('contact', WALLED_ID)])),
        })
      );
      expect(result).toEqual({
        kind: 'recipients',
        recipients: ['rita@example.com', 'gary@example.com'],
        notice:
          'Someone listed may be walled off from this record: some email addresses of people on the No Access list could not be read. Check the list before sending.',
      });
    });
  });

  it('never rejects, even when a reader throws synchronously', async () => {
    const result = await resolveEmailMembersRecipients({
      readRoleContacts: () => {
        throw new Error('sync');
      },
    });
    expect(result).toEqual({ kind: 'failed' });
  });
});
