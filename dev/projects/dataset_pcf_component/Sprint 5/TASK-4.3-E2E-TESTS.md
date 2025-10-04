# TASK-4.3: E2E Tests with Playwright (Reusable PCF Framework)

**Status**: 🚧 IN PROGRESS
**Estimated Time**: 3 hours
**Sprint**: 5 - Universal Dataset PCF Component
**Phase**: 4 - Testing & Quality
**Dependencies**: TASK-4.2 (Integration Tests)

---

## Objective

Create a **reusable Playwright E2E testing framework** for all PCF components, with configuration-driven test scenarios. This framework will support any PCF control we build in the future.

---

## Scope

### In Scope (Reusable Framework)
- ✅ Playwright configuration for Power Apps/Dataverse
- ✅ Reusable PCF page objects and selectors
- ✅ Authentication helpers (Azure AD, service accounts)
- ✅ Dataverse API utilities (setup/teardown test data)
- ✅ PCF lifecycle waiters (init, updateView, render)
- ✅ Screenshot and video capture on failure
- ✅ Configuration-driven test scenarios
- ✅ Cross-browser testing (Chromium, Firefox, WebKit)
- ✅ Accessibility testing with axe-core
- ✅ Network request mocking for offline scenarios

### In Scope (Universal Dataset Grid E2E)
- ✅ 8 critical user workflows
- ✅ Dataverse integration validation
- ✅ Custom command execution (real Custom API calls)
- ✅ Entity configuration loading from JSON
- ✅ Performance testing (large datasets)

### Out of Scope
- Visual regression testing (future enhancement)
- Load testing / stress testing
- Mobile app testing (separate framework)

---

## Architecture: Reusable PCF E2E Framework

### Directory Structure
```
tests/
├── e2e/                              # E2E test framework (REUSABLE)
│   ├── config/
│   │   ├── playwright.config.ts      # Playwright configuration
│   │   ├── test.env.example          # Environment template
│   │   └── pcf-controls.config.json  # PCF control registry
│   ├── fixtures/
│   │   ├── pcf.fixture.ts            # PCF-specific Playwright fixtures
│   │   ├── dataverse.fixture.ts      # Dataverse API fixtures
│   │   └── auth.fixture.ts           # Azure AD auth fixtures
│   ├── pages/
│   │   ├── BasePCFPage.ts            # Base page object for all PCF controls
│   │   ├── PowerAppsPage.ts          # Power Apps environment page
│   │   ├── DataverseFormPage.ts      # Dataverse form page
│   │   └── controls/                 # Control-specific page objects
│   │       └── UniversalDatasetGridPage.ts
│   ├── utils/
│   │   ├── dataverse-api.ts          # Dataverse Web API helpers
│   │   ├── pcf-helpers.ts            # PCF lifecycle utilities
│   │   ├── selectors.ts              # Reusable selector strategies
│   │   └── test-data-factory.ts      # Test data generation
│   └── specs/                         # Test specifications
│       ├── universal-dataset-grid/
│       │   ├── grid-rendering.spec.ts
│       │   ├── commands.spec.ts
│       │   ├── entity-config.spec.ts
│       │   └── performance.spec.ts
│       └── common/
│           ├── accessibility.spec.ts  # Reusable a11y tests
│           └── responsive.spec.ts     # Reusable responsive tests
```

---

## 1. Playwright Configuration (Reusable)

**File**: `tests/e2e/config/playwright.config.ts`

```typescript
import { defineConfig, devices } from '@playwright/test';
import * as dotenv from 'dotenv';

dotenv.config({ path: './tests/e2e/config/.env' });

export default defineConfig({
  testDir: '../specs',

  // Reusable settings for all PCF tests
  timeout: 60000,
  expect: { timeout: 10000 },

  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,

  reporter: [
    ['html', { outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'test-results.json' }],
    ['junit', { outputFile: 'junit.xml' }]
  ],

  use: {
    baseURL: process.env.POWER_APPS_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',

    // Power Apps requires longer timeouts
    actionTimeout: 15000,
    navigationTimeout: 30000
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] }
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] }
    },
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 5'] }
    }
  ],

  webServer: {
    command: 'npm run serve:test-env',
    port: 3000,
    reuseExistingServer: !process.env.CI
  }
});
```

---

## 2. Environment Configuration (Reusable)

**File**: `tests/e2e/config/test.env.example`

```env
# Power Platform Environment
POWER_APPS_URL=https://org.crm.dynamics.com
DATAVERSE_API_URL=https://org.api.crm.dynamics.com/api/data/v9.2

# Azure AD Authentication
TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
CLIENT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
CLIENT_SECRET=xxxxxxxxxxxxxxxxxxxxx

# Test User Credentials (for user-context tests)
TEST_USER_EMAIL=testuser@org.onmicrosoft.com
TEST_USER_PASSWORD=TestPassword123!

# Test Configuration
TEST_TIMEOUT=60000
HEADLESS=true
SLOW_MO=0

# PCF Control Configuration
PCF_CONTROL_NAME=spaarke_UniversalDatasetGrid
PCF_PUBLISHER_PREFIX=spaarke
```

**File**: `tests/e2e/config/pcf-controls.config.json`

```json
{
  "controls": {
    "UniversalDatasetGrid": {
      "namespace": "Spaarke.UI.Components",
      "controlName": "UniversalDatasetGrid",
      "selector": "[data-control-name='spaarke_UniversalDatasetGrid']",
      "supportedEntities": ["account", "contact", "sprk_document"],
      "requiredFeatures": ["create", "read", "update", "delete"],
      "testDataFactory": "createUniversalDatasetTestData"
    },
    "OtherControl": {
      "namespace": "Spaarke.UI.Components",
      "controlName": "OtherControl",
      "selector": "[data-control-name='spaarke_OtherControl']",
      "supportedEntities": ["lead"],
      "requiredFeatures": ["read"],
      "testDataFactory": "createOtherControlTestData"
    }
  }
}
```

---

## 3. Base PCF Page Object (Reusable)

**File**: `tests/e2e/pages/BasePCFPage.ts`

```typescript
import { Page, Locator } from '@playwright/test';
import { PCFControlConfig } from '../config/pcf-controls.config';

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
```

---

## 4. Dataverse API Utilities (Reusable)

**File**: `tests/e2e/utils/dataverse-api.ts`

```typescript
import axios, { AxiosInstance } from 'axios';
import { ClientSecretCredential } from '@azure/identity';

/**
 * Reusable Dataverse Web API client for test data management
 */
export class DataverseAPI {
  private client: AxiosInstance;
  private baseUrl: string;

  constructor(baseUrl: string, accessToken: string) {
    this.baseUrl = baseUrl;
    this.client = axios.create({
      baseURL: baseUrl,
      headers: {
        'Authorization': `Bearer ${accessToken}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        'Accept': 'application/json',
        'Content-Type': 'application/json'
      }
    });
  }

  /**
   * Create Azure AD access token
   */
  static async authenticate(
    tenantId: string,
    clientId: string,
    clientSecret: string,
    resource: string
  ): Promise<string> {
    const credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    const token = await credential.getToken(`${resource}/.default`);
    return token.token;
  }

  /**
   * Create test record
   */
  async createRecord(entityName: string, data: Record<string, any>): Promise<string> {
    const response = await this.client.post(`/${entityName}`, data);
    const recordUrl = response.headers['odata-entityid'];
    const recordId = recordUrl.split('(')[1].split(')')[0];
    return recordId;
  }

  /**
   * Delete test record
   */
  async deleteRecord(entityName: string, recordId: string): Promise<void> {
    await this.client.delete(`/${entityName}(${recordId})`);
  }

  /**
   * Batch delete records (cleanup)
   */
  async deleteRecords(entityName: string, recordIds: string[]): Promise<void> {
    const batch = recordIds.map(id =>
      this.client.delete(`/${entityName}(${id})`)
    );
    await Promise.all(batch);
  }

  /**
   * Query records with FetchXML
   */
  async fetchRecords(entityName: string, fetchXml: string): Promise<any[]> {
    const response = await this.client.get(`/${entityName}`, {
      params: { fetchXml }
    });
    return response.data.value;
  }

  /**
   * Execute Custom API (for testing custom commands)
   */
  async executeCustomAPI(apiName: string, parameters: Record<string, any>): Promise<any> {
    const response = await this.client.post(`/${apiName}`, parameters);
    return response.data;
  }
}
```

---

## 5. Universal Dataset Grid Page Object

**File**: `tests/e2e/pages/controls/UniversalDatasetGridPage.ts`

```typescript
import { Page, Locator } from '@playwright/test';
import { BasePCFPage } from '../BasePCFPage';
import { PCFControlConfig } from '../../config/pcf-controls.config';

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
}
```

---

## 6. Playwright Fixtures (Reusable)

**File**: `tests/e2e/fixtures/pcf.fixture.ts`

```typescript
import { test as base } from '@playwright/test';
import { DataverseAPI } from '../utils/dataverse-api';
import { UniversalDatasetGridPage } from '../pages/controls/UniversalDatasetGridPage';
import controlsConfig from '../config/pcf-controls.config.json';

type PCFFixtures = {
  dataverseAPI: DataverseAPI;
  universalDatasetGrid: UniversalDatasetGridPage;
  testRecords: string[]; // Track created records for cleanup
};

export const test = base.extend<PCFFixtures>({
  // Dataverse API fixture
  dataverseAPI: async ({}, use) => {
    const token = await DataverseAPI.authenticate(
      process.env.TENANT_ID!,
      process.env.CLIENT_ID!,
      process.env.CLIENT_SECRET!,
      process.env.DATAVERSE_API_URL!
    );

    const api = new DataverseAPI(process.env.DATAVERSE_API_URL!, token);
    await use(api);
  },

  // Universal Dataset Grid page object
  universalDatasetGrid: async ({ page }, use) => {
    const config = controlsConfig.controls.UniversalDatasetGrid;
    const gridPage = new UniversalDatasetGridPage(page, config);
    await gridPage.waitForControlInit();
    await use(gridPage);
  },

  // Test records cleanup
  testRecords: async ({ dataverseAPI }, use, testInfo) => {
    const recordIds: string[] = [];

    await use(recordIds);

    // Cleanup after test
    if (recordIds.length > 0) {
      const entityName = testInfo.project.name.includes('account') ? 'accounts' : 'contacts';
      await dataverseAPI.deleteRecords(entityName, recordIds);
    }
  }
});

export { expect } from '@playwright/test';
```

---

## 7. E2E Test Specifications

### Test 1: Grid Rendering
**File**: `tests/e2e/specs/universal-dataset-grid/grid-rendering.spec.ts`

```typescript
import { test, expect } from '../../fixtures/pcf.fixture';

test.describe('Universal Dataset Grid - Rendering', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(`/main.aspx?pagetype=entitylist&etn=account`);
  });

  test('should render grid with records from Dataverse', async ({ universalDatasetGrid }) => {
    await expect(universalDatasetGrid.grid).toBeVisible();

    const recordCount = await universalDatasetGrid.getRecordCount();
    expect(recordCount).toBeGreaterThan(0);
  });

  test('should display correct column headers from view definition', async ({ universalDatasetGrid }) => {
    const headers = await universalDatasetGrid.getColumnHeaders();

    expect(headers).toContain('Account Name');
    expect(headers).toContain('Primary Contact');
  });

  test('should switch between view modes', async ({ universalDatasetGrid }) => {
    await universalDatasetGrid.switchView('List');
    await expect(universalDatasetGrid.page.locator('[data-view-mode="List"]')).toBeVisible();

    await universalDatasetGrid.switchView('Card');
    await expect(universalDatasetGrid.page.locator('[data-view-mode="Card"]')).toBeVisible();
  });
});
```

### Test 2: Command Execution
**File**: `tests/e2e/specs/universal-dataset-grid/commands.spec.ts`

```typescript
import { test, expect } from '../../fixtures/pcf.fixture';

test.describe('Universal Dataset Grid - Commands', () => {
  test('should create new record via toolbar', async ({ page, universalDatasetGrid, dataverseAPI, testRecords }) => {
    await page.goto(`/main.aspx?pagetype=entitylist&etn=account`);

    const initialCount = await universalDatasetGrid.getRecordCount();

    await universalDatasetGrid.clickCommand('New');

    // Power Apps opens form in new context
    const formPage = await page.waitForEvent('popup');
    await formPage.waitForLoadState();

    // Fill form
    await formPage.fill('input[aria-label="Account Name"]', 'E2E Test Account');
    await formPage.click('button[aria-label="Save"]');

    // Wait for save and close
    await formPage.waitForEvent('close');

    // Grid should refresh
    await universalDatasetGrid.waitForUpdate();

    const newCount = await universalDatasetGrid.getRecordCount();
    expect(newCount).toBe(initialCount + 1);
  });

  test('should delete selected records', async ({ page, universalDatasetGrid, dataverseAPI, testRecords }) => {
    // Create test record
    const recordId = await dataverseAPI.createRecord('accounts', {
      name: 'Test Delete Account'
    });
    testRecords.push(recordId);

    await page.goto(`/main.aspx?pagetype=entitylist&etn=account`);
    await universalDatasetGrid.refresh();

    // Select record
    await universalDatasetGrid.selectRow(0);

    // Delete
    await universalDatasetGrid.clickCommand('Delete');

    // Confirm dialog
    await page.click('button:has-text("Delete")');

    // Verify deleted
    await universalDatasetGrid.waitForUpdate();
    const text = await universalDatasetGrid.grid.textContent();
    expect(text).not.toContain('Test Delete Account');
  });

  test('should execute custom command', async ({ page, universalDatasetGrid }) => {
    await page.goto(`/main.aspx?pagetype=entitylist&etn=sprk_document`);

    await universalDatasetGrid.selectRow(0);
    await universalDatasetGrid.clickCommand('Upload to SPE');

    // Wait for custom API execution
    await page.waitForResponse(resp => resp.url().includes('sprk_UploadDocument'));

    // Verify success message
    await expect(page.locator('text=Document uploaded successfully')).toBeVisible();
  });
});
```

### Test 3: Accessibility
**File**: `tests/e2e/specs/common/accessibility.spec.ts`

```typescript
import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.describe('Accessibility - WCAG 2.1 AA', () => {
  test('should pass axe accessibility scan', async ({ page }) => {
    await page.goto(`/main.aspx?pagetype=entitylist&etn=account`);

    const accessibilityScanResults = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();

    expect(accessibilityScanResults.violations).toEqual([]);
  });

  test('should support keyboard navigation', async ({ page }) => {
    await page.goto(`/main.aspx?pagetype=entitylist&etn=account`);

    // Tab to first toolbar button
    await page.keyboard.press('Tab');

    const focusedElement = await page.evaluateHandle(() => document.activeElement);
    const tagName = await focusedElement.evaluate(el => el.tagName);

    expect(tagName).toBe('BUTTON');
  });
});
```

---

## 8. Package.json Scripts

```json
{
  "scripts": {
    "test:e2e": "playwright test",
    "test:e2e:ui": "playwright test --ui",
    "test:e2e:headed": "playwright test --headed",
    "test:e2e:debug": "playwright test --debug",
    "test:e2e:report": "playwright show-report",
    "test:e2e:codegen": "playwright codegen"
  },
  "devDependencies": {
    "@playwright/test": "^1.40.0",
    "@axe-core/playwright": "^4.8.2",
    "@azure/identity": "^4.0.0",
    "axios": "^1.6.2",
    "dotenv": "^16.3.1"
  }
}
```

---

## Success Criteria

✅ **Reusable framework** for all PCF controls
✅ **Configuration-driven** test scenarios
✅ **Dataverse integration** with test data management
✅ **Authentication** via Azure AD service principal
✅ **Cross-browser** testing (Chromium, Firefox, WebKit)
✅ **Accessibility** testing with axe-core
✅ **Screenshot/video** capture on failure
✅ **CI/CD ready** with parallel execution

---

## Deliverables

1. ✅ Playwright configuration for Power Apps
2. ✅ Base PCF page object (reusable)
3. ✅ Dataverse API utilities
4. ✅ Universal Dataset Grid page object
5. ✅ Playwright fixtures for PCF
6. ✅ 8 E2E test scenarios
7. ✅ Accessibility test suite
8. ✅ Documentation for adding new PCF controls

---

## Reusability: Adding New PCF Controls

### Step 1: Register Control
Add to `pcf-controls.config.json`:
```json
{
  "MyNewControl": {
    "namespace": "Spaarke.UI.Components",
    "controlName": "MyNewControl",
    "selector": "[data-control-name='spaarke_MyNewControl']",
    "supportedEntities": ["lead"],
    "requiredFeatures": ["read"],
    "testDataFactory": "createMyNewControlTestData"
  }
}
```

### Step 2: Create Page Object
Extend `BasePCFPage`:
```typescript
export class MyNewControlPage extends BasePCFPage {
  // Add control-specific selectors and methods
}
```

### Step 3: Write Tests
Use reusable fixtures:
```typescript
import { test, expect } from '../../fixtures/pcf.fixture';

test('my new control test', async ({ page, dataverseAPI }) => {
  const config = controlsConfig.controls.MyNewControl;
  const controlPage = new MyNewControlPage(page, config);

  // Test logic
});
```

---

## Timeline

- **Hour 1**: Set up Playwright framework, auth, Dataverse API utilities
- **Hour 2**: Create page objects and fixtures
- **Hour 3**: Write E2E test scenarios, run tests, create documentation
