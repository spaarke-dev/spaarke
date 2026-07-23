# Test diet report — visual-host-version-update

**Run date**: 2026-07-13
**Branch**: work/visual-host-version-update (merged to master via PR #639)
**Scope**: test files touched by the project's own commits (VisualHost + `@spaarke/visuals`)

## Scope note — classifier applicability

ADR-038 §7's **17-ban classifier (B1–B17) is .NET/xUnit-specific** (`tests/**/*.cs`, `[Fact]`, `Mock<HttpMessageHandler>`, records, `services.GetRequiredService`, etc.). This project is **TypeScript/Jest** and touched **zero `tests/**/*.cs`** files. The literal skill scope → "not applicable." Applying the **spirit** of the classifier (path-violation, name-without-scenario, mirror/coverage-filler, mock-heavy-trivial) to the project's Jest tests instead.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 3 | confirmed — no action |
| MAINTAIN **but PATH-VIOLATION** | 6 | relocate → `@spaarke/visuals` harness (commands below) |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| **Total project test files** | **9** | — |

**No deletions.** Every touched test is behavioral (renders DOM, asserts drill/nav/theme, or exercises a real service). The only debt is **location**: 6 tests test components that moved to `@spaarke/visuals` (VHVU-041) but the test files still sit in the PCF.

## MAINTAIN — confirmed, correct path (no action)

| File | Tests | Why maintain / why it stays |
|---|---|---|
| `control/components/__tests__/CalendarVisual.test.tsx` | the PCF **container** `CalendarVisual` (imports `../CalendarVisual`) | Behavioral (weekday headers, month nav, drill interactivity, dark theme). The container is PCF-side, so the test correctly stays. *Optional add:* a pure-component test in `@spaarke/visuals` for the `detailedEvents` props-in path. |
| `control/services/__tests__/ConfigurationLoader.test.ts` | PCF host service `ConfigurationLoader` | Behavioral service test; service stays in the PCF. Correct path. |
| `control/services/__tests__/DataAggregationService.test.ts` | PCF host service `DataAggregationService` | Behavioral service test (also proved the VHVU-050 `ViewDataService` re-export chain); stays in PCF. Correct path. |

## MAINTAIN but PATH-VIOLATION — relocate to `@spaarke/visuals`

These 6 files each `import` their component from `../../../../../shared/Spaarke.Visuals/src/components/<X>` — i.e. they test components that **moved to `@spaarke/visuals` in VHVU-041**, but the test files remained in the PCF's `__tests__`. They currently run **cross-package from the VisualHost Jest harness** (works after the VHVU-060 harness fix, but the wrong home). Canonical home: co-located in `@spaarke/visuals`.

| File (current) | Component tested | Proposed canonical path |
|---|---|---|
| `…/VisualHost/control/components/__tests__/BarChart.test.tsx` | `@spaarke/visuals` BarChart | `…/Spaarke.Visuals/src/components/__tests__/BarChart.test.tsx` |
| `…/DonutChart.test.tsx` | DonutChart | `…/Spaarke.Visuals/src/components/__tests__/DonutChart.test.tsx` |
| `…/LineChart.test.tsx` | LineChart | `…/Spaarke.Visuals/src/components/__tests__/LineChart.test.tsx` |
| `…/MetricCard.test.tsx` | MetricCard | `…/Spaarke.Visuals/src/components/__tests__/MetricCard.test.tsx` |
| `…/MiniTable.test.tsx` | MiniTable | `…/Spaarke.Visuals/src/components/__tests__/MiniTable.test.tsx` |
| `…/StatusDistributionBar.test.tsx` | StatusDistributionBar | `…/Spaarke.Visuals/src/components/__tests__/StatusDistributionBar.test.tsx` |

### Blocker: `@spaarke/visuals` has NO Jest harness yet

`src/client/shared/Spaarke.Visuals/package.json` has only `tsc` build/lint scripts — no jest config, no test deps. The relocation is **not a bare `git mv`**; it requires bootstrapping a harness in the package first.

### Recommended relocation procedure (DO NOT auto-execute — reviewer's call)

```bash
# 1. Bootstrap a Jest harness in @spaarke/visuals (mirror the VisualHost one):
#    - add devDeps: jest, ts-jest, jest-environment-jsdom, babel-jest + @babel presets,
#      @testing-library/react, @testing-library/dom, @testing-library/jest-dom,
#      @types/jest, scheduler
#    - add jest.config.js (preset ts-jest, testEnvironment jsdom, jest.setup.ts with
#      @testing-library/jest-dom, transformIgnorePatterns allowing @fluentui/react-charting + d3)
#    - add "test": "jest" to package.json scripts
#    NOTE: no React singleton moduleNameMapper needed here — the package has its own
#          single React/@fluentui copy (that mapper was only needed for the cross-package case).

# 2. Move the 6 test files (git mv preserves history):
git mv src/client/pcf/VisualHost/control/components/__tests__/BarChart.test.tsx              src/client/shared/Spaarke.Visuals/src/components/__tests__/BarChart.test.tsx
git mv src/client/pcf/VisualHost/control/components/__tests__/DonutChart.test.tsx            src/client/shared/Spaarke.Visuals/src/components/__tests__/DonutChart.test.tsx
git mv src/client/pcf/VisualHost/control/components/__tests__/LineChart.test.tsx             src/client/shared/Spaarke.Visuals/src/components/__tests__/LineChart.test.tsx
git mv src/client/pcf/VisualHost/control/components/__tests__/MetricCard.test.tsx            src/client/shared/Spaarke.Visuals/src/components/__tests__/MetricCard.test.tsx
git mv src/client/pcf/VisualHost/control/components/__tests__/MiniTable.test.tsx             src/client/shared/Spaarke.Visuals/src/components/__tests__/MiniTable.test.tsx
git mv src/client/pcf/VisualHost/control/components/__tests__/StatusDistributionBar.test.tsx src/client/shared/Spaarke.Visuals/src/components/__tests__/StatusDistributionBar.test.tsx

# 3. Fix each moved test's import (they resolve from the new location):
#    FROM: from '../../../../../shared/Spaarke.Visuals/src/components/<X>'
#    TO:   from '../<X>'
#    (also any '../../types' imports → '../../types')

# 4. Verify both harnesses:
cd src/client/shared/Spaarke.Visuals && npm test          # the 6 relocated tests
cd src/client/pcf/VisualHost && npx jest                  # remaining 3 (Calendar container + 2 services)
```

After relocation, the VisualHost Jest harness no longer reaches across into the sibling package — its singleton `moduleNameMapper` (added in VHVU-060) can stay (harmless) or be trimmed to just the `CalendarVisual` container's cross-package import of the pure component.

## Count delta

- Test files touched by project: **9**
- MAINTAIN (stay): **3**
- MAINTAIN but relocate: **6**
- SCAFFOLDING (delete): **0**
- AMBIGUOUS: **0**
- Net post-diet file count: **9** (0 deleted; 6 moved)

## Verdict

**No scaffolding to delete.** The single actionable item is the **6-file relocation into a new `@spaarke/visuals` Jest harness** — the VHVU-090 follow-up already tracked in `current-task.md`. It is a clean-up, not a correctness blocker: all 9 tests pass today (131 assertions green) via the VisualHost harness.

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes). 17-ban classifier B1–B17 (here: .NET-specific → spirit applied to TS/Jest; the operative principle is the **KEEP-path / co-location** rule — tests live with the code they exercise).
