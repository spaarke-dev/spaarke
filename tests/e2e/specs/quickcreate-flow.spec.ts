/**
 * E2E Tests: Quick Create Flow
 *
 * Tests validate the Quick Create functionality for creating new Dataverse entities
 * (Matter, Project, Invoice, Account, Contact) inline from the Office add-in
 * without leaving the save workflow.
 *
 * Test Coverage:
 * - Create all 5 supported entity types
 * - Validation of required fields per entity type
 * - Auto-selection of created entity in picker
 * - Cancel flow returns to picker
 * - Error handling for creation failures
 *
 * Prerequisites:
 * - Deployed Office add-in (Outlook or Word)
 * - BFF API with /office/quickcreate/* endpoints enabled
 * - Authentication configured in .env
 * - User has create permissions for target entities
 *
 * @see spec.md FR-03: Quick Create entity
 */

import { test, expect, Page, BrowserContext } from '@playwright/test';
import { DataverseAPI } from '../utils/dataverse-api';

// Entity logical names in Dataverse
const ENTITY_LOGICAL_NAMES = {
  matter: 'sprk_matter',
  project: 'sprk_project',
  invoice: 'sprk_invoice',
  account: 'account',
  contact: 'contact',
} as const;

// Test data for each entity type
const TEST_DATA = {
  matter: {
    name: `E2E Test Matter ${Date.now()}`,
    description: 'Created by E2E test',
  },
  project: {
    name: `E2E Test Project ${Date.now()}`,
    description: 'Created by E2E test',
  },
  invoice: {
    name: `E2E Test Invoice ${Date.now()}`,
    description: 'Created by E2E test',
  },
  account: {
    name: `E2E Test Account ${Date.now()}`,
    industry: 'Technology',
    city: 'Seattle',
  },
  contact: {
    firstName: 'E2E',
    lastName: `TestContact ${Date.now()}`,
    email: `e2e.test.${Date.now()}@example.com`,
  },
} as const;

// Selectors for Quick Create dialog and entity picker
const SELECTORS = {
  // Entity picker
  entityPicker: '[data-testid="entity-picker"]',
  entityPickerInput: '[data-testid="entity-picker-input"]',
  entityPickerOption: '[data-testid="entity-picker-option"]',
  createNewButton: '[data-testid="create-new-entity-button"]',

  // Quick Create dialog
  quickCreateDialog: '[data-testid="quick-create-dialog"]',
  quickCreateTitle: '[data-testid="quick-create-title"]',
  quickCreateForm: '[data-testid="quick-create-form"]',

  // Form fields
  nameInput: '[data-testid="quick-create-name"]',
  descriptionInput: '[data-testid="quick-create-description"]',
  industryInput: '[data-testid="quick-create-industry"]',
  cityInput: '[data-testid="quick-create-city"]',
  firstNameInput: '[data-testid="quick-create-firstname"]',
  lastNameInput: '[data-testid="quick-create-lastname"]',
  emailInput: '[data-testid="quick-create-email"]',

  // Buttons
  createButton: '[data-testid="quick-create-submit"]',
  cancelButton: '[data-testid="quick-create-cancel"]',

  // Status indicators
  loadingSpinner: '[data-testid="quick-create-loading"]',
  errorMessage: '[data-testid="quick-create-error"]',
  successMessage: '[data-testid="quick-create-success"]',
  fieldError: '[data-testid="field-error"]',
} as const;

/**
 * Page object for Quick Create flow interactions
 */
class QuickCreatePage {
  readonly page: Page;
  private createdEntityIds: { entityType: string; id: string }[] = [];

  constructor(page: Page) {
    this.page = page;
  }

  /**
   * Navigate to the save flow where Quick Create is available
   */
  async navigateToSaveFlow(): Promise<void> {
    // Navigate to task pane in save mode
    // Note: Actual URL will depend on add-in deployment
    await this.page.goto(`${process.env.OFFICE_ADDIN_URL || '/taskpane.html'}?mode=save`);
    await this.page.waitForSelector(SELECTORS.entityPicker, { state: 'visible' });
  }

  /**
   * Open the Quick Create dialog for a specific entity type
   */
  async openQuickCreateDialog(entityType: keyof typeof TEST_DATA): Promise<void> {
    // Click "Create New" button in entity picker
    await this.page.click(SELECTORS.createNewButton);

    // Wait for dialog to appear
    await this.page.waitForSelector(SELECTORS.quickCreateDialog, { state: 'visible' });

    // Select entity type if dropdown is present
    const entityTypeSelector = `[data-testid="entity-type-${entityType}"]`;
    const entityTypeOption = this.page.locator(entityTypeSelector);
    if (await entityTypeOption.isVisible()) {
      await entityTypeOption.click();
    }
  }

  /**
   * Fill the Quick Create form for Matter/Project/Invoice
   */
  async fillMatterProjectInvoiceForm(data: { name: string; description?: string }): Promise<void> {
    await this.page.fill(SELECTORS.nameInput, data.name);
    if (data.description) {
      await this.page.fill(SELECTORS.descriptionInput, data.description);
    }
  }

  /**
   * Fill the Quick Create form for Account
   */
  async fillAccountForm(data: { name: string; industry?: string; city?: string }): Promise<void> {
    await this.page.fill(SELECTORS.nameInput, data.name);
    if (data.industry) {
      await this.page.fill(SELECTORS.industryInput, data.industry);
    }
    if (data.city) {
      await this.page.fill(SELECTORS.cityInput, data.city);
    }
  }

  /**
   * Fill the Quick Create form for Contact
   */
  async fillContactForm(data: { firstName: string; lastName: string; email?: string }): Promise<void> {
    await this.page.fill(SELECTORS.firstNameInput, data.firstName);
    await this.page.fill(SELECTORS.lastNameInput, data.lastName);
    if (data.email) {
      await this.page.fill(SELECTORS.emailInput, data.email);
    }
  }

  /**
   * Submit the Quick Create form
   */
  async submitForm(): Promise<void> {
    await this.page.click(SELECTORS.createButton);
  }

  /**
   * Cancel the Quick Create dialog
   */
  async cancelDialog(): Promise<void> {
    await this.page.click(SELECTORS.cancelButton);
    await this.page.waitForSelector(SELECTORS.quickCreateDialog, { state: 'hidden' });
  }

  /**
   * Wait for successful creation
   */
  async waitForSuccess(timeout = 10000): Promise<string> {
    // Wait for loading to complete
    await this.page.waitForSelector(SELECTORS.loadingSpinner, { state: 'hidden', timeout });

    // Check for success message or dialog closing
    await expect(this.page.locator(SELECTORS.quickCreateDialog)).toBeHidden({ timeout });

    // Get the created entity ID from the picker (auto-selected)
    const selectedOption = this.page.locator(`${SELECTORS.entityPickerOption}[aria-selected="true"]`);
    const entityId = await selectedOption.getAttribute('data-entity-id');

    return entityId || '';
  }

  /**
   * Wait for validation error
   */
  async waitForValidationError(fieldName?: string): Promise<string> {
    if (fieldName) {
      const fieldErrorSelector = `[data-testid="field-error-${fieldName}"]`;
      await this.page.waitForSelector(fieldErrorSelector, { state: 'visible' });
      return await this.page.textContent(fieldErrorSelector) || '';
    }

    await this.page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });
    return await this.page.textContent(SELECTORS.errorMessage) || '';
  }

  /**
   * Verify entity is selected in picker
   */
  async verifyEntitySelectedInPicker(entityId: string, entityName: string): Promise<void> {
    const pickerInput = this.page.locator(SELECTORS.entityPickerInput);
    await expect(pickerInput).toHaveValue(entityName);

    const selectedOption = this.page.locator(`${SELECTORS.entityPickerOption}[data-entity-id="${entityId}"]`);
    await expect(selectedOption).toHaveAttribute('aria-selected', 'true');
  }

  /**
   * Track created entity for cleanup
   */
  trackCreatedEntity(entityType: string, id: string): void {
    this.createdEntityIds.push({ entityType, id });
  }

  /**
   * Get all created entities for cleanup
   */
  getCreatedEntities(): { entityType: string; id: string }[] {
    return [...this.createdEntityIds];
  }
}

// ============================================
// Test Suite: Quick Create Flow
// ============================================

test.describe('Quick Create Flow @e2e @office', () => {
  let quickCreatePage: QuickCreatePage;
  let dataverseApi: DataverseAPI;
  const createdEntities: { entityType: string; id: string }[] = [];

  test.beforeAll(async () => {
    // Initialize Dataverse API for test data cleanup
    const token = await DataverseAPI.authenticate(
      process.env.TENANT_ID || '',
      process.env.CLIENT_ID || '',
      process.env.CLIENT_SECRET || '',
      process.env.DATAVERSE_API_URL || ''
    );
    dataverseApi = new DataverseAPI(process.env.DATAVERSE_API_URL || '', token);
  });

  test.beforeEach(async ({ page }) => {
    quickCreatePage = new QuickCreatePage(page);
  });

  test.afterAll(async () => {
    // Cleanup created test entities
    for (const entity of createdEntities) {
      const logicalName = ENTITY_LOGICAL_NAMES[entity.entityType as keyof typeof ENTITY_LOGICAL_NAMES];
      if (logicalName && entity.id) {
        try {
          await dataverseApi.deleteRecord(logicalName, entity.id);
        } catch (error) {
          console.warn(`Failed to cleanup ${entity.entityType} ${entity.id}:`, error);
        }
      }
    }
  });

  // ============================================
  // Test: Create Matter via Quick Create
  // ============================================

  test('should create Matter via Quick Create', async ({ page }) => {
    await quickCreatePage.navigateToSaveFlow();
    await quickCreatePage.openQuickCreateDialog('matter');

    // Fill form
    await quickCreatePage.fillMatterProjectInvoiceForm(TEST_DATA.matter);

    // Submit
    await quickCreatePage.submitForm();

    // Wait for success
    const entityId = await quickCreatePage.waitForSuccess();
    expect(entityId).toBeTruthy();

    // Track for cleanup
    createdEntities.push({ entityType: 'matter', id: entityId });

    // Verify entity is selected in picker
    await quickCreatePage.verifyEntitySelectedInPicker(entityId, TEST_DATA.matter.name);
  });

  // ============================================
  // Test: Create Project via Quick Create
  // ============================================

  test('should create Project via Quick Create', async ({ page }) => {
    await quickCreatePage.navigateToSaveFlow();
    await quickCreatePage.openQuickCreateDialog('project');

    // Fill form
    await quickCreatePage.fillMatterProjectInvoiceForm(TEST_DATA.project);

    // Submit
    await quickCreatePage.submitForm();

    // Wait for success
    const entityId = await quickCreatePage.waitForSuccess();
    expect(entityId).toBeTruthy();

    // Track for cleanup
    createdEntities.push({ entityType: 'project', id: entityId });

    // Verify entity is selected in picker
    await quickCreatePage.verifyEntitySelectedInPicker(entityId, TEST_DATA.project.name);
  });

  // ============================================
  // Test: Create Account via Quick Create
  // ============================================

  test('should create Account via Quick Create', async ({ page }) => {
    await quickCreatePage.navigateToSaveFlow();
    await quickCreatePage.openQuickCreateDialog('account');

    // Fill form with Account-specific fields
    await quickCreatePage.fillAccountForm(TEST_DATA.account);

    // Submit
    await quickCreatePage.submitForm();

    // Wait for success
    const entityId = await quickCreatePage.waitForSuccess();
    expect(entityId).toBeTruthy();

    // Track for cleanup
    createdEntities.push({ entityType: 'account', id: entityId });

    // Verify entity is selected in picker
    await quickCreatePage.verifyEntitySelectedInPicker(entityId, TEST_DATA.account.name);
  });

  // ============================================
  // Test: Create Contact via Quick Create
  // ============================================

  test('should create Contact via Quick Create', async ({ page }) => {
    await quickCreatePage.navigateToSaveFlow();
    await quickCreatePage.openQuickCreateDialog('contact');

    // Fill form with Contact-specific fields
    await quickCreatePage.fillContactForm(TEST_DATA.contact);

    // Submit
    await quickCreatePage.submitForm();

    // Wait for success
    const entityId = await quickCreatePage.waitForSuccess();
    expect(entityId).toBeTruthy();

    // Track for cleanup
    createdEntities.push({ entityType: 'contact', id: entityId });

    // Verify entity is selected in picker (Contact uses full name)
    const fullName = `${TEST_DATA.contact.firstName} ${TEST_DATA.contact.lastName}`;
    await quickCreatePage.verifyEntitySelectedInPicker(entityId, fullName);
  });

  // ============================================
  // Test: Create Invoice via Quick Create
  // ============================================

  test('should create Invoice via Quick Create', async ({ page }) => {
    await quickCreatePage.navigateToSaveFlow();
    await quickCreatePage.openQuickCreateDialog('invoice');

    // Fill form
    await quickCreatePage.fillMatterProjectInvoiceForm(TEST_DATA.invoice);

    // Submit
    await quickCreatePage.submitForm();

    // Wait for success
    const entityId = await quickCreatePage.waitForSuccess();
    expect(entityId).toBeTruthy();

    // Track for cleanup
    createdEntities.push({ entityType: 'invoice', id: entityId });

    // Verify entity is selected in picker
    await quickCreatePage.verifyEntitySelectedInPicker(entityId, TEST_DATA.invoice.name);
  });

  // ============================================
  // Test: Validation for Required Fields
  // ============================================

  test.describe('Validation for required fields', () => {
    test('should show validation error for Matter without name', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');

      // Submit without filling name
      await quickCreatePage.submitForm();

      // Expect validation error
      const errorMessage = await quickCreatePage.waitForValidationError('name');
      expect(errorMessage).toContain('Name is required');

      // Dialog should remain open
      await expect(page.locator(SELECTORS.quickCreateDialog)).toBeVisible();
    });

    test('should show validation error for Contact without firstName', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('contact');

      // Only fill lastName
      await page.fill(SELECTORS.lastNameInput, 'TestLastName');

      // Submit
      await quickCreatePage.submitForm();

      // Expect validation error for firstName
      const errorMessage = await quickCreatePage.waitForValidationError('firstName');
      expect(errorMessage).toContain('First name is required');
    });

    test('should show validation error for Contact without lastName', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('contact');

      // Only fill firstName
      await page.fill(SELECTORS.firstNameInput, 'TestFirstName');

      // Submit
      await quickCreatePage.submitForm();

      // Expect validation error for lastName
      const errorMessage = await quickCreatePage.waitForValidationError('lastName');
      expect(errorMessage).toContain('Last name is required');
    });

    test('should show validation error for Account without name', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('account');

      // Fill only optional fields
      await page.fill(SELECTORS.industryInput, 'Technology');

      // Submit
      await quickCreatePage.submitForm();

      // Expect validation error for name
      const errorMessage = await quickCreatePage.waitForValidationError('name');
      expect(errorMessage).toContain('Name is required');
    });

    test('should clear validation errors when field is corrected', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');

      // Submit without name
      await quickCreatePage.submitForm();

      // Verify error is shown
      await quickCreatePage.waitForValidationError('name');
      expect(await page.locator('[data-testid="field-error-name"]').isVisible()).toBeTruthy();

      // Fill the name field
      await page.fill(SELECTORS.nameInput, 'Corrected Name');

      // Error should be cleared (on blur or input)
      await page.locator(SELECTORS.descriptionInput).click(); // Trigger blur
      await expect(page.locator('[data-testid="field-error-name"]')).toBeHidden();
    });
  });

  // ============================================
  // Test: Created Entity Auto-Selected in Picker
  // ============================================

  test.describe('Entity picker refresh after create', () => {
    test('should auto-select created Matter in picker', async ({ page }) => {
      const testMatterName = `AutoSelect Test Matter ${Date.now()}`;

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: testMatterName });
      await quickCreatePage.submitForm();

      // Wait for dialog to close and entity to be created
      const entityId = await quickCreatePage.waitForSuccess();
      createdEntities.push({ entityType: 'matter', id: entityId });

      // Verify the picker shows the new entity
      const pickerInput = page.locator(SELECTORS.entityPickerInput);
      await expect(pickerInput).toHaveValue(testMatterName);

      // Open picker dropdown and verify new entity is at top
      await pickerInput.click();
      const firstOption = page.locator(`${SELECTORS.entityPickerOption}`).first();
      await expect(firstOption).toContainText(testMatterName);
    });

    test('should show created entity when searching in picker', async ({ page }) => {
      const uniqueSearchTerm = `UniqueE2E${Date.now()}`;
      const testProjectName = `${uniqueSearchTerm} Test Project`;

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('project');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: testProjectName });
      await quickCreatePage.submitForm();

      const entityId = await quickCreatePage.waitForSuccess();
      createdEntities.push({ entityType: 'project', id: entityId });

      // Clear selection and search for the unique term
      await page.fill(SELECTORS.entityPickerInput, uniqueSearchTerm);

      // Wait for search results
      await page.waitForSelector(SELECTORS.entityPickerOption, { state: 'visible' });

      // Verify the created entity appears in results
      const searchResults = page.locator(SELECTORS.entityPickerOption);
      await expect(searchResults.first()).toContainText(testProjectName);
    });
  });

  // ============================================
  // Test: Cancel Quick Create and Return to Picker
  // ============================================

  test.describe('Cancel Quick Create flow', () => {
    test('should return to picker when canceling Quick Create', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();

      // Verify picker is visible initially
      await expect(page.locator(SELECTORS.entityPicker)).toBeVisible();

      // Open Quick Create
      await quickCreatePage.openQuickCreateDialog('matter');
      await expect(page.locator(SELECTORS.quickCreateDialog)).toBeVisible();

      // Cancel
      await quickCreatePage.cancelDialog();

      // Verify dialog is closed and picker is visible again
      await expect(page.locator(SELECTORS.quickCreateDialog)).toBeHidden();
      await expect(page.locator(SELECTORS.entityPicker)).toBeVisible();
    });

    test('should not create entity when canceling with filled form', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('account');

      // Fill form
      const testAccountName = `Cancel Test Account ${Date.now()}`;
      await quickCreatePage.fillAccountForm({ name: testAccountName });

      // Cancel instead of submit
      await quickCreatePage.cancelDialog();

      // Verify no entity was created by searching
      await page.fill(SELECTORS.entityPickerInput, testAccountName);
      await page.waitForTimeout(1000); // Wait for search debounce

      // Should show "no results" or empty state
      const noResults = page.locator('[data-testid="entity-picker-no-results"]');
      const results = page.locator(SELECTORS.entityPickerOption);

      // Either no results message or no matching results
      const hasNoResults = await noResults.isVisible();
      const matchingResults = await results.filter({ hasText: testAccountName }).count();

      expect(hasNoResults || matchingResults === 0).toBeTruthy();
    });

    test('should preserve picker selection when canceling Quick Create', async ({ page }) => {
      await quickCreatePage.navigateToSaveFlow();

      // First, search and select an existing entity
      await page.fill(SELECTORS.entityPickerInput, 'test');
      await page.waitForSelector(SELECTORS.entityPickerOption, { state: 'visible' });

      // Click first result to select
      const firstResult = page.locator(SELECTORS.entityPickerOption).first();
      const selectedName = await firstResult.textContent();
      await firstResult.click();

      // Open Quick Create
      await quickCreatePage.openQuickCreateDialog('matter');

      // Fill some data but cancel
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'Should Not Create' });
      await quickCreatePage.cancelDialog();

      // Verify original selection is preserved
      const pickerInput = page.locator(SELECTORS.entityPickerInput);
      await expect(pickerInput).toHaveValue(selectedName?.trim() || '');
    });
  });

  // ============================================
  // Test: Error Handling for Creation Failures
  // ============================================

  test.describe('Error handling for creation failures', () => {
    test('should display error message when API returns error', async ({ page, context }) => {
      // Mock API to return error
      await context.route('**/office/quickcreate/**', async (route) => {
        await route.fulfill({
          status: 500,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            type: 'https://spaarke.com/errors/internal-error',
            title: 'Internal Server Error',
            status: 500,
            detail: 'Failed to create entity in Dataverse',
            errorCode: 'OFFICE_014',
          }),
        });
      });

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'Error Test Matter' });
      await quickCreatePage.submitForm();

      // Verify error message is displayed
      await page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });
      const errorText = await page.textContent(SELECTORS.errorMessage);
      expect(errorText).toContain('Failed to create entity');

      // Dialog should remain open
      await expect(page.locator(SELECTORS.quickCreateDialog)).toBeVisible();
    });

    test('should display network error when offline', async ({ page, context }) => {
      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'Offline Test Matter' });

      // Simulate offline
      await context.setOffline(true);

      // Submit
      await quickCreatePage.submitForm();

      // Verify network error is displayed
      await page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });
      const errorText = await page.textContent(SELECTORS.errorMessage);
      expect(errorText).toMatch(/network|offline|connection/i);

      // Restore online
      await context.setOffline(false);
    });

    test('should display permission error when user lacks create permission', async ({ page, context }) => {
      // Mock API to return 403 Forbidden
      await context.route('**/office/quickcreate/matter', async (route) => {
        await route.fulfill({
          status: 403,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            type: 'https://spaarke.com/errors/access-denied',
            title: 'Forbidden',
            status: 403,
            detail: 'You do not have permission to create Matter records',
            errorCode: 'OFFICE_010',
          }),
        });
      });

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'Permission Test Matter' });
      await quickCreatePage.submitForm();

      // Verify permission error is displayed
      await page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });
      const errorText = await page.textContent(SELECTORS.errorMessage);
      expect(errorText).toContain('permission');
    });

    test('should display rate limit error with retry-after', async ({ page, context }) => {
      // Mock API to return 429 Too Many Requests
      await context.route('**/office/quickcreate/**', async (route) => {
        await route.fulfill({
          status: 429,
          contentType: 'application/problem+json',
          headers: {
            'Retry-After': '60',
          },
          body: JSON.stringify({
            type: 'https://spaarke.com/errors/rate-limited',
            title: 'Too Many Requests',
            status: 429,
            detail: 'Rate limit exceeded. Please try again later.',
          }),
        });
      });

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'RateLimit Test Matter' });
      await quickCreatePage.submitForm();

      // Verify rate limit error is displayed
      await page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });
      const errorText = await page.textContent(SELECTORS.errorMessage);
      expect(errorText).toMatch(/rate limit|too many requests|try again/i);
    });

    test('should allow retry after error', async ({ page, context }) => {
      let requestCount = 0;

      // Mock API to fail first, then succeed
      await context.route('**/office/quickcreate/matter', async (route) => {
        requestCount++;
        if (requestCount === 1) {
          await route.fulfill({
            status: 500,
            contentType: 'application/problem+json',
            body: JSON.stringify({
              type: 'https://spaarke.com/errors/internal-error',
              title: 'Internal Server Error',
              status: 500,
              detail: 'Temporary failure',
            }),
          });
        } else {
          await route.fulfill({
            status: 201,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'test-matter-id-123',
              entityType: 'Matter',
              logicalName: 'sprk_matter',
              name: 'Retry Test Matter',
              url: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=test-matter-id-123',
            }),
          });
        }
      });

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'Retry Test Matter' });

      // First attempt - should fail
      await quickCreatePage.submitForm();
      await page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });

      // Retry - should succeed
      await quickCreatePage.submitForm();
      await quickCreatePage.waitForSuccess();

      expect(requestCount).toBe(2);
    });

    test('should handle duplicate entity error gracefully', async ({ page, context }) => {
      // Mock API to return 409 Conflict
      await context.route('**/office/quickcreate/matter', async (route) => {
        await route.fulfill({
          status: 409,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            type: 'https://spaarke.com/errors/duplicate-entity',
            title: 'Conflict',
            status: 409,
            detail: 'A Matter with this name already exists',
            errorCode: 'OFFICE_011',
          }),
        });
      });

      await quickCreatePage.navigateToSaveFlow();
      await quickCreatePage.openQuickCreateDialog('matter');
      await quickCreatePage.fillMatterProjectInvoiceForm({ name: 'Duplicate Test Matter' });
      await quickCreatePage.submitForm();

      // Verify duplicate error is displayed
      await page.waitForSelector(SELECTORS.errorMessage, { state: 'visible' });
      const errorText = await page.textContent(SELECTORS.errorMessage);
      expect(errorText).toContain('already exists');
    });
  });
});

/**
 * NOTE: These are E2E tests that require a deployed environment
 *
 * To run these tests:
 * 1. Deploy Office add-in to test environment
 * 2. Configure BFF API with /office/quickcreate/* endpoints enabled
 * 3. Configure .env with:
 *    - OFFICE_ADDIN_URL (task pane URL)
 *    - DATAVERSE_API_URL
 *    - TENANT_ID, CLIENT_ID, CLIENT_SECRET
 *    - Authentication credentials
 * 4. Ensure test user has create permissions for all entity types
 *
 * Run with:
 *   npx playwright test quickcreate-flow.spec.ts --headed
 *
 * Run specific test:
 *   npx playwright test quickcreate-flow.spec.ts -g "should create Matter"
 */
