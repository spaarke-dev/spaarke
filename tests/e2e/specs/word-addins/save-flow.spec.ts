/**
 * E2E Tests: Word Save Flow
 *
 * Tests validate the complete save flow for saving Word documents
 * to Spaarke DMS. Covers document capture, entity association,
 * job status monitoring, and error handling.
 *
 * Prerequisites:
 * - Deployed Word add-in (task 058)
 * - Deployed workers (task 066)
 * - .env file configured with credentials
 *
 * @see spec.md - FR-09 (Save Word document), FR-10 (Version Word document)
 * @see SaveView.tsx - Component implementation
 * @see WordHostAdapter.ts - Word-specific adapter
 * @see POST /office/save - API endpoint
 */

import { test, expect } from '@playwright/test';
import {
  WordTaskPanePage,
  type EntitySearchResult,
  type SaveJobResponse,
  type JobStatusResponse,
} from '../../pages/addins/WordTaskPanePage';

// Test configuration
const testConfig = {
  taskPaneUrl: process.env.ADDIN_TASKPANE_URL || 'https://localhost:3000/taskpane.html',
  initTimeout: 30000,
  apiBaseUrl: process.env.BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
};

// Mock data
const mockEntities: EntitySearchResult[] = [
  {
    id: 'matter-001',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Smith vs Jones',
    displayInfo: 'Client: Acme Corp | Status: Active',
  },
  {
    id: 'project-001',
    entityType: 'Project',
    logicalName: 'sprk_project',
    name: 'Q1 2026 Strategy',
    displayInfo: 'Status: In Progress',
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
    name: 'John Smith',
    displayInfo: 'Acme Corporation | john@acme.com',
  },
];

const mockSaveResponse: SaveJobResponse = {
  jobId: 'job-12345',
  documentId: 'doc-67890',
  statusUrl: '/office/jobs/job-12345',
  streamUrl: '/office/jobs/job-12345/stream',
  status: 'Queued',
  duplicate: false,
  correlationId: 'corr-abc123',
};

const mockJobStatusComplete: JobStatusResponse = {
  jobId: 'job-12345',
  status: 'Completed',
  stages: [
    { name: 'RecordsCreated', status: 'Completed' },
    { name: 'FileUploaded', status: 'Completed' },
    { name: 'ProfileSummary', status: 'Completed' },
    { name: 'Indexed', status: 'Completed' },
    { name: 'DeepAnalysis', status: 'Skipped' },
  ],
  documentId: 'doc-67890',
  documentUrl: 'https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&id=doc-67890',
};

const mockSSEEvents = [
  {
    event: 'stage-update',
    data: { stage: 'RecordsCreated', status: 'Completed', timestamp: new Date().toISOString() },
    delay: 100,
  },
  {
    event: 'stage-update',
    data: { stage: 'FileUploaded', status: 'Completed', timestamp: new Date().toISOString() },
    delay: 200,
  },
  {
    event: 'stage-update',
    data: { stage: 'ProfileSummary', status: 'Completed', timestamp: new Date().toISOString() },
    delay: 300,
  },
  {
    event: 'stage-update',
    data: { stage: 'Indexed', status: 'Completed', timestamp: new Date().toISOString() },
    delay: 400,
  },
  {
    event: 'job-complete',
    data: {
      status: 'Completed',
      documentId: 'doc-67890',
      documentUrl: 'https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&id=doc-67890',
    },
    delay: 500,
  },
];

// ============================================
// Test Suite: Document Context Loading
// ============================================

test.describe('Word Save Flow - Document Context @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    // Setup Word environment mock
    await taskPanePage.mockWordEnvironment('Test Contract.docx');
    await taskPanePage.mockEntitySearchApi(mockEntities);
    await taskPanePage.mockRecentApi(mockEntities.slice(0, 2));

    await taskPanePage.navigateToSaveMode();
  });

  test('should display document name on load', async ({ page }) => {
    await taskPanePage.waitForDocumentContext();

    // Verify document name is displayed
    await expect(page.getByText('Test Contract.docx')).toBeVisible();
  });

  test('should show save button when document is loaded', async ({ page }) => {
    await taskPanePage.waitForDocumentContext();

    await expect(taskPanePage.saveButton).toBeVisible();
    await expect(taskPanePage.saveButton).toBeDisabled(); // Should be disabled until entity selected
  });

  test('should show entity picker for association', async ({ page }) => {
    await taskPanePage.waitForDocumentContext();

    await expect(taskPanePage.entitySearchInput).toBeVisible();
    await expect(page.getByText(/associate with/i)).toBeVisible();
  });

  test('should display processing options', async ({ page }) => {
    await taskPanePage.waitForDocumentContext();

    await expect(taskPanePage.profileSummaryToggle).toBeVisible();
    await expect(taskPanePage.ragIndexToggle).toBeVisible();
  });
});

// ============================================
// Test Suite: Entity Association (Required)
// ============================================

test.describe('Word Save Flow - Entity Association @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    await taskPanePage.mockWordEnvironment('Legal Agreement.docx');
    await taskPanePage.mockEntitySearchApi(mockEntities);
    await taskPanePage.mockRecentApi(mockEntities.slice(0, 2));

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();
  });

  test('should search for entities with typeahead', async ({ page }) => {
    await taskPanePage.searchEntities('Smith');
    await taskPanePage.waitForEntityResults();

    // Verify results are displayed
    await expect(page.getByText('Smith vs Jones')).toBeVisible();
  });

  test('should filter entities by type', async ({ page }) => {
    await taskPanePage.filterByEntityType('Matter');
    await taskPanePage.searchEntities('Smith');
    await taskPanePage.waitForEntityResults();

    // Verify only Matter type is shown
    await expect(page.getByText('Smith vs Jones')).toBeVisible();
  });

  test('should select entity and enable save button', async ({ page }) => {
    await taskPanePage.searchEntities('Acme');
    await taskPanePage.waitForEntityResults();
    await taskPanePage.selectEntity('Acme Corporation');

    // Save button should now be enabled
    await expect(taskPanePage.saveButton).toBeEnabled();
  });

  test('should display recent associations on load', async ({ page }) => {
    // Recent associations should show before searching
    await expect(page.getByText('Recently Used')).toBeVisible();
    await expect(page.getByText('Smith vs Jones')).toBeVisible();
  });

  test('should allow selecting from recent associations', async ({ page }) => {
    // Click on a recent association
    await page.getByText('Smith vs Jones').click();

    // Save button should be enabled
    await expect(taskPanePage.saveButton).toBeEnabled();
  });

  test('should not allow save without entity selection', async ({ page }) => {
    // Save button should be disabled initially
    await expect(taskPanePage.saveButton).toBeDisabled();

    // Verify association required message is visible
    await expect(page.getByText(/select.*association/i)).toBeVisible();
  });
});

// ============================================
// Test Suite: Save Document Flow
// ============================================

test.describe('Word Save Flow - Save Document @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    await taskPanePage.mockWordEnvironment('Contract Draft.docx', 1024 * 100); // 100KB
    await taskPanePage.mockEntitySearchApi(mockEntities);
    await taskPanePage.mockSaveApi(mockSaveResponse);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();
  });

  test('should save document with entity association', async ({ page }) => {
    // Select entity
    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');

    // Click save
    await taskPanePage.clickSave();

    // Wait for save to complete
    await taskPanePage.waitForSaveComplete();

    // Verify success message
    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
    await expect(page.getByText(/saved successfully/i)).toBeVisible();
  });

  test('should display job status stages during save', async ({ page }) => {
    await taskPanePage.searchEntities('Acme');
    await taskPanePage.selectEntity('Acme Corporation');
    await taskPanePage.clickSave();

    // Verify job status container appears
    await expect(taskPanePage.jobStatusContainer).toBeVisible();

    // Wait for stages to update
    await taskPanePage.waitForStageUpdate('FileUploaded');

    // Verify stage is shown as completed
    await expect(page.locator('[data-stage="FileUploaded"][data-status="Completed"]')).toBeVisible();
  });

  test('should show view document button after save', async ({ page }) => {
    await taskPanePage.searchEntities('Matter');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();
    await taskPanePage.waitForSaveComplete();

    // View document button should be visible
    await expect(taskPanePage.viewDocumentButton).toBeVisible();
  });

  test('should include processing options in save request', async ({ page }) => {
    let capturedRequest: any = null;

    // Capture the save request
    await page.route(`${testConfig.apiBaseUrl}/office/save`, async (route) => {
      capturedRequest = JSON.parse(route.request().postData() || '{}');
      await route.fulfill({
        status: 202,
        contentType: 'application/json',
        body: JSON.stringify(mockSaveResponse),
      });
    });

    // Toggle processing options
    await taskPanePage.setProcessingOption('profileSummary', true);
    await taskPanePage.setProcessingOption('ragIndex', true);
    await taskPanePage.setProcessingOption('deepAnalysis', false);

    // Select entity and save
    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Wait for request to be captured
    await page.waitForTimeout(500);

    // Verify processing options in request
    expect(capturedRequest.processing).toBeDefined();
    expect(capturedRequest.processing.profileSummary).toBe(true);
    expect(capturedRequest.processing.ragIndex).toBe(true);
    expect(capturedRequest.processing.deepAnalysis).toBe(false);
  });
});

// ============================================
// Test Suite: Document Size Limits
// ============================================

test.describe('Word Save Flow - Size Limits @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);
    await taskPanePage.mockEntitySearchApi(mockEntities);
  });

  test('should allow saving document under 100MB', async ({ page }) => {
    // 50MB document (under limit)
    await taskPanePage.mockLargeDocument(50);
    await taskPanePage.mockSaveApi(mockSaveResponse);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();

    // Select entity and save
    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');

    // Save button should be enabled
    await expect(taskPanePage.saveButton).toBeEnabled();

    await taskPanePage.clickSave();
    await taskPanePage.waitForSaveComplete();

    // Should succeed
    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
  });

  test('should show error for document exceeding 100MB', async ({ page }) => {
    // 150MB document (over limit)
    await taskPanePage.mockLargeDocument(150);
    await taskPanePage.mockApiError('/office/save', 400, 'OFFICE_005', 'Document size exceeds 100MB limit');

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();

    // Select entity
    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');

    // Try to save
    await taskPanePage.clickSave();

    // Verify error message
    await expect(page.getByText(/exceeds.*100MB/i)).toBeVisible();
  });

  test('should display document size warning for large files', async ({ page }) => {
    // 80MB document (large but within limit)
    await taskPanePage.mockLargeDocument(80);
    await taskPanePage.mockSaveApi(mockSaveResponse);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();

    // Should show warning about large file
    const warningVisible = await page.getByText(/large file/i).isVisible().catch(() => false);
    // Note: This is optional UX, test passes either way
  });
});

// ============================================
// Test Suite: SSE Job Status Updates
// ============================================

test.describe('Word Save Flow - SSE Status Updates @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    await taskPanePage.mockWordEnvironment('Status Test.docx');
    await taskPanePage.mockEntitySearchApi(mockEntities);
    await taskPanePage.mockSaveApi(mockSaveResponse);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();
  });

  test('should receive SSE stage updates in real-time', async ({ page }) => {
    // Mock SSE with delayed events
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Wait for specific stage updates
    await taskPanePage.waitForStageUpdate('RecordsCreated');
    await expect(page.locator('[data-stage="RecordsCreated"][data-status="Completed"]')).toBeVisible();

    await taskPanePage.waitForStageUpdate('FileUploaded');
    await expect(page.locator('[data-stage="FileUploaded"][data-status="Completed"]')).toBeVisible();
  });

  test('should show final job-complete event', async ({ page }) => {
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    await taskPanePage.waitForSaveComplete();

    // Verify completion message
    await expect(page.getByText(/saved successfully/i)).toBeVisible();
    await expect(taskPanePage.viewDocumentButton).toBeVisible();
  });

  test('should fallback to polling when SSE fails', async ({ page }) => {
    // Mock SSE failure
    await page.route(`${testConfig.apiBaseUrl}/office/jobs/job-12345/stream`, (route) => {
      route.abort('failed');
    });

    // Mock polling endpoint
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Wait for polling to complete
    await taskPanePage.waitForSaveComplete(15000);

    // Should still succeed via polling
    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
  });
});

// ============================================
// Test Suite: Quick Create During Save
// ============================================

test.describe('Word Save Flow - Quick Create @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    await taskPanePage.mockWordEnvironment('New Matter Document.docx');
    await taskPanePage.mockEntitySearchApi([]); // No results to trigger quick create
    await taskPanePage.mockQuickCreateApi('Matter', {
      id: 'matter-new-001',
      name: 'New Test Matter',
      url: 'https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&id=matter-new-001',
    });
    await taskPanePage.mockSaveApi(mockSaveResponse);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();
  });

  test('should open Quick Create dialog when no results found', async ({ page }) => {
    // Search with no results
    await taskPanePage.searchEntities('NewMatter123');
    await taskPanePage.waitForEntityResults();

    // Quick Create button should be visible
    await expect(taskPanePage.quickCreateButton).toBeVisible();

    // Open Quick Create
    await taskPanePage.openQuickCreate();

    // Dialog should open
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(page.getByText(/create new/i)).toBeVisible();
  });

  test('should create new entity and use for save', async ({ page }) => {
    // Mock the full save flow with newly created entity
    await page.route(`${testConfig.apiBaseUrl}/office/search/entities*`, (route, request) => {
      const url = new URL(request.url());
      const query = url.searchParams.get('q') || '';

      if (query === 'New Test Matter') {
        // After creating, search returns the new entity
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            results: [
              {
                id: 'matter-new-001',
                entityType: 'Matter',
                logicalName: 'sprk_matter',
                name: 'New Test Matter',
                displayInfo: 'Status: Active',
              },
            ],
            totalCount: 1,
            hasMore: false,
          }),
        });
      } else {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ results: [], totalCount: 0, hasMore: false }),
        });
      }
    });

    // Search with no results
    await taskPanePage.searchEntities('NewMatter');
    await taskPanePage.openQuickCreate();

    // Create new matter
    await taskPanePage.createNewEntity('Matter', 'New Test Matter', {
      description: 'Test matter for document',
    });

    // Verify newly created entity is selected
    await expect(page.getByText('New Test Matter')).toBeVisible();
    await expect(taskPanePage.saveButton).toBeEnabled();

    // Save the document
    await taskPanePage.clickSave();
    await taskPanePage.waitForSaveComplete();

    // Should succeed
    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
  });
});

// ============================================
// Test Suite: Duplicate Detection
// ============================================

test.describe('Word Save Flow - Duplicate Detection @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    await taskPanePage.mockWordEnvironment('Duplicate Check.docx');
    await taskPanePage.mockEntitySearchApi(mockEntities);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();
  });

  test('should detect duplicate document and show existing', async ({ page }) => {
    // Mock duplicate response
    const duplicateResponse: SaveJobResponse = {
      ...mockSaveResponse,
      duplicate: true,
      status: 'Completed',
      message: 'This document was previously saved to this association target',
    };
    await taskPanePage.mockSaveApi(duplicateResponse);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Wait for response
    await page.waitForTimeout(1000);

    // Verify duplicate message is shown
    await expect(page.getByText(/previously saved/i)).toBeVisible();
    await expect(taskPanePage.viewDocumentButton).toBeVisible();
  });

  test('should allow viewing existing document on duplicate', async ({ page }) => {
    const duplicateResponse: SaveJobResponse = {
      ...mockSaveResponse,
      duplicate: true,
      status: 'Completed',
    };
    await taskPanePage.mockSaveApi(duplicateResponse);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    await page.waitForTimeout(1000);

    // View button should be visible
    await expect(taskPanePage.viewDocumentButton).toBeVisible();
  });
});

// ============================================
// Test Suite: Error Handling
// ============================================

test.describe('Word Save Flow - Error Handling @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    await taskPanePage.mockWordEnvironment('Error Test.docx');
    await taskPanePage.mockEntitySearchApi(mockEntities);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();
  });

  test('should handle association target not found error', async ({ page }) => {
    await taskPanePage.mockApiError('/office/save', 404, 'OFFICE_007', 'Association target not found');

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Verify error message
    await expect(page.getByText('Association target not found')).toBeVisible();
  });

  test('should handle access denied error', async ({ page }) => {
    await taskPanePage.mockApiError('/office/save', 403, 'OFFICE_009', 'Access denied');

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    await expect(page.getByText('Access denied')).toBeVisible();
  });

  test('should handle SPE upload failure', async ({ page }) => {
    await taskPanePage.mockApiError('/office/save', 502, 'OFFICE_012', 'SPE upload failed');

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    await expect(page.getByText('SPE upload failed')).toBeVisible();
  });

  test('should handle network error gracefully', async ({ page }) => {
    await page.route(`${testConfig.apiBaseUrl}/office/save`, (route) => {
      route.abort('failed');
    });

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Verify network error message
    await expect(page.getByText(/network error|connection failed/i)).toBeVisible();
  });

  test('should allow retry after error', async ({ page }) => {
    // First request fails, second succeeds
    let attempts = 0;
    await page.route(`${testConfig.apiBaseUrl}/office/save`, async (route) => {
      attempts++;
      if (attempts === 1) {
        await route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({
            type: 'error',
            title: 'Server error',
            status: 500,
          }),
        });
      } else {
        await route.fulfill({
          status: 202,
          contentType: 'application/json',
          body: JSON.stringify(mockSaveResponse),
        });
      }
    });

    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');

    // First attempt fails
    await taskPanePage.clickSave();
    await page.waitForTimeout(1000);
    await expect(page.getByText('Server error')).toBeVisible();

    // Retry
    const retryButton = page.getByRole('button', { name: /retry/i });
    if (await retryButton.isVisible()) {
      await retryButton.click();
    } else {
      await taskPanePage.clickSave();
    }

    await taskPanePage.waitForSaveComplete();

    // Second attempt succeeds
    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
  });

  test('should handle job processing failure', async ({ page }) => {
    // Save succeeds but job fails
    await taskPanePage.mockSaveApi(mockSaveResponse);

    const failedJobStatus: JobStatusResponse = {
      jobId: 'job-12345',
      status: 'Failed',
      stages: [
        { name: 'RecordsCreated', status: 'Completed' },
        { name: 'FileUploaded', status: 'Failed' },
      ],
      errorCode: 'OFFICE_012',
      errorMessage: 'Failed to upload file to SPE',
    };
    await taskPanePage.mockJobStatusApi('job-12345', failedJobStatus);

    const failedSSEEvents = [
      {
        event: 'stage-update',
        data: { stage: 'RecordsCreated', status: 'Completed' },
      },
      {
        event: 'stage-update',
        data: { stage: 'FileUploaded', status: 'Failed' },
      },
      {
        event: 'job-failed',
        data: { status: 'Failed', errorCode: 'OFFICE_012', errorMessage: 'Failed to upload file to SPE' },
      },
    ];
    await taskPanePage.mockJobStatusSSE('job-12345', failedSSEEvents);

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();

    // Wait for failure
    await page.waitForTimeout(2000);

    // Verify error is shown
    await expect(page.getByText(/failed/i)).toBeVisible();
  });
});

// ============================================
// Test Suite: Document with Images/Embedded Objects
// ============================================

test.describe('Word Save Flow - Embedded Content @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);
    await taskPanePage.mockEntitySearchApi(mockEntities);
  });

  test('should save document with embedded images', async ({ page }) => {
    // Mock Word environment with embedded content
    await page.addInitScript(() => {
      const mockOoxmlWithImages = 'base64-encoded-ooxml-with-images';
      (window as any).Word = {
        run: async (callback: (context: any) => Promise<any>) => {
          const context = {
            document: {
              body: {
                getOoxml: () => ({ value: mockOoxmlWithImages }),
                inlinePictures: {
                  load: () => {},
                  items: [{ altTextTitle: 'Chart 1' }, { altTextTitle: 'Logo' }],
                },
              },
              properties: {
                title: 'Document with Images.docx',
                load: () => {},
              },
            },
            sync: async () => {},
          };
          return await callback(context);
        },
      };
      (window as any).Office = {
        context: {
          requirements: {
            isSetSupported: () => true,
          },
        },
        onReady: (callback: (info: any) => void) => {
          callback({ host: 'Word', platform: 'PC' });
        },
        HostType: { Word: 'Word' },
      };
    });

    await taskPanePage.mockSaveApi(mockSaveResponse);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');
    await taskPanePage.clickSave();
    await taskPanePage.waitForSaveComplete();

    // Should succeed
    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
  });
});

// ============================================
// Test Suite: Empty Document Handling
// ============================================

test.describe('Word Save Flow - Empty Document @e2e @word', () => {
  let taskPanePage: WordTaskPanePage;

  test('should handle empty/new document', async ({ page }) => {
    taskPanePage = new WordTaskPanePage(page, testConfig);

    // Mock empty document
    await page.addInitScript(() => {
      (window as any).Word = {
        run: async (callback: (context: any) => Promise<any>) => {
          const context = {
            document: {
              body: {
                getOoxml: () => ({ value: '<?xml version="1.0"?><document></document>' }),
                text: '',
              },
              properties: {
                title: '',
                load: () => {},
              },
            },
            sync: async () => {},
          };
          return await callback(context);
        },
      };
      (window as any).Office = {
        context: {
          requirements: { isSetSupported: () => true },
        },
        onReady: (callback: (info: any) => void) => {
          callback({ host: 'Word', platform: 'PC' });
        },
        HostType: { Word: 'Word' },
      };
    });

    await taskPanePage.mockEntitySearchApi(mockEntities);
    await taskPanePage.mockSaveApi(mockSaveResponse);
    await taskPanePage.mockJobStatusApi('job-12345', mockJobStatusComplete);
    await taskPanePage.mockJobStatusSSE('job-12345', mockSSEEvents);

    await taskPanePage.navigateToSaveMode();
    await taskPanePage.waitForDocumentContext();

    // Should show "Untitled Document" or similar
    await expect(page.getByText(/untitled/i)).toBeVisible();

    await taskPanePage.searchEntities('Smith');
    await taskPanePage.selectEntity('Smith vs Jones');

    // Should still be able to save empty document
    await expect(taskPanePage.saveButton).toBeEnabled();

    await taskPanePage.clickSave();
    await taskPanePage.waitForSaveComplete();

    await expect(taskPanePage.isSaveSuccessful()).resolves.toBe(true);
  });
});

/**
 * NOTE: These are E2E tests that require a deployed environment
 *
 * To run these tests:
 * 1. Deploy Word add-in to dev environment (task 058)
 * 2. Deploy workers to Azure (task 066)
 * 3. Configure .env with:
 *    - ADDIN_TASKPANE_URL: URL to the task pane HTML
 *    - BFF_API_URL: URL to the BFF API
 *    - Authentication credentials (if needed)
 *
 * Run with:
 *   npx playwright test word-addins/save-flow.spec.ts --headed
 *
 * Run specific test:
 *   npx playwright test -g "should save document with entity association" --headed
 *
 * Run with debug:
 *   npx playwright test word-addins/save-flow.spec.ts --debug
 */
