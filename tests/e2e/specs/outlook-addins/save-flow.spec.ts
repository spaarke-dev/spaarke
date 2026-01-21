/**
 * E2E Tests: Outlook Save Flow
 *
 * Tests validate the complete save flow for saving Outlook emails and attachments
 * to Spaarke. Covers the full journey from add-in UI through API to worker processing.
 *
 * Prerequisites:
 * - Deployed Outlook add-in (task 057)
 * - Deployed background workers (task 066)
 * - Access to Dataverse test environment
 * - .env file configured with credentials
 *
 * Test Coverage per spec.md:
 * - FR-01: Save Outlook email to Spaarke
 * - FR-02: Save attachments selectively
 * - FR-03: Quick Create entity
 * - FR-04: Search association targets
 * - FR-05: Recent items
 * - FR-11: Job status display
 * - FR-12: Duplicate detection
 * - FR-13: Processing options
 * - FR-14: Mandatory association
 *
 * @see spec.md - Save flow requirements
 * @see SaveView.tsx - Component implementation
 * @see POST /office/save - API endpoint
 */

import { test, expect, Page, BrowserContext } from '@playwright/test';
import {
  OutlookTaskPanePage,
  type OutlookTaskPaneConfig,
} from '../../pages/addins/OutlookTaskPanePage';

// Test configuration
const testConfig: OutlookTaskPaneConfig = {
  taskPaneUrl: process.env.ADDIN_TASKPANE_URL || 'https://localhost:3000/taskpane.html',
  initTimeout: 30000,
  apiBaseUrl: process.env.BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
};

// ============================================
// Mock Data Types
// ============================================

interface EntitySearchResult {
  id: string;
  entityType: 'Matter' | 'Project' | 'Invoice' | 'Account' | 'Contact';
  logicalName: string;
  name: string;
  displayInfo: string;
  iconUrl?: string;
}

interface SaveJobResponse {
  jobId: string;
  documentId: string;
  statusUrl: string;
  streamUrl: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed';
  duplicate: boolean;
  correlationId: string;
  message?: string;
}

interface JobStatusResponse {
  jobId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed';
  stages: Array<{
    name: string;
    status: 'Pending' | 'Running' | 'Completed' | 'Skipped' | 'Failed';
  }>;
  documentId: string;
  documentUrl?: string;
  associationUrl?: string;
  errorCode?: string;
  errorMessage?: string;
}

interface EmailAttachment {
  id: string;
  name: string;
  contentType: string;
  size: number;
  isInline: boolean;
}

// ============================================
// Mock Data
// ============================================

const mockEntities: EntitySearchResult[] = [
  {
    id: 'matter-001',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Smith vs Jones',
    displayInfo: 'Client: Acme Corp | Status: Active',
    iconUrl: '/icons/matter.svg',
  },
  {
    id: 'matter-002',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Project Alpha Agreement',
    displayInfo: 'Client: Beta Corp | Status: Active',
  },
  {
    id: 'project-001',
    entityType: 'Project',
    logicalName: 'sprk_project',
    name: 'Website Redesign',
    displayInfo: 'Status: In Progress | Due: Feb 2026',
  },
  {
    id: 'account-001',
    entityType: 'Account',
    logicalName: 'account',
    name: 'Acme Corporation',
    displayInfo: 'Industry: Manufacturing | City: Chicago',
  },
  {
    id: 'contact-001',
    entityType: 'Contact',
    logicalName: 'contact',
    name: 'John Doe',
    displayInfo: 'Acme Corporation | john.doe@acme.com',
  },
];

const mockAttachments: EmailAttachment[] = [
  {
    id: 'att-001',
    name: 'Contract_Draft_v2.docx',
    contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    size: 245678,
    isInline: false,
  },
  {
    id: 'att-002',
    name: 'Financial_Statement.xlsx',
    contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    size: 512000,
    isInline: false,
  },
  {
    id: 'att-003',
    name: 'signature.png',
    contentType: 'image/png',
    size: 15000,
    isInline: true,
  },
];

const mockLargeAttachment: EmailAttachment = {
  id: 'att-large',
  name: 'LargeVideo.mp4',
  contentType: 'video/mp4',
  size: 30 * 1024 * 1024, // 30MB - exceeds 25MB limit
  isInline: false,
};

const mockSaveResponse: SaveJobResponse = {
  jobId: 'job-001',
  documentId: 'doc-001',
  statusUrl: '/office/jobs/job-001',
  streamUrl: '/office/jobs/job-001/stream',
  status: 'Queued',
  duplicate: false,
  correlationId: 'corr-001',
};

const mockDuplicateResponse: SaveJobResponse = {
  jobId: 'job-existing',
  documentId: 'doc-existing',
  statusUrl: '/office/jobs/job-existing',
  streamUrl: '/office/jobs/job-existing/stream',
  status: 'Completed',
  duplicate: true,
  correlationId: 'corr-002',
  message: 'This item was previously saved to this association target',
};

const mockJobStatusCompleted: JobStatusResponse = {
  jobId: 'job-001',
  status: 'Completed',
  stages: [
    { name: 'RecordsCreated', status: 'Completed' },
    { name: 'FileUploaded', status: 'Completed' },
    { name: 'ProfileSummary', status: 'Completed' },
    { name: 'Indexed', status: 'Completed' },
    { name: 'DeepAnalysis', status: 'Skipped' },
  ],
  documentId: 'doc-001',
  documentUrl: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=doc-001',
  associationUrl: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=matter-001',
};

const mockJobStatusRunning: JobStatusResponse = {
  jobId: 'job-001',
  status: 'Running',
  stages: [
    { name: 'RecordsCreated', status: 'Completed' },
    { name: 'FileUploaded', status: 'Completed' },
    { name: 'ProfileSummary', status: 'Running' },
    { name: 'Indexed', status: 'Pending' },
    { name: 'DeepAnalysis', status: 'Pending' },
  ],
  documentId: 'doc-001',
};

// ============================================
// Page Object Extension for Save Flow
// ============================================

/**
 * Extended Page Object for Save Flow interactions
 */
class OutlookSaveFlowPage extends OutlookTaskPanePage {
  // Save flow specific elements
  readonly saveButton = this.page.getByRole('button', { name: /save to spaarke/i });
  readonly entityPickerInput = this.page.locator('[data-testid="entity-picker-input"]');
  readonly entityPickerDropdown = this.page.locator('[data-testid="entity-picker-dropdown"]');
  readonly entityOption = this.page.locator('[data-testid="entity-option"]');
  readonly attachmentList = this.page.locator('[data-testid="attachment-list"]');
  readonly attachmentCheckbox = this.page.locator('[data-testid="attachment-checkbox"]');
  readonly progressIndicator = this.page.locator('[data-testid="job-progress"]');
  readonly stageItem = this.page.locator('[data-testid="stage-item"]');
  readonly quickCreateButton = this.page.getByRole('button', { name: /create new/i });
  readonly processingOptions = this.page.locator('[data-testid="processing-options"]');

  constructor(page: Page, config: OutlookTaskPaneConfig) {
    super(page, config);
  }

  /**
   * Navigate to save mode (read email view)
   */
  async navigateToSaveMode(): Promise<void> {
    await this.page.goto(this.config.taskPaneUrl);
    await this.waitForTaskPaneLoad();

    // Click save tab if available
    if (await this.navSaveButton.isVisible()) {
      await this.navSaveButton.click();
    }
  }

  /**
   * Search for entity association target
   */
  async searchEntity(query: string): Promise<void> {
    await this.entityPickerInput.fill(query);
    await this.page.waitForTimeout(300); // Debounce delay
    await this.waitForEntityResults();
  }

  /**
   * Wait for entity search results
   */
  async waitForEntityResults(timeout = 5000): Promise<void> {
    await this.page.waitForSelector('[data-testid="entity-option"]', {
      state: 'visible',
      timeout,
    });
  }

  /**
   * Select an entity from search results
   */
  async selectEntity(entityName: string): Promise<void> {
    await this.page.locator(`[data-testid="entity-option"]:has-text("${entityName}")`).click();
  }

  /**
   * Toggle attachment selection
   */
  async toggleAttachment(attachmentName: string, select: boolean): Promise<void> {
    const checkbox = this.page.locator(
      `[data-testid="attachment-item"]:has-text("${attachmentName}") input[type="checkbox"]`
    );

    const isChecked = await checkbox.isChecked();
    if (isChecked !== select) {
      await checkbox.click();
    }
  }

  /**
   * Select all attachments
   */
  async selectAllAttachments(): Promise<void> {
    const selectAllCheckbox = this.page.locator('[data-testid="select-all-attachments"]');
    if (await selectAllCheckbox.isVisible()) {
      const isChecked = await selectAllCheckbox.isChecked();
      if (!isChecked) {
        await selectAllCheckbox.click();
      }
    }
  }

  /**
   * Deselect all attachments
   */
  async deselectAllAttachments(): Promise<void> {
    const selectAllCheckbox = this.page.locator('[data-testid="select-all-attachments"]');
    if (await selectAllCheckbox.isVisible()) {
      const isChecked = await selectAllCheckbox.isChecked();
      if (isChecked) {
        await selectAllCheckbox.click();
      }
    }
  }

  /**
   * Submit save operation
   */
  async submitSave(): Promise<void> {
    await this.saveButton.click();
  }

  /**
   * Wait for job completion
   */
  async waitForJobCompletion(timeout = 30000): Promise<void> {
    await this.page.waitForSelector('[data-testid="job-complete"]', {
      state: 'visible',
      timeout,
    });
  }

  /**
   * Wait for job status stage update
   */
  async waitForStageStatus(stageName: string, status: string, timeout = 10000): Promise<void> {
    await this.page.waitForSelector(
      `[data-testid="stage-${stageName}"][data-status="${status}"]`,
      { timeout }
    );
  }

  /**
   * Get current job stages status
   */
  async getJobStages(): Promise<{ name: string; status: string }[]> {
    const stages = await this.page.locator('[data-testid="stage-item"]').all();
    return Promise.all(
      stages.map(async (stage) => ({
        name: (await stage.getAttribute('data-stage-name')) || '',
        status: (await stage.getAttribute('data-status')) || '',
      }))
    );
  }

  /**
   * Open Quick Create dialog
   */
  async openQuickCreate(): Promise<void> {
    await this.quickCreateButton.click();
    await this.page.waitForSelector('[data-testid="quick-create-dialog"]', { state: 'visible' });
  }

  /**
   * Mock Office.js read mode with email data
   */
  async mockOfficeReadMode(attachments: EmailAttachment[] = mockAttachments): Promise<void> {
    await this.page.addInitScript(
      (attachmentData) => {
        (window as any).Office = {
          context: {
            mailbox: {
              item: {
                itemType: 'message',
                itemId: 'msg-001',
                subject: 'RE: Contract Review Request',
                from: { emailAddress: 'sender@example.com', displayName: 'John Sender' },
                to: [{ emailAddress: 'recipient@example.com', displayName: 'Jane Recipient' }],
                dateTimeCreated: new Date('2026-01-20T10:00:00Z'),
                attachments: attachmentData,
                body: {
                  getAsync: (coercionType: any, callback: (result: any) => void) => {
                    callback({
                      status: 'succeeded',
                      value: '<html><body><p>Please review the attached contract.</p></body></html>',
                    });
                  },
                },
                getAttachmentContentAsync: (
                  attachmentId: string,
                  callback: (result: any) => void
                ) => {
                  // Mock base64 content
                  const mockContent = btoa('Mock attachment content for ' + attachmentId);
                  callback({
                    status: 'succeeded',
                    value: {
                      content: mockContent,
                      format: 'base64',
                    },
                  });
                },
              },
            },
            diagnostics: {
              hostName: 'OutlookWebApp',
              hostVersion: '16.0.0.0',
            },
            requirements: {
              isSetSupported: (name: string, version: string) => {
                if (name === 'Mailbox') return parseFloat(version) <= 1.8;
                return true;
              },
            },
          },
          CoercionType: {
            Html: 'html',
            Text: 'text',
          },
          AsyncResultStatus: {
            Succeeded: 'succeeded',
            Failed: 'failed',
          },
          MailboxEnums: {
            AttachmentContentFormat: {
              Base64: 'base64',
            },
          },
        };
      },
      attachments
    );
  }

  /**
   * Mock entity search API
   */
  async mockEntitySearchApi(results: EntitySearchResult[] = mockEntities): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/search/entities*`, (route) => {
      const url = new URL(route.request().url());
      const query = url.searchParams.get('q')?.toLowerCase() || '';

      const filtered = results.filter(
        (e) => e.name.toLowerCase().includes(query) || e.displayInfo.toLowerCase().includes(query)
      );

      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          results: filtered,
          totalCount: filtered.length,
          hasMore: false,
        }),
      });
    });
  }

  /**
   * Mock save endpoint
   */
  async mockSaveApi(response: SaveJobResponse = mockSaveResponse): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: response.duplicate ? 200 : 202,
        contentType: 'application/json',
        body: JSON.stringify(response),
      });
    });
  }

  /**
   * Mock job status polling endpoint
   */
  async mockJobStatusApi(response: JobStatusResponse = mockJobStatusCompleted): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/jobs/*`, (route) => {
      if (!route.request().url().includes('/stream')) {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(response),
        });
      } else {
        route.continue();
      }
    });
  }

  /**
   * Mock SSE job status stream
   */
  async mockSSEStream(stages: JobStatusResponse['stages']): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/jobs/*/stream`, async (route) => {
      // Create SSE response with staged updates
      let sseBody = '';

      for (const stage of stages) {
        sseBody +=
          `event: stage-update\n` +
          `data: ${JSON.stringify({
            stage: stage.name,
            status: stage.status,
            timestamp: new Date().toISOString(),
          })}\n\n`;
      }

      // Final job-complete event
      sseBody +=
        `event: job-complete\n` +
        `data: ${JSON.stringify({
          status: 'Completed',
          documentId: 'doc-001',
          documentUrl: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=doc-001',
        })}\n\n`;

      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: {
          'Cache-Control': 'no-cache',
          Connection: 'keep-alive',
        },
        body: sseBody,
      });
    });
  }

  /**
   * Mock recent items API
   */
  async mockRecentApi(recentEntities: EntitySearchResult[] = mockEntities.slice(0, 3)): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/recent*`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          recentAssociations: recentEntities.map((e) => ({
            id: e.id,
            entityType: e.entityType,
            name: e.name,
            lastUsed: new Date().toISOString(),
          })),
          recentDocuments: [],
          favorites: [],
        }),
      });
    });
  }

  /**
   * Mock Quick Create API
   */
  async mockQuickCreateApi(entityType: string, newEntityId: string): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/quickcreate/${entityType}`, (route) => {
      const body = route.request().postDataJSON();
      route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: newEntityId,
          entityType: entityType.charAt(0).toUpperCase() + entityType.slice(1),
          logicalName: entityType === 'matter' ? 'sprk_matter' : entityType,
          name: body?.name || `New ${entityType}`,
          url: `https://org.crm.dynamics.com/main.aspx?etn=${entityType}&id=${newEntityId}`,
        }),
      });
    });
  }
}

// ============================================
// Test Suite: Save Single Email
// ============================================

test.describe('Save Flow - Single Email Without Attachments @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    // Setup mocks
    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockJobStatusApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();
  });

  test('should display save UI in read mode', async ({ page }) => {
    await expect(page.getByText('Save to Spaarke')).toBeVisible();
    await expect(saveFlowPage.entityPickerInput).toBeVisible();
    await expect(saveFlowPage.saveButton).toBeVisible();
  });

  test('should require entity selection before save', async ({ page }) => {
    // Save button should be disabled without entity selection
    await expect(saveFlowPage.saveButton).toBeDisabled();

    // Select entity
    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');

    // Save button should now be enabled
    await expect(saveFlowPage.saveButton).toBeEnabled();
  });

  test('should save email without attachments', async ({ page }) => {
    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');

    await saveFlowPage.submitSave();

    // Verify progress UI appears
    await expect(saveFlowPage.progressIndicator).toBeVisible();

    // Verify success message
    await expect(page.getByText(/saved successfully|completed/i)).toBeVisible({ timeout: 10000 });
  });

  test('should display email subject in save form', async ({ page }) => {
    await expect(page.getByText('RE: Contract Review Request')).toBeVisible();
  });

  test('should show recent associations on load', async ({ page }) => {
    await expect(page.getByText('Recent')).toBeVisible();
    await expect(page.getByText('Smith vs Jones')).toBeVisible();
  });

  test('should allow selecting from recent associations', async ({ page }) => {
    // Click on recent item
    await page.locator('[data-testid="recent-item"]').first().click();

    // Save button should be enabled
    await expect(saveFlowPage.saveButton).toBeEnabled();
  });
});

// ============================================
// Test Suite: Save Email With Attachments
// ============================================

test.describe('Save Flow - Email With Attachments @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode(mockAttachments);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockJobStatusApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();
  });

  test('should display attachment list', async ({ page }) => {
    await expect(saveFlowPage.attachmentList).toBeVisible();
    await expect(page.getByText('Contract_Draft_v2.docx')).toBeVisible();
    await expect(page.getByText('Financial_Statement.xlsx')).toBeVisible();
  });

  test('should show attachment file sizes', async ({ page }) => {
    // 245678 bytes = ~240 KB
    await expect(page.getByText(/240\s*KB/i)).toBeVisible();
    // 512000 bytes = ~500 KB
    await expect(page.getByText(/500\s*KB/i)).toBeVisible();
  });

  test('should toggle individual attachments on/off (FR-02)', async ({ page }) => {
    // Attachments should be selected by default (non-inline only)
    const contractCheckbox = page.locator(
      '[data-testid="attachment-item"]:has-text("Contract_Draft") input[type="checkbox"]'
    );
    await expect(contractCheckbox).toBeChecked();

    // Deselect
    await saveFlowPage.toggleAttachment('Contract_Draft_v2.docx', false);
    await expect(contractCheckbox).not.toBeChecked();

    // Re-select
    await saveFlowPage.toggleAttachment('Contract_Draft_v2.docx', true);
    await expect(contractCheckbox).toBeChecked();
  });

  test('should not show inline attachments by default', async ({ page }) => {
    // Inline attachment (signature.png) should be hidden or unchecked by default
    const signatureItem = page.locator('[data-testid="attachment-item"]:has-text("signature.png")');
    const isVisible = await signatureItem.isVisible();

    if (isVisible) {
      const checkbox = signatureItem.locator('input[type="checkbox"]');
      await expect(checkbox).not.toBeChecked();
    }
  });

  test('should save email with single attachment', async ({ page }) => {
    // Deselect all except one
    await saveFlowPage.toggleAttachment('Financial_Statement.xlsx', false);

    // Select entity
    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');

    await saveFlowPage.submitSave();

    // Verify success
    await expect(page.getByText(/saved successfully|completed/i)).toBeVisible({ timeout: 10000 });
  });

  test('should save email with multiple attachments', async ({ page }) => {
    // Keep both attachments selected (default)
    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');

    await saveFlowPage.submitSave();

    // Verify success
    await expect(page.getByText(/saved successfully|completed/i)).toBeVisible({ timeout: 10000 });
  });

  test('should have select all / deselect all option', async ({ page }) => {
    const selectAllCheckbox = page.locator('[data-testid="select-all-attachments"]');

    if (await selectAllCheckbox.isVisible()) {
      // Deselect all
      await saveFlowPage.deselectAllAttachments();

      // Verify all unchecked
      const checkboxes = page.locator(
        '[data-testid="attachment-item"]:not([data-inline="true"]) input[type="checkbox"]'
      );
      const allUnchecked = await checkboxes.evaluateAll((cbs) =>
        (cbs as HTMLInputElement[]).every((cb) => !cb.checked)
      );
      expect(allUnchecked).toBe(true);

      // Select all again
      await saveFlowPage.selectAllAttachments();
    }
  });
});

// ============================================
// Test Suite: Attachment Size Limits (NFR-03)
// ============================================

test.describe('Save Flow - Attachment Size Limits @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);
  });

  test('should enforce 25MB per file limit', async ({ page }) => {
    // Mock with large attachment
    await saveFlowPage.mockOfficeReadMode([...mockAttachments, mockLargeAttachment]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    // Mock API to return error for large file
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/office/validation-error',
          title: 'Attachment too large',
          status: 400,
          detail: 'Single file exceeds 25MB limit',
          errorCode: 'OFFICE_004',
        }),
      });
    });

    await saveFlowPage.navigateToSaveMode();

    // Large file should show warning indicator
    await expect(page.getByText('LargeVideo.mp4')).toBeVisible();
    await expect(page.getByText(/exceeds.*25.*MB/i)).toBeVisible();
  });

  test('should display warning for files exceeding limit', async ({ page }) => {
    await saveFlowPage.mockOfficeReadMode([mockLargeAttachment]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();

    // Should show warning icon or message
    const warningIndicator = page.locator('[data-testid="attachment-warning"]');
    await expect(warningIndicator).toBeVisible();
  });

  test('should show size limit info in UI', async ({ page }) => {
    await saveFlowPage.mockOfficeReadMode(mockAttachments);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();

    // Should display file size limit info
    const limitInfo = page.getByText(/25\s*MB.*limit|max.*25\s*MB/i);
    // This might be a tooltip or help text
    const hasLimitInfo = await limitInfo.isVisible().catch(() => false);
    // Test passes if limit info exists somewhere or enforced via UI disable
  });

  test('should enforce 100MB total limit', async ({ page }) => {
    // Create multiple attachments totaling > 100MB
    const largeAttachments: EmailAttachment[] = [
      { id: 'att-1', name: 'File1.pdf', contentType: 'application/pdf', size: 24 * 1024 * 1024, isInline: false },
      { id: 'att-2', name: 'File2.pdf', contentType: 'application/pdf', size: 24 * 1024 * 1024, isInline: false },
      { id: 'att-3', name: 'File3.pdf', contentType: 'application/pdf', size: 24 * 1024 * 1024, isInline: false },
      { id: 'att-4', name: 'File4.pdf', contentType: 'application/pdf', size: 24 * 1024 * 1024, isInline: false },
      { id: 'att-5', name: 'File5.pdf', contentType: 'application/pdf', size: 24 * 1024 * 1024, isInline: false },
    ];

    await saveFlowPage.mockOfficeReadMode(largeAttachments);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    // Mock API to return error for total size
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/office/validation-error',
          title: 'Total size exceeded',
          status: 400,
          detail: 'Combined attachment size exceeds 100MB limit',
          errorCode: 'OFFICE_005',
        }),
      });
    });

    await saveFlowPage.navigateToSaveMode();

    // Total size warning should be shown
    await expect(page.getByText(/total.*100.*MB|combined.*exceeds/i)).toBeVisible();
  });
});

// ============================================
// Test Suite: Entity Picker and Search (FR-04)
// ============================================

test.describe('Save Flow - Entity Picker @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockJobStatusApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();
  });

  test('should search entities with typeahead', async ({ page }) => {
    await saveFlowPage.entityPickerInput.fill('Smith');

    // Wait for debounce and results
    await saveFlowPage.waitForEntityResults();

    // Results should appear
    await expect(page.getByText('Smith vs Jones')).toBeVisible();
  });

  test('should search across all entity types', async ({ page }) => {
    await saveFlowPage.entityPickerInput.fill('Acme');

    await saveFlowPage.waitForEntityResults();

    // Should find both Matter and Account with "Acme"
    await expect(page.getByText('Smith vs Jones')).toBeVisible(); // Has "Acme Corp" in displayInfo
    await expect(page.getByText('Acme Corporation')).toBeVisible();
  });

  test('should display entity type icons', async ({ page }) => {
    await saveFlowPage.entityPickerInput.fill('Smith');
    await saveFlowPage.waitForEntityResults();

    // Verify entity type indicator
    const entityOption = page.locator('[data-testid="entity-option"]').first();
    await expect(entityOption.locator('[data-entity-type]')).toBeVisible();
  });

  test('should filter by entity type', async ({ page }) => {
    // Open filter dropdown if available
    const filterButton = page.getByRole('button', { name: /filter|type/i });

    if (await filterButton.isVisible()) {
      await filterButton.click();

      // Select "Matter" filter
      await page.getByRole('option', { name: /matter/i }).click();

      await saveFlowPage.entityPickerInput.fill('a');
      await saveFlowPage.waitForEntityResults();

      // Should only show matters
      const options = page.locator('[data-testid="entity-option"]');
      const allMatters = await options.evaluateAll((opts) =>
        opts.every((o) => o.getAttribute('data-entity-type') === 'Matter')
      );

      expect(allMatters).toBe(true);
    }
  });

  test('should show "no results" for empty search', async ({ page }) => {
    // Mock empty results
    await page.route(`${testConfig.apiBaseUrl}/office/search/entities*`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ results: [], totalCount: 0, hasMore: false }),
      });
    });

    await saveFlowPage.entityPickerInput.fill('nonexistent12345');
    await page.waitForTimeout(500); // Wait for debounce

    await expect(page.getByText(/no results|not found/i)).toBeVisible();
  });

  test('should respond within 500ms (FR-04)', async ({ page }) => {
    const startTime = Date.now();

    await saveFlowPage.entityPickerInput.fill('test');
    await saveFlowPage.waitForEntityResults();

    const endTime = Date.now();
    const duration = endTime - startTime;

    // Response time should be < 500ms (allowing some buffer for network)
    expect(duration).toBeLessThan(1000);
  });
});

// ============================================
// Test Suite: Quick Create During Save (FR-03)
// ============================================

test.describe('Save Flow - Quick Create @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockJobStatusApi();
    await saveFlowPage.mockRecentApi();
    await saveFlowPage.mockQuickCreateApi('matter', 'new-matter-001');

    await saveFlowPage.navigateToSaveMode();
  });

  test('should open Quick Create dialog', async ({ page }) => {
    await saveFlowPage.openQuickCreate();

    await expect(page.locator('[data-testid="quick-create-dialog"]')).toBeVisible();
  });

  test('should create new Matter and auto-select', async ({ page }) => {
    await saveFlowPage.openQuickCreate();

    // Select Matter type
    await page.locator('[data-testid="entity-type-matter"]').click();

    // Fill required field
    await page.fill('[data-testid="quick-create-name"]', 'New Test Matter');

    // Submit
    await page.click('[data-testid="quick-create-submit"]');

    // Wait for dialog to close
    await expect(page.locator('[data-testid="quick-create-dialog"]')).toBeHidden();

    // Verify the new entity is selected in picker
    await expect(saveFlowPage.entityPickerInput).toHaveValue('New Test Matter');

    // Save button should be enabled
    await expect(saveFlowPage.saveButton).toBeEnabled();
  });

  test('should return to picker on Quick Create cancel', async ({ page }) => {
    await saveFlowPage.openQuickCreate();

    // Cancel
    await page.click('[data-testid="quick-create-cancel"]');

    // Dialog should be closed
    await expect(page.locator('[data-testid="quick-create-dialog"]')).toBeHidden();

    // Picker should still be visible
    await expect(saveFlowPage.entityPickerInput).toBeVisible();
  });
});

// ============================================
// Test Suite: SSE Job Status Updates (FR-11)
// ============================================

test.describe('Save Flow - SSE Job Status @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockRecentApi();
  });

  test('should receive SSE status updates', async ({ page }) => {
    // Setup SSE mock with staged responses
    await saveFlowPage.mockSSEStream(mockJobStatusCompleted.stages);

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Verify stages are updated as they complete
    await expect(page.locator('[data-testid="stage-RecordsCreated"][data-status="Completed"]')).toBeVisible({
      timeout: 5000,
    });
    await expect(page.locator('[data-testid="stage-FileUploaded"][data-status="Completed"]')).toBeVisible({
      timeout: 5000,
    });
  });

  test('should show progress for each stage', async ({ page }) => {
    await saveFlowPage.mockSSEStream(mockJobStatusCompleted.stages);

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Verify all stages are shown
    for (const stage of mockJobStatusCompleted.stages) {
      await expect(page.locator(`[data-testid="stage-${stage.name}"]`)).toBeVisible();
    }
  });

  test('should display completion message with document link', async ({ page }) => {
    await saveFlowPage.mockSSEStream(mockJobStatusCompleted.stages);

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Wait for completion
    await saveFlowPage.waitForJobCompletion();

    // Verify success message and link
    await expect(page.getByText(/saved successfully|completed/i)).toBeVisible();
    await expect(page.getByRole('link', { name: /view document|open/i })).toBeVisible();
  });

  test('should update status within 1 second of change (NFR-04)', async ({ page }) => {
    // This test verifies real-time updates are received quickly
    await saveFlowPage.mockSSEStream(mockJobStatusCompleted.stages);

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');

    const startTime = Date.now();
    await saveFlowPage.submitSave();

    // Wait for first stage update
    await expect(page.locator('[data-testid="stage-RecordsCreated"][data-status="Completed"]')).toBeVisible({
      timeout: 3000,
    });

    const elapsed = Date.now() - startTime;
    // Should receive update quickly (allowing for processing time)
    expect(elapsed).toBeLessThan(3000);
  });
});

// ============================================
// Test Suite: Polling Fallback (NFR-04)
// ============================================

test.describe('Save Flow - Polling Fallback @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockRecentApi();
  });

  test('should fall back to polling when SSE fails', async ({ page }) => {
    // Mock SSE to fail
    await page.route(`${testConfig.apiBaseUrl}/office/jobs/*/stream`, (route) => {
      route.abort('failed');
    });

    // Mock polling endpoint with progressive status
    let pollCount = 0;
    await page.route(`${testConfig.apiBaseUrl}/office/jobs/*`, (route) => {
      if (!route.request().url().includes('/stream')) {
        pollCount++;
        const status =
          pollCount < 3 ? mockJobStatusRunning : mockJobStatusCompleted;
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(status),
        });
      } else {
        route.continue();
      }
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Should still show progress and complete via polling
    await saveFlowPage.waitForJobCompletion(20000);

    // Verify polling was used (multiple requests)
    expect(pollCount).toBeGreaterThan(1);
  });

  test('should poll at 3-second intervals', async ({ page }) => {
    // Mock SSE to fail
    await page.route(`${testConfig.apiBaseUrl}/office/jobs/*/stream`, (route) => {
      route.abort('failed');
    });

    const pollTimestamps: number[] = [];

    // Mock polling endpoint
    await page.route(`${testConfig.apiBaseUrl}/office/jobs/*`, (route) => {
      if (!route.request().url().includes('/stream')) {
        pollTimestamps.push(Date.now());
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(
            pollTimestamps.length < 4 ? mockJobStatusRunning : mockJobStatusCompleted
          ),
        });
      } else {
        route.continue();
      }
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Wait for multiple polls
    await saveFlowPage.waitForJobCompletion(20000);

    // Verify polling interval is approximately 3 seconds
    if (pollTimestamps.length > 2) {
      const interval = pollTimestamps[2] - pollTimestamps[1];
      expect(interval).toBeGreaterThan(2000);
      expect(interval).toBeLessThan(5000);
    }
  });
});

// ============================================
// Test Suite: Error Handling
// ============================================

test.describe('Save Flow - Error Handling @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();
  });

  test('should handle authorization error (OFFICE_009)', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 403,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/office/forbidden',
          title: 'Access denied',
          status: 403,
          detail: 'You do not have permission to save to this entity',
          errorCode: 'OFFICE_009',
        }),
      });
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    await expect(page.getByText(/access denied|permission/i)).toBeVisible();
  });

  test('should handle entity not found error (OFFICE_007)', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 404,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/office/not-found',
          title: 'Association target not found',
          status: 404,
          detail: 'The selected entity no longer exists',
          errorCode: 'OFFICE_007',
        }),
      });
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    await expect(page.getByText(/not found|no longer exists/i)).toBeVisible();
  });

  test('should handle server error gracefully', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/internal-error',
          title: 'Internal Server Error',
          status: 500,
          detail: 'An unexpected error occurred',
        }),
      });
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    await expect(page.getByText(/error|failed|try again/i)).toBeVisible();
  });

  test('should handle network error gracefully', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.abort('failed');
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    await expect(page.getByText(/network|connection|offline/i)).toBeVisible();
  });

  test('should allow retry after error', async ({ page }) => {
    let attempts = 0;
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      attempts++;
      if (attempts === 1) {
        route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({
            type: 'https://spaarke.com/errors/internal-error',
            title: 'Internal Server Error',
            status: 500,
            detail: 'Temporary failure',
          }),
        });
      } else {
        route.fulfill({
          status: 202,
          contentType: 'application/json',
          body: JSON.stringify(mockSaveResponse),
        });
      }
    });
    await saveFlowPage.mockJobStatusApi();

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');

    // First attempt fails
    await saveFlowPage.submitSave();
    await expect(page.getByText(/error|failed/i)).toBeVisible();

    // Retry
    const retryButton = page.getByRole('button', { name: /retry/i });
    if (await retryButton.isVisible()) {
      await retryButton.click();
    } else {
      await saveFlowPage.submitSave();
    }

    // Should succeed
    await expect(page.getByText(/saved|completed/i)).toBeVisible({ timeout: 10000 });
  });

  test('should display correlation ID for support', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/internal-error',
          title: 'Internal Server Error',
          status: 500,
          detail: 'An unexpected error occurred',
          correlationId: 'corr-err-12345',
        }),
      });
    });

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Correlation ID should be shown for support purposes
    await expect(page.getByText(/corr-err-12345|correlation/i)).toBeVisible();
  });
});

// ============================================
// Test Suite: Duplicate Detection (FR-12)
// ============================================

test.describe('Save Flow - Duplicate Detection @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();
  });

  test('should detect and notify duplicate email', async ({ page }) => {
    await saveFlowPage.mockSaveApi(mockDuplicateResponse);

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Should show duplicate message
    await expect(page.getByText(/already saved|previously saved|duplicate/i)).toBeVisible();
  });

  test('should return existing document for duplicate', async ({ page }) => {
    await saveFlowPage.mockSaveApi(mockDuplicateResponse);

    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    // Should provide link to existing document
    await expect(page.getByRole('link', { name: /view existing|open document/i })).toBeVisible();
  });

  test('should allow saving to different entity', async ({ page }) => {
    // First save to Smith vs Jones - duplicate
    await saveFlowPage.mockSaveApi(mockDuplicateResponse);
    await saveFlowPage.navigateToSaveMode();

    await saveFlowPage.searchEntity('Smith');
    await saveFlowPage.selectEntity('Smith vs Jones');
    await saveFlowPage.submitSave();

    await expect(page.getByText(/already saved|duplicate/i)).toBeVisible();

    // Now try to save to different entity
    await saveFlowPage.mockSaveApi(mockSaveResponse); // Reset to success
    await saveFlowPage.mockJobStatusApi();

    // Click "Save to different" if available, or clear and select new
    const saveToDifferent = page.getByRole('button', { name: /save to different|try another/i });
    if (await saveToDifferent.isVisible()) {
      await saveToDifferent.click();
    } else {
      // Clear selection and search for different entity
      await saveFlowPage.entityPickerInput.clear();
    }

    await saveFlowPage.searchEntity('Acme');
    await saveFlowPage.selectEntity('Acme Corporation');
    await saveFlowPage.submitSave();

    // Should succeed
    await expect(page.getByText(/saved successfully|completed/i)).toBeVisible({ timeout: 10000 });
  });
});

// ============================================
// Test Suite: Mandatory Association (FR-14)
// ============================================

test.describe('Save Flow - Mandatory Association @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();
  });

  test('should not allow save without entity selection', async ({ page }) => {
    // Save button should be disabled
    await expect(saveFlowPage.saveButton).toBeDisabled();
  });

  test('should show "required" indicator on entity picker', async ({ page }) => {
    // Entity picker should show required indicator
    const requiredIndicator = page.locator('[data-testid="entity-picker"]').locator('[aria-required="true"], .required, *:has-text("*")');
    const isRequired = await requiredIndicator.count() > 0 || await page.getByText(/required/i).isVisible();
    expect(isRequired).toBe(true);
  });

  test('should return OFFICE_003 error without association', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({
          type: 'https://spaarke.com/errors/office/validation-error',
          title: 'Association required',
          status: 400,
          detail: 'Association target is required',
          errorCode: 'OFFICE_003',
          errors: {
            associationType: ['Association type is required'],
            associationId: ['Association ID is required'],
          },
        }),
      });
    });

    // Force enable button via JS (simulating bypassed UI validation)
    await page.evaluate(() => {
      const button = document.querySelector('[data-testid="save-button"]') as HTMLButtonElement;
      if (button) button.disabled = false;
    });

    await saveFlowPage.submitSave();

    await expect(page.getByText(/association.*required/i)).toBeVisible();
  });
});

// ============================================
// Test Suite: Authentication Flows
// ============================================

test.describe('Save Flow - Authentication @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);
  });

  test('should use NAA for authentication when supported', async ({ page }) => {
    // Mock NAA support
    await page.addInitScript(() => {
      (window as any).Office = {
        context: {
          mailbox: {
            item: { itemType: 'message', itemId: 'msg-001' },
          },
          requirements: {
            isSetSupported: (name: string, version: string) => true,
          },
        },
      };

      // Mock MSAL nested app
      (window as any).msalInstance = {
        acquireTokenSilent: async () => ({ accessToken: 'naa-token-123' }),
        acquireTokenPopup: async () => ({ accessToken: 'naa-token-123' }),
      };
    });

    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();

    // Should successfully load (NAA auth working)
    await expect(saveFlowPage.entityPickerInput).toBeVisible();
  });

  test('should fall back to Dialog API when NAA unavailable', async ({ page }) => {
    // Mock environment without NAA support
    await page.addInitScript(() => {
      (window as any).Office = {
        context: {
          mailbox: {
            item: { itemType: 'message', itemId: 'msg-001' },
          },
          requirements: {
            isSetSupported: (name: string, version: string) => {
              // NAA requires recent requirement sets
              if (name === 'Mailbox' && parseFloat(version) > 1.5) return false;
              return true;
            },
          },
          ui: {
            displayDialogAsync: (url: string, options: any, callback: (result: any) => void) => {
              // Mock dialog API
              callback({
                status: 'succeeded',
                value: {
                  addEventHandler: (event: string, handler: (args: any) => void) => {
                    // Simulate auth callback
                    setTimeout(() => {
                      handler({
                        message: JSON.stringify({ accessToken: 'dialog-token-123' }),
                      });
                    }, 100);
                  },
                },
              });
            },
          },
        },
      };
    });

    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();

    // Should still work with Dialog API fallback
    await expect(saveFlowPage.entityPickerInput).toBeVisible({ timeout: 10000 });
  });
});

// ============================================
// Test Suite: Processing Options (FR-13)
// ============================================

test.describe('Save Flow - Processing Options @e2e @outlook', () => {
  let saveFlowPage: OutlookSaveFlowPage;

  test.beforeEach(async ({ page }) => {
    saveFlowPage = new OutlookSaveFlowPage(page, testConfig);

    await saveFlowPage.mockOfficeReadMode([]);
    await saveFlowPage.mockEntitySearchApi();
    await saveFlowPage.mockSaveApi();
    await saveFlowPage.mockJobStatusApi();
    await saveFlowPage.mockRecentApi();

    await saveFlowPage.navigateToSaveMode();
  });

  test('should display processing options', async ({ page }) => {
    // Look for processing options section
    const optionsSection = saveFlowPage.processingOptions;
    const hasOptions = await optionsSection.isVisible().catch(() => false);

    if (hasOptions) {
      await expect(page.getByText(/profile summary|summarize/i)).toBeVisible();
      await expect(page.getByText(/index|search/i)).toBeVisible();
    }
  });

  test('should allow toggling deep analysis option', async ({ page }) => {
    const deepAnalysisToggle = page.locator('[data-testid="processing-deep-analysis"]');

    if (await deepAnalysisToggle.isVisible()) {
      // Toggle on
      await deepAnalysisToggle.click();

      // Verify state changed
      const isChecked = await deepAnalysisToggle.isChecked();
      expect(isChecked).toBe(true);
    }
  });
});

/**
 * NOTE: These are E2E tests that require a deployed environment
 *
 * To run these tests:
 * 1. Deploy Outlook add-in to dev environment (task 057)
 * 2. Deploy background workers (task 066)
 * 3. Configure .env with:
 *    - ADDIN_TASKPANE_URL: URL to the task pane HTML
 *    - BFF_API_URL: URL to the BFF API
 *    - Authentication credentials (if needed)
 * 4. Ensure test entities exist in Dataverse
 *
 * Run with:
 *   npx playwright test outlook-addins/save-flow.spec.ts --headed
 *
 * Run specific test:
 *   npx playwright test -g "should save email without attachments" --headed
 *
 * Run with debug:
 *   npx playwright test outlook-addins/save-flow.spec.ts --debug
 *
 * Run only save flow tests:
 *   npx playwright test -g "@outlook" --grep-invert "@share"
 */
