/**
 * E2E Tests: Universal Dataset Grid - Rendering
 *
 * These tests validate grid rendering in a real Power Apps environment
 *
 * Prerequisites:
 * - Deployed UniversalDatasetGrid PCF control
 * - Dataverse environment with test data
 * - .env file configured with credentials
 */

import { test, expect } from '@playwright/test';
import { UniversalDatasetGridPage } from '../../pages/controls/UniversalDatasetGridPage';
import controlsConfig from '../../config/pcf-controls.config.json';

test.describe('Universal Dataset Grid - Rendering @e2e', () => {
  let gridPage: UniversalDatasetGridPage;

  test.beforeEach(async ({ page }) => {
    // Navigate to account list view with PCF control
    await page.goto(`/main.aspx?pagetype=entitylist&etn=account`);

    // Initialize page object
    const config = controlsConfig.controls.UniversalDatasetGrid;
    gridPage = new UniversalDatasetGridPage(page, config);

    // Wait for PCF control to initialize
    await gridPage.waitForControlInit();
  });

  test('should render grid with records from Dataverse', async () => {
    // Verify grid is visible
    await expect(gridPage.grid).toBeVisible();

    // Verify records are loaded
    const recordCount = await gridPage.getRecordCount();
    expect(recordCount).toBeGreaterThan(0);
  });

  test('should display correct column headers from view definition', async () => {
    // Get column headers
    const headers = await gridPage.getColumnHeaders();

    // Verify expected columns (based on default account view)
    expect(headers.length).toBeGreaterThan(0);
    expect(headers.some(h => h.includes('Name') || h.includes('Account'))).toBe(true);
  });

  test('should render toolbar with commands', async () => {
    // Verify toolbar is visible
    await expect(gridPage.toolbar).toBeVisible();

    // Verify key commands are present
    await expect(gridPage.createButton).toBeVisible();
    await expect(gridPage.refreshButton).toBeVisible();
  });

  test('should handle empty dataset gracefully', async ({ page }) => {
    // Navigate to entity with no records (or filtered view)
    await page.goto(`/main.aspx?pagetype=entitylist&etn=account&viewid=empty`);

    // Grid should still render
    await expect(gridPage.grid).toBeVisible();

    // No rows should be present
    const rowCount = await gridPage.getRecordCount();
    expect(rowCount).toBe(0);
  });

  test('should maintain state after refresh', async () => {
    // Get initial record count
    const initialCount = await gridPage.getRecordCount();

    // Trigger refresh
    await gridPage.refresh();

    // Verify records persist
    const refreshedCount = await gridPage.getRecordCount();
    expect(refreshedCount).toBe(initialCount);
  });
});

/**
 * NOTE: These are EXAMPLE E2E tests
 *
 * To run these tests, you need:
 * 1. A deployed PCF control in a Dataverse environment
 * 2. Configured .env file with:
 *    - POWER_APPS_URL
 *    - DATAVERSE_API_URL
 *    - Authentication credentials
 * 3. Test data in the target environment
 *
 * Run with:
 *   npm run test:e2e
 *   npx playwright test --headed --project=chromium
 */
