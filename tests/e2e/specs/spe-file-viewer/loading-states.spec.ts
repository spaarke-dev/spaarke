/**
 * E2E Tests: SpeFileViewer - Loading States
 *
 * Tests validate that the FileViewer displays proper loading states
 * and transitions correctly through the component lifecycle.
 *
 * Prerequisites:
 * - Deployed SpeFileViewer PCF control
 * - sprk_document entity with test documents
 * - .env file configured with credentials
 */

import { test, expect } from '@playwright/test';
import { SpeFileViewerPage, FileViewerState } from '../../pages/controls/SpeFileViewerPage';
import controlsConfig from '../../config/pcf-controls.config.json';

test.describe('SpeFileViewer - Loading States @e2e', () => {
  let fileViewerPage: SpeFileViewerPage;

  test.beforeEach(async ({ page }) => {
    // Initialize page object
    const config = controlsConfig.controls.SpeFileViewer;
    fileViewerPage = new SpeFileViewerPage(page, config);
  });

  test('should show loading state within 500ms of navigation', async ({ page }) => {
    // Start timing
    const startTime = Date.now();

    // Navigate to document form with FileViewer
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={test-document-guid}`);

    // Wait for loading state to appear
    await fileViewerPage.waitForLoadingState(500);

    // Verify timing
    const elapsedTime = Date.now() - startTime;
    expect(elapsedTime).toBeLessThan(500);

    // Verify loading overlay is visible
    await expect(fileViewerPage.loadingOverlay).toBeVisible();
    await expect(fileViewerPage.loadingSpinner).toBeVisible();
  });

  test('should have proper accessibility attributes on loading state', async ({ page }) => {
    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={test-document-guid}`);

    // Wait for loading state
    await fileViewerPage.waitForLoadingState();

    // Verify accessibility attributes
    const accessibility = await fileViewerPage.verifyLoadingAccessibility();

    expect(accessibility.role).toBe('status');
    expect(accessibility.ariaBusy).toBe('true');
    expect(accessibility.ariaLabel).toBe('Loading document');
  });

  test('should transition from loading to ready state within 10 seconds', async ({ page }) => {
    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={test-document-guid}`);

    // Wait for control to initialize
    await fileViewerPage.waitForControlInit();

    // Wait for preview to be ready (loading complete)
    await fileViewerPage.waitForPreviewReady(10000);

    // Verify state is Ready
    const state = await fileViewerPage.getState();
    expect(state).toBe(FileViewerState.Ready);

    // Verify preview iframe is loaded
    await expect(fileViewerPage.previewIframe).toBeVisible();
  });

  test('should display loading text while fetching preview', async ({ page }) => {
    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={test-document-guid}`);

    // Wait for loading state
    await fileViewerPage.waitForLoadingState();

    // Verify loading text is displayed
    await expect(fileViewerPage.loadingText).toBeVisible();
    await expect(fileViewerPage.loadingText).toContainText('Loading');
  });

  test('should hide loading overlay when preview is ready', async ({ page }) => {
    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={test-document-guid}`);

    // Wait for preview to be ready
    await fileViewerPage.waitForPreviewReady();

    // Verify loading overlay is hidden
    await expect(fileViewerPage.loadingOverlay).not.toBeVisible();
  });

  test('should maintain loading state during slow network conditions', async ({ page, context }) => {
    // Simulate slow network
    await context.route('**/api/preview-url/**', async (route) => {
      // Delay response by 3 seconds
      await new Promise(resolve => setTimeout(resolve, 3000));
      await route.continue();
    });

    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id={test-document-guid}`);

    // Verify loading state persists
    await fileViewerPage.waitForLoadingState();

    // Wait 2 seconds and verify still loading
    await page.waitForTimeout(2000);
    await expect(fileViewerPage.loadingOverlay).toBeVisible();
  });
});

/**
 * NOTE: These are E2E tests that require a deployed environment
 *
 * To run these tests:
 * 1. Deploy SpeFileViewer PCF control to test environment
 * 2. Create test sprk_document records with associated files
 * 3. Configure .env with:
 *    - POWER_APPS_URL
 *    - DATAVERSE_API_URL
 *    - Authentication credentials
 * 4. Replace {test-document-guid} with actual document ID
 *
 * Run with:
 *   npx playwright test spe-file-viewer/loading-states.spec.ts --headed
 */
