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
  it.each([
    ['unreadable', 'not-started', true, /could not be looked up/i],
    ['column-missing', 'needs-administrator', false, /not yet set up to record who created/i],
    ['disabled', 'needs-administrator', false, /administrator needs to finish/i],
    ['application-user', 'needs-administrator', false, /administrator needs to finish/i],
    ['absent', 'needs-administrator', false, /administrator needs to finish/i],
    [undefined, 'needs-administrator', false, /administrator needs to finish/i],
  ])(
    'reads creatorState=%s from the problem body for resume_creator_unavailable',
    async (creatorState, kind, retryable, says) => {
      const authFetch = jest.fn().mockResolvedValue(
        problemResponse(creatorState === 'unreadable' || creatorState === 'column-missing' ? 500 : 409, {
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
  const EMITTED: ReadonlyArray<[string, string, boolean]> = [
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
    // decides; could not be checked — a read failed, the same caller may call again. Both refused before any change.
    ['sdap.provision.container_shared_with_another_record', 'not-started', false],
    ['sdap.provision.container_ownership_unreadable', 'not-started', true],
    // Task 133 r1: a SHARED container is unlinked before the move; that failed, so nothing moved and the same caller may
    // call again.
    ['sdap.provision.shared_container_not_cleared', 'not-started', true],
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
  ];

  it.each(EMITTED)('maps reason code %s to %s (retryable: %s)', (reasonCode, expected, retryable) => {
    const result = classifyProvisioningFailure(reasonCode);
    expect(result.failureKind).toBe(expected);
    expect(result.retryable).toBe(retryable);
    expect(result.errorMessage.trim().length).toBeGreaterThan(0);
  });

  it("never advises trying again in the message — the retry is the host's action, keyed on `retryable`", () => {
    // A message that says "try again" renders in hosts that may have nothing to click (FR-31). The advice lives
    // next to the "Try securing again" button in SecureProvisioningOutcome, which renders it only when retryable.
    for (const code of [...EMITTED.map(([c]) => c), undefined, 'sdap.provision.something_invented_later']) {
      expect(classifyProvisioningFailure(code).errorMessage).not.toMatch(/try (securing )?(it )?again|retry/i);
    }
  });

  it('never calls a secure-requested project a normal project', () => {
    // Task 150: `sprk_issecure` is the server's first write and never cleared; whether the project ends secure is open.
    for (const code of [...EMITTED.map(([c]) => c), undefined]) {
      expect(classifyProvisioningFailure(code).errorMessage).not.toMatch(/normal project/i);
    }
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

  it('classifies every reason code ProvisionProjectEndpoint can emit', () => {
    // EMITTED is the endpoint's `internal const string Reason*` set, transcribed — the guard against the drift task
    // 068 found (container_not_recorded once fell through to copy that was wrong in both halves). Adding a Reason*
    // constant server-side means adding it to EMITTED and deciding deliberately what state and retryability it has.
    // Nothing the endpoint emits lands on the generic 'error' copy.
    for (const [code] of EMITTED) {
      expect(classifyProvisioningFailure(code).failureKind).not.toBe('error');
    }
    expect(EMITTED).toHaveLength(29);
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
