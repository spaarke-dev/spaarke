import { Page, Locator } from '@playwright/test';

export interface PCFControlConfig {
  namespace: string;
  controlName: string;
  selector: string;
  supportedEntities: string[];
  requiredFeatures: string[];
  testDataFactory: string;
}

/**
 * Base Page Object for all PCF controls
 * Provides reusable methods for PCF lifecycle and common interactions
 */
export class BasePCFPage {
  readonly page: Page;
  readonly config: PCFControlConfig;
  readonly controlRoot: Locator;

  constructor(page: Page, config: PCFControlConfig) {
    this.page = page;
    this.config = config;
    this.controlRoot = page.locator(config.selector);
  }

  /**
   * Wait for PCF control to initialize
   */
  async waitForControlInit(timeout = 30000): Promise<void> {
    // Wait for control root element
    await this.controlRoot.waitFor({ state: 'attached', timeout });

    // Wait for PCF init to complete (check for loading spinner to disappear)
    await this.page.waitForFunction(
      (selector) => {
        const control = document.querySelector(selector);
        return control && !control.querySelector('[role="progressbar"]');
      },
      this.config.selector,
      { timeout }
    );

    // Additional wait for framework initialization
    await this.page.waitForTimeout(500);
  }

  /**
   * Wait for PCF updateView to complete
   */
  async waitForUpdate(timeout = 10000): Promise<void> {
    // Wait for any loading indicators to disappear
    await this.page.waitForFunction(
      (selector) => {
        const control = document.querySelector(selector);
        const spinner = control?.querySelector('[role="progressbar"]');
        return !spinner || spinner.getAttribute('aria-hidden') === 'true';
      },
      this.config.selector,
      { timeout }
    );
  }

  /**
   * Get control property value (via browser console)
   */
  async getControlProperty(propertyName: string): Promise<any> {
    return await this.page.evaluate(
      ({ selector, prop }) => {
        const controlElement = document.querySelector(selector) as any;
        return controlElement?.__pcfControl?.[prop];
      },
      { selector: this.config.selector, prop: propertyName }
    );
  }

  /**
   * Trigger PCF refresh
   */
  async refresh(): Promise<void> {
    await this.page.evaluate((selector) => {
      const controlElement = document.querySelector(selector) as any;
      controlElement?.__pcfControl?.refresh?.();
    }, this.config.selector);

    await this.waitForUpdate();
  }

  /**
   * Take screenshot of control only
   */
  async screenshotControl(path: string): Promise<void> {
    await this.controlRoot.screenshot({ path });
  }
}
