/**
 * E2E Tests: Outlook Share Flow
 *
 * Tests validate the complete share flow for sharing Spaarke documents
 * via Outlook compose mode. Covers both share-as-link and share-as-attachment
 * scenarios per spec.md requirements.
 *
 * Prerequisites:
 * - Deployed Outlook add-in (task 057)
 * - Access to test documents in Spaarke
 * - .env file configured with credentials
 *
 * @see spec.md - FR-06 (Share via link), FR-07 (Share via attachment)
 * @see ShareView.tsx - Component implementation
 * @see POST /office/share/links - API endpoint
 * @see POST /office/share/attach - API endpoint
 */

import { test, expect } from '@playwright/test';
import {
  OutlookTaskPanePage,
  type DocumentSearchResult,
  type ShareLinkResponse,
  type AttachmentResponse,
} from '../../pages/addins/OutlookTaskPanePage';

// Test configuration
const testConfig = {
  taskPaneUrl: process.env.ADDIN_TASKPANE_URL || 'https://localhost:3000/taskpane.html',
  initTimeout: 30000,
  apiBaseUrl: process.env.BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
};

// Mock data
const mockDocuments: DocumentSearchResult[] = [
  {
    id: 'doc-001',
    name: 'Client Agreement Q1 2026.docx',
    path: '/Legal/Agreements/',
    modifiedDate: '2026-01-15T10:00:00Z',
  },
  {
    id: 'doc-002',
    name: 'Project Proposal - Alpha.pdf',
    path: '/Projects/Alpha/',
    modifiedDate: '2026-01-14T15:30:00Z',
  },
  {
    id: 'doc-003',
    name: 'Financial Report December.xlsx',
    path: '/Finance/Reports/',
    modifiedDate: '2026-01-10T09:00:00Z',
  },
  {
    id: 'doc-004',
    name: 'Confidential Memo.docx',
    path: '/Internal/Memos/',
    modifiedDate: '2026-01-08T14:00:00Z',
  },
];

const mockShareLinkResponse: ShareLinkResponse = {
  links: [
    {
      documentId: 'doc-001',
      url: 'https://share.spaarke.com/d/abc123xyz',
      title: 'Client Agreement Q1 2026.docx',
    },
  ],
};

const mockMultipleLinksResponse: ShareLinkResponse = {
  links: [
    {
      documentId: 'doc-001',
      url: 'https://share.spaarke.com/d/abc123xyz',
      title: 'Client Agreement Q1 2026.docx',
    },
    {
      documentId: 'doc-002',
      url: 'https://share.spaarke.com/d/def456uvw',
      title: 'Project Proposal - Alpha.pdf',
    },
  ],
};

const mockAttachmentResponse: AttachmentResponse = {
  attachments: [
    {
      documentId: 'doc-001',
      filename: 'Client Agreement Q1 2026.docx',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      size: 245678,
      downloadUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net/office/share/attach/token123',
      urlExpiry: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    },
  ],
};

const mockMultipleAttachmentsResponse: AttachmentResponse = {
  attachments: [
    {
      documentId: 'doc-001',
      filename: 'Client Agreement Q1 2026.docx',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      size: 245678,
      downloadUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net/office/share/attach/token123',
      urlExpiry: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    },
    {
      documentId: 'doc-002',
      filename: 'Project Proposal - Alpha.pdf',
      contentType: 'application/pdf',
      size: 1024000,
      downloadUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net/office/share/attach/token456',
      urlExpiry: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    },
  ],
};

test.describe('Share Flow - Document Search @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    // Setup mocks
    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);
    await taskPanePage.mockShareLinksApi(mockShareLinkResponse);

    // Navigate to share mode
    await taskPanePage.navigateToShareMode();
  });

  test('should display search input and button on load', async ({ page }) => {
    await expect(taskPanePage.searchInput).toBeVisible();
    await expect(taskPanePage.searchButton).toBeVisible();
  });

  test('should search for documents with typeahead', async ({ page }) => {
    // Enter search query
    await taskPanePage.searchInput.fill('Agreement');

    // Verify search button is enabled
    await expect(taskPanePage.searchButton).toBeEnabled();

    // Click search
    await taskPanePage.searchButton.click();

    // Wait for results
    await taskPanePage.waitForSearchResults();

    // Verify results are displayed
    await expect(page.getByText('Client Agreement Q1 2026.docx')).toBeVisible();
  });

  test('should search on Enter key press', async ({ page }) => {
    // Enter search query and press Enter
    await taskPanePage.searchInput.fill('Project');
    await taskPanePage.searchInput.press('Enter');

    // Wait for results
    await taskPanePage.waitForSearchResults();

    // Verify results
    await expect(page.getByText('Project Proposal - Alpha.pdf')).toBeVisible();
  });

  test('should display document paths in search results', async ({ page }) => {
    await taskPanePage.searchDocuments('Report');
    await taskPanePage.waitForSearchResults();

    // Verify path is displayed
    await expect(page.getByText('/Finance/Reports/')).toBeVisible();
  });

  test('should show loading state during search', async ({ page }) => {
    // Add delay to search API
    await page.route(`${testConfig.apiBaseUrl}/office/search/documents*`, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1000));
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ results: mockDocuments, totalCount: mockDocuments.length, hasMore: false }),
      });
    });

    // Start search
    await taskPanePage.searchInput.fill('Agreement');
    await taskPanePage.searchButton.click();

    // Verify loading indicator appears
    await expect(taskPanePage.loadingSpinner).toBeVisible();

    // Wait for results
    await taskPanePage.waitForSearchResults();

    // Loading should be gone
    await expect(taskPanePage.loadingSpinner).not.toBeVisible();
  });

  test('should handle empty search results', async ({ page }) => {
    // Mock empty results
    await taskPanePage.mockSearchApi([]);

    await taskPanePage.searchDocuments('NonexistentDocument');
    await taskPanePage.waitForSearchResults();

    // Verify no results message or empty state
    await expect(page.getByText('No documents found')).toBeVisible();
  });
});

test.describe('Share Flow - Share as Link @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);
    await taskPanePage.mockShareLinksApi(mockShareLinkResponse);

    await taskPanePage.navigateToShareMode();
  });

  test('should select single document from search results', async ({ page }) => {
    // Search and select
    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    // Verify link generation section appears
    await expect(page.getByText('Generate Sharing Link')).toBeVisible();
    await expect(page.getByText('Selected: Client Agreement Q1 2026.docx')).toBeVisible();
  });

  test('should generate sharing link with view permission', async ({ page }) => {
    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    // Set view permission (default)
    await taskPanePage.setPermission('view');

    // Generate link
    const link = await taskPanePage.generateLink();

    // Verify link is generated
    expect(link).toContain('https://share.spaarke.com/');
  });

  test('should generate sharing link with edit permission', async ({ page }) => {
    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    // Set edit permission
    await taskPanePage.setPermission('edit');

    // Generate link
    const link = await taskPanePage.generateLink();

    // Verify link is generated
    expect(link).toBeTruthy();
  });

  test('should copy generated link to clipboard', async ({ page, context }) => {
    // Grant clipboard permissions
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);

    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');
    await taskPanePage.generateLink();

    // Copy link
    await taskPanePage.copyLink();

    // Verify "Copied!" feedback
    await expect(page.getByText('Copied!')).toBeVisible();
  });

  test('should insert link into email body', async ({ page }) => {
    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');
    const link = await taskPanePage.generateLink();

    // Insert link
    await taskPanePage.insertLink();

    // Verify link was inserted (via mock)
    const linkInserted = await taskPanePage.verifyLinkInEmailBody(link);
    expect(linkInserted).toBe(true);
  });

  test('should share multiple documents as links', async ({ page }) => {
    // Mock multi-link response
    await taskPanePage.mockShareLinksApi(mockMultipleLinksResponse);

    await taskPanePage.searchDocuments('doc');

    // Select multiple documents
    await taskPanePage.selectMultipleDocuments([
      'Client Agreement Q1 2026.docx',
      'Project Proposal - Alpha.pdf',
    ]);

    // Generate links
    await taskPanePage.generateLinkButton.click();
    await taskPanePage.waitForOperationComplete();

    // Verify multiple links inserted
    await expect(page.getByText('2 links generated')).toBeVisible();
  });
});

test.describe('Share Flow - Share as Attachment @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);
    await taskPanePage.mockShareAttachApi(mockAttachmentResponse);

    await taskPanePage.navigateToShareMode();
  });

  test('should share single document as attachment', async ({ page }) => {
    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    // Click share as attachment
    await taskPanePage.shareAsAttachment();

    // Verify success
    await expect(page.getByText('Attachment added')).toBeVisible();
  });

  test('should share multiple documents as attachments', async ({ page }) => {
    // Mock multiple attachments response
    await taskPanePage.mockShareAttachApi(mockMultipleAttachmentsResponse);

    await taskPanePage.searchDocuments('doc');
    await taskPanePage.selectMultipleDocuments([
      'Client Agreement Q1 2026.docx',
      'Project Proposal - Alpha.pdf',
    ]);

    await taskPanePage.shareAsAttachment();

    // Verify success message for multiple attachments
    await expect(page.getByText('2 attachments added')).toBeVisible();
  });

  test('should show file size in attachment confirmation', async ({ page }) => {
    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');
    await taskPanePage.shareAsAttachment();

    // Verify file size shown (240 KB)
    await expect(page.getByText(/240\s*KB/i)).toBeVisible();
  });
});

test.describe('Share Flow - Search Filters @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);

    await taskPanePage.navigateToShareMode();
  });

  test('should filter by entity type', async ({ page }) => {
    // Mock filtered results
    const legalDocs = mockDocuments.filter((d) => d.path.includes('/Legal/'));
    await taskPanePage.mockSearchApi(legalDocs);

    // Open filter dropdown and select entity type
    const filterButton = page.getByRole('button', { name: /filter/i });
    if (await filterButton.isVisible()) {
      await filterButton.click();
      await page.getByRole('option', { name: /legal/i }).click();
    }

    await taskPanePage.searchDocuments('Agreement');

    // Verify only legal documents shown
    await expect(page.getByText('/Legal/Agreements/')).toBeVisible();
    await expect(page.getByText('/Finance/Reports/')).not.toBeVisible();
  });

  test('should filter by date range', async ({ page }) => {
    // Mock filtered results (last 7 days)
    const recentDocs = mockDocuments.filter((d) => {
      const date = new Date(d.modifiedDate!);
      const weekAgo = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000);
      return date > weekAgo;
    });
    await taskPanePage.mockSearchApi(recentDocs);

    // Open date filter
    const dateFilter = page.getByRole('button', { name: /date/i });
    if (await dateFilter.isVisible()) {
      await dateFilter.click();
      await page.getByRole('option', { name: /last 7 days/i }).click();
    }

    await taskPanePage.searchDocuments('doc');

    // Verify only recent documents shown
    const resultCount = await taskPanePage.getSearchResultCount();
    expect(resultCount).toBeLessThanOrEqual(recentDocs.length);
  });
});

test.describe('Share Flow - Recent Documents @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    await taskPanePage.mockOfficeComposeMode();

    // Mock recent documents endpoint
    await page.route(`${testConfig.apiBaseUrl}/office/recent*`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          recentDocuments: mockDocuments.slice(0, 3),
          favorites: [],
        }),
      });
    });

    await taskPanePage.navigateToShareMode();
  });

  test('should display recent documents on load', async ({ page }) => {
    // Recent documents should show before searching
    await expect(page.getByText('Recent Documents')).toBeVisible();
    await expect(page.getByText('Client Agreement Q1 2026.docx')).toBeVisible();
  });

  test('should allow selecting from recent documents', async ({ page }) => {
    await taskPanePage.mockShareLinksApi(mockShareLinkResponse);

    // Click on a recent document
    await page.getByText('Client Agreement Q1 2026.docx').click();

    // Should show link generation
    await expect(page.getByText('Generate Sharing Link')).toBeVisible();
  });
});

test.describe('Share Flow - Error Handling @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);

    await taskPanePage.navigateToShareMode();
  });

  test('should handle unauthorized share error', async ({ page }) => {
    // Mock 403 error for share
    await taskPanePage.mockApiError('/office/share/links', 403, 'OFFICE_009', 'Access denied');

    await taskPanePage.searchDocuments('Confidential');
    await taskPanePage.selectDocument('Confidential Memo.docx');

    // Try to generate link
    await taskPanePage.generateLinkButton.click();
    await taskPanePage.waitForOperationComplete();

    // Verify error message
    await expect(page.getByText('Access denied')).toBeVisible();
  });

  test('should handle document not found error', async ({ page }) => {
    // Mock 404 error
    await taskPanePage.mockApiError('/office/share/links', 404, 'OFFICE_007', 'Document not found');

    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    await taskPanePage.generateLinkButton.click();
    await taskPanePage.waitForOperationComplete();

    await expect(page.getByText('Document not found')).toBeVisible();
  });

  test('should handle network error gracefully', async ({ page }) => {
    // Mock network failure
    await page.route(`${testConfig.apiBaseUrl}/office/share/links`, (route) => {
      route.abort('failed');
    });

    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    await taskPanePage.generateLinkButton.click();

    // Verify error is shown
    await expect(page.getByText(/network error|connection failed/i)).toBeVisible();
  });

  test('should handle attachment too large error', async ({ page }) => {
    // Mock 400 error for large attachment
    await taskPanePage.mockApiError('/office/share/attach', 400, 'OFFICE_004', 'Attachment too large (max 25MB)');

    await taskPanePage.searchDocuments('Report');
    await taskPanePage.selectDocument('Financial Report December.xlsx');

    await taskPanePage.shareAsAttachment();

    await expect(page.getByText('Attachment too large')).toBeVisible();
  });

  test('should allow retry after error', async ({ page }) => {
    // First request fails
    let attempts = 0;
    await page.route(`${testConfig.apiBaseUrl}/office/share/links`, (route) => {
      attempts++;
      if (attempts === 1) {
        route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({ type: 'error', title: 'Server error', status: 500 }),
        });
      } else {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockShareLinkResponse),
        });
      }
    });

    await taskPanePage.searchDocuments('Agreement');
    await taskPanePage.selectDocument('Client Agreement Q1 2026.docx');

    // First attempt fails
    await taskPanePage.generateLinkButton.click();
    await taskPanePage.waitForOperationComplete();
    await expect(page.getByText('Server error')).toBeVisible();

    // Retry button
    const retryButton = page.getByRole('button', { name: /retry/i });
    if (await retryButton.isVisible()) {
      await retryButton.click();
    } else {
      // Or click generate again
      await taskPanePage.generateLinkButton.click();
    }

    await taskPanePage.waitForOperationComplete();

    // Second attempt succeeds
    await expect(taskPanePage.generatedLinkInput).toBeVisible();
  });
});

test.describe('Share Flow - Partial Success @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);

    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);

    await taskPanePage.navigateToShareMode();
  });

  test('should handle partial success for multiple documents', async ({ page }) => {
    // Mock partial success - one link succeeds, one fails
    await page.route(`${testConfig.apiBaseUrl}/office/share/links`, (route) => {
      route.fulfill({
        status: 207, // Multi-status
        contentType: 'application/json',
        body: JSON.stringify({
          links: [
            {
              documentId: 'doc-001',
              url: 'https://share.spaarke.com/d/abc123xyz',
              title: 'Client Agreement Q1 2026.docx',
            },
          ],
          errors: [
            {
              documentId: 'doc-002',
              errorCode: 'OFFICE_009',
              message: 'Access denied to this document',
            },
          ],
        }),
      });
    });

    await taskPanePage.searchDocuments('doc');
    await taskPanePage.selectMultipleDocuments([
      'Client Agreement Q1 2026.docx',
      'Project Proposal - Alpha.pdf',
    ]);

    await taskPanePage.generateLinkButton.click();
    await taskPanePage.waitForOperationComplete();

    // Verify partial success message
    await expect(page.getByText(/1 of 2 links generated/i)).toBeVisible();
    await expect(page.getByText(/Access denied/i)).toBeVisible();
  });

  test('should show which documents failed in partial success', async ({ page }) => {
    // Mock partial attachment success
    await page.route(`${testConfig.apiBaseUrl}/office/share/attach`, (route) => {
      route.fulfill({
        status: 207,
        contentType: 'application/json',
        body: JSON.stringify({
          attachments: [mockAttachmentResponse.attachments[0]],
          errors: [
            {
              documentId: 'doc-002',
              filename: 'Project Proposal - Alpha.pdf',
              errorCode: 'OFFICE_004',
              message: 'File too large',
            },
          ],
        }),
      });
    });

    await taskPanePage.searchDocuments('doc');
    await taskPanePage.selectMultipleDocuments([
      'Client Agreement Q1 2026.docx',
      'Project Proposal - Alpha.pdf',
    ]);

    await taskPanePage.shareAsAttachment();

    // Verify failed document is identified
    await expect(page.getByText('Project Proposal - Alpha.pdf')).toBeVisible();
    await expect(page.getByText('File too large')).toBeVisible();
  });
});

test.describe('Share Flow - Compose Mode Detection @e2e @outlook', () => {
  let taskPanePage: OutlookTaskPanePage;

  test.beforeEach(async ({ page }) => {
    taskPanePage = new OutlookTaskPanePage(page, testConfig);
  });

  test('should detect compose mode and show share UI', async ({ page }) => {
    await taskPanePage.mockOfficeComposeMode();
    await taskPanePage.mockSearchApi(mockDocuments);

    await taskPanePage.navigateToShareMode();

    // Verify share UI is shown in compose mode
    await expect(page.getByText('Share from Spaarke')).toBeVisible();
    await expect(taskPanePage.searchInput).toBeVisible();
  });

  test('should hide share UI in read mode', async ({ page }) => {
    // Mock read mode instead of compose mode
    await page.addInitScript(() => {
      (window as any).Office = {
        context: {
          mailbox: {
            item: {
              itemType: 'message',
              displayReplyForm: () => {}, // Read mode has different methods
              body: {
                getAsync: (coercionType: any, callback: (result: any) => void) => {
                  callback({ status: 'succeeded', value: 'Email body content' });
                },
              },
              // No setAsync = read mode
            },
          },
          requirements: {
            isSetSupported: () => true,
          },
        },
      };
    });

    await page.goto(testConfig.taskPaneUrl);
    await taskPanePage.waitForTaskPaneLoad();

    // Share option should not be available or should show save mode
    const shareButton = page.getByText('Share from Spaarke');
    await expect(shareButton).not.toBeVisible();
  });

  test('should show appropriate message when not in compose mode', async ({ page }) => {
    // Mock read mode
    await page.addInitScript(() => {
      (window as any).Office = {
        context: {
          mailbox: {
            item: {
              itemType: 'message',
              itemMode: 'read', // Explicitly read mode
            },
          },
          requirements: {
            isSetSupported: () => true,
          },
        },
      };
    });

    await page.goto(testConfig.taskPaneUrl);
    await taskPanePage.waitForTaskPaneLoad();

    // Try to navigate to share mode
    if (await taskPanePage.navShareButton.isVisible()) {
      await taskPanePage.navShareButton.click();
    }

    // Should show informative message
    await expect(page.getByText(/compose mode|new email|reply/i)).toBeVisible();
  });
});

/**
 * NOTE: These are E2E tests that require a deployed environment
 *
 * To run these tests:
 * 1. Deploy Outlook add-in to dev environment (task 057)
 * 2. Configure .env with:
 *    - ADDIN_TASKPANE_URL: URL to the task pane HTML
 *    - BFF_API_URL: URL to the BFF API
 *    - Authentication credentials (if needed)
 * 3. Ensure test documents exist in Spaarke
 *
 * Run with:
 *   npx playwright test outlook-addins/share-flow.spec.ts --headed
 *
 * Run specific test:
 *   npx playwright test -g "should search for documents" --headed
 *
 * Run with debug:
 *   npx playwright test outlook-addins/share-flow.spec.ts --debug
 */
