/**
 * E2E: SmartTodo Code Page — Orientation flip (NFR-03)
 *
 * Extends the existing tests/e2e harness (smart-todo-r5 task 041, FR-17).
 * NFR-03: flipping the kanban orientation (stacked ↔ side-by-side) via the
 * overflow → "Layout" action must preserve selection + drag-drop and must not
 * produce a layout glitch. Assertions are DOM/geometry-based (selection
 * attribute, boundingBox overlap, column membership) — never pixel-diff
 * snapshots (ADR-021).
 *
 * Prerequisites (real environment — see task 041 escalation note):
 *   - SmartTodo Code Page deployed with ≥2 cards spanning ≥2 columns.
 *   - SMART_TODO_URL set in .env; authed session.
 */

import { test, expect } from '@playwright/test';
import { SmartTodoPage } from '../../pages/smart-todo/SmartTodoPage';

test.describe('SmartTodo Code Page - Orientation flip @orientation @e2e', () => {
  test('flip preserves selection + drag-drop with no layout glitch (NFR-03)', async ({ page }) => {
    const smartTodo = new SmartTodoPage(page);
    await smartTodo.goto();
    await smartTodo.waitForReady();

    // Need at least two columns to prove a cross-column drag survives the flip.
    const columnCount = await smartTodo.columns.count();
    test.skip(columnCount < 2, 'orientation drag-drop requires ≥2 kanban columns of test data');

    // ── 1. Select a card, capture its identity, assert selected state ────────
    const card = await smartTodo.selectCard(0);
    const cardText = ((await card.textContent()) ?? '').trim();
    expect(await smartTodo.isCardSelected(card), 'card should be selected after click').toBe(true);

    // ── 2. Flip orientation via overflow → Layout ───────────────────────────
    await smartTodo.flipOrientationViaLayout();

    // ── 3. Selection survives the flip ──────────────────────────────────────
    // Re-resolve the card by its text (the node may re-render on orientation change).
    const cardAfter = smartTodo.cards.filter({ hasText: cardText }).first();
    await expect(cardAfter, 'the selected card should still be present after the flip').toBeVisible();
    expect(await smartTodo.isCardSelected(cardAfter), 'selection state must survive the orientation flip').toBe(true);

    // ── 4. No layout glitch (no collapsed / overlapping columns) ────────────
    await smartTodo.assertNoLayoutGlitch();

    // ── 5. Drag-drop still works post-flip (card changes column membership) ──
    const sourceColumn = smartTodo.columns.filter({ has: cardAfter });
    const targetColumn = smartTodo.columns.nth(columnCount - 1);
    const targetHadCardBefore = await targetColumn.locator('[role="listitem"]', { hasText: cardText }).count();

    await cardAfter.dragTo(targetColumn);

    // The dragged card is now a descendant of the target column.
    const targetHasCardAfter = await targetColumn.locator('[role="listitem"]', { hasText: cardText }).count();
    expect(
      targetHasCardAfter,
      'after a post-flip drag, the card must appear in the target column (drag-drop still functions)'
    ).toBeGreaterThan(targetHadCardBefore);

    // Sanity: source and target were genuinely different columns.
    expect(await sourceColumn.first().evaluate((el, t) => el === t, await targetColumn.elementHandle())).toBe(false);
  });
});
