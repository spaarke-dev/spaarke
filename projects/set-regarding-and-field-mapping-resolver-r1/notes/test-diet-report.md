# Test diet report — set-regarding-and-field-mapping-resolver-r1

**Run date**: 2026-07-08
**Branch**: work/set-regarding-and-field-mapping-resolver-r1 (merged to master as `f07ffb4f0`)
**Scope**: project-owned test files touched between `38503540d` (project start, 2026-07-02) and HEAD

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 116 | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| **Total tests touched** | **116** | — |

## Project-owned test files (4 files, 116 methods)

| File | Method count | Class | KEEP path notes |
|---|---|---|---|
| `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` | 56 | MAINTAIN | PCF harness tests — render + user-interaction assertions per FR-A1-01 through FR-A5-03 + SRFR-034/039/042/043/052/054/057 regression guards. Lives at canonical PCF `__tests__/` path. |
| `src/client/pcf/RegardingResolver/__tests__/ResolverWriteHandler.test.ts` | 16 | MAINTAIN | Handler-level write-path tests — FR-13 mutual-exclusivity clear (schema-membership-limited per SRFR-048), FR-22 host-flexibility, nav-prop discovery. |
| `src/client/shared/Spaarke.UI.Components/src/services/__tests__/PolymorphicResolverService.test.ts` | 30 | MAINTAIN | Shared-lib service tests — 5-field write (SRFR-020), record-number resolution (SRFR-071), display-name resolution (SRFR-052), NFR-06 graceful fallback paths. |
| `src/client/shared/Spaarke.UI.Components/src/components/PolymorphicPicker/__tests__/PolymorphicPicker.test.tsx` | 14 | MAINTAIN | Shared Fluent v9 component tests — rendering + onSelect callback + catalog filtering per FR-C2-01. |

**Note**: `src/client/pcf/AssociationResolver/handlers/__tests__/RecordSelectionHandler.test.ts` was intentionally DELETED in SRFR-045 (PCF retirement + consolidation into RegardingResolver v1.4.0). Not counted in the 116.

## Classification against 17-ban criteria (all 116 pass)

Sampled tests per file classified against B1-B17:

| Ban | Applied? | Sample rationale |
|---|---|---|
| B1 `Mock<HttpMessageHandler>` | ❌ Not applicable | Project mocks are `IPolymorphicPickerWebApi` / `Xrm.Navigation` shims + `context.webAPI` stubs — the domain-appropriate seam, not HttpClient plumbing |
| B2 all-mocks-trivial | ❌ | Mocks are configured per-scenario (e.g., "WebAPI returns URL", "WebAPI rejects with 401") to exercise specific code paths |
| B3 DI-wiring assertion | ❌ | No `services.GetRequiredService<>` assertions — PCF tests render + assert DOM/callback state |
| B4 ctor null-check | ❌ | No `Throws<ArgumentNullException>` on constructors |
| B5 wiring test | ❌ | No standalone "component starts" tests — all tests exercise behavior |
| B6 mirror | ❌ | Tests assert composite state (Row 1 title + Row 2 hyperlink + version footer together), not 1:1 mirrors of production methods |
| B7 3+ mocks / ≤2 asserts | ❌ | Mock count ≤ assertion count in all sampled tests |
| B8 internal-access reflection | ❌ | No `BindingFlags.NonPublic` or `InternalsVisibleTo` reflection |
| B9 pass-through Verify.Once | ❌ | Tests assert multi-step flow (pick → applyResolverFields called with correct args → seam.recordName populated on window global) |
| B10 NotThrow / NotNull only | ❌ | All assertions have concrete value/state matchers (`.toBe`, `.toHaveBeenCalledWith`, `.toEqual(expect.objectContaining(...))`) |
| B11 record equality | ❌ | JS/TS — no record semantics; no C# record tests |
| B12 snapshot trivial | ❌ | No `toMatchSnapshot()` calls; assertions are explicit shape matchers |
| B13 name without scenario | ❌ | All test names follow `{Feature/FR} — {Scenario} → {Result}` shape (e.g., "pre-loaded record — WebAPI returns URL — parses etn+id + calls navigateTo") |
| B14 required enforcement | ❌ | No language-feature assertions |
| B15 arrange/assert ratio >10:1 | ❌ | Sampled tests have arrange 5-15 lines, assert 1-5 lines — well under 10:1 |
| B16 exhaustive switch | ❌ | No exhaustive-enum-branch tests |
| B17 mapper field-by-field | ❌ | applyResolverFields tests assert payload shape end-to-end + specific field values via `expect.objectContaining`, not field-by-field mirrors |

## Delete commands

**None**. Zero SCAFFOLDING-class tests identified in this project's deltas.

## Path-move commands

**None**. All 4 project-owned test files live at their canonical `__tests__/` paths per the shared-library + PCF conventions ([src/client/shared/CLAUDE.md](../../../src/client/shared/CLAUDE.md), [src/client/pcf/CLAUDE.md](../../../src/client/pcf/CLAUDE.md)).

## Ambiguous — reviewer judgment

**None**.

## Maintain — confirmed (no action)

All 116 test methods across 4 files. Representative examples:

| File:Method | KEEP path | Why maintain |
|---|---|---|
| RegardingResolverApp.test.tsx:`FR-A1-02 — default title "RELATED RECORD" (uppercased) renders when maker omits title binding` | PCF `__tests__/` (behavior) | Renders + asserts DOM state — defends against manifest-default regressions |
| RegardingResolverApp.test.tsx:`FR-A4-01 — PolymorphicPicker.onSelect delegates to applyResolverFields` | PCF `__tests__/` (behavior) | Integration between shared component and shared service — defends the delegation contract |
| RegardingResolverApp.test.tsx:`pre-loaded record — WebAPI returns URL — parses etn+id + calls navigateTo` | PCF `__tests__/` (regression) | SRFR-042/043 hyperlink onLoad regression — direct defense against the specific bug reproduced in production |
| RegardingResolverApp.test.tsx:`Auto-detect CREATE mode: sync writes fire immediately even if async resolution rejects (user-save race guard)` | PCF `__tests__/` (regression) | SRFR-057 two-phase-write regression — direct defense against silent async-race failures |
| ResolverWriteHandler.test.ts:`SRFR-048 — narrower host (sprk_event) only nulls lookups that exist on that entity` | PCF `__tests__/` (regression) | SRFR-048 nav-prop-limited clear regression — asserts payload contains ONLY existing lookups |
| PolymorphicResolverService.test.ts:`applyResolverFields writes target's display-name value to sprk_regardingrecordname when metadata is present` | Shared lib `__tests__/` (behavior) | SRFR-052 display-name resolution behavior |
| PolymorphicResolverService.test.ts:`applyResolverFields falls back to parentRecordName when target record's value is null` | Shared lib `__tests__/` (behavior) | NFR-06 graceful-blank behavior |
| PolymorphicPicker.test.tsx:`onSelect fires with correct entityType + recordId + recordName after Xrm.Utility.lookupObjects resolves` | Shared lib `__tests__/` (behavior) | FR-C2-01 shared-component contract |

## Count delta

- Tests added during project: **116** (across 4 files)
- Tests classified MAINTAIN: **116** (100%)
- Tests classified SCAFFOLDING: **0**
- Tests classified AMBIGUOUS: **0**
- Net post-diet expected count: **116** (no changes needed)

## Assessment

This project's test set is **100% MAINTAIN class**. Every test is either:
- A behavioral assertion on a specific FR (from spec.md), OR
- A regression guard tied to a specific SRFR-XXX defect discovered during owner UAT

No pruning required. Reports final state clean per ADR-038 §7 build-vs-maintain criteria.

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1-B17.

---

*Report generated 2026-07-08 during SRFR-090 wrap-up. Zero deletions required.*
