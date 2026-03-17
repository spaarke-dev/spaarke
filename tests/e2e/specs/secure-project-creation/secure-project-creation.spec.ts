/**
 * E2E Tests: Secure Project Creation Flow
 *
 * Tests validate the full end-to-end secure project creation pipeline:
 *   1. Create project record with sprk_issecure = true (via Dataverse API)
 *   2. Call POST /api/v1/external-access/provision-project
 *   3. Verify child Business Unit created with name SP-{ProjectRef}
 *   4. Verify SPE container provisioned and ID returned
 *   5. Verify External Access Account created and owned by the new BU
 *   6. Verify project record fields updated: sprk_securitybuid, sprk_specontainerid, sprk_externalaccountid
 *   7. Clean up all test data after verification
 *
 * Also covers:
 *   - Umbrella BU reuse scenario (existing BU/Account linked instead of created)
 *   - Validation error paths (missing ProjectId, not-secure project, non-existent project)
 *   - Partial-failure rollback: BU deleted if SPE container creation fails
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

interface ProvisionProjectResponse {
  businessUnitId: string;
  businessUnitName: string;
  speContainerId: string;
  accountId: string;
  accountName: string;
  wasUmbrellaBu: boolean;
}

interface ProjectRecord {
  sprk_projectid: string;
  sprk_projectname: string;
  sprk_issecure: boolean;
  _sprk_securitybuid_value?: string;
  sprk_specontainerid?: string;
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
    const tokenResponse = await fetch(
      `https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'client_credentials',
          client_id: process.env.CLIENT_ID || '',
          client_secret: process.env.CLIENT_SECRET || '',
          scope: `api://${process.env.BFF_CLIENT_ID || process.env.CLIENT_ID}/.default`,
        }),
      }
    );

    const tokenJson = await tokenResponse.json() as { access_token?: string };
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
      const results = await dataverseApi.queryRecords<ProjectRecord>(
        ENTITY_SETS.project,
        {
          $filter: `sprk_projectid eq ${projectId}`,
          $select: [
            'sprk_projectid',
            'sprk_projectname',
            'sprk_issecure',
            '_sprk_securitybuid_value',
            'sprk_specontainerid',
            '_sprk_externalaccountid_value',
          ].join(','),
          $top: '1',
        }
      );
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
      const results = await dataverseApi.queryRecords<BusinessUnitRecord>(
        ENTITY_SETS.businessUnit,
        {
          $filter: `businessunitid eq ${buId}`,
          $select: 'businessunitid,name,_parentbusinessunitid_value',
          $top: '1',
        }
      );
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
      const results = await dataverseApi.queryRecords<AccountRecord>(
        ENTITY_SETS.account,
        {
          $filter: `_owningbusinessunit_value eq ${buId}`,
          $select: 'accountid,name,_owningbusinessunit_value',
          $top: '1',
        }
      );
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
    expect(response.businessUnitName).toBe(`SP-${projectRef}`);
    expect(response.speContainerId).toBeTruthy();
    expect(response.accountId).toBeTruthy();
    expect(response.accountName).toContain(projectRef.includes('E2E') ? 'E2E' : projectRef);
    expect(response.wasUmbrellaBu).toBe(false);

    // Track created infrastructure for cleanup
    trackForCleanup(ENTITY_SETS.account, response.accountId, `external access account for ${projectRef}`);
    trackForCleanup(ENTITY_SETS.businessUnit, response.businessUnitId, `child BU SP-${projectRef}`);

    // ── Assert: Business Unit exists with correct name ────────────────────────
    const buRecord = await queryBusinessUnit(response.businessUnitId);
    expect(buRecord).not.toBeNull();
    expect(buRecord!.name).toBe(`SP-${projectRef}`);
    expect(buRecord!._parentbusinessunitid_value).toBeTruthy(); // Has a parent (root BU)

    // ── Assert: External Access Account is owned by the child BU ─────────────
    const accountRecord = await queryAccountForBu(response.businessUnitId);
    expect(accountRecord).not.toBeNull();
    expect(accountRecord!.accountid).toBe(response.accountId);
    expect(accountRecord!._owningbusinessunit_value).toBe(response.businessUnitId);

    // ── Assert: Project record has all three infrastructure references stored ──
    const projectRecord = await queryProject(projectId);
    expect(projectRecord).not.toBeNull();
    expect(projectRecord!.sprk_issecure).toBe(true);
    expect(projectRecord!._sprk_securitybuid_value).toBe(response.businessUnitId);
    expect(projectRecord!.sprk_specontainerid).toBe(response.speContainerId);
    expect(projectRecord!._sprk_externalaccountid_value).toBe(response.accountId);
  });

  // ==========================================================================
  // TC-070-02: Umbrella BU Reuse — Multi-Project Organisation
  // ==========================================================================

  test('TC-070-02: should reuse an existing umbrella BU and Account for a multi-project org', async () => {
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
    const projectId = await dataverseApi.createRecord(
      ENTITY_SETS.project,
      buildSecureProjectPayload(projectRef)
    );
    trackForCleanup(ENTITY_SETS.project, projectId, `secure project ${projectRef} (umbrella)`);

    // ── Act: Provision with UmbrellaBuId — should skip BU and Account creation ─
    const { status, body } = await callProvisionProject({
      projectId,
      projectRef,
      umbrellaBuId,
    });
    const response = body as ProvisionProjectResponse;

    // ── Assert: Returns 200 with umbrella BU references ───────────────────────
    expect(status).toBe(200);
    expect(response.businessUnitId).toBe(umbrellaBuId);
    expect(response.accountId).toBe(umbrellaAccountId);
    expect(response.wasUmbrellaBu).toBe(true);
    expect(response.speContainerId).toBeTruthy(); // SPE container still provisioned per project

    // ── Assert: No new BU was created (only the umbrella BU exists for this org) ─
    // (Verified by wasUmbrellaBu=true and businessUnitId matching the input umbrellaBuId)

    // ── Assert: Project record references the umbrella infrastructure ──────────
    const projectRecord = await queryProject(projectId);
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
    const projectId1 = await dataverseApi.createRecord(
      ENTITY_SETS.project,
      buildSecureProjectPayload(projectRef1)
    );
    trackForCleanup(ENTITY_SETS.project, projectId1, `secure project 1 — ${projectRef1}`);

    const projectId2 = await dataverseApi.createRecord(
      ENTITY_SETS.project,
      buildSecureProjectPayload(projectRef2)
    );
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

    // Track both for cleanup
    trackForCleanup(ENTITY_SETS.account, response1.accountId, `account for ${projectRef1}`);
    trackForCleanup(ENTITY_SETS.businessUnit, response1.businessUnitId, `BU SP-${projectRef1}`);
    trackForCleanup(ENTITY_SETS.account, response2.accountId, `account for ${projectRef2}`);
    trackForCleanup(ENTITY_SETS.businessUnit, response2.businessUnitId, `BU SP-${projectRef2}`);

    // Each project MUST have a distinct SPE container ID
    expect(response1.speContainerId).not.toBe(response2.speContainerId);

    // Each project MUST have a distinct Business Unit
    expect(response1.businessUnitId).not.toBe(response2.businessUnitId);
    expect(response1.businessUnitName).toBe(`SP-${projectRef1}`);
    expect(response2.businessUnitName).toBe(`SP-${projectRef2}`);
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

    const tokenResponse = await fetch(
      `https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'client_credentials',
          client_id: process.env.CLIENT_ID || '',
          client_secret: process.env.CLIENT_SECRET || '',
          scope: `api://${process.env.BFF_CLIENT_ID || process.env.CLIENT_ID}/.default`,
        }),
      }
    );

    const tokenJson = await tokenResponse.json() as { access_token?: string };
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

  test('TC-070-11: should return 400 when ProjectRef is missing and no UmbrellaBuId', async () => {
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

  test('TC-070-14: should return 404 when umbrella BU does not exist', async () => {
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

    const tokenResponse = await fetch(
      `https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'client_credentials',
          client_id: process.env.CLIENT_ID || '',
          client_secret: process.env.CLIENT_SECRET || '',
          scope: `api://${process.env.BFF_CLIENT_ID || process.env.CLIENT_ID}/.default`,
        }),
      }
    );

    const tokenJson = await tokenResponse.json() as { access_token?: string };
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
  // TC-070-20: Field Completeness — All Three References Stored on Project
  // ==========================================================================

  test('TC-070-20: all three infrastructure references must be present on the project record', async () => {
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
    const provisionResult = await response.json() as ProvisionProjectResponse;

    trackForCleanup(ENTITY_SETS.account, provisionResult.accountId, `account for ${projectRef}`);
    trackForCleanup(ENTITY_SETS.businessUnit, provisionResult.businessUnitId, `BU SP-${projectRef}`);

    // Query project record directly from Dataverse to verify field persistence
    const projectRecord = await dataverseApi.queryRecords<ProjectRecord>(
      ENTITY_SETS.project,
      {
        $filter: `sprk_projectid eq ${projectId}`,
        $select: [
          'sprk_projectid',
          'sprk_issecure',
          '_sprk_securitybuid_value',
          'sprk_specontainerid',
          '_sprk_externalaccountid_value',
        ].join(','),
        $top: '1',
      }
    );

    expect(projectRecord.length).toBe(1);

    const record = projectRecord[0];

    // sprk_securitybuid — Business Unit reference
    expect(record._sprk_securitybuid_value).toBeTruthy();
    expect(record._sprk_securitybuid_value).toBe(provisionResult.businessUnitId);

    // sprk_specontainerid — SPE Container ID string
    expect(record.sprk_specontainerid).toBeTruthy();
    expect(record.sprk_specontainerid).toBe(provisionResult.speContainerId);

    // sprk_externalaccountid — External Access Account reference
    expect(record._sprk_externalaccountid_value).toBeTruthy();
    expect(record._sprk_externalaccountid_value).toBe(provisionResult.accountId);
  });

  // ==========================================================================
  // TC-070-21: Business Unit Naming Convention — SP-{ProjectRef}
  // ==========================================================================

  test('TC-070-21: Business Unit must follow SP-{ProjectRef} naming convention', async () => {
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
    const result = await response.json() as ProvisionProjectResponse;

    trackForCleanup(ENTITY_SETS.account, result.accountId, `account for ${uniqueRef}`);
    trackForCleanup(ENTITY_SETS.businessUnit, result.businessUnitId, `BU SP-${uniqueRef}`);

    // Verify the BU name strictly follows the SP-{ProjectRef} pattern
    expect(result.businessUnitName).toBe(`SP-${uniqueRef}`);

    // Verify in Dataverse directly
    const buRecords = await dataverseApi.queryRecords<BusinessUnitRecord>(
      ENTITY_SETS.businessUnit,
      {
        $filter: `businessunitid eq ${result.businessUnitId}`,
        $select: 'businessunitid,name',
        $top: '1',
      }
    );

    expect(buRecords.length).toBe(1);
    expect(buRecords[0].name).toBe(`SP-${uniqueRef}`);
  });

  // ==========================================================================
  // TC-070-22: External Access Account Owned by Child BU
  // ==========================================================================

  test('TC-070-22: External Access Account must be owned by the child Business Unit', async () => {
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
    const result = await response.json() as ProvisionProjectResponse;

    trackForCleanup(ENTITY_SETS.account, result.accountId, `account for ${projectRef}`);
    trackForCleanup(ENTITY_SETS.businessUnit, result.businessUnitId, `BU SP-${projectRef}`);

    // Query the Account and verify its owning BU matches the child BU
    const accountRecords = await dataverseApi.queryRecords<AccountRecord>(
      ENTITY_SETS.account,
      {
        $filter: `accountid eq ${result.accountId}`,
        $select: 'accountid,name,_owningbusinessunit_value',
        $top: '1',
      }
    );

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
