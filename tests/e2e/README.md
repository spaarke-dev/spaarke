# Reusable PCF E2E Testing Framework

A configuration-driven Playwright framework for end-to-end testing of Power Apps Component Framework (PCF) controls.

## Features

✅ **Reusable for all PCF controls** - Add new controls with minimal configuration
✅ **Power Apps integration** - Handles PCF lifecycle, authentication, Dataverse API
✅ **Cross-browser testing** - Chromium, Firefox, WebKit
✅ **Accessibility testing** - Built-in axe-core integration
✅ **Test data management** - Automated setup/teardown via Dataverse API
✅ **Page Object pattern** - Maintainable, readable tests
✅ **CI/CD ready** - Parallel execution, retries, comprehensive reporting

---

## Quick Start

### 1. Install Dependencies

```bash
npm install
npx playwright install
```

### 2. Configure Environment

Copy `.env.example` to `.env` and configure:

```env
# tests/e2e/config/.env
POWER_APPS_URL=https://yourorg.crm.dynamics.com
DATAVERSE_API_URL=https://yourorg.api.crm.dynamics.com/api/data/v9.2
TENANT_ID=your-tenant-id
CLIENT_ID=your-client-id
CLIENT_SECRET=your-client-secret
```

### 3. Run Tests

```bash
# Run all E2E tests
npm run test:e2e

# Run with UI mode (recommended for development)
npm run test:e2e:ui

# Run in headed mode (see browser)
npm run test:e2e:headed

# Debug mode
npm run test:e2e:debug

# Generate test code (Playwright codegen)
npm run test:e2e:codegen
```

---

## Architecture

### Directory Structure

```
tests/e2e/
├── config/
│   ├── playwright.config.ts      # Playwright configuration
│   ├── .env.example               # Environment template
│   └── pcf-controls.config.json   # PCF control registry
├── pages/
│   ├── BasePCFPage.ts             # Base page object (REUSABLE)
│   └── controls/
│       └── UniversalDatasetGridPage.ts  # Control-specific page
├── utils/
│   └── dataverse-api.ts           # Dataverse API utilities (REUSABLE)
├── specs/
│   └── universal-dataset-grid/    # Test specifications
│       └── grid-rendering.spec.ts
└── README.md                      # This file
```

### Core Components

**1. BasePCFPage** - Reusable base class for all PCF controls
- PCF lifecycle management (init, updateView, refresh)
- Common control interactions
- Screenshot capabilities

**2. DataverseAPI** - Test data management
- Azure AD authentication
- CRUD operations via Web API
- Automated cleanup

**3. Control-specific Page Objects** - Extend BasePCFPage
- Control-specific selectors
- Domain-specific methods

---

## Adding New PCF Controls

### Step 1: Register Control

Add to `config/pcf-controls.config.json`:

```json
{
  "controls": {
    "MyNewControl": {
      "namespace": "Spaarke.UI.Components",
      "controlName": "MyNewControl",
      "selector": "[data-control-name='spaarke_MyNewControl']",
      "supportedEntities": ["account", "contact"],
      "requiredFeatures": ["create", "read"],
      "testDataFactory": "createMyNewControlTestData"
    }
  }
}
```

### Step 2: Create Page Object

```typescript
// pages/controls/MyNewControlPage.ts
import { BasePCFPage, PCFControlConfig } from '../BasePCFPage';
import { Page, Locator } from '@playwright/test';

export class MyNewControlPage extends BasePCFPage {
  readonly myButton: Locator;

  constructor(page: Page, config: PCFControlConfig) {
    super(page, config);
    this.myButton = this.controlRoot.locator('button.my-button');
  }

  async clickMyButton(): Promise<void> {
    await this.myButton.click();
    await this.waitForUpdate();
  }
}
```

### Step 3: Write Tests

```typescript
// specs/my-new-control/my-test.spec.ts
import { test, expect } from '@playwright/test';
import { MyNewControlPage } from '../../pages/controls/MyNewControlPage';
import controlsConfig from '../../config/pcf-controls.config.json';

test.describe('My New Control', () => {
  test('should do something', async ({ page }) => {
    await page.goto('/main.aspx?pagetype=entitylist&etn=account');

    const config = controlsConfig.controls.MyNewControl;
    const controlPage = new MyNewControlPage(page, config);

    await controlPage.waitForControlInit();
    await controlPage.clickMyButton();

    // Assertions...
  });
});
```

---

## Test Patterns

### PCF Control Initialization

```typescript
test('example test', async ({ page }) => {
  // Navigate to Power Apps page
  await page.goto('/main.aspx?pagetype=entitylist&etn=account');

  // Initialize control page object
  const config = controlsConfig.controls.UniversalDatasetGrid;
  const gridPage = new UniversalDatasetGridPage(page, config);

  // Wait for PCF control to initialize
  await gridPage.waitForControlInit();

  // Test interactions...
});
```

### Test Data Management

```typescript
import { DataverseAPI } from '../../utils/dataverse-api';

test('example with test data', async ({ page }) => {
  // Authenticate with Dataverse
  const token = await DataverseAPI.authenticate(
    process.env.TENANT_ID!,
    process.env.CLIENT_ID!,
    process.env.CLIENT_SECRET!,
    process.env.DATAVERSE_API_URL!
  );

  const api = new DataverseAPI(process.env.DATAVERSE_API_URL!, token);

  // Create test record
  const recordId = await api.createRecord('accounts', {
    name: 'Test Account'
  });

  try {
    // Run test...
    await page.goto('/main.aspx?...');
    // Verify record appears in grid...
  } finally {
    // Cleanup
    await api.deleteRecord('accounts', recordId);
  }
});
```

### Accessibility Testing

```typescript
import AxeBuilder from '@axe-core/playwright';

test('should pass accessibility scan', async ({ page }) => {
  await page.goto('/main.aspx?pagetype=entitylist&etn=account');

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .analyze();

  expect(results.violations).toEqual([]);
});
```

---

## Configuration

### Playwright Config

Key settings in `playwright.config.ts`:

```typescript
{
  timeout: 60000,              // Test timeout
  actionTimeout: 15000,        // Action timeout (Power Apps needs longer)
  navigationTimeout: 30000,    // Navigation timeout
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined
}
```

### Browser Projects

```typescript
projects: [
  { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  { name: 'webkit', use: { ...devices['Desktop Safari'] } }
]
```

Run specific browser:
```bash
npx playwright test --project=chromium
```

---

## Best Practices

### 1. Use Page Objects
✅ Encapsulate selectors and interactions
✅ Extend `BasePCFPage` for PCF controls
✅ Keep tests focused on business logic

### 2. Wait for PCF Lifecycle
```typescript
await gridPage.waitForControlInit();  // After navigation
await gridPage.waitForUpdate();       // After actions
```

### 3. Clean Up Test Data
```typescript
test.afterEach(async ({ dataverseAPI, testRecords }) => {
  await dataverseAPI.deleteRecords('accounts', testRecords);
});
```

### 4. Use Semantic Locators
```typescript
// ✅ Good
await page.locator('[role="button"][aria-label="New"]').click();

// ❌ Bad
await page.locator('.ms-button-123').click();
```

### 5. Handle Power Apps Timing
```typescript
// Power Apps opens forms in popups
const formPage = await page.waitForEvent('popup');
await formPage.waitForLoadState();
```

---

## Debugging

### Visual Debugging
```bash
npm run test:e2e:headed  # See browser
npm run test:e2e:debug   # Step through tests
```

### Playwright Inspector
```bash
npm run test:e2e:debug
# Sets PWDEBUG=1, opens Playwright Inspector
```

### Screenshots & Videos
Configured to capture on failure:
- Screenshots: `playwright-report/`
- Videos: `test-results/`
- Traces: `test-results/`

### View Reports
```bash
npm run test:e2e:report
```

---

## CI/CD Integration

### GitHub Actions Example

```yaml
name: E2E Tests

on: [push, pull_request]

jobs:
  e2e:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
      - run: npm ci
      - run: npx playwright install --with-deps
      - run: npm run test:e2e
        env:
          POWER_APPS_URL: ${{ secrets.POWER_APPS_URL }}
          TENANT_ID: ${{ secrets.TENANT_ID }}
          CLIENT_ID: ${{ secrets.CLIENT_ID }}
          CLIENT_SECRET: ${{ secrets.CLIENT_SECRET }}
      - uses: actions/upload-artifact@v3
        if: always()
        with:
          name: playwright-report
          path: playwright-report/
```

---

## Troubleshooting

### Issue: Control not found
**Solution**: Verify selector in `pcf-controls.config.json` matches deployed control

### Issue: Authentication fails
**Solution**: Check Azure AD app registration has Dataverse API permissions

### Issue: Tests timeout
**Solution**: Increase timeouts in `playwright.config.ts` for Power Apps

### Issue: Flaky tests
**Solution**: Use explicit waits (`waitForControlInit`, `waitForUpdate`)

---

## Resources

- [Playwright Documentation](https://playwright.dev)
- [PCF Documentation](https://docs.microsoft.com/power-apps/developer/component-framework/)
- [Dataverse Web API](https://docs.microsoft.com/power-apps/developer/data-platform/webapi/overview)
- [axe-core Accessibility](https://github.com/dequelabs/axe-core)

---

## Support

For issues or questions:
1. Check existing tests in `specs/` for examples
2. Review `BasePCFPage` methods for available utilities
3. Consult Playwright documentation for advanced scenarios
4. Use Playwright codegen for generating selectors
