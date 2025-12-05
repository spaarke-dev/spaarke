/**
 * E2E Tests: SpeFileViewer - Open in Desktop
 *
 * Tests validate the "Edit in Desktop" button functionality,
 * including protocol URL generation for Word, Excel, and PowerPoint files.
 *
 * Prerequisites:
 * - Deployed SpeFileViewer PCF control
 * - sprk_document entity with test documents (.docx, .xlsx, .pptx)
 * - .env file configured with credentials
 */

import { test, expect } from '@playwright/test';
import { SpeFileViewerPage } from '../../pages/controls/SpeFileViewerPage';
import controlsConfig from '../../config/pcf-controls.config.json';

// Test document GUIDs - replace with actual document IDs from test environment
const TEST_DOCUMENTS = {
  word: '{word-document-guid}',
  excel: '{excel-document-guid}',
  powerpoint: '{powerpoint-document-guid}',
  pdf: '{pdf-document-guid}',
  unsupported: '{unsupported-file-guid}'
};

test.describe('SpeFileViewer - Open in Desktop @e2e', () => {
  let fileViewerPage: SpeFileViewerPage;

  test.beforeEach(async ({ page }) => {
    // Initialize page object
    const config = controlsConfig.controls.SpeFileViewer;
    fileViewerPage = new SpeFileViewerPage(page, config);
  });

  test.describe('Edit Button Visibility', () => {
    test('should display Edit button when preview is ready', async ({ page }) => {
      // Navigate to Word document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForControlInit();
      await fileViewerPage.waitForPreviewReady();

      // Verify Edit button is visible
      await expect(fileViewerPage.editInDesktopButton).toBeVisible();
    });

    test('should enable Edit button for supported file types', async ({ page }) => {
      // Navigate to Word document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Verify button is enabled
      const isEnabled = await fileViewerPage.isEditButtonEnabled();
      expect(isEnabled).toBe(true);
    });

    test('should have accessible aria-label on Edit button', async ({ page }) => {
      // Navigate to Word document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Verify accessibility
      const accessibility = await fileViewerPage.verifyButtonAccessibility();
      expect(accessibility.editAriaLabel).toBeTruthy();
      expect(accessibility.editAriaLabel).toContain('Edit');
    });
  });

  test.describe('Protocol URL Generation', () => {
    test('should trigger ms-word: protocol for Word documents', async ({ page }) => {
      // Navigate to Word document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Click Edit and capture protocol URL
      const protocolUrl = await fileViewerPage.clickEditInDesktopAndCaptureUrl();

      // Verify protocol URL format
      expect(protocolUrl).not.toBeNull();
      expect(protocolUrl).toMatch(/^ms-word:ofe\|u\|/);
    });

    test('should trigger ms-excel: protocol for Excel documents', async ({ page }) => {
      // Navigate to Excel document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.excel}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Click Edit and capture protocol URL
      const protocolUrl = await fileViewerPage.clickEditInDesktopAndCaptureUrl();

      // Verify protocol URL format
      expect(protocolUrl).not.toBeNull();
      expect(protocolUrl).toMatch(/^ms-excel:ofe\|u\|/);
    });

    test('should trigger ms-powerpoint: protocol for PowerPoint documents', async ({ page }) => {
      // Navigate to PowerPoint document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.powerpoint}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Click Edit and capture protocol URL
      const protocolUrl = await fileViewerPage.clickEditInDesktopAndCaptureUrl();

      // Verify protocol URL format
      expect(protocolUrl).not.toBeNull();
      expect(protocolUrl).toMatch(/^ms-powerpoint:ofe\|u\|/);
    });

    test('should include URL-encoded web URL in protocol', async ({ page }) => {
      // Navigate to Word document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Click Edit and capture protocol URL
      const protocolUrl = await fileViewerPage.clickEditInDesktopAndCaptureUrl();

      // Verify URL is encoded (contains %3A for colon, %2F for slash)
      expect(protocolUrl).toContain('%3A');
      expect(protocolUrl).toContain('%2F');
    });
  });

  test.describe('Unsupported File Types', () => {
    test('should not show Edit button for unsupported file types (PDF)', async ({ page }) => {
      // Navigate to PDF document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.pdf}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Verify Edit button is not visible or disabled
      const isEnabled = await fileViewerPage.isEditButtonEnabled();
      expect(isEnabled).toBe(false);
    });
  });

  test.describe('Loading State During Edit', () => {
    test('should show loading state while fetching open-links', async ({ page, context }) => {
      // Intercept open-links API call and delay response
      await context.route('**/api/open-links/**', async (route) => {
        await new Promise(resolve => setTimeout(resolve, 1000));
        await route.continue();
      });

      // Navigate to Word document
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

      // Wait for preview to be ready
      await fileViewerPage.waitForPreviewReady();

      // Click Edit button
      await fileViewerPage.clickEditInDesktop();

      // Verify button shows loading state
      const isLoading = await fileViewerPage.isEditButtonLoading();
      expect(isLoading).toBe(true);
    });
  });
});

test.describe('SpeFileViewer - Error States @e2e', () => {
  let fileViewerPage: SpeFileViewerPage;

  test.beforeEach(async ({ page }) => {
    const config = controlsConfig.controls.SpeFileViewer;
    fileViewerPage = new SpeFileViewerPage(page, config);
  });

  test('should display error state on fetch failure', async ({ page, context }) => {
    // Intercept preview URL API and return error
    await context.route('**/api/preview-url/**', async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Internal server error' })
      });
    });

    // Navigate to document
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

    // Wait for error state
    await fileViewerPage.waitForErrorState();

    // Verify error container is visible
    await expect(fileViewerPage.errorContainer).toBeVisible();
  });

  test('should display meaningful error message', async ({ page, context }) => {
    // Intercept and return error
    await context.route('**/api/preview-url/**', async (route) => {
      await route.fulfill({
        status: 404,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Document not found' })
      });
    });

    // Navigate to non-existent document
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={non-existent-guid}`);

    // Wait for error state
    await fileViewerPage.waitForErrorState();

    // Verify error message
    const errorMessage = await fileViewerPage.getErrorMessage();
    expect(errorMessage.length).toBeGreaterThan(0);
  });

  test('should show retry button in error state', async ({ page, context }) => {
    // Intercept and return error
    await context.route('**/api/preview-url/**', async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Server error' })
      });
    });

    // Navigate to document
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

    // Wait for error state
    await fileViewerPage.waitForErrorState();

    // Verify retry button is visible
    await expect(fileViewerPage.retryButton).toBeVisible();
  });

  test('should refetch on retry button click', async ({ page, context }) => {
    let callCount = 0;

    // Intercept API calls and track count
    await context.route('**/api/preview-url/**', async (route) => {
      callCount++;
      if (callCount === 1) {
        // First call fails
        await route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Temporary error' })
        });
      } else {
        // Subsequent calls succeed
        await route.continue();
      }
    });

    // Navigate to document
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENTS.word}`);

    // Wait for error state
    await fileViewerPage.waitForErrorState();

    // Click retry
    await fileViewerPage.clickRetry();

    // Verify second call was made
    expect(callCount).toBe(2);
  });
});

/**
 * NOTE: These are E2E tests that require a deployed environment
 *
 * To run these tests:
 * 1. Deploy SpeFileViewer PCF control to test environment
 * 2. Create test sprk_document records with:
 *    - Word document (.docx)
 *    - Excel document (.xlsx)
 *    - PowerPoint document (.pptx)
 *    - PDF document (.pdf)
 * 3. Configure .env with credentials
 * 4. Replace TEST_DOCUMENTS GUIDs with actual document IDs
 *
 * Run with:
 *   npx playwright test spe-file-viewer/open-in-desktop.spec.ts --headed
 */
