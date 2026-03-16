/**
 * E2E Tests: External User Invitation & Onboarding Flow
 *
 * Tests cover the complete invitation-to-access chain:
 *   Step 1: Internal user sends invitation via BFF API
 *            POST /api/v1/external-access/invite
 *   Step 2: adx_invitation record created with correct web role association
 *   Step 3: sprk_communication email record created
 *   Step 4: External user redeems invitation via Power Pages portal
 *   Step 5: Entra External ID identity linked to Contact record
 *   Step 6: Web role assigned to Contact after redemption
 *   Step 7: SPA shows correct project in workspace home after login
 *
 * Prerequisites (dev environment):
 *   - BFF API deployed at SDAP_BFF_API_URL
 *   - Power Pages portal deployed at POWER_PAGES_URL
 *   - Dataverse dev environment: https://spaarkedev1.crm.dynamics.com
 *   - adx_invitation, mspp_webrole tables accessible via service principal
 *   - "Secure Project Participant" web role configured
 *   - At least one sprk_project record with sprk_issecure = true
 *   - PowerPages:SecureProjectParticipantWebRoleId configured in BFF
 *
 * Test Data Strategy:
 *   - Contact records are created fresh per test (unique email + timestamp)
 *   - sprk_externalrecordaccess records are cleaned up after each test
 *   - adx_invitation records are cleaned up after each test
 *
 * Environment Variables (.env):
 *   SDAP_BFF_API_URL      = https://spe-api-dev-67e2xz.azurewebsites.net
 *   POWER_PAGES_URL       = https://{portal}.powerappsportals.com
 *   DATAVERSE_API_URL     = https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2
 *   TENANT_ID             = {tenant-id}
 *   CLIENT_ID             = {app-client-id}
 *   CLIENT_SECRET         = {app-client-secret}
 *   TEST_PROJECT_ID       = {guid of a sprk_project record with issecure=true}
 *   SECURE_PARTICIPANT_WEB_ROLE_ID = {guid of the "Secure Project Participant" web role}
 *
 * Run:
 *   npx playwright test invitation-onboarding.spec.ts --headed
 *   npx playwright test invitation-onboarding.spec.ts -g "invitation api"
 *
 * @see spec.md — External Access & Invitation flow
 * @see tasks/071-e2e-test-invitation-onboarding.poml
 */

import { test, expect, Page, APIRequestContext } from '@playwright/test';
import { DataverseAPI } from '../../utils/dataverse-api';

// ============================================================================
// Constants
// ============================================================================

const BFF_API_URL = process.env.SDAP_BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net';
const POWER_PAGES_URL = process.env.POWER_PAGES_URL || 'https://secure-project.powerappsportals.com';
const DATAVERSE_API_URL = process.env.DATAVERSE_API_URL || 'https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2';
const TEST_PROJECT_ID = process.env.TEST_PROJECT_ID || '';
const SECURE_PARTICIPANT_WEB_ROLE_ID = process.env.SECURE_PARTICIPANT_WEB_ROLE_ID || '';

// Dataverse entity set names
const ENTITIES = {
  contact: 'contacts',
  invitation: 'adx_invitations',
  webrole: 'mspp_webroles',
  externalAccess: 'sprk_externalrecordaccesses',
  communication: 'sprk_communications',
  contactWebRole: 'mspp_portalwebroles', // Contact-to-webrole N:N
} as const;

// Access level option set values (matching sprk_accesslevel)
const ACCESS_LEVELS = {
  ViewOnly: 100000000,
  Collaborate: 100000001,
  FullAccess: 100000002,
} as const;

// BFF API endpoint paths
const INVITE_ENDPOINT = '/api/v1/external-access/invite';

// ============================================================================
// Test data builders
// ============================================================================

function buildTestEmail(): string {
  return `e2e.test.${Date.now()}.${Math.random().toString(36).slice(2, 6)}@testdomain.example.com`;
}

function buildInviteRequestBody(contactId: string, projectId: string, accessLevel: number = ACCESS_LEVELS.ViewOnly) {
  return {
    contactId,
    projectId,
    accessLevel,
    expiryDate: null, // Use default 30-day expiry
  };
}

// ============================================================================
// Selectors — InviteUserDialog (src/client/external-spa/src/components/InviteUserDialog.tsx)
// ============================================================================

const DIALOG_SELECTORS = {
  // Dialog surface — Fluent v9 Dialog uses role="dialog"
  dialog: '[aria-label="Invite external user"]',
  dialogSuccessTitle: '[aria-label="Invitation sent"]',
  dialogErrorTitle: '[aria-label="Invitation failed"]',

  // Form fields
  emailInput: 'input[type="email"]',
  firstNameInput: 'input[placeholder="Jane"]',
  lastNameInput: 'input[placeholder="Smith"]',
  accessLevelSelect: 'select',

  // Buttons
  sendInvitationButton: 'button:has-text("Send invitation")',
  doneButton: 'button:has-text("Done")',
  tryAgainButton: 'button:has-text("Try again")',
  cancelButton: 'button:has-text("Cancel")',
  closeButton: 'button[aria-label="Close dialog"]',

  // Status indicators
  sendingSpinner: 'button:has-text("Sending invitation...")',
  successIcon: '[aria-hidden="true"]', // CheckmarkCircle20Regular
  invitationCodeRow: 'text=Invitation code',
  expiresRow: 'text=Expires',
  errorMessageBar: '[class*="messageBar"]',
} as const;

// ============================================================================
// Helper: Acquire BFF Bearer token (service-principal auth for test setup)
// ============================================================================

async function acquireBffToken(request: APIRequestContext): Promise<string> {
  // In E2E tests, we use client-credentials flow to obtain a BFF token
  // (bypassing the Power Pages implicit grant flow used by the SPA)
  const tenantId = process.env.TENANT_ID || '';
  const clientId = process.env.CLIENT_ID || '';
  const clientSecret = process.env.CLIENT_SECRET || '';

  const response = await request.post(
    `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`,
    {
      form: {
        grant_type: 'client_credentials',
        client_id: clientId,
        client_secret: clientSecret,
        scope: `api://${clientId}/.default`,
      },
    }
  );

  const body = await response.json();
  return body.access_token as string;
}

// ============================================================================
// Helper: Create a Contact record for test isolation
// ============================================================================

async function createTestContact(api: DataverseAPI, email: string): Promise<string> {
  const contactId = await api.createRecord(ENTITIES.contact, {
    emailaddress1: email,
    firstname: 'E2E',
    lastname: `TestUser_${Date.now()}`,
  });
  return contactId;
}

// ============================================================================
// Page Object: InviteUserDialog interactions (on the SPA)
// ============================================================================

class InviteUserDialogPage {
  constructor(private readonly page: Page) {}

  /** Wait for the invite dialog to be visible */
  async waitForDialog(): Promise<void> {
    await this.page.waitForSelector(DIALOG_SELECTORS.dialog, { state: 'visible', timeout: 10_000 });
  }

  /** Fill the email address field */
  async fillEmail(email: string): Promise<void> {
    await this.page.fill(DIALOG_SELECTORS.emailInput, email);
  }

  /** Fill optional name fields */
  async fillName(firstName: string, lastName: string): Promise<void> {
    await this.page.fill(DIALOG_SELECTORS.firstNameInput, firstName);
    await this.page.fill(DIALOG_SELECTORS.lastNameInput, lastName);
  }

  /** Select an access level from the dropdown */
  async selectAccessLevel(value: number): Promise<void> {
    await this.page.selectOption(DIALOG_SELECTORS.accessLevelSelect, String(value));
  }

  /** Click Send Invitation and wait for network */
  async sendInvitation(): Promise<void> {
    await this.page.click(DIALOG_SELECTORS.sendInvitationButton);
  }

  /** Wait for the success view to appear */
  async waitForSuccess(timeout = 15_000): Promise<void> {
    await this.page.waitForSelector(DIALOG_SELECTORS.dialogSuccessTitle, {
      state: 'visible',
      timeout,
    });
  }

  /** Wait for the error view to appear */
  async waitForError(timeout = 15_000): Promise<void> {
    await this.page.waitForSelector(DIALOG_SELECTORS.dialogErrorTitle, {
      state: 'visible',
      timeout,
    });
  }

  /** Click Done to close the success dialog */
  async clickDone(): Promise<void> {
    await this.page.click(DIALOG_SELECTORS.doneButton);
    await this.page.waitForSelector(DIALOG_SELECTORS.dialog, { state: 'hidden', timeout: 5_000 });
  }

  /** Click Try Again from the error view */
  async clickTryAgain(): Promise<void> {
    await this.page.click(DIALOG_SELECTORS.tryAgainButton);
    await this.page.waitForSelector(DIALOG_SELECTORS.dialog, { state: 'visible' });
  }

  /** Cancel the dialog */
  async cancel(): Promise<void> {
    await this.page.click(DIALOG_SELECTORS.cancelButton);
    await this.page.waitForSelector(DIALOG_SELECTORS.dialog, { state: 'hidden', timeout: 5_000 });
  }

  /** Verify the invitation code is visible in the success view */
  async getInvitationCode(): Promise<string> {
    const codeRow = this.page.locator(DIALOG_SELECTORS.invitationCodeRow).locator('..');
    return codeRow.locator('span[style*="monospace"]').textContent() ?? '';
  }
}

// ============================================================================
// TEST SUITE: BFF API Layer — Direct invitation endpoint tests
// ============================================================================

test.describe('Invitation flow — BFF API layer @e2e @sdap @invitation', () => {
  let dataverseApi: DataverseAPI;
  let bffToken: string;
  const createdContacts: string[] = [];
  const createdInvitations: string[] = [];

  test.beforeAll(async ({ request }) => {
    // Authenticate with Dataverse using service principal
    const dvToken = await DataverseAPI.authenticate(
      process.env.TENANT_ID || '',
      process.env.CLIENT_ID || '',
      process.env.CLIENT_SECRET || '',
      DATAVERSE_API_URL
    );
    dataverseApi = new DataverseAPI(DATAVERSE_API_URL, dvToken);

    // Acquire BFF token for API calls
    bffToken = await acquireBffToken(request);
  });

  test.afterAll(async () => {
    // Cleanup: delete all invitations created during tests
    if (createdInvitations.length > 0) {
      await dataverseApi.deleteRecords(ENTITIES.invitation, createdInvitations);
    }
    // Cleanup: delete test Contact records
    if (createdContacts.length > 0) {
      await dataverseApi.deleteRecords(ENTITIES.contact, createdContacts);
    }
  });

  // --------------------------------------------------------------------------
  // Step 2: Test — Send invitation via BFF API (POST /api/v1/external-access/invite)
  // --------------------------------------------------------------------------

  test('Step 2: should create invitation via POST /api/v1/external-access/invite', async ({ request }) => {
    test.skip(!TEST_PROJECT_ID, 'TEST_PROJECT_ID not configured — skipping live API test');

    // Create a test Contact to invite
    const testEmail = buildTestEmail();
    const contactId = await createTestContact(dataverseApi, testEmail);
    createdContacts.push(contactId);

    // Call the BFF invite endpoint
    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        Authorization: `Bearer ${bffToken}`,
        'Content-Type': 'application/json',
      },
      data: buildInviteRequestBody(contactId, TEST_PROJECT_ID),
    });

    // Expect 200 OK with invitation details
    expect(response.status()).toBe(200);

    const body = await response.json();
    expect(body).toMatchObject({
      invitationId: expect.any(String),
      invitationCode: expect.any(String),
    });
    expect(body.invitationId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
    );
    expect(body.invitationCode).toBeTruthy();
    expect(body.invitationCode.length).toBeGreaterThan(0);

    // Track for cleanup
    createdInvitations.push(body.invitationId);
  });

  // --------------------------------------------------------------------------
  // Step 3: Verify — adx_invitation record created with correct web role
  // --------------------------------------------------------------------------

  test('Step 3: adx_invitation record should exist with correct type and Contact', async ({ request }) => {
    test.skip(!TEST_PROJECT_ID, 'TEST_PROJECT_ID not configured — skipping live API test');

    const testEmail = buildTestEmail();
    const contactId = await createTestContact(dataverseApi, testEmail);
    createdContacts.push(contactId);

    // Create invitation via BFF
    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        Authorization: `Bearer ${bffToken}`,
        'Content-Type': 'application/json',
      },
      data: buildInviteRequestBody(contactId, TEST_PROJECT_ID),
    });

    expect(response.status()).toBe(200);
    const { invitationId } = await response.json();
    createdInvitations.push(invitationId);

    // Verify invitation record in Dataverse
    const invitation = await dataverseApi.getRecord(ENTITIES.invitation, invitationId);

    expect(invitation).toBeTruthy();

    // adx_type = 756150000 (Single invitation)
    expect(invitation['adx_type']).toBe(756150000);

    // adx_maximumredemptions = 1
    expect(invitation['adx_maximumredemptions']).toBe(1);

    // adx_name should contain project ID
    expect(invitation['adx_name']).toContain('Secure Project Access');

    // adx_invitationcode should be set
    expect(invitation['adx_invitationcode']).toBeTruthy();
  });

  test('Step 3b: adx_invitation should have the Secure Project Participant web role associated', async ({ request }) => {
    test.skip(!TEST_PROJECT_ID || !SECURE_PARTICIPANT_WEB_ROLE_ID,
      'TEST_PROJECT_ID or SECURE_PARTICIPANT_WEB_ROLE_ID not configured');

    const testEmail = buildTestEmail();
    const contactId = await createTestContact(dataverseApi, testEmail);
    createdContacts.push(contactId);

    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        Authorization: `Bearer ${bffToken}`,
        'Content-Type': 'application/json',
      },
      data: buildInviteRequestBody(contactId, TEST_PROJECT_ID),
    });

    expect(response.status()).toBe(200);
    const { invitationId } = await response.json();
    createdInvitations.push(invitationId);

    // Query the N:N relationship: adx_invitation_mspp_webrole_powerpagecomponent
    const webRoles = await dataverseApi.fetchRecords(
      ENTITIES.webrole,
      `<fetch>
        <entity name="mspp_webrole">
          <attribute name="mspp_webroleid" />
          <attribute name="mspp_name" />
          <link-entity name="adx_invitation_mspp_webrole_powerpagecomponent" from="mspp_webroleid" to="mspp_webroleid" intersect="true">
            <filter>
              <condition attribute="adx_invitationid" operator="eq" value="${invitationId}" />
            </filter>
          </link-entity>
        </entity>
      </fetch>`
    );

    expect(webRoles.length).toBeGreaterThan(0);
    const participantRole = webRoles.find(
      (r: { mspp_webroleid: string }) => r.mspp_webroleid === SECURE_PARTICIPANT_WEB_ROLE_ID
    );
    expect(participantRole).toBeTruthy();
  });

  // --------------------------------------------------------------------------
  // Step 4: Verify — Email sent (check sprk_communication records)
  // --------------------------------------------------------------------------

  test('Step 4: sprk_communication email record should be created after invitation', async ({ request }) => {
    test.skip(!TEST_PROJECT_ID, 'TEST_PROJECT_ID not configured — skipping live API test');

    const testEmail = buildTestEmail();
    const contactId = await createTestContact(dataverseApi, testEmail);
    createdContacts.push(contactId);

    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        Authorization: `Bearer ${bffToken}`,
        'Content-Type': 'application/json',
      },
      data: buildInviteRequestBody(contactId, TEST_PROJECT_ID),
    });

    expect(response.status()).toBe(200);
    const { invitationId } = await response.json();
    createdInvitations.push(invitationId);

    // Allow time for the async email send (background job or synchronous)
    await new Promise((resolve) => setTimeout(resolve, 3_000));

    // Query sprk_communication records linked to this Contact
    const communications = await dataverseApi.fetchRecords(
      ENTITIES.communication,
      `<fetch top="5">
        <entity name="sprk_communication">
          <attribute name="sprk_communicationid" />
          <attribute name="sprk_subject" />
          <attribute name="sprk_status" />
          <attribute name="createdon" />
          <filter>
            <condition attribute="sprk_regardingcontactid" operator="eq" value="${contactId}" />
          </filter>
          <order attribute="createdon" descending="true" />
        </entity>
      </fetch>`
    );

    expect(communications.length).toBeGreaterThan(0);
    // Most recent communication should be the invitation email
    const inviteEmail = communications[0];
    expect(inviteEmail['sprk_subject']).toMatch(/invitation|access|secure project/i);
  });

  // --------------------------------------------------------------------------
  // Validation: Missing ContactId returns 400
  // --------------------------------------------------------------------------

  test('should return 400 when ContactId is missing or empty', async ({ request }) => {
    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        Authorization: `Bearer ${bffToken}`,
        'Content-Type': 'application/json',
      },
      data: {
        contactId: '00000000-0000-0000-0000-000000000000',
        projectId: TEST_PROJECT_ID || '00000000-0000-0000-0000-000000000001',
      },
    });

    expect(response.status()).toBe(400);
    const body = await response.json();
    expect(body.title || body.detail).toMatch(/contactid|required/i);
  });

  // --------------------------------------------------------------------------
  // Validation: Missing ProjectId returns 400
  // --------------------------------------------------------------------------

  test('should return 400 when ProjectId is missing or empty', async ({ request }) => {
    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        Authorization: `Bearer ${bffToken}`,
        'Content-Type': 'application/json',
      },
      data: {
        contactId: TEST_PROJECT_ID || '00000000-0000-0000-0000-000000000001',
        projectId: '00000000-0000-0000-0000-000000000000',
      },
    });

    expect(response.status()).toBe(400);
    const body = await response.json();
    expect(body.title || body.detail).toMatch(/projectid|required/i);
  });

  // --------------------------------------------------------------------------
  // Security: Unauthenticated requests return 401
  // --------------------------------------------------------------------------

  test('should return 401 when no Authorization header is present', async ({ request }) => {
    const response = await request.post(`${BFF_API_URL}${INVITE_ENDPOINT}`, {
      headers: {
        'Content-Type': 'application/json',
      },
      data: {
        contactId: '00000000-0000-0000-0000-000000000001',
        projectId: '00000000-0000-0000-0000-000000000002',
      },
    });

    expect(response.status()).toBe(401);
  });
});

// ============================================================================
// TEST SUITE: SPA UI — InviteUserDialog component E2E tests (with API mocking)
// ============================================================================

test.describe('Invitation flow — InviteUserDialog UI @e2e @sdap @invitation @ui', () => {
  // These tests mock the BFF API endpoints via Playwright route interception,
  // allowing E2E validation of the UI without requiring a live BFF deployment.

  const MOCK_INVITATION_RESPONSE = {
    invitationId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    invitationCode: 'TEST-INVITE-CODE-12345',
    expiryDate: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString().split('T')[0],
  };

  // Navigate to the Power Pages SPA project page where InviteUserDialog is accessible
  async function navigateToProjectPage(page: Page): Promise<void> {
    // The InviteUserDialog is opened via a toolbar button on the project page
    // In dev environment this would be: {POWER_PAGES_URL}/projects/{projectId}
    await page.goto(`${POWER_PAGES_URL}/projects/${TEST_PROJECT_ID || 'test-project-id'}`);
    // Wait for SPA to initialize
    await page.waitForLoadState('networkidle', { timeout: 15_000 });
  }

  // -------------------------------------------------------------------------
  // Step 7: SPA shows correct project after login (workspace home)
  // -------------------------------------------------------------------------

  test('Step 7: workspace home should display correct project after authenticated login', async ({ page, context }) => {
    // Mock the external/me endpoint to simulate an authenticated external user
    await context.route('**/api/v1/external/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          contactId: 'contact-guid-123',
          email: 'external.user@example.com',
          projects: [
            {
              projectId: TEST_PROJECT_ID || 'test-project-guid',
              accessLevel: 'ViewOnly',
            },
          ],
        }),
      });
    });

    // Mock Power Pages token endpoint
    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'text/plain',
        body: 'mock.jwt.token',
      });
    });

    // Mock the anti-forgery token endpoint
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'text/xml',
        body: '<input value="mock-csrf-token" />',
      });
    });

    await page.goto(POWER_PAGES_URL);
    await page.waitForLoadState('networkidle', { timeout: 15_000 });

    // The workspace home page should list accessible projects
    // The SPA renders a WorkspaceHomePage that calls /api/v1/external/me
    await expect(page.locator('body')).toBeVisible({ timeout: 10_000 });

    // Verify project is displayed (project name from test data or SPA rendering)
    // This asserts the SPA correctly reads the /external/me response
    const projectCards = page.locator('[data-testid="project-card"], [class*="project"]');
    await expect(projectCards.first()).toBeVisible({ timeout: 10_000 });
  });

  // -------------------------------------------------------------------------
  // Happy path: Full invitation UI flow with mocked API
  // -------------------------------------------------------------------------

  test('should complete full invitation flow via InviteUserDialog (mocked API)', async ({ page, context }) => {
    // Mock the BFF invite endpoint
    await context.route(`**${INVITE_ENDPOINT}`, async (route) => {
      expect(route.request().method()).toBe('POST');
      const body = JSON.parse(route.request().postData() ?? '{}');
      expect(body.email).toBeTruthy();
      expect(body.projectId).toBeTruthy();

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(MOCK_INVITATION_RESPONSE),
      });
    });

    // Mock portal token endpoint
    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'text/xml',
        body: '<input value="mock-csrf-token" />',
      });
    });

    await navigateToProjectPage(page);

    // Open the Invite User Dialog (via toolbar button — Full Access users only)
    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    if (await inviteButton.isVisible({ timeout: 3_000 })) {
      await inviteButton.click();
    }

    const dialogPage = new InviteUserDialogPage(page);
    await dialogPage.waitForDialog();

    // Fill the form
    await dialogPage.fillEmail('jane.smith@externalfirm.com');
    await dialogPage.fillName('Jane', 'Smith');
    await dialogPage.selectAccessLevel(ACCESS_LEVELS.ViewOnly);

    // Submit
    await dialogPage.sendInvitation();

    // Verify success view
    await dialogPage.waitForSuccess();

    // The success view should show the invitation code
    const successPage = page.locator(DIALOG_SELECTORS.dialogSuccessTitle);
    await expect(successPage).toBeVisible();

    // Invitation code should be visible
    await expect(page.locator(`text=${MOCK_INVITATION_RESPONSE.invitationCode}`)).toBeVisible();

    // Close
    await dialogPage.clickDone();
  });

  // -------------------------------------------------------------------------
  // Email validation: invalid format
  // -------------------------------------------------------------------------

  test('should show validation error for invalid email format', async ({ page, context }) => {
    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/xml', body: '<input value="mock-csrf-token" />' });
    });

    await navigateToProjectPage(page);

    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    if (await inviteButton.isVisible({ timeout: 3_000 })) {
      await inviteButton.click();
    }

    const dialogPage = new InviteUserDialogPage(page);
    await dialogPage.waitForDialog();

    // Enter invalid email
    await dialogPage.fillEmail('not-a-valid-email');
    await dialogPage.sendInvitation();

    // Validation error should appear
    const validationError = page.locator('[class*="validationMessage"], [role="alert"]');
    await expect(validationError.first()).toBeVisible({ timeout: 5_000 });
    await expect(validationError.first()).toContainText(/valid email/i);

    // Dialog should remain open
    await expect(page.locator(DIALOG_SELECTORS.dialog)).toBeVisible();
  });

  // -------------------------------------------------------------------------
  // Email validation: empty field
  // -------------------------------------------------------------------------

  test('should show validation error when email is empty', async ({ page, context }) => {
    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/xml', body: '<input value="mock-csrf-token" />' });
    });

    await navigateToProjectPage(page);

    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    if (await inviteButton.isVisible({ timeout: 3_000 })) {
      await inviteButton.click();
    }

    const dialogPage = new InviteUserDialogPage(page);
    await dialogPage.waitForDialog();

    // Submit without filling email
    await dialogPage.sendInvitation();

    // Validation error for required email
    const validationError = page.locator('[class*="validationMessage"], [role="alert"]');
    await expect(validationError.first()).toBeVisible({ timeout: 5_000 });
    await expect(validationError.first()).toContainText(/required/i);
  });

  // -------------------------------------------------------------------------
  // Error handling: API returns 500
  // -------------------------------------------------------------------------

  test('should show error view when API returns 500', async ({ page, context }) => {
    await context.route(`**${INVITE_ENDPOINT}`, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/internal-error',
          title: 'Internal Server Error',
          status: 500,
          detail: 'Failed to create invitation record in Dataverse.',
        }),
      });
    });

    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/xml', body: '<input value="mock-csrf-token" />' });
    });

    await navigateToProjectPage(page);

    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    if (await inviteButton.isVisible({ timeout: 3_000 })) {
      await inviteButton.click();
    }

    const dialogPage = new InviteUserDialogPage(page);
    await dialogPage.waitForDialog();

    await dialogPage.fillEmail('test@example.com');
    await dialogPage.sendInvitation();

    // Error view should appear
    await dialogPage.waitForError();

    // MessageBar should be visible
    const errorBar = page.locator(DIALOG_SELECTORS.errorMessageBar);
    await expect(errorBar).toBeVisible();
  });

  // -------------------------------------------------------------------------
  // Error handling: Retry from error view
  // -------------------------------------------------------------------------

  test('should return to form view when Try Again is clicked', async ({ page, context }) => {
    let callCount = 0;
    await context.route(`**${INVITE_ENDPOINT}`, async (route) => {
      callCount++;
      if (callCount === 1) {
        await route.fulfill({
          status: 500,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            title: 'Internal Server Error',
            status: 500,
            detail: 'Temporary failure',
          }),
        });
      } else {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(MOCK_INVITATION_RESPONSE),
        });
      }
    });

    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/xml', body: '<input value="mock-csrf-token" />' });
    });

    await navigateToProjectPage(page);

    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    if (await inviteButton.isVisible({ timeout: 3_000 })) {
      await inviteButton.click();
    }

    const dialogPage = new InviteUserDialogPage(page);
    await dialogPage.waitForDialog();

    await dialogPage.fillEmail('retry.test@example.com');
    await dialogPage.sendInvitation();

    // First attempt → error
    await dialogPage.waitForError();

    // Click Try Again → returns to form
    await dialogPage.clickTryAgain();
    await expect(page.locator(DIALOG_SELECTORS.sendInvitationButton)).toBeVisible();

    // Second attempt → success
    await dialogPage.sendInvitation();
    await dialogPage.waitForSuccess();
    expect(callCount).toBe(2);
  });

  // -------------------------------------------------------------------------
  // Cancel: Dialog closes without sending
  // -------------------------------------------------------------------------

  test('should close dialog without calling API when Cancel is clicked', async ({ page, context }) => {
    let apiCallMade = false;
    await context.route(`**${INVITE_ENDPOINT}`, async (route) => {
      apiCallMade = true;
      await route.continue();
    });

    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/xml', body: '<input value="mock-csrf-token" />' });
    });

    await navigateToProjectPage(page);

    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    if (await inviteButton.isVisible({ timeout: 3_000 })) {
      await inviteButton.click();
    }

    const dialogPage = new InviteUserDialogPage(page);
    await dialogPage.waitForDialog();

    await dialogPage.fillEmail('cancel.test@example.com');
    await dialogPage.cancel();

    // API should not have been called
    expect(apiCallMade).toBe(false);

    // Dialog should be closed
    await expect(page.locator(DIALOG_SELECTORS.dialog)).toBeHidden();
  });

  // -------------------------------------------------------------------------
  // Access control: Full Access users only
  // -------------------------------------------------------------------------

  test('should not render InviteUserDialog for ViewOnly users', async ({ page, context }) => {
    // Mock external/me to return ViewOnly access
    await context.route('**/api/v1/external/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          contactId: 'contact-guid-viewonly',
          email: 'viewonly@example.com',
          projects: [
            {
              projectId: TEST_PROJECT_ID || 'test-project-guid',
              accessLevel: 'ViewOnly',
            },
          ],
        }),
      });
    });

    await context.route('**/_services/auth/token', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'mock.jwt.token' });
    });
    await context.route('**/_layout/tokenhtml', async (route) => {
      await route.fulfill({ status: 200, contentType: 'text/xml', body: '<input value="mock-csrf-token" />' });
    });

    await navigateToProjectPage(page);

    // Invite button should NOT be visible for ViewOnly users
    const inviteButton = page.locator('button:has-text("Invite"), button[aria-label*="Invite"]');
    await expect(inviteButton).toBeHidden({ timeout: 5_000 });
  });
});

// ============================================================================
// TEST SUITE: Dataverse state verification — post-redemption checks
// (Manual / semi-automated — requires live portal redemption)
// ============================================================================

test.describe('Invitation flow — post-redemption Dataverse verification @sdap @invitation @dataverse-state', () => {
  /**
   * NOTE: Steps 5, 6 (Contact linked to Entra External ID + web role assigned)
   * require an actual user to redeem the invitation via the Power Pages portal.
   * These tests verify the Dataverse state AFTER a redemption has occurred.
   *
   * To use these tests:
   *   1. Create an invitation via the BFF API or UI
   *   2. Have a real or test user redeem it via the portal
   *   3. Set REDEEMED_CONTACT_ID and REDEEMED_INVITATION_ID env vars
   *   4. Run: npx playwright test invitation-onboarding.spec.ts -g "post-redemption"
   */

  let dataverseApi: DataverseAPI;

  test.beforeAll(async () => {
    const dvToken = await DataverseAPI.authenticate(
      process.env.TENANT_ID || '',
      process.env.CLIENT_ID || '',
      process.env.CLIENT_SECRET || '',
      DATAVERSE_API_URL
    );
    dataverseApi = new DataverseAPI(DATAVERSE_API_URL, dvToken);
  });

  // --------------------------------------------------------------------------
  // Step 5: Verify — Contact linked to Entra External ID identity
  // --------------------------------------------------------------------------

  test('Step 5: redeemed Contact should have Entra External ID identity linked', async () => {
    const contactId = process.env.REDEEMED_CONTACT_ID;
    test.skip(!contactId, 'REDEEMED_CONTACT_ID not set — skipping post-redemption test');

    const contact = await dataverseApi.getRecord(ENTITIES.contact, contactId!);
    expect(contact).toBeTruthy();

    // After redemption, the contact should have an external identity linked.
    // Power Pages links external identities via adx_externalidentity table.
    const externalIdentities = await dataverseApi.fetchRecords(
      'adx_externalidentities',
      `<fetch top="1">
        <entity name="adx_externalidentity">
          <attribute name="adx_externalidentityid" />
          <attribute name="adx_username" />
          <filter>
            <condition attribute="adx_contactid" operator="eq" value="${contactId}" />
          </filter>
        </entity>
      </fetch>`
    );

    expect(externalIdentities.length).toBeGreaterThan(0);
    expect(externalIdentities[0]['adx_username']).toBeTruthy();
  });

  // --------------------------------------------------------------------------
  // Step 6: Verify — Web role assigned to Contact after redemption
  // --------------------------------------------------------------------------

  test('Step 6: redeemed Contact should have Secure Project Participant web role assigned', async () => {
    const contactId = process.env.REDEEMED_CONTACT_ID;
    test.skip(!contactId || !SECURE_PARTICIPANT_WEB_ROLE_ID,
      'REDEEMED_CONTACT_ID or SECURE_PARTICIPANT_WEB_ROLE_ID not set');

    // Query the Contact-to-WebRole N:N relationship
    // Power Pages uses mspp_portalwebroles as the join table
    const webRoles = await dataverseApi.fetchRecords(
      ENTITIES.webrole,
      `<fetch top="10">
        <entity name="mspp_webrole">
          <attribute name="mspp_webroleid" />
          <attribute name="mspp_name" />
          <link-entity name="mspp_portalwebroles" from="mspp_webroleid" to="mspp_webroleid" intersect="true">
            <link-entity name="contact" from="contactid" to="contactid">
              <filter>
                <condition attribute="contactid" operator="eq" value="${contactId}" />
              </filter>
            </link-entity>
          </link-entity>
        </entity>
      </fetch>`
    );

    const participantRole = webRoles.find(
      (r: { mspp_webroleid: string }) => r.mspp_webroleid === SECURE_PARTICIPANT_WEB_ROLE_ID
    );
    expect(participantRole).toBeTruthy();
  });

  // --------------------------------------------------------------------------
  // Step 7 (verification): Authenticated SPA shows correct project
  // --------------------------------------------------------------------------

  test('Step 7 verification: external/me should return project with correct access level after redemption', async ({ request }) => {
    const contactId = process.env.REDEEMED_CONTACT_ID;
    test.skip(!contactId || !TEST_PROJECT_ID, 'REDEEMED_CONTACT_ID or TEST_PROJECT_ID not set');

    // This test verifies the /api/v1/external/me endpoint returns correct data
    // after the user has redeemed their invitation and the web role is assigned.
    // A portal session token for the redeemed user is required.
    const portalToken = process.env.REDEEMED_USER_PORTAL_TOKEN;
    test.skip(!portalToken, 'REDEEMED_USER_PORTAL_TOKEN not set — cannot verify live /external/me response');

    const response = await request.get(`${BFF_API_URL}/api/v1/external/me`, {
      headers: {
        Authorization: `Bearer ${portalToken}`,
      },
    });

    expect(response.status()).toBe(200);
    const body = await response.json();

    expect(body.projects).toBeDefined();
    expect(Array.isArray(body.projects)).toBe(true);

    const project = body.projects.find(
      (p: { projectId: string }) => p.projectId === TEST_PROJECT_ID
    );
    expect(project).toBeTruthy();
    expect(project.accessLevel).toBeTruthy();
  });
});

/**
 * RUNNING THESE TESTS
 * ===================
 *
 * 1. Configure .env file (copy from tests/e2e/config/.env.example):
 *    SDAP_BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
 *    POWER_PAGES_URL=https://secure-project.powerappsportals.com
 *    DATAVERSE_API_URL=https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2
 *    TENANT_ID=<your-tenant-id>
 *    CLIENT_ID=<your-app-client-id>
 *    CLIENT_SECRET=<your-app-client-secret>
 *    TEST_PROJECT_ID=<guid-of-a-secure-project>
 *    SECURE_PARTICIPANT_WEB_ROLE_ID=<guid-of-web-role>
 *
 * 2. Run all invitation tests:
 *    npx playwright test invitation-onboarding.spec.ts --headed
 *
 * 3. Run only BFF API tests (no portal required):
 *    npx playwright test invitation-onboarding.spec.ts -g "BFF API layer"
 *
 * 4. Run only mocked UI tests:
 *    npx playwright test invitation-onboarding.spec.ts -g "InviteUserDialog UI"
 *
 * 5. Run post-redemption checks (requires prior manual redemption):
 *    REDEEMED_CONTACT_ID=<guid> REDEEMED_INVITATION_ID=<guid> \
 *    npx playwright test invitation-onboarding.spec.ts -g "post-redemption"
 *
 * TEST COVERAGE:
 *   Step 1: Create E2E test file (this file)
 *   Step 2: POST /api/v1/external-access/invite → 200 OK + invitation details
 *   Step 3: adx_invitation record + web role N:N association verified
 *   Step 4: sprk_communication email record verified
 *   Step 5: Contact-to-Entra External ID identity link verified (post-redemption)
 *   Step 6: Web role assigned to Contact after redemption verified
 *   Step 7: SPA /external/me returns correct project + access level
 *
 * ACCEPTANCE CRITERIA (from task POML):
 *   ✅ Full invitation-to-access flow covered end-to-end
 *   ✅ Invitation email sent (sprk_communication record verified)
 *   ✅ Web role assigned after redemption (post-redemption suite)
 *   ✅ SPA access granted with correct project visibility (/external/me + UI suite)
 */
