/**
 * E2E: SmartTodo Code Page — Performance (NFR-02: page-load < 3s)
 *
 * Extends the existing tests/e2e harness (smart-todo-r5 task 041, FR-17).
 * Mirrors the timing pattern proven in
 * tests/e2e/specs/spe-file-viewer/performance.spec.ts: Date.now() bracketing a
 * ready-state wait, test.info().annotations.push(...) for CI visibility, and an
 * explicit expect(elapsed).toBeLessThan(target).
 *
 * Prerequisites (real environment — see task 041 escalation note):
 *   - SmartTodo Code Page (sprk_smarttodo) deployed
 *   - SMART_TODO_URL set in tests/e2e/config/.env (the deployed page route)
 *   - An authenticated Power Apps session (baseURL / storage state)
 */

import { test, expect } from '@playwright/test';
import { SmartTodoPage } from '../../pages/smart-todo/SmartTodoPage';

// NFR-02 target (spec): Code Page interactive within 3 seconds.
const LOAD_TARGET_MS = 3000;

test.describe('SmartTodo Code Page - Performance @performance @e2e', () => {
  test('page-load to kanban ready-state is under 3s (NFR-02)', async ({ page }) => {
    const smartTodo = new SmartTodoPage(page);

    const startTime = Date.now();

    await smartTodo.goto();
    // Ready-state = toolbar visible + first kanban column rendered (post auth +
    // first data fetch). This is the equivalent of the spe-file-viewer
    // waitForPreviewReady signal for a Code Page.
    await smartTodo.waitForReady(LOAD_TARGET_MS);

    const elapsed = Date.now() - startTime;

    expect(elapsed).toBeLessThan(LOAD_TARGET_MS);

    test.info().annotations.push({
      type: 'performance',
      description: `SmartTodo Code Page ready in ${elapsed}ms (target: <${LOAD_TARGET_MS}ms)`,
    });

    console.log(`[Performance] SmartTodo ready: ${elapsed}ms`);
  });

  test('measure ready-state across 3 loads for P50/P95 (documentation)', async ({ page }) => {
    const smartTodo = new SmartTodoPage(page);
    const measurements: number[] = [];
    const iterations = 3;

    for (let i = 0; i < iterations; i++) {
      const startTime = Date.now();
      await smartTodo.goto();
      await smartTodo.waitForReady(LOAD_TARGET_MS * 2); // generous for cold iterations
      measurements.push(Date.now() - startTime);
      await page.waitForTimeout(500);
    }

    measurements.sort((a, b) => a - b);
    const p50 = measurements[Math.floor(measurements.length * 0.5)];
    const p95 = measurements[Math.min(measurements.length - 1, Math.floor(measurements.length * 0.95))];
    const avg = measurements.reduce((a, b) => a + b, 0) / measurements.length;

    test.info().annotations.push({
      type: 'performance',
      description: `SmartTodo ready-state — Avg: ${avg.toFixed(0)}ms, P50: ${p50}ms, P95: ${p95}ms`,
    });
    console.log(`[Performance] SmartTodo ready-state — Avg: ${avg.toFixed(0)}ms, P50: ${p50}ms, P95: ${p95}ms`);

    // P95 is the binding view of NFR-02 across repeated warm loads.
    expect(p95).toBeLessThan(LOAD_TARGET_MS);
  });
});
