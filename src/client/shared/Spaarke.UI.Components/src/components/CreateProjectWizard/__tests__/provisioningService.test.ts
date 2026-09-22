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

const okResponse = (body: unknown) =>
  ({ ok: true, status: 200, json: async () => body }) as unknown as Response;

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

  it('classifies a missing Secure Project business unit as an unconfigured environment — despite the 500', async () => {
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
    expect(result.errorMessage).toMatch(/Secure Project business unit/i);
    // The operator-facing text still reaches the console for support.
    expect(consoleError).toHaveBeenCalled();
  });

  it.each([
    ['sdap.provision.secure_bu_not_found', 'environment-not-configured'],
    ['sdap.provision.secure_bu_ambiguous', 'environment-not-configured'],
    ['sdap.provision.secure_owner_team_not_found', 'environment-not-configured'],
    ['sdap.provision.secure_owner_team_ambiguous', 'environment-not-configured'],
    ['sdap.provision.already_provisioned', 'already-provisioned'],
    ['sdap.provision.legacy_per_project_bu', 'legacy-provisioning'],
    ['sdap.provision.creator_unresolved', 'share-failed'],
    ['sdap.provision.creator_share_failed', 'share-failed'],
    ['sdap.provision.container_not_recorded', 'container-not-recorded'],
    // "Nothing has been provisioned" per the endpoint — the generic copy is already accurate.
    ['sdap.provision.owner_assignment_failed', 'error'],
    ['sdap.provision.owner_assignment_not_applied', 'error'],
  ])('maps reason code %s to %s', (reasonCode, expected) => {
    expect(classifyProvisioningFailure(reasonCode).failureKind).toBe(expected);
  });

  it('classifies every reason code ProvisionProjectEndpoint can emit', () => {
    // This list is the endpoint's `internal const string Reason*` set, transcribed. It is the guard
    // against the drift this task found: the client originally classified 8 of the 11, and one of
    // the 3 it missed (container_not_recorded) fell through to copy that told the user the project
    // had been left as a NORMAL project — when in fact it had been secured AND had left an orphaned
    // container behind. Wrong in both halves. Adding a Reason* constant server-side means adding it
    // here, and deciding deliberately whether the generic message is honest for it.
    const emittedByEndpoint = [
      'sdap.provision.secure_bu_not_found',
      'sdap.provision.secure_bu_ambiguous',
      'sdap.provision.secure_owner_team_not_found',
      'sdap.provision.secure_owner_team_ambiguous',
      'sdap.provision.owner_assignment_failed',
      'sdap.provision.owner_assignment_not_applied',
      'sdap.provision.container_not_recorded',
      'sdap.provision.already_provisioned',
      'sdap.provision.legacy_per_project_bu',
      'sdap.provision.creator_unresolved',
      'sdap.provision.creator_share_failed',
    ];

    for (const code of emittedByEndpoint) {
      const { failureKind, errorMessage } = classifyProvisioningFailure(code);
      expect(errorMessage.trim().length).toBeGreaterThan(0);
      // A code that lands on 'error' must be one we decided reads correctly there, not one nobody
      // has looked at.
      if (failureKind === 'error') {
        expect(code).toMatch(/owner_assignment_(failed|not_applied)/);
      }
    }
  });

  it('falls back to a generic error for an unknown or absent reason code', () => {
    expect(classifyProvisioningFailure(undefined).failureKind).toBe('error');
    expect(classifyProvisioningFailure('sdap.provision.something_invented_later').failureKind).toBe('error');
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
