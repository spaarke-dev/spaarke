# Task Index: MDA Dark Mode Theme Toggle

> **Last Updated**: December 5, 2025
> **Format**: POML (valid XML documents)

## Status Legend
- ⬜ Not Started
- 🔄 In Progress
- ✅ Complete
- ⏸️ Blocked

## Phase 1: Shared Infrastructure (4h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [001](./001-create-theme-storage.poml) | Create themeStorage.ts utilities | ✅ | 2h | None |
| [002](./002-create-theme-menu-js.poml) | Create sprk_ThemeMenu.js web resource | ✅ | 1h | 001 |
| [003](./003-create-svg-icons.poml) | Create SVG icons (4 icons) | ✅ | 1h | None |

## Phase 2: PCF Control Updates (7h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [010](./010-update-spefileviewer.poml) | Update SpeFileViewer to use shared theme | ✅ | 3h | 001 |
| [011](./011-update-universaldatasetgrid.poml) | Update UniversalDatasetGrid theme support | ✅ | 2h | 001 |
| [012](./012-update-universalquickcreate.poml) | Update UniversalQuickCreate theme support | ✅ | 2h | 001 |

## Phase 3: Ribbon Configuration (3h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [020](./020-configure-ribbon-flyout.poml) | Configure flyout menu via Ribbon Workbench | ✅ | 3h | 002, 003 |

## Phase 4: Documentation & Testing (5h)

| ID | Title | Status | Est | Dependencies |
|----|-------|--------|-----|--------------|
| [030](./030-update-documentation.poml) | Update sdap-pcf-patterns.md with theming | ⬜ | 1h | 001 |
| [031](./031-integration-testing.poml) | Integration testing (all controls) | ⬜ | 3h | 010, 011, 012, 020 |
| [032](./032-deploy-to-dev.poml) | Deploy to DEV environment | ⬜ | 1h | 031 |

## Summary

| Phase | Tasks | Hours | Status |
|-------|-------|-------|--------|
| Phase 1: Shared Infrastructure | 3 | 4h | ✅ |
| Phase 2: PCF Control Updates | 3 | 7h | ✅ |
| Phase 3: Ribbon Configuration | 1 | 3h | ✅ |
| Phase 4: Documentation & Testing | 3 | 5h | ⬜ |
| **Total** | **10** | **19h** | 🔄 |

## Execution Order (Recommended)

Start with tasks that have no dependencies:
1. **001** - themeStorage.ts (unlocks 002, 010, 011, 012, 030)
2. **003** - SVG icons (unlocks 020)

Then follow dependency chain:
3. **002** - Theme menu JS (unlocks 020)
4. **010, 011, 012** - PCF updates (can run in parallel)
5. **020** - Ribbon configuration
6. **030** - Documentation
7. **031** - Integration testing
8. **032** - Deployment

## Critical Path

```
001 → 002 → 020 → 031 → 032
  ↘ 010 ↗
  ↘ 011 ↗
  ↘ 012 ↗
003 → 020
```
