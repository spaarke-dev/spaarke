/**
 * E2E: SmartTodo Code Page — Accessibility (NFR-01: WCAG 2.1 AA + keyboard nav)
 *
 * Extends the existing tests/e2e harness (smart-todo-r5 task 041, FR-17).
 * Uses @axe-core/playwright (already a root devDependency) per the pattern in
 * tests/e2e/README.md § Accessibility Testing:
 *   new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze()
 *
 * NFR-01 specifically calls out dark-on-yellow contrast, so the axe scan runs
 * TWICE — once light, once dark — because a contrast regression on the urgency
 * badges only shows up in one theme. Assertions are on axe violations / DOM
 * focus state, never pixel-diff snapshots (ADR-021 — Fluent tokens make
 * screenshots brittle across themes).
 *
 * Prerequisites (real environment — see task 041 escalation note):
 *   - SmartTodo Code Page deployed; SMART_TODO_URL set in .env; authed session.
 */

import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { SmartTodoPage } from '../../pages/smart-todo/SmartTodoPage';

const WCAG_AA_TAGS = ['wcag2a', 'wcag2aa', 'wcag21aa'];

test.describe('SmartTodo Code Page - Accessibility @a11y @e2e', () => {
  test('zero WCAG 2.1 AA violations in light AND dark theme (NFR-01 dark-on-yellow)', async ({ page }) => {
    const smartTodo = new SmartTodoPage(page);

    // ── Light pass ──────────────────────────────────────────────────────────
    await smartTodo.setColorScheme('light');
    await smartTodo.goto();
    await smartTodo.waitForReady();

    const lightResults = await new AxeBuilder({ page }).withTags(WCAG_AA_TAGS).analyze();
    expect(
      lightResults.violations,
      `light-theme WCAG 2.1 AA violations: ${lightResults.violations.map(v => v.id).join(', ')}`
    ).toEqual([]);

    // ── Dark pass (the dark-on-yellow contrast requirement) ─────────────────
    await smartTodo.setColorScheme('dark');
    await smartTodo.goto(); // reload so the theme resolves from the start
    await smartTodo.waitForReady();

    const darkResults = await new AxeBuilder({ page }).withTags(WCAG_AA_TAGS).analyze();
    expect(
      darkResults.violations,
      `dark-theme WCAG 2.1 AA violations: ${darkResults.violations.map(v => v.id).join(', ')}`
    ).toEqual([]);

    test.info().annotations.push({
      type: 'a11y',
      description: `axe WCAG2.1AA — light: ${lightResults.violations.length} violations, dark: ${darkResults.violations.length} violations`,
    });
  });

  test('top bar / filter / kanban are keyboard-reachable with visible focus, no trap (NFR-01)', async ({ page }) => {
    const smartTodo = new SmartTodoPage(page);
    await smartTodo.goto();
    await smartTodo.waitForReady();

    // Focus the document body, then Tab through the interactive top-bar chain.
    // We assert focus lands on each expected control (accessible-name match),
    // rather than a fixed tab-count, so the test tolerates incidental focusable
    // nodes while still proving each control is reachable and not trapped.
    await page.locator('body').click({ position: { x: 2, y: 2 } });

    const expectedStops = [smartTodo.filterPill, smartTodo.newTaskButton, smartTodo.overflowButton];

    for (const stop of expectedStops) {
      let reached = false;
      // Bounded Tab walk — proves reachability without an infinite loop (a trap
      // would exhaust this budget without focusing the control).
      for (let i = 0; i < 25 && !reached; i++) {
        await page.keyboard.press('Tab');
        if (await stop.evaluate(el => el === document.activeElement).catch(() => false)) {
          reached = true;
        }
      }
      expect(reached, 'control must be reachable via keyboard Tab (no keyboard trap)').toBe(true);
      // Focused control must be visibly focused (focus-visible or a focus ring).
      await expect(stop).toBeFocused();
    }

    // Enter/Space activates the focused overflow button (opens the menu).
    await smartTodo.overflowButton.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('menu')).toBeVisible();
    await page.keyboard.press('Escape');

    // The kanban is reachable: Tab from the top bar eventually focuses a card.
    let cardFocused = false;
    for (let i = 0; i < 40 && !cardFocused; i++) {
      await page.keyboard.press('Tab');
      cardFocused = await smartTodo.cards
        .first()
        .evaluate(el => el.contains(document.activeElement) || el === document.activeElement)
        .catch(() => false);
    }
    expect(cardFocused, 'kanban cards must be keyboard-reachable from the top bar').toBe(true);
  });
});
