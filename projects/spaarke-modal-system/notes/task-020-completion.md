# Task 020 Completion — P0.5 App-Shell `--sprk-ui-scale` Control (FR-06)

**Result: ✅ complete.** Solo task (parallel-group: none), FULL rigor, Sonnet 5 @ effort high.

## Derivation design

One effective `uiScale` = `max(settingMultiplier, breakpointMultiplier)`:

| Input | Value |
|---|---|
| Auto breakpoint (`window.innerWidth >= 2560`) | **1.15** (design §6.9 exact value) |
| Display size = Default | **1.0** (spec exact value) |
| Display size = Large | **1.25** (chosen — not named in spec/design; see rationale) |
| Display size = Extra-large | **1.5** (chosen — not named in spec/design; see rationale) |

**Why `max()` and not literal multiplication**: design §6.9 only names the breakpoint (1.15) and Default (1.0) as exact values, explicitly leaving Large/Extra-large as "fixed multipliers you choose." FR-06's own acceptance text tests `uiScale` at exactly `1.0 / 1.25 / 1.5` — so I chose Large=1.25 and Extra-large=1.5 to land on the SAME already-named, already-tested scale points rather than inventing new ones. Given that, `max(settingMultiplier, breakpointMultiplier)` keeps every possible output confined to the four values `{1.0, 1.15, 1.25, 1.5}` FR-06 acceptance actually exercises. Literal multiplication (e.g. Large × breakpoint = 1.4375) would produce an untested, arbitrary value and was rejected. Precedence in practice: Default on a ≥2560 viewport bumps to 1.15 (matches acceptance criterion 2 exactly); an explicit Large/Extra-large setting is already ≥ the breakpoint bump, so the setting always wins once chosen — no separate "which wins" rule needed.

## Where the "Display size" affordance landed

**Finding (verified, not assumed)**: SpaarkeAi has **no pre-existing appearance/settings surface** of its own. Confirmed via targeted greps + reads:
- No `ThemeToggle`, `SettingsMenu`, or any dark/light control renders anywhere inside SpaarkeAi's own component tree (`App.tsx` → `ThreePaneShell` → panes).
- The only existing `ThemeToggle` usage is in **LegalWorkspace's standalone `PageHeader`** — which is skipped entirely (`{!embedded && <PageHeader/>}`) when LegalWorkspace is embedded inside SpaarkeAi. So embedding does NOT bring a theme/settings surface into SpaarkeAi either.
- The only "menu" affordances in SpaarkeAi's chrome (`AssistantToolMenu`, `WorkspacePaneMenu`, `ContextPaneMenu`) are AI-tool/pane-scoped (Quick Start, My Assistant, Memory, workspace-layout actions) — semantically the wrong home for a UI/display setting, and per-pane rather than app-shell-scoped.
- `ThreePaneShell.tsx` itself has no persistent global header row outside the three panes (confirmed by reading its full render tree) — nothing existing to extend.

**Decision (CLAUDE.md §11 three-question test applied)**: built one new, small, self-contained `DisplaySizeMenu` component — reusing the **exact same established Fluent `Menu`/`MenuTrigger`/`MenuPopover`/`MenuList`/`MenuItemRadio` + `checkedValues`/`onCheckedValueChange` idiom** already used by `ViewSelector`, `AssistantToolMenu`, `WorkspacePaneMenu`, `ContextPaneMenu` (no parallel menu abstraction — just the established pattern applied to a new, narrow purpose). Existing-question: closest neighbors (`ThemeToggle` = wrong shape for 3 options; the 3 sibling menus = wrong semantic scope) don't extend cleanly. Cost-of-doing-nothing: without ANY control, FR-06 acceptance criterion 1 (setting persists + `uiScale` re-derives) has no way to be exercised by a user.

- **SpaarkeAi**: mounted in a new slim, right-aligned, non-intrusive strip (`styles.scaleBar`) at the very top of `AppWithAuth`'s root — above `ThreePaneShell`, touching **zero** lines of `ThreePaneShell.tsx` or any pane header (keeps the SpaarkeAi hot-path diff to `App.tsx` only, per the "keep the touch narrow" constraint).
- **LegalWorkspace**: mirrored into the **existing** `PageHeader.tsx`, directly beside the existing `<ThemeToggle />` (standalone-only — `PageHeader` is already skipped in embedded mode). When embedded inside SpaarkeAi, LegalWorkspace does **not** render a second control — the host's `DisplaySizeMenu` (in `App.tsx`) already covers it, consistent with "no second FluentProvider/mechanism."

## The `uiScale` seam for conversion tasks (P2+)

Export: **`useUiScale()`** — `src/client/shared/Spaarke.UI.Components/src/hooks/useUiScale.ts`, re-exported from the main barrel (`@spaarke/ui-components`).

```ts
const { uiScale, displaySize, setDisplaySize } = useUiScale();
// uiScale: number — pass directly as <SprkModal uiScale={uiScale} ... />
```

Any P2+ conversion task rendering a `SprkModal` inside SpaarkeAi or LegalWorkspace calls `useUiScale()` in the same component (or threads the already-computed `uiScale` down via props/context) and passes it straight through as the `uiScale` prop — no new plumbing required. The underlying pure derivation (`getEffectiveUiScale`, `isLargeViewport`, `subscribeToViewportBreakpoint`, `DISPLAY_SIZE_MULTIPLIERS`) lives in `components/SprkModal/uiScale.ts`, sitting alongside task-002's `sizes.ts`/`scaledTheme.ts` as the third piece of "modal scale machinery."

## Storage-pattern extension shape

Extended `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts` **in place** (not replaced, not a new module):
- Added `DISPLAY_SIZE_STORAGE_KEY = 'spaarke-display-size'`, `DisplaySizePreference` type, `getDisplaySizePreference()`, `setDisplaySizePreference()` — same shape as the existing `getUserThemePreference`/`setUserThemePreference` pair.
- `setDisplaySizePreference()` dispatches the **SAME** `THEME_CHANGE_EVENT` the theme functions use (not a new event name).
- Broadened `setupCodePageThemeListener()`'s `storage`-event key filter to also match `DISPLAY_SIZE_STORAGE_KEY` (one-line change) so a Display-size change from another tab also recomputes through the **same, unchanged** listener — no second listener was registered anywhere. `setupThemeListener` (the PCF-side sibling) was deliberately **left untouched** — Display-size wiring is Code-Page/app-shell-scoped only per task constraint; broadening the PCF listener too wasn't needed and would have widened the diff for no consumer.
- Multiple simultaneous `useUiScale()` instances (e.g. `App.tsx`'s own call + `DisplaySizeMenu`'s own call) each independently call the unchanged `setupCodePageThemeListener()` — this mirrors the codebase's pre-existing pattern (e.g. `useTheme()` and `App.tsx`'s own theme state already coexist as independent consumers of the same function today), not a second mechanism.

## Per-shell wiring

- **`src/solutions/SpaarkeAi/src/App.tsx`**: `App` component now calls `useUiScale()`, computes `scaledTheme = scaleTheme(theme, uiScale)` (memoized), feeds it to the single `<FluentProvider>`, and sets `--sprk-ui-scale` on `document.documentElement` in a `useEffect` (portal-safe — Fluent `Dialog` surfaces portal to `document.body`, still a `:root` descendant). `AppWithAuth` renders `<DisplaySizeMenu />` in a new slim header strip above `ThreePaneShell`.
- **`src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx`**: same `useUiScale()` + `scaleTheme` wiring, but gated by the existing `embedded` prop — standalone applies the scaled theme + sets the CSS var; embedded mode skips both (the host already owns them), mirroring the file's pre-existing `if (embedded) return;` guard used by the Dataverse theme-sync effects.
- **`src/solutions/LegalWorkspace/src/components/Shell/PageHeader.tsx`**: added `<DisplaySizeMenu />` beside the existing `<ThemeToggle />` (one import + one JSX line).

## Files modified / created

**Modified:**
- `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/index.ts` (barrel)
- `src/client/shared/Spaarke.UI.Components/src/hooks/index.ts` (barrel)
- `src/client/shared/Spaarke.UI.Components/src/components/index.ts` (barrel)
- `src/solutions/SpaarkeAi/src/App.tsx`
- `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx`
- `src/solutions/LegalWorkspace/src/components/Shell/PageHeader.tsx`

**Created:**
- `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/uiScale.ts` (derivation logic)
- `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/__tests__/uiScale.test.ts` (21 tests)
- `src/client/shared/Spaarke.UI.Components/src/hooks/useUiScale.ts` (React hook seam)
- `src/client/shared/Spaarke.UI.Components/src/components/DisplaySizeMenu/DisplaySizeMenu.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/DisplaySizeMenu/index.ts`

**Not touched** (per hard boundary): `SprkModal.tsx`, `SprkModal.types.ts`, `sizes.ts`, `scaledTheme.ts`, `pcf-safe.ts`, `TASK-INDEX.md`, `current-task.md`, anything under `.claude/`.

## Builds

| Package | Command | Result |
|---|---|---|
| Spaarke.UI.Components | `npm run build` (tsc) | ✅ clean, zero errors |
| SpaarkeAi (Code Page, React 19) | `npm run build` | ✅ Succeeded — 4023 modules (baseline was 4019; +4 = my 4 new files), `tsc-surface-gate`: 0 surface-owned errors |
| LegalWorkspace | `npx tsc --noEmit` (its own `npm run build` is pre-existing-broken, Issue #712) | ✅ 238 total errors (task-stated baseline ~265); the only 2 errors touching `LegalWorkspaceApp.tsx` (lines 141/151, `IWebApi`/`IThemeWebApi` mismatch) verified via `git diff` to be on **untouched** pre-existing lines (only shifted down by my unrelated insertions above them); zero errors in `PageHeader.tsx` |
| SemanticSearchControl (PCF, React 16/17) | `npm run build:prod` | ✅ Succeeded — 0 ESLint errors (17 pre-existing warnings, none in touched files), webpack build succeeded |

## Tests

- New `uiScale.test.ts`: **21/21 passing** (constants, `isLargeViewport` at 4 viewport widths, `getEffectiveUiScale` precedence across all 3 settings × both breakpoint states, persistence round-trip + defaulting + event-dispatch reuse, `subscribeToViewportBreakpoint` incl. legacy-fallback and defensive no-throw paths).
- P0 gate (`SprkModal` + `ModalWindowControls` families): **107/107 passing** (86 pre-existing + 21 new) — gate stays green.
- Full `Spaarke.UI.Components` suite: **186 passed / 11 failed suites** (2465 passed / 22 failed tests of 2487 total). Verified via direct `FAIL` line diff that the failing suite list is **byte-identical** to the documented 11 pre-existing failures (`EntityCreationService.cascade`, `XrmDataverseClient`, `surfaceLaunchRegistry`, `buildDynamicWorkspaceConfig`, `toolbarLaunchDefaults`, `SendEmailDialog.characterize`, `RichFilePreview`, `ConversationView.forward`, `recordHeader.integration`, `TimelineComposeBox`, `ConversationView.emailInFlow`) — zero new failures; the delta (+1 suite, +21 tests, all passing) is exactly my new test file.

## Step 9.5 quality gates

- **Self code-review**: traced the full listener/state flow for race conditions and stale closures (none found — `recompute` in `useUiScale` has an empty dep array and always reads fresh state; multiple independent `useUiScale()`/`setupCodePageThemeListener()` consumers are the pre-existing established pattern, not new fragility). Verified `theme` variable's only consumption site was correctly updated to `scaledTheme` in both `App.tsx` and `LegalWorkspaceApp.tsx` (no stale references left). Verified no dead imports/exports. ESLint clean on all touched/created shared-lib files (0 errors, 0 warnings). No findings requiring a fix.
- **adr-check**:
  - **ADR-021** (semantic tokens only): ✅ PASS — new `scaleBar`/`trigger` styles use `tokens.*` exclusively; confirmed via mechanical grep (see below).
  - **ADR-012** (shared components, extend don't fork): ✅ PASS — extended `themeStorage.ts` in place; `DisplaySizeMenu` reuses the established `Menu`/`MenuItemRadio` idiom (no parallel abstraction); justified per CLAUDE.md §11 three-question test (see above).
  - **NFR-04** (dual-React 16/17 + 19): ✅ PASS — no React-18/19-exclusive APIs in any new file; empirically proven via the actual `SemanticSearchControl` PCF `build:prod` success (not just static analysis); exported from the main barrel only, `pcf-safe.ts` untouched.
  - **NFR-05** (client-only): ✅ PASS — zero files touched under `src/server/api/Sprk.Bff.Api/**`.
- **ADR-021 diff gate**: grepped every added line (tracked-file diff + direct read of all 5 new untracked files) for hex codes / `'1px'` literals / inline color styles — **CLEAN**, zero hits.

## Acceptance-criteria checklist (POML, 6 criteria)

1. ✅ Display-size setting persists across reload via the existing theme-storage pattern; `uiScale` re-derives from it — verified by the persistence-round-trip test + `useUiScale`'s mount-time initializers reading fresh state.
2. ✅ Viewport ≥2560 CSS px + Display size = Default → `uiScale` = 1.15 — verified by test.
3. ⚠️ **Mechanism-level pass, end-to-end deferred by explicit scope.** The scaled-theme + `--sprk-ui-scale` mechanism is fully wired (verified independently: `scaleTheme`/`getSurfaceStyle` were already tested in task 002; my derivation is tested here) and the seam (`useUiScale()` → `uiScale` prop) is ready for any `SprkModal` a conversion task renders. No `SprkModal` instance exists in these shells yet — the POML itself states this ("no SprkModal instances exist in these shells yet (conversions are P2+)... Do not convert any dialog in this task"), so a literal "open a modal from the shell" visual check isn't possible within this task's boundary. Full end-to-end (visual, no-clipping) verification lands with the first P2+ conversion task that renders a `SprkModal` inside SpaarkeAi/LegalWorkspace.
4. ✅ Wired into SpaarkeAi + LegalWorkspace (SpaarkeAi itself satisfies the "code-page shell" leg per the POML's own scope guard) at the app `FluentProvider`; `SprkModal` internals untouched — only inherits.
5. ✅ No second `FluentProvider`/listener; shared-lib compiles clean under PCF (empirically proven) and Code Page React 19; zero BFF touch.
6. ✅ All unit tests pass; PCF + Code Page builds green; `/conflict-check` already run + cited by the orchestrating session (not re-run here per its explicit instruction).

## Escalations / deviations

- **None requiring escalation.** The one judgment call worth flagging (not an escalation — directional step mode, documented per instructions): SpaarkeAi had no pre-existing settings surface to "find and extend," so a new minimal `DisplaySizeMenu` component was built instead, reusing an established idiom (see "Where the affordance landed" above). This is exactly the kind of decision the task's own text anticipated ("find the existing settings UI... if it's embedded-engine-only, the host provides the setting — note it") and CLAUDE.md §11 governs (three-question justification recorded above).
- Large/Extra-large multiplier values (1.25 / 1.5) were chosen (not specified exactly in spec/design) per the task's own fallback instruction, landing on values already named elsewhere in FR-06 acceptance text and the task-002 scale machinery.
