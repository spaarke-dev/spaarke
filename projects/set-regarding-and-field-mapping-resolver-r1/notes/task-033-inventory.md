# SRFR-033 — Version Anchor Inventory + Read-Only Detection

## Read-only detection pattern (in-use)

Location: `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverHost.tsx` → `resolveReadOnly()`.

Combines BOTH signals (either triggers read-only):
- `context.parameters.readOnly.raw === true` — explicit manifest input property
- `context.mode.isControlDisabled === true` — Dataverse-inherited (read-only form OR FLS)

Pattern is consistent with the RegardingResolver v1.2.0 baseline. No change needed for FR-A5-01 detection layer.

## Read-only render pattern (in-use)

- `RegardingResolverHost` computes `readOnly` and passes to `RegardingResolverApp`.
- `RegardingResolverApp` passes `readOnly` to `PolymorphicPicker`.
- Shared `PolymorphicPicker.tsx` line 292 already gates the trigger icon on `!readOnly` — trigger is fully hidden when read-only.
- Row 2 renders unchanged under read-only — hyperlink click handler still opens the modal (view-only intent per FR-A5-01).
- Defensive write-gate at RegardingResolverApp line 282 refuses `handlePickerSelect` if `readOnly === true` (belt + suspenders).

**Verdict**: Read-only mode already meets FR-A5-01 semantics (Row 1 = title-only, no icon; Row 2 = unchanged with hyperlink still active). Task 033's read-only work is a strengthened test + preservation, not a code change.

## URL field write pattern (in-use)

Location: `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` line 372:

```ts
entity['sprk_regardingrecordurl'] = buildRecordUrl(parentEntityLogicalName, cleanRecordId);
```

Existing shared-lib tests confirm the write:
- `PolymorphicResolverService.test.ts` L290 / L353 / L499
- `TodoRegardingUpdateBuilder.test.ts` L174 / L268 / L358 / L472

Task 033 adds a **consumer-side regression test** in the RegardingResolver test file that asserts the resolver payload from `handlePickerSelect` populates the URL field. Since the URL is written inside `applyResolverFields` (which the RegardingResolver mock stubs out), the consumer-side assertion is on the CREATE-mode bridge seam's `recordUrl` field (already asserted at test-file line 619 by SRFR-032). Extend the FR-A5-03 verification by ensuring a dedicated test asserts the shape.

## 4 (actually 7) version anchors — v1.2.0 → v1.3.0

Per `src/client/pcf/CLAUDE.md` Version Update Checklist plus `grep -R '1.2.0'` scan:

| # | File | Anchor |
|---|------|--------|
| 1 | `RegardingResolver/RegardingResolver/ControlManifest.Input.xml` | `version="1.2.0"` line 3 |
| 2 | `RegardingResolver/RegardingResolver/index.ts` | `CONTROL_VERSION = '1.2.0'` line 32 |
| 3 | `RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (footer) | render `v{version}` (line 493) → needs `v{version} • Built YYYY-MM-DD` |
| 4 | `RegardingResolver/Solution/solution.xml` | `<Version>1.2.0</Version>` line 11 |
| 5 | `RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` | `version="1.2.0"` line 3 |
| 6 | `RegardingResolver/package.json` | `"version": "1.2.0"` line 3 |
| 7 | `RegardingResolver/Solution/pack.ps1` | `$version = "1.2.0"` line 1 |

Test file (`__tests__/RegardingResolverApp.test.tsx`) has 25 `version="1.2.0"` occurrences (test props); update to 1.3.0 for signal consistency.

Comment lines in test file at 613, 671, 709, 736 reference `Presave v1.2.0` — those are references to the **presave webresource** version, which is a SEPARATE artifact (bumped by SRFR-040 to 1.2.0). Leave those comments untouched.

Similarly `RegardingResolverApp.tsx` lines 318–319 reference "presave v1.2.0" (webresource contract) — do NOT bump.

`package-lock.json` — transitive dependency versions (`@azure/core-tracing`, `gopd`, `mitt`, etc.) that happen to be `1.2.0` are external NPM package versions and MUST NOT be edited.

## Build date

Bump day: 2026-07-02 (per system context `currentDate`). Footer will read `v1.3.0 • Built 2026-07-02`.
