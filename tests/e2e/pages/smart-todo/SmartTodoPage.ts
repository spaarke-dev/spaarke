import { Page, Locator, expect } from '@playwright/test';

/**
 * Page object for the **SmartTodo Code Page** (`sprk_smarttodo` web resource),
 * hosted inside a Power Apps model-driven app.
 *
 * Deliberately does NOT extend `BasePCFPage` — SmartTodo is a Code Page
 * (React SPA web resource), not a PCF control, so the PCF `[data-control-name]`
 * / `__pcfControl` lifecycle machinery does not apply (smart-todo-r5 task 041,
 * per the POML: "adapt — do not literally extend"). Instead it waits on the
 * app's own ready-state signal (the toolbar + the first kanban column).
 *
 * Selectors are derived from the SmartTodo source (stable roles / test-ids /
 * aria-labels), NOT pixel positions, so they survive Fluent-token theme changes
 * (ADR-021):
 *   - Toolbar region:  aria-label="Smart To Do toolbar"
 *   - Filter pill:     data-testid="search-filter"
 *   - Filter input:    data-testid="search-filter-input"
 *   - New Task:        aria-label="Add to-do item"
 *   - Overflow menu:   aria-label="More options"  → menuitem "Layout"
 *   - Kanban columns:  role="list"     (one per Today/Tomorrow/Future)
 *   - Kanban cards:    role="listitem"
 *
 * @remarks REAL-ENVIRONMENT NOTE (task 041 escalation): the card-selection and
 *   theme-toggle mechanisms below are the two interactions that could not be
 *   confirmed against a live DOM in the authoring sandbox. Both are implemented
 *   defensively with documented assumptions; the follow-up real-environment run
 *   (per the POML escalation trigger) should confirm/adjust `selectCard()` and
 *   `setColorScheme()` against the deployed page.
 */
export class SmartTodoPage {
  readonly page: Page;
  /** Deployed Code Page URL (from SMART_TODO_URL env — see .env.example). */
  readonly url: string;

  readonly toolbar: Locator;
  readonly filterPill: Locator;
  readonly filterInput: Locator;
  readonly newTaskButton: Locator;
  readonly overflowButton: Locator;
  readonly columns: Locator;
  readonly cards: Locator;

  constructor(page: Page, url?: string) {
    this.page = page;
    // Default is an explicit placeholder so a misconfigured run fails loudly on
    // navigation rather than silently hitting make.powerapps.com's baseURL.
    this.url = url ?? process.env.SMART_TODO_URL ?? '__SET_SMART_TODO_URL__';

    this.toolbar = page.getByRole('toolbar', { name: 'Smart To Do toolbar' });
    this.filterPill = page.getByTestId('search-filter');
    this.filterInput = page.getByTestId('search-filter-input');
    this.newTaskButton = page.getByRole('button', { name: 'Add to-do item' });
    this.overflowButton = page.getByRole('button', { name: 'More options' });
    this.columns = page.getByRole('list');
    this.cards = page.getByRole('listitem');
  }

  /** Navigate to the deployed Code Page (does not wait for readiness). */
  async goto(): Promise<void> {
    await this.page.goto(this.url);
  }

  /**
   * Wait for the app's ready-state signal — analogous to
   * `BasePCFPage.waitForControlInit`, but keyed on the SmartTodo toolbar +
   * first kanban column rather than a PCF `[role=progressbar]` disappearance.
   * The kanban only renders after `isAuthReady` + the first data fetch resolve,
   * so a visible column is a reliable "interactive" signal.
   */
  async waitForReady(timeout = 30000): Promise<void> {
    await this.toolbar.waitFor({ state: 'visible', timeout });
    await this.columns.first().waitFor({ state: 'visible', timeout });
  }

  /** Open the top-bar overflow ("More options") menu. */
  async openOverflowMenu(): Promise<void> {
    await this.overflowButton.click();
    await this.page.getByRole('menu').waitFor({ state: 'visible' });
  }

  /**
   * Flip the kanban orientation via the overflow → "Layout" menu item
   * (FR-05 / FR-09 — stacked ↔ side-by-side). The Layout item only renders when
   * both `orientation` and `onOrientationChange` are wired (Header.tsx).
   */
  async flipOrientationViaLayout(): Promise<void> {
    await this.openOverflowMenu();
    await this.page.getByRole('menuitem', { name: 'Layout' }).click();
  }

  /**
   * Select the Nth kanban card and return its locator.
   *
   * ASSUMPTION (real-env confirm): selection is a click on the card that sets an
   * `aria-selected="true"` / `[data-selected]` attribute. If the deployed app
   * uses an explicit checkbox instead, adjust to click the card's checkbox.
   */
  async selectCard(index = 0): Promise<Locator> {
    const card = this.cards.nth(index);
    await card.scrollIntoViewIfNeeded();
    await card.click();
    return card;
  }

  /** True if `card` is in a selected DOM state (aria-selected or data-selected). */
  async isCardSelected(card: Locator): Promise<boolean> {
    const ariaSelected = await card.getAttribute('aria-selected');
    const dataSelected = await card.getAttribute('data-selected');
    return ariaSelected === 'true' || dataSelected != null;
  }

  /**
   * Emulate a color scheme for the axe dark/light passes.
   *
   * Primary mechanism: `page.emulateMedia({ colorScheme })` — works if the
   * deployed app resolves theme from `prefers-color-scheme`. Spaarke surfaces
   * also honor a stored user-preference (`themeStorage`); as a belt, we also set
   * the documented localStorage key so the real-env run works either way.
   * ASSUMPTION (real-env confirm): the themeStorage key name / value below.
   */
  async setColorScheme(scheme: 'light' | 'dark'): Promise<void> {
    await this.page.emulateMedia({ colorScheme: scheme });
    await this.page.evaluate(s => {
      try {
        // Best-effort: mirrors resolveThemeWithUserPreference's storage contract.
        window.localStorage.setItem('spaarke.theme.preference', s);
      } catch {
        /* storage may be unavailable in some embed contexts — non-fatal */
      }
    }, scheme);
  }

  /**
   * Bounding boxes of all rendered kanban columns — used by the orientation
   * layout-glitch check (no overlaps, no zero-size boxes).
   */
  async columnBoxes(): Promise<Array<{ x: number; y: number; width: number; height: number }>> {
    const n = await this.columns.count();
    const boxes: Array<{ x: number; y: number; width: number; height: number }> = [];
    for (let i = 0; i < n; i++) {
      const box = await this.columns.nth(i).boundingBox();
      if (box) boxes.push(box);
    }
    return boxes;
  }

  /** Assert no column has a zero/negative-size box and no two columns overlap. */
  async assertNoLayoutGlitch(): Promise<void> {
    const boxes = await this.columnBoxes();
    expect(boxes.length, 'at least one kanban column should render').toBeGreaterThan(0);
    for (const b of boxes) {
      expect(b.width, 'column width > 0 (no collapsed column)').toBeGreaterThan(0);
      expect(b.height, 'column height > 0 (no collapsed column)').toBeGreaterThan(0);
    }
    for (let i = 0; i < boxes.length; i++) {
      for (let j = i + 1; j < boxes.length; j++) {
        const a = boxes[i];
        const b = boxes[j];
        const overlap = a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
        expect(overlap, `columns ${i} and ${j} must not overlap after orientation flip`).toBe(false);
      }
    }
  }
}
