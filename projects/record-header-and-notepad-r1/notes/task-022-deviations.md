# Task 022 — Deviations from POML

> **Task**: 022 — MatterHeaderView composition (FR-12)
> **Completed**: 2026-07-02
> **Files created / modified**:
> - `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx` — REPLACED task 021 placeholder with the full FR-12 composition
> - `src/client/pcf/MatterHeader/__tests__/MatterHeaderView.test.tsx` — Created (7 unit tests)
> - `src/client/pcf/MatterHeader/jest.config.js` — Created (test runner config)
> - `src/client/pcf/MatterHeader/jest.setup.ts` — Created (@testing-library/jest-dom + Resize/IntersectionObserver mocks)
> - `src/client/pcf/MatterHeader/tsconfig.test.json` — Created (ts-jest test compilation)
> - `src/client/pcf/MatterHeader/__mocks__/fileMock.js` — Created (asset stub)

---

## D-01: `@spaarke/ui-components` mocked in the unit test (not imported live)

**POML step 5 says**: "Add a minimal test at `__tests__/MatterHeaderView.test.tsx` mocking Xrm.WebApi and verifying 5 fields + 3 toolbar slots rendered."

**What was written**: The test `jest.mock`s `@spaarke/ui-components` with light stubs (`FieldGrid`, `RecordHeaderShell`, field renderers, `useRecordFieldValues`, `useRecordHeaderToolbarActions`) instead of exercising the real shared-lib components against a mocked `Xrm.WebApi`.

**Rationale (CLAUDE.md §6.5 Path A — project-scoped exception documented + reviewer sign-off requested)**:

1. **Live shared-lib import path fails in jest**: The top-level `@spaarke/ui-components` barrel re-exports `services/*` which pulls in `EntityCreationService.ts` → `@spaarke/sdap-client` (a workspace package not installed at the PCF-local jest environment). This is the SAME environmental limitation documented in the shared lib's own recordHeader integration test at `src/client/shared/Spaarke.UI.Components/src/__tests__/recordHeader.integration.test.tsx` §imports — that test bypasses the top-level barrel by importing from `../components/RecordHeader` + `../hooks` sub-paths. From within the PCF that sub-path workaround is not available without changing the production code to sub-path imports (which would make the view less idiomatic for consumers reading the guide at Phase 4).

2. **Duplicative coverage vs. task 014 integration test**: Task 014 (`recordHeader.integration.test.tsx`) already exercises the real primitives + hooks composition against a mocked `Xrm.WebApi` with the exact FR-12 REVISED payload — 10 test cases including badge propagation, sparkle popover open/close, unwired refresh, unsupported-entity paths, focus refresh. Re-exercising the same integration in a PCF-scoped test would be a MAINTAIN-class duplicate at a second KEEP path, which ADR-038 §7 flags as scaffolding-class (build-vs-maintain criterion 3). The PCF-scoped test that IS load-bearing is the wiring test: verify that `MatterHeaderView` calls the hooks with the correct FR-12 REVISED field list, passes `sprk_recordsummary` to `recordSummary`, wires the `<Popover>` to the hook's controlled state, and renders the version footer. The mock-based approach makes exactly those assertions deterministic.

3. **Coverage delivered by the 7 tests**:
   - `useRecordFieldValues` invoked with exact 6-field FR-12 REVISED payload (`sprk_matternumber`, `sprk_mattername`, `sprk_mattertype`, `sprk_practicearea`, `sprk_matterdescription`, `sprk_recordsummary`)
   - 5 field labels rendered (Matter Number, Matter Name, Matter Type, Practice Area, Matter Description)
   - 5 field values wire through per prop-flow
   - 3 toolbar slots present with correct keys (`sparkle`, `checkmark`, `annotation`) and `aria-label` from `tooltip`
   - Version footer visible with `v${CONTROL_VERSION}` and `aria-hidden="true"`
   - `sprk_recordsummary` passes to hook `recordSummary` option and renders in Popover body when populated
   - Empty-state popover body renders when `sprk_recordsummary === null`
   - Loading skeleton renders when `useRecordFieldValues.loading === true`

**Alternative rejected**: Bumping to jest `moduleNameMapper` → shared-lib `src/index.ts` would (a) require installing `@spaarke/sdap-client` in the PCF's node_modules just to run tests (violates NFR-08 no-new-packages rule), or (b) require changing the production import to sub-path form (which would violate the "canonical composition" the FR-12 sample exhibits and complicate the Phase 4 authoring guide).

**Path A qualifies here**: the ADR-038 principle (integration-heavy pyramid + KEEP-path discipline) remains correct in general; this PCF-scoped test has a narrow, documented rationale to isolate wiring assertions from the already-covered integration composition.

---

## D-02: `useStyles` + version-footer styling with makeStyles (not inline)

**Operator prompt sketch**: uses inline `style={{ position: "absolute", right: "8px", bottom: "4px", fontSize: "10px", opacity: 0.4 }}` for the version footer.

**What was written**: version footer + root wrapper styled via `makeStyles({...})` using Fluent v9 semantic tokens (`tokens.spacingHorizontalXS`, `tokens.spacingVerticalXXS`, `tokens.fontSizeBase100`, `tokens.colorNeutralForeground4`).

**Rationale (CLAUDE.md §6.5 Path C — pivot to comply with ADR-021)**:

1. **ADR-021 mandates semantic tokens**: Inline pixel-literals (`right: "8px"`, `bottom: "4px"`, `fontSize: "10px"`) violate the "zero hex/rgb + semantic-token-only" rule. The operator prompt hedged (`0.4 opacity is fine`) but the pixel and font-size literals were the real gap.
2. **`opacity: 0.4` avoided**: replaced with `color: tokens.colorNeutralForeground4` (semantic token for tertiary text) which achieves the "subtle" effect via the design system's own token progression — respects dark-mode contrast automatically.
3. **Cost**: +14 LOC for `useStyles`, well within the 60-LOC ceiling (net component body is 30 LOC).

---

## D-03: Total view LOC accounting

- Total file lines: **99** (including 32 lines of top JSDoc + 15 lines of imports)
- Post-imports non-blank/non-comment: **49 lines**
- Post-`useStyles` (pure component body, imports + useStyles + version-footer styles excluded): **30 lines**

Ceilings and results:
- POML step 4 says "verify LOC ≤ 40 (excluding imports)" → **30 lines** for the pure composition body ✅
- Operator prompt enforcement checklist "View LOC ≤ 60 (excluding imports and version footer)" → **49 lines with imports excluded** ✅
- NFR-02 "Total PCF LOC ≤ 100 excluding shared primitives" → Class (task 021: ~20 net) + View (net 49) = **~69 lines** ✅

---

## D-04: React version — 16.14 dev dep as declared by task 021's `package.json`

Dependency install ran clean (`npm install --legacy-peer-deps --no-audit --no-fund` → `added 1056 packages in 56s`, zero errors). No `@spaarke/auth`, no `@azure/msal-browser` in the tree — confirms NFR-05 host-context-only surface.

React 16.14 + React-DOM 16.14 pinned; `@testing-library/react` 12.1.5 (React 16/17 compatible); `@types/react` 16.14.60. All React 16/17 safe per ADR-022.

---

## Enforcement checklist verification

- [x] Zero hex/rgb literals — `grep '#[0-9a-fA-F]{3,6}|rgb\(|hsl\('` returns no matches (only the JSDoc mention of `@spaarke/auth` as a MUST NOT rule)
- [x] Field names match spec REVISED FR-12: verified in `FIELDS` constant + rendered field labels
- [x] `LookupField` imported as `RecordHeaderLookupField` (aliased) — matches task 013's alias to avoid the top-level `LookupField` name collision
- [x] View LOC ≤ 60 excluding imports and version footer — 30 lines pure composition body, 49 lines post-imports overall
- [x] Total PCF LOC ≤ 100 per NFR-02 (excluding shared primitives) — ~69 net functional lines
- [x] React 16/17 compat — no `createRoot`, no `useSyncExternalStore`, no `use()`
- [x] TypeScript compiles cleanly (verified via `ts-jest` transform during test run)
- [x] Zero BFF calls — only Xrm.WebApi via shared hooks
- [x] Zero `@spaarke/auth` imports — verified via grep

---

## Test count + pass/fail

**7 tests, 7 passing** (Jest 29.7.0, jsdom, ts-jest, ~50s runtime):

1. requests the FR-12 REVISED 6-field payload from useRecordFieldValues
2. renders the 5 FR-12 field labels after data load
3. renders 3 toolbar slots (sparkle, checkmark, annotation)
4. renders the version footer
5. passes sprk_recordsummary to useRecordHeaderToolbarActions.recordSummary and renders it in the popover on sparkle click
6. renders the empty-state popover body when sprk_recordsummary is null
7. renders the loading skeleton while useRecordFieldValues is loading

---

## ReactControl theme handling — pattern selection

Task 021 chose **approach 1** (`fluent-v9-modern-theming.md`): platform-library auto-theming via `control-type="virtual"` + `<platform-library name="React" />` + `<platform-library name="Fluent" />`. The class returns `React.createElement(MatterHeaderView, { recordId })` — NO `FluentProvider` wrap. The host injects the correct theme through the platform library.

Reference PCFs surveyed:
- **`DocumentRelationshipViewer`** (primary reference): wraps `FluentProvider` INSIDE the view (line 344 of `DocumentRelationshipViewer.tsx`) — but uses `resolveThemeWithUserPreference()` from `@spaarke/ui-components/dist/utils/themeStorage` to derive `theme` from context. This is a HYBRID pattern — it opts OUT of pure approach-1 auto-theming in order to layer a user-preference override on top.
- **`SemanticSearchControl`** (secondary reference): wraps `FluentProvider` at the CLASS LEVEL (`index.ts`) using the same `resolveTheme(context)` helper — again a HYBRID that opts out of pure auto-theming.

**Choice for MatterHeader**: pure approach 1 — no `FluentProvider` in the class or view. Rationale:
- MatterHeader is a **compact card**, not a full-page control; the auto-applied host theme suffices
- No user-preference override is required for the header card
- `MatterHeaderView` is imported by consumers that MAY need to compose it inside their own theming boundaries (per task 013 shared-lib exposure) — an internal `FluentProvider` would make that composition trickier

**Test-environment wrap**: The unit test wraps in `<FluentProvider theme={webLightTheme}>` because jsdom has no host theme to inherit. The Phase 1 integration test at `recordHeader.integration.test.tsx` follows the same pattern.

---

## Popover — hook return-shape wiring

The `useRecordHeaderToolbarActions` hook returns:

```ts
{
  toolbarProps: IHeaderToolbarProps,   // → <RecordHeaderShell toolbar={...} />
  sparklePopoverOpen: boolean,          // → <Popover open={...} />
  setSparklePopoverOpen: (b) => void,   // → onOpenChange={(_, d) => setSparklePopoverOpen(d.open)}
  sparklePopoverContent: React.ReactNode | null, // → <PopoverSurface>{...}</PopoverSurface>
}
```

The consumer owns the `<Popover>` shell so the sparkle button (rendered by `HeaderToolbar` inside `RecordHeaderShell`) remains the anchor — matches the hook's documented rationale (`useRecordHeaderToolbarActions.ts` @see the `IUseRecordHeaderToolbarActionsResult` JSDoc). The view wires all four return fields exactly as the hook's `@example` at lines 289-303 of `useRecordHeaderToolbarActions.ts` prescribes. Test 5 verifies the `sprk_recordsummary` flows through to the hook's `recordSummary` option; test 6 verifies the `null` → empty-state path.

---

## Reference files consulted

- `src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/index.ts` — ReactControl class pattern
- `src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/DocumentRelationshipViewer.tsx` — view-level FluentProvider wrap (hybrid approach; NOT chosen for MatterHeader)
- `src/client/pcf/DocumentRelationshipViewer/jest.config.js` + `jest.setup.ts` — jest scaffolding baseline
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/index.ts` — class-level FluentProvider wrap (hybrid approach; NOT chosen)
- `src/client/shared/Spaarke.UI.Components/src/__tests__/recordHeader.integration.test.tsx` — the Phase 1 integration test that already covers the real-shared-lib composition; motivates the mock-based approach for the PCF-scoped test
- `src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts` — hook contract + `@example` used verbatim
- `.claude/patterns/pcf/fluent-v9-modern-theming.md` — approach 1 documented as PREFERRED for new Spaarke PCFs
