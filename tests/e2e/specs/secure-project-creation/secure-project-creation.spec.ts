/**
 * E2E Tests: Secure Project Creation Flow
 *
 * This spec drives the SUPPORTED secure-create path, the one the Create Project wizard uses
 * (`CreateProjectWizard.tsx`: `projectService.createProject`, then `provisioningService.provisionSecureProject`):
 *
 *   1. The creating USER creates an ORDINARY `sprk_project`, WITHOUT `sprk_issecure`. Since
 *      unified-access-control-r2 task 150 the column is field-secured: only the BFF application user may create or
 *      update it (`scripts/Set-SecureFlagFieldSecurity.ps1`), so a create payload that names it, even as `false`, is
 *      refused for every other identity. This spec used to post `sprk_issecure: true` directly; that is now refused.
 *   2. The SAME user calls POST /api/v1/external-access/provision-project with their own delegated token. The route is
 *      gated by `DelegationRuleFilter` (Write on the record, evaluated as the caller through OBO), and an unflagged
 *      record is secured only for the person who created it (`createdby`, else the BFF-stamped
 *      `sprk_createdbyperson`). So the create and the call must be made by the same person. A client-credentials token
 *      can do neither: OBO has no user to exchange, and an app-only create makes the application user `createdby`.
 *   3. Provisioning (`ProvisionProjectEndpoint.cs`) then:
 *        - resolves the ONE canonical `Secure Record` business unit by name, and its NAMED, non-default owner team
 *          (`Secure Record Owners`, task 144), refusing unless the team has no members and the BU holds no users
 *        - sets `sprk_issecure = true` as its FIRST write, and reads it back (task 150)
 *        - shares the record to its creator, then assigns it to that team (verified by read-back)
 *        - creates the record's own SPE container and records it on `sprk_containerid`, failing loudly if that write
 *          does not land
 *
 * History: task 021 (2026-08-25) removed the child business unit per project (`SP-{ProjectRef}`), the External Access
 * account per project, umbrella-BU reuse, and the `sprk_securitybuid` / `sprk_specontainerid` /
 * `sprk_externalaccountid` stamps (none of those columns ever existed on sprk_project; `sprk_externalaccount` is the
 * project's CLIENT lookup and provisioning must never write it). The cases that tested those mechanisms
 * (TC-070-02 umbrella reuse, TC-070-11 ProjectRef required, TC-070-21 SP-{ProjectRef} naming, TC-070-22 account owned
 * by the child BU) were removed, because the scenarios no longer exist. Their offline replacements live in the BFF unit
 * tests (for example `ProvisionProject_OnTheHappyPath_NeverWritesTheClientLookup`).
 *
 * Also covers:
 *   - The route's gate: no token → 401; an unresolvable or unreachable target → 403 from the delegation filter, never a
 *     400 or 404 (an unauthorized caller must not be able to enumerate records)
 *   - The creator rule: an ordinary project the caller did not create is refused, and nothing changes
 *   - Fail-closed on a missing Secure Record BU (operator-armed, see TC-070-14)
 *
 * This file is not run by CI (no workflow references tests/e2e). It needs a deployed environment.
 *
 * Prerequisites:
 *   - BFF API deployed to dev with the /api/v1/external-access/* endpoints
 *   - The canonical `Secure Record` BU and its `Secure Record Owners` team set up (docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md)
 *   - `sprk_issecure` field security applied (scripts/Set-SecureFlagFieldSecurity.ps1)
 *   - SharePointEmbedded:ContainerTypeId configured on the BFF API
 *   - tests/e2e/config/.env, see "Manual Execution Notes" at the bottom
 *
 * Cleanup: test projects are deleted with the app identity (the creator's share does not include Delete). SPE
 * containers are NOT deleted. Each container id is logged so an orphan can be reconciled.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs
 * @see src/server/api/Sprk.Bff.Api/Api/ExternalAccess/DelegationRuleFilter.cs
 * @see src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/provisioningService.ts
 * @see projects/unified-access-control-r2/tasks/047-validate-secure-project-provisioning-live.poml
 */

import { test, expect } from '@playwright/test';
import { DataverseAPI } from '../../utils/dataverse-api';

// ============================================================================
// Constants
// ============================================================================

const BFF_API_BASE = process.env.BFF_API_URL || 'https://spaarke-bff-dev.azurewebsites.net';
const DATAVERSE_API_URL = process.env.DATAVERSE_API_URL || 'https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2';

/** The Dataverse resource (origin) that tokens are requested for. The Web API path is not part of the resource. */
const DATAVERSE_RESOURCE = new URL(DATAVERSE_API_URL).origin;

/** Dataverse entity set names */
const ENTITY_SETS = {
  project: 'sprk_projects',
  businessUnit: 'businessunits',
} as const;

/** BFF external access endpoint base path */
const EXTERNAL_ACCESS_BASE = `${BFF_API_BASE}/api/v1/external-access`;

/**
 * The canonical Secure Record business unit's name.
 *
 * SINGULAR, verified against live Dataverse metadata 2026-08-25. It must match whatever the BFF's
 * `SecureRecord:BusinessUnitName` is set to in the target environment (default `Secure Record`).
 * Every secure project shares this business unit, and environment setup creates it. Tests must never delete it.
 */
const SECURE_BU_NAME = process.env.SECURE_RECORD_BU_NAME || 'Secure Record';

/**
 * The NAMED owner team that owns every secure record (task 144, #967; owner decision F9). Must match the
 * BFF's `SecureRecord:OwnerTeamName` (default `Secure Record Owners`). Deliberately NOT the business unit's
 * default team, which carries the BU's own name and whose membership follows every user placed in the BU.
 */
const SECURE_OWNER_TEAM_NAME = process.env.SECURE_RECORD_OWNER_TEAM_NAME || 'Secure Record Owners';

/**
 * Optional: the BFF's `SharePointEmbedded:DefaultContainerId` (the SHARED container). When set, the provisioned
 * container must differ from it as well as from every business unit's container.
 */
const SHARED_DEFAULT_CONTAINER_ID = process.env.SPE_DEFAULT_CONTAINER_ID || '';

/**
 * The CREATOR: one internal Dataverse user, the person who creates the project and secures it, exactly as in the
 * wizard. Both tokens are delegated tokens for that SAME user:
 *   - SECURE_CREATOR_BFF_TOKEN: audience the BFF app registration (the token the wizard's MSAL-backed fetch sends).
 *     The BFF exchanges it through OBO to find the caller, so a client-credentials token does not work here.
 *   - SECURE_CREATOR_DATAVERSE_TOKEN: audience Dataverse. Used to create the project, so `createdby` is the caller,
 *     and to read it back afterwards (after provisioning, the creator's share is the only way in for a human).
 * Supplied pre-acquired, like the portal tokens in specs/secure-project/access-level-enforcement.spec.ts.
 */
const CREATOR_BFF_TOKEN = process.env.SECURE_CREATOR_BFF_TOKEN || '';
const CREATOR_DATAVERSE_TOKEN = process.env.SECURE_CREATOR_DATAVERSE_TOKEN || '';
const CREATOR_TOKENS_MISSING = !CREATOR_BFF_TOKEN || !CREATOR_DATAVERSE_TOKEN;
const CREATOR_TOKENS_REASON =
  'SECURE_CREATOR_BFF_TOKEN and SECURE_CREATOR_DATAVERSE_TOKEN (delegated tokens for ONE internal user) ' +
  'not configured — skipping live test';

/** Reason codes asserted below (ProvisionProjectEndpoint.cs / DelegationRuleFilter.cs). */
const REASON = {
  alreadyProvisioned: 'sdap.provision.already_provisioned',
  secureBuNotFound: 'sdap.provision.secure_bu_not_found',
  notRecordCreator: 'sdap.provision.not_record_creator',
  delegationTargetUnresolved: 'sdap.access.deny.delegation_target_unresolved',
  delegationWriteRequired: 'sdap.access.deny.delegation_write_required',
  delegationCheckFailed: 'sdap.access.deny.delegation_check_failed',
} as const;

// ============================================================================
// Test data helpers
// ============================================================================

/**
 * Generates a unique project reference code for each test run to avoid
 * collisions between parallel test executions.
 */
function generateProjectRef(): string {
  return `E2E-${Date.now()}-${Math.random().toString(36).slice(2, 6).toUpperCase()}`;
}

/**
 * The create payload the Create Project wizard sends (projectService.createProject), without its optional lookups.
 *
 * `sprk_issecure` is deliberately ABSENT: it is field-secured (task 150) and only provisioning sets it. The column
 * names are the live ones: `sprk_projectname` and `sprk_projectdescription`. `sprk_projectref` and `sprk_description`,
 * which this builder used to send, do not exist on sprk_project.
 */
function buildProjectPayload(projectRef: string, label = 'Secure Project'): Record<string, unknown> {
  return {
    sprk_projectname: `E2E Test ${label} — ${projectRef}`,
    sprk_projectdescription: 'Created by E2E test — safe to delete',
  };
}

// ============================================================================
// Types
// ============================================================================

/** Mirrors ProvisionProjectResponse.cs (the fields this spec asserts). */
interface ProvisionProjectResponse {
  /** The canonical Secure Record BU, resolved by name, not created. */
  businessUnitId: string;
  businessUnitName: string;
  /** That BU's NAMED owner team (task 144, never its default team), which now owns the record. */
  ownerTeamId: string;
  ownerTeamName: string;
  speContainerId: string;
  /** The systemuser the record was shared to: the creator, identified from the caller's own token. */
  sharedToCreatorSystemUserId: string;
  /** Task 144: `project` | `matter` | `workassignment`, and the record's id. */
  recordType: string;
  recordId: string;
  resumed?: boolean;
}

/** A problem-details body with the BFF's `reasonCode` extension. */
interface ProblemBody {
  title?: string;
  detail?: string;
  reasonCode?: string;
}

/**
 * Columns that actually exist on live `sprk_project` (verified 2026-08-25).
 *
 * `_sprk_securitybuid_value`, `sprk_specontainerid` and `_sprk_externalaccountid_value`, which this
 * spec previously declared, do not exist on the table at all. `sprk_specontainerid` belongs to
 * `sprk_container`, which is where the name was borrowed from.
 */
interface ProjectRecord {
  sprk_projectid: string;
  sprk_projectname: string;
  sprk_issecure: boolean | null;
  /** The project's own SPE container, written by provisioning. */
  sprk_containerid?: string | null;
  /** The owning team, which shows that provisioning secured the record. */
  _owningteam_value?: string | null;
  /** The owning user, set on an ordinary (user-owned) record. */
  _owninguser_value?: string | null;
  /** The systemuser who created the record. The creator rule compares it with the caller. */
  _createdby_value?: string | null;
  /** Retired per-project security BU. Present only on legacy rows; never written now. */
  _sprk_securitybu_value?: string | null;
}

interface BusinessUnitRecord {
  businessunitid: string;
  name: string;
  _parentbusinessunitid_value?: string;
  sprk_containerid?: string | null;
}

// ============================================================================
// Shared helpers
// ============================================================================

/** The app identity, used ONLY for read-only lookups and for cleanup, never to create or secure a project. */
async function connectAppDataverse(): Promise<DataverseAPI> {
  const appToken = await DataverseAPI.authenticate(
    process.env.TENANT_ID || '',
    process.env.CLIENT_ID || '',
    process.env.CLIENT_SECRET || '',
    DATAVERSE_RESOURCE
  );
  return new DataverseAPI(DATAVERSE_API_URL, appToken);
}

/** The creator's own Dataverse session, which is how the wizard creates a project. */
function connectCreatorDataverse(): DataverseAPI {
  return new DataverseAPI(DATAVERSE_API_URL, CREATOR_DATAVERSE_TOKEN);
}

/** Calls provision-project as the creator, which is what the wizard does right after the create. */
async function callProvisionProject(
  body: Record<string, unknown>,
  token: string = CREATOR_BFF_TOKEN
): Promise<{ status: number; body: unknown }> {
  const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(body),
  });

  const responseBody = await response.json().catch(() => ({}));
  return { status: response.status, body: responseBody };
}

/** Reads the project's security-relevant columns. Every column named here exists on live sprk_project. */
async function queryProject(api: DataverseAPI, projectId: string): Promise<ProjectRecord | null> {
  // No catch: a failed read must fail the test, not turn into a null that a later assertion explains away.
  const results = await api.queryRecords<ProjectRecord>(ENTITY_SETS.project, {
    $filter: `sprk_projectid eq ${projectId}`,
    $select: [
      'sprk_projectid',
      'sprk_projectname',
      'sprk_issecure',
      'sprk_containerid',
      '_owningteam_value',
      '_owninguser_value',
      '_createdby_value',
      '_sprk_securitybu_value',
    ].join(','),
    $top: '1',
  });
  return results[0] ?? null;
}

/**
 * The container ids that provisioning must NOT record: every business unit's container, plus the configured shared
 * default when supplied. A populated `sprk_containerid` proves nothing, because before task 076 the wizard's BU cascade
 * filled it with shared storage. The provisioned id must be DIFFERENT from all of these.
 */
async function sharedContainerIds(appApi: DataverseAPI): Promise<string[]> {
  const units = await appApi.queryRecords<BusinessUnitRecord>(ENTITY_SETS.businessUnit, {
    $select: 'businessunitid,name,sprk_containerid',
  });
  const ids = units.map(u => u.sprk_containerid).filter((id): id is string => !!id);
  if (SHARED_DEFAULT_CONTAINER_ID) ids.push(SHARED_DEFAULT_CONTAINER_ID);
  return ids;
}

// ============================================================================
// Test Suite: Secure Project Creation — Happy Path
// ============================================================================

test.describe('Secure Project Creation Flow @e2e @secure-project', () => {
  let appApi: DataverseAPI;
  let creatorApi: DataverseAPI;

  /**
   * Track all resources created during tests for cleanup.
   * Each entry holds the entity set name and ID.
   */
  const resourcesToCleanup: { entitySet: string; id: string; label: string }[] = [];

  // --------------------------------------------------------------------------
  // Setup / Teardown
  // --------------------------------------------------------------------------

  test.beforeAll(async () => {
    appApi = await connectAppDataverse();
    creatorApi = connectCreatorDataverse();

    if (CREATOR_TOKENS_MISSING) {
      console.warn(`[E2E] ${CREATOR_TOKENS_REASON}. Set them in tests/e2e/config/.env.`);
    }
  });

  test.afterAll(async () => {
    // Only projects are tracked. The canonical Secure Record BU and its owner team are shared infrastructure and
    // are never deleted. SPE containers are not deleted here: their ids are logged for reconciliation.
    console.log(`[E2E] Cleaning up ${resourcesToCleanup.length} test resources...`);

    for (const resource of [...resourcesToCleanup].reverse()) {
      try {
        await appApi.deleteRecord(resource.entitySet, resource.id);
        console.log(`[E2E] Cleaned up: ${resource.label} (${resource.id})`);
      } catch (error) {
        console.warn(`[E2E] Cleanup failed for ${resource.label} (${resource.id}):`, error);
      }
    }
  });

  // --------------------------------------------------------------------------
  // Helper: track resource for cleanup
  // --------------------------------------------------------------------------

  function trackForCleanup(entitySet: string, id: string, label: string): void {
    resourcesToCleanup.push({ entitySet, id, label });
  }

  // --------------------------------------------------------------------------
  // Helper: query a Business Unit by ID
  // --------------------------------------------------------------------------

  async function queryBusinessUnit(buId: string): Promise<BusinessUnitRecord | null> {
    const results = await appApi.queryRecords<BusinessUnitRecord>(ENTITY_SETS.businessUnit, {
      $filter: `businessunitid eq ${buId}`,
      $select: 'businessunitid,name,_parentbusinessunitid_value',
      $top: '1',
    });
    return results[0] ?? null;
  }

  // ==========================================================================
  // TC-070-01: Standard Secure Project Creation (the wizard's path)
  // ==========================================================================

  test('TC-070-01: should provision full infrastructure for a new secure project', async () => {
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);
    const projectRef = generateProjectRef();
    const forbiddenContainers = await sharedContainerIds(appApi);

    // ── Arrange: the creator creates an ORDINARY project, as the wizard does (no sprk_issecure) ──
    const projectId = await creatorApi.createRecord(ENTITY_SETS.project, buildProjectPayload(projectRef));
    trackForCleanup(ENTITY_SETS.project, projectId, `secure project ${projectRef}`);

    const beforeProvisioning = await queryProject(creatorApi, projectId);
    expect(beforeProvisioning).not.toBeNull();
    // Provisioning, not the create, makes the project secure.
    expect(beforeProvisioning!.sprk_issecure).toBe(false);
    expect(beforeProvisioning!.sprk_containerid).toBeFalsy();
    const creatorSystemUserId = beforeProvisioning!._createdby_value;
    expect(creatorSystemUserId).toBeTruthy();

    // ── Act: the same creator calls provision-project ─────────────────────────
    const { status, body } = await callProvisionProject({ projectId, projectRef });
    const response = body as ProvisionProjectResponse;
    console.log(`[E2E] TC-070-01 provisioned ${projectId}: container ${response.speContainerId ?? '(none)'}`);

    // ── Assert: HTTP 200 with correct shape ───────────────────────────────────
    expect(status, JSON.stringify(body)).toBe(200);
    expect(response.businessUnitId).toBeTruthy();
    expect(response.businessUnitName).toBe(SECURE_BU_NAME);
    expect(response.ownerTeamId).toBeTruthy();
    expect(response.speContainerId).toBeTruthy();
    expect(response.resumed ?? false).toBe(false);

    // Assert INEQUALITY, not presence. A container that is a business unit's container or the shared default is
    // shared storage, which is the disclosure this whole mechanism exists to prevent.
    expect(forbiddenContainers).not.toContain(response.speContainerId);

    // Only the project is tracked for cleanup. The business unit is NOT tracked: it is the CANONICAL
    // `Secure Record` BU, shared by every secure project and created during environment setup. The retired version
    // of this test tracked it for deletion because provisioning used to create one per project. Running that against
    // the current endpoint would delete shared infrastructure. Provisioning creates no account either.

    // ── Assert: the resolved BU is the canonical one, not a per-project child ──
    const buRecord = await queryBusinessUnit(response.businessUnitId);
    expect(buRecord).not.toBeNull();
    expect(buRecord!.name).toBe(SECURE_BU_NAME);
    expect(buRecord!.name).not.toContain('SP-'); // no per-project BU was created

    // ── Assert: the project is OWNED by that BU's NAMED owner team ─────────────
    // This is the security-relevant outcome. Ownership puts the record in the Secure Record business unit, and per
    // design.md §5.1a no human holds access through it. Task 144: the owner is the NAMED team, never the BU's default
    // team (whose membership is every user placed in the BU).
    expect(response.ownerTeamName).toBe(SECURE_OWNER_TEAM_NAME);
    expect(response.ownerTeamName).not.toBe(SECURE_BU_NAME);
    expect(response.recordType).toBe('project');
    expect(response.recordId).toBe(projectId);

    // ── Assert: the creator, and only the creator, was shared to ──────────────
    expect(response.sharedToCreatorSystemUserId).toBe(creatorSystemUserId);

    // Read back AS THE CREATOR. A memberless team owns the record now, so this read succeeds only through the
    // creator's share.
    const projectRecord = await queryProject(creatorApi, projectId);
    expect(projectRecord).not.toBeNull();
    expect(projectRecord!.sprk_issecure).toBe(true);
    expect(projectRecord!._owningteam_value).toBe(response.ownerTeamId);

    // ── Assert: the container is recorded on the project ─────────────────────
    expect(projectRecord!.sprk_containerid).toBe(response.speContainerId);

    // ── Assert: no per-project security BU was stamped ───────────────────────
    expect(projectRecord!._sprk_securitybu_value).toBeFalsy();

    // ── Assert: a second call changes nothing (409 already_provisioned, no second container) ──
    const again = await callProvisionProject({ projectId, projectRef });
    expect(again.status, JSON.stringify(again.body)).toBe(409);
    expect((again.body as ProblemBody).reasonCode).toBe(REASON.alreadyProvisioned);
    const afterRepost = await queryProject(creatorApi, projectId);
    expect(afterRepost!.sprk_containerid).toBe(response.speContainerId);
    expect(afterRepost!._owningteam_value).toBe(response.ownerTeamId);
  });

  // ==========================================================================
  // TC-070-03: SPE Container Is Unique Per Project (Not Shared with BU)
  // ==========================================================================

  test('TC-070-03: each project gets its own isolated SPE container', async () => {
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);
    const projectRef1 = generateProjectRef();
    const projectRef2 = generateProjectRef();
    const forbiddenContainers = await sharedContainerIds(appApi);

    // The creator creates two ordinary projects
    const projectId1 = await creatorApi.createRecord(ENTITY_SETS.project, buildProjectPayload(projectRef1));
    trackForCleanup(ENTITY_SETS.project, projectId1, `secure project 1 — ${projectRef1}`);

    const projectId2 = await creatorApi.createRecord(ENTITY_SETS.project, buildProjectPayload(projectRef2));
    trackForCleanup(ENTITY_SETS.project, projectId2, `secure project 2 — ${projectRef2}`);

    // ...and secures both
    const [result1, result2] = await Promise.all([
      callProvisionProject({ projectId: projectId1, projectRef: projectRef1 }),
      callProvisionProject({ projectId: projectId2, projectRef: projectRef2 }),
    ]);

    const response1 = result1.body as ProvisionProjectResponse;
    const response2 = result2.body as ProvisionProjectResponse;
    console.log(
      `[E2E] TC-070-03 containers: ${projectId1} → ${response1.speContainerId ?? '(none)'}, ` +
        `${projectId2} → ${response2.speContainerId ?? '(none)'}`
    );

    expect(result1.status, JSON.stringify(result1.body)).toBe(200);
    expect(result2.status, JSON.stringify(result2.body)).toBe(200);

    // Nothing extra to track: provisioning creates no accounts and no per-project business units. The canonical
    // Secure Record BU is shared infrastructure and must NEVER be tracked for deletion. The retired version of this
    // test queued it twice.

    // The assertion this test exists for, unchanged by the re-scope: each secure project MUST get its OWN SPE
    // container. A shared container is the disclosure.
    expect(response1.speContainerId).toBeTruthy();
    expect(response2.speContainerId).toBeTruthy();
    expect(response1.speContainerId).not.toBe(response2.speContainerId);
    expect(forbiddenContainers).not.toContain(response1.speContainerId);
    expect(forbiddenContainers).not.toContain(response2.speContainerId);

    // Both projects now resolve to the SAME business unit. That is the inverse of the old expectation, and the point
    // of design.md §5.1's "no BU-per-project proliferation".
    expect(response1.businessUnitId).toBe(response2.businessUnitId);
    expect(response1.businessUnitName).toBe(SECURE_BU_NAME);
    expect(response2.businessUnitName).toBe(SECURE_BU_NAME);

    // ...and to the same owner team, while still holding distinct containers.
    expect(response1.ownerTeamId).toBe(response2.ownerTeamId);
  });
});

// ============================================================================
// Test Suite: Secure Project Creation — Validation & Error Paths
// ============================================================================

test.describe('Secure Project Creation — Validation & Error Paths @e2e @secure-project', () => {
  let appApi: DataverseAPI;
  let creatorApi: DataverseAPI;

  const resourcesToCleanup: { entitySet: string; id: string; label: string }[] = [];

  test.beforeAll(async () => {
    appApi = await connectAppDataverse();
    creatorApi = connectCreatorDataverse();
  });

  test.afterAll(async () => {
    for (const resource of [...resourcesToCleanup].reverse()) {
      try {
        await appApi.deleteRecord(resource.entitySet, resource.id);
      } catch {
        console.warn(`[E2E] Cleanup failed for ${resource.label} (${resource.id})`);
      }
    }
  });

  function trackForCleanup(entitySet: string, id: string, label: string): void {
    resourcesToCleanup.push({ entitySet, id, label });
  }

  // ==========================================================================
  // TC-070-10: Gate — Empty ProjectId
  // ==========================================================================

  // DelegationRuleFilter runs before the handler and answers 403 on EVERY exit path, including the ones that could
  // arguably be 400 or 404. That way an unauthorized caller cannot enumerate records. The handler's own 400 is not
  // reachable for a request that names no record.
  test('TC-070-10: should return 403 (never 400) when ProjectId is the empty GUID', async () => {
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);
    const { status, body } = await callProvisionProject({
      projectId: '00000000-0000-0000-0000-000000000000',
      projectRef: 'E2E-VALIDATION',
    });

    expect(status, JSON.stringify(body)).toBe(403);
    expect((body as ProblemBody).reasonCode).toBe(REASON.delegationTargetUnresolved);
  });

  // ==========================================================================
  // TC-070-12: Gate — Project Does Not Exist
  // ==========================================================================

  // A caller cannot hold Write on a record that does not exist, so the delegation filter refuses with 403. A 404 here
  // would tell an unauthorized caller which ids exist.
  test('TC-070-12: should return 403 (never 404) when the project does not exist in Dataverse', async () => {
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);
    const nonExistentId = 'ffffffff-ffff-ffff-ffff-ffffffffffff';

    const { status, body } = await callProvisionProject({
      projectId: nonExistentId,
      projectRef: 'E2E-NOT-FOUND',
    });

    expect(status, JSON.stringify(body)).toBe(403);
    expect([REASON.delegationWriteRequired, REASON.delegationCheckFailed]).toContain((body as ProblemBody).reasonCode);
  });

  // ==========================================================================
  // TC-070-13: Creator rule — an ordinary project the caller did NOT create
  // ==========================================================================

  // Was "400 when the project is not a Secure Project". Since task 150 an unflagged project is the NORMAL input, and
  // provisioning is what flags it. It is secured that way only for the person who created it (owner round 10 item 10).
  // Here the APP identity creates the project, so `createdby` is an application user and no `sprk_createdbyperson` is
  // stamped (only the BFF stamps it). The creator is then refused: by the delegation filter when they cannot write
  // the record, otherwise by the creator rule. Either way the refusal comes before any write.
  test('TC-070-13: should refuse to secure an ordinary project the caller did not create, changing nothing', async () => {
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);
    const projectRef = `E2E-NC-${Date.now()}`;
    const projectId = await appApi.createRecord(
      ENTITY_SETS.project,
      buildProjectPayload(projectRef, 'Not-Mine Project')
    );
    trackForCleanup(ENTITY_SETS.project, projectId, `app-created project ${projectRef}`);

    const before = await queryProject(appApi, projectId);
    expect(before).not.toBeNull();

    const { status, body } = await callProvisionProject({ projectId, projectRef });

    expect(status, JSON.stringify(body)).toBe(403);
    expect([REASON.notRecordCreator, REASON.delegationWriteRequired]).toContain((body as ProblemBody).reasonCode);

    // Nothing changed: not flagged, not moved, no container.
    const after = await queryProject(appApi, projectId);
    expect(after).not.toBeNull();
    expect(after!.sprk_issecure).not.toBe(true);
    expect(after!._owningteam_value ?? null).toBe(before!._owningteam_value ?? null);
    expect(after!._owninguser_value ?? null).toBe(before!._owninguser_value ?? null);
    expect(after!.sprk_containerid).toBeFalsy();
  });

  // ==========================================================================
  // TC-070-14: Fail closed — the configured Secure Record BU does not exist
  // ==========================================================================

  // OPERATOR-ARMED (task 047 step 7). It runs only when an operator has pointed the BFF's
  // `SecureRecord:BusinessUnitName` at a name that does not exist and set E2E_SECURE_BU_MISCONFIGURED=true. Restore the
  // setting afterwards: while it is wrong, EVERY secure create in the environment fails. Provisioning must fail closed
  // with `sdap.provision.secure_bu_not_found` before any write, and never fall back to the root or the caller's BU.
  // Offline: ProvisionProject_WhenTheSecureBusinessUnitIsAbsent_FailsClosedAndProvisionsNothing.
  test('TC-070-14: should fail closed, changing nothing, when the configured Secure Record BU is absent', async () => {
    test.skip(
      process.env.E2E_SECURE_BU_MISCONFIGURED !== 'true',
      'Operator-armed: set SecureRecord:BusinessUnitName to a nonexistent name and E2E_SECURE_BU_MISCONFIGURED=true'
    );
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);

    const projectRef = `E2E-BU-NF-${Date.now()}`;
    const projectId = await creatorApi.createRecord(ENTITY_SETS.project, buildProjectPayload(projectRef));
    trackForCleanup(ENTITY_SETS.project, projectId, `project for secure-BU-not-found test`);
    const before = await queryProject(creatorApi, projectId);
    expect(before).not.toBeNull();

    const { status, body } = await callProvisionProject({ projectId, projectRef });

    expect(status, JSON.stringify(body)).toBe(500);
    expect((body as ProblemBody).reasonCode).toBe(REASON.secureBuNotFound);

    const after = await queryProject(creatorApi, projectId);
    expect(after).not.toBeNull();
    expect(after!.sprk_issecure).toBe(false);
    expect(after!._owninguser_value).toBe(before!._owninguser_value);
    expect(after!._owningteam_value ?? null).toBe(before!._owningteam_value ?? null);
    expect(after!.sprk_containerid).toBeFalsy();
  });

  // ==========================================================================
  // TC-070-15: Unauthorized — No Bearer Token
  // ==========================================================================

  test('TC-070-15: should return 401 when no authorization token is provided', async () => {
    const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        projectId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        projectRef: 'E2E-UNAUTH',
      }),
    });

    expect(response.status).toBe(401);
  });
});

// ============================================================================
// Test Suite: Infrastructure References Verification
// ============================================================================

test.describe('Secure Project — Infrastructure Reference Verification @e2e @secure-project', () => {
  let appApi: DataverseAPI;
  let creatorApi: DataverseAPI;

  const resourcesToCleanup: { entitySet: string; id: string; label: string }[] = [];

  test.beforeAll(async () => {
    appApi = await connectAppDataverse();
    creatorApi = connectCreatorDataverse();
  });

  test.afterAll(async () => {
    for (const resource of [...resourcesToCleanup].reverse()) {
      try {
        await appApi.deleteRecord(resource.entitySet, resource.id);
      } catch {
        console.warn(`[E2E] Cleanup failed for ${resource.label}`);
      }
    }
  });

  function trackForCleanup(entitySet: string, id: string, label: string): void {
    resourcesToCleanup.push({ entitySet, id, label });
  }

  // ==========================================================================
  // TC-070-20: Field Completeness — the container reference is stored, and no
  //            per-project security BU is stamped
  //
  // Was "all THREE references". Two of the three should never have existed:
  //   - sprk_securitybuid  → there is no per-project BU to reference
  //   - sprk_externalaccountid → the real column, sprk_externalaccount, is the
  //     project's CLIENT; writing it would overwrite the client
  // Neither column name even existed on the table, which is why the write
  // silently failed for five months.
  // ==========================================================================

  test('TC-070-20: the container reference is stored and no per-project security BU is stamped', async () => {
    test.skip(CREATOR_TOKENS_MISSING, CREATOR_TOKENS_REASON);
    const projectRef = `E2E-REFS-${Date.now()}`;
    const projectId = await creatorApi.createRecord(
      ENTITY_SETS.project,
      buildProjectPayload(projectRef, 'Reference Check')
    );
    trackForCleanup(ENTITY_SETS.project, projectId, `project ${projectRef}`);

    // Provision, as the creator
    const { status, body } = await callProvisionProject({ projectId, projectRef });
    expect(status, JSON.stringify(body)).toBe(200);
    const provisionResult = body as ProvisionProjectResponse;
    console.log(`[E2E] TC-070-20 provisioned ${projectId}: container ${provisionResult.speContainerId}`);

    // Provisioning creates no account and no per-project BU, so there is nothing extra to clean up. The canonical
    // Secure Record BU must NEVER be tracked for deletion: it is shared infrastructure.

    // Read the project back from Dataverse, as the creator, to verify field persistence.
    const record = await queryProject(creatorApi, projectId);
    expect(record).not.toBeNull();

    // sprk_issecure, set by provisioning (task 150), never by the create
    expect(record!.sprk_issecure).toBe(true);

    // sprk_containerid: the project's own container, the ONE reference provisioning records
    expect(record!.sprk_containerid).toBeTruthy();
    expect(record!.sprk_containerid).toBe(provisionResult.speContainerId);

    // Ownership: the security-relevant outcome
    expect(record!._owningteam_value).toBe(provisionResult.ownerTeamId);

    // No per-project security BU is stamped any more
    expect(record!._sprk_securitybu_value).toBeFalsy();
  });
});

// ============================================================================
// Manual Execution Notes
// ============================================================================

/**
 * NOTE: These are E2E tests that require a deployed environment.
 *
 * Prerequisites before running:
 *   1. BFF API deployed to dev: https://spaarke-bff-dev.azurewebsites.net
 *   2. Dataverse dev environment available: https://spaarkedev1.crm.dynamics.com
 *   3. SharePointEmbedded:ContainerTypeId configured on the BFF API
 *   4. An app registration (CLIENT_ID) with Dataverse access, used only for lookups and cleanup. It needs Delete on
 *      sprk_project, because the creator's share does not include Delete.
 *   5. ONE internal test user (the creator) with Create + Write on sprk_project, and delegated tokens for that user.
 *   6. Configure tests/e2e/config/.env with:
 *      TENANT_ID=<your-tenant-id>
 *      CLIENT_ID=<app-client-id>
 *      CLIENT_SECRET=<app-client-secret>
 *      BFF_API_URL=https://spaarke-bff-dev.azurewebsites.net
 *      DATAVERSE_API_URL=https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2
 *      SECURE_CREATOR_BFF_TOKEN=<delegated token for the creator, audience the BFF app>
 *      SECURE_CREATOR_DATAVERSE_TOKEN=<delegated token for the SAME user, audience Dataverse>
 *      SPE_DEFAULT_CONTAINER_ID=<optional: the BFF's SharePointEmbedded:DefaultContainerId>
 *      E2E_SECURE_BU_MISCONFIGURED=<true only while an operator has broken SecureRecord:BusinessUnitName (TC-070-14)>
 *
 * Without the two creator tokens, every case except TC-070-15 skips with a reason.
 *
 * Run all secure project creation tests:
 *   npx playwright test secure-project-creation.spec.ts
 *
 * Run a single test case by ID:
 *   npx playwright test secure-project-creation.spec.ts -g "TC-070-01"
 *
 * Run with visible output:
 *   npx playwright test secure-project-creation.spec.ts --reporter=list
 *
 * Expected test execution time: ~2-5 minutes (depends on Dataverse and SPE latency)
 *
 * NOTE: SPE container creation requires the BFF API to have valid Graph credentials with the
 * FileStorageContainer.Selected scope. Without it, TC-070-01, TC-070-03 and TC-070-20 fail with a 500 response
 * (`sdap.provision.container_creation_failed`). TC-070-10 through TC-070-15 still pass, because they test exit paths
 * that come before SPE container creation.
 */
