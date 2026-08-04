# Task 070 — Replace hand-rolled ConversationModal with SprkModal (P5 / FR-16)

> **Status**: ✅ COMPLETE — the highest-risk conversion. Transform-robust centering (design §4.6 / FR-08) **VALIDATED** in this exact PCF; the escalation trigger did NOT fire.
> **Date**: 2026-08-02 · sole agent this wave · RIGOR: FULL · sonnet @ xhigh
> **File**: `src/client/pcf/CommunicationConversationPanel/CommunicationConversationPanel/ConversationModal.tsx`

---

## 1. Outcome in one line

The hand-rolled `createPortal` + `position:fixed` overlay is **deleted**; `ConversationModal` now composes `<SprkModal size="md" dismiss="light" padded={false}>` from `@spaarke/ui-components/dist/pcf-safe`. The Fluent Dialog portal centers correctly under a CSS-transformed ancestor — the exact bug the hand-roll worked around — proven by a structural jest test in this PCF's own suite.

## 2. Old overlay inventory (what was deleted)

The pre-change file (269 lines) hand-rolled the entire envelope:

| Deleted element | What it did | Now owned by |
|---|---|---|
| `ReactDOM.createPortal(..., document.body)` | portal to body for stacking/centering | Fluent `Dialog` (portals `DialogSurface` above transformed ancestors) |
| `<FluentProvider theme={webLightTheme}>` re-wrap | theme the portaled subtree | inherited host provider via `applyStylesToPortals` (same document) |
| `s.overlay` (`position:fixed; inset:0`, `colorBackgroundOverlay`, `zIndex:10000`, flex-center) | dimmed backdrop + centering | shell backdrop + Fluent portal |
| `s.surface` (`min(1040px,95vw) × 72vh`, `maxHeight:90vh`, shadow, radius) | the fixed-size surface | `getSurfaceStyle('md')` |
| `s.surfaceExpanded` (`96vw × 94vh`) + `expanded`/`setExpanded` state | "expand fills container" toggle | shell `maximized` state → `full` size |
| `s.windowControls` + `<ModalWindowControls>` (imported from MAIN barrel) | max/restore + × cluster, absolute top-right | shell's internal `ModalWindowControls` (header-right) |
| `s.title` ("Messages") | title bar | shell header `title="Messages"` |
| Esc `useEffect` (`keydown` + `stopPropagation`) | Esc-to-dismiss | `dismiss="light"` (Fluent Dialog Esc) |
| focus `useEffect` (`surfaceRef.focus()`) | crude focus-in | Fluent Dialog focus trap (a11y upgrade) |
| `handleOverlayMouseDown` backdrop handler | backdrop-click dismiss | `dismiss="light"` (Fluent Dialog backdrop) |

Net: the file went from a bespoke overlay to a thin shell consumer. Only a single `workspaceHost` layout style remains.

## 3. Mapping table (old → shell)

| Old (hand-roll) | New (SprkModal) |
|---|---|
| `open` gate + `if (!open) return null` | `<SprkModal open={open}>` (shell renders nothing when closed) |
| `onClose` (manual Esc + backdrop) | `onClose` + `dismiss="light"` (Esc + backdrop) |
| `<div s.title>Messages</div>` | `title="Messages"` |
| `min(1040px,95vw) × 72vh` surface | `size="md"` → `min(1040·uiScale, 92vw) × min(72vh, 720·uiScale)` |
| `expanded` → `96vw × 94vh` | shell `maximized` → `full` (`100vw × 100vh`) |
| `<ModalWindowControls isMaximized onToggleMaximize onClose>` | shell-internal `ModalWindowControls` (`maximizable` default true + `onClose`) |
| content `flex` column + `workspaceHost` `flex:1` | `padded={false}` body + `workspaceHost` (`height:100%` fill) |
| `ConversationWorkspace` / `ConversationView` / `renderConversation` wiring | **UNCHANGED** — passed through byte-for-byte |
| `authenticatedFetch` prop → workspace/view | **UNCHANGED** (ADR-028 function pass-through) |

**Content/behavior preserved byte-for-byte** — this was an envelope replacement only. `ConversationWorkspace`, `ConversationView`, `renderConversation` (title/rename/onOpenRecord/onOpenEmail seams), `navigationService`, `initialThreadId`, and all props are identical to the original.

## 4. Transform-robust centering — the FR-08 validation (load-bearing)

**Result: PASS.** The shell centers correctly under a transformed ancestor; no shell defect; escalation trigger NOT fired.

**Evidence** — `__tests__/ConversationModal.transform.test.tsx` (2 tests, both green), modeled on the P0 `SprkModal.test.tsx` transform test, adapted to this PCF's React-16 + `@testing-library/react` 12 env:

- Test 1 renders `<ConversationModal open>` inside `<div style={{transform:'scale(0.9)'}} data-testid="xf">` and asserts `expect(transformed).not.toContainElement(dialog)` — the Fluent Dialog surface (role=`dialog`) mounts **OUTSIDE** the transformed subtree. This is the exact failure the hand-rolled `position:fixed` overlay could not escape (a transform on an ancestor redefines the containing block for `position:fixed`).
- Test 2 asserts the shell chrome renders (Messages title + `maximize dialog`/`close` buttons) with the workspace mounted in the body — envelope + content-mount parity.
- The test exercises the **REAL** `SprkModal` (resolved via `pcfSafeShim.ts` → shared source), not a mock — so the portal behavior under React 16 is genuinely proven. The heavy conversation widgets are stubbed because this is an *envelope* test (the surface/portal), not a content test (content has its own suites).

**Reasoning for why it holds**: `SprkModal` renders a Fluent `Dialog`, whose `DialogSurface` portal mounts at the `FluentProvider` root (above any transformed app-shell ancestor). A `position:fixed` surface inside a transformed ancestor is positioned relative to that ancestor's box; the portal escapes that subtree entirely, so the transform cannot offset it. Passing under React 16 also **confirms `SprkModal` is genuinely PCF-safe** (renders with `@types/react` 16.14 + Fluent 9.46 in jsdom).

**Visual-pass recommendation (both the automatable bar is met AND a human pass is advised):** run a manual visual check in a real model-driven form where an app-shell ancestor carries a CSS `transform` (the R3 UAT round-5 repro), at 1080p/1440p/2560, in BOTH light and dark host themes. Confirm: (a) the modal is viewport-centered (not top-anchored), (b) dark mode renders correctly now that the hard-coded `webLightTheme` is gone (host theme inherited), (c) maximize fills the viewport, (d) the two-pane workspace scrolls internally with a native thin scrollbar (no chevron overlay).

## 5. Size-cap notes (documented deviations, both ACCEPTED)

- **Width `95vw → 92vw`**: `md` caps width at `min(1040px, 92vw)`; the hand-roll used `95vw`. Per the POML notes this 95→92 vw unification is **accepted** (converges every mid-size modal on the shell's single `md` rectangle), not a blocker.
- **New height cap `min(72vh, 720px)`**: `md` adds a `720px·uiScale` height cap the hand-roll lacked (it used `72vh` + `maxHeight:90vh`). The cap holds the landscape aspect on tall monitors (1440p/2560) so `md` never reads square — the exact fix design §0/§6.2 introduced. On the 1280–1440 baseline the cap rarely binds (720px ≈ 72vh at ~1000px height), so day-to-day appearance is unchanged; on tall panels it correctly prevents the square-modal regression.
- **Maximize `96vw×94vh → full (100vw×100vh)`**: the old `expanded` filled the container with a thin margin; the shell's `maximized`→`full` is a true viewport fill. Accepted as part of the standard window-controls semantics (consistent with the P1/P2 rollout).
- **uiScale NOT threaded (deliberate, in-scope)**: `SprkModal uiScale` defaults to `1`. This PCF host does not currently install `--sprk-ui-scale`, and threading `useUiScale()` is outside this envelope-replacement's ACs. `uiScale=1` is the identity → preserves current behavior exactly. A future task can thread `useUiScale()` if this PCF adopts the app-shell scale control (P0.5 seam).

## 6. pcf-safe route adoption (+ a documented specifier deviation)

- **SprkModal is imported from the PCF-safe entry** per the task mandate (the main session pre-added `SprkModal` + `ModalWindowControls` + types to `pcf-safe.ts` for this task; the shared `dist` was rebuilt so the additions are consumable — `dist/pcf-safe.d.ts` now exports `SprkModal`).
- **Specifier deviation — `@spaarke/ui-components/dist/pcf-safe`, NOT `.../src/pcf-safe`** (the form named in the task/`pcf-safe.ts` header). Empirical reason: this PCF's `tsconfig.json` maps `@spaarke/ui-components/*` → `dist/*`, and the shared lib emits `pcf-safe` at `dist/pcf-safe.js` (rootDir=`src`, so no `dist/src/`). Build behavior established via the failed-then-fixed build:
  - webpack (pcf-scripts) uses **node resolution** for subpaths (it does NOT honor tsconfig `paths`), so `@spaarke/ui-components/pcf-safe` → `<pkg>/pcf-safe` → **not found** (build error `pcf-safe doesn't exist`).
  - `@spaarke/ui-components/dist/pcf-safe` → webpack node-resolves `dist/pcf-safe.js` ✓; TypeScript falls back to `dist/pcf-safe.d.ts` ✓.
  - This is the **ADR-012 sanctioned PCF `/dist/` deep-import pattern** and matches this control's existing `@spaarke/ui-components/dist/utils/themeStorage` import. The `src/pcf-safe` form in the header is for *source*-consuming PCFs; this one consumes the built dist.
- **The pre-existing `ModalWindowControls` main-barrel deviation is retired by removal**, not by re-import. `SprkModal` composes `ModalWindowControls` internally, so `ConversationModal` no longer imports it from any barrel (an unused import would be a lint error). The `pcf-safe` `ModalWindowControls` export remains available for future *direct* PCF consumers. (Minor divergence from the literal "migrate the import to pcf-safe" wording — the correct, lint-clean outcome given the shell owns the controls.)
- **React-version-drift casts (ADR-022)**: `SprkModal` is re-cast at the import boundary — `as unknown as React.ComponentType<Omit<SprkModalProps,'children'> & { children?: React.ReactNode }>` — mirroring the file's existing `ConversationWorkspaceR16`/`ConversationViewR16` casts. `children` is retyped to this control's React-16 `ReactNode` so the workspace element assigns cleanly (the shared lib carries `@types/react` 19).

## 7. Test-infra changes (this PCF only)

- **NEW** `__tests__/ConversationModal.transform.test.tsx` — the FR-08 structural validation (+ chrome parity). 2 tests.
- **NEW** `__tests__/pcfSafeShim.ts` — re-exports the REAL `SprkModal` from shared source for jest (mirrors the existing `uiComponentsShim.ts`; jest has no tsconfig-paths support).
- **MODIFIED** `jest.config.js` — added `^@spaarke/ui-components/dist/pcf-safe$` → `pcfSafeShim.ts` moduleNameMapper.

## 8. Gates (Step 9.5) — all PASS

**Self code-review:**
- Overlay fully deleted — grep for `createPortal`/`position:fixed`/`FluentProvider`/`webLightTheme`/`ModalWindowControls` matches ONLY JSDoc comment lines; **zero in code**. ✓
- No orphaned imports/styles — `npm run lint` Succeeded (exit 0); `build:prod` type-check clean; every import used; only `workspaceHost` style retained. ✓
- Dismiss/scroll/controls parity — `dismiss="light"` (Esc+backdrop); native thin scrollbar body + `padded={false}` (no chevron); shell `ModalWindowControls` (max/restore + ×, verified in test). ✓
- ADR-028 — `authenticatedFetch: AuthenticatedFetchFn` passed as a FUNCTION through props (`<ConversationWorkspace authenticatedFetch={authenticatedFetch}>` + renderer `<ConversationView authenticatedFetch={props.authenticatedFetch}>`); never snapshotted (no `useState`/`useEffect` capture). Preserved from original. ✓

**adr-check (per-item):**
| Item | Verdict | Evidence |
|---|---|---|
| ADR-012 (shared components) | ✅ PASS | Composes shared `SprkModal` via pcf-safe; no fork; net component surface DECREASES (deleted overlay + own controls usage); ADR-012 `/dist/` PCF import pattern. |
| ADR-021 (Fluent v9 tokens) | ✅ PASS | No hex, no `'1px'`, no inline color (diff gate clean); Fluent v9 only; **dark parity IMPROVED** (removed hard-coded `webLightTheme` → inherits host dark-aware theme). |
| ADR-022 (PCF React 16/17) | ✅ PASS | No React-18 APIs; `SprkModal` proven React-16-safe (renders in React-16 jest); re-cast at import boundary per the React-version-drift rule. |
| ADR-028 (auth as function) | ✅ PASS | See self-review above. |
| NFR-03 (token discipline) | ✅ PASS | Reaffirmed under ADR-021. |
| NFR-04 (dual-React compat) | ✅ PASS | `build:prod` clean under this PCF's `@types/react` **16.14** (stricter than the spec's generic "18"; React-16-safety proven — see note below). |
| NFR-05 (client-only, no BFF) | ✅ PASS | Only a PCF `.tsx` + tests + jest config; zero BFF/server touch; zero publish-size impact. |

No ADR conflict surfaced → no CLAUDE.md §6.5 escalation. The FR-08 escalation trigger did NOT fire (shell centers correctly).

> **NFR-04 note**: the spec/POML generically say PCF consumers target `@types/react` 18, but THIS control pins `@types/react` `^16.14.0` (per its `package.json` + ADR-022 field-bound-PCF rule). Compiling clean under 16.14 is the stricter bar and proves the React-16-safety the shell needs.

## 9. Verification summary

| Check | Result |
|---|---|
| Shared `npm run build` (dist w/ pcf-safe additions) | ✅ exit 0; `dist/pcf-safe.d.ts` now exports `SprkModal` |
| Shared `npx jest src/components/SprkModal` | ✅ 11 suites / **106 tests** green |
| PCF `npm install --legacy-peer-deps` | ✅ exit 0 (node_modules was missing) |
| PCF jest BASELINE (pre-change) | 3 suites / **36 tests** |
| PCF jest (post-change) | ✅ 4 suites / **38 tests** (+1 suite, +2 tests; **zero regressions**) |
| PCF `npm run build:prod` | ✅ `[build] Succeeded` (bundle 2.5 MiB < 5 MB; only pre-existing size warnings) |
| PCF `npm run lint` | ✅ Succeeded (exit 0) |
| Diff gate (hex / `'1px'` / inline color / overlay code) | ✅ NONE in code |

## 10. Files touched

1. `src/client/pcf/CommunicationConversationPanel/CommunicationConversationPanel/ConversationModal.tsx` — rewritten (269 → 173 lines): overlay deleted, `SprkModal` composed.
2. `src/client/pcf/CommunicationConversationPanel/CommunicationConversationPanel/__tests__/ConversationModal.transform.test.tsx` — NEW (FR-08 validation).
3. `src/client/pcf/CommunicationConversationPanel/CommunicationConversationPanel/__tests__/pcfSafeShim.ts` — NEW (jest resolution of pcf-safe).
4. `src/client/pcf/CommunicationConversationPanel/jest.config.js` — +1 moduleNameMapper entry.
5. `src/client/shared/Spaarke.UI.Components/dist/**` — rebuilt (mechanical `tsc` output; no source change).

## 11. POML acceptance criteria — all 5 PASS

1. ✅ Renders via `SprkModal size="md"`, `min(1040px,95vw)×72vh` intent mapped to `md`; cap differences (95→92vw, +720px) documented (§5).
2. ✅ Centers correctly under a CSS-transformed ancestor (FR-08) — transform test green.
3. ✅ `ModalWindowControls` (via shell), Esc/backdrop dismiss, native body scroll preserved; no chevron overlay.
4. ✅ `position:fixed`/`createPortal` hand-roll removed — grep returns code matches = none (comments only).
5. ✅ Negative/escalation: no mis-center → no re-hand-roll; PCF builds via `build:prod`, compiles under its `@types/react`, tests green.

## 12. Deviations (surfaced, not silent)

1. **Import specifier `@spaarke/ui-components/dist/pcf-safe`** (vs the task's literal `.../src/pcf-safe`) — empirically required for this dist-consuming PCF; ADR-012-sanctioned `/dist/` form; matches the control's existing `dist/utils/themeStorage` import (§6).
2. **`ModalWindowControls` deviation retired by removal**, not re-import — the shell owns the controls, so a direct import would be unused/lint-error (§6).
3. **`current-task.md` / `TASK-INDEX.md` NOT updated** — reserved to the orchestrator per this wave's hard boundaries (overrides POML step 6).
4. **uiScale not threaded** — out of scope for the envelope replacement; `uiScale=1` preserves behavior (§5).
