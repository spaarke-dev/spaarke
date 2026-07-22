# Task 030 — Record right-pane conversation PCF (Surface 1, FR-13)

**Status:** implemented + verified (build:prod pass, 29 jest tests pass). UI-tests deferred to deploy (task 034 — no browser here).
**Rigor:** FULL. **Model:** opus.

## New PCF directory + files

`src/client/pcf/CommunicationConversationPanel/` — mirrors `CommunicationTimelineRegarding` structure.

Config: `package.json`, `CommunicationConversationPanel.pcfproj`, `pcfconfig.json`, `featureconfig.json`,
`webpack.config.js`, `eslint.config.mjs`, `.gitignore`, `tsconfig.json`, `jest.config.js`, `jest.setup.ts`.

Control (`CommunicationConversationPanel/`):
- `ControlManifest.Input.xml` — virtual control, `anchorField` bound (form anchor), auth inputs, `showVersionFooter`.
- `index.ts` — ReactControl `init/updateView/getOutputs/destroy` (CONTROL_VERSION 1.0.0).
- `CommunicationConversationPanelHost.tsx` — FluentProvider + `resolveThemeWithUserPreference` (ADR-021 theme owner).
- `CommunicationConversationPanelApp.tsx` — regarding resolution from Xrm page context, `@spaarke/auth` bootstrap,
  `readByRegarding` read (+5s poll), `buildPreviewModel`, `threadNames` map, `onOpenRecord` (close-modal-then-navigateTo).
- `ConversationPreview.tsx` — compact preview: ≤3 threads, default auto-expanded, ≤5 msgs/thread, per-message
  `MessageQuickView`, footer "N of M" + total messages + version.
- `ConversationModal.tsx` — Fluent v9 `Dialog` hosting shared `ConversationWorkspace` (record-filtered) with
  `renderConversation` → `ConversationView` (title/regarding/onOpenRecord wired).
- `previewModel.ts` — pure bounding (reuses shared `mapRegardingReadResultToGroups` + `buildTimeline`).
- `authInit.ts`, `hostContext.ts`, `styles.css`.
- `__tests__/` — `hostContext.test.ts`, `previewModel.test.ts`, `ConversationPreview.test.tsx`, `uiComponentsShim.ts`.

## How the shared widget is mounted (NFR-06 — reuse, no reimplementation)

- **Preview**: `readByRegarding(entityType,id)` → `mapRegardingReadResultToGroups` → `buildTimeline` (all shared) →
  bounded to 3 threads / last 5 msgs. Message model is the shared `TimelineMessage`; per-message quick-view is the
  shared `MessageQuickView`. The compact rows are a NEW bounded presentation (deliberately not the chat-bubble view).
- **Modal**: `<ConversationWorkspace regarding={{entityType,id}} renderConversation={…}>`; the seam renders
  `<ConversationView threadId title regarding onOpenRecord currentUserSystemUserId>`. The shell owns the thread list
  + selection, so thread navigation happens entirely inside the modal — the user never returns to the PCF.
- **onOpenRecord** → host closes the Fluent dialog first, then `Xrm.Navigation.navigateTo({pageType:'entityrecord',…},
  {target:2, 85%×85%})` (Layout 1, MODAL-DECISION-CRITERIA; honors anti-pattern #5 "no nested modals").
- **currentUserSystemUserId** from `context.userSettings.userId` (via shared `cleanGuid`).

## No second regarding mechanism (NFR-06 / ADR-024)

`resolveRegardingContext` = the SAME 11-family guard the sibling uses; host record's `(entityType,id)` is passed
straight through to the read API and the modal. Escalation trigger NOT fired — the PCF context supplies the regarding
identity exactly as `CommunicationTimelineRegarding` does.

## Build / test

- `npm run build:prod` → **Succeeded** (only bundle-size perf warnings, same as sibling PCFs).
- `npx jest` → **3 suites, 29 tests, all pass.**
- Repo uses **jest** (not vitest); POML's vitest mention ignored per instructions.

### Test-infra note (documented deviation, test-only)
The shared lib ships an **ESM-only `dist/index.js`** that Jest's node runtime can't `require()`. Tests map
`@spaarke/ui-components` to `__tests__/uiComponentsShim.ts`, which re-exports the exact surface from the shared **TS
source** (ts-jest transforms it) — so tests exercise the REAL `buildTimeline`/`mapRegardingReadResultToGroups`/
`MessageQuickView` (no behavioral mock). `react`/`react-dom`/`@fluentui/*` are pinned to single copies via
`moduleNameMapper` to avoid the two-React-copies `useContext` null. Production/build resolution is unchanged.

## Step 9.5 self-review + ADR-check

- **ADR-021** ✓ Fluent v9 semantic tokens only; host FluentProvider owns dark mode (preview + every MessageQuickView + modal).
- **ADR-026** ✓ virtual PCF on OOB form (Path-A); no Code Page / FCC. Cite in deploy PR (034).
- **ADR-028** ✓ single `@spaarke/auth` fetch path; `authenticatedFetch` injected to read API + ConversationWorkspace + ConversationView. No token props.
- **NFR-06** ✓ mounts shared ConversationWorkspace/ConversationView/MessageQuickView; shared message model + mapping; no 2nd regarding path.
- **§11** ✓ concrete justification; new modules are host-glue + a bounding helper that reuses shared engines — no new service/endpoint/regarding mechanism.

### Findings (no Critical/Major)
- **Minor** — `onOpenRecord` opens the host record itself (regarding == host record), so the header link re-opens the
  record the user is already on. Redundant but contract-correct (FR-12); harmless.
- **Minor** — `renderConversation` callback cast to `ConversationWorkspaceProps['renderConversation']` bridges the
  React-16-vs-newer `ReactNode` type seam (same rationale as the component boundary casts). Runtime unaffected.
- **Minor** — 5s preview poll added for freshness (consistent with NFR-07 feel); not strictly required by FR-13.

## Acceptance criteria

1. New PCF mirrors sibling manifest/lifecycle/auth/FluentProvider; compiles under `build:prod` — **MET**.
2. Preview bounded ≤3 threads / last 5 comms / default auto-expanded — **MET** (previewModel.test + ConversationPreview.test).
3. Footer "N of M" (+ total messages) — **MET** (tests).
4. Per-message `MessageQuickView` — **MET** (test opens the shared popover).
5. Open → shared two-pane widget as record-filtered modal, thread-nav inside modal without returning — **MET** (code; UI-verify at 034).
6. No new regarding mechanism / no reimplemented bubbles/thread-list/quick-view — **MET**.
7. All reads via `@spaarke/auth` `authenticatedFetch` — **MET**.
8. Dark mode via host FluentProvider, semantic tokens — **MET** (no hardcoded colors; UI-verify at 034).

## Deferred to task 034 (deploy)
- Solution/ packaging (customizations, solution.xml, bundle copy) + version-footer 4-location bump for the release.
- Live `ui-tests` (Matter form render, modal thread-nav, dark mode) — no browser in this environment.
