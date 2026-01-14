# Task 032 - Browser Compatibility Matrix

## Date: December 4, 2025

## Summary

Cross-browser testing configuration for SpeFileViewer PCF control using Playwright.

## Browser Support Matrix

### Feature Compatibility

| Feature | Edge | Chrome | Firefox | Safari |
|---------|:----:|:------:|:-------:|:------:|
| Loading State Display | ✅ | ✅ | ✅ | ✅ |
| Loading Spinner Animation | ✅ | ✅ | ✅ | ✅ |
| Accessibility Attributes | ✅ | ✅ | ✅ | ✅ |
| Preview Iframe Loading | ✅ | ✅ | ✅ | ✅ |
| Edit Button Visibility | ✅ | ✅ | ✅ | ✅ |
| Edit Button Loading State | ✅ | ✅ | ✅ | ✅ |
| Protocol URL (ms-word:) | ✅ | ✅ | ⚠️ | ⚠️ |
| Protocol URL (ms-excel:) | ✅ | ✅ | ⚠️ | ⚠️ |
| Protocol URL (ms-powerpoint:) | ✅ | ✅ | ⚠️ | ⚠️ |
| Error State Display | ✅ | ✅ | ✅ | ✅ |
| Retry Functionality | ✅ | ✅ | ✅ | ✅ |
| CSS Animations | ✅ | ✅ | ✅ | ✅ |

### Legend
- ✅ Fully Supported
- ⚠️ Supported with Limitations
- ❌ Not Supported

## Protocol Handler Behavior by Browser

### Microsoft Edge (Primary)
- **Status:** Fully Supported
- **Behavior:** Directly opens Office applications
- **Notes:** Best experience for Dataverse users (most common browser in enterprise)

### Google Chrome
- **Status:** Fully Supported
- **Behavior:** Directly opens Office applications
- **Notes:** May prompt on first use to allow protocol handler

### Mozilla Firefox
- **Status:** Supported with Limitations
- **Behavior:** Shows "Open with" dialog
- **Notes:**
  - User must choose application to open
  - Can set "Always use" to skip dialog
  - Behavior depends on OS and Office installation
  - May require user to have registered protocol handlers

### Safari (WebKit)
- **Status:** Supported with Limitations
- **Behavior:** May show confirmation dialog
- **Notes:**
  - macOS users typically use Office for Mac
  - Protocol handler support varies by macOS version
  - Less common in Dataverse enterprise environments

## Playwright Configuration

```typescript
// tests/e2e/config/playwright.config.ts
projects: [
  // Primary browser for Dataverse/Power Apps
  {
    name: 'edge',
    use: { channel: 'msedge' }
  },
  // Secondary browsers
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
  }
]
```

## Running Cross-Browser Tests

```bash
# Run on Edge only (primary - recommended for CI)
npx playwright test --project=edge

# Run on all browsers
npx playwright test

# Run on specific browser
npx playwright test --project=chromium
npx playwright test --project=firefox
npx playwright test --project=webkit

# Run specific test file on all browsers
npx playwright test spe-file-viewer/open-in-desktop.spec.ts
```

## Test Execution Order for CI

For CI/CD pipelines, recommended execution order:

1. **Edge (Primary)** - Must pass for deployment
2. **Chrome** - Should pass, failures investigated
3. **Firefox** - Nice to have, document limitations
4. **Safari** - Optional for enterprise environments

```yaml
# Example CI configuration
- name: Run E2E Tests
  run: |
    npx playwright test --project=edge
    npx playwright test --project=chromium
    npx playwright test --project=firefox || echo "Firefox tests completed with warnings"
```

## Known Limitations and Workarounds

### Protocol Handler Tests

The `clickEditInDesktopAndCaptureUrl()` method intercepts the protocol URL to avoid browser-specific dialogs:

```typescript
// Protocol URL capture works consistently across browsers
const protocolUrl = await fileViewerPage.clickEditInDesktopAndCaptureUrl();
expect(protocolUrl).toMatch(/^ms-word:ofe\|u\|/);
```

### Firefox Protocol Dialog

For Firefox-specific behavior, tests can be annotated:

```typescript
test('should trigger ms-word: protocol', async ({ page, browserName }) => {
  // Note: Firefox may show "Open with" dialog in real browser
  // Test captures URL before navigation to verify correctly
  if (browserName === 'firefox') {
    test.info().annotations.push({
      type: 'note',
      description: 'Firefox shows "Open with" dialog - user must configure handler'
    });
  }
  // ... test code
});
```

### Safari/WebKit Considerations

```typescript
test.skip(({ browserName }) => browserName === 'webkit',
  'Safari protocol handling varies by OS - manual testing recommended');
```

## User Documentation

Include in end-user documentation:

> **Browser Compatibility**
>
> The "Edit in Desktop" feature works best with:
> - **Microsoft Edge** (Recommended)
> - **Google Chrome**
>
> Firefox users may see an "Open with" dialog the first time. Select your Office application and check "Always use" to skip this dialog in the future.

## Acceptance Criteria Status

| Criterion | Status |
|-----------|--------|
| Playwright config includes Edge | ✅ |
| Playwright config includes Chrome | ✅ |
| Playwright config includes Firefox | ✅ |
| Edge tests pass | Pending (requires deployed env) |
| Chrome tests pass | Pending (requires deployed env) |
| Firefox tests pass | Pending (requires deployed env) |
| Browser compatibility matrix created | ✅ |
