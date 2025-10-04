import { Page, Locator } from '@playwright/test';
import { BasePCFPage, PCFControlConfig } from '../BasePCFPage';

/**
 * Page Object for Universal Dataset Grid PCF Control
 * Provides methods specific to the grid control
 */
export class UniversalDatasetGridPage extends BasePCFPage {
  // Toolbar
  readonly toolbar: Locator;
  readonly createButton: Locator;
  readonly deleteButton: Locator;
  readonly refreshButton: Locator;
  readonly overflowButton: Locator;

  // Grid
  readonly grid: Locator;
  readonly gridRows: Locator;
  readonly gridHeaders: Locator;

  // Views
  readonly viewSwitcher: Locator;

  constructor(page: Page, config: PCFControlConfig) {
    super(page, config);

    // Toolbar selectors
    this.toolbar = this.controlRoot.locator('[role="toolbar"]');
    this.createButton = this.toolbar.locator('button', { hasText: 'New' });
    this.deleteButton = this.toolbar.locator('button', { hasText: 'Delete' });
    this.refreshButton = this.toolbar.locator('button', { hasText: 'Refresh' });
    this.overflowButton = this.toolbar.locator('button[aria-label*="More"]');

    // Grid selectors
    this.grid = this.controlRoot.locator('[role="grid"]');
    this.gridRows = this.grid.locator('[role="row"]').filter({ hasNot: this.page.locator('[role="columnheader"]') });
    this.gridHeaders = this.grid.locator('[role="columnheader"]');

    // View switcher
    this.viewSwitcher = this.controlRoot.locator('[aria-label="View mode"]');
  }

  /**
   * Click toolbar command by label
   */
  async clickCommand(commandLabel: string): Promise<void> {
    const button = this.toolbar.locator(`button:has-text("${commandLabel}")`);
    await button.click();
    await this.waitForUpdate();
  }

  /**
   * Click command from overflow menu
   */
  async clickOverflowCommand(commandLabel: string): Promise<void> {
    await this.overflowButton.click();
    const menuItem = this.page.locator(`[role="menuitem"]:has-text("${commandLabel}")`);
    await menuItem.click();
    await this.waitForUpdate();
  }

  /**
   * Select record by row index
   */
  async selectRow(index: number): Promise<void> {
    const row = this.gridRows.nth(index);
    await row.click();
  }

  /**
   * Select multiple records
   */
  async selectRows(indices: number[]): Promise<void> {
    for (const index of indices) {
      const checkbox = this.gridRows.nth(index).locator('input[type="checkbox"]');
      await checkbox.check();
    }
  }

  /**
   * Get record count
   */
  async getRecordCount(): Promise<number> {
    return await this.gridRows.count();
  }

  /**
   * Get column headers
   */
  async getColumnHeaders(): Promise<string[]> {
    return await this.gridHeaders.allTextContents();
  }

  /**
   * Switch view mode
   */
  async switchView(viewMode: 'Grid' | 'List' | 'Card'): Promise<void> {
    await this.viewSwitcher.click();
    await this.page.locator(`[role="menuitem"]:has-text("${viewMode}")`).click();
    await this.waitForUpdate();
  }

  /**
   * Execute keyboard shortcut
   */
  async executeShortcut(shortcut: string): Promise<void> {
    // Parse shortcut like "Ctrl+N"
    const parts = shortcut.split('+');
    const modifiers = parts.slice(0, -1);
    const key = parts[parts.length - 1];

    let pressString = '';
    if (modifiers.includes('Ctrl')) pressString += 'Control+';
    if (modifiers.includes('Shift')) pressString += 'Shift+';
    if (modifiers.includes('Alt')) pressString += 'Alt+';
    pressString += key;

    await this.page.keyboard.press(pressString);
    await this.waitForUpdate();
  }

  /**
   * Check if command is enabled
   */
  async isCommandEnabled(commandLabel: string): Promise<boolean> {
    const button = this.toolbar.locator(`button:has-text("${commandLabel}")`);
    return await button.isEnabled();
  }

  /**
   * Wait for specific record to appear in grid
   */
  async waitForRecord(recordName: string, timeout = 10000): Promise<void> {
    await this.grid.locator(`text=${recordName}`).waitFor({ timeout });
  }
}
