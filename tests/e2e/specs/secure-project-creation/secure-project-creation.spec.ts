/**
 * E2E Tests: Secure Project Creation Flow
 *
 * ⚠️ PARTIALLY OBSOLETE AS OF 2026-08-25 — READ BEFORE RUNNING.
 *
 * `unified-access-control-r2` task 021 re-scoped `/provision-project`. The mechanism most of this
 * spec was written against no longer exists:
 *
 *   REMOVED  child Business Unit per project (named SP-{ProjectRef})
 *   REMOVED  External Access Account per project
 *   REMOVED  umbrella-BU reuse (there is now ONE canonical `Secure Project` BU, resolved by name
 *            from server configuration, so there is nothing for a caller to select)
 *   REMOVED  the BU-rollback path (nothing destructive is created any more)
 *   REMOVED  the stamps `sprk_securitybuid` / `sprk_specontainerid` / `sprk_externalaccountid` —
 *            none of those columns ever existed on sprk_project, which is why the write silently
 *            failed for five months. `sprk_externalaccount` (the real column) is the project's
 *            CLIENT lookup and must NEVER be written by provisioning.
 *
 *   NOW      1. Resolve the canonical `Secure Project` BU by name
 *            2. Assign the project to that BU's DEFAULT OWNER TEAM (verified by read-back)
 *            3. Create the project's own SPE container
 *            4. Record it on `sprk_containerid` — and FAIL LOUDLY if that write does not land
 *
 * Cases that exercised a deleted mechanism are `test.skip`ped below with a per-case reason rather
 * than rewritten. They need re-authoring against a live environment, which is the only place this
 * spec can be validated; rewriting them blind would produce assertions that look verified and are
 * not. This file is not run by CI (no workflow references tests/e2e).
 *
 * NOTE, independent of task 021: the payload builders below use `sprk_projectref` and
 * `sprk_description`, neither of which exists on live `sprk_project` (the description column is
 * `sprk_projectdescription`). This spec therefore could not have passed as written, whatever the
 * endpoint did. Fix that as part of the re-authoring.
 *
 * Still valid as written: the 401 case, the not-a-secure-project case, the non-existent-project
 * case, and per-project container isolation.
 *
 * Tests validate the end-to-end secure project creation pipeline:
 *   1. Create project record with sprk_issecure = true (via Dataverse API)
 *   2. Call POST /api/v1/external-access/provision-project
 *   3. Verify the project is owned by the Secure Project BU's default owner team
 *   4. Verify SPE container provisioned and ID returned
 *   5. Verify the project record's sprk_containerid points at it
 *   6. Clean up all test data after verification
 *
 * Also covers:
 *   - Validation error paths (missing ProjectId, not-secure project, non-existent project)
 *
 * Prerequisites:
 *   - BFF API deployed to dev with /api/v1/external-access/* endpoints enabled
 *   - Dataverse dev environment configured (spaarkedev1.crm.dynamics.com)
 *   - Entra External ID provider configured (task 020 complete)
 *   - Azure AD app with Dataverse + Graph API permissions
 *   - SharePointEmbedded:ContainerTypeId configured on BFF API
 *   - Authentication credentials in .env
 *
 * @see tasks/070-e2e-test-secure-project-creation.poml
 * @see tasks/011-grant-access-endpoint.poml
 * @see src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs
 */

import { test, expect } from '@playwright/test';
import { DataverseAPI } from '../../utils/dataverse-api';

// ============================================================================
// Constants
// ============================================================================

const BFF_API_BASE = process.env.BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net';
const DATAVERSE_API_URL = process.env.DATAVERSE_API_URL || 'https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2';

/** Dataverse entity set names */
const ENTITY_SETS = {
  project: 'sprk_projects',
  businessUnit: 'businessunits',
  account: 'accounts',
} as const;

/** BFF external access endpoint base path */
const EXTERNAL_ACCESS_BASE = `${BFF_API_BASE}/api/v1/external-access`;

/**
 * The canonical Secure Project business unit's name.
 *
 * SINGULAR — verified against live Dataverse metadata 2026-08-25. Must match whatever the BFF's
 * `SecureProject:BusinessUnitName` is set to in the target environment (default `Secure Project`).
 * This business unit is shared by every secure project and is created during environment setup;
 * tests must never delete it.
 */
const SECURE_BU_NAME = process.env.SECURE_PROJECT_BU_NAME || 'Secure Project';

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
 * Minimal Dataverse project record payload with sprk_issecure = true.
 * Only includes required fields for a test project.
 */
function buildSecureProjectPayload(projectRef: string): Record<string, unknown> {
  return {
    sprk_projectname: `E2E Test Secure Project — ${projectRef}`,
    sprk_projectref: projectRef,
    sprk_issecure: true,
    sprk_description: 'Created by E2E test — safe to delete',
  };
}

/**
 * Minimal project payload with sprk_issecure = false (for negative testing).
 */
function buildNonSecureProjectPayload(projectRef: string): Record<string, unknown> {
  return {
    sprk_projectname: `E2E Test Non-Secure Project — ${projectRef}`,
    sprk_projectref: projectRef,
    sprk_issecure: false,
    sprk_description: 'Created by E2E test — safe to delete',
  };
}

// ============================================================================
// Types
// ============================================================================

/** Mirrors the task-021 response shape. */
interface ProvisionProjectResponse {
  /** The canonical Secure Project BU — resolved by name, not created. */
  businessUnitId: string;
  businessUnitName: string;
  /** That BU's default owner team, which now owns the project. */
  ownerTeamId: string;
  ownerTeamName: string;
  speContainerId: string;
}

/**
 * Columns that actually exist on live `sprk_project` (verified 2026-08-25).
 *
 * `_sprk_securitybuid_value`, `sprk_specontainerid` and `_sprk_externalaccountid_value` — which this
 * spec previously declared — do not exist on the table at all. `sprk_specontainerid` belongs to
 * `sprk_container`, which is where the name was borrowed from.
 */
interface ProjectRecord {
  sprk_projectid: string;
  sprk_projectname: string;
  sprk_issecure: boolean;
  /** The project's own SPE container, written by provisioning. */
  sprk_containerid?: string;
  /** The owning team — the observable proof that provisioning secured the record. */
  _owningteam_value?: string;
  /** Retired per-project security BU. Present only on legacy rows; never written now. */
  _sprk_securitybu_value?: string;
}

/**
 * The RETIRED response and record shapes, kept so the `test.skip`ped legacy cases below still
 * type-check (TypeScript checks skipped bodies even though Playwright does not run them).
 *
 * This is a record of what the contract used to be, not something to build on. Every member here
 * either no longer exists on the response or names a column that never existed on `sprk_project`.
 * Delete this block when those cases are re-authored against a live environment.
 */
interface LegacyProvisionProjectResponse {
  businessUnitId: string;
  businessUnitName: string;
  speContainerId: string;
  accountId: string;
  accountName: string;
  wasUmbrellaBu: boolean;
}

interface LegacyProjectRecord {
  sprk_projectid: string;
  sprk_issecure: boolean;
  /** Never existed on sprk_project. */
  _sprk_securitybuid_value?: string;
  /** Belongs to sprk_container, not sprk_project. */
  sprk_specontainerid?: string;
  /** Never existed; the real column, sprk_externalaccount, is the CLIENT. */
  _sprk_externalaccountid_value?: string;
}

interface BusinessUnitRecord {
  businessunitid: string;
  name: string;
  _parentbusinessunitid_value?: string;
}

interface AccountRecord {
  accountid: string;
  name: string;
  _owningbusinessunit_value?: string;
}

// ============================================================================
// Test Suite: Secure Project Creation — Happy Path
// ============================================================================

test.describe('Secure Project Creation Flow @e2e @secure-project', () => {
  let dataverseApi: DataverseAPI;
  let bffToken: string;

  /**
   * Track all resources created during tests for cleanup.
   * Each entry holds the entity set name and ID.
   */
  const resourcesToCleanup: { entitySet: string; id: string; label: string }[] = [];

  // --------------------------------------------------------------------------
  // Setup / Teardown
  // --------------------------------------------------------------------------

  test.beforeAll(async () => {
    // Authenticate with Dataverse for record verification and cleanup
    const dvToken = await DataverseAPI.authenticate(
      process.env.TENANT_ID || '',
      process.env.CLIENT_ID || '',
      process.env.CLIENT_SECRET || '',
      DATAVERSE_API_URL
    );
    dataverseApi = new DataverseAPI(DATAVERSE_API_URL, dvToken);

    // Obtain BFF API token (client credentials against the BFF app registration)
    const tokenResponse = await fetch(`https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: process.env.CLIENT_ID || '',
        client_secret: process.env.CLIENT_SECRET || '',
        scope: `api://${process.env.BFF_CLIENT_ID || process.env.CLIENT_ID}/.default`,
      }),
    });

    const tokenJson = (await tokenResponse.json()) as { access_token?: string };
    bffToken = tokenJson.access_token || '';

    if (!bffToken) {
      console.warn(
        '[E2E] BFF API token not obtained. Tests that call the BFF API will fail. ' +
          'Ensure BFF_CLIENT_ID and credentials are set in .env.'
      );
    }
  });

  test.afterAll(async () => {
    // Clean up all test records in reverse creation order (most specific first)
    // Order: Account → Business Unit → SPE Container (via BFF if needed) → Project
    console.log(`[E2E] Cleaning up ${resourcesToCleanup.length} test resources...`);

    for (const resource of [...resourcesToCleanup].reverse()) {
      try {
        await dataverseApi.deleteRecord(resource.entitySet, resource.id);
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
  // Helper: call provision-project endpoint
  // --------------------------------------------------------------------------

  async function callProvisionProject(body: {
    projectId: string;
    projectRef?: string;
    /**
     * REMOVED from the real request contract by task 021 — retained on this helper ONLY so the
     * skipped legacy cases below still type-check (TypeScript compiles skipped tests). The server
     * ignores unknown JSON properties. Delete this when those cases are re-authored.
     */
    umbrellaBuId?: string;
  }): Promise<{ status: number; body: unknown }> {
    const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${bffToken}`,
      },
      body: JSON.stringify(body),
    });

    const responseBody = await response.json().catch(() => ({}));
    return { status: response.status, body: responseBody };
  }

  // --------------------------------------------------------------------------
  // Helper: query Dataverse project record with infrastructure fields
  // --------------------------------------------------------------------------

  async function queryProject(projectId: string): Promise<ProjectRecord | null> {
    try {
      const results = await dataverseApi.queryRecords<ProjectRecord>(ENTITY_SETS.project, {
        $filter: `sprk_projectid eq ${projectId}`,
        // Live columns only (verified 2026-08-25). The three names this helper previously
        // projected — _sprk_securitybuid_value, sprk_specontainerid,
        // _sprk_externalaccountid_value — do not exist on sprk_project, so this query was a 400
        // and the catch below turned it into a silent null. Every assertion built on it was
        // therefore vacuous.
        $select: [
          'sprk_projectid',
          'sprk_projectname',
          'sprk_issecure',
          'sprk_containerid',
          '_owningteam_value',
          '_sprk_securitybu_value',
        ].join(','),
        $top: '1',
      });
      return results[0] ?? null;
    } catch {
      return null;
    }
  }

  // --------------------------------------------------------------------------
  // Helper: query a Business Unit by ID
  // --------------------------------------------------------------------------

  async function queryBusinessUnit(buId: string): Promise<BusinessUnitRecord | null> {
    try {
      const results = await dataverseApi.queryRecords<BusinessUnitRecord>(ENTITY_SETS.businessUnit, {
        $filter: `businessunitid eq ${buId}`,
        $select: 'businessunitid,name,_parentbusinessunitid_value',
        $top: '1',
      });
      return results[0] ?? null;
    } catch {
      return null;
    }
  }

  // --------------------------------------------------------------------------
  // Helper: query Account owned by a specific Business Unit
  // --------------------------------------------------------------------------

  async function queryAccountForBu(buId: string): Promise<AccountRecord | null> {
    try {
      const results = await dataverseApi.queryRecords<AccountRecord>(ENTITY_SETS.account, {
        $filter: `_owningbusinessunit_value eq ${buId}`,
        $select: 'accountid,name,_owningbusinessunit_value',
        $top: '1',
      });
      return results[0] ?? null;
    } catch {
      return null;
    }
  }

  // ==========================================================================
  // TC-070-01: Standard Secure Project Creation (new BU path)
  // ==========================================================================

  test('TC-070-01: should provision full infrastructure for a new secure project', async () => {
    const projectRef = generateProjectRef();

    // ── Arrange: Create a project record in Dataverse with sprk_issecure = true ──
    const projectPayload = buildSecureProjectPayload(projectRef);
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, projectPayload);
    trackForCleanup(ENTITY_SETS.project, projectId, `secure project ${projectRef}`);

    // ── Act: Call provision-project endpoint ─────────────────────────────────
    const { status, body } = await callProvisionProject({ projectId, projectRef });
    const response = body as ProvisionProjectResponse;

    // ── Assert: HTTP 200 with correct shape ───────────────────────────────────
    expect(status).toBe(200);
    expect(response.businessUnitId).toBeTruthy();
    expect(response.businessUnitName).toBe(SECURE_BU_NAME);
    expect(response.ownerTeamId).toBeTruthy();
    expect(response.speContainerId).toBeTruthy();

    // Track ONLY the SPE container-bearing project for cleanup.
    //
    // Deliberately NOT tracking the business unit: it is the CANONICAL `Secure Project` BU, shared
    // by every secure project and created during environment setup. The retired version of this test
    // tracked it for deletion because provisioning created it per project — running that against the
    // new endpoint would delete shared infrastructure. Nor is there an account to clean up.

    // ── Assert: the resolved BU is the canonical one, not a per-project child ──
    const buRecord = await queryBusinessUnit(response.businessUnitId);
    expect(buRecord).not.toBeNull();
    expect(buRecord!.name).toBe(SECURE_BU_NAME);
    expect(buRecord!.name).not.toContain('SP-'); // no per-project BU was created

    // ── Assert: the project is OWNED by that BU's default owner team ──────────
    // This is the security-relevant outcome. Ownership is what puts the record in the Secure Project
    // business unit, and per design.md §5.1a no human holds access through it.
    const projectRecord = await queryProject(projectId);
    expect(projectRecord).not.toBeNull();
    expect(projectRecord!.sprk_issecure).toBe(true);
    expect(projectRecord!._owningteam_value).toBe(response.ownerTeamId);

    // ── Assert: the container is recorded on the project ─────────────────────
    expect(projectRecord!.sprk_containerid).toBe(response.speContainerId);

    // ── Assert: the CLIENT lookup was not touched ────────────────────────────
    // sprk_externalaccount is the project's client. Provisioning must never write it — had the
    // retired stamp's column name been repaired instead of removed, it would have overwritten the
    // client with a synthetic "External Access — {project}" account.
    expect(projectRecord!._sprk_securitybu_value).toBeFalsy();
  });

  // ==========================================================================
  // TC-070-02: Umbrella BU Reuse — Multi-Project Organisation
  // ==========================================================================

  // OBSOLETE (task 021, 2026-08-25): umbrella-BU reuse was one branch of "create a BU per project
  // or reuse this one". Neither branch survives — there is ONE canonical `Secure Project` BU,
  // resolved by name from server configuration, so a caller has no BU to select and `umbrellaBuId`
  // no longer exists on the request. Nothing to re-author: the scenario itself is gone.
  test.skip('TC-070-02: should reuse an existing umbrella BU and Account for a multi-project org', async () => {
    const orgName = `E2E Org ${Date.now()}`;

    // ── Arrange: Create a root-level Account to act as the "umbrella" org ─────
    // First, we need a BU to own the Account (simulate an existing umbrella BU).
    // We create a BU and Account pair as if they were set up by a previous project.
    const umbrellaBuPayload = {
      name: `E2E-Umbrella-BU-${Date.now()}`,
      description: 'E2E test umbrella BU — safe to delete',
    };
    const umbrellaBuId = await dataverseApi.createRecord(ENTITY_SETS.businessUnit, umbrellaBuPayload);
    trackForCleanup(ENTITY_SETS.businessUnit, umbrellaBuId, `umbrella BU for ${orgName}`);

    const umbrellaAccountPayload = {
      name: `External Access — ${orgName}`,
      description: 'E2E test umbrella account — safe to delete',
      'owningbusinessunit@odata.bind': `/businessunits(${umbrellaBuId})`,
    };
    const umbrellaAccountId = await dataverseApi.createRecord(ENTITY_SETS.account, umbrellaAccountPayload);
    trackForCleanup(ENTITY_SETS.account, umbrellaAccountId, `umbrella account for ${orgName}`);

    // Create a new project that will reuse the umbrella BU
    const projectRef = generateProjectRef();
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, buildSecureProjectPayload(projectRef));
    trackForCleanup(ENTITY_SETS.project, projectId, `secure project ${projectRef} (umbrella)`);

    // ── Act: Provision with UmbrellaBuId — should skip BU and Account creation ─
    const { status, body } = await callProvisionProject({
      projectId,
      projectRef,
      umbrellaBuId,
    });
    const response = body as LegacyProvisionProjectResponse;

    // ── Assert: Returns 200 with umbrella BU references ───────────────────────
    expect(status).toBe(200);
    expect(response.businessUnitId).toBe(umbrellaBuId);
    expect(response.accountId).toBe(umbrellaAccountId);
    expect(response.wasUmbrellaBu).toBe(true);
    expect(response.speContainerId).toBeTruthy(); // SPE container still provisioned per project

    // ── Assert: No new BU was created (only the umbrella BU exists for this org) ─
    // (Verified by wasUmbrellaBu=true and businessUnitId matching the input umbrellaBuId)

    // ── Assert: Project record references the umbrella infrastructure ──────────
    const projectRecord = (await queryProject(projectId)) as LegacyProjectRecord | null;
    expect(projectRecord).not.toBeNull();
    expect(projectRecord!._sprk_securitybuid_value).toBe(umbrellaBuId);
    expect(projectRecord!._sprk_externalaccountid_value).toBe(umbrellaAccountId);
    expect(projectRecord!.sprk_specontainerid).toBe(response.speContainerId);
  });

  // ==========================================================================
  // TC-070-03: SPE Container Is Unique Per Project (Not Shared with BU)
  // ==========================================================================

  test('TC-070-03: each project gets its own isolated SPE container', async () => {
    const projectRef1 = generateProjectRef();
    const projectRef2 = generateProjectRef();

    // Create two separate secure projects
    const projectId1 = await dataverseApi.createRecord(ENTITY_SETS.project, buildSecureProjectPayload(projectRef1));
    trackForCleanup(ENTITY_SETS.project, projectId1, `secure project 1 — ${projectRef1}`);

    const projectId2 = await dataverseApi.createRecord(ENTITY_SETS.project, buildSecureProjectPayload(projectRef2));
    trackForCleanup(ENTITY_SETS.project, projectId2, `secure project 2 — ${projectRef2}`);

    // Provision both
    const [result1, result2] = await Promise.all([
      callProvisionProject({ projectId: projectId1, projectRef: projectRef1 }),
      callProvisionProject({ projectId: projectId2, projectRef: projectRef2 }),
    ]);

    const response1 = result1.body as ProvisionProjectResponse;
    const response2 = result2.body as ProvisionProjectResponse;

    expect(result1.status).toBe(200);
    expect(result2.status).toBe(200);

    // Nothing extra to track: no accounts and no per-project business units are created. The
    // canonical Secure Project BU is shared infrastructure and must NEVER be tracked for deletion —
    // the retired version of this test queued it twice.

    // The assertion this test exists for, and it survives the re-scope unchanged: each secure
    // project MUST get its OWN SPE container. A shared container is the disclosure.
    expect(response1.speContainerId).toBeTruthy();
    expect(response2.speContainerId).toBeTruthy();
    expect(response1.speContainerId).not.toBe(response2.speContainerId);

    // Both projects now resolve to the SAME business unit — the inverse of the old expectation, and
    // the point of design.md §5.1's "no BU-per-project proliferation".
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
  let dataverseApi: DataverseAPI;
  let bffToken: string;

  const resourcesToCleanup: { entitySet: string; id: string; label: string }[] = [];

  test.beforeAll(async () => {
    const dvToken = await DataverseAPI.authenticate(
      process.env.TENANT_ID || '',
      process.env.CLIENT_ID || '',
      process.env.CLIENT_SECRET || '',
      DATAVERSE_API_URL
    );
    dataverseApi = new DataverseAPI(DATAVERSE_API_URL, dvToken);

    const tokenResponse = await fetch(`https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: process.env.CLIENT_ID || '',
        client_secret: process.env.CLIENT_SECRET || '',
        scope: `api://${process.env.BFF_CLIENT_ID || process.env.CLIENT_ID}/.default`,
      }),
    });

    const tokenJson = (await tokenResponse.json()) as { access_token?: string };
    bffToken = tokenJson.access_token || '';
  });

  test.afterAll(async () => {
    for (const resource of [...resourcesToCleanup].reverse()) {
      try {
        await dataverseApi.deleteRecord(resource.entitySet, resource.id);
      } catch {
        console.warn(`[E2E] Cleanup failed for ${resource.label} (${resource.id})`);
      }
    }
  });

  function trackForCleanup(entitySet: string, id: string, label: string): void {
    resourcesToCleanup.push({ entitySet, id, label });
  }

  async function callProvisionProject(body: Record<string, unknown>): Promise<{ status: number; body: unknown }> {
    const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${bffToken}`,
      },
      body: JSON.stringify(body),
    });

    const responseBody = await response.json().catch(() => ({}));
    return { status: response.status, body: responseBody };
  }

  // ==========================================================================
  // TC-070-10: Validation — Empty ProjectId
  // ==========================================================================

  test('TC-070-10: should return 400 when ProjectId is empty GUID', async () => {
    const { status, body } = await callProvisionProject({
      projectId: '00000000-0000-0000-0000-000000000000',
      projectRef: 'E2E-VALIDATION',
    });

    expect(status).toBe(400);
    const problem = body as { title?: string; detail?: string };
    expect(problem.title).toMatch(/validation|bad request/i);
  });

  // ==========================================================================
  // TC-070-11: Validation — Missing ProjectRef (when UmbrellaBuId not provided)
  // ==========================================================================

  // OBSOLETE (task 021): the rule under test was "ProjectRef is required unless UmbrellaBuId is
  // provided", and it existed only because ProjectRef named the per-project BU (SP-{ProjectRef}).
  // With no BU to name, ProjectRef is a display-name fallback and is genuinely optional, so this
  // request is now VALID rather than a 400.
  test.skip('TC-070-11: should return 400 when ProjectRef is missing and no UmbrellaBuId', async () => {
    const { status, body } = await callProvisionProject({
      projectId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      // No projectRef, no umbrellaBuId
    });

    expect(status).toBe(400);
    const problem = body as { detail?: string };
    expect(problem.detail).toMatch(/projectRef|required/i);
  });

  // ==========================================================================
  // TC-070-12: Not Found — Project Does Not Exist
  // ==========================================================================

  test('TC-070-12: should return 404 when project does not exist in Dataverse', async () => {
    const nonExistentId = 'ffffffff-ffff-ffff-ffff-ffffffffffff';

    const { status, body } = await callProvisionProject({
      projectId: nonExistentId,
      projectRef: 'E2E-NOT-FOUND',
    });

    expect(status).toBe(404);
    const problem = body as { title?: string; detail?: string };
    expect(problem.title).toMatch(/not found/i);
    expect(problem.detail).toContain(nonExistentId);
  });

  // ==========================================================================
  // TC-070-13: Validation — Project Exists But sprk_issecure = false
  // ==========================================================================

  test('TC-070-13: should return 400 when project exists but is not a Secure Project', async () => {
    const projectRef = `E2E-NS-${Date.now()}`;
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, {
      sprk_projectname: `E2E Non-Secure Project ${projectRef}`,
      sprk_projectref: projectRef,
      sprk_issecure: false,
      sprk_description: 'E2E test non-secure project — safe to delete',
    });
    trackForCleanup(ENTITY_SETS.project, projectId, `non-secure project ${projectRef}`);

    const { status, body } = await callProvisionProject({ projectId, projectRef });

    expect(status).toBe(400);
    const problem = body as { detail?: string };
    expect(problem.detail).toMatch(/not a secure project|sprk_issecure/i);
  });

  // ==========================================================================
  // TC-070-14: Not Found — Umbrella BU Does Not Exist
  // ==========================================================================

  // OBSOLETE (task 021): there is no caller-supplied BU to be absent. The equivalent case now is
  // "the CONFIGURED Secure Project BU does not exist", which must fail closed with reasonCode
  // `sdap.provision.secure_bu_not_found` and never fall back to the root or caller BU. Covered
  // offline by ProvisionProject_WhenTheSecureBusinessUnitIsAbsent_FailsClosedAndProvisionsNothing;
  // worth re-authoring here against a live environment, by temporarily pointing
  // SecureProject:BusinessUnitName at a name that does not exist.
  test.skip('TC-070-14: should return 404 when umbrella BU does not exist', async () => {
    const projectRef = `E2E-UMBRELLA-NF-${Date.now()}`;
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, {
      sprk_projectname: `E2E Umbrella Not Found ${projectRef}`,
      sprk_projectref: projectRef,
      sprk_issecure: true,
      sprk_description: 'E2E test — safe to delete',
    });
    trackForCleanup(ENTITY_SETS.project, projectId, `project for umbrella-not-found test`);

    const nonExistentBuId = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';

    const { status, body } = await callProvisionProject({
      projectId,
      projectRef,
      umbrellaBuId: nonExistentBuId,
    });

    expect(status).toBe(404);
    const problem = body as { detail?: string };
    expect(problem.detail).toContain(nonExistentBuId);
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
  let dataverseApi: DataverseAPI;
  let bffToken: string;

  const resourcesToCleanup: { entitySet: string; id: string; label: string }[] = [];

  test.beforeAll(async () => {
    const dvToken = await DataverseAPI.authenticate(
      process.env.TENANT_ID || '',
      process.env.CLIENT_ID || '',
      process.env.CLIENT_SECRET || '',
      DATAVERSE_API_URL
    );
    dataverseApi = new DataverseAPI(DATAVERSE_API_URL, dvToken);

    const tokenResponse = await fetch(`https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: process.env.CLIENT_ID || '',
        client_secret: process.env.CLIENT_SECRET || '',
        scope: `api://${process.env.BFF_CLIENT_ID || process.env.CLIENT_ID}/.default`,
      }),
    });

    const tokenJson = (await tokenResponse.json()) as { access_token?: string };
    bffToken = tokenJson.access_token || '';
  });

  test.afterAll(async () => {
    for (const resource of [...resourcesToCleanup].reverse()) {
      try {
        await dataverseApi.deleteRecord(resource.entitySet, resource.id);
      } catch {
        console.warn(`[E2E] Cleanup failed for ${resource.label}`);
      }
    }
  });

  function trackForCleanup(entitySet: string, id: string, label: string): void {
    resourcesToCleanup.push({ entitySet, id, label });
  }

  // ==========================================================================
  // TC-070-20: Field Completeness — the container reference is stored, and the
  //            CLIENT lookup is not touched
  //
  // Was "all THREE references". Two of the three should never have existed:
  //   - sprk_securitybuid  → there is no per-project BU to reference
  //   - sprk_externalaccountid → the real column, sprk_externalaccount, is the
  //     project's CLIENT; writing it would overwrite the client
  // Neither column name even existed on the table, which is why the write
  // silently failed for five months.
  // ==========================================================================

  test('TC-070-20: the container reference is stored and the client lookup is untouched', async () => {
    const projectRef = `E2E-REFS-${Date.now()}`;
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, {
      sprk_projectname: `E2E Reference Check ${projectRef}`,
      sprk_projectref: projectRef,
      sprk_issecure: true,
      sprk_description: 'E2E test — safe to delete',
    });
    trackForCleanup(ENTITY_SETS.project, projectId, `project ${projectRef}`);

    // Provision
    const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${bffToken}`,
      },
      body: JSON.stringify({ projectId, projectRef }),
    });

    expect(response.status).toBe(200);
    const provisionResult = (await response.json()) as ProvisionProjectResponse;

    // No account and no per-project BU are created, so there is nothing extra to clean up — and the
    // canonical Secure Project BU must NEVER be tracked for deletion; it is shared infrastructure.

    // Query the project record directly from Dataverse to verify field persistence.
    // Every column named here exists on live sprk_project (verified 2026-08-25) — a $select naming a
    // nonexistent column is a 400, which is how the retired version of this assertion could never
    // have passed.
    const projectRecord = await dataverseApi.queryRecords<ProjectRecord>(ENTITY_SETS.project, {
      $filter: `sprk_projectid eq ${projectId}`,
      $select: [
        'sprk_projectid',
        'sprk_issecure',
        'sprk_containerid',
        '_owningteam_value',
        '_sprk_securitybu_value',
      ].join(','),
      $top: '1',
    });

    expect(projectRecord.length).toBe(1);

    const record = projectRecord[0];

    // sprk_containerid — the project's own container, the ONE thing provisioning records
    expect(record.sprk_containerid).toBeTruthy();
    expect(record.sprk_containerid).toBe(provisionResult.speContainerId);

    // Ownership — the security-relevant outcome
    expect(record._owningteam_value).toBe(provisionResult.ownerTeamId);

    // No per-project security BU is stamped any more
    expect(record._sprk_securitybu_value).toBeFalsy();
  });

  // ==========================================================================
  // TC-070-21: Business Unit Naming Convention — SP-{ProjectRef}
  // ==========================================================================

  // OBSOLETE (task 021): the SP-{ProjectRef} convention named a per-project BU. design.md §5.1 says
  // "no BU-per-project proliferation" — and those BUs were parented to the ROOT BU, placing them
  // OUTSIDE the BU that NFR-05's standing assertion guards, so the convention was not merely
  // redundant. The BU name is now whatever SecureProject:BusinessUnitName resolves to.
  test.skip('TC-070-21: Business Unit must follow SP-{ProjectRef} naming convention', async () => {
    const uniqueRef = `REF-TEST-${Date.now()}`;
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, {
      sprk_projectname: `E2E BU Naming Test ${uniqueRef}`,
      sprk_projectref: uniqueRef,
      sprk_issecure: true,
      sprk_description: 'E2E test — safe to delete',
    });
    trackForCleanup(ENTITY_SETS.project, projectId, `project ${uniqueRef}`);

    const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${bffToken}`,
      },
      body: JSON.stringify({ projectId, projectRef: uniqueRef }),
    });

    expect(response.status).toBe(200);
    const result = (await response.json()) as LegacyProvisionProjectResponse;

    trackForCleanup(ENTITY_SETS.account, result.accountId, `account for ${uniqueRef}`);
    trackForCleanup(ENTITY_SETS.businessUnit, result.businessUnitId, `BU SP-${uniqueRef}`);

    // Verify the BU name strictly follows the SP-{ProjectRef} pattern
    expect(result.businessUnitName).toBe(`SP-${uniqueRef}`);

    // Verify in Dataverse directly
    const buRecords = await dataverseApi.queryRecords<BusinessUnitRecord>(ENTITY_SETS.businessUnit, {
      $filter: `businessunitid eq ${result.businessUnitId}`,
      $select: 'businessunitid,name',
      $top: '1',
    });

    expect(buRecords.length).toBe(1);
    expect(buRecords[0].name).toBe(`SP-${uniqueRef}`);
  });

  // ==========================================================================
  // TC-070-22: External Access Account Owned by Child BU
  // ==========================================================================

  // OBSOLETE (task 021): no account is created. Firms are `sprk_organization` in this codebase and
  // nothing in the external-access model reads an `account`. Critically, the column the synthetic
  // account was aimed at — `sprk_externalaccount` — is the project's CLIENT lookup
  // (ProjectLiveFactResolver.cs:33), so had the stamp ever worked it would have overwritten the
  // client. Provisioning must now never write that column; asserted offline by
  // ProvisionProject_OnTheHappyPath_NeverWritesTheClientLookup.
  test.skip('TC-070-22: External Access Account must be owned by the child Business Unit', async () => {
    const projectRef = `E2E-ACC-OWN-${Date.now()}`;
    const projectId = await dataverseApi.createRecord(ENTITY_SETS.project, {
      sprk_projectname: `E2E Account Ownership Test ${projectRef}`,
      sprk_projectref: projectRef,
      sprk_issecure: true,
      sprk_description: 'E2E test — safe to delete',
    });
    trackForCleanup(ENTITY_SETS.project, projectId, `project ${projectRef}`);

    const response = await fetch(`${EXTERNAL_ACCESS_BASE}/provision-project`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${bffToken}`,
      },
      body: JSON.stringify({ projectId, projectRef }),
    });

    expect(response.status).toBe(200);
    const result = (await response.json()) as LegacyProvisionProjectResponse;

    trackForCleanup(ENTITY_SETS.account, result.accountId, `account for ${projectRef}`);
    trackForCleanup(ENTITY_SETS.businessUnit, result.businessUnitId, `BU SP-${projectRef}`);

    // Query the Account and verify its owning BU matches the child BU
    const accountRecords = await dataverseApi.queryRecords<AccountRecord>(ENTITY_SETS.account, {
      $filter: `accountid eq ${result.accountId}`,
      $select: 'accountid,name,_owningbusinessunit_value',
      $top: '1',
    });

    expect(accountRecords.length).toBe(1);

    const account = accountRecords[0];
    expect(account._owningbusinessunit_value).toBe(result.businessUnitId);
    expect(account.name).toContain('External Access');
  });
});

// ============================================================================
// Manual Execution Notes
// ============================================================================

/**
 * NOTE: These are E2E tests that require a deployed environment.
 *
 * Prerequisites before running:
 *   1. BFF API deployed to dev: https://spe-api-dev-67e2xz.azurewebsites.net
 *   2. Dataverse dev environment available: https://spaarkedev1.crm.dynamics.com
 *   3. SharePointEmbedded:ContainerTypeId configured on the BFF API
 *   4. Azure AD app registration with the following permissions:
 *      - Dataverse API: user_impersonation
 *      - Microsoft Graph: FileStorageContainer.Selected
 *   5. Configure tests/e2e/config/.env with:
 *      TENANT_ID=<your-tenant-id>
 *      CLIENT_ID=<app-client-id>
 *      CLIENT_SECRET=<app-client-secret>
 *      BFF_CLIENT_ID=<bff-api-client-id>
 *      BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
 *      DATAVERSE_API_URL=https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2
 *
 * Run all secure project creation tests:
 *   npx playwright test secure-project-creation.spec.ts
 *
 * Run only happy-path tests:
 *   npx playwright test secure-project-creation.spec.ts -g "@e2e @secure-project"
 *
 * Run a single test case by ID:
 *   npx playwright test secure-project-creation.spec.ts -g "TC-070-01"
 *
 * Run with visible output:
 *   npx playwright test secure-project-creation.spec.ts --reporter=list
 *
 * Expected test execution time: ~2-5 minutes (depends on Dataverse and SPE latency)
 *
 * NOTE: SPE container creation requires the BFF API to have valid Graph credentials
 * with FileStorageContainer.Selected scope. If this is not configured, TC-070-01
 * through TC-070-03 and TC-070-20 through TC-070-22 will fail with a 500 response.
 * TC-070-10 through TC-070-15 (validation tests) will still pass as they test
 * early-exit paths before SPE container creation.
 */
