# Task Index: SDAP FileViewer Enhancements 1

> **Last Updated**: December 4, 2025
> **Format**: POML (valid XML documents)

## Status Legend
- ⬜ Not Started
- 🔄 In Progress
- ✅ Complete
- ⏸️ Blocked

## Phase 1: BFF Endpoint Development (9h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [001](./001-create-desktop-url-builder.poml) | Create DesktopUrlBuilder utility | ✅ | 2h | None |
| [002](./002-implement-open-links-endpoint.poml) | Implement /open-links endpoint | ✅ | 3h | 001 |
| [003](./003-add-endpoint-auth-filter.poml) | Add endpoint authorization filter | ✅ | 2h | 002 |
| [004](./004-write-endpoint-unit-tests.poml) | Write unit tests for endpoint | ✅ | 2h | 002 |

## Phase 2: FileViewer PCF Updates (13h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [010](./010-implement-loading-state-machine.poml) | Implement loading state machine | ✅ | 2h | None |
| [011](./011-create-loading-overlay.poml) | Create loading overlay component | ✅ | 2h | 010 |
| [012](./012-update-render-logic.poml) | Update render logic (iframe onload) | ✅ | 3h | 011 |
| [013](./013-remove-embedded-edit-mode.poml) | Remove embedded edit mode | ✅ | 1h | None |
| [014](./014-add-desktop-edit-button.poml) | Add "Open in Desktop" button | ✅ | 2h | 002 |
| [015](./015-integrate-open-links-api.poml) | Integrate with open-links endpoint | ✅ | 2h | 014, 002 |
| [016](./016-add-loading-css.poml) | Add CSS for loading states | ✅ | 1h | 011 |

## Phase 3: Performance Enhancements (4h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [020](./020-enable-always-on.poml) | Enable App Service Always On | ✅ | 1h | None |
| [021](./021-add-ping-endpoint.poml) | Add /ping warm-up endpoint | ✅ | 1h | None |
| [022](./022-move-preview-fetch.poml) | Move preview URL fetch to init() | ✅ | 1h | 012 |
| [023](./023-verify-graph-singleton.poml) | Verify Graph client is singleton | ✅ | 1h | None |

## Phase 4: Testing & Validation (9h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [030](./030-integration-testing-bff.poml) | Integration testing - BFF | ✅ | 3h | 004 |
| [031](./031-e2e-testing-fileviewer.poml) | E2E testing - FileViewer | ✅ | 3h | 015, 016 |
| [032](./032-cross-browser-testing.poml) | Cross-browser testing | ✅ | 1.5h | 031 |
| [033](./033-performance-validation.poml) | Performance validation | ✅ | 1.5h | 020, 021, 022 |

## Phase 5: Deployment (4h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [040](./040-deploy-to-test.poml) | Deploy to test environment | ✅ | 1.5h | 030, 031 |
| [041](./041-pilot-validation.poml) | Pilot validation | ✅ | 1.5h | 040 |
| [042](./042-production-deployment.poml) | Production deployment | ✅ | 1h | 041 |

## Summary

| Phase | Tasks | Hours | Status |
|-------|-------|-------|--------|
| Phase 1: BFF Endpoint | 4 | 9h | ✅ |
| Phase 2: PCF Updates | 7 | 13h | ✅ |
| Phase 3: Performance | 4 | 4h | ✅ |
| Phase 4: Testing | 4 | 9h | ✅ |
| Phase 5: Deployment | 3 | 4h | ✅ |
| **Total** | **22** | **39h** | ✅ |

## Execution Order (Recommended)

Start with tasks that have no dependencies:
1. **001** - DesktopUrlBuilder (unlocks 002)
2. **010** - Loading state machine (unlocks 011)
3. **013** - Remove embedded edit (independent)
4. **020, 021, 023** - Performance tasks (independent)

Then follow dependency chain.
