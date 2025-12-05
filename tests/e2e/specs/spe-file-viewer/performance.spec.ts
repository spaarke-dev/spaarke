/**
 * E2E Tests: SpeFileViewer - Performance Validation
 *
 * Tests validate that performance targets from the spec are met:
 * - Loading state appears within 200ms of init (500ms with navigation)
 * - Preview loads within 3 seconds with warm BFF
 * - Cold start time is documented (measurement only)
 *
 * Prerequisites:
 * - Deployed SpeFileViewer PCF control
 * - BFF API deployed and accessible
 * - .env file configured with credentials
 */

import { test, expect } from '@playwright/test';
import { SpeFileViewerPage, FileViewerState } from '../../pages/controls/SpeFileViewerPage';
import controlsConfig from '../../config/pcf-controls.config.json';

// Test document for performance testing
const TEST_DOCUMENT_ID = '{performance-test-document-guid}';

// Performance targets (from spec)
const TARGETS = {
  loadingStateMs: 500,      // 200ms target + 300ms navigation margin
  previewLoadWarmMs: 3000,  // 3 seconds with warm BFF
  previewLoadColdMs: 10000  // 10 seconds allowed for cold start
};

// BFF API URL for warm-up
const BFF_API_URL = process.env.BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net';

test.describe('SpeFileViewer - Performance @performance @e2e', () => {
  let fileViewerPage: SpeFileViewerPage;

  test.beforeEach(async ({ page }) => {
    const config = controlsConfig.controls.SpeFileViewer;
    fileViewerPage = new SpeFileViewerPage(page, config);
  });

  test('loading state appears within 500ms of navigation', async ({ page }) => {
    // Start timing
    const startTime = Date.now();

    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENT_ID}`);

    // Wait for loading state to appear
    await fileViewerPage.loadingOverlay.waitFor({ state: 'visible', timeout: TARGETS.loadingStateMs });

    // Calculate elapsed time
    const elapsed = Date.now() - startTime;

    // Verify target met
    expect(elapsed).toBeLessThan(TARGETS.loadingStateMs);

    // Record performance data in test annotations
    test.info().annotations.push({
      type: 'performance',
      description: `Loading state appeared in ${elapsed}ms (target: <${TARGETS.loadingStateMs}ms)`
    });

    console.log(`[Performance] Loading state: ${elapsed}ms`);
  });

  test('preview loads within 3 seconds with warm BFF', async ({ page, request }) => {
    // Warm up BFF by hitting ping endpoint
    console.log('[Performance] Warming up BFF...');
    const warmupResponse = await request.get(`${BFF_API_URL}/ping`);
    expect(warmupResponse.ok()).toBeTruthy();

    // Wait for any potential container scaling
    await page.waitForTimeout(500);

    // Start timing after warm-up
    const startTime = Date.now();

    // Navigate to document form
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENT_ID}`);

    // Wait for preview to be ready
    await fileViewerPage.waitForPreviewReady(TARGETS.previewLoadWarmMs);

    // Calculate elapsed time
    const elapsed = Date.now() - startTime;

    // Verify target met
    expect(elapsed).toBeLessThan(TARGETS.previewLoadWarmMs);

    // Record performance data
    test.info().annotations.push({
      type: 'performance',
      description: `Preview loaded in ${elapsed}ms with warm BFF (target: <${TARGETS.previewLoadWarmMs}ms)`
    });

    console.log(`[Performance] Preview load (warm): ${elapsed}ms`);
  });

  test('measure cold start time (documentation only)', async ({ page }) => {
    // This test measures cold start time but does not fail if target exceeded
    // It's for documentation/baseline purposes

    // Start timing immediately
    const startTime = Date.now();

    // Navigate to document form (no warm-up)
    await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENT_ID}`);

    // Wait for preview to be ready (with extended timeout for cold start)
    try {
      await fileViewerPage.waitForPreviewReady(TARGETS.previewLoadColdMs);

      const elapsed = Date.now() - startTime;

      // Record cold start time (does not fail test)
      test.info().annotations.push({
        type: 'performance',
        description: `Cold start preview load: ${elapsed}ms`
      });

      console.log(`[Performance] Cold start: ${elapsed}ms`);

      // Mark test result based on timing
      if (elapsed > TARGETS.previewLoadWarmMs) {
        test.info().annotations.push({
          type: 'warning',
          description: `Cold start (${elapsed}ms) exceeded warm target (${TARGETS.previewLoadWarmMs}ms) - expected behavior`
        });
      }
    } catch (error) {
      test.info().annotations.push({
        type: 'error',
        description: `Cold start exceeded ${TARGETS.previewLoadColdMs}ms timeout`
      });
      console.error(`[Performance] Cold start exceeded timeout`);
    }
  });

  test('multiple load measurements for P50/P95 calculation', async ({ page, request }) => {
    // Run multiple measurements to calculate percentiles
    const measurements: number[] = [];
    const iterations = 5;

    for (let i = 0; i < iterations; i++) {
      // Warm up BFF
      await request.get(`${BFF_API_URL}/ping`);

      const startTime = Date.now();

      // Navigate fresh
      await page.goto(`/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${TEST_DOCUMENT_ID}`, {
        waitUntil: 'domcontentloaded'
      });

      // Wait for preview ready
      await fileViewerPage.waitForPreviewReady(TARGETS.previewLoadWarmMs);

      const elapsed = Date.now() - startTime;
      measurements.push(elapsed);

      console.log(`[Performance] Run ${i + 1}/${iterations}: ${elapsed}ms`);

      // Brief pause between iterations
      await page.waitForTimeout(1000);
    }

    // Calculate statistics
    measurements.sort((a, b) => a - b);
    const p50 = measurements[Math.floor(measurements.length * 0.5)];
    const p95 = measurements[Math.floor(measurements.length * 0.95)];
    const avg = measurements.reduce((a, b) => a + b, 0) / measurements.length;

    // Record statistics
    test.info().annotations.push({
      type: 'performance',
      description: `Preview Load - Avg: ${avg.toFixed(0)}ms, P50: ${p50}ms, P95: ${p95}ms`
    });

    console.log(`[Performance] Stats - Avg: ${avg.toFixed(0)}ms, P50: ${p50}ms, P95: ${p95}ms`);

    // P95 should meet target
    expect(p95).toBeLessThan(TARGETS.previewLoadWarmMs);
  });
});

test.describe('SpeFileViewer - API Response Times @performance @e2e', () => {
  test('BFF /ping endpoint responds within 100ms', async ({ request }) => {
    const startTime = Date.now();

    const response = await request.get(`${BFF_API_URL}/ping`);

    const elapsed = Date.now() - startTime;

    expect(response.ok()).toBeTruthy();
    expect(elapsed).toBeLessThan(100);

    test.info().annotations.push({
      type: 'performance',
      description: `/ping response: ${elapsed}ms (target: <100ms)`
    });
  });

  test('BFF /healthz endpoint responds within 500ms', async ({ request }) => {
    const startTime = Date.now();

    const response = await request.get(`${BFF_API_URL}/healthz`);

    const elapsed = Date.now() - startTime;

    expect(response.ok()).toBeTruthy();
    expect(elapsed).toBeLessThan(500);

    test.info().annotations.push({
      type: 'performance',
      description: `/healthz response: ${elapsed}ms (target: <500ms)`
    });
  });

  test('BFF /status endpoint responds within 500ms', async ({ request }) => {
    const startTime = Date.now();

    const response = await request.get(`${BFF_API_URL}/status`);

    const elapsed = Date.now() - startTime;

    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body.service).toBe('Spe.Bff.Api');

    expect(elapsed).toBeLessThan(500);

    test.info().annotations.push({
      type: 'performance',
      description: `/status response: ${elapsed}ms (target: <500ms)`
    });
  });
});

/**
 * NOTE: Performance tests require a deployed environment
 *
 * To run these tests:
 * 1. Deploy SpeFileViewer PCF to test environment
 * 2. Deploy BFF API with /ping, /healthz, /status endpoints
 * 3. Configure .env with:
 *    - POWER_APPS_URL
 *    - BFF_API_URL
 *    - Authentication credentials
 * 4. Replace TEST_DOCUMENT_ID with actual document GUID
 *
 * Run with:
 *   npx playwright test spe-file-viewer/performance.spec.ts --headed
 *
 * For baseline measurements, run multiple times and record results
 * in the performance-baseline.md document.
 */
