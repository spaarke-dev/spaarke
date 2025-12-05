# Task 031 Implementation Notes

## Date: December 4, 2025

## Summary

**Status: Test Framework Created (Requires Deployed Environment to Execute)**

Created comprehensive Playwright E2E test suite for SpeFileViewer PCF control. Tests are ready but require a deployed test environment with test data to execute.

## Files Created

### 1. Control Configuration
**File:** `tests/e2e/config/pcf-controls.config.json`

Added SpeFileViewer control registration:
```json
{
  "SpeFileViewer": {
    "namespace": "Spaarke.UI.Components",
    "controlName": "SpeFileViewer",
    "selector": "[data-control-name='spaarke_SpeFileViewer']",
    "supportedEntities": ["sprk_document"],
    "requiredFeatures": ["read"],
    "testDataFactory": "createSpeFileViewerTestData"
  }
}
```

### 2. Page Object
**File:** `tests/e2e/pages/controls/SpeFileViewerPage.ts`

| Feature | Description |
|---------|-------------|
| State detection | `getState()` - Returns Loading, Ready, or Error |
| Loading state | `waitForLoadingState()` - Waits for loading overlay |
| Preview ready | `waitForPreviewReady()` - Waits for iframe to load |
| Edit button | `clickEditInDesktopAndCaptureUrl()` - Captures protocol URL |
| Error handling | `waitForErrorState()`, `clickRetry()` |
| Accessibility | `verifyLoadingAccessibility()`, `verifyButtonAccessibility()` |

### 3. Test Specifications

#### Loading States (`loading-states.spec.ts`)

| Test | Acceptance Criterion |
|------|---------------------|
| Loading state within 500ms | Loading overlay appears within 500ms |
| Accessibility attributes | role="status", aria-busy="true" |
| Ready within 10 seconds | Preview loads within timeout |
| Loading text display | "Loading" text shown |
| Slow network handling | Loading persists during delay |

#### Open in Desktop (`open-in-desktop.spec.ts`)

| Test | Acceptance Criterion |
|------|---------------------|
| Edit button visible | Button shows when preview ready |
| Word protocol URL | ms-word:ofe\|u\| format |
| Excel protocol URL | ms-excel:ofe\|u\| format |
| PowerPoint protocol URL | ms-powerpoint:ofe\|u\| format |
| URL encoding | Web URL properly encoded |
| Unsupported types | Edit disabled for PDF |
| Loading during fetch | Button shows loading state |
| Error state display | Error container visible on failure |
| Retry functionality | Refetches on retry click |

## Test Execution Requirements

### Prerequisites

1. **Deployed PCF Control**
   - SpeFileViewer must be deployed to Dataverse test environment
   - Control must be configured on sprk_document entity form

2. **Test Data**
   - sprk_document records with associated files:
     - Word document (.docx)
     - Excel document (.xlsx)
     - PowerPoint document (.pptx)
     - PDF document (.pdf)

3. **Environment Configuration**
   ```env
   # tests/e2e/config/.env
   POWER_APPS_URL=https://yourorg.crm.dynamics.com
   DATAVERSE_API_URL=https://yourorg.api.crm.dynamics.com/api/data/v9.2
   TENANT_ID=your-tenant-id
   CLIENT_ID=your-client-id
   CLIENT_SECRET=your-client-secret
   ```

4. **Test Document GUIDs**
   - Update `TEST_DOCUMENTS` object in `open-in-desktop.spec.ts` with actual document IDs

### Running Tests

```bash
# Install Playwright browsers
npx playwright install

# Run all FileViewer tests
npx playwright test spe-file-viewer --headed

# Run specific test file
npx playwright test spe-file-viewer/loading-states.spec.ts --headed

# Run with debug mode
npx playwright test spe-file-viewer --debug
```

## Protocol URL Capture Strategy

The `clickEditInDesktopAndCaptureUrl()` method intercepts `window.location.href` assignments to capture protocol URLs without actually triggering browser navigation:

```typescript
// Intercept window.location.href assignment
await this.page.evaluate(() => {
  const originalDescriptor = Object.getOwnPropertyDescriptor(window, 'location');
  Object.defineProperty(window, 'location', {
    set: (url: string) => {
      if (url.startsWith('ms-word:') || url.startsWith('ms-excel:') || ...) {
        (window as any).__capturedProtocolUrl = url;
        return; // Don't navigate
      }
      originalDescriptor.set.call(window, url);
    }
  });
});
```

This approach:
- Captures the protocol URL for verification
- Prevents actual navigation (which would fail in browser)
- Restores original behavior after test

## Acceptance Criteria Status

| Criterion | Status | Notes |
|-----------|--------|-------|
| Loading state within 500ms | Test created | Requires env |
| Preview loads within 10s | Test created | Requires env |
| Edit button visible | Test created | Requires env |
| Protocol URL triggered | Test created | Uses capture strategy |
| Error state display | Test created | Uses route interception |
| All tests pass | Pending | Requires deployed env |

## Recommendations

1. **Test Environment Setup**
   - Create dedicated test environment for E2E tests
   - Use Dataverse API to create test records programmatically
   - Consider fixture-based test data management

2. **CI/CD Integration**
   - Configure GitHub Actions with environment secrets
   - Run tests after deployment to test environment
   - Use retries for flaky browser tests

3. **Test Data Management**
   - Create utility to set up/tear down test documents
   - Use unique naming to avoid conflicts
   - Clean up after test runs

## Next Steps

- [ ] Deploy SpeFileViewer to test environment
- [ ] Create test sprk_document records
- [ ] Configure .env with credentials
- [ ] Update TEST_DOCUMENTS with actual GUIDs
- [ ] Run tests to validate
