# PCF Bundle Size Baseline

> **Baseline date**: 2026-05-14
> **Branch**: `work/ai-procedure-quality-r1`
> **Source**: committed `bundle.js` files under `src/client/pcf/*/[Ss]olution/Controls/*/`
> **Purpose**: known-good reference for `scripts/quality/Check-BundleSizeDrift.ps1` (task 063)
> **Tolerance**: 20% (default; per-bundle override possible in the JSON)
> **Motivation**: the 2026-05-14 SDV regression (6.7 MB vs 440 KB), caught only because the user pushed back.

---

## Summary

- **PCF folders found**: 25 total under `src/client/pcf/`
- **PCFs with own `package.json`**: 14
- **PCFs with committed `bundle.js`**: 10 PCFs producing **11 bundles** (UniversalDatasetGrid has two namespaces — one legacy, one current)
- **Total committed bundle bytes**: 18,087,694 (~17.25 MB) — but **13.1 MB of that is the stale legacy UniversalDatasetGrid bundle** scheduled for removal
- **PCFs with WRONG `build:prod` script** (3): DocumentRelationshipViewer, EmailProcessingMonitor, ThemeEnforcer — all missing the script entirely

---

## Committed Bundles

| # | Control | Size (bytes) | Size (KB) | Last Commit | Date | build:prod OK |
|---|---|---:|---:|---|---|---|
| 1 | AssociationResolver | 401,824 | 392 | `bc404e98` | 2026-05-13 | YES |
| 2 | DocumentRelationshipViewer | 603,308 | 589 | `651b8dd7` | 2026-05-13 | **NO (missing)** |
| 3 | EmailProcessingMonitor | 418,170 | 408 | `2e2fdd49` | 2026-05-13 | **NO (missing)** |
| 4 | RelatedDocumentCount | 432,603 | 422 | `ef57fc3f` | 2026-05-13 | YES |
| 5 | SemanticSearchControl | 539,042 | 526 | `ef57fc3f` | 2026-05-13 | YES |
| 6 | SpaarkeGridCustomizer | 12,160 | 12 | `b3f16818` | 2026-02-03 | YES (but stale & tiny — verify still used) |
| 7 | SpeDocumentViewer | 440,821 | 430 | `c132773c` | 2026-05-14 | YES (reference good build — post-fix) |
| 8 | UniversalDatasetGrid (legacy) | 13,138,510 | 12,830 | `6366335a` | 2026-02-06 | YES (but **STALE 13.1 MB** — candidate for removal) |
| 9 | UniversalDatasetGrid (current) | 446,839 | 436 | `3d3df3fb` | 2026-05-13 | YES |
| 10 | UpdateRelatedButton | 1,108,293 | 1,082 | `b3f16818` | 2026-02-03 | YES (but on the high side — predates production-flag fix) |
| 11 | VisualHost | 546,024 | 533 | `999614ed` | 2026-05-13 | YES |

### Healthy size range

Most production PCFs land in **400 – 600 KB**. That's the baseline. Anything outside that should be examined.

| Range | Count | Notes |
|---|---:|---|
| < 50 KB | 1 | SpaarkeGridCustomizer (stub?) |
| 400–650 KB | 8 | Healthy band |
| 1.0–1.5 MB | 1 | UpdateRelatedButton (pre-fix, may shrink on rebuild) |
| > 10 MB | 1 | UniversalDatasetGrid legacy (remove) |

---

## PCFs with Wrong `build:prod` (FUTURE TASK)

Three PCFs have **NO** `build:prod` script. They only define `build` (which silently produces a dev-mode bundle 5-10× larger). All three already have committed bundles, so those bundles were almost certainly built in dev mode.

| Control | Has bundle? | Issue |
|---|---|---|
| DocumentRelationshipViewer | Yes (603 KB) | `package.json` missing `build:prod` — current bundle likely dev mode |
| EmailProcessingMonitor | Yes (418 KB) | `package.json` missing `build:prod` |
| ThemeEnforcer | No | `package.json` missing `build:prod`; deploy will be a dev-mode bundle when it happens |

**Recommended follow-up task**: Add `"build:prod": "pcf-scripts build --buildMode production"` to each, rebuild, verify size reduction, recommit.

---

## PCF Folders WITHOUT a Committed Bundle

| Folder | Has package.json? | build:prod | Note |
|---|---|---|---|
| AIMetadataExtractor | No | — | Not an independent PCF solution |
| AnalysisBuilder | No | — | Not an independent PCF solution |
| AnalysisWorkspace | No | — | Not an independent PCF solution |
| DrillThroughWorkspace | Yes | Correct | Built but not deployed yet |
| DueDatesWidget | No | — | Not an independent PCF solution |
| EventAutoAssociate | No | — | Not an independent PCF solution |
| EventCalendarFilter | No | — | Not an independent PCF solution |
| EventFormController | No | — | Not an independent PCF solution |
| PlaybookBuilderHost | No | — | Not an independent PCF solution |
| RegardingLink | No | — | Not an independent PCF solution |
| ScopeConfigEditor | Yes | Correct | Built but not deployed yet |
| SpeFileViewer | No | — | Not an independent PCF solution |
| ThemeEnforcer | Yes | **MISSING** | See "Wrong build:prod" above |
| UniversalQuickCreate | Yes | Correct | Built but not deployed yet |

---

## Consumer

The JSON sibling (`pcf-bundle-baseline.json`) is the machine-readable feed for task 063 `scripts/quality/Check-BundleSizeDrift.ps1`. That validator will:

1. Glob every `bundle.js` in the working tree
2. Compare actual size against `expected_size_bytes` in the JSON
3. Warn if the delta exceeds `tolerance_percent` (default 20%)
4. Hard-fail if the delta exceeds 200% (the SDV-class blow-up case)

Stale bundles (legacy UniversalDatasetGrid, 2026-02 SpaarkeGridCustomizer, UpdateRelatedButton) should be either pruned from the baseline before task 063 lands or marked `ignore: true` so they don't trigger noise.

---

## Acceptance Criteria (from task 005)

- [x] Every committed PCF bundle.js is in the baseline → **Yes, 11 entries**
- [x] JSON format consumable by `Check-BundleSizeDrift.ps1` → **Yes (`path`, `expected_size_bytes`, `tolerance_percent` on every entry)**
- [x] Any PCF with the wrong build:prod flag is flagged → **Yes, 3 listed in `wrong_build_prod_summary`**
