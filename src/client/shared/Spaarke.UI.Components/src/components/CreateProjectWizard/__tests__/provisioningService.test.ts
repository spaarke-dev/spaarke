/**
 * provisioningService.test.ts — the provision-project client against the task 061 contract
 * (task 068, spec FR-31).
 *
 * Two things are pinned here, and they are different in kind:
 *
 *   1. The REQUEST/RESPONSE mirror. The server shape changed twice (task 021 removed the umbrella
 *      BU and the account; task 061 added the share plane). A client mirror that silently lags is
 *      how the wizard ends up sending a field nothing reads.
 *
 *   2. The FAILURE CLASSIFICATION. The endpoint returns its environment-setup refusals as HTTP
 *      **500**, not 4xx — so status code cannot distinguish "this environment has no secure
 *      topology" from "something broke". Only the `reasonCode` ProblemDetails extension can, and
 *      the whole point of reading it is that the user must never see the server's operator-facing
 *      `detail` prose. Both halves are asserted: the right kind comes back, AND the raw detail does
 *      not leak into the message shown to the user.
 */
import {
  provisionSecureProject,
  classifyProvisioningFailure,
  describeSkippedPrincipal,
  UNNAMED_PERSON,
  type IProvisionProjectResponse,
} from '../provisioningService';

const BFF = 'https://bff.example.test';
const PROJECT_ID = '11111111-1111-1111-1111-111111111111';

/** A full task-061-shaped success body. */
const successBody: IProvisionProjectResponse = {
  businessUnitId: '22222222-2222-2222-2222-222222222222',
  businessUnitName: 'Secure Project',
  ownerTeamId: '33333333-3333-3333-3333-333333333333',
  ownerTeamName: 'Secure Project',
  speContainerId: 'b!container',
  sharedToCreatorSystemUserId: '44444444-4444-4444-4444-444444444444',
  additionalPrincipalsShared: 0,
};

const okResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body }) as unknown as Response;

const problemResponse = (status: number, problem: Record<string, unknown>) =>
  ({ ok: false, status, json: async () => problem }) as unknown as Response;

/** The operator-facing prose the endpoint actually returns for a missing BU. Must never be shown. */
const SERVER_DETAIL =
  "No business unit named 'Secure Project' exists in this environment. The canonical Secure Project " +
  'business unit is created during environment setup; provisioning will not create one, and will not ' +
  'fall back to another business unit.';

describe('provisionSecureProject — request shape (task 061 contract)', () => {
  let consoleInfo: jest.SpyInstance;
  let consoleError: jest.SpyInstance;

  beforeEach(() => {
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => {});
    consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleInfo.mockRestore();
    consoleError.mockRestore();
  });

  it('POSTs to the external-access provisioning route with the request as JSON', async () => {
    const authFetch = jest.fn().mockResolvedValue(okResponse(successBody));

    await provisionSecureProject({ projectId: PROJECT_ID, projectRef: 'P-1' }, authFetch as never, BFF);

    expect(authFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authFetch.mock.calls[0];
    expect(url).toBe(`${BFF}/api/v1/external-access/provision-project`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ projectId: PROJECT_ID, projectRef: 'P-1' });
  });

  it('carries sharePrincipalIds when the caller supplies them', async () => {
    // Optional on the server (ProvisionProjectRequest.SharePrincipalIds). The wizard sends none,
    // but the mirror must be able to — otherwise a caller that DOES collect colleagues cannot.
    const authFetch = jest.fn().mockResolvedValue(okResponse({ ...successBody, additionalPrincipalsShared: 2 }));
    const colleagues = ['55555555-5555-5555-5555-555555555555', '66666666-6666-6666-6666-666666666666'];

    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, sharePrincipalIds: colleagues },
      authFetch as never,
      BFF
    );

    expect(JSON.parse(authFetch.mock.calls[0][1].body).sharePrincipalIds).toEqual(colleagues);
    expect(result.data!.additionalPrincipalsShared).toBe(2);
  });

  it('returns the share plane the server reports on success', async () => {
    const authFetch = jest.fn().mockResolvedValue(okResponse(successBody));

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(true);
    expect(result.failureKind).toBeUndefined();
    expect(result.data).toEqual(successBody);
    // The creator share is the thing that makes the project reachable at all — a success that did
    // not report one would mean the client had stopped mirroring the field that matters most.
    expect(result.data!.sharedToCreatorSystemUserId).toBe(successBody.sharedToCreatorSystemUserId);
  });

  it('accepts a childrenOnly success (task 148) — only the related records of a project secured earlier were completed', async () => {
    const body = { ...successBody, sharedToCreatorSystemUserId: '', resumed: true, childrenOnly: true };
    const authFetch = jest.fn().mockResolvedValue(okResponse(body));

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(true);
    expect(result.data!.childrenOnly).toBe(true);
  });

  it('still refuses a 2xx with no creator share that is NOT childrenOnly', async () => {
    const authFetch = jest.fn().mockResolvedValue(okResponse({ ...successBody, sharedToCreatorSystemUserId: '' }));

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(false);
    expect(result.failureKind).toBe('error');
  });
});

describe('provisionSecureProject — failure classification', () => {
  let consoleError: jest.SpyInstance;

  beforeEach(() => {
    consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => consoleError.mockRestore());

  it('classifies a missing Secure Record business unit as an unconfigured environment — despite the 500', async () => {
    const authFetch = jest.fn().mockResolvedValue(
      problemResponse(500, {
        title: 'Internal Server Error',
        detail: SERVER_DETAIL,
        reasonCode: 'sdap.provision.secure_bu_not_found',
      })
    );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(false);
    expect(result.failureKind).toBe('environment-not-configured');
    expect(result.reasonCode).toBe('sdap.provision.secure_bu_not_found');
    expect(result.retryable).toBe(false);
  });

  it('carries `retryable` through for a state the same caller can finish (task 133)', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue(
        problemResponse(500, { detail: 'operator text', reasonCode: 'sdap.provision.container_creation_failed' })
      );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.failureKind).toBe('storage-incomplete');
    expect(result.retryable).toBe(true);
  });

  // Task 133 verifier round 2: an unverified owner move's copy follows the server's `creatorShareConfirmed`. Only a
  // share read back on the record may be asserted; an unconfirmed (or unreported) one names the administrator, because
  // if the move landed and the share did not take, the caller's own retry is refused at the Write gate.
  it.each([
    [true, /read back, so you can open it/i, /administrator/i],
    [false, /administrator needs to finish/i, /can open it either way/i],
    [undefined, /administrator needs to finish/i, /can open it either way/i],
  ])(
    'reads creatorShareConfirmed=%s from the problem body for owner_assignment_unverified',
    async (confirmed, says, neverSays) => {
      const authFetch = jest.fn().mockResolvedValue(
        problemResponse(500, {
          detail: 'operator text',
          reasonCode: 'sdap.provision.owner_assignment_unverified',
          ...(confirmed === undefined ? {} : { creatorShareConfirmed: confirmed }),
        })
      );

      const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

      expect(result.failureKind).toBe('interrupted');
      expect(result.retryable).toBe(true);
      expect(result.errorMessage).toMatch(says);
      expect(result.errorMessage).not.toMatch(neverSays);
      expect(result.errorMessage).not.toMatch(/try (securing )?(it )?again|retry/i);
    }
  );

  // Task 133 b2 (verifier finding): `resume_creator_unavailable` follows the server's `creatorState`. `unreadable` is a
  // failed READ — the server's detail and guide §7a tell the same caller they may call again — so it is retryable with
  // copy that says only that the creator could not be looked up. Every other state is the administrator's.
  //
  // Task 133 r1 (verifier finding 10): `column-missing` — the creator column is not in this environment (a deterministic
  // 400) — is NOT the retryable `unreadable`: a "Try securing again" would fail until an administrator applies the schema.
  //
  // Owner round 14 item 3 (task 133 c1-r4): `refused` — Dataverse refused the read (a 401/403: the service's sign-in or
  // Read privilege) — is deterministic too: not retryable, and the copy names the service's permission to look the
  // creator up, as the server's detail and guide §7a do.
  it.each([
    ['unreadable', 'not-started', true, /could not be looked up/i],
    ['column-missing', 'needs-administrator', false, /not yet set up to record who created/i],
    ['refused', 'needs-administrator', false, /check the service's permission to look them up/i],
    ['disabled', 'needs-administrator', false, /administrator needs to finish/i],
    ['application-user', 'needs-administrator', false, /administrator needs to finish/i],
    ['absent', 'needs-administrator', false, /administrator needs to finish/i],
    [undefined, 'needs-administrator', false, /administrator needs to finish/i],
  ])(
    'reads creatorState=%s from the problem body for resume_creator_unavailable',
    async (creatorState, kind, retryable, says) => {
      const authFetch = jest.fn().mockResolvedValue(
        problemResponse(['unreadable', 'column-missing', 'refused'].includes(creatorState ?? '') ? 500 : 409, {
          detail: 'operator text',
          reasonCode: 'sdap.provision.resume_creator_unavailable',
          ...(creatorState === undefined ? {} : { creatorState }),
        })
      );

      const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

      expect(result.failureKind).toBe(kind);
      expect(result.retryable).toBe(retryable);
      expect(result.errorMessage).toMatch(says);
      expect(result.errorMessage).not.toMatch(/try (securing )?(it )?again|retry/i);
      if (retryable) {
        expect(result.errorMessage).not.toMatch(/administrator/i);
      }
    }
  );

  // Task 133 r2 (verifier round 5 finding 8): the server's `containerKept`. A project that kept its own document container
  // reads as provisioned to every later call once the secure owner holds it, so "an administrator needs to finish securing
  // it" names a recovery that does not exist; the copy names the one that does — a Manage Access share. Without the flag
  // (or with it false) the copy is unchanged, and a confirmed share keeps its "you can open it either way" copy.
  it.each([
    ['sdap.provision.creator_share_failed_resumable', { containerKept: true }, 'needs-administrator', false, true],
    ['sdap.provision.creator_share_failed_resumable', { containerKept: false }, 'needs-administrator', false, false],
    ['sdap.provision.creator_share_failed_resumable', {}, 'needs-administrator', false, false],
    ['sdap.provision.owner_assignment_unverified', { containerKept: true }, 'interrupted', true, true],
    [
      'sdap.provision.owner_assignment_unverified',
      { containerKept: true, creatorShareConfirmed: false },
      'interrupted',
      true,
      true,
    ],
    ['sdap.provision.owner_assignment_unverified', { containerKept: false }, 'interrupted', true, false],
  ])(
    'reads containerKept from the problem body: %s %o',
    async (reasonCode, extensions, kind, retryable, namesManageAccess) => {
      const authFetch = jest
        .fn()
        .mockResolvedValue(problemResponse(500, { detail: 'operator text', reasonCode, ...extensions }));

      const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

      expect(result.failureKind).toBe(kind);
      expect(result.retryable).toBe(retryable);
      expect(result.errorMessage).not.toMatch(/try (securing )?(it )?again|retry/i);
      if (namesManageAccess) {
        expect(result.errorMessage).toMatch(/share it with you through Manage Access/i);
        expect(result.errorMessage).not.toMatch(/finish securing/i);
      } else {
        expect(result.errorMessage).toMatch(/administrator needs to finish securing/i);
        expect(result.errorMessage).not.toMatch(/Manage Access/i);
      }
    }
  );

  // Task 133 c1 (owner round 10 item 4): `cascade_children_unreadable` follows the server's `cascadeChildState`.
  // `unreadable` (or absent) is a failed read — the same caller may call again; `refused` is Dataverse refusing the read
  // (or answering it incompletely), deterministic — a "Try securing again" would fail every time. Since round c1-r2 a
  // 401/403 (the service's sign-in or Read privilege refused) is `refused` too, so the refused copy names the service's
  // permission to read those records beside the records themselves, as the server's detail and guide §7a do (c1-r3).
  it.each([
    ['unreadable', true],
    [undefined, true],
    ['refused', false],
  ])('reads cascadeChildState=%s from the problem body for cascade_children_unreadable', async (state, retryable) => {
    const authFetch = jest.fn().mockResolvedValue(
      problemResponse(500, {
        detail: 'operator text',
        reasonCode: 'sdap.provision.cascade_children_unreadable',
        childTable: 'sharepointdocumentlocation',
        ...(state === undefined ? {} : { cascadeChildState: state }),
      })
    );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.failureKind).toBe('not-started');
    expect(result.retryable).toBe(retryable);
    expect(result.errorMessage).toMatch(/Nothing about the project changed/);
    expect(result.errorMessage).not.toMatch(/try (securing )?(it )?again|retry/i);
    if (retryable) {
      expect(result.errorMessage).not.toMatch(/administrator/i);
    } else {
      expect(result.errorMessage).toMatch(/administrator needs to look at those records/i);
      expect(result.errorMessage).toMatch(/the service's permission to read them/i);
    }
  });

  // Owner round 14 item 3 (task 133 c1-r4): `container_ownership_unreadable` follows the server's
  // `containerOwnershipState` exactly as `cascade_children_unreadable` follows `cascadeChildState`. `unreadable` (or
  // absent) is a failed read — the same caller may call again; `refused` is Dataverse refusing the read (a 401/403: the
  // service's sign-in or Read privilege), deterministic — a "Try securing again" would fail every time.
  it.each([
    ['unreadable', true],
    [undefined, true],
    ['refused', false],
  ])(
    'reads containerOwnershipState=%s from the problem body for container_ownership_unreadable',
    async (state, retryable) => {
      const authFetch = jest.fn().mockResolvedValue(
        problemResponse(500, {
          detail: 'operator text',
          reasonCode: 'sdap.provision.container_ownership_unreadable',
          speContainerId: 'b!its-own-container',
          ...(state === undefined ? {} : { containerOwnershipState: state }),
        })
      );

      const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

      expect(result.failureKind).toBe('not-started');
      expect(result.retryable).toBe(retryable);
      expect(result.errorMessage).toMatch(/Nothing about the project changed/);
      expect(result.errorMessage).not.toMatch(/try (securing )?(it )?again|retry/i);
      if (retryable) {
        expect(result.errorMessage).not.toMatch(/administrator/i);
      } else {
        expect(result.errorMessage).toMatch(/administrator needs to look at the service's permission to check it/i);
      }
    }
  );

  it('keeps the confirmed-share copy for a kept container whose share was read back', () => {
    const result = classifyProvisioningFailure('sdap.provision.owner_assignment_unverified', {
      containerKept: true,
      creatorShareConfirmed: true,
    });
    expect(result.errorMessage).toMatch(/read back, so you can open it/i);
    expect(result.errorMessage).not.toMatch(/Manage Access/i);
  });

  it('does not leak the server ProblemDetails detail or status into what the user is shown', async () => {
    const authFetch = jest.fn().mockResolvedValue(
      problemResponse(500, {
        title: 'Internal Server Error',
        detail: SERVER_DETAIL,
        reasonCode: 'sdap.provision.secure_bu_not_found',
      })
    );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.errorMessage).not.toContain(SERVER_DETAIL);
    expect(result.errorMessage).not.toContain('Internal Server Error');
    expect(result.errorMessage).not.toMatch(/HTTP\s*5\d\d/);
    // …and it names the missing setup, which is the one actionable fact.
    expect(result.errorMessage).toMatch(/Secure Record business unit/i);
    // The operator-facing text still reaches the console for support.
    expect(consoleError).toHaveBeenCalled();
  });

  // Task 133 (C11): every code the endpoint can emit, the state it maps to, and whether the SAME caller can finish
  // securing by calling again. `retryable` must match what the endpoint's detail tells that caller — a state the
  // server calls retryable that the client does not offer to retry strands the user; the reverse promises an action
  // that fails.
  //
  // Round 29 widened the row: an optional HTTP status (the one code answered with two statuses is classified by it), and
  // the PER-PERSON warnings — reason codes the endpoint emits inside a successful response (`skippedPrincipals`), which are
  // not failures: their kind is 'per-person-warning', their copy comes from describeSkippedPrincipal, and `retryable` does
  // not apply (false).
  const EMITTED: ReadonlyArray<[string, string, boolean, number?]> = [
    ['sdap.provision.secure_bu_not_found', 'environment-not-configured', false],
    ['sdap.provision.secure_bu_ambiguous', 'environment-not-configured', false],
    ['sdap.provision.secure_owner_team_not_found', 'environment-not-configured', false],
    ['sdap.provision.secure_owner_team_ambiguous', 'environment-not-configured', false],
    // Task 144: the named owner team's members and the business unit's users are checked BEFORE any
    // mutation, so each of these is a not-safe environment with nothing moved — never 'error'.
    ['sdap.provision.secure_owner_team_has_members', 'environment-not-configured', false],
    ['sdap.provision.secure_owner_team_membership_unreadable', 'environment-not-configured', false],
    ['sdap.provision.secure_bu_has_users', 'environment-not-configured', false],
    ['sdap.provision.secure_bu_users_unreadable', 'environment-not-configured', false],
    // Task 133: checked before any mutation too.
    ['sdap.provision.container_type_not_configured', 'environment-not-configured', false],
    // Since task 133 a 409 here means genuinely provisioned: owned by the team WITH a container.
    ['sdap.provision.already_provisioned', 'already-provisioned', false],
    // Task 144: secured before the named team existed, under the retired default team — already claimed.
    ['sdap.provision.owned_by_other_secure_team', 'already-provisioned', false],
    ['sdap.provision.legacy_per_project_bu', 'legacy-provisioning', false],
    // Task 133: refused before any change — nothing moved, the caller still passes the Write gate.
    ['sdap.provision.creator_unresolved', 'not-started', true],
    // Task 133 verifier round 1: refused before any change, but DETERMINISTIC — the same call is refused again (an
    // ownerless row; a resume naming colleagues from someone other than the creator), so no retry is offered.
    ['sdap.provision.record_owner_unreadable', 'not-started', false],
    ['sdap.provision.resume_colleagues_not_permitted', 'not-started', false],
    // Task 133 b2: a container already on the project. Recorded on ANOTHER record too — deterministic, an administrator
    // decides; could not be checked — a read failed, the same caller may call again (without the extension, or with
    // `containerOwnershipState: unreadable`; `refused` is pinned above). Both refused before any change.
    ['sdap.provision.container_shared_with_another_record', 'not-started', false],
    ['sdap.provision.container_ownership_unreadable', 'not-started', true],
    // Task 133 r1: a SHARED container is unlinked before the move; that failed, so nothing moved and the same caller may
    // call again.
    ['sdap.provision.shared_container_not_cleared', 'not-started', true],
    // Task 133 c1: the records that move with the project could not be read before any change. Without the extension
    // (or with `cascadeChildState: unreadable`) a read failed — the same caller may call again; `refused` is pinned below.
    ['sdap.provision.cascade_children_unreadable', 'not-started', true],
    // Task 133 c1: undone, but records that moved with it are not back on their own owners — an administrator first.
    ['sdap.provision.cascade_children_not_restored', 'needs-administrator', false],
    // Task 150: marking the project secure is the server's FIRST write; it failed, nothing else changed, the same caller
    // may call again.
    ['sdap.provision.secure_flag_not_set', 'not-started', true],
    // Task 150 (owner round 10 item 10): an UNFLAGGED record is secured only for its creator. Refused before any change —
    // deterministic for a caller who did not create it; a failed read of the creator, the same caller may call again.
    ['sdap.provision.not_record_creator', 'not-started', false],
    ['sdap.provision.record_creator_unverifiable', 'not-started', true],
    // Task 133: the share failed and the move was undone (or never made), read back.
    ['sdap.provision.creator_share_failed', 'share-failed', true],
    // Read back unchanged: nothing moved — but retrying a refused or ignored assignment repeats it.
    ['sdap.provision.owner_assignment_failed', 'not-secured', false],
    ['sdap.provision.owner_assignment_not_applied', 'not-secured', false],
    // Task 133: a share to the creator was issued (confirmed or not — copy pinned above), so the next call resumes or
    // restarts by the observed state.
    ['sdap.provision.owner_assignment_unverified', 'interrupted', true],
    // Task 133: secured and shared, no container recorded — the next call resumes.
    ['sdap.provision.container_creation_failed', 'storage-incomplete', true],
    ['sdap.provision.container_not_recorded', 'storage-incomplete', true],
    // Task 133: only an administrator can finish these — the creator may no longer pass the Write gate.
    ['sdap.provision.creator_share_failed_resumable', 'needs-administrator', false],
    ['sdap.provision.resume_creator_unavailable', 'needs-administrator', false],
    // Task 148: secured and shared, but some existing related records are not secured yet — the next call completes them.
    ['sdap.provision.children_incomplete', 'interrupted', true],
    // Round 26 item 3 (batch-4 integration): secured and shared, but some existing files are not moved into the record's
    // own container yet — the next call completes them.
    ['sdap.provision.files_incomplete', 'interrupted', true],
    // Task 143 (owner N6), copy round 29: the caller is on the No Access list — refused 403 before any change,
    // deterministic; whether they are could not be checked — refused 500 before any change, the same caller may call again.
    ['sdap.provision.creator_no_access', 'not-started', false],
    ['sdap.provision.creator_no_access_unverifiable', 'not-started', true],
    // Task 150 round 53 item 1: on Make Secure only, the caller's effective rights could not be read — refused 500 before
    // any change, the same caller may call again (copy pinned verbatim below).
    ['sdap.provision.caller_rights_unverifiable', 'not-started', true],
    // Task 143: a RESUME's person on the No Access list — 409, an administrator reviews the access (the 500 "could not be
    // checked" twin is pinned verbatim below, retryable).
    ['sdap.provision.resume_creator_no_access', 'needs-administrator', false, 409],
    // Task 143: named colleagues skipped inside a SUCCESS — per-person warnings, not failures.
    ['sdap.provision.principal_no_access', 'per-person-warning', false],
    ['sdap.provision.principal_no_access_unverifiable', 'per-person-warning', false],
    // Task 150 (round 33 items 1 and 5): a named colleague whose share itself failed — named, never silent.
    ['sdap.provision.principal_share_failed', 'per-person-warning', false],
  ];

  const FAILURE_CODES = EMITTED.filter(([, kind]) => kind !== 'per-person-warning');
  const WARNING_CODES = EMITTED.filter(([, kind]) => kind === 'per-person-warning');

  // A rest parameter: jest reads a callback declaring more parameters than a row's values as a done-callback test.
  it.each(EMITTED)('maps reason code %s to %s (retryable: %s)', (...row) => {
    const [reasonCode, expected, retryable, status] = row;
    if (expected === 'per-person-warning') {
      expect(describeSkippedPrincipal(reasonCode, 'Dana Reyes')).toContain('Dana Reyes');
      return;
    }

    const result = classifyProvisioningFailure(reasonCode, undefined, status);
    expect(result.failureKind).toBe(expected);
    expect(result.retryable).toBe(retryable);
    expect(result.errorMessage.trim().length).toBeGreaterThan(0);
  });

  it("never advises trying again in the message — the retry is the host's action, keyed on `retryable`", () => {
    // A message that says "try again" renders in hosts that may have nothing to click (FR-31). The advice lives
    // next to the "Try securing again" button in SecureProvisioningOutcome, which renders it only when retryable.
    const cases: Array<[string | undefined, number | undefined]> = [
      ...FAILURE_CODES.map(([code, , , status]): [string, number | undefined] => [code, status]),
      ['sdap.provision.resume_creator_no_access', 500],
      [undefined, undefined],
      ['sdap.provision.something_invented_later', undefined],
    ];
    for (const [code, status] of cases) {
      expect(classifyProvisioningFailure(code, undefined, status).errorMessage).not.toMatch(
        /try (securing )?(it )?again|retry/i
      );
    }
    for (const [code] of WARNING_CODES) {
      expect(describeSkippedPrincipal(code, 'Dana Reyes')).not.toMatch(/try (securing )?(it )?again|retry/i);
    }
  });

  it('never calls a secure-requested project a normal project', () => {
    // Task 150: `sprk_issecure` is the server's first write and never cleared; whether the project ends secure is open.
    for (const [code, , , status] of FAILURE_CODES) {
      expect(classifyProvisioningFailure(code, undefined, status).errorMessage).not.toMatch(/normal project/i);
    }
    expect(classifyProvisioningFailure(undefined).errorMessage).not.toMatch(/normal project/i);
  });

  // Task 150, owner round 10 item 9 (F6): the copy the owner picked, verbatim. Row 2 is the resume-neutral option D: an
  // environment refusal comes before the flag write on a FIRST call, but on a RESUME (or for a row an older client
  // flagged) the project is already flagged, and the response does not say which — so the copy claims neither.
  it('says an environment refusal could not finish securing the project — claiming neither "not secured" nor "nothing changed" (F6 row 2, option D)', () => {
    const { errorMessage } = classifyProvisioningFailure('sdap.provision.secure_bu_not_found');
    expect(errorMessage).toBe(
      'Secure projects cannot be set up in this environment right now — its Secure Record business unit, owner team or document storage is missing or not in a safe state. The project was created, but securing it could not be finished; an administrator can finish securing it once the setup is fixed.'
    );
    expect(errorMessage).not.toMatch(/not secured|nothing about it changed|marked secure/i);
  });

  it('says the flag could not be set, and that nothing else changed (F6 row 3, option A)', () => {
    expect(classifyProvisioningFailure('sdap.provision.secure_flag_not_set').errorMessage).toBe(
      'The project could not be marked secure, so securing it stopped before anything else changed: its ownership, sharing and document storage are as they were.'
    );
  });

  // Task 150, owner round 13 item 10 (F6 rows 7-8): option B, verbatim. The two creator-rule refusals of an UNFLAGGED
  // record (owner round 10 item 10).
  it('says only the creator can secure the project this way, and that nothing changed (F6 row 7, option B)', () => {
    expect(classifyProvisioningFailure('sdap.provision.not_record_creator').errorMessage).toBe(
      'Only the person who created this project can secure it this way. Nothing about the project changed.'
    );
  });

  // Task 150 round 17 item 2: rows 8 and task 133's `creator_unresolved` also answer an anomalous unflagged RESUME, where
  // the Secure Record owner team already owns the record — so, like row 2's option D, neither may describe a first call.
  it('says who created the project could not be checked, so securing it could not be finished — resume-neutral (F6 row 8, round 17)', () => {
    const { errorMessage } = classifyProvisioningFailure('sdap.provision.record_creator_unverifiable');
    expect(errorMessage).toBe(
      'Who created this project could not be checked, so securing it could not be finished. Nothing about the project changed.'
    );
    expect(errorMessage).not.toMatch(/did not start|not secured/i);
  });

  it('says securing could not be finished because the account could not be confirmed — resume-neutral (creator_unresolved, round 17)', () => {
    const { errorMessage } = classifyProvisioningFailure('sdap.provision.creator_unresolved');
    expect(errorMessage).toBe(
      'Securing the project could not be finished, because your account could not be confirmed. Nothing about the project changed.'
    );
    expect(errorMessage).not.toMatch(/did not start|not secured/i);
  });

  // Task 150 round 17 item 1: a missing creator column on the creator rule is UNVERIFIABLE (the server's
  // record_creator_unverifiable, 403, creatorState column-missing) — deterministic, so no retry and the administrator named,
  // with the same message the resume's column-missing refusal shows (one environment fact, one message).
  it('reads creatorState=column-missing on record_creator_unverifiable as setup for an administrator, not a retry', async () => {
    const authFetch = jest.fn().mockResolvedValue(
      problemResponse(403, {
        detail: 'operator text',
        reasonCode: 'sdap.provision.record_creator_unverifiable',
        creatorState: 'column-missing',
      })
    );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.failureKind).toBe('needs-administrator');
    expect(result.retryable).toBe(false);
    expect(result.errorMessage).toBe(
      classifyProvisioningFailure('sdap.provision.resume_creator_unavailable', { creatorState: 'column-missing' })
        .errorMessage
    );
    expect(result.errorMessage).toMatch(/not yet set up to record who created/i);
    expect(result.errorMessage).not.toMatch(/did not start|not secured/i);
  });

  it('classifies every reason code ProvisionProjectEndpoint can emit', () => {
    // EMITTED is the endpoint's `internal const string Reason*` set, transcribed — the guard against the drift task
    // 068 found (container_not_recorded once fell through to copy that was wrong in both halves). Adding a Reason*
    // constant server-side means adding it to EMITTED and deciding deliberately what state and retryability it has.
    // Nothing the endpoint emits lands on the generic 'error' copy — including `caller_rights_unverifiable` (task 150
    // round 53 item 1), which only the form's Make Secure command can meet (the wizard sends no transition, and the
    // server reads no floor for it: SecureFlagEndpointWriteTests.Provision_TheWizardsPath_ReadsNoFloor_...).
    for (const [code, , , status] of FAILURE_CODES) {
      expect(classifyProvisioningFailure(code, undefined, status).failureKind).not.toBe('error');
    }
    for (const [code] of WARNING_CODES) {
      expect(describeSkippedPrincipal(code, 'Dana Reyes')).toBeDefined();
    }
    expect(EMITTED).toHaveLength(40);
  });

  // Task 150 round 53 item 1: provisioning's own code for an unreadable floor (codes are namespaced by endpoint — F3's
  // `sdap.unsecure.permission_unverifiable` is the unsecure endpoint's). Round 53's ratified sentence, verbatim, with
  // {record} = project; its closing "you may try again" is the host's retry action here (`retryable: true`), never words.
  it('says which access the caller holds could not be read, and that nothing changed — retryable (caller_rights_unverifiable, round 53)', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue(
        problemResponse(500, { detail: 'operator text', reasonCode: 'sdap.provision.caller_rights_unverifiable' })
      );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.failureKind).toBe('not-started');
    expect(result.retryable).toBe(true);
    expect(result.reasonCode).toBe('sdap.provision.caller_rights_unverifiable');
    expect(result.errorMessage).toBe(
      'Which access you hold on this project could not be read, so securing it could not make sure you keep that access. Nothing was changed.'
    );
    expect(result.errorMessage).not.toContain('operator text');
  });

  // Round 29 (owner round 27's stance: the recommended wording, adjustable in UAT): task 143's five provisioning codes,
  // verbatim. Resume-neutral ("could not be finished") as in rounds 10 and 17.
  it('says the caller is on the No Access list and nothing changed — not retryable (creator_no_access, round 29)', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue(
        problemResponse(403, { detail: 'operator text', reasonCode: 'sdap.provision.creator_no_access' })
      );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.errorMessage).toBe(
      "You are on this project's No Access list, so you cannot secure it. Nothing about the project changed."
    );
    expect(result.retryable).toBe(false);
  });

  it('says whether the caller may access the project could not be checked — retryable (creator_no_access_unverifiable, round 29)', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue(
        problemResponse(500, { detail: 'operator text', reasonCode: 'sdap.provision.creator_no_access_unverifiable' })
      );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.errorMessage).toBe(
      'Whether you may access this project could not be checked, so securing it could not be finished. Nothing about the project changed.'
    );
    expect(result.retryable).toBe(true);
  });

  it('reads resume_creator_no_access by its status: 409 — the creator is on the No Access list, an administrator reviews (round 29)', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue(
        problemResponse(409, { detail: 'operator text', reasonCode: 'sdap.provision.resume_creator_no_access' })
      );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.errorMessage).toBe(
      "Securing the project could not be finished, because the person who created it is on its No Access list. Nothing about the project changed. An administrator needs to review the project's access."
    );
    expect(result.failureKind).toBe('needs-administrator');
    expect(result.retryable).toBe(false);
  });

  it("reads resume_creator_no_access by its status: 500 — the creator's access could not be checked, retryable (round 29)", async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue(
        problemResponse(500, { detail: 'operator text', reasonCode: 'sdap.provision.resume_creator_no_access' })
      );

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.errorMessage).toBe(
      'Securing the project could not be finished, because the access of the person who created it could not be checked. Nothing about the project changed.'
    );
    expect(result.failureKind).toBe('not-started');
    expect(result.retryable).toBe(true);
  });

  it('does not guess resume_creator_no_access without a 409 or 500 — the generic state, never a retry', () => {
    const result = classifyProvisioningFailure('sdap.provision.resume_creator_no_access');
    expect(result.failureKind).toBe('error');
    expect(result.retryable).toBe(false);
  });

  it('turns each skipped colleague into an authored per-person warning on the success result (round 29)', async () => {
    const walled = '55555555-5555-5555-5555-555555555555';
    const unchecked = '66666666-6666-6666-6666-666666666666';
    const unshared = '88888888-8888-8888-8888-888888888888';
    const authFetch = jest.fn().mockResolvedValue(
      okResponse({
        ...successBody,
        skippedPrincipals: [
          {
            systemUserId: walled,
            reasonCode: 'sdap.provision.principal_no_access',
            message: 'server prose, never shown',
          },
          {
            systemUserId: unchecked,
            reasonCode: 'sdap.provision.principal_no_access_unverifiable',
            message: 'server prose, never shown',
          },
          {
            systemUserId: unshared,
            reasonCode: 'sdap.provision.principal_share_failed',
            message: 'server prose, never shown',
          },
        ],
      })
    );

    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, sharePrincipalIds: [walled, unchecked, unshared] },
      authFetch as never,
      BFF,
      { [walled]: 'Dana Reyes', [unchecked]: 'Sam Ortiz', [unshared]: 'Lee Park' }
    );

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual([
      "Dana Reyes is on this project's No Access list, so the project was not shared with them.",
      'Whether Sam Ortiz may access this project could not be checked, so the project was not shared with them. You can share it with them later from Manage Access.',
      // Task 150 (round 33 items 1 and 5): the server names a colleague whose share failed — a KNOWN code, not the generic.
      'Lee Park was not given access to this project. You can share it with them later from Manage Access.',
    ]);
    expect(consoleError).not.toHaveBeenCalledWith(
      expect.stringContaining('for a reason this client does not know'),
      expect.anything()
    );
    expect(result.warnings?.join(' ')).not.toContain('server prose');
  });

  // Round 40 item 3: a person who cannot be named is "Someone" — never an empty name, never the raw id, never silent.
  it.each([
    ['an id the host gave no name for', '99999999-9999-9999-9999-999999999999', {}],
    [
      'an id the host named with a blank',
      '99999999-9999-9999-9999-999999999999',
      { '99999999-9999-9999-9999-999999999999': '  ' },
    ],
    ['an empty id', '', { '': 'Not Used' }],
  ])('names %s as "Someone" (round 40 item 3)', async (_case, systemUserId, names) => {
    const authFetch = jest.fn().mockResolvedValue(
      okResponse({
        ...successBody,
        skippedPrincipals: [{ systemUserId, reasonCode: 'sdap.provision.principal_share_failed', message: 'x' }],
      })
    );

    const result = await provisionSecureProject(
      { projectId: PROJECT_ID },
      authFetch as never,
      BFF,
      names as Record<string, string>
    );

    expect(result.warnings).toEqual([
      'Someone was not given access to this project. You can share it with them later from Manage Access.',
    ]);
  });

  it('the fallback word is ONE constant, and a blank name given to describeSkippedPrincipal is never shown blank', () => {
    expect(UNNAMED_PERSON).toBe('Someone');
    expect(describeSkippedPrincipal('sdap.provision.principal_no_access', '')).toBe(
      "Someone is on this project's No Access list, so the project was not shared with them."
    );
  });

  it('returns no warnings when no colleague was skipped', async () => {
    const authFetch = jest.fn().mockResolvedValue(okResponse({ ...successBody, skippedPrincipals: [] }));

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(true);
    expect(result.warnings).toBeUndefined();
  });

  // Round 33 item 5: an unknown skipped-principal reason is never silent — the generic per-person warning, and a log.
  it('shows the generic per-person warning, and logs the code, for a skipped colleague whose reason it does not know', async () => {
    const someone = '77777777-7777-7777-7777-777777777777';
    const authFetch = jest.fn().mockResolvedValue(
      okResponse({
        ...successBody,
        skippedPrincipals: [
          { systemUserId: someone, reasonCode: 'sdap.provision.principal_invented_later', message: 'server prose' },
        ],
      })
    );

    const result = await provisionSecureProject(
      { projectId: PROJECT_ID, sharePrincipalIds: [someone] },
      authFetch as never,
      BFF,
      { [someone]: 'Rui Tanaka' }
    );

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual(['Rui Tanaka was not given access to this project.']);
    expect(consoleError).toHaveBeenCalledWith(
      expect.stringContaining('for a reason this client does not know'),
      expect.objectContaining({ reasonCode: 'sdap.provision.principal_invented_later' })
    );
  });

  // Round 40 item 3 on the generic path: an unknown reason for a person the host gave no (or a blank) name for is
  // "Someone" too — the name is resolved once (skippedPersonName), before either warning is composed.
  it.each([
    ['no name', {}],
    ['a blank name', { '66666666-6666-6666-6666-666666666666': '   ' }],
  ])('the generic per-person warning names an unnamed person as "Someone" (%s)', async (_case, names) => {
    const authFetch = jest.fn().mockResolvedValue(
      okResponse({
        ...successBody,
        skippedPrincipals: [
          {
            systemUserId: '66666666-6666-6666-6666-666666666666',
            reasonCode: 'sdap.provision.principal_invented_later',
            message: 'x',
          },
        ],
      })
    );

    const result = await provisionSecureProject(
      { projectId: PROJECT_ID },
      authFetch as never,
      BFF,
      names as Record<string, string>
    );

    expect(result.warnings).toEqual(['Someone was not given access to this project.']);
  });

  it('falls back to a generic error for an unknown or absent reason code', () => {
    expect(classifyProvisioningFailure(undefined).failureKind).toBe('error');
    expect(classifyProvisioningFailure('sdap.provision.something_invented_later').failureKind).toBe('error');
    // An unknown state is never offered as retryable: the client cannot tell the caller still passes the Write gate.
    expect(classifyProvisioningFailure(undefined).retryable).toBe(false);
  });

  it('produces a non-empty authored message for every failure kind', () => {
    // Guards the branch that returns a kind but forgets its copy — which would render an empty
    // MessageBar, the failure mode of "handled" errors that show the user nothing.
    for (const code of [
      undefined,
      'sdap.provision.secure_bu_not_found',
      'sdap.provision.already_provisioned',
      'sdap.provision.legacy_per_project_bu',
      'sdap.provision.creator_share_failed',
    ]) {
      const { errorMessage } = classifyProvisioningFailure(code);
      expect(errorMessage.trim().length).toBeGreaterThan(0);
    }
  });

  it('classifies a transport failure as an error without inventing a reason code', async () => {
    const authFetch = jest.fn().mockRejectedValue(new Error('ECONNREFUSED bff.example.test'));

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(false);
    expect(result.failureKind).toBe('error');
    expect(result.reasonCode).toBeUndefined();
    expect(result.errorMessage).not.toContain('ECONNREFUSED');
  });

  it('survives a non-JSON error body', async () => {
    const authFetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 502,
      json: async () => {
        throw new SyntaxError('Unexpected token < in JSON');
      },
    } as unknown as Response);

    const result = await provisionSecureProject({ projectId: PROJECT_ID }, authFetch as never, BFF);

    expect(result.success).toBe(false);
    expect(result.failureKind).toBe('error');
    expect(result.errorMessage).not.toContain('Unexpected token');
  });
});

// NOT TESTED HERE: `PROVISIONING_STEPS`.
//
// A test asserting its keys was written for this task and then removed. `PROVISIONING_STEPS` has
// ZERO production consumers — its renderer `ProvisioningProgressStep` was deleted 2026-09-04 (see
// `../index.ts`), leaving the constant, the barrel export, and nothing that reads either. Pinning
// its order would have been "implementation == implementation" over code nothing runs: an ADR-038 B6
// mirror test and a B10 coverage-filler at once. Nothing renders the order, so nothing can regress
// it. If the progress UI is ever rebuilt, the test belongs with the component that renders it.
